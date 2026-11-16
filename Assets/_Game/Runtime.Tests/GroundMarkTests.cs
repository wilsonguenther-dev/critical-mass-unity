using System.Collections.Generic;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Reactions;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b11-eh: the grey square with an X left on the ground after fights. Ground marks from a staged fight (hits and
    /// knock-outs) are gone once their life is over at every GORE level, no splat shape is drawn with straight edges
    /// (every triangle of a splat belongs to a round fan, never a quad), and the stamped stains are wiped when GORE drops
    /// below Full.
    /// </summary>
    [TestFixture]
    public sealed class GroundMarkTests
    {
        static World NewWorld(GameConfig cfg) => World.CreateWorld(new WorldOptions
        {
            Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
        });

        static FxMeshBuilder Ground() => new FxMeshBuilder("g", CombatFx.GroundVerts, CombatFx.GroundVerts * 3);

        static void Destroy(FxMeshBuilder b)
        {
            if (b != null && b.Mesh != null) Object.DestroyImmediate(b.Mesh);
        }

        /// <summary>Six hits and two knock-outs around the first pad (a small hill fight), all at tick 30.</summary>
        static void StageFight(FxScene scene, World w)
        {
            PadDef pad = w.Map.Pads[0];
            for (int n = 0; n < 8; n++)
            {
                var e = new FxEvent
                {
                    Kind = n >= 6 ? FxKind.Death : FxKind.Hit, Tick = 30 + n, A = 1, B = 2, X = pad.X + 0.4 * n, Z = pad.Z,
                    Value = 22, DirX = 0.6, DirZ = 0.8, Cls = ClassId.Miner, Tier = (byte)(n % 3),
                };
                scene.OnFx(ref e, w);
            }
        }

        [TestCase(GoreLevel.Full)]
        [TestCase(GoreLevel.Reduced)]
        [TestCase(GoreLevel.Off)]
        public void NoGroundMarkOutlivesItsLife(GoreLevel gore)
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            var scene = new FxScene(cfg, w.Map) { Gore = gore };
            FxMeshBuilder g = Ground(), a = new FxMeshBuilder("a", CombatFx.AirVerts, CombatFx.AirVerts * 3),
                s = new FxMeshBuilder("s", CombatFx.SolidVerts, CombatFx.SolidVerts * 2);
            try
            {
                StageFight(scene, w);
                double t0 = 30 / cfg.SimHz;
                // Every drop lands within a few seconds: advance past the landings and look at the ground then.
                double landed = t0 + scene.S(3.0);
                for (double t = t0; t <= landed; t += 1.0 / 30.0) scene.Advance(t);
                scene.Build(landed, w, Vector3.right, Vector3.up, g, a, s);
                if (gore == GoreLevel.Off)
                {
                    Assert.AreEqual(0, scene.DecalCount(landed), "GORE Off: no splat or pool on the ground");
                    Assert.AreEqual(0, scene.LastGroundMarks, "GORE Off: no ground mark drawn after the fight");
                    return;
                }
                Assert.Greater(scene.DecalCount(landed), 0, gore + ": the fight leaves splats while they live");
                double end = scene.LastDecalEnd();
                Assert.That(end, Is.GreaterThan(landed), gore + ": the newest splat is still alive at the look");
                double after = end + 1e-3;
                scene.Advance(after);
                scene.Build(after, w, Vector3.right, Vector3.up, g, a, s);
                Assert.AreEqual(0, scene.DecalCount(after), gore + ": every splat and pool is gone past its life");
                Assert.AreEqual(0, scene.LastGroundMarks, gore + ": nothing is drawn on the ground past its life");
                if (gore == GoreLevel.Reduced)
                    Assert.That(end - t0, Is.LessThan(scene.S(20.0) * GoreGate.LifeScale(GoreLevel.Reduced) + scene.S(4.0)),
                        "Reduced: splats fade on the Reduced life");
            }
            finally { Destroy(g); Destroy(a); Destroy(s); }
        }

        /// <summary>
        /// A quad (or any straight-edged strip) is two triangles around a corner, with no centre shared by the rest; every
        /// splat piece is a round fan (Blob, Star): n of 5 or more triangles around one centre vertex. So: each triangle of
        /// every splat shape, red or neutral ink, must hold a vertex shared by at least five triangles.
        /// </summary>
        [Test]
        public void NoSplatShapeIsDrawnAsACrossedSquare()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            var scene = new FxScene(cfg, w.Map);
            FxMeshBuilder g = Ground();
            var uses = new Dictionary<int, int>();
            try
            {
                for (byte kind = 0; kind <= FxScene.KindPoolRed; kind++)
                for (byte shape = 0; shape < FxScene.SplatShapes; shape++)
                for (uint seed = 1; seed <= 6; seed++)
                {
                    g.Clear();
                    scene.DrawSplatShape(g, new Vector3(10f, 0f, -10f), 0.5f, shape, seed * 7919u, 0.6, 0.8, kind);
                    Assert.Greater(g.VertCount, 0, "shape " + shape + " draws");
                    Assert.LessOrEqual(g.VertCount, FxScene.CostDecal, "shape " + shape + " stays inside CostDecal");
                    g.Upload();
                    int[] idx = g.Mesh.GetIndices(0);
                    uses.Clear();
                    for (int i = 0; i < idx.Length; i++) uses[idx[i]] = uses.TryGetValue(idx[i], out int n) ? n + 1 : 1;
                    for (int t = 0; t < idx.Length; t += 3)
                    {
                        bool fan = uses[idx[t]] >= 5 || uses[idx[t + 1]] >= 5 || uses[idx[t + 2]] >= 5;
                        Assert.IsTrue(fan, "shape " + shape + " kind " + kind + " seed " + seed + ": triangle " + t / 3 +
                            " is part of a straight-edged quad (the grey square with an X)");
                    }
                }
            }
            finally { Destroy(g); }
        }

        [Test]
        public void StampedStainsAreWipedWhenGoreDropsBelowFull()
        {
            Assert.IsTrue(GroundPaint.ShouldWipe(GoreLevel.Full, GoreLevel.Reduced));
            Assert.IsTrue(GroundPaint.ShouldWipe(GoreLevel.Full, GoreLevel.Off));
            Assert.IsFalse(GroundPaint.ShouldWipe(GoreLevel.Full, GoreLevel.Full));
            Assert.IsFalse(GroundPaint.ShouldWipe(GoreLevel.Reduced, GoreLevel.Off), "nothing was stamped below Full");
            Assert.IsFalse(GroundPaint.ShouldWipe(GoreLevel.Off, GoreLevel.Full));
        }
    }
}
