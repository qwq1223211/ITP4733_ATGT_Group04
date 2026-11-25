using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace Local.UnityAgent
{
    /// <summary>性能快照、画质、只读场景审计。不改层级、不保存场景。</summary>
    public static class PerfCommands
    {
        const int DEFAULT_LIGHTS = 4;
        const int DEFAULT_TRIS = 500000;
        const int DEFAULT_OBJECTS = 5000;

        [MenuItem("Tools/Unity Agent/Perf Snapshot")]
        public static void SnapshotMenu()
        {
            try
            {
                var data = Snapshot();
                var mem = MiniJson.AsObject(data["memoryMB"]);
                var counts = MiniJson.AsObject(data["counts"]);
                UnityEngine.Debug.Log("[UnityAgent] Perf Snapshot"
                    + " objects=" + ReadInt(counts, "objects", 0)
                    + " tris=" + ReadInt(counts, "triangles", 0)
                    + " reservedMB=" + ReadDouble(mem, "totalReserved", 0)
                    + " playing=" + MiniJson.GetBool(data, "isPlaying", false));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Perf Snapshot failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "perf.snapshot")
            {
                data = Snapshot();
                return true;
            }
            if (cmd == "perf.quality")
            {
                data = Quality(args);
                return true;
            }
            if (cmd == "perf.audit")
            {
                data = Audit(args);
                return true;
            }
            return false;
        }

        public static Dictionary<string, object> Snapshot()
        {
            var playing = Application.isPlaying;
            var fps = 0.0;
            if (playing && Time.smoothDeltaTime > 0.0001f)
                fps = Math.Round(1.0 / Time.smoothDeltaTime, 1);

            var filters = UnityEngine.Object.FindObjectsOfType<MeshFilter>(true);
            var triangles = 0;
            for (var i = 0; i < filters.Length; i++)
                triangles += Tris(filters[i] != null ? filters[i].sharedMesh : null);

            return new Dictionary<string, object>
            {
                { "isPlaying", playing },
                { "fps", fps },
                { "time", Math.Round(Time.realtimeSinceStartup, 3) },
                { "memoryMB", new Dictionary<string, object>
                    {
                        { "totalAllocated", Mb(Profiler.GetTotalAllocatedMemoryLong()) },
                        { "totalReserved", Mb(Profiler.GetTotalReservedMemoryLong()) },
                        { "monoHeap", Mb(Profiler.GetMonoHeapSizeLong()) },
                        { "monoUsed", Mb(Profiler.GetMonoUsedSizeLong()) },
                    }
                },
                { "textureMemoryMB", Mb((long)Texture.currentTextureMemory) },
                { "counts", new Dictionary<string, object>
                    {
                        { "objects", UnityEngine.Object.FindObjectsOfType<GameObject>(true).Length },
                        { "renderers", UnityEngine.Object.FindObjectsOfType<Renderer>(true).Length },
                        { "lights", UnityEngine.Object.FindObjectsOfType<Light>(true).Length },
                        { "cameras", UnityEngine.Object.FindObjectsOfType<Camera>(true).Length },
                        { "colliders", UnityEngine.Object.FindObjectsOfType<Collider>(true).Length },
                        { "triangles", triangles },
                    }
                },
            };
        }

        public static Dictionary<string, object> Quality(Dictionary<string, object> args)
        {
            var hasLevel = Has(args, "level");
            var hasVsync = Has(args, "vSync");
            var hasAa = Has(args, "antiAliasing");
            var hasShadows = Has(args, "shadows");
            var hasTex = Has(args, "textureQuality");
            var apply = hasLevel || hasVsync || hasAa || hasShadows || hasTex;

            if (hasLevel)
            {
                var level = ReadInt(args, "level", 0);
                var names = QualitySettings.names;
                var count = names != null ? names.Length : 0;
                if (level < 0 || level >= count)
                    throw new AgentException("BAD_ARGS", "quality level 超出范围 0.." + Math.Max(0, count - 1));
                QualitySettings.SetQualityLevel(level, true);
            }
            if (hasVsync)
            {
                var v = ReadInt(args, "vSync", 0);
                if (v < 0 || v > 4) throw new AgentException("BAD_ARGS", "vSync 需要在 0..4");
                QualitySettings.vSyncCount = v;
            }
            if (hasAa)
            {
                var aa = ReadInt(args, "antiAliasing", 0);
                if (aa != 0 && aa != 2 && aa != 4 && aa != 8)
                    throw new AgentException("BAD_ARGS", "antiAliasing 只能是 0/2/4/8");
                QualitySettings.antiAliasing = aa;
            }
            if (hasShadows)
            {
                QualitySettings.shadows = ParseShadows(args["shadows"]);
            }
            if (hasTex)
            {
                var q = ReadInt(args, "textureQuality", 3);
                if (q < 0 || q > 3) throw new AgentException("BAD_ARGS", "textureQuality 需要在 0..3");
                QualitySettings.globalTextureMipmapLimit = 3 - q;
            }

            var current = CurrentQuality();
            if (apply) current["applied"] = true;
            return current;
        }

        public static Dictionary<string, object> Audit(Dictionary<string, object> args)
        {
            var maxLights = ReadInt(args, "maxRealtimeLights", DEFAULT_LIGHTS);
            var triBudget = ReadInt(args, "trianglesBudget", DEFAULT_TRIS);
            var objBudget = ReadInt(args, "objectsBudget", DEFAULT_OBJECTS);
            var rootArg = MiniJson.GetString(args, "root", null);
            Transform root = null;
            if (!string.IsNullOrEmpty(rootArg))
            {
                root = FindByPath(rootArg);
                if (root == null) throw new AgentException("NOT_FOUND", "root not found: " + rootArg);
            }

            var objectCount = CountObjects(root);
            var lights = Collect<Light>(root);
            var cameras = Collect<Camera>(root);
            var renderers = Collect<MeshRenderer>(root);
            var filters = Collect<MeshFilter>(root);

            var realtime = 0;
            var directional = 0;
            for (var i = 0; i < lights.Count; i++)
            {
                var light = lights[i];
                if (light == null) continue;
                if (light.lightmapBakeType == LightmapBakeType.Realtime) realtime++;
                if (light.type == LightType.Directional) directional++;
            }

            var triangles = 0;
            for (var i = 0; i < filters.Count; i++)
            {
                if (filters[i] == null) continue;
                triangles += Tris(filters[i].sharedMesh);
            }

            var issues = new List<object>();
            var errors = 0;
            var warns = 0;
            var scene = SceneManager.GetActiveScene();
            var scenePath = string.IsNullOrEmpty(scene.path) ? (scene.name ?? "") : scene.path;
            var scope = root != null ? AgentCommands.GetHierarchyPath(root) : scenePath;

            if (realtime > maxLights)
                Add(issues, ref errors, ref warns, "too-many-realtime-lights", "warn", scope,
                    "实时光 " + realtime + " > " + maxLights);
            if (directional > 1)
                Add(issues, ref errors, ref warns, "too-many-directional-lights", "warn", scope,
                    "方向光 " + directional + " > 1");
            if (cameras.Count > 1)
                Add(issues, ref errors, ref warns, "too-many-cameras", "warn", scope,
                    "相机 " + cameras.Count + " > 1");

            for (var i = 0; i < renderers.Count; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var path = AgentCommands.GetHierarchyPath(r.transform);
                var mats = r.sharedMaterials;
                var nullSlot = false;
                for (var s = 0; s < mats.Length; s++)
                {
                    if (mats[s] == null) nullSlot = true;
                }
                if (nullSlot)
                    Add(issues, ref errors, ref warns, "null-material", "error", path, "材质槽有空引用");
                if (mats.Length > 4)
                    Add(issues, ref errors, ref warns, "too-many-materials", "warn", path,
                        "材质数 " + mats.Length + " > 4");
            }

            if (triangles > triBudget)
                Add(issues, ref errors, ref warns, "too-many-triangles", "warn", scope,
                    "三角形 " + triangles + " > " + triBudget);
            if (objectCount > objBudget)
                Add(issues, ref errors, ref warns, "too-many-objects", "warn", scope,
                    "对象 " + objectCount + " > " + objBudget);

            return new Dictionary<string, object>
            {
                { "scanned", objectCount },
                { "issues", issues },
                { "counts", new Dictionary<string, object>
                    {
                        { "error", errors },
                        { "warn", warns },
                        { "objects", objectCount },
                        { "realtimeLights", realtime },
                        { "directionalLights", directional },
                        { "cameras", cameras.Count },
                        { "triangles", triangles },
                    }
                },
            };
        }

        static Dictionary<string, object> CurrentQuality()
        {
            return new Dictionary<string, object>
            {
                { "level", QualitySettings.GetQualityLevel() },
                { "vSync", QualitySettings.vSyncCount },
                { "antiAliasing", QualitySettings.antiAliasing },
                { "shadows", (int)QualitySettings.shadows },
                { "textureQuality", TexQuality() },
            };
        }

        static int TexQuality()
        {
            var q = 3 - QualitySettings.globalTextureMipmapLimit;
            if (q < 0) return 0;
            if (q > 3) return 3;
            return q;
        }

        static ShadowQuality ParseShadows(object value)
        {
            if (value is string s && Enum.TryParse(s, true, out ShadowQuality named))
                return named;
            var n = 0;
            if (value is int i) n = i;
            else if (value is long l) n = (int)l;
            else if (value is double d) n = (int)d;
            else if (value is float f) n = (int)f;
            else throw new AgentException("BAD_ARGS", "shadows 无法识别");
            if (n < 0 || n > 2) throw new AgentException("BAD_ARGS", "shadows 需要 0..2 (Disable/HardOnly/All)");
            return (ShadowQuality)n;
        }

        static int CountObjects(Transform root)
        {
            if (root == null) return UnityEngine.Object.FindObjectsOfType<GameObject>(true).Length;
            return root.GetComponentsInChildren<Transform>(true).Length;
        }

        static List<T> Collect<T>(Transform root) where T : Component
        {
            if (root == null)
            {
                var found = UnityEngine.Object.FindObjectsOfType<T>(true);
                return new List<T>(found);
            }
            return new List<T>(root.GetComponentsInChildren<T>(true));
        }

        static int Tris(Mesh mesh)
        {
            if (mesh == null) return 0;
            try
            {
                var idx = mesh.triangles;
                if (idx == null) return 0;
                return idx.Length / 3;
            }
            catch
            {
                return 0;
            }
        }

        static double Mb(long bytes)
        {
            return Math.Round(bytes / 1048576.0, 1);
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

        static Transform FindByPath(string path)
        {
            path = (path ?? "").Replace('\\', '/').Trim('/');
            var scene = SceneManager.GetActiveScene();
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

        static bool Has(Dictionary<string, object> args, string key)
        {
            return args != null && args.ContainsKey(key) && args[key] != null;
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

        static double ReadDouble(Dictionary<string, object> args, string key, double fallback)
        {
            if (args == null || !args.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is long l) return l;
            return MiniJson.GetDouble(args, key, fallback);
        }
    }
}
