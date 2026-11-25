using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Local.UnityAgent
{
    /// <summary>场景 UI 文本扫描、导出 JSON、按路径应用翻译。</summary>
    public static class L10nCommands
    {
        const string DefaultExport = "Assets/Localization/strings.json";
        const string LocFolder = "Assets/Localization";

        [MenuItem("Tools/Unity Agent/Export Localization")]
        public static void MenuExport()
        {
            try
            {
                var data = Export(new Dictionary<string, object> { { "path", DefaultExport } });
                UnityEngine.Debug.Log("[UnityAgent] Export Localization path=" + MiniJson.GetString(data, "path")
                    + " count=" + ReadInt(data, "count", 0));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Export Localization failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "l10n.scan") { data = Scan(args); return true; }
            if (cmd == "l10n.export") { data = Export(args); return true; }
            if (cmd == "l10n.apply") { data = Apply(args); return true; }
            if (cmd == "l10n.info") { data = Info(); return true; }
            return false;
        }

        public static Dictionary<string, object> Scan(Dictionary<string, object> args)
        {
            var rootPath = MiniJson.GetString(args, "root", null);
            Transform root = null;
            if (!string.IsNullOrEmpty(rootPath))
            {
                root = AgentCommands.FindTransformByPath(rootPath);
                if (root == null) throw new AgentException("NOT_FOUND", "root not found: " + rootPath);
            }
            var entries = CollectEntries(root);
            return new Dictionary<string, object>
            {
                { "count", entries.Count },
                { "entries", entries },
            };
        }

        public static Dictionary<string, object> Export(Dictionary<string, object> args)
        {
            var assetPath = MiniJson.GetString(args, "path", DefaultExport);
            if (string.IsNullOrWhiteSpace(assetPath)) assetPath = DefaultExport;
            var scan = Scan(args);
            var list = scan["entries"] as List<object>;
            var map = new Dictionary<string, object>();
            if (list != null)
            {
                for (var i = 0; i < list.Count; i++)
                {
                    var row = MiniJson.AsObject(list[i]);
                    if (row == null) continue;
                    var p = MiniJson.GetString(row, "path", null);
                    var t = MiniJson.GetString(row, "text", null);
                    if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(t)) continue;
                    map[p] = t;
                }
            }
            var full = PathUtil.ResolveProjectRelative(assetPath.Replace('\\', '/'), allowLibraryAgent: false);
            PathUtil.EnsureDir(Path.GetDirectoryName(full));
            File.WriteAllText(full, PrettyObject(map), new UTF8Encoding(false));
            AssetDatabase.Refresh();
            return new Dictionary<string, object>
            {
                { "path", PathUtil.ToProjectRelative(full) },
                { "count", map.Count },
            };
        }

        public static Dictionary<string, object> Apply(Dictionary<string, object> args)
        {
            var map = LoadMap(args);
            var applied = 0;
            var missing = new List<object>();
            foreach (var kv in map)
            {
                if (!TrySetText(kv.Key, kv.Value)) missing.Add(kv.Key);
                else applied++;
            }
            return new Dictionary<string, object>
            {
                { "applied", applied },
                { "missing", missing },
            };
        }

        public static Dictionary<string, object> Info()
        {
            var scan = Scan(null);
            var files = new List<object>();
            var folder = PathUtil.CombineUnderRoot(LocFolder);
            if (Directory.Exists(folder))
            {
                foreach (var file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories))
                {
                    var rel = PathUtil.ToProjectRelative(file).Replace('\\', '/');
                    files.Add(rel);
                }
                files.Sort((a, b) => string.Compare(Convert.ToString(a), Convert.ToString(b), StringComparison.Ordinal));
            }
            return new Dictionary<string, object>
            {
                { "textCount", ReadInt(scan, "count", 0) },
                { "localizationFiles", files },
            };
        }

        static Dictionary<string, string> LoadMap(Dictionary<string, object> args)
        {
            if (args != null && args.TryGetValue("map", out var inline) && inline != null)
                return FlatMap(MiniJson.AsObject(inline));
            var mapPath = MiniJson.GetString(args, "mapPath", null);
            if (string.IsNullOrEmpty(mapPath))
                throw new AgentException("BAD_ARGS", "map 或 mapPath 必填其一");
            var full = PathUtil.ResolveProjectRelative(mapPath.Replace('\\', '/'), allowLibraryAgent: false);
            if (!File.Exists(full)) throw new AgentException("NOT_FOUND", "map file not found: " + mapPath);
            var parsed = MiniJson.Parse(File.ReadAllText(full, Encoding.UTF8));
            return FlatMap(MiniJson.AsObject(parsed));
        }

        static Dictionary<string, string> FlatMap(Dictionary<string, object> obj)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (obj == null) return map;
            foreach (var kv in obj)
            {
                if (kv.Value == null) continue;
                map[kv.Key] = Convert.ToString(kv.Value);
            }
            return map;
        }

        static List<object> CollectEntries(Transform root)
        {
            var entries = new List<object>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var tmp in UnityEngine.Object.FindObjectsOfType<TMP_Text>(true))
            {
                if (tmp == null) continue;
                if (!UnderRoot(tmp.transform, root)) continue;
                AddEntry(entries, seen, tmp.transform, tmp.text, "tmp");
            }

            foreach (var ui in UnityEngine.Object.FindObjectsOfType<Text>(true))
            {
                if (ui == null) continue;
                if (ui.GetComponent<TMP_Text>() != null) continue;
                if (!UnderRoot(ui.transform, root)) continue;
                AddEntry(entries, seen, ui.transform, ui.text, "text");
            }

            return entries;
        }

        static void AddEntry(List<object> entries, HashSet<string> seen, Transform t, string text, string type)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var path = AgentCommands.GetHierarchyPath(t);
            if (!seen.Add(path)) return;
            entries.Add(new Dictionary<string, object>
            {
                { "path", path },
                { "text", text },
                { "type", type },
            });
        }

        static bool UnderRoot(Transform t, Transform root)
        {
            if (root == null) return true;
            if (t == null) return false;
            if (t == root) return true;
            return t.IsChildOf(root);
        }

        static bool TrySetText(string path, string text)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var tr = AgentCommands.FindTransformByPath(path);
            if (tr == null) return false;
            var tmp = tr.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                tmp.text = text ?? "";
                return true;
            }
            var ui = tr.GetComponent<Text>();
            if (ui != null)
            {
                ui.text = text ?? "";
                return true;
            }
            return false;
        }

        static string PrettyObject(Dictionary<string, object> map)
        {
            var keys = new List<string>();
            foreach (var kv in map) keys.Add(kv.Key);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            sb.Append("{\n");
            for (var i = 0; i < keys.Count; i++)
            {
                var k = keys[i];
                sb.Append("  ");
                sb.Append(MiniJson.Stringify(k));
                sb.Append(": ");
                sb.Append(MiniJson.Stringify(map[k]));
                if (i < keys.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append('}');
            return sb.ToString();
        }

        static int ReadInt(Dictionary<string, object> data, string key, int fallback)
        {
            if (data == null || !data.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is double d) return (int)d;
            return MiniJson.GetInt(data, key, fallback);
        }
    }
}
