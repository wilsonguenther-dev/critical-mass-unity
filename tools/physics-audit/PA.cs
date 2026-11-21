// Physics audit 2026-11-21 (Editor-only scratch, OUTSIDE Assets/: never compiled into the game).
// Builds its own sim worlds (Full rules, the game's GameConfig.CreateDefault) and measures movement, collision and vertical
// physics. It never writes the Editor's running match. Run:
//   ~/cm-editor-eyes/bin/u 300 script run -- --file <this> --entry PA.Movement
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;

public static class PA
{
    public const string Out = "/Users/wilsonguenther/cm-incidents/physics-audit-2026-11-21/";
    static readonly string[] Cn = { "Warrior", "Farmer", "Miner", "Demolitions", "Builder", "Healer" };

    // ---------------------------------------------------------------- world helpers
    public static World NewW(ClassId cls, int seed = 4242, int team = 16, double hatch = 1e6)
    {
        GameConfig cfg = GameConfig.CreateDefault();
        cfg.HatchSeconds = hatch;
        return World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = team, HumanTeam = TeamId.Azure, HumanClass = cls, Config = cfg });
    }
    public static void Put(World w, int id, double x, double z)
    {
        w.X[id] = x; w.Z[id] = z; w.PrevX[id] = x; w.PrevZ[id] = z; w.Vx[id] = 0; w.Vz[id] = 0;
        Actor a = w.Actors[id]; a.KVx = 0; a.KVz = 0; a.HitStunTicks = 0;
    }
    public static void Bench(World w, ICollection<int> keep)
    {
        for (int id = 0; id < w.ActorCount; id++)
        {
            Actor a = w.Actors[id];
            if (keep.Contains(id)) { a.Alive = true; a.Hp = a.MaxHp; a.RespawnIn = 0; a.StandUp = 0; a.SpawnProtect = 0; continue; }
            a.Alive = false; a.Hp = 0; a.RespawnIn = 1e9; a.Moving = false; w.X[id] = -50 - id; w.Z[id] = -50; w.PrevX[id] = w.X[id]; w.PrevZ[id] = -50;
        }
    }
    public static void SetCls(World w, int id, ClassId c) { Actor a = w.Actors[id]; a.Cls = c; a.MaxHp = w.Config.Class(c).MaxHp; a.Hp = a.MaxHp; }
    static int Idx(World w, int cx, int cz) => cz * w.Map.Width + cx;
    public static bool Flat(World w, int cx, int cz)
    {
        if (cx < 0 || cz < 0 || cx >= w.Map.Width || cz >= w.Map.Height) return false;
        int i = Idx(w, cx, cz);
        return w.Map.Cells[i] == Cell.Floor && (w.Map.Lift == null || w.Map.Lift[i] == 0);
    }
    /// <summary>Centre of an n x n all-floor level-0 square, nearest to (nearX, nearZ).</summary>
    public static bool OpenSpot(World w, int n, double nearX, double nearZ, out double x, out double z)
    {
        double best = 1e18; x = z = 0;
        for (int cz = 0; cz + n <= w.Map.Height; cz++)
            for (int cx = 0; cx + n <= w.Map.Width; cx++)
            {
                double mx = cx + n * 0.5, mz = cz + n * 0.5;
                double d = (mx - nearX) * (mx - nearX) + (mz - nearZ) * (mz - nearZ);
                if (d >= best) continue;
                bool ok = true;
                for (int j = 0; j < n && ok; j++) for (int i = 0; i < n && ok; i++) ok = Flat(w, cx + i, cz + j);
                if (!ok) continue;
                best = d; x = mx; z = mz;
            }
        return best < 1e18;
    }
    static readonly InputFrame F = SimInput.EmptyInput();
    public static void In(World w, int id, double mx, double mz, bool jump = false, bool dodge = false, bool attack = false, double ax = double.NaN, double az = double.NaN)
    {
        F.MoveX = mx; F.MoveZ = mz; F.Jump = jump; F.Dodge = dodge; F.Attack = attack; F.Ability = false; F.Interact = false; F.Terraform = false; F.TerraformSwitch = false;
        F.AimX = ax; F.AimZ = az;
        SimInput.ApplyInput(w, id, F);
        w.Inputs[id].Dodge = dodge;
    }
    public static double Sp(World w, int id) => Math.Sqrt(w.Vx[id] * w.Vx[id] + w.Vz[id] * w.Vz[id]);
    static string F3(double v) => v.ToString("F3");
    static string F2(double v) => v.ToString("F2");
    static void Save(string name, StringBuilder sb) { Directory.CreateDirectory(Out + "data"); File.WriteAllText(Out + "data/" + name, sb.ToString()); }

    // ---------------------------------------------------------------- 1. movement feel
    public static string Movement()
    {
        var sb = new StringBuilder("# 1. movement (sim 30 Hz, 1 u = 1 cell; Full rules, GameConfig.CreateDefault)\n");
        sb.Append("class | cfgSpeed | top u/s | t90 ticks | t100 ticks | accelDist u | stop ticks | stopDist u | reverse: ticks to vx<0 | ticks to -90% | overshoot u | 90deg turn ticks (<5deg) | diag (1,1) u/s | analog 0.5 u/s | facing snap ticks | mass | radius\n");
        for (int c = 0; c < 6; c++)
        {
            ClassId cls = (ClassId)c;
            World w = NewW(cls);
            Bench(w, new[] { 0 });
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            Put(w, 0, ox - 6, oz);
            double cfgSp = w.Config.Class(cls).MoveSpeed;
            int t90 = -1, t100 = -1; double x0 = w.X[0];
            for (int t = 1; t <= 30; t++)
            {
                In(w, 0, 1, 0); SimStep.Step(w);
                double s = Sp(w, 0);
                if (t90 < 0 && s >= 0.9 * cfgSp - 1e-6) t90 = t;
                if (t100 < 0 && s >= cfgSp - 1e-4) t100 = t;
            }
            double top = Sp(w, 0);
            // accel distance: distance in the first t100 ticks minus ideal
            World w2 = NewW(cls); Bench(w2, new[] { 0 }); Put(w2, 0, ox - 6, oz);
            for (int t = 0; t < Math.Max(1, t100); t++) { In(w2, 0, 1, 0); SimStep.Step(w2); }
            double accelLoss = cfgSp * Math.Max(1, t100) / 30.0 - (w2.X[0] - (ox - 6));
            // stop
            double xs = w.X[0]; int stopT = -1;
            for (int t = 1; t <= 30; t++) { In(w, 0, 0, 0); SimStep.Step(w); if (stopT < 0 && Sp(w, 0) == 0) stopT = t; }
            double stopD = w.X[0] - xs;
            // reverse
            Put(w, 0, ox - 6, oz);
            for (int t = 0; t < 20; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
            double xr = w.X[0], xmax = xr; int tneg = -1, t90r = -1;
            for (int t = 1; t <= 30; t++)
            {
                In(w, 0, -1, 0); SimStep.Step(w);
                xmax = Math.Max(xmax, w.X[0]);
                if (tneg < 0 && w.Vx[0] < 0) tneg = t;
                if (t90r < 0 && w.Vx[0] <= -0.9 * cfgSp) t90r = t;
            }
            // 90 degree turn
            Put(w, 0, ox - 6, oz);
            for (int t = 0; t < 20; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
            int tturn = -1;
            for (int t = 1; t <= 30; t++)
            {
                In(w, 0, 0, 1); SimStep.Step(w);
                double ang = Math.Atan2(w.Vz[0], w.Vx[0]) * 180 / Math.PI;
                if (tturn < 0 && Math.Abs(90 - ang) < 5 && Sp(w, 0) > 0.9 * cfgSp) tturn = t;
            }
            // facing snap: facing after one tick of a new direction
            double f0 = w.Actors[0].Facing; In(w, 0, -1, 0); SimStep.Step(w);
            int fsnap = Math.Abs(Math.Abs(w.Actors[0].Facing) - Math.PI) < 1e-3 ? 1 : -1;
            // diagonal and analog
            Put(w, 0, ox - 6, oz - 5);
            for (int t = 0; t < 20; t++) { In(w, 0, 1, 1); SimStep.Step(w); }
            double diag = Sp(w, 0);
            Put(w, 0, ox - 6, oz);
            for (int t = 0; t < 20; t++) { In(w, 0, 0.5, 0); SimStep.Step(w); }
            double analog = Sp(w, 0);
            sb.Append(Cn[c]).Append(" | ").Append(F2(cfgSp)).Append(" | ").Append(F3(top)).Append(" | ").Append(t90).Append(" | ").Append(t100)
              .Append(" | lost ").Append(F3(accelLoss)).Append(" | ").Append(stopT).Append(" | ").Append(F3(stopD)).Append(" | ").Append(tneg).Append(" | ").Append(t90r)
              .Append(" | ").Append(F3(xmax - xr)).Append(" | ").Append(tturn).Append(" | ").Append(F3(diag)).Append(" | ").Append(F3(analog)).Append(" | ").Append(fsnap)
              .Append(" | ").Append(F2(w.Config.Class(cls).Mass)).Append(" | ").Append(F2(w.Config.Class(cls).Radius)).Append('\n');
        }
        // input-to-motion delay in sim ticks: a press applied before tick N moves the body in tick N
        {
            World w = NewW(ClassId.Warrior); Bench(w, new[] { 0 });
            OpenSpot(w, 12, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz); Put(w, 0, ox, oz);
            In(w, 0, 0, 0); SimStep.Step(w);
            double x = w.X[0]; In(w, 0, 1, 0); SimStep.Step(w);
            sb.Append("input-to-motion: press applied for tick N moves the body ").Append(F3(w.X[0] - x)).Append(" u in tick N itself (0 sim ticks of delay; the first tick is 1/").Append(w.Config.Movement.AccelSeconds * 30).Append(" of top speed)\n");
        }
        // Speed modifiers seen in a fight: carry, swing, stagger, ladder
        {
            var cf = new GameConfig().Depth.CombatFeel;
            sb.Append("combat-feel speed scales: windup ").Append(cf.WindupMoveMultiplier).Append(", recovery ").Append(cf.RecoveryMoveMultiplier).Append(", stagger slow ").Append(cf.StaggerSlowMultiplier).Append(", ladder ").Append(cf.LadderSpeedMultiplier).Append('\n');
        }
        Save("1-movement.txt", sb);
        return sb.ToString();
    }

    // ---------------------------------------------------------------- 2. collision
    static double Pen(World w, int i, int j)
    {
        double dx = w.X[i] - w.X[j], dz = w.Z[i] - w.Z[j];
        return Collide.ActorRadius(w, i) + Collide.ActorRadius(w, j) - Math.Sqrt(dx * dx + dz * dz);
    }
    public static string Collision()
    {
        var sb = new StringBuilder("# 2. collision (sim)\n");
        // (a) two bodies spawned overlapping: how fast they part
        {
            World w = NewW(ClassId.Warrior); Bench(w, new[] { 0, 16 });
            OpenSpot(w, 12, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            Put(w, 0, ox, oz); Put(w, 16, ox + 0.2, oz);
            sb.Append("(a) enemy pair spawned 0.2 u apart (overlap ").Append(F3(Pen(w, 0, 16))).Append(" u), no input: overlap per tick ");
            for (int t = 1; t <= 8; t++) { In(w, 0, 0, 0); In(w, 16, 0, 0); SimStep.Step(w); sb.Append(F3(Pen(w, 0, 16))).Append(' '); }
            sb.Append('\n');
        }
        // (b) head-on walkers: enemies and allies
        foreach (bool ally in new[] { false, true })
        {
            World w = NewW(ClassId.Warrior); int other = ally ? 1 : 16; Bench(w, new[] { 0, other });
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            Put(w, 0, ox - 4, oz); Put(w, other, ox + 4, oz + 0.05);
            double maxPen = -9; int passT = -1;
            for (int t = 1; t <= 120; t++)
            {
                In(w, 0, 1, 0); In(w, other, -1, 0); SimStep.Step(w);
                maxPen = Math.Max(maxPen, Pen(w, 0, other));
                if (passT < 0 && w.X[0] > w.X[other]) passT = t;
            }
            sb.Append("(b) head-on ").Append(ally ? "ALLIES" : "ENEMIES").Append(" (0.05 u off-line), 4 s: max overlap ").Append(F3(maxPen)).Append(" u, passed each other at tick ").Append(passT)
              .Append(", final x ").Append(F2(w.X[0] - ox)).Append(" / ").Append(F2(w.X[other] - ox)).Append(" lateral ").Append(F2(w.Z[0] - oz)).Append(" / ").Append(F2(w.Z[other] - oz)).Append('\n');
        }
        // (c) doorway: 8 bodies through a gap of 1, 2 and 3 cells
        foreach (int gap in new[] { 1, 2, 3 })
        {
            foreach (bool mixed in new[] { false, true })
            {
                World w = NewW(ClassId.Warrior);
                var ids = new List<int>();
                for (int k = 0; k < 8; k++) ids.Add(mixed && (k % 2 == 1) ? 16 + k : k);
                Bench(w, ids);
                OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
                int wx = (int)Math.Floor(ox);
                int gz0 = (int)Math.Floor(oz) - gap / 2;
                for (int cz = (int)oz - 8; cz <= (int)oz + 8; cz++) if (cz < gz0 || cz >= gz0 + gap) w.Map.Cells[Idx(w, wx, cz)] = Cell.Wall;
                double doorZ = gz0 + gap * 0.5;
                for (int k = 0; k < 8; k++) Put(w, ids[k], ox - 4 - (k / 3) * 1.0, doorZ - 1.2 + (k % 3) * 1.2);
                double maxPen = -9; int allT = -1; var through = new int[8]; for (int k = 0; k < 8; k++) through[k] = -1;
                double jit = 0; int jn = 0; var px1 = new double[8]; var pz1 = new double[8]; var px2 = new double[8]; var pz2 = new double[8];
                int reversals = 0;
                for (int t = 1; t <= 600 && allT < 0; t++)
                {
                    for (int k = 0; k < 8; k++)
                    {
                        int id = ids[k];
                        double tx = w.X[id] < wx + 0.5 ? wx + 0.5 : ox + 5, tz = w.X[id] < wx + 0.5 ? doorZ : w.Z[id];
                        double dx = tx - w.X[id], dz = tz - w.Z[id], l = Math.Sqrt(dx * dx + dz * dz) + 1e-9;
                        In(w, id, dx / l, dz / l);
                    }
                    SimStep.Step(w);
                    for (int a = 0; a < 8; a++) for (int b = a + 1; b < 8; b++) maxPen = Math.Max(maxPen, Pen(w, ids[a], ids[b]));
                    bool all = true;
                    for (int k = 0; k < 8; k++)
                    {
                        int id = ids[k];
                        if (through[k] < 0 && w.X[id] > wx + 1 + 0.5) through[k] = t;
                        if (through[k] < 0) all = false;
                        if (t > 2 && through[k] < 0)
                        {
                            double ddx = w.X[id] - 2 * px1[k] + px2[k], ddz = w.Z[id] - 2 * pz1[k] + pz2[k];
                            jit += Math.Sqrt(ddx * ddx + ddz * ddz); jn++;
                            double v1x = w.X[id] - px1[k], v1z = w.Z[id] - pz1[k], v0x = px1[k] - px2[k], v0z = pz1[k] - pz2[k];
                            if (v1x * v0x + v1z * v0z < 0 && v1x * v1x + v1z * v1z > 1e-4) reversals++;
                        }
                        px2[k] = px1[k]; pz2[k] = pz1[k]; px1[k] = w.X[id]; pz1[k] = w.Z[id];
                    }
                    if (all) allT = t;
                }
                int done = 0; for (int k = 0; k < 8; k++) if (through[k] >= 0) done++;
                Array.Sort(through);
                sb.Append("(c) doorway ").Append(gap).Append(" cell(s), 8 ").Append(mixed ? "MIXED-team" : "same-team").Append(" bodies: through ").Append(done).Append("/8, all by tick ").Append(allT)
                  .Append(" (").Append(allT > 0 ? F2(allT / 30.0) : "never").Append(" s), first/last ").Append(string.Join(",", through)).Append(", max overlap ").Append(F3(maxPen))
                  .Append(" u, queue jitter mean |d2x| ").Append(F3(jn > 0 ? jit / jn : 0)).Append(" u/tick2, velocity reversals ").Append(reversals).Append('\n');
            }
        }
        // (d) wall slide: run into a wall at 10, 30, 45, 60 degrees; along-wall speed / top speed
        {
            World w = NewW(ClassId.Warrior); Bench(w, new[] { 0 });
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            int wz = (int)Math.Floor(oz) + 2;
            for (int cx = (int)ox - 7; cx <= (int)ox + 3; cx++) w.Map.Cells[Idx(w, cx, wz)] = Cell.Wall;
            sb.Append("(d) wall slide (wall along x, input angle into it): ");
            foreach (double deg in new[] { 10.0, 30, 45, 60, 80 })
            {
                Put(w, 0, ox - 6, wz - 0.46);
                double r = deg * Math.PI / 180; double x0 = 0;
                for (int t = 0; t < 40; t++) { In(w, 0, Math.Cos(r), Math.Sin(r)); SimStep.Step(w); if (t == 9) x0 = w.X[0]; }
                double along = (w.X[0] - x0) / (30 / 30.0);
                sb.Append(deg).Append("deg -> ").Append(F2(along)).Append(" u/s (").Append(F2(along / 3.8)).Append(" of top, input along-share ").Append(F2(Math.Cos(r))).Append("); ");
            }
            sb.Append('\n');
            // (e) convex corner: walk along +x past the end of the wall, hugging it, then turn the corner (input +x,+z)
            int endX = (int)ox + 4;
            Put(w, 0, endX - 3, wz - 0.46);
                        for (int t = 0; t < 60; t++) { In(w, 0, 0.7, 0.7); SimStep.Step(w); }
            sb.Append("(e) convex corner: hugging the wall with input (0.7,0.7) from 3 u before its end: after 2 s at x=").Append(F2(w.X[0] - endX)).Append(" z=").Append(F2(w.Z[0] - wz)).Append(" (past the end and round the corner when z > 1)\n");
            foreach (double off in new[] { 0.05, 0.15, 0.3, 0.4 })
            {
                // a single wall cell (pillar); walk +x with the body overlapping its side by `off`
                World w3 = NewW(ClassId.Warrior); Bench(w3, new[] { 0 });
                OpenSpot(w3, 12, w3.Map.Hill.X, w3.Map.Hill.Z, out double qx, out double qz);
                int pc = (int)Math.Floor(qx), pz = (int)Math.Floor(qz);
                w3.Map.Cells[Idx(w3, pc, pz)] = Cell.Wall;
                double r0 = Collide.ActorRadius(w3, 0);
                Put(w3, 0, pc - 3, pz - r0 + off);
                int t2 = -1; for (int t = 1; t <= 90; t++) { In(w3, 0, 1, 0); SimStep.Step(w3); if (t2 < 0 && w3.X[0] > pc + 1 + r0) t2 = t; }
                sb.Append("    pillar graze, body overlaps the pillar side by ").Append(off).Append(" u, input straight +x: past it at tick ").Append(t2).Append(" (free run ~24), lateral nudge ").Append(F3(w3.Z[0] - (pz - r0 + off))).Append('\n');
            }
        }
        // (f) tunnelling: dodge and big knock into a 1-cell wall, a LowWall and a block
        {
            foreach (byte cellT in new[] { Cell.Wall, Cell.LowWall, Cell.Barricade })
            {
                World w = NewW(ClassId.Warrior); Bench(w, new[] { 0 });
                OpenSpot(w, 12, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
                int wx = (int)Math.Floor(ox) + 1;
                for (int cz = (int)oz - 3; cz <= (int)oz + 3; cz++) w.Map.Cells[Idx(w, wx, cz)] = cellT;
                Put(w, 0, wx - 0.6, oz);
                for (int t = 0; t < 3; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
                In(w, 0, 1, 0, false, true); SimStep.Step(w);
                for (int t = 0; t < 10; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
                double dodgeX = w.X[0] - wx;
                Put(w, 0, wx - 0.6, oz);
                w.Actors[0].KVx = 60; // 60 u/s knock (about 10x the biggest real one)
                for (int t = 0; t < 30; t++) { In(w, 0, 0, 0); SimStep.Step(w); }
                double knockX = w.X[0] - wx;
                Put(w, 0, wx - 1.5, oz);
                for (int t = 0; t < 10; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
                In(w, 0, 1, 0, true); SimStep.Step(w);
                for (int t = 0; t < 30; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
                double jumpX = w.X[0] - wx;
                sb.Append("(f) cell ").Append(cellT).Append(" one cell thick at x=").Append(wx).Append(": dodge into it ends at x-wall ").Append(F2(dodgeX)).Append(", 60 u/s knock ends ").Append(F2(knockX)).Append(", running jump ends ").Append(F2(jumpX)).Append(" (negative = stopped in front, > 1 = through)\n");
            }
        }
        // (g) bridge mouths: a PLAYER (actor 0, human-driven) walking onto each diagonal span from island-side offsets
        {
            int spans = 0, tries = 0, stuck = 0;
            var sbs = new StringBuilder();
            for (int si = 0; si < BridgeSpans.All.Count; si++)
            {
                BridgeSpan s = BridgeSpans.All[si];
                spans++;
                for (int end = 0; end < 2; end++)
                {
                    double ex = end == 0 ? s.X0 : s.X1, ez = end == 0 ? s.Z0 : s.Z1;
                    double ux = end == 0 ? s.Ux : -s.Ux, uz = end == 0 ? s.Uz : -s.Uz; // into the span
                    for (int a = -3; a <= 3; a++)
                    {
                        foreach (double back in new[] { 1.5, 2.5 })
                        {
                            World w = NewW(ClassId.Warrior); Bench(w, new[] { 0 });
                            double ang = a * 20 * Math.PI / 180;
                            double bx = -(ux * Math.Cos(ang) - uz * Math.Sin(ang)), bz = -(ux * Math.Sin(ang) + uz * Math.Cos(ang));
                            double sx = ex + bx * back, sz = ez + bz * back;
                            if (!Collide.ActorFits(w, sx, sz, Collide.ActorRadius(w, 0))) continue;
                            Put(w, 0, sx, sz);
                            tries++;
                            double tx = ex + ux * 3, tz = ez + uz * 3;
                            int still = 0, maxStill = 0;
                            for (int t = 0; t < 90; t++)
                            {
                                double dx = tx - w.X[0], dz = tz - w.Z[0], l = Math.Sqrt(dx * dx + dz * dz);
                                if (l < 0.3) break;
                                In(w, 0, dx / l, dz / l); SimStep.Step(w);
                                double moved = Math.Sqrt((w.X[0] - w.PrevX[0]) * (w.X[0] - w.PrevX[0]) + (w.Z[0] - w.PrevZ[0]) * (w.Z[0] - w.PrevZ[0]));
                                if (moved < 0.02) { still++; maxStill = Math.Max(maxStill, still); } else still = 0;
                            }
                            double rem = Math.Sqrt((tx - w.X[0]) * (tx - w.X[0]) + (tz - w.Z[0]) * (tz - w.Z[0]));
                            if (rem > 0.6 || maxStill >= 10)
                            {
                                stuck++;
                                sbs.Append("    STUCK span ").Append(si).Append(s.Diagonal ? " diag" : " straight").Append(" end ").Append(end).Append(" approach ").Append(a * 20).Append("deg back ").Append(back)
                                   .Append(": start ").Append(F2(sx)).Append(",").Append(F2(sz)).Append(" stopped at ").Append(F2(w.X[0])).Append(",").Append(F2(w.Z[0])).Append(" still ").Append(maxStill).Append(" ticks, ").Append(F2(rem)).Append(" u short\n");
                            }
                        }
                    }
                }
            }
            sb.Append("(g) bridge mouths, player-steered (rail on), straight-line walk to 3 u inside each end from 7 angles x 2 distances: ").Append(stuck).Append(" snags of ").Append(tries).Append(" walks over ").Append(spans).Append(" spans\n").Append(sbs);
        }
        Save("2-collision.txt", sb);
        return sb.ToString();
    }
}

public static class PACrowd
{
    static readonly long[] T = new long[16];
    static readonly string[] Names = { "bots", "abilities", "MOVEMENT+separation", "spatialHash", "combat", "pickups..respawn misc", "KAIJU (ai+body)", "terrainHeight", "heal/hill/match", "total" };
    static double Now() => Stopwatch.GetTimestamp();
    static void Tick(World w, InputFrame scratch, bool timed, ref long allocMove)
    {
        long t0 = Stopwatch.GetTimestamp();
        double dt = SimStep.Dt(w.Config);
        BotReflex.BeginFills(w);
        BotBrain.FillBotInput(w, 0, scratch); SimInput.ApplyInput(w, 0, scratch);
        BotBrain.BotsThink(w, dt);
        for (int id = 1; id < w.ActorCount; id++) { BotBrain.FillBotInput(w, id, scratch); SimInput.ApplyInput(w, id, scratch); }
        BotReflex.EndFills(w);
        long t1 = Stopwatch.GetTimestamp(); T[0] += t1 - t0;
        if (w.Match.Phase == MatchPhase.Ended) return;
        w.Tick++;
        int n = w.ActorCount;
        for (int i = 0; i < n; i++) { w.PrevX[i] = w.X[i]; w.PrevZ[i] = w.Z[i]; }
        Kaiju k = w.Kaiju; k.PrevX = k.X; k.PrevZ = k.Z;
        Abilities.AbilitiesSystem(w, dt);
        long t2 = Stopwatch.GetTimestamp(); T[1] += t2 - t1;
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        CriticalMass.Sim.Core.Movement.MovementSystem(w, dt);
        allocMove += GC.GetAllocatedBytesForCurrentThread() - a0;
        long t3 = Stopwatch.GetTimestamp(); T[2] += t3 - t2;
        SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, n);
        long t4 = Stopwatch.GetTimestamp(); T[3] += t4 - t3;
        Combat.CombatSystem(w, dt);
        long t5 = Stopwatch.GetTimestamp(); T[4] += t5 - t4;
        Pickups.PickupsSystem(w); MapEvents.EventsSystem(w); Topple.ToppleSystem(w); BuiltPieces.PiecesSystem(w);
        Barricades.BarricadesSystem(w, dt); BuilderWalls.WallsSystem(w); SimResources.ResourcesSystem(w, dt); Corruption.CorruptionSystem(w, dt);
        Roles.HandoffSystem(w); Sources.SourcesSystem(w, dt); Blacksmith.HutSystem(w);
        long t6 = Stopwatch.GetTimestamp(); T[5] += t6 - t5;
        CriticalMass.Sim.Core.Kaiju.KaijuSystem(w, dt);
        long t7 = Stopwatch.GetTimestamp(); T[6] += t7 - t6;
        TerrainHeight.HeightSystem(w);
        long t8 = Stopwatch.GetTimestamp(); T[7] += t8 - t7;
        Stations.StationSystem(w); PingRules.PingSystem(w); Respawn.RespawnSystem(w, dt); Heal.HealSystem(w); Hill.HillSystem(w, dt); CriticalMass.Sim.Core.Match.MatchSystem(w, dt);
        DepthState.EndStep(w);
        w.Match.Time += dt;
        long t9 = Stopwatch.GetTimestamp(); T[8] += t9 - t8;
        T[9] += t9 - t0;
        T[10] = Math.Max(T[10], t9 - t0);
    }

    /// <summary>16v16 bot match, Kaiju from 0:30: crowd overlap and jitter, shiver flags, determinism and per-system cost.</summary>
    public static string Run(int seed, int ticks)
    {
        var sb = new StringBuilder("# crowd + cost: 16v16 Full, seed " + seed + ", Kaiju hatch 0:30, " + ticks + " ticks\n");
        Array.Clear(T, 0, T.Length);
        World w = PA.NewW(ClassId.Warrior, seed, 16, 30);
        World r = PA.NewW(ClassId.Warrior, seed, 16, 30); // the real MatchTick path, untimed
        InputFrame s1 = SimInput.EmptyInput(), s2 = SimInput.EmptyInput();
        var watch = new ShiverWatch();
        int n = w.ActorCount;
        var hx = new double[n, 3]; var hz = new double[n, 3];
        var crowdD2 = new List<double>(); var openD2 = new List<double>();
        double worstOverlap = 0; int worstT = -1; double worstKaiju = 0; int overlapPairs = 0, contactPairs = 0;
        int maxHill = 0, hillTicks20 = 0, crowdRev = 0, crowdSamples = 0;
        double worstKx = 0, worstKz = 0;
        int mismatch = -1; long allocMove = 0, allocWarm = 0;
        double hR = w.Map.Hill.R, hX = w.Map.Hill.X, hZ = w.Map.Hill.Z;
        double kR = w.Config.Physics.KaijuBodyRadius;
        for (int t = 0; t < ticks && w.Match.Phase != MatchPhase.Ended; t++)
        {
            if (t == 600) allocWarm = allocMove;
            Tick(w, s1, true, ref allocMove);
            MatchTick.Advance(r, null, s2);
            watch.Observe(w);
            if (mismatch < 0 && t % 150 == 0 && CriticalMass.Net.StateHash.Of(w) != CriticalMass.Net.StateHash.Of(r)) mismatch = t;
            int onHill = 0;
            for (int i = 0; i < n; i++) { if (!w.Actors[i].Alive) continue; double dx = w.X[i] - hX, dz = w.Z[i] - hZ; if (dx * dx + dz * dz < hR * hR) onHill++; }
            maxHill = Math.Max(maxHill, onHill); if (onHill >= 20) hillTicks20++;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.AirLen > 0) { hx[i, 0] = double.NaN; continue; }
                for (int j = i + 1; j < n; j++)
                {
                    if (!w.Actors[j].Alive || w.Actors[j].AirLen > 0) continue;
                    double dx = w.X[i] - w.X[j], dz = w.Z[i] - w.Z[j];
                    double ov = Collide.ActorRadius(w, i) + Collide.ActorRadius(w, j) - Math.Sqrt(dx * dx + dz * dz);
                    if (ov > -0.05) contactPairs++;
                    if (ov > 0.05) overlapPairs++;
                    if (ov > worstOverlap) { worstOverlap = ov; worstT = w.Tick; }
                }
                if (w.Match.Hatched && w.Kaiju.Hp > 0)
                {
                    double dx = w.X[i] - w.Kaiju.X, dz = w.Z[i] - w.Kaiju.Z;
                    double ov = kR + Collide.ActorRadius(w, i) - Math.Sqrt(dx * dx + dz * dz);
                    if (ov > worstKaiju) { worstKaiju = ov; worstKx = w.X[i]; worstKz = w.Z[i]; }
                }
                // jitter: second difference of position, crowd (on the hill with 20+ bodies) vs open (no body within 2 u)
                bool valid = !double.IsNaN(hx[i, 0]) && !double.IsNaN(hx[i, 1]);
                if (valid)
                {
                    double ddx = w.X[i] - 2 * hx[i, 0] + hx[i, 1], ddz = w.Z[i] - 2 * hz[i, 0] + hz[i, 1];
                    double d2 = Math.Sqrt(ddx * ddx + ddz * ddz);
                    if (d2 < 1.0)
                    {
                        double cx = w.X[i] - hX, cz = w.Z[i] - hZ;
                        bool onH = cx * cx + cz * cz < hR * hR;
                        int near = 0;
                        for (int j = 0; j < n; j++) { if (j == i || !w.Actors[j].Alive) continue; double ex = w.X[j] - w.X[i], ez = w.Z[j] - w.Z[i]; if (ex * ex + ez * ez < 4) near++; }
                        if (onH && onHill >= 20)
                        {
                            crowdD2.Add(d2); crowdSamples++;
                            double v1x = w.X[i] - hx[i, 0], v1z = w.Z[i] - hz[i, 0], v0x = hx[i, 0] - hx[i, 1], v0z = hz[i, 0] - hz[i, 1];
                            if (v1x * v0x + v1z * v0z < 0 && v1x * v1x + v1z * v1z > 1e-4 && v0x * v0x + v0z * v0z > 1e-4) crowdRev++;
                        }
                        else if (near == 0 && w.Actors[i].Moving) openD2.Add(d2);
                    }
                }
                hx[i, 1] = hx[i, 0]; hz[i, 1] = hz[i, 0]; hx[i, 0] = w.X[i]; hz[i, 0] = w.Z[i];
            }
        }
        crowdD2.Sort(); openD2.Sort();
        Func<List<double>, double, double> P = (l, q) => l.Count == 0 ? 0 : l[Math.Min(l.Count - 1, (int)(q * l.Count))];
        double tickUs = 1e6 / Stopwatch.Frequency;
        int nt = w.Tick;
        sb.Append("ticks run ").Append(nt).Append(", hatched ").Append(w.Match.Hatched).Append(", replica vs MatchTick.Advance StateHash: ").Append(mismatch < 0 ? "IDENTICAL every 150 ticks" : "DIFFER at " + mismatch).Append('\n');
        sb.Append("hill crowd: max bodies inside the 6 u hill disc ").Append(maxHill).Append(", ticks with 20+ ").Append(hillTicks20).Append('\n');
        sb.Append("body overlap: worst ").Append(worstOverlap.ToString("F3")).Append(" u at tick ").Append(worstT).Append(", pair-ticks in contact ").Append(contactPairs).Append(", of them overlapping > 0.05 u ").Append(overlapPairs)
          .Append(" (").Append((100.0 * overlapPairs / Math.Max(1, contactPairs)).ToString("F1")).Append("%)\n");
        sb.Append("Kaiju body (r ").Append(kR).Append(") vs fighters: worst overlap ").Append(worstKaiju.ToString("F3")).Append(" u at ").Append(worstKx.ToString("F1")).Append(",").Append(worstKz.ToString("F1")).Append('\n');
        sb.Append("jitter |second difference| u/tick2: hill crowd (20+) n=").Append(crowdD2.Count).Append(" median ").Append(P(crowdD2, 0.5).ToString("F4")).Append(" p95 ").Append(P(crowdD2, 0.95).ToString("F4")).Append(" p99 ").Append(P(crowdD2, 0.99).ToString("F4"))
          .Append(" | open walkers n=").Append(openD2.Count).Append(" median ").Append(P(openD2, 0.5).ToString("F4")).Append(" p95 ").Append(P(openD2, 0.95).ToString("F4")).Append(" p99 ").Append(P(openD2, 0.99).ToString("F4")).Append('\n');
        sb.Append("hill crowd velocity reversals: ").Append(crowdRev).Append(" of ").Append(crowdSamples).Append(" body-ticks (").Append((100.0 * crowdRev / Math.Max(1, crowdSamples)).ToString("F2")).Append("%)\n");
        sb.Append("shiver flags: ").Append(watch.Count).Append(" (dither ").Append(watch.CountByKind[0]).Append(", reversals ").Append(watch.CountByKind[1]).Append(", pressed ").Append(watch.CountByKind[2]).Append(")\n");
        foreach (ShiverFlag f in watch.Flags) sb.Append("    ").Append(ShiverWatch.Format(f)).Append('\n');
        sb.Append("cost per tick (Editor, Mono, mean us; share of the sim step):\n");
        for (int k = 0; k < 10; k++) sb.Append("    ").Append(Names[k]).Append(": ").Append((T[k] * tickUs / nt).ToString("F1")).Append(" us (").Append((100.0 * T[k] / Math.Max(1, T[9])).ToString("F1")).Append("%)\n");
        sb.Append("    worst tick ").Append((T[10] * tickUs / 1000).ToString("F2")).Append(" ms\n");
        sb.Append("allocations inside MovementSystem after 600 warm-up ticks: ").Append(allocMove - allocWarm).Append(" B (whole run ").Append(allocMove).Append(" B)\n");
        File.WriteAllText(PA.Out + "data/crowd-seed" + seed + ".txt", sb.ToString());
        return sb.ToString();
    }
}

public static class PAV
{
    static int Idx(World w, int cx, int cz) => cz * w.Map.Width + cx;
    static string F2(double v) => v.ToString("F2");
    static int Lvl(World w, int id) => TerrainHeight.LevelAt(w.Map, w.X[id], w.Z[id], Collide.ActorRadius(w, id));
    static World Solo(ClassId c, out double ox, out double oz)
    {
        World w = PA.NewW(c); PA.Bench(w, new[] { 0 });
        PA.OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out ox, out oz);
        PA.Put(w, 0, ox, oz);
        return w;
    }
    static void Run(World w, int ticks, double mx, double mz) { for (int t = 0; t < ticks; t++) { PA.In(w, 0, mx, mz); SimStep.Step(w); } }

    public static string Vertical()
    {
        var sb = new StringBuilder("# 3. vertical (sim; 1 level = TerrainHeightConfig.StepU 0.45 u in the view)\n");
        // (a) jump per class at full speed and from a standstill
        sb.Append("(a) jump: class | air ticks | distance at full speed u | from standstill u | apex u (view)\n");
        for (int c = 0; c < 6; c++)
        {
            World w = Solo((ClassId)c, out double ox, out double oz); PA.Put(w, 0, ox - 6, oz);
            Run(w, 10, 1, 0);
            double x0 = w.X[0]; PA.In(w, 0, 1, 0, true); SimStep.Step(w);
            int air = w.Actors[0].AirLen; double x1 = w.Actors[0].AirToX;
            World w2 = Solo((ClassId)c, out ox, out oz); Run(w2, 5, 0, 0); PA.In(w2, 0, 0, 0, true); SimStep.Step(w2);
            sb.Append("    ").Append((ClassId)c).Append(" | ").Append(air).Append(" | ").Append(F2(x1 - x0)).Append(" | ").Append(F2(w2.Actors[0].AirToX - w2.X[0])).Append(" (air ").Append(w2.Actors[0].AirLen).Append(") | ").Append(w.Config.Jump.ApexUnits).Append('\n');
        }
        // arc shape
        {
            World w = Solo(ClassId.Warrior, out double ox, out double oz); PA.In(w, 0, 0, 0, true); SimStep.Step(w);
            sb.Append("    arc lift per tick (u): ");
            for (int k = 1; k <= 18; k++) sb.Append(F2(Jump.JumpLift(w.Config, w.Actors[0], k / 18.0))).Append(' ');
            sb.Append(" (symmetric parabola, equivalent gravity ").Append(F2(8 * 0.8 / 0.36)).Append(" u/s2; no air control: move input ignored in the air)\n");
        }
        // air control check
        {
            World w = Solo(ClassId.Warrior, out double ox, out double oz); PA.Put(w, 0, ox - 4, oz); Run(w, 10, 1, 0);
            PA.In(w, 0, 1, 0, true); SimStep.Step(w); double toX = w.Actors[0].AirToX;
            for (int t = 0; t < 17; t++) { PA.In(w, 0, -1, 1); SimStep.Step(w); }
            sb.Append("    air control: steering hard back mid-air lands at dx ").Append(F2(w.X[0] - toX)).Append(", dz ").Append(F2(w.Z[0] - oz)).Append(" from the planned landing\n");
        }
        // jump into a wall: shortened / hop in place
        {
            World w = Solo(ClassId.Warrior, out double ox, out double oz);
            int wx = (int)ox + 1; for (int cz = (int)oz - 3; cz <= (int)oz + 3; cz++) w.Map.Cells[Idx(w, wx, cz)] = Cell.Wall;
            PA.Put(w, 0, wx - 2.5, oz); Run(w, 8, 1, 0); PA.In(w, 0, 1, 0, true); SimStep.Step(w);
            sb.Append("    running jump at a tall wall 1.5 u ahead: air ").Append(w.Actors[0].AirLen).Append(" ticks, lands ").Append(F2(w.Actors[0].AirToX - wx)).Append(" from the wall face line\n");
        }
        // (b) coyote time: floor removed under a standing body, jump pressed k ticks later
        sb.Append("(b) coyote: floor cell removed under a standing body, jump pressed k ticks later -> ");
        for (int k = 0; k <= 6; k++)
        {
            World w = Solo(ClassId.Warrior, out double ox, out double oz);
            int cx = (int)Math.Floor(ox), cz = (int)Math.Floor(oz); PA.Put(w, 0, cx + 0.5, cz + 0.5); Run(w, 3, 0, 0);
            w.Map.Cells[Idx(w, cx, cz)] = Cell.Void;
            bool jumped = false, fell = false;
            for (int t = 0; t <= 8; t++)
            {
                PA.In(w, 0, 0, 0, t == k); SimStep.Step(w);
                if (w.Actors[0].AirLen > 0) jumped = true;
                if (w.Actors[0].StandUp > 0 && !jumped) { fell = true; break; }
            }
            sb.Append("k=").Append(k).Append(jumped ? " JUMP" : fell ? " fell" : " walked").Append("; ");
        }
        sb.Append('\n');
        // fall: what a fall costs
        {
            World w = Solo(ClassId.Warrior, out double ox, out double oz);
            int cx = (int)Math.Floor(ox), cz = (int)Math.Floor(oz); PA.Put(w, 0, cx + 0.5, cz + 0.5); Run(w, 3, 0, 0);
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) w.Map.Cells[Idx(w, cx + dx, cz + dz)] = Cell.Void;
            double hp = w.Actors[0].Hp; int t = 0;
            for (; t < 30 && w.Actors[0].StandUp <= 0; t++) { PA.In(w, 0, 0, 0); SimStep.Step(w); }
            sb.Append("    fall into a 3x3 hole: Fall after ").Append(t).Append(" ticks, moved ").Append(F2(Math.Sqrt((w.X[0] - cx - 0.5) * (w.X[0] - cx - 0.5) + (w.Z[0] - cz - 0.5) * (w.Z[0] - cz - 0.5))))
              .Append(" u to the nearest floor (teleport, Prev reset), stand-up ").Append(F2(w.Actors[0].StandUp)).Append(" s, HP lost ").Append(F2(hp - w.Actors[0].Hp)).Append('\n');
        }
        // (c) step-up limits and pits (Lift)
        sb.Append("(c) levels (lift): walking +x into a 3-cell-deep raised strip of L levels:\n");
        foreach (int L in new[] { 1, 2, 3 })
        {
            foreach (bool jump in new[] { false, true })
            {
                World w = Solo(ClassId.Warrior, out double ox, out double oz);
                int wx = (int)ox;
                for (int cz = (int)oz - 5; cz <= (int)oz + 5; cz++) for (int cx = wx; cx < wx + 3; cx++) w.Map.Lift[Idx(w, cx, cz)] = (sbyte)L;
                PA.Put(w, 0, wx - 2, oz + 0.5);
                int onT = -1; double minSp = 9;
                for (int t = 1; t <= 90; t++)
                {
                    PA.In(w, 0, 1, 0, jump && w.X[0] > wx - 1.2 && w.Actors[0].AirLen == 0 && onT < 0); SimStep.Step(w);
                    if (t > 8 && onT < 0) minSp = Math.Min(minSp, PA.Sp(w, 0));
                    if (onT < 0 && Lvl(w, 0) == L) onT = t;
                }
                sb.Append("    L").Append(L).Append(jump ? " with a jump" : " walking").Append(": on top at tick ").Append(onT).Append(" (flat walk reaches it at ~").Append((int)Math.Ceiling((1.45) / 3.8 * 30) + 2).Append("), final x-edge ").Append(F2(w.X[0] - wx)).Append(", level ").Append(Lvl(w, 0)).Append('\n');
            }
        }
        sb.Append("    pits: body standing in the middle of a 3x3 pit of depth D walks out +x:\n");
        foreach (int D in new[] { 1, 2 })
        {
            World w = Solo(ClassId.Warrior, out double ox, out double oz);
            int cx = (int)Math.Floor(ox), cz = (int)Math.Floor(oz);
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) w.Map.Lift[Idx(w, cx + dx, cz + dz)] = (sbyte)(-D);
            PA.Put(w, 0, cx + 0.5, cz + 0.5); Run(w, 3, 0, 0);
            int outT = -1; for (int t = 1; t <= 120 && outT < 0; t++) { PA.In(w, 0, 1, 0); SimStep.Step(w); if (Lvl(w, 0) == 0) outT = t; }
            World w2 = Solo(ClassId.Warrior, out ox, out oz);
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) w2.Map.Lift[Idx(w2, cx + dx, cz + dz)] = (sbyte)(-D);
            PA.Put(w2, 0, cx + 0.5, cz + 0.5); Run(w2, 3, 0, 0);
            int jT = -1; for (int t = 1; t <= 120 && jT < 0; t++) { PA.In(w2, 0, 1, 0, w2.X[0] > cx + 0.9 && w2.Actors[0].AirLen == 0); SimStep.Step(w2); if (Lvl(w2, 0) == 0) jT = t; }
            sb.Append("    depth ").Append(D).Append(": walking out takes ").Append(outT).Append(" ticks, jumping out ").Append(jT).Append(" ticks (flat 1 u walk ~10)\n");
        }
        // (d) island edge and the void
        {
            World w = PA.NewW(ClassId.Warrior); PA.Bench(w, new[] { 0 });
            // walk south from the hill square until the island ends
            double ex = w.Map.Hill.X, ez = w.Map.Hill.Z;
            PA.Put(w, 0, ex, ez);
            for (int t = 0; t < 300; t++) { PA.In(w, 0, 0, 1); SimStep.Step(w); }
            double edgeZ = w.Z[0]; int cellAhead = MapGrid.CellAt(w.Map, w.X[0], w.Z[0] + 0.6);
            PA.In(w, 0, 0, 1, true); SimStep.Step(w); int air = w.Actors[0].AirLen; double land = w.Actors[0].AirToZ;
            for (int t = 0; t < 20; t++) { PA.In(w, 0, 0, 1); SimStep.Step(w); }
            double afterJump = w.Z[0];
            PA.Put(w, 0, w.X[0], edgeZ - 0.5); w.Actors[0].KVz = 40;
            for (int t = 0; t < 30; t++) { PA.In(w, 0, 0, 0); SimStep.Step(w); }
            double afterKnock = w.Z[0];
            PA.Put(w, 0, w.X[0], edgeZ - 0.5); Run(w, 5, 0, 1); PA.In(w, 0, 0, 1, false, true); SimStep.Step(w); Run(w, 10, 0, 1);
            sb.Append("(d) island edge (walking +z from the hill): stops at z ").Append(F2(edgeZ)).Append(" with cell ").Append(cellAhead).Append(" ahead; a jump there: air ").Append(air).Append(" ticks landing dz ").Append(F2(land - edgeZ))
              .Append(", after ").Append(F2(afterJump - edgeZ)).Append("; a 40 u/s knock outward ends dz ").Append(F2(afterKnock - edgeZ)).Append("; a dodge outward ends dz ").Append(F2(w.Z[0] - edgeZ)).Append(" (0 = held at the edge: no ring-outs)\n");
        }
        PA_Save("3-vertical.txt", sb);
        return sb.ToString();
    }
    static void PA_Save(string n, StringBuilder sb) { Directory.CreateDirectory(PA.Out + "data"); File.WriteAllText(PA.Out + "data/" + n, sb.ToString()); }

    public static string Hits()
    {
        var sb = new StringBuilder("# 4. hits (sim)\n");
        GameConfig cfg0 = new GameConfig();
        var strikes = cfg0.Depth.CombatFeel.Strikes;
        // (a) melee knock matrix through Knock.Push on open ground: travelled distance and ticks
        sb.Append("(a) melee knock, attacker row -> victim column: travelled u / ticks moving (Strike.Knock / victim Mass, decay 0.82/tick)\n           ");
        for (int v = 0; v < 6; v++) sb.Append(((ClassId)v).ToString().PadRight(14));
        sb.Append('\n');
        for (int a = 0; a < 6; a++)
        {
            sb.Append("    ").Append(((ClassId)a).ToString().PadRight(12)).Append(strikes[a].Knock.ToString("F1")).Append(' ');
            for (int v = 0; v < 6; v++)
            {
                World w = PA.NewW(ClassId.Warrior); PA.Bench(w, new[] { 16 }); PA.SetCls(w, 16, (ClassId)v);
                PA.OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz); PA.Put(w, 16, ox - 3, oz);
                double eff = Knock.Effective(w, 16, strikes[a].Knock, ox - 4, oz);
                Knock.Push(w, 16, 0, 1, 0, eff, 0, 0, DamageCause.Melee);
                int mt = 0; for (int t = 0; t < 60; t++) { PA.In(w, 16, 0, 0); double x = w.X[16]; SimStep.Step(w); if (w.X[16] != x) mt = t + 1; }
                sb.Append((F2(w.X[16] - (ox - 3)) + " / " + mt).PadRight(14));
            }
            sb.Append('\n');
        }
        // (b) real swings: each class hits a Warrior and a Farmer standing still 1.0 u in front
        sb.Append("(b) real swings (attack pressed until the hit lands): attacker -> victim: knock u, stun ticks, damage, ticks press-to-hit\n");
        for (int a = 0; a < 6; a++)
        {
            foreach (int v in new[] { 0, 1 })
            {
                World w = PA.NewW((ClassId)a); PA.Bench(w, new[] { 0, 16 }); PA.SetCls(w, 16, (ClassId)v);
                PA.OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
                PA.Put(w, 0, ox - 1, oz); PA.Put(w, 16, ox, oz);
                double hp = w.Actors[16].Hp; int hitT = -1, stun = 0; double x0 = w.X[16];
                for (int t = 1; t <= 60; t++)
                {
                    PA.In(w, 0, 0, 0, false, false, hitT < 0, w.X[16], w.Z[16]); PA.In(w, 16, 0, 0); SimStep.Step(w);
                    if (hitT < 0 && w.Actors[16].Hp < hp) { hitT = t; }
                    stun = Math.Max(stun, w.Actors[16].HitStunLen);
                }
                sb.Append("    ").Append((ClassId)a).Append(" -> ").Append((ClassId)v).Append(": ").Append(F2(w.X[16] - x0)).Append(" u, stun ").Append(stun).Append(", dmg ").Append(F2(hp - w.Actors[16].Hp)).Append(", hit at tick ").Append(hitT).Append('\n');
            }
        }
        // (c) knock (2.4 u, the slam centre) toward a wall, a short wall, the island edge, a 1- and a 2-deep pit, a raised block
        sb.Append("(c) a 2.4 u knock (slam centre) from 0.6 u away toward: ");
        foreach (string kind in new[] { "wall", "lowwall", "pit1", "pit2", "raised1", "raised2" })
        {
            World w = PA.NewW(ClassId.Warrior); PA.Bench(w, new[] { 16 });
            PA.OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            int wx = (int)ox;
            for (int cz = (int)oz - 4; cz <= (int)oz + 4; cz++) for (int cx = wx; cx < wx + 3; cx++)
            {
                int i = Idx(w, cx, cz);
                if (kind == "wall" && cx == wx) w.Map.Cells[i] = Cell.Wall;
                if (kind == "lowwall" && cx == wx) w.Map.Cells[i] = Cell.LowWall;
                if (kind == "pit1") w.Map.Lift[i] = -1;
                if (kind == "pit2") w.Map.Lift[i] = -2;
                if (kind == "raised1") w.Map.Lift[i] = 1;
                if (kind == "raised2") w.Map.Lift[i] = 2;
            }
            PA.Put(w, 16, wx - 0.6, oz + 0.5); PA.In(w, 16, 0, 0); SimStep.Step(w);
            Knock.Push(w, 16, 0, 1, 0, 2.4, 0, 0, DamageCause.Melee);
            for (int t = 0; t < 40; t++) { PA.In(w, 16, 0, 0); SimStep.Step(w); }
            sb.Append(kind).Append(" -> ends ").Append(F2(w.X[16] - wx)).Append(" u past the edge line, level ").Append(Lvl(w, 16)).Append("; ");
        }
        sb.Append('\n');
        // (d) TNT: thrown at a point; enemies standing 0 .. 3 u from it
        {
            World w = PA.NewW(ClassId.Demolitions);
            var keep = new List<int> { 0 }; for (int k = 0; k < 7; k++) keep.Add(16 + k);
            PA.Bench(w, keep);
            PA.OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            PA.Put(w, 0, ox - 5, oz);
            double[] d = { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0 };
            var hp = new double[7]; var x0 = new double[7];
            for (int k = 0; k < 7; k++) { PA.Put(w, 16 + k, ox + d[k] * 0.0 + (k % 2 == 0 ? 0 : 0), oz); }
            for (int k = 0; k < 7; k++) { double ang = k * 2 * Math.PI / 7; PA.Put(w, 16 + k, ox + d[k] * Math.Cos(ang), oz + d[k] * Math.Sin(ang)); hp[k] = w.Actors[16 + k].Hp; x0[k] = w.X[16 + k]; }
            var z0 = new double[7]; for (int k = 0; k < 7; k++) z0[k] = w.Z[16 + k];
            int res = Demolitions.ThrowTnt(w, 0, ox, oz);
            int boomT = -1; var maxStun = new int[7];
            for (int t = 1; t <= 150; t++)
            {
                PA.In(w, 0, 0, 0); for (int k = 0; k < 7; k++) { PA.In(w, 16 + k, 0, 0); }
                // hold the victims still until the blast (they must not drift), then free them
                if (boomT < 0) for (int k = 0; k < 7; k++) { w.X[16 + k] = x0[k]; w.Z[16 + k] = z0[k]; }
                SimStep.Step(w);
                for (int k = 0; k < 7; k++) maxStun[k] = Math.Max(maxStun[k], w.Actors[16 + k].HitStunLen);
                if (boomT < 0 && w.Actors[16].Hp < hp[0]) boomT = t;
            }
            sb.Append("(d) TNT thrown 5 u (ThrowTnt result ").Append(res).Append("), blast at tick ").Append(boomT).Append(" after the throw; per victim distance -> damage, knock u, stun ticks:\n    ");
            for (int k = 0; k < 7; k++)
            {
                double moved = Math.Sqrt((w.X[16 + k] - x0[k]) * (w.X[16 + k] - x0[k]) + (w.Z[16 + k] - z0[k]) * (w.Z[16 + k] - z0[k]));
                sb.Append(d[k]).Append(" u: ").Append(F2(hp[k] - w.Actors[16 + k].Hp)).Append(" dmg, ").Append(F2(moved)).Append(" u, ").Append(maxStun[k]).Append(" | ");
            }
            sb.Append("\n    thrower (5 u away) hp lost ").Append(F2(w.Actors[0].MaxHp - w.Actors[0].Hp)).Append('\n');
        }
        PA_Save("4-hits.txt", sb);
        return sb.ToString();
    }
}

public static class PAK
{
    static int Idx(World w, int cx, int cz) => cz * w.Map.Width + cx;
    static string F2(double v) => v.ToString("F2");
    /// <summary>A whole 16v16 match with the Kaiju out at 0:30: its turning, walking, shoves and the knocks it gives.</summary>
    public static string KaijuMatch(int seed, int ticks)
    {
        var sb = new StringBuilder("# 5. Kaiju in a 16v16 bot match, seed " + seed + ", game hatch time (240 s), " + ticks + " ticks\n");
        World w = PA.NewW(ClassId.Warrior, seed, 16, 240);
        InputFrame s = SimInput.EmptyInput();
        int head = 0; double maxTurn = 0, sumTurn = 0; int turnN = 0, flips = 0; double lastF = 0, lastDF = 0; bool have = false;
        double maxSp = 0; int shoves = 0, shoveTicks = 0; double maxShove = 0;
        var knock = new List<string>(); int kKnocks = 0; double kEffMax = 0, kEffSum = 0; int kStun = 0, kStunMax = 0;
        int bKnocks = 0; double bEffMax = 0; int falls = 0, deathsKaiju = 0, deathsBlast = 0, kaijuHits = 0; double kaijuHitDmg = 0;
        double onHillTicks = 0, bodiesNearSum = 0; int fastT = 0, walkT = 0, big45 = 0, big90 = 0; var fastLog = new StringBuilder();
        for (int t = 0; t < ticks && w.Match.Phase != MatchPhase.Ended; t++)
        {
            MatchTick.Advance(w, null, s);
            for (; head < w.EventHead; head++)
            {
                SimEvent e = w.Events[head % World.EventCapacity];
                if (e.Type == SimEventType.Knock && e.B == Damage.KaijuSource) { kKnocks++; kEffMax = Math.Max(kEffMax, e.Impulse); kEffSum += e.Impulse; }
                if (e.Type == SimEventType.Knock && e.Cause == DamageCause.Explosion) { bKnocks++; bEffMax = Math.Max(bEffMax, e.Impulse); }
                if (e.Type == SimEventType.HitStun && e.B == Damage.KaijuSource) { kStun++; kStunMax = Math.Max(kStunMax, (int)e.Value); }
                if (e.Type == SimEventType.BodyShove) { shoveTicks++; shoves += (int)e.Value; maxShove = Math.Max(maxShove, e.Impulse); }
                if (e.Type == SimEventType.Fall) falls++;
                if (e.Type == SimEventType.Death && e.B == Damage.KaijuSource) deathsKaiju++;
                if (e.Type == SimEventType.Death && e.Cause == DamageCause.Explosion) deathsBlast++;
            }
            if (!w.Match.Hatched || w.Kaiju.State == KaijuState.Egg) continue;
            Kaiju k = w.Kaiju;
            double sp = Math.Sqrt((k.X - k.PrevX) * (k.X - k.PrevX) + (k.Z - k.PrevZ) * (k.Z - k.PrevZ)) * 30;
            maxSp = Math.Max(maxSp, sp); if (sp > 3.2) { fastT++; if (fastLog.Length < 900) fastLog.Append(" t" + w.Tick + ":" + F2(sp) + "u/s@" + F2(k.X) + "," + F2(k.Z)); } walkT++;
            if (have)
            {
                double df = k.Facing - lastF; while (df > Math.PI) df -= 2 * Math.PI; while (df < -Math.PI) df += 2 * Math.PI;
                double ad = Math.Abs(df) * 180 / Math.PI;
                if (ad > 45) big45++; if (ad > 90) big90++;
                if (ad > 1e-6) { maxTurn = Math.Max(maxTurn, ad); sumTurn += ad; turnN++; if (lastDF * df < 0 && ad > 2) flips++; lastDF = df; }
            }
            lastF = k.Facing; have = true;
            double hx = k.X - w.Map.Hill.X, hz = k.Z - w.Map.Hill.Z;
            if (hx * hx + hz * hz < w.Map.Hill.R * w.Map.Hill.R)
            {
                onHillTicks++;
                int near = 0; for (int i = 0; i < w.ActorCount; i++) { if (!w.Actors[i].Alive) continue; double dx = w.X[i] - k.X, dz = w.Z[i] - k.Z; if (dx * dx + dz * dz < 9) near++; }
                bodiesNearSum += near;
            }
        }
        sb.Append("Kaiju walk: max speed ").Append(F2(maxSp)).Append(" u/s (config ").Append(w.Config.Kaiju.MoveSpeed).Append("), facing change max ").Append(F2(maxTurn)).Append(" deg/tick, mean ").Append(F2(turnN > 0 ? sumTurn / turnN : 0))
          .Append(" deg/tick over ").Append(turnN).Append(" turning ticks, direction flips (sign change > 2 deg) ").Append(flips).Append(", turns > 45 deg in ONE tick ").Append(big45).Append(", > 90 deg ").Append(big90).Append('\n');
        sb.Append("ticks moving faster than 3.2 u/s: ").Append(fastT).Append(" of ").Append(walkT).Append(" hatched ticks:").Append(fastLog).Append('\n');
        sb.Append("body shoves: ").Append(shoveTicks).Append(" ticks with a shove, ").Append(shoves).Append(" fighter-shoves, largest single shove ").Append(F2(maxShove)).Append(" u in one tick\n");
        sb.Append("Kaiju knocks: ").Append(kKnocks).Append(", eff mean ").Append(F2(kKnocks > 0 ? kEffSum / kKnocks : 0)).Append(" u, max ").Append(F2(kEffMax)).Append(" u; Kaiju stuns ").Append(kStun).Append(" (longest ").Append(kStunMax).Append(" ticks); kills by the Kaiju ").Append(deathsKaiju).Append('\n');
        sb.Append("blast knocks (TNT, mines): ").Append(bKnocks).Append(" (max ").Append(F2(bEffMax)).Append(" u); blast kills ").Append(deathsBlast).Append("; falls (teleport out of a hole) ").Append(falls).Append('\n');
        sb.Append("Kaiju on the hill ").Append(F2(onHillTicks / 30)).Append(" s, bodies within 3 u of it there on average ").Append(F2(onHillTicks > 0 ? bodiesNearSum / onHillTicks : 0)).Append('\n');
        File.WriteAllText(PA.Out + "data/5-kaiju-match-seed" + seed + ".txt", sb.ToString());
        return sb.ToString();
    }

    /// <summary>The Kaiju body scripted into a fighter standing in front of a wall (pin), and into a fighter in a 2-cell corridor.</summary>
    public static string Trap()
    {
        var sb = new StringBuilder("# 5b. Kaiju body vs a fighter (scripted Kaiju path, 2.6 u/s)\n");
        foreach (string mode in new[] { "open", "wall", "corridor2", "corner" })
        {
            World w = PA.NewW(ClassId.Warrior, 4242, 16, 1e6); PA.Bench(w, new[] { 0 });
            PA.OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            w.ForceHatch = true; InputFrame s = SimInput.EmptyInput();
            for (int t = 0; t < 40 && !w.Match.Hatched; t++) { PA.In(w, 0, 0, 0); SimStep.Step(w); }
            int wx = (int)ox + 2;
            if (mode != "open") for (int cz = (int)oz - 5; cz <= (int)oz + 5; cz++) w.Map.Cells[Idx(w, wx, cz)] = Cell.Wall;
            if (mode == "corridor2") for (int cx = (int)ox - 6; cx <= wx; cx++) { w.Map.Cells[Idx(w, cx, (int)oz - 1)] = Cell.Wall; w.Map.Cells[Idx(w, cx, (int)oz + 2)] = Cell.Wall; }
            if (mode == "corner") for (int cx = (int)ox - 6; cx <= wx; cx++) w.Map.Cells[Idx(w, cx, (int)oz + 2)] = Cell.Wall;
            double fx = wx - 0.5, fz = oz + 0.5;
            PA.Put(w, 0, fx, fz);
            Kaiju k = w.Kaiju; double kx = fx - 4, kz = fz;
            double minD = 99; int inside = 0, stillTicks = 0; double kBack = 0; int escapeT = -1;
            for (int t = 0; t < 120; t++)
            {
                kx = Math.Min(kx + 2.6 / 30, wx - 0.2);
                k.X = kx; k.Z = kz; k.PrevX = kx; k.PrevZ = kz; k.TargetId = -1;
                double px = w.X[0], pz = w.Z[0];
                // the fighter tries to escape sideways (+z) after tick 30
                PA.In(w, 0, 0, t >= 30 ? -1 : 0); SimStep.Step(w);
                double dx = w.X[0] - w.Kaiju.X, dz = w.Z[0] - w.Kaiju.Z, d = Math.Sqrt(dx * dx + dz * dz);
                minD = Math.Min(minD, d);
                if (d < w.Config.Physics.KaijuBodyRadius) inside++;
                if (t >= 30 && Math.Abs(w.Z[0] - pz) < 0.01 && Math.Abs(w.X[0] - px) < 0.01) stillTicks++;
                kBack = Math.Max(kBack, kx - w.Kaiju.X);
                if (t >= 30 && escapeT < 0 && d > w.Config.Physics.KaijuBodyRadius + 0.45 + 0.5) escapeT = t - 30;
            }
            sb.Append(mode).Append(": closest centre distance ").Append(F2(minD)).Append(" u (body 1.3 + fighter 0.45 = 1.75 contact), ticks with the centre INSIDE the body ").Append(inside)
              .Append(", fighter pinned still while pressing away ").Append(stillTicks).Append(" ticks, the Kaiju shoved back up to ").Append(F2(kBack)).Append(" u, escaped (0.5 u clear) ").Append(escapeT).Append(" ticks after trying, fighter HP ").Append(F2(w.Actors[0].Hp)).Append('\n');
        }
        File.WriteAllText(PA.Out + "data/5b-kaiju-trap.txt", sb.ToString());
        return sb.ToString();
    }
}

public static class PAW
{
    static int Idx(World w, int cx, int cz) => cz * w.Map.Width + cx;
    static string F2(double v) => v.ToString("F2");
    public static string WorldPhys()
    {
        var sb = new StringBuilder("# 6. world (sim)\n");
        // (a) TNT on a raised 3x3 stack (level 2) with an enemy standing on top, and on short / tall wall cells
        {
            World w = PA.NewW(ClassId.Demolitions); PA.Bench(w, new[] { 0, 16 });
            PA.OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            int cx = (int)ox, cz = (int)oz;
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) w.Map.Lift[Idx(w, cx + dx, cz + dz)] = 2;
            int lw = Idx(w, cx + 3, cz), bw = Idx(w, cx + 3, cz + 1);
            w.Map.Cells[lw] = Cell.LowWall; w.Map.Cells[bw] = Cell.Barricade;
            double lwHp0 = w.Map.WallHp != null ? w.Map.WallHp[lw] : -1, bwHp0 = w.Map.WallHp != null ? w.Map.WallHp[bw] : -1;
            PA.Put(w, 0, ox - 5, oz); PA.Put(w, 16, cx + 0.5, cz + 0.5);
            int r = Demolitions.ThrowTnt(w, 0, cx + 1.5, cz + 0.5);
            int lvBefore = TerrainHeight.LevelAt(w.Map, w.X[16], w.Z[16], 0.45);
            for (int t = 0; t < 90; t++) { PA.In(w, 0, 0, 0); PA.In(w, 16, 0, 0); SimStep.Step(w); }
            sb.Append("(a) TNT (result ").Append(r).Append(") at the edge of a level-2 3x3 stack with an enemy on top: stack levels after ");
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) sb.Append(w.Map.Lift[Idx(w, cx + dx, cz + dz)]).Append(' ');
            sb.Append("; enemy level ").Append(lvBefore).Append(" -> ").Append(TerrainHeight.LevelAt(w.Map, w.X[16], w.Z[16], 0.45)).Append(", alive ").Append(w.Actors[16].Alive)
              .Append("; short wall 1.5 u from the blast: cell ").Append(w.Map.Cells[lw]).Append(" hp ").Append(lwHp0).Append(" -> ").Append(w.Map.WallHp != null ? w.Map.WallHp[lw] : -1)
              .Append("; tall wall: cell ").Append(w.Map.Cells[bw]).Append(" hp ").Append(bwHp0).Append(" -> ").Append(w.Map.WallHp != null ? w.Map.WallHp[bw] : -1).Append('\n');
        }
        // (b) carry speed
        {
            sb.Append("(b) carry: top speed (u/s) carrying ");
            foreach (CarryKind kind in new[] { CarryKind.None, CarryKind.Biomass, CarryKind.Ore, CarryKind.Corruption })
            {
                World w = PA.NewW(ClassId.Warrior); PA.Bench(w, new[] { 0 });
                PA.OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz); PA.Put(w, 0, ox - 6, oz);
                w.Actors[0].Carry = kind; w.Actors[0].CarryAmount = kind == CarryKind.None ? 0 : 1;
                for (int t = 0; t < 20; t++) { PA.In(w, 0, 1, 0); SimStep.Step(w); }
                sb.Append(kind).Append(' ').Append(F2(PA.Sp(w, 0))).Append("; ");
            }
            sb.Append('\n');
        }
        // (c) determinism: the same seed twice, independent worlds, 3600 ticks
        {
            World a = PA.NewW(ClassId.Warrior, 31337, 16, 240), b = PA.NewW(ClassId.Warrior, 31337, 16, 240);
            InputFrame s1 = SimInput.EmptyInput(), s2 = SimInput.EmptyInput(); int diff = -1;
            for (int t = 0; t < 3600; t++) { MatchTick.Advance(a, null, s1); MatchTick.Advance(b, null, s2); if (diff < 0 && t % 300 == 299 && CriticalMass.Net.StateHash.Of(a) != CriticalMass.Net.StateHash.Of(b)) diff = t; }
            sb.Append("(c) same seed (31337) twice, 3600 ticks, StateHash every 300 ticks: ").Append(diff < 0 ? "IDENTICAL, final " + CriticalMass.Net.StateHash.Of(a).ToString("X16") : "DIFFER at " + diff).Append('\n');
        }
        File.WriteAllText(PA.Out + "data/6-world.txt", sb.ToString());
        return sb.ToString();
    }
}
