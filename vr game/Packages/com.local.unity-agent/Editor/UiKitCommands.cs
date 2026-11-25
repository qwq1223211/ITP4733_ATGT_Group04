using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Local.UnityAgent
{
    /// <summary>一键 HUD / 暂停菜单。不保存场景。</summary>
    public static class UiKitCommands
    {
        const string OUT_DIR = "Assets/UI/Generated";

        [MenuItem("Tools/Unity Agent/Create HUD")]
        public static void HudMenu()
        {
            try
            {
                var data = Hud(null);
                UnityEngine.Debug.Log("[UnityAgent] Create HUD path=" + MiniJson.GetString(data, "path")
                    + " elements=" + ReadInt(data, "elements", 0)
                    + " prefab=" + MiniJson.GetString(data, "prefab", ""));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Create HUD failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "uikit.hud") { data = Hud(args); return true; }
            if (cmd == "uikit.pause") { data = Pause(args); return true; }
            if (cmd == "uikit.list") { data = List(args); return true; }
            return false;
        }

        public static Dictionary<string, object> Hud(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", "HUD");
            if (string.IsNullOrWhiteSpace(name)) name = "HUD";
            var root = MakeCanvas(name);
            var names = new List<object>();
            var bar = AddImage(root.transform, "HealthBar", new Color(0.12f, 0.14f, 0.18f, 0.9f),
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(240, 28));
            names.Add(bar.name);
            var fill = AddImage(bar.transform, "HealthFill", new Color(0.86f, 0.22f, 0.24f, 1f),
                new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(0, 0), new Vector2(168, 0));
            names.Add(fill.name);
            names.Add(AddLabel(root.transform, "HealthText", "100 / 100", 16, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(240, 28)).name);
            names.Add(AddImage(root.transform, "Crosshair", Color.white,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(6, 6)).name);
            names.Add(AddLabel(root.transform, "Ammo", "30 / 90", 20, Color.white, TextAnchor.MiddleRight,
                new Vector2(1, 0), new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(220, 32)).name);
            names.Add(AddLabel(root.transform, "Objective", "OBJECTIVE", 18, new Color(0.45f, 0.48f, 0.55f, 1f), TextAnchor.MiddleCenter,
                new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(480, 32)).name);
            return Finish(root, names, ReadBool(args, "savePrefab", true));
        }

        public static Dictionary<string, object> Pause(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", "PauseMenu");
            if (string.IsNullOrWhiteSpace(name)) name = "PauseMenu";
            var root = MakeCanvas(name);
            var names = new List<object>();
            names.Add(AddImage(root.transform, "Dim", new Color(0f, 0f, 0f, 0.8f),
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero).name);
            names.Add(AddLabel(root.transform, "Title", "PAUSED", 36, Color.white, TextAnchor.MiddleCenter,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(420, 56)).name);

            var stack = AddRect(root.transform, "Buttons");
            Place(stack, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(220, 176));
            var layout = stack.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 16;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            names.Add(stack.name);
            names.Add(AddButton(stack, "Resume", "Resume").name);
            names.Add(AddButton(stack, "Settings", "Settings").name);
            names.Add(AddButton(stack, "Quit", "Quit").name);
            return Finish(root, names, ReadBool(args, "savePrefab", true));
        }

        public static Dictionary<string, object> List(Dictionary<string, object> args)
        {
            var folder = MiniJson.GetString(args, "folder", OUT_DIR);
            if (string.IsNullOrWhiteSpace(folder)) folder = OUT_DIR;
            folder = folder.Replace('\\', '/').TrimEnd('/');
            var paths = new List<object>();
            if (AssetDatabase.IsValidFolder(folder))
            {
                var guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
                for (var i = 0; i < guids.Length; i++)
                    paths.Add(AssetDatabase.GUIDToAssetPath(guids[i]));
            }
            return new Dictionary<string, object>
            {
                { "folder", folder },
                { "prefabs", paths },
            };
        }

        static Dictionary<string, object> Finish(GameObject root, List<object> names, bool savePrefab)
        {
            var prefab = "";
            if (savePrefab)
                prefab = SavePrefab(root);
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(root.transform) },
                { "elements", names.Count },
                { "names", names },
                { "prefab", prefab },
            };
        }

        static string SavePrefab(GameObject root)
        {
            EnsureFolder(OUT_DIR);
            var safe = root.name;
            foreach (var c in Path.GetInvalidFileNameChars())
                safe = safe.Replace(c.ToString(), "");
            if (string.IsNullOrEmpty(safe)) safe = "UI";
            var assetPath = OUT_DIR + "/" + safe + ".prefab";
            var saved = PrefabUtility.SaveAsPrefabAsset(root, assetPath, out var ok);
            if (!ok || saved == null)
                throw new AgentException("INTERNAL", "保存 prefab 失败: " + assetPath);
            AssetDatabase.SaveAssets();
            return assetPath;
        }

        static void EnsureFolder(string assetDir)
        {
            if (AssetDatabase.IsValidFolder(assetDir)) return;
            var parts = assetDir.Split('/');
            var cur = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }

        static GameObject MakeCanvas(string name)
        {
            var go = new GameObject(name);
            var canvas = go.AddComponent<Canvas>();
            // HUD 用 Overlay：不依赖具体相机，Game 视图/截图都能稳定显示。
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            var scaler = go.AddComponent<CanvasScaler>();
            // HUD 用 1:1 像素坐标，避免参考分辨率缩放把带锚点的元素推出屏幕。
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            go.AddComponent<GraphicRaycaster>();
            if (UnityEngine.Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem");
                es.AddComponent<EventSystem>();
                es.AddComponent<StandaloneInputModule>();
            }
            return go;
        }

        static GameObject AddImage(Transform parent, string name, Color color, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            var rt = AddRect(parent, name);
            Place(rt, aMin, aMax, pivot, pos, size);
            var img = rt.gameObject.AddComponent<Image>();
            // 和 ui.panel(sprite:none) 一样走纯色 Simple 路径，避免内置 Sprite 缺失时画不出来。
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.color = color;
            img.raycastTarget = false;
            return rt.gameObject;
        }

        static GameObject AddLabel(Transform parent, string name, string text, int size, Color color, TextAnchor align, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 box)
        {
            var rt = AddRect(parent, name);
            Place(rt, aMin, aMax, pivot, pos, box);
            PutText(rt.gameObject, text, size, color, align);
            return rt.gameObject;
        }

        static GameObject AddButton(Transform parent, string name, string label)
        {
            var rt = AddRect(parent, name);
            var le = rt.gameObject.AddComponent<LayoutElement>();
            le.preferredWidth = 220;
            le.preferredHeight = 48;
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = null;
            img.type = Image.Type.Simple;
            img.color = new Color(0.18f, 0.22f, 0.3f, 1f);
            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var labelRt = AddRect(rt, "Label");
            labelRt.anchorMin = Vector2.zero;
            labelRt.anchorMax = Vector2.one;
            labelRt.offsetMin = Vector2.zero;
            labelRt.offsetMax = Vector2.zero;
            PutText(labelRt.gameObject, label, 20, Color.white, TextAnchor.MiddleCenter);
            return rt.gameObject;
        }

        static void PutText(GameObject go, string text, int size, Color color, TextAnchor align)
        {
            if (TmpOk())
            {
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.text = text;
                tmp.fontSize = size;
                tmp.color = color;
                tmp.alignment = align == TextAnchor.MiddleRight ? TextAlignmentOptions.MidlineRight
                    : align == TextAnchor.MiddleCenter ? TextAlignmentOptions.Center
                    : TextAlignmentOptions.MidlineLeft;
                tmp.raycastTarget = false;
                return;
            }
            var ui = go.AddComponent<Text>();
            ui.text = text;
            ui.fontSize = size;
            ui.color = color;
            ui.alignment = align;
            ui.raycastTarget = false;
            ui.font = BuiltinFont();
        }

        static bool TmpOk()
        {
            try { return TMP_Settings.defaultFontAsset != null; }
            catch { return false; }
        }

        static Font BuiltinFont()
        {
            try
            {
                var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (f != null) return f;
            }
            catch { /* ignore */ }
            try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { return null; }
        }

        static Sprite UiSprite()
        {
            try { return AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"); }
            catch { return null; }
        }

        static RectTransform AddRect(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.AddComponent<RectTransform>();
        }

        static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
        }

        static bool ReadBool(Dictionary<string, object> args, string key, bool fallback)
        {
            if (args == null || !args.ContainsKey(key)) return fallback;
            return MiniJson.GetBool(args, key, fallback);
        }

        static int ReadInt(Dictionary<string, object> args, string key, int fallback)
        {
            if (args == null || !args.TryGetValue(key, out var v) || v == null) return fallback;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is double d) return (int)d;
            return MiniJson.GetInt(args, key, fallback);
        }
    }
}
