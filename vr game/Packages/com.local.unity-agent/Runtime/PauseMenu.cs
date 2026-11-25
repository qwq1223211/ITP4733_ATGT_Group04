using UnityEngine;

namespace Local.UnityAgent.Runtime
{
    /// <summary>Esc 切换暂停。同一状态重复设置直接跳过。</summary>
    public class PauseMenu : MonoBehaviour
    {
        public GameObject panel;
        public bool IsPaused { get; private set; }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) Toggle();
        }

        public void SetPaused(bool paused)
        {
            var show = paused;
            if (IsPaused == paused && (panel == null || panel.activeSelf == show)) return;
            IsPaused = paused;
            Time.timeScale = paused ? 0f : 1f;
            if (panel != null) panel.SetActive(show);
        }

        public void Resume()
        {
            SetPaused(false);
        }

        public void Quit()
        {
            Application.Quit();
        }

        public void Toggle()
        {
            SetPaused(!IsPaused);
        }
    }
}
