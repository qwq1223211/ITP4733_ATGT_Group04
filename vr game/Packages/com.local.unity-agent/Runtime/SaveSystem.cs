using System;
using System.IO;
using UnityEngine;

namespace Local.UnityAgent.Runtime
{
    /// <summary>JSON 槽位存档。写临时文件再替换，不吞异常。</summary>
    public static class SaveSystem
    {
        public static string RootDir => Path.Combine(Application.persistentDataPath, "saves");

        public static string PathFor(string slot)
        {
            return Path.Combine(RootDir, CheckSlot(slot) + ".json");
        }

        public static bool Exists(string slot)
        {
            return File.Exists(PathFor(slot));
        }

        public static void Save<T>(string slot, T data, int version = 1)
        {
            SaveRaw(slot, JsonUtility.ToJson(data), version);
        }

        public static T Load<T>(string slot, T fallback = default)
        {
            if (!Exists(slot)) return fallback;
            var env = ReadEnvelope(slot);
            if (string.IsNullOrEmpty(env.payload)) return fallback;
            return JsonUtility.FromJson<T>(env.payload);
        }

        public static string LoadRaw(string slot)
        {
            if (!Exists(slot)) return null;
            return ReadEnvelope(slot).payload;
        }

        public static void SaveRaw(string slot, string payloadJson, int version = 1)
        {
            var path = PathFor(slot);
            var env = new Envelope
            {
                version = version,
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                payload = payloadJson ?? "",
            };
            AtomicWrite(path, JsonUtility.ToJson(env));
        }

        public static bool Delete(string slot)
        {
            var path = PathFor(slot);
            if (!File.Exists(path)) return false;
            File.Delete(path);
            return true;
        }

        public static string[] List()
        {
            if (!Directory.Exists(RootDir)) return new string[0];
            var files = Directory.GetFiles(RootDir, "*.json");
            var names = new string[files.Length];
            for (var i = 0; i < files.Length; i++)
                names[i] = Path.GetFileNameWithoutExtension(files[i]);
            Array.Sort(names, StringComparer.Ordinal);
            return names;
        }

        public static void DeleteAll()
        {
            if (!Directory.Exists(RootDir)) return;
            var files = Directory.GetFiles(RootDir, "*.json");
            for (var i = 0; i < files.Length; i++)
                File.Delete(files[i]);
        }

        public static Envelope ReadEnvelope(string slot)
        {
            var text = File.ReadAllText(PathFor(slot));
            var env = JsonUtility.FromJson<Envelope>(text);
            if (env == null) throw new InvalidDataException("存档不是合法 envelope: " + slot);
            return env;
        }

        public static string CheckSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot))
                throw new ArgumentException("slot 不能为空");
            for (var i = 0; i < slot.Length; i++)
            {
                var c = slot[i];
                var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                    || c == '-' || c == '_' || c == '.';
                if (!ok) throw new ArgumentException("slot 只允许字母数字以及 - _ . : " + slot);
            }
            return slot;
        }

        static void AtomicWrite(string path, string text)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            var tmp = path + ".tmp";
            File.WriteAllText(tmp, text);
            if (!File.Exists(path))
            {
                File.Move(tmp, path);
                return;
            }
            var bak = path + ".bak";
            try
            {
                File.Replace(tmp, path, bak, true);
            }
            catch
            {
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            if (File.Exists(bak)) File.Delete(bak);
        }

        [Serializable]
        public class Envelope
        {
            public int version;
            public string savedAtUtc;
            public string payload;
        }
    }
}
