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
    /// <summary>127.0.0.1 TCP + NDJSON。接受连接在后台线程；命令入主线程队列。</summary>
    public sealed class AgentServer
    {
        readonly string _token;
        readonly Action<string, string, Dictionary<string, object>, string, int, Action<Dictionary<string, object>>> _enqueue;
        TcpListener _listener;
        Thread _acceptThread;
        volatile bool _running;

        public int Port { get; private set; }
        public bool IsListening => _running;

        public AgentServer(
            string token,
            Action<string, string, Dictionary<string, object>, string, int, Action<Dictionary<string, object>>> enqueue)
        {
            _token = token;
            _enqueue = enqueue;
        }

        public void Start()
        {
            if (_running) return;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _running = true;
            _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "UnityAgent.Accept" };
            _acceptThread.Start();
        }

        public void Stop()
        {
            _running = false;
            try { _listener?.Stop(); } catch { /* ignore */ }
            _listener = null;
            try
            {
                if (_acceptThread != null && _acceptThread.IsAlive)
                    _acceptThread.Join(500);
            }
            catch { /* ignore */ }
            _acceptThread = null;
        }

        void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    var client = _listener.AcceptTcpClient();
                    client.NoDelay = true;
                    var t = new Thread(() => HandleClient(client))
                    {
                        IsBackground = true,
                        Name = "UnityAgent.Client",
                    };
                    t.Start();
                }
                catch (SocketException)
                {
                    if (!_running) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (_running)
                        Debug.LogWarning("[UnityAgent] accept: " + ex.Message);
                }
            }
        }

        void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var stream = client.GetStream())
                using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
                {
                    while (_running && client.Connected)
                    {
                        string line;
                        try { line = reader.ReadLine(); }
                        catch { break; }
                        if (line == null) break;
                        if (line.Length == 0) continue;

                        if (!AgentProtocol.TryParseRequest(line, out var id, out var cmd, out var args, out var token, out var timeoutMs, out var perr))
                        {
                            WriteLine(writer, AgentProtocol.Fail(id ?? "", "BAD_REQUEST", perr ?? "bad request"));
                            continue;
                        }

                        var done = new ManualResetEventSlim(false);
                        Dictionary<string, object> response = null;
                        _enqueue(id, cmd, args, token, timeoutMs, r =>
                        {
                            response = r;
                            done.Set();
                        });

                        // 等主线程；额外缓冲 2s
                        var waitMs = Math.Max(1000, timeoutMs + 2000);
                        if (!done.Wait(waitMs))
                        {
                            response = AgentProtocol.Fail(id, "TIMEOUT", "main-thread command timed out after " + waitMs + "ms");
                        }
                        WriteLine(writer, response ?? AgentProtocol.Fail(id, "INTERNAL", "empty response"));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityAgent] client: " + ex.Message);
            }
        }

        static void WriteLine(StreamWriter writer, Dictionary<string, object> obj)
        {
            try
            {
                writer.WriteLine(MiniJson.Stringify(obj));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[UnityAgent] write: " + ex.Message);
            }
        }
    }
}
