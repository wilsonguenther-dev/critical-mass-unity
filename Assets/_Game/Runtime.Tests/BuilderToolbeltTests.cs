using System;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using B = CriticalMass.Runtime.Input.HumanInputState.HumanButton;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b14 (owner: "one clear Builder scheme", "ladder and bridge direction"): the Builder's one tool wheel. The ladder /
    /// bridge axis at eight facings with its hysteresis, the wheel order (next / previous, the tall wall only from tier 1),
    /// the sim selection the wheel converges to, and the routing of the one "use tool" key (F; the ability button is an
    /// alias): a climb tool sends the ability press, every other tool the dig / place / build press.
    /// </summary>
    public sealed class BuilderToolbeltTests
    {
        static void Axis(double deg, int pdx, int pdz, out int dx, out int dz)
        {
            double r = deg * Math.PI / 180.0;
            BuilderToolbelt.ChooseAxis(Math.Cos(r), Math.Sin(r), pdx, pdz, out dx, out dz);
        }

        [Test]
        public void TheAxisAtEightFacingsIsTheNearestGridAxis()
        {
            // cardinals: exactly that axis, from no axis and from every other axis that is more than 60 degrees away
            int[,] card = { { 1, 0 }, { 0, 1 }, { -1, 0 }, { 0, -1 } };
            for (int k = 0; k < 8; k++)
            {
                double deg = k * 45.0;
                Axis(deg, 0, 0, out int dx, out int dz);
                Debug.Log($"[CM.Toolbelt] facing {deg:0} deg, no axis yet -> ({dx},{dz})");
                Assert.AreEqual(1, Math.Abs(dx) + Math.Abs(dz), "one grid axis at " + deg);
                if (k % 2 == 0)
                {
                    Assert.AreEqual(card[k / 2, 0], dx, "x at " + deg);
                    Assert.AreEqual(card[k / 2, 1], dz, "z at " + deg);
                }
                else
                {
                    // a diagonal: one of its two neighbouring axes, and each of them is kept (no flip-flop on the diagonal)
                    int ax = card[(k - 1) / 2, 0], az = card[(k - 1) / 2, 1], bx = card[(k + 1) / 2 % 4, 0], bz = card[(k + 1) / 2 % 4, 1];
                    Assert.IsTrue((dx == ax && dz == az) || (dx == bx && dz == bz), "a neighbouring axis at " + deg);
                    Axis(deg, ax, az, out int kx, out int kz);
                    Assert.AreEqual(ax, kx, "keeps the previous axis on the diagonal " + deg);
                    Assert.AreEqual(az, kz);
                    Axis(deg, bx, bz, out kx, out kz);
                    Assert.AreEqual(bx, kx, "keeps the other previous axis on the diagonal " + deg);
                    Assert.AreEqual(bz, kz);
                    Debug.Log($"[CM.Toolbelt] facing {deg:0} deg keeps ({ax},{az}) and ({bx},{bz})");
                }
            }
        }

        [Test]
        public void TheAxisHasHysteresisAndTurnsPastSixtyDegrees()
        {
            Axis(50, 1, 0, out int dx, out int dz);
            Assert.AreEqual((1, 0), (dx, dz), "50 degrees off +x keeps +x");
            Axis(40, 0, 1, out dx, out dz);
            Assert.AreEqual((0, 1), (dx, dz), "50 degrees off +z keeps +z");
            Axis(70, 1, 0, out dx, out dz);
            Assert.AreEqual((0, 1), (dx, dz), "70 degrees off +x turns to +z");
            Axis(180, 1, 0, out dx, out dz);
            Assert.AreEqual((-1, 0), (dx, dz), "turning around flips it");
            BuilderToolbelt.ChooseAxis(0, 0, 0, -1, out dx, out dz);
            Assert.AreEqual((0, -1), (dx, dz), "no look keeps the axis");
        }

        [Test]
        public void TheWheelStepsThroughEveryToolInOrder()
        {
            BuilderTool t = BuilderTool.Dig;
            string seen = "";
            for (int n = 0; n < BuilderToolbelt.Count; n++)
            {
                seen += BuilderToolbelt.Word(t, 1) + " > ";
                t = BuilderToolbelt.Next(t, 1, true, true, true);
            }
            Debug.Log("[CM.Toolbelt] wheel: " + seen);
            Assert.AreEqual("DIG > PLACE > LADDER > BRIDGE > SHORT WALL > TALL WALL > BLOCK > ", seen);
            Assert.AreEqual(BuilderTool.Dig, t, "and back to dig");
            Assert.AreEqual(BuilderTool.Block, BuilderToolbelt.Next(BuilderTool.Dig, -1, true, true, true), "back steps the other way");
            Assert.AreEqual(BuilderTool.Block, BuilderToolbelt.Next(BuilderTool.ShortWall, 1, true, true, false), "no tall wall before tier 1");
            Assert.AreEqual(BuilderTool.Ladder, BuilderToolbelt.Next(BuilderTool.Block, 1, true, false, false), "no dig / place without the terrain rule");
            Assert.AreEqual(BuilderTool.Bridge, BuilderToolbelt.Next(BuilderTool.Ladder, 1, false, false, false), "ladder and bridge always");
            Assert.AreEqual("TALL WALL x5", BuilderToolbelt.Word(BuilderTool.TallWall, 2));
        }

        [Test]
        public void TheWheelConvergesTheSimSelection()
        {
            Assert.AreEqual(2, BuilderToolbelt.StepsTo(BuilderTool.Dig, BuilderTool.ShortWall, true, true, true));
            Assert.AreEqual(0, BuilderToolbelt.StepsTo(BuilderTool.Place, BuilderTool.Place, true, true, true));
            Assert.AreEqual(-1, BuilderToolbelt.StepsTo(BuilderTool.ShortWall, BuilderTool.TallWall, true, true, false), "no tall wall before tier 1");
            Assert.IsTrue(BuilderToolbelt.SimTarget(BuilderTool.Ladder, true, true, out BuilderTool sim));
            Assert.AreEqual(BuilderTool.Place, sim, "a climb tool parks the sim on PLACE (no wall ghost)");
            Assert.IsFalse(BuilderToolbelt.SimTarget(BuilderTool.Bridge, true, false, out _), "nothing to park on without terrain");
        }

        static World NewWorld(ClassId cls)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions { Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = cls, Config = cfg });
            w.Actors[0].StandUp = 0;
            return w;
        }

        static void Press(HumanInputState st, InputFrame f, B button, int fills, out int ability, out int terraform)
        {
            ability = terraform = 0;
            st.ButtonDown(button);
            for (int n = 0; n < fills; n++)
            {
                st.Fill(f);
                if (f.Ability) ability++;
                if (f.Terraform) terraform++;
                if (n == 0) st.ButtonUp(button);
            }
        }

        [Test]
        public void TheUseKeyBuildsTheSelectedToolOnly()
        {
            World w = NewWorld(ClassId.Builder);
            var st = new HumanInputState { BufferWorld = w };
            InputFrame f = SimInput.EmptyInput();
            BuilderToolbelt.Rules(w, 0, out bool walls, out bool terrain, out bool tall);
            Debug.Log($"[CM.Toolbelt] rules: walls {walls}, terrain {terrain}, tall {tall}");

            st.PickBuilderTool((int)BuilderTool.Ladder);
            Press(st, f, B.DigPlace, 6, out int ab, out int tf);
            Debug.Log($"[CM.Toolbelt] LADDER + F: ability {ab}, terraform {tf}, aim ({f.AimX:0.0},{f.AimZ:0.0})");
            Assert.AreEqual(BuilderTool.Ladder, st.Toolbelt.Selected);
            Assert.AreEqual(1, ab, "F with the ladder = one build press");
            Assert.AreEqual(0, tf, "and never a dig");

            st.PickBuilderTool((int)BuilderTool.Bridge);
            Press(st, f, B.Ability, 6, out ab, out tf);
            Assert.AreEqual(1, ab, "the ability button is an alias of F for the Builder");
            Assert.AreEqual(0, tf);

            BuilderTool other = terrain ? BuilderTool.Dig : walls ? BuilderTool.ShortWall : BuilderTool.Ladder;
            if (other == BuilderTool.Ladder) Assert.Inconclusive("no dig and no walls under this rule set");
            st.PickBuilderTool((int)other);
            Press(st, f, B.DigPlace, 8, out ab, out tf);
            Debug.Log($"[CM.Toolbelt] {other} + F: ability {ab}, terraform {tf}");
            Assert.AreEqual(other, st.Toolbelt.Selected);
            Assert.AreEqual(0, ab, other + " never sends the ability");
            Assert.AreEqual(1, tf, other + " + F = one press");
        }

        [Test]
        public void OtherClassesKeepTheirAbilityButton()
        {
            World w = NewWorld(ClassId.Warrior);
            var st = new HumanInputState { BufferWorld = w };
            InputFrame f = SimInput.EmptyInput();
            st.PickBuilderTool(2);
            st.ButtonDown(B.Ability);
            st.Fill(f);
            Assert.IsTrue(f.Ability, "B / right-click is the class ability");
        }
    }
}
