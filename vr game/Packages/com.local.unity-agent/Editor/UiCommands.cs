using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Local.UnityAgent
{
    /// <summary>uGUI + TMP UI 命令包（ui.*）。</summary>
    public static class UiCommands
    {
        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (string.IsNullOrEmpty(cmd) || !cmd.StartsWith("ui.", StringComparison.Ordinal))
                return false;

            switch (cmd)
            {
                case "ui.canvas": data = CmdCanvas(args); return true;
                case "ui.panel": data = CmdPanel(args); return true;
                case "ui.text": data = CmdText(args); return true;
                case "ui.button": data = CmdButton(args); return true;
                case "ui.image": data = CmdImage(args); return true;
                case "ui.slider": data = CmdSlider(args); return true;
                case "ui.toggle": data = CmdToggle(args); return true;
                case "ui.scrollview": data = CmdScrollView(args); return true;
                case "ui.rect": data = CmdRect(args); return true;
                case "ui.layout": data = CmdLayout(args); return true;
                case "ui.tree": data = CmdTree(args); return true;
                case "ui.validate": data = CmdValidate(args); return true;
                case "ui.tokens": data = CmdTokens(); return true;
                case "ui.fonts": data = CmdFonts(args); return true;
                default:
                    throw new AgentException("UNKNOWN_COMMAND", "unknown ui cmd: " + cmd);
            }
        }

        // ---------- commands ----------

        static Dictionary<string, object> CmdCanvas(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", "Canvas");
            var mode = MiniJson.GetString(args, "mode", "overlay");
            var match = (float)MiniJson.GetDouble(args, "match", 0.5);
            var pixelPerfect = MiniJson.GetBool(args, "pixelPerfect", false);
            var sortingOrder = MiniJson.GetInt(args, "sortingOrder", 0);
            var refRes = ReadVec2(args, "referenceResolution", new Vector2(1920, 1080));

            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "ui");
            var canvas = go.AddComponent<Canvas>();
            if (string.Equals(mode, "camera", StringComparison.OrdinalIgnoreCase))
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
            else if (string.Equals(mode, "world", StringComparison.OrdinalIgnoreCase))
                canvas.renderMode = RenderMode.WorldSpace;
            else
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            canvas.pixelPerfect = pixelPerfect;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = refRes;
            scaler.matchWidthOrHeight = Mathf.Clamp01(match);
            go.AddComponent<GraphicRaycaster>();

            EnsureEventSystem();
            MarkDirty(go);
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(go.transform) },
                { "name", go.name },
                { "instanceId", go.GetInstanceID() },
                {
                    "referenceResolution", new List<object> { refRes.x, refRes.y }
                },
            };
        }

        static Dictionary<string, object> CmdPanel(Dictionary<string, object> args)
        {
            var name = RequireName(args);
            var go = CreateUiObject(name, args);
            var img = go.AddComponent<Image>();
            var spriteKey = MiniJson.GetString(args, "sprite", "rounded");
            ApplyImageSprite(img, spriteKey, MiniJson.GetString(args, "type", null));
            if (args != null && args.ContainsKey("color"))
                img.color = ParseColor(args["color"], img.color);
            else if (spriteKey == "none" || string.IsNullOrEmpty(spriteKey))
                img.color = new Color(1, 1, 1, 0.35f);

            MarkDirty(go);
            return IdResult(go);
        }

        static Dictionary<string, object> CmdText(Dictionary<string, object> args)
        {
            var name = RequireName(args);
            var text = MiniJson.GetString(args, "text", "");
            var fontSize = (float)MiniJson.GetDouble(args, "fontSize", 24);
            var bold = MiniJson.GetBool(args, "bold", false);
            var align = MiniJson.GetString(args, "align", "left");
            var color = args != null && args.ContainsKey("color")
                ? ParseColor(args["color"], Color.white)
                : Color.white;

            var go = CreateUiObject(name, args);
            string backend;
            if (TmpAvailable())
            {
                var tmp = go.AddComponent<TextMeshProUGUI>();
                tmp.text = text;
                tmp.fontSize = fontSize;
                tmp.color = color;
                tmp.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
                tmp.alignment = MapTmpAlign(align);
                tmp.raycastTarget = false;
                ApplyFontPath(tmp, null, args);
                backend = "tmp";
            }
            else
            {
                var uiText = go.AddComponent<Text>();
                uiText.text = text;
                uiText.fontSize = Mathf.RoundToInt(fontSize);
                uiText.color = color;
                uiText.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
                uiText.alignment = MapUguiAlign(align);
                uiText.horizontalOverflow = HorizontalWrapMode.Wrap;
                uiText.verticalOverflow = VerticalWrapMode.Truncate;
                uiText.raycastTarget = false;
                uiText.font = LoadBuiltinFont();
                ApplyFontPath(null, uiText, args);
                backend = "ugui";
            }

            MarkDirty(go);
            var result = IdResult(go);
            result["backend"] = backend;
            return result;
        }

        static Dictionary<string, object> CmdButton(Dictionary<string, object> args)
        {
            var name = RequireName(args);
            var label = MiniJson.GetString(args, "label", "Button");
            var fontSize = (float)MiniJson.GetDouble(args, "fontSize", 18);
            var bg = args != null && args.ContainsKey("color")
                ? ParseColor(args["color"], new Color(0.18f, 0.45f, 0.85f, 1f))
                : new Color(0.18f, 0.45f, 0.85f, 1f);
            var textColor = args != null && args.ContainsKey("textColor")
                ? ParseColor(args["textColor"], Color.white)
                : Color.white;

            // default size for buttons if omitted
            EnsureDefaultSize(args, 160, 44);

            var go = CreateUiObject(name, args);
            var img = go.AddComponent<Image>();
            ApplyImageSprite(img, "rounded", "sliced");
            img.color = bg;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.colors = ColorBlock.defaultColorBlock;

            var textGo = new GameObject("Label");
            Undo.RegisterCreatedObjectUndo(textGo, "ui");
            textGo.transform.SetParent(go.transform, false);
            var tr = textGo.AddComponent<RectTransform>();
            StretchWithPadding(tr, 12);

            if (TmpAvailable())
            {
                var tmp = textGo.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                tmp.fontSize = fontSize;
                tmp.color = textColor;
                tmp.alignment = TextAlignmentOptions.Center;
                tmp.raycastTarget = false;
                ApplyFontPath(tmp, null, args);
            }
            else
            {
                var uiText = textGo.AddComponent<Text>();
                uiText.text = label;
                uiText.fontSize = Mathf.RoundToInt(fontSize);
                uiText.color = textColor;
                uiText.alignment = TextAnchor.MiddleCenter;
                uiText.raycastTarget = false;
                uiText.font = LoadBuiltinFont();
                ApplyFontPath(null, uiText, args);
            }

            MarkDirty(go);
            return IdResult(go);
        }

        static Dictionary<string, object> CmdImage(Dictionary<string, object> args)
        {
            var name = RequireName(args);
            var go = CreateUiObject(name, args);
            var img = go.AddComponent<Image>();
            var spritePath = MiniJson.GetString(args, "spritePath", null);
            if (!string.IsNullOrEmpty(spritePath))
            {
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
                if (sp == null)
                    throw new AgentException("NOT_FOUND", "sprite not found: " + spritePath);
                img.sprite = sp;
            }
            var type = MiniJson.GetString(args, "type", "simple");
            img.type = ParseImageType(type);
            if (args != null && args.ContainsKey("color"))
                img.color = ParseColor(args["color"], Color.white);
            img.preserveAspect = MiniJson.GetBool(args, "preserveAspect", false);
            MarkDirty(go);
            return IdResult(go);
        }

        static Dictionary<string, object> CmdSlider(Dictionary<string, object> args)
        {
            var name = RequireName(args);
            EnsureDefaultSize(args, 200, 20);
            var go = CreateUiObject(name, args);
            var slider = go.AddComponent<Slider>();
            slider.minValue = (float)MiniJson.GetDouble(args, "min", 0);
            slider.maxValue = (float)MiniJson.GetDouble(args, "max", 1);
            slider.value = (float)MiniJson.GetDouble(args, "value", 0.5);

            var bg = CreateChild(go.transform, "Background");
            Stretch(bg);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = BuiltinSprite("UI/Skin/Background.psd");
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(0.2f, 0.2f, 0.2f, 1f);

            var fillArea = CreateChild(go.transform, "Fill Area");
            StretchWithPadding(fillArea, 5, 0, 5, 0);
            var fill = CreateChild(fillArea, "Fill");
            var fillRt = fill;
            fillRt.sizeDelta = new Vector2(10, 0);
            var fillImg = fill.gameObject.AddComponent<Image>();
            fillImg.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            fillImg.type = Image.Type.Sliced;
            fillImg.color = new Color(0.18f, 0.45f, 0.85f, 1f);

            var handleArea = CreateChild(go.transform, "Handle Slide Area");
            Stretch(handleArea);
            var handle = CreateChild(handleArea, "Handle");
            handle.sizeDelta = new Vector2(20, 0);
            var handleImg = handle.gameObject.AddComponent<Image>();
            handleImg.sprite = BuiltinSprite("UI/Skin/Knob.psd");

            slider.fillRect = fill;
            slider.handleRect = handle;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;

            MarkDirty(go);
            return IdResult(go);
        }

        static Dictionary<string, object> CmdToggle(Dictionary<string, object> args)
        {
            var name = RequireName(args);
            var label = MiniJson.GetString(args, "label", "Toggle");
            var isOn = MiniJson.GetBool(args, "isOn", true);
            EnsureDefaultSize(args, 160, 24);

            var go = CreateUiObject(name, args);
            var toggle = go.AddComponent<Toggle>();
            toggle.isOn = isOn;

            var bg = CreateChild(go.transform, "Background");
            bg.anchorMin = new Vector2(0, 0.5f);
            bg.anchorMax = new Vector2(0, 0.5f);
            bg.pivot = new Vector2(0.5f, 0.5f);
            bg.anchoredPosition = new Vector2(12, 0);
            bg.sizeDelta = new Vector2(20, 20);
            var bgImg = bg.gameObject.AddComponent<Image>();
            bgImg.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
            bgImg.type = Image.Type.Sliced;
            bgImg.color = Color.white;

            var check = CreateChild(bg, "Checkmark");
            Stretch(check);
            var checkImg = check.gameObject.AddComponent<Image>();
            checkImg.sprite = BuiltinSprite("UI/Skin/Checkmark.psd");

            var labelGo = CreateChild(go.transform, "Label");
            labelGo.anchorMin = new Vector2(0, 0);
            labelGo.anchorMax = new Vector2(1, 1);
            labelGo.offsetMin = new Vector2(28, 0);
            labelGo.offsetMax = new Vector2(0, 0);

            if (TmpAvailable())
            {
                var tmp = labelGo.gameObject.AddComponent<TextMeshProUGUI>();
                tmp.text = label;
                tmp.fontSize = 16;
                tmp.color = Color.white;
                tmp.alignment = TextAlignmentOptions.MidlineLeft;
                tmp.raycastTarget = false;
            }
            else
            {
                var uiText = labelGo.gameObject.AddComponent<Text>();
                uiText.text = label;
                uiText.fontSize = 16;
                uiText.color = Color.white;
                uiText.alignment = TextAnchor.MiddleLeft;
                uiText.raycastTarget = false;
                uiText.font = LoadBuiltinFont();
            }

            toggle.graphic = checkImg;
            toggle.targetGraphic = bgImg;

            MarkDirty(go);
            return IdResult(go);
        }

        static Dictionary<string, object> CmdScrollView(Dictionary<string, object> args)
        {
            var name = RequireName(args);
            var axis = MiniJson.GetString(args, "axis", "vertical");
            EnsureDefaultSize(args, 320, 240);

            var go = CreateUiObject(name, args);
            var scroll = go.AddComponent<ScrollRect>();
            var bgImg = go.AddComponent<Image>();
            bgImg.sprite = BuiltinSprite("UI/Skin/Background.psd");
            bgImg.type = Image.Type.Sliced;
            bgImg.color = new Color(1, 1, 1, 0.15f);

            var viewport = CreateChild(go.transform, "Viewport");
            Stretch(viewport);
            viewport.gameObject.AddComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = CreateChild(viewport, "Content");
            content.anchorMin = new Vector2(0, 1);
            content.anchorMax = new Vector2(1, 1);
            content.pivot = new Vector2(0.5f, 1);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0, 0);

            var vlg = content.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.spacing = 8;
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = string.Equals(axis, "horizontal", StringComparison.OrdinalIgnoreCase);
            scroll.vertical = !scroll.horizontal || string.Equals(axis, "vertical", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(axis, "vertical", StringComparison.OrdinalIgnoreCase))
            {
                scroll.horizontal = false;
                scroll.vertical = true;
            }

            MarkDirty(go);
            var result = IdResult(go);
            result["contentPath"] = AgentCommands.GetHierarchyPath(content);
            return result;
        }

        static Dictionary<string, object> CmdRect(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            if (string.IsNullOrEmpty(path))
                throw new AgentException("BAD_ARGS", "path required");
            var t = AgentCommands.FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            var rt = t as RectTransform ?? t.GetComponent<RectTransform>();
            if (rt == null) throw new AgentException("BAD_ARGS", "not a RectTransform: " + path);

            if (args.ContainsKey("anchorMin")) rt.anchorMin = ReadVec2Value(args["anchorMin"], rt.anchorMin);
            if (args.ContainsKey("anchorMax")) rt.anchorMax = ReadVec2Value(args["anchorMax"], rt.anchorMax);
            if (args.ContainsKey("pivot")) rt.pivot = ReadVec2Value(args["pivot"], rt.pivot);
            if (args.ContainsKey("anchoredPosition"))
                rt.anchoredPosition = ReadVec2Value(args["anchoredPosition"], rt.anchoredPosition);
            if (args.ContainsKey("sizeDelta"))
                rt.sizeDelta = ReadVec2Value(args["sizeDelta"], rt.sizeDelta);
            if (args.ContainsKey("offsetMin"))
                rt.offsetMin = ReadVec2Value(args["offsetMin"], rt.offsetMin);
            if (args.ContainsKey("offsetMax"))
                rt.offsetMax = ReadVec2Value(args["offsetMax"], rt.offsetMax);

            MarkDirty(rt.gameObject);
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(rt) },
                { "rect", RectDict(rt) },
            };
        }

        static Dictionary<string, object> CmdLayout(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            var type = MiniJson.GetString(args, "type", null);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(type))
                throw new AgentException("BAD_ARGS", "path and type required");
            var t = AgentCommands.FindTransformByPath(path);
            if (t == null) throw new AgentException("NOT_FOUND", "not found: " + path);
            var go = t.gameObject;

            // remove existing layout groups
            foreach (var lg in go.GetComponents<LayoutGroup>())
                Object.DestroyImmediate(lg);

            var spacing = (float)MiniJson.GetDouble(args, "spacing", 8);
            RectOffset padding = null;
            if (args != null && args.TryGetValue("padding", out var padObj))
                padding = ReadPadding(padObj);

            if (string.Equals(type, "vertical", StringComparison.OrdinalIgnoreCase))
            {
                var v = go.AddComponent<VerticalLayoutGroup>();
                v.spacing = spacing;
                if (padding != null) v.padding = padding;
                ApplyChildControls(v, args);
            }
            else if (string.Equals(type, "horizontal", StringComparison.OrdinalIgnoreCase))
            {
                var h = go.AddComponent<HorizontalLayoutGroup>();
                h.spacing = spacing;
                if (padding != null) h.padding = padding;
                ApplyChildControls(h, args);
            }
            else if (string.Equals(type, "grid", StringComparison.OrdinalIgnoreCase))
            {
                var g = go.AddComponent<GridLayoutGroup>();
                if (padding != null) g.padding = padding;
                if (args != null && args.ContainsKey("cellSize"))
                    g.cellSize = ReadVec2Value(args["cellSize"], g.cellSize);
                if (args != null && args.ContainsKey("cellSpacing"))
                    g.spacing = ReadVec2Value(args["cellSpacing"], g.spacing);
                else
                    g.spacing = new Vector2(spacing, spacing);
            }
            else
            {
                throw new AgentException("BAD_ARGS", "type must be vertical|horizontal|grid");
            }

            MarkDirty(go);
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(t) },
                { "type", type.ToLowerInvariant() },
            };
        }

        static object CmdTree(Dictionary<string, object> args)
        {
            var rootPath = MiniJson.GetString(args, "root", null);
            Transform root = null;
            if (!string.IsNullOrEmpty(rootPath))
            {
                root = AgentCommands.FindTransformByPath(rootPath);
                if (root == null) throw new AgentException("NOT_FOUND", "root not found: " + rootPath);
            }
            else
            {
                var canvas = Object.FindObjectOfType<Canvas>();
                if (canvas == null) return new List<object>();
                root = canvas.transform;
            }

            return BuildTreeNode(root);
        }

        static Dictionary<string, object> CmdValidate(Dictionary<string, object> args)
        {
            var rootPath = MiniJson.GetString(args, "root", null);
            Transform root = null;
            if (!string.IsNullOrEmpty(rootPath))
            {
                root = AgentCommands.FindTransformByPath(rootPath);
                if (root == null) throw new AgentException("NOT_FOUND", "root not found: " + rootPath);
            }
            else
            {
                var canvas = Object.FindObjectOfType<Canvas>();
                if (canvas == null)
                    return new Dictionary<string, object> { { "issues", new List<object>() }, { "count", 0 } };
                root = canvas.transform;
            }

            var issues = new List<object>();
            var canvasRt = root.GetComponentInParent<Canvas>()?.GetComponent<RectTransform>();
            ValidateWalk(root, issues, canvasRt);
            return new Dictionary<string, object>
            {
                { "issues", issues },
                { "count", issues.Count },
            };
        }

        static Dictionary<string, object> CmdFonts(Dictionary<string, object> args)
        {
            var filter = MiniJson.GetString(args, "filter", null);
            var limit = MiniJson.GetInt(args, "limit", 30);
            if (limit <= 0) limit = 30;

            var list = new List<object>();
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(assetPath);
                var cjk = false;
                try
                {
                    if (fa != null) cjk = fa.HasCharacter('中');
                }
                catch
                {
                    cjk = false;
                }
                TryAddFont(list, assetPath, "tmp", cjk, filter);
            }

            foreach (var guid in AssetDatabase.FindAssets("t:Font"))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath)) continue;
                TryAddFont(list, assetPath, "font", false, filter);
            }

            list.Sort((a, b) => string.Compare(
                MiniJson.GetString(a as Dictionary<string, object>, "path", ""),
                MiniJson.GetString(b as Dictionary<string, object>, "path", ""),
                StringComparison.OrdinalIgnoreCase));
            if (list.Count > limit)
                list = list.GetRange(0, limit);

            return new Dictionary<string, object> { { "fonts", list } };
        }

        static void TryAddFont(List<object> list, string assetPath, string type, bool cjk, string filter)
        {
            var name = Path.GetFileNameWithoutExtension(assetPath);
            if (!string.IsNullOrEmpty(filter))
            {
                var hit = assetPath.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                if (!hit) return;
            }
            list.Add(new Dictionary<string, object>
            {
                { "path", assetPath },
                { "name", name },
                { "type", type },
                { "cjk", cjk },
            });
        }

        static void ApplyFontPath(TMP_Text tmp, Text uiText, Dictionary<string, object> args)
        {
            var fontPath = MiniJson.GetString(args, "fontPath", null);
            if (string.IsNullOrEmpty(fontPath)) return;
            fontPath = fontPath.Replace('\\', '/');
            if (tmp != null)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
                if (asset == null)
                    throw new AgentException("NOT_FOUND", "TMP font not found: " + fontPath);
                tmp.font = asset;
                return;
            }
            if (uiText != null)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                if (font == null)
                    throw new AgentException("NOT_FOUND", "font not found: " + fontPath);
                uiText.font = font;
            }
        }

        static Dictionary<string, object> CmdTokens()
        {
            return new Dictionary<string, object>
            {
                {
                    "palette", new Dictionary<string, object>
                    {
                        { "bg", "#0F1419" },
                        { "surface", "#1A2332" },
                        { "primary", "#3B82F6" },
                        { "text", "#F1F5F9" },
                        { "muted", "#94A3B8" },
                        { "danger", "#EF4444" },
                        { "success", "#22C55E" },
                    }
                },
                { "spacing", new List<object> { 4, 8, 12, 16, 24, 32 } },
                { "fontSize", new List<object> { 12, 14, 16, 20, 28 } },
                { "radius", 8 },
                { "buttonHeight", 44 },
            };
        }

        // ---------- helpers ----------

        static string RequireName(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", null);
            if (string.IsNullOrEmpty(name))
                throw new AgentException("BAD_ARGS", "name required");
            return name;
        }

        static void EnsureDefaultSize(Dictionary<string, object> args, float w, float h)
        {
            if (args == null) return;
            if (!args.ContainsKey("w")) args["w"] = w;
            if (!args.ContainsKey("h")) args["h"] = h;
        }

        static GameObject CreateUiObject(string name, Dictionary<string, object> args)
        {
            var parent = ResolveParent(args);
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "ui");
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            ApplyPresetAndBox(rt, args);
            return go;
        }

        static RectTransform ResolveParent(Dictionary<string, object> args)
        {
            var parentPath = MiniJson.GetString(args, "parent", null);
            if (!string.IsNullOrEmpty(parentPath))
            {
                var t = AgentCommands.FindTransformByPath(parentPath);
                if (t == null) throw new AgentException("NOT_FOUND", "parent not found: " + parentPath);
                var rt = t as RectTransform ?? t.GetComponent<RectTransform>();
                if (rt == null)
                    throw new AgentException("BAD_ARGS", "parent is not RectTransform: " + parentPath);
                return rt;
            }

            var canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
                throw new AgentException("NOT_FOUND", "no Canvas in scene; call ui.canvas first");
            return canvas.GetComponent<RectTransform>();
        }

        static void ApplyPresetAndBox(RectTransform rt, Dictionary<string, object> args)
        {
            var preset = MiniJson.GetString(args, "preset", "center");
            ApplyPreset(rt, preset);

            var hasW = args != null && args.ContainsKey("w");
            var hasH = args != null && args.ContainsKey("h");
            var hasX = args != null && args.ContainsKey("x");
            var hasY = args != null && args.ContainsKey("y");
            if (hasW || hasH)
            {
                var sd = rt.sizeDelta;
                if (hasW) sd.x = (float)MiniJson.GetDouble(args, "w", sd.x);
                if (hasH) sd.y = (float)MiniJson.GetDouble(args, "h", sd.y);
                rt.sizeDelta = sd;
            }
            if (hasX || hasY)
            {
                var ap = rt.anchoredPosition;
                if (hasX) ap.x = (float)MiniJson.GetDouble(args, "x", ap.x);
                if (hasY) ap.y = (float)MiniJson.GetDouble(args, "y", ap.y);
                rt.anchoredPosition = ap;
            }
        }

        static void ApplyPreset(RectTransform rt, string preset)
        {
            Vector2 min, max, pivot;
            switch ((preset ?? "center").ToLowerInvariant())
            {
                case "stretch":
                    min = Vector2.zero; max = Vector2.one; pivot = new Vector2(0.5f, 0.5f); break;
                case "top-left":
                    min = max = pivot = new Vector2(0, 1); break;
                case "top-center":
                    min = max = pivot = new Vector2(0.5f, 1); break;
                case "top-right":
                    min = max = pivot = new Vector2(1, 1); break;
                case "bottom-left":
                    min = max = pivot = new Vector2(0, 0); break;
                case "bottom-center":
                    min = max = pivot = new Vector2(0.5f, 0); break;
                case "bottom-right":
                    min = max = pivot = new Vector2(1, 0); break;
                case "middle-left":
                    min = max = pivot = new Vector2(0, 0.5f); break;
                case "middle-right":
                    min = max = pivot = new Vector2(1, 0.5f); break;
                case "center":
                default:
                    min = max = pivot = new Vector2(0.5f, 0.5f); break;
            }
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = pivot;
            if (preset != null && preset.Equals("stretch", StringComparison.OrdinalIgnoreCase))
            {
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        static RectTransform CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "ui");
            var rt = go.AddComponent<RectTransform>();
            go.transform.SetParent(parent, false);
            return rt;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        static void StretchWithPadding(RectTransform rt, float pad)
        {
            StretchWithPadding(rt, pad, pad, pad, pad);
        }

        static void StretchWithPadding(RectTransform rt, float left, float top, float right, float bottom)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        static void ApplyImageSprite(Image img, string spriteKey, string type)
        {
            if (string.IsNullOrEmpty(spriteKey) || spriteKey == "none")
            {
                img.sprite = null;
                img.type = Image.Type.Simple;
                return;
            }
            if (spriteKey == "rounded")
            {
                img.sprite = BuiltinSprite("UI/Skin/UISprite.psd");
                img.type = ParseImageType(string.IsNullOrEmpty(type) ? "sliced" : type);
                return;
            }
            // asset path
            var sp = AssetDatabase.LoadAssetAtPath<Sprite>(spriteKey);
            if (sp == null)
                sp = BuiltinSprite(spriteKey);
            img.sprite = sp;
            img.type = ParseImageType(string.IsNullOrEmpty(type) ? "simple" : type);
        }

        static Image.Type ParseImageType(string type)
        {
            if (string.Equals(type, "sliced", StringComparison.OrdinalIgnoreCase)) return Image.Type.Sliced;
            if (string.Equals(type, "tiled", StringComparison.OrdinalIgnoreCase)) return Image.Type.Tiled;
            if (string.Equals(type, "filled", StringComparison.OrdinalIgnoreCase)) return Image.Type.Filled;
            return Image.Type.Simple;
        }

        static Sprite BuiltinSprite(string path)
        {
            try
            {
                return AssetDatabase.GetBuiltinExtraResource<Sprite>(path);
            }
            catch
            {
                return null;
            }
        }

        static bool TmpAvailable()
        {
            try
            {
                return TMP_Settings.defaultFontAsset != null;
            }
            catch
            {
                return false;
            }
        }

        static Font LoadBuiltinFont()
        {
            try
            {
                var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (f != null) return f;
            }
            catch { /* ignore */ }
            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch
            {
                return null;
            }
        }

        static TextAlignmentOptions MapTmpAlign(string align)
        {
            switch ((align ?? "left").ToLowerInvariant())
            {
                case "center": return TextAlignmentOptions.Center;
                case "right": return TextAlignmentOptions.Right;
                default: return TextAlignmentOptions.Left;
            }
        }

        static TextAnchor MapUguiAlign(string align)
        {
            switch ((align ?? "left").ToLowerInvariant())
            {
                case "center": return TextAnchor.MiddleCenter;
                case "right": return TextAnchor.MiddleRight;
                default: return TextAnchor.MiddleLeft;
            }
        }

        static void ApplyChildControls(HorizontalOrVerticalLayoutGroup g, Dictionary<string, object> args)
        {
            if (args == null) return;
            if (args.ContainsKey("childControlWidth"))
                g.childControlWidth = MiniJson.GetBool(args, "childControlWidth", g.childControlWidth);
            if (args.ContainsKey("childControlHeight"))
                g.childControlHeight = MiniJson.GetBool(args, "childControlHeight", g.childControlHeight);
            if (args.ContainsKey("childForceExpandWidth"))
                g.childForceExpandWidth = MiniJson.GetBool(args, "childForceExpandWidth", g.childForceExpandWidth);
            if (args.ContainsKey("childForceExpandHeight"))
                g.childForceExpandHeight = MiniJson.GetBool(args, "childForceExpandHeight", g.childForceExpandHeight);
        }

        static RectOffset ReadPadding(object padObj)
        {
            if (padObj is List<object> list && list.Count >= 4)
            {
                return new RectOffset(
                    Convert.ToInt32(list[0]),
                    Convert.ToInt32(list[2]),
                    Convert.ToInt32(list[1]),
                    Convert.ToInt32(list[3]));
            }
            return new RectOffset();
        }

        static Vector2 ReadVec2(Dictionary<string, object> args, string key, Vector2 fallback)
        {
            if (args == null || !args.TryGetValue(key, out var v) || v == null) return fallback;
            return ReadVec2Value(v, fallback);
        }

        static Vector2 ReadVec2Value(object v, Vector2 fallback)
        {
            if (v is List<object> list && list.Count >= 2)
                return new Vector2(Convert.ToSingle(list[0]), Convert.ToSingle(list[1]));
            if (v is Dictionary<string, object> d)
                return new Vector2((float)MiniJson.GetDouble(d, "x", fallback.x), (float)MiniJson.GetDouble(d, "y", fallback.y));
            return fallback;
        }

        public static Color ParseColor(object v, Color fallback)
        {
            if (v == null) return fallback;
            if (v is string s)
            {
                s = s.Trim();
                if (s.StartsWith("#", StringComparison.Ordinal))
                {
                    s = s.Substring(1);
                    if (s.Length == 6 || s.Length == 8)
                    {
                        var r = int.Parse(s.Substring(0, 2), NumberStyles.HexNumber);
                        var g = int.Parse(s.Substring(2, 2), NumberStyles.HexNumber);
                        var b = int.Parse(s.Substring(4, 2), NumberStyles.HexNumber);
                        var a = s.Length == 8
                            ? int.Parse(s.Substring(6, 2), NumberStyles.HexNumber)
                            : 255;
                        return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                    }
                }
                return fallback;
            }
            if (v is List<object> list && list.Count >= 3)
            {
                var r = Convert.ToSingle(list[0]);
                var g = Convert.ToSingle(list[1]);
                var b = Convert.ToSingle(list[2]);
                var a = list.Count >= 4 ? Convert.ToSingle(list[3]) : 1f;
                return new Color(r, g, b, a);
            }
            if (v is Dictionary<string, object> d)
            {
                return new Color(
                    (float)MiniJson.GetDouble(d, "r", fallback.r),
                    (float)MiniJson.GetDouble(d, "g", fallback.g),
                    (float)MiniJson.GetDouble(d, "b", fallback.b),
                    (float)MiniJson.GetDouble(d, "a", fallback.a));
            }
            return fallback;
        }

        static Dictionary<string, object> IdResult(GameObject go)
        {
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(go.transform) },
                { "name", go.name },
                { "instanceId", go.GetInstanceID() },
            };
        }

        static Dictionary<string, object> RectDict(RectTransform rt)
        {
            return new Dictionary<string, object>
            {
                { "anchorMin", Vec2List(rt.anchorMin) },
                { "anchorMax", Vec2List(rt.anchorMax) },
                { "pivot", Vec2List(rt.pivot) },
                { "anchoredPosition", Vec2List(rt.anchoredPosition) },
                { "sizeDelta", Vec2List(rt.sizeDelta) },
                { "offsetMin", Vec2List(rt.offsetMin) },
                { "offsetMax", Vec2List(rt.offsetMax) },
            };
        }

        static List<object> Vec2List(Vector2 v)
        {
            return new List<object> { v.x, v.y };
        }

        static Dictionary<string, object> BuildTreeNode(Transform t)
        {
            var rt = t as RectTransform ?? t.GetComponent<RectTransform>();
            var node = new Dictionary<string, object>
            {
                { "name", t.name },
                { "path", AgentCommands.GetHierarchyPath(t) },
                { "active", t.gameObject.activeSelf },
            };
            if (rt != null) node["rect"] = RectDict(rt);

            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                node["text"] = tmp.text;
                node["fontSize"] = tmp.fontSize;
            }
            else
            {
                var uiText = t.GetComponent<Text>();
                if (uiText != null)
                {
                    node["text"] = uiText.text;
                    node["fontSize"] = uiText.fontSize;
                }
            }

            var img = t.GetComponent<Image>();
            if (img != null)
            {
                var c = img.color;
                node["imageColor"] = new List<object> { c.r, c.g, c.b, c.a };
            }

            if (t.childCount > 0)
            {
                var kids = new List<object>();
                for (var i = 0; i < t.childCount; i++)
                    kids.Add(BuildTreeNode(t.GetChild(i)));
                node["children"] = kids;
            }
            return node;
        }

        static void ValidateWalk(Transform t, List<object> issues, RectTransform canvasRt)
        {
            var path = AgentCommands.GetHierarchyPath(t);
            var rt = t as RectTransform ?? t.GetComponent<RectTransform>();
            if (rt != null)
            {
                var size = rt.rect.size;
                if (size.x <= 0 || size.y <= 0)
                    AddIssue(issues, "zero-size", path, "w/h <= 0");

                if (canvasRt != null)
                {
                    var corners = new Vector3[4];
                    rt.GetWorldCorners(corners);
                    var canvasCorners = new Vector3[4];
                    canvasRt.GetWorldCorners(canvasCorners);
                    var min = canvasCorners[0];
                    var max = canvasCorners[2];
                    var tol = 2f;
                    var anyInside = false;
                    for (var i = 0; i < 4; i++)
                    {
                        var p = corners[i];
                        if (p.x >= min.x - tol && p.x <= max.x + tol &&
                            p.y >= min.y - tol && p.y <= max.y + tol)
                            anyInside = true;
                    }
                    // offscreen if ALL corners outside
                    var allOutside = true;
                    for (var i = 0; i < 4; i++)
                    {
                        var p = corners[i];
                        if (p.x >= min.x - tol && p.x <= max.x + tol &&
                            p.y >= min.y - tol && p.y <= max.y + tol)
                        {
                            allOutside = false;
                            break;
                        }
                    }
                    if (allOutside && !anyInside)
                        AddIssue(issues, "offscreen", path, "rect corners outside canvas");
                }
            }

            var btn = t.GetComponent<Button>();
            if (btn != null)
            {
                if (btn.targetGraphic == null)
                    AddIssue(issues, "missing-target", path, "Button has no targetGraphic");
                if (rt != null)
                {
                    var h = rt.rect.height;
                    var w = rt.rect.width;
                    if (h < 44 || w < 88)
                        AddIssue(issues, "small-target", path, "Button height < 44 or width < 88");
                }
            }

            float? fontSize = null;
            string textVal = null;
            Color? textColor = null;
            var tmp = t.GetComponent<TMP_Text>();
            if (tmp != null)
            {
                fontSize = tmp.fontSize;
                textVal = tmp.text;
                textColor = tmp.color;
            }
            else
            {
                var uiText = t.GetComponent<Text>();
                if (uiText != null)
                {
                    fontSize = uiText.fontSize;
                    textVal = uiText.text;
                    textColor = uiText.color;
                }
            }
            if (fontSize.HasValue && fontSize.Value < 12)
                AddIssue(issues, "tiny-text", path, "fontSize < 12");
            if (textVal != null && string.IsNullOrEmpty(textVal))
                AddIssue(issues, "empty-text", path, "text is empty");

            if (textColor.HasValue)
            {
                var parentImg = t.parent != null ? t.parent.GetComponent<Image>() : null;
                if (parentImg == null && t.parent != null)
                    parentImg = t.parent.GetComponentInParent<Image>();
                // prefer immediate parent Image for contrast (panel behind label)
                Image bg = null;
                if (t.parent != null)
                {
                    bg = t.parent.GetComponent<Image>();
                    if (bg == null) bg = t.GetComponentInParent<Image>();
                    // if self has image and is the only one, skip
                    if (bg != null && bg.transform == t) bg = null;
                }
                if (bg != null)
                {
                    var ratio = ContrastRatio(textColor.Value, bg.color);
                    if (ratio < 3f)
                        AddIssue(issues, "low-contrast", path, "contrast " + ratio.ToString("0.00", CultureInfo.InvariantCulture) + " < 3");
                }
            }

            // duplicate names under same parent
            if (t.parent != null)
            {
                var count = 0;
                for (var i = 0; i < t.parent.childCount; i++)
                {
                    if (t.parent.GetChild(i).name == t.name) count++;
                }
                if (count > 1)
                    AddIssue(issues, "duplicate-name", path, "duplicate name under parent");
            }

            for (var i = 0; i < t.childCount; i++)
                ValidateWalk(t.GetChild(i), issues, canvasRt);
        }

        static void AddIssue(List<object> issues, string code, string path, string message)
        {
            issues.Add(new Dictionary<string, object>
            {
                { "code", code },
                { "path", path },
                { "message", message },
            });
        }

        static float ContrastRatio(Color a, Color b)
        {
            var l1 = RelativeLuminance(a);
            var l2 = RelativeLuminance(b);
            var lighter = Math.Max(l1, l2);
            var darker = Math.Min(l1, l2);
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        static float RelativeLuminance(Color c)
        {
            float Chan(float v)
            {
                return v <= 0.03928f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);
            }
            return 0.2126f * Chan(c.r) + 0.7152f * Chan(c.g) + 0.0722f * Chan(c.b);
        }

        static void EnsureEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null) return;
            var es = new GameObject("EventSystem");
            Undo.RegisterCreatedObjectUndo(es, "ui");
            es.AddComponent<EventSystem>();
            es.AddComponent<StandaloneInputModule>();
            MarkDirty(es);
        }

        static void MarkDirty(GameObject go)
        {
            if (go != null)
                EditorSceneManager.MarkSceneDirty(go.scene);
        }
    }
}
