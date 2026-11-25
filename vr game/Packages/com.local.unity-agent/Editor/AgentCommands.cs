using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Local.UnityAgent
{
    // 记录"最后一次编译真正结束"的时间：compile.wait 之前只报当前状态，
    // 改动还没开始编译时会秒回 completed=true，调用方就以为已经编好了。
    [InitializeOnLoad]
    public static class CompileTracker
    {
        const string PrefKey = "UnityAgent.LastCompileFinished";

        public static DateTime LastFinishedUtc { get; private set; } = DateTime.MinValue;

        static CompileTracker()
        {
            // 域重载会清空静态字段，但 compilationFinished 是在旧域里触发的，
            // 所以时间戳要落到 EditorPrefs，新域启动时再读回来。
            var saved = EditorPrefs.GetString(PrefKey, "");
            if (!string.IsNullOrEmpty(saved))
            {
                DateTime parsed;
                if (DateTime.TryParse(saved, out parsed))
                    LastFinishedUtc = parsed;
            }

            CompilationPipeline.compilationFinished += _ => MarkFinished();
        }

        static void MarkFinished()
        {
            LastFinishedUtc = DateTime.UtcNow;
            EditorPrefs.SetString(PrefKey, LastFinishedUtc.ToString("o"));
        }

        public static string LastFinishedIso
        {
            get { return LastFinishedUtc == DateTime.MinValue ? "" : LastFinishedUtc.ToString("o"); }
        }

        public static long LastFinishedUnixMs
        {
            get { return LastFinishedUtc == DateTime.MinValue ? 0L : new DateTimeOffset(LastFinishedUtc).ToUnixTimeMilliseconds(); }
        }
    }

    public static class AgentCommands
    {
        public static Dictionary<string, object> Execute(string id, string cmd, Dictionary<string, object> args, int timeoutMs)
        {
            if (string.IsNullOrEmpty(cmd))
                return AgentProtocol.Fail(id, "BAD_REQUEST", "cmd required");

            // 让用户在 Unity 里看得见"这次是 DSH 在动手"；只读命令在 HUD 内部会被过滤。
            AgentHud.Note(cmd, args);

            try
            {
                object data;
                switch (cmd)
                {
                    case "ping": data = CmdPing(); break;
                    case "status": data = CmdStatus(); break;
                    case "console.tail": data = CmdConsoleTail(args); break;
                    case "console.clear": data = CmdConsoleClear(); break;
                    case "compile.wait": data = CmdCompileWait(args, timeoutMs); break;
                    case "compile.force": data = CmdCompileForce(); break;
                    case "scene.hierarchy": data = CmdSceneHierarchy(args); break;
                    case "scene.open": data = CmdSceneOpen(args); break;
                    case "scene.save": data = CmdSceneSave(args); break;
                    case "scene.saveAll": data = CmdSceneSaveAll(); break;
                    case "go.create": data = CmdGoCreate(args); break;
                    case "go.find": data = CmdGoFind(args); break;
                    case "go.set": data = CmdGoSet(args); break;
                    case "go.transform": data = CmdGoTransform(args); break;
                    case "go.destroy": data = CmdGoDestroy(args); break;
                    case "component.add": data = CmdComponentAdd(args); break;
                    case "component.get": data = CmdComponentGet(args); break;
                    case "component.set": data = CmdComponentSet(args); break;
                    case "asset.find": data = CmdAssetFind(args); break;
                    case "asset.refresh": data = CmdAssetRefresh(); break;
                    case "asset.importPackage": data = CmdAssetImportPackage(args); break;
                    case "screenshot": data = CmdScreenshot(args); break;
                    case "screenshot.last": data = CmdScreenshotLast(); break;
                    case "play.enter": data = CmdPlayEnter(); break;
                    case "play.exit": data = CmdPlayExit(); break;
                    case "play.restart": data = CmdPlayRestart(); break;
                    case "play.status": data = CmdPlayStatus(); break;
                    case "menu.execute": data = CmdMenuExecute(args); break;
                    case "script.create": data = CmdScriptCreate(args); break;
                    case "script.validate": data = ScriptValidation.Validate(args); break;
                    case "script.read": data = ScriptValidation.Read(args); break;
                    case "test.run": data = TestRunnerCommands.Run(args); break;
                    case "test.status": data = TestRunnerCommands.Status(); break;
                    case "build.player": data = CmdBuildPlayer(args); break;
                    case "eval":
                        return AgentProtocol.Fail(id, "NOT_READY",
                            "eval is unavailable in v1: Roslyn/dynamic C# compilation is not supported on Unity 2022.3 agent bridge");
                    default:
                        if (PrefabMaterialCommands.TryHandle(cmd, args, out var prefabMaterialData))
                        {
                            data = prefabMaterialData;
                            break;
                        }
                        if (PackageCommands.TryHandle(cmd, args, out var packageData))
                        {
                            data = packageData;
                            break;
                        }
                        if (AnimationCommands.TryHandle(cmd, args, out var animationData))
                        {
                            data = animationData;
                            break;
                        }
                        if (AudioCommands.TryHandle(cmd, args, out var audioData))
                        {
                            data = audioData;
                            break;
                        }
                        if (NavMeshCommands.TryHandle(cmd, args, out var navMeshData))
                        {
                            data = navMeshData;
                            break;
                        }
                        if (UiCommands.TryHandle(cmd, args, out var uiData))
                        {
                            data = uiData;
                            break;
                        }
                        if (AssetCommands.TryHandle(cmd, args, out var assetData))
                        {
                            data = assetData;
                            break;
                        }
                        if (SceneCommands.TryHandle(cmd, args, out var sceneData))
                        {
                            data = sceneData;
                            break;
                        }
                        if (LevelCommands.TryHandle(cmd, args, out var levelData))
                        {
                            data = levelData;
                            break;
                        }
                        if (PerfCommands.TryHandle(cmd, args, out var perfData))
                        {
                            data = perfData;
                            break;
                        }
                        if (SaveCommands.TryHandle(cmd, args, out var saveData))
                        {
                            data = saveData;
                            break;
                        }
                        if (UiKitCommands.TryHandle(cmd, args, out var uiKitData))
                        {
                            data = uiKitData;
                            break;
                        }
                        if (LightingCommands.TryHandle(cmd, args, out var lightingData))
                        {
                            data = lightingData;
                            break;
                        }
                        if (PlayerCommands.TryHandle(cmd, args, out var playerData))
                        {
                            data = playerData;
                            break;
                        }
                        if (EnemyCommands.TryHandle(cmd, args, out var enemyData))
                        {
                            data = enemyData;
                            break;
                        }
                        if (L10nCommands.TryHandle(cmd, args, out var l10nData))
                        {
                            data = l10nData;
                            break;
                        }
                        if (GameplayCommands.TryHandle(cmd, args, out var gameplayData))
                        {
                            data = gameplayData;
                            break;
                        }
                        if (UiFlowCommands.TryHandle(cmd, args, out var uiFlowData))
                        {
                            data = uiFlowData;
                            break;
                        }
                        if (DialogueCommands.TryHandle(cmd, args, out var dialogueData))
                        {
                            data = dialogueData;
                            break;
                        }
                        return AgentProtocol.Fail(id, "UNKNOWN_COMMAND", "unknown cmd: " + cmd);
                }
                return AgentProtocol.Ok(id, data);
            }
            catch (AgentException ae)
            {
                return AgentProtocol.Fail(id, ae.Code, ae.Message, ae.Detail);
            }
            catch (Exception ex)
            {
                return AgentProtocol.Fail(id, "INTERNAL", ex.GetType().Name + ": " + ex.Message);
            }
        }

        static Dictionary<string, object> CmdPing()
        {
            return new Dictionary<string, object>
            {
                { "pong", true },
                { "pid", Process.GetCurrentProcess().Id },
                { "unityVersion", Application.unityVersion },
                { "projectPath", PathUtil.ProjectRoot },
                { "isPlaying", EditorApplication.isPlaying },
                { "isCompiling", EditorApplication.isCompiling },
                { "time", DateTime.UtcNow.ToString("o") },
            };
        }

        static Dictionary<string, object> CmdStatus()
        {
            var scene = SceneManager.GetActiveScene();
            var sel = Selection.gameObjects;
            var selList = new List<object>();
            if (sel != null)
            {
                foreach (var go in sel)
                {
                    if (go == null) continue;
                    selList.Add(new Dictionary<string, object>
                    {
                        { "name", go.name },
                        { "path", GetHierarchyPath(go.transform) },
                        { "instanceId", go.GetInstanceID() },
                    });
                }
            }

            string playMode = "stopped";
            if (EditorApplication.isPlaying) playMode = EditorApplication.isPaused ? "paused" : "playing";
            else if (EditorApplication.isPlayingOrWillChangePlaymode) playMode = "entering";

            return new Dictionary<string, object>
            {
                { "pid", Process.GetCurrentProcess().Id },
                { "projectPath", PathUtil.ProjectRoot },
                { "unityVersion", Application.unityVersion },
                { "playMode", playMode },
                { "fps", EditorApplication.isPlaying ? Mathf.RoundToInt(1f / Mathf.Max(0.0001f, Time.smoothDeltaTime)) : 0 },
                { "isCompiling", EditorApplication.isCompiling },
                { "isUpdating", EditorApplication.isUpdating },
                { "scenePath", scene.path ?? "" },
                { "dirty", scene.isDirty },
                { "activeScene", scene.name ?? "" },
                { "selection", selList },
                { "logCounts", LogBuffer.CountsDict() },
                { "consoleRecent", LogBuffer.Tail(3, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "error", "exception", "assert" }) },
                {
                    "server", new Dictionary<string, object>
                    {
                        { "listening", AgentHost.IsListening },
                        { "port", AgentHost.Port },
                        { "batchMode", Application.isBatchMode },
                    }
                },
            };
        }

        static Dictionary<string, object> CmdConsoleTail(Dictionary<string, object> args)
        {
            var count = MiniJson.GetInt(args, "count", 50);
            HashSet<string> types = null;
            if (args != null && args.TryGetValue("types", out var t) && t is List<object> arr)
            {
                types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var x in arr)
                {
                    var s = MiniJson.AsString(x);
                    if (!string.IsNullOrEmpty(s)) types.Add(s);
                }
            }
            return new Dictionary<string, object> { { "entries", LogBuffer.Tail(count, types) } };
        }

        static Dictionary<string, object> CmdConsoleClear()
        {
            LogBuffer.Clear();
            try
            {
                var logEntries = Type.GetType("UnityEditor.LogEntries,UnityEditor.dll");
                var clear = logEntries?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
                clear?.Invoke(null, null);
            }
            catch { /* optional */ }
            return new Dictionary<string, object> { { "cleared", true } };
        }

        static Dictionary<string, object> CmdCompileWait(Dictionary<string, object> args, int timeoutMs)
        {
            // 非阻塞快照：早期版本在主线程里 Thread.Sleep 死等编译，反而卡住
            // Unity 自己的导入/编译进度，最终被看门狗掐掉。现在只报当前状态，
            // 真正的等待交给调用方轮询（DSH 侧的 unity_compile_wait）。
            var entries = LogBuffer.RecentErrorsAndWarnings();
            var errors = new List<object>();
            var warnings = new List<object>();
            foreach (var e in entries)
            {
                if (e is Dictionary<string, object> d)
                {
                    var ty = MiniJson.GetString(d, "type", "");
                    if (ty == "error") errors.Add(e);
                    else if (ty == "warning") warnings.Add(e);
                }
            }

            return new Dictionary<string, object>
            {
                { "completed", !EditorApplication.isCompiling && !EditorApplication.isUpdating },
                { "failed", EditorUtility.scriptCompilationFailed },
                { "isCompiling", EditorApplication.isCompiling },
                { "isUpdating", EditorApplication.isUpdating },
                { "errors", errors },
                { "warnings", warnings },
                { "durationMs", 0 },
                { "lastCompileFinishedAt", CompileTracker.LastFinishedIso },
                { "lastCompileFinishedMs", CompileTracker.LastFinishedUnixMs },
            };
        }

        static Dictionary<string, object> CmdCompileForce()
        {
            AssetDatabase.Refresh();
            CompilationPipeline.RequestScriptCompilation();
            return new Dictionary<string, object>
            {
                { "requested", true },
                { "isCompiling", EditorApplication.isCompiling },
            };
        }

        static Dictionary<string, object> CmdSceneHierarchy(Dictionary<string, object> args)
        {
            var depth = MiniJson.GetInt(args, "depth", 8);
            var includeInactive = MiniJson.GetBool(args, "includeInactive", true);
            var includeComponents = MiniJson.GetBool(args, "includeComponents", false);
            var rootPath = MiniJson.GetString(args, "root", null);

            // 大场景一次能吐几百 KB：给节点数设上限，超了标记 truncated
            int maxNodes = MiniJson.GetInt(args, "maxNodes", 2000);
            if (maxNodes <= 0) maxNodes = int.MaxValue;

            var budget = new NodeBudget { Left = maxNodes };

            var scene = SceneManager.GetActiveScene();
            var roots = scene.GetRootGameObjects();
            var children = new List<object>();
            foreach (var go in roots)
            {
                if (!includeInactive && !go.activeInHierarchy) continue;
                if (!string.IsNullOrEmpty(rootPath))
                {
                    var p = GetHierarchyPath(go.transform);
                    if (!string.Equals(p, rootPath, StringComparison.Ordinal) &&
                        !p.StartsWith(rootPath + "/", StringComparison.Ordinal))
                        continue;
                    if (string.Equals(p, rootPath, StringComparison.Ordinal))
                    {
                        return WithBudget(BuildNode(go.transform, depth, includeInactive, includeComponents, 0, budget), budget);
                    }
                }
                children.Add(BuildNode(go.transform, depth, includeInactive, includeComponents, 0, budget));
            }

            if (!string.IsNullOrEmpty(rootPath))
            {
                var t = FindTransformByPath(rootPath);
                if (t == null) throw new AgentException("NOT_FOUND", "root not found: " + rootPath);
                return WithBudget(BuildNode(t, depth, includeInactive, includeComponents, 0, budget), budget);
            }

            var result = new Dictionary<string, object>
            {
                { "name", scene.name },
                { "path", scene.path ?? "" },
                { "active", true },
                { "children", children },
            };

            return WithBudget(result, budget);
        }

        sealed class NodeBudget
        {
            public int Left;
            public int Count;
            public bool Truncated;
        }

        static Dictionary<string, object> WithBudget(Dictionary<string, object> node, NodeBudget budget)
        {
            node["nodeCount"] = budget.Count;
            if (budget.Truncated)
                node["truncated"] = true;
            return node;
        }

        static Dictionary<string, object> BuildNode(Transform t, int maxDepth, bool includeInactive,
            bool includeComponents, int level, NodeBudget budget)
        {
            var go = t.gameObject;
            budget.Count++;

            var node = new Dictionary<string, object>
            {
                { "name", go.name },
                { "path", GetHierarchyPath(t) },
                { "active", go.activeSelf },
            };

            if (budget.Left-- <= 0)
            {
                budget.Truncated = true;
                node["cut"] = true;
                return node;
            }

            if (includeComponents)
            {
                var comps = new List<object>();
                foreach (var c in go.GetComponents<Component>())
                {
                    if (c == null) continue;
                    comps.Add(c.GetType().FullName);
                }
                node["components"] = comps;
            }
            if (level < maxDepth)
            {
                var kids = new List<object>();
                for (var i = 0; i < t.childCount; i++)
                {
                    var child = t.GetChild(i);
                    if (!includeInactive && !child.gameObject.activeSelf) continue;
                    if (budget.Truncated) break;
                    kids.Add(BuildNode(child, maxDepth, includeInactive, includeComponents, level + 1, budget));
                }
                if (kids.Count > 0) node["children"] = kids;
            }
            return node;
        }

        static Dictionary<string, object> CmdSceneOpen(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            if (string.IsNullOrEmpty(path)) throw new AgentException("BAD_ARGS", "path required");
            var assetPath = PathUtil.ToAssetPath(path);
            if (!assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", "scene path must end with .unity");
            if (!File.Exists(PathUtil.ResolveProjectRelative(assetPath, false)))
                throw new AgentException("NOT_FOUND", "scene not found: " + assetPath);
            var ok = EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Single).IsValid();
            return new Dictionary<string, object> { { "opened", ok }, { "path", assetPath } };
        }

        static Dictionary<string, object> CmdSceneSave(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            var scene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(path))
            {
                var assetPath = PathUtil.ToAssetPath(path);
                EditorSceneManager.SaveScene(scene, assetPath);
                return new Dictionary<string, object> { { "saved", true }, { "path", assetPath } };
            }
            if (string.IsNullOrEmpty(scene.path))
                throw new AgentException("BAD_ARGS", "untitled scene requires path");
            EditorSceneManager.SaveScene(scene);
            return new Dictionary<string, object> { { "saved", true }, { "path", scene.path } };
        }

        static Dictionary<string, object> CmdSceneSaveAll()
        {
            EditorSceneManager.SaveOpenScenes();
            return new Dictionary<string, object> { { "saved", true } };
        }

        static Dictionary<string, object> CmdGoCreate(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", "GameObject");
            var parentPath = MiniJson.GetString(args, "parent", null);
            var primitive = MiniJson.GetString(args, "primitive", null);

            GameObject go;
            if (!string.IsNullOrEmpty(primitive) &&
                Enum.TryParse(primitive, true, out PrimitiveType pt))
            {
                go = GameObject.CreatePrimitive(pt);
                go.name = name;
            }
            else
            {
                go = new GameObject(name);
            }

            if (!string.IsNullOrEmpty(parentPath))
            {
                var parent = FindTransformByPath(parentPath);
                if (parent == null) throw new AgentException("NOT_FOUND", "parent not found: " + parentPath);
                go.transform.SetParent(parent, false);
            }

            if (args != null && args.TryGetValue("position", out var pos))
                go.transform.localPosition = ReadVec3(pos, Vector3.zero);
            if (args != null && args.TryGetValue("rotation", out var rot))
                go.transform.localEulerAngles = ReadVec3(rot, Vector3.zero);
            if (args != null && args.TryGetValue("scale", out var scl))
                go.transform.localScale = ReadVec3(scl, Vector3.one);

            Undo.RegisterCreatedObjectUndo(go, "UnityAgent.go.create");
            return new Dictionary<string, object>
            {
                { "path", GetHierarchyPath(go.transform) },
                { "name", go.name },
                { "instanceId", go.GetInstanceID() },
            };
        }

        static Dictionary<string, object> CmdGoFind(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", null);
            var path = MiniJson.GetString(args, "path", null);
            var component = MiniJson.GetString(args, "component", null);
            var tag = MiniJson.GetString(args, "tag", null);
            var limit = MiniJson.GetInt(args, "limit", 50);
            if (limit <= 0) limit = 50;

            var results = new List<object>();
            if (!string.IsNullOrEmpty(path))
            {
                var t = FindTransformByPath(path);
                if (t != null) results.Add(DescribeGo(t.gameObject));
                return GoFindResult(results);
            }

            Type compType = null;
            if (!string.IsNullOrEmpty(component))
                compType = ResolveType(component);

            var transforms = Object.FindObjectsOfType<Transform>(true);
            foreach (var tr in transforms)
            {
                if (results.Count >= limit) break;
                var go = tr.gameObject;
                if (!string.IsNullOrEmpty(name) && !string.Equals(go.name, name, StringComparison.Ordinal))
                    continue;
                if (!string.IsNullOrEmpty(tag))
                {
                    try { if (!go.CompareTag(tag)) continue; }
                    catch { continue; }
                }
                if (compType != null && go.GetComponent(compType) == null) continue;
                // 只返回本节点（避免每个子节点重复）——仍扫所有 Transform
                results.Add(DescribeGo(go));
            }

            return GoFindResult(results);
        }

        // 返回值必须是对象：裸数组会让上层"value must be an object"
        static Dictionary<string, object> GoFindResult(List<object> results)
        {
            return new Dictionary<string, object>
            {
                { "count", results.Count },
                { "results", results },
            };
        }

        static Dictionary<string, object> DescribeGo(GameObject go)
        {
            var comps = new List<object>();
            foreach (var c in go.GetComponents<Component>())
            {
                if (c == null) continue;
                comps.Add(c.GetType().FullName);
            }
            return new Dictionary<string, object>
            {
                { "path", GetAddressPath(go.transform) },
                { "name", go.name },
                { "instanceId", go.GetInstanceID() },
                { "siblingIndex", go.transform.GetSiblingIndex() },
                { "components", comps },
            };
        }

        // 同名兄弟节点用 Name#n 标出来，返回的路径可以直接再传回去定位
        public static string GetAddressPath(Transform t)
        {
            if (t == null) return "";

            var parts = new List<string>();

            while (t != null)
            {
                string name = t.name;
                var parent = t.parent;

                if (parent != null)
                {
                    int ordinal = 0;
                    int seen = 0;
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        if (!string.Equals(parent.GetChild(i).name, name, StringComparison.Ordinal))
                            continue;
                        if (parent.GetChild(i) == t) ordinal = seen;
                        seen++;
                    }

                    if (seen > 1)
                        name = name + "#" + (ordinal + 1);
                }

                parts.Insert(0, name);
                t = parent;
            }

            return string.Join("/", parts);
        }

        static Dictionary<string, object> CmdGoSet(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            if (string.IsNullOrEmpty(path)) throw new AgentException("BAD_ARGS", "path required");
            var t = FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            var go = t.gameObject;
            if (args.ContainsKey("active")) go.SetActive(MiniJson.GetBool(args, "active", go.activeSelf));
            if (args.ContainsKey("name"))
            {
                var n = MiniJson.GetString(args, "name", go.name);
                if (!string.IsNullOrEmpty(n)) go.name = n;
            }
            if (args.ContainsKey("tag"))
            {
                var tag = MiniJson.GetString(args, "tag", "Untagged");
                go.tag = tag;
            }
            if (args.ContainsKey("layer"))
                go.layer = MiniJson.GetInt(args, "layer", go.layer);
            return new Dictionary<string, object> { { "ok", true } };
        }

        static Dictionary<string, object> CmdGoTransform(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            if (string.IsNullOrEmpty(path)) throw new AgentException("BAD_ARGS", "path required");
            var t = FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            var local = MiniJson.GetBool(args, "local", true);

            if (args.ContainsKey("position"))
            {
                var v = ReadVec3(args["position"], Vector3.zero);
                if (local) t.localPosition = v; else t.position = v;
            }
            if (args.ContainsKey("rotation"))
            {
                var v = ReadVec3(args["rotation"], Vector3.zero);
                if (local) t.localEulerAngles = v; else t.eulerAngles = v;
            }
            if (args.ContainsKey("scale"))
                t.localScale = ReadVec3(args["scale"], Vector3.one);

            var pos = local ? t.localPosition : t.position;
            var rot = local ? t.localEulerAngles : t.eulerAngles;
            return new Dictionary<string, object>
            {
                { "position", Vec3Dict(pos) },
                { "rotation", Vec3Dict(rot) },
                { "scale", Vec3Dict(t.localScale) },
            };
        }

        static Dictionary<string, object> CmdGoDestroy(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            if (string.IsNullOrEmpty(path)) throw new AgentException("BAD_ARGS", "path required");
            var t = FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            Undo.DestroyObjectImmediate(t.gameObject);
            return new Dictionary<string, object> { { "destroyed", true } };
        }

        static Dictionary<string, object> CmdComponentAdd(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            var typeName = MiniJson.GetString(args, "type", null);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(typeName))
                throw new AgentException("BAD_ARGS", "path and type required");
            var t = FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            var type = ResolveType(typeName);
            if (type == null || !typeof(Component).IsAssignableFrom(type))
                throw new AgentException("BAD_ARGS", "unknown component type: " + typeName);
            var c = Undo.AddComponent(t.gameObject, type);
            return new Dictionary<string, object>
            {
                { "path", GetHierarchyPath(t) },
                { "type", type.FullName },
                { "instanceId", c.GetInstanceID() },
            };
        }

        static Dictionary<string, object> CmdComponentGet(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            var component = MiniJson.GetString(args, "component", null);
            var property = MiniJson.GetString(args, "property", null);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(component) || string.IsNullOrEmpty(property))
                throw new AgentException("BAD_ARGS", "path, component, property required");
            var t = FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            var type = ResolveType(component);
            if (type == null) throw new AgentException("BAD_ARGS", "unknown component: " + component);
            var c = t.gameObject.GetComponent(type);
            if (c == null) throw new AgentException("NOT_FOUND", "component missing: " + component);

            var so = new SerializedObject(c);
            var prop = so.FindProperty(property);
            if (prop != null)
                return new Dictionary<string, object> { { "value", ReadSerialized(prop) } };

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var pi = type.GetProperty(property, flags);
            if (pi != null && pi.CanRead)
                return new Dictionary<string, object> { { "value", BoxValue(pi.GetValue(c, null)) } };
            var fi = type.GetField(property, flags);
            if (fi != null)
                return new Dictionary<string, object> { { "value", BoxValue(fi.GetValue(c)) } };

            throw new AgentException("NOT_FOUND", "property not found: " + property);
        }

        static Dictionary<string, object> CmdComponentSet(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            var component = MiniJson.GetString(args, "component", null);
            var property = MiniJson.GetString(args, "property", null);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(component) || string.IsNullOrEmpty(property))
                throw new AgentException("BAD_ARGS", "path, component, property required");
            if (args == null || !args.ContainsKey("value"))
                throw new AgentException("BAD_ARGS", "value required");
            var value = args["value"];

            var t = FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            var type = ResolveType(component);
            if (type == null) throw new AgentException("BAD_ARGS", "unknown component: " + component);
            var c = t.gameObject.GetComponent(type);
            if (c == null) throw new AgentException("NOT_FOUND", "component missing: " + component);

            var so = new SerializedObject(c);
            var prop = so.FindProperty(property);
            if (prop != null)
            {
                WriteSerialized(prop, value);
                so.ApplyModifiedProperties();
                return new Dictionary<string, object> { { "value", ReadSerialized(prop) } };
            }

            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var pi = type.GetProperty(property, flags);
            if (pi != null && pi.CanWrite)
            {
                var converted = ConvertTo(value, pi.PropertyType);
                pi.SetValue(c, converted, null);
                return new Dictionary<string, object> { { "value", BoxValue(pi.GetValue(c, null)) } };
            }
            var fi = type.GetField(property, flags);
            if (fi != null)
            {
                var converted = ConvertTo(value, fi.FieldType);
                fi.SetValue(c, converted);
                return new Dictionary<string, object> { { "value", BoxValue(fi.GetValue(c)) } };
            }
            throw new AgentException("NOT_FOUND", "property not found: " + property);
        }

        static List<object> CmdAssetFind(Dictionary<string, object> args)
        {
            var filter = MiniJson.GetString(args, "filter", "");
            var type = MiniJson.GetString(args, "type", null);
            var folder = MiniJson.GetString(args, "folder", "Assets");
            var limit = MiniJson.GetInt(args, "limit", 50);
            if (limit <= 0) limit = 50;

            folder = folder.Replace('\\', '/');
            if (!folder.StartsWith("Assets", StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", "folder must be under Assets/");
            PathUtil.ResolveProjectRelative(folder, false);

            var search = filter ?? "";
            if (!string.IsNullOrEmpty(type)) search = (search + " t:" + type).Trim();

            var guids = AssetDatabase.FindAssets(search, new[] { folder });
            var list = new List<object>();
            foreach (var guid in guids)
            {
                if (list.Count >= limit) break;
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var name = Path.GetFileNameWithoutExtension(path);
                var main = AssetDatabase.LoadMainAssetAtPath(path);
                var ty = main != null ? main.GetType().Name : "";
                list.Add(new Dictionary<string, object>
                {
                    { "path", path },
                    { "guid", guid },
                    { "name", name },
                    { "type", ty },
                });
            }
            return list;
        }

        static Dictionary<string, object> CmdAssetRefresh()
        {
            AssetDatabase.Refresh();
            return new Dictionary<string, object> { { "refreshed", true } };
        }

        static Dictionary<string, object> CmdAssetImportPackage(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            if (string.IsNullOrEmpty(path)) throw new AgentException("BAD_ARGS", "path required");
            var full = PathUtil.ResolveProjectRelative(path, true);
            if (!File.Exists(full)) throw new AgentException("NOT_FOUND", "package not found: " + path);
            var rel = PathUtil.ToProjectRelative(full);
            AssetDatabase.ImportPackage(full, false);
            return new Dictionary<string, object> { { "imported", true }, { "path", rel } };
        }

        static Dictionary<string, object> CmdScreenshot(Dictionary<string, object> args)
        {
            var gui = MiniJson.GetBool(args, "gui", false);

            if (gui)
            {
                if (!Application.isPlaying)
                    throw new AgentException("NOT_READY", "gui screenshot requires play mode");

                PathUtil.EnsureDir(PathUtil.ShotsDir);
                var guiName = "gui-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png";
                var guiRel = MiniJson.GetString(args, "path", "Library/UnityAgent/shots/" + guiName);
                var guiFull = PathUtil.ResolveProjectRelative(guiRel, true);
                PathUtil.EnsureDir(Path.GetDirectoryName(guiFull));

                // 路径由调用方指定时先删掉旧文件，避免轮询读到上一张的字节
                try { if (File.Exists(guiFull)) File.Delete(guiFull); } catch { }

                _lastGuiPath = guiFull;
                _lastGuiReady = false;

                // 相机 RT 拍不到 OnGUI；ScreenCapture 在帧末写文件，所以这里只排队
                ScreenCapture.CaptureScreenshot(guiFull);

                EditorApplication.update -= GuiShotTick;
                EditorApplication.update += GuiShotTick;

                return new Dictionary<string, object>
                {
                    { "queued", true },
                    { "path", PathUtil.ToProjectRelative(guiFull) },
                    { "note", "capture is written at end of frame; poll screenshot.last" },
                };
            }

            var view = MiniJson.GetString(args, "view", "scene");
            var width = MiniJson.GetInt(args, "width", 1280);
            var height = MiniJson.GetInt(args, "height", 720);
            if (width <= 0) width = 1280;
            if (height <= 0) height = 720;

            PathUtil.EnsureDir(PathUtil.ShotsDir);
            var fileName = "shot-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + ".png";
            var outRel = MiniJson.GetString(args, "path", "Library/UnityAgent/shots/" + fileName);
            var full = PathUtil.ResolveProjectRelative(outRel, true);
            PathUtil.EnsureDir(Path.GetDirectoryName(full));

            Texture2D tex = null;
            try
            {
                if (string.Equals(view, "game", StringComparison.OrdinalIgnoreCase))
                {
                    if (Application.isPlaying)
                    {
                        tex = ScreenCapture.CaptureScreenshotAsTexture();
                    }
                    else
                    {
                        var cam = Camera.main ?? Object.FindObjectOfType<Camera>();
                        if (cam == null)
                            throw new AgentException("NOT_READY", "no camera for game screenshot in edit mode");
                        tex = RenderCamera(cam, width, height);
                    }
                }
                else
                {
                    var sv = SceneView.lastActiveSceneView;
                    if (sv == null || sv.camera == null)
                        throw new AgentException("NOT_READY", "SceneView unavailable (batchmode or no scene view)");
                    tex = RenderCamera(sv.camera, width, height);
                }

                if (tex == null) throw new AgentException("INTERNAL", "screenshot texture null");
                var png = tex.EncodeToPNG();
                File.WriteAllBytes(full, png);
                return new Dictionary<string, object>
                {
                    { "path", PathUtil.ToProjectRelative(full) },
                    { "width", tex.width },
                    { "height", tex.height },
                };
            }
            finally
            {
                if (tex != null) Object.DestroyImmediate(tex);
            }
        }

        static string _lastGuiPath;
        static bool _lastGuiReady;

        static void GuiShotTick()
        {
            if (string.IsNullOrEmpty(_lastGuiPath))
            {
                EditorApplication.update -= GuiShotTick;
                return;
            }

            if (!File.Exists(_lastGuiPath))
                return;

            if (new FileInfo(_lastGuiPath).Length <= 0)
                return;

            _lastGuiReady = true;
            EditorApplication.update -= GuiShotTick;
        }

        static Dictionary<string, object> CmdScreenshotLast()
        {
            return new Dictionary<string, object>
            {
                { "ready", _lastGuiReady },
                { "path", string.IsNullOrEmpty(_lastGuiPath) ? "" : PathUtil.ToProjectRelative(_lastGuiPath) },
            };
        }

        static Texture2D RenderCamera(Camera cam, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width, height, 24);
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            var prevActive = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        static Dictionary<string, object> CmdPlayEnter()
        {
            if (!EditorApplication.isPlaying)
                EditorApplication.isPlaying = true;
            return CmdPlayStatus();
        }

        static Dictionary<string, object> CmdPlayExit()
        {
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
            return CmdPlayStatus();
        }

        // 退出 → 等编译/刷新 → 重新进 Play。改了脚本又想让新代码生效时用，
        // 避免"进过 Play 的会话里还跑着旧程序集"。
        static bool _restartPending;

        static Dictionary<string, object> CmdPlayRestart()
        {
            if (_restartPending)
                return CmdPlayStatus();

            if (!EditorApplication.isPlaying)
            {
                AssetDatabase.Refresh();
                EditorApplication.isPlaying = true;
                return CmdPlayStatus();
            }

            _restartPending = true;
            EditorApplication.isPlaying = false;
            EditorApplication.update += RestartTick;
            return CmdPlayStatus();
        }

        static void RestartTick()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            _restartPending = false;
            EditorApplication.update -= RestartTick;

            AssetDatabase.Refresh();
            EditorApplication.isPlaying = true;
        }

        static Dictionary<string, object> CmdPlayStatus()
        {
            return new Dictionary<string, object>
            {
                { "isPlaying", EditorApplication.isPlaying },
                { "isPaused", EditorApplication.isPaused },
            };
        }

        static Dictionary<string, object> CmdMenuExecute(Dictionary<string, object> args)
        {
            var menuPath = MiniJson.GetString(args, "menuPath", null);
            if (string.IsNullOrEmpty(menuPath)) throw new AgentException("BAD_ARGS", "menuPath required");
            var ok = EditorApplication.ExecuteMenuItem(menuPath);
            if (!ok) throw new AgentException("NOT_FOUND", "menu item failed or missing: " + menuPath);
            return new Dictionary<string, object> { { "executed", true } };
        }

        static Dictionary<string, object> CmdScriptCreate(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            var content = MiniJson.GetString(args, "content", null);
            var refresh = MiniJson.GetBool(args, "refresh", true);
            if (string.IsNullOrEmpty(path)) throw new AgentException("BAD_ARGS", "path required");
            if (content == null) content = "";
            var assetPath = PathUtil.ToAssetPath(path);
            if (!assetPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", "script path must end with .cs");
            var full = PathUtil.ResolveProjectRelative(assetPath, false);
            PathUtil.EnsureDir(Path.GetDirectoryName(full));
            File.WriteAllText(full, content, new UTF8Encoding(false));
            if (refresh) AssetDatabase.Refresh();
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            return new Dictionary<string, object> { { "path", assetPath }, { "guid", guid ?? "" } };
        }

        static bool StartsWithAny(string path, params string[] prefixes)
        {
            for (int i = 0; i < prefixes.Length; i++)
                if (path.StartsWith(prefixes[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static Dictionary<string, object> CmdBuildPlayer(Dictionary<string, object> args)
        {
            var outputPath = MiniJson.GetString(args, "outputPath", null);
            if (string.IsNullOrEmpty(outputPath))
                throw new AgentException("BAD_ARGS", "outputPath required");

            // 允许写到项目内 Library/UnityAgent 或 Assets 外？DESIGN 说文件写入限制在 Assets/ 与 Library/UnityAgent/
            // 构建产物通常在项目外；为安全，要求相对路径且落在 Library/UnityAgent/builds/
            outputPath = outputPath.Replace('\\', '/');

            // 兼容历史默认值：Library/UnityAgent/builds 在 Unity 里非法（内部工作目录），
            // 直接重定向到 Builds/UnityAgent —— 这样老插件配置、老脚本都不用改也能打包。
            const string legacy = "Library/UnityAgent/builds";
            if (outputPath.StartsWith(legacy, StringComparison.OrdinalIgnoreCase))
                outputPath = "Builds/UnityAgent" + outputPath.Substring(legacy.Length);

            if (Path.IsPathRooted(outputPath))
                throw new AgentException("BAD_ARGS", "outputPath must be project-relative");
            // Unity 不允许把包打在 Library/ 里：会抛
            // "Invalid build path ... The 'Library' directory is an internal work directory"。
            // 原来是强制 outputPath 必须在 Library/UnityAgent/ 下 —— 那条限制本身就跑不通，
            // 所以改成：允许工程内任意相对路径，但排除 Unity 的保留目录。
            if (StartsWithAny(outputPath, "Library/", "Assets/", "Packages/", "ProjectSettings/"))
                throw new AgentException("BAD_ARGS", "outputPath 不能在 Library/Assets/Packages/ProjectSettings 下，请用 Builds/ 之类的目录");

            var fullOut = PathUtil.ResolveProjectRelative(outputPath, true);
            PathUtil.EnsureDir(Path.GetDirectoryName(fullOut));

            var targetName = MiniJson.GetString(args, "target", "StandaloneWindows64");
            if (!Enum.TryParse(targetName, true, out BuildTarget target))
                throw new AgentException("BAD_ARGS", "unknown BuildTarget: " + targetName);

            string[] scenes;
            if (args != null && args.TryGetValue("scenes", out var sc) && sc is List<object> list && list.Count > 0)
            {
                var tmp = new List<string>();
                foreach (var x in list)
                {
                    var p = PathUtil.ToAssetPath(MiniJson.AsString(x));
                    tmp.Add(p);
                }
                scenes = tmp.ToArray();
            }
            else
            {
                var enabled = new List<string>();
                foreach (var s in EditorBuildSettings.scenes)
                    if (s.enabled) enabled.Add(s.path);
                if (enabled.Count == 0)
                {
                    var active = SceneManager.GetActiveScene().path;
                    if (!string.IsNullOrEmpty(active)) enabled.Add(active);
                }
                scenes = enabled.ToArray();
            }

            if (scenes.Length == 0)
                throw new AgentException("BAD_ARGS", "no scenes to build");

            var opts = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = fullOut,
                target = target,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var result = report.summary.result.ToString();
            var totalBytes = (long)report.summary.totalSize;
            var errors = new List<object>();
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        errors.Add(msg.content);
                }
            }
            return new Dictionary<string, object>
            {
                { "result", result },
                { "totalBytes", totalBytes },
                { "errors", errors },
            };
        }

        // ---- helpers ----

        public static string GetHierarchyPath(Transform t)
        {
            var stack = new Stack<string>();
            while (t != null)
            {
                stack.Push(t.name);
                t = t.parent;
            }
            return string.Join("/", stack.ToArray());
        }

        public static Transform FindTransformByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            path = path.Replace('\\', '/').Trim('/');

            // 支持 Name#2 这种"第 2 个同名子节点"的写法：
            // 场景里同名兄弟很常见（Cell_3_9/Ceiling），只按名字找永远只能拿到第一个。
            var segments = path.Split('/');

            Transform current = null;
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (string.Equals(root.name, segments[0], StringComparison.Ordinal))
                {
                    current = root.transform;
                    break;
                }
            }

            if (current != null)
            {
                for (int i = 1; i < segments.Length; i++)
                {
                    current = FindChild(current, segments[i]);
                    if (current == null) break;
                }

                if (current != null) return current;
            }

            // fallback: deep search by full path（不支持 #n，但路径唯一时仍可用）
            foreach (var tr in Object.FindObjectsOfType<Transform>(true))
            {
                if (string.Equals(GetHierarchyPath(tr), path, StringComparison.Ordinal))
                    return tr;
            }
            return null;
        }

        static Transform FindChild(Transform parent, string segment)
        {
            if (parent == null || string.IsNullOrEmpty(segment))
                return null;

            int hash = segment.LastIndexOf('#');
            if (hash > 0)
            {
                int ordinal;
                if (int.TryParse(segment.Substring(hash + 1), out ordinal))
                {
                    string name = segment.Substring(0, hash);
                    int seen = 0;
                    for (int i = 0; i < parent.childCount; i++)
                    {
                        var child = parent.GetChild(i);
                        if (!string.Equals(child.name, name, StringComparison.Ordinal))
                            continue;
                        if (seen++ == Math.Max(0, ordinal - 1))
                            return child;
                    }
                    return null;
                }
            }

            return parent.Find(segment);
        }

        public static Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var t = Type.GetType(name);
            if (t != null) return t;
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(name);
                    if (t != null) return t;
                    foreach (var candidate in asm.GetTypes())
                    {
                        if (candidate.Name == name || candidate.FullName == name)
                            return candidate;
                    }
                }
                catch { /* dynamic assemblies */ }
            }
            return null;
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
                return new Vector3(
                    Convert.ToSingle(a[0]),
                    Convert.ToSingle(a[1]),
                    Convert.ToSingle(a[2]));
            }
            return fallback;
        }

        static Dictionary<string, object> Vec3Dict(Vector3 v)
        {
            return new Dictionary<string, object> { { "x", v.x }, { "y", v.y }, { "z", v.z } };
        }

        static object BoxValue(object v)
        {
            if (v == null) return null;
            switch (v)
            {
                case string _:
                case bool _:
                case byte _:
                case sbyte _:
                case short _:
                case ushort _:
                case int _:
                case uint _:
                case long _:
                case ulong _:
                case float _:
                case double _:
                    return v;
                case Enum e:
                    return e.ToString();
                case Vector2 v2:
                    return new Dictionary<string, object> { { "x", v2.x }, { "y", v2.y } };
                case Vector3 v3:
                    return Vec3Dict(v3);
                case Vector4 v4:
                    return new Dictionary<string, object> { { "x", v4.x }, { "y", v4.y }, { "z", v4.z }, { "w", v4.w } };
                case Quaternion q:
                    return new Dictionary<string, object> { { "x", q.x }, { "y", q.y }, { "z", q.z }, { "w", q.w } };
                case Color c:
                    return new Dictionary<string, object> { { "r", c.r }, { "g", c.g }, { "b", c.b }, { "a", c.a } };
                case Color32 c32:
                    return new Dictionary<string, object> { { "r", c32.r }, { "g", c32.g }, { "b", c32.b }, { "a", c32.a } };
                case Rect r:
                    return new Dictionary<string, object> { { "x", r.x }, { "y", r.y }, { "width", r.width }, { "height", r.height } };
                default:
                    return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        static object ConvertTo(object value, Type target)
        {
            if (value == null) return null;
            target = Nullable.GetUnderlyingType(target) ?? target;
            if (target.IsInstanceOfType(value)) return value;
            if (target == typeof(string)) return MiniJson.AsString(value);
            if (target == typeof(bool)) return MiniJson.GetBool(new Dictionary<string, object> { { "v", value } }, "v");
            if (target.IsEnum)
            {
                if (value is string es) return Enum.Parse(target, es, true);
                if (value is double ed) return Enum.ToObject(target, (int)ed);
            }
            if (target == typeof(int) || target == typeof(long) || target == typeof(float) || target == typeof(double))
                return Convert.ChangeType(value is double ? value : Convert.ToDouble(value), target, System.Globalization.CultureInfo.InvariantCulture);
            if (target == typeof(Vector2) && value is Dictionary<string, object> d2)
                return new Vector2((float)MiniJson.GetDouble(d2, "x"), (float)MiniJson.GetDouble(d2, "y"));
            if (target == typeof(Vector3))
                return ReadVec3(value, Vector3.zero);
            if (target == typeof(Vector4) && value is Dictionary<string, object> d4)
                return new Vector4((float)MiniJson.GetDouble(d4, "x"), (float)MiniJson.GetDouble(d4, "y"),
                    (float)MiniJson.GetDouble(d4, "z"), (float)MiniJson.GetDouble(d4, "w"));
            if (target == typeof(Quaternion) && value is Dictionary<string, object> dq)
                return new Quaternion((float)MiniJson.GetDouble(dq, "x"), (float)MiniJson.GetDouble(dq, "y"),
                    (float)MiniJson.GetDouble(dq, "z"), (float)MiniJson.GetDouble(dq, "w"));
            if (target == typeof(Color) && value is Dictionary<string, object> dc)
                return new Color((float)MiniJson.GetDouble(dc, "r"), (float)MiniJson.GetDouble(dc, "g"),
                    (float)MiniJson.GetDouble(dc, "b"), (float)MiniJson.GetDouble(dc, "a", 1));
            if (target == typeof(Color32) && value is Dictionary<string, object> d32)
                return new Color32((byte)MiniJson.GetInt(d32, "r"), (byte)MiniJson.GetInt(d32, "g"),
                    (byte)MiniJson.GetInt(d32, "b"), (byte)MiniJson.GetInt(d32, "a", 255));
            if (target == typeof(Rect) && value is Dictionary<string, object> dr)
                return new Rect((float)MiniJson.GetDouble(dr, "x"), (float)MiniJson.GetDouble(dr, "y"),
                    (float)MiniJson.GetDouble(dr, "width"), (float)MiniJson.GetDouble(dr, "height"));
            return Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }

        static object ReadSerialized(SerializedProperty prop)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer: return prop.intValue;
                case SerializedPropertyType.Boolean: return prop.boolValue;
                case SerializedPropertyType.Float: return prop.floatValue;
                case SerializedPropertyType.String: return prop.stringValue;
                case SerializedPropertyType.Enum: return prop.enumDisplayNames != null && prop.enumValueIndex >= 0 && prop.enumValueIndex < prop.enumDisplayNames.Length
                    ? prop.enumDisplayNames[prop.enumValueIndex] : prop.enumValueIndex;
                case SerializedPropertyType.Vector2: return new Dictionary<string, object> { { "x", prop.vector2Value.x }, { "y", prop.vector2Value.y } };
                case SerializedPropertyType.Vector3: return Vec3Dict(prop.vector3Value);
                case SerializedPropertyType.Vector4: return new Dictionary<string, object> { { "x", prop.vector4Value.x }, { "y", prop.vector4Value.y }, { "z", prop.vector4Value.z }, { "w", prop.vector4Value.w } };
                case SerializedPropertyType.Color: return new Dictionary<string, object> { { "r", prop.colorValue.r }, { "g", prop.colorValue.g }, { "b", prop.colorValue.b }, { "a", prop.colorValue.a } };
                case SerializedPropertyType.Quaternion: return new Dictionary<string, object> { { "x", prop.quaternionValue.x }, { "y", prop.quaternionValue.y }, { "z", prop.quaternionValue.z }, { "w", prop.quaternionValue.w } };
                case SerializedPropertyType.Rect: return new Dictionary<string, object> { { "x", prop.rectValue.x }, { "y", prop.rectValue.y }, { "width", prop.rectValue.width }, { "height", prop.rectValue.height } };
                case SerializedPropertyType.ObjectReference:
                    return prop.objectReferenceValue != null ? prop.objectReferenceValue.name : null;
                default:
                    return prop.propertyType.ToString();
            }
        }

        static void WriteSerialized(SerializedProperty prop, object value)
        {
            switch (prop.propertyType)
            {
                case SerializedPropertyType.Integer:
                    prop.intValue = value is double d ? (int)d : Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Boolean:
                    prop.boolValue = value is bool b ? b : Convert.ToBoolean(value);
                    break;
                case SerializedPropertyType.Float:
                    prop.floatValue = value is double df ? (float)df : Convert.ToSingle(value);
                    break;
                case SerializedPropertyType.String:
                    prop.stringValue = MiniJson.AsString(value, "");
                    break;
                case SerializedPropertyType.Enum:
                    if (value is string es)
                    {
                        var idx = Array.IndexOf(prop.enumNames, es);
                        if (idx < 0) idx = Array.FindIndex(prop.enumNames, n => string.Equals(n, es, StringComparison.OrdinalIgnoreCase));
                        if (idx >= 0) prop.enumValueIndex = idx;
                        else throw new AgentException("BAD_ARGS", "unknown enum " + es);
                    }
                    else prop.enumValueIndex = Convert.ToInt32(value);
                    break;
                case SerializedPropertyType.Vector2:
                    if (value is Dictionary<string, object> d2)
                        prop.vector2Value = new Vector2((float)MiniJson.GetDouble(d2, "x"), (float)MiniJson.GetDouble(d2, "y"));
                    break;
                case SerializedPropertyType.Vector3:
                    prop.vector3Value = ReadVec3(value, Vector3.zero);
                    break;
                case SerializedPropertyType.Vector4:
                    if (value is Dictionary<string, object> d4)
                        prop.vector4Value = new Vector4((float)MiniJson.GetDouble(d4, "x"), (float)MiniJson.GetDouble(d4, "y"),
                            (float)MiniJson.GetDouble(d4, "z"), (float)MiniJson.GetDouble(d4, "w"));
                    break;
                case SerializedPropertyType.Color:
                    if (value is Dictionary<string, object> dc)
                        prop.colorValue = new Color((float)MiniJson.GetDouble(dc, "r"), (float)MiniJson.GetDouble(dc, "g"),
                            (float)MiniJson.GetDouble(dc, "b"), (float)MiniJson.GetDouble(dc, "a", 1));
                    break;
                case SerializedPropertyType.Quaternion:
                    if (value is Dictionary<string, object> dq)
                        prop.quaternionValue = new Quaternion((float)MiniJson.GetDouble(dq, "x"), (float)MiniJson.GetDouble(dq, "y"),
                            (float)MiniJson.GetDouble(dq, "z"), (float)MiniJson.GetDouble(dq, "w"));
                    break;
                case SerializedPropertyType.Rect:
                    if (value is Dictionary<string, object> dr)
                        prop.rectValue = new Rect((float)MiniJson.GetDouble(dr, "x"), (float)MiniJson.GetDouble(dr, "y"),
                            (float)MiniJson.GetDouble(dr, "width"), (float)MiniJson.GetDouble(dr, "height"));
                    break;
                default:
                    throw new AgentException("BAD_ARGS", "unsupported serialized type " + prop.propertyType);
            }
        }
    }
}
