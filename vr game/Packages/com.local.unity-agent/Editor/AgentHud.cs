using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// DSH 操作提示：Scene 视图横幅 + 目标对象高亮。
namespace Local.UnityAgent
{
    /// <summary>
    /// 在 Scene 视图左上角飘一行提示，显示 DSH 正在执行什么命令。
    ///
    /// 目的：DSH 通过桥操作编辑器时，用户能立刻知道"这次是它动的"，而不是
    /// 看到场景里的对象自己跳了还以为见鬼。只读命令（ping / 查询 / 轮询）全部
    /// 过滤掉，否则 compile.wait 每 500ms 一次会刷屏。
    ///
    /// 不依赖 Play 模式：走 SceneView.duringSceneGui，编辑状态下就能显示。
    /// </summary>
    [InitializeOnLoad]
    public static class AgentHud
    {
        const double DefaultSeconds = 3.5;

        static string _text = "";
        static double _until;
        static GUIStyle _style;

        static AgentHud()
        {
            SceneView.duringSceneGui += OnSceneGui;
        }

        /// <summary>命令进入桥时调用；只读命令会被静默过滤。</summary>
        public static void Note(string cmd, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(cmd)) return;
            if (!ShouldShow(cmd)) return;
            Show("DSH ▸ " + Describe(cmd, args));
        }

        /// <summary>
        /// 命令执行完后高亮目标对象，让用户在 Hierarchy / Project 里看到"刚动的是它"。
        /// 只对写操作生效；对象还不存在（比如刚 create 失败）时静默跳过。
        /// </summary>
        public static void Ping(string cmd, Dictionary<string, object> args)
        {
            if (string.IsNullOrEmpty(cmd) || args == null) return;
            if (!ShouldShow(cmd)) return;
            try
            {
                var name = MiniJson.GetString(args, "name", null);
                if (!string.IsNullOrEmpty(name))
                {
                    var found = GameObject.Find(name);
                    if (found != null)
                    {
                        EditorGUIUtility.PingObject(found);
                        return;
                    }
                }

                var path = MiniJson.GetString(args, "path", null);
                if (!string.IsNullOrEmpty(path)
                    && path.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    if (asset != null) EditorGUIUtility.PingObject(asset);
                }
            }
            catch
            {
                // 高亮失败不应该影响命令结果。
            }
        }

        /// <summary>只读或高频命令不提示——它们不代表"DSH 在动手"。</summary>
        static bool ShouldShow(string cmd)        {
            switch (cmd)
            {
                case "ping":
                case "status":
                case "compile.wait":
                case "console.tail":
                case "console.clear":
                case "scene.hierarchy":
                case "go.find":
                case "component.get":
                case "asset.find":
                case "screenshot":
                case "play.status":
                case "test.status":
                case "script.read":
                case "script.validate":
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>挑一个最有辨识度的参数当副标题。</summary>
        static string Describe(string cmd, Dictionary<string, object> args)
        {
            string detail = null;
            if (args != null)
            {
                detail = MiniJson.GetString(args, "name", null)
                    ?? MiniJson.GetString(args, "path", null)
                    ?? MiniJson.GetString(args, "type", null)
                    ?? MiniJson.GetString(args, "id", null);
            }
            return string.IsNullOrEmpty(detail) ? cmd : cmd + "  " + detail;
        }

        public static void Show(string text, double seconds = DefaultSeconds)
        {
            _text = text ?? "";
            _until = EditorApplication.timeSinceStartup + seconds;
            // duringSceneGui 只在 Scene 视图重绘时回调；不主动刷一次，
            // 用户没动鼠标时横幅永远不会出现（实测踩过）。
            SceneView.RepaintAll();
        }

        static void OnSceneGui(SceneView view)
        {
            if (string.IsNullOrEmpty(_text)) return;
            if (EditorApplication.timeSinceStartup > _until)
            {
                _text = "";
                return;
            }

            var width = Mathf.Min(620f, Mathf.Max(240f, view.position.width - 24f));
            var rect = new Rect(12f, view.position.height - 52f, width, 26f);

            Handles.BeginGUI();
            var previous = GUI.color;
            GUI.color = new Color(0.07f, 0.08f, 0.10f, 0.85f);
            GUI.DrawTexture(rect, EditorGUIUtility.whiteTexture);
            GUI.color = previous;

            if (_style == null)
            {
                _style = new GUIStyle(EditorStyles.boldLabel);
                _style.normal.textColor = new Color(0.85f, 0.92f, 1f, 1f);
                _style.clipping = TextClipping.Clip;
            }
            GUI.Label(new Rect(rect.x + 10f, rect.y + 4f, rect.width - 20f, 18f), _text, _style);
            Handles.EndGUI();

            // 过期前持续重绘，保证淡出时机准确（只在有提示时跑）。
            view.Repaint();
        }
    }
}
