using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// Port of src/sim/jump.test.ts (09e0904), CM-32 jumping, plus the DG-02 additions (coyote time and jump buffer, not in
    /// the browser). All timing in sim ticks (Dt = 1 / SimHz), never wall clock. Each test paints its own walled arena in a
    /// corner of the map (SetCell) and parks every other actor dead (RespawnIn huge) so only actor 0 moves; no bots run in
    /// Step.
    /// </summary>
    [TestFixture]
    public sealed class JumpTests
    {
        private static readonly GameConfig Cfg = GameConfig.CreateBrowser5v5();
        private static readonly double Dt = SimStep.Dt(Cfg);
        private static readonly int J = (int)Cfg.Jump.AirTicks;
        private static readonly int Min = (int)Cfg.Jump.MinAirTicks;
        /// <summary>Arena rows (z) and columns (x): walls on the border, FLOOR inside. Actor 0 runs along row Row in +x.</summary>
        private const int Z0 = 60, Z1 = 66, X0 = 2, X1 = 40, GapX = 15;
        private const double Row = 63.5;
        private static readonly InputFrame Frame = SimInput.EmptyInput();

        private static World Arena(int gapWidth)
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 1,
                TeamSize = 5,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            });
            for (int cz = Z0; cz <= Z1; cz++)
            {
                for (int cx = X0; cx <= X1; cx++)
                {
                    bool border = cz == Z0 || cz == Z1 || cx == X0 || cx == X1;
                    bool gap = cx >= GapX && cx < GapX + gapWidth;
                    MapGrid.SetCell(w, cx, cz, border ? Cell.Wall : gap ? Cell.Void : Cell.Floor);
                }
            }
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
            }
            Actor a = w.Actors[0];
            a.Alive = true;
            a.StandUp = 0;
            a.SpawnProtect = 0;
            w.X[0] = 8.5;
            w.Z[0] = Row;
            w.Vx[0] = 0;
            w.Vz[0] = 0;
            return w;
        }

        private static void Tick(World w, double mx, bool jump)
        {
            Frame.MoveX = mx;
            Frame.MoveZ = 0;
            Frame.AimX = double.NaN;
            Frame.AimZ = double.NaN;
            Frame.Jump = jump;
            SimInput.ApplyInput(w, 0, Frame);
            SimStep.Step(w, Dt);
        }

        /// <summary>Run +x until actor 0's leading edge is within lead of the gap's near edge (x = GapX). Returns the ticks used.</summary>
        private static int RunUpTo(World w, double lead)
        {
            double r = Collide.ActorRadius(w, 0);
            int n = 0;
            while (w.X[0] + r < GapX - lead && n < 300)
            {
                Tick(w, 1, false);
                n++;
            }
            return n;
        }

        [Test]
        public void InputFrameJumpStartsAnAirborneStateOfJTicksTheLiftRisesPeaksAtJ2AndIsBackTo0AtJ()
        {
            World w = Arena(1);
            Actor a = w.Actors[0];
            Tick(w, 0, false);
            Assert.AreEqual(0, a.AirLen);
            var lift = new List<double>();
            Tick(w, 0, true); // takeoff step = air tick 1
            Assert.AreEqual(J, a.AirLen);
            lift.Add(Jump.JumpLift(w.Config, a, Jump.JumpPhase(a, 1)));
            for (int k = 2; k <= J; k++)
            {
                Tick(w, 0, false);
                if (k < J)
                {
                    Assert.AreEqual(k, a.AirTick);
                    Assert.AreEqual(J, a.AirLen);
                }
                lift.Add(Jump.JumpLift(w.Config, a, Jump.JumpPhase(a, 1)));
            }
            // Landed at the end of tick J: grounded, lift 0.
            Assert.AreEqual(0, a.AirLen);
            Assert.AreEqual(0.0, lift[J - 1]);
            Assert.Greater(lift[0], 0);
            int peak = 0;
            for (int k = 1; k < J; k++) if (lift[k] > lift[peak]) peak = k;
            // lift[k - 1] is tick k; the parabola 4h u (1 - u) peaks at u = 1/2, tick J/2.
            Assert.AreEqual((int)JsMath.Round(J / 2.0), peak + 1);
            Assert.AreEqual(Cfg.Jump.ApexUnits, lift[peak], 5e-7);
            for (int k = 1; k <= peak; k++) Assert.Greater(lift[k], lift[k - 1]);
            for (int k = peak + 1; k < J; k++) Assert.Less(lift[k], lift[k - 1]);
            // Standing still: lands where it took off.
            Assert.AreEqual(8.5, w.X[0], 5e-6);
        }

        [Test]
        public void TheJumpFlagIsOneShotAHeldStoredTrueDoesNotChainJumpsAndAPressMidAirIsIgnored()
        {
            World w = Arena(1);
            Actor a = w.Actors[0];
            Tick(w, 0, true);
            Assert.AreEqual(J, a.AirLen);
            // The sim cleared the stored flag; stepping with no new frame keeps flying, then stays down.
            for (int k = 2; k <= J; k++) SimStep.Step(w, Dt);
            Assert.AreEqual(0, a.AirLen);
            SimStep.Step(w, Dt);
            Assert.AreEqual(0, a.AirLen);
            Tick(w, 0, true);
            Tick(w, 0, true); // mid-air press: no restart
            Assert.AreEqual(2, a.AirTick);
        }

        [Test]
        public void A1CellVoidGapBlocksWalking()
        {
            World w = Arena(1);
            double r = Collide.ActorRadius(w, 0);
            for (int k = 0; k < 90; k++) Tick(w, 1, false);
            Assert.Less(w.X[0] + r, GapX);
            Assert.IsTrue(Collide.FootprintClear(w, 0, w.X[0], w.Z[0]));
        }

        [Test]
        public void AnAirborneActorCrossesThe1CellVoidGapAndLandsOnTheFarSide()
        {
            World w = Arena(1);
            Actor a = w.Actors[0];
            double r = Collide.ActorRadius(w, 0);
            RunUpTo(w, 0.3);
            Tick(w, 1, true);
            Assert.AreEqual(J, a.AirLen);
            bool overGap = false;
            for (int k = 2; k <= J; k++)
            {
                Tick(w, 1, false);
                if (w.X[0] + r > GapX && w.X[0] - r < GapX + 1) overGap = true;
            }
            Assert.IsTrue(overGap);
            Assert.AreEqual(0, a.AirLen);
            Assert.IsTrue(a.Alive);
            Assert.Greater(w.X[0] - r, GapX + 1);
            Assert.IsTrue(Collide.FootprintClear(w, 0, w.X[0], w.Z[0]));
        }

        [Test]
        public void AJumpThatWouldLandOnVoidIsShortenedLandsBeforeTheEdgeOrRefusedNeverEndsOnABlockedCell()
        {
            // Wide gap (5 cells, beyond a full jump): pressed 1.0 u before the edge the landing guard shortens the jump.
            World w = Arena(5);
            Actor a = w.Actors[0];
            double r = Collide.ActorRadius(w, 0);
            RunUpTo(w, 1.0);
            Tick(w, 1, true);
            Assert.GreaterOrEqual(a.AirLen, Min);
            Assert.Less(a.AirLen, J);
            int len = a.AirLen;
            for (int k = 2; k <= len; k++) Tick(w, 1, false);
            Assert.AreEqual(0, a.AirLen);
            Assert.Less(w.X[0] + r, GapX);
            Assert.IsTrue(Collide.FootprintClear(w, 0, w.X[0], w.Z[0]));

            // Pressed at the edge, no landing of Min ticks or more is clear: refused, the actor stays grounded and blocked.
            World w2 = Arena(5);
            Actor a2 = w2.Actors[0];
            RunUpTo(w2, 0.2);
            Tick(w2, 1, true);
            Assert.AreEqual(0, a2.AirLen);
            for (int k = 0; k < 2 * J; k++)
            {
                Tick(w2, 1, k % 3 == 0);
                Assert.IsTrue(a2.Alive);
                if (a2.AirLen == 0) Assert.IsTrue(Collide.FootprintClear(w2, 0, w2.X[0], w2.Z[0]));
            }
            Assert.Less(w2.X[0] + r, GapX);
        }

        [Test]
        public void IsDeterministicTheSameInputsGiveTheSamePositions()
        {
            Func<double[]> run = () =>
            {
                World w = Arena(1);
                RunUpTo(w, 0.3);
                Tick(w, 1, true);
                for (int k = 0; k < J + 5; k++) Tick(w, 1, false);
                return new[] { w.X[0], w.Z[0], w.Vx[0], w.Tick };
            };
            CollectionAssert.AreEqual(run(), run());
        }

        // ---- DG-02 additions (no vitest twin) ----

        [Test]
        public void DG02CoyoteAndBufferAre3TicksEachAt30Hz()
        {
            Assert.AreEqual(3, Jump.CoyoteTicks(Cfg));
            Assert.AreEqual(3, Jump.BufferTicks(Cfg));
        }

        /// <summary>
        /// Runs +x, then removes the floor under actor 0 (its ledge leaves it: the only way a grounded body loses its footing
        /// in a sim where walking never leaves walkable cells), steps delay ticks, then presses jump.
        /// </summary>
        private static World LedgeLeaves(int delay)
        {
            World w = Arena(0);
            for (int k = 0; k < 20; k++) Tick(w, 1, false);
            double r = Collide.ActorRadius(w, 0);
            int c0 = (int)Math.Floor(w.X[0] - r - 0.5);
            int c1 = (int)Math.Floor(w.X[0] + r + 0.5);
            for (int cx = c0; cx <= c1; cx++) for (int cz = Z0 + 1; cz < Z1; cz++) MapGrid.SetCell(w, cx, cz, Cell.Void);
            Assert.IsFalse(Collide.FootprintClear(w, 0, w.X[0], w.Z[0]), "the footing is gone");
            for (int k = 0; k < delay; k++) Tick(w, 1, false);
            Tick(w, 1, true);
            return w;
        }

        [Test]
        public void DG02CoyotePressingJump2TicksAfterTheLedgeLeavesStillJumpsAndLandsOnClearGround()
        {
            World w = LedgeLeaves(2);
            Actor a = w.Actors[0];
            Assert.AreEqual(J, a.AirLen, "took off inside the coyote window");
            for (int k = 2; k <= J; k++) Tick(w, 1, false);
            Assert.AreEqual(0, a.AirLen);
            Assert.IsTrue(Collide.FootprintClear(w, 0, w.X[0], w.Z[0]), "landed on clear ground");
        }

        [Test]
        public void DG02CoyoteAPressAfterTheWindowIsRefused()
        {
            // Unsupported ticks 1..3 may jump; the press on the 4th unsupported tick (3 ticks of delay + the press tick) is refused.
            Assert.AreEqual(J, LedgeLeaves(Jump.CoyoteTicks(Cfg) - 1).Actors[0].AirLen);
            Assert.AreEqual(0, LedgeLeaves(Jump.CoyoteTicks(Cfg)).Actors[0].AirLen);
        }

        [Test]
        public void DG02BufferAPress2TicksBeforeLandingJumpsOnTheFirstGroundedTick()
        {
            World w = Arena(1);
            Actor a = w.Actors[0];
            Tick(w, 0, true);
            for (int k = 2; k <= J - 2; k++) Tick(w, 0, false);
            Assert.AreEqual(J - 2, a.AirTick);
            Tick(w, 0, true); // air tick J - 1: two ticks before the first grounded tick
            Assert.AreEqual(J - 1, a.AirTick);
            Tick(w, 0, false); // air tick J: lands at the end of it
            Assert.AreEqual(0, a.AirLen);
            Tick(w, 0, false); // first grounded tick: the buffered press takes off
            Assert.AreEqual(J, a.AirLen);
            Assert.AreEqual(1, a.AirTick);
        }

        [Test]
        public void DG02BufferAPressMoreThan3TicksBeforeTheFirstGroundedTickIsDropped()
        {
            World w = Arena(1);
            Actor a = w.Actors[0];
            Tick(w, 0, true);
            for (int k = 2; k <= J - 4; k++) Tick(w, 0, false);
            Tick(w, 0, true); // air tick J - 3: four ticks before the first grounded tick
            for (int k = J - 2; k <= J; k++) Tick(w, 0, false);
            Assert.AreEqual(0, a.AirLen);
            Tick(w, 0, false);
            Assert.AreEqual(0, a.AirLen);
        }

        [Test]
        public void DG02BufferAPressRefusedAtAWideGapFiresWhenALandingClearsAndNeverEndsOnABlockedCell()
        {
            // Backing away from the edge of a 5-cell gap: the press at the edge is refused, the buffer retries for 3 ticks.
            World w = Arena(5);
            Actor a = w.Actors[0];
            RunUpTo(w, 0.2);
            Tick(w, 1, true);
            Assert.AreEqual(0, a.AirLen);
            Assert.AreEqual(Jump.BufferTicks(Cfg), a.JumpBuffer);
            for (int k = 0; k < 4; k++)
            {
                Tick(w, 1, false);
                Assert.AreEqual(0, a.AirLen, "every landing is over the gap: still refused");
            }
            Assert.AreEqual(0, a.JumpBuffer, "the buffer has run out");
        }

        [Test]
        public void DG02DeathClearsTheJumpAndTheBuffer()
        {
            World w = Arena(1);
            Actor a = w.Actors[0];
            Tick(w, 0, true);
            Tick(w, 0, true); // buffered mid-air press
            a.Alive = false;
            a.RespawnIn = 1e9;
            Tick(w, 0, false);
            Assert.AreEqual(0, a.AirLen);
            Assert.AreEqual(0, a.AirTick);
            Assert.AreEqual(0, a.JumpBuffer);
        }

        /// <summary>FNV-1a over the bits of every actor's position, velocity and jump state.</summary>
        private static ulong StateHash(World w)
        {
            ulong h = 14695981039346656037UL;
            void Mix(long v)
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }
            Mix(w.Tick);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Mix(BitConverter.DoubleToInt64Bits(w.X[i]));
                Mix(BitConverter.DoubleToInt64Bits(w.Z[i]));
                Mix(BitConverter.DoubleToInt64Bits(w.Vx[i]));
                Mix(BitConverter.DoubleToInt64Bits(w.Vz[i]));
                Mix(w.Actors[i].AirLen);
                Mix(w.Actors[i].AirTick);
                Mix(w.Actors[i].JumpBuffer);
                Mix(w.Actors[i].Alive ? 1 : 0);
            }
            Mix(BitConverter.DoubleToInt64Bits(w.Match.HillSeconds.Azure));
            Mix(BitConverter.DoubleToInt64Bits(w.Match.HillSeconds.Crimson));
            return h;
        }

        /// <summary>A 16v16 bot match with actor 0 on autopilot pressing jump every 10th tick; returns the state hash.</summary>
        private static ulong JumpingMatchHash(int seed, int ticks, out int jumps, out int unfit)
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = seed,
                TeamSize = 16,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Config = GameConfig.CreateBrowser5v5(),
            });
            InputFrame scratch = SimInput.EmptyInput();
            InputFrame human = SimInput.EmptyInput();
            jumps = 0;
            unfit = 0;
            double dt = SimStep.Dt(w.Config);
            for (int t = 0; t < ticks; t++)
            {
                // Actor 0 thinks like a bot (the autopilot frame), plus a jump press every 10th tick.
                BotBrain.FillBotInput(w, 0, human);
                human.Jump = t % 10 == 0;
                MatchTick.Advance(w, human, scratch);
                if (w.Actors[0].AirTick == 1) jumps++;
                for (int i = 0; i < w.ActorCount; i++)
                {
                    Actor a = w.Actors[i];
                    if (a.Alive && a.AirLen == 0 && !Collide.FootprintClear(w, i, w.X[i], w.Z[i])) unfit++;
                }
            }
            return StateHash(w);
        }

        [Test]
        public void DG02SameSeedSameStateHashIn16v16WithTheHumanJumpingAndNoGroundedBodyEverStandsOnABlockedCell()
        {
            ulong a = JumpingMatchHash(1337, 900, out int jumpsA, out int unfitA);
            ulong b = JumpingMatchHash(1337, 900, out int jumpsB, out int unfitB);
            Assert.AreEqual(a, b, "same seed, same inputs: same state hash");
            Assert.AreEqual(jumpsA, jumpsB);
            Assert.Greater(jumpsA, 10, "actor 0 jumped during the match");
            Assert.AreEqual(0, unfitA, "grounded bodies on a blocked cell");
            Assert.AreNotEqual(a, JumpingMatchHash(7, 900, out _, out _), "a different seed gives a different match");
        }
    }
}
