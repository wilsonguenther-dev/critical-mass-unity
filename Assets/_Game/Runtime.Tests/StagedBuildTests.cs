using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>feat/staged-building-1: model choice, the piece cut, the construction timeline and a read-only sim.</summary>
    public sealed class StagedBuildTests
    {
        [Test]
        public void EachStructureDrawsAsItsAuthoredModel()
        {
            Assert.AreEqual(ArtMapping.BuilderLadder, StagedBuildCore.ModelKey(StagedBuildCore.KindLadder));
            Assert.AreEqual(ArtMapping.BuilderRopeBridge, StagedBuildCore.ModelKey(StagedBuildCore.KindBridge));
            Assert.AreEqual(ArtMapping.MinerBarricade, StagedBuildCore.ModelKey(StagedBuildCore.KindBarricade));
            Assert.IsNull(StagedBuildCore.ModelKey(0));
            Assert.AreEqual(ArtMapping.LadderPlaced, StagedBuildCore.PlacedMesh(StagedBuildCore.KindLadder));
            Assert.AreEqual(ArtMapping.RopeBridgePlaced, StagedBuildCore.PlacedMesh(StagedBuildCore.KindBridge));
            Assert.AreEqual(ArtMapping.BarricadeDeployed, StagedBuildCore.PlacedMesh(StagedBuildCore.KindBarricade));
            Assert.AreEqual(ArtMapping.LadderCarried, StagedBuildCore.CarriedMesh(StagedBuildCore.KindLadder));
            Assert.AreEqual(ArtMapping.RopeBridgeCarried, StagedBuildCore.CarriedMesh(StagedBuildCore.KindBridge));
            // the sim's kind codes map straight through
            Assert.AreEqual(BuiltPieces.Ladder, StagedBuildCore.KindLadder);
            Assert.AreEqual(BuiltPieces.Bridge, StagedBuildCore.KindBridge);
            foreach (int kind in new[] { StagedBuildCore.KindLadder, StagedBuildCore.KindBridge, StagedBuildCore.KindBarricade })
            {
                ModelSpec spec = ArtMapping.Spec(StagedBuildCore.ModelKey(kind));
                Assert.IsNotNull(spec, "catalog spec for kind " + kind);
                CollectionAssert.Contains(spec.Meshes, StagedBuildCore.PlacedMesh(kind));
            }
        }

        static void Box(List<Vector3> v, List<int> t, Vector3 min, Vector3 max)
        {
            int b = v.Count;
            for (int i = 0; i < 8; i++)
                v.Add(new Vector3((i & 1) == 0 ? min.x : max.x, (i & 2) == 0 ? min.y : max.y, (i & 4) == 0 ? min.z : max.z));
            int[] f = { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
            foreach (int k in f) t.Add(b + k);
        }

        [Test]
        public void BridgeCutPutsPostsFirstPlanksInOrderRopesLast()
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            // planks listed out of order on purpose
            float[] plankZ = { 2.0f, 0.5f, 3.5f, 1.25f, 2.75f };
            foreach (float z in plankZ) Box(v, t, new Vector3(-0.8f, 0f, z), new Vector3(0.8f, 0.05f, z + 0.3f));
            Box(v, t, new Vector3(-0.9f, 0f, 0f), new Vector3(-0.8f, 1.1f, 0.1f));    // post near
            Box(v, t, new Vector3(0.8f, 0f, 4.0f), new Vector3(0.9f, 1.1f, 4.1f));    // post far
            Box(v, t, new Vector3(-0.85f, 0.8f, 0f), new Vector3(-0.83f, 0.82f, 4.1f)); // rope along the whole span
            var triStage = new int[t.Count / 3];
            int stages = StagedBuildCore.Partition(v, t, StagedBuildCore.KindBridge, 10, triStage);
            int Stage(int box) => triStage[box * 12];
            Assert.AreEqual(7, stages, "posts + 5 planks + rope");
            Assert.AreEqual(0, Stage(5));
            Assert.AreEqual(0, Stage(6));
            // planks by z: 0.5 (box 1), 1.25 (3), 2.0 (0), 2.75 (4), 3.5 (2)
            Assert.AreEqual(1, Stage(1));
            Assert.AreEqual(2, Stage(3));
            Assert.AreEqual(3, Stage(0));
            Assert.AreEqual(4, Stage(4));
            Assert.AreEqual(5, Stage(2));
            Assert.AreEqual(6, Stage(7), "the rope comes last");
        }

        [Test]
        public void LadderCutClimbsBottomToTopAndRespectsTheStageBudget()
        {
            var v = new List<Vector3>();
            var t = new List<int>();
            Box(v, t, new Vector3(-0.3f, 0f, -0.05f), new Vector3(-0.25f, 1.3f, 0.05f));  // rail
            Box(v, t, new Vector3(0.25f, 0f, -0.05f), new Vector3(0.3f, 1.3f, 0.05f));    // rail
            for (int r = 7; r >= 0; r--) Box(v, t, new Vector3(-0.25f, 0.1f + r * 0.15f, -0.02f), new Vector3(0.25f, 0.13f + r * 0.15f, 0.02f));
            var triStage = new int[t.Count / 3];
            int stages = StagedBuildCore.Partition(v, t, StagedBuildCore.KindLadder, 5, triStage);
            Assert.AreEqual(5, stages);
            Assert.AreEqual(0, triStage[0]);
            Assert.AreEqual(0, triStage[12]);
            int last = -1;
            for (int r = 7; r >= 0; r--)   // boxes 2.. were added top rung first; walk bottom (box 9) to top (box 2)
            {
                int box = 2 + r;
                int s = triStage[box * 12];
                Assert.GreaterOrEqual(s, 1);
                Assert.GreaterOrEqual(s, last, "rungs never go back down");
                last = s;
            }
            Assert.AreEqual(0, StagedBuildCore.Partition(new List<Vector3>(), new List<int>(), StagedBuildCore.KindLadder, 5, null));
        }

        [Test]
        public void SequenceFinishesAndLeavesExactlyTheFinalStructure()
        {
            for (int n = 1; n <= 24; n++)
            {
                float dur = StagedBuildCore.Duration(n);
                Assert.That(dur, Is.InRange(StagedBuildCore.MinSeconds, StagedBuildCore.MaxSeconds));
                float prev = -1f;
                for (int i = 0; i < n; i++)
                {
                    float at = StagedBuildCore.ArriveAt(i, n, dur);
                    Assert.GreaterOrEqual(at, prev, "pieces arrive in order");
                    Assert.LessOrEqual(at + StagedBuildCore.SettleSeconds, dur + 1e-4f, "every piece lands before the end");
                    prev = at;
                    // before its time: not there; mid fall: above; at the end: exactly in place
                    Assert.IsFalse(StagedBuildCore.Pose(at - 0.01f, at, false, out _));
                    Assert.IsTrue(StagedBuildCore.Pose(at, at, false, out float d0));
                    Assert.AreEqual(StagedBuildCore.DropHeight, d0, 1e-5f);
                    Assert.IsTrue(StagedBuildCore.Pose(dur, at, false, out float dEnd));
                    Assert.AreEqual(0f, dEnd, "settled exactly on the final spot");
                    Assert.IsTrue(StagedBuildCore.Pose(at + 0.05f, at, true, out float dReduced));
                    Assert.AreEqual(0f, dReduced, "reduced motion: no fall");
                }
                Assert.IsTrue(StagedBuildCore.Done(dur, dur));
                Assert.AreEqual(0f, StagedBuildCore.GlowScale(dur, dur), "the glow is gone at the end");
                Assert.AreEqual(1f, StagedBuildCore.Progress(dur, dur));
            }
        }

        [Test]
        public void TrackerStartsOnANewBuildAndEndsWithNothingRunning()
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = cfg });
            var tr = new StagedBuildCore.Tracker();
            tr.Observe(w, 0f, true, 0, 4);
            Assert.AreEqual(0, tr.Started, "nothing built at bind");
            // a build landing (the fields BuiltPieces.Build writes)
            BuiltPiece p = w.Builder.Pieces[0];
            p.Active = true; p.Carried = false; p.Kind = BuiltPieces.Bridge; p.Owner = 0; p.Team = TeamId.Azure; p.PlacedTick = w.Tick + 1; p.X = 10; p.Z = 10;
            tr.Observe(w, 1f, true, 12, 4);
            Assert.AreEqual(1, tr.Started);
            Assert.IsTrue(tr.Pieces[0].On);
            Assert.IsTrue(tr.BuildingBy(0, out StagedBuildCore.Seq seq));
            tr.Observe(w, 1.1f, true, 12, 4);
            Assert.AreEqual(1, tr.Started, "the same build never restarts");
            tr.Expire(1f + seq.Dur);
            Assert.IsFalse(tr.AnyOn, "the sequence ends; the piece stays as built");
            Assert.IsTrue(p.Active);
            // disabled: recorded, no sequence
            p.PlacedTick += 5;
            tr.Observe(w, 3f, false, 12, 4);
            Assert.IsFalse(tr.AnyOn);
        }

        static ulong RunHash(bool watch, out int started)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            World w = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 8, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            var tr = new StagedBuildCore.Tracker();
            for (int t = 0; t < 2400 && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                if (watch) { tr.Observe(w, t / 30f, true, 0, 4); tr.Expire(t / 30f); tr.BuildingBy(0, out _); }
            }
            started = tr.Started;
            return AutoplayArgs.WorldHash(w);
        }

        [Test]
        public void SimulationChecksumIsUnchangedWithTheStagedBuildWatching()
        {
            ulong plain = RunHash(false, out _);
            ulong watched = RunHash(true, out int started);
            Debug.Log("[StagedBuild] sequences started in the watched run: " + started);
            Assert.AreEqual(plain, watched, "the staged build only reads the sim");
            Assert.AreEqual(plain, RunHash(false, out _), "same seed, same hash");
        }
    }
}
