using System;
using System.Collections.Generic;
using System.Diagnostics;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b16 physics fixes (physics audit 2026-11-21, ~/cm-incidents/physics-audit-2026-11-21/PHYSICS-AUDIT.md). Every test
    /// rebuilds the audit's own scenario (tools/physics-audit/PA.cs on audit/physics-2026-11-21), runs it with
    /// DepthConfig.PhysicsFeel.Enabled off (before: the audited number) and on (after), prints one JSON line
    /// {"item":..,"before":..,"after":..} and asserts the after number against the stated target. Full rules
    /// (GameConfig.CreateDefault); Classic and PhysicsOnly never see the rule (their golden rows are pinned elsewhere).
    /// </summary>
    [TestFixture]
    public sealed class PhysicsFeelTests
    {
        // ------------------------------------------------------------------------------------------------ helpers (PA.cs)

        private static World NewW(bool on, ClassId cls, int seed = 4242, int team = 16, double hatch = 1e6, Action<PhysicsFeelConfig> tweak = null)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            cfg.HatchSeconds = hatch;
            cfg.Depth.PhysicsFeel.Enabled = on;
            tweak?.Invoke(cfg.Depth.PhysicsFeel);
            return World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = team, HumanTeam = TeamId.Azure, HumanClass = cls, Config = cfg });
        }

        private static void Put(World w, int id, double x, double z)
        {
            w.X[id] = x; w.Z[id] = z; w.PrevX[id] = x; w.PrevZ[id] = z; w.Vx[id] = 0; w.Vz[id] = 0;
            Actor a = w.Actors[id]; a.KVx = 0; a.KVz = 0; a.HitStunTicks = 0;
        }

        private static void Bench(World w, ICollection<int> keep)
        {
            for (int id = 0; id < w.ActorCount; id++)
            {
                Actor a = w.Actors[id];
                if (keep.Contains(id)) { a.Alive = true; a.Hp = a.MaxHp; a.RespawnIn = 0; a.StandUp = 0; a.SpawnProtect = 0; continue; }
                a.Alive = false; a.Hp = 0; a.RespawnIn = 1e9; a.Moving = false; w.X[id] = -50 - id; w.Z[id] = -50; w.PrevX[id] = w.X[id]; w.PrevZ[id] = -50;
            }
        }

        private static void SetCls(World w, int id, ClassId c) { Actor a = w.Actors[id]; a.Cls = c; a.MaxHp = w.Config.Class(c).MaxHp; a.Hp = a.MaxHp; }

        private static int Idx(World w, int cx, int cz) => cz * w.Map.Width + cx;

        private static bool Flat(World w, int cx, int cz)
        {
            if (cx < 0 || cz < 0 || cx >= w.Map.Width || cz >= w.Map.Height) return false;
            int i = Idx(w, cx, cz);
            return w.Map.Cells[i] == Cell.Floor && (w.Map.Lift == null || w.Map.Lift[i] == 0);
        }

        /// <summary>Centre of an n x n all-floor level-0 square, nearest to (nearX, nearZ).</summary>
        private static void OpenSpot(World w, int n, double nearX, double nearZ, out double x, out double z)
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
            Assert.Less(best, 1e18, "an open square exists");
        }

        private static readonly InputFrame F = SimInput.EmptyInput();

        private static void In(World w, int id, double mx, double mz, bool jump = false, bool dodge = false)
        {
            F.MoveX = mx; F.MoveZ = mz; F.Jump = jump; F.Dodge = dodge; F.Attack = false; F.Ability = false; F.Interact = false; F.Terraform = false; F.TerraformSwitch = false;
            F.AimX = double.NaN; F.AimZ = double.NaN;
            SimInput.ApplyInput(w, id, F);
            w.Inputs[id].Dodge = dodge;
        }

        private static string N(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

        private static void Json(string item, string before, string after, string target)
        {
            Console.WriteLine("{\"item\":\"" + item + "\",\"before\":" + before + ",\"after\":" + after + ",\"target\":\"" + target + "\"}");
        }

        private static void Hatch(World w)
        {
            w.ForceHatch = true;
            for (int t = 0; t < 60 && !w.Match.Hatched; t++) { In(w, 0, 0, 0); SimStep.Step(w); }
            Assert.IsTrue(w.Match.Hatched, "the Kaiju hatched");
        }

        // ------------------------------------------------------------------------------------------------ [1] dead-end pin

        private struct PinResult { public double MaxStep; public int Inside; public double MinD; }

        /// <summary>The audit's 5b corridor2 trap: a fighter in a 2-cell dead end, the Kaiju scripted into it at 2.6 u/s.</summary>
        private static PinResult Pin(bool on)
        {
            World w = NewW(on, ClassId.Warrior); Bench(w, new[] { 0 });
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            Hatch(w);
            int wx = (int)ox + 2;
            for (int cz = (int)oz - 5; cz <= (int)oz + 5; cz++) w.Map.Cells[Idx(w, wx, cz)] = Cell.Wall;
            for (int cx = (int)ox - 6; cx <= wx; cx++) { w.Map.Cells[Idx(w, cx, (int)oz - 1)] = Cell.Wall; w.Map.Cells[Idx(w, cx, (int)oz + 2)] = Cell.Wall; }
            double fx = wx - 0.5, fz = oz + 0.5;
            Put(w, 0, fx, fz);
            Kaiju k = w.Kaiju;
            k.X = fx - 4; k.Z = fz; k.PrevX = k.X; k.PrevZ = k.Z;
            double body = w.Config.Physics.KaijuBodyRadius;
            var r = new PinResult { MinD = 99 };
            for (int t = 0; t < 120; t++)
            {
                // The script walks the Kaiju on from where the sim left it (a give-way shows), toward the dead end.
                double kx = Math.Min(k.X + 2.6 / 30, wx - 0.2);
                k.X = kx; k.Z = fz; k.PrevX = kx; k.PrevZ = fz; k.TargetId = -1;
                double px = w.X[0], pz = w.Z[0];
                In(w, 0, 0, t >= 30 ? -1 : 0);
                SimStep.Step(w);
                double step = Math.Sqrt((w.X[0] - px) * (w.X[0] - px) + (w.Z[0] - pz) * (w.Z[0] - pz));
                r.MaxStep = Math.Max(r.MaxStep, step);
                double dx = w.X[0] - k.X, dz = w.Z[0] - k.Z, d = Math.Sqrt(dx * dx + dz * dz);
                r.MinD = Math.Min(r.MinD, d);
                if (d < body) r.Inside++;
            }
            return r;
        }

        [Test]
        public void Item1_AKaijuPinningAFighterInADeadEndNeverMovesItMoreThanACapOrThroughTheBody()
        {
            PinResult b = Pin(false), a = Pin(true);
            Json("1 dead-end pin max per-tick displacement (u)", N(b.MaxStep), N(a.MaxStep), "<= 0.35");
            Json("1 dead-end pin ticks with the centre inside the body", b.Inside.ToString(), a.Inside.ToString(), "0");
            Json("1 dead-end pin closest centre distance (u)", N(b.MinD), N(a.MinD), ">= 1.3");
            Assert.LessOrEqual(a.MaxStep, 0.35 + 1e-6, "no per-tick displacement over the cap");
            Assert.AreEqual(0, a.Inside, "the fighter's centre is never inside the Kaiju's body");
        }

        // ------------------------------------------------------------------------------------------------ [2] TNT shove

        private static readonly double[] TntD = { 0.0, 0.5, 1.0, 1.5, 2.0, 2.5, 3.0 };

        private static double[] Tnt(bool on, out int[] stun)
        {
            World w = NewW(on, ClassId.Demolitions);
            var keep = new List<int> { 0 }; for (int k = 0; k < 7; k++) keep.Add(16 + k);
            Bench(w, keep);
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            Put(w, 0, ox - 5, oz);
            var x0 = new double[7]; var z0 = new double[7];
            for (int k = 0; k < 7; k++)
            {
                SetCls(w, 16 + k, ClassId.Miner);
                w.Actors[16 + k].MaxHp = 1000; w.Actors[16 + k].Hp = 1000;
                double ang = k * 2 * Math.PI / 7;
                Put(w, 16 + k, ox + TntD[k] * Math.Cos(ang), oz + TntD[k] * Math.Sin(ang));
                x0[k] = w.X[16 + k]; z0[k] = w.Z[16 + k];
            }
            Assert.Greater(Demolitions.ThrowTnt(w, 0, ox, oz), 0, "the TNT was thrown");
            int boomT = -1; stun = new int[7];
            for (int t = 1; t <= 150; t++)
            {
                In(w, 0, 0, 0); for (int k = 0; k < 7; k++) In(w, 16 + k, 0, 0);
                if (boomT < 0) for (int k = 0; k < 7; k++) { w.X[16 + k] = x0[k]; w.Z[16 + k] = z0[k]; }
                SimStep.Step(w);
                for (int k = 0; k < 7; k++) stun[k] = Math.Max(stun[k], w.Actors[16 + k].HitStunLen);
                if (boomT < 0 && w.Actors[16].Hp < 1000) boomT = t;
            }
            Assert.Greater(boomT, 0, "the TNT went off");
            var moved = new double[7];
            for (int k = 0; k < 7; k++) moved[k] = Math.Sqrt((w.X[16 + k] - x0[k]) * (w.X[16 + k] - x0[k]) + (w.Z[16 + k] - z0[k]) * (w.Z[16 + k] - z0[k]));
            return moved;
        }

        private static string Arr(double[] v) { var s = new List<string>(); foreach (double d in v) s.Add(N(d)); return "[" + string.Join(",", s) + "]"; }
        private static string Arr(int[] v) { var s = new List<string>(); foreach (int d in v) s.Add(d.ToString()); return "[" + string.Join(",", s) + "]"; }

        [Test]
        public void Item2_TntShovesBodiesWithDistanceFalloffAndAShortStagger()
        {
            double[] b = Tnt(false, out int[] sb), a = Tnt(true, out int[] sa);
            Json("2 TNT shove by distance 0..3 u (u, Miner mass 1.1)", Arr(b), Arr(a), "2..3.5 at the centre, falling to 0 at the radius (2)");
            Json("2 TNT stagger by distance (ticks)", Arr(sb), Arr(sa), "short, falling with distance");
            Assert.GreaterOrEqual(a[0], 2.0, "centre shove");
            Assert.LessOrEqual(a[0], 3.5, "centre shove");
            for (int k = 1; k < 5; k++) Assert.LessOrEqual(a[k], a[k - 1] + 0.15, "the shove falls with distance (" + TntD[k] + " u)");
            Assert.Less(a[4], 0.9, "near the radius the shove is small");
            Assert.Less(a[5], 0.05, "outside the radius nobody moves");
            Assert.Less(a[6], 0.05, "outside the radius nobody moves");
            Assert.Greater(sa[0], 0, "the centre victim is staggered");
            Assert.LessOrEqual(sa[0], 10, "the stagger is short (<= 1/3 s)");
            Assert.LessOrEqual(sa[3], sa[0], "the stagger falls with distance");
        }

        /// <summary>
        /// The void-edge rule: a blast shove moves a grounded body like walking, so it stops at the island edge (no ring-outs;
        /// only a body over a freshly carved crater falls, by the existing fall rule). A body 0.6 u from the edge, the blast
        /// 1 u inside it: the body ends on walkable ground, alive, at most its radius from the edge line.
        /// </summary>
        [Test]
        public void Item2_ABlastShoveStopsAtTheIslandEdge()
        {
            World w = NewW(true, ClassId.Demolitions);
            Bench(w, new List<int> { 0, 16 });
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            int ex = (int)Math.Floor(ox) + 2;
            for (int cx = ex; cx <= ex + 4; cx++) for (int cz = (int)oz - 6; cz <= (int)oz + 6; cz++) w.Map.Cells[Idx(w, cx, cz)] = Cell.Void;
            SetCls(w, 16, ClassId.Farmer);   // the lightest: the longest shove
            w.Actors[16].MaxHp = 1000; w.Actors[16].Hp = 1000;
            Put(w, 0, ox - 5, oz);
            double vx0 = ex - 0.6, vz0 = oz + 0.5;
            Put(w, 16, vx0, vz0);
            Assert.Greater(Demolitions.ThrowTnt(w, 0, vx0 - 1.0, vz0), 0);
            for (int t = 0; t < 120; t++)
            {
                In(w, 0, 0, 0); In(w, 16, 0, 0);
                if (w.Actors[16].Hp >= 1000) { w.X[16] = vx0; w.Z[16] = vz0; }
                SimStep.Step(w);
            }
            double r = Collide.ActorRadius(w, 16);
            Json("2 void edge: victim x past the edge line (u)", "null", N(w.X[16] - ex), "<= 0 (body stops at the edge)");
            Assert.Less(w.Actors[16].Hp, 1000, "the blast hit");
            Assert.IsTrue(w.Actors[16].Alive, "alive");
            Assert.LessOrEqual(w.X[16] + r, ex + 0.02, "the body stopped at the edge");
            Assert.IsTrue(MapGrid.IsWalkable(w.Map, (int)Math.Floor(w.X[16]), (int)Math.Floor(w.Z[16])), "on walkable ground");
        }

        /// <summary>
        /// The Kaiju's blast slide: (1) in a match, a TNT 1 u behind the hatched Kaiju sets its slide velocity away from the
        /// blast; (2) the slide itself (PhysicsFeel.BlastKaiju then KaijuPushStep a tick, through the Kaiju's wall collider),
        /// isolated from its own walk, moves it a small distance and stops.
        /// </summary>
        [Test]
        public void Item2_ATntBlastGivesTheKaijuASmallSlide()
        {
            World w = NewW(true, ClassId.Demolitions); Bench(w, new[] { 0 });
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            Hatch(w);
            Kaiju k = w.Kaiju;
            Put(w, 0, ox - 5, oz);
            w.Actors[0].MaxHp = 5000; w.Actors[0].Hp = 5000;
            k.X = ox; k.Z = oz; k.PrevX = ox; k.PrevZ = oz;
            Assert.Greater(Demolitions.ThrowTnt(w, 0, ox - 1.0, oz), 0);
            double maxV = 0;
            for (int t = 0; t < 90; t++)
            {
                if (maxV == 0) { k.X = ox; k.Z = oz; }   // held on its spot until the blast (its AI would walk it off)
                In(w, 0, 0, 0); SimStep.Step(w);
                maxV = Math.Max(maxV, Math.Sqrt(k.PushVx * k.PushVx + k.PushVz * k.PushVz));
            }
            World w2 = NewW(true, ClassId.Warrior); Bench(w2, new[] { 0 });
            OpenSpot(w2, 14, w2.Map.Hill.X, w2.Map.Hill.Z, out double qx, out double qz);
            Hatch(w2);
            Kaiju k2 = w2.Kaiju;
            k2.X = qx; k2.Z = qz; k2.PrevX = qx; k2.PrevZ = qz; k2.PushVx = 0; k2.PushVz = 0;
            double kr = w2.Config.Abilities.DemolitionsTnt.BlastRadius + w2.Config.Kaiju.Radius;
            PhysicsFeel.BlastKaiju(w2, qx - 1.0, qz, kr);
            double dt = SimStep.Dt(w2.Config);
            for (int t = 0; t < 60; t++) PhysicsFeel.KaijuPushStep(w2, dt);
            double slide = k2.X - qx;
            Json("2 Kaiju slide from a TNT 1 u behind it (max slide speed u/s in the match; slide u)", "[0,0]", "[" + N(maxV) + "," + N(slide) + "]", "small: 0.05..0.4 u");
            Assert.Greater(maxV, 0, "the blast in the match set the Kaiju sliding");
            Assert.Greater(slide, 0.05, "the Kaiju is moved");
            Assert.Less(slide, 0.4, "a small slide only");
            Assert.AreEqual(0, k2.PushVx, "the slide ended");
        }

        // ------------------------------------------------------------------------------------------------ [4] corner grazes

        /// <summary>Ticks to pass a pillar grazed by `off` u (NaN: no pillar, the free run over the same distance); -1 stopped.</summary>
        private static int Graze(bool on, double off)
        {
            World w = NewW(on, ClassId.Warrior); Bench(w, new[] { 0 });
            OpenSpot(w, 12, w.Map.Hill.X, w.Map.Hill.Z, out double qx, out double qz);
            int pc = (int)Math.Floor(qx), pz = (int)Math.Floor(qz);
            if (double.IsNaN(off)) off = 0.0;
            else w.Map.Cells[Idx(w, pc, pz)] = Cell.Wall;
            double r0 = Collide.ActorRadius(w, 0);
            Put(w, 0, pc - 3, pz - r0 + off);
            for (int t = 1; t <= 90; t++) { In(w, 0, 1, 0); SimStep.Step(w); if (w.X[0] > pc + 1 + r0) return t; }
            return -1;
        }

        [Test]
        public void Item4_AGrazeUpTo035UOfAPillarSlidesRoundItInsteadOfStopping()
        {
            int free = Graze(true, double.NaN);
            Json("4 pillar graze: free run over the same distance (ticks)", Graze(false, double.NaN).ToString(), free.ToString(), "reference (the audit's ~24 was an estimate)");
            foreach (double off in new[] { 0.05, 0.15, 0.3, 0.34 })
            {
                int b = Graze(false, off), a = Graze(true, off);
                Json("4 pillar graze by " + N(off) + " u: tick past the pillar (-1 = stopped)", b.ToString(), a.ToString(), "past it, at most 6 ticks over the free run " + free);
                Assert.Greater(a, 0, "a " + off + " u graze slides round");
                Assert.LessOrEqual(a, free + 6, "a " + off + " u graze costs at most 0.2 s over a free run");
            }
        }

        /// <summary>Feels-best regression 2 (solid walls): a dodge, a 60 u/s knock and a running jump never pass a 1-cell wall.</summary>
        [Test]
        public void FeelsBest_SolidWallsZeroTunnellingAt60UPerSecond()
        {
            foreach (byte cellT in new[] { Cell.Wall, Cell.LowWall, Cell.Barricade })
            {
                World w = NewW(true, ClassId.Warrior); Bench(w, new[] { 0 });
                OpenSpot(w, 12, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
                int wx = (int)Math.Floor(ox) + 1;
                for (int cz = (int)oz - 3; cz <= (int)oz + 3; cz++) w.Map.Cells[Idx(w, wx, cz)] = cellT;
                Put(w, 0, wx - 0.6, oz);
                for (int t = 0; t < 3; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
                In(w, 0, 1, 0, false, true); SimStep.Step(w);
                for (int t = 0; t < 10; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
                double dodgeX = w.X[0] - wx;
                Put(w, 0, wx - 0.6, oz);
                w.Actors[0].KVx = 60;
                for (int t = 0; t < 30; t++) { In(w, 0, 0, 0); SimStep.Step(w); }
                double knockX = w.X[0] - wx;
                Put(w, 0, wx - 1.5, oz);
                for (int t = 0; t < 10; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
                In(w, 0, 1, 0, true); SimStep.Step(w);
                for (int t = 0; t < 30; t++) { In(w, 0, 1, 0); SimStep.Step(w); }
                double jumpX = w.X[0] - wx;
                Console.WriteLine("{\"item\":\"feels-best solid walls\",\"cell\":" + cellT + ",\"dodge\":" + N(dodgeX) + ",\"knock60\":" + N(knockX) + ",\"jump\":" + N(jumpX) + "}");
                Assert.Less(dodgeX, 0, "a dodge stops in front of cell " + cellT);
                Assert.Less(knockX, 0, "a 60 u/s knock stops in front of cell " + cellT);
                if (cellT != Cell.LowWall) Assert.Less(jumpX, 0, "a running jump stops in front of cell " + cellT);
            }
        }

        /// <summary>Feels-best regression 1 (instant response): ticks to top speed from rest and to a stop, unchanged by the rule.</summary>
        [Test]
        public void FeelsBest_InstantMovementResponseIsUnchanged()
        {
            int[] up = new int[2], stop = new int[2];
            for (int arm = 0; arm < 2; arm++)
            {
                World w = NewW(arm == 1, ClassId.Warrior); Bench(w, new[] { 0 });
                OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
                Put(w, 0, ox - 6, oz);
                double top = w.Config.Class(ClassId.Warrior).MoveSpeed;
                up[arm] = -1; stop[arm] = -1;
                for (int t = 1; t <= 30 && up[arm] < 0; t++) { In(w, 0, 1, 0); SimStep.Step(w); if (Math.Abs(w.Vx[0] - top) < 1e-3) up[arm] = t; }
                for (int t = 1; t <= 30 && stop[arm] < 0; t++) { In(w, 0, 0, 0); SimStep.Step(w); if (Math.Abs(w.Vx[0]) < 1e-6) stop[arm] = t; }
            }
            Json("feels-best response: ticks to top speed", up[0].ToString(), up[1].ToString(), "unchanged");
            Json("feels-best response: ticks to a stop", stop[0].ToString(), stop[1].ToString(), "unchanged");
            Assert.AreEqual(up[0], up[1]);
            Assert.AreEqual(stop[0], stop[1]);
            Assert.Greater(up[1], 0);
            Assert.LessOrEqual(up[1], 6, "top speed within 0.2 s");
        }

        // ------------------------------------------------------------------------------------------------ [12] wall slide

        private static double Slide(bool on, double deg)
        {
            World w = NewW(on, ClassId.Warrior); Bench(w, new[] { 0 });
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            int wz = (int)Math.Floor(oz) + 2;
            for (int cx = (int)ox - 7; cx <= (int)ox + 3; cx++) w.Map.Cells[Idx(w, cx, wz)] = Cell.Wall;
            Put(w, 0, ox - 6, wz - 0.46);
            double r = deg * Math.PI / 180, x0 = 0;
            for (int t = 0; t < 40; t++) { In(w, 0, Math.Cos(r), Math.Sin(r)); SimStep.Step(w); if (t == 9) x0 = w.X[0]; }
            return (w.X[0] - x0) / w.Config.Class(ClassId.Warrior).MoveSpeed;
        }

        [Test]
        public void Item12_AWallSlideKeepsAtLeast90PercentAlongTheWallAtShallowAngles()
        {
            foreach (double deg in new[] { 10.0, 30, 45, 60, 80 })
            {
                double b = Slide(false, deg), a = Slide(true, deg);
                Json("12 wall slide at " + deg + " deg: share of top speed along the wall", N(b), N(a), deg <= 45 ? ">= 0.90" : "between cos and 0.92");
                if (deg <= 45) Assert.GreaterOrEqual(a, 0.90, deg + " deg");
                else
                {
                    Assert.GreaterOrEqual(a, b - 0.02, deg + " deg: never slower than before");
                    Assert.LessOrEqual(a, 0.95, deg + " deg");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ [5] one-cell gap

        private static int Door(bool on, bool mixed, int ticks, out double maxPen)
        {
            World w = NewW(on, ClassId.Warrior);
            var ids = new List<int>();
            for (int k = 0; k < 8; k++) ids.Add(mixed && (k % 2 == 1) ? 16 + k : k);
            Bench(w, ids);
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            int wx = (int)Math.Floor(ox);
            int gz0 = (int)Math.Floor(oz);
            for (int cz = (int)oz - 8; cz <= (int)oz + 8; cz++) if (cz != gz0) w.Map.Cells[Idx(w, wx, cz)] = Cell.Wall;
            double doorZ = gz0 + 0.5;
            for (int k = 0; k < 8; k++) Put(w, ids[k], ox - 4 - (k / 3) * 1.0, doorZ - 1.2 + (k % 3) * 1.2);
            maxPen = -9;
            int done = 0;
            var through = new bool[8];
            for (int t = 1; t <= ticks; t++)
            {
                for (int k = 0; k < 8; k++)
                {
                    int id = ids[k];
                    double tx = w.X[id] < wx + 0.5 ? wx + 0.5 : ox + 5, tz = w.X[id] < wx + 0.5 ? doorZ : w.Z[id];
                    double dx = tx - w.X[id], dz = tz - w.Z[id], l = Math.Sqrt(dx * dx + dz * dz) + 1e-9;
                    In(w, id, dx / l, dz / l);
                }
                SimStep.Step(w);
                for (int a = 0; a < 8; a++)
                    for (int b = a + 1; b < 8; b++)
                    {
                        double ddx = w.X[ids[a]] - w.X[ids[b]], ddz = w.Z[ids[a]] - w.Z[ids[b]];
                        maxPen = Math.Max(maxPen, Collide.ActorRadius(w, ids[a]) + Collide.ActorRadius(w, ids[b]) - Math.Sqrt(ddx * ddx + ddz * ddz));
                    }
                for (int k = 0; k < 8; k++) if (!through[k] && w.X[ids[k]] > wx + 1.5) { through[k] = true; done++; }
            }
            return done;
        }

        [Test]
        public void Item5_EightBodiesQueueThroughAOneCellGap()
        {
            int b20 = Door(false, false, 600, out double pb), a8 = Door(true, false, 240, out double pa);
            int bm = Door(false, true, 600, out double pbm), am = Door(true, true, 240, out double pam);
            Json("5 one-cell gap, 8 same-team bodies: through (before 20 s, after 8 s)", b20.ToString(), a8.ToString(), ">= 7 in 8 s");
            Json("5 one-cell gap, 8 mixed-team bodies: through (before 20 s, after 8 s)", bm.ToString(), am.ToString(), "reported");
            Json("5 one-cell gap max overlap (u)", N(pb), N(pa), "reported");
            Assert.GreaterOrEqual(a8, 7, "7 of 8 through the one-cell gap within 8 s");
        }

        // ------------------------------------------------------------------------------------------------ [7] [8] Kaiju

        /// <summary>[7] Ten fighters walk into the hatched Kaiju for 3 s: its per-tick speed never spikes over 3.2 u/s.</summary>
        private static double CrowdShove(bool on)
        {
            World w = NewW(on, ClassId.Warrior);
            var keep = new List<int>(); for (int k = 0; k < 10; k++) keep.Add(k);
            Bench(w, keep);
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            Hatch(w);
            Kaiju k0 = w.Kaiju;
            k0.X = ox; k0.Z = oz; k0.PrevX = ox; k0.PrevZ = oz;
            for (int k = 0; k < 10; k++)
            {
                double ang = k * 2 * Math.PI / 10;
                Put(w, k, ox + 2.2 * Math.Cos(ang), oz + 2.2 * Math.Sin(ang));
                w.Actors[k].MaxHp = 5000; w.Actors[k].Hp = 5000;
            }
            double max = 0;
            for (int t = 0; t < 90; t++)
            {
                for (int k = 0; k < 10; k++)
                {
                    double dx = k0.X - w.X[k], dz = k0.Z - w.Z[k], l = Math.Sqrt(dx * dx + dz * dz) + 1e-9;
                    In(w, k, dx / l, dz / l);
                }
                double x0 = k0.X, z0 = k0.Z;
                SimStep.Step(w);
                max = Math.Max(max, Math.Sqrt((k0.X - x0) * (k0.X - x0) + (k0.Z - z0) * (k0.Z - z0)) * w.Config.SimHz);
            }
            return max;
        }

        [Test]
        public void Item7_ACrowdShovingTheKaijuNeverLurchesIt()
        {
            double b = CrowdShove(false), a = CrowdShove(true);
            Json("7 Kaiju max speed with 10 fighters shoving it (u/s)", N(b), N(a), "<= 3.2 (walk 2.6 + slow drift)");
            Assert.LessOrEqual(a, 3.2, "the crowd cannot lurch the Kaiju");
        }

        /// <summary>
        /// [8] Circle-strafe: a fighter runs a 2.4 u circle round the hatched Kaiju at its top speed (about 90 deg/s round it),
        /// then jumps (teleport) to the far side. The sim facing turns at most PhysicsFeelConfig.KaijuTurnDegPerSec (300 deg/s,
        /// 10 deg a tick); printed: the max turn per tick, the mean and max lag behind the bearing to the fighter.
        /// </summary>
        private static void Strafe(bool on, out double maxTurn, out double meanLag, out double maxLag, out int flipTicks)
        {
            World w = NewW(on, ClassId.Warrior); Bench(w, new[] { 0 });
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            Hatch(w);
            Kaiju k = w.Kaiju;
            k.X = ox; k.Z = oz; k.PrevX = ox; k.PrevZ = oz;
            w.Actors[0].MaxHp = 50000; w.Actors[0].Hp = 50000;
            Put(w, 0, ox + 2.4, oz);
            double speed = w.Config.Class(ClassId.Warrior).MoveSpeed;
            double omega = speed / 2.4 / w.Config.SimHz;
            double ang = 0, last = k.Facing;
            maxTurn = 0; meanLag = 0; maxLag = 0; flipTicks = -1;
            int n = 0;
            for (int t = 0; t < 240; t++)
            {
                ang += omega;
                if (t == 180) ang += Math.PI;   // the far side in one tick
                // The Kaiju is held on its spot and on this fighter (its AI faces a chased target inside claw reach + 1.5).
                k.X = ox; k.Z = oz; k.PrevX = ox; k.PrevZ = oz; k.TargetId = 0;
                double fx = k.X + 2.4 * Math.Cos(ang), fz = k.Z + 2.4 * Math.Sin(ang);
                Put(w, 0, fx, fz);
                In(w, 0, 0, 0);
                SimStep.Step(w);
                double turn = Math.Abs(PhysicsFeel.AngleDelta(k.Facing, last)) * 180 / Math.PI;
                maxTurn = Math.Max(maxTurn, turn);
                last = k.Facing;
                double bearing = Math.Atan2(w.Z[0] - k.Z, w.X[0] - k.X);
                double lag = Math.Abs(PhysicsFeel.AngleDelta(bearing, k.Facing)) * 180 / Math.PI;
                if (t >= 30 && t < 180) { meanLag += lag; n++; maxLag = Math.Max(maxLag, lag); }
                if (t >= 180 && flipTicks < 0 && lag < 15) flipTicks = t - 180;
            }
            meanLag /= Math.Max(1, n);
        }

        [Test]
        public void Item8_TheKaijuSimFacingTurnsAtMost300DegreesASecondUnderACircleStrafe()
        {
            Strafe(false, out double bt, out double bl, out double bm, out int bf);
            Strafe(true, out double at, out double al, out double am, out int af);
            Json("8 Kaiju facing: max turn in one tick (deg)", N(bt), N(at), "<= 10 (300 deg/s)");
            Json("8 circle strafe at top speed: mean / max lag behind the fighter (deg)", "[" + N(bl) + "," + N(bm) + "]", "[" + N(al) + "," + N(am) + "]", "reported");
            Json("8 fighter jumps to the far side: ticks until the Kaiju faces it within 15 deg", bf.ToString(), af.ToString(), "about 17 (180 deg at 300 deg/s)");
            Assert.LessOrEqual(at, 10.0 + 1e-6, "turn-rate cap");
            Assert.LessOrEqual(al, 30, "the Kaiju keeps up with a circle strafe at top speed");
        }

        // ------------------------------------------------------------------------------------------------ [3] [6] [7] [8] crowd match

        private struct Crowd
        {
            public double WorstOverlap, WorstHill, WorstPlain, CrowdP50, CrowdP95, OpenP95, KaijuMaxTurn, KaijuMaxSpeed;
            public string WorstWhat, PlainWhat;
            public int Hard;
            public int Flags, KaijuFast, Big45;
            public long Alloc;
            public ulong Hash;
        }

        /// <summary>The audit's crowd run (PACrowd.Run): 16v16 Full, seed 7, Kaiju from 0:30, 3600 ticks (the fast shiver gate's match).</summary>
        private static Crowd RunCrowd(bool on, int seed = 7, int ticks = 3600, Action<PhysicsFeelConfig> tweak = null, bool print = true)
        {
            World w = NewW(on, ClassId.Warrior, seed, 16, 30, tweak);
            InputFrame s = SimInput.EmptyInput();
            var watch = new ShiverWatch();
            int n = w.ActorCount;
            var hx = new double[n, 2]; var hz = new double[n, 2];
            for (int i = 0; i < n; i++) { hx[i, 0] = double.NaN; hx[i, 1] = double.NaN; }
            var crowdD2 = new List<double>(4096); var openD2 = new List<double>(4096);
            var c = new Crowd();
            double hR = w.Map.Hill.R, hX = w.Map.Hill.X, hZ = w.Map.Hill.Z;
            double lastF = 0; bool have = false;
            for (int t = 0; t < ticks && w.Match.Phase != MatchPhase.Ended; t++)
            {
                Kaiju k = w.Kaiju;
                // MatchTick.Advance's body (bots, then the step), with the allocation counter round the sim step alone.
                BotReflex.BeginFills(w);
                BotBrain.FillBotInput(w, 0, s);
                SimInput.ApplyInput(w, 0, s);
                BotBrain.BotsThink(w, SimStep.Dt(w.Config));
                for (int id = 1; id < w.ActorCount; id++) { BotBrain.FillBotInput(w, id, s); SimInput.ApplyInput(w, id, s); }
                BotReflex.EndFills(w);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                SimStep.Step(w);
                if (t >= 600) c.Alloc += GC.GetAllocatedBytesForCurrentThread() - a0;
                watch.Observe(w);
                int onHill = 0;
                for (int i = 0; i < n; i++) { if (!w.Actors[i].Alive) continue; double dx = w.X[i] - hX, dz = w.Z[i] - hZ; if (dx * dx + dz * dz < hR * hR) onHill++; }
                for (int i = 0; i < n; i++)
                {
                    Actor a = w.Actors[i];
                    if (!a.Alive || a.AirLen > 0) { hx[i, 0] = double.NaN; continue; }
                    for (int j = i + 1; j < n; j++)
                    {
                        if (!w.Actors[j].Alive || w.Actors[j].AirLen > 0) continue;
                        double dx = w.X[i] - w.X[j], dz = w.Z[i] - w.Z[j];
                        double ov = Collide.ActorRadius(w, i) + Collide.ActorRadius(w, j) - Math.Sqrt(dx * dx + dz * dz);
                        if (ov <= 0.05) continue;
                        bool ladder = a.Team == w.Actors[j].Team && CombatFeel.On(w) && CombatFeel.AllyPasses(w, i, j);
                        double mx = (w.X[i] + w.X[j]) * 0.5 - hX, mz = (w.Z[i] + w.Z[j]) * 0.5 - hZ;
                        bool hill = mx * mx + mz * mz < hR * hR;
                        string what = "tick " + w.Tick + " " + i + "/" + j + (a.Team == w.Actors[j].Team ? " allies" : " enemies") + (ladder ? " ladder-pass" : "")
                            + " " + a.AiState + "/" + w.Actors[j].AiState + " standUp " + a.StandUp + "/" + w.Actors[j].StandUp + " at " + N(w.X[i]) + "," + N(w.Z[i])
                            + " cells " + w.Map.Cells[Idx(w, JsMath.FloorToInt(w.X[i]), JsMath.FloorToInt(w.Z[i]))] + "/" + w.Map.Cells[Idx(w, JsMath.FloorToInt(w.X[j]), JsMath.FloorToInt(w.Z[j]))];
                        if (ov > c.WorstOverlap) { c.WorstOverlap = ov; c.WorstWhat = what; }
                        if (hill && ov > c.WorstHill) c.WorstHill = ov;
                        if (!ladder && ov > c.WorstPlain) { c.WorstPlain = ov; c.PlainWhat = what; }
                    }
                    if (!double.IsNaN(hx[i, 0]) && !double.IsNaN(hx[i, 1]))
                    {
                        double ddx = w.X[i] - 2 * hx[i, 0] + hx[i, 1], ddz = w.Z[i] - 2 * hz[i, 0] + hz[i, 1];
                        double d2 = Math.Sqrt(ddx * ddx + ddz * ddz);
                        if (d2 < 1.0)
                        {
                            double cx = w.X[i] - hX, cz = w.Z[i] - hZ;
                            int near = 0;
                            for (int j = 0; j < n; j++) { if (j == i || !w.Actors[j].Alive) continue; double ex = w.X[j] - w.X[i], ez = w.Z[j] - w.Z[i]; if (ex * ex + ez * ez < 4) near++; }
                            if (cx * cx + cz * cz < hR * hR && onHill >= 20) crowdD2.Add(d2);
                            else if (near == 0 && a.Moving) openD2.Add(d2);
                        }
                    }
                    hx[i, 1] = hx[i, 0]; hz[i, 1] = hz[i, 0]; hx[i, 0] = w.X[i]; hz[i, 0] = w.Z[i];
                }
                if (w.Match.Hatched && k.State != KaijuState.Egg)
                {
                    double sp = Math.Sqrt((k.X - k.PrevX) * (k.X - k.PrevX) + (k.Z - k.PrevZ) * (k.Z - k.PrevZ)) * w.Config.SimHz;
                    bool sliding = k.PushVx != 0 || k.PushVz != 0;
                    if (!sliding) c.KaijuMaxSpeed = Math.Max(c.KaijuMaxSpeed, sp);
                    if (sp > 3.2 && !sliding) c.KaijuFast++;
                    if (have)
                    {
                        double ad = Math.Abs(PhysicsFeel.AngleDelta(k.Facing, lastF)) * 180 / Math.PI;
                        c.KaijuMaxTurn = Math.Max(c.KaijuMaxTurn, ad);
                        if (ad > 45) c.Big45++;
                    }
                    lastF = k.Facing; have = true;
                }
            }
            crowdD2.Sort(); openD2.Sort();
            c.CrowdP50 = crowdD2.Count == 0 ? 0 : crowdD2[crowdD2.Count / 2];
            c.CrowdP95 = crowdD2.Count == 0 ? 0 : crowdD2[Math.Min(crowdD2.Count - 1, (int)(0.95 * crowdD2.Count))];
            c.OpenP95 = openD2.Count == 0 ? 0 : openD2[Math.Min(openD2.Count - 1, (int)(0.95 * openD2.Count))];
            c.Flags = watch.Count;
            foreach (ShiverFlag f in watch.Flags) if (f.Actor == ShiverWatch.KaijuId || f.Kind == ShiverKind.Reversals || f.Kind == ShiverKind.Dither && f.Reversals >= 4) c.Hard++;
            c.Hash = CriticalMass.Net.StateHash.Of(w);
            if (on && print) { int shown = 0; foreach (ShiverFlag f in watch.Flags) if (shown++ < 20) Console.WriteLine(ShiverWatch.Format(f)); }
            return c;
        }

        [Test]
        public void Items3678_TheHillCrowdMatchNoFusedAlliesNoAddedJitterNoKaijuLurchOrFlip()
        {
            Crowd b = RunCrowd(false), a = RunCrowd(true);
            Json("6 worst body overlap anywhere (u)", N(b.WorstOverlap), N(a.WorstOverlap), "reported");
            Console.WriteLine("    worst before: " + b.WorstWhat + " | after: " + a.WorstWhat);
            Json("6 worst body overlap in the hill disc (u)", N(b.WorstHill), N(a.WorstHill), "reported");
            Json("6 worst body overlap, pairs not on a ladder lane (b15 lets allies pass there) (u)", N(b.WorstPlain), N(a.WorstPlain), "<= 0.25");
            Console.WriteLine("    worst plain before: " + b.PlainWhat + " | after: " + a.PlainWhat);
            Json("3 hard shiver flags (Kaiju, Reversals, Dither 4+), seed 7 hatch 0:30, 2 min", b.Hard.ToString(), a.Hard.ToString(), "0");
            Json("6 hill crowd jitter |second difference| median (u/tick^2)", N(b.CrowdP50), N(a.CrowdP50), "no added jitter");
            Json("6 hill crowd jitter p95 (u/tick^2)", N(b.CrowdP95), N(a.CrowdP95), "no added jitter");
            Json("6 open walkers jitter p95 (u/tick^2)", N(b.OpenP95), N(a.OpenP95), "unchanged");
            Json("3 shiver flags, seed 7 hatch 0:30, 2 min", b.Flags.ToString(), a.Flags.ToString(), ">= 80% fewer (soak: Slow test)");
            Json("7 Kaiju ticks over 3.2 u/s (no blast slide)", b.KaijuFast.ToString(), a.KaijuFast.ToString(), "0");
            Json("7 Kaiju max speed (u/s, no blast slide)", N(b.KaijuMaxSpeed), N(a.KaijuMaxSpeed), "<= 3.2");
            Json("8 Kaiju max facing change in one tick (deg)", N(b.KaijuMaxTurn), N(a.KaijuMaxTurn), "<= 10");
            Json("8 Kaiju turns over 45 deg in one tick", b.Big45.ToString(), a.Big45.ToString(), "0");
            Json("feels-best cheap step: bytes allocated in SimStep after a 600-tick warm-up", b.Alloc.ToString(), a.Alloc.ToString(), "0 (not more than before)");
            Assert.LessOrEqual(a.WorstPlain, 0.25, "no fused bodies (off the ladder lanes)");
            Assert.LessOrEqual(a.CrowdP95, b.CrowdP95 * 1.15 + 0.002, "no added crowd jitter");
            Assert.AreEqual(0, a.KaijuFast, "no Kaiju lurch");
            Assert.LessOrEqual(a.KaijuMaxTurn, 10.0 + 1e-6, "Kaiju turn-rate cap");
            Assert.IsTrue(a.Alloc == 0 || a.Alloc <= b.Alloc, "no added allocation in the step");
            Crowd again = RunCrowd(true);
            Assert.AreEqual(a.Hash, again.Hash, "deterministic: the same seed twice gives the same hash");
        }

        /// <summary>
        /// Diagnostic (prints only): the fast shiver gate's match with each b16 rule switched off in turn, so a change in flags,
        /// hard flags or overlap is pinned on the rule that makes it.
        /// </summary>
        [Test]
        public void Diag_EachRuleOffInTurnOnTheFastGateMatch()
        {
            var arms = new List<KeyValuePair<string, Action<PhysicsFeelConfig>>>
            {
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("all on", null),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("blast shove off", f => f.BlastShove = false),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("separation no-reversal off", f => f.SeparationNoReversal = false),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("bot pull deadband 0", f => f.BotPullDeadband = 0),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("gap flow off", f => f.GapFlow = false),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("ally push ramp off", f => { f.AllyPushRampFrom = 1e9; f.AllyPushFullAt = 2e9; }),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("ally pass anywhere", f => f.AllyPassInGapsOnly = false),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("corner nudge 0.1", f => f.CornerNudgeMax = 0.1),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("wall slide keep off", f => f.WallSlideKeep = 0),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("Kaiju turn uncapped", f => f.KaijuTurnDegPerSec = 1e9),
                new KeyValuePair<string, Action<PhysicsFeelConfig>>("Kaiju pin/crowd caps off", f => { f.KaijuPinStepMax = 1e9; f.KaijuCrowdShoveMax = 1e9; f.KaijuNetSpeedMax = 1e9; }),
            };
            Crowd off = RunCrowd(false, print: false);
            Console.WriteLine("{\"item\":\"diag rule off\",\"arm\":\"rule disabled (before)\",\"flags\":" + off.Flags + ",\"hard\":" + off.Hard + ",\"worstPlain\":" + N(off.WorstPlain) + ",\"crowdP50\":" + N(off.CrowdP50) + "}");
            foreach (var arm in arms)
            {
                Crowd c = RunCrowd(true, tweak: arm.Value, print: false);
                Console.WriteLine("{\"item\":\"diag rule off\",\"arm\":\"" + arm.Key + "\",\"flags\":" + c.Flags + ",\"hard\":" + c.Hard + ",\"worstPlain\":" + N(c.WorstPlain) + ",\"crowdP50\":" + N(c.CrowdP50) + "}");
            }
        }

        /// <summary>Classic and PhysicsOnly never see the rule: the same match hash with the switch on and off, PhysicsFeel.On false.</summary>
        [Test]
        public void ClassicAndPhysicsOnlyNeverSeeTheRule()
        {
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                ulong on = CriticalMass.Sim.Tests.Harness.MatchHash.Run(1, 16, 1800, ClassId.Warrior, null, c => c.ApplyRules(p));
                ulong off = CriticalMass.Sim.Tests.Harness.MatchHash.Run(1, 16, 1800, ClassId.Warrior, null, c => { c.ApplyRules(p); c.Depth.PhysicsFeel.Enabled = false; });
                Console.WriteLine("{\"item\":\"" + p + " unchanged\",\"on\":\"0x" + on.ToString("X16") + "\",\"off\":\"0x" + off.ToString("X16") + "\"}");
                Assert.AreEqual(off, on, p);
                Assert.IsFalse(PhysicsFeel.On(GameConfig.CreateBrowser5v5().ApplyRules(p)), p + ": PhysicsFeel.On");
            }
            Assert.IsTrue(PhysicsFeel.On(GameConfig.CreateDefault()), "on in the game's default (Full)");
        }

        /// <summary>[3] The shiver soak (the audit's PAW.Soak, MovementShiverTests' six matches): at least 80% fewer flags.</summary>
        [Test, Category("Slow")]
        public void Item3_TheShiverSoakHasAtLeast80PercentFewerFlags()
        {
            int before = 0, after = 0;
            foreach (bool on in new[] { false, true })
                foreach (int seed in new[] { 1, 7, 1337 })
                    foreach (double hatch in new[] { 240.0, 60.0 })
                    {
                        World w = NewW(on, ClassId.Warrior, seed, 16, hatch);
                        var watch = new ShiverWatch(); watch.MaxFlags = 4096;
                        InputFrame s = SimInput.EmptyInput();
                        for (int t = 0; t < 14400 && w.Match.Phase != MatchPhase.Ended; t++) { MatchTick.Advance(w, null, s); watch.Observe(w); }
                        var byState = new Dictionary<string, int>();
                        foreach (ShiverFlag f in watch.Flags) { string key = f.Kind + "/" + f.State; byState[key] = byState.TryGetValue(key, out int v) ? v + 1 : 1; }
                        var parts = new List<string>(); foreach (var kv in byState) parts.Add("\"" + kv.Key + "\":" + kv.Value);
                        Console.WriteLine("{\"item\":\"3 shiver soak\",\"on\":" + (on ? "true" : "false") + ",\"seed\":" + seed + ",\"hatch\":" + hatch + ",\"flags\":" + watch.Count + ",\"byKindState\":{" + string.Join(",", parts) + "}}");
                        if (on) after += watch.Count; else before += watch.Count;
                    }
            Json("3 shiver soak total flags (6 matches)", before.ToString(), after.ToString(), ">= 80% fewer");
            Assert.LessOrEqual(after, before * 0.2, "at least 80% fewer shiver flags");
        }

        /// <summary>Feels-best regression 3 (cheap step): the whole tick's cost with and without the rule, alternating, best of 3.</summary>
        [Test, Category("Slow")]
        public void FeelsBest_TheStepStaysCheap()
        {
            double[] best = { double.MaxValue, double.MaxValue };
            for (int rep = 0; rep < 3; rep++)
                for (int arm = 0; arm < 2; arm++)
                {
                    World w = NewW(arm == 1, ClassId.Warrior, 7, 16, 30);
                    InputFrame s = SimInput.EmptyInput();
                    for (int t = 0; t < 300; t++) MatchTick.Advance(w, null, s);
                    long t0 = Stopwatch.GetTimestamp();
                    for (int t = 0; t < 3300 && w.Match.Phase != MatchPhase.Ended; t++) MatchTick.Advance(w, null, s);
                    double us = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency / 3300;
                    best[arm] = Math.Min(best[arm], us);
                }
            Json("feels-best cheap step: whole 16v16 tick (us, best of 3)", N(best[0]), N(best[1]), "physics part within +10% of 230 us (+23 us)");
            Assert.LessOrEqual(best[1], best[0] + 23 + best[0] * 0.05, "the rule adds at most ~23 us a tick (plus 5% timing noise)");
        }

        // ------------------------------------------------------------------------------------------------ [11] refused jump

        private static int Refused(bool on)
        {
            World w = NewW(on, ClassId.Warrior); Bench(w, new[] { 0 });
            OpenSpot(w, 14, w.Map.Hill.X, w.Map.Hill.Z, out double ox, out double oz);
            int ex = (int)Math.Floor(ox) + 1;
            for (int cx = ex; cx <= ex + 8 && cx < w.Map.Width; cx++) for (int cz = (int)oz - 8; cz <= (int)oz + 8; cz++) w.Map.Cells[Idx(w, cx, cz)] = Cell.Void;
            Put(w, 0, ex - 0.5, oz + 0.5);
            int head = w.EventHead, count = 0;
            for (int t = 0; t < 12; t++)
            {
                In(w, 0, 1, 0, t == 0);
                SimStep.Step(w);
                for (; head < w.EventHead; head++)
                {
                    SimEvent e = w.Events[head % World.EventCapacity];
                    if (e.Type == SimEventType.JumpRefused && e.A == 0) count++;
                }
            }
            Assert.AreEqual(0, w.Actors[0].AirLen, "no jump into the void");
            return count;
        }

        [Test]
        public void Item11_ARefusedJumpAtTheIslandEdgeTellsTheViewOnce()
        {
            int b = Refused(false), a = Refused(true);
            Json("11 JumpRefused events for one press at the island edge", b.ToString(), a.ToString(), "1");
            Assert.AreEqual(0, b);
            Assert.AreEqual(1, a);
        }

        // ------------------------------------------------------------------------------------------------ [15] wall health

        [Test]
        public void Item15_ALowWallsAndABarricadesHealthReadsTrueAfterTnt()
        {
            foreach (int tier in new[] { 0, 1 })
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5();
                cfg.ApplyRules(RulesProfile.Full);
                World w = World.CreateWorld(new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = cfg });
                for (int j = 1; j < w.ActorCount; j++) { w.Actors[j].Alive = false; w.Actors[j].RespawnIn = 1e9; w.MeatDropped[j] = 1; w.Depth.WasAlive[j] = 0; }
                w.Actors[0].Cls = ClassId.Builder;
                foreach (ResourceNode nd in w.Nodes) { nd.X = -100; nd.Z = -100; }
                if (w.Depth.Sources != null) foreach (SourceNode sn in w.Depth.Sources) if (sn != null) { sn.X = -100; sn.Z = -100; }
                w.Tick = 100;
                for (int j = 0; j < w.Walls.Tool.Length; j++) w.Walls.Tool[j] = 1;
                int[] tiers = w.Config.Depth.Build.TierAtHutLevel; for (int k = 0; k < tiers.Length; k++) tiers[k] = tier;
                if (tier == 1) w.Walls.Tall[0] = 1;
                MapDef m = w.Map;
                int ox = -1, oz = -1; double best = double.MaxValue;
                for (int cz = 5; cz < m.Height - 5; cz++)
                    for (int cx = 5; cx < m.Width - 5; cx++)
                    {
                        bool ok = true;
                        for (int dz = -4; dz <= 4 && ok; dz++) for (int dx = -4; dx <= 4 && ok; dx++) ok = m.Cells[(cz + dz) * m.Width + cx + dx] == Cell.Floor && BuilderWalls.CellRefusal(w, cx + dx, cz + dz) == 0;
                        if (!ok) continue;
                        double d = (cx - m.Width / 2.0) * (cx - m.Width / 2.0) + (cz - m.Height / 2.0) * (cz - m.Height / 2.0);
                        if (d < best) { best = d; ox = cx; oz = cz; }
                    }
                Assert.GreaterOrEqual(ox, 0);
                Actor me = w.Actors[0]; me.Alive = true; me.Hp = me.MaxHp; me.SpawnProtect = 0; me.StandUp = 0; me.AirLen = 0;
                Put(w, 0, ox + 0.5, oz + 0.5);
                Assert.AreEqual(BuilderWalls.Built, BuilderWalls.Build(w, 0, ox + 2.5, oz + 0.5));
                int cx2 = ox + 2, cz2 = oz;
                byte cell = m.Cells[Idx(w, cx2, cz2)];
                double hp0 = PhysicsFeel.CellHp(w, cx2, cz2);
                double old0 = m.WallHp != null ? m.WallHp[Idx(w, cx2, cz2)] : 0;
                int enemy = w.ActorCount / 2;
                double r = w.Config.Abilities.DemolitionsTnt.BlastRadius;
                Barricades.DamageBarricadesInArc(w, enemy, ox + 2.5, oz + 0.5, 1, 0, r, -1, w.Config.Abilities.DemolitionsTnt.Damage, true);
                BuilderWalls.BlastWalls(w, TeamId.Crimson, ox + 2.5, oz + 0.5, r);
                double hp1 = PhysicsFeel.CellHp(w, cx2, cz2);
                double old1 = m.WallHp != null ? m.WallHp[Idx(w, cx2, cz2)] : 0;
                Json("15 " + (cell == Cell.LowWall ? "LowWall" : cell == Cell.Barricade ? "Barricade" : "cell " + cell) + " health before -> after one TNT (MapDef.WallHp read vs PhysicsFeel.CellHp)",
                    "[" + N(old0) + "," + N(old1) + "]", "[" + N(hp0) + "," + N(hp1) + "]", "the true health, falling");
                Assert.IsTrue(cell == Cell.LowWall || cell == Cell.Barricade, "a built wall cell");
                Assert.Greater(hp0, 0, "a built wall has health");
                Assert.Less(hp1, hp0, "TNT took health off");
            }
        }
    }
}
