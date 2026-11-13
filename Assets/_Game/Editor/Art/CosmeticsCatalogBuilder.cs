using System;
using System.Linq;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// Writes Resources/Cosmetics/CosmeticsCatalog.asset from every file under Assets/_Game/Art/Cosmetics that follows the
    /// Cosmetics name convention (&lt;Class&gt;/Head_&lt;Name&gt;.fbx, Emblem_&lt;Name&gt;.png, Mount_Emblem.fbx). Runs by itself
    /// after any import, move or delete under that folder, and from the menu. Idempotent: an unchanged catalog is not
    /// re-saved, so git stays clean. Files that break the convention are listed once as a warning and skipped.
    /// </summary>
    public sealed class CosmeticsCatalogBuilder : AssetPostprocessor
    {
        static bool _queued;

        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool touched = imported.Concat(deleted).Concat(moved).Concat(movedFrom)
                .Any(p => p.Replace('\\', '/').StartsWith(Cosmetics.Folder + "/", StringComparison.Ordinal));
            if (!touched || _queued) return;
            _queued = true;
            // Creating assets inside the import callback is not allowed: build right after it.
            EditorApplication.delayCall += () =>
            {
                _queued = false;
                Build();
            };
        }

        [MenuItem("Critical Mass/Rebuild Cosmetics Catalog")]
        public static void BuildMenu() => Build();

        public static CosmeticsCatalog Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Resources/Cosmetics")) AssetDatabase.CreateFolder("Assets/_Game/Resources", "Cosmetics");
            CosmeticAsset[] entries = new CosmeticAsset[0];
            if (AssetDatabase.IsValidFolder(Cosmetics.Folder))
            {
                string[] paths = AssetDatabase.FindAssets(string.Empty, new[] { Cosmetics.Folder })
                    .Select(AssetDatabase.GUIDToAssetPath).Where(p => !AssetDatabase.IsValidFolder(p)).Distinct()
                    .OrderBy(p => p, StringComparer.Ordinal).ToArray();
                var bad = paths.Where(p => !p.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
                                           !Cosmetics.TryParsePath(p, out _, out _, out _)).ToArray();
                if (bad.Length > 0) Debug.LogWarning("[Cosmetics] skipped (name convention): " + string.Join(", ", bad));
                entries = paths.Select(Entry).Where(e => e != null).ToArray();
            }
            var cat = AssetDatabase.LoadAssetAtPath<CosmeticsCatalog>(CosmeticsCatalog.AssetPath);
            bool created = cat == null;
            if (created)
            {
                cat = ScriptableObject.CreateInstance<CosmeticsCatalog>();
                cat.name = "CosmeticsCatalog";
                cat.Entries = entries;
                AssetDatabase.CreateAsset(cat, CosmeticsCatalog.AssetPath);
            }
            else if (!Same(cat.Entries, entries))
            {
                cat.Entries = entries;
                EditorUtility.SetDirty(cat);
                AssetDatabase.SaveAssetIfDirty(cat);
            }
            CosmeticsCatalog.Reload();
            return cat;
        }

        static CosmeticAsset Entry(string path)
        {
            if (!Cosmetics.TryParsePath(path, out ClassId cls, out string slot, out string name)) return null;
            var e = new CosmeticAsset { Class = cls.ToString(), Slot = slot, Name = name };
            if (slot == Cosmetics.EmblemSlot) e.Texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            else e.Model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            return e.Model != null || e.Texture != null ? e : null;
        }

        static bool Same(CosmeticAsset[] a, CosmeticAsset[] b)
        {
            if (a == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] == null) return false;
                if (a[i].Class != b[i].Class || a[i].Slot != b[i].Slot || a[i].Name != b[i].Name || a[i].Model != b[i].Model ||
                    a[i].Texture != b[i].Texture) return false;
            }
            return true;
        }
    }
}
