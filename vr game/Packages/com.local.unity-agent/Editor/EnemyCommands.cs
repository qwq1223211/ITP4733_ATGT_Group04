using System;
using System.Collections.Generic;
using Local.UnityAgent.Runtime;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.AI;

namespace Local.UnityAgent
{
    /// <summary>场景里放一个巡逻敌人。不保存场景。</summary>
    public static class EnemyCommands
    {
        [MenuItem("Tools/Unity Agent/Create Enemy")]
        public static void MenuCreate()
        {
            try
            {
                var data = Create(new Dictionary<string, object>
                {
                    { "waypoints", DefaultTriangle() },
                });
                UnityEngine.Debug.Log("[UnityAgent] Create Enemy path=" + MiniJson.GetString(data, "path")
                    + " waypoints=" + ReadCount(data, "waypoints"));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Create Enemy failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "enemy.create") { data = Create(args); return true; }
            if (cmd == "enemy.info") { data = Info(); return true; }
            return false;
        }

        public static Dictionary<string, object> Create(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", "Enemy");
            if (string.IsNullOrWhiteSpace(name)) name = "Enemy";
            var chaseRange = ReadFloat(args, "chaseRange", 8f);
            var speed = ReadFloat(args, "speed", 2f);
            var spawn = Vector3.zero;
            if (args != null && args.TryGetValue("spawn", out var spawnVal) && spawnVal != null)
                spawn = ReadVec3(spawnVal, Vector3.zero);

            var root = new GameObject(name);
            root.transform.position = spawn;

            var agent = root.AddComponent<NavMeshAgent>();
            agent.speed = speed;
            agent.radius = 0.4f;
            agent.height = 1.8f;

            var patrol = root.AddComponent<PatrolAgent>();
            patrol.chaseRange = chaseRange;

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            Tint(body, new Color(0.72f, 0.16f, 0.16f, 1f));

            var points = ReadPoints(args != null && args.ContainsKey("waypoints") ? args["waypoints"] : null);
            var holder = new GameObject("Waypoints");
            holder.transform.SetParent(root.transform, false);
            var links = new Transform[points.Count];
            var wpPaths = new List<object>();
            for (var i = 0; i < points.Count; i++)
            {
                var wp = new GameObject("Waypoint_" + i);
                wp.transform.SetParent(holder.transform, false);
                wp.transform.localPosition = points[i] - spawn;
                links[i] = wp.transform;
                wpPaths.Add(AgentCommands.GetHierarchyPath(wp.transform));
            }
            patrol.waypoints = links;
            TryTag(root, "Enemy");

            Undo.RegisterCreatedObjectUndo(root, "UnityAgent.enemy.create");

            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(root.transform) },
                { "agent", agent.GetType().FullName },
                { "patrol", patrol.GetType().FullName },
                { "waypoints", wpPaths },
                { "chaseRange", chaseRange },
                { "speed", speed },
            };
        }

        public static Dictionary<string, object> Info()
        {
            var enemies = new List<object>();
            foreach (var patrol in UnityEngine.Object.FindObjectsOfType<PatrolAgent>(true))
            {
                if (patrol == null) continue;
                var n = patrol.waypoints != null ? patrol.waypoints.Length : 0;
                enemies.Add(new Dictionary<string, object>
                {
                    { "path", AgentCommands.GetHierarchyPath(patrol.transform) },
                    { "waypoints", n },
                    { "chaseRange", patrol.chaseRange },
                });
            }
            return new Dictionary<string, object> { { "enemies", enemies } };
        }

        static List<object> DefaultTriangle()
        {
            return new List<object>
            {
                new List<object> { 2d, 0d, 0d },
                new List<object> { 0d, 0d, 2d },
                new List<object> { -2d, 0d, 0d },
            };
        }

        static void Tint(GameObject go, Color color)
        {
            if (!go.TryGetComponent<MeshRenderer>(out var renderer)) return;
            var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            if (mat == null)
            {
                var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
                if (shader == null) return;
                mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
            renderer.sharedMaterial = mat;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
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

        static List<Vector3> ReadPoints(object v)
        {
            var list = new List<Vector3>();
            if (!(v is List<object> arr)) return list;
            for (var i = 0; i < arr.Count; i++)
                list.Add(ReadVec3(arr[i], Vector3.zero));
            return list;
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
                return new Vector3(Convert.ToSingle(a[0]), Convert.ToSingle(a[1]), Convert.ToSingle(a[2]));
            return fallback;
        }

        static float ReadFloat(Dictionary<string, object> args, string key, float fallback)
        {
            if (args == null || !args.ContainsKey(key) || args[key] == null) return fallback;
            return (float)MiniJson.GetDouble(args, key, fallback);
        }

        static int ReadCount(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.TryGetValue(key, out var v) || v == null) return 0;
            if (v is List<object> list) return list.Count;
            if (v is double d) return (int)d;
            return 0;
        }
    }
}
