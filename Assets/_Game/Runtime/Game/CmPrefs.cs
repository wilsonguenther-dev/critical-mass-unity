using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CriticalMass.Runtime
{
    /// <summary>
    /// rough-spots pass 2: the game's one door to its saved settings (the PlayerPrefs API, same names and defaults).
    /// <para>A player build: every call goes straight to UnityEngine.PlayerPrefs (the shipped store, com.drivia.criticalmass
    /// on macOS), exactly as before.</para>
    /// <para>The Unity Editor: the game never writes the Editor's PlayerPrefs store ("unity.Drivia.Critical Mass") and can
    /// never reach the shipped one. Values live in memory and in a small file, editor-prefs.txt, in the Editor's own data
    /// folder (GamePaths.PersistentRoot, "Critical Mass (Editor)"), written on Save and when Play ends. Tests can point it at
    /// their own file or memory only (SetFileForTests).</para>
    /// Main thread only, like PlayerPrefs.
    /// </summary>
    public static class CmPrefs
    {
        public const string EditorFileName = "editor-prefs.txt";

#if UNITY_EDITOR
        static Dictionary<string, object> _map;
        static string _file;
        static bool _fileSet, _dirty, _hooked;

        static string FilePath
        {
            get
            {
                if (!_fileSet) { _file = Path.Combine(global::CriticalMass.Runtime.Game.GamePaths.PersistentRoot, EditorFileName); _fileSet = true; }
                return _file;
            }
        }

        static Dictionary<string, object> Map
        {
            get
            {
                if (_map == null)
                {
                    _map = Load(FilePath);
                    if (!_hooked) { _hooked = true; Application.quitting += Save; }
                }
                return _map;
            }
        }

        /// <summary>Tests: use this file (null = memory only) and forget what was loaded.</summary>
        public static void SetFileForTests(string path)
        {
            _file = path;
            _fileSet = true;
            _map = null;
            _dirty = false;
        }

        /// <summary>The Editor store's file (null when memory only).</summary>
        public static string EditorFile => FilePath;

        static Dictionary<string, object> Load(string path)
        {
            var m = new Dictionary<string, object>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return m;
            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] p = line.Split('\t');
                    if (p.Length != 3 || p[0].Length != 1) continue;
                    string k = Uri.UnescapeDataString(p[1]), v = Uri.UnescapeDataString(p[2]);
                    switch (p[0][0])
                    {
                        case 'i': if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) m[k] = i; break;
                        case 'f': if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) m[k] = f; break;
                        case 's': m[k] = v; break;
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[CM.Prefs] Editor prefs unreadable, starting empty: " + e.GetType().Name); }
            return m;
        }

        static void Put(string key, object v)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            Map[key] = v;
            _dirty = true;
        }

        static T Get<T>(string key, T def)
        {
            if (key != null && Map.TryGetValue(key, out object v) && v is T t) return t;
            return def;
        }

        public static void SetInt(string key, int value) => Put(key, value);
        public static void SetFloat(string key, float value) => Put(key, value);
        public static void SetString(string key, string value) => Put(key, value ?? "");
        public static int GetInt(string key, int defaultValue) => Get(key, defaultValue);
        public static int GetInt(string key) => Get(key, 0);
        public static float GetFloat(string key, float defaultValue) => Get(key, defaultValue);
        public static float GetFloat(string key) => Get(key, 0f);
        public static string GetString(string key, string defaultValue) => Get(key, defaultValue);
        public static string GetString(string key) => Get(key, "");
        public static bool HasKey(string key) => key != null && Map.ContainsKey(key);

        public static void DeleteKey(string key)
        {
            if (key != null && Map.Remove(key)) _dirty = true;
        }

        public static void DeleteAll()
        {
            Map.Clear();
            _dirty = true;
        }

        public static void Save()
        {
            if (!_dirty || _map == null) return;
            _dirty = false;
            string path = FilePath;
            if (string.IsNullOrEmpty(path)) return;
            if (global::CriticalMass.Runtime.Game.GamePaths.IsInsidePlayerRoot(path)) { Debug.LogWarning("[CM.Prefs] Editor: refused to write prefs into the player's folder " + path); return; }
            try
            {
                var sb = new StringBuilder();
                var keys = new List<string>(_map.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (string k in keys)
                {
                    object v = _map[k];
                    char t = v is int ? 'i' : v is float ? 'f' : 's';
                    string s = v is int iv ? iv.ToString(CultureInfo.InvariantCulture) : v is float fv ? fv.ToString("R", CultureInfo.InvariantCulture) : (string)v;
                    sb.Append(t).Append('\t').Append(Uri.EscapeDataString(k)).Append('\t').Append(Uri.EscapeDataString(s)).Append('\n');
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString());
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogWarning("[CM.Prefs] Editor prefs not saved: " + e.GetType().Name + ": " + e.Message); }
        }
#else
        public static void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public static void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);
        public static void SetString(string key, string value) => PlayerPrefs.SetString(key, value);
        public static int GetInt(string key, int defaultValue) => PlayerPrefs.GetInt(key, defaultValue);
        public static int GetInt(string key) => PlayerPrefs.GetInt(key);
        public static float GetFloat(string key, float defaultValue) => PlayerPrefs.GetFloat(key, defaultValue);
        public static float GetFloat(string key) => PlayerPrefs.GetFloat(key);
        public static string GetString(string key, string defaultValue) => PlayerPrefs.GetString(key, defaultValue);
        public static string GetString(string key) => PlayerPrefs.GetString(key);
        public static bool HasKey(string key) => PlayerPrefs.HasKey(key);
        public static void DeleteKey(string key) => PlayerPrefs.DeleteKey(key);
        public static void DeleteAll() => PlayerPrefs.DeleteAll();
        public static void Save() => PlayerPrefs.Save();
#endif
    }
}
