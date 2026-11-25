using System;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Local.UnityAgent.Runtime
{
    /// <summary>按场景名加载。不在 Build Settings 里就抛异常。</summary>
    public static class SceneLoader
    {
        public static void Load(string sceneName)
        {
            EnsureListed(sceneName);
            SceneManager.LoadScene(sceneName);
        }

        public static AsyncOperation LoadAsync(string sceneName)
        {
            EnsureListed(sceneName);
            return SceneManager.LoadSceneAsync(sceneName);
        }

        static void EnsureListed(string sceneName)
        {
            if (string.IsNullOrWhiteSpace(sceneName))
                throw new ArgumentException("场景名不能为空");
            var count = SceneManager.sceneCountInBuildSettings;
            for (var i = 0; i < count; i++)
            {
                var path = SceneUtility.GetScenePathByBuildIndex(i);
                var name = Path.GetFileNameWithoutExtension(path);
                if (string.Equals(name, sceneName, StringComparison.Ordinal)) return;
            }
            throw new InvalidOperationException("Build Settings 里找不到场景: " + sceneName);
        }
    }
}
