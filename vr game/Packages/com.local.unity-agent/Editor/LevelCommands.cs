using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Local.UnityAgent
{
    /// <summary>程序化迷宫。根对象名 UnityAgentLevel。不写资产、不保存场景。</summary>
    public static class LevelCommands
    {
        const string ROOT_NAME = "UnityAgentLevel";
        static int _seed;
        const float WALL_THICK = 0.2f;
        const float FLOOR_THICK = 0.2f;

        static readonly int[] DX = { 1, -1, 0, 0 };
        static readonly int[] DZ = { 0, 0, 1, -1 };

        [MenuItem("Tools/Unity Agent/Generate Level")]
        public static void GenerateMenu()
        {
            LogGenerate(null, "Generate Level");
        }

        [MenuItem("Tools/Unity Agent/Generate Rooms")]
        public static void GenerateRoomsMenu()
        {
            LogGenerate(new Dictionary<string, object>
            {
                { "style", "rooms" },
                { "width", 10 },
                { "depth", 10 },
                { "props", 4 },
            }, "Generate Rooms");
        }

        static void LogGenerate(Dictionary<string, object> args, string label)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var data = Generate(args);
                sw.Stop();
                UnityEngine.Debug.Log("[UnityAgent] " + label
                    + " style=" + MiniJson.GetString(data, "style", "")
                    + " seed=" + ReadInt(data, "seed", 0)
                    + " objects=" + ReadInt(data, "objects", 0)
                    + " doors=" + ReadInt(data, "doorSlots", 0)
                    + " props=" + ReadInt(data, "propSlots", 0)
                    + " ms=" + sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] " + label + " failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "level.generate")
            {
                data = Generate(args);
                return true;
            }
            if (cmd == "level.clear")
            {
                data = Clear();
                return true;
            }
            if (cmd == "level.info")
            {
                data = Info();
                return true;
            }
            if (cmd == "level.bake_navmesh")
            {
                data = Bake();
                return true;
            }
            if (cmd == "level.save")
            {
                data = Save(args);
                return true;
            }
            return false;
        }

        public static Dictionary<string, object> Generate(Dictionary<string, object> args)
        {
            var width = ReadInt(args, "width", 16);
            var depth = ReadInt(args, "depth", 16);
            var cellSize = (float)ReadDouble(args, "cellSize", 2);
            var wallHeight = (float)ReadDouble(args, "wallHeight", 3);
            var seed = ReadInt(args, "seed", 0);
            var style = MiniJson.GetString(args, "style", "maze");
            if (string.IsNullOrEmpty(style)) style = "maze";
            var roomsStyle = string.Equals(style, "rooms", StringComparison.OrdinalIgnoreCase);
            if (!roomsStyle && !string.Equals(style, "maze", StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", "style 只支持 maze 或 rooms");
            style = roomsStyle ? "rooms" : "maze";
            var roomsX = ReadInt(args, "roomsX", 3);
            var roomsZ = ReadInt(args, "roomsZ", 3);
            var props = ReadInt(args, "props", 0);
            var doors = ReadBool(args, "doors", true);
            var doorPrefab = LoadSlotPrefab(MiniJson.GetString(args, "doorPrefab", null), "doorPrefab");
            var propPrefab = LoadSlotPrefab(MiniJson.GetString(args, "propPrefab", null), "propPrefab");
            _seed = seed;
            if (props < 0 || props > 20)
                throw new AgentException("BAD_ARGS", "props 需要在 0..20");
            if (width < 2 || depth < 2 || width > 48 || depth > 48)
                throw new AgentException("BAD_ARGS", "width/depth 需要在 2..48");
            if (cellSize < 0.2f || wallHeight < 0.2f)
                throw new AgentException("BAD_ARGS", "cellSize/wallHeight 过小");

            ClearRoots();
            var root = new GameObject(ROOT_NAME);
            var mat = SharedMat();
            var block = new MaterialPropertyBlock();
            var floorColor = new Color(0.22f, 0.22f, 0.24f);
            var wallColor = new Color(0.78f, 0.78f, 0.8f);

            var wallEast = new bool[width, depth];
            var wallNorth = new bool[width, depth];
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
            {
                wallEast[x, z] = x < width - 1;
                wallNorth[x, z] = z < depth - 1;
            }
            List<Vector3> doorPoints;
            var roomCount = 0;
            if (roomsStyle)
            {
                roomCount = roomsX * roomsZ;
                doorPoints = BuildRooms(width, depth, roomsX, roomsZ, cellSize, seed, wallEast, wallNorth);
            }
            else
            {
                Carve(width, depth, seed, wallEast, wallNorth);
                doorPoints = Openings(width, depth, cellSize, wallEast, wallNorth);
            }

            var floors = 0;
            var walls = 0;
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
            {
                var cx = (x + 0.5f) * cellSize;
                var cz = (z + 0.5f) * cellSize;
                MakeBox(root.transform, "F_" + x + "_" + z,
                    new Vector3(cx, 0f, cz),
                    new Vector3(cellSize, FLOOR_THICK, cellSize),
                    mat, floorColor, block);
                floors++;
            }

            for (var z = 0; z < depth; z++)
            {
                var cz = (z + 0.5f) * cellSize;
                MakeBox(root.transform, "Wb_" + z,
                    new Vector3(0f, wallHeight * 0.5f, cz),
                    new Vector3(WALL_THICK, wallHeight, cellSize),
                    mat, wallColor, block);
                MakeBox(root.transform, "We_" + z,
                    new Vector3(width * cellSize, wallHeight * 0.5f, cz),
                    new Vector3(WALL_THICK, wallHeight, cellSize),
                    mat, wallColor, block);
                walls += 2;
            }
            for (var x = 0; x < width; x++)
            {
                var cx = (x + 0.5f) * cellSize;
                MakeBox(root.transform, "Ws_" + x,
                    new Vector3(cx, wallHeight * 0.5f, 0f),
                    new Vector3(cellSize, wallHeight, WALL_THICK),
                    mat, wallColor, block);
                MakeBox(root.transform, "Wn_" + x,
                    new Vector3(cx, wallHeight * 0.5f, depth * cellSize),
                    new Vector3(cellSize, wallHeight, WALL_THICK),
                    mat, wallColor, block);
                walls += 2;
            }
            for (var x = 0; x < width - 1; x++)
            for (var z = 0; z < depth; z++)
            {
                if (!wallEast[x, z]) continue;
                MakeBox(root.transform, "Vi_" + x + "_" + z,
                    new Vector3((x + 1) * cellSize, wallHeight * 0.5f, (z + 0.5f) * cellSize),
                    new Vector3(WALL_THICK, wallHeight, cellSize),
                    mat, wallColor, block);
                walls++;
            }
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth - 1; z++)
            {
                if (!wallNorth[x, z]) continue;
                MakeBox(root.transform, "Hi_" + x + "_" + z,
                    new Vector3((x + 0.5f) * cellSize, wallHeight * 0.5f, (z + 1) * cellSize),
                    new Vector3(cellSize, wallHeight, WALL_THICK),
                    mat, wallColor, block);
                walls++;
            }

            var doorSlots = 0;
            if (doors)
                doorSlots = SpawnDoors(root.transform, doorPoints, doorPrefab);
            var propSlots = SpawnProps(root.transform, props, width, depth, cellSize, seed, mat, block, propPrefab);
            var objects = ChildCount(root);

            var result = new Dictionary<string, object>
            {
                { "root", ROOT_NAME },
                { "seed", seed },
                { "style", style },
                { "cells", width * depth },
                { "walls", walls },
                { "floors", floors },
                { "objects", objects },
                { "bounds", BoundsDict(WorldBounds(root)) },
                { "doors", doors },
                { "props", props },
                { "doorSlots", doorSlots },
                { "propSlots", propSlots },
            };
            if (roomsStyle) result["rooms"] = roomCount;
            return result;
        }

        public static Dictionary<string, object> Clear()
        {
            var removed = ClearRoots();
            return new Dictionary<string, object> { { "removed", removed } };
        }

        public static Dictionary<string, object> Info()
        {
            var root = FindRoot();
            if (root == null)
            {
                return new Dictionary<string, object>
                {
                    { "exists", false },
                    { "root", null },
                    { "objects", 0 },
                    { "bounds", null },
                    { "doorSlots", 0 },
                    { "propSlots", 0 },
                    { "navMeshExists", NavExists() },
                };
            }
            return new Dictionary<string, object>
            {
                { "exists", true },
                { "root", ROOT_NAME },
                { "objects", ChildCount(root) },
                { "bounds", BoundsDict(WorldBounds(root)) },
                { "doorSlots", CountNamed(root, "DoorSlot_") },
                { "propSlots", CountNamed(root, "PropSlot_") },
                { "navMeshExists", NavExists() },
            };
        }

        static Dictionary<string, object> Bake()
        {
            try
            {
                var type = Type.GetType("UnityEditor.AI.NavMeshBuilder, UnityEditor");
                var method = type != null ? type.GetMethod("BuildNavMesh", Type.EmptyTypes) : null;
                if (method == null)
                    throw new AgentException("NOT_READY", "UnityEditor.AI.NavMeshBuilder.BuildNavMesh 不可用");
                method.Invoke(null, null);
            }
            catch (AgentException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException tie && tie.InnerException != null
                    ? tie.InnerException.Message
                    : ex.Message;
                throw new AgentException("NOT_READY", "NavMesh 烘焙失败: " + inner);
            }
            return new Dictionary<string, object>
            {
                { "built", true },
                { "objects", SceneObjects() },
            };
        }

        static List<Vector3> BuildRooms(int width, int depth, int roomsX, int roomsZ, float cellSize, int seed, bool[,] wallEast, bool[,] wallNorth)
        {
            if (roomsX < 1 || roomsZ < 1 || roomsX > width || roomsZ > depth)
                throw new AgentException("BAD_ARGS", "roomsX/roomsZ 需要在 1..宽/深");
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
            {
                wallEast[x, z] = false;
                wallNorth[x, z] = false;
            }
            var xCuts = Cuts(width, roomsX);
            var zCuts = Cuts(depth, roomsZ);
            var rng = new System.Random(seed);
            var doors = new List<Vector3>();
            for (var rz = 0; rz < roomsZ; rz++)
            for (var rx = 0; rx < roomsX - 1; rx++)
            {
                var z0 = zCuts[rz];
                var z1 = zCuts[rz + 1];
                var xWall = xCuts[rx + 1] - 1;
                for (var z = z0; z < z1; z++) wallEast[xWall, z] = true;
                var doorZ = z0 + rng.Next(z1 - z0);
                wallEast[xWall, doorZ] = false;
                doors.Add(new Vector3((xWall + 1) * cellSize, 0.1f, (doorZ + 0.5f) * cellSize));
            }
            for (var rx = 0; rx < roomsX; rx++)
            for (var rz = 0; rz < roomsZ - 1; rz++)
            {
                var x0 = xCuts[rx];
                var x1 = xCuts[rx + 1];
                var zWall = zCuts[rz + 1] - 1;
                for (var x = x0; x < x1; x++) wallNorth[x, zWall] = true;
                var doorX = x0 + rng.Next(x1 - x0);
                wallNorth[doorX, zWall] = false;
                doors.Add(new Vector3((doorX + 0.5f) * cellSize, 0.1f, (zWall + 1) * cellSize));
            }
            return doors;
        }

        static int[] Cuts(int total, int parts)
        {
            var starts = new int[parts + 1];
            var baseSize = total / parts;
            var rem = total % parts;
            var cursor = 0;
            for (var i = 0; i < parts; i++)
            {
                starts[i] = cursor;
                cursor += baseSize + (i < rem ? 1 : 0);
            }
            starts[parts] = total;
            return starts;
        }

        static List<Vector3> Openings(int width, int depth, float cellSize, bool[,] wallEast, bool[,] wallNorth)
        {
            var doors = new List<Vector3>();
            for (var x = 0; x < width - 1; x++)
            for (var z = 0; z < depth; z++)
            {
                if (wallEast[x, z]) continue;
                doors.Add(new Vector3((x + 1) * cellSize, 0.1f, (z + 0.5f) * cellSize));
            }
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth - 1; z++)
            {
                if (wallNorth[x, z]) continue;
                doors.Add(new Vector3((x + 0.5f) * cellSize, 0.1f, (z + 1) * cellSize));
            }
            return doors;
        }

        static int SpawnDoors(Transform parent, List<Vector3> points, GameObject prefab)
        {
            for (var i = 0; i < points.Count; i++)
            {
                if (prefab == null)
                {
                    var go = new GameObject("DoorSlot_" + i);
                    go.transform.SetParent(parent, false);
                    go.transform.localPosition = points[i];
                }
                else
                {
                    SpawnPrefab(parent, "DoorSlot_" + i, points[i], prefab);
                }
            }
            return points.Count;
        }

        static int SpawnProps(Transform parent, int count, int width, int depth, float cellSize, int seed, Material mat, MaterialPropertyBlock block, GameObject prefab)
        {
            if (count <= 0) return 0;
            var cells = new List<Vector2Int>(width * depth);
            for (var x = 0; x < width; x++)
            for (var z = 0; z < depth; z++)
                cells.Add(new Vector2Int(x, z));
            var rng = new System.Random(seed);
            for (var i = cells.Count - 1; i > 0; i--)
            {
                var j = rng.Next(i + 1);
                var tmp = cells[i];
                cells[i] = cells[j];
                cells[j] = tmp;
            }
            var n = Math.Min(count, cells.Count);
            var boxColor = new Color(0.55f, 0.42f, 0.28f);
            for (var i = 0; i < n; i++)
            {
                var c = cells[i];
                var pos = new Vector3((c.x + 0.5f) * cellSize, 0.1f, (c.y + 0.5f) * cellSize);
                if (prefab != null)
                {
                    SpawnPrefab(parent, "PropSlot_" + i, pos, prefab);
                    continue;
                }
                var slot = new GameObject("PropSlot_" + i);
                slot.transform.SetParent(parent, false);
                slot.transform.localPosition = pos;
                MakeBox(slot.transform, "PropBox_" + i, new Vector3(0f, 0.25f, 0f), Vector3.one * 0.5f, mat, boxColor, block);
            }
            return n;
        }

        static void SpawnPrefab(Transform parent, string name, Vector3 pos, GameObject prefab)
        {
            var inst = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
            if (inst == null)
                throw new AgentException("BAD_ARGS", "无法实例化 prefab: " + prefab.name);
            inst.name = name;
            inst.transform.localPosition = pos;
        }

        static GameObject LoadSlotPrefab(string raw, string label)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var assetPath = PathUtil.ToAssetPath(raw);
            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", label + " 必须在 Assets/ 下: " + raw);
            var main = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (main == null)
                throw new AgentException("NOT_FOUND", label + " 不存在: " + assetPath);
            var go = main as GameObject;
            if (go == null)
                throw new AgentException("BAD_ARGS", label + " 不是 GameObject prefab: " + assetPath);
            return go;
        }

        static Dictionary<string, object> Save(Dictionary<string, object> args)
        {
            var root = FindRoot();
            if (root == null)
                throw new AgentException("NOT_FOUND", "UnityAgentLevel 不存在");
            var mode = MiniJson.GetString(args, "mode", "prefab");
            if (string.IsNullOrEmpty(mode)) mode = "prefab";
            mode = mode.ToLowerInvariant();
            if (mode != "prefab" && mode != "scene")
                throw new AgentException("BAD_ARGS", "mode 只能是 prefab 或 scene");

            var fileName = MiniJson.GetString(args, "name", null);
            if (string.IsNullOrWhiteSpace(fileName)) fileName = "level_" + _seed;
            fileName = fileName.Trim();
            if (fileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 || fileName.Contains(".."))
                throw new AgentException("BAD_ARGS", "name 不能包含路径: " + fileName);

            var given = MiniJson.GetString(args, "path", null);
            string assetPath;
            if (!string.IsNullOrWhiteSpace(given))
                assetPath = PathUtil.ToAssetPath(given);
            else if (mode == "prefab")
                assetPath = "Assets/Levels/Generated/" + fileName + ".prefab";
            else
                assetPath = "Assets/Scenes/Generated/" + fileName + ".unity";

            if (!assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", "path 必须在 Assets/ 下: " + assetPath);
            if (mode == "prefab" && !assetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", "prefab 路径必须以 .prefab 结尾: " + assetPath);
            if (mode == "scene" && !assetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
                throw new AgentException("BAD_ARGS", "scene 路径必须以 .unity 结尾: " + assetPath);

            var overwrite = ReadBool(args, "overwrite", false);
            if (AssetDatabase.LoadMainAssetAtPath(assetPath) != null && !overwrite)
                throw new AgentException("BAD_ARGS", "资产已存在，需要 overwrite: " + assetPath);

            var dir = assetPath.Substring(0, assetPath.LastIndexOf('/'));
            EnsureAssetFolder(dir);

            if (mode == "prefab")
            {
                var saved = PrefabUtility.SaveAsPrefabAsset(root, assetPath, out var ok);
                if (!ok || saved == null)
                    throw new AgentException("INTERNAL", "保存 prefab 失败: " + assetPath);
                AssetDatabase.SaveAssets();
                return new Dictionary<string, object>
                {
                    { "mode", "prefab" },
                    { "path", assetPath },
                    { "objects", ChildCount(root) },
                };
            }

            var sceneOk = UnityEditor.SceneManagement.EditorSceneManager.SaveScene(root.scene, assetPath, true);
            if (!sceneOk)
                throw new AgentException("INTERNAL", "保存场景失败: " + assetPath);
            return new Dictionary<string, object>
            {
                { "mode", "scene" },
                { "path", assetPath },
            };
        }

        static void EnsureAssetFolder(string assetDir)
        {
            assetDir = (assetDir ?? "").Replace('\\', '/').Trim('/');
            if (assetDir.Length == 0 || AssetDatabase.IsValidFolder(assetDir)) return;
            var parts = assetDir.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    var guid = AssetDatabase.CreateFolder(cur, parts[i]);
                    if (string.IsNullOrEmpty(guid) && !AssetDatabase.IsValidFolder(next))
                        throw new AgentException("INTERNAL", "创建目录失败: " + next);
                }
                cur = next;
            }
        }

        static int ChildCount(GameObject root)
        {
            var n = 0;
            foreach (var _ in root.GetComponentsInChildren<Transform>(true)) n++;
            return Math.Max(0, n - 1);
        }

        static int CountNamed(GameObject root, string prefix)
        {
            var n = 0;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name.StartsWith(prefix, StringComparison.Ordinal)) n++;
            }
            return n;
        }

        static bool NavExists()
        {
            try
            {
                var tri = NavMesh.CalculateTriangulation();
                return tri.vertices != null && tri.vertices.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        static int SceneObjects()
        {
            var n = 0;
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go == null) continue;
                n += go.GetComponentsInChildren<Transform>(true).Length;
            }
            return n;
        }

        static void Carve(int width, int depth, int seed, bool[,] wallEast, bool[,] wallNorth)
        {
            var visited = new bool[width, depth];
            var rng = new System.Random(seed);
            var stack = new Stack<Vector2Int>();
            visited[0, 0] = true;
            stack.Push(new Vector2Int(0, 0));
            var order = new int[4];
            while (stack.Count > 0)
            {
                var cur = stack.Peek();
                var moved = false;
                for (var i = 0; i < 4; i++) order[i] = i;
                for (var i = 3; i > 0; i--)
                {
                    var j = rng.Next(i + 1);
                    var tmp = order[i];
                    order[i] = order[j];
                    order[j] = tmp;
                }
                for (var i = 0; i < 4; i++)
                {
                    var d = order[i];
                    var nx = cur.x + DX[d];
                    var nz = cur.y + DZ[d];
                    if (nx < 0 || nz < 0 || nx >= width || nz >= depth || visited[nx, nz]) continue;
                    if (d == 0) wallEast[cur.x, cur.y] = false;
                    else if (d == 1) wallEast[nx, nz] = false;
                    else if (d == 2) wallNorth[cur.x, cur.y] = false;
                    else wallNorth[nx, nz] = false;
                    visited[nx, nz] = true;
                    stack.Push(new Vector2Int(nx, nz));
                    moved = true;
                    break;
                }
                if (!moved) stack.Pop();
            }
        }

        static void MakeBox(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat, Color color, MaterialPropertyBlock block)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            block.Clear();
            block.SetColor("_Color", color);
            block.SetColor("_BaseColor", color);
            renderer.SetPropertyBlock(block);
        }

        static Material SharedMat()
        {
            var mat = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
            if (mat != null) return mat;
            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            if (shader == null) throw new AgentException("NOT_READY", "找不到 Standard/Diffuse shader");
            mat = new Material(shader);
            mat.hideFlags = HideFlags.HideAndDontSave;
            return mat;
        }

        static int ClearRoots()
        {
            var scene = SceneManager.GetActiveScene();
            var doomed = new List<GameObject>();
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go != null && go.name == ROOT_NAME) doomed.Add(go);
            }
            var removed = 0;
            foreach (var go in doomed)
            {
                removed += go.GetComponentsInChildren<Transform>(true).Length;
                UnityEngine.Object.DestroyImmediate(go);
            }
            return removed;
        }

        static GameObject FindRoot()
        {
            var scene = SceneManager.GetActiveScene();
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go != null && go.name == ROOT_NAME) return go;
            }
            return null;
        }

        static Bounds WorldBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.zero);
            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b;
        }

        static Dictionary<string, object> BoundsDict(Bounds b)
        {
            return new Dictionary<string, object>
            {
                { "min", Vec(b.min) },
                { "max", Vec(b.max) },
            };
        }

        static Dictionary<string, object> Vec(Vector3 v)
        {
            return new Dictionary<string, object> { { "x", v.x }, { "y", v.y }, { "z", v.z } };
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

        static bool ReadBool(Dictionary<string, object> args, string key, bool fallback)
        {
            if (args == null || !args.ContainsKey(key)) return fallback;
            return MiniJson.GetBool(args, key, fallback);
        }
    }
}
