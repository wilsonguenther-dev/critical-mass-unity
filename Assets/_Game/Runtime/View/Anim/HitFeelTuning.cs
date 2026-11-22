using System;
using CriticalMass.Sim.Config;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// Player-experience wave, lane C (2026-11-22): EVERY number of the hit-feel stack in one place, with the pure functions
    /// that turn them into curves, so the edit-mode tests (Runtime.Tests/PlayerExperience/PxImpact*) can hold them without eyes.
    /// The owner, on build 1.0.1409: "I can be in a player's face, swing, and I see nothing to indicate I've hit". Bar:
    /// anticipation, impact, follow-through; flash + stagger + knock + hit-stop + burst + camera kick within 100 ms, tiered by
    /// damage, stronger when the local player is the attacker or the victim ("own hit"), normal for everyone else's hits.
    /// View only: nothing here reads or writes the sim beyond the config rows; no Time.timeScale; times are WALL seconds
    /// unless a name says sim.
    /// </summary>
    public static class HitFeelTuning
    {
        // ------------------------------------------------------------------ 1a. victim flash
        /// <summary>Reduce flashing OFF: the full warm-white silhouette.</summary>
        public const float FlashFull = 1f;
        /// <summary>Reduce flashing ON (the default): a solid team-neutral cream fill + rim at this share toward white (was 0.3).</summary>
        public const float FlashSafeOwn = 0.72f, FlashSafeOther = 0.62f;
        /// <summary>Flash length (wall s): own hits a touch longer. Both inside the 100-130 ms brief.</summary>
        public const double FlashOwnSeconds = 0.12, FlashOtherSeconds = 0.10;
        /// <summary>Per-victim gap between two flashes (wall s): 1 / 0.34 = 2.94 flashes per second, under WCAG 2.3.1's 3.</summary>
        public const double FlashMinGapSeconds = 0.34;
        /// <summary>The cream the safe flash tints toward (ImpactPalette cream, never a team hue).</summary>
        public static readonly Color FlashCream = new Color(1f, 0.97f, 0.9f, 1f);

        public static float FlashStrength(bool reduceFlashing, bool own) => reduceFlashing ? (own ? FlashSafeOwn : FlashSafeOther) : FlashFull;
        public static double FlashSeconds(bool own) => own ? FlashOwnSeconds : FlashOtherSeconds;
        /// <summary>A new flash may start on a victim whose last flash began `sinceLast` wall seconds ago.</summary>
        public static bool FlashAllowed(double sinceLast) => !(sinceLast < FlashMinGapSeconds);
        public static double MaxFlashesPerSecond => 1.0 / FlashMinGapSeconds;

        // ------------------------------------------------------------------ 1b. hit-stop (per entity, view only)
        /// <summary>Hit-stop wall seconds by tier on the victim AND the attacker (was 45 / 60 / 70 ms, measured 35 ms).</summary>
        public const double HoldLight = 0.06, HoldHeavy = 0.08, HoldCritical = 0.11;

        public static double HoldWallSeconds(byte tier, bool reduceMotion) =>
            reduceMotion ? 0 : tier == 0 ? HoldLight : tier == 1 ? HoldHeavy : HoldCritical;

        // ------------------------------------------------------------------ 1c. visible knock
        /// <summary>
        /// The drawn victim travels KnockShare of the sim knock by KnockCheckSeconds (the sim's knock decays 0.82 per tick,
        /// so the interpolated body alone shows only 45% at 100 ms): a view-only lead along the blow fills the gap, overshoots
        /// by KnockOvershoot, settles, and fades out while the sim body catches up.
        /// </summary>
        public const double KnockShare = 0.8, KnockCheckSeconds = 0.1;
        public const double KnockFrontSeconds = 0.07, KnockOvershoot = 0.08, KnockSettleSeconds = 0.18;
        public const double KnockLeadFadeFrom = 0.3, KnockLeadFadeTo = 0.6;
        /// <summary>Light classes' tiny knocks (Miner on Warrior 0.19 u) still recoil at least this far, out and back.</summary>
        public const double MinVisualKnock = 0.3, RecoilOutSeconds = 0.07, RecoilBackSeconds = 0.35;
        /// <summary>The lead's cap, and the cap of everything drawn off the interpolated sim spot (the play-mode placement
        /// check allows 0.35 u; GameplaySanityPlayTests).</summary>
        public const float MaxKnockLead = 0.3f, MaxDrawnOffset = 0.34f;

        static double Smooth(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
        static double EaseOut(double x) => x <= 0 ? 0 : x >= 1 ? 1 : 1 - (1 - x) * (1 - x);

        /// <summary>Drawn travel along the blow the eye should see `age` wall s after a knock of `knock` u (pure).</summary>
        public static double KnockWant(double age, double knock)
        {
            if (!(age >= 0) || knock <= 0) return 0;
            double f;
            if (age < KnockFrontSeconds) f = (1 + KnockOvershoot) * EaseOut(age / KnockFrontSeconds);
            else f = 1 + KnockOvershoot * (1 - Smooth((age - KnockFrontSeconds) / KnockSettleSeconds));
            double want = knock * f;
            if (knock < MinVisualKnock) want += (MinVisualKnock - knock) * Recoil(age);
            return want;
        }

        /// <summary>0 -> 1 -> 0 recoil shape (out in RecoilOutSeconds, back by RecoilBackSeconds).</summary>
        public static double Recoil(double age)
        {
            if (!(age >= 0) || age >= RecoilBackSeconds) return 0;
            if (age < RecoilOutSeconds) return EaseOut(age / RecoilOutSeconds);
            return 1 - Smooth((age - RecoilOutSeconds) / (RecoilBackSeconds - RecoilOutSeconds));
        }

        /// <summary>The view-only lead (u along the blow) on top of what the interpolated body has `travelled` (pure).</summary>
        public static double KnockLead(double age, double knock, double travelled)
        {
            if (!(age >= 0) || age >= KnockLeadFadeTo || knock <= 0) return 0;
            double lead = KnockWant(age, knock) - Math.Max(0, travelled);
            if (lead <= 0) return 0;
            double fade = age <= KnockLeadFadeFrom ? 1 : 1 - Smooth((age - KnockLeadFadeFrom) / (KnockLeadFadeTo - KnockLeadFadeFrom));
            return Math.Min(MaxKnockLead, lead * fade);
        }

        /// <summary>The sim body's own share of a knock after `ticks` (decay per tick d): 1 - d^ticks.</summary>
        public static double SimKnockShare(double ticks, double decayPerTick) => ticks <= 0 ? 0 : 1 - Math.Pow(decayPerTick, ticks);

        // ------------------------------------------------------------------ 1d. impact burst
        /// <summary>World size (u) of the star at the contact point: own light >= 0.9, own heavy >= 1.3 (was 0.34-0.95).</summary>
        public const float BurstOwnLight = 0.95f, BurstOwnHeavy = 1.35f, BurstOwnCritical = 1.5f;
        public const float BurstOtherLight = 0.5f, BurstOtherHeavy = 0.72f, BurstOtherCritical = 1.0f;
        /// <summary>Burst life (wall s): at least 0.22 s for own hits.</summary>
        public const double BurstLifeOwn = 0.24, BurstLifeOwnCritical = 0.3, BurstLifeOther = 0.18;
        /// <summary>Own hits: a ground dust kick at every tier (puffs).</summary>
        public const int OwnDustPuffs = 3;

        public static float BurstSize(byte tier, bool own, double damage)
        {
            float d = Mathf.Clamp01((float)(damage / 45.0));
            if (own) return tier == 0 ? BurstOwnLight + 0.15f * d : tier == 1 ? BurstOwnHeavy + 0.1f * d : BurstOwnCritical;
            return tier == 0 ? BurstOtherLight + 0.2f * d : tier == 1 ? BurstOtherHeavy + 0.2f * d : BurstOtherCritical;
        }

        public static double BurstLife(byte tier, bool own) => own ? (tier >= 2 ? BurstLifeOwnCritical : BurstLifeOwn) : BurstLifeOther;

        // ------------------------------------------------------------------ 1e. stagger pose
        /// <summary>Hit reaction clip weight on own hits (was 0.85; back hits 0.6).</summary>
        public const float ReactionWeightOwn = 1f;

        // ------------------------------------------------------------------ 2. the committed swing
        /// <summary>The attack pose blends in to full over this many sim seconds after the press (ease-out): local player 0.05
        /// (0.89 at 2 frames of 60 fps), everyone else 0.075 (0.89 at 3 frames). Was a smoothstep over the whole wind-up.</summary>
        public const double CommitRiseLocal = 0.05, CommitRiseOther = 0.075;
        /// <summary>The weight the tests hold at 2 frames (local) and 3 frames (others) after the press.</summary>
        public const float CommitAtCheck = 0.8f;
        /// <summary>The render rate the frame counts are written against.</summary>
        public const double RenderHz = 60;
        /// <summary>The strike (wind-up extreme to the contact frame) plays over this many 60 fps frames, ending on the contact
        /// tick (was ONE sim tick = 2 frames).</summary>
        public const double StrikeFrames = 5;
        public const double StrikeSecondsTarget = StrikeFrames / RenderHz;
        /// <summary>The strike never takes more than this share of a short wind-up (the anticipation needs the rest).</summary>
        public const double StrikeMaxShare = 0.45;
        /// <summary>Ease from the clip start into the wind-up extreme (the anticipation pose), sim s at most.</summary>
        public const double AnticipationSeconds = 0.12;
        /// <summary>Standing still, the base layer (hips, legs) also plays the swing at this weight against idle 1 (75%).</summary>
        public const float LegCommit = 3f;

        public static float CommitWeight(double age, bool local)
        {
            double rise = local ? CommitRiseLocal : CommitRiseOther;
            return (float)EaseOut(age / rise);
        }

        public static double StrikeSeconds(double windup) => Math.Max(0, Math.Min(StrikeSecondsTarget, windup * StrikeMaxShare));

        /// <summary>The strike's shape 0..1 (ease-in: the weapon is fastest on the contact frame).</summary>
        public static double StrikeShape(double s) => s <= 0 ? 0 : s >= 1 ? 1 : 0.4 * s + 0.6 * s * s;

        /// <summary>Whiff (AttackContact with nothing hit): follow-through plays faster, the body over-rotates and stumbles.</summary>
        public const double WhiffOverRotateDeg = 22, WhiffLeanDeg = 9, WhiffSeconds = 0.32, WhiffRate = 1.3;
        /// <summary>Hit: the follow-through holds through the hit-stop (AttackTime), then plays at rate 1.</summary>
        public const double HitFollowRate = 1.0;

        /// <summary>Whiff over-rotation 0..1 at `since` sim s after the contact: out fast, back slow (pure).</summary>
        public static double WhiffShape(double since)
        {
            if (!(since >= 0) || since >= WhiffSeconds) return 0;
            const double outT = 0.08;
            if (since < outT) return EaseOut(since / outT);
            return 1 - Smooth((since - outT) / (WhiffSeconds - outT));
        }

        /// <summary>A feint (SimEventType.Feint, Value 1) rewinds the swing to its start and fades it out over this (sim s).</summary>
        public const double FeintAbortSeconds = 0.12;

        // ------------------------------------------------------------------ 3. the picture matches the rule
        /// <summary>The sim hits a body whose CENTRE is within AttackRange + its radius (ResolveSwing), so the drawn reach is
        /// AttackRange + this (the class radii are 0.4-0.45).</summary>
        public const double BodyWidening = 0.45;
        /// <summary>The weapon-coloured reach arc (the slash at the real reach) lives this long (wall s).</summary>
        public const double SlashSeconds = 0.16;

        /// <summary>The swing's true centre-to-centre reach for a class (u).</summary>
        public static double SimReach(GameConfig cfg, ClassId cls) => LightWeaponView.Range(cfg, cls) + BodyWidening;

        /// <summary>Half the drawn arc (radians): the class arc plus ArcCoversBody's asin(r / d) at the reach edge.</summary>
        public static double DrawnHalfArc(GameConfig cfg, ClassId cls)
        {
            double reach = SimReach(cfg, cls);
            return LightWeaponView.ArcDeg(cfg, cls) * 0.5 * Math.PI / 180 + Math.Asin(Math.Min(1, BodyWidening / Math.Max(1e-6, reach)));
        }

        // ------------------------------------------------------------------ 4. the camera answers
        /// <summary>
        /// Directional hit kick, NOT scaled by the accessibility shake slider (only REDUCE MOTION zeroes it, and the
        /// HitKickScale setting scales it). Sized as a share of the screen height at the followed fighter, so it reads the
        /// same in both views; never below the KickMin* world sizes.
        /// </summary>
        public const float KickShareByMe = 0.011f, KickShareOnMe = 0.018f, KickShareKill = 0.016f, KickShareDeath = 0.026f;
        public const float KickMinByMe = 0.04f, KickMinOnMe = 0.08f, KickMinKill = 0.07f, KickMinDeath = 0.12f;
        /// <summary>Kick envelope (wall s): out in KickRise, spring back by KickReturn.</summary>
        public const double KickRise = 0.025, KickReturn = 0.15;

        public enum KickKind { ByMe, OnMe, MyKill, MyDeath }

        public static float KickShare(KickKind k) =>
            k == KickKind.ByMe ? KickShareByMe : k == KickKind.OnMe ? KickShareOnMe : k == KickKind.MyKill ? KickShareKill : KickShareDeath;

        public static float KickMin(KickKind k) =>
            k == KickKind.ByMe ? KickMinByMe : k == KickKind.OnMe ? KickMinOnMe : k == KickKind.MyKill ? KickMinKill : KickMinDeath;

        /// <summary>World height (u) the screen shows at distance d with a vertical FOV.</summary>
        public static float VisibleHeight(double distance, double fovDeg) => (float)(2 * distance * Math.Tan(fovDeg * 0.5 * Math.PI / 180));

        /// <summary>Kick size (u at the camera) for a kind, at the view's focus distance and FOV, times the setting scale.</summary>
        public static float KickU(KickKind k, double distance, double fovDeg, float scale = 1f) =>
            Mathf.Max(KickMin(k), KickShare(k) * VisibleHeight(distance, fovDeg)) * Mathf.Max(0f, scale);

        /// <summary>Screen pixels a kick of u moves the followed fighter at distance d (vertical FOV, screen height px).</summary>
        public static float KickPx(float u, double distance, double fovDeg, float screenPx = 1080f) => u / Mathf.Max(1e-4f, VisibleHeight(distance, fovDeg)) * screenPx;

        public static float KickEnvelope(double t)
        {
            if (!(t >= 0) || t >= KickReturn) return 0f;
            if (t < KickRise) return (float)EaseOut(t / KickRise);
            return (float)(1 - Smooth((t - KickRise) / (KickReturn - KickRise)));
        }

        /// <summary>Fight framing (third person): an enemy within FightRange inside +-FightConeDeg in front, or damage dealt or
        /// taken in the last FightMemorySeconds, eases the camera back FightDistanceShare and FightFovAdd degrees wider over
        /// FightInSeconds, home over FightOutSeconds. Under reduce motion nothing changes.</summary>
        public const double FightRange = 5, FightConeDeg = 60, FightMemorySeconds = 2;
        public const double FightDistanceShare = 0.12, FightFovAdd = 3, FightInSeconds = 0.4, FightOutSeconds = 1.0;
        /// <summary>The hatch pull toward the egg while in a fight: this share of it (0.2: a hint, not a yank).</summary>
        public const double HatchShareInFight = 0.2;

        /// <summary>One step of the fight-framing weight toward 1 (in fight) or 0, linear-in-time smooth (pure).</summary>
        public static double StepFight(double w, bool inFight, double dt)
        {
            if (dt <= 0) return w;
            double rate = inFight ? 1 / FightInSeconds : -1 / FightOutSeconds;
            return Math.Max(0, Math.Min(1, w + rate * dt));
        }

        /// <summary>Rumble for landed hits by me and on me (was none for light hits: shock 0.10 under the 0.15 threshold).</summary>
        public const float RumbleLightMs = 50f, RumbleHeavyMs = 105f, RumbleLightLow = 0.3f, RumbleLightHigh = 0.45f, RumbleHeavyLow = 0.6f, RumbleHeavyHigh = 0.55f;
    }
}
