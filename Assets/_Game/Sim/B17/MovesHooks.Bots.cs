// Class moves build (2026-11-22): LANE 5 (Kits and bots) owns this file: T1-4b bot step-out, T1-5 the Builder's quick wall
// (its rule lives in BuilderWalls.cs / BuildConfig.cs; the per-Builder record of his quick walls lives here), and the bots' use
// of guard, parry, charge, combo and their read of a slide. Keep every signature; test the switch with
// MovesHooks.On(w, MovesHooks.Cfg(w).<Switch>). Bots are seat fillers: their use of a new verb is capped against a human seat the
// way call 18 does it (BotMeleeDodgeHumansOnly): every read below is of a HUMAN seat's move, so bot-only matches keep their
// fights. Contract: LANE1-CONTRACT.md.
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>Lane 5's class-moves state (World.Moves.Bots). Public primitive arrays sized in Init, cleared in Reset.</summary>
    public sealed class BotsMovesState
    {
        /// <summary>Quick walls remembered per Builder (QuickWallMaxPerBuilder is 1; a ring of this many build orders).</summary>
        public const int QuickRing = 4;

        /// <summary>T1-5: per actor x QuickRing, the WallsState.PSeq of each of his quick walls (0 = none), and the ring cursor.</summary>
        public int[] QuickSeq = new int[0];
        public int[] QuickNext = new int[0];

        /// <summary>The human swing (attacker SwingTick x 64 + attacker + 1) this bot last decided on; 0 = none.</summary>
        public int[] ReadKey = new int[0];
        /// <summary>T1-4b: the step-out runs while World.Tick is below this, away from StepFrom.</summary>
        public int[] StepUntil = new int[0];
        public int[] StepFrom = new int[0];
        /// <summary>T2-1 bot use: hold Guard while World.Tick is below this.</summary>
        public int[] GuardUntil = new int[0];
        /// <summary>T2-1 bot use (Hard only): press Guard on this tick (the PARRY edge), -1 = none.</summary>
        public int[] ParryAt = new int[0];
        /// <summary>T2-2 bot use: the swing (SwingTick + 1) a charge was rolled after, and the hold length of a charge in progress.</summary>
        public int[] ChargeKey = new int[0];
        public int[] ChargeHold = new int[0];
        /// <summary>T2-3 bot use: the landed swing (SwingTick + 1) whose chain was decided, and the decision (1 = chain).</summary>
        public int[] ComboKey = new int[0];
        public byte[] ComboGo = new byte[0];
        /// <summary>T2-3 bot use: tick + 1 of the last press a no-chain bot held back inside the chain window (0 = none); it is
        /// replayed on the tick its swing could start if the 0.35 s attack buffer would still hold it, so the timing is today's.</summary>
        public int[] ComboDefer = new int[0];
        /// <summary>T2-5 read: a side-step from a human SLIDE runs from SideFrom to SideUntil along (SideX, SideZ).</summary>
        public int[] SideFrom = new int[0];
        public int[] SideUntil = new int[0];
        public double[] SideX = new double[0];
        public double[] SideZ = new double[0];
        /// <summary>[0]: the World.EventHead the slide read has seen up to.</summary>
        public int[] EventCursor = new int[1];

        /// <summary>Counters per bot (tests and reports): reads decided, step-outs, guards, parries, charges, chains, slide side-steps.</summary>
        public int[] Reads = new int[0];
        public int[] StepOuts = new int[0];
        public int[] Guards = new int[0];
        public int[] Parries = new int[0];
        public int[] Charges = new int[0];
        public int[] Chains = new int[0];
        public int[] SideSteps = new int[0];

        public void Init(World w)
        {
            int n = World.ActorCapacity(w.Config);
            QuickSeq = new int[n * QuickRing];
            QuickNext = new int[n];
            ReadKey = new int[n];
            StepUntil = new int[n];
            StepFrom = new int[n];
            GuardUntil = new int[n];
            ParryAt = new int[n];
            ChargeKey = new int[n];
            ChargeHold = new int[n];
            ComboKey = new int[n];
            ComboGo = new byte[n];
            ComboDefer = new int[n];
            SideFrom = new int[n];
            SideUntil = new int[n];
            SideX = new double[n];
            SideZ = new double[n];
            EventCursor = new int[1];
            Reads = new int[n];
            StepOuts = new int[n];
            Guards = new int[n];
            Parries = new int[n];
            Charges = new int[n];
            Chains = new int[n];
            SideSteps = new int[n];
        }

        public void Reset(World w)
        {
            System.Array.Clear(QuickSeq, 0, QuickSeq.Length);
            System.Array.Clear(QuickNext, 0, QuickNext.Length);
            System.Array.Clear(ReadKey, 0, ReadKey.Length);
            System.Array.Clear(StepUntil, 0, StepUntil.Length);
            System.Array.Clear(StepFrom, 0, StepFrom.Length);
            System.Array.Clear(GuardUntil, 0, GuardUntil.Length);
            for (int i = 0; i < ParryAt.Length; i++) ParryAt[i] = -1;
            System.Array.Clear(ChargeKey, 0, ChargeKey.Length);
            System.Array.Clear(ChargeHold, 0, ChargeHold.Length);
            System.Array.Clear(ComboKey, 0, ComboKey.Length);
            System.Array.Clear(ComboGo, 0, ComboGo.Length);
            System.Array.Clear(ComboDefer, 0, ComboDefer.Length);
            System.Array.Clear(SideFrom, 0, SideFrom.Length);
            System.Array.Clear(SideUntil, 0, SideUntil.Length);
            System.Array.Clear(SideX, 0, SideX.Length);
            System.Array.Clear(SideZ, 0, SideZ.Length);
            EventCursor[0] = 0;
            System.Array.Clear(Reads, 0, Reads.Length);
            System.Array.Clear(StepOuts, 0, StepOuts.Length);
            System.Array.Clear(Guards, 0, Guards.Length);
            System.Array.Clear(Parries, 0, Parries.Length);
            System.Array.Clear(Charges, 0, Charges.Length);
            System.Array.Clear(Chains, 0, Chains.Length);
            System.Array.Clear(SideSteps, 0, SideSteps.Length);
        }
    }

    public static partial class MovesHooks
    {
        // ---- lane 5 knobs MovesConfig does not carry (asked for in the lane 5 report; the integrator may move them there).
        // Index order Normal, Easy, Hard (the MovesConfig bot-chance order).
        // Integrator: the charge and combo chances are MovesConfig.BotChargeChance / BotComboChance now (same values).
        /// <summary>T1-4b: the step-out lasts at least this, and at most BotsStepMaxTicks, past the read.</summary>
        const int BotsStepMinTicks = 3, BotsStepMaxTicks = 12;
        /// <summary>T1-4b: a FEINTED human swing still reads as a wind-up this many ticks past its would-be contact (the bait).</summary>
        const int BotsFeintReadTicks = 3;
        /// <summary>Roll salts (BotReflex.Roll keeps its own seed; these never share a roll with the dodge).</summary>
        const int BotsSaltRead = 0x2B17A5, BotsSaltCharge = 0x51C3E9, BotsSaltCombo = 0x6D0B47, BotsSaltSlide = 0x7F3A11;

        /// <summary>Per tick, before Abilities: the slide read (a human SLIDE start event aimed at a bot).</summary>
        static void BotsStep(World w, double dt)
        {
            BotsMovesState b = w.Moves.Bots;
            int head = w.EventHead;
            int from = b.EventCursor[0];
            if (from < head - World.EventCapacity) from = head - World.EventCapacity;
            if (from < 0) from = 0;
            b.EventCursor[0] = head;
            if (!On(w, Cfg(w).Slide)) return;
            for (int k = from; k < head; k++)
            {
                SimEvent e = w.Events[k % World.EventCapacity];
                if (e.Type != SimEventType.Slide || e.B != -1) continue;
                BotsReadSlide(w, b, e.A);
            }
        }

        /// <summary>Bot id's frame, last in BotBrain.FillBotInput: step-out, guard / parry, charge, combo and the slide side-step.
        /// output.Guard and output.JumpHeld arrive false.</summary>
        static void BotsFill(World w, int id, InputFrame output)
        {
            Actor a = w.Actors[id];
            BotsMovesState b = w.Moves.Bots;
            if (!a.Alive || a.StandUp > 0)
            {
                b.StepUntil[id] = b.GuardUntil[id] = b.ChargeHold[id] = b.SideUntil[id] = 0;
                b.ParryAt[id] = -1;
                return;
            }
            MovesConfig m = Cfg(w);
            int tick = w.Tick;
            bool guardOn = On(w, m.Guard), stepOn = On(w, m.BotStepOut);
            if ((guardOn || stepOn) && a.AirLen <= 0 && a.Carry != CarryKind.Corruption) BotsRead(w, b, m, id, output, guardOn, stepOn);

            bool responding = false;
            if (tick < b.StepUntil[id] && BotsStepOut(w, b, id, output)) responding = true;
            else b.StepUntil[id] = 0;
            if (guardOn && tick < b.GuardUntil[id])
            {
                output.Guard = true;
                responding = true;
            }
            if (guardOn && b.ParryAt[id] == tick)
            {
                output.Guard = true;   // the press edge (a 1-tick tap; lane 2 holds a whiffed parry ParryWhiffHoldTicks)
                b.ParryAt[id] = -1;
                responding = true;
            }
            else if (b.ParryAt[id] >= 0 && b.ParryAt[id] < tick) b.ParryAt[id] = -1;
            if (responding)
            {
                output.Attack = false;
                output.Dodge = false;   // the answer to this read is the step-out / guard / parry, not also a dodge
                b.ChargeHold[id] = 0;
            }
            if (On(w, m.Slide) && tick >= b.SideFrom[id] && tick < b.SideUntil[id] && !output.Dodge)
            {
                output.MoveX = b.SideX[id];
                output.MoveZ = b.SideZ[id];
            }
            if (!responding) BotsAttack(w, b, m, id, output);
        }

        // ------------------------------------------------------------------------------------------------ the read

        /// <summary>Config chance arrays are Normal, Easy, Hard; BotReflex.DiffIndex is Easy 0, Normal 1, Hard 2.</summary>
        static double BotsChance(double[] c, int d)
        {
            int k = d == 1 ? 0 : d == 0 ? 1 : 2;
            return c != null && k < c.Length ? c[k] : 0;
        }

        /// <summary>The read lag of a human wind-up for the step-out, guard and parry (BotReflex.DiffIndex order Easy, Normal, Hard):
        /// Normal reads at 6 ticks, the human seat's own 0.2 s (PlayerSeatRig lag 6); Easy 9; Hard 5 (the dodge's Hard read). The
        /// dodge keeps DodgeConfig.ReactionTicks (8 on Normal): at 8 a step-out against a 10-tick swing gets 2 ticks of walking
        /// and never leaves reach, so a feint had nothing to bait (measured: feint value +0 of 40 at 8).</summary>
        static readonly int[] BotsReadLag = { 9, 6, 5 };

        static int BotsReadTicks(World w, int d) => d >= 0 && d < BotsReadLag.Length ? BotsReadLag[d] : 6;

        /// <summary>
        /// T1-4b + T2-1 bot use. A HUMAN enemy's melee wind-up aimed at bot id within its reach + MeleeSlack, read once it is
        /// BotsReadTicks old, is decided ONCE: (Hard only) parry with BotParryChance, else guard with BotGuardChance, else step out
        /// of reach with BotStepOutChance (one roll, consecutive bands, each band the spec's chance). A FEINTED swing still reads
        /// for BotsFeintReadTicks past its would-be contact: the bot that read it commits, which is the bait the feint needs.
        /// A dodge BotReflex already pressed is the answer (nothing else is decided), and a bot winding up its own swing trades.
        /// </summary>
        static void BotsRead(World w, BotsMovesState b, MovesConfig m, int id, InputFrame o, bool guardOn, bool stepOn)
        {
            Actor me = w.Actors[id];
            if (me.SwingPending) return;
            GameConfig cfg = w.Config;
            DodgeConfig dc = cfg.Depth.Dodge;
            CombatFeelState feel = w.Depth != null ? w.Depth.Feel : null;
            double slack = dc != null ? dc.MeleeSlack : 0.5;
            double myR = cfg.Class(me.Cls).Radius;
            double x = w.X[id], z = w.Z[id];
            int tick = w.Tick;
            for (int e = 0; e < w.ActorCount; e++)
            {
                Actor en = w.Actors[e];
                if (e == id || !en.Alive || en.Team == me.Team || !BotsHumanSeat(w, e)) continue;
                int contact = en.SwingTick + en.SwingWindup;
                bool live = en.SwingPending;
                bool feinted = !live && feel != null && feel.CancelUsed != null && e < feel.CancelUsed.Length
                    && feel.CancelUsed[e] == en.SwingTick && feel.LandedSwing[e] != en.SwingTick && tick <= contact + BotsFeintReadTicks;
                if (!live && !feinted) continue;
                if (en.SwingTick > tick) continue;
                double ox = x - w.X[e], oz = z - w.Z[e];
                double reach = cfg.Class(en.Cls).AttackRange + slack + myR;
                double d2 = ox * ox + oz * oz;
                if (d2 > reach * reach) continue;
                double l = System.Math.Sqrt(d2);
                if (l > 1e-9 && (JsMath.Cos(en.SwingFacing) * ox + JsMath.Sin(en.SwingFacing) * oz) / l < 0.7) continue;
                int key = en.SwingTick * 64 + e + 1;
                if (b.ReadKey[id] == key) return;
                int d = BotReflex.DiffIndex(w, id);
                if (tick - en.SwingTick < BotsReadTicks(w, d)) return;
                if (o.Dodge || Dodge.Active(w, id))
                {
                    b.ReadKey[id] = key;   // the dodge answered it
                    return;
                }
                b.ReadKey[id] = key;
                b.Reads[id]++;
                double r = BotReflex.Roll(w.Opts.Seed ^ BotsSaltRead, key, id);
                double pParry = guardOn ? BotsChance(m.BotParryChance, d) : 0;
                double pGuard = guardOn ? BotsChance(m.BotGuardChance, d) : 0;
                double pStep = stepOn ? BotsChance(m.BotStepOutChance, d) : 0;
                int window = m.ParryWindowTicks != null && (int)me.Cls < m.ParryWindowTicks.Length ? m.ParryWindowTicks[(int)me.Cls] : 3;
                int parryAt = contact - (window + 1) / 2;
                if (r < pParry && live && parryAt > tick)
                {
                    b.ParryAt[id] = parryAt;
                    b.Parries[id]++;
                    return;
                }
                if (r < pParry + pGuard)
                {
                    int until = contact + 2;
                    if (until < tick + m.GuardRaiseTicks + 1) until = tick + m.GuardRaiseTicks + 1;
                    b.GuardUntil[id] = until;
                    b.Guards[id]++;
                    return;
                }
                if (r < pParry + pGuard + pStep)
                {
                    int until = contact + 2;
                    if (until < tick + BotsStepMinTicks) until = tick + BotsStepMinTicks;
                    if (until > tick + BotsStepMaxTicks) until = tick + BotsStepMaxTicks;
                    b.StepUntil[id] = until;
                    b.StepFrom[id] = e;
                    b.StepOuts[id]++;
                }
                return;
            }
        }

        // Candidate turns away from the attacker (cos, sin): 0, +-45, +-90 degrees (never toward him).
        const double BotsH = 0.70710678118654757;
        static readonly double[] BotsTurnC = { 1, BotsH, BotsH, 0, 0 };
        static readonly double[] BotsTurnS = { 0, BotsH, -BotsH, 1, -1 };

        /// <summary>T1-4b: this tick's step-out move (straight away from the attacker, turned toward open ground; it is a walk, not
        /// a dodge). False = nothing walkable away from him or he is gone (the step-out ends).</summary>
        static bool BotsStepOut(World w, BotsMovesState b, int id, InputFrame o)
        {
            int e = b.StepFrom[id];
            if (e < 0 || e >= w.ActorCount || !w.Actors[e].Alive) return false;
            double ox = w.X[id] - w.X[e], oz = w.Z[id] - w.Z[e];
            double l = System.Math.Sqrt(ox * ox + oz * oz);
            if (l < 1e-6) { ox = -JsMath.Cos(w.Actors[id].Facing); oz = -JsMath.Sin(w.Actors[id].Facing); l = 1; }
            ox /= l;
            oz /= l;
            double sign = (id & 1) == 0 ? 1 : -1;
            for (int t = 0; t < BotsTurnC.Length; t++)
            {
                double c = BotsTurnC[t], s = BotsTurnS[t] * sign;
                double cx = ox * c - oz * s, cz = ox * s + oz * c;
                if (!MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(w.X[id] + cx * 0.9), JsMath.FloorToInt(w.Z[id] + cz * 0.9))) continue;
                o.MoveX = cx;
                o.MoveZ = cz;
                return true;
            }
            return false;
        }

        /// <summary>
        /// T2-5 read: a HUMAN enemy's SLIDE start, coming at a bot (the bot within SlideDistance + 1 and within 45 degrees of the
        /// slide line), is read like a wind-up: after the bot's BotsReadTicks, it side-steps (perpendicular to the slide, toward
        /// the open side) for SlideTicks with BotStepOutChance (Normal 0.30, inside the spec's 0.10 to 0.35 band).
        /// </summary>
        static void BotsReadSlide(World w, BotsMovesState b, int slider)
        {
            if (slider < 0 || slider >= w.ActorCount || !BotsHumanSeat(w, slider)) return;
            MovesConfig m = Cfg(w);
            double vx = w.Vx[slider], vz = w.Vz[slider];
            double vl = System.Math.Sqrt(vx * vx + vz * vz);
            if (vl < 1e-6) { vx = JsMath.Cos(w.Actors[slider].Facing); vz = JsMath.Sin(w.Actors[slider].Facing); vl = 1; }
            vx /= vl;
            vz /= vl;
            double range = m.SlideDistance + 1.0;
            TeamId team = w.Actors[slider].Team;
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                if (a.IsHuman || !a.Alive || a.Team == team) continue;
                double ox = w.X[id] - w.X[slider], oz = w.Z[id] - w.Z[slider];
                double d2 = ox * ox + oz * oz;
                if (d2 > range * range || d2 < 1e-9) continue;
                double l = System.Math.Sqrt(d2);
                if ((ox * vx + oz * vz) / l < BotsH) continue;
                int d = BotReflex.DiffIndex(w, id);
                b.Reads[id]++;
                if (!(BotReflex.Roll(w.Opts.Seed ^ BotsSaltSlide, w.Tick * 64 + slider, id) < BotsChance(m.BotStepOutChance, d))) continue;
                // perpendicular to the slide line, on the side the bot already stands (ties: id parity)
                double px = -vz, pz = vx;
                double side = ox * px + oz * pz;
                if (side < 0 || (side == 0 && (id & 1) == 1)) { px = -px; pz = -pz; }
                b.SideX[id] = px;
                b.SideZ[id] = pz;
                b.SideFrom[id] = w.Tick + BotsReadTicks(w, d);
                b.SideUntil[id] = b.SideFrom[id] + m.SlideTicks;
                b.SideSteps[id]++;
            }
        }

        // ------------------------------------------------------------------------------------------------ attack discipline

        /// <summary>
        /// T2-2 / T2-3 bot use. Heavy on: a bot's attack is a TAP (pressed one tick, let go the next), so a held steer press never
        /// becomes a charge, except a charge the bot chose: at a human in heavy reach, once per swing cycle with BotsChargeChance,
        /// held ChargeFullTick ticks then let go (the release fires the heavy). Combos on: in the recovery of a LANDED swing on a
        /// human (the hit-cancel window) a bot chains with BotsComboChance (a fresh press) and otherwise does not press there.
        /// Every press this holds back (a tap's let-go tick, a no-chain press in the window) is remembered (ComboDefer) and replayed
        /// on the tick the swing could start if the 0.35 s attack buffer would still hold it, so a bot that chose no charge and no
        /// chain swings on exactly the ticks it swings today (checked tick for tick against the switch-off run). Demolitions with
        /// TNT ready: her Attack press is the throw (DemolitionsTnt.ThrowOnAttack), left as it is.
        /// </summary>
        static void BotsAttack(World w, BotsMovesState b, MovesConfig m, int id, InputFrame o)
        {
            Actor a = w.Actors[id];
            int held = w.Moves.AttackHeldTicks[id];
            bool heavy = On(w, m.Heavy), combos = On(w, m.Combos);
            if (!heavy && !combos) return;
            // integrator: the same throw rule as Combat and Demolitions.AttackThrowsNow (a bot's press with the TNT ready is its throw)
            if (a.Cls == ClassId.Demolitions && w.Demolitions != null && OffenceAttackMayThrow(w, id)
                && w.Tick >= w.Demolitions.TntReadyTick[id]) return;
            int target = BotsTarget(w, id);
            bool human = target >= 0 && BotsHumanSeat(w, target) && w.Actors[target].Alive && w.Actors[target].Team != a.Team;
            int d = BotReflex.DiffIndex(w, id);
            if (heavy && b.ChargeHold[id] > 0)
            {
                if (a.SwingPending || held >= b.ChargeHold[id])
                {
                    o.Attack = false;   // full: the release fires the heavy (lane 4)
                    b.ChargeHold[id] = 0;
                }
                else o.Attack = true;
                return;
            }
            if (combos)
            {
                CombatFeelState feel = w.Depth != null ? w.Depth.Feel : null;
                int contact = a.SwingTick + a.SwingWindup;
                bool window = feel != null && feel.LandedSwing != null && feel.LandedSwing[id] == a.SwingTick && !a.SwingPending
                    && w.Tick > contact && w.Tick <= contact + a.SwingRecovery;
                if (window && human)   // fix/mv-heavy-press: against a bot no press is held back (bots never chain on bots)
                {
                    if (b.ComboKey[id] != a.SwingTick + 1)
                    {
                        b.ComboKey[id] = a.SwingTick + 1;
                        bool go = human && BotReflex.Roll(w.Opts.Seed ^ BotsSaltCombo, a.SwingTick * 64 + id + 1, id) < BotsChance(Cfg(w).BotComboChance, d);
                        b.ComboGo[id] = go ? (byte)1 : (byte)0;
                        if (go) b.Chains[id]++;
                    }
                    if (b.ComboGo[id] == 1 && o.Attack && held == 0) return;   // a chain: a fresh press
                    if (o.Attack) b.ComboDefer[id] = w.Tick + 1;   // held back (the buffer keeps the LATEST press)
                    o.Attack = false;
                    return;
                }
            }
            if (!o.Attack)
            {
                if (b.ComboDefer[id] == 0) return;
                // a held-back press, replayed on the tick the swing could start, only while the buffer would still hold it: the
                // swing the buffered press gave before (no earlier, no later, never one the buffer had lost)
                if (w.Tick - (b.ComboDefer[id] - 1) > CombatFeel.Ticks(w, w.Config.Depth.CombatFeel.BufferSeconds)) b.ComboDefer[id] = 0;
                else if (Combat.AttackReady(w, id))   // fix/mv-heavy-press: Heavy no longer changes a bot's presses
                {
                    o.Attack = true;
                    b.ComboDefer[id] = 0;
                }
                return;
            }
            b.ComboDefer[id] = 0;   // the steer presses itself: the buffer is refreshed by this press
            if (!heavy) return;
            if (human && held == 0 && b.ChargeKey[id] != a.SwingTick + 1 && Combat.AttackReady(w, id))
            {
                b.ChargeKey[id] = a.SwingTick + 1;
                double reach = m.HeavyReach != null && (int)a.Cls < m.HeavyReach.Length ? m.HeavyReach[(int)a.Cls] : 1.5;
                reach += w.Config.Class(w.Actors[target].Cls).Radius;
                double dx = w.X[target] - w.X[id], dz = w.Z[target] - w.Z[id];
                if (dx * dx + dz * dz <= reach * reach
                    && BotReflex.Roll(w.Opts.Seed ^ BotsSaltCharge, a.SwingTick * 64 + id + 1, id) < BotsChance(Cfg(w).BotChargeChance, d))
                {
                    // fix/mv-heavy-press: the bot's deliberate charge goes through lane 4's bot entry (its swing waits, then the
                    // heavy fires); a bot's held Attack is never a charge, so no tap discipline is needed and a bot that does not
                    // charge presses exactly as with Heavy off
                    OffenceBotCharge(w, id, m.ChargeFullTick);
                    b.Charges[id]++;
                    return;
                }
            }
        }

        /// <summary>A seat a PERSON is playing: BotAim.IsHumanSeat (call 18's test), and not actor 0 while the bots drive it (the idle
        /// autopilot, or a bot-only soak's null human frame): a seat on autopilot is a bot, so bot-only matches keep today's fights.</summary>
        static bool BotsHumanSeat(World w, int t) => BotAim.IsHumanSeat(w, t) && !(t == 0 && BotBrain.ActorZeroOnAutopilot(w));

        /// <summary>Bot id's current target (BotsState.Target), -1 = none; read without creating or touching the bot state.</summary>
        static int BotsTarget(World w, int id)
        {
            if (!BotBrain.TryGetBotsState(w, out BotsState st) || st.Target == null || id >= st.Target.Length) return -1;
            int t = st.Target[id];
            return t >= 0 && t < w.ActorCount ? t : -1;
        }

        // ------------------------------------------------------------------------------------------------ T1-5 quick wall record

        /// <summary>BuilderWalls.Commit: Builder id built a QUICK WALL as piece slot `slot` with build order `seq`.</summary>
        public static void BotsQuickWallBuilt(World w, int id, int slot, int seq)
        {
            if (w.Moves == null || id < 0 || id >= w.Moves.Bots.QuickNext.Length) return;
            BotsMovesState b = w.Moves.Bots;
            int k = b.QuickNext[id];
            b.QuickSeq[id * BotsMovesState.QuickRing + k] = seq;
            b.QuickNext[id] = (k + 1) % BotsMovesState.QuickRing;
        }

        /// <summary>BuilderWalls: piece slot `slot` (build order `seq`, owner id) is one of his quick walls.</summary>
        public static bool BotsIsQuickWall(World w, int id, int slot, int seq)
        {
            if (w.Moves == null || seq <= 0 || id < 0 || id >= w.Moves.Bots.QuickNext.Length) return false;
            int[] q = w.Moves.Bots.QuickSeq;
            for (int k = 0; k < BotsMovesState.QuickRing; k++) if (q[id * BotsMovesState.QuickRing + k] == seq) return true;
            return false;
        }
    }
}
