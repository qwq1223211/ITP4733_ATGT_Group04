using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
namespace Local.UnityAgent
{
    /// <summary>环境光与方向光预设。不保存场景。</summary>
    public static class LightingCommands
    {
        const string DIR_NAME = "Directional Light";

        [MenuItem("Tools/Unity Agent/Lighting Preset")]
        public static void MenuWarehouse()
        {
            try
            {
                var data = Apply(new Dictionary<string, object> { { "preset", "warehouse" } });
                UnityEngine.Debug.Log("[UnityAgent] Lighting Preset warehouse fog="
                    + MiniJson.GetBool(MiniJson.AsObject(data["fog"]), "enabled", false));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Lighting Preset failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "lighting.presets") { data = Presets(); return true; }
            if (cmd == "lighting.apply") { data = Apply(args); return true; }
            if (cmd == "lighting.info") { data = Info(); return true; }
            return false;
        }

        public static Dictionary<string, object> Presets()
        {
            var list = new List<object>
            {
                PresetMeta("day", "Daylight", "明亮户外，无雾"),
                PresetMeta("dusk", "Dusk", "暖色黄昏与轻雾"),
                PresetMeta("night", "Night", "冷色夜雾"),
                PresetMeta("horror", "Horror", "暗红恐怖氛围"),
                PresetMeta("warehouse", "Warehouse", "工业仓库灰雾"),
            };
            return new Dictionary<string, object> { { "presets", list } };
        }

        public static Dictionary<string, object> Apply(Dictionary<string, object> args)
        {
            var id = MiniJson.GetString(args, "preset", "warehouse");
            if (string.IsNullOrEmpty(id)) id = "warehouse";
            var def = FindPreset(id);
            if (def == null) throw new AgentException("BAD_ARGS", "未知 preset: " + id);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = def.Ambient;
            RenderSettings.ambientIntensity = def.AmbientIntensity;

            var fogOn = def.FogEnabled;
            if (args != null && args.ContainsKey("fog"))
                fogOn = MiniJson.GetBool(args, "fog", fogOn);
            RenderSettings.fog = fogOn;
            if (fogOn)
            {
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = args != null && args.ContainsKey("fogColor")
                    ? ParseHex(MiniJson.GetString(args, "fogColor", null), def.FogColor)
                    : def.FogColor;
                RenderSettings.fogDensity = args != null && args.ContainsKey("fogDensity")
                    ? (float)MiniJson.GetDouble(args, "fogDensity", def.FogDensity)
                    : def.FogDensity;
            }

            var light = FindOrCreateDirLight();
            light.type = LightType.Directional;
            light.color = def.LightColor;
            light.intensity = def.LightIntensity;
            light.transform.rotation = Quaternion.Euler(def.LightEuler);

            return new Dictionary<string, object>
            {
                { "preset", def.Id },
                { "ambient", AmbientDict() },
                { "fog", FogDict() },
                { "light", LightDict(light) },
            };
        }

        public static Dictionary<string, object> Info()
        {
            var lights = new List<object>();
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>(true))
            {
                if (l == null) continue;
                lights.Add(new Dictionary<string, object>
                {
                    { "name", l.gameObject.name },
                    { "path", AgentCommands.GetHierarchyPath(l.transform) },
                    { "type", l.type.ToString() },
                    { "intensity", l.intensity },
                    { "color", ColorHex(l.color) },
                });
            }
            return new Dictionary<string, object>
            {
                { "ambient", AmbientDict() },
                { "fog", FogDict() },
                { "lights", lights },
            };
        }

        public static Snapshot CaptureSnapshot()
        {
            var snap = new Snapshot
            {
                AmbientMode = RenderSettings.ambientMode,
                AmbientLight = RenderSettings.ambientLight,
                AmbientIntensity = RenderSettings.ambientIntensity,
                Fog = RenderSettings.fog,
                FogColor = RenderSettings.fogColor,
                FogMode = RenderSettings.fogMode,
                FogDensity = RenderSettings.fogDensity,
            };
            var light = FindDirLight();
            if (light != null)
            {
                snap.HadLight = true;
                snap.LightInstanceId = light.GetInstanceID();
                snap.LightColor = light.color;
                snap.LightIntensity = light.intensity;
                snap.LightEuler = light.transform.rotation.eulerAngles;
            }
            return snap;
        }

        public static void RestoreSnapshot(Snapshot snap)
        {
            RenderSettings.ambientMode = snap.AmbientMode;
            RenderSettings.ambientLight = snap.AmbientLight;
            RenderSettings.ambientIntensity = snap.AmbientIntensity;
            RenderSettings.fog = snap.Fog;
            RenderSettings.fogColor = snap.FogColor;
            RenderSettings.fogMode = snap.FogMode;
            RenderSettings.fogDensity = snap.FogDensity;
            if (!snap.HadLight) return;
            var light = FindDirLight();
            if (light == null || light.GetInstanceID() != snap.LightInstanceId) return;
            light.color = snap.LightColor;
            light.intensity = snap.LightIntensity;
            light.transform.rotation = Quaternion.Euler(snap.LightEuler);
        }

        public sealed class Snapshot
        {
            public AmbientMode AmbientMode;
            public Color AmbientLight;
            public float AmbientIntensity;
            public bool Fog;
            public Color FogColor;
            public FogMode FogMode;
            public float FogDensity;
            public bool HadLight;
            public int LightInstanceId;
            public Color LightColor;
            public float LightIntensity;
            public Vector3 LightEuler;
        }

        static PresetDef FindPreset(string id)
        {
            foreach (var p in AllPresets())
            {
                if (string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) return p;
            }
            return null;
        }

        static IEnumerable<PresetDef> AllPresets()
        {
            yield return new PresetDef("day", Hex("#87CEEB"), 1f, false, Color.white, 0f,
                Color.white, 1.1f, new Vector3(50, -30, 0));
            yield return new PresetDef("dusk", Hex("#FF9E6D"), 0.7f, true, Hex("#FF8A5B"), 0.012f,
                Hex("#FFB27A"), 0.7f, new Vector3(15, -40, 0));
            yield return new PresetDef("night", Hex("#1B2A4A"), 0.35f, true, Hex("#0B1220"), 0.03f,
                Hex("#8FA8FF"), 0.25f, new Vector3(35, -20, 0));
            yield return new PresetDef("horror", Hex("#12000A"), 0.25f, true, Hex("#140008"), 0.075f,
                Hex("#FF3B3B"), 0.35f, new Vector3(80, 20, 0));
            yield return new PresetDef("warehouse", Hex("#6B7280"), 0.6f, true, Hex("#8A8F98"), 0.02f,
                Hex("#FFF3E0"), 0.9f, new Vector3(55, -25, 0));
        }

        static Dictionary<string, object> PresetMeta(string id, string label, string note)
        {
            return new Dictionary<string, object> { { "id", id }, { "label", label }, { "note", note } };
        }

        static Light FindDirLight()
        {
            foreach (var l in UnityEngine.Object.FindObjectsOfType<Light>(true))
            {
                if (l != null && l.type == LightType.Directional && l.gameObject.name == DIR_NAME)
                    return l;
            }
            return null;
        }

        static Light FindOrCreateDirLight()
        {
            var light = FindDirLight();
            if (light != null) return light;
            var go = new GameObject(DIR_NAME);
            Undo.RegisterCreatedObjectUndo(go, "lighting.apply");
            light = go.AddComponent<Light>();
            return light;
        }

        static Dictionary<string, object> AmbientDict()
        {
            return new Dictionary<string, object>
            {
                { "mode", RenderSettings.ambientMode.ToString() },
                { "color", ColorHex(RenderSettings.ambientLight) },
                { "intensity", RenderSettings.ambientIntensity },
            };
        }

        static Dictionary<string, object> FogDict()
        {
            return new Dictionary<string, object>
            {
                { "enabled", RenderSettings.fog },
                { "color", ColorHex(RenderSettings.fogColor) },
                { "density", RenderSettings.fogDensity },
                { "mode", RenderSettings.fogMode.ToString() },
            };
        }

        static Dictionary<string, object> LightDict(Light light)
        {
            var euler = light.transform.rotation.eulerAngles;
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(light.transform) },
                { "color", ColorHex(light.color) },
                { "intensity", light.intensity },
                { "rotation", RotDict(euler) },
            };
        }

        static Dictionary<string, object> RotDict(Vector3 v)
        {
            return new Dictionary<string, object> { { "x", v.x }, { "y", v.y }, { "z", v.z } };
        }

        static string ColorHex(Color c)
        {
            return "#" + ColorUtility.ToHtmlStringRGB(c);
        }

        static Color Hex(string s)
        {
            if (ColorUtility.TryParseHtmlString(s, out var c)) return c;
            return Color.white;
        }

        static Color ParseHex(string s, Color fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            if (ColorUtility.TryParseHtmlString(s, out var c)) return c;
            return fallback;
        }

        sealed class PresetDef
        {
            public readonly string Id;
            public readonly Color Ambient;
            public readonly float AmbientIntensity;
            public readonly bool FogEnabled;
            public readonly Color FogColor;
            public readonly float FogDensity;
            public readonly Color LightColor;
            public readonly float LightIntensity;
            public readonly Vector3 LightEuler;

            public PresetDef(string id, Color ambient, float ambientIntensity, bool fogEnabled, Color fogColor, float fogDensity,
                Color lightColor, float lightIntensity, Vector3 lightEuler)
            {
                Id = id;
                Ambient = ambient;
                AmbientIntensity = ambientIntensity;
                FogEnabled = fogEnabled;
                FogColor = fogColor;
                FogDensity = fogDensity;
                LightColor = lightColor;
                LightIntensity = lightIntensity;
                LightEuler = lightEuler;
            }
        }
    }
}
