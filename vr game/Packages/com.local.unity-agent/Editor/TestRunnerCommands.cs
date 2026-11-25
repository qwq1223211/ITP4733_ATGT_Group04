using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>异步 Test Runner：test.run 立即返回，test.status 读累计结果。</summary>
    [InitializeOnLoad]
    public static class TestRunnerCommands
    {
        static TestRunnerCommands()
        {
            // 测试程序集编译会重载域；重新注册后才能收到随后的 TestFinished。
            if (Type.GetType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi, UnityEditor.TestRunner") != null)
                RunState.EnsureApi();
        }

        public static Dictionary<string, object> Run(Dictionary<string, object> args)
        {
            if (Type.GetType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi, UnityEditor.TestRunner") == null)
            {
                throw new AgentException("NOT_READY",
                    "test.run unavailable: com.unity.test-framework / TestRunnerApi not present in this project");
            }
            if (RunState.Running)
                throw new AgentException("BUSY", "a test run is already in progress");

            var mode = MiniJson.GetString(args, "mode", "edit");
            var play = string.Equals(mode, "play", StringComparison.OrdinalIgnoreCase);
            var nameFilter = MiniJson.GetString(args, "filter", null);

            RunState.Begin(play ? "play" : "edit");
            var api = RunState.EnsureApi();
            var testMode = play ? TestMode.PlayMode : TestMode.EditMode;

            Action start = () => StartRun(api, testMode, nameFilter);

            // Play 模式里不能直接跑测试（Test Framework 会报 "cannot be used during play mode"）：
            // 先退出 Play，等编译/刷新完再真正开始。
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                ScheduleStart(start);
            }
            else
            {
                start();
            }

            return new Dictionary<string, object>
            {
                { "started", true },
                { "mode", play ? "play" : "edit" },
                { "filter", nameFilter ?? "" },
                { "deferred", EditorApplication.isPlaying },
            };

        }

        static void StartRun(TestRunnerApi api, TestMode testMode, string nameFilter)
        {
            if (string.IsNullOrEmpty(nameFilter))
            {
                api.Execute(new ExecutionSettings(new Filter { testMode = testMode }));
                return;
            }

            // Filter.testNames 只认完整测试名：子串要先拿列表再展开，
            // 否则 "MapBuild" 这种过滤会一条都跑不到（之前就是这个坑）。
            api.RetrieveTestList(testMode, root =>
            {
                var names = new List<string>();
                CollectMatching(root, nameFilter, names);

                if (names.Count == 0)
                {
                    RunState.Fail("no tests match '" + nameFilter + "'");
                    return;
                }

                api.Execute(new ExecutionSettings(new Filter
                {
                    testMode = testMode,
                    testNames = names.ToArray(),
                }));
            });
        }

        static Action _pendingStart;

        static void ScheduleStart(Action start)
        {
            _pendingStart = start;
            EditorApplication.update -= PendingTick;
            EditorApplication.update += PendingTick;
        }

        static void PendingTick()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            EditorApplication.update -= PendingTick;

            var start = _pendingStart;
            _pendingStart = null;

            if (start != null)
                start();
        }

        static void CollectMatching(ITestAdaptor node, string filter, List<string> names)
        {
            if (node == null)
                return;

            if (node.HasChildren)
            {
                foreach (var child in node.Children)
                    CollectMatching(child, filter, names);
                return;
            }

            if (!string.IsNullOrEmpty(node.FullName) &&
                node.FullName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                names.Add(node.FullName);
        }

        public static Dictionary<string, object> Status()
        {
            if (Type.GetType("UnityEditor.TestTools.TestRunner.Api.TestRunnerApi, UnityEditor.TestRunner") == null)
            {
                throw new AgentException("NOT_READY",
                    "test.status unavailable: com.unity.test-framework / TestRunnerApi not present in this project");
            }
            return RunState.Snapshot();
        }

        sealed class RunState : ICallbacks
        {
            static readonly RunState Instance = new RunState();
            static TestRunnerApi _api;
            static bool _hooked;

            public static bool Running { get; private set; }

            static bool _completed;
            static int _passed;
            static int _failed;
            static int _skipped;
            static int _total;
            static string _startedAt = "";
            static string _mode = "";
            static string _note = "";

            public static void Fail(string note)
            {
                Running = false;
                _completed = true;
                _note = note;
            }

            public static TestRunnerApi EnsureApi()
            {
                if (_api == null)
                {
                    _api = ScriptableObject.CreateInstance<TestRunnerApi>();
                    _api.hideFlags = HideFlags.HideAndDontSave;
                }
                if (!_hooked)
                {
                    _api.RegisterCallbacks(Instance);
                    _hooked = true;
                }
                return _api;
            }

            public static void Begin(string mode)
            {
                Running = true;
                _completed = false;
                _passed = 0;
                _failed = 0;
                _skipped = 0;
                _total = 0;
                _mode = mode;
                _startedAt = DateTime.UtcNow.ToString("o");
            }

            public static Dictionary<string, object> Snapshot()
            {
                var data = new Dictionary<string, object>
                {
                    { "running", Running },
                    { "completed", _completed },
                    { "passed", _passed },
                    { "failed", _failed },
                    { "skipped", _skipped },
                    { "total", _total },
                    { "mode", _mode },
                };
                if (!string.IsNullOrEmpty(_startedAt))
                    data["startedAt"] = _startedAt;
                if (!string.IsNullOrEmpty(_note))
                    data["note"] = _note;
                return data;
            }

            public void RunStarted(ITestAdaptor testsToRun)
            {
                Running = true;
            }

            public void RunFinished(ITestResultAdaptor result)
            {
                Running = false;
                _completed = true;
            }

            public void TestStarted(ITestAdaptor test)
            {
            }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (result == null) return;
                if (result.HasChildren) return;
                if (result.Test != null && result.Test.IsSuite) return;
                _total++;
                switch (result.TestStatus)
                {
                    case TestStatus.Passed:
                        _passed++;
                        break;
                    case TestStatus.Failed:
                        _failed++;
                        break;
                    default:
                        _skipped++;
                        break;
                }
            }
        }
    }
}
