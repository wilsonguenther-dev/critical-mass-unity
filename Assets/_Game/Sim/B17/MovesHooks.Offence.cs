// Class moves build (2026-11-22): LANE 4 (Offence) owns this file: T1-1 TNT on its own key, T2-2 heavy / charged, T2-3 combos.
// Contract: cm-incidents/class-moves-audit-2026-11-22/LANE1-CONTRACT.md; items: SPEC.md T1-1, T2-2, T2-3.
//
// INPUT SEMANTICS (T2-2, switch Heavy). A HUMAN seat (Actor.IsHuman, not on the idle autopilot): holding Attack no longer
// auto-repeats light swings. A tap (released before ChargeStartTick of ready hold) is the light swing, started on the release
// tick (the CombatFeel input buffer still holds it BufferSeconds). A hold of ChargeStartTick or more is a CHARGE (move x
// ChargeMoveMul, charge armour against hits under the class Poise); the HEAVY fires on release, or on its own at
// ChargeAutoReleaseTick, with the class wind-up; charge share = (hold - ChargeStartTick) / (ChargeFullTick - ChargeStartTick),
// clamped 0..1. The hold counts only from the tick the actor could swing (no swing winding up, cooldown spent), so a press held
// through a cooldown charges from the moment the swing would be accepted. A dodge, a stagger, a raised guard or death breaks
// the charge (no swing; let go and press again). CANCEL during the charge is a feint (FeintRecoverySeconds, ChargeCancelRefund
// of a class cooldown back, the feint lockout, the Feint event Value 1 and FeintBaited through MovesHooks.Feinted).
// A BOT's Attack bit keeps its old meaning (a press is a light at once): bots hold Attack while a target is in reach, so the
// hold rule would turn every bot swing into an auto-released heavy. Bots charge only through OffenceBotCharge (lane 5).
//
// COMBOS (T2-3, switch Combos; human seats always, bots under the cap in OffenceMayChain / OffenceBotChains): a light swing that LANDS opens the chain until its cooldown is spent plus the
// input buffer (BufferSeconds), at most ComboResetSeconds; the next light swing started inside it is the next step (wind-up x ComboStepWindupMul, never under 5 ticks, damage x ComboStepDamageMul);
// the class's last step is the FINISHER (damage x FinisherDamageMul, knock x FinisherKnockMul, recovery +
// FinisherExtraRecoveryTicks) and closes the chain. A whiff, a heavy, or the window running out resets to step 1. Steps keep
// the normal cooldown (the press may come during the recovery; the buffer holds it): the chain adds damage, not swings.
//
// Everything here acts only while its own switch is on (MovesHooks.On(w, Cfg(w).X)); with every switch off World.Moves is
// null and no method below is reached. Deterministic: plain arithmetic in actor-id order, no RNG, no allocation per tick.
using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>Lane 4's class-moves state (World.Moves.Offence). Every field: public, a primitive array sized in Init.</summary>
    public sealed class OffenceMovesState
    {
        /// <summary>Ticks of the current ready hold of Attack (human seats; a bot's OffenceBotCharge hold). 0 = not holding.</summary>
        public int[] HoldCount = new int[0];
        /// <summary>1 = this hold was used (heavy fired on its own, the charge broke or was cancelled): nothing until released.</summary>
        public byte[] HoldSpent = new byte[0];
        /// <summary>1 while charging (hold at or past ChargeStartTick): view (CHARGE slot, glow, the heavy's ground arc).</summary>
        public byte[] Charging = new byte[0];
        /// <summary>The charge share 0..1 of the current charge (view).</summary>
        public double[] ChargeShare = new double[0];
        /// <summary>A heavy released and waiting to start (its charge share), -1 = none; and the last tick it may still start.</summary>
        public double[] PendingHeavy = new double[0];
        public int[] PendingHeavyUntil = new int[0];
        /// <summary>1 = the release of a spent hold: the input buffer's copy of it is dropped.</summary>
        public byte[] DropRelease = new byte[0];
        /// <summary>The SwingTick of the actor's heavy swing and its charge share (the swing is a heavy while SwingTick matches).</summary>
        public int[] HeavySwingTick = new int[0];
        public double[] HeavySwingShare = new double[0];
        /// <summary>The SwingTick of the actor's last light swing and its combo step (1 = the opener).</summary>
        public int[] StepSwingTick = new int[0];
        public int[] StepOf = new int[0];
        /// <summary>The next combo step (0 = none) and the last tick a swing may start as that step.</summary>
        public int[] ChainNext = new int[0];
        public int[] ChainUntil = new int[0];
        /// <summary>fix/mv-combo-buffer: the tick of a person's Attack press made in the active / recovery frames of a swing that
        /// opened the chain (NoTick = none); it starts the next step on the first tick the swing can start, inside the chain.</summary>
        public int[] ComboPressTick = new int[0];
        /// <summary>fix/mv-combo-reach: cooldown ticks a person's open chain borrowed (the next step may start when the recovery
        /// ends); paid back on the finisher, a step's whiff, or the next opener, so a whole chain keeps the class's swing rate.</summary>
        public int[] ChainDebt = new int[0];
        /// <summary>Integrator (decision 2): the body the actor's last landed strike hit (its dodge breaks the chain), -1 = none.</summary>
        public int[] ChainVictim = new int[0];
        /// <summary>Demolitions' fuse strike: the tick a fuse stuck on this body blows (NoTick = none) and who lit it.</summary>
        public int[] FuseTick = new int[0];
        public int[] FuseOwner = new int[0];
        /// <summary>Lane 5's override of a bot's combo use (OffenceBotChains): 0 = the default cap below, 1 = always chain, 2 = never.
        /// Default cap: a bot chains fully against bots; against a human seat Easy never chains, Normal chains every second
        /// chance (BotChainFlip), Hard always (the combo stays the human seat's skill payoff against Normal bots).</summary>
        public byte[] BotChains = new byte[0];
        public byte[] BotChainFlip = new byte[0];
        /// <summary>1 = the actor's last landed melee strike was on a human seat (the bot chain cap reads it).</summary>
        public byte[] LastHitHuman = new byte[0];
        /// <summary>Lane 5: ticks a bot holds its charge (OffenceBotCharge), 0 = none.</summary>
        public int[] BotChargeLeft = new int[0];
        /// <summary>[0] = the amount of the melee strike being applied now (Combat sets it before CombatFeel.ApplyStrike; charge armour).</summary>
        public double[] StrikeAmount = new double[1];
        /// <summary>Counters for tests and the balance print: heavies released, full charges, landed heavies, combo steps landed, finishers, fuses blown.</summary>
        public int[] Counts = new int[6];

        public void Init(World w)
        {
            int n = World.ActorCapacity(w.Config);
            HoldCount = new int[n];
            HoldSpent = new byte[n];
            Charging = new byte[n];
            ChargeShare = new double[n];
            PendingHeavy = new double[n];
            PendingHeavyUntil = new int[n];
            DropRelease = new byte[n];
            HeavySwingTick = new int[n];
            HeavySwingShare = new double[n];
            StepSwingTick = new int[n];
            StepOf = new int[n];
            ChainNext = new int[n];
            ChainUntil = new int[n];
            ComboPressTick = new int[n];
            ChainDebt = new int[n];
            ChainVictim = new int[n];
            FuseTick = new int[n];
            FuseOwner = new int[n];
            BotChargeLeft = new int[n];
            BotChains = new byte[n];
            BotChainFlip = new byte[n];
            LastHitHuman = new byte[n];
        }

        public void Reset(World w)
        {
            for (int i = 0; i < HoldCount.Length; i++)
            {
                HoldCount[i] = 0;
                HoldSpent[i] = 0;
                Charging[i] = 0;
                ChargeShare[i] = 0;
                PendingHeavy[i] = -1;
                PendingHeavyUntil[i] = CombatFeel.NoTick;
                DropRelease[i] = 0;
                HeavySwingTick[i] = CombatFeel.NoTick;
                HeavySwingShare[i] = 0;
                StepSwingTick[i] = CombatFeel.NoTick;
                StepOf[i] = 0;
                ChainNext[i] = 0;
                ChainUntil[i] = CombatFeel.NoTick;
                ComboPressTick[i] = CombatFeel.NoTick;
                ChainDebt[i] = 0;
                ChainVictim[i] = -1;
                FuseTick[i] = CombatFeel.NoTick;
                FuseOwner[i] = -1;
                BotChargeLeft[i] = 0;
                BotChains[i] = 0;
                BotChainFlip[i] = 0;
                LastHitHuman[i] = 0;
            }
            StrikeAmount[0] = 0;
            for (int k = 0; k < Counts.Length; k++) Counts[k] = 0;
        }
    }

    public static partial class MovesHooks
    {
        /// <summary>Counter slots of OffenceMovesState.Counts.</summary>
        public const int OffenceCountHeavies = 0, OffenceCountFullCharges = 1, OffenceCountHeavyHits = 2, OffenceCountSteps = 3, OffenceCountFinishers = 4, OffenceCountFuses = 5;

        // ================================================================== public queries (view, lane 2, lane 5, Combat, Demolitions)

        /// <summary>T1-1: Demolitions' Attack is always the pry bar (TNT on F / U / pad Y only).</summary>
        public static bool OffenceTntOnDigOnly(World w) => On(w, Cfg(w).DemoTntOnDigOnly);

        /// <summary>Integrator (lane 6's request): the config-only form of T1-1 for the view (HUD, aim assist, the controls card):
        /// with DemoTntOnDigOnly on under the rules where class moves run, a person's Attack is never the TNT throw.</summary>
        public static bool OffenceConfigTntOnDigOnly(GameConfig c) =>
            c != null && c.Depth != null && c.Depth.Moves != null && c.Depth.Moves.DemoTntOnDigOnly && !c.Depth.Classic && !c.Physics.Classic && CombatFeel.On(c);

        /// <summary>Integrator (combine): the one rule every DemolitionsTnt.ThrowOnAttack reader uses (Combat, Demolitions.AttackThrowsNow,
        /// the bots' attack shaping): her Attack press may be the TNT throw unless DemoTntOnDigOnly is on and a person holds the
        /// controls (a bot's attack intent with the TNT ready stays its throw request, so bot TNT use is unchanged).</summary>
        public static bool OffenceAttackMayThrow(World w, int id) =>
            w.Config.Abilities.DemolitionsTnt.ThrowOnAttack && !(w.Moves != null && OffenceTntOnDigOnly(w) && OffenceHuman(w, id));

        /// <summary>A person at the controls of actor id (not a bot, not the idle autopilot): the hold-to-charge input applies.</summary>
        public static bool OffenceHuman(World w, int id) =>
            id >= 0 && id < w.ActorCount && w.Actors[id].IsHuman && !MechRules.Autopiloted(w, id)
            && !(id == 0 && Bots.BotBrain.ActorZeroOnAutopilot(w));   // fix/mv-heavy-press: a bot-only soak's null-frame seat is a bot (lane 5's BotsHumanSeat rule)

        /// <summary>True when actor id's current (or just-landed) swing is a heavy.</summary>
        public static bool OffenceIsHeavy(World w, int id)
        {
            if (w.Moves == null || id < 0 || id >= w.ActorCount) return false;
            return w.Moves.Offence.HeavySwingTick[id] == w.Actors[id].SwingTick;
        }

        /// <summary>The charge share 0..1 of actor id's current heavy swing, or -1 when it is not a heavy.</summary>
        public static double OffenceHeavyShare(World w, int id) => OffenceIsHeavy(w, id) ? w.Moves.Offence.HeavySwingShare[id] : -1;

        /// <summary>View: actor id is charging (and how far: 0..1). The enemy's telegraph draws the heavy shape below.</summary>
        public static bool OffenceCharging(World w, int id, out double share)
        {
            share = 0;
            if (w.Moves == null || id < 0 || id >= w.ActorCount || w.Moves.Offence.Charging[id] == 0) return false;
            share = w.Moves.Offence.ChargeShare[id];
            return true;
        }

        /// <summary>The heavy's reach and full arc in degrees for class cls (the view's ground arc while charging).</summary>
        public static void OffenceHeavyShape(World w, ClassId cls, out double reach, out double arcDeg)
        {
            MovesConfig m = Cfg(w);
            int c = (int)cls;
            reach = m.HeavyReach != null && c < m.HeavyReach.Length ? m.HeavyReach[c] : w.Config.Class(cls).AttackRange;
            arcDeg = m.HeavyArcDeg != null && c < m.HeavyArcDeg.Length ? m.HeavyArcDeg[c] : w.Config.Class(cls).AttackArcDeg;
        }

        /// <summary>The combo step of actor id's current light swing (1 = opener, 0 = none / not a light).</summary>
        public static int OffenceComboStep(World w, int id)
        {
            if (w.Moves == null || id < 0 || id >= w.ActorCount) return 0;
            OffenceMovesState s = w.Moves.Offence;
            return s.StepSwingTick[id] == w.Actors[id].SwingTick ? s.StepOf[id] : 0;
        }

        /// <summary>Lane 2 (guard): the Warrior's FULL-charge overhead breaks any guard at once (SPEC T2-2).</summary>
        public static bool OffenceHeavyBreaksGuard(World w, int source) =>
            source >= 0 && source < w.ActorCount && w.Actors[source].Cls == ClassId.Warrior && OffenceHeavyShare(w, source) >= 1 - 1e-9;

        /// <summary>Lane 2 (guard): the guard drain multiplier of source's strike from lane 4's verbs (the Miner's armour breaker:
        /// MinerHeavyGuardDrain, replacing the pick's GuardDrainMinerPick); 0 = no lane-4 rule (lane 2 uses its own).</summary>
        public static double OffenceGuardDrainMul(World w, int source) =>
            source >= 0 && source < w.ActorCount && w.Actors[source].Cls == ClassId.Miner && OffenceIsHeavy(w, source) ? Cfg(w).MinerHeavyGuardDrain : 0;

        /// <summary>Lane 2 (jump evade, T2-7): the Builder's ground slam still hits an airborne body.</summary>
        public static bool OffenceHitsAirborne(World w, int source) =>
            source >= 0 && source < w.ActorCount && w.Actors[source].Cls == ClassId.Builder && OffenceIsHeavy(w, source);

        /// <summary>Lane 5: bot id holds a charge for ticks (clamped to ChargeStartTick..ChargeAutoReleaseTick, 30 Hz ticks) and
        /// then releases the heavy. Ignored while the Heavy switch is off, for a human seat, or while a charge is already held.</summary>
        public static void OffenceBotCharge(World w, int id, int ticks)
        {
            if (!On(w, Cfg(w).Heavy) || id < 0 || id >= w.ActorCount || OffenceHuman(w, id)) return;
            OffenceMovesState s = w.Moves.Offence;
            if (s.BotChargeLeft[id] > 0 || s.HoldCount[id] > 0) return;
            MovesConfig m = Cfg(w);
            int lo = Durations.T30(w.Config, m.ChargeStartTick), hi = Durations.T30(w.Config, m.ChargeAutoReleaseTick);
            int t = Durations.T30(w.Config, ticks);
            s.BotChargeLeft[id] = t < lo ? lo : t > hi ? hi : t;
        }

        /// <summary>Lane 5: override bot id's combo use: 1 = always chain, 2 = never, 0 = lane 4's default cap. Human seats always chain.</summary>
        public static void OffenceBotChains(World w, int id, byte mode)
        {
            if (w.Moves == null || id < 0 || id >= w.ActorCount) return;
            w.Moves.Offence.BotChains[id] = mode;
        }

        /// <summary>Integrator (decision 2, "every combo is dodgeable"): actor id started a dodge; every chain whose last landed
        /// strike hit id is broken (the next swing is an opener). Lane 2's DefenceDodgeBegan calls it.</summary>
        public static void OffenceVictimDodged(World w, int id)
        {
            if (w.Moves == null) return;
            MovesConfig m = Cfg(w);
            if (!m.Combos || m.ComboBreaksOnDodge == 0) return;
            OffenceMovesState s = w.Moves.Offence;
            for (int i = 0; i < w.ActorCount; i++)
                if (s.ChainNext[i] != 0 && s.ChainVictim[i] == id) s.ChainNext[i] = 0;
        }

        /// <summary>May actor i take the combo step it is entitled to now (human seats always; bots by the cap above).</summary>
        static bool OffenceMayChain(World w, OffenceMovesState s, int i)
        {
            if (OffenceHuman(w, i)) return true;
            if (s.BotChains[i] == 1) return true;
            if (s.BotChains[i] == 2) return false;
            if (s.LastHitHuman[i] == 0) return false;   // bot against bot: exactly today's swings (orchestrator 2026-11-23)
            switch (Bots.BotSkill.Of(w, i))
            {
                case BotDifficulty.Easy: return false;
                case BotDifficulty.Hard: return true;
                default:
                    s.BotChainFlip[i] ^= 1;
                    return s.BotChainFlip[i] == 0;
            }
        }

        // ================================================================== Combat.cs / Demolitions.cs helpers (lane 4's own files)

        /// <summary>Combat, right before CombatFeel.ApplyStrike: the strike's amount (charge armour reads it in StrikeKnock).</summary>
        public static void OffenceBeforeStrike(World w, double amount)
        {
            if (w.Moves == null) return;
            w.Moves.Offence.StrikeAmount[0] = amount;
        }

        /// <summary>Combat, right after CombatFeel.ApplyStrike dealt (dealt &gt; 0 landed): the Miner's 5 s SUNDER and the
        /// Demolitions fuse of a heavy.</summary>
        public static void OffenceAfterStrike(World w, int target, int source, double dealt)
        {
            if (w.Moves == null || !(dealt > 0) || source < 0 || source >= w.ActorCount || target < 0 || target >= w.ActorCount) return;
            OffenceMovesState s = w.Moves.Offence;
            s.LastHitHuman[source] = OffenceHuman(w, target) ? (byte)1 : (byte)0;   // a person's seat (an autopiloted seat is a bot)
            s.ChainVictim[source] = target;
            if (!On(w, Cfg(w).Heavy) || !OffenceIsHeavy(w, source)) return;
            s.Counts[OffenceCountHeavyHits]++;
            ClassId cls = w.Actors[source].Cls;
            if (cls == ClassId.Miner)
            {
                int until = w.Tick + CombatFeel.Ticks(w, Cfg(w).MinerSunderSeconds);
                if (until > w.Depth.Feel.SunderUntil[target]) w.Depth.Feel.SunderUntil[target] = until;
            }
            else if (cls == ClassId.Demolitions && w.Actors[target].Alive && Cfg(w).DemoFuseDamage > 0)
            {
                s.FuseTick[target] = w.Tick + CombatFeel.Ticks(w, Cfg(w).DemoFuseSeconds);
                s.FuseOwner[target] = source;
            }
        }

        /// <summary>Combat, before the AttackStart telegraph of a swing starting now: the heavy's real reach and arc.</summary>
        public static void OffenceTelegraph(World w, int i, ref double range, ref double arcDeg)
        {
            if (w.Moves == null || w.Moves.Offence.HeavySwingTick[i] != w.Tick) return;
            OffenceHeavyShape(w, w.Actors[i].Cls, out range, out arcDeg);
        }

        /// <summary>Combat: the Healer's press mends first, except a heavy release (the flare always swings).</summary>
        public static bool OffenceHeavyStartingNow(World w, int i) =>
            w.Moves != null && On(w, Cfg(w).Heavy) && w.Moves.Offence.PendingHeavy[i] >= 0;

        // ================================================================== hook bodies (signatures are lane 1's)

        /// <summary>Per tick, before Abilities: the charge state machine, auto-release, the cancel of a charge, fuse blasts.</summary>
        static void OffenceStep(World w, double dt)
        {
            MovesConfig m = Cfg(w);
            bool heavy = On(w, m.Heavy);
            OffenceMovesState s = w.Moves.Offence;
            if (heavy)
            {
                MovesState ms = w.Moves;
                int start = Durations.T30(w.Config, m.ChargeStartTick), full = Durations.T30(w.Config, m.ChargeFullTick), auto = Durations.T30(w.Config, m.ChargeAutoReleaseTick);
                for (int i = 0; i < w.ActorCount; i++)
                {
                    Actor a = w.Actors[i];
                    s.DropRelease[i] = 0;
                    bool human = OffenceHuman(w, i);
                    bool live = a.Alive && a.StandUp <= 0;
                    if (!live)
                    {
                        s.ChainDebt[i] = 0;
                        OffenceClearHold(s, i);
                        s.PendingHeavy[i] = -1;
                        s.BotChargeLeft[i] = 0;
                        continue;
                    }
                    if (human)
                    {
                        OffenceHumanHold(w, s, ms, i, a, start, full, auto);
                        continue;
                    }
                    // a bot: only lane 5's deliberate OffenceBotCharge holds a charge (the swing waits, then the heavy fires)
                    bool held = s.BotChargeLeft[i] > 0;
                    if (!held)
                    {
                        OffenceClearHold(s, i);
                        continue;
                    }
                    if (s.HoldSpent[i] != 0) continue;
                    bool charging = s.HoldCount[i] >= start;
                    // a dodge, a stagger (a hit at or over the Poise got through the charge armour), a raised guard: the charge breaks
                    bool broken = Dodge.Active(w, i) || a.HitStunTicks > 0 || ms.GuardHeldTicks[i] > 0;
                    if (broken)
                    {
                        if (s.HoldCount[i] > 0) { s.HoldSpent[i] = 1; s.HoldCount[i] = 0; s.Charging[i] = 0; s.ChargeShare[i] = 0; s.BotChargeLeft[i] = 0; }
                        continue;
                    }
                    if (charging && w.Inputs[i].Cancel && OffenceCancelCharge(w, s, i)) continue;
                    bool ready = !a.SwingPending && Combat.TickDown(a.AttackCd, dt) <= 0;
                    if (s.HoldCount[i] > 0 || ready) s.HoldCount[i]++;
                    if (!human && s.BotChargeLeft[i] > 0 && s.HoldCount[i] > 0) s.BotChargeLeft[i]--;
                    int h = s.HoldCount[i];
                    if (h >= start)
                    {
                        s.Charging[i] = 1;
                        s.ChargeShare[i] = OffenceShare(h, start, full);
                    }
                    bool botDone = !human && s.BotChargeLeft[i] <= 0 && h > 0;
                    if (h >= auto || botDone)
                    {
                        if (h >= start) OffenceRelease(w, s, i, h, start, full);
                        s.HoldSpent[i] = human ? (byte)1 : (byte)0;
                        s.HoldCount[i] = 0;
                        s.Charging[i] = 0;
                        s.ChargeShare[i] = 0;
                        s.BotChargeLeft[i] = 0;
                    }
                }
            }
            // T2-2 Demolitions fuse strike: a fuse stuck on a body blows DemoFuseSeconds after the heavy landed.
            if (heavy)
            {
                for (int j = 0; j < w.ActorCount; j++)
                {
                    if (s.FuseTick[j] == CombatFeel.NoTick || w.Tick < s.FuseTick[j]) continue;
                    s.FuseTick[j] = CombatFeel.NoTick;
                    int owner = s.FuseOwner[j];
                    s.FuseOwner[j] = -1;
                    Demolitions.FuseBlast(w, owner, w.X[j], w.Z[j], m.DemoFuseRadius, m.DemoFuseDamage);
                    s.Counts[OffenceCountFuses]++;
                }
            }
        }

        /// <summary>
        /// A person's hold (fix/mv-heavy-press, orchestrator 2026-11-23): the light swing starts on the PRESS exactly as with Heavy
        /// off (same tick, same frame data; Combat starts it). If Attack is still held when the swing has been winding up for
        /// ChargeStartTick ticks (8 at 30 Hz, the press tick counts 1; every class's light wind-up is 7 to 12 ticks, so the light
        /// has not landed yet), that wind-up becomes the charge: it is held open (no contact while held), the share grows to full
        /// at ChargeFullTick, and the release (or ChargeAutoReleaseTick) fires the heavy with its own class wind-up, so a read
        /// heavy stays dodgeable. Let go before the threshold: nothing changes. A light that lands before the threshold (a combo
        /// step's short wind-up, the Fast pace) lands; a still-held Attack then auto-repeats lights exactly as with Heavy off.
        /// A dodge, a stagger or a raised guard breaks the charge and drops the swing; CANCEL feints it.
        /// </summary>
        static void OffenceHumanHold(World w, OffenceMovesState s, MovesState ms, int i, Actor a, int start, int full, int auto)
        {
            int held = ms.AttackHeldTicks[i];
            if (held <= 0)
            {
                if (ms.AttackReleased[i] != 0)
                {
                    if (s.Charging[i] != 0 && a.SwingPending) OffenceFireHeavy(w, s, i, a, s.ChargeShare[i]);
                    else if (s.HoldSpent[i] != 0) s.DropRelease[i] = 1;
                }
                OffenceClearHold(s, i);
                return;
            }
            if (s.HoldSpent[i] != 0) return;
            if (s.Charging[i] == 0)
            {
                if (!a.SwingPending) return;
                int h0 = w.Tick - a.SwingTick + 1;
                if (h0 < start || held < h0) return;                    // under the threshold, or a buffered press already let go
                // (a pending swing has not landed: this runs before Combat, so even its contact tick is still convertible)
                if (w.Moves.Defence.LungeSwingTick != null && i < w.Moves.Defence.LungeSwingTick.Length
                    && w.Moves.Defence.LungeSwingTick[i] == a.SwingTick) return;   // lane 2's dodge-in lunge stays a lunge
                s.Charging[i] = 1;
                s.StepSwingTick[i] = CombatFeel.NoTick;   // a converted combo step is no longer a step
                s.StepOf[i] = 0;
                s.ChainNext[i] = 0;
            }
            bool broken = !a.SwingPending || Dodge.Active(w, i) || a.HitStunTicks > 0 || ms.GuardHeldTicks[i] > 0;
            if (broken)
            {
                a.SwingPending = false;
                s.HoldSpent[i] = 1;
                s.HoldCount[i] = 0;
                s.Charging[i] = 0;
                s.ChargeShare[i] = 0;
                return;
            }
            if (w.Inputs[i].Cancel && OffenceCancelCharge(w, s, i)) return;
            int h = w.Tick - a.SwingTick + 1;
            s.HoldCount[i] = h;
            s.ChargeShare[i] = OffenceShare(h, start, full);
            if (h >= auto)
            {
                OffenceFireHeavy(w, s, i, a, s.ChargeShare[i]);
                s.HoldSpent[i] = 1;
                s.HoldCount[i] = 0;
                s.Charging[i] = 0;
                s.ChargeShare[i] = 0;
                return;
            }
            if (a.SwingWindup < h) a.SwingWindup = h;   // held open: Combat's contact test (Tick >= SwingTick + SwingWindup) waits
        }

        /// <summary>A person's charge let go: the held swing becomes the heavy, winding up its class wind-up from now (the
        /// telegraph and HeavyRelease carry the heavy's real shape). Combat sees a pending swing and lands it on contact.</summary>
        static void OffenceFireHeavy(World w, OffenceMovesState s, int i, Actor a, double share)
        {
            int c = (int)a.Cls;
            LightWeapon lw = LightWeapons.Of(w, a.Cls);
            ClassConfig cc = w.Config.Class(a.Cls);
            int windup = lw != null ? w.Dur.LightWindup[c] : w.Dur.AttackWindup[c];
            if (windup < 1) windup = 1;
            a.AttackCd = lw != null ? lw.CooldownSeconds : cc.AttackCooldown;
            OffencePayChainDebt(w, s, i);
            a.AnimAction = 0;
            a.SwingTick = w.Tick;
            a.SwingWindup = windup;
            a.SwingRecovery = lw != null ? w.Dur.LightRecovery[c] : w.Dur.Recovery[c];
            a.SwingFacing = a.Facing;
            a.SwingPending = true;
            s.HeavySwingTick[i] = w.Tick;
            s.HeavySwingShare[i] = share;
            s.StepSwingTick[i] = CombatFeel.NoTick;
            s.StepOf[i] = 0;
            s.ChainNext[i] = 0;
            s.Counts[OffenceCountHeavies]++;
            if (share >= 1 - 1e-9) s.Counts[OffenceCountFullCharges]++;
            OffenceHeavyShape(w, a.Cls, out double reach, out double arc);
            World.PushEvent(w, SimEventType.HeavyRelease, i, -1, w.X[i], w.Z[i], share, null, DamageCause.None, reach, 0, 0, arc, 0, windup, 0);
            double fx = JsMath.Cos(a.Facing), fz = JsMath.Sin(a.Facing);
            World.PushEvent(w, SimEventType.AttackStart, i, Combat.KindAttack, w.X[i] + fx * reach, w.Z[i] + fz * reach, a.Facing, null, DamageCause.None, reach, fx, fz, arc, 0, windup, a.SwingRecovery);
        }

        static double OffenceShare(int hold, int start, int full)
        {
            if (full <= start) return 1;
            double x = (double)(hold - start) / (full - start);
            return x < 0 ? 0 : x > 1 ? 1 : x;
        }

        static void OffenceClearHold(OffenceMovesState s, int i)
        {
            s.HoldCount[i] = 0;
            s.HoldSpent[i] = 0;
            s.Charging[i] = 0;
            s.ChargeShare[i] = 0;
        }

        /// <summary>A heavy released at hold ticks h: queued for Combat this tick (the buffer's span).</summary>
        static void OffenceRelease(World w, OffenceMovesState s, int i, int h, int start, int full)
        {
            s.PendingHeavy[i] = OffenceShare(h, start, full);
            s.PendingHeavyUntil[i] = w.Tick + CombatFeel.Ticks(w, CombatFeel.Cfg(w).BufferSeconds);
        }

        /// <summary>CANCEL during a charge: the b16 feint rule on a charge (no swing; ChargeCancelRefund of a class cooldown back).
        /// The press is read and cleared here, so CombatFeel.TakeCancel sees nothing. False when the feint lockout holds.</summary>
        static bool OffenceCancelCharge(World w, OffenceMovesState s, int i)
        {
            CombatFeelConfig f = CombatFeel.Cfg(w);
            if (!f.CancelEnabled) return false;
            CombatFeelState fs = w.Depth.Feel;
            if (w.Tick < fs.FeintLockUntil[i]) return false;
            w.Inputs[i].Cancel = false;
            Actor a = w.Actors[i];
            double hz = w.Config.SimHz;
            int rec = CombatFeel.Ticks(w, f.FeintRecoverySeconds);
            double left = w.Config.Class(a.Cls).AttackCooldown * (1 - Cfg(w).ChargeCancelRefund);
            a.AttackCd = left > rec / hz ? left : rec / hz;
            fs.FeintLockUntil[i] = w.Tick + CombatFeel.Ticks(w, f.FeintLockoutSeconds);
            fs.BufferTick[i] = CombatFeel.NoTick;
            fs.Feints++;
            if (OffenceHuman(w, i)) a.SwingPending = false;   // the held swing is dropped (a person's charge is a held wind-up)
            s.HoldSpent[i] = 1;
            s.HoldCount[i] = 0;
            s.Charging[i] = 0;
            s.ChargeShare[i] = 0;
            s.BotChargeLeft[i] = 0;
            World.PushEvent(w, SimEventType.Feint, i, -1, w.X[i], w.Z[i], 1);
            Feinted(w, i);
            return true;
        }

        /// <summary>The attack want after the buffer: hold = charge (false while charging), release = the heavy (true once),
        /// combo press in the chain window. Return wants unchanged when off.</summary>
        static bool OffenceWantsAttack(World w, int i, bool wants)
        {
            if (!On(w, Cfg(w).Heavy)) return OffenceHuman(w, i) ? OffenceComboBuffer(w, w.Moves.Offence, i, wants) : wants;
            OffenceMovesState s = w.Moves.Offence;
            if (s.PendingHeavy[i] >= 0)
            {
                if (w.Tick <= s.PendingHeavyUntil[i] && w.Actors[i].Alive) return true;
                s.PendingHeavy[i] = -1;
            }
            if (OffenceHuman(w, i))
            {
                // the light starts on the press exactly as with Heavy off; a hold that already fired its heavy (or was broken or
                // feinted) swings nothing more until it is let go, and its let-go leaves nothing in the buffer
                if (s.HoldSpent[i] != 0 || s.DropRelease[i] != 0)
                {
                    w.Depth.Feel.BufferTick[i] = CombatFeel.NoTick;
                    return false;
                }
                return OffenceComboBuffer(w, s, i, wants);
            }
            // a bot holding a charge for lane 5 swings nothing until the release
            if (s.BotChargeLeft[i] > 0 || s.HoldCount[i] > 0) return false;
            return wants;
        }

        /// <summary>
        /// fix/mv-combo-buffer (Editor review): the combo's input buffer, a person's seat only. An Attack PRESS (a new press, not a
        /// held button) made in the active or recovery frames of a swing that landed and opened the chain, from its contact tick to
        /// the tick the next swing can start (ComboBufferTicks: the class cooldown minus its wind-up, 8 to 16 ticks at 30 Hz), is
        /// remembered and starts the next step on the window's first tick. A press during the wind-up (before the hit) is not
        /// remembered, a held Attack is not a press, and the press is dropped when the chain closes (a whiff, the victim's dodge
        /// with ComboBreaksOnDodge, the window running out). Works whether or not the Heavy switch is on (the light is on press).
        /// </summary>
        static bool OffenceComboBuffer(World w, OffenceMovesState s, int i, bool wants)
        {
            if (!On(w, Cfg(w).Combos)) return wants;
            Actor a = w.Actors[i];
            bool open = s.ChainNext[i] >= 2 && w.Tick <= s.ChainUntil[i];
            if (!open)
            {
                s.ComboPressTick[i] = CombatFeel.NoTick;
                return wants;
            }
            bool press = w.Inputs[i].Attack && w.Moves.AttackHeldTicks[i] == 1;
            if (press && !a.SwingPending && a.AttackCd > 0 && s.ComboPressTick[i] == CombatFeel.NoTick) s.ComboPressTick[i] = w.Tick;
            return wants || s.ComboPressTick[i] != CombatFeel.NoTick;
        }

        /// <summary>The ComboBufferTicks of class cls (the report's N): its cooldown minus its light wind-up, in this world's ticks.</summary>
        public static int OffenceComboBufferTicks(World w, ClassId cls)
        {
            int cd = (int)Math.Ceiling(w.Config.Class(cls).AttackCooldown * w.Config.SimHz - 1e-9);
            int wu = w.Dur.AttackWindup[(int)cls];
            return cd > wu ? cd - wu : 0;
        }

        /// <summary>The wind-up of the swing starting now (combo step x ComboStepWindupMul). Return windup when not a step.</summary>
        static int OffenceSwingWindup(World w, int i, int windup)
        {
            MovesConfig m = Cfg(w);
            OffenceMovesState s = w.Moves.Offence;
            if (On(w, m.Heavy) && s.PendingHeavy[i] >= 0)
            {
                double share = s.PendingHeavy[i];
                s.PendingHeavy[i] = -1;
                s.HeavySwingTick[i] = w.Tick;
                s.HeavySwingShare[i] = share;
                s.StepSwingTick[i] = CombatFeel.NoTick;
                s.StepOf[i] = 0;
                s.ChainNext[i] = 0;
                s.Counts[OffenceCountHeavies]++;
                if (share >= 1 - 1e-9) s.Counts[OffenceCountFullCharges]++;
                OffenceHeavyShape(w, w.Actors[i].Cls, out double reach, out double arc);
                World.PushEvent(w, SimEventType.HeavyRelease, i, -1, w.X[i], w.Z[i], share, null, DamageCause.None, reach, 0, 0, arc, 0, windup, 0);
                return windup;   // the class wind-up (SPEC T2-2)
            }
            s.HeavySwingTick[i] = CombatFeel.NoTick;
            if (!On(w, m.Combos)) return windup;
            s.ComboPressTick[i] = CombatFeel.NoTick;   // the remembered press is spent by this swing
            int step = 1;
            if (s.ChainNext[i] >= 2 && w.Tick <= s.ChainUntil[i] && OffenceMayChain(w, s, i)) step = s.ChainNext[i];
            if (step < 2) OffencePayChainDebt(w, s, i);   // an opener after a chain that did not finish pays what it borrowed
            s.ChainNext[i] = 0;
            s.StepSwingTick[i] = w.Tick;
            s.StepOf[i] = step;
            if (step < 2) return windup;
            int floor = Durations.T30(w.Config, OffenceMinStepWindupTicks);
            int k = (int)Math.Floor(windup * m.ComboStepWindupMul + 0.5);
            if (k < floor) k = floor;
            return k < windup ? k : windup;
        }

        /// <summary>SPEC T2-3: "every step's wind-up is at least 5 ticks" (30 Hz), so the victim can dodge or guard between steps.</summary>
        public const int OffenceMinStepWindupTicks = 5;

        static int OffenceSteps(World w, ClassId cls)
        {
            int[] st = Cfg(w).ComboSteps;
            int c = (int)cls;
            return st != null && c < st.Length ? st[c] : 1;
        }

        /// <summary>The contact tick's reach, arc and damage (heavy shape, charge and combo damage). Leave them when off.</summary>
        static void OffenceSwingShape(World w, int i, ref double range, ref double arcDeg, ref double dmg)
        {
            MovesConfig m = Cfg(w);
            OffenceMovesState s = w.Moves.Offence;
            Actor a = w.Actors[i];
            if (On(w, m.Heavy) && s.HeavySwingTick[i] == a.SwingTick)
            {
                OffenceHeavyShape(w, a.Cls, out range, out arcDeg);
                int c = (int)a.Cls;
                bool bonus = m.HeavyDamageBonus == null || c >= m.HeavyDamageBonus.Length || m.HeavyDamageBonus[c];
                if (bonus) dmg *= 1 + (m.ChargeDamageMulMax - 1) * s.HeavySwingShare[i];
                return;
            }
            if (!On(w, m.Combos) || s.StepSwingTick[i] != a.SwingTick) return;
            int step = s.StepOf[i];
            if (step < 2) return;
            double fin = m.FinisherDamageMulByClass != null && (int)a.Cls < m.FinisherDamageMulByClass.Length && m.FinisherDamageMulByClass[(int)a.Cls] >= 0
                ? m.FinisherDamageMulByClass[(int)a.Cls] : m.FinisherDamageMul;   // integrator: per-class finisher (Demolitions)
            dmg *= step >= OffenceSteps(w, a.Cls) ? fin : m.ComboStepDamageMul;
        }

        /// <summary>After actor i's swing contact (hits 0 = whiff): combo advance or reset, HeavyWhiffExtraRecoveryTicks.</summary>
        static void OffenceAfterContact(World w, int i, int hits)
        {
            MovesConfig m = Cfg(w);
            OffenceMovesState s = w.Moves.Offence;
            Actor a = w.Actors[i];
            if (On(w, m.Heavy) && s.HeavySwingTick[i] == a.SwingTick)
            {
                s.ChainNext[i] = 0;
                if (hits <= 0)
                {
                    a.SwingRecovery += Durations.T30(w.Config, m.HeavyWhiffExtraRecoveryTicks);
                    w.Depth.Feel.WhiffRecoverUntil[i] = w.Tick + a.SwingRecovery;   // the PUNISH window covers the longer recovery
                    OffenceCooldownFloor(w, a);
                }
                return;
            }
            if (!On(w, m.Combos) || s.StepSwingTick[i] != a.SwingTick) return;
            int step = s.StepOf[i];
            if (hits <= 0)
            {
                s.ChainNext[i] = 0;   // a whiff resets to step 1
                OffencePayChainDebt(w, s, i);
                return;
            }
            int steps = OffenceSteps(w, a.Cls);
            bool finisher = step >= 2 && step >= steps;
            if (step >= 2)
            {
                s.Counts[OffenceCountSteps]++;
                World.PushEvent(w, SimEventType.ComboStep, i, -1, w.X[i], w.Z[i], step, null, DamageCause.None, 0, 0, 0, 0, finisher ? 1 : 0, 0, 0);
            }
            if (finisher || steps < 2)
            {
                s.ChainNext[i] = 0;
                if (finisher)
                {
                    s.Counts[OffenceCountFinishers]++;
                    a.SwingRecovery += Durations.T30(w.Config, m.FinisherExtraRecoveryTicks);
                    OffenceCooldownFloor(w, a);
                    OffencePayChainDebt(w, s, i);
                }
                return;
            }
            s.ChainNext[i] = step + 1;
            // fix/mv-combo-reach: a person's next step may start as soon as this swing's recovery ends (never sooner than
            // OffenceMinStepGapTicks after the hit, so a lag-6 victim's dodge still fits), not after the full class cooldown: the
            // step follows before the staggered victim's own swing can land (the Editor's chain that "never connected" was this
            // trade, not reach). The cooldown it skips is borrowed and paid back when the chain ends.
            if (OffenceHuman(w, i))
            {
                double hz = w.Config.SimHz;
                int cdTicks = (int)Math.Ceiling(a.AttackCd * hz - 1e-9);
                int early = a.SwingRecovery > OffenceMinStepGapTicks ? a.SwingRecovery : OffenceMinStepGapTicks;
                int c = (int)a.Cls, most = c < OffenceStepBorrowTicks.Length ? OffenceStepBorrowTicks[c] : 0;
                if (cdTicks - early > most) early = cdTicks - most;
                if (early < cdTicks)
                {
                    s.ChainDebt[i] += cdTicks - early;
                    a.AttackCd = early / hz;
                }
            }
            // the chain holds while the next step can follow at once: until the cooldown is spent plus the input buffer (a press
            // made during the recovery is held that long), never past ComboResetSeconds. A dodge out of reach between steps
            // therefore breaks it (the attacker cannot walk back in and still finish), and so does a pause.
            int cdEnd = w.Tick + (int)Math.Ceiling(a.AttackCd * w.Config.SimHz - 1e-9);
            int until = cdEnd + CombatFeel.Ticks(w, CombatFeel.Cfg(w).BufferSeconds);
            int cap = w.Tick + CombatFeel.Ticks(w, m.ComboResetSeconds);
            s.ChainUntil[i] = until < cap ? until : cap;
        }

        /// <summary>Guard fix (2026-11-23; lane 2 calls it when the queued hit lands, after OffenceAfterContact advanced the chain):
        /// actor i's current swing was parried (the chain breaks: the next swing is an opener) or every body it reached was
        /// guard-blocked (no advance: the next swing repeats this step).</summary>
        internal static void OffenceChainUndo(World w, int i, bool parried)
        {
            if (w.Moves == null || !On(w, Cfg(w).Combos)) return;
            OffenceMovesState s = w.Moves.Offence;
            if (s.StepSwingTick[i] != w.Actors[i].SwingTick) return;
            int step = s.StepOf[i];
            s.ChainNext[i] = parried || step < 2 ? 0 : step;
            s.ComboPressTick[i] = CombatFeel.NoTick;   // fix/mv-combo-buffer: a press remembered for this swing is dropped too
        }

        /// <summary>The cooldown never ends before the (longer) recovery: CombatFeel.AfterContact's rule on the new recovery.</summary>
        /// <summary>fix/mv-combo-reach: the fewest ticks from a step's hit to the next step's start (its contact comes at least a
        /// step wind-up, 5 ticks, later).</summary>
        public const int OffenceMinStepGapTicks = 3;

        /// <summary>fix/mv-combo-reach: the most cooldown (30 Hz ticks) one step may borrow, per class. The Farmer borrows less
        /// (his 3-step chain front-loads two borrows: passive time to kill must stay inside the 5-15% band).</summary>
        public static readonly int[] OffenceStepBorrowTicks = { 99, 6, 99, 99, 99, 0 };

        static void OffencePayChainDebt(World w, OffenceMovesState s, int i)
        {
            if (s.ChainDebt[i] <= 0) return;
            w.Actors[i].AttackCd += s.ChainDebt[i] / w.Config.SimHz;
            s.ChainDebt[i] = 0;
        }

        static void OffenceCooldownFloor(World w, Actor a)
        {
            double left = a.SwingRecovery / w.Config.SimHz;
            if (a.AttackCd < left) a.AttackCd = left;
        }

        /// <summary>A melee strike's knock and stagger (heavy x charge, finisher x FinisherKnockMul, charge armour on the target).</summary>
        static void OffenceStrikeKnock(World w, int source, int target, ref double knock, ref int stun)
        {
            MovesConfig m = Cfg(w);
            OffenceMovesState s = w.Moves.Offence;
            if (source < 0 || source >= w.ActorCount || target < 0 || target >= w.ActorCount) return;
            Actor src = w.Actors[source];
            bool heavyOn = On(w, m.Heavy);
            if (heavyOn && s.HeavySwingTick[source] == src.SwingTick)
            {
                int c = (int)src.Cls;
                double cls = m.HeavyKnockMul != null && c < m.HeavyKnockMul.Length ? m.HeavyKnockMul[c] : 1;
                knock *= (1 + (m.ChargeKnockMulMax - 1) * s.HeavySwingShare[source]) * cls;
                // "always a heavy stagger", except a class whose strikes never stagger (b16 StrikeStaggers: the Healer's lantern;
                // her flare is a disengage through its knock, not a stun)
                bool[] ss = CombatFeel.Cfg(w).StrikeStaggers;
                if (ss == null || c >= ss.Length || ss[c]) stun = OffenceHeavyStagger(w, target, stun);
            }
            else if (On(w, m.Combos) && s.StepSwingTick[source] == src.SwingTick && s.StepOf[source] >= 2 && s.StepOf[source] >= OffenceSteps(w, src.Cls))
                knock *= m.FinisherKnockMul;
            // charge armour: a hit under the charging body's Poise does not stagger it (the charge goes on)
            if (heavyOn && s.Charging[target] != 0 && s.StrikeAmount[0] < w.Config.Class(w.Actors[target].Cls).Poise) stun = 0;
        }

        /// <summary>"Always a heavy stagger": at least HeavyStaggerSeconds, unless the target is immune or in an armoured wind-up
        /// (the Warrior's cleave armour stays his edge), and never past the running lock's room (StaggerMaxLockSeconds).</summary>
        static int OffenceHeavyStagger(World w, int target, int stun)
        {
            CombatFeelConfig f = CombatFeel.Cfg(w);
            CombatFeelState fs = w.Depth.Feel;
            Actor t = w.Actors[target];
            if (w.Tick < fs.ImmuneUntil[target]) return stun;
            bool armoured = t.SwingPending && f.Strikes[(int)t.Cls].ArmouredWindup && (w.Tick > fs.SunderUntil[target] || !f.SunderBreaksArmour)
                && w.Tick > fs.ArmourBreakUntil[target];
            if (armoured && stun <= 0) return stun;
            int heavy = CombatFeel.Ticks(w, f.HeavyStaggerSeconds);
            if (stun >= heavy) return stun;
            int ticks = heavy;
            if (fs.LockStart[target] != CombatFeel.NoTick && w.Tick <= fs.LockUntil[target] + 1)
            {
                int cap = fs.LockStart[target] + CombatFeel.Ticks(w, f.StaggerMaxLockSeconds) - w.Tick;
                if (ticks > cap) ticks = cap < 0 ? 0 : cap;
            }
            return ticks > stun ? ticks : stun;
        }

        /// <summary>HP was taken (dmg &gt; 0). A stagger that got through breaks the charge (OffenceStep, next tick).</summary>
        static void OffenceDamaged(World w, int target, int source, DamageCause cause, double dmg) { }

        /// <summary>Walk-speed multiplier (charging x ChargeMoveMul). Exactly 1.0 when not charging.</summary>
        static double OffenceSpeedScale(World w, int i)
        {
            if (w.Moves.Offence.Charging[i] != 0 && On(w, Cfg(w).Heavy)) return Cfg(w).ChargeMoveMul;
            return OffenceStepLunge(w, i);
        }

        /// <summary>fix/mv-combo-reach: the most a combo step may close on its victim during its wind-up, per class (units), sized
        /// to the class's own step-1 knock plus the victim's walk-out in that wind-up (the Farmer's thrust shoves furthest).</summary>
        public static readonly double[] OffenceStepLungeUnits = { 1.2, 2.0, 1.2, 1.4, 1.5, 0 };

        /// <summary>
        /// fix/mv-combo-reach (Editor pass 2: "the Farmer's combo never connected"): a person's combo step (2 or 3) that winds up
        /// while the move stick points at the chain's victim steps in: the walk speed is scaled so the step closes the gap to
        /// its reach by its contact tick, at most OffenceStepLungeUnits of extra ground over the wind-up. No step, no stick
        /// toward the victim, a bot, or already in reach: exactly 1. A victim who dodged broke the chain, so no step lunges after it.
        /// </summary>
        static double OffenceStepLunge(World w, int i)
        {
            MovesConfig m = Cfg(w);
            OffenceMovesState s = w.Moves.Offence;
            Actor a = w.Actors[i];
            if (!On(w, m.Combos) || !a.SwingPending || s.StepSwingTick[i] != a.SwingTick || s.StepOf[i] < 2) return 1.0;
            int v = s.ChainVictim[i];
            if (v < 0 || v >= w.ActorCount || !w.Actors[v].Alive || !OffenceHuman(w, i)) return 1.0;
            int c = (int)a.Cls;
            double budget = c < OffenceStepLungeUnits.Length ? OffenceStepLungeUnits[c] : 0;
            if (!(budget > 0)) return 1.0;
            double dx = w.X[v] - w.X[i], dz = w.Z[v] - w.Z[i];
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6) return 1.0;
            InputFrame f = w.Inputs[i];
            double ml = Math.Sqrt(f.MoveX * f.MoveX + f.MoveZ * f.MoveZ);
            if (ml < 0.5 || (f.MoveX * dx + f.MoveZ * dz) / (ml * d) < 0.7) return 1.0;   // the stick must point at the victim
            ClassConfig cc = w.Config.Class(a.Cls);
            double want = cc.AttackRange + w.Config.Class(w.Actors[v].Cls).Radius - 0.25;   // a little inside the reach
            double gap = d - want;
            if (gap <= 0) return 1.0;
            int left = a.SwingTick + a.SwingWindup - w.Tick;
            if (left < 1) left = 1;
            double dt = 1.0 / w.Config.SimHz;
            double basePerTick = cc.MoveSpeed * dt;
            if (!(basePerTick > 0)) return 1.0;
            double need = gap / left / basePerTick;   // the scale that closes the gap by contact
            double cap = 1 + budget / (a.SwingWindup > 0 ? a.SwingWindup : 1) / basePerTick;
            double k = need < cap ? need : cap;
            return k > 1 ? k : 1.0;
        }
    }
}
