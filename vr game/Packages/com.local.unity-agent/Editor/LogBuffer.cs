using System;
using System.Collections.Generic;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>环形缓冲：捕获 Console 日志供 console.tail / compile.wait。</summary>
    public static class LogBuffer
    {
        const int Capacity = 500;

        struct Entry
        {
            public string type;
            public string message;
            public string stackTrace;
            public string time;
        }

        static readonly object Gate = new object();
        static readonly Entry[] Ring = new Entry[Capacity];
        static int _write;
        static int _count;
        static bool _hooked;

        public static int ErrorCount;
        public static int WarningCount;
        public static int LogCount;

        public static void EnsureHooked()
        {
            if (_hooked) return;
            Application.logMessageReceivedThreaded += OnLog;
            _hooked = true;
        }

        public static void Unhook()
        {
            if (!_hooked) return;
            Application.logMessageReceivedThreaded -= OnLog;
            _hooked = false;
        }

        static void OnLog(string message, string stackTrace, LogType type)
        {
            var e = new Entry
            {
                type = MapType(type),
                message = message ?? "",
                stackTrace = stackTrace ?? "",
                time = DateTime.UtcNow.ToString("o"),
            };
            lock (Gate)
            {
                Ring[_write % Capacity] = e;
                _write++;
                if (_count < Capacity) _count++;
                switch (type)
                {
                    case LogType.Error:
                    case LogType.Assert:
                    case LogType.Exception:
                        ErrorCount++;
                        break;
                    case LogType.Warning:
                        WarningCount++;
                        break;
                    default:
                        LogCount++;
                        break;
                }
            }
        }

        static string MapType(LogType t)
        {
            switch (t)
            {
                case LogType.Error:
                case LogType.Assert:
                case LogType.Exception:
                    return "error";
                case LogType.Warning:
                    return "warning";
                default:
                    return "log";
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                _write = 0;
                _count = 0;
                ErrorCount = 0;
                WarningCount = 0;
                LogCount = 0;
                Array.Clear(Ring, 0, Ring.Length);
            }
        }

        public static Dictionary<string, object> CountsDict()
        {
            lock (Gate)
            {
                return new Dictionary<string, object>
                {
                    { "error", ErrorCount },
                    { "warning", WarningCount },
                    { "log", LogCount },
                };
            }
        }

        public static List<object> Tail(int count, HashSet<string> types)
        {
            if (count <= 0) count = 50;
            if (count > Capacity) count = Capacity;
            var result = new List<object>();
            lock (Gate)
            {
                var gathered = new List<Entry>();
                for (var i = 0; i < _count; i++)
                {
                    var idx = (_write - _count + i + Capacity * 8) % Capacity;
                    var e = Ring[idx];
                    if (types != null && types.Count > 0 && !types.Contains(e.type)) continue;
                    gathered.Add(e);
                }
                var start = Math.Max(0, gathered.Count - count);
                for (var i = start; i < gathered.Count; i++)
                {
                    var e = gathered[i];
                    result.Add(new Dictionary<string, object>
                    {
                        { "type", e.type },
                        { "message", e.message },
                        { "stackTrace", e.stackTrace },
                        { "time", e.time },
                    });
                }
            }
            return result;
        }

        public static List<object> RecentErrorsAndWarnings(int max = 100)
        {
            return Tail(max, new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "error", "warning" });
        }
    }
}
