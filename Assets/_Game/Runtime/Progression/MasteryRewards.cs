using System;
using System.Collections.Generic;
using CriticalMass.Net;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.Progression
{
    public enum RewardKind { Title, Frame, Trim, Emblem }

    /// <summary>One row of the mastery reward table: at this class's mastery Level, the item Id of Kind unlocks.</summary>
    public readonly struct MasteryReward
    {
        public readonly ClassId Class;
        public readonly int Level;
        public readonly RewardKind Kind;
        public readonly int Id;

        public MasteryReward(ClassId cls, int level, RewardKind kind, int id)
        {
            Class = cls;
            Level = level;
            Kind = kind;
            Id = id;
        }

        public string Name => MasteryRewards.Name(Class, Kind, Id);
        public string KindName => MasteryRewards.KindName(Kind);
    }

    /// <summary>
    /// A colour trim: a palette variant of a character's FIXED colours only (vertex mask code 0: skin, hair, leather,
    /// metal). Team cloth, team dark, team glow and markers (codes 3 to 8) are never touched, so the team read stays as
    /// the art bible draws it. Applied with the toon shader's existing fixed-colour mix (the Kaiju berserk slot, which is
    /// 0 on every character): fixed' = lerp(fixed, lerp(fixed x Mul, Tint, TintAmount), Strength), in sRGB.
    /// </summary>
    public readonly struct TrimSpec
    {
        public readonly string Hex;
        public readonly Vector3 Mul;
        public readonly float TintAmount;
        public readonly float Strength;

        public TrimSpec(string hex, Vector3 mul, float tintAmount, float strength)
        {
            Hex = hex;
            Mul = mul;
            TintAmount = tintAmount;
            Strength = strength;
        }

        public Color Tint => ColorUtility.TryParseHtmlString(Hex, out Color c) ? c : Color.gray;

        /// <summary>The trimmed colour of one fixed sRGB colour (the shader's maths on the CPU; tests and the menu swatch).</summary>
        public Color Apply(Color fixedSrgb)
        {
            Color t = Tint;
            var mul = new Color(fixedSrgb.r * Mul.x, fixedSrgb.g * Mul.y, fixedSrgb.b * Mul.z, 1f);
            Color inner = Color.Lerp(mul, t, TintAmount);
            Color o = Color.Lerp(fixedSrgb, inner, Strength);
            o.a = fixedSrgb.a;
            return o;
        }
    }

    /// <summary>
    /// b15 profile: the full class-mastery reward table, in data. Every class has 20 mastery levels and every level from 1
    /// to 20 unlocks exactly one cosmetic: 7 titles, 4 portrait frames, 5 colour trims and 5 emblems (21 rows; level 20
    /// unlocks both the last title and the Basin Crown frame). The unlock levels are the wire schedules in CosmeticLook,
    /// so a peer's look is clamped by the same table. Nothing here needs new art: titles are words, frames and emblems are
    /// drawn from VectorShape primitives, trims are shader parameters on the existing six models.
    /// </summary>
    public static class MasteryRewards
    {
        /// <summary>Frame ring colours (index = frame id; 0 = the plain frame keeps the card's own outline).</summary>
        public static readonly string[] FrameHex = { "", "#B87A46", "#C9CED6", "#E3B341", "#F2D27A" };
        /// <summary>Ring width per frame (px at 1920 x 1080); the crown doubles its ring.</summary>
        public static readonly float[] FrameRingPx = { 0f, 4f, 5f, 6f, 7f };

        /// <summary>Trims (index = trim id; 0 = stock). Hues kept away from both team hues, both colour-blind team hues and
        /// corruption violet (TrimRules test), strength at most 0.7.</summary>
        public static readonly TrimSpec[] Trims =
        {
            new TrimSpec("#808080", new Vector3(1f, 1f, 1f), 0f, 0f),
            new TrimSpec("#5E7F45", new Vector3(0.80f, 0.95f, 0.76f), 0.18f, 0.60f),   // Moss
            new TrimSpec("#6B6E72", new Vector3(0.86f, 0.86f, 0.86f), 0.30f, 0.60f),   // Slate
            new TrimSpec("#E9E2D2", new Vector3(1f, 1f, 1f), 0.38f, 0.55f),            // Bone
            new TrimSpec("#2C2B2F", new Vector3(0.62f, 0.62f, 0.64f), 0.12f, 0.65f),   // Soot
            new TrimSpec("#88A84A", new Vector3(0.95f, 1f, 0.72f), 0.16f, 0.55f),      // Lichen
        };

        /// <summary>Emblem shapes (index = emblem id) and their fill; drawn on a navy disc, never in a team colour.</summary>
        public static readonly CriticalMass.Runtime.Hud.Ui.ShapeKind[] EmblemShape =
        {
            CriticalMass.Runtime.Hud.Ui.ShapeKind.Circle,
            CriticalMass.Runtime.Hud.Ui.ShapeKind.Circle,
            CriticalMass.Runtime.Hud.Ui.ShapeKind.Diamond,
            CriticalMass.Runtime.Hud.Ui.ShapeKind.Shield,
            CriticalMass.Runtime.Hud.Ui.ShapeKind.Hexagon,
            CriticalMass.Runtime.Hud.Ui.ShapeKind.Crown,
        };

        /// <summary>Per-class emblem fill (brass, cream, stone family: no team hue).</summary>
        public static readonly string[] EmblemHex = { "#C79245", "#B7C98A", "#A88A6A", "#D9B27C", "#C9B79C", "#E8DCC2" };

        static List<MasteryReward>[] _table;

        /// <summary>Every reward of a class, by level (then title, frame, trim, emblem).</summary>
        public static IReadOnlyList<MasteryReward> For(ClassId cls)
        {
            if (_table == null) Build();
            int c = (int)cls;
            return c >= 0 && c < _table.Length ? _table[c] : _table[0];
        }

        static void Build()
        {
            _table = new List<MasteryReward>[ClassIds.All.Count];
            foreach (ClassId cls in ClassIds.All)
            {
                var list = new List<MasteryReward>(24);
                for (int i = 1; i < CosmeticLook.TitleLevels.Length; i++) list.Add(new MasteryReward(cls, CosmeticLook.TitleLevels[i], RewardKind.Title, i));
                for (int i = 1; i < CosmeticLook.FrameLevels.Length; i++) list.Add(new MasteryReward(cls, CosmeticLook.FrameLevels[i], RewardKind.Frame, i));
                for (int i = 1; i < CosmeticLook.TrimLevels.Length; i++) list.Add(new MasteryReward(cls, CosmeticLook.TrimLevels[i], RewardKind.Trim, i));
                for (int i = 1; i < CosmeticLook.EmblemLevels.Length; i++) list.Add(new MasteryReward(cls, CosmeticLook.EmblemLevels[i], RewardKind.Emblem, i));
                list.Sort((a, b) => a.Level != b.Level ? a.Level.CompareTo(b.Level) : a.Kind.CompareTo(b.Kind));
                _table[(int)cls] = list;
            }
        }

        public static int[] Levels(RewardKind k)
        {
            switch (k)
            {
                case RewardKind.Title: return CosmeticLook.TitleLevels;
                case RewardKind.Frame: return CosmeticLook.FrameLevels;
                case RewardKind.Trim: return CosmeticLook.TrimLevels;
                default: return CosmeticLook.EmblemLevels;
            }
        }

        public static int Count(RewardKind k) => Levels(k).Length - 1;

        public static bool Unlocked(RewardKind k, int id, int masteryLevel) => CosmeticLook.Allowed(Levels(k), id, masteryLevel);

        /// <summary>The rewards that unlocked going from level before to level after (exclusive, inclusive).</summary>
        public static List<MasteryReward> Between(ClassId cls, int before, int after)
        {
            var r = new List<MasteryReward>();
            foreach (MasteryReward m in For(cls)) if (m.Level > before && m.Level <= after) r.Add(m);
            return r;
        }

        /// <summary>The first reward above a level (null at the top).</summary>
        public static MasteryReward? NextAfter(ClassId cls, int level)
        {
            foreach (MasteryReward m in For(cls)) if (m.Level > level) return m;
            return null;
        }

        public static string Name(ClassId cls, RewardKind k, int id)
        {
            switch (k)
            {
                case RewardKind.Title: return id == 0 ? MetaWords.NoTitle : MetaWords.Title(cls, id);
                case RewardKind.Frame: return Pick(MetaWords.Frames, id);
                case RewardKind.Trim: return Pick(MetaWords.Trims, id);
                default: return Pick(MetaWords.Emblems, id);
            }
        }

        static string Pick(string[] a, int id) => id >= 0 && id < a.Length ? a[id] : a[0];

        public static string KindName(RewardKind k) =>
            k == RewardKind.Title ? MetaWords.KindTitle : k == RewardKind.Frame ? MetaWords.KindFrame : k == RewardKind.Trim ? MetaWords.KindTrim : MetaWords.KindEmblem;

        public static Color FrameColor(int id) =>
            id > 0 && id < FrameHex.Length && ColorUtility.TryParseHtmlString(FrameHex[id], out Color c) ? c : Color.clear;

        public static Color EmblemColor(ClassId cls)
        {
            int i = Math.Max(0, Math.Min((int)cls, EmblemHex.Length - 1));
            return ColorUtility.TryParseHtmlString(EmblemHex[i], out Color c) ? c : Color.white;
        }

        public static TrimSpec Trim(int id) => id > 0 && id < Trims.Length ? Trims[id] : Trims[0];
    }
}
