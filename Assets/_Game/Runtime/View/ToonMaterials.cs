using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.View
{
    /// <summary>
    /// CM/Toon and CM/ToonGround materials for the grey-box views (art direction 2026-11-12): clones of
    /// Resources/Toon/CMToon and Resources/Toon/CMToonGround (made by ProjectSetup.ApplyToonMaterials, so both shaders
    /// ship in a player build), one shared instance per look, cached. Never Shader.Find at runtime.
    /// Outline widths at 1080p (2.3): characters 3 px, props 2.5 px, structures 2 px, ground none.
    /// </summary>
    public static class ToonMaterials
    {
        public const string ToonPath = "Toon/CMToon";
        public const string GroundPath = "Toon/CMToonGround";

        public const float CharacterOutlinePx = 3f;
        public const float PropOutlinePx = 2.5f;
        public const float StructureOutlinePx = 2f;
        /// <summary>Character rim (2.2: kept on characters only, toward the team colour).</summary>
        public const float CharacterRim = 0.6f;

        /// <summary>The ground variants.</summary>
        public enum GroundKind { Grass, Water, Foam }

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Material _toon;
        private static Material _ground;

        /// <summary>A character (capsule, Kaiju): team colour body and rim, 3 px line.</summary>
        public static Material Character(string teamHex, bool convexPrimitive = false) =>
            Toon("char", teamHex, teamHex, CharacterOutlinePx, CharacterRim, convexPrimitive ? OutlineFromPivot : OutlineFromNormal);

        /// <summary>A prop (node, hill plate): 2.5 px line, no rim.</summary>
        public static Material Prop(string hex, bool convexPrimitive = true) =>
            Toon("prop", hex, hex, PropOutlinePx, 0f, convexPrimitive ? OutlineFromPivot : OutlineFromNormal);

        /// <summary>A structure (walls, bridges, barricades in the chunk meshes): 2 px line, no rim.</summary>
        public static Material Structure(string hex) => Toon("struct", hex, hex, StructureOutlinePx, 0f, OutlineFromUv3);

        /// <summary>CM/Toon _OutlineSource values: the vertex normal, away from the pivot, the mesh's UV3 direction.</summary>
        public const float OutlineFromNormal = 0f, OutlineFromPivot = 1f, OutlineFromUv3 = 2f;

        /// <summary>CM/Toon with base and team colours, an outline width (px at 1080p) and rim strength.</summary>
        public static Material Toon(string kind, string baseHex, string teamHex, float outlinePx, float rim, float outlineSource)
        {
            string key = kind + baseHex + teamHex + outlinePx + "/" + rim + "/" + outlineSource;
            // Domain reload is disabled on entering Play Mode, so a cached Material may have been destroyed: Unity's
            // == null catches that and the entry is rebuilt.
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;
            var m = new Material(Load(ref _toon, ToonPath));
            m.SetColor("_BaseColor", Hex(baseHex));
            m.SetColor("_TeamColor", Hex(teamHex));
            m.SetFloat("_OutlinePx", outlinePx);
            m.SetFloat("_RimStrength", rim);
            m.SetFloat("_OutlineSource", outlineSource);
            m.name = "CMToon " + kind + " " + baseHex;
            Cache[key] = m;
            return m;
        }

        /// <summary>The ground: patterned grass, flat water, flat white foam (no outline on any).</summary>
        public static Material Ground(GroundKind kind)
        {
            string key = "ground" + kind;
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;
            var m = new Material(Load(ref _ground, GroundPath));
            if (kind != GroundKind.Grass)
            {
                Color c = Hex(kind == GroundKind.Water ? WaterHex : FoamHex);
                m.SetFloat("_PatternStrength", 0f);
                m.SetColor("_GrassMid", c);
                m.SetColor("_CliffColor", c);
            }
            m.name = "CMToonGround " + kind;
            Cache[key] = m;
            return m;
        }

        /// <summary>Environment swatches (art direction 2.4 / 2.5): water and foam.</summary>
        public const string WaterHex = "#6FC0EC";
        public const string FoamHex = "#F7F9F6";

        private static Material Load(ref Material slot, string path)
        {
            if (slot == null)
            {
                slot = UnityEngine.Resources.Load<Material>(path);
                if (slot == null) throw new InvalidOperationException("Resources/" + path + " missing: run ProjectSetup.Apply");
            }
            return slot;
        }

        private static Color Hex(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out Color c)) throw new ArgumentException("not an html colour: " + hex, nameof(hex));
            return c;
        }
    }
}
