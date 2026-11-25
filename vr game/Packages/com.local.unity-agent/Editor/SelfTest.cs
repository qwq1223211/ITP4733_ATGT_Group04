using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Local.UnityAgent.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

namespace Local.UnityAgent
{
    /// <summary>
    /// 批处理自检：直接调命令实现（不经网络）。
    /// Unity.exe -batchmode -quit -executeMethod Local.UnityAgent.SelfTest.Run
    /// </summary>
    public static class SelfTest
    {
        public static void Run()
        {
            LogBuffer.EnsureHooked();
            var report = new Dictionary<string, object>();
            var steps = new List<object>();
            var ok = true;
            string failMsg = null;

            try
            {
                // MiniJson roundtrip
                {
                    var sample = "{\"a\":1,\"b\":true,\"c\":null,\"d\":\"x\\\"y\",\"e\":[1,2],\"f\":{\"g\":3.5}}";
                    var parsed = MiniJson.Parse(sample);
                    var again = MiniJson.Stringify(parsed);
                    var parsed2 = MiniJson.Parse(again) as Dictionary<string, object>;
                    if (parsed2 == null || MiniJson.GetInt(parsed2, "a") != 1)
                        throw new Exception("MiniJson roundtrip failed: " + again);
                    steps.Add(Pass("minijson", again));
                }

                ExpectOk("ping", null, steps);
                ExpectOk("status", null, steps);
                ExpectOk("console.tail", new Dictionary<string, object> { { "count", 5 } }, steps);
                ExpectOk("console.clear", null, steps);
                ExpectOk("compile.wait", new Dictionary<string, object> { { "timeoutMs", 5000 } }, steps);
                ExpectOk("scene.hierarchy", new Dictionary<string, object> { { "depth", 2 } }, steps);
                ExpectOk("play.status", null, steps);
                ExpectOk("asset.refresh", null, steps);
                ExpectOk("asset.find", new Dictionary<string, object>
                {
                    { "folder", "Assets" },
                    { "limit", 5 },
                }, steps);

                // GameObject lifecycle
                Dictionary<string, object> created;
                {
                    var resp = AgentHost.ExecuteDirect("go.create", new Dictionary<string, object>
                    {
                        { "name", "UA_SelfTest_Cube" },
                        { "primitive", "Cube" },
                        { "position", new Dictionary<string, object> { { "x", 1 }, { "y", 2 }, { "z", 3 } } },
                    });
                    AssertOk(resp, "go.create");
                    created = MiniJson.AsObject(resp["data"]);
                    steps.Add(Pass("go.create", MiniJson.GetString(created, "path")));
                }

                var goPath = MiniJson.GetString(created, "path");
                ExpectOk("go.set", new Dictionary<string, object>
                {
                    { "path", goPath },
                    { "active", true },
                    { "name", "UA_SelfTest_Cube2" },
                }, steps);
                goPath = "UA_SelfTest_Cube2";

                ExpectOk("go.transform", new Dictionary<string, object>
                {
                    { "path", goPath },
                    { "local", true },
                    { "scale", new Dictionary<string, object> { { "x", 2 }, { "y", 2 }, { "z", 2 } } },
                }, steps);

                ExpectOk("go.find", new Dictionary<string, object>
                {
                    { "name", "UA_SelfTest_Cube2" },
                    { "limit", 5 },
                }, steps);

                // Cube already has BoxCollider; add Rigidbody instead
                ExpectOk("component.add", new Dictionary<string, object>
                {
                    { "path", goPath },
                    { "type", "UnityEngine.Rigidbody" },
                }, steps);

                ExpectOk("component.get", new Dictionary<string, object>
                {
                    { "path", goPath },
                    { "component", "UnityEngine.Transform" },
                    { "property", "m_LocalPosition" },
                }, steps);

                ExpectOk("component.set", new Dictionary<string, object>
                {
                    { "path", goPath },
                    { "component", "UnityEngine.Transform" },
                    { "property", "m_LocalPosition" },
                    {
                        "value", new Dictionary<string, object>
                        {
                            { "x", 9 }, { "y", 8 }, { "z", 7 },
                        }
                    },
                }, steps);

                ExpectOk("go.destroy", new Dictionary<string, object> { { "path", goPath } }, steps);

                // script.create without refresh to avoid domain reload mid-test
                var scriptRel = "Assets/UA_SelfTest_Temp.cs";
                try
                {
                    var scriptResp = AgentHost.ExecuteDirect("script.create", new Dictionary<string, object>
                    {
                        { "path", scriptRel },
                        { "content", "// selftest temp\n" },
                        { "refresh", false },
                    });
                    AssertOk(scriptResp, "script.create");
                    steps.Add(Pass("script.create", scriptRel));
                }
                finally
                {
                    try
                    {
                        var full = PathUtil.ResolveProjectRelative(scriptRel, false);
                        if (File.Exists(full)) File.Delete(full);
                        var meta = full + ".meta";
                        if (File.Exists(meta)) File.Delete(meta);
                    }
                    catch { /* ignore */ }
                }

                // scene.save to Assets
                var scenePath = "Assets/_UA_SelfTest_Scene.unity";
                ExpectOk("scene.save", new Dictionary<string, object> { { "path", scenePath } }, steps);
                ExpectOk("scene.saveAll", null, steps);

                // Expected NOT_READY
                ExpectCode("eval", new Dictionary<string, object> { { "code", "return 1;" } }, "NOT_READY", steps);
                ExpectTestStatus(steps);

                // Batchmode: server must not listen / no server.json pollution required
                if (Application.isBatchMode && AgentHost.IsListening)
                    throw new Exception("server should not listen in batchmode");
                steps.Add(Pass("batchmode.noServer", "ok"));

                // Path guard
                try
                {
                    PathUtil.ResolveProjectRelative("../outside.txt", false);
                    throw new Exception("path escape not rejected");
                }
                catch (AgentException)
                {
                    steps.Add(Pass("path.guard", "rejected"));
                }

                // ---------- UI commands (uGUI + TMP) ----------
                RunUiSelfTest(steps);

                // ---------- asset pipeline ----------
                RunAssetSelfTest(steps);
                RunSpriteImportSelfTest(steps);
                RunSceneValidateSelfTest(steps);
                RunLevelSelfTest(steps);
                RunPerfSelfTest(steps);
                RunSaveSelfTest(steps);
                RunUiKitSelfTest(steps);
                RunLightingSelfTest(steps);
                RunPlayerSelfTest(steps);
                RunEnemySelfTest(steps);
                RunL10nSelfTest(steps);
                RunGameplaySelfTest(steps);
                RunUiFlowSelfTest(steps);
                RunDialogueSelfTest(steps);

                report["ok"] = true;
                report["steps"] = steps;
                report["unityVersion"] = Application.unityVersion;
                report["projectPath"] = PathUtil.ProjectRoot;
            }
            catch (Exception ex)
            {
                ok = false;
                failMsg = ex.Message;
                report["ok"] = false;
                report["error"] = failMsg;
                report["steps"] = steps;
            }

            var line = "SELFTEST " + MiniJson.Stringify(report);
            Console.WriteLine(line);
            Debug.Log(line);

            // EditorApplication.Exit 才能在 batchmode 可靠地带退出码结束（Environment.Exit 易挂起）
            if (!ok)
            {
                Console.Error.WriteLine("SELFTEST failed: " + failMsg);
                EditorApplication.Exit(1);
                return;
            }
            EditorApplication.Exit(0);
        }

        static void RunUiSelfTest(List<object> steps)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var tokensResp = AgentHost.ExecuteDirect("ui.tokens", null);
            AssertOk(tokensResp, "ui.tokens");
            var tokens = MiniJson.AsObject(tokensResp["data"]);
            var palette = MiniJson.AsObject(tokens["palette"]);
            if (string.IsNullOrEmpty(MiniJson.GetString(palette, "primary", null)))
                throw new Exception("ui.tokens palette.primary empty");
            steps.Add(Pass("ui.tokens", MiniJson.GetString(palette, "primary")));

            var canvasResp = AgentHost.ExecuteDirect("ui.canvas", new Dictionary<string, object>
            {
                { "name", "UICanvas" },
                { "referenceResolution", new List<object> { 1920, 1080 } },
            });
            AssertOk(canvasResp, "ui.canvas");
            steps.Add(Pass("ui.canvas", "1920x1080"));

            // stretch with negative sizeDelta ≈ margin
            ExpectOk("ui.panel", new Dictionary<string, object>
            {
                { "name", "MainPanel" },
                { "preset", "stretch" },
                { "w", -48 },
                { "h", -48 },
                { "color", "#1A2332" },
                { "sprite", "rounded" },
            }, steps);

            ExpectOk("ui.text", new Dictionary<string, object>
            {
                { "parent", "UICanvas/MainPanel" },
                { "name", "Title" },
                { "text", "Unity Agent UI" },
                { "preset", "top-center" },
                { "y", -24 },
                { "w", 600 },
                { "h", 48 },
                { "fontSize", 28 },
                { "align", "center" },
                { "color", "#F1F5F9" },
            }, steps);

            ExpectOk("ui.button", new Dictionary<string, object>
            {
                { "parent", "UICanvas/MainPanel" },
                { "name", "PrimaryBtn" },
                { "label", "OK" },
                { "preset", "center" },
                { "w", 160 },
                { "h", 44 },
            }, steps);

            // vertical layout host + 3 buttons
            ExpectOk("ui.panel", new Dictionary<string, object>
            {
                { "parent", "UICanvas/MainPanel" },
                { "name", "BtnColumn" },
                { "preset", "center" },
                { "y", -80 },
                { "w", 200 },
                { "h", 180 },
                { "sprite", "none" },
                { "color", new List<object> { 0, 0, 0, 0 } },
            }, steps);

            ExpectOk("ui.layout", new Dictionary<string, object>
            {
                { "path", "UICanvas/MainPanel/BtnColumn" },
                { "type", "vertical" },
                { "spacing", 8 },
                { "padding", new List<object> { 8, 8, 8, 8 } },
                { "childControlWidth", true },
                { "childControlHeight", true },
                { "childForceExpandWidth", true },
                { "childForceExpandHeight", false },
            }, steps);

            for (var i = 1; i <= 3; i++)
            {
                ExpectOk("ui.button", new Dictionary<string, object>
                {
                    { "parent", "UICanvas/MainPanel/BtnColumn" },
                    { "name", "LayoutBtn" + i },
                    { "label", "Item " + i },
                    { "w", 180 },
                    { "h", 44 },
                }, steps);
            }

            var treeResp = AgentHost.ExecuteDirect("ui.tree", null);
            AssertOk(treeResp, "ui.tree");
            var treeJson = MiniJson.Stringify(treeResp["data"]);
            foreach (var need in new[] { "UICanvas", "MainPanel", "Title", "PrimaryBtn", "BtnColumn", "LayoutBtn1", "LayoutBtn2", "LayoutBtn3" })
            {
                if (treeJson.IndexOf(need, StringComparison.Ordinal) < 0)
                    throw new Exception("ui.tree missing name: " + need);
            }
            steps.Add(Pass("ui.tree", "names-ok"));

            var valResp = AgentHost.ExecuteDirect("ui.validate", null);
            AssertOk(valResp, "ui.validate");
            var valData = MiniJson.AsObject(valResp["data"]);
            if (!(valData["issues"] is List<object>))
                throw new Exception("ui.validate issues not array");
            if (!valData.ContainsKey("count"))
                throw new Exception("ui.validate missing count");
            steps.Add(Pass("ui.validate", "count=" + MiniJson.GetInt(valData, "count")));

            ExpectOk("ui.slider", new Dictionary<string, object>
            {
                { "parent", "UICanvas/MainPanel" },
                { "name", "VolSlider" },
                { "preset", "bottom-center" },
                { "y", 40 },
                { "w", 240 },
                { "h", 20 },
                { "min", 0 },
                { "max", 1 },
                { "value", 0.3 },
            }, steps);

            ExpectOk("ui.toggle", new Dictionary<string, object>
            {
                { "parent", "UICanvas/MainPanel" },
                { "name", "OptToggle" },
                { "label", "Enable" },
                { "preset", "bottom-left" },
                { "x", 40 },
                { "y", 40 },
                { "w", 140 },
                { "h", 28 },
                { "isOn", true },
            }, steps);

            ExpectOk("ui.scrollview", new Dictionary<string, object>
            {
                { "parent", "UICanvas/MainPanel" },
                { "name", "ListScroll" },
                { "preset", "middle-right" },
                { "x", -40 },
                { "w", 220 },
                { "h", 200 },
                { "axis", "vertical" },
            }, steps);

            var fontsResp = AgentHost.ExecuteDirect("ui.fonts", new Dictionary<string, object>
            {
                { "limit", 30 },
            });
            AssertOk(fontsResp, "ui.fonts");
            var fontsData = MiniJson.AsObject(fontsResp["data"]);
            var fonts = fontsData != null ? fontsData["fonts"] as List<object> : null;
            if (fonts == null)
                throw new Exception("ui.fonts fonts not array");
            steps.Add(Pass("ui.fonts", "count=" + fonts.Count));

            string cjkPath = null;
            foreach (var item in fonts)
            {
                var d = item as Dictionary<string, object>;
                if (d == null) continue;
                if (!MiniJson.GetBool(d, "cjk", false)) continue;
                if (!string.Equals(MiniJson.GetString(d, "type", ""), "tmp", StringComparison.Ordinal)) continue;
                cjkPath = MiniJson.GetString(d, "path", null);
                if (!string.IsNullOrEmpty(cjkPath)) break;
            }
            if (!string.IsNullOrEmpty(cjkPath))
            {
                ExpectOk("ui.text", new Dictionary<string, object>
                {
                    { "parent", "UICanvas/MainPanel" },
                    { "name", "CjkTitle" },
                    { "text", "中文" },
                    { "fontPath", cjkPath },
                    { "fontSize", 24 },
                    { "w", 240 },
                    { "h", 40 },
                }, steps);
            }
        }

        static void RunAssetSelfTest(List<object> steps)
        {
            const string folder = "Assets/UA_SelfTest";
            const string texPath = "Assets/UA_SelfTest/tex.png";
            try
            {
                var fullDir = Path.Combine(PathUtil.ProjectRoot, "Assets", "UA_SelfTest");
                Directory.CreateDirectory(fullDir);
                var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                var pixels = new Color[64 * 64];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = Color.red;
                tex.SetPixels(pixels);
                tex.Apply();
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                File.WriteAllBytes(Path.Combine(fullDir, "tex.png"), png);
                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);

                var setResp = AgentHost.ExecuteDirect("asset.importSettings", new Dictionary<string, object>
                {
                    { "path", texPath },
                    { "type", "texture" },
                    {
                        "settings", new Dictionary<string, object>
                        {
                            { "readable", true },
                            { "maxTextureSize", 4096 },
                        }
                    },
                });
                AssertOk(setResp, "asset.importSettings");
                steps.Add(Pass("asset.importSettings", "readable+4096"));

                var prof = AgentHost.ExecuteDirect("asset.profile", null);
                AssertOk(prof, "asset.profile");
                var pdata = MiniJson.AsObject(prof["data"]);
                var profiles = MiniJson.AsObject(pdata["profiles"]);
                var rules = pdata["rules"] as List<object>;
                if (profiles == null || !profiles.ContainsKey("pc") || !profiles.ContainsKey("mobile") || !profiles.ContainsKey("web"))
                    throw new Exception("asset.profile missing pc/mobile/web");
                if (rules == null || rules.Count == 0)
                    throw new Exception("asset.profile rules empty");
                steps.Add(Pass("asset.profile", "rules=" + rules.Count));

                var audit = AgentHost.ExecuteDirect("asset.audit", new Dictionary<string, object>
                {
                    { "folder", folder },
                    { "profile", "mobile" },
                });
                AssertOk(audit, "asset.audit");
                var issues = MiniJson.AsObject(audit["data"])["issues"] as List<object>;
                if (!HasCode(issues, "texture.readable") || !HasCode(issues, "texture.max-size"))
                    throw new Exception("audit missing texture.readable/max-size");
                steps.Add(Pass("asset.audit", "readable+max-size"));

                var before = (TextureImporter)AssetImporter.GetAtPath(texPath);
                var readableBefore = before.isReadable;
                var maxBefore = before.maxTextureSize;

                var dry = AgentHost.ExecuteDirect("asset.fix", new Dictionary<string, object>
                {
                    { "folder", folder },
                    { "profile", "mobile" },
                    { "dryRun", true },
                });
                AssertOk(dry, "asset.fix dry");
                var planned = Convert.ToInt32(MiniJson.AsObject(dry["data"])["planned"]);
                if (planned < 2)
                    throw new Exception("dryRun planned < 2");
                var afterDry = (TextureImporter)AssetImporter.GetAtPath(texPath);
                if (afterDry.isReadable != readableBefore || afterDry.maxTextureSize != maxBefore)
                    throw new Exception("dryRun changed the importer");
                steps.Add(Pass("asset.fix", "dry planned=" + planned));

                var fix = AgentHost.ExecuteDirect("asset.fix", new Dictionary<string, object>
                {
                    { "folder", folder },
                    { "profile", "mobile" },
                    { "dryRun", false },
                    { "codes", new List<object> { "texture.readable", "texture.max-size" } },
                });
                AssertOk(fix, "asset.fix");
                var fdata = MiniJson.AsObject(fix["data"]);
                var changed = Convert.ToInt32(fdata["changed"]);
                if (changed < 2)
                    throw new Exception("changed < 2");
                steps.Add(Pass("asset.fix.apply", "changed=" + changed));

                var audit2 = AgentHost.ExecuteDirect("asset.audit", new Dictionary<string, object>
                {
                    { "folder", folder },
                    { "profile", "mobile" },
                });
                AssertOk(audit2, "asset.audit recheck");
                var issues2 = MiniJson.AsObject(audit2["data"])["issues"] as List<object>;
                if (HasCode(issues2, "texture.readable") || HasCode(issues2, "texture.max-size"))
                    throw new Exception("texture issues remain after fix");

                var stats = AgentHost.ExecuteDirect("asset.stats", new Dictionary<string, object>
                {
                    { "folder", folder },
                });
                AssertOk(stats, "asset.stats");
                var total = Convert.ToInt32(MiniJson.AsObject(stats["data"])["totalAssets"]);
                if (total < 1)
                    throw new Exception("asset.stats totalAssets < 1");

                var reportPath = MiniJson.GetString(fdata, "reportPath", null);
                if (string.IsNullOrEmpty(reportPath))
                    throw new Exception("reportPath missing");
                var jsonFull = PathUtil.ResolveProjectRelative(reportPath, true);
                var mdFull = Path.ChangeExtension(jsonFull, ".md");
                if (!File.Exists(jsonFull) || !File.Exists(mdFull))
                    throw new Exception("report files missing: " + reportPath);
                steps.Add(Pass("asset.stats", "total=" + total + " report=" + reportPath));
            }
            finally
            {
                try
                {
                    if (AssetDatabase.IsValidFolder(folder))
                        AssetDatabase.DeleteAsset(folder);
                    var leftover = Path.Combine(PathUtil.ProjectRoot, "Assets", "UA_SelfTest");
                    if (Directory.Exists(leftover))
                        Directory.Delete(leftover, true);
                    var meta = leftover + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[UnityAgent] cleanup UA_SelfTest: " + ex.Message);
                }
            }
        }

        static void RunSpriteImportSelfTest(List<object> steps)
        {
            const string folder = "Assets/UA_SelfTestSprite";
            const string texPath = "Assets/UA_SelfTestSprite/tex.png";
            try
            {
                var fullDir = Path.Combine(PathUtil.ProjectRoot, "Assets", "UA_SelfTestSprite");
                Directory.CreateDirectory(fullDir);
                var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
                var pixels = new Color[64 * 64];
                for (var i = 0; i < pixels.Length; i++) pixels[i] = new Color(1, 0, 0, 0.5f);
                tex.SetPixels(pixels);
                tex.Apply();
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                File.WriteAllBytes(Path.Combine(fullDir, "tex.png"), png);
                AssetDatabase.Refresh();
                AssetDatabase.ImportAsset(texPath, ImportAssetOptions.ForceUpdate);

                var resp = AgentHost.ExecuteDirect("asset.importSettings", new Dictionary<string, object>
                {
                    { "path", texPath },
                    { "type", "texture" },
                    {
                        "settings", new Dictionary<string, object>
                        {
                            { "textureType", "sprite" },
                            { "spriteBorder", new List<object> { 8, 8, 8, 8 } },
                            { "spritePixelsPerUnit", 100 },
                            { "alphaIsTransparency", true },
                        }
                    },
                });
                AssertOk(resp, "asset.importSettings.sprite");
                var importer = AssetImporter.GetAtPath(texPath) as TextureImporter;
                if (importer == null)
                    throw new Exception("sprite importer missing");
                if (importer.textureType != TextureImporterType.Sprite)
                    throw new Exception("textureType is " + importer.textureType);
                var border = importer.spriteBorder;
                if (Mathf.Abs(border.x - 8f) > 0.01f || Mathf.Abs(border.y - 8f) > 0.01f
                    || Mathf.Abs(border.z - 8f) > 0.01f || Mathf.Abs(border.w - 8f) > 0.01f)
                    throw new Exception("spriteBorder " + border);
                steps.Add(Pass("asset.importSettings.sprite", "sprite border 8"));
            }
            finally
            {
                try
                {
                    if (AssetDatabase.IsValidFolder(folder))
                        AssetDatabase.DeleteAsset(folder);
                    var leftover = Path.Combine(PathUtil.ProjectRoot, "Assets", "UA_SelfTestSprite");
                    if (Directory.Exists(leftover))
                        Directory.Delete(leftover, true);
                    var meta = leftover + ".meta";
                    if (File.Exists(meta)) File.Delete(meta);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[UnityAgent] cleanup UA_SelfTestSprite: " + ex.Message);
                }
            }
        }

        static void RunSceneValidateSelfTest(List<object> steps)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var parent = new GameObject("DupParent");
            var twinA = new GameObject("Twin");
            var twinB = new GameObject("Twin");
            twinA.transform.SetParent(parent.transform, false);
            twinB.transform.SetParent(parent.transform, false);
            new GameObject("EmptyOne");

            var noMat = new GameObject("NoMat");
            var renderer = noMat.AddComponent<MeshRenderer>();
            var so = new SerializedObject(renderer);
            var mats = so.FindProperty("m_Materials");
            mats.arraySize = 1;
            mats.GetArrayElementAtIndex(0).objectReferenceValue = null;
            so.ApplyModifiedPropertiesWithoutUndo();

            var resp = AgentHost.ExecuteDirect("scene.validate", null);
            AssertOk(resp, "scene.validate");
            var data = MiniJson.AsObject(resp["data"]);
            var issues = data["issues"] as List<object>;
            if (!HasCode(issues, "duplicate-name") || !HasCode(issues, "empty-object") || !HasCode(issues, "missing-material-slot"))
                throw new Exception("scene.validate missing duplicate-name/empty-object/missing-material-slot");
            var counts = MiniJson.AsObject(data["counts"]);
            var errors = Convert.ToInt32(counts["error"]);
            if (errors <= 0)
                throw new Exception("scene.validate errors=" + errors);
            steps.Add(Pass("scene.validate", "dup+empty+slot errors=" + errors));
        }

        static bool HasCode(List<object> issues, string code)
        {
            if (issues == null) return false;
            foreach (var item in issues)
            {
                var d = item as Dictionary<string, object>;
                if (d != null && MiniJson.GetString(d, "code", "") == code)
                    return true;
            }
            return false;
        }

        static void RunLevelSelfTest(List<object> steps)
        {
            var args = new Dictionary<string, object>
            {
                { "width", 6 },
                { "depth", 6 },
                { "seed", 1 },
            };
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var resp = AgentHost.ExecuteDirect("level.generate", args);
            sw.Stop();
            AssertOk(resp, "level.generate");
            var data = MiniJson.AsObject(resp["data"]);
            var objects = ReadNum(data, "objects");
            if (GameObject.Find("UnityAgentLevel") == null)
                throw new Exception("level.generate root missing");
            if (objects <= 30)
                throw new Exception("level.generate objects=" + objects);
            var ms6 = sw.ElapsedMilliseconds;

            sw.Restart();
            var again = AgentHost.ExecuteDirect("level.generate", args);
            sw.Stop();
            AssertOk(again, "level.generate");
            var objects2 = ReadNum(MiniJson.AsObject(again["data"]), "objects");
            if (objects2 != objects)
                throw new Exception("level.generate seed mismatch " + objects + " vs " + objects2);
            ms6 = sw.ElapsedMilliseconds;

            var infoResp = AgentHost.ExecuteDirect("level.info", null);
            AssertOk(infoResp, "level.info");
            var info = MiniJson.AsObject(infoResp["data"]);
            if (!MiniJson.GetBool(info, "exists", false))
                throw new Exception("level.info exists=false");
            if (ReadNum(info, "objects") != objects2)
                throw new Exception("level.info objects mismatch");
            steps.Add(Pass("level.info", "objects=" + objects2));

            var bigArgs = new Dictionary<string, object>
            {
                { "width", 16 },
                { "depth", 16 },
                { "seed", 1 },
            };
            sw.Restart();
            var big = AgentHost.ExecuteDirect("level.generate", bigArgs);
            sw.Stop();
            AssertOk(big, "level.generate");
            var objects16 = ReadNum(MiniJson.AsObject(big["data"]), "objects");
            var ms16 = sw.ElapsedMilliseconds;
            steps.Add(Pass("level.generate", "6x6=" + objects2 + " ms=" + ms6 + " 16x16=" + objects16 + " ms=" + ms16));

            var clearResp = AgentHost.ExecuteDirect("level.clear", null);
            AssertOk(clearResp, "level.clear");
            if (GameObject.Find("UnityAgentLevel") != null)
                throw new Exception("level.clear root still exists");
            var removed = ReadNum(MiniJson.AsObject(clearResp["data"]), "removed");
            steps.Add(Pass("level.clear", "removed=" + removed));

            var roomArgs = new Dictionary<string, object>
            {
                { "style", "rooms" },
                { "width", 10 },
                { "depth", 10 },
                { "seed", 2 },
                { "props", 4 },
            };
            var roomResp = AgentHost.ExecuteDirect("level.generate", roomArgs);
            AssertOk(roomResp, "level.rooms");
            var room = MiniJson.AsObject(roomResp["data"]);
            var roomObjects = ReadNum(room, "objects");
            var doorSlots = ReadNum(room, "doorSlots");
            var propSlots = ReadNum(room, "propSlots");
            if (roomObjects <= 0) throw new Exception("level.rooms objects=" + roomObjects);
            if (doorSlots <= 0) throw new Exception("level.rooms doorSlots=" + doorSlots);
            if (propSlots != 4) throw new Exception("level.rooms propSlots=" + propSlots);
            var roomAgain = AgentHost.ExecuteDirect("level.generate", roomArgs);
            AssertOk(roomAgain, "level.rooms");
            var roomObjects2 = ReadNum(MiniJson.AsObject(roomAgain["data"]), "objects");
            if (roomObjects2 != roomObjects)
                throw new Exception("level.rooms seed mismatch " + roomObjects + " vs " + roomObjects2);
            steps.Add(Pass("level.rooms", "objects=" + roomObjects2 + " doors=" + doorSlots + " props=" + propSlots));

            var bakeResp = AgentHost.ExecuteDirect("level.bake_navmesh", null);
            if (IsOk(bakeResp))
            {
                var baked = MiniJson.AsObject(bakeResp["data"]);
                steps.Add(Pass("level.bake_navmesh", "built objects=" + ReadNum(baked, "objects")));
            }
            else
            {
                var err = bakeResp != null && bakeResp.ContainsKey("error") ? MiniJson.AsObject(bakeResp["error"]) : null;
                var code = MiniJson.GetString(err, "code", "");
                if (!string.Equals(code, "NOT_READY", StringComparison.Ordinal))
                    throw new Exception("level.bake_navmesh failed: " + code + " " + MiniJson.GetString(err, "message"));
                steps.Add(Pass("level.bake_navmesh", "NOT_READY " + MiniJson.GetString(err, "message")));
            }

            var savePrep = "Assets/Levels/Generated/_UA_SelfTest_Level.prefab";
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(savePrep) != null)
                AssetDatabase.DeleteAsset(savePrep);
            var saveGen = AgentHost.ExecuteDirect("level.generate", new Dictionary<string, object>
            {
                { "style", "rooms" },
                { "width", 6 },
                { "depth", 6 },
                { "props", 2 },
                { "seed", 3 },
            });
            AssertOk(saveGen, "level.generate");
            var saveResp = AgentHost.ExecuteDirect("level.save", new Dictionary<string, object>
            {
                { "mode", "prefab" },
                { "name", "_UA_SelfTest_Level" },
            });
            AssertOk(saveResp, "level.save");
            var saved = MiniJson.AsObject(saveResp["data"]);
            var savePath = MiniJson.GetString(saved, "path", "");
            if (string.IsNullOrEmpty(savePath))
                throw new Exception("level.save path empty");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(savePath) == null)
                throw new Exception("level.save prefab missing: " + savePath);
            var saveObjects = ReadNum(saved, "objects");
            steps.Add(Pass("level.save", savePath + " objects=" + saveObjects));
            if (!AssetDatabase.DeleteAsset(savePath))
                throw new Exception("level.save delete failed: " + savePath);

            var clearRooms = AgentHost.ExecuteDirect("level.clear", null);
            AssertOk(clearRooms, "level.clear");
            if (GameObject.Find("UnityAgentLevel") != null)
                throw new Exception("level.clear rooms root still exists");
            steps.Add(Pass("level.clear", "rooms removed=" + ReadNum(MiniJson.AsObject(clearRooms["data"]), "removed")));
        }

        static void RunSaveSelfTest(List<object> steps)
        {
            const string slot = "_ua_selftest";
            var backups = new Dictionary<string, string>();
            foreach (var name in Runtime.SaveSystem.List())
            {
                if (name == slot) continue;
                backups[name] = File.ReadAllText(Runtime.SaveSystem.PathFor(name));
            }

            var payload = "{\"hp\":7,\"name\":\"test\"}";
            var write = AgentHost.ExecuteDirect("save.write", new Dictionary<string, object>
            {
                { "slot", slot },
                { "json", payload },
            });
            AssertOk(write, "save.write");
            steps.Add(Pass("save.write", "bytes=" + ReadNum(MiniJson.AsObject(write["data"]), "bytes")));

            var read = AgentHost.ExecuteDirect("save.read", new Dictionary<string, object> { { "slot", slot } });
            AssertOk(read, "save.read");
            var got = MiniJson.GetString(MiniJson.AsObject(read["data"]), "payload", "");
            if (got.IndexOf("\"hp\":7", StringComparison.Ordinal) < 0)
                throw new Exception("save.read payload=" + got);
            steps.Add(Pass("save.read", got));

            var list = AgentHost.ExecuteDirect("save.list", null);
            AssertOk(list, "save.list");
            if (!HasSlot(MiniJson.AsObject(list["data"]), slot))
                throw new Exception("save.list missing " + slot);
            steps.Add(Pass("save.list", "has " + slot));

            var del = AgentHost.ExecuteDirect("save.delete", new Dictionary<string, object> { { "slot", slot } });
            AssertOk(del, "save.delete");
            var again = AgentHost.ExecuteDirect("save.read", new Dictionary<string, object> { { "slot", slot } });
            AssertOk(again, "save.read");
            if (MiniJson.GetBool(MiniJson.AsObject(again["data"]), "exists", true))
                throw new Exception("save.delete still exists");
            steps.Add(Pass("save.delete", "exists=false"));

            Runtime.SaveSystem.SaveRaw(slot, payload, 1);
            Runtime.SaveSystem.DeleteAll();
            if (Runtime.SaveSystem.Exists(slot))
                throw new Exception("DeleteAll left " + slot);
            foreach (var pair in backups)
            {
                var path = Runtime.SaveSystem.PathFor(pair.Key);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(path, pair.Value);
            }
        }

        static bool HasSlot(Dictionary<string, object> data, string slot)
        {
            if (data == null || !data.TryGetValue("slots", out var v) || v == null || v is string) return false;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return false;
            foreach (var item in list)
            {
                if (string.Equals(Convert.ToString(item), slot, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        static void RunUiKitSelfTest(List<object> steps)
        {
            var hudResp = AgentHost.ExecuteDirect("uikit.hud", new Dictionary<string, object>
            {
                { "name", "_UA_SelfTest_HUD" },
                { "savePrefab", false },
            });
            AssertOk(hudResp, "uikit.hud");
            var hud = MiniJson.AsObject(hudResp["data"]);
            var hudN = ReadNum(hud, "elements");
            if (hudN <= 0) throw new Exception("uikit.hud elements=" + hudN);
            if (GameObject.Find("_UA_SelfTest_HUD") == null)
                throw new Exception("uikit.hud canvas missing");
            var hudCanvas = GameObject.Find("_UA_SelfTest_HUD").GetComponent<Canvas>();
            if (hudCanvas == null) throw new Exception("uikit.hud no Canvas");
            steps.Add(Pass("uikit.hud", "elements=" + hudN + " " + JoinNames(hud)));

            var pauseResp = AgentHost.ExecuteDirect("uikit.pause", new Dictionary<string, object>
            {
                { "name", "_UA_SelfTest_Pause" },
                { "savePrefab", false },
            });
            AssertOk(pauseResp, "uikit.pause");
            var pause = MiniJson.AsObject(pauseResp["data"]);
            var pauseN = ReadNum(pause, "elements");
            if (pauseN <= 0) throw new Exception("uikit.pause elements=" + pauseN);
            steps.Add(Pass("uikit.pause", "elements=" + pauseN + " " + JoinNames(pause)));

            var hudGo = GameObject.Find("_UA_SelfTest_HUD");
            var pauseGo = GameObject.Find("_UA_SelfTest_Pause");
            if (hudGo != null) UnityEngine.Object.DestroyImmediate(hudGo);
            if (pauseGo != null) UnityEngine.Object.DestroyImmediate(pauseGo);
            if (GameObject.Find("_UA_SelfTest_HUD") != null || GameObject.Find("_UA_SelfTest_Pause") != null)
                throw new Exception("uikit roots still exist");
        }

        static string JoinNames(Dictionary<string, object> data)
        {
            if (data == null || !data.TryGetValue("names", out var v) || v == null || v is string) return "";
            var list = v as System.Collections.IEnumerable;
            if (list == null) return "";
            var parts = new List<string>();
            foreach (var item in list) parts.Add(Convert.ToString(item));
            return string.Join(",", parts);
        }

        static void RunLightingSelfTest(List<object> steps)
        {
            var snap = LightingCommands.CaptureSnapshot();
            var apply = AgentHost.ExecuteDirect("lighting.apply", new Dictionary<string, object> { { "preset", "horror" } });
            AssertOk(apply, "lighting.apply");
            var data = MiniJson.AsObject(apply["data"]);
            var fog = MiniJson.AsObject(data["fog"]);
            var ambient = MiniJson.AsObject(data["ambient"]);
            var light = MiniJson.AsObject(data["light"]);
            if (!MiniJson.GetBool(fog, "enabled", false))
                throw new Exception("lighting.apply horror fog off");
            if (string.IsNullOrEmpty(MiniJson.GetString(ambient, "color", null)))
                throw new Exception("lighting.apply ambient missing");
            if (string.IsNullOrEmpty(MiniJson.GetString(light, "path", null)))
                throw new Exception("lighting.apply light missing");
            steps.Add(Pass("lighting.apply", "horror fog=" + MiniJson.GetString(fog, "color")
                + " ambient=" + MiniJson.GetString(ambient, "color")
                + " light=" + MiniJson.GetString(light, "color")));

            var presets = AgentHost.ExecuteDirect("lighting.presets", null);
            AssertOk(presets, "lighting.presets");
            var list = MiniJson.AsObject(presets["data"])["presets"] as System.Collections.IList;
            if (list == null || list.Count < 5)
                throw new Exception("lighting.presets count=" + (list?.Count ?? 0));
            steps.Add(Pass("lighting.presets", "count=" + list.Count));

            var info = AgentHost.ExecuteDirect("lighting.info", null);
            AssertOk(info, "lighting.info");
            steps.Add(Pass("lighting.info", "ok"));

            LightingCommands.RestoreSnapshot(snap);
        }

        static void RunPlayerSelfTest(List<object> steps)
        {
            var create = AgentHost.ExecuteDirect("player.create", new Dictionary<string, object>
            {
                { "name", "_UA_SelfTest_Player" },
                { "spawn", new List<object> { 0d, 1d, 0d } },
            });
            AssertOk(create, "player.create");
            var data = MiniJson.AsObject(create["data"]);
            var path = MiniJson.GetString(data, "path", "");
            var camPath = MiniJson.GetString(data, "cameraPath", "");
            if (string.IsNullOrEmpty(path)) throw new Exception("player.create path empty");
            if (string.IsNullOrEmpty(camPath)) throw new Exception("player.create cameraPath empty");
            var rootT = AgentCommands.FindTransformByPath(path);
            if (rootT == null) throw new Exception("player.create root missing " + path);
            var cc = rootT.GetComponent<CharacterController>();
            var fps = rootT.GetComponent<FirstPersonController>();
            if (cc == null) throw new Exception("player.create no CharacterController");
            if (fps == null) throw new Exception("player.create no FirstPersonController");
            var camT = AgentCommands.FindTransformByPath(camPath);
            if (camT == null || camT.GetComponent<Camera>() == null)
                throw new Exception("player.create camera missing " + camPath);
            if (fps.cameraTransform != camT)
                throw new Exception("player.create cameraTransform mismatch");
            var pos = rootT.position;
            steps.Add(Pass("player.create", "path=" + path
                + " cam=" + camPath
                + " pos=" + pos.x.ToString("0.##") + "," + pos.y.ToString("0.##") + "," + pos.z.ToString("0.##")
                + " height=" + cc.height.ToString("0.##")
                + " radius=" + cc.radius.ToString("0.##")
                + " centerY=" + cc.center.y.ToString("0.##")
                + " speed=" + fps.moveSpeed.ToString("0.##")
                + " jump=" + fps.jumpHeight.ToString("0.##")
                + " sens=" + fps.mouseSensitivity.ToString("0.##")
                + " camY=" + camT.localPosition.y.ToString("0.##")));

            var info = AgentHost.ExecuteDirect("player.info", null);
            AssertOk(info, "player.info");
            if (!PlayerListed(MiniJson.AsObject(info["data"]), path))
                throw new Exception("player.info missing " + path);
            steps.Add(Pass("player.info", "path=" + path));

            UnityEngine.Object.DestroyImmediate(rootT.gameObject);
            if (GameObject.Find("_UA_SelfTest_Player") != null)
                throw new Exception("player root still exists");
        }

        static void RunEnemySelfTest(List<object> steps)
        {
            var create = AgentHost.ExecuteDirect("enemy.create", new Dictionary<string, object>
            {
                { "name", "_UA_SelfTest_Enemy" },
                { "waypoints", new List<object>
                    {
                        new List<object> { 2d, 0d, 0d },
                        new List<object> { 0d, 0d, 2d },
                        new List<object> { -2d, 0d, 0d },
                    }
                },
            });
            AssertOk(create, "enemy.create");
            var data = MiniJson.AsObject(create["data"]);
            var path = MiniJson.GetString(data, "path", "");
            if (string.IsNullOrEmpty(path)) throw new Exception("enemy.create path empty");
            var rootT = AgentCommands.FindTransformByPath(path);
            if (rootT == null) throw new Exception("enemy.create root missing " + path);
            var agent = rootT.GetComponent<NavMeshAgent>();
            var patrol = rootT.GetComponent<PatrolAgent>();
            if (agent == null) throw new Exception("enemy.create no NavMeshAgent");
            if (patrol == null) throw new Exception("enemy.create no PatrolAgent");
            if (patrol.waypoints == null || patrol.waypoints.Length != 3)
                throw new Exception("enemy.create waypoints=" + (patrol.waypoints == null ? -1 : patrol.waypoints.Length));
            var body = rootT.Find("Body");
            if (body == null) throw new Exception("enemy.create Body missing");
            var wp0 = patrol.waypoints[0].localPosition;
            var wp1 = patrol.waypoints[1].localPosition;
            var wp2 = patrol.waypoints[2].localPosition;
            steps.Add(Pass("enemy.create", "path=" + path
                + " agent=" + agent.GetType().Name
                + " patrol=" + patrol.GetType().Name
                + " speed=" + agent.speed.ToString("0.##")
                + " radius=" + agent.radius.ToString("0.##")
                + " height=" + agent.height.ToString("0.##")
                + " chase=" + patrol.chaseRange.ToString("0.##")
                + " waypoints=" + patrol.waypoints.Length
                + " bodyY=" + body.localPosition.y.ToString("0.##")
                + " wp=" + Fmt(wp0) + ";" + Fmt(wp1) + ";" + Fmt(wp2)));

            var info = AgentHost.ExecuteDirect("enemy.info", null);
            AssertOk(info, "enemy.info");
            if (!EnemyListed(MiniJson.AsObject(info["data"]), path))
                throw new Exception("enemy.info missing " + path);
            steps.Add(Pass("enemy.info", "path=" + path + " waypoints=3"));

            UnityEngine.Object.DestroyImmediate(rootT.gameObject);
            if (GameObject.Find("_UA_SelfTest_Enemy") != null)
                throw new Exception("enemy root still exists");
        }

        static string Fmt(Vector3 v)
        {
            return v.x.ToString("0.##") + "," + v.y.ToString("0.##") + "," + v.z.ToString("0.##");
        }

        static bool EnemyListed(Dictionary<string, object> data, string path)
        {
            if (data == null || !data.TryGetValue("enemies", out var v) || v == null || v is string) return false;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return false;
            foreach (var item in list)
            {
                var row = MiniJson.AsObject(item);
                if (row == null) continue;
                if (!string.Equals(MiniJson.GetString(row, "path", ""), path, StringComparison.Ordinal)) continue;
                return ReadNum(row, "waypoints") == 3;
            }
            return false;
        }

        static void RunL10nSelfTest(List<object> steps)
        {
            var canvasA = AgentHost.ExecuteDirect("ui.canvas", new Dictionary<string, object> { { "name", "_UA_L10n_A" } });
            AssertOk(canvasA, "ui.canvas A");
            var rootA = MiniJson.GetString(MiniJson.AsObject(canvasA["data"]), "path", "");
            var textA = AgentHost.ExecuteDirect("ui.text", new Dictionary<string, object>
            {
                { "name", "Label" },
                { "parent", rootA },
                { "text", "Start" },
            });
            AssertOk(textA, "ui.text A");
            var pathA = MiniJson.GetString(MiniJson.AsObject(textA["data"]), "path", "");

            var canvasB = AgentHost.ExecuteDirect("ui.canvas", new Dictionary<string, object> { { "name", "_UA_L10n_B" } });
            AssertOk(canvasB, "ui.canvas B");
            var rootB = MiniJson.GetString(MiniJson.AsObject(canvasB["data"]), "path", "");
            var textB = AgentHost.ExecuteDirect("ui.text", new Dictionary<string, object>
            {
                { "name", "Label" },
                { "parent", rootB },
                { "text", "Quit" },
            });
            AssertOk(textB, "ui.text B");
            var pathB = MiniJson.GetString(MiniJson.AsObject(textB["data"]), "path", "");

            var scan = AgentHost.ExecuteDirect("l10n.scan", null);
            AssertOk(scan, "l10n.scan");
            var scanData = MiniJson.AsObject(scan["data"]);
            if (!L10nHasText(scanData, pathA, "Start") || !L10nHasText(scanData, pathB, "Quit"))
                throw new Exception("l10n.scan missing Start/Quit");
            steps.Add(Pass("l10n.scan", "count=" + ReadNum(scanData, "count") + " a=" + pathA + " b=" + pathB));

            var applyMap = new Dictionary<string, object> { { pathA, "开始" } };
            var apply = AgentHost.ExecuteDirect("l10n.apply", new Dictionary<string, object> { { "map", applyMap } });
            AssertOk(apply, "l10n.apply");
            var applyData = MiniJson.AsObject(apply["data"]);
            if (ReadNum(applyData, "applied") < 1) throw new Exception("l10n.apply applied=0");
            if (GetSceneText(pathA) != "开始") throw new Exception("l10n.apply A not 开始");
            if (GetSceneText(pathB) != "Quit") throw new Exception("l10n.apply B changed");
            steps.Add(Pass("l10n.apply", "applied=" + ReadNum(applyData, "applied") + " a=开始 b=Quit"));

            const string exportRel = "Assets/Localization/_UA_SelfTest.json";
            var export = AgentHost.ExecuteDirect("l10n.export", new Dictionary<string, object> { { "path", exportRel } });
            AssertOk(export, "l10n.export");
            var exportFull = PathUtil.ResolveProjectRelative(exportRel, allowLibraryAgent: false);
            if (!File.Exists(exportFull)) throw new Exception("l10n.export file missing");
            var parsed = MiniJson.AsObject(MiniJson.Parse(File.ReadAllText(exportFull, Encoding.UTF8)));
            if (parsed == null || !parsed.ContainsKey(pathA))
                throw new Exception("l10n.export json missing pathA");
            steps.Add(Pass("l10n.export", "path=" + exportRel + " keys=" + parsed.Count));

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(exportRel) != null)
                AssetDatabase.DeleteAsset(exportRel);
            else if (File.Exists(exportFull))
                File.Delete(exportFull);
            AssetDatabase.Refresh();

            var goA = GameObject.Find("_UA_L10n_A");
            var goB = GameObject.Find("_UA_L10n_B");
            if (goA != null) UnityEngine.Object.DestroyImmediate(goA);
            if (goB != null) UnityEngine.Object.DestroyImmediate(goB);
            if (GameObject.Find("_UA_L10n_A") != null || GameObject.Find("_UA_L10n_B") != null)
                throw new Exception("l10n roots still exist");
        }

        static void RunGameplaySelfTest(List<object> steps)
        {
            var hpGo = AgentHost.ExecuteDirect("go.create", new Dictionary<string, object> { { "name", "_UA_Gp_Hp" } });
            AssertOk(hpGo, "go.create hp");
            var hpPath = MiniJson.GetString(MiniJson.AsObject(hpGo["data"]), "path", "");

            var attach = AgentHost.ExecuteDirect("gameplay.attach_health", new Dictionary<string, object>
            {
                { "path", hpPath },
                { "max", 30d },
            });
            AssertOk(attach, "gameplay.attach_health");
            var hp = AgentCommands.FindTransformByPath(hpPath).GetComponent<Health>();
            if (hp == null || Math.Abs(hp.maxHealth - 30f) > 0.01f)
                throw new Exception("gameplay.attach_health max");

            var d1 = AgentHost.ExecuteDirect("gameplay.damage", new Dictionary<string, object>
            {
                { "path", hpPath },
                { "amount", 12d },
            });
            AssertOk(d1, "gameplay.damage");
            var s1 = MiniJson.AsObject(d1["data"]);
            if (Math.Abs(ReadDouble(s1, "current") - 18d) > 0.01d || MiniJson.GetBool(s1, "dead", false))
                throw new Exception("gameplay.damage current=" + ReadDouble(s1, "current"));
            steps.Add(Pass("gameplay.damage", "current=" + ReadDouble(s1, "current") + " dead=false max=30"));

            var d2 = AgentHost.ExecuteDirect("gameplay.damage", new Dictionary<string, object>
            {
                { "path", hpPath },
                { "amount", 20d },
            });
            AssertOk(d2, "gameplay.damage lethal");
            var s2 = MiniJson.AsObject(d2["data"]);
            if (Math.Abs(ReadDouble(s2, "current")) > 0.01d || !MiniJson.GetBool(s2, "dead", false) || !hp.IsDead)
                throw new Exception("gameplay.damage not dead current=" + ReadDouble(s2, "current"));
            steps.Add(Pass("gameplay.attach_health", "max=30 then current=0 dead=true"));

            var pickup = AgentHost.ExecuteDirect("gameplay.create_pickup", new Dictionary<string, object>
            {
                { "name", "_UA_Gp_Pickup" },
                { "itemId", "coin" },
                { "amount", 1 },
            });
            AssertOk(pickup, "gameplay.create_pickup");
            var pickupData = MiniJson.AsObject(pickup["data"]);
            var pickupPath = MiniJson.GetString(pickupData, "path", "");
            var pickupGo = AgentCommands.FindTransformByPath(pickupPath);
            var pk = pickupGo != null ? pickupGo.GetComponent<Pickup>() : null;
            var pkCol = pickupGo != null ? pickupGo.GetComponent<Collider>() : null;
            if (pk == null || pk.itemId != "coin" || pk.amount != 1 || pkCol == null || !pkCol.isTrigger)
                throw new Exception("gameplay.create_pickup bad");

            var use = AgentHost.ExecuteDirect("gameplay.create_interactable", new Dictionary<string, object>
            {
                { "name", "_UA_Gp_Use" },
            });
            AssertOk(use, "gameplay.create_interactable");
            var useData = MiniJson.AsObject(use["data"]);
            var usePath = MiniJson.GetString(useData, "path", "");
            var useGo = AgentCommands.FindTransformByPath(usePath);
            var inter = useGo != null ? useGo.GetComponent<Interactable>() : null;
            var useCol = useGo != null ? useGo.GetComponent<Collider>() : null;
            if (inter == null || inter.prompt != "按 E 交互" || useCol == null || !useCol.isTrigger)
                throw new Exception("gameplay.create_interactable bad");

            var info = AgentHost.ExecuteDirect("gameplay.info", null);
            AssertOk(info, "gameplay.info");
            var infoData = MiniJson.AsObject(info["data"]);
            var healthN = UnityEngine.Object.FindObjectsOfType<Health>(true).Length;
            var pickupN = UnityEngine.Object.FindObjectsOfType<Pickup>(true).Length;
            var useN = UnityEngine.Object.FindObjectsOfType<Interactable>(true).Length;
            if (GpCount(infoData, "health") != healthN || GpCount(infoData, "pickups") != pickupN || GpCount(infoData, "interactables") != useN)
                throw new Exception("gameplay.info count mismatch");
            if (!GpHas(infoData, "health", hpPath) || !GpHas(infoData, "pickups", pickupPath) || !GpHas(infoData, "interactables", usePath))
                throw new Exception("gameplay.info missing path");
            steps.Add(Pass("gameplay.create_pickup", "path=" + pickupPath
                + " item=coin amount=1 trigger=true color=" + MiniJson.GetString(pickupData, "color", "")));
            steps.Add(Pass("gameplay.create_interactable", "path=" + usePath
                + " prompt=" + inter.prompt + " trigger=true color=" + MiniJson.GetString(useData, "color", "")));
            steps.Add(Pass("gameplay.info", "health=" + healthN + " pickups=" + pickupN + " interactables=" + useN));

            var player = AgentHost.ExecuteDirect("go.create", new Dictionary<string, object> { { "name", "_UA_Gp_Player" } });
            AssertOk(player, "go.create player");
            var playerPath = MiniJson.GetString(MiniJson.AsObject(player["data"]), "path", "");
            var cam = AgentHost.ExecuteDirect("go.create", new Dictionary<string, object>
            {
                { "name", "Cam" },
                { "parent", playerPath },
            });
            AssertOk(cam, "go.create cam");
            var camPath = MiniJson.GetString(MiniJson.AsObject(cam["data"]), "path", "");
            ExpectOk("component.add", new Dictionary<string, object>
            {
                { "path", camPath },
                { "type", "UnityEngine.Camera" },
            }, steps);

            var bind = AgentHost.ExecuteDirect("gameplay.attach_interactor", new Dictionary<string, object> { { "path", playerPath } });
            AssertOk(bind, "gameplay.attach_interactor");
            var bindData = MiniJson.AsObject(bind["data"]);
            var actor = AgentCommands.FindTransformByPath(playerPath).GetComponent<PlayerInteractor>();
            if (actor == null || actor.camera == null || Math.Abs(actor.range - 3f) > 0.01f)
                throw new Exception("gameplay.attach_interactor camera/range");
            if (MiniJson.GetString(bindData, "cameraPath", "") != camPath)
                throw new Exception("gameplay.attach_interactor cameraPath");
            steps.Add(Pass("gameplay.attach_interactor", "range=3 camera=" + camPath));

            foreach (var name in new[] { "_UA_Gp_Hp", "_UA_Gp_Pickup", "_UA_Gp_Use", "_UA_Gp_Player" })
            {
                var left = GameObject.Find(name);
                if (left != null) UnityEngine.Object.DestroyImmediate(left);
                if (GameObject.Find(name) != null) throw new Exception("gameplay leftover " + name);
            }
        }

        static void RunUiFlowSelfTest(List<object> steps)
        {
            var panel = new GameObject("_UA_Flow_Panel");
            var resumeGo = MakeFlowButton(panel.transform, "Resume");
            var quitGo = MakeFlowButton(panel.transform, "Quit");
            var panelPath = AgentCommands.GetHierarchyPath(panel.transform);

            var attach = AgentHost.ExecuteDirect("uiflow.attach_pause", new Dictionary<string, object>
            {
                { "panelPath", panelPath },
            });
            AssertOk(attach, "uiflow.attach_pause");
            var attachData = MiniJson.AsObject(attach["data"]);
            var ctrlPath = MiniJson.GetString(attachData, "path", "");
            var retPanel = MiniJson.GetString(attachData, "panel", "");
            var ctrlGo = AgentCommands.FindTransformByPath(ctrlPath);
            var menu = ctrlGo != null ? ctrlGo.GetComponent<PauseMenu>() : null;
            var resumeBtn = resumeGo.GetComponent<Button>();
            var quitBtn = quitGo.GetComponent<Button>();
            var resumeN = resumeBtn != null ? resumeBtn.onClick.GetPersistentEventCount() : 0;
            var quitN = quitBtn != null ? quitBtn.onClick.GetPersistentEventCount() : 0;
            if (menu == null || menu.panel != panel || resumeN <= 0 || quitN <= 0)
                throw new Exception("uiflow.attach_pause listeners resume=" + resumeN + " quit=" + quitN);
            if (panel.activeSelf) throw new Exception("uiflow.attach_pause panel still active");
            if (ctrlGo == null || !ctrlGo.gameObject.activeSelf)
                throw new Exception("uiflow.attach_pause controller inactive");
            if (panel.GetComponent<PauseMenu>() != null)
                throw new Exception("uiflow.attach_pause menu on panel");
            if (retPanel != panelPath) throw new Exception("uiflow.attach_pause panel path");
            steps.Add(Pass("uiflow.attach_pause", "controller=" + ctrlPath + " active=true panel=" + panelPath
                + " active=false listeners=" + ReadNum(attachData, "listeners")
                + " resume=" + resumeN + " quit=" + quitN
                + " method=" + resumeBtn.onClick.GetPersistentMethodName(0) + "/" + quitBtn.onClick.GetPersistentMethodName(0)));

            var audio = AgentHost.ExecuteDirect("uiflow.create_audio_manager", new Dictionary<string, object>
            {
                { "name", "_UA_Flow_Audio" },
            });
            AssertOk(audio, "uiflow.create_audio_manager");
            var audioPath = MiniJson.GetString(MiniJson.AsObject(audio["data"]), "path", "");
            var audioT = AgentCommands.FindTransformByPath(audioPath);
            var manager = audioT != null ? audioT.GetComponent<AudioManager>() : null;
            var sources = audioT != null ? audioT.GetComponents<AudioSource>() : null;
            if (manager == null || sources == null || sources.Length < 2 || manager.sfxSource == null || manager.musicSource == null)
                throw new Exception("uiflow.create_audio_manager sources");
            if (manager.sfxSource == manager.musicSource)
                throw new Exception("uiflow.create_audio_manager same source");
            steps.Add(Pass("uiflow.create_audio_manager", "path=" + audioPath
                + " sources=" + sources.Length
                + " sfx=" + manager.sfxSource.playOnAwake
                + " musicLoop=" + manager.musicSource.loop));

            var info = AgentHost.ExecuteDirect("uiflow.info", null);
            AssertOk(info, "uiflow.info");
            var infoData = MiniJson.AsObject(info["data"]);
            var pauseN = UnityEngine.Object.FindObjectsOfType<PauseMenu>(true).Length;
            var audioN = UnityEngine.Object.FindObjectsOfType<AudioManager>(true).Length;
            if (GpCount(infoData, "pauseMenus") != pauseN || GpCount(infoData, "audioManagers") != audioN)
                throw new Exception("uiflow.info count mismatch");
            if (!GpHas(infoData, "pauseMenus", ctrlPath) || !GpHas(infoData, "audioManagers", audioPath))
                throw new Exception("uiflow.info missing path");
            steps.Add(Pass("uiflow.info", "pause=" + pauseN + " audio=" + audioN));

            if (ctrlGo != null) UnityEngine.Object.DestroyImmediate(ctrlGo.gameObject);
            UnityEngine.Object.DestroyImmediate(panel);
            if (audioT != null) UnityEngine.Object.DestroyImmediate(audioT.gameObject);
            Time.timeScale = 1f;
            if (GameObject.Find("_UA_Flow_Panel") != null || GameObject.Find("_UA_Flow_Panel_PauseController") != null)
                throw new Exception("uiflow pause leftover");
            if (GameObject.Find("_UA_Flow_Audio") != null)
                throw new Exception("uiflow audio leftover");
        }

        static void RunDialogueSelfTest(List<object> steps)
        {
            string dialoguePath = null;
            string questPath = null;
            GameObject host = null;
            try
            {
                var lines = new List<object>
                {
                    new Dictionary<string, object> { { "speaker", "A" }, { "text", "Hi" } },
                    new Dictionary<string, object> { { "speaker", "B" }, { "text", "Bye" } },
                };
                var created = AgentHost.ExecuteDirect("dialogue.create", new Dictionary<string, object>
                {
                    { "name", "_UA_SelfTest_Dialogue" },
                    { "lines", lines },
                });
                AssertOk(created, "dialogue.create");
                dialoguePath = MiniJson.GetString(MiniJson.AsObject(created["data"]), "path", "");
                var dialogue = AssetDatabase.LoadAssetAtPath<DialogueAsset>(dialoguePath);
                if (dialogue == null || dialogue.lines == null || dialogue.lines.Length != 2)
                    throw new Exception("dialogue.create lines=" + (dialogue == null || dialogue.lines == null ? -1 : dialogue.lines.Length));
                steps.Add(Pass("dialogue.create", "path=" + dialoguePath
                    + " lines=" + dialogue.lines.Length
                    + " " + dialogue.lines[0].speaker + ":" + dialogue.lines[0].text
                    + "," + dialogue.lines[1].speaker + ":" + dialogue.lines[1].text));

                var dInfo = AgentHost.ExecuteDirect("dialogue.info", null);
                AssertOk(dInfo, "dialogue.info");
                if (!AssetListed(MiniJson.AsObject(dInfo["data"]), "dialogues", dialoguePath))
                    throw new Exception("dialogue.info missing " + dialoguePath);
                steps.Add(Pass("dialogue.info", "count=" + ReadNum(MiniJson.AsObject(dInfo["data"]), "count")));

                var objectives = new List<object> { "find", "return" };
                var questResp = AgentHost.ExecuteDirect("quest.create", new Dictionary<string, object>
                {
                    { "name", "_UA_SelfTest_Quest" },
                    { "id", "ua_quest" },
                    { "title", "Self Test" },
                    { "objectives", objectives },
                });
                AssertOk(questResp, "quest.create");
                questPath = MiniJson.GetString(MiniJson.AsObject(questResp["data"]), "path", "");
                var quest = AssetDatabase.LoadAssetAtPath<QuestAsset>(questPath);
                if (quest == null || quest.objectives == null || quest.objectives.Length != 2)
                    throw new Exception("quest.create objectives");
                steps.Add(Pass("quest.create", "path=" + questPath
                    + " objectives=" + quest.objectives.Length
                    + " " + quest.objectives[0] + "," + quest.objectives[1]
                    + " id=" + quest.id));

                var qInfo = AgentHost.ExecuteDirect("quest.info", null);
                AssertOk(qInfo, "quest.info");
                if (!AssetListed(MiniJson.AsObject(qInfo["data"]), "quests", questPath))
                    throw new Exception("quest.info missing " + questPath);
                steps.Add(Pass("quest.info", "count=" + ReadNum(MiniJson.AsObject(qInfo["data"]), "count")));

                host = new GameObject("_UA_Dlg_Host");
                var hostPath = AgentCommands.GetHierarchyPath(host.transform);
                var runnerResp = AgentHost.ExecuteDirect("dialogue.attach_runner", new Dictionary<string, object>
                {
                    { "path", hostPath },
                    { "dialoguePath", dialoguePath },
                });
                AssertOk(runnerResp, "dialogue.attach_runner");
                var runner = host.GetComponent<DialogueRunner>();
                if (runner == null || runner.dialogue != dialogue)
                    throw new Exception("dialogue.attach_runner asset mismatch");
                steps.Add(Pass("dialogue.attach_runner", "path=" + hostPath + " dialogue=" + dialoguePath));

                var logResp = AgentHost.ExecuteDirect("quest.attach_log", new Dictionary<string, object>
                {
                    { "path", hostPath },
                });
                AssertOk(logResp, "quest.attach_log");
                var log = host.GetComponent<QuestLog>();
                if (log == null) throw new Exception("quest.attach_log missing");
                steps.Add(Pass("quest.attach_log", "path=" + hostPath + " active=" + (log.active == null ? 0 : log.active.Count)));
            }
            finally
            {
                if (!string.IsNullOrEmpty(dialoguePath)) AssetDatabase.DeleteAsset(dialoguePath);
                if (!string.IsNullOrEmpty(questPath)) AssetDatabase.DeleteAsset(questPath);
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
                AssetDatabase.SaveAssets();
            }
            if (!string.IsNullOrEmpty(dialoguePath) && AssetDatabase.LoadAssetAtPath<DialogueAsset>(dialoguePath) != null)
                throw new Exception("dialogue asset still exists");
            if (!string.IsNullOrEmpty(questPath) && AssetDatabase.LoadAssetAtPath<QuestAsset>(questPath) != null)
                throw new Exception("quest asset still exists");
            if (GameObject.Find("_UA_Dlg_Host") != null)
                throw new Exception("dialogue host still exists");
        }

        static bool AssetListed(Dictionary<string, object> data, string key, string assetPath)
        {
            if (data == null || !data.TryGetValue(key, out var v) || v == null || v is string) return false;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return false;
            foreach (var item in list)
            {
                if (string.Equals(Convert.ToString(item), assetPath, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        static GameObject MakeFlowButton(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<Button>();
            var textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);
            var text = textGo.AddComponent<Text>();
            text.text = name;
            text.font = FlowFont();
            text.color = Color.white;
            return go;
        }

        static Font FlowFont()
        {
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) return font;
            }
            catch { /* ignore */ }
            try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch { return null; }
        }

        static int GpCount(Dictionary<string, object> info, string key)
        {
            var box = info != null ? MiniJson.AsObject(info[key]) : null;
            return ReadNum(box, "count");
        }

        static bool GpHas(Dictionary<string, object> info, string key, string path)
        {
            var box = info != null ? MiniJson.AsObject(info[key]) : null;
            if (box == null || !box.TryGetValue("paths", out var v) || v == null || v is string) return false;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return false;
            foreach (var item in list)
            {
                if (string.Equals(Convert.ToString(item), path, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        static bool L10nHasText(Dictionary<string, object> scanData, string path, string text)
        {
            if (scanData == null || !scanData.TryGetValue("entries", out var v) || v == null || v is string) return false;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return false;
            foreach (var item in list)
            {
                var row = MiniJson.AsObject(item);
                if (row == null) continue;
                if (!string.Equals(MiniJson.GetString(row, "path", ""), path, StringComparison.Ordinal)) continue;
                return string.Equals(MiniJson.GetString(row, "text", ""), text, StringComparison.Ordinal);
            }
            return false;
        }

        static string GetSceneText(string path)
        {
            var tr = AgentCommands.FindTransformByPath(path);
            if (tr == null) return null;
            var tmp = tr.GetComponent<TMPro.TMP_Text>();
            if (tmp != null) return tmp.text;
            var ui = tr.GetComponent<UnityEngine.UI.Text>();
            return ui != null ? ui.text : null;
        }

        static bool PlayerListed(Dictionary<string, object> data, string path)
        {
            if (data == null || !data.TryGetValue("players", out var v) || v == null || v is string) return false;
            var list = v as System.Collections.IEnumerable;
            if (list == null) return false;
            foreach (var item in list)
            {
                var row = MiniJson.AsObject(item);
                if (row == null) continue;
                if (!string.Equals(MiniJson.GetString(row, "path", ""), path, StringComparison.Ordinal)) continue;
                return MiniJson.GetBool(row, "hasCamera", false);
            }
            return false;
        }

        static void RunPerfSelfTest(List<object> steps)
        {
            var snapResp = AgentHost.ExecuteDirect("perf.snapshot", null);
            AssertOk(snapResp, "perf.snapshot");
            var snap = MiniJson.AsObject(snapResp["data"]);
            var counts = MiniJson.AsObject(snap["counts"]);
            var mem = MiniJson.AsObject(snap["memoryMB"]);
            var objects = ReadNum(counts, "objects");
            var reserved = ReadDouble(mem, "totalReserved");
            if (objects <= 0) throw new Exception("perf.snapshot objects=" + objects);
            if (reserved <= 0) throw new Exception("perf.snapshot totalReserved=" + reserved);
            steps.Add(Pass("perf.snapshot", "objects=" + objects
                + " reservedMB=" + reserved
                + " allocMB=" + ReadDouble(mem, "totalAllocated")
                + " tris=" + ReadNum(counts, "triangles")
                + " texMB=" + ReadDouble(snap, "textureMemoryMB")
                + " fps=" + ReadDouble(snap, "fps")));

            var getResp = AgentHost.ExecuteDirect("perf.quality", null);
            AssertOk(getResp, "perf.quality");
            var current = MiniJson.AsObject(getResp["data"]);
            if (!current.ContainsKey("level") || !current.ContainsKey("vSync") || !current.ContainsKey("antiAliasing")
                || !current.ContainsKey("shadows") || !current.ContainsKey("textureQuality"))
                throw new Exception("perf.quality missing fields");
            var level = ReadNum(current, "level");
            var names = QualitySettings.names;
            var target = names != null && names.Length > 1 ? 1 : 0;
            var setResp = AgentHost.ExecuteDirect("perf.quality", new Dictionary<string, object> { { "level", target } });
            AssertOk(setResp, "perf.quality");
            var setData = MiniJson.AsObject(setResp["data"]);
            if (!MiniJson.GetBool(setData, "applied", false))
                throw new Exception("perf.quality applied=false");
            if (ReadNum(setData, "level") != target)
                throw new Exception("perf.quality level=" + ReadNum(setData, "level"));
            var back = AgentHost.ExecuteDirect("perf.quality", new Dictionary<string, object> { { "level", level } });
            AssertOk(back, "perf.quality");
            steps.Add(Pass("perf.quality", "level " + level + " -> " + target + " restored"));

            var auditResp = AgentHost.ExecuteDirect("perf.audit", null);
            AssertOk(auditResp, "perf.audit");
            var audit = MiniJson.AsObject(auditResp["data"]);
            var issues = audit["issues"] as System.Collections.IList;
            if (issues == null) throw new Exception("perf.audit issues missing");
            steps.Add(Pass("perf.audit", "issues=" + issues.Count + " scanned=" + ReadNum(audit, "scanned")));
        }

        static double ReadDouble(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.TryGetValue(key, out var v) || v == null) return 0;
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is int i) return i;
            if (v is long l) return l;
            return MiniJson.GetDouble(data, key);
        }

        static int ReadNum(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.TryGetValue(key, out var v) || v == null) return 0;
            if (v is int i) return i;
            if (v is long l) return (int)l;
            if (v is double d) return (int)d;
            if (v is float f) return (int)f;
            return MiniJson.GetInt(data, key);
        }

        static void ExpectOk(string cmd, Dictionary<string, object> args, List<object> steps)
        {
            var resp = AgentHost.ExecuteDirect(cmd, args);
            AssertOk(resp, cmd);
            steps.Add(Pass(cmd, "ok"));
        }

        static void ExpectTestStatus(List<object> steps)
        {
            var resp = AgentHost.ExecuteDirect("test.status", null);
            if (IsOk(resp))
            {
                var data = MiniJson.AsObject(resp["data"]);
                if (data == null || !data.ContainsKey("running") || !data.ContainsKey("completed"))
                    throw new Exception("test.status missing running/completed");
                steps.Add(Pass("test.status", "ok"));
                return;
            }
            var err = resp != null && resp.ContainsKey("error") ? MiniJson.AsObject(resp["error"]) : null;
            var code = MiniJson.GetString(err, "code", "");
            if (!string.Equals(code, "NOT_READY", StringComparison.Ordinal))
                throw new Exception("test.status failed: " + code + " " + MiniJson.GetString(err, "message"));
            steps.Add(Pass("test.status", "NOT_READY"));
        }

        static void ExpectCode(string cmd, Dictionary<string, object> args, string code, List<object> steps)
        {
            var resp = AgentHost.ExecuteDirect(cmd, args);
            if (IsOk(resp))
                throw new Exception(cmd + " expected error " + code + " but ok");
            var err = MiniJson.AsObject(resp["error"]);
            var got = MiniJson.GetString(err, "code", "");
            if (!string.Equals(got, code, StringComparison.Ordinal))
                throw new Exception(cmd + " expected " + code + " got " + got + " msg=" + MiniJson.GetString(err, "message"));
            steps.Add(Pass(cmd, code));
        }

        static void AssertOk(Dictionary<string, object> resp, string label)
        {
            if (!IsOk(resp))
            {
                var err = resp != null && resp.ContainsKey("error") ? MiniJson.AsObject(resp["error"]) : null;
                var code = MiniJson.GetString(err, "code", "?");
                var msg = MiniJson.GetString(err, "message", "?");
                throw new Exception(label + " failed: " + code + " " + msg);
            }
        }

        static bool IsOk(Dictionary<string, object> resp)
        {
            return resp != null && resp.TryGetValue("ok", out var o) && o is bool b && b;
        }

        static Dictionary<string, object> Pass(string name, string detail)
        {
            return new Dictionary<string, object>
            {
                { "name", name },
                { "ok", true },
                { "detail", detail ?? "" },
            };
        }
    }
}
