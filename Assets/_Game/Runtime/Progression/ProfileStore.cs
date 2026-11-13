using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace CriticalMass.Runtime.Progression
{
    /// <summary>How the last Load found the profile.</summary>
    public enum ProfileLoadSource
    {
        /// <summary>No file yet: a fresh profile.</summary>
        New,
        /// <summary>profile.json read and parsed.</summary>
        Main,
        /// <summary>profile.json missing or unreadable; profile.json.bak used.</summary>
        Backup,
        /// <summary>Both files unreadable: a fresh profile (the bad main file is kept as profile.json.corrupt).</summary>
        ResetAfterCorruption,
    }

    /// <summary>
    /// Reads and writes one PlayerProfile as versioned JSON in a folder (the game uses Application.persistentDataPath/
    /// profile). Writes are atomic: the JSON goes to profile.json.tmp, is flushed to disk, then replaces profile.json with
    /// the previous good file kept as profile.json.bak (File.Replace, with a copy-and-move fallback where the platform
    /// has no replace). Loading tolerates corruption: an unreadable main file falls back to the .bak; when both are bad
    /// the profile starts fresh and the bad main file is kept as profile.json.corrupt for diagnosis. Never throws to the
    /// caller; Save returns false and logs a warning when the disk refuses.
    /// </summary>
    public sealed class ProfileStore
    {
        public const string FileName = "profile.json";

        public readonly string Folder;
        public string MainPath => Path.Combine(Folder, FileName);
        public string BackupPath => MainPath + ".bak";
        public string TempPath => MainPath + ".tmp";
        public string CorruptPath => MainPath + ".corrupt";

        public ProfileLoadSource LastLoadSource { get; private set; } = ProfileLoadSource.New;

        public ProfileStore(string folder)
        {
            Folder = folder;
        }

        /// <summary>Reads the profile (see the class summary for the fallback order). Always returns a sanitised profile.</summary>
        public PlayerProfile Load()
        {
            bool mainExists = File.Exists(MainPath);
            if (mainExists && TryRead(MainPath, out PlayerProfile p))
            {
                LastLoadSource = ProfileLoadSource.Main;
                return p;
            }
            if (TryRead(BackupPath, out p))
            {
                LastLoadSource = ProfileLoadSource.Backup;
                if (mainExists) Warn("profile.json was unreadable; restored the backup");
                return p;
            }
            if (mainExists)
            {
                KeepCorrupt();
                LastLoadSource = ProfileLoadSource.ResetAfterCorruption;
                Warn("profile.json and its backup were unreadable; starting a fresh profile");
            }
            else LastLoadSource = ProfileLoadSource.New;
            var fresh = new PlayerProfile();
            fresh.Sanitize();
            return fresh;
        }

        /// <summary>Writes the profile atomically; false when the disk refused (the old file stays as it was).</summary>
        public bool Save(PlayerProfile profile)
        {
            if (profile == null) return false;
            try
            {
                Directory.CreateDirectory(Folder);
                profile.Sanitize();
                byte[] bytes = new UTF8Encoding(false).GetBytes(profile.ToJson());
                using (var fs = new FileStream(TempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(bytes, 0, bytes.Length);
                    fs.Flush(true);
                }
                if (!File.Exists(MainPath))
                {
                    File.Move(TempPath, MainPath);
                    return true;
                }
                // A corrupt main file must not become the backup (it would push out the last good one).
                if (!TryRead(MainPath, out _))
                {
                    KeepCorrupt();
                    File.Delete(MainPath); // integration batch 2: File.Move never overwrites, so the save failed here
                    File.Move(TempPath, MainPath);
                    return true;
                }
                try
                {
                    File.Replace(TempPath, MainPath, BackupPath, true);
                }
                catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException)
                {
                    File.Copy(MainPath, BackupPath, true);
                    File.Delete(MainPath);
                    File.Move(TempPath, MainPath);
                }
                return true;
            }
            catch (Exception e)
            {
                Warn("could not save the profile: " + e.Message);
                try { if (File.Exists(TempPath)) File.Delete(TempPath); } catch (Exception) { /* best effort */ }
                return false;
            }
        }

        /// <summary>Parses one file; false for missing, empty, unparsable or a future schema this build cannot read.</summary>
        public static bool TryRead(string path, out PlayerProfile profile)
        {
            profile = null;
            try
            {
                if (!File.Exists(path)) return false;
                string json = File.ReadAllText(path, Encoding.UTF8);
                return TryParse(json, out profile);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool TryParse(string json, out PlayerProfile profile)
        {
            profile = null;
            if (string.IsNullOrWhiteSpace(json)) return false;
            string t = json.Trim();
            if (t[0] != '{' || t[t.Length - 1] != '}') return false;
            try
            {
                PlayerProfile p = JsonUtility.FromJson<PlayerProfile>(t);
                if (p == null || p.Version < 1) return false;
                if (!Migrate(p)) return false;
                p.Sanitize();
                profile = p;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Brings an older schema up to CurrentVersion in place; false for a newer schema (a later build wrote it: it is
        /// not overwritten from a fallback by accident, because Load then reads the .bak or starts fresh and keeps the file
        /// as .corrupt). Version 1 is the first schema; add a case per bump.
        /// </summary>
        public static bool Migrate(PlayerProfile p)
        {
            if (p.Version > PlayerProfile.CurrentVersion) return false;
            // switch (p.Version) { case 1: ...; p.Version = 2; goto case 2; }
            p.Version = PlayerProfile.CurrentVersion;
            return true;
        }

        void KeepCorrupt()
        {
            try { File.Copy(MainPath, CorruptPath, true); } catch (Exception) { /* best effort */ }
        }

        static void Warn(string message)
        {
            if (!Application.isBatchMode) Debug.LogWarning("[Profile] " + message);
        }
    }

    /// <summary>
    /// The game's one profile: loaded lazily from persistentDataPath/profile, saved on Commit. In batch mode (gates, the
    /// capture tool, the test runner) it is never read from or written to disk unless a test installs its own store, so a
    /// tool run never changes a player's file. Changed fires after every Commit.
    /// </summary>
    public static class Profile
    {
        public const string FolderName = "profile";

        static ProfileStore _store;
        static PlayerProfile _current;

        /// <summary>Raised after Commit (Barracks, coin chip and level bars re-read).</summary>
        public static event Action Changed;

        /// <summary>False in batch mode by default: the profile lives in memory only.</summary>
        public static bool DiskEnabled = !Application.isBatchMode;

        public static ProfileStore Store
        {
            get
            {
                if (_store == null) _store = new ProfileStore(Path.Combine(Application.persistentDataPath, FolderName));
                return _store;
            }
        }

        public static PlayerProfile Current
        {
            get
            {
                if (_current == null)
                {
                    if (DiskEnabled) _current = Store.Load();
                    else
                    {
                        _current = new PlayerProfile();
                        _current.Sanitize();
                    }
                }
                return _current;
            }
        }

        /// <summary>Saves the current profile (when the disk is enabled) and raises Changed.</summary>
        public static bool Commit()
        {
            bool ok = true;
            if (DiskEnabled && _current != null) ok = Store.Save(_current);
            Changed?.Invoke();
            return ok;
        }

        /// <summary>Tests: use a store in another folder (null = back to the default), dropping the loaded profile.</summary>
        public static void UseStore(ProfileStore store, bool diskEnabled)
        {
            _store = store;
            DiskEnabled = diskEnabled;
            _current = null;
        }

        // Domain reload is off on entering Play Mode: drop the cached profile so each play session reads the file.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _store = null;
            _current = null;
            Changed = null;
            DiskEnabled = !Application.isBatchMode;
        }
    }
}
