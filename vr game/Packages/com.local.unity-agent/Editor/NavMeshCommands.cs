using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

namespace Local.UnityAgent
{
    /// <summary>Generic NavMesh bake / query / agent bridge commands.</summary>
    public static class NavMeshCommands
    {
        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            switch (cmd)
            {
                case "navmesh.bake": data = Bake(args); break;
                case "navmesh.info": data = Info(); break;
                case "navmesh.markStatic": data = MarkStatic(args); break;
                case "navmesh.addAgent": data = AddAgent(args); break;
                case "navmesh.sample": data = Sample(args); break;
                default: return false;
            }
            return true;
        }

        static Dictionary<string, object> Bake(Dictionary<string, object> args)
        {
            var marked = 0;
            if (MiniJson.GetBool(args, "markStatic", false))
            {
                var markedInfo = MarkStatic(args);
                marked = MiniJson.GetInt(markedInfo, "markedStatic", 0);
            }

            UnityEditor.AI.NavMeshBuilder.BuildNavMesh();
            var result = Info();
            result["markedStatic"] = marked;
            return result;
        }

        static Dictionary<string, object> Info()
        {
            var triangulation = NavMesh.CalculateTriangulation();
            var result = new Dictionary<string, object>
            {
                { "vertices", triangulation.vertices != null ? triangulation.vertices.Length : 0 },
                { "triangles", triangulation.indices != null ? triangulation.indices.Length / 3 : 0 },
                { "areas", triangulation.areas != null ? triangulation.areas.Length : 0 },
            };
            try
            {
                var settings = NavMesh.GetSettingsByID(0);
                result["agentRadius"] = settings.agentRadius;
                result["agentHeight"] = settings.agentHeight;
                result["agentSlope"] = settings.agentSlope;
                result["agentClimb"] = settings.agentClimb;
            }
            catch (Exception ex)
            {
                result["settingsError"] = ex.Message;
            }
            return result;
        }

        static Dictionary<string, object> MarkStatic(Dictionary<string, object> args)
        {
            var root = MiniJson.GetString(args, "root", "");
            var marked = 0;
            if (string.IsNullOrEmpty(root))
            {
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                foreach (var go in scene.GetRootGameObjects())
                    marked += MarkTree(go);
            }
            else
            {
                marked = MarkTree(FindGo(root));
            }
            return new Dictionary<string, object>
            {
                { "markedStatic", marked },
                { "root", string.IsNullOrEmpty(root) ? "<scene>" : root },
            };
        }

        static int MarkTree(GameObject root)
        {
            var count = 0;
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
            {
                var go = transform.gameObject;
                var flags = GameObjectUtility.GetStaticEditorFlags(go);
                GameObjectUtility.SetStaticEditorFlags(go, flags | StaticEditorFlags.NavigationStatic);
                count++;
            }
            return count;
        }

        static Dictionary<string, object> AddAgent(Dictionary<string, object> args)
        {
            var go = FindGo(MiniJson.GetString(args, "object", ""));
            var agent = go.GetComponent<NavMeshAgent>();
            if (agent == null) agent = Undo.AddComponent<NavMeshAgent>(go);
            Undo.RecordObject(agent, "UnityAgent.navmesh.addAgent");
            agent.speed = (float)MiniJson.GetDouble(args, "speed", agent.speed);
            agent.angularSpeed = (float)MiniJson.GetDouble(args, "angularSpeed", agent.angularSpeed);
            agent.acceleration = (float)MiniJson.GetDouble(args, "acceleration", agent.acceleration);
            agent.stoppingDistance = (float)MiniJson.GetDouble(args, "stoppingDistance", agent.stoppingDistance);
            agent.radius = (float)MiniJson.GetDouble(args, "radius", agent.radius);
            agent.height = (float)MiniJson.GetDouble(args, "height", agent.height);
            EditorUtility.SetDirty(agent);
            return new Dictionary<string, object>
            {
                { "object", AgentCommands.GetHierarchyPath(go.transform) },
                { "speed", agent.speed },
                { "radius", agent.radius },
                { "height", agent.height },
                { "onNavMesh", agent.isOnNavMesh },
            };
        }

        static Dictionary<string, object> Sample(Dictionary<string, object> args)
        {
            var position = ReadVector3(args, "position");
            if (!position.HasValue) throw new AgentException("BAD_ARGS", "position [x,y,z] is required");
            var maxDistance = (float)MiniJson.GetDouble(args, "maxDistance", 2);
            if (NavMesh.SamplePosition(position.Value, out var hit, maxDistance, NavMesh.AllAreas))
            {
                return new Dictionary<string, object>
                {
                    { "hit", true },
                    { "position", new List<object> { hit.position.x, hit.position.y, hit.position.z } },
                    { "distance", hit.distance },
                };
            }
            return new Dictionary<string, object>
            {
                { "hit", false },
                { "position", new List<object> { position.Value.x, position.Value.y, position.Value.z } },
            };
        }

        static Vector3? ReadVector3(Dictionary<string, object> args, string key)
        {
            if (args == null || !args.TryGetValue(key, out var value) || value == null) return null;
            var list = MiniJson.AsArray(value);
            if (list == null || list.Count < 3)
                throw new AgentException("BAD_ARGS", key + " must be [x,y,z]");
            return new Vector3(
                Convert.ToSingle(list[0], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToSingle(list[1], System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToSingle(list[2], System.Globalization.CultureInfo.InvariantCulture));
        }

        static GameObject FindGo(string rawPath)
        {
            var path = (rawPath ?? "").Replace('\\', '/').Trim('/');
            if (path.Length == 0) throw new AgentException("BAD_ARGS", "object is required");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (string.Equals(AgentCommands.GetHierarchyPath(transform), path, StringComparison.Ordinal))
                        return transform.gameObject;
                }
            }
            throw new AgentException("NOT_FOUND", "object not found: " + rawPath);
        }
    }
}
