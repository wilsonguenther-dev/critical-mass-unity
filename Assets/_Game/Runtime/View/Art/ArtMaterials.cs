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
            m.SetColor("_DefaultMarker", Hex(MarkerAzure));
            m.SetColor("_DefaultMarkerDark", Hex(MarkerDarkAzure));
            m.SetColor("_TeamColor", Hex(rimHex));
            m.SetFloat("_RimStrength", spec.Rim);
            m.SetFloat("_OutlinePx", spec.OutlinePx);
            // Rigged models carry smoothed hull directions in TANGENT (ArtImportPostprocessor): no torn hull, no crease lines.
            m.SetFloat("_OutlineSource", spec.Rigged ? 3f : 0f);
            // outline_px 0 in the art manifest: no hull at all (crystals, shards, all-glow pieces).
            m.SetShaderPassEnabled("SRPDefaultUnlit", spec.OutlinePx > 0f);
            // Art refresh 2: the face decal mixed over the main material (FaceSlot -1), the baked UV4 outline weights.
            Texture2D face = FaceOf(spec);
            m.SetFloat("_DecalOn", face != null && spec.FaceSlot < 0 ? 1f : 0f);
            if (face != null) m.SetTexture("_FaceTex", face);
            m.SetFloat("_OutlineWeightUV4", spec.BakesOutlineWeights ? 1f : 0f);
            m.SetFloat("_Berserk", 0f);
            Cache[key] = m;
            return m;
        }

        static Texture2D FaceOf(ModelSpec spec) => spec.FaceTexture == null ? null : ArtCatalog.Load()?.Find(spec.Key)?.Face;

        /// <summary>
        /// The face material of a model whose decal is its own slot (ModelSpec.FaceSlot, the Miner's CM_FaceDecal): the body
        /// material with the decal on and no hull (per-material outline suppression). Null when the model has no face slot.
        /// </summary>
        public static Material Face(ModelSpec spec, Material body)
        {
            if (spec == null || spec.FaceSlot < 0 || body == null) return null;
            Texture2D face = FaceOf(spec);
            if (face == null) return null;
            string key = "face|" + body.name;
            if (Cache.TryGetValue(key, out Material cached) && cached != null) return cached;
            var m = new Material(body) { name = "CMToonArt face " + body.name };
            m.SetFloat("_DecalOn", 1f);
            m.SetTexture("_FaceTex", face);
            m.SetFloat("_OutlinePx", 0f);
            m.SetFloat("_OutlineWeightUV4", 0f);
            m.SetShaderPassEnabled("SRPDefaultUnlit", false);
            Cache[key] = m;
            return m;
        }

        /// <summary>A character or prop in its team's colours, rim in the team core colour (characters only).</summary>
        public static Material ForTeam(ModelSpec spec, TeamId team)
        {
            Material m = For(spec, ArtMapping.Swatch(spec, team), team == TeamId.Azure ? Palette.Azure : Palette.Crimson);
            // Props v2 marker codes 3 and 7 (authored Azure #27F8F8 / #43C2C5; Crimson #E6A595 / #D6594F).
            m.SetColor("_TeamMarker", Hex(team == TeamId.Azure ? MarkerAzure : MarkerCrimson));
            m.SetColor("_TeamMarkerDark", Hex(team == TeamId.Azure ? MarkerDarkAzure : MarkerDarkCrimson));
            return m;
        }

        public const string MarkerAzure = "#27F8F8", MarkerDarkAzure = "#43C2C5", MarkerCrimson = "#E6A595", MarkerDarkCrimson = "#D6594F";

        /// <summary>A Kaiju stage in an allegiance glow.</summary>
        public static Material ForGlow(ModelSpec spec, string glowHex)
        {
            Material m = For(spec, new TeamSwatch(glowHex, glowHex, glowHex), glowHex);
            // Kaiju v3: the berserk glow also darkens the fixed plates of the creature stages (rigged; not shards or spikes) (_Berserk, CMToonCommon.hlsl). The cache key holds
            // the glow, so the berserk material is its own shared instance.
            m.SetFloat("_Berserk", spec.Kind == ArtKind.Kaiju && spec.Rigged && glowHex == ArtMapping.GlowBerserk ? 1f : 0f);
            return m;
        }

        public static Color Hex(string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out Color c)) throw new ArgumentException("not an html colour: " + hex, nameof(hex));
            return c;
        }
    }
}
