using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>
    /// 桥侧操作台账：每次写命令执行前后对目标对象拍快照、算 diff、记一条。
    ///
    /// 为什么放在桥上而不是 Session 里：Session 只记得"模型看到的工具调用"，
    /// 换个会话就断了；桥记得的是"Unity 真正被动了什么"，跨会话、跨客户端都在。
    /// </summary>
    public static class AgentOpLog
    {
        public const int Capacity = 200;

        static readonly List<Dictionary<string, object>> Entries = new List<Dictionary<string, object>>();
        static readonly object Gate = new object();
        static long _seq;

        /// <summary>一个 GameObject 在某一刻的可比较状态。</summary>
        public sealed class Snap
        {
            public string name;
            public string path;
            public Vector3 pos;
            public Vector3 rot;
            public Vector3 scale;
            public bool active;
            public int components;
        }

        /// <summary>只读命令不改东西，不进台账。</summary>
        public static bool IsWriteCommand(string cmd)
        {
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

        /// <summary>按命令参数找目标对象；找不到返回 null（不算错误）。</summary>
        public static Snap Capture(Dictionary<string, object> args)
        {
            if (args == null) return null;
            try
            {
                GameObject go = null;
                var name = MiniJson.GetString(args, "name", null);
                if (string.IsNullOrEmpty(name)) name = MiniJson.GetString(args, "target", null);
                if (!string.IsNullOrEmpty(name))
                {
                    go = GameObject.Find(name);
                }
                // go.transform / go.set 这类命令只给 path（可含层级），走桥自己的查找。
                if (go == null)
                {
                    var path = MiniJson.GetString(args, "path", null);
                    if (!string.IsNullOrEmpty(path))
                    {
                        var found = AgentCommands.FindTransformByPath(path);
                        if (found != null) go = found.gameObject;
                    }
                }
                if (go == null)
                {
                    var id = MiniJson.GetInt(args, "instanceId", 0);
                    if (id != 0)
                    {
                        var found = EditorUtility.InstanceIDToObject(id) as GameObject;
                        if (found != null) go = found;
                    }
                }
                if (go == null) return null;

                var t = go.transform;
                return new Snap
                {
                    name = go.name,
                    path = PathOf(t),
                    pos = t.position,
                    rot = t.eulerAngles,
                    scale = t.localScale,
                    active = go.activeSelf,
                    components = go.GetComponents<Component>().Length,
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把前后快照压成一行人能读的差异。</summary>
        public static string Diff(Snap before, Snap after)
        {
            if (before == null && after == null) return "";
            if (before == null && after != null) return "created " + after.name;
            if (before != null && after == null) return "destroyed " + before.name;

            var parts = new List<string>();
            if (!Approx(before.pos, after.pos)) parts.Add("position " + V(before.pos) + " → " + V(after.pos));
            if (!Approx(before.rot, after.rot)) parts.Add("rotation " + V(before.rot) + " → " + V(after.rot));
            if (!Approx(before.scale, after.scale)) parts.Add("scale " + V(before.scale) + " → " + V(after.scale));
            if (before.active != after.active) parts.Add("activeSelf " + before.active + " → " + after.active);
            if (before.components != after.components) parts.Add("components " + before.components + " → " + after.components);
            return string.Join("; ", parts);
        }

        static bool Approx(Vector3 a, Vector3 b)
        {
            return Mathf.Abs(a.x - b.x) < 0.0005f
                && Mathf.Abs(a.y - b.y) < 0.0005f
                && Mathf.Abs(a.z - b.z) < 0.0005f;
        }

        static string V(Vector3 v)
        {
            return "(" + Round(v.x) + ", " + Round(v.y) + ", " + Round(v.z) + ")";
        }

        static string Round(float value)
        {
            return Math.Round(value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static string PathOf(Transform t)
        {
            var sb = new StringBuilder(t.name);
            var cursor = t.parent;
            while (cursor != null)
            {
                sb.Insert(0, cursor.name + "/");
                cursor = cursor.parent;
            }
            return sb.ToString();
        }

        /// <summary>记一条；容量满了丢最旧的。</summary>
        public static void Record(string cmd, string target, bool ok, long ms, string diff)
        {
            lock (Gate)
            {
                _seq += 1;
                Entries.Add(new Dictionary<string, object>
                {
                    { "seq", _seq },
                    { "time", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() },
                    { "cmd", cmd ?? "" },
                    { "target", target ?? "" },
                    { "ok", ok },
                    { "ms", ms },
                    { "diff", diff ?? "" },
                });
                while (Entries.Count > Capacity) Entries.RemoveAt(0);
            }
        }

        /// <summary>最近 n 条（最新在前）。</summary>
        public static List<object> Recent(int limit)
        {
            lock (Gate)
            {
                var count = limit <= 0 ? 50 : Math.Min(limit, Entries.Count);
                var result = new List<object>(count);
                for (var i = Entries.Count - 1; i >= 0 && result.Count < count; i--)
                {
                    result.Add(Entries[i]);
                }
                return result;
            }
        }

        public static int Count
        {
            get { lock (Gate) return Entries.Count; }
        }
    }

    /// <summary>
    /// 用户侧的总闸：Unity 菜单里一键暂停 DSH 的写操作。
    /// 解决"抢 Unity"——用户可以先把 DSH 摁住，自己改完再放开。
    /// </summary>
    public static class AgentGuard
    {
        const string PauseKey = "UnityAgent.PauseWrites";
        const string MenuPath = "Window/DSH Agent/暂停 DSH 写操作";

        public static bool WritesPaused
        {
            get { return EditorPrefs.GetBool(PauseKey, false); }
            set
            {
                EditorPrefs.SetBool(PauseKey, value);
                Debug.Log("[UnityAgent] DSH 写操作已" + (value ? "暂停" : "恢复"));
                AgentHud.Show(value ? "DSH ▸ 写操作已暂停（菜单可恢复）" : "DSH ▸ 写操作已恢复", 3.0);
            }
        }

        [MenuItem(MenuPath)]
        static void Toggle()
        {
            WritesPaused = !WritesPaused;
        }

        [MenuItem(MenuPath, true)]
        static bool ToggleValidate()
        {
            Menu.SetChecked(MenuPath, WritesPaused);
            return true;
        }
    }
}
