using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Local.UnityAgent
{
    /// <summary>
    /// script.validate / script.read —— 写脚本前的结构校验与读回。
    ///
    /// 结构校验规则与 JS 侧 dsh-unity-code/lib/csharp-lint.js 对齐：
    ///   1. () [] {} 三类括号成对且不交叉
    ///   2. 普通字符串 / verbatim @"..." / 插值 $"..." / 原始字符串 """ 必须闭合
    ///   3. 字符字面量必须闭合，块注释必须闭合
    ///   4. 至少要有一个 class / struct / interface / record / enum 声明
    ///
    /// 额外能力（JS 侧做不了，需要编辑器）：用反射扫描已加载程序集，检查 using 的命名空间
    /// 是否真实存在。只报 warning —— 程序集可能尚未加载，误报不应该拦住写入。
    /// </summary>
    public static class ScriptValidation
    {
        static readonly Regex UsingRegex = new Regex(
            @"^\s*using\s+(?!static\b)(?:[A-Za-z_][A-Za-z0-9_]*\s*=\s*)?([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*;",
            RegexOptions.Multiline | RegexOptions.Compiled);

        static readonly Regex TypeKeywordRegex = new Regex(
            @"\b(class|struct|interface|record|enum)\b", RegexOptions.Compiled);

        public static Dictionary<string, object> Validate(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            var content = MiniJson.GetString(args, "content", null);

            string source;
            if (content != null)
            {
                source = content;
            }
            else
            {
                if (string.IsNullOrEmpty(path))
                    throw new AgentException("BAD_ARGS", "content 或 path 必须给一个");
                source = ReadAllText(RequireAssetPath(path));
            }

            var errors = new List<object>();
            var warnings = new List<object>();

            // ---- 结构扫描 ----
            ScanStructure(source, errors);

            // ---- 类型声明 ----
            if (!TypeKeywordRegex.IsMatch(source))
            {
                warnings.Add(Diag(1, 1, "NO_TYPE_DECLARATION",
                    "没有找到 class / struct / interface / record / enum 声明：这个 .cs 文件可能不会产生任何类型"));
            }

            // ---- using 命名空间存在性（反射）----
            var known = CollectKnownNamespaces();
            foreach (Match m in UsingRegex.Matches(source))
            {
                var ns = m.Groups[1].Value;
                if (!NamespaceExists(known, ns))
                {
                    warnings.Add(Diag(LineOf(source, m.Index), 1, "UNKNOWN_NAMESPACE",
                        "using " + ns + "：已加载的程序集里没找到这个命名空间（可能程序集未加载，不一定是错）"));
                }
            }

            return new Dictionary<string, object>
            {
                { "ok", errors.Count == 0 },
                { "path", path ?? "" },
                { "errors", errors },
                { "warnings", warnings },
            };
        }

        public static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            if (string.IsNullOrEmpty(path))
                throw new AgentException("BAD_ARGS", "path required");
            var full = RequireAssetPath(path);
            var text = ReadAllText(full);
            return new Dictionary<string, object>
            {
                { "path", path },
                { "content", text },
                { "length", text.Length },
            };
        }

        // ====================================================================
        // 扫描
        // ====================================================================

        static void ScanStructure(string text, List<object> errors)
        {
            var stack = new List<Tuple<char, int, int>>();
            var i = 0;
            var line = 1;
            var column = 1;

            Action<int> advance = n =>
            {
                for (var k = 0; k < n && i < text.Length; k++)
                {
                    if (text[i] == '\n') { line++; column = 1; }
                    else column++;
                    i++;
                }
            };

            while (i < text.Length)
            {
                var ch = text[i];
                var next = i + 1 < text.Length ? text[i + 1] : '\0';

                // 行注释
                if (ch == '/' && next == '/')
                {
                    while (i < text.Length && text[i] != '\n') advance(1);
                    continue;
                }

                // 块注释
                if (ch == '/' && next == '*')
                {
                    var sl = line; var sc = column;
                    advance(2);
                    var closed = false;
                    while (i < text.Length)
                    {
                        if (text[i] == '*' && i + 1 < text.Length && text[i + 1] == '/')
                        {
                            advance(2); closed = true; break;
                        }
                        advance(1);
                    }
                    if (!closed) errors.Add(Diag(sl, sc, "UNCLOSED_COMMENT", "块注释未闭合：缺少 */"));
                    continue;
                }

                // 原始字符串 """
                if (ch == '"' && i + 2 < text.Length && text[i + 1] == '"' && text[i + 2] == '"')
                {
                    var sl = line; var sc = column;
                    advance(3);
                    var closed = false;
                    while (i < text.Length)
                    {
                        if (i + 2 < text.Length && text[i] == '"' && text[i + 1] == '"' && text[i + 2] == '"')
                        {
                            advance(3); closed = true; break;
                        }
                        advance(1);
                    }
                    if (!closed) errors.Add(Diag(sl, sc, "UNCLOSED_STRING", "原始字符串未闭合：缺少三个双引号"));
                    continue;
                }

                // verbatim @"..." / $@"..." / @$"..."
                var isVerbatim =
                    (ch == '@' && next == '"')
                    || (ch == '@' && next == '$' && i + 2 < text.Length && text[i + 2] == '"')
                    || (ch == '$' && next == '@' && i + 2 < text.Length && text[i + 2] == '"');
                if (isVerbatim)
                {
                    var sl = line; var sc = column;
                    while (i < text.Length && text[i] != '"') advance(1);
                    advance(1); // 开引号
                    var closed = false;
                    while (i < text.Length)
                    {
                        if (text[i] == '"')
                        {
                            if (i + 1 < text.Length && text[i + 1] == '"') { advance(2); continue; }
                            advance(1); closed = true; break;
                        }
                        advance(1);
                    }
                    if (!closed) errors.Add(Diag(sl, sc, "UNCLOSED_STRING", "verbatim 字符串未闭合"));
                    continue;
                }

                // 普通 / 插值字符串
                if (ch == '"' || (ch == '$' && next == '"'))
                {
                    var sl = line; var sc = column;
                    if (ch == '$') advance(1);
                    advance(1); // 开引号
                    var closed = false;
                    while (i < text.Length)
                    {
                        if (text[i] == '\\') { advance(2); continue; }
                        if (text[i] == '"') { advance(1); closed = true; break; }
                        if (text[i] == '\n') break;
                        advance(1);
                    }
                    if (!closed) errors.Add(Diag(sl, sc, "UNCLOSED_STRING", "字符串未闭合：普通字符串不能跨行"));
                    continue;
                }

                // 字符字面量
                if (ch == '\'')
                {
                    var sl = line; var sc = column;
                    advance(1);
                    var closed = false;
                    while (i < text.Length)
                    {
                        if (text[i] == '\\') { advance(2); continue; }
                        if (text[i] == '\'') { advance(1); closed = true; break; }
                        if (text[i] == '\n') break;
                        advance(1);
                    }
                    if (!closed) errors.Add(Diag(sl, sc, "UNCLOSED_CHAR", "字符字面量未闭合：缺少结尾单引号"));
                    continue;
                }

                // 括号
                if (ch == '(' || ch == '[' || ch == '{')
                {
                    stack.Add(Tuple.Create(ch, line, column));
                    advance(1);
                    continue;
                }
                if (ch == ')' || ch == ']' || ch == '}')
                {
                    var expected = ch == ')' ? '(' : ch == ']' ? '[' : '{';
                    if (stack.Count == 0)
                    {
                        errors.Add(Diag(line, column, "UNEXPECTED_CLOSER", "多余的 " + ch + "：前面没有对应的 " + expected));
                    }
                    else
                    {
                        var top = stack[stack.Count - 1];
                        stack.RemoveAt(stack.Count - 1);
                        if (top.Item1 != expected)
                        {
                            errors.Add(Diag(line, column, "MISMATCHED_BRACKET",
                                "括号不匹配：" + top.Item2 + " 行 " + top.Item3 + " 列的 " + top.Item1
                                + " 被 " + ch + " 关闭（这里应为 " + CloseOf(top.Item1) + "）"));
                        }
                    }
                    advance(1);
                    continue;
                }

                advance(1);
            }

            foreach (var open in stack)
            {
                errors.Add(Diag(open.Item2, open.Item3, "UNCLOSED_BRACKET",
                    "未闭合的 " + open.Item1 + "：在第 " + open.Item2 + " 行第 " + open.Item3
                    + " 列打开，一直到文件末尾都没有关闭"));
            }
        }

        static char CloseOf(char open) => open == '(' ? ')' : open == '[' ? ']' : '}';

        static int LineOf(string text, int index)
        {
            var line = 1;
            for (var k = 0; k < index && k < text.Length; k++)
                if (text[k] == '\n') line++;
            return line;
        }

        static Dictionary<string, object> Diag(int line, int column, string code, string message)
        {
            return new Dictionary<string, object>
            {
                { "line", line },
                { "column", column },
                { "code", code },
                { "message", message },
            };
        }

        // ====================================================================
        // 反射：命名空间存在性
        // ====================================================================

        static HashSet<string> CollectKnownNamespaces()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (t == null || string.IsNullOrEmpty(t.Namespace)) continue;
                        var ns = t.Namespace;
                        while (!string.IsNullOrEmpty(ns))
                        {
                            set.Add(ns);
                            var dot = ns.LastIndexOf('.');
                            ns = dot > 0 ? ns.Substring(0, dot) : null;
                        }
                    }
                }
                catch (ReflectionTypeLoadException)
                {
                    // 部分类型加载失败很常见（缺依赖），跳过这个程序集即可。
                }
                catch
                {
                    // 反射异常不应该让校验整个失败。
                }
            }
            return set;
        }

        static bool NamespaceExists(HashSet<string> known, string ns)
        {
            if (known.Contains(ns)) return true;
            // 已知命名空间的子命名空间也算存在（类型可能全是泛型/接口，没被扫到）。
            foreach (var k in known)
            {
                if (k.StartsWith(ns + ".", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // ====================================================================
        // 文件
        // ====================================================================

        static string RequireAssetPath(string path)
        {
            var assetPath = PathUtil.ToAssetPath(path);
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", "只能读 Assets/ 下的文件: " + assetPath);
            var full = PathUtil.ResolveProjectRelative(assetPath, false);
            PathUtil.EnsureInsideProject(full);
            return full;
        }

        static string ReadAllText(string fullPath)
        {
            if (!File.Exists(fullPath))
                throw new AgentException("NOT_FOUND", "文件不存在: " + PathUtil.ToProjectRelative(fullPath));
            return File.ReadAllText(fullPath, Encoding.UTF8);
        }
    }
}
