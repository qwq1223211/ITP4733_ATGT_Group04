using System;
using System.IO;
using UnityEngine;

namespace Local.UnityAgent
{
    public static class PathUtil
    {
        public static string ProjectRoot
        {
            get
            {
                // dataPath = <project>/Assets
                var assets = Application.dataPath.Replace('\\', '/');
                if (assets.EndsWith("/Assets", StringComparison.OrdinalIgnoreCase))
                    return assets.Substring(0, assets.Length - "/Assets".Length);
                return Directory.GetParent(Application.dataPath)?.FullName?.Replace('\\', '/') ?? assets;
            }
        }

        public static string AgentLibDir => CombineUnderRoot("Library/UnityAgent");
        public static string ServerJsonPath => CombineUnderRoot("Library/UnityAgent/server.json");
        public static string ShotsDir => CombineUnderRoot("Library/UnityAgent/shots");

        public static string CombineUnderRoot(string relative)
        {
            relative = (relative ?? "").Replace('\\', '/').TrimStart('/');
            var full = Path.GetFullPath(Path.Combine(ProjectRoot, relative)).Replace('\\', '/');
            EnsureInsideProject(full);
            return full;
        }

        /// <summary>
        /// Resolve Assets/... or Library/UnityAgent/... relative path; reject escapes.
        /// Returns full filesystem path.
        /// </summary>
        public static string ResolveProjectRelative(string relative, bool allowLibraryAgent = true)
        {
            if (string.IsNullOrEmpty(relative))
                throw new AgentException("BAD_ARGS", "path is required");

            relative = relative.Replace('\\', '/').Trim();
            if (Path.IsPathRooted(relative) || relative.Contains(":"))
                throw new AgentException("BAD_ARGS", "absolute paths are not allowed: " + relative);

            // normalize . and ..
            var full = Path.GetFullPath(Path.Combine(ProjectRoot, relative)).Replace('\\', '/');
            EnsureInsideProject(full);

            var rel = ToProjectRelative(full);
            var okAssets = rel.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                           rel.Equals("Assets", StringComparison.OrdinalIgnoreCase);
            var okLib = allowLibraryAgent && (
                rel.StartsWith("Library/UnityAgent/", StringComparison.OrdinalIgnoreCase) ||
                rel.Equals("Library/UnityAgent", StringComparison.OrdinalIgnoreCase));

            // Builds/ 是打包输出的常规目录。
            // 必须放行：Unity 禁止把包打在 Library/ 里（内部工作目录），
            // 所以 build.player 的合法输出只能落在工程内其它目录。
            var okBuilds = rel.StartsWith("Builds/", StringComparison.OrdinalIgnoreCase) ||
                           rel.Equals("Builds", StringComparison.OrdinalIgnoreCase);

            if (!okAssets && !okLib && !okBuilds)
                throw new AgentException("BAD_ARGS", "path must be under Assets/, Builds/ or Library/UnityAgent/: " + relative);

            return full;
        }

        public static string ToProjectRelative(string fullPath)
        {
            var full = Path.GetFullPath(fullPath).Replace('\\', '/');
            var root = Path.GetFullPath(ProjectRoot).Replace('\\', '/').TrimEnd('/');
            EnsureInsideProject(full);
            if (full.Length == root.Length) return "";
            return full.Substring(root.Length + 1);
        }

        public static string ToAssetPath(string fullOrRelative)
        {
            var full = ResolveProjectRelative(fullOrRelative.Replace('\\', '/'), allowLibraryAgent: false);
            return ToProjectRelative(full);
        }

        public static void EnsureInsideProject(string fullPath)
        {
            var full = Path.GetFullPath(fullPath).Replace('\\', '/').TrimEnd('/');
            var root = Path.GetFullPath(ProjectRoot).Replace('\\', '/').TrimEnd('/');
            if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase))
            {
                throw new AgentException("BAD_ARGS", "path escapes project root: " + fullPath);
            }
        }

        public static void EnsureDir(string fullDir)
        {
            if (!Directory.Exists(fullDir))
                Directory.CreateDirectory(fullDir);
        }
    }

    public sealed class AgentException : Exception
    {
        public string Code { get; }
        public object Detail { get; }

        public AgentException(string code, string message, object detail = null) : base(message)
        {
            Code = code ?? "INTERNAL";
            Detail = detail;
        }
    }
}
