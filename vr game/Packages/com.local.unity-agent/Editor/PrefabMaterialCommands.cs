using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Local.UnityAgent
{
    /// <summary>
    /// Prefab / Material / Undo transaction commands.
    /// These are intentionally small, path-safe wrappers around UnityEditor APIs;
    /// all mutating calls go through Undo so the agent can roll back a batch.
    /// </summary>
    public static class PrefabMaterialCommands
    {
        static int _txnGroup = -1;
        static string _txnName = "";

        [InitializeOnLoadMethod]
        static void ResetTransactionAfterReload()
        {
            // Undo groups do not survive a domain reload; never pretend one is still open.
            _txnGroup = -1;
            _txnName = "";
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            switch (cmd)
            {
                case "prefab.create": data = PrefabCreate(args); break;
                case "prefab.instantiate": data = PrefabInstantiate(args); break;
                case "prefab.apply": data = PrefabApply(args); break;
                case "prefab.revert": data = PrefabRevert(args); break;
                case "material.create": data = MaterialCreate(args); break;
                case "material.set": data = MaterialSet(args); break;
                case "txn.begin": data = TxnBegin(args); break;
                case "txn.commit": data = TxnCommit(); break;
                case "txn.rollback": data = TxnRollback(); break;
                case "txn.status": data = TxnStatus(); break;
                default: return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- prefab

        static Dictionary<string, object> PrefabCreate(Dictionary<string, object> args)
        {
            var rootPath = MiniJson.GetString(args, "root", null);
            var go = string.IsNullOrEmpty(rootPath) ? Selection.activeGameObject : FindGo(rootPath);
            if (go == null)
                throw new AgentException("BAD_ARGS", "root is required, or select a GameObject in the scene");

            var assetPath = RequireAssetPath(args, "path", ".prefab");
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (existing != null && !MiniJson.GetBool(args, "replace", false))
                throw new AgentException("BAD_ARGS", "prefab already exists (pass replace:true): " + assetPath);

            var saved = PrefabUtility.SaveAsPrefabAsset(go, assetPath, out var success);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            if (!success || saved == null)
                throw new AgentException("INTERNAL", "save prefab failed: " + assetPath);

            return new Dictionary<string, object>
            {
                { "path", assetPath },
                { "name", saved.name },
                { "root", AgentCommands.GetHierarchyPath(go.transform) },
                { "replaced", existing != null },
            };
        }

        static Dictionary<string, object> PrefabInstantiate(Dictionary<string, object> args)
        {
            var assetPath = RequireAssetPath(args, "path", ".prefab");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                throw new AgentException("NOT_FOUND", "prefab not found: " + assetPath);

            var parentPath = MiniJson.GetString(args, "parent", null);
            Transform parent = null;
            if (!string.IsNullOrEmpty(parentPath))
            {
                var parentGo = FindGo(parentPath);
                parent = parentGo.transform;
            }

            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance == null)
                throw new AgentException("INTERNAL", "InstantiatePrefab returned null: " + assetPath);

            if (parent != null) instance.transform.SetParent(parent, false);
            var pos = ReadVector3(args, "position");
            if (pos.HasValue) instance.transform.position = pos.Value;
            Undo.RegisterCreatedObjectUndo(instance, "Instantiate prefab");

            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(instance.transform) },
                { "prefab", assetPath },
                { "name", instance.name },
            };
        }

        static Dictionary<string, object> PrefabApply(Dictionary<string, object> args)
        {
            var go = FindGo(MiniJson.GetString(args, "path", null));
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (root == null)
                throw new AgentException("BAD_ARGS", "not a prefab instance: " + AgentCommands.GetHierarchyPath(go.transform));

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            PrefabUtility.ApplyPrefabInstance(root, InteractionMode.AutomatedAction);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "applied", true },
                { "path", prefabPath },
                { "root", AgentCommands.GetHierarchyPath(root.transform) },
            };
        }

        static Dictionary<string, object> PrefabRevert(Dictionary<string, object> args)
        {
            var go = FindGo(MiniJson.GetString(args, "path", null));
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(go);
            if (root == null)
                throw new AgentException("BAD_ARGS", "not a prefab instance: " + AgentCommands.GetHierarchyPath(go.transform));

            var prefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root);
            PrefabUtility.RevertPrefabInstance(root, InteractionMode.AutomatedAction);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "reverted", true },
                { "path", prefabPath },
                { "root", AgentCommands.GetHierarchyPath(root.transform) },
            };
        }

        // --------------------------------------------------------------- material

        static Dictionary<string, object> MaterialCreate(Dictionary<string, object> args)
        {
            var assetPath = RequireAssetPath(args, "path", ".mat");
            var existing = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (existing != null && !MiniJson.GetBool(args, "replace", false))
                throw new AgentException("BAD_ARGS", "material already exists (pass replace:true): " + assetPath);
            if (existing != null) AssetDatabase.DeleteAsset(assetPath);

            var shaderName = MiniJson.GetString(args, "shader", "Standard");
            var shader = Shader.Find(shaderName);
            if (shader == null)
                throw new AgentException("NOT_FOUND", "shader not found: " + shaderName);

            var material = new Material(shader)
            {
                name = Path.GetFileNameWithoutExtension(assetPath),
            };
            AssetDatabase.CreateAsset(material, assetPath);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "path", assetPath },
                { "shader", shader.name },
                { "name", material.name },
            };
        }

        static Dictionary<string, object> MaterialSet(Dictionary<string, object> args)
        {
            var assetPath = RequireAssetPath(args, "path", ".mat");
            var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (material == null)
                throw new AgentException("NOT_FOUND", "material not found: " + assetPath);

            var property = MiniJson.GetString(args, "property", null);
            if (string.IsNullOrEmpty(property))
                throw new AgentException("BAD_ARGS", "property is required");
            if (!material.HasProperty(property))
                throw new AgentException("NOT_FOUND", "material has no property: " + property);
            if (args == null || !args.TryGetValue("value", out var value))
                throw new AgentException("BAD_ARGS", "value is required");

            var typeName = PropertyTypeName(material.shader, property);
            switch (typeName)
            {
                case "Color":
                    material.SetColor(property, ReadColor(value));
                    break;
                case "Vector":
                    material.SetVector(property, ReadVector4(value));
                    break;
                case "Float":
                case "Range":
                case "Int":
                    material.SetFloat(property, ToFloat(value));
                    break;
                case "TexEnv":
                case "Texture":
                    var texturePath = PathUtil.ToAssetPath(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture));
                    var texture = AssetDatabase.LoadAssetAtPath<Texture>(texturePath);
                    if (texture == null)
                        throw new AgentException("NOT_FOUND", "texture not found: " + texturePath);
                    material.SetTexture(property, texture);
                    break;
                default:
                    throw new AgentException("BAD_ARGS", "unsupported shader property type: " + typeName);
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "path", assetPath },
                { "property", property },
                { "type", typeName },
            };
        }

        // --------------------------------------------------------------- transaction

        static Dictionary<string, object> TxnBegin(Dictionary<string, object> args)
        {
            if (_txnGroup >= 0)
                throw new AgentException("BAD_REQUEST", "transaction already open (group " + _txnGroup + ")");

            var name = MiniJson.GetString(args, "name", "agent transaction");
            Undo.IncrementCurrentGroup();
            _txnGroup = Undo.GetCurrentGroup();
            _txnName = name;
            Undo.SetCurrentGroupName(name);
            return new Dictionary<string, object>
            {
                { "active", true },
                { "group", _txnGroup },
                { "name", _txnName },
            };
        }

        static Dictionary<string, object> TxnCommit()
        {
            if (_txnGroup < 0)
                throw new AgentException("BAD_REQUEST", "no open transaction");
            var group = _txnGroup;
            Undo.CollapseUndoOperations(group);
            _txnGroup = -1;
            var name = _txnName;
            _txnName = "";
            return new Dictionary<string, object>
            {
                { "active", false },
                { "committed", true },
                { "group", group },
                { "name", name },
            };
        }

        static Dictionary<string, object> TxnRollback()
        {
            if (_txnGroup < 0)
                throw new AgentException("BAD_REQUEST", "no open transaction");
            var group = _txnGroup;
            Undo.RevertAllDownToGroup(group);
            _txnGroup = -1;
            var name = _txnName;
            _txnName = "";
            return new Dictionary<string, object>
            {
                { "active", false },
                { "rolledBack", true },
                { "group", group },
                { "name", name },
            };
        }

        static Dictionary<string, object> TxnStatus()
        {
            return new Dictionary<string, object>
            {
                { "active", _txnGroup >= 0 },
                { "group", _txnGroup },
                { "name", _txnName },
            };
        }

        // ---------------------------------------------------------------- helpers

        static string RequireAssetPath(Dictionary<string, object> args, string key, string extension)
        {
            var raw = MiniJson.GetString(args, key, null);
            if (string.IsNullOrEmpty(raw))
                throw new AgentException("BAD_ARGS", key + " is required");
            var assetPath = PathUtil.ToAssetPath(raw);
            if (!assetPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", key + " must end with " + extension + ": " + assetPath);

            var full = PathUtil.ResolveProjectRelative(assetPath, allowLibraryAgent: false);
            var dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) PathUtil.EnsureDir(dir);
            return assetPath;
        }

        static GameObject FindGo(string rawPath)
        {
            var path = (rawPath ?? "").Replace('\\', '/').Trim('/');
            if (path.Length == 0)
                throw new AgentException("BAD_ARGS", "path is required");

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            for (var i = 0; i < roots.Length; i++)
            {
                var transforms = roots[i].GetComponentsInChildren<Transform>(true);
                for (var j = 0; j < transforms.Length; j++)
                {
                    if (string.Equals(AgentCommands.GetHierarchyPath(transforms[j]), path, StringComparison.Ordinal))
                        return transforms[j].gameObject;
                }
            }
            throw new AgentException("NOT_FOUND", "object not found: " + rawPath);
        }

        static string PropertyTypeName(Shader shader, string property)
        {
            if (shader == null) return "";
            var count = shader.GetPropertyCount();
            for (var i = 0; i < count; i++)
            {
                if (string.Equals(shader.GetPropertyName(i), property, StringComparison.Ordinal))
                    return ShaderUtil.GetPropertyType(shader, i).ToString();
            }
            return "";
        }

        static Vector3? ReadVector3(Dictionary<string, object> args, string key)
        {
            if (args == null || !args.TryGetValue(key, out var value) || value == null) return null;
            var list = MiniJson.AsArray(value);
            if (list == null || list.Count < 3)
                throw new AgentException("BAD_ARGS", key + " must be [x,y,z]");
            return new Vector3(ToFloat(list[0]), ToFloat(list[1]), ToFloat(list[2]));
        }

        static Vector4 ReadVector4(object value)
        {
            var list = MiniJson.AsArray(value);
            if (list == null || list.Count < 2)
                throw new AgentException("BAD_ARGS", "vector value must be an array of 2-4 numbers");
            var x = ToFloat(list[0]);
            var y = ToFloat(list[1]);
            var z = list.Count > 2 ? ToFloat(list[2]) : 0f;
            var w = list.Count > 3 ? ToFloat(list[3]) : 0f;
            return new Vector4(x, y, z, w);
        }

        static Color ReadColor(object value)
        {
            var list = MiniJson.AsArray(value);
            if (list == null || list.Count < 3)
                throw new AgentException("BAD_ARGS", "color value must be [r,g,b] or [r,g,b,a]");
            var r = ToFloat(list[0]);
            var g = ToFloat(list[1]);
            var b = ToFloat(list[2]);
            var a = list.Count > 3 ? ToFloat(list[3]) : 1f;
            return new Color(r, g, b, a);
        }

        static float ToFloat(object value)
        {
            if (value is bool b) return b ? 1f : 0f;
            return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
