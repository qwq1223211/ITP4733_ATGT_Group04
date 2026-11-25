using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>AnimatorController / AnimationClip bridge commands.</summary>
    public static class AnimationCommands
    {
        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            switch (cmd)
            {
                case "animator.create": data = CreateController(args); break;
                case "animator.addParameter": data = AddParameter(args); break;
                case "animator.addState": data = AddState(args); break;
                case "animator.setController": data = SetController(args); break;
                case "animator.info": data = Info(args); break;
                case "anim.clip.create": data = CreateClip(args); break;
                default: return false;
            }
            return true;
        }

        static Dictionary<string, object> CreateController(Dictionary<string, object> args)
        {
            var assetPath = RequireAssetPath(args, "path", ".controller");
            var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (existing != null && !MiniJson.GetBool(args, "replace", false))
                throw new AgentException("BAD_ARGS", "controller already exists (pass replace:true): " + assetPath);
            if (existing != null) AssetDatabase.DeleteAsset(assetPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(assetPath);
            if (controller == null)
                throw new AgentException("INTERNAL", "CreateAnimatorControllerAtPath failed: " + assetPath);
            if (controller.layers.Length == 0) controller.AddLayer("Base");
            AssetDatabase.SaveAssets();
            return InfoFor(controller, assetPath);
        }

        static Dictionary<string, object> CreateClip(Dictionary<string, object> args)
        {
            var assetPath = RequireAssetPath(args, "path", ".anim");
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
            if (existing != null && !MiniJson.GetBool(args, "replace", false))
                throw new AgentException("BAD_ARGS", "clip already exists (pass replace:true): " + assetPath);
            if (existing != null) AssetDatabase.DeleteAsset(assetPath);

            var clip = new AnimationClip
            {
                name = Path.GetFileNameWithoutExtension(assetPath),
                frameRate = MiniJson.GetDouble(args, "frameRate", 30) > 0
                    ? (float)MiniJson.GetDouble(args, "frameRate", 30)
                    : 30f,
            };
            AssetDatabase.CreateAsset(clip, assetPath);
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "path", assetPath },
                { "name", clip.name },
                { "frameRate", clip.frameRate },
                { "length", clip.length },
            };
        }

        static Dictionary<string, object> AddParameter(Dictionary<string, object> args)
        {
            var assetPath = RequireAssetPath(args, "path", ".controller");
            var controller = LoadController(assetPath);
            var name = MiniJson.GetString(args, "name", "");
            if (string.IsNullOrEmpty(name)) throw new AgentException("BAD_ARGS", "name is required");

            var typeName = MiniJson.GetString(args, "type", "float");
            if (!Enum.TryParse(typeName, true, out AnimatorControllerParameterType type))
                throw new AgentException("BAD_ARGS", "bad parameter type: " + typeName);

            foreach (var parameter in controller.parameters)
            {
                if (parameter.name == name)
                    throw new AgentException("BAD_ARGS", "parameter already exists: " + name);
            }
            controller.AddParameter(name, type);
            foreach (var parameter in controller.parameters)
            {
                if (parameter.name != name) continue;
                parameter.defaultFloat = (float)MiniJson.GetDouble(args, "default", 0);
                parameter.defaultInt = MiniJson.GetInt(args, "default", 0);
                parameter.defaultBool = MiniJson.GetBool(args, "default", false);
                break;
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return InfoFor(controller, assetPath);
        }

        static Dictionary<string, object> AddState(Dictionary<string, object> args)
        {
            var assetPath = RequireAssetPath(args, "path", ".controller");
            var controller = LoadController(assetPath);
            if (controller.layers.Length == 0) controller.AddLayer("Base");
            var stateMachine = controller.layers[0].stateMachine;
            var name = MiniJson.GetString(args, "name", "New State");
            if (string.IsNullOrEmpty(name)) throw new AgentException("BAD_ARGS", "name is required");

            var state = stateMachine.AddState(name);
            var clipPath = MiniJson.GetString(args, "clip", "");
            if (!string.IsNullOrEmpty(clipPath))
            {
                var clipAsset = AssetDatabase.LoadAssetAtPath<AnimationClip>(PathUtil.ToAssetPath(clipPath));
                if (clipAsset == null) throw new AgentException("NOT_FOUND", "clip not found: " + clipPath);
                state.motion = clipAsset;
            }
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return InfoFor(controller, assetPath);
        }

        static Dictionary<string, object> SetController(Dictionary<string, object> args)
        {
            var go = FindGo(MiniJson.GetString(args, "object", ""));
            var assetPath = RequireAssetPath(args, "path", ".controller");
            var controller = LoadController(assetPath);
            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = Undo.AddComponent<Animator>(go);
            Undo.RecordObject(animator, "UnityAgent.animator.setController");
            animator.runtimeAnimatorController = controller;
            EditorUtility.SetDirty(animator);
            return new Dictionary<string, object>
            {
                { "object", AgentCommands.GetHierarchyPath(go.transform) },
                { "controller", assetPath },
            };
        }

        static Dictionary<string, object> Info(Dictionary<string, object> args)
        {
            var assetPath = RequireAssetPath(args, "path", ".controller");
            return InfoFor(LoadController(assetPath), assetPath);
        }

        static Dictionary<string, object> InfoFor(AnimatorController controller, string assetPath)
        {
            var parameters = new List<object>();
            foreach (var parameter in controller.parameters)
            {
                parameters.Add(new Dictionary<string, object>
                {
                    { "name", parameter.name },
                    { "type", parameter.type.ToString() },
                });
            }
            var layers = new List<object>();
            foreach (var layer in controller.layers)
            {
                var states = new List<object>();
                if (layer.stateMachine != null)
                {
                    foreach (var child in layer.stateMachine.states)
                        states.Add(child.state.name);
                }
                layers.Add(new Dictionary<string, object>
                {
                    { "name", layer.name },
                    { "states", states },
                });
            }
            return new Dictionary<string, object>
            {
                { "path", assetPath },
                { "parameters", parameters },
                { "layers", layers },
            };
        }

        static AnimatorController LoadController(string assetPath)
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(assetPath);
            if (controller == null) throw new AgentException("NOT_FOUND", "controller not found: " + assetPath);
            return controller;
        }

        static string RequireAssetPath(Dictionary<string, object> args, string key, string extension)
        {
            var raw = MiniJson.GetString(args, key, null);
            if (string.IsNullOrEmpty(raw)) throw new AgentException("BAD_ARGS", key + " is required");
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
