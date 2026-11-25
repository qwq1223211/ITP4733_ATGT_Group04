using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>资产导入检查 / 修复 / 报告（asset.profile|stats|audit|fix|importSettings）。</summary>
    public static class AssetCommands
    {
        const long MB = 1024L * 1024L;

        struct Profile
        {
            public string Name;
            public int MaxTexture;
            public long LargeBytes;
        }

        struct Rule
        {
            public string Code;
            public string Description;
            public string Severity;
            public bool Fixable;
        }

        static readonly Rule[] Rules =
        {
            new Rule { Code = "texture.readable", Description = "TextureImporter.isReadable=true 会把像素留在内存", Severity = "error", Fixable = true },
            new Rule { Code = "texture.max-size", Description = "maxTextureSize 超过 profile 预算（pc 2048 / mobile 1024 / web 1024）", Severity = "error", Fixable = true },
            new Rule { Code = "texture.uncompressed", Description = "textureCompression=Uncompressed", Severity = "error", Fixable = true },
            new Rule { Code = "texture.mipmap-ui", Description = "路径含 /UI/ 或 /Sprites/ 且开启了 mipmap", Severity = "warn", Fixable = true },
            new Rule { Code = "texture.npot", Description = "非 2 的幂且 npotScale 不是 ToNearest", Severity = "warn", Fixable = true },
            new Rule { Code = "model.readable", Description = "ModelImporter.isReadable=true", Severity = "error", Fixable = true },
            new Rule { Code = "model.mesh-compression", Description = "meshCompression=Off", Severity = "warn", Fixable = true },
            new Rule { Code = "model.import-materials", Description = "importMaterials 开启（materialImportMode != None），会生成内嵌材质副本", Severity = "warn", Fixable = false },
            new Rule { Code = "audio.streaming", Description = "文件 > 5MB 且 loadType=DecompressOnLoad，建议 Streaming", Severity = "warn", Fixable = true },
            new Rule { Code = "audio.force-mono", Description = "大于 1MB 的双声道且 forceToMono=false", Severity = "warn", Fixable = true },
            new Rule { Code = "material.no-shader", Description = "Material.shader == null", Severity = "error", Fixable = false },
            new Rule { Code = "prefab.missing-script", Description = "Prefab 上有缺失脚本（需显式 codes 才删除）", Severity = "error", Fixable = true },
            new Rule { Code = "asset.large", Description = "单文件超过 profile 阈值（pc 20MB / mobile 10MB / web 5MB）", Severity = "warn", Fixable = false },
        };

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            switch (cmd)
            {
                case "asset.profile": data = CmdProfile(); return true;
                case "asset.stats": data = CmdStats(args); return true;
                case "asset.audit": data = CmdAudit(args); return true;
                case "asset.fix": data = CmdFix(args); return true;
                case "asset.importSettings": data = CmdImportSettings(args); return true;
                default: return false;
            }
        }

        public static Dictionary<string, object> AuditProject()
        {
            return CmdAudit(new Dictionary<string, object>
            {
                { "folder", "Assets" },
                { "profile", "pc" },
                { "maxIssues", 200 },
            });
        }

        static Dictionary<string, object> CmdProfile()
        {
            var profiles = new Dictionary<string, object>();
            foreach (var name in new[] { "pc", "mobile", "web" })
            {
                var p = GetProfile(name);
                profiles[name] = new Dictionary<string, object>
                {
                    { "maxTextureSize", p.MaxTexture },
                    { "largeBytes", p.LargeBytes },
                };
            }

            var rules = new List<object>();
            foreach (var r in Rules)
            {
                rules.Add(new Dictionary<string, object>
                {
                    { "code", r.Code },
                    { "description", r.Description },
                    { "severity", r.Severity },
                    { "fixable", r.Fixable },
                });
            }

            return new Dictionary<string, object>
            {
                { "profiles", profiles },
                { "rules", rules },
            };
        }

        static Dictionary<string, object> CmdStats(Dictionary<string, object> args)
        {
            var folder = NormalizeFolder(MiniJson.GetString(args, "folder", "Assets"));
            var guids = AssetDatabase.FindAssets(string.Empty, new[] { folder });
            var byType = new Dictionary<string, long[]>(StringComparer.Ordinal);
            var files = new List<KeyValuePair<string, long>>();
            long totalBytes = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) continue;
                var bytes = FileBytes(path);
                totalBytes += bytes;
                var type = AssetDatabase.GetMainAssetTypeAtPath(path);
                var typeName = type != null ? type.Name : "Unknown";
                if (!byType.TryGetValue(typeName, out var acc))
                {
                    acc = new long[2];
                    byType[typeName] = acc;
                }
                acc[0]++;
                acc[1] += bytes;
                files.Add(new KeyValuePair<string, long>(path, bytes));
            }

            files.Sort((a, b) => b.Value.CompareTo(a.Value));
            var largest = new List<object>();
            var take = Math.Min(10, files.Count);
            for (var i = 0; i < take; i++)
            {
                var t = AssetDatabase.GetMainAssetTypeAtPath(files[i].Key);
                largest.Add(new Dictionary<string, object>
                {
                    { "path", files[i].Key },
                    { "bytes", files[i].Value },
                    { "type", t != null ? t.Name : "Unknown" },
                });
            }

            var types = new List<object>();
            foreach (var kv in byType)
            {
                types.Add(new Dictionary<string, object>
                {
                    { "type", kv.Key },
                    { "count", kv.Value[0] },
                    { "bytes", kv.Value[1] },
                });
            }

            return new Dictionary<string, object>
            {
                { "folder", folder },
                { "totalAssets", seen.Count - CountFolders(guids) },
                { "totalBytes", totalBytes },
                { "byType", types },
                { "largest", largest },
            };
        }

        static int CountFolders(string[] guids)
        {
            var n = 0;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || !seen.Add(path)) continue;
                if (AssetDatabase.IsValidFolder(path)) n++;
            }
            return n;
        }

        static Dictionary<string, object> CmdAudit(Dictionary<string, object> args)
        {
            var folder = NormalizeFolder(MiniJson.GetString(args, "folder", "Assets"));
            var profile = GetProfile(MiniJson.GetString(args, "profile", "pc"));
            var maxIssues = MiniJson.GetInt(args, "maxIssues", 200);
            if (maxIssues <= 0) maxIssues = 200;

            var all = Collect(folder, profile, out var scanned);
            var errors = 0;
            var warns = 0;
            foreach (var issue in all)
            {
                var sev = MiniJson.GetString(issue, "severity", "");
                if (sev == "error") errors++;
                else if (sev == "warn") warns++;
            }

            var trimmed = new List<object>();
            var n = Math.Min(maxIssues, all.Count);
            for (var i = 0; i < n; i++) trimmed.Add(all[i]);

            var data = new Dictionary<string, object>
            {
                { "profile", profile.Name },
                { "folder", folder },
                { "scanned", scanned },
                { "counts", new Dictionary<string, object> { { "error", errors }, { "warn", warns } } },
                { "issues", trimmed },
            };
            data["reportPath"] = WriteReport("audit", data, null);
            return data;
        }

        static Dictionary<string, object> CmdFix(Dictionary<string, object> args)
        {
            var folder = NormalizeFolder(MiniJson.GetString(args, "folder", "Assets"));
            var profile = GetProfile(MiniJson.GetString(args, "profile", "pc"));
            var dryRun = !args.ContainsKey("dryRun") || MiniJson.GetBool(args, "dryRun", true);
            var codes = ParseCodes(args);
            var explicitMissing = codes != null && codes.Contains("prefab.missing-script");

            var all = Collect(folder, profile, out _);
            var planned = 0;
            var changed = 0;
            var skipped = new List<object>();
            var importers = new Dictionary<string, AssetImporter>(StringComparer.OrdinalIgnoreCase);
            var prefabs = new List<string>();
            var changeLog = new List<object>();

            foreach (var issue in all)
            {
                var code = MiniJson.GetString(issue, "code", "");
                var path = MiniJson.GetString(issue, "path", "");
                var fixable = issue.ContainsKey("fixable") && issue["fixable"] is bool b && b;
                if (!fixable)
                {
                    skipped.Add(Skip(path, code, "not auto-fixable"));
                    continue;
                }
                if (code == "prefab.missing-script" && !explicitMissing)
                {
                    skipped.Add(Skip(path, code, "requires explicit codes"));
                    continue;
                }
                if (codes != null && !codes.Contains(code))
                {
                    skipped.Add(Skip(path, code, "code not selected"));
                    continue;
                }

                planned++;
                if (dryRun) continue;

                try
                {
                    if (code == "prefab.missing-script")
                    {
                        if (!prefabs.Contains(path)) prefabs.Add(path);
                    }
                    else
                    {
                        var importer = AssetImporter.GetAtPath(path);
                        if (importer == null)
                            throw new AgentException("NOT_FOUND", "no importer: " + path);
                        ApplyFix(importer, code, profile);
                        importers[path] = importer;
                    }
                    changed++;
                    changeLog.Add(new Dictionary<string, object> { { "path", path }, { "code", code } });
                }
                catch (Exception ex)
                {
                    planned--;
                    skipped.Add(Skip(path, code, ex.Message));
                }
            }

            if (!dryRun)
            {
                foreach (var kv in importers)
                {
                    EditorUtility.SetDirty(kv.Value);
                    kv.Value.SaveAndReimport();
                }
                foreach (var path in prefabs)
                    FixMissingScripts(path);
                AssetDatabase.SaveAssets();
            }

            var recheck = Collect(folder, profile, out var scanned);
            var data = new Dictionary<string, object>
            {
                { "planned", planned },
                { "changed", changed },
                { "skipped", skipped },
                { "profile", profile.Name },
                { "folder", folder },
                { "dryRun", dryRun },
                { "changes", changeLog },
                { "recheckScanned", scanned },
                { "recheckCounts", CountSev(recheck) },
            };
            data["reportPath"] = WriteReport(dryRun ? "fix-dry" : "fix", data, recheck);
            return data;
        }

        static Dictionary<string, object> CmdImportSettings(Dictionary<string, object> args)
        {
            var path = MiniJson.GetString(args, "path", null);
            var type = MiniJson.GetString(args, "type", null);
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(type))
                throw new AgentException("BAD_ARGS", "path and type required");
            path = PathUtil.ToAssetPath(path);
            if (args == null || !args.TryGetValue("settings", out var raw) || !(raw is Dictionary<string, object> settings))
                throw new AgentException("BAD_ARGS", "settings object required");

            var importer = AssetImporter.GetAtPath(path);
            if (importer == null)
                throw new AgentException("NOT_FOUND", "no importer: " + path);

            var applied = new Dictionary<string, object>();
            if (type == "texture")
            {
                var tex = importer as TextureImporter;
                if (tex == null) throw new AgentException("BAD_ARGS", "not a texture: " + path);
                ApplyTextureSettings(tex, settings, applied);
            }
            else if (type == "model")
            {
                var model = importer as ModelImporter;
                if (model == null) throw new AgentException("BAD_ARGS", "not a model: " + path);
                ApplyModelSettings(model, settings, applied);
            }
            else if (type == "audio")
            {
                var audio = importer as AudioImporter;
                if (audio == null) throw new AgentException("BAD_ARGS", "not audio: " + path);
                ApplyAudioSettings(audio, settings, applied);
            }
            else
            {
                throw new AgentException("BAD_ARGS", "type must be texture|model|audio");
            }

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();
            AssetDatabase.SaveAssets();
            return new Dictionary<string, object>
            {
                { "path", path },
                { "type", type },
                { "applied", applied },
            };
        }

        static List<Dictionary<string, object>> Collect(string folder, Profile profile, out int scanned)
        {
            var issues = new List<Dictionary<string, object>>();
            var guids = AssetDatabase.FindAssets(
                "t:Texture2D t:Model t:AudioClip t:Material t:Prefab",
                new[] { folder });
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            scanned = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path) || !seen.Add(path))
                    continue;
                scanned++;
                Inspect(path, profile, issues);
            }
            return issues;
        }

        static void Inspect(string path, Profile profile, List<Dictionary<string, object>> issues)
        {
            var bytes = FileBytes(path);
            if (bytes > profile.LargeBytes)
            {
                issues.Add(Issue("asset.large", path,
                    "file is " + bytes + " bytes, budget " + profile.LargeBytes,
                    bytes.ToString()));
            }

            var importer = AssetImporter.GetAtPath(path);
            if (importer is TextureImporter tex)
                InspectTexture(tex, path, profile, issues);
            else if (importer is ModelImporter model)
                InspectModel(model, path, issues);
            else if (importer is AudioImporter audio)
                InspectAudio(audio, path, bytes, issues);

            var main = AssetDatabase.GetMainAssetTypeAtPath(path);
            if (main == typeof(Material) || path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null && mat.shader == null)
                    issues.Add(Issue("material.no-shader", path, "Material.shader is null", null));
            }

            if (main == typeof(GameObject) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go != null && PrefabHasMissing(go))
                    issues.Add(Issue("prefab.missing-script", path, "prefab has a missing script", null));
            }
        }

        static void InspectTexture(TextureImporter tex, string path, Profile profile, List<Dictionary<string, object>> issues)
        {
            if (tex.isReadable)
                issues.Add(Issue("texture.readable", path, "isReadable=true", "true"));
            if (tex.maxTextureSize > profile.MaxTexture)
                issues.Add(Issue("texture.max-size", path,
                    "maxTextureSize " + tex.maxTextureSize + " > " + profile.MaxTexture,
                    tex.maxTextureSize.ToString()));
            if (tex.textureCompression == TextureImporterCompression.Uncompressed)
                issues.Add(Issue("texture.uncompressed", path, "textureCompression=Uncompressed", "Uncompressed"));

            var norm = path.Replace('\\', '/');
            if (tex.mipmapEnabled &&
                (norm.IndexOf("/UI/", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 norm.IndexOf("/Sprites/", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                issues.Add(Issue("texture.mipmap-ui", path, "mipmapEnabled on UI/Sprites texture", "true"));
            }

            try
            {
                tex.GetSourceTextureWidthAndHeight(out var w, out var h);
                if ((w > 0 && h > 0) && (!IsPot(w) || !IsPot(h)) && tex.npotScale != TextureImporterNPOTScale.ToNearest)
                    issues.Add(Issue("texture.npot", path, "npot " + w + "x" + h + " scale=" + tex.npotScale, tex.npotScale.ToString()));
            }
            catch
            {
                // 尺寸读不到就跳过 npot，不把贴图 Load 进内存
            }
        }

        // 有意保留"可读网格"的路径。
        // 审计只看导入设置，判断不出"运行时到底要不要读顶点"，所以需要人工白名单。
        // 现状：Abyss/KitProps.Textured 会 Instantiate 网格后读 vertices/normals，
        // 按材质 tile（3.0/2.4/1.6 米）重投影 UV；同一网格有多种 tile，
        // 导入期没法预烘焙，只能运行时做 —— 关掉 Read/Write 会直接抛异常。
        static readonly string[] ReadableIntentional =
        {
            "Assets/Resources/DungeonKit/",
        };

        static bool ReadableIsIntentional(string path)
        {
            string p = path.Replace('\\', '/');
            for (int i = 0; i < ReadableIntentional.Length; i++)
                if (p.StartsWith(ReadableIntentional[i], System.StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static void InspectModel(ModelImporter model, string path, List<Dictionary<string, object>> issues)
        {
            if (model.isReadable && !ReadableIsIntentional(path))
                issues.Add(Issue("model.readable", path, "isReadable=true", "true"));
            if (model.meshCompression == ModelImporterMeshCompression.Off)
                issues.Add(Issue("model.mesh-compression", path, "meshCompression=Off", "Off"));
            if (model.materialImportMode != ModelImporterMaterialImportMode.None)
                issues.Add(Issue("model.import-materials", path, "materialImportMode=" + model.materialImportMode, model.materialImportMode.ToString()));
        }

        static void InspectAudio(AudioImporter audio, string path, long bytes, List<Dictionary<string, object>> issues)
        {
            var settings = audio.defaultSampleSettings;
            if (bytes > 5 * MB && settings.loadType == AudioClipLoadType.DecompressOnLoad)
                issues.Add(Issue("audio.streaming", path, "loadType=DecompressOnLoad and file > 5MB", "DecompressOnLoad"));

            if (bytes > MB && !audio.forceToMono && IsStereoFile(path))
                issues.Add(Issue("audio.force-mono", path, "stereo and forceToMono=false, file > 1MB", "forceToMono=false"));
        }

        static void ApplyFix(AssetImporter importer, string code, Profile profile)
        {
            switch (code)
            {
                case "texture.readable":
                    ((TextureImporter)importer).isReadable = false;
                    break;
                case "texture.max-size":
                    ((TextureImporter)importer).maxTextureSize = profile.MaxTexture;
                    break;
                case "texture.uncompressed":
                    ((TextureImporter)importer).textureCompression = TextureImporterCompression.Compressed;
                    break;
                case "texture.mipmap-ui":
                    ((TextureImporter)importer).mipmapEnabled = false;
                    break;
                case "texture.npot":
                    ((TextureImporter)importer).npotScale = TextureImporterNPOTScale.ToNearest;
                    break;
                case "model.readable":
                    ((ModelImporter)importer).isReadable = false;
                    break;
                case "model.mesh-compression":
                    ((ModelImporter)importer).meshCompression = ModelImporterMeshCompression.Medium;
                    break;
                case "audio.streaming":
                {
                    var audio = (AudioImporter)importer;
                    var s = audio.defaultSampleSettings;
                    s.loadType = AudioClipLoadType.Streaming;
                    audio.defaultSampleSettings = s;
                    break;
                }
                case "audio.force-mono":
                    ((AudioImporter)importer).forceToMono = true;
                    break;
                default:
                    throw new AgentException("BAD_ARGS", "no fixer for " + code);
            }
        }

        static void ApplyTextureSettings(TextureImporter tex, Dictionary<string, object> settings, Dictionary<string, object> applied)
        {
            // sprite / textureType 只在 TextureImporter 上处理。model、audio 的 settings 不读这些键，直接忽略。
            if (settings.ContainsKey("textureType"))
            {
                var name = MiniJson.GetString(settings, "textureType", "default");
                TextureImporterType kind;
                switch ((name ?? "").ToLowerInvariant())
                {
                    case "sprite": kind = TextureImporterType.Sprite; break;
                    case "default": kind = TextureImporterType.Default; break;
                    case "normalmap": kind = TextureImporterType.NormalMap; break;
                    default:
                        throw new AgentException("BAD_ARGS", "textureType must be sprite|default|normalMap");
                }
                tex.textureType = kind;
                if (kind == TextureImporterType.Sprite)
                    tex.spriteImportMode = SpriteImportMode.Single;
                applied["textureType"] = name;
            }
            if (settings.ContainsKey("spritePixelsPerUnit"))
            {
                var raw = settings["spritePixelsPerUnit"];
                var ppu = raw is double d ? (float)d : raw is int i ? i : Convert.ToSingle(raw);
                tex.spritePixelsPerUnit = ppu;
                applied["spritePixelsPerUnit"] = ppu;
            }
            if (settings.ContainsKey("spriteBorder"))
            {
                if (!(settings["spriteBorder"] is List<object> border) || border.Count < 4)
                    throw new AgentException("BAD_ARGS", "spriteBorder must be [left,bottom,right,top]");
                tex.spriteBorder = new Vector4(
                    Convert.ToSingle(border[0]),
                    Convert.ToSingle(border[1]),
                    Convert.ToSingle(border[2]),
                    Convert.ToSingle(border[3]));
                applied["spriteBorder"] = new List<object> { border[0], border[1], border[2], border[3] };
            }
            if (settings.ContainsKey("alphaIsTransparency"))
            {
                var v = MiniJson.GetBool(settings, "alphaIsTransparency", false);
                tex.alphaIsTransparency = v;
                applied["alphaIsTransparency"] = v;
            }
            if (settings.ContainsKey("readable") || settings.ContainsKey("isReadable"))
            {
                var key = settings.ContainsKey("readable") ? "readable" : "isReadable";
                var v = MiniJson.GetBool(settings, key, false);
                tex.isReadable = v;
                applied["readable"] = v;
            }
            if (settings.ContainsKey("maxTextureSize"))
            {
                var v = MiniJson.GetInt(settings, "maxTextureSize", tex.maxTextureSize);
                if (settings["maxTextureSize"] is int mi) v = mi;
                if (settings["maxTextureSize"] is long ml) v = (int)ml;
                tex.maxTextureSize = v;
                applied["maxTextureSize"] = v;
            }
            if (settings.ContainsKey("textureCompression"))
            {
                var raw = settings["textureCompression"];
                TextureImporterCompression c;
                if (raw is double dn)
                    c = (TextureImporterCompression)(int)dn;
                else if (raw is int ni)
                    c = (TextureImporterCompression)ni;
                else if (!Enum.TryParse(MiniJson.GetString(settings, "textureCompression", ""), true, out c))
                    throw new AgentException("BAD_ARGS", "bad textureCompression: " + raw);
                tex.textureCompression = c;
                applied["textureCompression"] = c.ToString();
            }
            if (settings.ContainsKey("mipmapEnabled"))
            {
                var v = MiniJson.GetBool(settings, "mipmapEnabled", false);
                tex.mipmapEnabled = v;
                applied["mipmapEnabled"] = v;
            }
            if (settings.ContainsKey("npotScale"))
            {
                var name = MiniJson.GetString(settings, "npotScale", "");
                if (!Enum.TryParse(name, true, out TextureImporterNPOTScale s))
                    throw new AgentException("BAD_ARGS", "bad npotScale: " + name);
                tex.npotScale = s;
                applied["npotScale"] = s.ToString();
            }
        }

        static void ApplyModelSettings(ModelImporter model, Dictionary<string, object> settings, Dictionary<string, object> applied)
        {
            if (settings.ContainsKey("readable") || settings.ContainsKey("isReadable"))
            {
                var key = settings.ContainsKey("readable") ? "readable" : "isReadable";
                var v = MiniJson.GetBool(settings, key, false);
                model.isReadable = v;
                applied["readable"] = v;
            }
            if (settings.ContainsKey("meshCompression"))
            {
                var name = MiniJson.GetString(settings, "meshCompression", "");
                if (!Enum.TryParse(name, true, out ModelImporterMeshCompression c))
                    throw new AgentException("BAD_ARGS", "bad meshCompression: " + name);
                model.meshCompression = c;
                applied["meshCompression"] = c.ToString();
            }
            if (settings.ContainsKey("importMaterials"))
            {
                var on = MiniJson.GetBool(settings, "importMaterials", false);
                model.materialImportMode = on
                    ? ModelImporterMaterialImportMode.ImportViaMaterialDescription
                    : ModelImporterMaterialImportMode.None;
                applied["importMaterials"] = on;
            }
        }

        static void ApplyAudioSettings(AudioImporter audio, Dictionary<string, object> settings, Dictionary<string, object> applied)
        {
            if (settings.ContainsKey("loadType"))
            {
                var name = MiniJson.GetString(settings, "loadType", "");
                if (!Enum.TryParse(name, true, out AudioClipLoadType load))
                    throw new AgentException("BAD_ARGS", "bad loadType: " + name);
                var s = audio.defaultSampleSettings;
                s.loadType = load;
                audio.defaultSampleSettings = s;
                applied["loadType"] = load.ToString();
            }
            if (settings.ContainsKey("forceToMono"))
            {
                var v = MiniJson.GetBool(settings, "forceToMono", false);
                audio.forceToMono = v;
                applied["forceToMono"] = v;
            }
            if (settings.ContainsKey("compressionFormat"))
            {
                var name = MiniJson.GetString(settings, "compressionFormat", "");
                if (!Enum.TryParse(name, true, out AudioCompressionFormat fmt))
                    throw new AgentException("BAD_ARGS", "bad compressionFormat: " + name);
                var s = audio.defaultSampleSettings;
                s.compressionFormat = fmt;
                audio.defaultSampleSettings = s;
                applied["compressionFormat"] = fmt.ToString();
            }
        }

        static void FixMissingScripts(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                    GameObjectUtility.RemoveMonoBehavioursWithMissingScript(t.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        static bool PrefabHasMissing(GameObject root)
        {
            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            if (behaviours != null)
            {
                foreach (var b in behaviours)
                {
                    if (b == null) return true;
                }
            }
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) > 0)
                    return true;
            }
            return false;
        }

        static string WriteReport(string kind, Dictionary<string, object> data, List<Dictionary<string, object>> recheck)
        {
            var dir = PathUtil.CombineUnderRoot("Library/UnityAgent/reports");
            PathUtil.EnsureDir(dir);
            var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var baseName = "asset-audit-" + stamp;
            var jsonFull = Path.Combine(dir, baseName + ".json");
            var n = 2;
            while (File.Exists(jsonFull))
            {
                jsonFull = Path.Combine(dir, baseName + "-" + n + ".json");
                n++;
            }
            var mdFull = Path.ChangeExtension(jsonFull, ".md");

            var payload = new Dictionary<string, object>(data);
            payload["kind"] = kind;
            if (recheck != null)
            {
                var list = new List<object>();
                foreach (var issue in recheck) list.Add(issue);
                payload["recheckIssues"] = list;
            }

            File.WriteAllText(jsonFull, MiniJson.Stringify(payload), new UTF8Encoding(false));
            File.WriteAllText(mdFull, RenderMd(kind, data, recheck), new UTF8Encoding(false));
            return PathUtil.ToProjectRelative(jsonFull);
        }

        static string RenderMd(string kind, Dictionary<string, object> data, List<Dictionary<string, object>> recheck)
        {
            var sb = new StringBuilder();
            sb.Append("# asset ").Append(kind).Append('\n');
            sb.Append("- profile: ").Append(MiniJson.GetString(data, "profile", "")).Append('\n');
            sb.Append("- folder: ").Append(MiniJson.GetString(data, "folder", "")).Append('\n');
            if (data.ContainsKey("planned"))
                sb.Append("- planned: ").Append(data["planned"]).Append(" changed: ").Append(data["changed"]).Append('\n');
            if (data.TryGetValue("issues", out var issues) && issues is List<object> list)
            {
                sb.Append("\n## issues\n");
                foreach (var item in list)
                {
                    if (!(item is Dictionary<string, object> d)) continue;
                    sb.Append("- ").Append(MiniJson.GetString(d, "severity", ""))
                        .Append(" `").Append(MiniJson.GetString(d, "code", "")).Append("` ")
                        .Append(MiniJson.GetString(d, "path", "")).Append(" — ")
                        .Append(MiniJson.GetString(d, "message", "")).Append('\n');
                }
            }
            if (data.TryGetValue("changes", out var changes) && changes is List<object> cl)
            {
                sb.Append("\n## changed\n");
                foreach (var item in cl)
                {
                    if (!(item is Dictionary<string, object> d)) continue;
                    sb.Append("- ").Append(MiniJson.GetString(d, "code", ""))
                        .Append(" ").Append(MiniJson.GetString(d, "path", "")).Append('\n');
                }
            }
            if (recheck != null)
            {
                sb.Append("\n## recheck\n");
                sb.Append("remaining ").Append(recheck.Count).Append('\n');
            }
            return sb.ToString();
        }

        static Dictionary<string, object> CountSev(List<Dictionary<string, object>> issues)
        {
            var errors = 0;
            var warns = 0;
            foreach (var issue in issues)
            {
                var sev = MiniJson.GetString(issue, "severity", "");
                if (sev == "error") errors++;
                else if (sev == "warn") warns++;
            }
            return new Dictionary<string, object> { { "error", errors }, { "warn", warns } };
        }

        static Dictionary<string, object> Issue(string code, string path, string message, string current)
        {
            Rule rule = default;
            foreach (var r in Rules)
            {
                if (r.Code == code) { rule = r; break; }
            }
            var d = new Dictionary<string, object>
            {
                { "code", code },
                { "severity", rule.Severity ?? "warn" },
                { "path", path },
                { "message", message ?? "" },
                { "fixable", rule.Fixable },
            };
            if (!string.IsNullOrEmpty(current))
                d["current"] = current;
            return d;
        }

        static Dictionary<string, object> Skip(string path, string code, string reason)
        {
            return new Dictionary<string, object>
            {
                { "path", path ?? "" },
                { "code", code ?? "" },
                { "reason", reason ?? "" },
            };
        }

        static HashSet<string> ParseCodes(Dictionary<string, object> args)
        {
            if (args == null || !args.ContainsKey("codes") || args["codes"] == null)
                return null;
            if (!(args["codes"] is List<object> list))
                throw new AgentException("BAD_ARGS", "codes must be an array");
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in list)
            {
                var s = MiniJson.AsString(item);
                if (!string.IsNullOrEmpty(s)) set.Add(s);
            }
            return set;
        }

        static Profile GetProfile(string name)
        {
            switch ((name ?? "pc").ToLowerInvariant())
            {
                case "pc": return new Profile { Name = "pc", MaxTexture = 2048, LargeBytes = 20 * MB };
                case "mobile": return new Profile { Name = "mobile", MaxTexture = 1024, LargeBytes = 10 * MB };
                case "web": return new Profile { Name = "web", MaxTexture = 1024, LargeBytes = 5 * MB };
                default:
                    throw new AgentException("BAD_ARGS", "profile must be pc|mobile|web");
            }
        }

        static string NormalizeFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder)) folder = "Assets";
            folder = PathUtil.ToAssetPath(folder).Replace('\\', '/').TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(folder))
                throw new AgentException("NOT_FOUND", "folder not found: " + folder);
            return folder;
        }

        static long FileBytes(string assetPath)
        {
            try
            {
                var full = PathUtil.CombineUnderRoot(assetPath);
                if (!File.Exists(full)) return 0;
                return new FileInfo(full).Length;
            }
            catch
            {
                return 0;
            }
        }

        static bool IsPot(int n)
        {
            return n > 0 && (n & (n - 1)) == 0;
        }

        /// <summary>只读文件头判断双声道，不 Load AudioClip。非 wav/aiff 返回 false。</summary>
        static bool IsStereoFile(string assetPath)
        {
            try
            {
                var full = PathUtil.CombineUnderRoot(assetPath);
                if (!File.Exists(full)) return false;
                var ext = Path.GetExtension(full).ToLowerInvariant();
                using (var fs = File.OpenRead(full))
                {
                    var buf = new byte[64];
                    var n = fs.Read(buf, 0, buf.Length);
                    if (n < 24) return false;
                    if (ext == ".wav" && buf[0] == 'R' && buf[1] == 'I' && buf[2] == 'F' && buf[3] == 'F')
                    {
                        var channels = buf[22] | (buf[23] << 8);
                        return channels >= 2;
                    }
                }
            }
            catch
            {
                return false;
            }
            return false;
        }
    }
}
