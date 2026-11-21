using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b14-c item 2: the Kaiju idle bug. TNT craters at the shrine bridge's mouths stranded the hatched Kaiju on the shrine island
    /// (16v16 seeds 1 and 25: no target for the whole 240 s it was owned). The CM-30 route guard now also keeps the Kaiju's body
    /// route to the hill (Kaiju.RouteGuard.cs). Every case prints its numbers.
    /// </summary>
    [TestFixture]
    public sealed class KaijuRouteGuardTests
    {
        /// <summary>The crater cells measured on seeds 1 and 25 (diag 44157934512): both bridge mouths and the hatch ground.</summary>
        static readonly int[] Carve =
        {
            55, 15, 56, 15, 57, 15, 56, 16, 57, 16, 54, 25, 53, 26, 54, 26,
            50, 12, 51, 12, 52, 12, 53, 12, 51, 13, 52, 13, 53, 13, 55, 13, 56, 13, 57, 13, 58, 13, 59, 13, 54, 23, 54, 24,
        };

        static int CarveThroughTheGuard(World w, out int applied)
        {
            int refused = 0;
            applied = 0;
            for (int k = 0; k < Carve.Length; k += 2)
            {
                int cx = Carve[k], cz = Carve[k + 1];
                byte t = w.Map.Cells[cz * w.Map.Width + cx];
                if (t != Cell.Floor && t != Cell.Bridge) continue;
                if (!Terraform.DigKeepsRoutes(w, cz * w.Map.Width + cx)) { refused++; continue; }
                MapGrid.SetCell(w, cx, cz, Cell.Void);
                applied++;
            }
            return refused;
        }

        [Test]
        public void TheShrineBridgeCratersThatStrandedTheKaijuAreRefusedAndItsRouteToTheHillHolds()
        {
            double hx = 56, hz = 14;   // the hatch spot: shrine (56, 8) + HatchOffsetZ 6
            // control: the same carve with the Kaiju clause off (crush rule off switches the clause off): the body is cut off
            GameConfig c0 = GameConfig.CreateDefault();
            c0.Physics.KaijuHillSiege = false;
            World w0 = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c0 });
            bool before0 = Kaiju.BodyReachesHillFrom(w0, hx, hz);
            int refused0 = CarveThroughTheGuard(w0, out int applied0);
            bool after0 = Kaiju.BodyReachesHillFrom(w0, hx, hz);
            // the rule (the game's default)
            World w = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault() });
            bool before = Kaiju.BodyReachesHillFrom(w, hx, hz);
            int refused = CarveThroughTheGuard(w, out int applied);
            bool after = Kaiju.BodyReachesHillFrom(w, hx, hz);
            Console.WriteLine("[b14-c route guard] control (Kaiju clause off): body reaches the hill before " + before0 + ", craters applied " + applied0 + " refused " + refused0 + ", after " + after0 + " (expected False: the stranding)");
            Console.WriteLine("[b14-c route guard] rule on: body reaches the hill before " + before + ", craters applied " + applied + " refused " + refused + ", after " + after + " (limit True, at least 1 refused)");
            Assert.IsTrue(before0 && before, "the hatch spot reaches the hill on the fresh map");
            Assert.IsFalse(after0, "the measured craters strand the Kaiju without the clause (else the test proves nothing)");
            Assert.Greater(refused, refused0, "the clause refuses the stranding craters");
            Assert.IsTrue(after, "the Kaiju's route from its hatch spot to the hill holds");
            Assert.Greater(applied, 0, "craters away from the route still carve");
        }

        [Test]
        public void ClassicAndPhysicsOnlyRouteGuardIsUnchanged()
        {
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                World w = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault().ApplyRules(p) });
                int refused = CarveThroughTheGuard(w, out int applied);
                GameConfig c0 = GameConfig.CreateDefault().ApplyRules(p);
                c0.Physics.KaijuHillSiege = false;
                World w0 = World.CreateWorld(new WorldOptions { Seed = 1, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c0 });
                int refused0 = CarveThroughTheGuard(w0, out int applied0);
                Console.WriteLine("[b14-c route guard] " + p + ": refused " + refused + " applied " + applied + " (the pads-only guard: refused " + refused0 + " applied " + applied0 + "; limit equal)");
                Assert.AreEqual(refused0, refused);
                Assert.AreEqual(applied0, applied);
            }
        }

        /// <summary>Longest stretch with an owned or wild Kaiju up, no target, off the hill while it has enemies alive (s).</summary>
        internal static double LongestNoTarget(int seed, out string where)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            HillDef hill = w.Map.Hill;
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + 30;
            double run = 0, worst = 0, dt = 1.0 / cfg.SimHz;
            where = "";
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                Kaiju k = w.Kaiju;
                bool up = w.Match.Hatched && k.KoIn <= 0 && (k.State == KaijuState.Azure || k.State == KaijuState.Crimson || k.State == KaijuState.Berserk);
                double ex = k.X - hill.X, ez = k.Z - hill.Z;
                bool enemies = false;
                if (up)
                    for (int i = 0; i < w.ActorCount && !enemies; i++)
                    {
                        Actor a = w.Actors[i];
                        enemies = a.Alive && (k.State == KaijuState.Berserk || (k.State == KaijuState.Azure) == (a.Team == TeamId.Crimson));
                    }
                bool idle = up && enemies && k.TargetId < 0 && k.MeleeWindupIn <= 0 && ex * ex + ez * ez > hill.R * hill.R
                            && !Kaiju.AtIdlePost(w, k.X, k.Z);   // b16: the rim post is where it waits on purpose
                run = idle ? run + dt : 0;
                if (run > worst) { worst = run; where = "ending " + w.Match.Time.ToString("F0") + " s at (" + k.X.ToString("F1") + ", " + k.Z.ToString("F1") + "), body reaches the hill " + Kaiju.BodyReachesHillFrom(w, k.X, k.Z); }
            }
            return worst;
        }

        /// <summary>The measured seed: on 16v16 seed 1 the Kaiju stood 240 s on the shrine island; now it never idles 20 s.</summary>
        [Test]
        public void Seed1TheKaijuIsNeverLeftWithNoTargetOffTheHillFor20Seconds()
        {
            double worst = LongestNoTarget(1, out string where);
            Console.WriteLine("[b14-c kaiju idle] 16v16 seed 1: longest no-target stretch off the hill " + worst.ToString("F1") + " s " + where + " (limit 20 s; was 240.0 s)");
            Assert.LessOrEqual(worst, 20.0);
        }

        /// <summary>The scan: 16v16 seeds 1 to 40 (the game's rules): no Kaiju with no target off the hill for over 20 s while it has enemies.</summary>
        [Test, Category("Slow")]
        public void FortySeedsNoKaijuIdlesWithNoTargetOffTheHillOver20Seconds()
        {
            int over = 0;
            string list = "";
            for (int seed = 1; seed <= 40; seed++)
            {
                double worst = LongestNoTarget(seed, out string where);
                Console.WriteLine("[b14-c kaiju idle] 16v16 seed " + seed + ": longest no-target stretch off the hill " + worst.ToString("F1") + " s " + where);
                if (worst > 20.0) { over++; list += " " + seed; }
            }
            Console.WriteLine("[b14-c kaiju idle] seeds over 20 s: " + over + " of 40" + (over > 0 ? " (" + list.Trim() + ")" : "") + " (limit 0; before the fix 3: seeds 1, 19, 25)");
            Assert.AreEqual(0, over);
        }
    }
}
