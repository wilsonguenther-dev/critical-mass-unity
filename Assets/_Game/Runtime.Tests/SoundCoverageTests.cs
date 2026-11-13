using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Runtime.View.Moments;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Sound coverage: the event table is complete (every sim event type, world-force source, footfall kind and big
    /// moment maps to a cue or an explicit silence, and every cue resolves to a file or a synth recipe), the voice
    /// limiter's priority, caps and cooldowns, the distance maths, ducking, synth determinism, and the feed's decode.
    /// </summary>
    public sealed class SoundCoverageTests
    {
        // ------------------------------------------------------------------ the table

        [Test]
        public void EveryCueResolvesToAFileOrARecipeAndItsReferencesExist()
        {
            var ids = new HashSet<string>();
            var credited = System.IO.File.ReadAllText("Assets/_Game/Audio/credits.json");
            foreach (CueDef c in SoundTable.Cues)
            {
                Assert.IsTrue(ids.Add(c.Id), "duplicate cue " + c.Id);
                Assert.Greater(c.Files.Length + c.Synth.Length, 0, c.Id + " has no sound");
                foreach (string f in c.Files) StringAssert.Contains("\"" + f + ".ogg\"", credited, c.Id + ": " + f + " is a credited CC0 file");
                foreach (string s in c.Synth) Assert.IsNotNull(SynthRecipes.Find(s), c.Id + ": recipe " + s);
                if (c.Layer != null) Assert.GreaterOrEqual(SoundTable.IndexOf(c.Layer), 0, c.Id + " layer " + c.Layer);
                if (c.FarCue != null) Assert.GreaterOrEqual(SoundTable.IndexOf(c.FarCue), 0, c.Id + " far cue " + c.FarCue);
                Assert.Greater(c.Gain, 0f, c.Id);
                Assert.GreaterOrEqual(c.MaxVoices, 1, c.Id);
                if (c.Loop) Assert.AreEqual(1, c.Synth.Length + c.Files.Length, c.Id + ": a loop has one source");
            }
        }

        [Test]
        public void EveryTableUsesAll42CreditedKenneySounds()
        {
            Assert.AreEqual(42, SoundTable.Files().Count);
            Assert.AreEqual(42, new HashSet<string>(Flatten(CombatAudio.Table)).Count, "CombatAudio.Table mirrors the sound table's files");
        }

        private static IEnumerable<string> Flatten((string Id, string[] Files)[] rows)
        {
            foreach (var r in rows) foreach (string f in r.Files) yield return f;
        }

        [Test]
        public void EverySimEventTypeMapsToACueOrAnExplicitSilence()
        {
            foreach (SimEventType t in Enum.GetValues(typeof(SimEventType)))
            {
                int rows = 0;
                foreach (SimRoute r in SoundTable.Sim)
                {
                    if (r.Type != t) continue;
                    rows++;
                    if (r.Pick == Pick.Silence) Assert.IsFalse(string.IsNullOrEmpty(r.Why), t + " is silent without a reason");
                    else
                    {
                        Assert.Greater(r.Cues.Length, 0, t + " has no cues");
                        foreach (string id in r.Cues) Assert.GreaterOrEqual(SoundTable.IndexOf(id), 0, t + ": cue " + id);
                    }
                }
                Assert.AreEqual(1, rows, t + " needs exactly one route");
            }
        }

        [Test]
        public void EveryWorldForceSourceMapsToACueOrAnExplicitSilence()
        {
            foreach (ForceSource s in Enum.GetValues(typeof(ForceSource)))
            {
                int rows = 0;
                foreach (ForceRoute r in SoundTable.Forces)
                {
                    if (r.Source != s) continue;
                    rows++;
                    if (r.Pick == Pick.Silence) Assert.IsFalse(string.IsNullOrEmpty(r.Why), s + " is silent without a reason");
                    else Assert.GreaterOrEqual(SoundTable.IndexOf(r.Cue), 0, s + ": cue " + r.Cue);
                    if (r.Sub != null) Assert.AreEqual(SoundBus.Sub, SoundTable.Cue(SoundTable.IndexOf(r.Sub)).Bus, s + ": sub layer " + r.Sub);
                }
                Assert.AreEqual(1, rows, s + " needs exactly one route");
            }
            // The biggest impacts carry the low-frequency layer that replaces camera shake.
            Assert.GreaterOrEqual(SoundTable.ForceSub(ForceSource.KaijuSlam), 0);
            Assert.GreaterOrEqual(SoundTable.ForceSub(ForceSource.Explosion), 0);
            Assert.GreaterOrEqual(SoundTable.ForceSub(ForceSource.Hatch), 0);
        }

        [Test]
        public void EveryFootfallKindMomentEdgeAndNamedHookResolves()
        {
            foreach (FootfallKind k in Enum.GetValues(typeof(FootfallKind)))
            {
                int rows = 0;
                foreach (var r in SoundTable.Footsteps)
                {
                    if (r.Kind != k) continue;
                    rows++;
                    if (r.Pick == Pick.Silence) Assert.IsFalse(string.IsNullOrEmpty(r.Why), k.ToString());
                    else foreach (string id in r.Cues) Assert.GreaterOrEqual(SoundTable.IndexOf(id), 0, k + ": " + id);
                }
                Assert.AreEqual(1, rows, k + " needs exactly one route");
            }
            foreach (MomentId m in Enum.GetValues(typeof(MomentId)))
            {
                int rows = 0;
                foreach (var r in SoundTable.Moments)
                {
                    if (r.Id != m) continue;
                    rows++;
                    if (r.Cue == null) Assert.IsFalse(string.IsNullOrEmpty(r.Why), m.ToString());
                    else Assert.AreEqual(SoundBus.Stinger, SoundTable.Cue(SoundTable.IndexOf(r.Cue)).Bus, m + ": " + r.Cue);
                }
                Assert.AreEqual(1, rows, m + " needs exactly one route");
            }
            foreach (SoundEdge e in Enum.GetValues(typeof(SoundEdge))) Assert.GreaterOrEqual(SoundTable.EdgeCue(e), 0, e.ToString());
            foreach (var n in SoundTable.Named) Assert.GreaterOrEqual(SoundTable.NamedCue(n.Name), 0, n.Name);
            foreach (string p in CriticalMass.Runtime.Hud.Interact.PingWords.SoundCue) Assert.GreaterOrEqual(SoundTable.NamedCue(p), 0, "ping " + p);
        }

        // ------------------------------------------------------------------ voice limiting, priority, cooldowns

        private static VoicePlanner Planner(int voices, int cues, int max, float cooldown)
        {
            var m = new int[cues];
            var c = new float[cues];
            for (int i = 0; i < cues; i++) { m[i] = max; c[i] = cooldown; }
            return new VoicePlanner(voices, m, c);
        }

        [Test]
        public void HigherPriorityStealsTheWeakestVoiceAndLowerIsRefused()
        {
            var p = Planner(4, 8, 4, 0f);
            p.BeginFrame(1);
            for (int i = 0; i < 4; i++) Assert.GreaterOrEqual(p.Request(i, 10f + i, 0.0, 5.0, false), 0);
            p.BeginFrame(2);
            Assert.AreEqual(-1, p.Request(5, 5f, 0.1, 1.0, false), "quieter than every voice: refused");
            Assert.AreEqual(VoiceRefusal.Outranked, p.LastRefusal);
            int slot = p.Request(6, 50f, 0.1, 1.0, false);
            Assert.GreaterOrEqual(slot, 0, "a louder sound steals");
            Assert.AreEqual(6, p.CueOf(slot));
            Assert.AreEqual(4, p.BusyCount(0.2));
            for (int i = 0; i < 4; i++) Assert.AreNotEqual(10f, p.PriorityOf(i), "the weakest (priority 10) was the one stolen");
        }

        [Test]
        public void OwnAndThreatsOutrankTheCrowd()
        {
            float crowd = SoundSpace.Priority(45, 1f, false, false);
            float own = SoundSpace.Priority(10, 0.2f, true, false);
            float threat = SoundSpace.Priority(30, 0.3f, false, true);
            Assert.Greater(own, crowd, "your own footstep beats a full-volume heavy hit in the crowd");
            Assert.Greater(threat, crowd, "a wind-up at you beats the crowd");
            Assert.Greater(SoundSpace.Priority(30, 1f, false, false), SoundSpace.Priority(30, 0.1f, false, false), "nearer wins");

            // 32 fighters swinging at once: the player's swing still gets a voice.
            var p = VoicePlanner.FromTable(SoundDirector.Voices);
            int swing = SoundTable.IndexOf("swing_mid"), hit = SoundTable.IndexOf("hit_flesh_heavy");
            int frame = 0;
            for (int i = 0; i < 64; i++)
            {
                p.BeginFrame(frame++);
                p.Request(i % 2 == 0 ? hit : SoundTable.IndexOf("hit_armor"), SoundSpace.Priority(45, 0.9f, false, false), i * 0.001, 1.0, false);
            }
            Assert.LessOrEqual(p.Playing(hit, 0.1), SoundTable.Cues[hit].MaxVoices, "per-cue cap holds under a flood");
            p.BeginFrame(frame++);
            Assert.GreaterOrEqual(p.Request(swing, SoundSpace.Priority(22, 1f, true, false), 0.1, 0.3, true), 0, "own swing plays");
        }

        [Test]
        public void PerCueCapRefusesTheCrowdButTheOwnSoundReplacesItsOldestCopy()
        {
            var p = Planner(8, 2, 2, 0f);
            p.BeginFrame(1);
            int a = p.Request(0, 10f, 0.0, 5.0, false);
            int b = p.Request(0, 10f, 0.01, 5.0, false);
            Assert.AreEqual(-1, p.Request(0, 90f, 0.02, 5.0, false));
            Assert.AreEqual(VoiceRefusal.CueCap, p.LastRefusal);
            int own = p.Request(0, 90f, 0.1, 5.0, true);
            Assert.AreEqual(a, own, "the oldest copy makes room");
            Assert.AreEqual(2, p.Playing(0, 0.2));
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void CooldownsHoldForTheCrowdAndOnlyTheRepeatGuardForOwnActions()
        {
            var p = Planner(8, 1, 8, 0.2f);
            p.BeginFrame(1);
            Assert.GreaterOrEqual(p.Request(0, 10f, 1.0, 0.1, false), 0);
            Assert.AreEqual(-1, p.Request(0, 10f, 1.1, 0.1, false));
            Assert.AreEqual(VoiceRefusal.Cooldown, p.LastRefusal);
            Assert.GreaterOrEqual(p.Request(0, 10f, 1.21, 0.1, false), 0, "after the cooldown");
            Assert.AreEqual(-1, p.Request(0, 10f, 1.22, 0.1, true), "own: the 30 ms repeat guard");
            Assert.GreaterOrEqual(p.Request(0, 10f, 1.25, 0.1, true), 0, "own: no cooldown");
        }

        [Test]
        public void NewVoicesPerFrameAreCappedForTheCrowd()
        {
            var p = Planner(32, 32, 4, 0f);
            p.BeginFrame(7);
            for (int i = 0; i < VoicePlanner.StartsPerFrame; i++) Assert.GreaterOrEqual(p.Request(i, 10f, 0.0, 1.0, false), 0);
            Assert.AreEqual(-1, p.Request(20, 10f, 0.0, 1.0, false));
            Assert.AreEqual(VoiceRefusal.FrameCap, p.LastRefusal);
            Assert.GreaterOrEqual(p.Request(21, 10f, 0.0, 1.0, true), 0, "own sound is never frame-capped");
            p.BeginFrame(8);
            Assert.GreaterOrEqual(p.Request(22, 10f, 0.0, 1.0, false), 0, "next frame");
            p.Release(0);
            Assert.IsFalse(p.Busy(0, 0.0));
        }

        // ------------------------------------------------------------------ distance, pan, filtering, ducking

        [Test]
        public void AttenuationIsFullNearSilentFarAndMonotonic()
        {
            Assert.AreEqual(1f, SoundSpace.Gain(3f, 9f, 40f));
            Assert.AreEqual(0f, SoundSpace.Gain(40f, 9f, 40f));
            Assert.AreEqual(0f, SoundSpace.Gain(100f, 9f, 40f));
            float last = 1f;
            for (float d = 9f; d <= 40f; d += 0.5f)
            {
                float g = SoundSpace.Gain(d, 9f, 40f);
                Assert.LessOrEqual(g, last + 1e-6f, "falloff never rises");
                last = g;
            }
            Assert.AreEqual(0.25f, SoundSpace.Gain(24.5f, 9f, 40f), 1e-4f, "half way is a quarter (square falloff)");
            Assert.AreEqual(1f, CombatAudio.Falloff(3f));
            Assert.AreEqual(0f, CombatAudio.Falloff(CombatAudio.FarDistance + 1f));
            SoundSpace.Range(SoundRange.Huge, out float hn, out float hf);
            SoundSpace.Range(SoundRange.Near, out float nn, out float nf);
            Assert.Greater(hf, nf, "blasts carry farther than footsteps");
            Assert.Greater(hn, nn);
        }

        [Test]
        public void PanFollowsTheCameraRightAndStaysCentredAtTheFocus()
        {
            Assert.Greater(SoundSpace.Pan(10f, 0f, 1f, 0f), 0.5f, "to the camera's right");
            Assert.Less(SoundSpace.Pan(-10f, 0f, 1f, 0f), -0.5f, "to the left");
            Assert.AreEqual(0f, SoundSpace.Pan(0f, 10f, 1f, 0f), 1e-5f, "straight ahead");
            Assert.AreEqual(0f, SoundSpace.Pan(0.2f, 0f, 1f, 0f), "on the focus");
            Assert.LessOrEqual(Math.Abs(SoundSpace.Pan(1000f, 0f, 1f, 0f)), 1f);
            Assert.Greater(SoundSpace.Pan(0f, -10f, 0f, -1f), 0.5f, "a rotated camera rotates the pan");
        }

        [Test]
        public void FarSoundsLoseHighsAndBlastsArriveLate()
        {
            Assert.AreEqual(SoundSpace.OpenCutoff, SoundSpace.LowPass(5f, 22f, 140f));
            Assert.AreEqual(SoundSpace.FarCutoff, SoundSpace.LowPass(140f, 22f, 140f), 1f);
            Assert.Less(SoundSpace.LowPass(80f, 22f, 140f), SoundSpace.LowPass(40f, 22f, 140f));
            Assert.AreEqual(0f, SoundSpace.Delay(10f));
            Assert.Greater(SoundSpace.Delay(60f), SoundSpace.Delay(30f));
            Assert.AreEqual(SoundSpace.MaxDelay, SoundSpace.Delay(10000f));
        }

        [Test]
        public void DuckingDipsEffectsUnderTheAnnouncerAndMoreUnderBigMoments()
        {
            Assert.AreEqual(1f, DuckMix.Target(SoundBus.Sfx, false, false, false));
            Assert.AreEqual(DuckMix.AnnouncerSfx, DuckMix.Target(SoundBus.Sfx, false, true, false));
            Assert.AreEqual(DuckMix.MomentSfx, DuckMix.Target(SoundBus.Sfx, false, true, true));
            Assert.AreEqual(DuckMix.OwnFloor, DuckMix.Target(SoundBus.Sfx, true, true, true), "own sounds dip least");
            Assert.AreEqual(1f, DuckMix.Target(SoundBus.Stinger, false, true, true), "stingers never duck");
            Assert.Less(DuckMix.Target(SoundBus.Ambience, false, false, true), DuckMix.Target(SoundBus.Sfx, false, false, true));
            float g = 1f;
            for (int i = 0; i < 60; i++) g = DuckMix.Step(g, 0.5f, 1f / 60f);
            Assert.AreEqual(0.5f, g, 0.01f, "settles within a second");
            Assert.AreEqual(1f, SoundSpace.Crowd(4, false));
            Assert.Less(SoundSpace.Crowd(32, false), 0.6f, "a crowd of voices is scaled down");
            Assert.AreEqual(1f, SoundSpace.Crowd(32, true), "your own sound is not");
        }

        // ------------------------------------------------------------------ synth

        [Test]
        public void SynthIsDeterministicFiniteAndNormalised()
        {
            foreach (SynthRecipe r in SynthRecipes.All)
            {
                float[] a = SoundSynth.Render(r, 0), b = SoundSynth.Render(r, 0);
                Assert.AreEqual(a.Length, b.Length, r.Id);
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) Assert.Fail(r.Id + " differs at sample " + i);
                Assert.AreEqual(Math.Round(r.Length * SoundSynth.SampleRate), a.Length, 2, r.Id + " length");
                float peak = 0f;
                for (int i = 0; i < a.Length; i++)
                {
                    Assert.IsFalse(float.IsNaN(a[i]) || float.IsInfinity(a[i]), r.Id + " sample " + i);
                    peak = Math.Max(peak, Math.Abs(a[i]));
                }
                Assert.LessOrEqual(peak, SoundSynth.PeakTarget + 1e-4f, r.Id + " peak");
                Assert.Greater(peak, 0.1f, r.Id + " is audible");
            }
        }

        [Test]
        public void SynthVariantsDifferAndRecipeIdsAreUnique()
        {
            var ids = new HashSet<string>();
            foreach (SynthRecipe r in SynthRecipes.All) Assert.IsTrue(ids.Add(r.Id), "duplicate recipe " + r.Id);
            SynthRecipe hit = SynthRecipes.Find("hit_stone");
            float[] v0 = SoundSynth.Render(hit, 0), v1 = SoundSynth.Render(hit, 1);
            bool differs = v0.Length != v1.Length;
            for (int i = 0; !differs && i < v0.Length; i++) differs = v0[i] != v1[i];
            Assert.IsTrue(differs, "variant 1 is not variant 0");
            Assert.AreEqual(SoundSynth.Fnv("kaiju_roar"), SoundSynth.Fnv("kaiju_roar"));
            Assert.AreNotEqual(SoundSynth.Fnv("kaiju_roar"), SoundSynth.Fnv("kaiju_rear"));
        }

        // ------------------------------------------------------------------ the feed

        private sealed class Recorder : ISoundSink
        {
            public readonly List<SoundRequest> Plays = new List<SoundRequest>();
            public int Fuses, BerserkOn;
            public void Play(in SoundRequest r) => Plays.Add(r);
            public void Fuse(int slot, double x, double z, int endTick, int cue) => Fuses++;
            public void Berserk(bool on, int cue) { if (on) BerserkOn++; }
            public bool Has(string cue) => Plays.Exists(p => p.Cue == SoundTable.IndexOf(cue));
            public SoundRequest Get(string cue) => Plays.Find(p => p.Cue == SoundTable.IndexOf(cue));
        }

        private sealed class Counter : ISoundSink
        {
            public int N;
            public void Play(in SoundRequest r) => N++;
            public void Fuse(int slot, double x, double z, int endTick, int cue) => N++;
            public void Berserk(bool on, int cue) => N++;
        }

        private static World NewWorld() => World.CreateWorld(new WorldOptions
        {
            Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
        });

        private static int FirstOf(World w, Func<Actor, bool> pred)
        {
            for (int i = 1; i < w.ActorCount; i++) if (pred(w.Actors[i])) return i;
            return -1;
        }

        [Test]
        public void FeedPicksHitsByStrengthAndMaterialAndFlagsOwnAndThreats()
        {
            World w = NewWorld();
            var feed = new SoundFeed(World.ActorCapacity(w.Config));
            var rec = new Recorder();
            feed.OnTick(w, rec);
            int farmer = FirstOf(w, a => a.Cls != ClassId.Warrior && a.Team == TeamId.Crimson);
            int enemy = FirstOf(w, a => a.Team == TeamId.Crimson);
            Assert.GreaterOrEqual(farmer, 0);
            World.PushEvent(w, SimEventType.Hit, farmer, 0, w.X[farmer], w.Z[farmer], 25, null, DamageCause.Melee);
            World.PushEvent(w, SimEventType.Hit, farmer, enemy, w.X[farmer], w.Z[farmer], 8, null, DamageCause.Melee);
            World.PushEvent(w, SimEventType.KaijuMeleeWindup, 0, -1, w.X[0], w.Z[0], 10);
            World.PushEvent(w, SimEventType.Explosion, enemy, Demolitions.ExplosionTnt, w.X[0] + 1, w.Z[0], 2, null, DamageCause.Explosion, 2.5);
            World.PushEvent(w, SimEventType.HillOwnerChange, 1, 0, 50, 37, 0);
            World.PushEvent(w, SimEventType.MatchEnd, -1, -1, 0, 0, 0);
            feed.OnTick(w, rec);
            SoundRequest heavy = rec.Get("hit_flesh_heavy");
            Assert.IsTrue(rec.Has("hit_flesh_heavy"), "25 damage on a non-warrior is a heavy flesh hit");
            Assert.IsTrue(heavy.Own, "the player dealt it");
            Assert.IsTrue(rec.Has("hit_flesh_light"), "8 damage is light");
            Assert.IsTrue(rec.Has("grunt"), "a heavy hit knocks a grunt out of the victim");
            Assert.IsTrue(rec.Get("kaiju_windup").Threat, "a wind-up at the player is a threat");
            Assert.IsTrue(rec.Get("blast_near").Threat, "a blast beside the player is a threat");
            Assert.IsTrue(rec.Has("hill_captured"), "azure took the hill for the azure player");
            Assert.GreaterOrEqual(feed.Silenced, 1, "match end is silent (the music sting covers it)");
        }

        [Test]
        public void FeedRoutesEverySimEventTypeWithoutThrowing()
        {
            World w = NewWorld();
            var feed = new SoundFeed(World.ActorCapacity(w.Config));
            var sink = new Counter();
            feed.OnTick(w, sink);
            int n = 0;
            foreach (SimEventType t in Enum.GetValues(typeof(SimEventType)))
            {
                World.PushEvent(w, t, 1, 0, w.X[0], w.Z[0], 1, null, DamageCause.Melee, 2);
                n++;
            }
            feed.OnTick(w, sink);
            Assert.AreEqual((long)n, feed.Decoded);
            Assert.Greater(sink.N, 20, "most event types make a sound");
        }

        [Test]
        public void FeedAndPlannerDoNotAllocatePerEvent()
        {
            World w = NewWorld();
            var feed = new SoundFeed(World.ActorCapacity(w.Config));
            var sink = new Counter();
            var p = VoicePlanner.FromTable(SoundDirector.Voices);
            feed.OnTick(w, sink);
            for (int k = 0; k < 2; k++)
            {
                for (int i = 0; i < 100; i++) World.PushEvent(w, i % 2 == 0 ? SimEventType.AttackStart : SimEventType.Hit, 1 + i % 8, 0, w.X[0], w.Z[0], 12, null, DamageCause.Melee);
                long before = GC.GetAllocatedBytesForCurrentThread();
                feed.OnTick(w, sink);
                for (int i = 0; i < 200; i++)
                {
                    p.BeginFrame(i);
                    p.Request(i % SoundTable.Cues.Length, 30f, i * 0.01, 0.5, i % 5 == 0);
                }
                long used = GC.GetAllocatedBytesForCurrentThread() - before;
                if (k == 1) Assert.AreEqual(0L, used, "bytes allocated decoding 100 events and planning 200 voices");
            }
        }

        [Test]
        public void FootstepSurfaceFollowsTheCellUnderTheFoot()
        {
            World w = NewWorld();
            var map = w.Map;
            int bridge = Array.IndexOf(map.Cells, Cell.Bridge);
            if (bridge < 0) Assert.Ignore("this map has no bridge cell");
            double x = bridge % map.Width + 0.5, z = bridge / map.Width + 0.5;
            Assert.AreEqual(1, SoundDirector.StepIndex(w, 1, x, z), "bridges are wood");
            Assert.AreEqual("step_wood", SoundTable.Cue(SoundTable.StepCues[1]).Id);
        }
    }
}
