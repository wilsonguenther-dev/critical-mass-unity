using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using CriticalMass.Net.Replay;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Replay;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b13-replay: the Runtime side, pure: file names (no culture, no path pitfalls), keeping the newest 20, the list row
    /// words, the playback bar line, paging, the request a replay is shown under, and a save / list / load round trip in a
    /// temp folder. Every test prints what it measured.
    /// </summary>
    public sealed class ReplayScreenTests
    {
        [Test]
        public void FileNamesAreCultureFreeAsciiAndSortByAge()
        {
            CultureInfo keep = Thread.CurrentThread.CurrentCulture;
            try
            {
                var names = new List<string>();
                foreach (string c in new[] { "en-US", "de-DE", "ar-SA", "th-TH", "fa-IR" })
                {
                    Thread.CurrentThread.CurrentCulture = new CultureInfo(c);
                    names.Add(ReplayStore.FileName(new DateTime(2026, 11, 16, 17, 44, 5, DateTimeKind.Utc), -1234));
                }
                Console.WriteLine($"[replay] file names under 5 cultures: {string.Join(" | ", names)} (limit: all 'replay-20261116-174405-1234.cmreplay')");
                foreach (string n in names)
                {
                    Assert.AreEqual("replay-20261116-174405-1234.cmreplay", n);
                    foreach (char ch in n) Assert.IsTrue(ch < 128 && Array.IndexOf(Path.GetInvalidFileNameChars(), ch) < 0 && ch != ':', "char " + ch);
                }
            }
            finally { Thread.CurrentThread.CurrentCulture = keep; }
            string older = ReplayStore.FileName(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc), 9);
            string newer = ReplayStore.FileName(new DateTime(2026, 11, 2, 3, 4, 5, DateTimeKind.Utc), 1);
            Assert.Less(string.CompareOrdinal(older, newer), 0, "name order is age order");
        }

        [Test]
        public void OnlyTheNewestTwentyAreKept()
        {
            var names = new List<string>();
            for (int i = 0; i < 27; i++) names.Add(ReplayStore.FileName(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i * 37), i));
            names.Add("notes.txt");
            names.Reverse();   // any listing order
            List<string> del = ReplayStore.ToDelete(names, ReplayStore.Keep);
            Console.WriteLine($"[replay] keep newest {ReplayStore.Keep} of 27: delete {del.Count} (limit 7), oldest deleted {del[0]}, newest deleted {del[del.Count - 1]}");
            Assert.AreEqual(7, del.Count);
            for (int i = 0; i < 7; i++) Assert.AreEqual(ReplayStore.FileName(new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i * 37), i), del[i]);
            Assert.IsFalse(del.Contains("notes.txt"));
            Assert.AreEqual(0, ReplayStore.ToDelete(names.GetRange(0, 5), ReplayStore.Keep).Count);
        }

        static ReplayEntry Entry(MatchResult winner, bool finished, double time, ReplayCompatibility c)
        {
            var h = new ReplayHeader
            {
                RecordedUtcTicks = new DateTime(2026, 11, 16, 23, 44, 0, DateTimeKind.Utc).Ticks,
                MapName = "Skybreak Basin",
                ModeLabel = "Full battle",
                Winner = winner,
                Finished = finished,
                MatchTime = time,
                Start = new ReplayStart { TeamSize = 16 },
            };
            return new ReplayEntry { Header = h, Compat = c };
        }

        [Test]
        public void ListRowsSayDateMapModeWinnerAndLength()
        {
            string a = ReplayWords.Row(Entry(MatchResult.Azure, true, 600, ReplayCompatibility.Ok), false);
            string b = ReplayWords.Row(Entry(MatchResult.None, false, 95.7, ReplayCompatibility.OtherVersion), false);
            string c = ReplayWords.Row(new ReplayEntry { Compat = ReplayCompatibility.Damaged }, false);
            Console.WriteLine($"[replay] rows: \"{a}\" | \"{b}\" | \"{c}\"");
            Assert.AreEqual("MON 16 NOV  23:44   SKYBREAK BASIN   FULL BATTLE 16 V 16   AZURE WON   10:00", a);
            StringAssert.Contains("LEFT EARLY   1:35   OTHER VERSION", b);
            Assert.AreEqual("DAMAGED REPLAY FILE", c);
            Assert.AreEqual("0:00", ReplayWords.Length(double.NaN));
            Assert.AreEqual("DRAW", ReplayWords.Result(Entry(MatchResult.Draw, true, 1, ReplayCompatibility.Ok).Header));
        }

        [Test]
        public void ThePlaybackBarSaysStateTimeSpeedAndWhoIsFollowed()
        {
            string s = ReplayPlayback.StatusLine(false, 5760, 18000, 30, 2, false, "WILSON · WARRIOR · AZURE");
            string p = ReplayPlayback.StatusLine(true, 0, 900, 30, 0.5, true, "");
            Console.WriteLine($"[replay] bar: \"{s}\" | \"{p}\" | keys \"{ReplayPlayback.HintLine(false)}\"");
            Assert.AreEqual("PLAYING  3:12 / 10:00   2X   FOLLOWING WILSON · WARRIOR · AZURE", s);
            Assert.AreEqual("PAUSED  0:00 / 0:30   0.5X   FREE CAMERA", p);
            CollectionAssert.AreEqual(new[] { 0.5, 1, 2, 4 }, ReplayPlayback.Speeds);
            Assert.AreEqual(1, ReplaysMenuScreen.PageCount(0));
            Assert.AreEqual(1, ReplaysMenuScreen.PageCount(8));
            Assert.AreEqual(3, ReplaysMenuScreen.PageCount(20));
        }

        [Test]
        public void AReplayIsShownUnderARequestThatIsNeverRecordedAgain()
        {
            var h = new ReplayHeader
            {
                Mode = (int)MatchMode.FullBattle,
                Start = new ReplayStart { Seed = 77, TeamSize = 16, Pace = 2, LocalActor = 17 },
                Roster = new ReplayActor[32],
            };
            h.Roster[17] = new ReplayActor { Team = TeamId.Crimson, Class = ClassId.Healer, Human = true, Name = "TRAVIS" };
            MatchRequest r = ReplayPlayback.RequestFor(h);
            Console.WriteLine($"[replay] request: seed {r.Seed}, team {r.Team}, class {r.Class}, size {r.TeamSize}, pace {r.Pace}, mode {r.Mode}, replay {r.Replay}, backdrop {r.Backdrop}");
            Assert.IsTrue(r.Replay);
            Assert.IsFalse(r.Backdrop);
            Assert.AreEqual(77, r.Seed);
            Assert.AreEqual(TeamId.Crimson, r.Team);
            Assert.AreEqual(ClassId.Healer, r.Class);
            Assert.AreEqual(MatchMode.FullBattle, r.Mode);
        }

        [Test]
        public void SaveListAndLoadRoundTripInAFolder()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cm-replay-test-" + Guid.NewGuid().ToString("N"));
            ReplayStore.SetFolderForTests(dir);
            try
            {
                var s = new ReplayStart { Basis = ConfigBasis.Browser5v5, Seed = 3, TeamSize = 5, Pace = 1 };
                World w = World.CreateWorld(s.ToWorldOptions(s.BuildConfig()));
                var rec = new ReplayRecorder();
                Assert.IsTrue(rec.Begin(w, new ReplayHeader
                {
                    BuildVersion = Game.BuildInfo.Version, BuildCommit = Game.BuildInfo.Commit, Start = s, MapName = "SKYBREAK BASIN", ModeLabel = "CUSTOM",
                }));
                InputFrame scratch = SimInput.EmptyInput();
                for (int t = 0; t < 300; t++)
                {
                    rec.OnAdvance(w, null);
                    CriticalMass.Sim.Bots.MatchTick.Advance(w, null, scratch);
                }
                ReplayData d = rec.Finish(false, new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc).Ticks);
                string path = ReplayStore.SaveNow(d, dir);
                File.WriteAllBytes(Path.Combine(dir, "replay-20200101-000000-1.cmreplay"), new byte[] { 1, 2, 3 });   // a damaged file
                List<ReplayEntry> list = ReplayStore.List();
                ReplayData back = ReplayStore.Load(path, out string why);
                Console.WriteLine($"[replay] folder round trip: saved {Path.GetFileName(path)}, listed {list.Count} (limit 2), first {list[0].Compat}, " +
                                  $"second {list[1].Compat} \"{list[1].Why}\", loaded {(back != null ? back.Header.Ticks + " ticks" : "null: " + why)}");
                Assert.IsNotNull(path);
                Assert.AreEqual(2, list.Count);
                Assert.AreEqual(ReplayCompatibility.Ok, list[0].Compat, list[0].Why);
                Assert.AreEqual(ReplayCompatibility.Damaged, list[1].Compat);
                Assert.IsNotNull(back, why);
                Assert.AreEqual(300, back.Header.Ticks);
            }
            finally
            {
                ReplayStore.SetFolderForTests(null);
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }
    }
}
