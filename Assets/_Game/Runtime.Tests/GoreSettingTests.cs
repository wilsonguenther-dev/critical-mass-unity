using System;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// The GORE setting (owner, 2026-11-14): Full (default, today's look), Reduced (no bones or body parts, fewer and
    /// shorter-lived splats, nothing builds up) and Off (no blood, bones or body parts at all). GoreGate is the one switch
    /// and GoreGate.All its registry: these tests enumerate it, drive every hit, death, Kaiju and status path into a real
    /// FxScene at each level, and count what each registry entry spawned.
    /// </summary>
    public sealed class GoreSettingTests
    {
        private static World NewWorld(GameConfig cfg) => World.CreateWorld(new WorldOptions
        {
            Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
        });

        private sealed class Run
        {
            public FxScene Scene;
            public double T;
            public int VertsAtOneSecond, ActiveAtOneSecond, ActiveAtThreeSeconds;
        }

        /// <summary>Every gore-producing path once: three hit tiers, a Kaiju claw, a knock-out inside a blast and a Kaiju slam,
        /// a melee knock-out, and a badly hurt fighter walking (drips); then 6 s of advance and mesh builds.</summary>
        private static Run Drive(GoreLevel level, GameConfig cfg, World ww)
        {
            var scene = new FxScene(cfg, ww.Map) { Gore = level };
            PadDef pad = ww.Map.Pads[0];
            const int tick = 30;
            ww.Tick = tick;
            double t = tick / cfg.SimHz;
            double[] dmg = { 12, 22, 45 };
            for (int n = 0; n < 3; n++)
            {
                var hit = new FxEvent { Kind = FxKind.Hit, Tick = tick, A = 1, B = 2, X = pad.X, Z = pad.Z, Value = dmg[n], DirX = 0.6, DirZ = 0.8, Cls = ClassId.Miner, Tier = CombatFeed.TierOf(dmg[n], false), HasAttacker = true, AttCls = ClassId.Warrior };
                scene.OnFx(ref hit, ww);
            }
            var claw = new FxEvent { Kind = FxKind.Hit, Tick = tick, A = 1, B = Damage.KaijuSource, X = pad.X, Z = pad.Z, Value = 30, DirX = 1, Cls = ClassId.Farmer, Tier = 1 };
            scene.OnFx(ref claw, ww);
            var melee = new FxEvent { Kind = FxKind.Death, Tick = tick, A = 4, B = 2, X = pad.X - 0.5, Z = pad.Z, Value = 22, DirX = -1, Cls = ClassId.Warrior, Tier = 2, HasAttacker = true, AttCls = ClassId.Miner };
            scene.OnFx(ref melee, ww);
            var ko = new FxEvent { Kind = FxKind.Death, Tick = tick, A = 3, B = -1, X = pad.X + 0.5, Z = pad.Z, Value = 30, DirX = 1, Cls = ClassId.Builder, Tier = 2, Cause = DamageCause.Explosion };
            scene.OnFx(ref ko, ww);
            var boom = new FxEvent { Kind = FxKind.Explosion, Tick = tick, A = -1, B = (int)ExplosionKind.Tnt, X = pad.X + 0.5, Z = pad.Z, Value = 2.0 };
            scene.OnFx(ref boom, ww);
            var slam = new FxEvent { Kind = FxKind.KaijuSlam, Tick = tick, A = -1, B = -1, X = pad.X + 0.5, Z = pad.Z, Value = 3.0 };
            scene.OnFx(ref slam, ww);
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
            var run = new Run { Scene = scene, T = t };
            var g = new FxMeshBuilder("g", CombatFx.GroundVerts, CombatFx.GroundVerts * 3);
            var a = new FxMeshBuilder("a", CombatFx.AirVerts, CombatFx.AirVerts * 3);
            var s = new FxMeshBuilder("s", CombatFx.SolidVerts, CombatFx.SolidVerts * 2);
            try
            {
                for (double now = t; now < t + 6.0; now += 0.1)
                {
                    scene.Advance(now);
                    scene.Build(now, ww, Vector3.right, Vector3.up, g, a, s);
                    if (Math.Abs(now - (t + 1.0)) < 0.05) { run.VertsAtOneSecond = g.VertCount + a.VertCount + s.VertCount; run.ActiveAtOneSecond = scene.ActiveCount(now); }
                    if (Math.Abs(now - (t + 3.0)) < 0.05) run.ActiveAtThreeSeconds = scene.ActiveCount(now);
                    if (level == GoreLevel.Off) Assert.AreEqual(0, scene.BloodCount(now), $"GORE Off: a blood piece is alive at {now - t:0.0} s");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(g.Mesh); UnityEngine.Object.DestroyImmediate(a.Mesh); UnityEngine.Object.DestroyImmediate(s.Mesh);
            }
            return run;
        }

        [Test]
        public void TheRegistryIsGatedAtEveryLevel()
        {
            Assert.Greater(GoreGate.All.Length, 10, "every gore piece is in the registry");
            foreach (GorePiece p in GoreGate.All)
            {
                Assert.IsTrue(GoreGate.Allows(p, GoreLevel.Full), p + " is part of today's look at Full");
                Assert.IsFalse(GoreGate.Allows(p, GoreLevel.Off), p + " must never show at Off");
                if (GoreGate.IsBodyPart(p)) Assert.IsFalse(GoreGate.Allows(p, GoreLevel.Reduced), p + " is a bone or body part: never at Reduced");
            }
            foreach (GorePiece p in new[] { GorePiece.GroundStain, GorePiece.KnockOutPool, GorePiece.BloodDrip, GorePiece.WallTopSplat })
                Assert.IsFalse(GoreGate.Allows(p, GoreLevel.Reduced), p + ": nothing builds up on the ground at Reduced");
            Assert.AreEqual(0f, GoreGate.CountScale(GoreLevel.Off));
            Assert.Less(GoreGate.CountScale(GoreLevel.Reduced), 1f);
            Assert.Less(GoreGate.LifeScale(GoreLevel.Reduced), 1f);
            Assert.AreEqual(GoreLevel.Reduced, GoreGate.Step(GoreLevel.Full, 1));
            Assert.AreEqual(GoreLevel.Off, GoreGate.Step(GoreLevel.Reduced, 1));
            Assert.AreEqual(GoreLevel.Full, GoreGate.Step(GoreLevel.Off, 1));
            Assert.AreEqual(GoreLevel.Off, GoreGate.Step(GoreLevel.Full, -1));
        }

        [Test]
        public void AtOffNoGorePieceSpawnsThroughAnyPath()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            Run r = Drive(GoreLevel.Off, cfg, NewWorld(cfg));
            foreach (GorePiece p in GoreGate.All) Assert.AreEqual(0, r.Scene.GoreSpawns(p), p + " spawned at GORE Off");
            Assert.Greater(r.Scene.SparkCount(r.T + 0.05), 0, "GORE Off: star sparks at the contact point instead");
            Assert.AreEqual(0, r.Scene.DecalCount(r.T + 3.0));
            Assert.AreEqual(0, r.Scene.GibCount(r.T + 0.2));
            Assert.AreEqual(0, r.Scene.DripCount(r.T + 3.0));
        }

        [Test]
        public void ReducedSpawnsNoBonesBodyPartsOrBuildUp()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            Run full = Drive(GoreLevel.Full, cfg, NewWorld(cfg));
            Run red = Drive(GoreLevel.Reduced, cfg, NewWorld(cfg));
            foreach (GorePiece p in GoreGate.All)
                if (!GoreGate.Allows(p, GoreLevel.Reduced)) Assert.AreEqual(0, red.Scene.GoreSpawns(p), p + " spawned at GORE Reduced");
            Assert.Greater(red.Scene.GoreSpawns(GorePiece.BloodDroplet), 0, "Reduced still shows some blood");
            Assert.Less(red.Scene.GoreSpawns(GorePiece.BloodDroplet), full.Scene.GoreSpawns(GorePiece.BloodDroplet), "fewer drops than Full");
            Assert.Less(red.Scene.GoreSpawns(GorePiece.BloodSplat), full.Scene.GoreSpawns(GorePiece.BloodSplat), "fewer splats than Full");
            Assert.AreEqual(0, red.Scene.DecalCount(red.T + red.Scene.S(5.0)), "Reduced splats are gone within about 4 s wall");
        }

        [Test]
        public void FullIsTodaysLook()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            Run full = Drive(GoreLevel.Full, cfg, NewWorld(cfg));
            // Today's drop count per hit: 2 + min(6, damage / 6) + 2 x tier, plus one mist drop per tier; 14 + 2 per knock-out.
            int expected = 0;
            foreach (double d in new double[] { 12, 22, 45 })
            {
                int tier = CombatFeed.TierOf(d, false);
                expected += 2 + (int)Math.Min(6, d / 6) + tier * 2 + tier;
            }
            expected += 2 + (int)Math.Min(6, 30 / 6.0) + 2 + 1;   // the Kaiju claw (tier 1)
            expected += (14 + 2) * 2;                             // two knock-outs (14 drops and two mist drops each)
            Assert.AreEqual(expected, full.Scene.GoreSpawns(GorePiece.BloodDroplet), "Full throws exactly today's drops");
            Assert.Greater(full.Scene.GoreSpawns(GorePiece.KnockOutPool), 0, "the knock-out pool");
            Assert.Greater(full.Scene.GoreSpawns(GorePiece.GibMeat), 0, "meat from the blast knock-out");
            Assert.Greater(full.Scene.GoreSpawns(GorePiece.GibBone), 0, "bones from the blast knock-out");
            Assert.Greater(full.Scene.GoreSpawns(GorePiece.BloodDrip), 0, "drips behind the hurt fighter");
            Assert.Greater(full.Scene.DecalCount(full.T + 3.0), 0, "splats stay on the ground");
            var fresh = new FxScene(cfg, NewWorld(cfg).Map);
            Assert.AreEqual(GoreLevel.Full, fresh.Gore, "a scene starts at Full");
        }

        [Test]
        public void OffAndReducedCostLessThanFull()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            Run full = Drive(GoreLevel.Full, cfg, NewWorld(cfg));
            Run red = Drive(GoreLevel.Reduced, cfg, NewWorld(cfg));
            Run off = Drive(GoreLevel.Off, cfg, NewWorld(cfg));
            Assert.Less(red.ActiveAtOneSecond, full.ActiveAtOneSecond, "Reduced: fewer live effects than Full");
            Assert.Less(off.ActiveAtOneSecond, red.ActiveAtOneSecond, "Off: fewer live effects than Reduced");
            Assert.Less(red.ActiveAtThreeSeconds, full.ActiveAtThreeSeconds);
            Assert.Less(off.ActiveAtThreeSeconds, red.ActiveAtThreeSeconds);
            Assert.Less(red.VertsAtOneSecond, full.VertsAtOneSecond, "Reduced: fewer vertices than Full");
            Assert.Less(off.VertsAtOneSecond, full.VertsAtOneSecond, "Off: fewer vertices than Full");
        }

        [Test]
        public void TheSettingDefaultsToFullAndPersists()
        {
            bool hadG = PlayerPrefs.HasKey(GameSettings.GoreKey), hadB = PlayerPrefs.HasKey(GameSettings.BloodKey);
            int oldG = PlayerPrefs.GetInt(GameSettings.GoreKey, 2), oldB = PlayerPrefs.GetInt(GameSettings.BloodKey, 1);
            GoreLevel? oldOverride = GoreGate.Override;
            try
            {
                GoreGate.Override = null;
                PlayerPrefs.DeleteKey(GameSettings.GoreKey);
                PlayerPrefs.DeleteKey(GameSettings.BloodKey);
                Assert.AreEqual(GoreLevel.Full, GameSettings.Gore, "Full is the default");
                Assert.AreEqual(GoreLevel.Full, GoreGate.Current);
                Assert.IsFalse(PlayerPrefs.HasKey(GameSettings.GoreKey), "a default is not written until changed");
                Assert.Contains(GameSettings.GoreKey, GameSettings.AllKeys, "reset clears it with the other settings");

                GameSettings.Gore = GoreLevel.Reduced;
                Assert.AreEqual(1, PlayerPrefs.GetInt("cm.gore", -1), "stored in PlayerPrefs for the next launch");
                Assert.AreEqual(GoreLevel.Reduced, GoreGate.Current);
                GameSettings.Gore = GoreLevel.Off;
                Assert.AreEqual(0, PlayerPrefs.GetInt("cm.gore", -1));
                Assert.IsFalse(BloodSetting.Enabled, "Off turns the legacy blood flag off");

                // A player who had switched the old BLOOD option off starts at Off.
                PlayerPrefs.DeleteKey(GameSettings.GoreKey);
                PlayerPrefs.SetInt(GameSettings.BloodKey, 0);
                Assert.AreEqual(GoreLevel.Off, GameSettings.Gore);

                GoreGate.Override = GoreLevel.Reduced;
                Assert.AreEqual(GoreLevel.Reduced, GoreGate.Current, "tests and captures can pin the level");
            }
            finally
            {
                GoreGate.Override = oldOverride;
                if (hadG) PlayerPrefs.SetInt(GameSettings.GoreKey, oldG); else PlayerPrefs.DeleteKey(GameSettings.GoreKey);
                if (hadB) PlayerPrefs.SetInt(GameSettings.BloodKey, oldB); else PlayerPrefs.DeleteKey(GameSettings.BloodKey);
            }
        }

        [Test]
        public void NoLevelWritesTheWorld()
        {
            // The effects only read the sim: the fixed-seed checksum cannot depend on the GORE level.
            GameConfig cfg = GameConfig.CreateDefault();
            foreach (GoreLevel level in GoreGate.Levels)
            {
                World w = NewWorld(cfg);
                PadDef pad = w.Map.Pads[0];
                ulong before = Hash(w);
                var scene = new FxScene(cfg, w.Map) { Gore = level };
                var e = new FxEvent { Kind = FxKind.Death, Tick = w.Tick, A = 1, B = 2, X = pad.X, Z = pad.Z, Value = 40, DirX = 1, Cls = ClassId.Miner, Tier = 2, Cause = DamageCause.Explosion };
                scene.OnFx(ref e, w);
                var slam = new FxEvent { Kind = FxKind.KaijuSlam, Tick = w.Tick, A = -1, B = -1, X = pad.X, Z = pad.Z, Value = 3.0 };
                scene.OnFx(ref slam, w);
                scene.OnTickState(w);
                scene.Advance(w.Tick / cfg.SimHz + 2.0);
                Assert.AreEqual(before, Hash(w), "GORE " + level + " changed the sim state");
            }
        }

        private static ulong Hash(World w)
        {
            ulong h = 1469598103934665603UL;
            void Mix(double v) { h = (h ^ (ulong)BitConverter.DoubleToInt64Bits(v)) * 1099511628211UL; }
            Mix(w.Tick);
            for (int i = 0; i < w.ActorCount; i++) { Mix(w.X[i]); Mix(w.Z[i]); Mix(w.Actors[i].Hp); Mix(w.Actors[i].Alive ? 1 : 0); }
            Mix(w.Kaiju.X); Mix(w.Kaiju.Z);
            return h;
        }
    }
}
