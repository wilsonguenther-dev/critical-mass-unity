using System;
using System.Collections.Generic;
using System.Reflection;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests.PlayerExperience
{
    /// <summary>
    /// px lane F (2026-11-22): the local player's own sound tier, from the sim event to the sound request (SoundFeed with a
    /// recording sink: no AudioSource, nothing played) and the voice allocator (VoicePlanner, pure). The owner: "I can be in
    /// a player's face, swing, and I see nothing to indicate I've hit."
    /// </summary>
    public sealed class PxAudioTests
    {
        sealed class Rec : ISoundSink
        {
            public readonly List<SoundRequest> Plays = new List<SoundRequest>();
            public void Play(in SoundRequest r) => Plays.Add(r);
            public void Fuse(int slot, double x, double z, int endTick, int cue) { }
            public void Berserk(bool on, int cue) { }
            public int Count(string id) => Plays.FindAll(p => p.Cue == SoundTable.IndexOf(id)).Count;
            public bool Has(string id) => Count(id) > 0;
            public SoundRequest Get(string id) => Plays.Find(p => p.Cue == SoundTable.IndexOf(id));
        }

        static World NewWorld(ClassId mine = ClassId.Warrior) => World.CreateWorld(new WorldOptions
        {
            Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = mine, Config = GameConfig.CreateDefault(),
        });

        static int Enemy(World w, int k = 0)
        {
            TeamId mine = w.Actors[LocalSeat.Actor].Team;
            for (int i = 0; i < w.ActorCount; i++)
                if (w.Actors[i].Team != mine && k-- == 0) return i;
            Assert.Fail("no enemy " + k);
            return -1;
        }

        static (SoundFeed, Rec) Feed(World w)
        {
            var feed = new SoundFeed(World.ActorCapacity(w.Config));
            var rec = new Rec();
            feed.OnTick(w, rec);   // consume the start-of-match events
            rec.Plays.Clear();
            return (feed, rec);
        }

        static void Step(World w, SoundFeed f, Rec r)
        {
            w.Tick++;
            f.OnTick(w, r);
        }

        static readonly string[] Impacts = { "own_impact_thud", "own_impact_crack", "own_impact_clank", "own_impact_bell" };

        [Test]
        public void EveryHitByMeAlwaysPlaysAMeatyWeaponImpactAndTheConfirmTick()
        {
            foreach (ClassId cls in ClassIds.All)
            {
                World w = NewWorld(cls);
                (SoundFeed feed, Rec rec) = Feed(w);
                int v = Enemy(w);
                w.Actors[v].AbilityActive = 0;
                string impact = Impacts[SoundFeed.WeaponImpact(cls)];
                for (int i = 0; i < 20; i++)
                {
                    World.PushEvent(w, SimEventType.Hit, v, LocalSeat.Actor, w.X[v], w.Z[v], 10, null, DamageCause.Melee);
                    Step(w, feed, rec);
                }
                Assert.AreEqual(20, rec.Count(impact), cls + ": every hit plays " + impact + " (never half)");
                Assert.AreEqual(20, rec.Count("hit_confirm"), cls + ": every hit plays the confirm tick");
                SoundRequest r = rec.Get(impact);
                Assert.IsTrue(r.Own, cls + ": my impact is at own priority");
                Assert.AreEqual((int)FeedbackChannel.ImpactSound + 1, r.Feedback, cls + ": it reports ImpactSound when it starts");
                Assert.GreaterOrEqual(SoundTable.Cue(r.Cue).Gain * r.Gain, 0.7f, cls + ": a meaty layer (gain >= 0.7)");
                Assert.IsFalse(rec.Has("whiff_swish"), cls + ": a hit never plays the whiff");
            }
            // the confirm tick is the same for every class and short and bright
            CueDef c = SoundTable.Cue(SoundTable.IndexOf("hit_confirm"));
            SynthRecipe tick = SynthRecipes.Find(c.Synth[0]);
            Assert.That(tick.Length, Is.InRange(0.06f, 0.09f), "60-90 ms");
            foreach (SynthLayer l in tick.Layers) if (l.Wave == Wave.Sine) Assert.That(l.F0, Is.InRange(2000f, 4000f), "2-4 kHz");
        }

        [Test]
        public void AWhiffIsALightSwishOnlyAndAnAnsweredSwingIsNotAWhiff()
        {
            World w = NewWorld();
            (SoundFeed feed, Rec rec) = Feed(w);
            int me = LocalSeat.Actor;
            World.PushEvent(w, SimEventType.AttackContact, me, -1, w.X[me], w.Z[me], 0);
            Step(w, feed, rec);
            Assert.AreEqual(1, rec.Count("whiff_swish"), "a contact with nobody in it: the swish");
            Assert.AreEqual(1, rec.Plays.Count, "and only the swish (no slice, no impact, no confirm)");
            Assert.AreEqual((int)FeedbackChannel.WhiffCue + 1, rec.Get("whiff_swish").Feedback);

            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.AttackContact, me, -1, w.X[me], w.Z[me], 1);
            Step(w, feed, rec);
            Assert.IsFalse(rec.Has("whiff_swish"), "a contact with a hit is the impact's, not a whiff");

            rec.Plays.Clear();
            int v = Enemy(w);
            World.PushEvent(w, SimEventType.HitBlocked, v, me, w.X[v], w.Z[v], PlayerReasons.BlockedDodge);
            World.PushEvent(w, SimEventType.AttackContact, me, -1, w.X[me], w.Z[me], 0);
            Step(w, feed, rec);
            Assert.IsTrue(rec.Has("dodge_phew"), "a dodged swing says phew");
            Assert.IsFalse(rec.Has("whiff_swish"), "and is not also a whiff");

            // the Warrior's cleave is no longer a knife slice; the knife slices moved to the feint
            CueDef cleave = SoundTable.Cue(SoundTable.IndexOf("swing_warrior"));
            CollectionAssert.DoesNotContain(cleave.Files, "knifeSlice");
            SimRoute feint = Array.Find(SoundTable.Sim, r => r.Type == SimEventType.Feint);
            CollectionAssert.AreEqual(new[] { "feint" }, feint.Cues, "the feint has its own cue (it played the light swing)");
        }

        [Test]
        public void AHitOnMePlaysMyOwnHurtThumpScaledByTheDamageShare()
        {
            World w = NewWorld(ClassId.Farmer);
            (SoundFeed feed, Rec rec) = Feed(w);
            int me = LocalSeat.Actor, foe = Enemy(w);
            double max = w.Actors[me].MaxHp;
            World.PushEvent(w, SimEventType.Hit, me, foe, w.X[me], w.Z[me], max * 0.05, null, DamageCause.Melee);
            Step(w, feed, rec);
            SoundRequest small = rec.Get("own_hurt");
            Assert.IsTrue(rec.Has("own_hurt"), "a hit on me plays my hurt thump");
            Assert.IsTrue(small.Own && small.Threat, "own priority, and a threat");
            Assert.AreEqual((int)FeedbackChannel.OwnHurtSound + 1, small.Feedback);
            Assert.IsTrue(rec.Has("own_pain"), "with the pain voice (its own cue for my hits, so a crowd pain never swallows it)");
            Assert.IsFalse(rec.Has("pain"));
            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.Hit, me, foe, w.X[me], w.Z[me], max * 0.3, null, DamageCause.Melee);
            Step(w, feed, rec);
            Assert.Greater(rec.Get("own_hurt").Gain, small.Gain, "a big blow is louder than a scratch");
            Assert.AreEqual(0.55f, SoundFeed.HurtGain(0), 1e-5f);
            Assert.AreEqual(1f, SoundFeed.HurtGain(0.25), 1e-5f);
            Assert.AreEqual(1f, SoundFeed.HurtGain(2), 1e-5f);
            // someone else's hit nearby is the crowd's cue, never my thump
            rec.Plays.Clear();
            int ally = -1;
            for (int i = 1; i < w.ActorCount && ally < 0; i++) if (w.Actors[i].Team == w.Actors[me].Team) ally = i;
            World.PushEvent(w, SimEventType.Hit, ally, foe, w.X[ally], w.Z[ally], 10, null, DamageCause.Melee);
            Step(w, feed, rec);
            Assert.IsFalse(rec.Has("own_hurt"), "a nearby hit is not mine");
            Assert.IsFalse(rec.Has("hit_confirm"));
        }

        [Test]
        public void EveryKillCreditedToMePlaysTheStingAndAnAssistPlaysTheSofterOne()
        {
            World w = NewWorld();
            (SoundFeed feed, Rec rec) = Feed(w);
            int me = LocalSeat.Actor, a = Enemy(w, 0), b = Enemy(w, 1), c = Enemy(w, 2);
            World.PushEvent(w, SimEventType.Death, a, me, w.X[a], w.Z[a], 30, null, DamageCause.Melee);
            Step(w, feed, rec);
            Assert.IsTrue(rec.Has("kill_sting"), "a single kill has its sting (the announcer only calls 2+)");
            Assert.IsTrue(rec.Has("kill_confirm"));
            Assert.AreEqual(SoundBus.Stinger, SoundTable.Cue(SoundTable.IndexOf("kill_sting")).Bus, "never queued behind the announcer");
            Assert.AreEqual((int)FeedbackChannel.KillSound + 1, rec.Get("kill_sting").Feedback);
            Assert.Less(SynthRecipes.Find("px_kill_sting").Length, 0.5f, "under half a second");

            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.Hit, b, me, w.X[b], w.Z[b], 10, null, DamageCause.Melee);
            Step(w, feed, rec);
            World.PushEvent(w, SimEventType.Death, b, 1, w.X[b], w.Z[b], 30, null, DamageCause.Melee);
            Step(w, feed, rec);
            Assert.IsTrue(rec.Has("assist_sting"), "a teammate's kill on an enemy I hit is my assist");
            Assert.IsFalse(rec.Has("kill_sting"));

            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.Death, c, 1, w.X[c], w.Z[c], 30, null, DamageCause.Melee);
            Step(w, feed, rec);
            Assert.IsFalse(rec.Has("assist_sting"), "no assist on an enemy I never touched");

            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.Death, me, a, w.X[me], w.Z[me], 30, null, DamageCause.Melee);
            World.PushEvent(w, SimEventType.Respawn, me, -1, w.X[me], w.Z[me], 0);
            Step(w, feed, rec);
            Assert.IsTrue(rec.Has("my_death"), "my death has its own cue");
            Assert.IsTrue(rec.Has("my_respawn"), "my respawn is a real event, not the UI blip");
            Assert.IsFalse(rec.Has("respawn"));
        }

        [Test]
        public void EveryPlayerReasonsCodeHasACueOrAStatedSilence()
        {
            // the stated silences (another event already sounds them)
            var silent = new Dictionary<int, string>
            {
                { PlayerReasons.BlockedWard, "WardAbsorb plays block_heavy" },
            };
            int me = LocalSeat.Actor;
            foreach (FieldInfo f in typeof(PlayerReasons).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                int code = (int)f.GetRawConstantValue();
                World w = NewWorld();
                (SoundFeed feed, Rec rec) = Feed(w);
                int v = Enemy(w);
                if (f.Name.StartsWith("Blocked", StringComparison.Ordinal))
                {
                    World.PushEvent(w, SimEventType.HitBlocked, v, me, w.X[v], w.Z[v], code);
                    Step(w, feed, rec);
                    if (silent.ContainsKey(code)) Assert.AreEqual(0, rec.Plays.Count, f.Name + ": " + silent[code]);
                    else Assert.AreEqual(1, rec.Plays.Count, f.Name + " answers with one cue");
                }
                else if (f.Name.StartsWith("Structure", StringComparison.Ordinal))
                {
                    World.PushEvent(w, SimEventType.StructureHit, me, code, w.X[me], w.Z[me], 5);
                    Step(w, feed, rec);
                    Assert.AreEqual(1, rec.Plays.Count, f.Name + " answers with one cue");
                    if (code == PlayerReasons.StructureSolid) Assert.IsTrue(rec.Has("structure_solid"), "an unbreakable wall says clonk");
                }
                else if (f.Name.StartsWith("Refused", StringComparison.Ordinal))
                {
                    World.PushEvent(w, SimEventType.ActionRefused, me, PlayerReasons.ButtonDigPlace, w.X[me], w.Z[me], code);
                    World.PushEvent(w, SimEventType.ActionRefused, v, PlayerReasons.ButtonDigPlace, w.X[v], w.Z[v], code);
                    Step(w, feed, rec);
                    Assert.AreEqual(1, rec.Count("denied"), f.Name + ": my dead press says one soft denied tick; a bot's is silent");
                    Assert.AreEqual(1, rec.Plays.Count);
                }
                else if (f.Name.StartsWith("World", StringComparison.Ordinal))
                {
                    World.PushEvent(w, SimEventType.WorldAction, me, code, w.X[me], w.Z[me], 0);
                    World.PushEvent(w, SimEventType.WorldAction, me, code, w.X[me], w.Z[me], 1);
                    Step(w, feed, rec);
                    Assert.AreEqual(2, rec.Plays.Count, f.Name + ": the channel start and the verb both sound");
                }
            }
            Assert.AreEqual(2, SoundFeed.WorldActCue(10, true), "WorldGather (10, from feat/px-s2-class) starts with the gather rustle");
            foreach (SimEventType t in new[] { SimEventType.HitBlocked, SimEventType.StructureHit, SimEventType.ActionRefused, SimEventType.WorldAction,
                         SimEventType.Feint, SimEventType.CorruptionRoll, SimEventType.KaijuPower, SimEventType.CrystalMined,
                         SimEventType.AutopilotChanged, SimEventType.CriticalHit })
                Assert.AreNotEqual(Pick.Silence, Array.Find(SoundTable.Sim, r => r.Type == t).Pick, t + " is routed");
        }

        [Test]
        public void MyDodgeAndMyShieldSoundOwnAndTheGlancingHitTapsOncePerTargetPerTwoSeconds()
        {
            World w = NewWorld();
            (SoundFeed feed, Rec rec) = Feed(w);
            int me = LocalSeat.Actor, foe = Enemy(w), other = Enemy(w, 1), ally = -1;
            for (int i = 1; i < w.ActorCount && ally < 0; i++) if (w.Actors[i].Team == w.Actors[me].Team) ally = i;

            World.PushEvent(w, SimEventType.HitBlocked, me, foe, w.X[me], w.Z[me], PlayerReasons.BlockedDodge);
            World.PushEvent(w, SimEventType.AttackContact, foe, -1, w.X[foe], w.Z[foe], 0);
            Step(w, feed, rec);
            SoundRequest d = rec.Get("my_dodge");
            Assert.IsTrue(rec.Has("my_dodge") && d.Own, "MY successful dodge: its own cue at own priority");
            Assert.IsFalse(rec.Has("dodge_phew"), "distinct from the crowd's phew");
            Assert.IsNotNull(SoundTable.Cue(SoundTable.IndexOf("my_dodge")).Caption);
            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.HitBlocked, foe, me, w.X[foe], w.Z[foe], PlayerReasons.BlockedDodge);
            Step(w, feed, rec);
            Assert.IsTrue(rec.Has("dodge_phew") && !rec.Has("my_dodge"), "they dodged MY swing: the phew");

            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.HitBlocked, me, foe, w.X[me], w.Z[me], PlayerReasons.BlockedShield);
            Step(w, feed, rec);
            Assert.IsTrue(rec.Has("my_shield") && rec.Get("my_shield").Own, "my raised shield took it: the shield block, own");
            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.HitBlocked, ally, foe, w.X[ally], w.Z[ally], PlayerReasons.BlockedShield);
            Step(w, feed, rec);
            Assert.AreEqual(0, rec.Plays.Count, "someone else's shield: the Hit plays its block");

            int hz = (int)w.Config.SimHz, played = 0;
            for (int k = 0; k < hz; k++)   // a glancing hit on the same target every tick for one second
            {
                rec.Plays.Clear();
                World.PushEvent(w, SimEventType.HitBlocked, foe, me, w.X[foe], w.Z[foe], PlayerReasons.BlockedGlancing);
                Step(w, feed, rec);
                played += rec.Count("glance_tap");
            }
            Assert.AreEqual(1, played, "one tap per target per 2 s");
            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.HitBlocked, other, me, w.X[other], w.Z[other], PlayerReasons.BlockedGlancing);
            Step(w, feed, rec);
            Assert.AreEqual(1, rec.Count("glance_tap"), "another target has its own clock");
            for (int k = 0; k < 2 * hz; k++) Step(w, feed, rec);
            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.HitBlocked, foe, me, w.X[foe], w.Z[foe], PlayerReasons.BlockedGlancing);
            Step(w, feed, rec);
            Assert.AreEqual(1, rec.Count("glance_tap"), "after 2 s it taps again");
            Assert.Less(SynthRecipes.Find("px_glance").Length, 0.12f, "soft and short");
            Assert.Less(SoundTable.Cue(SoundTable.IndexOf("glance_tap")).Gain, 0.4f);
        }

        [Test]
        public void MySwingOnAStructureOrRockPlaysItsKnockAtOwnPriorityAndReportsImpactSound()
        {
            string[] byKind = { null, "barricade_hit", "sb_wall_hit", "wall_crack", "hit_wood", "structure_solid" };
            int me = LocalSeat.Actor;
            for (int kind = PlayerReasons.StructureBarricade; kind <= PlayerReasons.StructureSolid; kind++)
            {
                World w = NewWorld();
                (SoundFeed feed, Rec rec) = Feed(w);
                World.PushEvent(w, SimEventType.StructureHit, me, kind, w.X[me] + 1, w.Z[me], kind == PlayerReasons.StructureSolid ? 0 : 6);
                World.PushEvent(w, SimEventType.AttackContact, me, -1, w.X[me], w.Z[me], 0);
                Step(w, feed, rec);
                Assert.AreEqual(1, rec.Plays.Count, "kind " + kind + ": one knock, and the contact is not a whiff");
                SoundRequest r = rec.Get(byKind[kind]);
                Assert.IsTrue(rec.Has(byKind[kind]), "kind " + kind + " plays " + byKind[kind]);
                Assert.IsTrue(r.Own, "kind " + kind + ": own priority");
                Assert.AreEqual((int)FeedbackChannel.ImpactSound + 1, r.Feedback, "kind " + kind + ": ImpactSound on the ledger, like a body hit");
                Assert.AreEqual(me, r.FeedbackActor);
                Assert.AreEqual(kind, r.Reason);
                Assert.GreaterOrEqual(r.Gain, 1f);

                rec.Plays.Clear();
                int bot = Enemy(w);
                World.PushEvent(w, SimEventType.StructureHit, bot, kind, w.X[bot], w.Z[bot], 6);
                Step(w, feed, rec);
                foreach (SoundRequest p in rec.Plays) Assert.AreEqual(0, p.Feedback, "a bot's structure hit is not my impact");
                if (kind == PlayerReasons.StructureSolid) Assert.AreEqual(0, rec.Plays.Count, "a bot never knocks on rock");
            }
        }

        [Test]
        public void TheDirectorsSkipAFrameQuietlyWhenTheirBuildDidNotFinish()
        {
            // round 4 (pass 4 capture): ~735,000 NullReferenceExceptions from MusicDirector.UpdateCost and
            // SoundDirector.LateUpdateBody after their Awake stopped early. In edit mode Awake does not run, which is exactly
            // that state: the per-frame bodies must return without a throw.
            var go = new GameObject("Px Directors Test");
            try
            {
                var music = go.AddComponent<CriticalMass.Runtime.Audio.MusicDirector>();
                Assert.IsFalse(music.ReadyForTests);
                music.HoldRebuildForTests();
                MethodInfo mu = typeof(CriticalMass.Runtime.Audio.MusicDirector).GetMethod("UpdateCost", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(mu);
                Assert.DoesNotThrow(() => { for (int i = 0; i < 3; i++) mu.Invoke(music, null); }, "music: no per-frame throw");

                var sound = go.AddComponent<SoundDirector>();
                Assert.IsFalse(sound.Ready);
                sound.HoldInitForTests();
                MethodInfo su = typeof(SoundDirector).GetMethod("LateUpdateBody", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(su);
                Assert.DoesNotThrow(() => { for (int i = 0; i < 3; i++) su.Invoke(sound, null); }, "sound: no per-frame throw");
                Assert.IsFalse(sound.UpdateGuard(), "held: still not ready, and no throw");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ExactlyOneAudioListenerIsKeptAndItIsTheGamesOwn()
        {
            var cam = new GameObject("Main Camera");
            var ear = new GameObject("CM Listener");
            try
            {
                AudioListener a = cam.AddComponent<AudioListener>(), b = ear.AddComponent<AudioListener>();
                int removed = CriticalMass.Runtime.Audio.SingleListener.Enforce(new[] { a, b });
                Assert.AreEqual(1, removed);
                Assert.IsTrue(ear.GetComponent<AudioListener>() != null, "the persistent CM Listener stays");
                Assert.IsTrue(cam.GetComponent<AudioListener>() == null, "the scene camera's is removed");
                Assert.AreEqual(0, CriticalMass.Runtime.Audio.SingleListener.Enforce(new[] { ear.GetComponent<AudioListener>() }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cam);
                UnityEngine.Object.DestroyImmediate(ear);
            }
        }

        [Test]
        public void TheAutopilotAndACriticalHitAreHeardByTheLocalPlayer()
        {
            World w = NewWorld();
            (SoundFeed feed, Rec rec) = Feed(w);
            int me = LocalSeat.Actor, foe = Enemy(w), far = Enemy(w, 3);
            World.PushEvent(w, SimEventType.AutopilotChanged, me, -1, w.X[me], w.Z[me], 1);
            World.PushEvent(w, SimEventType.AutopilotChanged, foe, -1, w.X[foe], w.Z[foe], 1);
            Step(w, feed, rec);
            Assert.AreEqual(1, rec.Plays.Count, "only my own takeover");
            Assert.IsTrue(rec.Has("autopilot_on"));
            Assert.IsNotNull(SoundTable.Cue(SoundTable.IndexOf("autopilot_on")).Caption);
            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.AutopilotChanged, me, -1, w.X[me], w.Z[me], 0);
            Step(w, feed, rec);
            Assert.IsTrue(rec.Has("autopilot_off"), "handed back: you have control");

            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.CriticalHit, foe, me, w.X[foe], w.Z[foe], 3);
            Step(w, feed, rec);
            SoundRequest mine = rec.Get("critical_hit");
            Assert.IsTrue(rec.Has("critical_hit") && mine.Own, "my critical hit is at own priority");
            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.CriticalHit, me, foe, w.X[me], w.Z[me], 1);
            Step(w, feed, rec);
            Assert.IsTrue(rec.Get("critical_hit").Own && rec.Get("critical_hit").Threat, "a crit on me is a threat");
            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.CriticalHit, far, foe, w.X[far], w.Z[far], 2);
            Step(w, feed, rec);
            foreach (SoundRequest r in rec.Plays) Assert.IsFalse(r.Own, "someone else's crit is the crowd's");
        }

        [Test]
        public void MyOwnVoicesAreNeverDroppedAt32Fighters()
        {
            VoicePlanner p = VoicePlanner.FromTable(SoundDirector.Voices);
            Assert.Greater(p.OwnReserve, 0);
            int[] crowd = { SoundTable.IndexOf("hit_flesh_heavy"), SoundTable.IndexOf("pain"), SoundTable.IndexOf("swing_heavy"), SoundTable.IndexOf("dodge_phew"),
                SoundTable.IndexOf("step_grass"), SoundTable.IndexOf("blast_near"), SoundTable.IndexOf("kaiju_slam"), SoundTable.IndexOf("death") };
            // Everything the feed sends at own priority for my hits, hurts, kills and defence. Each is an own-only cue (my pain
            // is own_pain, not the crowd's pain), so the only limit left is the planner's 30 ms repeat guard on MY OWN copy of
            // the same cue (VoicePlanner.OwnRepeatGuard: two identical starts inside 30 ms are one sound); the loop asks for
            // each cue every 7 frames (117 ms), and a second loop below asks inside the guard to pin that exact limit.
            int[] mine = { SoundTable.IndexOf("own_impact_thud"), SoundTable.IndexOf("hit_confirm"), SoundTable.IndexOf("own_hurt"), SoundTable.IndexOf("own_pain"),
                SoundTable.IndexOf("kill_sting"), SoundTable.IndexOf("my_dodge"), SoundTable.IndexOf("my_shield"), SoundTable.IndexOf("own_hurt_block") };
            foreach (int c in mine) Assert.GreaterOrEqual(c, 0);
            int refusedOwn = 0, startedCrowd = 0;
            double now = 0;
            for (int frame = 0; frame < 600; frame++, now += 1.0 / 60.0)
            {
                p.BeginFrame(frame);
                // 32 fighters: every one of them asks for a sound every frame, the loudest and most threatening first
                for (int f = 0; f < 32; f++)
                {
                    int cue = crowd[(f + frame) % crowd.Length];
                    float pr = SoundSpace.Priority(SoundTable.Cues[cue].Priority, 1f, false, f % 5 == 0);
                    if (p.Request(cue, pr, now, 0.8, false) >= 0) startedCrowd++;
                }
                if (frame % 7 == 0)
                    foreach (int cue in mine)
                        if (p.Request(cue, SoundSpace.Priority(SoundTable.Cues[cue].Priority, 1f, true, false), now, 0.6, true) < 0) refusedOwn++;
            }
            Assert.AreEqual(0, refusedOwn, "every own cue always gets a voice at 32 fighters");
            Assert.Greater(startedCrowd, 0, "the crowd still plays");
            p.BeginFrame(9999);
            Assert.GreaterOrEqual(p.Request(mine[0], 200f, now, 0.5, true), 0);
            Assert.AreEqual(-1, p.Request(mine[0], 200f, now + 0.01, 0.5, true), "the one stated limit: my own repeat inside 30 ms");
            Assert.AreEqual(VoiceRefusal.Cooldown, p.LastRefusal);
            Assert.GreaterOrEqual(p.Request(mine[0], 200f, now + VoicePlanner.OwnRepeatGuard + 0.001, 0.5, true), 0);
        }

        [Test]
        public void TheHeartbeatStartsUnder30StopsOver35AndRisesTo18HzUnder15()
        {
            var h = new Heartbeat();
            h.Update(0.5f, true);
            Assert.IsFalse(h.On);
            h.Update(0.29f, true);
            Assert.IsTrue(h.On);
            Assert.AreEqual(Heartbeat.RateFor(0.29f), h.Rate, 1e-5f);
            h.Update(0.33f, true);
            Assert.IsTrue(h.On, "hysteresis: a heal tick to 33% keeps it");
            Assert.AreEqual(1.2f, h.Rate, 1e-5f);
            h.Update(0.36f, true);
            Assert.IsFalse(h.On, "off above 35%");
            Assert.AreEqual(0f, h.Rate);
            h.Update(0.33f, true);
            Assert.IsFalse(h.On, "and stays off until under 30% again");
            h.Update(0.1f, true);
            Assert.AreEqual(1.8f, h.Rate, 1e-5f, "1.8 Hz under 15%");
            Assert.AreEqual(1.5f, Heartbeat.RateFor(0.225f), 1e-4f, "linear between");
            h.Update(0.1f, false);
            Assert.IsFalse(h.On, "never while dead");
            Assert.LessOrEqual(Heartbeat.FastHz, 2f, "inside the 3 flashes a second limit with room");
            Assert.IsNotNull(SoundTable.Cue(SoundTable.IndexOf("heartbeat")).Caption, "captioned for the hard of hearing");

            // the feed drives it from the local player's health
            World w = NewWorld();
            (SoundFeed feed, Rec rec) = Feed(w);
            w.Actors[LocalSeat.Actor].Hp = w.Actors[LocalSeat.Actor].MaxHp * 0.2;
            Step(w, feed, rec);
            Assert.IsTrue(feed.Heart.On);
            w.Actors[LocalSeat.Actor].Hp = w.Actors[LocalSeat.Actor].MaxHp;
            Step(w, feed, rec);
            Assert.IsFalse(feed.Heart.On);
        }
    }
}
