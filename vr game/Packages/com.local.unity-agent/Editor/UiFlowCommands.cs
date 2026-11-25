using System;
using System.Collections.Generic;
using Local.UnityAgent.Runtime;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Local.UnityAgent
{
    /// <summary>暂停菜单接线与音频管理器。不保存场景。</summary>
    public static class UiFlowCommands
    {
        [MenuItem("Tools/Unity Agent/Create Audio Manager")]
        public static void MenuAudio()
        {
            try
            {
                var data = CreateAudio(null);
                UnityEngine.Debug.Log("[UnityAgent] Create Audio Manager path=" + MiniJson.GetString(data, "path"));
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError("[UnityAgent] Create Audio Manager failed: " + ex.Message);
            }
        }

        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            if (cmd == "uiflow.attach_pause") { data = AttachPause(args); return true; }
            if (cmd == "uiflow.create_audio_manager") { data = CreateAudio(args); return true; }
            if (cmd == "uiflow.info") { data = Info(); return true; }
            return false;
        }

        public static Dictionary<string, object> AttachPause(Dictionary<string, object> args)
        {
            var panelPath = MiniJson.GetString(args, "panelPath", null);
            if (string.IsNullOrEmpty(panelPath)) throw new AgentException("BAD_ARGS", "panelPath 必填");
            var panelT = AgentCommands.FindTransformByPath(panelPath);
            if (panelT == null) throw new AgentException("NOT_FOUND", "panel not found: " + panelPath);
            var resumeName = MiniJson.GetString(args, "resumeButtonName", "Resume");
            var quitName = MiniJson.GetString(args, "quitButtonName", "Quit");
            if (string.IsNullOrEmpty(resumeName)) resumeName = "Resume";
            if (string.IsNullOrEmpty(quitName)) quitName = "Quit";

            var ctrlName = panelT.name + "_PauseController";
            var ctrlT = FindPauseController(panelT, ctrlName);
            if (ctrlT == null)
            {
                var ctrlGo = new GameObject(ctrlName);
                ctrlGo.transform.SetParent(panelT.parent, false);
                ctrlT = ctrlGo.transform;
                Undo.RegisterCreatedObjectUndo(ctrlGo, "UnityAgent.uiflow.attach_pause");
            }

            var onPanel = panelT.GetComponent<PauseMenu>();
            if (onPanel != null) UnityEngine.Object.DestroyImmediate(onPanel);

            var menu = ctrlT.GetComponent<PauseMenu>();
            if (menu == null) menu = ctrlT.gameObject.AddComponent<PauseMenu>();
            menu.panel = panelT.gameObject;

            var resume = FindButton(panelT, resumeName);
            var quit = FindButton(panelT, quitName);
            if (resume == null) throw new AgentException("NOT_FOUND", "button not found: " + resumeName);
            if (quit == null) throw new AgentException("NOT_FOUND", "button not found: " + quitName);

            UnityEventTools.AddPersistentListener(resume.onClick, menu.Resume);
            UnityEventTools.AddPersistentListener(quit.onClick, menu.Quit);
            var resumeN = resume.onClick.GetPersistentEventCount();
            var quitN = quit.onClick.GetPersistentEventCount();

            panelT.gameObject.SetActive(false);
            ctrlT.gameObject.SetActive(true);
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(ctrlT) },
                { "panel", AgentCommands.GetHierarchyPath(panelT) },
                { "resume", AgentCommands.GetHierarchyPath(resume.transform) },
                { "quit", AgentCommands.GetHierarchyPath(quit.transform) },
                { "listeners", resumeN + quitN },
            };
        }

        static Transform FindPauseController(Transform panelT, string ctrlName)
        {
            if (panelT.parent != null)
            {
                var child = panelT.parent.Find(ctrlName);
                if (child != null) return child;
                return null;
            }
            var scene = SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root == panelT.gameObject) continue;
                if (string.Equals(root.name, ctrlName, StringComparison.Ordinal)) return root.transform;
            }
            return null;
        }

        public static Dictionary<string, object> CreateAudio(Dictionary<string, object> args)
        {
            var name = MiniJson.GetString(args, "name", "AudioManager");
            if (string.IsNullOrWhiteSpace(name)) name = "AudioManager";
            var go = new GameObject(name);
            var manager = go.AddComponent<AudioManager>();
            var sfx = go.AddComponent<AudioSource>();
            var music = go.AddComponent<AudioSource>();
            sfx.playOnAwake = false;
            music.playOnAwake = false;
            music.loop = true;
            manager.sfxSource = sfx;
            manager.musicSource = music;
            Undo.RegisterCreatedObjectUndo(go, "UnityAgent.uiflow.create_audio_manager");
            return new Dictionary<string, object>
            {
                { "path", AgentCommands.GetHierarchyPath(go.transform) },
            };
        }

        public static Dictionary<string, object> Info()
        {
            return new Dictionary<string, object>
            {
                { "pauseMenus", Group<PauseMenu>() },
                { "audioManagers", Group<AudioManager>() },
            };
        }

        static Button FindButton(Transform root, string name)
        {
            var buttons = root.GetComponentsInChildren<Button>(true);
            for (var i = 0; i < buttons.Length; i++)
            {
                if (buttons[i] != null && buttons[i].gameObject.name == name) return buttons[i];
            }
            return null;
        }

        static Dictionary<string, object> Group<T>() where T : Component
        {
            var paths = new List<object>();
            foreach (var c in UnityEngine.Object.FindObjectsOfType<T>(true))
            {
                if (c == null) continue;
                paths.Add(AgentCommands.GetHierarchyPath(c.transform));
            }
            return new Dictionary<string, object>
            {
                { "count", paths.Count },
                { "paths", paths },
            };
        }
    }
}
