using System;
using CriticalMass.Runtime.View.Art;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>Which layer a reaction plays on.</summary>
    public enum ReactionLayer : byte { None, Upper, FullBody }

    /// <summary>Strength tier of a hit (the same bands as the effects: light under 20 damage, heavy 20+, critical 40+ or a kill).</summary>
    public enum HitTier : byte { Light, Heavy, Critical }

    /// <summary>Where the blow came from, relative to the victim's facing.</summary>
    public enum HitSide : byte { Front, Back, Left, Right }

    /// <summary>One chosen reaction: clip, layer, peak weight, and the side the body leans away to.</summary>
    public readonly struct HitReaction
    {
        public readonly string Clip;
        public readonly ReactionLayer Layer;
        public readonly float Weight;
        public readonly HitTier Tier;
        public readonly HitSide Side;
        /// <summary>Extra yaw (degrees, + = turn left) the upper body twists away from the blow; the lean itself is CombatFx's.</summary>
        public readonly float TwistDeg;
        public HitReaction(string clip, ReactionLayer layer, float weight, HitTier tier, HitSide side, float twistDeg)
        {
            Clip = clip; Layer = layer; Weight = weight; Tier = tier; Side = side; TwistDeg = twistDeg;
        }
    }

    /// <summary>
    /// Tiered, directional hit reactions (DG-88, A12): light hits flinch the upper body (Hit_A on the upper layer, the legs
    /// keep going), heavy and critical hits stagger the whole body (Hit_B), blocked hits play Block_Hit on the arms. The side
    /// comes from the hit direction (source to victim) against the victim's facing; back hits keep the flinch (the pack has
    /// no back-hit clip and Generic clips cannot be mirrored) and twist the other way. Knock-downs (Lie_Down then Lie_StandUp)
    /// wait for the sim's hit-stun (DG-70): with no stun the sim lets the victim walk on, and a lying pose would lie.
    /// Pure: tests drive it with a table.
    /// </summary>
    public static class HitReactions
    {
        public const double HeavyDamage = 20, CriticalDamage = 40;
        public const float TwistMaxDeg = 10f;

        public static HitTier Tier(double damage, bool kill) =>
            kill || damage >= CriticalDamage ? HitTier.Critical : damage >= HeavyDamage ? HitTier.Heavy : HitTier.Light;

        /// <summary>
        /// Side of a hit: hit direction (dx, dz: from the source toward the victim) against the victim's facing (fx, fz),
        /// all Unity XZ. A blow from the front travels against the facing.
        /// </summary>
        public static HitSide Side(double fx, double fz, double dx, double dz)
        {
            // The source is at -d from the victim: its angle relative to the facing, + = to the left.
            double left = Locomotion.LeftAngleDeg(fx, fz, -dx, -dz);
            double a = Math.Abs(left);
            if (a <= 45) return HitSide.Front;
            if (a >= 135) return HitSide.Back;
            return left > 0 ? HitSide.Left : HitSide.Right;
        }

        /// <summary>Picks the reaction for one hit.</summary>
        public static HitReaction Pick(double damage, bool kill, bool blocked, HitSide side)
        {
            HitTier tier = Tier(damage, kill);
            // Twist away from the blow: hit from the left turns the chest right (negative), and so on.
            float twist = side == HitSide.Left ? -TwistMaxDeg : side == HitSide.Right ? TwistMaxDeg : 0f;
            if (tier != HitTier.Light) twist *= 1.5f;
            if (blocked) return new HitReaction(ArtMapping.BlockHit, ReactionLayer.Upper, 1f, tier, side, twist * 0.5f);
            if (tier == HitTier.Light)
                return new HitReaction(ArtMapping.Hit, ReactionLayer.Upper, side == HitSide.Back ? 0.6f : 0.85f, tier, side, twist);
            return new HitReaction(ArtMapping.HitHeavy, ReactionLayer.FullBody, tier == HitTier.Critical ? 1f : 0.85f, tier, side, twist);
        }

        /// <summary>Decay of a reaction over its clip: full at the start, gone at 80% of the clip.</summary>
        public static float Decay(double t, double length)
        {
            if (length <= 1e-6 || t >= length * 0.8) return 0f;
            return (float)Math.Max(0, 1 - t / (length * 0.8));
        }
    }
}
