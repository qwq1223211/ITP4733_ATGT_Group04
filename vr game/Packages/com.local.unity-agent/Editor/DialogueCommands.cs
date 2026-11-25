using System;
using System.Collections.Generic;
using Local.UnityAgent.Runtime;
using UnityEditor;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>对话/任务资产与挂载。会写 Assets，不保存场景。</summary>
    public static class DialogueCommands
    {
        const string DialogueFolder = "Assets/Dialogues";
        const string QuestFolder = "Assets/Quests";

        [MenuItem("Tools/Unity Agent/Create Dialogue")]
        public static void MenuDialogue()
        {
            try
            {
                var data = CreateDialogue(new Dictionary<string, object>
                {
                    { "name", "Dialogue" },
                    { "lines", new List<object>
                        {
                            new Dictionary<string, object> { { "speaker", "NPC" }, { "text", "你好" } },
                        }
                    },
                });
                UnityEngine.Debug.Log("[UnityAgent] Create Dialogue path=" + MiniJson.GetString(data, "path"));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Create Dialogue failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "dialogue.create") { data = CreateDialogue(args); return true; }
            if (cmd == "dialogue.info") { data = DialogueInfo(); return true; }
            if (cmd == "quest.create") { data = CreateQuest(args); return true; }
            if (cmd == "quest.info") { data = QuestInfo(); return true; }
            if (cmd == "dialogue.attach_runner") { data = AttachRunner(args); return true; }
            if (cmd == "quest.attach_log") { data = AttachLog(args); return true; }
            return false;
        }

        public static Dictionary<string, object> CreateDialogue(Dictionary<string, object> args)
        {
            var name = SafeName(MiniJson.GetString(args, "name", null));
            var folder = Folder(args, DialogueFolder);
            var lines = ReadLines(args);
            if (lines.Count == 0)
                lines.Add(new DialogueLine { speaker = "Narrator", text = "……" });
            EnsureFolder(folder);
            var assetPath = folder + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
            var asset = ScriptableObject.CreateInstance<DialogueAsset>();
            asset.lines = lines.ToArray();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "path", assetPath },
                { "lines", lines.Count },
            };
        }

        public static Dictionary<string, object> DialogueInfo()
        {
            return ListAssets<DialogueAsset>(DialogueFolder, "dialogues");
        }

        public static Dictionary<string, object> CreateQuest(Dictionary<string, object> args)
        {
            var name = SafeName(MiniJson.GetString(args, "name", null));
            var folder = Folder(args, QuestFolder);
            var id = MiniJson.GetString(args, "id", name);
            if (string.IsNullOrEmpty(id)) id = name;
            var title = MiniJson.GetString(args, "title", name);
            if (string.IsNullOrEmpty(title)) title = name;
            var description = MiniJson.GetString(args, "description", "");
            var objectives = ReadStrings(args, "objectives");
            EnsureFolder(folder);
            var assetPath = folder + "/" + name + ".asset";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
                AssetDatabase.DeleteAsset(assetPath);
            var asset = ScriptableObject.CreateInstance<QuestAsset>();
            asset.id = id;
            asset.title = title;
            asset.description = description ?? "";
            asset.objectives = objectives.ToArray();
            AssetDatabase.CreateAsset(asset, assetPath);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "path", assetPath },
                { "objectives", objectives.Count },
            };
        }

        public static Dictionary<string, object> QuestInfo()
        {
            return ListAssets<QuestAsset>(QuestFolder, "quests");
        }

        public static Dictionary<string, object> AttachRunner(Dictionary<string, object> args)
        {
            var go = RequireGo(args);
            var runner = go.GetComponent<DialogueRunner>();
            if (runner == null) runner = go.AddComponent<DialogueRunner>();
            var dialoguePath = MiniJson.GetString(args, "dialoguePath", null);
            if (!string.IsNullOrEmpty(dialoguePath))
            {
                var asset = AssetDatabase.LoadAssetAtPath<DialogueAsset>(dialoguePath.Replace('\\', '/'));
                if (asset == null) throw new AgentException("NOT_FOUND", "dialogue not found: " + dialoguePath);
                runner.dialogue = asset;
            }
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(go.transform) },
                { "dialoguePath", dialoguePath ?? "" },
                { "hasDialogue", runner.dialogue != null },
            };
        }

        public static Dictionary<string, object> AttachLog(Dictionary<string, object> args)
        {
            var go = RequireGo(args);
            var log = go.GetComponent<QuestLog>();
            if (log == null) log = go.AddComponent<QuestLog>();
            if (log.active == null) log.active = new List<QuestAsset>();
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(go.transform) },
                { "active", log.active.Count },
            };
        }

        static Dictionary<string, object> ListAssets<T>(string folder, string key) where T : ScriptableObject
        {
            var paths = new List<object>();
            if (AssetDatabase.IsValidFolder(folder))
            {
                var guids = AssetDatabase.FindAssets("t:" + typeof(T).Name, new[] { folder });
                for (var i = 0; i < guids.Length; i++)
                    paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }
            return new Dictionary<string, object>
            {
                { key, paths },
                { "count", paths.Count },
            };
        }

        static List<DialogueLine> ReadLines(Dictionary<string, object> args)
        {
            var lines = new List<DialogueLine>();
            if (args == null || !args.TryGetValue("lines", out var raw) || !(raw is List<object> list)) return lines;
            for (var i = 0; i < list.Count; i++)
            {
                var row = MiniJson.AsObject(list[i]);
                if (row == null) continue;
                lines.Add(new DialogueLine
                {
                    speaker = MiniJson.GetString(row, "speaker", ""),
                    text = MiniJson.GetString(row, "text", ""),
                });
            }
            return lines;
        }

        static List<string> ReadStrings(Dictionary<string, object> args, string key)
        {
            var list = new List<string>();
            if (args == null || !args.TryGetValue(key, out var raw) || !(raw is List<object> arr)) return list;
            for (var i = 0; i < arr.Count; i++)
            {
                if (arr[i] == null) continue;
                list.Add(Convert.ToString(arr[i]));
            }
            return list;
        }

        static void EnsureFolder(string folder)
        {
            var parts = folder.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        static string Folder(Dictionary<string, object> args, string fallback)
        {
            var folder = MiniJson.GetString(args, "folder", fallback);
            if (string.IsNullOrWhiteSpace(folder)) folder = fallback;
            folder = folder.Replace('\\', '/').Trim('/');
            if (!folder.StartsWith("Assets/", StringComparison.Ordinal) && folder != "Assets")
                throw new AgentException("BAD_ARGS", "folder 必须在 Assets 下: " + folder);
            return folder;
        }

        static string SafeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new AgentException("BAD_ARGS", "name 必填");
            name = name.Trim();
            if (name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ".asset".Length);
            if (name.IndexOfAny(new[] { '/', '\\', ':' }) >= 0)
                throw new AgentException("BAD_ARGS", "name 不能包含路径");
            return name;
        }

        static GameObject RequireGo(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            if (string.IsNullOrEmpty(path)) throw new AgentException("BAD_ARGS", "path 必填");
            var t = AgentCommands.FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            return t.gameObject;
        }
    }
}
