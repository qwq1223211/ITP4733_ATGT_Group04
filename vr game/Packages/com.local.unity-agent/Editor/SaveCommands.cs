using System;
using System.Collections.Generic;
using System.IO;
using Local.UnityAgent.Runtime;
using UnityEditor;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>编辑器侧存档槽。读写走运行时 SaveSystem。</summary>
    public static class SaveCommands
    {
        [MenuItem("Tools/Unity Agent/Open Save Folder")]
        public static void OpenFolderMenu()
        {
            try
            {
                if (!Directory.Exists(SaveSystem.RootDir))
                    Directory.CreateDirectory(SaveSystem.RootDir);
                EditorUtility.RevealInFinder(SaveSystem.RootDir);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Open Save Folder failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            try
            {
                if (cmd == "save.write") { data = Write(args); return true; }
                if (cmd == "save.read") { data = Read(args); return true; }
                if (cmd == "save.delete") { data = Delete(args); return true; }
                if (cmd == "save.list") { data = List(); return true; }
                if (cmd == "save.path") { data = PathOf(args); return true; }
            }
            catch (ArgumentException ex)
            {
                throw new AgentException("BAD_ARGS", ex.Message);
            }
            return false;
        }

        static Dictionary<string, object> Write(Dictionary<string, object> args)
        {
            var slot = MiniJson.GetString(args, "slot", null);
            if (string.IsNullOrEmpty(slot)) throw new AgentException("BAD_ARGS", "slot 必填");
            if (args == null || !args.ContainsKey("json")) throw new AgentException("BAD_ARGS", "json 必填");
            var json = args["json"] == null ? "" : Convert.ToString(args["json"]);
            var version = 1;
            if (args.ContainsKey("version") && args["version"] != null)
                version = ReadInt(args, "version", 1);
            SaveSystem.SaveRaw(slot, json, version);
            var path = SaveSystem.PathFor(slot);
            var bytes = File.Exists(path) ? (int)new FileInfo(path).Length : 0;
            return new Dictionary<string, object>
            {
                { "slot", slot },
                { "path", path.Replace('\\', '/') },
                { "bytes", bytes },
            };
        }

        static Dictionary<string, object> Read(Dictionary<string, object> args)
        {
            var slot = MiniJson.GetString(args, "slot", null);
            if (string.IsNullOrEmpty(slot)) throw new AgentException("BAD_ARGS", "slot 必填");
            if (!SaveSystem.Exists(slot))
            {
                return new Dictionary<string, object>
                {
                    { "exists", false },
                    { "slot", slot },
                };
            }
            var env = SaveSystem.ReadEnvelope(slot);
            return new Dictionary<string, object>
            {
                { "exists", true },
                { "slot", slot },
                { "version", env.version },
                { "savedAtUtc", env.savedAtUtc ?? "" },
                { "payload", env.payload ?? "" },
            };
        }

        static Dictionary<string, object> Delete(Dictionary<string, object> args)
        {
            var slot = MiniJson.GetString(args, "slot", null);
            if (string.IsNullOrEmpty(slot)) throw new AgentException("BAD_ARGS", "slot 必填");
            var deleted = SaveSystem.Delete(slot);
            return new Dictionary<string, object>
            {
                { "slot", slot },
                { "deleted", deleted },
            };
        }

        static Dictionary<string, object> List()
        {
            var names = SaveSystem.List();
            var slots = new List<object>();
            for (var i = 0; i < names.Length; i++) slots.Add(names[i]);
            return new Dictionary<string, object>
            {
                { "root", SaveSystem.RootDir.Replace('\\', '/') },
                { "slots", slots },
            };
        }

        static Dictionary<string, object> PathOf(Dictionary<string, object> args)
        {
            var result = new Dictionary<string, object>
            {
                { "root", SaveSystem.RootDir.Replace('\\', '/') },
            };
            var slot = MiniJson.GetString(args, "slot", null);
            if (!string.IsNullOrEmpty(slot))
                result["path"] = SaveSystem.PathFor(slot).Replace('\\', '/');
            return result;
        }

        static int ReadInt(Dictionary<string, object> args, string key, int fallback)
        {
            if (args == null || !args.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is double d) return (int)d;
            if (v is float f) return (int)f;
            return MiniJson.GetInt(args, key, fallback);
        }
    }
}
