using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>One cosmetic art file found under Assets/_Game/Art/Cosmetics (see Cosmetics for the name convention).</summary>
    [Serializable]
    public sealed class CosmeticAsset
    {
        /// <summary>ClassId name ("Warrior").</summary>
        public string Class;
        /// <summary>"Head", "Emblem" or "Mount".</summary>
        public string Slot;
        /// <summary>The &lt;Name&gt; part of the file name (the item id).</summary>
        public string Name;
        public GameObject Model;
        public Texture2D Texture;
    }

    /// <summary>
    /// The runtime index of the cosmetic art (DG-27): written only by the editor (CriticalMass.Editor.Art.
    /// CosmeticsCatalogBuilder, automatically whenever a file under Art/Cosmetics is imported, or from the menu
    /// Critical Mass / Rebuild Cosmetics Catalog); lives in Resources so a player build carries exactly that art. Missing
    /// catalog = every cosmetic slot shows "coming soon".
    /// </summary>
    public sealed class CosmeticsCatalog : ScriptableObject
    {
        public const string ResourcePath = "Cosmetics/CosmeticsCatalog";
        public const string AssetPath = "Assets/_Game/Resources/" + ResourcePath + ".asset";

        public CosmeticAsset[] Entries = new CosmeticAsset[0];

        static CosmeticsCatalog _loaded;
        static bool _looked;

        public static CosmeticsCatalog Load()
        {
            if (_loaded == null && !_looked)
            {
                _looked = true;
                _loaded = Resources.Load<CosmeticsCatalog>(ResourcePath);
            }
            return _loaded;
        }

        /// <summary>Forget the cached catalog (the editor builder calls it after a rebuild).</summary>
        public static void Reload()
        {
            _loaded = null;
            _looked = false;
        }

        public CosmeticAsset Find(string cls, string slot, string name)
        {
            if (Entries == null || string.IsNullOrEmpty(name)) return null;
            foreach (CosmeticAsset e in Entries)
                if (e != null && e.Class == cls && e.Slot == slot && e.Name == name && (e.Model != null || e.Texture != null)) return e;
            return null;
        }

        /// <summary>Every item id present for a class and slot (a new list; menus only).</summary>
        public List<string> Ids(string cls, string slot)
        {
            var r = new List<string>();
            if (Entries == null) return r;
            foreach (CosmeticAsset e in Entries)
                if (e != null && e.Class == cls && e.Slot == slot && !string.IsNullOrEmpty(e.Name) && !r.Contains(e.Name)) r.Add(e.Name);
            return r;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Reload();
    }
}
