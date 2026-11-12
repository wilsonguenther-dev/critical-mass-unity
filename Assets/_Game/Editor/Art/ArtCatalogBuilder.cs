using System;
using System.Linq;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Dressing;
using UnityEditor;
using UnityEngine;

namespace CriticalMass.Editor.Art
{
    /// <summary>
    /// Writes Resources/Art/ArtCatalog.asset from ArtMapping and the imported FBX files: each mapped model with its clips
    /// (its own, or its ClipSource's). Idempotent: an unchanged catalog is not re-saved, so git stays clean.
    /// </summary>
    public static class ArtCatalogBuilder
    {
        public static ArtCatalog Build()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Resources/Art")) AssetDatabase.CreateFolder("Assets/_Game/Resources", "Art");
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath);
            bool created = cat == null;
            if (created)
            {
                cat = ScriptableObject.CreateInstance<ArtCatalog>();
                cat.name = "ArtCatalog";
            }

            var entries = ArtMapping.Models
                .Select(spec => new ArtEntry
                {
                    Key = spec.Key,
                    Model = AssetDatabase.LoadAssetAtPath<GameObject>(spec.AssetPath),
                    Clips = Clips(spec.ClipSource ?? spec.AssetPath),
                })
                .Where(e => e.Model != null)
                .ToArray();

            if (created) AssetDatabase.CreateAsset(cat, ArtCatalog.AssetPath);
            if (created || !Same(cat.Entries, entries))
            {
                cat.Entries = entries;
                EditorUtility.SetDirty(cat);
                AssetDatabase.SaveAssetIfDirty(cat);
            }
            foreach (ModelSpec spec in ArtMapping.Models)
            {
                ArtEntry e = entries.FirstOrDefault(x => x.Key == spec.Key);
                Debug.Log(e == null
                    ? $"[ArtCatalog] {spec.Key}: MISSING {spec.AssetPath} (view falls back to grey-box)"
                    : $"[ArtCatalog] {spec.Key}: {spec.AssetPath} clips=[{string.Join(",", e.Clips.Select(c => c.name))}]");
            }
            BuildWorld();
            return cat;
        }

        /// <summary>
        /// Writes Resources/World/WorldKitCatalog.asset: every FBX under Art/Environment and Art/Bases by file name, sorted
        /// (the world view's model lookup). Idempotent like the art catalog.
        /// </summary>
        public static WorldKitCatalog BuildWorld()
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Game/Resources/World")) AssetDatabase.CreateFolder("Assets/_Game/Resources", "World");
            var pairs = AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Art/Environment", "Assets/_Game/Art/Bases" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => WorldKit.IsEnvironment(p) || WorldKit.IsBases(p))
                .Select(p => (name: System.IO.Path.GetFileNameWithoutExtension(p), model: AssetDatabase.LoadAssetAtPath<GameObject>(p)))
                .Where(x => x.model != null)
                .OrderBy(x => x.name, StringComparer.Ordinal).ToArray();
            var cat = AssetDatabase.LoadAssetAtPath<WorldKitCatalog>(WorldKitCatalog.AssetPath);
            bool created = cat == null;
            if (created)
            {
                cat = ScriptableObject.CreateInstance<WorldKitCatalog>();
                cat.name = "WorldKitCatalog";
                AssetDatabase.CreateAsset(cat, WorldKitCatalog.AssetPath);
            }
            string[] names = pairs.Select(x => x.name).ToArray();
            GameObject[] models = pairs.Select(x => x.model).ToArray();
            if (created || !cat.Names.SequenceEqual(names) || !cat.Models.SequenceEqual(models))
            {
                cat.Names = names;
                cat.Models = models;
                EditorUtility.SetDirty(cat);
                AssetDatabase.SaveAssetIfDirty(cat);
            }
            Debug.Log($"[ArtCatalog] world kit: {names.Length} models");
            return cat;
        }

        public static AnimationClip[] Clips(string path) =>
            AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal))
                .OrderBy(c => c.name, StringComparer.Ordinal).ToArray();

        static bool Same(ArtEntry[] a, ArtEntry[] b)
        {
            if (a == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].Key != b[i].Key || a[i].Model != b[i].Model) return false;
                if ((a[i].Clips?.Length ?? 0) != b[i].Clips.Length) return false;
                for (int j = 0; j < b[i].Clips.Length; j++) if (a[i].Clips[j] != b[i].Clips[j]) return false;
            }
            return true;
        }
    }

    /// <summary>Batch entry for tools/art-sync.sh: force re-import of every FBX under Assets/_Game/Art, then rebuild the catalog.</summary>
    public static class ArtSync
    {
        public static void Reimport()
        {
            try
            {
                ProjectSetup.Apply();
                string[] paths = AssetDatabase.FindAssets("t:Model", new[] { "Assets/_Game/Art" })
                    .Select(AssetDatabase.GUIDToAssetPath).Where(ArtImportPostprocessor.IsArt).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                AssetDatabase.StartAssetEditing();
                try
                {
                    foreach (string p in paths) AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                }
                finally
                {
                    AssetDatabase.StopAssetEditing();
                }
                AssetDatabase.Refresh();
                ArtCatalogBuilder.Build();
                AssetDatabase.SaveAssets();
                Debug.Log($"[ArtSync] re-imported {paths.Length} FBX file(s)");
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }
    }
}
