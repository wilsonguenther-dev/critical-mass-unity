using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Audio;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 Skybreak sound: the adaptive score's state map, plan and clips (exact lengths on the 80 BPM grid), the bar-line
    /// maths, the five b13 events now routed to their Skybreak cues (the Dodge as the dodger's own sound), and the top-down
    /// screen-space placement (darker below the screen centre, the behind-you tick).
    /// </summary>
    public sealed class SkybreakScoreTests
    {
        static ScoreInputs Calm() => new ScoreInputs { Remaining = 600, MatchSeconds = 720, UntilHatch = 200, Kaiju = KaijuState.Egg };

        // ------------------------------------------------------------------ the state map

        [Test]
        public void TheScoreStateFollowsTheMatchByPriority()
        {
            ScoreInputs i = Calm();
            Assert.AreEqual(ScoreState.Calm, ScoreMix.Decide(i).State, "build-up");
            i.LeadSeconds = 12; Assert.AreEqual(ScoreState.Leading, ScoreMix.Decide(i).State);
            i.LeadSeconds = -12; Assert.AreEqual(ScoreState.Trailing, ScoreMix.Decide(i).State);
            i.Rally = true; Assert.AreEqual(ScoreState.Rally, ScoreMix.Decide(i).State, "the comeback rally outranks trailing");
            i.HillContested = true; Assert.AreEqual(ScoreState.Contested, ScoreMix.Decide(i).State, "a contested hill outranks the score line");
            i.UntilHatch = 20; Assert.AreEqual(ScoreState.Egg, ScoreMix.Decide(i).State, "the egg's heartbeat in the last 30 s before the hatch");
            i.UntilHatch = 0; i.Hatched = true; i.Kaiju = KaijuState.Berserk;
            ScoreTarget b = ScoreMix.Decide(i);
            Assert.AreEqual(ScoreState.Berserk, b.State);
            Assert.AreEqual(ScoreColour.Corrupt, b.Colour, "berserk plays the corrupt theme");
            Assert.AreEqual(2, b.Variant(ScoreLayer.Theme));
            i.Remaining = 45;
            Assert.AreEqual(ScoreState.FinalMinute, ScoreMix.Decide(i).State, "the final minute outranks everything");
            i.MatchSeconds = 50;
            Assert.AreNotEqual(ScoreState.FinalMinute, ScoreMix.Decide(i).State, "a match shorter than a minute has no final minute");
        }

        [Test]
        public void AFedKaijuPlaysTheMajorFormAndLessSoundThinsTheRhythm()
        {
            ScoreInputs i = Calm();
            i.Hatched = true; i.Kaiju = KaijuState.Azure; i.HillContested = true;
            ScoreTarget t = ScoreMix.Decide(i);
            Assert.AreEqual(ScoreColour.Feed, t.Colour);
            Assert.AreEqual(1, t.Variant(ScoreLayer.Bed), "bed feed");
            Assert.AreEqual(1, t.Variant(ScoreLayer.Theme), "theme feed");
            Assert.AreEqual((int)RhythmForm.March, t.Variant(ScoreLayer.Rhythm));
            i.Kaiju = KaijuState.Neutral;
            Assert.AreEqual(ScoreColour.Minor, ScoreMix.Decide(i).Colour, "a hatched Kaiju on no side keeps the minor form");
            float full = ScoreMix.Decide(i).RhythmGain;
            i.LessSound = true;
            Assert.AreEqual(full * ScoreMix.LessSoundRhythm, ScoreMix.Decide(i).RhythmGain, 1e-6f);
        }

        [Test]
        public void ThePlanRunsFourMinutesWithoutRepeatingASectionBackToBack()
        {
            Assert.GreaterOrEqual(ScoreMix.Plan.Length * ScoreMix.Section, 240.0);
            var used = new HashSet<int>();
            for (int k = 0; k < ScoreMix.Plan.Length; k++)
            {
                used.Add(ScoreMix.SectionAt(k));
                Assert.AreNotEqual(ScoreMix.SectionAt(k), ScoreMix.SectionAt(k + 1), "step " + k + " (with the wrap)");
            }
            Assert.AreEqual(ScoreMix.Sections.Length, used.Count, "every section plays");
            Assert.AreEqual(ScoreMix.Section, ScoreMix.SectionBars * ScoreMix.Bar, 1e-9);
            Assert.AreEqual(ScoreMix.Bar, 4 * ScoreMix.Beat, 1e-9);
            Assert.AreEqual(60.0 / ScoreMix.Bpm, ScoreMix.Beat, 1e-9);
        }

        [Test]
        public void EveryStemAndStingerIsInTheBuildAtItsExactLength()
        {
            for (int s = 0; s < ScoreMix.Sections.Length; s++)
                for (int l = 0; l < 3; l++)
                    for (int v = 0; v < ScoreMix.VariantNames[l].Length; v++)
                    {
                        string name = ScoreMix.Clip(s, (ScoreLayer)l, v);
                        AudioClip c = Resources.Load<AudioClip>(ScoreMix.Folder + name);
                        Assert.IsNotNull(c, name);
                        Assert.AreEqual(ScoreMix.Section + ScoreMix.Tail, c.length, 0.005, name + ": 8 bars and the 2.5 s tail");
                    }
            AudioClip title = Resources.Load<AudioClip>(MusicDirector.MusicFolder + "music-title");
            Assert.IsNotNull(title);
            Assert.AreEqual(16 * ScoreMix.Bar, title.length, 0.005, "the title loop is 16 bars, its tail wrapped onto its start");
            foreach (KaijuType t in Enum.GetValues(typeof(KaijuType)))
                Assert.IsNotNull(Resources.Load<AudioClip>(MusicDirector.StingFolder + ScoreMix.HatchSting(t)), "hatch stinger for " + t);
            foreach (string old in new[] { "music-match", "music-final", "music-berserk" })
                Assert.IsNull(Resources.Load<AudioClip>(MusicDirector.MusicFolder + old), old + ": the library loops are out of the build");
        }

        [Test]
        public void ChangesLandOnTheBarGridAndTheHatchDucksTheScore()
        {
            Assert.AreEqual(3.0, ScoreMix.NextLine(0, ScoreMix.Bar, 1.2), 1e-9);
            Assert.AreEqual(6.0, ScoreMix.NextLine(0, ScoreMix.Bar, 2.98), 1e-9, "too close to the line: the next one");
            Assert.AreEqual(10.75, ScoreMix.NextLine(10, ScoreMix.Beat, 10.1), 1e-9);
            Assert.AreEqual(1f, ScoreMix.Fade(6, 5, 0.75, 1, 0), 1e-6f, "faded in after the beat");
            float a = ScoreMix.Fade(5.375, 5, 0.75, 1, 0), b = ScoreMix.Fade(5.375, 5, 0.75, -1, 1);
            Assert.AreEqual(1f, a * a + b * b, 1e-5f, "equal power through the crossfade");
            Assert.AreEqual(1f, ScoreMix.HatchDuck(9, double.NaN, 6));
            Assert.AreEqual(0.15f, ScoreMix.HatchDuck(11, 10, 6), 1e-6f);
            Assert.Less(ScoreMix.HatchDuck(16, 10, 6), 1f);
            Assert.AreEqual(1f, ScoreMix.HatchDuck(10 + 5 + ScoreMix.Bar + 0.01, 10, 6));
        }

        // ------------------------------------------------------------------ the five b13 events

        private sealed class Recorder : ISoundSink
        {
            public readonly List<SoundRequest> Plays = new List<SoundRequest>();
            public void Play(in SoundRequest r) => Plays.Add(r);
            public void Fuse(int slot, double x, double z, int endTick, int cue) { }
            public void Berserk(bool on, int cue) { }
            public bool Has(string cue) => Plays.Exists(p => p.Cue == SoundTable.IndexOf(cue));
            public SoundRequest Get(string cue) => Plays.Find(p => p.Cue == SoundTable.IndexOf(cue));
        }

        private static World NewWorld() => World.CreateWorld(new WorldOptions
        {
            Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
        });

        [Test]
        public void TheFiveSilentB13EventsNowSoundAndTheDodgeIsTheDodgersOwn()
        {
            World w = NewWorld();
            var feed = new SoundFeed(World.ActorCapacity(w.Config));
            var rec = new Recorder();
            feed.OnTick(w, rec);
            int me = LocalSeat.Actor, bot = me == 1 ? 2 : 1;
            double x = w.X[me], z = w.Z[me];
            World.PushEvent(w, SimEventType.HutLevel, (int)TeamId.Azure, me, x, z, 2);
            World.PushEvent(w, SimEventType.OreMined, me, 3, x, z, 1);
            World.PushEvent(w, SimEventType.WallBuilt, me, 10, x, z, 3);
            World.PushEvent(w, SimEventType.WallBuilt, bot, 12, x + 1, z, 1);
            World.PushEvent(w, SimEventType.WallBroken, bot, 0, x + 2, z, 0);
            World.PushEvent(w, SimEventType.Dodge, bot, 0, x, z, 1);
            World.PushEvent(w, SimEventType.BankDeposit, me, (int)TeamId.Azure, x, z, 2, BiomassType.Ore, DamageCause.Melee);
            World.PushEvent(w, SimEventType.PiecePlaced, me, 0, x, z, 0);
            feed.OnTick(w, rec);
            foreach (string id in new[] { "sb_hut_level", "sb_ore_hit", "sb_wall_place", "sb_block_place", "sb_wall_break", "sb_dodge", "sb_ore_bank", "sb_ladder_place" })
                Assert.IsTrue(rec.Has(id), id + " plays");
            Assert.IsFalse(rec.Get("sb_dodge").Own, "a bot's dodge (B = 0) is not the player's own");
            Assert.IsTrue(rec.Get("sb_hut_level").Own, "your team's hut wins a voice");
            Assert.IsTrue(rec.Get("sb_wall_place").Own);

            rec.Plays.Clear();
            World.PushEvent(w, SimEventType.Dodge, me, 0, x, z, 1);
            World.PushEvent(w, SimEventType.OreMined, bot, -1, x, z, -2);
            feed.OnTick(w, rec);
            Assert.IsTrue(rec.Get("sb_dodge").Own, "the player's dodge carries the own priority");
            Assert.IsFalse(rec.Has("refused"), "another player's refused press is silent");
            foreach (SimRoute r in SoundTable.Sim)
                if (r.Type == SimEventType.HutLevel || r.Type == SimEventType.OreMined || r.Type == SimEventType.WallBuilt
                    || r.Type == SimEventType.WallBroken || r.Type == SimEventType.Dodge)
                    Assert.AreNotEqual(Pick.Silence, r.Pick, r.Type + " is no longer silent");
        }

        [Test]
        public void EverySkybreakEffectIsInTheBuildAndShort()
        {
            HashSet<string> files = SoundTable.ScoreFiles();
            Assert.GreaterOrEqual(files.Count, 13);
            foreach (string f in files)
            {
                AudioClip c = Resources.Load<AudioClip>(SoundDirector.ScoreSfxFolder + "/" + f);
                Assert.IsNotNull(c, f);
                Assert.LessOrEqual(c.length, 2.0f, f + " is short");
            }
            Assert.AreEqual(42, SoundTable.Files().Count, "the library count is unchanged");
        }

        // ------------------------------------------------------------------ top-down space

        [Test]
        public void SoundsBelowTheScreenCentreAreDarkerThanTheirMirrorAbove()
        {
            const float aspect = 16f / 9f, half = 12f;
            SoundSpace.Screen(0.6f, 0.8f, aspect, half, out float panUp, out float dUp, out float belowUp);
            SoundSpace.Screen(0.6f, 0.2f, aspect, half, out float panDown, out float dDown, out float belowDown);
            Assert.AreEqual(dUp, dDown, 1e-4f, "mirrored about the focus: the same screen distance");
            Assert.AreEqual(panUp, panDown, 1e-6f, "pan follows screen x only");
            Assert.Greater(panUp, 0f, "right of centre pans right");
            SoundSpace.Range(SoundRange.Mid, out float near, out float far);
            float open = SoundSpace.LowPass(dUp, near, far);
            float up = SoundSpace.DepthCutoff(open, belowUp), down = SoundSpace.DepthCutoff(open, belowDown);
            Assert.AreEqual(open, up, 1e-3f, "above the centre row stays open");
            Assert.GreaterOrEqual((up - down) / up, 0.3f, "below the centre the cutoff is at least 30% lower");
            Assert.Less(SoundSpace.DepthGain(belowDown), SoundSpace.DepthGain(belowUp));
            Assert.AreEqual(Math.Pow(10, SoundSpace.BelowDb / 20.0), SoundSpace.DepthGain(1f), 1e-5);
        }

        [Test]
        public void TheBehindYouTickIsForOffScreenThreatsBelowAndSpaced()
        {
            SoundSpace.Screen(0.5f, -0.2f, 16f / 9f, 12f, out _, out _, out float below);
            Assert.Greater(below, 1f, "past the bottom edge");
            Assert.IsTrue(SoundSpace.BehindYou(true, false, below, 10, 0));
            Assert.IsFalse(SoundSpace.BehindYou(false, false, below, 10, 0), "not a threat");
            Assert.IsFalse(SoundSpace.BehindYou(true, true, below, 10, 0), "your own sound");
            Assert.IsFalse(SoundSpace.BehindYou(true, false, 0.9f, 10, 0), "still on screen");
            Assert.IsFalse(SoundSpace.BehindYou(true, false, below, 10, 9.5), "spaced " + SoundSpace.BehindTickGap + " s");
            Assert.GreaterOrEqual(SoundTable.IndexOf("sb_behind_tick"), 0);
            Assert.IsNotNull(SoundTable.Cue(SoundTable.IndexOf("sb_behind_tick")).Caption, "the tick has a caption");
        }
    }
}
