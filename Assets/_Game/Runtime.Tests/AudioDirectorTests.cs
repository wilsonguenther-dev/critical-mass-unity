using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Audio;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// DG-18 announcer and music director, DG-31 UI sound. The announcer tests replay event logs through the real
    /// decoder and queue with a test clock: each trigger plays exactly one line, never two within 6 s, and a line that
    /// waited more than 2 s is dropped. Assets: every line, track, sting and UI cue has its clip.
    /// </summary>
    public sealed class AudioDirectorTests
    {
        static World NewWorld()
        {
            return World.CreateWorld(new WorldOptions
            {
                Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
            });
        }

        static void AssertSpacing(AnnouncerQueue q)
        {
            for (int i = 1; i < q.HistoryCount; i++)
            {
                double gap = q.HistoryAt(i).Time - q.HistoryAt(i - 1).Time;
                Assert.GreaterOrEqual(gap, AnnouncerQueue.CooldownSeconds - 1e-9,
                    $"{q.HistoryAt(i - 1).Id} and {q.HistoryAt(i).Id} started {gap:0.00} s apart");
            }
        }

        // ---- lines and words pack ----

        [Test]
        public void EveryTriggerHasExactlyOneLine()
        {
            int n = Enum.GetValues(typeof(AnnouncerTrigger)).Length;
            Assert.AreEqual(n, AnnouncerLines.Table.Length);
            Assert.AreEqual(n, AnnouncerLines.TriggerNames.Length);
            var ids = new HashSet<string>();
            for (int i = 0; i < n; i++)
            {
                AnnouncerLine l = AnnouncerLines.Table[i];
                Assert.AreEqual((AnnouncerTrigger)i, l.Trigger, "table order");
                Assert.IsFalse(string.IsNullOrEmpty(l.Text), l.Id);
                Assert.IsTrue(ids.Add(l.Id), "duplicate id " + l.Id);
                Assert.AreEqual(i, AnnouncerLines.TriggerIndex(AnnouncerLines.TriggerNames[i]));
            }
        }

        [Test]
        public void BuiltInLinesMatchTheWordsPack()
        {
            string path = Path.Combine(Application.dataPath, "_Game", "Content", "Lore", "announcer.json");
            if (!File.Exists(path)) Assert.Ignore("words pack (DG-04) not merged yet");
            var before = new string[AnnouncerLines.Table.Length];
            for (int i = 0; i < before.Length; i++) before[i] = AnnouncerLines.Table[i].Id + "|" + AnnouncerLines.Table[i].Text;
            int applied = AnnouncerLines.ApplyWordsPack(File.ReadAllText(path));
            Assert.AreEqual(AnnouncerLines.Table.Length, applied, "every words-pack trigger maps to a built-in trigger");
            for (int i = 0; i < before.Length; i++)
                Assert.AreEqual(before[i], AnnouncerLines.Table[i].Id + "|" + AnnouncerLines.Table[i].Text,
                    "built-in fallback drifted from announcer.json (and the baked voice line with it): re-run tools/audio/bake_voice.py");
        }

        [Test]
        public void BadWordsPackChangesNothing()
        {
            Assert.AreEqual(0, AnnouncerLines.ApplyWordsPack(""));
            Assert.AreEqual(0, AnnouncerLines.ApplyWordsPack("{not json"));
            Assert.AreEqual(0, AnnouncerLines.ApplyWordsPack("{\"lines\":[{\"id\":\"x\",\"trigger\":\"nope\",\"text\":\"t\"}]}"));
        }

        // ---- assets ----

        [Test]
        public void EveryLineHasAVoiceClip()
        {
            foreach (AnnouncerLine l in AnnouncerLines.Table)
                Assert.IsNotNull(Resources.Load<AudioClip>(Announcer.VoiceFolder + l.Id), "missing voice clip CMVoice/" + l.Id);
            foreach (string id in new[] { "story-title", "story-card-1", "story-card-2", "story-card-3", "story-card-4", "story-card-5" })
                Assert.IsNotNull(Resources.Load<AudioClip>(Announcer.VoiceFolder + id), "missing narrator clip " + id);
        }

        [Test]
        public void EveryTrackStingAndUiCueHasAClip()
        {
            foreach (string t in MusicDirector.TrackNames)
                if (t != null) Assert.IsNotNull(Resources.Load<AudioClip>(MusicDirector.MusicFolder + t), t);
            foreach (string s in new[] { "sting-victory", "sting-defeat", "sting-draw" })
                Assert.IsNotNull(Resources.Load<AudioClip>(MusicDirector.StingFolder + s), s);
            Assert.AreEqual(Enum.GetValues(typeof(UiCue)).Length, UiSoundBinder.CueFiles.Length);
            foreach (string u in UiSoundBinder.CueFiles)
                Assert.IsNotNull(Resources.Load<AudioClip>(UiSoundBinder.UiFolder + u), u);
        }

        [Test]
        public void ProjectHasOneMixerAndMusicUsesIt()
        {
            Assert.IsNotNull(Resources.Load<UnityEngine.Audio.AudioMixer>(GameSettings.MixerResource), "settings mixer missing");
            foreach (string folder in new[] { "CMMusic", "CMStings", "CMVoice", "CMUi" })
                Assert.AreEqual(0, Resources.LoadAll<UnityEngine.Audio.AudioMixer>(folder).Length, "DG-18/31 add no mixer of their own: " + folder);
        }

        // ---- queue ----

        [Test]
        public void IsolatedTriggersEachPlayExactlyOneLine()
        {
            var q = new AnnouncerQueue();
            int n = AnnouncerLines.Table.Length;
            for (int i = 0; i < n; i++)
            {
                double t = i * 7.0;
                Assert.IsTrue(q.Enqueue((AnnouncerTrigger)i, t));
                for (double c = t; c < t + 7.0; c += 0.1) q.TryNext(c, out _);
            }
            Assert.AreEqual(n, q.HistoryCount);
            for (int i = 0; i < n; i++)
            {
                Assert.AreEqual((AnnouncerTrigger)i, q.HistoryAt(i).Trigger);
                Assert.AreEqual(AnnouncerLines.Table[i].Id, q.HistoryAt(i).Id);
            }
            AssertSpacing(q);
            Assert.AreEqual(0, q.Dropped);
        }

        [Test]
        public void BurstPlaysOneAndDropsLinesOlderThanTwoSeconds()
        {
            var q = new AnnouncerQueue();
            q.Enqueue(AnnouncerTrigger.HillContested, 0);
            q.Enqueue(AnnouncerTrigger.FirstKnockOut, 0);
            q.Enqueue(AnnouncerTrigger.FinalMinute, 0);
            q.Enqueue(AnnouncerTrigger.HillTaken, 0);
            Assert.IsFalse(q.Enqueue(AnnouncerTrigger.HillTaken, 0.1), "a waiting trigger is not queued twice");
            for (double c = 0; c < 10; c += 0.05) q.TryNext(c, out _);
            Assert.AreEqual(1, q.HistoryCount, "the cooldown (6 s) outlasts the 2 s age limit: only one line of a burst plays");
            Assert.AreEqual(AnnouncerTrigger.FinalMinute, q.HistoryAt(0).Trigger, "highest priority first");
            Assert.AreEqual(3, q.Dropped);
            Assert.AreEqual(0, q.Pending);
        }

        [Test]
        public void LineQueuedNearTheEndOfTheCooldownPlaysWhenItEnds()
        {
            var q = new AnnouncerQueue();
            q.Enqueue(AnnouncerTrigger.HillTaken, 0);
            Assert.IsTrue(q.TryNext(0, out _));
            q.Enqueue(AnnouncerTrigger.HillLost, 4.5);
            for (double c = 4.5; c < 12; c += 0.05) q.TryNext(c, out _);
            Assert.AreEqual(2, q.HistoryCount);
            Assert.AreEqual(6.0, q.HistoryAt(1).Time, 0.051);
            AssertSpacing(q);
        }

        [Test]
        public void FullQueuePushesOutTheLowestPriority()
        {
            var q = new AnnouncerQueue();
            AnnouncerTrigger[] lows = { AnnouncerTrigger.HillContested, AnnouncerTrigger.FirstKnockOut, AnnouncerTrigger.MultiKo2,
                AnnouncerTrigger.MultiKo3, AnnouncerTrigger.MultiKo4, AnnouncerTrigger.SecretHillAlone, AnnouncerTrigger.HillTaken,
                AnnouncerTrigger.HillLost };
            for (int i = 0; i < lows.Length; i++) q.Enqueue(lows[i], i * 0.01);
            Assert.AreEqual(AnnouncerQueue.Capacity, q.Pending);
            q.Enqueue(AnnouncerTrigger.Victory, 0.2);
            Assert.AreEqual(AnnouncerQueue.Capacity, q.Pending);
            Assert.IsFalse(q.IsPending(AnnouncerTrigger.HillContested), "oldest priority-1 line pushed out");
            Assert.IsTrue(q.TryNext(0.3, out AnnouncerLine first));
            Assert.AreEqual(AnnouncerTrigger.Victory, first.Trigger);
        }

        // ---- decoder: an event log through the real decoder ----

        sealed class Step
        {
            public double T;
            public Action<World> Do;
            public AnnouncerTrigger? Expect;
        }

        static void Push(World w, SimEventType t, int a, int b, double v) => World.PushEvent(w, t, a, b, 0, 0, v);

        [Test]
        public void EventLogPlaysOneLinePerTriggerNeverTwoWithinSixSeconds()
        {
            World w = NewWorld();
            Assert.AreEqual(TeamId.Azure, w.Actors[0].Team);
            double end = w.Config.MatchSeconds;
            var steps = new List<Step>
            {
                new Step { T = 0, Do = _ => { }, Expect = AnnouncerTrigger.MatchStart },
                new Step { T = 10, Do = x => Push(x, SimEventType.HillOwnerChange, 1, 0, 0), Expect = AnnouncerTrigger.HillTaken },
                new Step { T = 20, Do = x => Push(x, SimEventType.HillOwnerChange, 3, 0, 0), Expect = AnnouncerTrigger.HillContested },
                new Step { T = 30, Do = x => Push(x, SimEventType.HillOwnerChange, 2, 0, 0), Expect = AnnouncerTrigger.HillTakenByEnemy },
                new Step { T = 40, Do = x => Push(x, SimEventType.HillOwnerChange, 0, 0, 0), Expect = null },
                new Step { T = 50, Do = x => Push(x, SimEventType.HillOwnerChange, 1, 0, 0), Expect = AnnouncerTrigger.HillTaken },
                new Step { T = 60, Do = x => Push(x, SimEventType.HillOwnerChange, 0, 0, 0), Expect = AnnouncerTrigger.HillLost },
                new Step { T = 70, Do = x => { x.Match.Time = x.Config.HatchSeconds - 29; }, Expect = AnnouncerTrigger.EggSoon },
                new Step { T = 80, Do = x => Push(x, SimEventType.BerserkLocked, -1, -1, 1), Expect = AnnouncerTrigger.BerserkLocked },
                new Step
                {
                    T = 90, Expect = AnnouncerTrigger.BerserkHatch,
                    Do = x => { x.Match.Time = x.Config.HatchSeconds; x.Match.Hatched = true; Push(x, SimEventType.Hatch, -1, -1, 3); },
                },
                new Step { T = 100, Do = x => Push(x, SimEventType.KaijuKO, -1, 0, 0), Expect = AnnouncerTrigger.KaijuKnockedOut },
                new Step { T = 110, Do = x => Push(x, SimEventType.KaijuRecover, -1, -1, 0), Expect = AnnouncerTrigger.KaijuRecovers },
                new Step { T = 120, Do = x => Push(x, SimEventType.Death, 20, 7, 10), Expect = AnnouncerTrigger.FirstKnockOut },
                new Step
                {
                    T = 130, Expect = AnnouncerTrigger.MultiKo2,
                    Do = x => { x.Match.Time += 1; Push(x, SimEventType.Death, 21, 0, 10); Push(x, SimEventType.Death, 22, 0, 10); },
                },
                new Step { T = 140, Do = x => { x.Match.Time = x.Config.MatchSeconds - 59; }, Expect = AnnouncerTrigger.FinalMinute },
                new Step
                {
                    T = 150, Expect = AnnouncerTrigger.Victory,
                    Do = x =>
                    {
                        x.Match.Time = x.Config.MatchSeconds;
                        x.Match.Phase = MatchPhase.Ended;
                        x.Match.Winner = MatchResult.Azure;
                        Push(x, SimEventType.MatchEnd, 0, 0, 12);
                    },
                },
            };

            var dec = new AnnouncerDecoder();
            var q = new AnnouncerQueue();
            var expected = new List<AnnouncerTrigger>();
            foreach (Step s in steps)
            {
                s.Do(w);
                if (s.Expect.HasValue) expected.Add(s.Expect.Value);
                // Poll every 0.1 s of wall time until the next step (9.9 s): no line may repeat or stack up.
                for (double c = s.T; c < s.T + 9.95; c += 0.1)
                {
                    dec.Poll(w, q, c);
                    q.TryNext(c, out _);
                }
                Assert.AreEqual(expected.Count, q.HistoryCount, $"after the step at {s.T} s");
            }
            for (int i = 0; i < expected.Count; i++) Assert.AreEqual(expected[i], q.HistoryAt(i).Trigger, "play " + i);
            AssertSpacing(q);
            Assert.AreEqual(0, q.Dropped, "events 10 s apart never drop");
            Assert.That(end, Is.GreaterThan(60));
        }

        [Test]
        public void HillFlickerCallsTakenOnce()
        {
            World w = NewWorld();
            var dec = new AnnouncerDecoder();
            var q = new AnnouncerQueue();
            dec.Poll(w, q, 0);
            q.Clear();
            Push(w, SimEventType.HillOwnerChange, 1, 0, 0);
            Push(w, SimEventType.HillOwnerChange, 3, 0, 0);
            Push(w, SimEventType.HillOwnerChange, 1, 0, 0);
            Push(w, SimEventType.HillOwnerChange, 3, 0, 0);
            Push(w, SimEventType.HillOwnerChange, 1, 0, 0);
            dec.Poll(w, q, 1);
            Assert.IsTrue(q.IsPending(AnnouncerTrigger.HillTaken));
            Assert.IsTrue(q.IsPending(AnnouncerTrigger.HillContested));
            Assert.AreEqual(2, q.Pending, "taken once, contested once");
        }

        [Test]
        public void DefeatAndDrawFromTheHumansSide()
        {
            foreach (var (winner, expect) in new[] { (MatchResult.Crimson, AnnouncerTrigger.Defeat), (MatchResult.Draw, AnnouncerTrigger.Draw) })
            {
                World w = NewWorld();
                var dec = new AnnouncerDecoder();
                var q = new AnnouncerQueue();
                dec.Poll(w, q, 0);
                q.Clear();
                w.Match.Phase = MatchPhase.Ended;
                w.Match.Winner = winner;
                Push(w, SimEventType.MatchEnd, 0, 0, 0);
                dec.Poll(w, q, 1);
                dec.Poll(w, q, 2);
                Assert.AreEqual(1, q.Pending);
                Assert.IsTrue(q.IsPending(expect), winner.ToString());
            }
        }

        [Test]
        public void DecoderNeverWritesTheWorld()
        {
            World w = NewWorld();
            var scratch = new InputFrame();
            for (int i = 0; i < 300; i++) MatchTick.AdvanceNoHuman(w, scratch);
            int head = w.EventHead, tick = w.Tick;
            double time = w.Match.Time, x0 = w.X[0];
            var dec = new AnnouncerDecoder();
            var q = new AnnouncerQueue();
            dec.Poll(w, q, 0);
            dec.Poll(w, q, 1);
            Assert.AreEqual(head, w.EventHead);
            Assert.AreEqual(tick, w.Tick);
            Assert.AreEqual(time, w.Match.Time);
            Assert.AreEqual(x0, w.X[0]);
        }

        /// <summary>A whole bot match at the default 2x pace: the rule holds on real traffic, and the result is called.</summary>
        [Test, Category("Slow")]
        public void FullMatchKeepsTheRule()
        {
            World w = NewWorld();
            var scratch = new InputFrame();
            var dec = new AnnouncerDecoder();
            var q = new AnnouncerQueue();
            double hz = w.Config.SimHz, speed = w.Config.DefaultSpeed;
            int guard = (int)((w.Config.MatchSeconds + 5) * hz);
            for (int i = 0; i < guard && w.Match.Phase == MatchPhase.Playing; i++)
            {
                MatchTick.AdvanceNoHuman(w, scratch);
                double wall = w.Tick / hz / speed;
                dec.Poll(w, q, wall);
                q.TryNext(wall, out _);
            }
            Assert.AreEqual(MatchPhase.Ended, w.Match.Phase);
            double t = w.Tick / hz / speed;
            for (double c = t; c < t + 8; c += 0.1)
            {
                dec.Poll(w, q, c);
                q.TryNext(c, out _);
            }
            AssertSpacing(q);
            Assert.Greater(q.HistoryCount, 3);
            Assert.AreEqual(AnnouncerTrigger.MatchStart, q.HistoryAt(0).Trigger);
            AnnouncerTrigger last = q.HistoryAt(q.HistoryCount - 1).Trigger;
            Assert.That(last == AnnouncerTrigger.Victory || last == AnnouncerTrigger.Defeat || last == AnnouncerTrigger.Draw,
                "the result is called last, got " + last);
        }

        // ---- music director ----

        [Test]
        public void MusicFollowsTheMatch()
        {
            World w = NewWorld();
            Assert.AreEqual(MusicState.Title, MusicDirector.Decide(null, false, false));
            Assert.AreEqual(MusicState.Title, MusicDirector.Decide(w, true, false));
            Assert.AreEqual(MusicState.Title, MusicDirector.Decide(w, false, true));
            Assert.AreEqual(MusicState.Match, MusicDirector.Decide(w, false, false));
            w.Match.Time = w.Config.MatchSeconds - 60;
            Assert.AreEqual(MusicState.FinalMinute, MusicDirector.Decide(w, false, false));
            Assert.Greater(MusicDirector.GainFor(MusicState.FinalMinute, w), MusicDirector.GainFor(MusicState.Match, w), "final-minute lift");
            w.Kaiju.State = KaijuState.Berserk;
            Assert.AreEqual(MusicState.Berserk, MusicDirector.Decide(w, false, false));
            Assert.AreEqual(MusicDirector.LiftGain, MusicDirector.GainFor(MusicState.Berserk, w), "berserk in the final minute keeps the lift");
            w.Match.Phase = MatchPhase.Ended;
            Assert.AreEqual(MusicState.Results, MusicDirector.Decide(w, false, false));
        }

        [Test]
        public void StingsFromTheHumansSide()
        {
            Assert.AreEqual("sting-victory", MusicDirector.StingFor(MatchResult.Azure, TeamId.Azure));
            Assert.AreEqual("sting-defeat", MusicDirector.StingFor(MatchResult.Azure, TeamId.Crimson));
            Assert.AreEqual("sting-victory", MusicDirector.StingFor(MatchResult.Crimson, TeamId.Crimson));
            Assert.AreEqual("sting-draw", MusicDirector.StingFor(MatchResult.Draw, TeamId.Azure));
            Assert.IsNull(MusicDirector.StingFor(MatchResult.None, TeamId.Azure));
        }

        [Test]
        public void CrossfadeIsEqualPower()
        {
            Assert.AreEqual(1f, MusicDirector.Crossfade(0).x, 1e-5f);
            Assert.AreEqual(0f, MusicDirector.Crossfade(0).y, 1e-5f);
            Assert.AreEqual(0f, MusicDirector.Crossfade(1).x, 1e-5f);
            Assert.AreEqual(1f, MusicDirector.Crossfade(1).y, 1e-5f);
            for (float t = 0; t <= 1f; t += 0.1f)
            {
                Vector2 g = MusicDirector.Crossfade(t);
                Assert.AreEqual(1f, g.x * g.x + g.y * g.y, 1e-4f);
            }
        }

        // ---- UI sound ----

        [Test]
        public void BindAllGivesEveryButtonItsSound()
        {
            var root = new GameObject("Screen", typeof(RectTransform));
            try
            {
                Button play = MakeButton(root.transform, "Play");
                Button back = MakeButton(root.transform, "Back");
                Button resume = MakeButton(root.transform, "  Resume");
                var hidden = new GameObject("Hidden Page", typeof(RectTransform));
                hidden.transform.SetParent(root.transform, false);
                hidden.SetActive(false);
                Button deep = MakeButton(hidden.transform, "Apply");
                var toggle = new GameObject("Toggle", typeof(RectTransform)).AddComponent<Toggle>();
                toggle.transform.SetParent(root.transform, false);

                var missing = new List<string>();
                Assert.AreEqual(5, UiSound.CountSelectables(root.transform));
                Assert.AreEqual(5, UiSound.CountUnbound(root.transform, missing));
                Assert.AreEqual(5, UiSound.BindAll(root.transform));
                missing.Clear();
                Assert.AreEqual(0, UiSound.CountUnbound(root.transform, missing), string.Join(", ", missing));
                Assert.AreEqual(0, UiSound.BindAll(root.transform), "binding twice adds nothing");
                Assert.AreEqual(UiSound.Role.Confirm, play.GetComponent<UiSound>().PressRole);
                Assert.AreEqual(UiSound.Role.Back, back.GetComponent<UiSound>().PressRole);
                Assert.AreEqual(UiSound.Role.Back, resume.GetComponent<UiSound>().PressRole);
                Assert.AreEqual(UiSound.Role.Confirm, deep.GetComponent<UiSound>().PressRole);
                Assert.AreEqual(1, deep.GetComponents<UiSound>().Length);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RolesFromLabels()
        {
            Assert.AreEqual(UiSound.Role.Back, UiSound.RoleFromLabel("BACK"));
            Assert.AreEqual(UiSound.Role.Back, UiSound.RoleFromLabel("Close"));
            Assert.AreEqual(UiSound.Role.Back, UiSound.RoleFromLabel("Cancel"));
            Assert.AreEqual(UiSound.Role.Confirm, UiSound.RoleFromLabel("Restart match"));
            Assert.AreEqual(UiSound.Role.Confirm, UiSound.RoleFromLabel(null));
        }

        static Button MakeButton(Transform parent, string label)
        {
            var go = new GameObject(label.Trim(), typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Button b = go.AddComponent<Button>();
            var t = new GameObject("Label", typeof(RectTransform)).AddComponent<Text>();
            t.transform.SetParent(go.transform, false);
            t.text = label;
            return b;
        }
    }
}
