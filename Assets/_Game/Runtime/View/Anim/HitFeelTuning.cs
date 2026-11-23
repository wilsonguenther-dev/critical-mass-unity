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
        /// <summary>Round 3: in third person the own-hit flash lasts 180 ms wall so it spans the impact frame AND the hit-stop
        /// hold (the review saw no flash on the victim at impact); starts stay one per FlashMinGapSeconds (under 3 a second).</summary>
        public const double FlashOwn3PSeconds = 0.18;
        /// <summary>Fix round 5 (impact pop): the share of the CMToon `_HitFlash` term, the colour laid UNSHADED over the
        /// whole lit body on the flash frames (after bands, time of day, rim and base map). Full flash 0.92 white; under
        /// reduce flashing 0.45 of the cream (no white frame). Before this the flash only set the albedo colours to white:
        /// the shading bands and time-of-day grade kept a cream fighter almost the same, and a textured model (Warrior /
        /// Builder base map) ignored them entirely, so no flash showed on the victim at 1080p.</summary>
        public const float FlashShareFull = 0.92f, FlashShareSafe = 0.25f;   // round 6: safe 0.45 -> 0.25 (pass 6: only ~11% dimmer than intense)
        public static readonly UnityEngine.Color FlashWhite = new UnityEngine.Color(1f, 0.99f, 0.96f, 1f);
        /// <summary>The `_HitFlash` vector for a flash copy (rgb colour, a share).</summary>
        public static UnityEngine.Vector4 HitFlashVector(bool reduceFlashing)
        {
            UnityEngine.Color c = reduceFlashing ? FlashCream : FlashWhite;
            return new UnityEngine.Vector4(c.r, c.g, c.b, reduceFlashing ? FlashShareSafe : FlashShareFull);
        }
        public static double FlashSeconds(bool own, bool thirdPerson) => own && thirdPerson ? FlashOwn3PSeconds : FlashSeconds(own);
        /// <summary>Fix round 6 (advisory 4): under reduce flashing (the default) the flash is also SHORTER: 60% of the
        /// intense length (own 72 ms, other 60 ms, third-person own 108 ms: still 4-6 frames at 60 fps, so it reads).</summary>
        public const double FlashSafeLengthShare = 0.6;
        /// <summary>Fix round 6: the share of the safe strength the reduce-flashing copy moves its albedo toward the cream.</summary>
        public const float FlashSafeColourShare = 0.6f;
        public static double FlashSeconds(bool own, bool thirdPerson, bool reduceFlashing) =>
            reduceFlashing ? System.Math.Max(4 / 60.0, FlashSeconds(own, thirdPerson) * FlashSafeLengthShare) : FlashSeconds(own, thirdPerson);   // review pass 7: never under 4 frames (other was 60 ms = 3.6)
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
        public const float MaxKnockLead = 0.34f, MaxDrawnOffset = 0.34f;

        /// <summary>
        /// Fix round 5 (item 7: big knocks drew at 0.53-0.72 of the sim body's travel at 100 ms). The clamp was the
        /// interpolation itself: a fighter is drawn Lerp(PrevX, X, alpha), up to one whole tick BEHIND the sim, and in a
        /// knock's first ticks one tick is 0.3-0.5 u of travel, more than the 0.34 u lead cap could make up. While a knock
        /// is fresh the victim is drawn at the CURRENT tick's position (share 1 of the gap from the interpolated spot to X),
        /// fading back to plain interpolation between KnockAlphaHold and KnockAlphaFade s, when the slide has all but stopped
        /// (no visible step). Pure; ActorViews applies it before the knock lead.
        /// </summary>
        public const double KnockAlphaHold = 0.3, KnockAlphaFade = 0.45;

        public static double KnockAlphaLead(double age)
        {
            if (!(age >= 0) || age >= KnockAlphaFade) return 0;
            if (age <= KnockAlphaHold) return 1;
            return 1 - (age - KnockAlphaHold) / (KnockAlphaFade - KnockAlphaHold);
        }

        static double Smooth(double x) => x <= 0 ? 0 : x >= 1 ? 1 : x * x * (3 - 2 * x);
        static double EaseOut(double x) => x <= 0 ? 0 : x >= 1 ? 1 : 1 - (1 - x) * (1 - x);

        /// <summary>Drawn travel along the blow the eye should see `age` wall s after a knock of `knock` u (pure).</summary>
        public static double KnockWant(double age, double knock) => KnockWant(age, knock, MinVisualKnock);

        /// <summary>KnockWant with a recoil floor (critical hits: CritMinVisualKnock).</summary>
        public static double KnockWant(double age, double knock, double minVisual)
        {
            if (!(age >= 0) || knock <= 0) return 0;
            double f;
            if (age < KnockFrontSeconds) f = (1 + KnockOvershoot) * EaseOut(age / KnockFrontSeconds);
            else f = 1 + KnockOvershoot * (1 - Smooth((age - KnockFrontSeconds) / KnockSettleSeconds));
            double want = knock * f;
            if (knock < minVisual) want += (minVisual - knock) * Recoil(age);
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
        public static double KnockLead(double age, double knock, double travelled) => KnockLead(age, knock, travelled, MinVisualKnock);

        /// <summary>KnockLead with a recoil floor (critical hits: CritMinVisualKnock).</summary>
        public static double KnockLead(double age, double knock, double travelled, double minVisual)
        {
            if (!(age >= 0) || age >= KnockLeadFadeTo || knock <= 0) return 0;
            double lead = KnockWant(age, knock, minVisual) - Math.Max(0, travelled);
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
        /// <summary>Fix round 5 (item 4): the local press is shown before the sim's tick takes it (presentation only): the
        /// wind-up pose starts on the press frame and holds at most PredictHoldShare of the wind-up until the sim's swing
        /// starts; if the sim has not started it within PredictTimeout (s, wall) the pose fades out like a feint.</summary>
        public const double PredictTimeout = 0.06, PredictHoldShare = 0.35;   // round 6: 0.06 + the 0.06 re-trigger fade = gone by 0.12 s

        /// <summary>Fix round 5 (pure; tests): the predicted swing's age (s into the wind-up) and weight, `wall` s after the
        /// press. Null age past the timeout (the caller aborts).</summary>
        /// <summary>Fix round 6: what a predicted wind-up does this frame.</summary>
        public enum PredictStep { Continue, HandOver, Abort }

        /// <summary>
        /// Fix round 6 (pure; tests; review pass 6: a Demolitions press with TNT ready threw the TNT and the predicted wind-up
        /// stayed raised at 0.56 for 566 ms+). Every frame, before the pose: the sim's melee swing started (its SwingTick
        /// moved) -> HandOver (the swing continues the same clip); any OTHER action started instead (AnimAction reset: TNT,
        /// an ability, a throw, a skill cue, a verb) or PredictTimeout passed with no swing -> Abort (the predicted pose fades
        /// out over the re-trigger fade, 0.06 s, and the other action's own pose takes over); else Continue.
        /// </summary>
        public static PredictStep StepPrediction(bool simSwingStarted, bool otherActionStarted, double wall)
        {
            if (simSwingStarted) return PredictStep.HandOver;
            if (otherActionStarted || !(wall >= 0) || wall > PredictTimeout) return PredictStep.Abort;
            return PredictStep.Continue;
        }

        /// <summary>Fix round 7 (review pass 7 (c)): a press the sim refuses because the attack is cooling down still shows a
        /// short wind-up so the press is acknowledged: the same predicted pose, capped at this weight and held short of the
        /// strike (PredictHoldShare), gone by PredictTimeout + the 0.06 s fade = 0.12 s. Never a strike, never a SwingCommit.</summary>
        public const float RefusedPressWeight = 0.5f;

        /// <summary>Fix round 7 (review pass 7 (a)): a Throw cue (TNT, Root Seeds) plays its whole clip over ThrowPoseSeconds
        /// (8 frames of 60 fps after the throw leaves the hand), full weight for ThrowHoldSeconds, then eases out to 0 by
        /// ThrowPoseSeconds. Pass 7: the clip ran at its own length, full weight to +24 and still 0.11 at +34.</summary>
        public const double ThrowPoseSeconds = 8.0 / 60.0, ThrowHoldSeconds = 3.0 / 60.0;

        /// <summary>Fix round 7 (pure; tests): the Throw cue's clip time and weight at `age` sim s after the throw.</summary>
        public static void ThrowPose(double age, double length, out double clipTime, out float weight)
        {
            if (!(age >= 0)) age = 0;
            double share = Math.Min(1.0, age / ThrowPoseSeconds);
            clipTime = length * share;
            if (age <= ThrowHoldSeconds) weight = 1f;
            else if (age >= ThrowPoseSeconds) weight = 0f;
            else weight = (float)(1.0 - (age - ThrowHoldSeconds) / (ThrowPoseSeconds - ThrowHoldSeconds));
        }

        /// <summary>Fix round 7 (pure; tests; review pass 7: Warrior 0.56 at press + 2): after a predicted press hands over to
        /// the sim's swing, the pose floor keeps rising on the PRESS's clock (wall s since the press, plus the press frame's
        /// own rise), so the hand-over never restarts the 0.05 s commit rise from the sim's later start tick. 0 once the
        /// rise is complete and FloorSeconds has passed (the sim's own envelope has long taken over by then).</summary>
        public static float RisingFloor(double wallSincePress)
        {
            if (!(wallSincePress >= 0) || wallSincePress > FloorSeconds) return 0f;
            return CommitWeight(wallSincePress + 1.0 / 60.0, true);
        }
        public const double FloorSeconds = 0.15;

        public static bool PredictedSwing(double wall, double windupSeconds, out double age, out float weight)
        {
            age = 0; weight = 0f;
            if (!(wall >= 0) || wall > PredictTimeout) return false;
            age = Math.Min(wall, windupSeconds * PredictHoldShare);
            weight = CommitWeight(wall + 1.0 / 60.0, true);   // the press frame already draws one frame's rise
            return true;
        }
        /// <summary>The weight the tests hold at 2 frames (local) and 3 frames (others) after the press.</summary>
        public const float CommitAtCheck = 0.8f;
        /// <summary>The render rate the frame counts are written against.</summary>
        public const double RenderHz = 60;
        /// <summary>The strike (wind-up extreme to the contact frame) plays over this many 60 fps frames, ending on the contact
        /// tick (was ONE sim tick = 2 frames).</summary>
        public const double StrikeFrames = 5;
        public const double StrikeSecondsTarget = StrikeFrames / RenderHz;
        /// <summary>The strike never takes more than this share of a short wind-up (the anticipation needs the rest).</summary>
        public const double StrikeMaxShare = 0.4;   // the share the fix/hit-feel-3 tests encode
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
        /// <para>Fix round (review defect 3): full on the impact frame (KickRise 0; it was 25 ms, a partial first frame) and back
        /// by 0.18 s; CameraShake ages it per rendered frame (at most 1/30 s a frame) so a slow frame never skips it.</para>
        public const double KickRise = 0.0, KickReturn = 0.18;
        /// <summary>The longest frame step the kick and punch age by (s): a hitch or a slow capture still shows it.</summary>
        public const float KickMaxFrameStep = 1f / 30f;

        public enum KickKind { ByMe, OnMe, MyKill, MyDeath, CritByMe, CritOnMe }

        // ---- critical hits (lead's call 12, 2026-11-22): the top tier (Tier 2 that is not a kill) when the local player is
        // the attacker or the victim. Lane S1's CriticalHit event (Value 1 counter, 2 punish, 3 back hit) is wired to
        // CombatFx.OnCritical at the combine; until then every own Tier-2 non-kill hit takes this look (kind 0).
        /// <summary>The critical kick is this many times the plain hit kick (by me / on me).</summary>
        public const float CritKickMul = 1.6f;

        // ---- fix round 2 (review pass 2): third person
        /// <summary>Own hit burst in third person: this share of the size, at most this long (s wall), drawn behind the victim.</summary>
        /// <para>Fix round 5 (impact pop): 0.6 behind him read thin (pass 4), full size over him hid him (pass 2): 0.8 of the
        /// burst, on the NEAR side, beside his body centre toward the attacker's weapon side (ContactSpark3P).</para>
        public const float Burst3PShare = 0.8f;
        /// <summary>Round 3: the near-side contact spark in third person (u, s wall, spark lines): it marks the impact frame
        /// without covering more than a small part of the victim (0.35 u against a ~1 x 2 u body).</summary>
        public const float NearSpark3PSize = 0.35f;
        public const double NearSpark3PLife = 0.1;
        public const int NearSpark3PLines = 4;
        public const double Burst3PLife = 0.2;

        /// <summary>Fix round 5: how far past the burst's own radius its centre sits beside the victim's centre, across the
        /// view (u): the burst touches his weapon-side edge but never covers his body centre.</summary>
        public const float Burst3PSideClear = 0.08f;

        /// <summary>
        /// Fix round 5 (pure; tests): where the third-person contact burst goes. On the victim's camera-near surface at chest
        /// height (chest + toward the lens x radius), moved across the view (the lens right, flat) to the attacker's weapon
        /// side (the attacker's right, from hitDir = attacker -> victim) by burst radius + Burst3PSideClear, so the burst's
        /// edge sits on his side and his body centre stays clear.
        /// </summary>
        public static UnityEngine.Vector3 ContactSpark3P(UnityEngine.Vector3 chest, UnityEngine.Vector3 lens, UnityEngine.Vector3 hitDir, float radius, float burstSize)
        {
            UnityEngine.Vector3 toLens = lens - chest; toLens.y = 0f;
            if (toLens.sqrMagnitude < 1e-6f) toLens = -hitDir;
            toLens.y = 0f;
            toLens = toLens.sqrMagnitude > 1e-6f ? toLens.normalized : UnityEngine.Vector3.back;
            UnityEngine.Vector3 across = new UnityEngine.Vector3(toLens.z, 0f, -toLens.x);   // perpendicular to the view, flat
            UnityEngine.Vector3 d = hitDir; d.y = 0f;
            UnityEngine.Vector3 weapon = d.sqrMagnitude > 1e-6f ? new UnityEngine.Vector3(d.z, 0f, -d.x) : across;   // attacker's right
            if (UnityEngine.Vector3.Dot(weapon, across) < 0f) across = -across;
            return chest + toLens * radius + across * (burstSize * 0.5f + Burst3PSideClear);
        }
        /// <summary>
        /// Third-person kick multipliers: the kick moves the lens, and a lens translation shifts a point by u / depth, so at the
        /// enemy's chest (about 6 u deep, not the 4.4 u focus) my hit measured 7.5-9.1 px and my kill 12.1 px. These bring them
        /// to about 14 / 20 px at the enemy; the hit on me (14.1 px) and top-down (13 / 19 px) read fine and are unchanged.
        /// </summary>
        public static float ThirdPersonKickMul(KickKind k) =>
            k == KickKind.ByMe ? 1.7f : k == KickKind.MyKill ? 1.65f : k == KickKind.CritByMe ? 1.5f : 1f;
        public static float KickU(KickKind k, double distance, double fovDeg, float scale, bool thirdPerson) =>
            KickU(k, distance, fovDeg, scale) * (thirdPerson ? ThirdPersonKickMul(k) : 1f);
        /// <summary>The critical FOV punch: +2 deg, out with the kick envelope (zero under reduce motion).</summary>
        public const float CritFovPunchDeg = 2f;
        public const double CritFovPunchSeconds = 0.2;
        /// <summary>The critical hit-stop on both fighters (130-150 ms band; the plain top tier is 110 ms).</summary>
        public const double HoldCriticalOwn = 0.14;
        public static double HoldCritWallSeconds(bool reduceMotion) => reduceMotion ? 0 : HoldCriticalOwn;
        /// <summary>The critical burst: stage 1 the cracked impact frame, stage 2 a bigger star with radial lines 50 ms later.</summary>
        public const float BurstCritStage1 = 1.6f, BurstCritStage2 = 2.0f;
        public const double BurstCritStage2Delay = 0.05, BurstCritLife = 0.34;
        public const int CritRadialLines = 10;
        /// <summary>The critical victim recoil floor (u, inside MaxDrawnOffset) and the follow-through trail colour (hot gold).</summary>
        public const double CritMinVisualKnock = 0.34;
        public static readonly Color32 CritTrail = new Color32(255, 196, 64, 255);

        /// <summary>The critical punch envelope (0..1) t wall seconds after the hit: out in 25 ms, home by 0.2 s.</summary>
        public static float FovPunchEnvelope(double t)
        {
            if (!(t >= 0) || t >= CritFovPunchSeconds) return 0f;
            if (t < KickRise) return (float)(t / KickRise);
            double s = (t - KickRise) / (CritFovPunchSeconds - KickRise);
            return (float)(1 - s * s * (3 - 2 * s));
        }

        public static float KickShare(KickKind k) =>
            k == KickKind.ByMe ? KickShareByMe : k == KickKind.OnMe ? KickShareOnMe : k == KickKind.MyKill ? KickShareKill :
            k == KickKind.CritByMe ? KickShareByMe * CritKickMul : k == KickKind.CritOnMe ? KickShareOnMe * CritKickMul : KickShareDeath;

        public static float KickMin(KickKind k) =>
            k == KickKind.ByMe ? KickMinByMe : k == KickKind.OnMe ? KickMinOnMe : k == KickKind.MyKill ? KickMinKill :
            k == KickKind.CritByMe ? KickMinByMe * CritKickMul : k == KickKind.CritOnMe ? KickMinOnMe * CritKickMul : KickMinDeath;

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

    /// <summary>
    /// Round 3 (review pass 3: no HitFlash / HitStop in the ledger): reports each impact cue exactly once per occurrence,
    /// keyed on the occurrence's own start (flash start time, hold tick), so a cue is never lost to a one-frame latch and
    /// never reported twice. Pure; ActorViews keeps one Seen per actor.
    /// </summary>
    public static class ImpactCueLedger
    {
        public struct Seen { public double Flash; public int Hold; }

        public static Seen[] Make(int n)
        {
            var a = new Seen[n];
            for (int k = 0; k < n; k++) { a[k].Flash = double.NaN; a[k].Hold = int.MinValue; }
            return a;
        }

        /// <summary>True the first time `key` is seen (and remembers it).</summary>
        public static bool Once(ref double seen, double key)
        {
            if (double.IsNegativeInfinity(key) || key.Equals(seen)) return false;
            seen = key;
            return true;
        }

        public static bool Once(ref int seen, int key)
        {
            if (key == int.MinValue || key == seen) return false;
            seen = key;
            return true;
        }

        /// <summary>
        /// One drawn frame of actor `actor`'s impact cues (ActorViews.ReportCues): HitFlash once per flash start while an own
        /// flash is drawn, HitStop once per hold tick while an own hold is drawn, KnockVisual on the knock's check frame.
        /// `source` must be the drawn renderer (never null: the caller falls back to itself). Returns the cues reported.
        /// </summary>
        public static int Frame(ref Seen seen, int actor, UnityEngine.Object source,
                                bool flashOn, bool flashOwn, double flashStart, float flashStrength,
                                bool holding, bool holdOwn, int holdTick, float holdMs,
                                bool knockCheck, bool knockOwn, float knockDrawn)
        {
            int n = 0;
            if (flashOn && flashOwn && Once(ref seen.Flash, flashStart))
            { CriticalMass.Runtime.Game.PlayerFeedback.Report(CriticalMass.Runtime.Game.FeedbackChannel.HitFlash, actor, flashStrength, source); n++; }
            if (holding && holdOwn && holdMs > 0f && Once(ref seen.Hold, holdTick))
            { CriticalMass.Runtime.Game.PlayerFeedback.Report(CriticalMass.Runtime.Game.FeedbackChannel.HitStop, actor, holdMs, source); n++; }
            if (knockCheck && knockOwn)
            { CriticalMass.Runtime.Game.PlayerFeedback.Report(CriticalMass.Runtime.Game.FeedbackChannel.KnockVisual, actor, knockDrawn, source); n++; }
            return n;
        }
    }
}
