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
            if (!Begin(w, s, id, dt, dc)) return false;
            DashTick(w, s, id, 0, dt);
            return true;
        }

        /// <summary>Every refusal but the path: alive, grounded, standing, not staggered or knocked, no wind-up, no crystal, cooldown 0.</summary>
        public static bool CanStart(World w, int id)
        {
            Actor a = w.Actors[id];
            if (!a.Alive || a.AirLen > 0 || a.StandUp > 0) return false;
            if (a.HitStunTicks > 0 || a.KVx != 0 || a.KVz != 0) return false;
            if (a.SwingPending) return false;
            if (a.Carry == CarryKind.Corruption) return false;
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
            int b = id * s.Stride;
            double x = w.X[id], z = w.Z[id];
            s.PathX[b] = x;
            s.PathZ[b] = z;
            int clear = 0;
            for (int k = 1; k <= n; k++)
            {
                double step = dc.Distance * Share(k, n);
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

        private static void DashTick(World w, DodgeState s, int id, int k, double dt)
        {
            Actor a = w.Actors[id];
            int b = id * s.Stride;
            double nx = s.PathX[b + k + 1], nz = s.PathZ[b + k + 1];
            // the grid may change mid-dash: never step onto ground that is no longer clear (stop where it stands)
            if (!Collide.FootprintClear(w, id, nx, nz))
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
