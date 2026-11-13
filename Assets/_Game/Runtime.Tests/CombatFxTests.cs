using System;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>Combat feel (Runtime/View/Effects): view-only, allocation-free after warm-up, capped, blood setting honoured.</summary>
    [TestFixture]
    public sealed class CombatFxTests
    {
        private static World NewWorld(GameConfig cfg) => World.CreateWorld(new WorldOptions
        {
            Seed = cfg.Seed, TeamSize = cfg.TeamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg,
        });

        private static void Mix(ref ulong h, double v)
        {
            h ^= (ulong)BitConverter.DoubleToInt64Bits(v);
            h *= 1099511628211UL;
        }

        /// <summary>FNV-1a over the state the sim owns: positions, velocities, hp, timers, kaiju, match clock, events.</summary>
        public static ulong StateHash(World w)
        {
            ulong h = 14695981039346656037UL;
            Mix(ref h, w.Tick);
            Mix(ref h, w.EventHead);
            Mix(ref h, w.Match.Time);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                Mix(ref h, w.X[i]); Mix(ref h, w.Z[i]); Mix(ref h, w.Vx[i]); Mix(ref h, w.Vz[i]);
                Mix(ref h, a.Hp); Mix(ref h, a.Alive ? 1 : 0); Mix(ref h, a.Facing); Mix(ref h, a.AttackCd);
                Mix(ref h, a.AbilityCd); Mix(ref h, a.AnimAction); Mix(ref h, a.Kills); Mix(ref h, a.Deaths);
                Mix(ref h, a.CarryAmount); Mix(ref h, a.RespawnIn);
            }
            Kaiju k = w.Kaiju;
            Mix(ref h, k.X); Mix(ref h, k.Z); Mix(ref h, k.Hp); Mix(ref h, (int)k.State); Mix(ref h, k.TelegraphIn);
            for (int n = 0; n < World.EventCapacity; n++)
            {
                SimEvent e = w.Events[n];
                Mix(ref h, (int)e.Type); Mix(ref h, e.A); Mix(ref h, e.B); Mix(ref h, e.X); Mix(ref h, e.Z); Mix(ref h, e.Value);
            }
            return h;
        }

        private sealed class Rig : IDisposable
        {
            public readonly CombatFeed Feed;
            public readonly FxScene Scene;
            public readonly CombatAudio Audio;
            public readonly FxMeshBuilder G, A, S;
            private readonly GameObject _go;

            public Rig(World w, GameConfig cfg, bool audio)
            {
                Feed = new CombatFeed(World.ActorCapacity(cfg));
                Scene = new FxScene(cfg, w.Map);
                _go = new GameObject("CombatFxTests");
                Audio = audio ? new CombatAudio(_go.transform, cfg) : null;
                G = new FxMeshBuilder("g", CombatFx.GroundVerts, CombatFx.GroundVerts * 3);
                A = new FxMeshBuilder("a", CombatFx.AirVerts, CombatFx.AirVerts * 3);
                S = new FxMeshBuilder("s", CombatFx.SolidVerts, CombatFx.SolidVerts * 2);
            }

            public void Frame(World w)
            {
                Feed.OnTick(w, Scene, Audio);
                double now = w.Tick / w.Config.SimHz;
                Audio?.Update(now, w);
                Scene.Advance(now);
                Scene.Build(now, w, Vector3.right, Vector3.up, G, A, S);
                G.Upload(); A.Upload(); S.Upload();
            }

            public void Dispose()
            {
                Audio?.Dispose();
                UnityEngine.Object.DestroyImmediate(_go);
                UnityEngine.Object.DestroyImmediate(G.Mesh);
                UnityEngine.Object.DestroyImmediate(A.Mesh);
                UnityEngine.Object.DestroyImmediate(S.Mesh);
            }
        }

        [Test]
        public void SimStateHashOfAFixedSeedMatchIsIdenticalWithEffectsOnAndOff()
        {
            GameConfig cfgOff = GameConfig.CreateDefault(), cfgOn = GameConfig.CreateDefault();
            World off = NewWorld(cfgOff), on = NewWorld(cfgOn);
            InputFrame f1 = SimInput.EmptyInput(), f2 = SimInput.EmptyInput();
            using (var rig = new Rig(on, cfgOn, true))
            {
                // 9000 ticks = 300 sim s: past the 240 s hatch, so Kaiju effects run too.
                for (int t = 0; t < 9000; t++)
                {
                    MatchTick.Advance(off, null, f1);
                    MatchTick.Advance(on, null, f2);
                    rig.Frame(on);
                    if (t % 500 == 499) Assert.AreEqual(StateHash(off), StateHash(on), $"state hash differs at tick {off.Tick}");
                }
                Assert.AreEqual(StateHash(off), StateHash(on));
                Assert.Greater(rig.Feed.Decoded, 1000, "the effects saw the match's events");
                Assert.AreEqual(0, rig.Feed.Lapped);
            }
        }

        [Test]
        public void EffectsDoNotAllocatePerFrameAfterWarmUp()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            InputFrame f = SimInput.EmptyInput();
            using (var rig = new Rig(w, cfg, true))
            {
                for (int t = 0; t < 1500; t++) { MatchTick.Advance(w, null, f); rig.Frame(w); }
                // Unity's allocation constraint (Profiler GC.Alloc recorder on this thread) around each frame of effects work;
                // the sim tick itself runs outside the measured delegate.
                long decoded = rig.Feed.Decoded;
                TestDelegate frame = () => rig.Frame(w);
                for (int t = 0; t < 900; t++)
                {
                    MatchTick.Advance(w, null, f);
                    Assert.That(frame, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory(), $"effects allocated at tick {w.Tick}");
                }
                Assert.Greater(rig.Feed.Decoded - decoded, 100, "events flowed during the measured window");
            }
        }

        [Test]
        public void PoolsAndMeshesStayInsideTheirCapsUnderAFlood()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = NewWorld(cfg);
            using (var rig = new Rig(w, cfg, false))
            {
                PadDef pad = w.Map.Pads[0];
                var kinds = (FxKind[])Enum.GetValues(typeof(FxKind));
                for (int n = 0; n < 4000; n++)
                {
                    var e = new FxEvent
                    {
                        Kind = kinds[n % kinds.Length], Tick = 10, A = n % w.ActorCount, B = (n + 1) % w.ActorCount,
                        X = pad.X + (n % 7) * 0.3, Z = pad.Z + (n % 5) * 0.3, Value = n % 3 == 0 ? 45 : 22, DirX = 1, DirZ = 0,
                        Cls = ClassId.Warrior, Team = TeamId.Azure, Heavy = true,
                    };
                    if (e.Kind == FxKind.KaijuSlam) e.Value = cfg.Kaiju.SlamRadius;
                    rig.Scene.OnFx(ref e, w);
                }
                for (int step = 0; step <= 60; step++)
                {
                    double now = 10 / cfg.SimHz + step * 0.05;
                    rig.Scene.Advance(now);
                    rig.Scene.Build(now, w, Vector3.right, Vector3.up, rig.G, rig.A, rig.S);
                    Assert.LessOrEqual(rig.Scene.ActiveCount(now), FxScene.Capacity);
                    Assert.LessOrEqual(rig.Scene.DecalCount(now), FxScene.MaxDecals);
                    Assert.AreEqual(0, rig.G.Dropped + rig.A.Dropped + rig.S.Dropped, $"mesh budget overflow at step {step}");
                }
                Assert.Greater(rig.Scene.DecalCount(10 / cfg.SimHz + 3.0), 0, "blood landed as decals");
            }
        }

        [Test]
        public void BloodSettingIsHonoured()
        {
            bool had = PlayerPrefs.HasKey(BloodSetting.Key);
            int old = PlayerPrefs.GetInt(BloodSetting.Key, 1);
            try
            {
                PlayerPrefs.DeleteKey(BloodSetting.Key);
                Assert.IsTrue(BloodSetting.Enabled, "blood defaults to on");
                PlayerPrefs.SetInt(BloodSetting.Key, 0);
                Assert.IsFalse(BloodSetting.Enabled);

                GameConfig cfg = GameConfig.CreateDefault();
                World w = NewWorld(cfg);
                PadDef pad = w.Map.Pads[0];
                foreach (bool blood in new[] { false, true })
                {
                    var scene = new FxScene(cfg, w.Map) { Blood = blood };
                    for (int n = 0; n < 6; n++)
                    {
                        var e = new FxEvent { Kind = n == 5 ? FxKind.Death : FxKind.Hit, Tick = 30, A = 1, B = 2, X = pad.X, Z = pad.Z, Value = 22, DirX = 0.6, DirZ = 0.8, Cls = ClassId.Miner };
                        scene.OnFx(ref e, w);
                    }
                    double soon = 30 / cfg.SimHz + 0.05;
                    scene.Advance(30 / cfg.SimHz + 4.0);
                    if (blood)
                    {
                        Assert.Greater(scene.DecalCount(30 / cfg.SimHz + 4.0), 0, "blood on: splats land on the ground");
                    }
                    else
                    {
                        Assert.AreEqual(0, scene.DecalCount(30 / cfg.SimHz + 4.0), "blood off: no blood decals");
                        Assert.Greater(scene.SparkCount(soon), 0, "blood off: neutral sparks instead");
                    }
                }
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(BloodSetting.Key, old);
                else PlayerPrefs.DeleteKey(BloodSetting.Key);
            }
        }

        [Test]
        public void AudioCueTableUsesAll42CreditedSoundsAndTheMixerExposesItsVolumes()
        {
            var files = new System.Collections.Generic.HashSet<string>();
            foreach (var row in CombatAudio.Table) foreach (string f in row.Files) files.Add(f);
            Assert.AreEqual(42, files.Count, "distinct files in the cue table");
            var clips = Resources.LoadAll<AudioClip>(CombatAudio.ClipFolder);
            Assert.AreEqual(42, clips.Length, "clips under Audio/Resources/CMSfx");
            foreach (AudioClip c in clips) Assert.IsTrue(files.Contains(c.name), c.name + " has a cue");
            string credits = System.IO.File.ReadAllText("Assets/_Game/Audio/CREDITS.md");
            foreach (AudioClip c in clips) StringAssert.Contains(c.name + ".ogg", credits, "credited: " + c.name);
            var mixer = Resources.Load<UnityEngine.Audio.AudioMixer>(CombatAudio.MixerResource);
            Assert.IsNotNull(mixer, "CMAudioMixer in Audio/Resources (ProjectSetup.Apply creates it)");
            Assert.AreEqual(1, mixer.FindMatchingGroups("Music").Length);
            Assert.AreEqual(1, mixer.FindMatchingGroups("Sfx").Length);
            string[] exposed = CriticalMassEditorBridge.ExposedNames(mixer);
            CollectionAssert.IsSubsetOf(new[] { "MasterVol", "MusicVol", "SfxVol" }, exposed);
            Assert.AreEqual(1f, CombatAudio.Falloff(3f));
            Assert.AreEqual(0f, CombatAudio.Falloff(CombatAudio.FarDistance + 1f));
        }

        /// <summary>Reads the mixer's exposed parameter names through the editor's internal controller (no public API).</summary>
        private static class CriticalMassEditorBridge
        {
            public static string[] ExposedNames(UnityEngine.Object mixer)
            {
                Type ctl = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.Audio.AudioMixerController", true);
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                var arr = (Array)ctl.GetProperty("exposedParameters", flags).GetValue(mixer);
                var names = new string[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    object e = arr.GetValue(i);
                    names[i] = (string)e.GetType().GetField("name", flags).GetValue(e);
                }
                return names;
            }
        }
    }
}
