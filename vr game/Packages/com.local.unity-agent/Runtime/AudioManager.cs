using UnityEngine;

namespace Local.UnityAgent.Runtime
{
    /// <summary>音效和音乐。进 Play 后跨场景保留。</summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        public AudioSource sfxSource;
        public AudioSource musicSource;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                if (Application.isPlaying) Destroy(gameObject);
                return;
            }
            Instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void PlaySfx(AudioClip clip, float volume = 1f)
        {
            if (clip == null || sfxSource == null) return;
            sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
        }

        public void PlayMusic(AudioClip clip, bool loop = true, float volume = 0.6f)
        {
            if (musicSource == null || clip == null) return;
            musicSource.loop = loop;
            musicSource.volume = Mathf.Clamp01(volume);
            musicSource.clip = clip;
            musicSource.Play();
        }

        public void StopMusic()
        {
            if (musicSource == null) return;
            musicSource.Stop();
        }

        public void SetMusicVolume(float volume)
        {
            if (musicSource == null) return;
            musicSource.volume = Mathf.Clamp01(volume);
        }
    }
}
