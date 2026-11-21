using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// b15 safe defaults (blind-spot audit P5 / A2, owner-approved 2026-11-17): the colours a hit, a knock-out puff or an
    /// impact may use, so team colour stays reserved for teams (bible: "never colour alone"; team hues are Azure #16C7D9,
    /// Crimson #FF5A5F and the colour-blind pair #3D9BFF / #FFB000). Two families:
    /// <list type="bullet">
    /// <item>NEUTRAL (every gore level, and the whole look at the default GORE Off): pale warm-white and cream-yellow sparks
    /// and dust. Every one is low in saturation (under <see cref="NeutralMaxSaturation"/>), so none reads as a team.</item>
    /// <item>FULL GORE (opt-in only): a dark violet-brown instead of a saturated red, at least <see cref="MinTeamHueGapDeg"/>
    /// degrees of hue from every team colour, and never "blood red" or "Crimson-like" by GoreShots' own measure.</item>
    /// </list>
    /// For the hit-effect owner (feat/b15-combat-feel, FxScene): new hit sparks and impact dust take these constants;
    /// FxScene.BloodLight / BloodMid / BloodDark should become GoreLight / GoreMid / GoreDark (routed patch in the b15 safety
    /// PR). View only: nothing here reaches the sim. ImpactPaletteTests hold every colour to the rules.
    /// </summary>
    public static class ImpactPalette
    {
        /// <summary>Hot core of a hit spark (near white).</summary>
        public static readonly Color32 SparkHot = new Color32(255, 250, 236, 255);
        /// <summary>Body of a hit spark (pale cream-yellow).</summary>
        public static readonly Color32 SparkMid = new Color32(255, 240, 200, 255);
        /// <summary>Cooling edge of a spark (warm sand).</summary>
        public static readonly Color32 SparkEdge = new Color32(238, 222, 184, 255);
        /// <summary>Impact dust, light and dark tone.</summary>
        public static readonly Color32 DustLight = new Color32(200, 190, 172, 255), DustDark = new Color32(128, 120, 108, 255);

        /// <summary>FULL gore only (opt-in): dark violet-brown in three tone steps (light, mid, dark).</summary>
        public static readonly Color32 GoreLight = new Color32(128, 62, 100, 255), GoreMid = new Color32(96, 44, 74, 255),
            GoreDark = new Color32(62, 28, 48, 255);

        public static readonly Color32[] Neutral = { SparkHot, SparkMid, SparkEdge, DustLight, DustDark };
        public static readonly Color32[] FullGore = { GoreLight, GoreMid, GoreDark };

        /// <summary>A neutral colour stays under this HSV saturation (it reads as white, cream or grey, not a hue).</summary>
        public const double NeutralMaxSaturation = 0.25;
        /// <summary>A saturated impact colour keeps at least this much hue distance from every team colour.</summary>
        public const double MinTeamHueGapDeg = 30;

        /// <summary>The four team colours (normal and colour-blind pairs) the impact colours keep away from.</summary>
        public static readonly Color32[] TeamHues =
        {
            new Color32(0x16, 0xC7, 0xD9, 255), new Color32(0xFF, 0x5A, 0x5F, 255),
            new Color32(0x3D, 0x9B, 0xFF, 255), new Color32(0xFF, 0xB0, 0x00, 255),
        };

        /// <summary>Shortest distance between two hues in degrees (0..180).</summary>
        public static double HueGap(double a, double b)
        {
            double d = System.Math.Abs(a - b) % 360.0;
            return d > 180.0 ? 360.0 - d : d;
        }

        /// <summary>The smallest hue gap from c to any team colour (degrees).</summary>
        public static double NearestTeamHueGap(Color32 c)
        {
            GoreShots.Hsv(c, out double h, out _, out _);
            double best = 180;
            for (int i = 0; i < TeamHues.Length; i++)
            {
                GoreShots.Hsv(TeamHues[i], out double th, out _, out _);
                best = System.Math.Min(best, HueGap(h, th));
            }
            return best;
        }
    }

    /// <summary>
    /// b15 view polish (Editor review item 3): the fighter wind-up wedge's look (pure; FxScene.DrawSwingTelegraphs and a
    /// test read it). Neutral colours only (ImpactPalette), so a wedge never reads as a team. Quiet tier for every swing,
    /// a brighter tier when the local fighter is inside an enemy's wedge. With reduced flashing the bright edges are capped.
    /// </summary>
    public struct SwingTelegraph
    {
        /// <summary>Wedge inner radius (u) and the widths of the leading edge, the outer ink and the outer bright edge.</summary>
        public const float Inner = 0.15f, LeadW = 0.08f, InkW = 0.14f, EdgeW = 0.07f;
        /// <summary>Reduced flashing: no bright edge above this alpha.</summary>
        public const float ReducedEdgeCap = 0.6f;

        // b15 view polish r2 (Editor review round 2): the ground wedge alone failed (on the hill it drew under the raised
        // paving; in contact the two bodies hid it). The warning now lives ON THE ATTACKER: (a) a bright neutral glint on
        // the weapon hand plus a streak along the weapon arm and past the hand along the weapon, (b) a thin bright arc at
        // chest height in front of the attacker along the swing path (air layer: depth-tested, so it shows over ground and
        // paving, and it sits outside the attacker's own body), (c) the wedge stays as a secondary cue, lifted to the local
        // walking surface (SurfaceLift: the hill paving's top on the hill).
        /// <summary>A wind-up this short or shorter (wall s) is at full strength from its first frame (the Miner's 0.20 s);
        /// a longer one ramps from RampFloor to 1.</summary>
        public const float ShortWindupSeconds = 0.25f, RampFloor = 0.6f;
        /// <summary>The arc: height over the surface, bright and ink widths (u), radius as a share of the reach, and the
        /// least clearance outside the attacker's body radius.</summary>
        public const float ArcHeight = 1.05f, ArcWidth = 0.06f, ArcInkWidth = 0.13f, ArcReachShare = 0.8f, ArcMinClear = 0.25f;
        /// <summary>The weapon cue: glint radius, arm streak width, how far past the hand it runs along the weapon, and the
        /// start flash (the glint starts FlashScale times larger and settles over FlashSeconds; none under reduced flashing).</summary>
        public const float GlintRadius = 0.24f, ArmWidth = 0.08f, WeaponExtend = 0.45f, FlashSeconds = 0.10f, FlashScale = 1.45f;
        /// <summary>The hill paving's top over GroundHeight.At inside the paving's radius; GroundLift everywhere else (was a
        /// flat 0.035 over the world ground). b15 Editor review r3: measured by raycast on the placed hill kit (WorldBase
        /// Hill group): env_hill_plaza sits sunk, its highest tile 0.145 over the ground at the centre (0.05..0.12 out to
        /// 5.5 u) and it ends at ~5.8 u; past that the walking surface is the ground itself. The old 0.27 inside 12 u (the
        /// catalogue's full 0.23 height) floated the wedge and heal ring 0.17..0.31 u over what was drawn.</summary>
        public const float HillPavingTop = 0.15f, HillPavingRadius = 5.8f, GroundLift = 0.04f;

        /// <summary>Cue strength 0..1 at wind-up progress p for a wind-up of this many wall seconds.</summary>
        public static float Strength(float p, float windupSeconds) =>
            windupSeconds <= ShortWindupSeconds ? 1f : Mathf.Lerp(RampFloor, 1f, Mathf.Clamp01(p));

        /// <summary>The cue's alpha: the strength itself, or under reduced flashing a steady glow capped at ReducedEdgeCap.</summary>
        public static float CueAlpha(float strength, bool reduceFlashing) =>
            reduceFlashing ? Mathf.Min(Mathf.Clamp01(strength), ReducedEdgeCap) : Mathf.Clamp01(strength);

        /// <summary>The glint's size factor: the start flash (FlashScale settling to 1 over FlashSeconds); 1 under reduced flashing.</summary>
        public static float GlintScale(float sinceStartSeconds, bool reduceFlashing) =>
            reduceFlashing ? 1f : Mathf.Lerp(FlashScale, 1f, Mathf.Clamp01(sinceStartSeconds / FlashSeconds));

        /// <summary>The arc's radius: ArcReachShare of the reach, never inside the body plus ArcMinClear.</summary>
        public static float ArcRadius(float reach, float bodyRadius) => Mathf.Max(bodyRadius + ArcMinClear, reach * ArcReachShare);

        /// <summary>The ground wedge's height over GroundHeight.At at sim (x, z): over the hill paving on the hill.</summary>
        public static float SurfaceLift(double x, double z, double hillX, double hillZ, bool hasHill)
        {
            if (!hasHill) return GroundLift;
            double dx = x - hillX, dz = z - hillZ;
            return dx * dx + dz * dz <= HillPavingRadius * HillPavingRadius ? HillPavingTop + GroundLift : GroundLift;
        }

        public Color32 Fill, Grow, Lead;
        public float BaseIn, BaseOut, GrowIn, GrowOut, LeadA, InkA, EdgeA;

        public static SwingTelegraph For(bool atMe, bool reduceFlashing)
        {
            float k = atMe ? 1f : 0.8f;
            var t = new SwingTelegraph
            {
                Fill = ImpactPalette.SparkEdge, Grow = ImpactPalette.SparkMid, Lead = ImpactPalette.SparkHot,
                BaseIn = 0.20f * k, BaseOut = 0.34f * k, GrowIn = 0.30f * k, GrowOut = 0.52f * k,
                LeadA = 0.95f * k, InkA = 0.75f * k, EdgeA = 0.92f * k,
            };
            if (reduceFlashing)
            {
                t.LeadA = Mathf.Min(t.LeadA, ReducedEdgeCap);
                t.EdgeA = Mathf.Min(t.EdgeA, ReducedEdgeCap);
            }
            return t;
        }
    }

    /// <summary>
    /// b15 view polish r2: where each fighter's weapon hand and elbow were drawn this frame (ActorViews.PostPose records
    /// them after the pose), so the wind-up cue sits on the attacker's weapon arm. View only; no allocation after a model's
    /// first frame (its hand bone is cached per model).
    /// </summary>
    public static class SwingCue
    {
        public const int Max = 64;
        static readonly CriticalMass.Runtime.View.Art.ModelView[] _mv = new CriticalMass.Runtime.View.Art.ModelView[Max];
        static readonly Transform[] _hand = new Transform[Max], _elbow = new Transform[Max];
        static readonly Vector3[] _handAt = new Vector3[Max], _elbowAt = new Vector3[Max];
        static readonly int[] _frame = new int[Max];

        public static void Record(int i, CriticalMass.Runtime.View.Art.ModelView mv)
        {
            if (i < 0 || i >= Max || mv == null) return;
            if (!ReferenceEquals(_mv[i], mv))
            {
                _mv[i] = mv;
                Transform h = mv.Bone(CriticalMass.Runtime.View.Anim.WeaponProps.HandBone);
                Transform e = h != null ? h.parent : null;   // handslot.r -> hand.r
                if (e != null && e.parent != null) e = e.parent;   // -> the forearm (its origin is the elbow)
                if (h == null)
                {
                    h = mv.Bone(CriticalMass.Runtime.View.Anim.WeaponProps.HandBoneFallback);
                    e = h != null ? h.parent : null;
                }
                _hand[i] = h;
                _elbow[i] = e;
            }
            if (_hand[i] == null) return;
            _handAt[i] = _hand[i].position;
            _elbowAt[i] = _elbow[i] != null ? _elbow[i].position : _handAt[i] + Vector3.up * 0.3f;
            _frame[i] = Time.frameCount;
        }

        /// <summary>The weapon hand and elbow drawn this frame or the last (false: no model or no hand bone).</summary>
        public static bool TryArm(int i, out Vector3 hand, out Vector3 elbow)
        {
            hand = elbow = Vector3.zero;
            if (i < 0 || i >= Max || _hand[i] == null || Time.frameCount - _frame[i] > 1) return false;
            hand = _handAt[i];
            elbow = _elbowAt[i];
            return true;
        }
    }
}
