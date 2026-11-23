// CM b13 item J (dodge for every class, Travis: reaction play). Numbers: Sim/Config/DodgeConfig.cs.
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// CM b13 item J: the dodges state on the World (World.Dodges). Fixed per-actor arrays sized once (Init / Ensure at
    /// warm-up); nothing is allocated per tick. Not hashed (Net/StateHash): a dodge shows through positions and HP.
    /// </summary>
    public sealed class DodgeState
    {
        /// <summary>World.Tick of the press that started actor i's current dodge (dash tick 0), -1 when none.</summary>
        public int[] Start = new int[0];
        /// <summary>Planned dash ticks of the current dodge (2..DashTicks).</summary>
        public int[] Len = new int[0];
        /// <summary>First World.Tick actor i may dodge again.</summary>
        public int[] Ready = new int[0];
        /// <summary>Dash direction (unit) of the current dodge.</summary>
        public double[] DirX = new double[0], DirZ = new double[0];
        /// <summary>Planned end point of dash tick k (index i * Stride + k, k 0 = the start point).</summary>
        public double[] PathX = new double[0], PathZ = new double[0];
        public int Stride = 1;
        /// <summary>Bots (BotReflex): the threat key a bot last looked at, when it first saw it, whether it already decided, last jump tick.</summary>
        public int[] SeenKey = new int[0], SeenTick = new int[0], LastJump = new int[0];
        public byte[] Decided = new byte[0];
        /// <summary>Dodges started since the reset (tests and balance prints).</summary>
        public int Count;
        /// <summary>b13 r3 evidence counters (tests and balance prints; sized once, never hashed): per threat kind k (1..4,
        /// BotReflex.Kind*) Seen[k] new threats a bot saw, Decided[k] rolls taken, Pressed[k] dodges pressed.</summary>
        public readonly int[] SeenByKind = new int[8], DecidedByKind = new int[8], PressedByKind = new int[8];
        /// <summary>b13 r3 hill evidence: dodges started inside the hill ring, of them those that ended outside it; bot
        /// dodges turned to stay on the hill, held (no dodge: none stays on it), and taken off it because the hit was lethal.</summary>
        public int StartedOnHill, LeftHill, HillTurned, HillHeld, HillLethalOut, OpenTurned;
        // b13 r3 integrator (sim cost): BotReflex's per-tick threat lists, built once per tick by MatchTick before the bot fills
        // (BotReflex.BeginFills / EndFills) instead of every bot walking every actor and the whole bomb pool. Same candidates in
        // the same order, so match results are bit-identical; a direct Fill or FindThreat call outside MatchTick scans as before.
        public bool ScanCached;
        public int ScanTick = int.MinValue;
        public int[] ScanSwing = new int[0], ScanBomb = new int[0];
        public int ScanSwingN, ScanBombN;

        /// <summary>Called once in the World constructor after the actor arrays exist.</summary>
        public void Init(World w) { Ensure(w); Reset(w); }

        /// <summary>Called by the world reset path (World.ResetWorld).</summary>
        public void Reset(World w)
        {
            Ensure(w);
            for (int i = 0; i < Start.Length; i++)
            {
                Start[i] = -1;
                Len[i] = 0;
                Ready[i] = 0;
                SeenKey[i] = 0;
                SeenTick[i] = 0;
                LastJump[i] = -1000000;
                Decided[i] = 0;
            }
            Count = 0;
            for (int k = 0; k < SeenByKind.Length; k++) { SeenByKind[k] = 0; DecidedByKind[k] = 0; PressedByKind[k] = 0; }
            StartedOnHill = 0; LeftHill = 0; HillTurned = 0; HillHeld = 0; HillLethalOut = 0; OpenTurned = 0;
            ScanCached = false; ScanTick = int.MinValue; ScanSwingN = 0; ScanBombN = 0;
        }

        /// <summary>Sizes the arrays for the world's actors and dash length (grows only; warm-up only).</summary>
        public void Ensure(World w)
        {
            int n = w.Actors != null ? w.Actors.Length : 0;
            int dash = w.Config != null && w.Config.Depth != null && w.Config.Depth.Dodge != null ? w.Config.Depth.Dodge.DashTicks : 6;
            if (dash < 1) dash = 1;
            int stride = dash + 1;
            if (Start.Length >= n && Stride >= stride) return;
            int cap = n > Start.Length ? n : Start.Length;
            int[] oldReady = Ready;
            Start = new int[cap];
            Len = new int[cap];
            Ready = new int[cap];
            DirX = new double[cap];
            DirZ = new double[cap];
            SeenKey = new int[cap];
            SeenTick = new int[cap];
            LastJump = new int[cap];
            Decided = new byte[cap];
            Stride = stride > Stride ? stride : Stride;
            PathX = new double[cap * Stride];
            PathZ = new double[cap * Stride];
            for (int i = 0; i < cap; i++)
            {
                Start[i] = -1;
                Ready[i] = i < oldReady.Length ? oldReady[i] : 0;
                LastJump[i] = -1000000;
            }
        }
    }

    /// <summary>
    /// CM b13 item J (RULE CHANGE, Full profile only): a short invulnerable dash for every class. InputFrame.Dodge (edge
    /// latched by the human layer, set by BotReflex for bots) starts a dodge when the actor is alive, on the ground, not
    /// getting up, not staggered or knocked, has no swing wind-up in flight, carries no crystal and is off cooldown. The dash
    /// goes DodgeConfig.Distance along the move input (facing when the input is under MinMoveInput) over DashTicks ticks
    /// with an ease-out (per-tick share (2n - 2k + 1) / n^2, summing to 1); the whole path is planned at the press like
    /// Jump.PlanJump but on the ground (Collide.SweepActor, airborne false, so short walls stop it too): a tick whose end
    /// footprint is not clear walkable ground, or that makes no progress (a wall), ends the dash there; fewer than
    /// MinClearTicks clear ticks = refused. Dash tick k = World.Tick - Start (0 = the press tick); invulnerable on k 1 to
    /// min(IFrameTicks, Len - 1) against every cause but Fall; then RecoverTicks ticks of walking at RecoverSpeedScale;
    /// CooldownTicks from the press. The dash ends at the walk speed along its direction (no stop-dead, no slide). A dodging
    /// actor cannot attack (Combat) or jump (its walk step is skipped). Event Dodge (A = actor, X/Z = start, Value = ticks).
    /// </summary>
    public static class Dodge
    {
        /// <summary>The rule is on (Full profile only).</summary>
        public static bool On(World w) => w.Config.Depth.Dodge != null && DepthRules.On(w, w.Config.Depth.Dodge.On);

        /// <summary>Share of the dash distance covered on dash tick k (1..n): an ease-out that sums to 1 over n ticks.</summary>
        public static double Share(int k, int n) => (2.0 * n - 2.0 * k + 1.0) / (n * (double)n);

        /// <summary>Moves actor id this tick when it dodges; true = its movement was handled (Movement skips the walk).</summary>
        public static bool Step(World w, int id, double dt)
        {
            if (!On(w)) return false;
            DodgeState s = w.Dodges;
            s.Ensure(w);
            DodgeConfig dc = w.Config.Depth.Dodge;
            InputFrame inp = w.Inputs[id];
            bool press = inp.Dodge;
            inp.Dodge = false; // read and cleared: a stored true never repeats a dodge
            if (s.Start[id] >= 0)
            {
                int k = w.Tick - s.Start[id];
                if (k >= 0 && k < s.Len[id])
                {
                    DashTick(w, s, id, k, dt);
                    return true;
                }
                if (k >= 0 && k < s.Len[id] + dc.RecoverTicks)
                {
                    RecoverTick(w, id, dt, dc);
                    return true;
                }
                s.Start[id] = -1;
            }
            if (!press || !CanStart(w, id)) return false;
            if (!Begin(w, s, id, dt, dc))
            {
                PlayerSeat.Refuse(w, id, PlayerReasons.ButtonDodge, PlayerReasons.RefusedBlockedPlace);   // lane S2: no clear dash path
                return false;
            }
            DashTick(w, s, id, 0, dt);
            return true;
        }

        /// <summary>Every refusal but the path: alive, grounded, standing, not staggered or knocked, no wind-up, no crystal, cooldown 0.</summary>
        public static bool CanStart(World w, int id)
        {
            Actor a = w.Actors[id];
            if (!a.Alive || a.AirLen > 0 || a.StandUp > 0) return false;
            if (a.HitStunTicks > 0) return false;
            if ((a.KVx != 0 || a.KVz != 0) && !MovesHooks.DefenceDodgeOutOfKnock(w, id)) return false;   // class moves T2-3 (integrator): dodge out of a light knock slide
            if (a.SwingPending) return false;
            if (CombatFeel.On(w) && CombatFeel.FeintRecovering(w, id)) return false; // b16: a feint's short recovery holds the dodge too
            if (a.Carry == CarryKind.Corruption) return false;
            if (MovesHooks.DefenceLocksDodge(w, id)) return false;   // class moves T2-7 (lane 2): the landing recovery after an evading jump
            return w.Tick >= w.Dodges.Ready[id];
        }

        /// <summary>Plans the dash (direction, path, length); false = refused (fewer than MinClearTicks clear ticks).</summary>
        private static bool Begin(World w, DodgeState s, int id, double dt, DodgeConfig dc)
        {
            Actor a = w.Actors[id];
            InputFrame inp = w.Inputs[id];
            double mx = inp.MoveX, mz = inp.MoveZ;
            double ml = System.Math.Sqrt(mx * mx + mz * mz);
            double dx, dz;
            if (ml > dc.MinMoveInput)
            {
                dx = mx / ml;
                dz = mz / ml;
            }
            else
            {
                dx = JsMath.Cos(a.Facing);
                dz = JsMath.Sin(a.Facing);
            }
            int n = dc.DashTicks < s.Stride ? dc.DashTicks : s.Stride - 1;
            // class moves T1-2 (lane 2, switch DodgeLunge): a dodge-in toward an enemy stops short of it (dc.Distance when off)
            double dist = MovesHooks.DefenceLungeDash(w, id, dx, dz, dc.Distance);
            int b = id * s.Stride;
            double x = w.X[id], z = w.Z[id];
            s.PathX[b] = x;
            s.PathZ[b] = z;
            int clear = 0;
            for (int k = 1; k <= n; k++)
            {
                double step = dist * Share(k, n);
                Collide.SweepActor(w, id, x, z, dx * step, dz * step, false, out double nx, out double nz);
                nx = JsMath.F32(nx);
                nz = JsMath.F32(nz);
                double along = (nx - x) * dx + (nz - z) * dz;
                if (!(along > step * 0.25) || !Collide.FootprintClear(w, id, nx, nz)) break; // a wall, or no ground: the dash ends here
                s.PathX[b + k] = nx;
                s.PathZ[b + k] = nz;
                x = nx;
                z = nz;
                clear = k;
            }
            if (clear < dc.MinClearTicks) return false;
            s.Start[id] = w.Tick;
            s.Len[id] = clear;
            s.Ready[id] = w.Tick + Durations.T30(w.Config, dc.CooldownTicks);   // DG68: config tick counts pass the converter
            s.DirX[id] = dx;
            s.DirZ[id] = dz;
            s.Count++;
            MovesHooks.DefenceDodgeBegan(w, id, s.Start[id], clear);   // class moves T1-2 (lane 2): the dodge-in's first recover tick
            CriticalMass.Sim.Grid.HillDef hill = w.Map != null ? w.Map.Hill : null;
            if (hill != null)
            {
                double hx = w.X[id] - hill.X, hz = w.Z[id] - hill.Z, ex = s.PathX[b + clear] - hill.X, ez = s.PathZ[b + clear] - hill.Z;
                if (hx * hx + hz * hz < hill.R * hill.R)
                {
                    s.StartedOnHill++;
                    if (ex * ex + ez * ez >= hill.R * hill.R) s.LeftHill++;
                }
            }
            a.Facing = JsMath.Atan2(dz, dx);
            World.PushEvent(w, SimEventType.Dodge, id, 0, w.X[id], w.Z[id], clear);
            return true;
        }

        /// <summary>True when (nx, nz) overlaps the hatched solid Kaiju's body and is closer to its centre than the actor is now.</summary>
        private static bool IntoKaiju(World w, int id, double nx, double nz)
        {
            Kaiju k = w.Kaiju;
            if (k == null || k.State == KaijuState.Egg || !Phys.On(w, w.Config.Physics.SolidKaiju)) return false;
            double rr = w.Config.Physics.KaijuBodyRadius + Collide.ActorRadius(w, id);
            double ex = nx - k.X, ez = nz - k.Z, cx = w.X[id] - k.X, cz = w.Z[id] - k.Z;
            double e2 = ex * ex + ez * ez;
            return e2 < rr * rr && e2 < cx * cx + cz * cz;
        }

        private static void DashTick(World w, DodgeState s, int id, int k, double dt)
        {
            Actor a = w.Actors[id];
            int b = id * s.Stride;
            double nx = s.PathX[b + k + 1], nz = s.PathZ[b + k + 1];
            // the grid may change mid-dash: never step onto ground that is no longer clear (stop where it stands); the Kaiju
            // moves mid-dash too: never dash into its body (b13 r3 integrator: Hard seed 101 16v16 tick 14061 dashed a Warrior
            // into a Kaiju wedged between walls, where the body push-out could not clear it)
            if (!Collide.FootprintClear(w, id, nx, nz) || IntoKaiju(w, id, nx, nz))
            {
                s.Len[id] = k;
                w.Vx[id] = JsMath.F32(0);
                w.Vz[id] = JsMath.F32(0);
                return;
            }
            double vx = (nx - w.X[id]) / dt, vz = (nz - w.Z[id]) / dt;
            w.X[id] = nx;
            w.Z[id] = nz;
            if (k == s.Len[id] - 1)
            {
                // the last dash tick: leave at the walk speed along the dash (no stop-dead, no slide)
                double speed = w.Config.Class(a.Cls).MoveSpeed;
                vx = s.DirX[id] * speed;
                vz = s.DirZ[id] * speed;
            }
            w.Vx[id] = JsMath.F32(vx);
            w.Vz[id] = JsMath.F32(vz);
            a.Moving = true;
            a.UnsupportedTicks = 0;
        }

        /// <summary>A recovery tick: walk along the move input at RecoverSpeedScale of the class speed (no jump, no accel).</summary>
        private static void RecoverTick(World w, int id, double dt, DodgeConfig dc)
        {
            Actor a = w.Actors[id];
            InputFrame inp = w.Inputs[id];
            double mx = inp.MoveX, mz = inp.MoveZ;
            double l2 = mx * mx + mz * mz;
            if (l2 > 1)
            {
                double inv = 1 / System.Math.Sqrt(l2);
                mx *= inv;
                mz *= inv;
            }
            double speed = w.Config.Class(a.Cls).MoveSpeed * dc.RecoverSpeedScale;
            double vx = mx * speed, vz = mz * speed;
            w.Vx[id] = JsMath.F32(vx);
            w.Vz[id] = JsMath.F32(vz);
            if (vx != 0 || vz != 0) Collide.MoveActor(w, id, vx * dt, vz * dt, false);
            a.Moving = l2 > 0.0001;
        }

        /// <summary>True when actor id ignores damage of this cause now (dash ticks 1 to min(IFrameTicks, Len - 1); never Fall).</summary>
        public static bool Immune(World w, int id, DamageCause cause)
        {
            if (cause == DamageCause.Fall || !On(w)) return false;
            DodgeState s = w.Dodges;
            if (id < 0 || id >= s.Start.Length || s.Start[id] < 0) return false;
            int k = w.Tick - s.Start[id];
            int last = w.Config.Depth.Dodge.IFrameTicks;
            if (last > s.Len[id] - 1) last = s.Len[id] - 1;
            return k >= 1 && k <= last;
        }

        /// <summary>True while actor id dashes (dash ticks 0 to Len - 1).</summary>
        public static bool Active(World w, int id)
        {
            if (!On(w)) return false;
            DodgeState s = w.Dodges;
            if (id < 0 || id >= s.Start.Length || s.Start[id] < 0) return false;
            int k = w.Tick - s.Start[id];
            return k >= 0 && k < s.Len[id];
        }

        /// <summary>Lane S2: ticks left of actor id's dash (and of its recovery too when withRecover); 0 when not dodging.</summary>
        public static int TicksLeft(World w, int id, bool withRecover)
        {
            if (!On(w)) return 0;
            DodgeState s = w.Dodges;
            if (s == null || s.Start == null || id < 0 || id >= s.Start.Length || s.Start[id] < 0) return 0;
            int end = s.Start[id] + s.Len[id] + (withRecover ? w.Config.Depth.Dodge.RecoverTicks : 0);
            int left = end - w.Tick;
            return left > 0 ? left : 0;
        }

        /// <summary>px DM: the tick actor id's last dodge started (derived from its cooldown; very negative when it never dodged).</summary>
        public static int LastStart(World w, int id)
        {
            DodgeState s = w.Dodges;
            if (id < 0 || id >= s.Ready.Length) return int.MinValue / 2;
            return s.Start[id] >= 0 ? s.Start[id] : s.Ready[id] - Durations.T30(w.Config, w.Config.Depth.Dodge.CooldownTicks);
        }

        /// <summary>
        /// px DM (lead's call 14, the timed-dodge reward): Combat.ResolveSwing calls this at the contact tick of actor i's melee
        /// swing from (ax, az) along (fx, fz), after CombatFeel.AfterContact. Every living enemy j that DODGED the swing raises
        /// ONE HitBlocked (A = j, B = i, Value = BlockedDodge, Radius = the swing's damage it avoided, Tier 1 = PERFECT: the
        /// dodge started PerfectDodgeTicks or fewer before contact). Dodged = j is in reach and arc now but inside its i-frames,
        /// or j is out of the reach or arc now, started its dodge during the wind-up (or up to DashTicks before it) and the
        /// spot it dodged from was inside the reach and arc. With DodgeReward on and a whiff (hits 0) the attacker stays open
        /// to a PUNISH critical for DodgePunishSeconds, and a perfect dodge refunds PerfectDodgeCooldownRefund of its cooldown.
        /// Full rules only. The event replaces lane S2's two dodge reports for melee (Combat's out-of-arc one and Damage's
        /// i-frame one), so a dodged swing says DODGED exactly once whether the dash left the reach, the arc or neither.
        /// </summary>
        public static void SwingDodges(World w, int i, double ax, double az, double fx, double fz, double range, double halfRad, bool body, int hits, double dmg, bool report)
        {
            if (!On(w)) return;
            DodgeState s = w.Dodges;
            DodgeConfig dc = w.Config.Depth.Dodge;
            Actor a = w.Actors[i];
            int cd = Durations.T30(w.Config, dc.CooldownTicks);
            int n = w.ActorCount;
            for (int j = 0; j < n && j < s.Ready.Length; j++)
            {
                Actor t = w.Actors[j];
                if (j == i || !t.Alive || t.Team == a.Team) continue;
                int last = s.Start[j] >= 0 ? s.Start[j] : s.Ready[j] - cd;   // the dash in flight, else the last one (from its cooldown)
                if (last > w.Tick || last < a.SwingTick - dc.DashTicks) continue;   // no dodge around this wind-up (cheap test first)
                double rt = w.Config.Class(t.Cls).Radius, reach = range + rt;
                bool inside = InSwing(w.X[j] - ax, w.Z[j] - az, fx, fz, reach, halfRad, body, rt);
                bool dodged;
                if (inside) dodged = Immune(w, j, DamageCause.Melee);
                else
                {
                    int b = j * s.Stride;
                    dodged = last >= a.SwingTick - dc.DashTicks && InSwing(s.PathX[b] - ax, s.PathZ[b] - az, fx, fz, reach, halfRad, body, rt);
                }
                if (!dodged) continue;
                MovesHooks.DefenceDodgedSwing(w, j, i);   // class moves T1-3 refund, T2-8 second wind (lane 2; no-op with every switch off)
                int before = w.Tick - last;
                bool perfect = before >= 1 && before <= dc.PerfectDodgeTicks;
                if (report) World.PushEvent(w, SimEventType.HitBlocked, j, i, w.X[j], w.Z[j], PlayerReasons.BlockedDodge, null, DamageCause.None, dmg, 0, 0, 0, perfect ? 1 : 0, 0, 0);
                if (!dc.DodgeReward || (dc.DodgeRewardHumansOnly && !w.Actors[j].IsHuman)) continue;   // px DM call 18: the dodger is a human seat
                if (hits <= 0 && CombatFeel.On(w))
                {
                    int until = w.Tick + CombatFeel.Ticks(w, dc.DodgePunishSeconds);
                    if (w.Depth.Feel.WhiffRecoverUntil[i] < until) w.Depth.Feel.WhiffRecoverUntil[i] = until;
                }
                if (perfect && dc.PerfectDodgeCooldownRefund > 0)
                {
                    int refund = (int)(cd * dc.PerfectDodgeCooldownRefund);
                    s.Ready[j] = s.Ready[j] - refund > w.Tick ? s.Ready[j] - refund : w.Tick;
                }
            }
        }

        /// <summary>True when the offset (dx, dz) from the swing's origin lies inside its reach and (body-widened) arc.</summary>
        private static bool InSwing(double dx, double dz, double fx, double fz, double reach, double halfRad, bool body, double rt)
        {
            double d2 = dx * dx + dz * dz;
            if (d2 > reach * reach) return false;
            if (d2 <= 1e-12) return true;
            double d = System.Math.Sqrt(d2);
            double cos = body ? Combat.WidenedCos(halfRad, rt, d) : JsMath.Cos(halfRad);
            return (dx * fx + dz * fz) / d >= cos - 1e-9;
        }

        /// <summary>Ticks until actor id may dodge again (0 = ready).</summary>
        public static int CooldownTicks(World w, int id)
        {
            if (!On(w)) return 0;
            DodgeState s = w.Dodges;
            if (id < 0 || id >= s.Ready.Length) return 0;
            int left = s.Ready[id] - w.Tick;
            return left > 0 ? left : 0;
        }

        /// <summary>0..1 through the dash at render fraction alpha; -1 when not dashing.</summary>
        public static double Phase(World w, int id, double alpha)
        {
            if (!Active(w, id)) return -1;
            DodgeState s = w.Dodges;
            double p = (w.Tick - s.Start[id] + alpha) / s.Len[id];
            return p < 0 ? 0 : p > 1 ? 1 : p;
        }
    }
}
