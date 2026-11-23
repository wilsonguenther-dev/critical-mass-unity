// Class moves build (2026-11-22), lane 1: every switch and every knob of the class-moves items T1-1..T1-8 and T2-1..T2-8
// (cm-incidents/class-moves-audit-2026-11-22/SPEC.md). Lane 1 owns this file; lanes 2-6 READ it and never edit it (a knob a
// lane finds missing is asked for in its report and added by the integrator at the combine).
namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// The class-moves switches (hung off DepthConfig.Moves). EVERY switch is false by default, and with every switch false
    /// the Classic, PhysicsOnly and Full goldens and the rules digest are bit for bit the base branch's: World.Moves is not
    /// even allocated (MovesRules.AnyOn), every hook is an identity, and ConfigHash mixes this block only while a switch is on.
    /// A switch acts only under the Full rules (MovesRules.On: DepthRules.On and CombatFeel.On).
    /// <para>The owner's play build turns every switch on through <see cref="PlayBuildAllOn"/>: MatchRequest.ToWorldOptions
    /// calls it when the scripting define CM_MOVES_ON is set (Project Settings, Player, Scripting Define Symbols, Standalone:
    /// add CM_MOVES_ON). Tests call it directly. Knobs keep the SPEC.md values; frame data is in 30 Hz sim ticks.</para>
    /// <para>Per-class arrays are indexed by ClassId: Warrior, Farmer, Miner, Demolitions, Builder, Healer.</para>
    /// </summary>
    public sealed class MovesConfig
    {
        // ================================================================== Tier 1 switches (repairs)

        /// <summary>T1-1 (lane 4): Demolitions' Attack is always the pry bar; F / U / pad Y throws TNT (the
        /// DemolitionsTntConfig.ThrowOnAttack = false path). Bots that threw through the Attack bit request it on DigPlace.</summary>
        public bool DemoTntOnDigOnly = false;
        /// <summary>T1-2 (lane 2): a dodge toward an enemy stops short at LungeStopShare x reach and an Attack buffered in the
        /// dash fires on the first recover tick with the wind-up x LungeWindupMul (LUNGE).</summary>
        public bool DodgeLunge = false;
        /// <summary>T1-3 (lane 2): any dodge that produces HitBlocked BlockedDodge gives back AvoidRefundShare of the cooldown left.</summary>
        public bool DodgeAvoidRefund = false;
        /// <summary>T1-4a (sim side lane 2, words lane 6): FeintBaited event when the victim started a dodge, guard or parry
        /// attempt FeintBaitBeforeTicks before the feint or FeintBaitAfterTicks after it. Event only.</summary>
        public bool FeintBait = false;
        /// <summary>T1-4b (lane 5): on a read human wind-up a bot steps out of reach (no dodge) with BotStepOutChance.</summary>
        public bool BotStepOut = false;
        /// <summary>T1-5 (lane 5): the wheel's SHORT WALL becomes the QUICK WALL (QuickWall* knobs).</summary>
        public bool BuilderQuickWall = false;
        // T1-6 (cast distance for stick and keys) is view and input only: the sim only receives an aim point. Its switch is
        // Runtime-side, CriticalMass.Runtime.Input.MovesInput.StickCastDistance (lane 1 file, lane 6 reads it).
        // T1-7 (the probe as tests) and T1-8 (replay to video) are tests and tooling: no switch.

        // ================================================================== Tier 2 switches (new verbs)

        /// <summary>T2-1 (lane 2): GUARD (hold InputFrame.Guard) and PARRY (a Guard press edge inside the window), every class;
        /// the Warrior parries only (his SHIELD stays his block). Bots guard / parry with the Bot* chances (lane 5).</summary>
        public bool Guard = false;
        /// <summary>T2-2 (lane 4): hold Attack to charge a heavy (tap stays light; a held Attack no longer auto-repeats).</summary>
        public bool Heavy = false;
        /// <summary>T2-3 (lane 4): light chains in the hit-cancel window (ComboSteps per class, finisher on the last step).</summary>
        public bool Combos = false;
        /// <summary>T2-4 (lane 3): TRAVEL, the automatic sprint out of combat.</summary>
        public bool Travel = false;
        /// <summary>T2-5 (lane 3): SLIDE, the dodge while travelling (a SLIDE TRIP on an enemy body). Needs Travel to ever fire.</summary>
        public bool Slide = false;
        /// <summary>T2-6 (lane 3): GLIDE, Healer only (GlideClasses): hold Jump after the apex (InputFrame.JumpHeld).</summary>
        public bool Glide = false;
        /// <summary>T2-7 (lanes 2 + 3): ground melee misses an airborne body on air ticks JumpEvadeFrom..JumpEvadeTo (lane 2's
        /// hit test, BlockedAir), then the landing recovery and the jump lock (lane 3).</summary>
        public bool JumpEvade = false;
        /// <summary>T2-8 (lane 2): once per life below SecondWindBelowShare, the next dodge / parry / guard block heals.</summary>
        public bool SecondWind = false;

        // ================================================================== Tier 1 knobs

        // ---- T1-2 lunge
        /// <summary>A dodge within this many degrees of an enemy direction is a dodge-in.</summary>
        public double LungeConeDeg = 35;
        /// <summary>The dash is clipped to end at this share of the dodger's reach from the enemy body.</summary>
        public double LungeStopShare = 0.85;
        /// <summary>The lunge swing's wind-up multiplier (Warrior 12 to 9 ticks).</summary>
        public double LungeWindupMul = 0.75;
        // ---- T1-3 refund
        /// <summary>Share of the dodge cooldown LEFT given back by a dodge that avoided a hit (BlockedDodge).</summary>
        public double AvoidRefundShare = 0.4;
        // ---- T1-4 feint bait and bot step-out
        public int FeintBaitBeforeTicks = 6;
        public int FeintBaitAfterTicks = 3;
        /// <summary>Bot step-out chance on a read human wind-up: Normal, Easy, Hard (BotDifficulty order).</summary>
        public double[] BotStepOutChance = { 0.30, 0.15, 0.45 };
        // ---- T1-5 quick wall
        public int QuickWallCells = 3;
        public double QuickWallDistance = 1.5;
        public int QuickWallWood = 1;
        public double QuickWallSeconds = 8;
        public double QuickWallHp = 60;
        public double QuickWallHillKeepOut = 3;
        public int QuickWallMaxPerBuilder = 1;

        // ================================================================== Tier 2 knobs

        // ---- T2-1 guard and parry (per class; the Warrior's guard row is unused: he parries only)
        /// <summary>Damage share a frontal guard cuts (Warrior 0 = no guard). Integrator (decision 1): the Healer's cut 0.35 -> 0.20.</summary>
        public double[] GuardCut = { 0, 0.40, 0.45, 0.35, 0.55, 0.20 };
        /// <summary>Damage the guard absorbs before it breaks.</summary>
        public double[] GuardMeter = { 0, 40, 50, 35, 60, 35 };
        /// <summary>Move speed multiplier while guarding.</summary>
        public double[] GuardMoveMul = { 1, 0.6, 0.6, 0.6, 0.5, 0.6 };
        /// <summary>Parry window: ticks before an enemy melee contact in which a Guard press edge parries (0 = no parry).
        /// Integrator (combine 2026-11-22, ruling "Healer loses nearly every one-on-one"): the Healer has NO parry (0); her
        /// guard only cuts damage (a tap of Guard holds her guard ParryWhiffHoldTicks, with no parry lockout).</summary>
        public int[] ParryWindowTicks = { 3, 4, 4, 4, 3, 0 };
        /// <summary>The Warrior's parry window while his shield is down (3 with it up is ParryWindowTicks[0]).</summary>
        public int WarriorParryWindowShieldDown = 2;
        public double GuardFrontalDeg = 60;
        public int GuardRaiseTicks = 3;
        public int GuardReleaseTicks = 2;
        public double GuardRefillPerSecond = 15;
        public double GuardRefillDelaySeconds = 1.0;
        public int GuardBreakStaggerTicks = 15;
        public double GuardDrainWarriorCleave = 1.5;
        public double GuardDrainMinerPick = 2.0;
        /// <summary>TNT and mines are cut by this share of the class cut; the Kaiju is never cut.</summary>
        public double GuardBlastCutShare = 0.5;
        public int ParryStaggerTicks = 12;
        /// <summary>The parrier's next melee hit inside this is the existing PUNISH critical (no new crit kind).</summary>
        public double ParryPunishSeconds = 0.8;
        /// <summary>A Guard press outside the window holds the guard at least this many ticks (a whiffed parry).</summary>
        public int ParryWhiffHoldTicks = 9;
        public double ParryLockoutSeconds = 1.0;
        /// <summary>Bot guard chance on a read human wind-up (Normal, Easy, Hard) and bot parry chance (Hard only).</summary>
        public double[] BotGuardChance = { 0.15, 0.0, 0.40 };
        public double[] BotParryChance = { 0.0, 0.0, 0.10 };

        // ---- T2-2 heavy / charged
        public int ChargeStartTick = 8;
        public int ChargeFullTick = 24;
        public int ChargeAutoReleaseTick = 36;
        public double ChargeDamageMulMax = 1.6;
        public double ChargeKnockMulMax = 1.6;
        public double ChargeMoveMul = 0.5;
        public int HeavyWhiffExtraRecoveryTicks = 8;
        /// <summary>Cancel during the charge is a feint with this cooldown refund (as today).</summary>
        public double ChargeCancelRefund = 0.7;
        /// <summary>Heavy shape per class: reach, arc in degrees (the Builder's slam is a 360 radius).</summary>
        public double[] HeavyReach = { 1.8, 2.9, 1.5, 1.4, 1.8, 1.6 };
        public double[] HeavyArcDeg = { 70, 30, 90, 90, 360, 180 };
        /// <summary>Extra knock multiplier per class on top of the charge (Farmer x2.0, Builder 1.5 radial, Healer x1.4).</summary>
        public double[] HeavyKnockMul = { 1.0, 2.0, 1.0, 1.0, 1.5, 1.4 };
        /// <summary>False = the heavy has no damage bonus (the Healer's flare is a disengage).</summary>
        public bool[] HeavyDamageBonus = { true, true, true, true, true, false };
        public double MinerSunderSeconds = 5;
        public double MinerHeavyGuardDrain = 3.0;
        public double DemoFuseDamage = 12;
        public double DemoFuseSeconds = 1.0;
        public double DemoFuseRadius = 1.0;

        // ---- T2-3 combos
        /// <summary>Steps per class (the last step is the finisher; 1 = no combo). Integrator (decision 1, the Healer ruling): the
        /// Healer has no chain (2 -> 1; with it her duel variants reached 10 to 14 of 40).</summary>
        public int[] ComboSteps = { 2, 3, 3, 2, 3, 1 };   // integrator: Demolitions 2 (strong-with-combos 34->32 at 3, 34->34 at 2 with finisher 1.3); Miner stays 3 (at 2 his combos went 31->33 but the Healer strong row rose to 9 of 40, over the ruling); Builder 3; Healer 1 (none)
        public double ComboStepWindupMul = 0.7;
        public double ComboStepDamageMul = 1.1;
        public double FinisherDamageMul = 1.35;
        public double FinisherKnockMul = 1.8;
        /// <summary>Integrator: per-class finisher damage, -1 = FinisherDamageMul. Demolitions on 2 steps: passive time to kill
        /// fell 0.170 at 1.35 (band 0.05 to 0.15).</summary>
        public double[] FinisherDamageMulByClass = { -1, -1, -1, 1.3, -1, -1 };   // Demolitions 1.3: time to kill falls 0.125, strong-with-combos 34 -> 34
        public int FinisherExtraRecoveryTicks = 6;
        public double ComboResetSeconds = 1.0;

        // ---- T2-4 travel
        public int TravelAfterMoveTicks = 15;   // integrator decision 5: x1.5 after 15 (16v16 respawn-to-first-hit 12.1 s -> 9.8 s; Warrior slowest, S58 0 stuck, bands hold)
        public double TravelEnemyRadius = 8;
        public double TravelNoCombatSeconds = 3;
        public double TravelSpeedMul = 1.5;
        public int TravelRampTicks = 9;

        // ---- T2-5 slide
        public double SlideDistance = 4.5;
        public int SlideTicks = 10;
        public double SlideTripDamage = 8;
        public int SlideTripStaggerTicks = 10;
        public double SlideTripKnock = 0.5;
        public int SlideTripRecoveryTicks = 6;

        // ---- T2-6 glide
        /// <summary>Classes that glide (Healer only by default; adding the Farmer later is one entry).</summary>
        public bool[] GlideClasses = { false, false, false, false, false, true };
        public int GlideFromAirTick = 9;
        public int GlideExtraTicks = 24;
        public double GlideSpeedMul = 0.9;
        public int GlideMaxVoidCells = 3;
        public double GlideCooldownSeconds = 4;

        // ---- T2-7 jump evade
        public int JumpEvadeFromAirTick = 5;
        public int JumpEvadeToAirTick = 12;
        public int JumpEvadeLandingTicks = 4;
        public double JumpEvadeRelockSeconds = 1.0;

        // ---- T2-8 second wind
        public double SecondWindBelowShare = 0.25;
        public double SecondWindHealShare = 0.15;

        // ================================================================== integrator knobs (combine 2026-11-22)

        /// <summary>T2-3 (lane 2 rule, decision 2 "every combo is dodgeable"): with Combos on, a victim whose stagger is over may
        /// start a dodge during the knock slide of an enemy's melee LIGHT strike (not a heavy) taken in the last
        /// ComboKnockDodgeTicks; the dodge ends the slide. Without it the Farmer's thrust slide outlasts the gap between steps.</summary>
        public int ComboDodgeOutOfKnock = 1;   // int, not bool: every public bool here is a SWITCH (MovesContractTests); 1 = on
        /// <summary>T2-1 (decision 1, ruling "Healer loses nearly every one-on-one"): a guard-blocked melee hit neither staggers nor
        /// fully knocks a class marked true here. The Healer's guard only cuts damage: she still staggers and slides.</summary>
        public bool[] GuardKeepsFooting = { true, true, true, true, true, false };
        /// <summary>T2-1 (decision 1): per-class recovery after the guard drops, -1 = GuardReleaseTicks. The Healer's guard is slow
        /// to drop (her weakest guard): block-then-punish is not her game.</summary>
        public int[] GuardReleaseTicksByClass = { -1, -1, -1, -1, -1, 16 };
        public int ComboKnockDodgeTicks = 20;
        /// <summary>T2-3 (decision 2): a victim's dodge breaks every chain whose last landed strike hit it, so the next swing is an
        /// opener (no step bonus, no finisher). Without it the fastest class (the Farmer) walks a dodged victim down inside the
        /// chain window and the dodge never escapes the combo.</summary>
        public int ComboBreaksOnDodge = 1;   // 1 = on (int: see ComboDodgeOutOfKnock)
        /// <summary>T1-2 (decision 6): a LUNGE wind-up shrugs off a hit under the lunger's class Poise (no stagger, the swing goes
        /// on), so a fleeing target's light hit does not cancel the lunge it ran into.</summary>
        public int LungeArmour = 1;   // 1 = on (int: see ComboDodgeOutOfKnock)
        /// <summary>T1-2 (decision 6): per-class lunge wind-up multiplier, -1 = LungeWindupMul. The Miner's 0.6: his lunges whiffed
        /// 61 of 115 contacts on a fleeing runner at 0.75 (dodge-in caught 15 of 40, plain walking 25 of 40); at 0.6, 30 of 40.</summary>
        public double[] LungeWindupMulByClass = { -1, -1, 0.6, -1, -1, -1 };
        /// <summary>T2-5 (lane 3's constant, now a knob): after TRAVEL ends because an enemy came within TravelEnemyRadius, Dodge
        /// stays the SLIDE for this many ticks unless a press, a hit or an enemy wind-up within 8 u ends it.</summary>
        public int SlideCarryTicks = 30;
        /// <summary>T2-2 / T2-3 bot use (lane 5's constants, now knobs; Normal, Easy, Hard): the chance a bot charges a heavy at a
        /// human in heavy reach once per swing cycle, and the chance it chains a LANDED light hit on a human.</summary>
        public double[] BotChargeChance = { 0.10, 0.0, 0.20 };
        public double[] BotComboChance = { 0.25, 0.0, 0.50 };

        /// <summary>True when any switch is on (World.Moves is allocated only then; ConfigHash mixes this block only then).</summary>
        public bool AnyOn =>
            DemoTntOnDigOnly || DodgeLunge || DodgeAvoidRefund || FeintBait || BotStepOut || BuilderQuickWall
            || Guard || Heavy || Combos || Travel || Slide || Glide || JumpEvade || SecondWind;

        /// <summary>The owner's play build: every switch on (knobs unchanged). Called by MatchRequest under CM_MOVES_ON.</summary>
        public static MovesConfig PlayBuildAllOn(MovesConfig m)
        {
            if (m == null) return null;
            m.DemoTntOnDigOnly = m.DodgeLunge = m.DodgeAvoidRefund = m.FeintBait = m.BotStepOut = m.BuilderQuickWall = true;
            m.Guard = m.Heavy = m.Combos = m.Travel = m.Slide = m.Glide = m.JumpEvade = m.SecondWind = true;
            return m;
        }

        /// <summary>Every switch off (knobs unchanged): the base branch's game.</summary>
        public static MovesConfig AllOff(MovesConfig m)
        {
            if (m == null) return null;
            m.DemoTntOnDigOnly = m.DodgeLunge = m.DodgeAvoidRefund = m.FeintBait = m.BotStepOut = m.BuilderQuickWall = false;
            m.Guard = m.Heavy = m.Combos = m.Travel = m.Slide = m.Glide = m.JumpEvade = m.SecondWind = false;
            return m;
        }

        /// <summary>The switches as a bit mask (bit 0 DemoTntOnDigOnly .. bit 13 SecondWind, declaration order): what a replay
        /// header stores (ReplayStart.MovesMask) so playback rebuilds the recorded switches. Knobs are not in it.</summary>
        public int Mask()
        {
            bool[] b = { DemoTntOnDigOnly, DodgeLunge, DodgeAvoidRefund, FeintBait, BotStepOut, BuilderQuickWall,
                Guard, Heavy, Combos, Travel, Slide, Glide, JumpEvade, SecondWind };
            int m = 0;
            for (int i = 0; i < b.Length; i++) if (b[i]) m |= 1 << i;
            return m;
        }

        /// <summary>Sets every switch from a <see cref="Mask"/> value (knobs unchanged).</summary>
        public MovesConfig SetMask(int m)
        {
            DemoTntOnDigOnly = (m & 1) != 0; DodgeLunge = (m & 2) != 0; DodgeAvoidRefund = (m & 4) != 0; FeintBait = (m & 8) != 0;
            BotStepOut = (m & 16) != 0; BuilderQuickWall = (m & 32) != 0; Guard = (m & 64) != 0; Heavy = (m & 128) != 0;
            Combos = (m & 256) != 0; Travel = (m & 512) != 0; Slide = (m & 1024) != 0; Glide = (m & 2048) != 0;
            JumpEvade = (m & 4096) != 0; SecondWind = (m & 8192) != 0;
            return this;
        }

        public MovesConfig Clone()
        {
            var c = (MovesConfig)MemberwiseClone();
            c.BotStepOutChance = (double[])BotStepOutChance.Clone();
            c.GuardCut = (double[])GuardCut.Clone();
            c.GuardMeter = (double[])GuardMeter.Clone();
            c.GuardMoveMul = (double[])GuardMoveMul.Clone();
            c.ParryWindowTicks = (int[])ParryWindowTicks.Clone();
            c.BotGuardChance = (double[])BotGuardChance.Clone();
            c.BotParryChance = (double[])BotParryChance.Clone();
            c.HeavyReach = (double[])HeavyReach.Clone();
            c.HeavyArcDeg = (double[])HeavyArcDeg.Clone();
            c.HeavyKnockMul = (double[])HeavyKnockMul.Clone();
            c.HeavyDamageBonus = (bool[])HeavyDamageBonus.Clone();
            c.ComboSteps = (int[])ComboSteps.Clone();
            c.GlideClasses = (bool[])GlideClasses.Clone();
            c.BotChargeChance = (double[])BotChargeChance.Clone();
            c.GuardKeepsFooting = (bool[])GuardKeepsFooting.Clone();
            c.GuardReleaseTicksByClass = (int[])GuardReleaseTicksByClass.Clone();
            c.LungeWindupMulByClass = (double[])LungeWindupMulByClass.Clone();
            c.FinisherDamageMulByClass = (double[])FinisherDamageMulByClass.Clone();
            c.BotComboChance = (double[])BotComboChance.Clone();
            return c;
        }
    }
}
