using System;
using System.Globalization;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b12-jk (Jeisil's j: "grenades that read as grenades"), view only: the aim arc lands where the Sim's throw lands, the
    /// ring is the blast radius, the fuse blink only ever speeds up, the warning goes to an enemy inside the ring and to nobody
    /// outside it, and an enemy never sees a mine ring. Every case prints what it measured, the value and the limit.
    /// </summary>
    [TestFixture]
    public sealed class GrenadeReadTests
    {
        const int Me = 0;

        static string F(double v) => v.ToString("0.000", CultureInfo.InvariantCulture);

        static void Print(string s) => Debug.Log("[CM.GrenadeRead] " + s);

        static World Field()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Demolitions, Config = GameConfig.CreateDefault() });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1;
                if (w.Depth != null) w.Depth.WasAlive[j] = 0;
            }
            Actor a = w.Actors[Me];
            a.Alive = true;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            w.Tick = 100;
            return w;
        }

        [Test]
        public void ArcLandsWhereTheSimThrowLandsForFiveAimAngles()
        {
            World w = Field();
            DemolitionsTntConfig cfg = w.Config.Abilities.DemolitionsTnt;
            Assert.AreEqual(ClassId.Demolitions, w.Actors[Me].Cls);
            var scan = new AffordanceScan();
            const double limit = 0.05;
            int n = GrenadeTuning.ArcPoints;
            for (int k = 0; k < 5; k++)
            {
                double ang = k * 2 * Math.PI / 5 + 0.3;
                double reach = k % 2 == 0 ? cfg.ThrowRange * 0.6 : cfg.ThrowRange * 1.8;   // inside and past the throw range
                double ox = w.X[Me], oz = w.Z[Me];
                double ax = ox + Math.Cos(ang) * reach, az = oz + Math.Sin(ang) * reach;

                // The view: the scan's TNT target (what the arc ends on) and the arc's last point.
                scan.Scan(w, Me, true, ax, az, false);
                Assert.AreEqual(AffAction.Tnt, scan.Primary.Action);
                GrenadeRead.ArcPoint(n - 1, n, ox, oz, scan.Primary.X, scan.Primary.Z, out double vx, out double vz, out float lastLift);
                GrenadeRead.ArcPoint(0, n, ox, oz, scan.Primary.X, scan.Primary.Z, out double hx, out double hz, out float handLift);

                // The Sim: a real throw.
                for (int b = 0; b < w.Demolitions.Bombs.Length; b++) w.Demolitions.Bombs[b].Active = false;
                w.Demolitions.TntReadyTick[Me] = 0;
                Assert.AreEqual(Demolitions.TntThrownCode, Demolitions.ThrowTnt(w, Me, ax, az), "the throw goes out");
                Bomb thrown = null;
                foreach (Bomb b in w.Demolitions.Bombs) if (b.Active && b.Owner == Me) thrown = b;
                Assert.IsNotNull(thrown);
                double err = Math.Sqrt((vx - thrown.X) * (vx - thrown.X) + (vz - thrown.Z) * (vz - thrown.Z));
                Print("arc landing angle " + F(ang) + " rad, aim " + F(reach) + " u: arc (" + F(vx) + ", " + F(vz) + ") sim (" + F(thrown.X) + ", "
                      + F(thrown.Z) + ") error " + F(err) + " u, limit " + F(limit) + " u; hand lift " + F(handLift) + ", landing lift " + F(lastLift));
                Assert.LessOrEqual(err, limit, "the arc ends where the Sim's bomb lands");
                Assert.AreEqual(ox, hx, 1e-9, "the arc starts at the thrower");
                Assert.AreEqual(oz, hz, 1e-9);
                Assert.AreEqual(GrenadeTuning.HandLift, handLift, 1e-4, "the arc starts at the hand");
                Assert.AreEqual(GrenadeTuning.LandLift, lastLift, 1e-4, "the arc comes down on the ground");
            }
        }

        [Test]
        public void RingRadiusIsTheBlastRadius()
        {
            World w = Field();
            DemolitionsTntConfig cfg = w.Config.Abilities.DemolitionsTnt;
            float r = GrenadeRead.RingRadius(cfg);
            float drawn = GrenadeRead.RingQuadHalf(r) * 28f / 32f;   // CellRing draws its ring at 28 of 32 px
            var scan = new AffordanceScan();
            scan.Scan(w, Me, false, double.NaN, double.NaN, false);
            Print("ring radius " + F(r) + " drawn " + F(drawn) + " aim ghost " + F(scan.Primary.GhostRadius) + ", limit BlastRadius " + F(cfg.BlastRadius) + " +/- 0.001");
            Assert.AreEqual(cfg.BlastRadius, r, 1e-3);
            Assert.AreEqual(cfg.BlastRadius, drawn, 1e-3, "the drawn ring sits on the blast radius");
            Assert.AreEqual(cfg.BlastRadius, scan.Primary.GhostRadius, 1e-9, "the aim's landing ring is the blast radius");
        }

        [Test]
        public void BlinkPeriodShortensMonotonicallyOverTheFuse()
        {
            World w = Field();
            DemolitionsTntConfig cfg = w.Config.Abilities.DemolitionsTnt;
            int fuse = Durations.T30(w.Config, cfg.FuseTicks);
            double total = fuse / w.Config.SimHz;
            float prev = float.MaxValue;
            for (int k = 0; k <= 10; k++)
            {
                // From the throw (fuse left 1) to the blast (0), read from Bomb ticks the way the view reads them.
                int thrown = 1000, blast = thrown + fuse;
                double now = thrown + fuse * (k / 10.0);
                float f = GrenadeRead.FuseLeft01(now, thrown, blast);
                float p = GrenadeRead.BlinkPeriod(f);
                Print("blink at fuse left " + F(f) + ": period " + F(p) + " s, limit below the previous " + (prev == float.MaxValue ? "-" : F(prev)) + " s");
                Assert.Less(p, prev, "the blink only gets faster");
                prev = p;
            }
            // Blinks in the first and the last third of the fuse (the closed-form phase).
            double first = GrenadeRead.BlinkPhase(total * 2 / 3, total) - GrenadeRead.BlinkPhase(total, total);
            double last = GrenadeRead.BlinkPhase(0, total) - GrenadeRead.BlinkPhase(total / 3, total);
            Print("blinks over a " + F(total) + " s fuse: first third " + F(first) + ", last third " + F(last) + ", limit last > first");
            Assert.Greater(last, first, "the last third blinks faster than the first");
            double prevPhase = -1;
            for (int k = 0; k <= 20; k++)
            {
                double ph = GrenadeRead.BlinkPhase(total * (1 - k / 20.0), total);
                Assert.GreaterOrEqual(ph, prevPhase, "the phase never runs backwards");
                prevPhase = ph;
            }
        }

        [Test]
        public void AnEnemyInsideTheRingIsWarnedAndOneOutsideIsNot()
        {
            World w = Field();
            DemolitionsTntConfig cfg = w.Config.Abilities.DemolitionsTnt;
            double r = w.Config.Class(ClassId.Warrior).Radius;
            const double bx = 20, bz = 20;
            double din = cfg.BlastRadius * 0.75, dout = cfg.BlastRadius + r + 0.3;
            bool inEnemy = GrenadeRead.Warns(TeamId.Azure, TeamId.Crimson, GrenadeRead.Inside(bx + din, bz, r, bx, bz, cfg.BlastRadius));
            bool outEnemy = GrenadeRead.Warns(TeamId.Azure, TeamId.Crimson, GrenadeRead.Inside(bx + dout, bz, r, bx, bz, cfg.BlastRadius));
            bool inMate = GrenadeRead.Warns(TeamId.Crimson, TeamId.Crimson, GrenadeRead.Inside(bx + din, bz, r, bx, bz, cfg.BlastRadius));
            bool edge = GrenadeRead.Inside(bx + cfg.BlastRadius + r - 1e-6, bz, r, bx, bz, cfg.BlastRadius);
            Print("warning: enemy at " + F(din) + " u -> " + inEnemy + " (limit true); enemy at " + F(dout) + " u -> " + outEnemy
                  + " (limit false); teammate at " + F(din) + " u -> " + inMate + " (limit false); edge " + F(cfg.BlastRadius + r) + " u inside -> " + edge);
            Assert.IsTrue(inEnemy, "an enemy inside is warned");
            Assert.IsFalse(outEnemy, "an enemy outside is not");
            Assert.IsFalse(inMate, "the thrower's team is not (TNT does not hurt it)");
            Assert.IsTrue(edge, "the Sim's reach (blast + body radius) counts as inside");
        }

        [Test]
        public void MinesAreNeverShownToTheEnemyTeam()
        {
            World w = Field();
            Mine[] ms = w.Demolitions.Mines;
            Assert.Greater(ms.Length, 0);
            MapDef m = w.Map;
            int cx = JsMath.FloorToInt(w.X[Me]) + 2, cz = JsMath.FloorToInt(w.Z[Me]);
            Mine mi = ms[0];
            mi.Active = true;
            mi.Team = TeamId.Azure;
            mi.Owner = Me;
            mi.Cx = Math.Max(0, Math.Min(m.Width - 1, cx));
            mi.Cz = Math.Max(0, Math.Min(m.Height - 1, cz));
            mi.Armed = true;
            int own = GrenadeRead.MineRingsFor(w, TeamId.Azure), enemy = GrenadeRead.MineRingsFor(w, TeamId.Crimson);
            Print("mine rings: owner team sees " + own + " (limit 1), enemy team sees " + enemy + " (limit 0)");
            Assert.AreEqual(1, own, "the owner's team sees its mine ring");
            Assert.AreEqual(0, enemy, "the enemy team never does");
            foreach (TeamId a in TeamIds.All)
            foreach (TeamId b in TeamIds.All)
            {
                bool shown = GrenadeRead.MineRingVisible(a, b);
                Print("mine of " + a + " seen by " + b + ": " + shown + " (limit " + (a == b) + ")");
                Assert.AreEqual(a == b, shown);
            }
        }
    }
}
