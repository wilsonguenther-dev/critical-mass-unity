using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Runtime.Menus;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// DG-19 story intro (690 frames at 30 Hz, skip on frame 0, cards read by id with fallbacks, three layers per card),
    /// DG-28 attract mode (20 s idle enters, any input exits on the same frame, shot poses) and DG-29 credits (sound credits
    /// parsed from CREDITS.md, every credited file present and every library file credited, team names only from the
    /// repo's docs, no technology or vendor names).
    /// </summary>
    [TestFixture]
    public sealed class StoryAttractCreditsTests
    {
        const double Frame = 1.0 / 30.0;
        static readonly string ProjectRoot = Directory.GetParent(Application.dataPath).FullName;

        static readonly string[] Forbidden =
        {
            "unity", "blender", "kokoro", "elevenlabs", "openai", "gemini", "claude", "anthropic", "gpt", "cinemachine",
            "urp", "shader", "github", "unreal", "nvidia",
        };

        static StoryTimeline DefaultTimeline()
        {
            var secs = new float[StoryContent.CardCount];
            for (int i = 0; i < secs.Length; i++) secs[i] = StoryContent.SecondsFor(StoryContent.DefaultTicks[i], 30, null);
            return new StoryTimeline(secs);
        }

        // ---------------------------------------------------------------- DG-19 story intro

        [Test]
        public void StoryDefaultsAre690TicksAt30Hz()
        {
            int sum = 0;
            foreach (int t in StoryContent.DefaultTicks) sum += t;
            Assert.AreEqual(690, sum);
            Assert.AreEqual(StoryContent.CardCount, StoryContent.DefaultCaptions.Length);
        }

        [Test]
        public void StoryAsLoadedPlaysIn690FramesAt30Hz()
        {
            var captions = new string[StoryContent.CardCount];
            var ticks = new int[StoryContent.CardCount];
            StoryContent.LoadText(captions, ticks, out int hz);
            Assert.AreEqual(30, hz, "story.json simHz");
            int sum = 0;
            foreach (int t in ticks) sum += t;
            Assert.AreEqual(690, sum, "card ticks (words pack or defaults) must total 690 = 23 s");
            for (int i = 0; i < captions.Length; i++) Assert.IsFalse(string.IsNullOrWhiteSpace(captions[i]), "card " + (i + 1));
        }

        [Test]
        public void TimelineEndsOnFrame690Exactly()
        {
            StoryTimeline tl = DefaultTimeline();
            for (int f = 1; f < 690; f++)
            {
                tl.Advance(Frame);
                Assert.IsFalse(tl.Done, "ended early at frame " + f);
            }
            tl.Advance(Frame);
            Assert.IsTrue(tl.Done);
            Assert.IsFalse(tl.Skipped);
        }

        [Test]
        public void TimelineChangesCardAtEachTickBoundary()
        {
            StoryTimeline tl = DefaultTimeline();
            int frame = 0, boundary = 0;
            for (int card = 0; card < StoryContent.CardCount - 1; card++)
            {
                boundary += StoryContent.DefaultTicks[card];
                while (frame < boundary - 1)
                {
                    tl.Advance(Frame);
                    frame++;
                }
                Assert.AreEqual(card, tl.Index, "frame " + frame);
                tl.Advance(Frame);
                frame++;
                Assert.AreEqual(card + 1, tl.Index, "frame " + frame);
            }
        }

        [Test]
        public void SkipWorksOnFrameZero()
        {
            StoryTimeline tl = DefaultTimeline();
            tl.Skip();
            Assert.IsTrue(tl.Done);
            Assert.IsTrue(tl.Skipped);
            Assert.AreEqual(0.0, tl.Time);
            Assert.IsFalse(tl.Advance(Frame), "nothing advances after a skip");
        }

        [Test]
        public void CardFadesStayUnder250Ms()
        {
            Assert.Less(StoryTimeline.FadeSeconds, 0.25);
            StoryTimeline tl = DefaultTimeline();
            Assert.AreEqual(1f, tl.Alpha(0));
            while (tl.Index == 0) tl.Advance(Frame);
            Assert.Less(tl.Alpha(1), 1f, "the second card fades in");
            Assert.AreEqual(1f, tl.Alpha(0), "the first card stays under it while it fades");
            for (int i = 0; i < 8; i++) tl.Advance(Frame);
            Assert.AreEqual(1f, tl.Alpha(1));
            Assert.AreEqual(0f, tl.Alpha(0));
        }

        [Test]
        public void ParseReadsCardsByIdAndKeepsFallbacks()
        {
            const string json = "{\"schema\":\"cm.lore.story/1\",\"simHz\":30,\"cards\":[" +
                                "{\"id\":\"card-3\",\"ticks\":120,\"caption\":\"Third.\"}," +
                                "{\"id\":\"card-1\",\"ticks\":0,\"caption\":\"First.\"}," +
                                "{\"id\":\"card-2\",\"ticks\":99,\"caption\":\"Has a {cfg:matchClock} token.\"}," +
                                "{\"id\":\"card-9\",\"ticks\":5,\"caption\":\"Not a card.\"}," +
                                "{\"id\":\"card-4\",\"ticks\":60,\"caption\":\"   \"}]}";
            var captions = (string[])StoryContent.DefaultCaptions.Clone();
            var ticks = (int[])StoryContent.DefaultTicks.Clone();
            int hz = StoryContent.Parse(json, captions, ticks);
            Assert.AreEqual(30, hz);
            Assert.AreEqual("First.", captions[0]);
            Assert.AreEqual(StoryContent.DefaultTicks[0], ticks[0], "ticks 0 keeps the default");
            Assert.AreEqual(StoryContent.DefaultCaptions[1], captions[1], "a line with a token keeps the default");
            Assert.AreEqual(99, ticks[1]);
            Assert.AreEqual("Third.", captions[2]);
            Assert.AreEqual(120, ticks[2]);
            Assert.AreEqual(StoryContent.DefaultCaptions[3], captions[3], "an empty line keeps the default");
            Assert.AreEqual(StoryContent.DefaultCaptions[4], captions[4]);
            Assert.AreEqual(30, StoryContent.Parse("not json", captions, ticks));
            Assert.AreEqual(30, StoryContent.Parse(null, captions, ticks));
        }

        [Test]
        public void CardLengthComesFromTicksWithoutAClip()
        {
            Assert.AreEqual(5f, StoryContent.SecondsFor(150, 30, null), 1e-6f);
            Assert.AreEqual(3f, StoryContent.SecondsFor(90, 30, null), 1e-6f);
            Assert.AreEqual(1, StoryContent.CardNumber("card-1"));
            Assert.AreEqual(0, StoryContent.CardNumber("card-6"));
            Assert.AreEqual(0, StoryContent.CardNumber("relic-1"));
        }

        [Test]
        public void EveryCardHasThreeLoadableLayers()
        {
            for (int n = 1; n <= StoryContent.CardCount; n++)
            {
                for (int l = 0; l < StoryContent.LayerCount; l++)
                {
                    Texture2D t = StoryContent.LoadLayer(n, l, out bool _);
                    Assert.IsNotNull(t, "card " + n + " layer " + StoryContent.LayerNames[l] + " (real or placeholder) under Resources/" +
                                        StoryContent.ArtResourceRoot);
                }
            }
        }

        [Test]
        public void PlaceholderLayersAreMarkedInTheirFileNames()
        {
            string root = Path.Combine(Application.dataPath, "_Game", "Art", "Story", "Resources", StoryContent.ArtResourceRoot);
            for (int n = 1; n <= StoryContent.CardCount; n++)
            {
                foreach (string layer in StoryContent.LayerNames)
                {
                    string ph = Path.Combine(root, "card-" + n, layer + StoryContent.PlaceholderSuffix + ".png");
                    string real = Path.Combine(root, "card-" + n, layer + ".png");
                    Assert.IsTrue(File.Exists(ph) || File.Exists(real), "missing " + ph);
                }
            }
        }

        [Test]
        public void StoryWordsNameNoTechnology()
        {
            foreach (string c in StoryContent.DefaultCaptions) AssertNoVendor(c);
        }

        // ---------------------------------------------------------------- DG-28 attract mode

        [Test]
        public void IdleTwentySecondsOnTheTitleEntersTheTour()
        {
            var clock = new AttractClock();
            Assert.AreEqual(20.0, AttractMode.IdleSeconds);
            for (int f = 1; f < 600; f++)
                Assert.AreEqual(AttractClock.Change.None, clock.Step(Frame, true, false), "entered early at frame " + f);
            Assert.AreEqual(AttractClock.Change.Enter, clock.Step(Frame, true, false));
            Assert.IsTrue(clock.Touring);
        }

        [Test]
        public void AnyInputExitsTheTourOnTheSameFrame()
        {
            var clock = new AttractClock();
            clock.ForceEnter();
            Assert.AreEqual(AttractClock.Change.None, clock.Step(Frame, true, false));
            Assert.AreEqual(AttractClock.Change.Exit, clock.Step(Frame, true, true));
            Assert.IsFalse(clock.Touring);
            Assert.AreEqual(0.0, clock.Idle);
        }

        [Test]
        public void InputOrLeavingTheTitleRestartsTheIdleClock()
        {
            var clock = new AttractClock();
            for (int f = 0; f < 590; f++) clock.Step(Frame, true, false);
            clock.Step(Frame, true, true);
            for (int f = 0; f < 590; f++) Assert.AreEqual(AttractClock.Change.None, clock.Step(Frame, true, false));
            clock.Step(Frame, false, false);
            Assert.AreEqual(0.0, clock.Idle);
            for (int f = 0; f < 600; f++) Assert.AreEqual(AttractClock.Change.None, clock.Step(Frame, false, false), "never off the title");
            clock.ForceEnter();
            Assert.AreEqual(AttractClock.Change.Exit, clock.Step(Frame, false, false), "leaving the title ends the tour");
        }

        [Test]
        public void ShotPoseLooksAtItsTargetAndHoldsStillWithReducedMotion()
        {
            var pivot = new Vector3(56f, 2f, -40f);
            for (int i = 0; i < 5; i++)
            {
                AttractMode.ShotPose(pivot, 45.0, i, 0.3f, false, out Vector3 pos, out Quaternion rot);
                Vector3 toLook = (pivot + Vector3.up * 1.4f - pos).normalized;
                Assert.Greater(Vector3.Dot(rot * Vector3.forward, toLook), 0.9999f, "shot " + i + " looks at its target");
                Assert.Greater(pos.y, pivot.y, "shot " + i + " is above its target");
                AttractMode.ShotPose(pivot, 45.0, i, 0f, true, out Vector3 a, out Quaternion ra);
                AttractMode.ShotPose(pivot, 45.0, i, 1f, true, out Vector3 b, out Quaternion rb);
                Assert.Less((a - b).magnitude, 1e-4f, "reduced motion: shot " + i + " does not move");
                Assert.Less(Quaternion.Angle(ra, rb), 1e-3f);
            }
        }

        [Test]
        public void KaijuCaptionFollowsItsState()
        {
            var seen = new HashSet<string>();
            foreach (KaijuState s in Enum.GetValues(typeof(KaijuState)))
            {
                AttractMode.KaijuCaption(s, out string title, out string line, out Color _);
                Assert.IsFalse(string.IsNullOrEmpty(title));
                Assert.IsTrue(seen.Add(line), "each state has its own line: " + s);
                AssertNoVendor(title + " " + line);
            }
        }

        // ---------------------------------------------------------------- DG-29 credits

        const string SampleCredits =
            "# Sound effects credits\n\n## Original credits (public/sfx/v1)\n\nEvery clip here is **CC0 1.0**.\n\n" +
            "## Pack One\n\n- Page: https://www.example.org/assets/one/\n- Licence: CC0 1.0 (page + License.txt)\n" +
            "- Zip sha256: `abc123`\n- Files (2): `a_000.ogg`, `b_001.ogg`\n\n" +
            "## Pack Two\n\n- Page: http://example.net/two\n- License: CC0 1.0\n- Files (1): `c.ogg`\n";

        [Test]
        public void SoundCreditsParseIntoPacks()
        {
            List<SoundPack> packs = CreditsContent.ParseSoundCredits(SampleCredits);
            Assert.AreEqual(2, packs.Count, "the introduction (no files) is not a pack");
            Assert.AreEqual("Pack One", packs[0].Name);
            Assert.AreEqual("CC0 1.0", packs[0].Licence);
            Assert.AreEqual("example.org/assets/one", packs[0].Page);
            CollectionAssert.AreEqual(new[] { "a_000.ogg", "b_001.ogg" }, packs[0].Files, "the sha256 is not a file");
            Assert.AreEqual("CC0 1.0", packs[1].Licence);
            Assert.AreEqual("example.net/two", packs[1].Page);
            Assert.AreEqual("2 sounds   ·   CC0 1.0", CreditsContent.PackDetail(packs[0]));
            Assert.AreEqual(0, CreditsContent.ParseSoundCredits(null).Count);
        }

        static readonly string AudioDir = Path.Combine(Application.dataPath, "_Game", "Audio");
        static string CreditsFile => Path.Combine(ProjectRoot, CreditsContent.SoundCreditsAssetPath);

        static List<SoundPack> RepoPacks()
        {
            if (!File.Exists(CreditsFile)) Assert.Ignore("no " + CreditsContent.SoundCreditsAssetPath + " in this tree (the sound lane adds it)");
            List<SoundPack> packs = CreditsContent.ParseSoundCredits(File.ReadAllText(CreditsFile));
            Assert.Greater(packs.Count, 0, "CREDITS.md has no pack with a Files line");
            return packs;
        }

        static bool IsAudio(string f)
        {
            string e = Path.GetExtension(f).ToLowerInvariant();
            return e == ".ogg" || e == ".wav" || e == ".mp3";
        }

        [Test]
        public void EveryCreditedSoundFileExists()
        {
            List<SoundPack> packs = RepoPacks();
            var onDisk = new HashSet<string>(StringComparer.Ordinal);
            foreach (string f in Directory.GetFiles(AudioDir, "*", SearchOption.AllDirectories))
                if (IsAudio(f)) onDisk.Add(Path.GetFileName(f));
            foreach (SoundPack p in packs)
            {
                Assert.IsFalse(string.IsNullOrEmpty(p.Licence), p.Name + " has no licence line");
                StringAssert.Contains("CC0", p.Licence, p.Name);
                foreach (string f in p.Files) Assert.IsTrue(onDisk.Contains(f), p.Name + " credits " + f + ", which is not under Assets/_Game/Audio");
            }
        }

        [Test]
        public void EverySoundLibraryFileIsCredited()
        {
            List<SoundPack> packs = RepoPacks();
            var credited = new HashSet<string>(StringComparer.Ordinal);
            foreach (SoundPack p in packs)
                foreach (string f in p.Files) credited.Add(f);
            string lib = Path.Combine(AudioDir, "Resources", "CMSfx");
            if (!Directory.Exists(lib)) Assert.Ignore("no sound library folder " + lib);
            foreach (string f in Directory.GetFiles(lib, "*", SearchOption.AllDirectories))
            {
                if (!IsAudio(f)) continue;
                Assert.IsTrue(credited.Contains(Path.GetFileName(f)), Path.GetFileName(f) + " ships but is not in CREDITS.md");
            }
        }

        [Test]
        public void CreditedPeopleAreNamedInTheRepoDocs()
        {
            string docs = string.Empty;
            foreach (string f in new[] { "README.md", "MAX-ITEMS.md", "ARCHITECTURE.md", "PARITY.md", "DECISIONS.md" })
            {
                string p = Path.Combine(ProjectRoot, f);
                if (File.Exists(p)) docs += File.ReadAllText(p) + "\n";
            }
            Assert.IsNotEmpty(docs);
            StringAssert.Contains(CreditsContent.Creator.Name, docs, "the creator");
            Assert.AreEqual("Travis", CreditsContent.Creator.Name);
            foreach (CreditLine c in CreditsContent.Team)
            {
                StringAssert.Contains(c.Name, docs, "not in the repo docs: " + c.Name);
                Assert.IsFalse(c.Name.Contains(" "), "first names only: " + c.Name);
                Assert.IsFalse(string.IsNullOrEmpty(c.Role));
            }
        }

        [Test]
        public void CreditsDescribeToolsWithoutNamingThem()
        {
            foreach (string t in CreditsContent.Tools) AssertNoVendor(t);
            foreach (CreditLine c in CreditsContent.Team) AssertNoVendor(c.Role);
            AssertNoVendor(CreditsContent.SoundIntro);
        }

        static void AssertNoVendor(string s)
        {
            string l = " " + s.ToLowerInvariant() + " ";
            foreach (string f in Forbidden)
                Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(l, "\\b" + f + "\\b"), "names '" + f + "': " + s);
        }
    }
}
