using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Local.UnityAgent
{
    /// <summary>只读场景体检（scene.validate）。不改层级、不保存。</summary>
    public static class SceneCommands
    {
        [MenuItem("Tools/Unity Agent/Validate Scene")]
        public static void ValidateMenu()
        {
            try
            {
                var data = Validate(null);
                var counts = MiniJson.AsObject(data["counts"]);
                Debug.Log("[UnityAgent] Validate Scene scene=" + MiniJson.GetString(data, "scene", "")
                    + " scanned=" + AsInt(data, "scanned")
                    + " errors=" + AsInt(counts, "error")
                    + " warns=" + AsInt(counts, "warn"));
            }
            catch (Exception ex)
            {
                Debug.LogError("[UnityAgent] Validate Scene failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (!string.Equals(cmd, "scene.validate", StringComparison.Ordinal))
                return false;
            data = Validate(args);
            return true;
        }

        public static Dictionary<string, object> Validate(Dictionary<string, object> args)
        {
            var scene = SceneManager.GetActiveScene();
            var rootArg = MiniJson.GetString(args, "root", null);
            var targets = new List<GameObject>();
            var wholeScene = string.IsNullOrEmpty(rootArg);

            if (wholeScene)
            {
                targets.AddRange(scene.GetRootGameObjects());
            }
            else
            {
                var norm = rootArg.Replace('\\', '/').Trim();
                var found = FindByPath(scene, norm);
                if (found != null)
                {
                    targets.Add(found.gameObject);
                }
                else if (ScenePathMatches(scene, norm))
                {
                    wholeScene = true;
                    targets.AddRange(scene.GetRootGameObjects());
                }
                else
                {
                    throw new AgentException("NOT_FOUND", "root not found: " + rootArg);
                }
            }

            var issues = new List<object>();
            var scanned = 0;
            var errors = 0;
            var warns = 0;
            var seen = new HashSet<int>();

            if (wholeScene)
                CheckSiblingNames(scene.GetRootGameObjects(), scene.name ?? "", issues, ref errors, ref warns);

            foreach (var go in targets)
            {
                if (go == null) continue;
                var transforms = go.GetComponentsInChildren<Transform>(true);
                foreach (var tr in transforms)
                {
                    if (tr == null) continue;
                    if (!seen.Add(tr.gameObject.GetInstanceID())) continue;
                    scanned++;
                    var path = AgentCommands.GetHierarchyPath(tr);
                    CheckObject(tr.gameObject, path, issues, ref errors, ref warns);
                    CheckSiblingNames(tr, path, issues, ref errors, ref warns);
                }
            }

            var sceneLabel = string.IsNullOrEmpty(scene.path) ? (scene.name ?? "") : scene.path;
            return new Dictionary<string, object>
            {
                { "scene", sceneLabel },
                { "scanned", scanned },
                {
                    "counts", new Dictionary<string, object>
                    {
                        { "error", errors },
                        { "warn", warns },
                    }
                },
                { "issues", issues },
            };
        }

        static void CheckObject(GameObject go, string path, List<object> issues, ref int errors, ref int warns)
        {
            if (HasMissingScript(go))
                Add(issues, ref errors, ref warns, "missing-script", "error", path, "存在 Missing Script");

            if (IsEmptyObject(go))
                Add(issues, ref errors, ref warns, "empty-object", "warn", path, "只有 Transform，没有其他组件和子物体");

            CheckMeshRefs(go, path, issues, ref errors, ref warns);

            if (PrefabUtility.GetPrefabInstanceStatus(go) == PrefabInstanceStatus.MissingAsset)
                Add(issues, ref errors, ref warns, "broken-prefab", "error", path, "Prefab 实例丢失");
        }

        static bool HasMissingScript(GameObject go)
        {
            var behaviours = go.GetComponents<MonoBehaviour>();
            for (var i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null) return true;
            }
            return false;
        }

        static bool IsEmptyObject(GameObject go)
        {
            if (go.transform.childCount > 0) return false;
            var comps = go.GetComponents<Component>();
            for (var i = 0; i < comps.Length; i++)
            {
                var c = comps[i];
                if (c == null) return false;
                if (!(c is Transform)) return false;
            }
            return true;
        }

        /// <summary>
        /// 只看 MeshFilter.sharedMesh 和 MeshRenderer 材质槽，不扫其他序列化引用。
        /// </summary>
        static void CheckMeshRefs(GameObject go, string path, List<object> issues, ref int errors, ref int warns)
        {
            var missingRef = false;
            var filter = go.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh == null)
            {
                Add(issues, ref errors, ref warns, "missing-mesh", "error", path, "MeshFilter.sharedMesh 为空");
                missingRef = true;
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var mats = renderer.sharedMaterials;
                var count = mats == null ? 0 : mats.Length;
                var nulls = 0;
                for (var i = 0; i < count; i++)
                {
                    if (mats[i] == null) nulls++;
                }
                if (count == 0 || nulls == count)
                {
                    Add(issues, ref errors, ref warns, "missing-mesh", "error", path, "MeshRenderer 没有材质");
                    missingRef = true;
                }
                if (nulls > 0)
                {
                    Add(issues, ref errors, ref warns, "missing-material-slot", "error", path, "材质槽有 " + nulls + " 个空引用");
                    missingRef = true;
                }
            }

            if (missingRef)
                Add(issues, ref errors, ref warns, "missing-reference", "error", path, "MeshRenderer/MeshFilter 引用丢失");
        }

        static void CheckSiblingNames(Transform parent, string parentPath, List<object> issues, ref int errors, ref int warns)
        {
            var kids = new GameObject[parent.childCount];
            for (var i = 0; i < parent.childCount; i++)
                kids[i] = parent.GetChild(i).gameObject;
            CheckSiblingNames(kids, parentPath, issues, ref errors, ref warns);
        }

        static void CheckSiblingNames(IList<GameObject> siblings, string parentPath, List<object> issues, ref int errors, ref int warns)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < siblings.Count; i++)
            {
                var go = siblings[i];
                if (go == null) continue;
                var name = go.name ?? "";
                if (counts.ContainsKey(name)) counts[name]++;
                else counts[name] = 1;
            }
            foreach (var pair in counts)
            {
                if (pair.Value < 2) continue;
                Add(issues, ref errors, ref warns, "duplicate-name", "warn", parentPath,
                    "子物体「" + pair.Key + "」重名 ×" + pair.Value);
            }
        }

        static void Add(List<object> issues, ref int errors, ref int warns, string code, string severity, string path, string message)
        {
            if (severity == "error") errors++;
            else warns++;
            issues.Add(new Dictionary<string, object>
            {
                { "code", code },
                { "severity", severity },
                { "path", path ?? "" },
                { "message", message },
            });
        }

        static Transform FindByPath(Scene scene, string path)
        {
            path = (path ?? "").Replace('\\', '/').Trim('/');
            if (path.Length == 0) return null;
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var all = roots[i].GetComponentsInChildren<Transform>(true);
                for (var j = 0; j < all.Length; j++)
                {
                    if (string.Equals(AgentCommands.GetHierarchyPath(all[j]), path, StringComparison.Ordinal))
                        return all[j];
                }
            }
            return null;
        }

        static bool ScenePathMatches(Scene scene, string root)
        {
            var path = (scene.path ?? "").Replace('\\', '/');
            if (path.Length == 0) return false;
            var norm = (root ?? "").Replace('\\', '/').TrimStart('/');
            return string.Equals(path, norm, StringComparison.OrdinalIgnoreCase);
        }

        static int AsInt(Dictionary<string, object> obj, string key)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return 0;
            return Convert.ToInt32(v);
        }
    }
}
