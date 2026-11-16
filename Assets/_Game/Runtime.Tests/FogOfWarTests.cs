using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Fog;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Batch 12 item P: fog of war (view only). The pure sight function, the enemy edge, the Classic / setting switches,
    /// the 10 Hz clock and the allocation-free update. Every case prints what it measured.
    /// </summary>
    public class FogOfWarTests
    {
        const double MapW = 112, MapH = 72;   // Skybreak Basin's size; any size works
        const int Res = FogConfig.Resolution;

        static void Print(string s) => Debug.Log("[FogOfWarTests] " + s);

        static int CountWithin(double x, double z, double r)
        {
            int n = 0;
            double csx = MapW / Res, csz = MapH / Res;
            for (int cz = 0; cz < Res; cz++)
                for (int cx = 0; cx < Res; cx++)
                {
                    double dx = (cx + 0.5) * csx - x, dz = (cz + 0.5) * csz - z;
                    if (dx * dx + dz * dz <= r * r) n++;
                }
            return n;
        }

        [Test]
        public void SightMarksTeammateHeldHillAndBaseCellsAndNothingElse()
        {
            // three discs far apart: a teammate (12 u), the held hill (10 u), the base (its zone, at least 9 u)
            var src = new[]
            {
                new FogSource(30, 20, FogConfig.SightTeammate),
                new FogSource(70, 50, FogConfig.SightHeldHill),
                new FogSource(100, 12, FogConfig.BaseSight),
            };
            var buf = new byte[Res * Res];
            FogSight.Compute(src, src.Length, MapW, MapH, Res, buf);
            double csx = MapW / Res, csz = MapH / Res;
            int marked = 0, wrong = 0;
            for (int cz = 0; cz < Res; cz++)
                for (int cx = 0; cx < Res; cx++)
                {
                    bool want = FogSight.SeenAt(src, src.Length, (cx + 0.5) * csx, (cz + 0.5) * csz);
                    bool got = FogSight.Marked(buf[cz * Res + cx]);
                    if (got) marked++;
                    if (got != want) wrong++;
                }
            int teammate = CountWithin(30, 20, FogConfig.SightTeammate), hill = CountWithin(70, 50, FogConfig.SightHeldHill), bas = CountWithin(100, 12, FogConfig.BaseSight);
            Print("cells " + Res + "x" + Res + " over " + MapW + " x " + MapH + " u: teammate 12 u = " + teammate + " cells, held hill 10 u = " + hill +
                  " cells, base 9 u = " + bas + " cells, expected total " + (teammate + hill + bas) + ", marked " + marked + ", cells marked wrongly " + wrong + " (limit 0)");
            Assert.AreEqual(0, wrong, "every cell is marked exactly when its centre is inside a disc");
            Assert.AreEqual(teammate + hill + bas, marked, "the three discs do not overlap, so the marked count is their sum and nothing else");

            // nothing at all: no sources, no marked cell
            FogSight.Compute(src, 0, MapW, MapH, Res, buf);
            int any = 0;
            for (int i = 0; i < buf.Length; i++) if (buf[i] != 0) any++;
            Print("no sources: non-zero cells " + any + " (limit 0)");
            Assert.AreEqual(0, any);
        }

        [Test]
        public void GatherTakesLivingTeammatesTheHillOnlyWhenHeldAndTheBase()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            var buf = new FogSource[w.ActorCount + 2];
            int living = 0;
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Alive && w.Actors[i].Team == TeamId.Azure) living++;
            w.Match.HillState = HillState.Neutral;
            int neutral = FogSight.Gather(w, TeamId.Azure, buf);
            w.Match.HillState = HillState.Azure;
            int held = FogSight.Gather(w, TeamId.Azure, buf);
            bool hillDisc = false, baseDisc = false;
            for (int k = 0; k < held; k++)
            {
                if (buf[k].X == w.Map.Hill.X && buf[k].Z == w.Map.Hill.Z && buf[k].R == FogConfig.SightHeldHill) hillDisc = true;
                if (buf[k].X == w.Map.Pads[(int)TeamId.Azure].X && buf[k].Z == w.Map.Pads[(int)TeamId.Azure].Z) baseDisc = buf[k].R >= FogConfig.BaseSight;
            }
            w.Match.HillState = HillState.Crimson;
            int enemyHeld = FogSight.Gather(w, TeamId.Azure, buf);
            Print("living Azure " + living + "; sources hill neutral " + neutral + " (want " + (living + 1) + "), held by Azure " + held + " (want " + (living + 2) +
                  ", hill disc " + hillDisc + ", base disc " + baseDisc + " r " + FogSight.BaseRadius(w, TeamId.Azure).ToString("0.0") + "), held by Crimson " + enemyHeld);
            Assert.AreEqual(living + 1, neutral);
            Assert.AreEqual(living + 2, held);
            Assert.AreEqual(living + 1, enemyHeld);
            Assert.IsTrue(hillDisc && baseDisc);
        }

        [Test]
        public void EnemyAt12_5IsHiddenAndAt11_5IsShown()
        {
            var src = new[] { new FogSource(50, 30, FogConfig.SightTeammate) };
            bool far = FogSight.SeenAt(src, 1, 50 + 12.5, 30), near = FogSight.SeenAt(src, 1, 50 + 11.5, 30);
            bool diagFar = FogSight.SeenAt(src, 1, 50 + 12.5 / Math.Sqrt(2), 30 + 12.5 / Math.Sqrt(2)), diagNear = FogSight.SeenAt(src, 1, 50 + 11.5 / Math.Sqrt(2), 30 - 11.5 / Math.Sqrt(2));
            Print("teammate sight " + FogConfig.SightTeammate + " u: enemy at 12.5 u seen=" + far + " (want false), at 11.5 u seen=" + near +
                  " (want true); diagonal 12.5 seen=" + diagFar + ", diagonal 11.5 seen=" + diagNear);
            Assert.IsFalse(far);
            Assert.IsTrue(near);
            Assert.IsFalse(diagFar);
            Assert.IsTrue(diagNear);
        }

        [Test]
        public void ClassicProfileSpectatorsResultsAndTheSettingTurnTheFogOff()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            bool full = FogConfig.ProfileAllows(cfg);
            cfg.Depth.Classic = true;
            bool classic = FogConfig.ProfileAllows(cfg);
            bool onFull = FogConfig.WantFog(true, false, false, false, false);
            bool onClassic = FogConfig.WantFog(true, true, false, false, false);
            bool settingOff = FogConfig.WantFog(false, false, false, false, false);
            bool spectating = FogConfig.WantFog(true, false, true, false, false);
            bool results = FogConfig.WantFog(true, false, false, true, false);
            bool title = FogConfig.WantFog(true, false, false, false, true);
            Print("profile allows: Full " + full + " (want true), Classic " + classic + " (want false); fog drawn: Full " + onFull + ", Classic " + onClassic +
                  ", setting off " + settingOff + ", spectating " + spectating + ", results " + results + ", title " + title);
            Assert.IsTrue(full);
            Assert.IsFalse(classic);
            Assert.IsTrue(onFull);
            Assert.IsFalse(onClassic || settingOff || spectating || results || title);

            bool? batch = FogConfig.CommandLinePin(new string[0], true), human = FogConfig.CommandLinePin(new string[0], false);
            bool? shots = FogConfig.CommandLinePin(new[] { "x", "-fogShots" }, true), pinOn = FogConfig.CommandLinePin(new[] { "-fog", "on" }, true), pinOff = FogConfig.CommandLinePin(new[] { "-fog", "off" }, false);
            Print("command line: batch " + batch + " (want False), player " + (human.HasValue ? human.ToString() : "none") + " (want none), -fogShots " + shots +
                  ", -fog on " + pinOn + ", -fog off " + pinOff);
            Assert.AreEqual(false, batch);
            Assert.IsNull(human);
            Assert.AreEqual(true, shots);
            Assert.AreEqual(true, pinOn);
            Assert.AreEqual(false, pinOff);
            Assert.IsTrue(global::CriticalMass.Runtime.Menu.SettingsExtras.FogOfWarKey.Length > 0);
            CollectionAssert.Contains(global::CriticalMass.Runtime.Menu.SettingsExtras.AllKeys, global::CriticalMass.Runtime.Menu.SettingsExtras.FogOfWarKey, "reset clears it");
        }

        [Test]
        public void UpdatesTenTimesASecondAndAllocateNothingAfterWarmUp()
        {
            foreach (double fps in new[] { 30.0, 60.0, 144.0 })
            {
                var clock = new FogClock();
                int fires = 0, frames = (int)Math.Round(fps);
                for (int f = 0; f < frames; f++) if (clock.Step(1.0 / fps)) fires++;
                Print(fps + " fps for 1 s: " + fires + " updates (want " + FogConfig.UpdateHz + ")");
                Assert.AreEqual((int)FogConfig.UpdateHz, fires);
            }

            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            var scratch = new InputFrame();
            var grid = new FogGrid();
            for (int i = 0; i < 5; i++) { MatchTick.AdvanceNoHuman(w, scratch); grid.Update(w, TeamId.Azure, i == 0); }
            long bytes = 0;
            for (int u = 0; u < 50; u++)
            {
                for (int t = 0; t < 6; t++) MatchTick.AdvanceNoHuman(w, scratch);   // the sim moves on between updates (not measured)
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                grid.Update(w, TeamId.Azure, false);
                bytes += GC.GetAllocatedBytesForCurrentThread() - b0;
            }
            int seenCells = 0;
            for (int c = 0; c < grid.Sight.Length; c++) if (FogSight.Marked(grid.Sight[c])) seenCells++;
            Print("50 updates after warm-up: allocated " + bytes + " bytes (limit 0); sources " + grid.SourceCount + ", seen cells " + seenCells + " of " + grid.Sight.Length +
                  ", fade step " + FogConfig.FadeStep + " per update");
            Assert.AreEqual(0, bytes);
            Assert.Greater(seenCells, 0);
            Assert.Less(seenCells, grid.Sight.Length);
        }
    }
}
