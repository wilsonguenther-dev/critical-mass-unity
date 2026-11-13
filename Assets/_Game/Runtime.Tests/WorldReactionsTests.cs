using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Reactions;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// World reactions (physics audit seat "world reactions", build list DG-77/78/79/88/94/96/102/103): the one force bus
    /// (priority, caps, ordering, scheduling, shader packing), the strength table, ground paint stamping hand-off, spring
    /// maths (fighter lag, Kaiju tail), death launch arcs (distance by tier, wall clamp, 500 seeded deaths), the shake
    /// default and its replacement's falloff, the post-pose shim's name parse and the feel tiers.
    /// </summary>
    [TestFixture]
    public sealed class WorldReactionsTests
    {
        private const double Hz = 30;

        private static WorldForce Force(ForceSource s, int tick, float x = 10, float z = 10, float size = 0f)
        {
            ForceTable.Of(s, size, ExplosionKind.Tnt, out float st, out float r, out float sh);
            return new WorldForce { Tick = tick, Source = s, X = x, Z = z, Strength = st, Radius = r, Shock = sh, Shape = ForceShape.Radial };
        }

        private sealed class Counter : IWorldForceListener
        {
            public int Count, LastSeq = -1, LastTick = int.MinValue;
            public bool Ordered = true;
            public readonly System.Collections.Generic.List<ForceSource> Seen = new System.Collections.Generic.List<ForceSource>();

            public void OnForce(ref WorldForce f)
            {
                if (f.Tick == LastTick && f.Seq != LastSeq + 1) Ordered = false;
                if (f.Tick != LastTick && f.Seq != 0) Ordered = false;
                LastTick = f.Tick;
                LastSeq = f.Seq;
                Count++;
                Seen.Add(f.Source);
                f.Strength = -1f; // must not leak to the next listener
            }
        }

        // ------------------------------------------------------------------ bus

        [Test]
        public void ASlamSurvivesA500EventFloodInItsSlot()
        {
            var bus = new WorldForceBus(Hz);
            int tick = 300;
            for (int n = 0; n < 500; n++)
            {
                if (n == 250) bus.Push(Force(ForceSource.KaijuSlam, tick, 20, 20, 5f));
                ForceSource s = n % 3 == 0 ? ForceSource.HitHeavy : ForceSource.HitLight;
                bus.Push(Force(s, tick + n / 50, 10 + n % 7, 10 + n % 5));
            }
            Assert.IsTrue(bus.SlotHolds(ForceSource.KaijuSlam, tick / Hz + 0.3), "the slam is still in a shader slot 0.3 s later");
            Assert.LessOrEqual(bus.LiveCount(tick / Hz + 0.3), WorldForceBus.SlotCount);
            var dst = new Vector4[WorldForceBus.SlotCount * 2];
            int count = bus.CopySlots(tick / Hz + 0.3, dst);
            bool found = false;
            for (int i = 0; i < count; i++) if (Mathf.Approximately(dst[i * 2].z, 5f * 1.15f)) found = true;
            Assert.IsTrue(found, "the packed shader slots carry the slam (radius = slam radius x 1.15)");
        }

        [Test]
        public void EveryListenerSeesTheSameForcesInSeqOrderAndCannotChangeThem()
        {
            var bus = new WorldForceBus(Hz);
            var a = new Counter();
            var b = new Counter();
            Assert.IsTrue(bus.Subscribe(a));
            Assert.IsTrue(bus.Subscribe(b));
            Assert.IsTrue(bus.Subscribe(a), "subscribing twice is a no-op");
            Assert.AreEqual(2, bus.ListenerCount);
            for (int t = 0; t < 5; t++)
            for (int n = 0; n < 7; n++)
                bus.Push(Force((ForceSource)(n % 5), 100 + t));
            Assert.AreEqual(35, a.Count);
            Assert.AreEqual(a.Count, b.Count, "same count per listener");
            Assert.IsTrue(a.Ordered && b.Ordered, "Seq counts 0, 1, 2 ... inside each tick");
            CollectionAssert.AreEqual(a.Seen, b.Seen, "same order");
            bus.Unsubscribe(a);
            bus.Push(Force(ForceSource.Land, 200));
            Assert.AreEqual(35, a.Count);
            Assert.AreEqual(36, b.Count);
        }

        [Test]
        public void ThePerTickCapDropsOnlyWeakForces()
        {
            var bus = new WorldForceBus(Hz) { PerTickCap = 10 };
            int delivered = 0;
            for (int n = 0; n < 50; n++) if (bus.Push(Force(ForceSource.HitLight, 7))) delivered++;
            Assert.AreEqual(10, delivered);
            Assert.AreEqual(40, bus.Dropped);
            Assert.IsTrue(bus.Push(Force(ForceSource.KaijuSlam, 7, size: 5f)), "a strong force is never capped");
            Assert.IsTrue(bus.Push(Force(ForceSource.HitLight, 8)), "the cap is per tick");
        }

        [Test]
        public void TheWeakestSlotIsEvictedNeverTheOldest()
        {
            var bus = new WorldForceBus(Hz);
            bus.Push(Force(ForceSource.KaijuSlam, 10, size: 5f)); // oldest and strongest
            for (int n = 0; n < WorldForceBus.SlotCount - 1; n++) bus.Push(Force(ForceSource.HitHeavy, 11));
            bus.Push(Force(ForceSource.HitCritical, 12));
            double now = 12 / Hz;
            Assert.IsTrue(bus.SlotHolds(ForceSource.KaijuSlam, now), "the oldest (a slam) stays");
            Assert.IsTrue(bus.SlotHolds(ForceSource.HitCritical, now), "the newcomer evicted a weaker heavy hit");
            bus.Push(Force(ForceSource.HitLight, 12));
            Assert.IsFalse(bus.SlotHolds(ForceSource.HitLight, now), "a weaker newcomer gets no slot (listeners still saw it)");
        }

        [Test]
        public void ShaderSlotsFlipZIntoUnityWorldSpace()
        {
            var bus = new WorldForceBus(Hz);
            WorldForce f = Force(ForceSource.HitHeavy, 30, 12f, 7f);
            f.DirX = 0.6f;
            f.DirZ = 0.8f;
            f.Shape = ForceShape.Cone;
            bus.Push(f);
            var dst = new Vector4[WorldForceBus.SlotCount * 2];
            Assert.AreEqual(1, bus.CopySlots(30 / Hz, dst));
            Assert.AreEqual(12f, dst[0].x, 1e-5f);
            Assert.AreEqual(-7f, dst[0].y, 1e-5f, "sim z is -Unity z");
            Assert.AreEqual(1.4f, dst[0].z, 1e-5f);
            Assert.AreEqual(0.5f, dst[0].w, 1e-5f, "full strength at age 0");
            Assert.AreEqual(-0.8f, dst[1].y, 1e-5f);
            Assert.AreEqual((float)ForceShape.Cone, dst[1].w);
            Assert.AreEqual(Vector4.zero, dst[2], "unused slots are zero");
        }

        [Test]
        public void ScheduledForcesArriveOnTheirTick()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
            });
            var bus = new WorldForceBus(cfg.SimHz);
            var c = new Counter();
            bus.Subscribe(c);
            int due = w.Tick + 3;
            Assert.IsTrue(bus.Schedule(Force(ForceSource.Land, 0), due));
            bus.OnTick(w);
            Assert.AreEqual(0, c.Count, "not yet");
            var scratch = new InputFrame();
            for (int i = 0; i < 3; i++) MatchTick.AdvanceNoHuman(w, scratch);
            bus.OnTick(w);
            Assert.AreEqual(1, c.Count);
            Assert.AreEqual(w.Tick, c.LastTick, "stamped with the tick it lands on");
            for (int n = 0; n < WorldForceBus.MaxPending; n++) bus.Schedule(Force(ForceSource.Land, 0), int.MaxValue);
            Assert.IsFalse(bus.Schedule(Force(ForceSource.Land, 0), int.MaxValue), "the pending list is capped");
        }

        [Test]
        public void TheStrengthTableHoldsTheAuditValues()
        {
            ForceTable.Of(ForceSource.HitLight, 0, ExplosionKind.Tnt, out float s, out float r, out float sh);
            Assert.AreEqual((0.25f, 0.9f), (s, r));
            ForceTable.Of(ForceSource.KaijuStep, 0, ExplosionKind.Tnt, out s, out r, out sh);
            Assert.AreEqual((0.6f, 3f, 0.35f), (s, r, sh));
            ForceTable.Of(ForceSource.KaijuSlam, 5, ExplosionKind.KaijuSlam, out s, out r, out sh);
            Assert.AreEqual(1f, s);
            Assert.AreEqual(5.75f, r, 1e-5f);
            Assert.AreEqual(0.9f, sh);
            ForceTable.Of(ForceSource.Explosion, 2, ExplosionKind.Tnt, out s, out r, out sh);
            Assert.AreEqual(0.76f, s, 1e-5f);
            Assert.AreEqual(3.4f, r, 1e-5f);
            Assert.AreEqual(FxScene.TraumaPeak(ExplosionKind.Tnt), sh);
            Assert.AreEqual(2.2f, ForceTable.LaunchDistance(2, 3));
            Assert.AreEqual(3f, ForceTable.LaunchDistance(2, Damage.KaijuSource));
        }

        /// <summary>Contract C1: World.Events is read by the bus's decoder and by the known readers that still have to move
        /// onto the bus (other lanes' files). A new reader anywhere else fails this test.</summary>
        [Test]
        public void NoNewReaderOfWorldEventsOutsideTheBus()
        {
            string root = Path.Combine(Application.dataPath, "_Game/Runtime");
            string[] known =
            {
                "View/Effects/CombatFeed.cs", "View/KaijuMoments.cs", "Game/CombatLog.cs", "Game/MatchStats.cs", "Hud/GameHud.cs",
                "Hud/DebugHud.cs",
            };
            var readers = Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                .Where(f => Regex.IsMatch(File.ReadAllText(f), @"\.EventHead\b"))
                .Select(f => f.Substring(root.Length + 1).Replace('\\', '/'))
                .Where(f => !known.Any(k => f.EndsWith(k, StringComparison.Ordinal)))
                .ToList();
            CollectionAssert.IsEmpty(readers, "new World.Events readers: subscribe to WorldForceBus instead");
        }

        // ------------------------------------------------------------------ ground paint hand-off

        private sealed class FakePaint : IGroundPaintTarget
        {
            public bool Accepts => true;
            public int Frame { get; set; }
            public FxMeshBuilder Builder { get; } = new FxMeshBuilder("paint", 12000, 36000);
        }

        [Test]
        public void StillMarksAreStampedOnceThenLeaveTheGroundMesh()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
            });
            PadDef pad = w.Map.Pads[0];
            var scene = new FxScene(cfg, w.Map);
            var paint = new FakePaint();
            scene.Paint = paint;
            var g = new FxMeshBuilder("g", CombatFx.GroundVerts, CombatFx.GroundVerts * 3);
            var a = new FxMeshBuilder("a", CombatFx.AirVerts, CombatFx.AirVerts * 3);
            var s = new FxMeshBuilder("s", CombatFx.SolidVerts, CombatFx.SolidVerts * 2);
            try
            {
                int tick = 30;
                var e = new FxEvent { Kind = FxKind.Explosion, Tick = tick, A = -1, B = (int)ExplosionKind.Tnt, X = pad.X, Z = pad.Z, Value = 2.0 };
                scene.OnFx(ref e, w);
                double now = tick / cfg.SimHz + scene.S(1.0); // scorch has spread, long before its fade
                scene.Advance(now);
                scene.Build(now, w, Vector3.right, Vector3.up, g, a, s);
                int stamped = paint.Builder.VertCount;
                Assert.Greater(stamped, 0, "the scorch was stamped into the paint");
                Assert.Greater(scene.LastGroundMarks, 0, "and is still drawn this frame (no blank frame)");
                scene.Build(now + 0.01, w, Vector3.right, Vector3.up, g, a, s);
                Assert.AreEqual(stamped, paint.Builder.VertCount, "stamped once only");
                Assert.Greater(scene.LastGroundMarks, 0, "still drawn until the paint has flushed");
                paint.Frame++;
                scene.Build(now + 0.02, w, Vector3.right, Vector3.up, g, a, s);
                Assert.AreEqual(0, scene.LastGroundMarks, "after the flush the mark lives only in the paint");
                Assert.AreEqual(stamped, paint.Builder.VertCount);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(g.Mesh);
                UnityEngine.Object.DestroyImmediate(a.Mesh);
                UnityEngine.Object.DestroyImmediate(s.Mesh);
                UnityEngine.Object.DestroyImmediate(paint.Builder.Mesh);
            }
        }

        // ------------------------------------------------------------------ springs

        [Test]
        public void TheHeadLagsAStartBy3To10DegreesAndSettlesWithinAQuarterSecond()
        {
            var lag = new FighterLag();
            const float dt = 1f / 60f;
            Vector3 p = Vector3.zero;
            float speed = 0f, peak = 0f;
            for (int f = 0; f < 6; f++) lag.Step(p, dt); // standing
            for (int f = 0; f < 12; f++)                 // 0 to 4 u/s in 0.2 s
            {
                speed = Mathf.Min(4f, speed + 4f / 12f);
                p += Vector3.right * speed * dt;
                peak = Mathf.Max(peak, lag.Step(p, dt).magnitude);
            }
            Assert.That(peak, Is.InRange(3f, 10f), "head lag on the start");
            Vector2 tilt = Vector2.zero;
            for (int f = 0; f < 15; f++) { p += Vector3.right * speed * dt; tilt = lag.Step(p, dt); } // 0.25 s at full speed
            Assert.Less(tilt.magnitude, 0.5f, "settled within 0.25 s");
            // Stopping leans the other way (forward).
            float stopPeak = 0f;
            Vector2 last = Vector2.zero;
            for (int f = 0; f < 12; f++)
            {
                speed = Mathf.Max(0f, speed - 4f / 12f);
                p += Vector3.right * speed * dt;
                last = lag.Step(p, dt);
                stopPeak = Mathf.Max(stopPeak, last.magnitude);
            }
            Assert.That(stopPeak, Is.InRange(3f, 10f), "head lag on the stop");
            Assert.Greater(last.x, 0f, "a stop tips the top forward (+x is the run direction)");
        }

        [Test]
        public void TheKaijuTailTrailsANinetyDegreeTurnByAtLeast15Degrees()
        {
            var tail = new KaijuTail();
            var off = new float[5];
            const float dt = 1f / 60f;
            float yaw = 0f;
            for (int f = 0; f < 10; f++) tail.Step(yaw, dt, off);
            for (int f = 0; f < 30; f++) { yaw += 90f / 30f; tail.Step(yaw, dt, off); } // 90 degrees in 0.5 s
            Assert.GreaterOrEqual(tail.Tail3Trail, 15f);
            Assert.Less(off[2], 0f, "the tail trails against the turn");
            for (int i = 0; i < 3; i++) Assert.LessOrEqual(Mathf.Abs(off[i]), KaijuTail.MaxDeg);
            for (int f = 0; f < 180; f++) tail.Step(yaw, dt, off);
            Assert.Less(tail.Tail3Trail, 1f, "settles once the turn ends");
        }

        [Test]
        public void SpringsAreStableAtLowFrameRates()
        {
            var s = new LagSpring2();
            for (int f = 0; f < 20; f++) s.Step(new Vector2(5f, 0f), 22f, 1f, 0.1f);
            Assert.AreEqual(5f, s.X.x, 0.05f);
            Assert.IsFalse(float.IsNaN(s.X.x));
        }

        // ------------------------------------------------------------------ death launch

        private static MapDef OpenMap(int n, byte fill = Cell.Floor)
        {
            var cells = new byte[n * n];
            for (int i = 0; i < cells.Length; i++) cells[i] = fill;
            return new MapDef { Width = n, Height = n, Cells = cells, Height01 = new double[n * n] };
        }

        [Test]
        public void ACriticalDeathFliesMoreThan1Point8UnitsAlongTheHitAndLandsOnTheGround()
        {
            MapDef map = OpenMap(20);
            LaunchPlan p = DeathLaunch.Plan(map, 10, 10, 1, 0, 2, 4);
            Assert.AreEqual(2.2f, p.Distance, 1e-4f);
            Assert.AreEqual(360f, p.Tumble);
            float apexSeen = 0f;
            for (float t = 0f; t < DeathLaunch.Duration(p); t += 0.01f)
            {
                DeathLaunch.Evaluate(p, t, out float along, out float h, out float tumble);
                Assert.GreaterOrEqual(h, -1e-4f, "never below the ground on solid floor");
                apexSeen = Mathf.Max(apexSeen, h);
            }
            DeathLaunch.Evaluate(p, DeathLaunch.Duration(p) + 0.1f, out float end, out float endH, out float endTumble);
            Assert.Greater(end, 1.8f);
            Assert.AreEqual(0f, endH, 1e-4f);
            Assert.AreEqual(360f, endTumble, "a whole turn: the body lands upright for the Death clip");
            Assert.AreEqual(p.Apex, apexSeen, 0.05f);
            Assert.AreEqual(0.4f, DeathLaunch.Plan(map, 10, 10, 0, 1, 0, 3).Distance, 1e-4f, "a light finisher barely moves");
            Assert.AreEqual(0f, DeathLaunch.Plan(map, 10, 10, 1, 0, 2, 4, 0.25f).Tumble, 1e-4f, "no tumble below critical at reduced motion either");
        }

        [Test]
        public void FiveHundredSeededDeathsNeverEndInsideAWall()
        {
            MapDef map = OpenMap(24);
            var rng = new System.Random(1234);
            for (int i = 0; i < 90; i++) map.Cells[rng.Next(map.Cells.Length)] = Cell.Wall;
            int checkedDeaths = 0;
            for (int n = 0; n < 500; n++)
            {
                double x = 2 + rng.NextDouble() * 20, z = 2 + rng.NextDouble() * 20;
                if (DeathLaunch.Blocks(map.Cells[(int)z * map.Width + (int)x])) continue;
                double a = rng.NextDouble() * Math.PI * 2;
                byte tier = (byte)rng.Next(3);
                int other = n % 7 == 0 ? Damage.KaijuSource : n % 11 == 0 ? -1 : 3;
                LaunchPlan p = DeathLaunch.Plan(map, x, z, Math.Cos(a), Math.Sin(a), tier, other);
                for (float t = 0f; t <= DeathLaunch.Duration(p) + 0.05f; t += 0.02f)
                {
                    DeathLaunch.Evaluate(p, t, out float along, out float h, out float tumble);
                    int px = (int)Math.Floor(x + p.DirX * along), pz = (int)Math.Floor(z + p.DirZ * along);
                    if (px < 0 || pz < 0 || px >= map.Width || pz >= map.Height) { Assert.IsTrue(p.Falls, $"death {n} left the map without falling"); continue; }
                    Assert.IsFalse(DeathLaunch.Blocks(map.Cells[pz * map.Width + px]), $"death {n} passes through a wall at t {t}");
                }
                checkedDeaths++;
            }
            Assert.Greater(checkedDeaths, 400);
        }

        [Test]
        public void ADeathOverTheVoidFalls()
        {
            MapDef map = OpenMap(20, Cell.Void);
            map.Cells[10 * 20 + 10] = Cell.Floor;
            LaunchPlan p = DeathLaunch.Plan(map, 10.5, 10.5, 1, 0, 2, 4);
            Assert.IsTrue(p.Falls);
            DeathLaunch.Evaluate(p, p.Flight + 0.5f, out float along, out float h, out float tumble);
            Assert.Less(h, -1f);
        }

        // ------------------------------------------------------------------ settings, shake replacement, shim, tiers

        [Test]
        public void ShakeIsOffOnAFreshInstallEverywhere()
        {
            bool had = PlayerPrefs.HasKey(GameSettings.ShakeKey);
            int old = PlayerPrefs.GetInt(GameSettings.ShakeKey, 0);
            bool hadRm = PlayerPrefs.HasKey(GameSettings.ReducedMotionKey);
            int oldRm = PlayerPrefs.GetInt(GameSettings.ReducedMotionKey, 0);
            try
            {
                PlayerPrefs.DeleteKey(GameSettings.ShakeKey);
                PlayerPrefs.DeleteKey(GameSettings.ReducedMotionKey);
                Assert.IsFalse(GameSettings.CameraShake);
                Assert.IsFalse(Effects.ShakeEnabled);
                Assert.AreEqual(GameSettings.CameraShake, Effects.ShakeEnabled);
                Assert.AreEqual(1f, ReactionBudget.MotionScale);
                PlayerPrefs.SetInt(GameSettings.ReducedMotionKey, 1);
                Assert.AreEqual(0.25f, ReactionBudget.MotionScale);
                PlayerPrefs.SetInt(GameSettings.ShakeKey, 1);
                Assert.IsTrue(Effects.ShakeEnabled, "the player can still turn shake on");
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(GameSettings.ShakeKey, old); else PlayerPrefs.DeleteKey(GameSettings.ShakeKey);
                if (hadRm) PlayerPrefs.SetInt(GameSettings.ReducedMotionKey, oldRm); else PlayerPrefs.DeleteKey(GameSettings.ReducedMotionKey);
            }
        }

        [Test]
        public void ATntFourUnitsAwayIsFeltAboveTheFlashThresholdAndNothingAt40()
        {
            WorldForce f = Force(ForceSource.Explosion, 1, 14, 10, 2f);
            Assert.Greater(CameraResponse.Felt(in f, 10, 10), CameraResponse.FlashThreshold);
            Assert.Greater(CameraResponse.Felt(in f, 10, 10), CameraResponse.RumbleThreshold);
            Assert.AreEqual(0f, CameraResponse.Felt(in f, 54, 10));
            WorldForce light = Force(ForceSource.HitLight, 1, 10, 10);
            Assert.Less(CameraResponse.Felt(in light, 10, 10), CameraResponse.FlashThreshold, "a light hit never flashes");
        }

        [Test]
        public void TheShimReadsActorIndicesFromModelNamesOnly()
        {
            Assert.AreEqual(12, PostPoseShim.ParseActor("Actor 12 Warrior"));
            Assert.AreEqual(0, PostPoseShim.ParseActor("Actor 0 Miner"));
            Assert.AreEqual(-1, PostPoseShim.ParseActor("Actor 12"), "the grey-box capsule has no model");
            Assert.AreEqual(-1, PostPoseShim.ParseActor("Kaiju Adult"));
            Assert.AreEqual(-1, PostPoseShim.ParseActor("Actor X Warrior"));
            Assert.AreEqual(-1, PostPoseShim.ParseActor(null));
        }

        [Test]
        public void FeelTiersFollowTheQualityLevels()
        {
            Assert.AreEqual(ReactionTier.Low, ReactionBudget.FromQualityLevel(0));
            Assert.AreEqual(ReactionTier.Low, ReactionBudget.FromQualityLevel(1));
            Assert.AreEqual(ReactionTier.Medium, ReactionBudget.FromQualityLevel(3));
            Assert.AreEqual(ReactionTier.High, ReactionBudget.FromQualityLevel(5));
            ReactionBudget l = ReactionBudget.For(ReactionTier.Low), m = ReactionBudget.For(ReactionTier.Medium), h = ReactionBudget.For(ReactionTier.High);
            Assert.AreEqual(0, l.ForceTexSize, "Low reads the 16 slots, no texture");
            Assert.AreEqual((128, 48f), (m.ForceTexSize, m.ForceWindow));
            Assert.AreEqual((256, 64f), (h.ForceTexSize, h.ForceWindow));
            Assert.IsFalse(l.FighterSprings);
            Assert.IsTrue(h.Footprints && !m.Footprints);
            Assert.That(l.PerTickCap, Is.LessThan(m.PerTickCap));
            Assert.That(m.PerTickCap, Is.LessThan(h.PerTickCap));
        }

        [Test]
        public void SwayKindsCoverTheMovingKitPieces()
        {
            Assert.AreEqual(1, View.Dressing.WorldKit.SwayKind("env_grass_tuft"));
            Assert.AreEqual(1, View.Dressing.WorldKit.SwayKind("env_flowers"));
            Assert.AreEqual(2, View.Dressing.WorldKit.SwayKind("env_bush"));
            Assert.AreEqual(3, View.Dressing.WorldKit.SwayKind("env_tree_round_l"));
            Assert.AreEqual(4, View.Dressing.WorldKit.SwayKind("env_banner"));
            Assert.AreEqual(5, View.Dressing.WorldKit.SwayKind("env_bridge_span"));
            Assert.AreEqual(7, View.Dressing.WorldKit.SwayKind("env_waterfall"));
            Assert.AreEqual(0, View.Dressing.WorldKit.SwayKind("env_ledge_band"), "ledges stay still and pay nothing");
            Assert.AreEqual(0, View.Dressing.WorldKit.SwayKind("env_ground_cell"));
        }
    }
}
