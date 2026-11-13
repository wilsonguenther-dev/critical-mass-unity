using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// CM-30 soak (port of src/sim/terraform.slow.test.ts, 09e0904) with every DG-09 mechanic active as shipped: a full
    /// 16v16 match (seed 101, 14,400 ticks = 8:00) with every miner forced to press F once per ForcePeriod ticks on top of
    /// the normal bot brains (CarryMax 3, the mode following the carry; in Place mode it aims at the nearest placeable cell,
    /// else straight ahead), knock-down pillars live (any swing chips them; from 1:00 the soak knocks the Azure one over as a
    /// block and the Crimson one as a ramp), the fall rule live.
    /// Every tick: no NaN; no living actor inside solid ground (centre on a walkable cell, or over Void only while airborne
    /// or carried by a diagonal deck); no travelling bot still for more than StuckTicks. Every SamplePeriod: both pads reach
    /// the hill. At the end: both teams reached the hill, the owned-hill share is inside the band batch B asserts for a 16v16
    /// match (OutcomeThresholds.OwnedShareMin), something was dug and placed. Time is counted in sim ticks only.
    /// </summary>
    [TestFixture]
    public sealed class TerraformSoakTests
    {
        private const int Seed = 101;
        private const int TeamSize = 16;
        /// <summary>Each miner is forced to press F once per this many ticks [guess, browser], staggered by actor id.</summary>
        private const int ForcePeriod = 90;
        /// <summary>Pad-to-hill connectivity sample period in ticks (10 s).</summary>
        private const int SamplePeriod = 300;
        /// <summary>A travelling bot may stand within StuckRadius of one spot for at most this many ticks [guess: 3 s].</summary>
        private const int StuckTicks = 90;
        private const double StuckRadius = 0.5;
        /// <summary>Tick from which the soak knocks both pillars over (1:00, the first hill fights are on).</summary>
        private const int ToppleFrom = 1800;

        private static bool Travelling(AiState s) => s == AiState.ToHill || s == AiState.Deliver || s == AiState.Retreat || s == AiState.Unstick;

        [Test, Category("Slow")]
        public void DG09Soak16v16NoNaNNobodyInSolidGroundPadsAlwaysReachTheHillNoTravellingBotStuckBandsHold()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            int ticks = (int)(cfg.MatchSeconds * cfg.SimHz);
            Assert.AreEqual(14400, ticks);
            World w = World.CreateWorld(new WorldOptions { Seed = Seed, TeamSize = TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Miner, Config = cfg });
            InputFrame frame = SimInput.EmptyInput();
            int n = w.ActorCount;
            var anchorX = new double[n];
            var anchorZ = new double[n];
            var anchorTick = new int[n];
            var anchorState = new AiState[n];
            var failures = new List<string>();
            int dug = 0, placed = 0, topples = 0, hits = 0, falls = 0, samples = 0, worstStill = 0, head = w.EventHead;
            int ownSamples = 0, owned = 0;
            double dt = SimStep.Dt(cfg);
            int hz = (int)cfg.SimHz;

            for (int s = 0; s < ticks && w.Match.Phase != MatchPhase.Ended; s++)
            {
                BotBrain.BotsThink(w, dt);
                for (int id = 0; id < n; id++)
                {
                    BotBrain.FillBotInput(w, id, frame); // id 0 too: the human slot runs on autopilot
                    bool force = w.Actors[id].Cls == ClassId.Miner && (w.Tick + id * 7) % ForcePeriod == 0;
                    frame.Terraform = force;
                    if (force) TerraformTests.AimForced(w, id, frame);
                    SimInput.ApplyInput(w, id, frame);
                }
                SimStep.Step(w, dt);
                int tick = w.Tick;
                // Bots rarely swing at a pillar, so the soak knocks both over once ToppleFrom has passed (retrying each tick
                // while refused): the Azure one as a BLOCK across its B2 mouth, the Crimson one as a RAMP to its overlook.
                if (tick >= ToppleFrom)
                {
                    if (w.Topple.Fallen[0] == Topple.Standing) Topple.ToppleDecoration(w, 0, Topple.Block, -1);
                    if (w.Topple.Fallen[1] == Topple.Standing) Topple.ToppleDecoration(w, 1, Topple.Ramp, -1);
                }

                for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
                {
                    SimEvent e = w.Events[i % World.EventCapacity];
                    if (e.Type == SimEventType.Terraform && e.Value == Terraform.Dug) dug++;
                    else if (e.Type == SimEventType.Terraform && e.Value == Terraform.Placed) placed++;
                    else if (e.Type == SimEventType.Topple && e.Value > 0) topples++;
                    else if (e.Type == SimEventType.ToppleHit) hits++;
                    else if (e.Type == SimEventType.Fall) falls++;
                }
                head = w.EventHead;

                for (int id = 0; id < n; id++)
                {
                    Actor a = w.Actors[id];
                    double x = w.X[id];
                    double z = w.Z[id];
                    if (!double.IsFinite(x) || !double.IsFinite(z)) failures.Add($"tick {tick}: actor {id} NaN position");
                    if (!a.Alive)
                    {
                        anchorState[id] = AiState.None;
                        continue;
                    }
                    if (!Terraform.WalkableAt(w.Map, x, z))
                    {
                        int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
                        bool overVoid = MapGrid.InBounds(w.Map, cx, cz) && w.Map.Cells[cz * w.Map.Width + cx] == Cell.Void;
                        bool carried = a.AirLen > 0 || Collide.DeckSpanAt(w, x, z, Collide.ActorRadius(w, id)) != null;
                        if (!(overVoid && carried)) failures.Add($"tick {tick}: actor {id} at ({x:F2}, {z:F2}) inside cell type {(MapGrid.InBounds(w.Map, cx, cz) ? w.Map.Cells[cz * w.Map.Width + cx] : -1)}");
                    }
                    // DG-16b (CM-64): a carrier winding up a delivery at the shrine (DeliverTicks != 0: winding, or the tick an enemy hit
                    // interrupted it) stands still on purpose; that is the contested hand-off, not a pathing stall.
                    bool windingUp = a.AiState == AiState.Deliver && a.DeliverTicks != 0;
                    bool travelling = a.StandUp <= 0 && Travelling(a.AiState) && !windingUp;
                    double dx = x - anchorX[id];
                    double dz = z - anchorZ[id];
                    if (!travelling || a.AiState != anchorState[id] || dx * dx + dz * dz >= StuckRadius * StuckRadius)
                    {
                        anchorX[id] = x;
                        anchorZ[id] = z;
                        anchorTick[id] = tick;
                        anchorState[id] = travelling ? a.AiState : AiState.None;
                        continue;
                    }
                    int still = tick - anchorTick[id];
                    if (still > worstStill) worstStill = still;
                    if (still == StuckTicks + 1) failures.Add($"tick {tick}: bot {id} ({a.Cls}, {a.AiState}) stationary > {StuckTicks} ticks at ({x:F2}, {z:F2})");
                }
                if (failures.Count > 20) break;

                if (tick % SamplePeriod == 0)
                {
                    samples++;
                    if (!Terraform.PadsReachHill(w.Map)) failures.Add($"tick {tick}: a pad cannot reach the hill");
                }
                // The OutcomeRunner's owned-share sample: once per sim second after OutcomeSampleFrom.
                if (tick % hz == 0 && w.Match.Time > OutcomeRunner.OutcomeSampleFrom + 1e-6)
                {
                    ownSamples++;
                    if (w.Match.HillState == HillState.Azure || w.Match.HillState == HillState.Crimson) owned++;
                }
            }

            double ownedShare = ownSamples > 0 ? (double)owned / ownSamples : 0;
            Console.WriteLine($"DG-09 soak seed {Seed} {TeamSize}v{TeamSize} ticks {w.Tick}: dug {dug}, placed {placed}, pillar hits {hits}, topples {topples}, falls {falls}, samples {samples}, worstStill {worstStill}, ownedShare {ownedShare:F3}, hill s azure {w.Match.HillSeconds.Azure:F1} crimson {w.Match.HillSeconds.Crimson:F1}, winner {w.Match.Winner}");
            CollectionAssert.IsEmpty(failures, string.Join("\n", failures));
            Assert.Greater(samples, 0);
            Assert.Greater(dug, 0);
            Assert.Greater(placed, 0);
            Assert.AreEqual(Topple.Block, w.Topple.Fallen[0], "the Azure pillar lies across B2 (block)");
            Assert.AreEqual(Topple.Ramp, w.Topple.Fallen[1], "the Crimson pillar bridges to its overlook (ramp)");
            Assert.Greater(w.Match.HillSeconds.Azure, 0, "Azure bots reached the hill");
            Assert.Greater(w.Match.HillSeconds.Crimson, 0, "Crimson bots reached the hill");
            Assert.GreaterOrEqual(ownedShare, OutcomeThresholds.OwnedShareMin, "batch B's per-match owned-share band");
        }

        /// <summary>DG-09 determinism: the same seed and forced presses (mode switches included) give the same state hash; a different seed does not.</summary>
        [Test]
        public void DG09SameSeedSameStateHashWithDigPlaceSwitchAndToppleActive()
        {
            ulong a = Run(1337, 1800);
            Assert.AreEqual(a, Run(1337, 1800), "same seed, same inputs: same state hash");
            Assert.AreNotEqual(a, Run(7, 1800));
        }

        private static ulong Run(int seed, int ticks)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Miner, Config = cfg });
            InputFrame frame = SimInput.EmptyInput();
            double dt = SimStep.Dt(cfg);
            for (int s = 0; s < ticks; s++)
            {
                BotBrain.BotsThink(w, dt);
                for (int id = 0; id < w.ActorCount; id++)
                {
                    BotBrain.FillBotInput(w, id, frame);
                    bool force = w.Actors[id].Cls == ClassId.Miner && (w.Tick + id * 7) % 45 == 0;
                    frame.Terraform = force;
                    frame.TerraformSwitch = w.Actors[id].Cls == ClassId.Miner && (w.Tick + id * 11) % 200 == 0;
                    if (force) TerraformTests.AimForced(w, id, frame);
                    SimInput.ApplyInput(w, id, frame);
                }
                SimStep.Step(w, dt);
            }
            ulong h = 14695981039346656037UL;
            void Mix(long v)
            {
                h ^= (ulong)v;
                h *= 1099511628211UL;
            }
            Mix(w.Tick);
            Mix(w.GridVersion);
            Mix(w.WallChangeTick);
            for (int i = 0; i < w.Map.Cells.Length; i++) Mix(w.Map.Cells[i]);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Mix(BitConverter.DoubleToInt64Bits(w.X[i]));
                Mix(BitConverter.DoubleToInt64Bits(w.Z[i]));
                Mix(w.Terraform.Carried[i]);
                Mix(w.Terraform.Mode[i]);
                Mix(w.Actors[i].Alive ? 1 : 0);
            }
            for (int k = 0; k < w.Topple.Fallen.Length; k++)
            {
                Mix(w.Topple.Fallen[k]);
                Mix(w.Topple.Hp[k]);
            }
            return h;
        }
    }
}
