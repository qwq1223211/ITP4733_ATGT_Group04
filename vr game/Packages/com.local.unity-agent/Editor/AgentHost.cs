using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Local.UnityAgent
{
    /// <summary>
    /// [InitializeOnLoad] 入口：非批处理启动 TCP；主线程串行队列。
    /// </summary>
    [InitializeOnLoad]
    public static class AgentHost
    {
        public const int ProtocolVersion = 1;
        public const int DefaultTimeoutMs = 30000;

        static readonly object QueueGate = new object();
        static readonly Queue<WorkItem> Queue = new Queue<WorkItem>();
        static bool _busy;
        static bool _started;
        static AgentServer _server;
        static AgentLiveServer _live;
        static double _lastStartAttempt;

        // 发现文件（server.json）自愈节流：外部工具靠它找端口，丢了就等于桥不可见
        static double _lastJsonCheck;
        static string _changesCache;
        static double _changesAt;
        static readonly object ChangesGate = new object();

        public static string Token { get; private set; }
        public static int Port => _server != null ? _server.Port : 0;
        public static bool IsListening => _server != null && _server.IsListening;
        public static DateTime StartedAtUtc { get; private set; }

        struct WorkItem
        {
            public string id;
            public string cmd;
            public Dictionary<string, object> args;
            public int timeoutMs;
            public Action<Dictionary<string, object>> complete;
        }

        static AgentHost()
        {
            LogBuffer.EnsureHooked();
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            EditorApplication.quitting += OnQuitting;
            EditorApplication.update += OnUpdate;

            if (Application.isBatchMode)
            {
                // 批处理：不监听、不写 server.json
                return;
            }

            EditorApplication.delayCall += StartServerSafe;
        }

        static void StartServerSafe()
        {
            if (_started || Application.isBatchMode) return;
            try
            {
                StartServer();
            }
            catch (Exception ex)
            {
                Debug.LogError("[UnityAgent] failed to start: " + ex);
            }
        }

        public static void StartServer()
        {
            if (_started) return;
            Token = CreateTokenHex(32);
            StartedAtUtc = DateTime.UtcNow;
            _server = new AgentServer(Token, EnqueueRequest);
            _server.Start();
            WriteServerJson();
            _started = true;
            StartLiveServer();
            Debug.Log("[UnityAgent] listening 127.0.0.1:" + _server.Port);
        }

        public static void StopServer()
        {
            try { _server?.Stop(); } catch { /* ignore */ }
            StopLiveServer();
            _server = null;
            _started = false;
            try
            {
                if (System.IO.File.Exists(PathUtil.ServerJsonPath))
                    System.IO.File.Delete(PathUtil.ServerJsonPath);
            }
            catch { /* ignore */ }
        }

        // ------------------------------------------------------------------
        // 实时画面镜：浏览器直连的只读 HTTP 端点（见 AgentLiveServer）
        // ------------------------------------------------------------------

        static void StartLiveServer()
        {
            if (_live != null && _live.IsListening) return;
            _live = new AgentLiveServer(AgentLiveServer.DefaultPort);
            _live.Start();
        }

        static void StopLiveServer()
        {
            try { _live?.Stop(); } catch { /* ignore */ }
            _live = null;
        }

        /// <summary>
        /// 在主线程截一张当前画面并返回 PNG 字节；供 HTTP 端点调用。
        /// 会覆盖写 Library/UnityAgent/live.png，不落历史文件。
        /// Play 模式优先用带 UI 的抓图（ScreenCapture 帧末写盘），失败再退回相机 RT。
        /// </summary>
        public static byte[] CaptureLivePng(string view, int width, int height)
        {
            var full = PathUtil.ResolveProjectRelative("Library/UnityAgent/live.png", true);

            // 1) 带 UI 的截图：命令只排队，文件在帧末出现，所以在 worker 线程上轮询
            try
            {
                if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
            }
            catch { }

            // 刚进 Play 的那一帧 HUD 还没画全，先让游戏跑几帧再抓
            System.Threading.Thread.Sleep(250);

            var guiArgs = new Dictionary<string, object>
            {
                { "gui", true },
                { "path", "Library/UnityAgent/live.png" },
            };

            var guiResult = ExecuteQueued("screenshot", guiArgs, 5000);
            if (guiResult != null &&
                !(guiResult.TryGetValue("ok", out var guiOk) && guiOk is bool guiOkValue && !guiOkValue))
            {
                for (int i = 0; i < 40; i++)
                {
                    System.Threading.Thread.Sleep(50);

                    try
                    {
                        if (!System.IO.File.Exists(full)) continue;
                        var bytes = System.IO.File.ReadAllBytes(full);
                        if (bytes.Length > 0) return bytes;
                    }
                    catch { }
                }
            }

            // 2) 回退：相机 RT（Edit 模式或 GUI 抓图失败）
            var args = new Dictionary<string, object>
            {
                { "view", string.IsNullOrEmpty(view) ? "scene" : view },
                { "width", width },
                { "height", height },
                { "path", "Library/UnityAgent/live.png" },
            };

            var result = ExecuteQueued("screenshot", args, 5000);
            if (result == null) return null;
            if (result.TryGetValue("ok", out var ok) && ok is bool okValue && !okValue) return null;

            try
            {
                return System.IO.File.Exists(full) ? System.IO.File.ReadAllBytes(full) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>工程仓库的改动清单（git status，5 秒缓存）；供 /changes 端点调用。</summary>
        public static string CaptureChangesJson()
        {
            lock (ChangesGate)
            {
                // HTTP 线程上不能碰 EditorApplication（Unity API 限主线程），用环境计时器。
                var now = DateTime.UtcNow.Ticks / 10000000.0;
                if (_changesCache != null && now - _changesAt < 5.0) return _changesCache;
                _changesAt = now;
                try
                {
                    var root = PathUtil.ProjectRoot;
                    var startInfo = new ProcessStartInfo("git", "-C \"" + root + "\" status --porcelain")
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = System.Text.Encoding.UTF8,
                    };
                    string output;
                    using (var process = Process.Start(startInfo))
                    {
                        output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit(4000);
                    }
                    _changesCache = MiniJson.Stringify(new Dictionary<string, object>
                    {
                        { "ok", true },
                        { "root", root },
                        { "files", ParseStatus(output) },
                    });
                }
                catch (Exception ex)
                {
                    _changesCache = MiniJson.Stringify(new Dictionary<string, object>
                    {
                        { "ok", false },
                        { "error", ex.Message },
                        { "files", new List<object>() },
                    });
                }
                return _changesCache;
            }
        }

        static List<object> ParseStatus(string output)
        {
            var files = new List<object>();
            if (string.IsNullOrEmpty(output)) return files;
            foreach (var raw in output.Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (line.Length < 4) continue;
                files.Add(new Dictionary<string, object>
                {
                    { "status", line.Substring(0, 2) },
                    { "path", line.Substring(3).Trim() },
                });
                if (files.Count >= 60) break;
            }
            return files;
        }

        /// <summary>在主线程读一次编辑器状态并序列化成 JSON；供 /status 端点调用。</summary>
        public static string CaptureStatusJson()
        {
            var result = ExecuteQueued("status", new Dictionary<string, object>(), 5000);
            if (result == null) return null;
            try
            {
                return MiniJson.Stringify(result);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>把命令塞进主线程串行队列并等结果（不经过网络/鉴权）。</summary>
        static Dictionary<string, object> ExecuteQueued(string cmd, Dictionary<string, object> args, int timeoutMs)
        {
            var done = new ManualResetEventSlim(false);
            Dictionary<string, object> result = null;
            try
            {
                lock (QueueGate)
                {
                    Queue.Enqueue(new WorkItem
                    {
                        id = "live",
                        cmd = cmd,
                        args = args ?? new Dictionary<string, object>(),
                        timeoutMs = timeoutMs,
                        complete = r =>
                        {
                            result = r;
                            done.Set();
                        },
                    });
                }
                done.Wait(Math.Max(1000, timeoutMs + 2000));
            }
            catch
            {
                return null;
            }
            finally
            {
                done.Dispose();
            }
            return result;
        }

        static void OnBeforeReload()
        {
            StopServer();
        }

        static void OnQuitting()
        {
            StopServer();
            LogBuffer.Unhook();
        }

        static string CreateTokenHex(int hexChars)
        {
            var bytes = new byte[Math.Max(1, hexChars / 2)];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(bytes);
            var sb = new System.Text.StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++)
                sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        public static void WriteServerJson()
        {
            if (_server == null) return;
            PathUtil.EnsureDir(PathUtil.AgentLibDir);
            var data = new Dictionary<string, object>
            {
                { "version", ProtocolVersion },
                { "port", _server.Port },
                { "token", Token },
                { "pid", Process.GetCurrentProcess().Id },
                { "projectPath", PathUtil.ProjectRoot },
                { "unityVersion", Application.unityVersion },
                { "startedAt", StartedAtUtc.ToString("o") },
            };
            System.IO.File.WriteAllText(PathUtil.ServerJsonPath, MiniJson.Stringify(data));
        }

        /// <summary>后台线程入队；主线程执行。</summary>
        public static void EnqueueRequest(
            string id,
            string cmd,
            Dictionary<string, object> args,
            string token,
            int timeoutMs,
            Action<Dictionary<string, object>> complete)
        {
            if (!string.Equals(token, Token, StringComparison.Ordinal))
            {
                complete(AgentProtocol.Fail(id, "AUTH_FAILED", "invalid token"));
                return;
            }

            if (timeoutMs <= 0) timeoutMs = DefaultTimeoutMs;

            lock (QueueGate)
            {
                if (_busy || Queue.Count > 0)
                {
                    // 仍入队串行；BUSY 仅在极端堆积时使用——v1 选择排队而非拒绝
                }
                Queue.Enqueue(new WorkItem
                {
                    id = id,
                    cmd = cmd,
                    args = args ?? new Dictionary<string, object>(),
                    timeoutMs = timeoutMs,
                    complete = complete,
                });
            }
        }

        static void OnUpdate()
        {
            // 【最小化自愈】域重载后靠 delayCall 启动桥，但编辑器最小化时主循环不跑，
            // delayCall 永远轮不到——桥就这么消失，只有恢复窗口才回来。
            // 这里在主循环恢复的第一时间补一次（失败也节流，避免刷日志）。
            if (!_started && !Application.isBatchMode)
            {
                var now = EditorApplication.timeSinceStartup;
                if (now - _lastStartAttempt > 2.0)
                {
                    _lastStartAttempt = now;
                    StartServerSafe();
                }
                return;
            }

            // 【发现文件自愈】beforeAssemblyReload 会删掉 server.json；若那次重载被取消，
            // 桥其实还在监听（_started 仍为 true），但文件再没人写回来，
            // 外部工具就永远报"编辑器可能没开"。按秒节流补写，不刷日志。
            if (_started && !Application.isBatchMode)
            {
                var jnow = EditorApplication.timeSinceStartup;
                if (jnow - _lastJsonCheck > 1.0)
                {
                    _lastJsonCheck = jnow;
                    try
                    {
                        if (!System.IO.File.Exists(PathUtil.ServerJsonPath)) WriteServerJson();
                    }
                    catch { /* ignore */ }
                }
            }

            WorkItem item;
            lock (QueueGate)
            {
                if (_busy || Queue.Count == 0) return;
                item = Queue.Dequeue();
                _busy = true;
            }

            Dictionary<string, object> response;
            var sw = Stopwatch.StartNew();
            var write = AgentOpLog.IsWriteCommand(item.cmd);

            // 用户按下了总闸：写操作直接拒绝，读操作照常放行。
            if (write && AgentGuard.WritesPaused)
            {
                lock (QueueGate) { _busy = false; }
                var paused = AgentProtocol.Fail(item.id, "PAUSED",
                    "DSH 写操作已暂停：Unity 菜单 Window/DSH Agent/暂停 DSH 写操作 可恢复");
                try { item.complete?.Invoke(paused); }
                catch (Exception ex) { Debug.LogError("[UnityAgent] complete failed: " + ex); }
                return;
            }

            // 执行前拍快照，执行后再拍一次算 diff（create 类命令之前对象不存在）。
            var beforeSnap = write ? AgentOpLog.Capture(item.args) : null;
            try
            {
                // 命令必须自行快速返回；这里用超时标记（无法中断 C#）
                response = AgentCommands.Execute(item.id, item.cmd, item.args, item.timeoutMs);
                if (sw.ElapsedMilliseconds > item.timeoutMs && response != null &&
                    response.TryGetValue("ok", out var okObj) && okObj is bool ok && ok)
                {
                    // 已成功则保留；超时保护主要靠命令内部
                }
            }
            catch (AgentException ae)
            {
                response = AgentProtocol.Fail(item.id, ae.Code, ae.Message, ae.Detail);
            }
            catch (Exception ex)
            {
                response = AgentProtocol.Fail(item.id, "INTERNAL", ex.Message);
            }
            finally
            {
                lock (QueueGate) { _busy = false; }
            }

            // 执行完再高亮目标：create 类命令此时对象才真正存在。
            AgentHud.Ping(item.cmd, item.args);

            // 记一条台账：目标名 + 前后 diff + 成败 + 耗时（跨会话，/ops 读它）。
            if (write)
            {
                var afterSnap = AgentOpLog.Capture(item.args);
                var targetName = afterSnap != null ? afterSnap.name
                    : beforeSnap != null ? beforeSnap.name : "";
                var succeeded = response != null
                    && response.TryGetValue("ok", out var okValue) && okValue is bool okBool && okBool;
                AgentOpLog.Record(
                    item.cmd,
                    targetName,
                    succeeded,
                    sw.ElapsedMilliseconds,
                    AgentOpLog.Diff(beforeSnap, afterSnap));
            }

            try { item.complete?.Invoke(response); }
            catch (Exception ex) { Debug.LogError("[UnityAgent] complete failed: " + ex); }
        }

        /// <summary>供 SelfTest 直接调用（不经网络）。</summary>
        public static Dictionary<string, object> ExecuteDirect(string cmd, Dictionary<string, object> args, int timeoutMs = DefaultTimeoutMs)
        {
            return AgentCommands.Execute("selftest", cmd, args ?? new Dictionary<string, object>(), timeoutMs);
        }
    }

    public static class AgentProtocol
    {
        public static Dictionary<string, object> Ok(string id, object data)
        {
            return new Dictionary<string, object>
            {
                { "id", id ?? "" },
                { "ok", true },
                { "data", data ?? new Dictionary<string, object>() },
            };
        }

        public static Dictionary<string, object> Fail(string id, string code, string message, object detail = null)
        {
            var err = new Dictionary<string, object>
            {
                { "code", code ?? "INTERNAL" },
                { "message", message ?? "" },
            };
            if (detail != null) err["detail"] = detail;
            return new Dictionary<string, object>
            {
                { "id", id ?? "" },
                { "ok", false },
                { "error", err },
            };
        }

        public static bool TryParseRequest(string line, out string id, out string cmd, out Dictionary<string, object> args, out string token, out int timeoutMs, out string error)
        {
            id = null; cmd = null; args = null; token = null; timeoutMs = AgentHost.DefaultTimeoutMs; error = null;
            try
            {
                var root = MiniJson.Parse(line) as Dictionary<string, object>;
                if (root == null) { error = "root must be object"; return false; }
                id = MiniJson.GetString(root, "id", "");
                cmd = MiniJson.GetString(root, "cmd", "");
                token = MiniJson.GetString(root, "token", "");
                timeoutMs = MiniJson.GetInt(root, "timeoutMs", AgentHost.DefaultTimeoutMs);
                if (root.TryGetValue("args", out var a) && a != null)
                {
                    args = a as Dictionary<string, object>;
                    if (args == null) { error = "args must be object"; return false; }
                }
                else args = new Dictionary<string, object>();
                if (string.IsNullOrEmpty(cmd)) { error = "cmd required"; return false; }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
