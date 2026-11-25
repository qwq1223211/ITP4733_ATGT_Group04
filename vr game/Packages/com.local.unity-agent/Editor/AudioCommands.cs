using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace Local.UnityAgent
{
    /// <summary>AudioSource / AudioMixer bridge commands.</summary>
    public static class AudioCommands
    {
        public static bool TryHandle(string cmd, Dictionary<string, object> args, out object data)
        {
            data = null;
            switch (cmd)
            {
                case "audio.source.add": data = AddSource(args); break;
                case "audio.source.set": data = SetSource(args); break;
                case "audio.play": data = Play(args); break;
                case "audio.info": data = Info(args); break;
                case "audio.mixer.set": data = MixerSet(args); break;
                case "audio.mixer.info": data = MixerInfo(args); break;
                default: return false;
            }
            return true;
        }

        static Dictionary<string, object> AddSource(Dictionary<string, object> args)
        {
            var go = FindGo(MiniJson.GetString(args, "object", ""));
            var source = go.GetComponent<AudioSource>();
            if (source == null) source = Undo.AddComponent<AudioSource>(go);
            Undo.RecordObject(source, "UnityAgent.audio.source.add");

            var clipPath = MiniJson.GetString(args, "clip", "");
            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PathUtil.ToAssetPath(clipPath));
                if (clip == null) throw new AgentException("NOT_FOUND", "audio clip not found: " + clipPath);
                source.clip = clip;
            }
            source.volume = (float)MiniJson.GetDouble(args, "volume", source.volume);
            source.pitch = (float)MiniJson.GetDouble(args, "pitch", source.pitch);
            source.loop = MiniJson.GetBool(args, "loop", source.loop);
            source.playOnAwake = MiniJson.GetBool(args, "playOnAwake", source.playOnAwake);
            source.spatialBlend = (float)MiniJson.GetDouble(args, "spatialBlend", source.spatialBlend);
            EditorUtility.SetDirty(source);
            return InfoFor(go, source);
        }

        static Dictionary<string, object> SetSource(Dictionary<string, object> args)
        {
            var go = FindGo(MiniJson.GetString(args, "object", ""));
            var source = go.GetComponent<AudioSource>();
            if (source == null) throw new AgentException("NOT_FOUND", "AudioSource not found on " + MiniJson.GetString(args, "object", ""));
            Undo.RecordObject(source, "UnityAgent.audio.source.set");

            var property = MiniJson.GetString(args, "property", "");
            if (string.IsNullOrEmpty(property)) throw new AgentException("BAD_ARGS", "property is required");
            if (args == null || !args.TryGetValue("value", out var value))
                throw new AgentException("BAD_ARGS", "value is required");

            switch (property)
            {
                case "volume": source.volume = ForFloat(value, source.volume); break;
                case "pitch": source.pitch = ForFloat(value, source.pitch); break;
                case "loop": source.loop = ForBool(value, source.loop); break;
                case "mute": source.mute = ForBool(value, source.mute); break;
                case "playOnAwake": source.playOnAwake = ForBool(value, source.playOnAwake); break;
                case "spatialBlend": source.spatialBlend = ForFloat(value, source.spatialBlend); break;
                case "clip":
                    var path = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PathUtil.ToAssetPath(path));
                    if (clip == null) throw new AgentException("NOT_FOUND", "audio clip not found: " + path);
                    source.clip = clip;
                    break;
                default:
                    throw new AgentException("BAD_ARGS", "unsupported audio property: " + property);
            }
            EditorUtility.SetDirty(source);
            return InfoFor(go, source);
        }

        static Dictionary<string, object> Play(Dictionary<string, object> args)
        {
            var go = FindGo(MiniJson.GetString(args, "object", ""));
            var source = go.GetComponent<AudioSource>();
            if (source == null) throw new AgentException("NOT_FOUND", "AudioSource not found on " + MiniJson.GetString(args, "object", ""));
            var clipPath = MiniJson.GetString(args, "clip", "");
            if (!string.IsNullOrEmpty(clipPath))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(PathUtil.ToAssetPath(clipPath));
                if (clip == null) throw new AgentException("NOT_FOUND", "audio clip not found: " + clipPath);
                source.PlayOneShot(clip, (float)MiniJson.GetDouble(args, "volume", 1));
            }
            else if (source.clip != null)
            {
                source.Play();
            }
            else
            {
                throw new AgentException("BAD_ARGS", "no clip assigned; pass clip or assign one first");
            }
            return InfoFor(go, source);
        }

        static Dictionary<string, object> Info(Dictionary<string, object> args)
        {
            var rawObject = MiniJson.GetString(args, "object", "");
            if (!string.IsNullOrEmpty(rawObject))
            {
                var go = FindGo(rawObject);
                var source = go.GetComponent<AudioSource>();
                if (source == null) throw new AgentException("NOT_FOUND", "AudioSource not found on " + rawObject);
                return InfoFor(go, source);
            }

            var rows = new List<object>();
            var sources = UnityEngine.Object.FindObjectsOfType<AudioSource>(true);
            foreach (var source in sources)
            {
                if (source == null) continue;
                rows.Add(InfoFor(source.gameObject, source));
            }
            return new Dictionary<string, object> { { "count", rows.Count }, { "sources", rows } };
        }

        static Dictionary<string, object> MixerSet(Dictionary<string, object> args)
        {
            var mixer = LoadMixer(args);
            var parameter = MiniJson.GetString(args, "parameter", "");
            if (string.IsNullOrEmpty(parameter)) throw new AgentException("BAD_ARGS", "parameter is required");
            if (args == null || !args.TryGetValue("value", out var value))
                throw new AgentException("BAD_ARGS", "value is required");
            var ok = mixer.SetFloat(parameter, ForFloat(value, 0));
            EditorUtility.SetDirty(mixer);
            return new Dictionary<string, object>
            {
                { "mixer", AssetDatabase.GetAssetPath(mixer) },
                { "parameter", parameter },
                { "set", ok },
            };
        }

        static Dictionary<string, object> MixerInfo(Dictionary<string, object> args)
        {
            var mixer = LoadMixer(args);
            var groups = new List<object>();
            foreach (var group in mixer.FindMatchingGroups(""))
                groups.Add(group.name);
            return new Dictionary<string, object>
            {
                { "mixer", AssetDatabase.GetAssetPath(mixer) },
                { "groups", groups },
            };
        }

        static AudioMixer LoadMixer(Dictionary<string, object> args)
        {
            var raw = MiniJson.GetString(args, "mixer", "");
            if (string.IsNullOrEmpty(raw)) throw new AgentException("BAD_ARGS", "mixer is required");
            var path = PathUtil.ToAssetPath(raw);
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(path);
            if (mixer == null) throw new AgentException("NOT_FOUND", "AudioMixer not found: " + path);
            return mixer;
        }

        static Dictionary<string, object> InfoFor(GameObject go, AudioSource source)
        {
            return new Dictionary<string, object>
            {
                { "object", AgentCommands.GetHierarchyPath(go.transform) },
                { "clip", source.clip != null ? source.clip.name : "" },
                { "volume", source.volume },
                { "pitch", source.pitch },
                { "loop", source.loop },
                { "mute", source.mute },
                { "playOnAwake", source.playOnAwake },
                { "spatialBlend", source.spatialBlend },
                { "playing", source.isPlaying },
            };
        }

        static float ForFloat(object value, float fallback)
        {
            if (value == null) return fallback;
            if (value is bool b) return b ? 1f : 0f;
            return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        static bool ForBool(object value, bool fallback)
        {
            if (value == null) return fallback;
            if (value is bool b) return b;
            return Convert.ToBoolean(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        static GameObject FindGo(string rawPath)
        {
            var path = (rawPath ?? "").Replace('\\', '/').Trim('/');
            if (path.Length == 0) throw new AgentException("BAD_ARGS", "object is required");
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (string.Equals(AgentCommands.GetHierarchyPath(transform), path, StringComparison.Ordinal))
                        return transform.gameObject;
                }
            }
            throw new AgentException("NOT_FOUND", "object not found: " + rawPath);
        }
    }
}
