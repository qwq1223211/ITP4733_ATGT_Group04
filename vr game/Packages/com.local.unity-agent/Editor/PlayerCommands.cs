using System;
using System.Collections.Generic;
using Local.UnityAgent.Runtime;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>场景里放一个第一人称玩家。不保存场景。</summary>
    public static class PlayerCommands
    {
        [MenuItem("Tools/Unity Agent/Create Player")]
        public static void MenuCreate()
        {
            try
            {
                var data = Create(null);
                UnityEngine.Debug.Log("[UnityAgent] Create Player path=" + MiniJson.GetString(data, "path"));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Create Player failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "player.create") { data = Create(args); return true; }
            if (cmd == "player.info") { data = Info(); return true; }
            return false;
        }

        public static Dictionary<string, object> Create(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", "Player");
            if (string.IsNullOrWhiteSpace(name)) name = "Player";
            var height = ReadFloat(args, "height", 1.8f);
            if (height < 0.5f) height = 1.8f;
            var speed = ReadFloat(args, "speed", 5f);
            var jump = ReadFloat(args, "jump", 1.2f);
            var sensitivity = ReadFloat(args, "sensitivity", 2f);

            var root = new GameObject(name);
            if (args != null && args.TryGetValue("spawn", out var spawn) && spawn != null)
                root.transform.position = ReadVec3(spawn, Vector3.zero);

            var cc = root.AddComponent<CharacterController>();
            cc.height = height;
            cc.radius = height * 0.25f;
            cc.center = new Vector3(0f, height * 0.5f, 0f);

            var fps = root.AddComponent<FirstPersonController>();
            fps.moveSpeed = speed;
            fps.jumpHeight = jump;
            fps.mouseSensitivity = sensitivity;

            var camGo = new GameObject(name + "Camera");
            camGo.transform.SetParent(root.transform, false);
            camGo.transform.localPosition = new Vector3(0f, height - 0.25f, 0f);
            camGo.AddComponent<Camera>();
            camGo.AddComponent<AudioListener>();
            TryTag(camGo, "MainCamera");
            TryTag(root, "Player");
            fps.cameraTransform = camGo.transform;

            Undo.RegisterCreatedObjectUndo(root, "UnityAgent.player.create");
            Undo.RegisterCreatedObjectUndo(camGo, "UnityAgent.player.create");

            var comps = new List<object>();
            foreach (var c in root.GetComponents<Component>())
            {
                if (c == null) continue;
                comps.Add(c.GetType().FullName);
            }

            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(root.transform) },
                { "cameraPath", AgentCommands.GetHierarchyPath(camGo.transform) },
                { "height", height },
                { "components", comps },
            };
        }

        public static Dictionary<string, object> Info()
        {
            var players = new List<object>();
            foreach (var fps in UnityEngine.Object.FindObjectsOfType<FirstPersonController>(true))
            {
                if (fps == null) continue;
                players.Add(new Dictionary<string, object>
                {
                    { "path", AgentCommands.GetHierarchyPath(fps.transform) },
                    { "hasCamera", fps.cameraTransform != null },
                });
            }
            return new Dictionary<string, object> { { "players", players } };
        }

        static void TryTag(GameObject go, string tag)
        {
            var tags = InternalEditorUtility.tags;
            for (var i = 0; i < tags.Length; i++)
            {
                if (tags[i] != tag) continue;
                go.tag = tag;
                return;
            }
        }

        static float ReadFloat(Dictionary<string, object> args, string key, float fallback)
        {
            if (args == null || !args.ContainsKey(key) || args[key] == null) return fallback;
            return (float)MiniJson.GetDouble(args, key, fallback);
        }

        static Vector3 ReadVec3(object v, Vector3 fallback)
        {
            if (v is Dictionary<string, object> d)
            {
                return new Vector3(
                    (float)MiniJson.GetDouble(d, "x", fallback.x),
                    (float)MiniJson.GetDouble(d, "y", fallback.y),
                    (float)MiniJson.GetDouble(d, "z", fallback.z));
            }
            if (v is List<object> a && a.Count >= 3)
            {
                return new Vector3(Convert.ToSingle(a[0]), Convert.ToSingle(a[1]), Convert.ToSingle(a[2]));
            }
            return fallback;
        }
    }
}
