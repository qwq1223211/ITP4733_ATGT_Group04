using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Local.UnityAgent
{
    /// <summary>
    /// Unity Package Manager bridge commands.
    /// list is synchronous; add/remove start an async request and return immediately,
    /// then package.status polls it. Never block the editor main thread on resolve.
    /// </summary>
    public static class PackageCommands
    {
        static Request _active;
        static string _kind = "";
        static string _identifier = "";

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            switch (cmd)
            {
                case "package.list": data = List(args); break;
                case "package.add": data = Add(args); break;
                case "package.remove": data = Remove(args); break;
                case "package.status": data = Status(); break;
                default: return false;
            }
            return true;
        }

        static Dictionary<string, object> List(Dictionary<string, object> args)
        {
            var filter = (MiniJson.GetString(args, "filter", "") ?? "").Trim();
            var limit = MiniJson.GetInt(args, "limit", 200);
            if (limit <= 0) limit = 200;
            if (limit > 1000) limit = 1000;

            var rows = new List<object>();
            var packages = PackageInfo.GetAllRegisteredPackages();
            foreach (var package in packages)
            {
                if (package == null) continue;
                if (!string.IsNullOrEmpty(filter) &&
                    package.name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (package.displayName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                rows.Add(PackageRow(package));
                if (rows.Count >= limit) break;
            }

            // Batchmode / early startup can report no registered packages; fall back to manifest dependencies.
            if (rows.Count == 0) rows.AddRange(ManifestRows(filter, limit));

            return new Dictionary<string, object>
            {
                { "count", rows.Count },
                { "filter", filter },
                { "packages", rows },
            };
        }

        static List<object> ManifestRows(string filter, int limit)
        {
            var rows = new List<object>();
            var file = Path.Combine(PathUtil.ProjectRoot, "Packages/manifest.json");
            if (!File.Exists(file)) return rows;
            var raw = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(file)));
            var dependencies = raw != null && raw.TryGetValue("dependencies", out var deps)
                ? MiniJson.AsObject(deps)
                : null;
            if (dependencies == null) return rows;
            foreach (var pair in dependencies)
            {
                if (!string.IsNullOrEmpty(filter) &&
                    pair.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                rows.Add(new Dictionary<string, object>
                {
                    { "name", pair.Key },
                    { "displayName", pair.Key },
                    { "version", Convert.ToString(pair.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "" },
                    { "source", "manifest" },
                    { "resolvedPath", "" },
                });
                if (rows.Count >= limit) break;
            }
            return rows;
        }

        static Dictionary<string, object> Add(Dictionary<string, object> args)
        {
            EnsureIdle();
            var identifier = (MiniJson.GetString(args, "identifier", "") ?? "").Trim();
            if (string.IsNullOrEmpty(identifier))
                throw new AgentException("BAD_ARGS", "identifier is required, e.g. com.unity.inputsystem@1.7.0");

            _active = Client.Add(identifier);
            _kind = "add";
            _identifier = identifier;
            return new Dictionary<string, object>
            {
                { "started", true },
                { "kind", _kind },
                { "identifier", identifier },
            };
        }

        static Dictionary<string, object> Remove(Dictionary<string, object> args)
        {
            EnsureIdle();
            var name = (MiniJson.GetString(args, "name", "") ?? "").Trim();
            if (string.IsNullOrEmpty(name))
                throw new AgentException("BAD_ARGS", "name is required, e.g. com.unity.inputsystem");

            _active = Client.Remove(name);
            _kind = "remove";
            _identifier = name;
            return new Dictionary<string, object>
            {
                { "started", true },
                { "kind", _kind },
                { "name", name },
            };
        }

        static Dictionary<string, object> Status()
        {
            if (_active == null)
            {
                return new Dictionary<string, object>
                {
                    { "active", false },
                    { "completed", true },
                };
            }

            var completed = _active.IsCompleted;
            var result = new Dictionary<string, object>
            {
                { "active", !completed },
                { "completed", completed },
                { "kind", _kind },
                { "identifier", _identifier },
                { "status", _active.Status.ToString() },
            };
            if (_active.Error != null && !string.IsNullOrEmpty(_active.Error.message))
                result["error"] = _active.Error.message;

            if (completed)
            {
                if (_active is AddRequest add && add.Result != null)
                    result["package"] = PackageRow(add.Result);
                _active = null;
                _kind = "";
                _identifier = "";
            }
            return result;
        }

        static void EnsureIdle()
        {
            if (_active != null && !_active.IsCompleted)
                throw new AgentException("BUSY", "a package request is already in progress; poll package.status first");
            _active = null;
        }

        static Dictionary<string, object> PackageRow(PackageInfo package)
        {
            return new Dictionary<string, object>
            {
                { "name", package.name },
                { "displayName", package.displayName ?? package.name },
                { "version", package.version ?? "" },
                { "source", package.source.ToString() },
                { "resolvedPath", package.resolvedPath ?? "" },
            };
        }
    }
}
