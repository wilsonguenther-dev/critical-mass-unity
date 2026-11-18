using System;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menus;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>b15 safe defaults, item 4: the local feedback file (one JSON object per line, nothing sent anywhere).</summary>
    public sealed class FeedbackLogTests
    {
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cm-feedback-" + Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            FeedbackCard.FolderOverride = null;
            FeedbackCard.ResetMarkersForTests();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        static MatchFeedback Sample() => new MatchFeedback
        {
            Utc = new DateTime(2026, 11, 17, 21, 5, 9, DateTimeKind.Utc), Build = "0.13.2", Commit = "abc1234", Class = "Builder",
            Team = "Azure", Result = FeedbackLog.Result(1, 0), MatchSeconds = 312.25, Seed = 42, Replay = "cm-20261117-42.cmreplay",
            Frames = new FrameSummary { Count = 9000, Over33 = 3, P50 = 8.5, P95 = 12, P99 = 20.5, Max = 61.25 },
            Shown = true, Fun = 4, Note = "the \"wall\" ate my\nhammer",
        };

        [Test]
        public void AMatchLineHoldsEveryFieldOnOneLine()
        {
            MatchFeedback m = Sample();
            m.FeltBad.Add("controls");
            m.FeltBad.Add("not-a-chip");
            m.FeltBad.Add("performance");
            string line = FeedbackLog.MatchLine(m);
            TestContext.WriteLine(line);
            Assert.AreEqual(-1, line.IndexOf('\n'), "one line");
            StringAssert.StartsWith("{\"type\":\"match\",\"v\":1,\"utc\":\"2026-11-17T21:05:09Z\"", line);
            foreach (string part in new[]
                     {
                         "\"build\":\"0.13.2\"", "\"class\":\"Builder\"", "\"team\":\"Azure\"", "\"result\":\"win\"", "\"match_seconds\":312.25",
                         "\"seed\":42", "\"replay\":\"cm-20261117-42.cmreplay\"", "\"count\":9000", "\"p50_ms\":8.5", "\"p95_ms\":12",
                         "\"p99_ms\":20.5", "\"max_ms\":61.25", "\"over_33ms\":3", "\"shown\":true", "\"skipped\":false", "\"fun\":4",
                         "\"felt_bad\":[\"controls\",\"performance\"]", "\"note\":\"the \\\"wall\\\" ate my hammer\"",
                     })
                StringAssert.Contains(part, line);
            StringAssert.EndsWith("}", line);
        }

        [Test]
        public void ASkippedLineHasNoAnswers()
        {
            MatchFeedback m = Sample();
            m.Fun = 0;
            m.Note = null;
            m.Skipped = true;
            m.Shown = false;
            m.Replay = null;
            string line = FeedbackLog.MatchLine(m);
            TestContext.WriteLine(line);
            foreach (string part in new[] { "\"fun\":null", "\"felt_bad\":[]", "\"note\":\"\"", "\"skipped\":true", "\"shown\":false", "\"replay\":null" })
                StringAssert.Contains(part, line);
        }

        [Test]
        public void ResultsReadFromTheLocalTeam()
        {
            Assert.AreEqual("win", FeedbackLog.Result(1, 0));
            Assert.AreEqual("loss", FeedbackLog.Result(2, 0));
            Assert.AreEqual("win", FeedbackLog.Result(2, 1));
            Assert.AreEqual("loss", FeedbackLog.Result(1, 1));
            Assert.AreEqual("draw", FeedbackLog.Result(3, 1));
            Assert.AreEqual("unfinished", FeedbackLog.Result(0, 0));
        }

        [Test]
        public void TheNoteIsOneShortLine()
        {
            Assert.AreEqual("a b", FeedbackLog.CleanNote("  a\r\nb  "));
            Assert.AreEqual(FeedbackLog.NoteMax, FeedbackLog.CleanNote(new string('x', 500)).Length);
            Assert.AreEqual("", FeedbackLog.CleanNote(null));
            Assert.AreEqual("\"\\u0001\"", FeedbackLog.Quote("\u0001"));
        }

        [Test]
        public void TheHistogramReportsPercentilesWithinHalfAMillisecond()
        {
            var h = new FrameHistogram();
            Assert.IsTrue(double.IsNaN(h.Summary().P50), "no frames, no numbers");
            for (int i = 0; i < 1000; i++) h.Add(i < 950 ? 8.2 : i < 990 ? 16.4 : 40.0);
            h.Add(400);   // one hitch past the last bucket
            FrameSummary s = h.Summary();
            TestContext.WriteLine("frames " + s.Count + " p50 " + s.P50 + " p95 " + s.P95 + " p99 " + s.P99 + " max " + s.Max + " over33 " + s.Over33);
            Assert.AreEqual(1001, s.Count);
            Assert.AreEqual(8.5, s.P50, 1e-9);
            Assert.AreEqual(16.5, s.P95, 1e-9);
            Assert.AreEqual(40.0, s.P99, 0.5);
            Assert.AreEqual(400.0, s.Max, 1e-9);
            Assert.AreEqual(11, s.Over33);
            h.Clear();
            Assert.AreEqual(0, h.Count);
        }

        [Test]
        public void LinesAppendToTheFileNextToTheReplays()
        {
            string path = FeedbackLog.PathIn(_dir);
            Assert.AreEqual(FeedbackLog.FileName, Path.GetFileName(path));
            Assert.IsTrue(FeedbackLog.Append(path, FeedbackLog.MatchLine(Sample()), out string e1), e1);
            var fun = new FirstFun { FirstMatch = 41.5, FirstHit = 63.25 };
            Assert.IsTrue(FeedbackLog.Append(path, FeedbackLog.FirstFunLine(fun, DateTime.UtcNow, "0.13.2"), out string e2), e2);
            string[] lines = File.ReadAllLines(path);
            foreach (string l in lines) TestContext.WriteLine(l);
            Assert.AreEqual(2, lines.Length);
            StringAssert.Contains("\"to_first_match_s\":41.5", lines[1]);
            StringAssert.Contains("\"to_first_hit_s\":63.25", lines[1]);
            StringAssert.Contains("\"to_first_objective_s\":null", lines[1]);
            Assert.AreEqual(3, FeedbackCard.MarkerBits(fun));
            Assert.IsFalse(fun.Complete);

            // The replay folder's pruning only touches replay files: the feedback file is safe there.
            Assert.IsFalse(FeedbackLog.FileName.EndsWith(CriticalMass.Runtime.Replay.ReplayStore.Extension, StringComparison.OrdinalIgnoreCase));
        }

        [Test]
        public void OnlyAMatchAPersonPlaysIsLogged()
        {
            Assert.IsTrue(FeedbackCard.HumanMatch(false, false, false));
            Assert.IsFalse(FeedbackCard.HumanMatch(true, false, false), "backdrop, capture or autoplay world");
            Assert.IsFalse(FeedbackCard.HumanMatch(false, true, false), "a replay");
            Assert.IsFalse(FeedbackCard.HumanMatch(false, false, true), "spectating");
            Assert.AreEqual(6, FeedbackLog.ChipIds.Length);
            CollectionAssert.AreEqual(new[] { "fighting", "controls", "confusing", "performance", "bug", "other" }, FeedbackLog.ChipIds);
            StringAssert.Contains("stay on your computer", FeedbackCard.Privacy);
        }
    }
}
