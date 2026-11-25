using System;
using System.Collections.Generic;
using System.Globalization;
using Local.UnityAgent.Runtime;
using UnityEditor;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>生命、拾取、交互。不保存场景。</summary>
    public static class GameplayCommands
    {
        static readonly Color PickupYellow = new Color(0.95f, 0.78f, 0.15f, 1f);
        static readonly Color InteractBlue = new Color(0.2f, 0.45f, 0.95f, 1f);

        [MenuItem("Tools/Unity Agent/Create Pickup")]
        public static void MenuPickup()
        {
            try
            {
                var data = CreatePickup(null);
                UnityEngine.Debug.Log("[UnityAgent] Create Pickup path=" + MiniJson.GetString(data, "path"));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Create Pickup failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "gameplay.attach_health") { data = AttachHealth(args); return true; }
            if (cmd == "gameplay.damage") { data = Damage(args); return true; }
            if (cmd == "gameplay.create_pickup") { data = CreatePickup(args); return true; }
            if (cmd == "gameplay.create_interactable") { data = CreateInteractable(args); return true; }
            if (cmd == "gameplay.attach_interactor") { data = AttachInteractor(args); return true; }
            if (cmd == "gameplay.info") { data = Info(); return true; }
            return false;
        }

        public static Dictionary<string, object> AttachHealth(Dictionary<string, object> args)
        {
            var go = RequireGo(args);
            var max = ReadFloat(args, "max", 100f);
            if (max < 1f) max = 1f;
            var hp = go.GetComponent<Health>();
            if (hp == null) hp = go.AddComponent<Health>();
            hp.maxHealth = max;
            hp.current = max;
            if (args != null && args.ContainsKey("destroyOnDeath"))
                hp.destroyOnDeath = MiniJson.GetBool(args, "destroyOnDeath", false);
            return HealthState(go, hp);
        }

        public static Dictionary<string, object> Damage(Dictionary<string, object> args)
        {
            var go = RequireGo(args);
            var hp = go.GetComponent<Health>();
            if (hp == null) throw new AgentException("NOT_FOUND", "Health missing: " + AgentCommands.GetHierarchyPath(go.transform));
            var amount = ReadFloat(args, "amount", 0f);
            hp.TakeDamage(amount);
            var data = HealthState(go, hp);
            data["amount"] = amount;
            return data;
        }

        public static Dictionary<string, object> CreatePickup(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", "Pickup");
            if (string.IsNullOrWhiteSpace(name)) name = "Pickup";
            var itemId = MiniJson.GetString(args, "itemId", "coin");
            if (string.IsNullOrEmpty(itemId)) itemId = "coin";
            var amount = ReadInt(args, "amount", 1);
            if (amount < 1) amount = 1;
            var color = ReadColor(args, "color", PickupYellow);
            var go = MakeCube(name, args, color);
            var box = go.GetComponent<BoxCollider>();
            if (box != null) box.isTrigger = true;
            var pickup = go.AddComponent<Pickup>();
            pickup.itemId = itemId;
            pickup.amount = amount;
            pickup.ForceTrigger();
            Undo.RegisterCreatedObjectUndo(go, "UnityAgent.gameplay.create_pickup");
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(go.transform) },
                { "itemId", itemId },
                { "amount", amount },
                { "trigger", box != null && box.isTrigger },
                { "color", ColorHex(color) },
            };
        }

        public static Dictionary<string, object> CreateInteractable(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", "Interactable");
            if (string.IsNullOrWhiteSpace(name)) name = "Interactable";
            var prompt = MiniJson.GetString(args, "prompt", "按 E 交互");
            if (string.IsNullOrEmpty(prompt)) prompt = "按 E 交互";
            var color = ReadColor(args, "color", InteractBlue);
            var go = MakeCube(name, args, color);
            var box = go.GetComponent<BoxCollider>();
            if (box != null) box.isTrigger = true;
            var interact = go.AddComponent<Interactable>();
            interact.prompt = prompt;
            Undo.RegisterCreatedObjectUndo(go, "UnityAgent.gameplay.create_interactable");
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(go.transform) },
                { "prompt", prompt },
                { "trigger", box != null && box.isTrigger },
                { "color", ColorHex(color) },
            };
        }

        public static Dictionary<string, object> AttachInteractor(Dictionary<string, object> args)
        {
            var go = RequireGo(args);
            var range = ReadFloat(args, "range", 3f);
            if (range < 0.1f) range = 3f;
            var interactor = go.GetComponent<PlayerInteractor>();
            if (interactor == null) interactor = go.AddComponent<PlayerInteractor>();
            interactor.range = range;
            var cam = go.GetComponentInChildren<Camera>(true);
            interactor.camera = cam != null ? cam.transform : null;
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(go.transform) },
                { "range", range },
                { "cameraPath", cam != null ? AgentCommands.GetHierarchyPath(cam.transform) : "" },
            };
        }

        public static Dictionary<string, object> Info()
        {
            return new Dictionary<string, object>
            {
                { "health", Group<Health>() },
                { "pickups", Group<Pickup>() },
                { "interactables", Group<Interactable>() },
            };
        }

        static Dictionary<string, object> HealthState(GameObject go, Health hp)
        {
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(go.transform) },
                { "current", hp.current },
                { "max", hp.maxHealth },
                { "dead", hp.IsDead },
                { "destroyOnDeath", hp.destroyOnDeath },
            };
        }

        static Dictionary<string, object> Group<T>() where T : Component
        {
            var paths = new List<object>();
            foreach (var c in UnityEngine.Object.FindObjectsOfType<T>(true))
            {
                if (c == null) continue;
                paths.Add(AgentCommands.GetHierarchyPath(c.transform));
            }
            return new Dictionary<string, object>
            {
                { "count", paths.Count },
                { "paths", paths },
            };
        }

        static GameObject MakeCube(string name, Dictionary<string, object> args, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            if (args != null && args.TryGetValue("spawn", out var spawn) && spawn != null)
                go.transform.position = ReadVec3(spawn, Vector3.zero);
            Tint(go, color);
            return go;
        }

        static GameObject RequireGo(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            if (string.IsNullOrEmpty(path)) throw new AgentException("BAD_ARGS", "path 必填");
            var t = AgentCommands.FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            return t.gameObject;
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

        static string ColorHex(Color c)
        {
            var r = Mathf.Clamp(Mathf.RoundToInt(c.r * 255f), 0, 255);
            var g = Mathf.Clamp(Mathf.RoundToInt(c.g * 255f), 0, 255);
            var b = Mathf.Clamp(Mathf.RoundToInt(c.b * 255f), 0, 255);
            return "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        static Color ReadColor(Dictionary<string, object> args, string key, Color fallback)
        {
            if (args == null || !args.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is string s) return Hex(s, fallback);
            var rgb = ReadVec3(v, new Vector3(fallback.r, fallback.g, fallback.b));
            return new Color(rgb.x, rgb.y, rgb.z, 1f);
        }

        static Color Hex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            if (hex[0] == '#') hex = hex.Substring(1);
            if (hex.Length != 6) return fallback;
            if (!int.TryParse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)) return fallback;
            if (!int.TryParse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)) return fallback;
            if (!int.TryParse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)) return fallback;
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
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

        static int ReadInt(Dictionary<string, object> args, string key, int fallback)
        {
            if (args == null || !args.ContainsKey(key) || args[key] == null) return fallback;
            return MiniJson.GetInt(args, key, fallback);
        }
    }
}
