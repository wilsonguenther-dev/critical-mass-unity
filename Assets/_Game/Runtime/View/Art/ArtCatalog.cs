using System;
using UnityEngine;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>One imported model and the clips that drive it (its own, or the ClipSource's).</summary>
    [Serializable]
    public sealed class ArtEntry
    {
        public string Key;
        public GameObject Model;
        public AnimationClip[] Clips = new AnimationClip[0];

        public AnimationClip Clip(string name)
        {
            if (Clips == null) return null;
            foreach (AnimationClip c in Clips) if (c != null && c.name == name) return c;
            return null;
        }
    }

    /// <summary>
    /// The runtime handle on the imported art: ArtMapping keys to the FBX model prefabs and their clips. Written only by the
    /// editor (CriticalMass.Editor.Art.ArtCatalogBuilder, run by ProjectSetup.Apply and tools/art-sync.sh); lives in
    /// Resources so a player build carries exactly the mapped art. A key with no model falls back to the grey-box view.
    /// </summary>
    public sealed class ArtCatalog : ScriptableObject
    {
        public const string ResourcePath = "Art/ArtCatalog";
        public const string AssetPath = "Assets/_Game/Resources/" + ResourcePath + ".asset";

        public ArtEntry[] Entries = new ArtEntry[0];

        private static ArtCatalog _loaded;

        /// <summary>The catalog in Resources, or null (never built: every view stays grey-box).</summary>
        public static ArtCatalog Load()
        {
            if (_loaded == null) _loaded = Resources.Load<ArtCatalog>(ResourcePath);
            return _loaded;
        }

        public ArtEntry Find(string key)
        {
            if (key == null || Entries == null) return null;
            foreach (ArtEntry e in Entries) if (e != null && e.Key == key && e.Model != null) return e;
            return null;
        }
    }
}
