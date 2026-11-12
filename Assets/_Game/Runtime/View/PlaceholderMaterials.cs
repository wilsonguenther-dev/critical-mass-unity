using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// Grey-box materials: one clone of Resources/Placeholder/PlaceholderLit (made by F-07's
    /// ProjectSetup.ApplyPlaceholderMaterial) per palette hex, cached. Never Shader.Find, never the render pipeline's
    /// default material at runtime (it is null in a player build).
    /// </summary>
    public static class PlaceholderMaterials
    {
        private const string BasePath = "Placeholder/PlaceholderLit";

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Material _base;

        /// <summary>The placeholder material tinted to hex ("#RRGGBB"), one shared instance per hex.</summary>
        public static Material Get(string hex)
        {
            // Domain reload is disabled on entering Play Mode, so a cached Material may have been destroyed: Unity's
            // == null catches that and the entry is rebuilt.
            if (Cache.TryGetValue(hex, out Material cached) && cached != null) return cached;

            if (_base == null)
            {
                _base = UnityEngine.Resources.Load<Material>(BasePath);
                if (_base == null) throw new InvalidOperationException("run ProjectSetup.Apply");
            }

            var m = new Material(_base);
            if (!ColorUtility.TryParseHtmlString(hex, out Color c)) throw new ArgumentException("not an html colour: " + hex, nameof(hex));
            m.SetColor("_BaseColor", c);
            m.name = "Placeholder " + hex;
            Cache[hex] = m;
            return m;
        }
    }
}
