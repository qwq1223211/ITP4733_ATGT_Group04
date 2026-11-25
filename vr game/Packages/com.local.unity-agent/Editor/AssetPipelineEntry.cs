using System;
using UnityEditor;
using UnityEngine;

namespace Local.UnityAgent
{
    /// <summary>菜单与 batchmode 入口：Tools/Unity Agent/Asset Audit。</summary>
    public static class AssetPipelineEntry
    {
        [MenuItem("Tools/Unity Agent/Asset Audit")]
        public static void AuditMenu()
        {
            try
            {
                var data = AssetCommands.AuditProject();
                var counts = MiniJson.AsObject(data["counts"]);
                var errors = AsInt(counts, "error");
                var warns = AsInt(counts, "warn");
                var report = MiniJson.GetString(data, "reportPath", "");
                Debug.Log("[UnityAgent] Asset Audit scanned=" + AsInt(data, "scanned")
                    + " errors=" + errors + " warns=" + warns + " report=" + report);
            }
            catch (Exception ex)
            {
                Debug.LogError("[UnityAgent] Asset Audit failed: " + ex.Message);
            }
        }

        /// <summary>batchmode: -executeMethod Local.UnityAgent.AssetPipelineEntry.AuditAll</summary>
        public static void AuditAll()
        {
            try
            {
                var data = AssetCommands.AuditProject();
                var counts = MiniJson.AsObject(data["counts"]);
                var errors = AsInt(counts, "error");
                var report = MiniJson.GetString(data, "reportPath", "");
                var line = "[UnityAgent] Asset Audit report=" + report + " errors=" + errors;
                Debug.Log(line);
                Console.WriteLine(line);
                EditorApplication.Exit(errors > 0 ? 1 : 0);
            }
            catch (Exception ex)
            {
                Debug.LogError("[UnityAgent] Asset Audit failed: " + ex);
                Console.Error.WriteLine(ex);
                EditorApplication.Exit(1);
            }
        }

        static int AsInt(System.Collections.Generic.Dictionary<string, object> obj, string key)
        {
            if (obj == null || !obj.TryGetValue(key, out var v) || v == null) return 0;
            return Convert.ToInt32(v);
        }
    }
}
