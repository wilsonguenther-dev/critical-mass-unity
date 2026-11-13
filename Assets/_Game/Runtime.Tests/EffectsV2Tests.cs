using System;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = NUnit.Framework.Is;
using UIs = UnityEngine.TestTools.Constraints.Is;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Effects v2 (explosions, tiered hits, blood and gore, world reactions): hard caps under a 500-event flood, the vertex
    /// budget per layer, the sim untouched with effects on and off, the blood setting swapping every blood piece, no
    /// allocation in the steady state, the gallery playing every step.
    /// </summary>
    [TestFixture]
    public sealed class EffectsV2Tests
    {
        private static World NewWorld(GameConfig cfg) => World.CreateWorld(new WorldOptions
        {
            Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
        });

        /// <summary>A foliage mask with grass on every floor cell, so the rustle path runs without the dressed world.</summary>
        private static byte[] AllGrass(MapDef map)
        {
            var mask = new byte[map.Cells.Length];
            for (int i = 0; i < mask.Length; i++) mask[i] = map.Cells[i] == Cell.Floor ? (byte)((i % 3) + 1) : (byte)0;
            return mask;
        }

        private sealed class Rig : IDisposable
        {
            public readonly CombatFeed Feed;
            public readonly FxScene Scene;
            public readonly FxMeshBuilder G, A, S;

            public Rig(World w, GameConfig cfg)
            {
                Feed = new CombatFeed(World.ActorCapacity(cfg));
                Scene = new FxScene(cfg, w.Map);
                Scene.SetFoliage(AllGrass(w.Map));
                G = new FxMeshBuilder("g", CombatFx.GroundVerts, CombatFx.GroundVerts * 3);
                A = new FxMeshBuilder("a", CombatFx.AirVerts, CombatFx.AirVerts * 3);
                S = new FxMeshBuilder("s", CombatFx.SolidVerts, CombatFx.SolidVerts * 2);
            }

            public void Build(World w, double now)
            {
                Scene.Advance(now);
                Scene.Build(now, w, Vector3.right, Vector3.up, G, A, S);
                G.Upload(); A.Upload(); S.Upload();
            }

            public int Dropped => G.Dropped + A.Dropped + S.Dropped;

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(G.Mesh);
                UnityEngine.Object.DestroyImmediate(A.Mesh);
                UnityEngine.Object.DestroyImmediate(S.Mesh);
            }
        }

        private static FxEvent Flood(int n, World w, GameConfig cfg, PadDef pad, int tick)
        {
            var kinds = (FxKind[])Enum.GetValues(typeof(FxKind));
            var e = new FxEvent
            {
                Kind = kinds[n % kinds.Length], Tick = tick, A = n % w.ActorCount, B = (n + 1) % w.ActorCount,
                X = pad.X + (n % 7) * 0.4, Z = pad.Z + (n % 5) * 0.4, Value = n % 3 == 0 ? 45 : 22, DirX = 0.8, DirZ = 0.6,
                Cls = (ClassId)(n % 5), Team = (TeamId)(n % 2), Heavy = true, Tier = (byte)(n % 3),
                HasAttacker = n % 2 == 0, AttCls = (ClassId)((n / 2) % 5),
            };
            if (e.Kind == FxKind.KaijuSlam) e.Value = cfg.Kaiju.SlamRadius;
            if (e.Kind == FxKind.Explosion) { e.B = n % 5; e.Value = 1.5 + (n % 4); }
            if (e.Kind == FxKind.Hit && n % 11 == 0) e.B = Damage.KaijuSource;
            return e;
        }

        [Test]
        public void WorstCaseVerticesFitEveryLayerBuffer()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            var scene = new FxScene(cfg, w.Map);
            TestContext.Progress.WriteLine($"[Effects v2 budget] worst case verts ground {scene.WorstCaseVerts(0)} / {CombatFx.GroundVerts}, " +
                                           $"air {scene.WorstCaseVerts(1)} / {CombatFx.AirVerts}, solid {scene.WorstCaseVerts(2)} / {CombatFx.SolidVerts}, " +
                                           $"pooled pieces {FxScene.Capacity}");
            Assert.LessOrEqual(scene.WorstCaseVerts(0), CombatFx.GroundVerts, "ground layer");
            Assert.LessOrEqual(scene.WorstCaseVerts(1), CombatFx.AirVerts, "air layer");
            Assert.LessOrEqual(scene.WorstCaseVerts(2), CombatFx.SolidVerts, "solid layer");
        }

        [Test]
        public void PoolsNeverExceedTheirCapsUnderA500EventFlood()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            PadDef pad = w.Map.Pads[0];
            using (var rig = new Rig(w, cfg))
            {
                int worstVerts = 0, worstActive = 0;
                double t0 = 10 / cfg.SimHz;
                for (int step = 0; step <= 120; step++)
                {
                    int tick = 10 + step;
                    // 500 events in each of the first ten ticks (every kind, every explosion kind, every tier).
                    if (step < 10)
                        for (int n = 0; n < 500; n++)
                        {
                            FxEvent e = Flood(n + step * 500, w, cfg, pad, tick);
                            rig.Scene.OnFx(ref e, w);
                        }
                    double now = tick / cfg.SimHz + 0.02;
                    rig.Build(w, now);
                    worstVerts = Math.Max(worstVerts, rig.G.VertCount + rig.A.VertCount + rig.S.VertCount);
                    worstActive = Math.Max(worstActive, rig.Scene.ActiveCount(now));
                    Assert.LessOrEqual(rig.Scene.ActiveCount(now), FxScene.Capacity);
                    Assert.LessOrEqual(rig.Scene.DecalCount(now), FxScene.MaxDecals);
                    Assert.LessOrEqual(rig.Scene.SparkCount(now), FxScene.MaxSparks);
                    Assert.LessOrEqual(rig.Scene.DripCount(now), FxScene.MaxDrips);
                    Assert.LessOrEqual(rig.Scene.GibCount(now), FxScene.MaxGibs);
                    Assert.LessOrEqual(rig.Scene.ExplosionCount(now), FxScene.MaxExplosions);
                    Assert.LessOrEqual(rig.Scene.BubbleCount(now), FxScene.MaxBubbles);
                    Assert.LessOrEqual(rig.Scene.MoteCount(now), FxScene.MaxMotes);
                    Assert.LessOrEqual(rig.Scene.LeafCount(now), FxScene.MaxLeaves);
                    Assert.LessOrEqual(rig.G.VertCount, CombatFx.GroundVerts);
                    Assert.LessOrEqual(rig.A.VertCount, CombatFx.AirVerts);
                    Assert.LessOrEqual(rig.S.VertCount, CombatFx.SolidVerts);
                    Assert.AreEqual(0, rig.Dropped, $"a mesh budget overflowed at step {step}");
                }
                Assert.Greater(worstActive, 200, "the flood really filled the pools");
                TestContext.Progress.WriteLine($"[Effects v2 flood] worst active {worstActive} of {FxScene.Capacity}, worst verts {worstVerts} of {CombatFx.GroundVerts + CombatFx.AirVerts + CombatFx.SolidVerts}");
            }
        }

        [Test]
        public void SimStateHashIsIdenticalWithEffectsV2OnAndOff()
        {
            GameConfig cfgOff = GameConfig.CreateDefault(), cfgOn = GameConfig.CreateDefault();
            World off = NewWorld(cfgOff), on = NewWorld(cfgOn);
            InputFrame f1 = SimInput.EmptyInput(), f2 = SimInput.EmptyInput();
            using (var rig = new Rig(on, cfgOn))
            {
                for (int t = 0; t < 9000; t++)
                {
                    MatchTick.Advance(off, null, f1);
                    MatchTick.Advance(on, null, f2);
                    rig.Feed.OnTick(on, rig.Scene);
                    rig.Scene.OnTickState(on);
                    if (t % 150 == 0)
                    {
                        // An explosion of every kind in turn, at a living actor, as the rules lane would call it.
                        int a = (t / 150) % on.ActorCount;
                        var e = new FxEvent { Kind = FxKind.Explosion, Tick = on.Tick, A = -1, B = (t / 150) % 5, X = on.X[a], Z = on.Z[a], Value = 2.0 };
                        rig.Scene.OnFx(ref e, on);
                    }
                    if (t % 3 == 0) rig.Build(on, on.Tick / cfgOn.SimHz);
                    if (t % 500 == 499) Assert.AreEqual(CombatFxTests.StateHash(off), CombatFxTests.StateHash(on), $"state hash differs at tick {off.Tick}");
                }
                Assert.AreEqual(CombatFxTests.StateHash(off), CombatFxTests.StateHash(on));
                Assert.Greater(rig.Feed.Decoded, 1000);
            }
        }

        [Test]
        public void TheBloodSettingSwapsEveryBloodEffect()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            PadDef pad = w.Map.Pads[0];
            foreach (bool blood in new[] { false, true })
            {
                World ww = NewWorld(cfg);
                var scene = new FxScene(cfg, ww.Map) { Blood = blood };
                const int tick = 30;
                ww.Tick = tick;
                double t = tick / cfg.SimHz;
                // Every tier, a finisher, a Kaiju claw, and a knock-out inside a blast (gibs).
                double[] dmg = { 12, 22, 45 };
                for (int n = 0; n < 3; n++)
                {
                    var hit = new FxEvent { Kind = FxKind.Hit, Tick = tick, A = 1, B = 2, X = pad.X, Z = pad.Z, Value = dmg[n], DirX = 0.6, DirZ = 0.8, Cls = ClassId.Miner, Tier = CombatFeed.TierOf(dmg[n], false), HasAttacker = true, AttCls = ClassId.Warrior };
                    scene.OnFx(ref hit, ww);
                }
                var claw = new FxEvent { Kind = FxKind.Hit, Tick = tick, A = 1, B = Damage.KaijuSource, X = pad.X, Z = pad.Z, Value = 30, DirX = 1, Cls = ClassId.Farmer, Tier = 1 };
                scene.OnFx(ref claw, ww);
                var ko = new FxEvent { Kind = FxKind.Death, Tick = tick, A = 3, B = -1, X = pad.X + 0.5, Z = pad.Z, Value = 30, DirX = 1, Cls = ClassId.Builder, Tier = 2 };
                scene.OnFx(ref ko, ww);
                var boom = new FxEvent { Kind = FxKind.Explosion, Tick = tick, A = -1, B = (int)ExplosionKind.Tnt, X = pad.X + 0.5, Z = pad.Z, Value = 2.0 };
                scene.OnFx(ref boom, ww);
                // A badly hurt actor walking: drips behind it.
                int hurt = 1;
                for (int i = 1; i < ww.ActorCount; i++) if (ww.Actors[i].Alive) { hurt = i; break; }
                ww.Actors[hurt].Hp = 1;
                double x0 = ww.X[hurt];
                for (int k = 0; k < 40; k++)
                {
                    ww.Tick = tick + 1 + k;
                    ww.X[hurt] = x0 + k * 0.05;
                    scene.OnTickState(ww);
                }
                double soon = t + 0.05, later = t + 2.0;
                if (blood)
                {
                    Assert.Greater(scene.BloodCount(soon), 0, "blood on: droplets in flight");
                    scene.Advance(later);
                    Assert.Greater(scene.DecalCount(later), 0, "blood on: splats and the knock-out pool");
                    Assert.Greater(scene.GibCount(t + 0.2), 0, "blood on: gibs from the blast knock-out");
                    Assert.Greater(scene.DripCount(later), 0, "blood on: drips behind the hurt actor");
                }
                else
                {
                    Assert.Greater(scene.SparkCount(soon), 0, "blood off: neutral sparks instead");
                    for (double now = t; now < t + 6.0; now += 0.1)
                    {
                        scene.Advance(now);
                        Assert.AreEqual(0, scene.BloodCount(now), $"blood off: no blood piece at {now - t:0.0} s");
                    }
                }
            }
            Assert.IsNotNull(w);
        }

        [Test, Explicit("allocation is unmeasurable under the batch test runner (same reason as CombatFxTests' steady-state test, batch 1); run it by name in the editor")]
        public void EffectsV2DoNotAllocateInTheSteadyState()
        {
            // Same pattern as CombatFxTests (Unity's GC allocation constraint around the effects work only; the first effects
            // PR found the thread memory counter inconclusive under the test runner, so it is not used).
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            InputFrame f = SimInput.EmptyInput();
            using (var rig = new Rig(w, cfg))
            {
                int frame = 0;
                TestDelegate work = () =>
                {
                    rig.Feed.OnTick(w, rig.Scene);
                    rig.Scene.OnTickState(w);
                    if (frame % 20 == 0)
                    {
                        var e = new FxEvent { Kind = FxKind.Explosion, Tick = w.Tick, A = -1, B = frame % 5, X = w.X[1], Z = w.Z[1], Value = 2.0 };
                        rig.Scene.OnFx(ref e, w);
                        var d = new FxEvent { Kind = FxKind.Death, Tick = w.Tick, A = 1, B = -1, X = w.X[1], Z = w.Z[1], Value = 40, DirX = 1, Tier = 2 };
                        rig.Scene.OnFx(ref d, w);
                    }
                    rig.Build(w, w.Tick / cfg.SimHz);
                };
                for (int t = 0; t < 1500; t++) { MatchTick.Advance(w, null, f); work(); frame++; }
                for (int t = 0; t < 600; t++)
                {
                    MatchTick.Advance(w, null, f);
                    Assert.That(work, UIs.Not.AllocatingGCMemory(), $"effects v2 allocated at tick {w.Tick}");
                    frame++;
                }
            }
        }

        [Test]
        public void HitTiersAndTraumaFollowTheirRules()
        {
            Assert.AreEqual(0, CombatFeed.TierOf(12, false), "Farmer hit is light");
            Assert.AreEqual(1, CombatFeed.TierOf(22, false), "Warrior cleave is heavy");
            Assert.AreEqual(2, CombatFeed.TierOf(45, false), "slam damage is critical");
            Assert.AreEqual(2, CombatFeed.TierOf(5, true), "a finishing blow is always critical");

            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            PadDef pad = w.Map.Pads[0];
            var scene = new FxScene(cfg, w.Map);
            double t = 30 / cfg.SimHz;
            Assert.AreEqual(0f, scene.Trauma(t, pad.X, pad.Z), "no explosion, no trauma");
            var e = new FxEvent { Kind = FxKind.Explosion, Tick = 30, A = -1, B = (int)ExplosionKind.Tnt, X = pad.X, Z = pad.Z, Value = 2.0 };
            scene.OnFx(ref e, w);
            Assert.Greater(scene.Trauma(t + 0.01, pad.X, pad.Z), 0.3f, "full trauma at the blast");
            Assert.AreEqual(0f, scene.Trauma(t + 0.01, pad.X + 40, pad.Z), "nothing 40 u away");
            Assert.AreEqual(0f, scene.Trauma(t + 5.0, pad.X, pad.Z), "decayed after the blast");
            Assert.AreEqual(1, scene.ExplosionCount(t + 0.01));

            bool had = PlayerPrefs.HasKey(Effects.ShakeKey);
            int old = PlayerPrefs.GetInt(Effects.ShakeKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(Effects.ShakeKey);
                Assert.IsFalse(Effects.ShakeEnabled, "shake is off unless the player turns it on (art direction)");
                PlayerPrefs.SetInt(Effects.ShakeKey, 1);
                Assert.IsTrue(Effects.ShakeEnabled);
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(Effects.ShakeKey, old);
                else PlayerPrefs.DeleteKey(Effects.ShakeKey);
            }
            Assert.IsFalse(Effects.Explode(Vector3.zero, 2f, ExplosionKind.Tnt), "no CombatFx running: the API is a safe no-op");
        }

        [Test]
        public void TheGalleryPlaysEveryStepInsideTheBudgetAndRestoresBlood()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            using (var rig = new Rig(w, cfg))
            {
                var gallery = new FxGallery(rig.Scene, null);
                rig.Scene.Blood = true;
                int tick0 = w.Tick;
                double t0 = tick0 / cfg.SimHz;
                gallery.Begin(t0, w);
                Assert.IsTrue(gallery.Running);
                double end = t0 + rig.Scene.S(1.1) * (FxGallery.Order.Length + 1);
                bool sawBloodOff = false;
                for (double now = t0; now <= end + 0.1; now += 1.0 / 60.0)
                {
                    w.Tick = tick0 + (int)Math.Floor((now - t0) * cfg.SimHz); // the sim clock the gallery stamps effects with
                    gallery.Update(now, w);
                    if (!rig.Scene.Blood) sawBloodOff = true;
                    rig.Build(w, now);
                    Assert.AreEqual(0, rig.Dropped, $"gallery overflowed a mesh at step {gallery.Current}");
                }
                Assert.IsFalse(gallery.Running, "the gallery finished");
                Assert.IsTrue(sawBloodOff, "the blood-off steps ran");
                Assert.IsTrue(rig.Scene.Blood, "blood restored after the gallery");
                Assert.GreaterOrEqual(FxGallery.Order.Length, 30, "every effect has a step");
            }
        }
    }
}
