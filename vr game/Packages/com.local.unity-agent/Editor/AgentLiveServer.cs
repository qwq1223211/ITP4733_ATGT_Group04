using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>
    /// 浏览器能直连的只读画面端点：GET /live.png 返回当前场景/游戏视图的 PNG。
    ///
    /// 为什么要有它：桥本身是裸 TCP + NDJSON，浏览器没法 `&lt;img src&gt;`。
    /// 这里用固定端口的极简 HTTP 服务补一个"镜子"，只接受 GET、只返回图片，
    /// 不暴露任何写操作，也不复用主桥的 token 通道。
    ///
    /// 截图必须在主线程做，所以请求会经 AgentHost 的串行队列执行。
    /// 只渲染相机画面（不含编辑器 GUI），这正好是"看 DSH 在场景里改了什么"要的视角。
    /// </summary>
    public sealed class AgentLiveServer
    {
        /// <summary>固定端口：client 侧写死同一个值，避免再去读 server.json。</summary>
        public const int DefaultPort = 17891;

        readonly TcpListener _listener;
        Thread _thread;
        volatile bool _running;

        public int Port { get; }
        public bool IsListening => _running;

        public AgentLiveServer(int port)
        {
            Port = port;
            _listener = new TcpListener(IPAddress.Loopback, port);
        }

        public void Start()
        {
            if (_running) return;
            try
            {
                _listener.Start();
                _running = true;
                _thread = new Thread(Loop) { IsBackground = true, Name = "UnityAgent.Live" };
                _thread.Start();
                Debug.Log("[UnityAgent] live view http://127.0.0.1:" + Port + "/live.png");
            }
            catch (Exception ex)
            {
                // 端口被占用不算致命：主桥继续工作，页签里显示"画面不可用"即可。
                Debug.LogWarning("[UnityAgent] live view unavailable on port " + Port + ": " + ex.Message);
                _running = false;
            }
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            try
            {
                if (_thread != null && _thread.IsAlive) _thread.Join(500);
            }
            catch { /* ignore */ }
            _thread = null;
        }

        void Loop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (!_running) break;
                    continue;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch
                {
                    if (!_running) break;
                    continue;
                }

                ThreadPool.QueueUserWorkItem(_ => Handle(client));
            }
        }

        void Handle(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                    var requestLine = reader.ReadLine();
                    if (string.IsNullOrEmpty(requestLine)) return;

                    // 读掉请求头直到空行（我们只用第一行）。
                    for (var guard = 0; guard < 100; guard++)
                    {
                        var header = reader.ReadLine();
                        if (string.IsNullOrEmpty(header)) break;
                    }

                    var parts = requestLine.Split(' ');
                    if (parts.Length < 2 || !string.Equals(parts[0], "GET", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteStatus(stream, "405 Method Not Allowed");
                        return;
                    }

                    var target = parts[1];

                    // 只读的编辑器状态 JSON：场景、playMode、编译状态、版本、选中对象。
                    // 浏览器 fetch 跨端口访问，必须带 CORS 头。
                    if (target.StartsWith("/status", StringComparison.OrdinalIgnoreCase))
                    {
                        var json = AgentHost.CaptureStatusJson();
                        if (json == null)
                        {
                            WriteStatus(stream, "503 Service Unavailable");
                            return;
                        }
                        var jsonBytes = Encoding.UTF8.GetBytes(json);
                        var jsonHead = "HTTP/1.1 200 OK\r\n"
                            + "Content-Type: application/json; charset=utf-8\r\n"
                            + "Content-Length: " + jsonBytes.Length + "\r\n"
                            + "Cache-Control: no-store\r\n"
                            + "Access-Control-Allow-Origin: *\r\n"
                            + "Connection: close\r\n\r\n";
                        var jsonHeadBytes = Encoding.ASCII.GetBytes(jsonHead);
                        stream.Write(jsonHeadBytes, 0, jsonHeadBytes.Length);
                        stream.Write(jsonBytes, 0, jsonBytes.Length);
                        stream.Flush();
                        return;
                    }

                    // 工程仓库的改动清单（git status），给"改了哪些文件"用。
                    if (target.StartsWith("/changes", StringComparison.OrdinalIgnoreCase))
                    {
                        var changesJson = AgentHost.CaptureChangesJson();
                        if (changesJson == null)
                        {
                            WriteStatus(stream, "503 Service Unavailable");
                            return;
                        }
                        var changesBytes = Encoding.UTF8.GetBytes(changesJson);
                        var changesHead = "HTTP/1.1 200 OK\r\n"
                            + "Content-Type: application/json; charset=utf-8\r\n"
                            + "Content-Length: " + changesBytes.Length + "\r\n"
                            + "Cache-Control: no-store\r\n"
                            + "Access-Control-Allow-Origin: *\r\n"
                            + "Connection: close\r\n\r\n";
                        var changesHeadBytes = Encoding.ASCII.GetBytes(changesHead);
                        stream.Write(changesHeadBytes, 0, changesHeadBytes.Length);
                        stream.Write(changesBytes, 0, changesBytes.Length);
                        stream.Flush();
                        return;
                    }

                    // 桥侧操作台账：跨会话、跨客户端，不依赖任何 Session。
                    if (target.StartsWith("/ops", StringComparison.OrdinalIgnoreCase))
                    {
                        var opsQuery = ParseQuery(target);
                        var limit = ParseInt(opsQuery, "limit", 50);
                        var recent = AgentOpLog.Recent(limit);
                        var payload = new Dictionary<string, object>
                        {
                            { "ok", true },
                            { "count", recent.Count },
                            { "ops", recent },
                        };
                        var opsBytes = Encoding.UTF8.GetBytes(MiniJson.Stringify(payload));
                        var opsHead = "HTTP/1.1 200 OK\r\n"
                            + "Content-Type: application/json; charset=utf-8\r\n"
                            + "Content-Length: " + opsBytes.Length + "\r\n"
                            + "Cache-Control: no-store\r\n"
                            + "Access-Control-Allow-Origin: *\r\n"
                            + "Connection: close\r\n\r\n";
                        var opsHeadBytes = Encoding.ASCII.GetBytes(opsHead);
                        stream.Write(opsHeadBytes, 0, opsHeadBytes.Length);
                        stream.Write(opsBytes, 0, opsBytes.Length);
                        stream.Flush();
                        return;
                    }

                    if (!target.StartsWith("/live.png", StringComparison.OrdinalIgnoreCase)
                        && !target.StartsWith("/live", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteStatus(stream, "404 Not Found");
                        return;
                    }

                    var query = ParseQuery(target);
                    var view = query.TryGetValue("view", out var viewValue) ? viewValue : "scene";
                    var width = ParseInt(query, "w", 960);
                    var height = ParseInt(query, "h", 540);

                    var png = AgentHost.CaptureLivePng(view, width, height);
                    if (png == null || png.Length == 0)
                    {
                        WriteStatus(stream, "503 Service Unavailable");
                        return;
                    }

                    var head = "HTTP/1.1 200 OK\r\n"
                        + "Content-Type: image/png\r\n"
                        + "Content-Length: " + png.Length + "\r\n"
                        + "Cache-Control: no-store, no-cache, must-revalidate\r\n"
                        + "Connection: close\r\n\r\n";
                    var headBytes = Encoding.ASCII.GetBytes(head);
                    stream.Write(headBytes, 0, headBytes.Length);
                    stream.Write(png, 0, png.Length);
                    stream.Flush();
                }
            }
            catch
            {
                // 单次请求失败不影响服务；浏览器那边会显示占位图。
            }
        }

        static int ParseInt(Dictionary<string, string> query, string key, int fallback)
        {
            if (!query.TryGetValue(key, out var raw)) return fallback;
            return int.TryParse(raw, out var value) && value > 0 && value <= 4096 ? value : fallback;
        }

        static Dictionary<string, string> ParseQuery(string target)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var question = target.IndexOf('?');
            if (question < 0 || question == target.Length - 1) return result;
            var query = target.Substring(question + 1);
            foreach (var pair in query.Split('&'))
            {
                if (pair.Length == 0) continue;
                var eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                result[Uri.UnescapeDataString(pair.Substring(0, eq))] = Uri.UnescapeDataString(pair.Substring(eq + 1));
            }
            return result;
        }

        static void WriteStatus(Stream stream, string status)
        {
            var body = Encoding.ASCII.GetBytes(status);
            var head = "HTTP/1.1 " + status + "\r\n"
                + "Content-Type: text/plain; charset=utf-8\r\n"
                + "Content-Length: " + body.Length + "\r\n"
                + "Connection: close\r\n\r\n";
            var headBytes = Encoding.ASCII.GetBytes(head);
            stream.Write(headBytes, 0, headBytes.Length);
            stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }
}
