using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>
    /// CM/Toon materials in vertex-colour mode for the imported art, one per (model, swatch, rim colour): shared by every
    /// instance of that combination (SRP Batcher friendly, no per-instance copies). Cloned from Resources/Toon/CMToonArt.
    /// </summary>
    public static class ArtMaterials
    {
        public const string ArtPath = "Toon/CMToonArt";
        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();
        private static Material _base;

        public static Material For(ModelSpec spec, TeamSwatch target, string rimHex)
        {
            string key = spec.Key + "|" + target.Cloth + target.Glow + target.Dark + "|" + rimHex;
            // Domain reload is off on entering Play Mode: Unity's == null catches a destroyed cached material.
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;
            if (_base == null)
            {
                _base = Resources.Load<Material>(ArtPath);
                if (_base == null) throw new InvalidOperationException("Resources/" + ArtPath + " missing: run ProjectSetup.Apply");
            }
            var m = new Material(_base) { name = "CMToonArt " + key };
            m.SetFloat("_ColorMode", 1f);
            m.SetColor("_DefaultCloth", Hex(spec.Authored.Cloth));
            m.SetColor("_DefaultGlow", Hex(spec.Authored.Glow));
            m.SetColor("_DefaultDark", Hex(spec.Authored.Dark));
            m.SetColor("_TeamCloth", Hex(target.Cloth));
            m.SetColor("_TeamGlow", Hex(target.Glow));
            m.SetColor("_TeamDark", Hex(target.Dark));
            m.SetColor("_TeamColor", Hex(rimHex));
            m.SetFloat("_RimStrength", spec.Rim);
            m.SetFloat("_OutlinePx", spec.OutlinePx);
            m.SetFloat("_OutlineSource", 0f);
            // outline_px 0 in the art manifest: no hull at all (crystals, shards, all-glow pieces).
            m.SetShaderPassEnabled("SRPDefaultUnlit", spec.OutlinePx > 0f);
            Cache[key] = m;
            return m;
        }

        /// <summary>A character or prop in its team's colours, rim in the team core colour (characters only).</summary>
        public static Material ForTeam(ModelSpec spec, TeamId team) =>
            For(spec, ArtMapping.Swatch(spec, team), team == TeamId.Azure ? Palette.Azure : Palette.Crimson);

        /// <summary>A Kaiju stage in an allegiance glow.</summary>
        public static Material ForGlow(ModelSpec spec, string glowHex) => For(spec, new TeamSwatch(glowHex, glowHex, glowHex), glowHex);

        public static Color Hex(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out Color c)) throw new ArgumentException("not an html colour: " + hex, nameof(hex));
            return c;
        }
    }
}
