using System;
using System.IO;
using System.Linq;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 profile: schema 2 and its migration from REAL schema-1 files (ProfileFixtures), the old-file backup, corrupt
    /// file recovery, the achievements import from PlayerPrefs, and the v2 parts round-tripping through disk. Every test
    /// works in its own temporary folder and stubs the PlayerPrefs reader; the player's real profile is never touched.
    /// </summary>
    public sealed class ProfileV2Tests
    {
        string _dir;
        Func<string, bool> _legacy;

        static void P(string s) => Debug.Log("[b15-profile] " + s);

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cm-profile-v2-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _legacy = ProfileStore.LegacyAchievement;
            ProfileStore.LegacyAchievement = id => ProfileFixtures.V1PlayerPrefsAchievements.Contains(id);
            Profile.UseStore(new ProfileStore(_dir), true);
        }

        [TearDown]
        public void TearDown()
        {
            ProfileStore.LegacyAchievement = _legacy;
            Profile.UseStore(null, !Application.isBatchMode);
            try { Directory.Delete(_dir, true); } catch (Exception) { /* temp */ }
        }

        ProfileStore StoreWith(string main, string bak = null)
        {
            var store = new ProfileStore(_dir);
            if (main != null) File.WriteAllText(store.MainPath, main);
            if (bak != null) File.WriteAllText(store.BackupPath, bak);
            return store;
        }

        [Test]
        public void TheRealV1ProfileMigratesWithEveryFieldKept()
        {
            ProfileStore store = StoreWith(ProfileFixtures.V1Main);
            PlayerProfile p = store.Load();
            P($"v1 main: source {store.LastLoadSource}, from version {store.LastLoadedVersion}, now {p.Version}; sp {p.SkillPoints} coins {p.Coins} matches {p.MatchesPlayed} W/D/L {p.Wins}/{p.Draws}/{p.Losses} ko {p.KnockOuts} best {p.BestMatchSkillPoints} relics {p.RelicsFound.Length} loadouts {p.Loadouts.Length}");
            Assert.AreEqual(ProfileLoadSource.Main, store.LastLoadSource);
            Assert.AreEqual(1, store.LastLoadedVersion);
            Assert.AreEqual(2, PlayerProfile.CurrentVersion);
            Assert.AreEqual(PlayerProfile.CurrentVersion, p.Version);
            Assert.AreEqual(PlayerProfile.CleanName("BOOB"), p.PlayerName);
            Assert.AreEqual(33060, p.SkillPoints);
            Assert.AreEqual(30, p.Level);
            Assert.AreEqual(8318, p.Coins);
            Assert.AreEqual(65, p.MatchesPlayed);
            Assert.AreEqual(28, p.Wins);
            Assert.AreEqual(0, p.Draws);
            Assert.AreEqual(37, p.Losses);
            Assert.AreEqual(1239, p.KnockOuts);
            Assert.AreEqual(1219, p.BestMatchSkillPoints);
            CollectionAssert.AreEquivalent(new[] { "relic-05", "relic-07", "relic-03", "relic-08" }, p.RelicsFound);
            Assert.AreEqual("Cheer", p.Loadout(ClassId.Warrior).Emote);
            Assert.AreEqual(0, p.Loadout(ClassId.Warrior).Title, "new look ids start at the default");
            Assert.AreEqual(0, p.History.Length, "v1 kept no history: nothing is invented");
            Assert.AreEqual(1, p.MasteryOf(ClassId.Warrior).Level, "v1 never recorded per-class play: mastery starts at 1");
        }

        [Test]
        public void MigrationImportsThePlayerPrefsAchievementsAndKeepsTheKeys()
        {
            PlayerProfile p = StoreWith(ProfileFixtures.V1Main).Load();
            string[] unlocked = Achievements.All.Where(d => p.HasAchievement(d.Id)).Select(d => d.Id).ToArray();
            P("imported: " + string.Join(", ", unlocked) + " (" + unlocked.Length + " of " + Achievements.All.Length + ")");
            CollectionAssert.AreEquivalent(ProfileFixtures.V1PlayerPrefsAchievements, unlocked);
            // the in-match store now reads the profile
            var store = new ProfileAchievementStore();
            Profile.UseStore(new ProfileStore(_dir), true);
            Assert.IsTrue(store.Has("payback"));
            Assert.IsFalse(store.Has("giant-slayer"));
            store.Set("giant-slayer");
            Assert.IsTrue(Profile.Current.HasAchievement("giant-slayer"));
            Assert.IsNotEmpty(Profile.Current.Achievement("giant-slayer").UnlockedUtc, "an unlock is stamped from the clock");
        }

        [Test]
        public void TheOldFileIsKeptOnceAndNeverOverwritten()
        {
            ProfileStore store = StoreWith(ProfileFixtures.V1Main);
            PlayerProfile p = store.Load();
            string keep = store.VersionBackupPath(1);
            Assert.IsTrue(File.Exists(keep), "the v1 file is kept beside the profile");
            Assert.AreEqual(ProfileFixtures.V1Main, File.ReadAllText(keep), "byte for byte");
            p.Coins = 1;
            Assert.IsTrue(store.Save(p));
            Assert.IsTrue(store.Save(p));
            Assert.AreEqual(ProfileFixtures.V1Main, File.ReadAllText(keep), "later saves never touch it");
            Assert.IsFalse(File.Exists(store.TempPath), "atomic write leaves no temp file");
            StringAssert.Contains("\"Version\": 2", File.ReadAllText(store.MainPath));
            PlayerProfile q = new ProfileStore(_dir).Load();
            Assert.AreEqual(2, new ProfileStore(_dir).Load().Version);
            Assert.AreEqual(1, q.Coins);
            P($"kept {Path.GetFileName(keep)} ({new FileInfo(keep).Length} B); main now v2; .bak is the v1 main: {ProfileStore.TryRead(store.BackupPath, out _, out int bv)} v{bv}");
        }

        [Test]
        public void ATruncatedMainFileRecoversFromTheRealBackup()
        {
            ProfileStore store = StoreWith(ProfileFixtures.V1Truncated, ProfileFixtures.V1Backup);
            PlayerProfile p = store.Load();
            P($"truncated main + v1 bak: source {store.LastLoadSource}, matches {p.MatchesPlayed}, coins {p.Coins}");
            Assert.AreEqual(ProfileLoadSource.Backup, store.LastLoadSource);
            Assert.AreEqual(64, p.MatchesPlayed, "the backup is one match older");
            Assert.AreEqual(8256, p.Coins);
            Assert.AreEqual(2, p.Version);
            Assert.IsTrue(store.Save(p), "saving over a corrupt main works");
            Assert.IsTrue(File.Exists(store.CorruptPath), "the bad file is kept for diagnosis");
            Assert.AreEqual(64, new ProfileStore(_dir).Load().MatchesPlayed);
        }

        [Test]
        public void BothFilesCorruptStartsFreshAndKeepsTheBadFile()
        {
            ProfileStore store = StoreWith(ProfileFixtures.V1Truncated, "{\"Version\":1,\"Coi");
            PlayerProfile p = store.Load();
            P($"both corrupt: source {store.LastLoadSource}, coins {p.Coins}, corrupt kept {File.Exists(store.CorruptPath)}");
            Assert.AreEqual(ProfileLoadSource.ResetAfterCorruption, store.LastLoadSource);
            Assert.AreEqual(0, p.Coins);
            Assert.IsTrue(File.Exists(store.CorruptPath));
            Assert.AreEqual(ProfileFixtures.V1Truncated, File.ReadAllText(store.CorruptPath));
        }

        [Test]
        public void AFutureSchemaIsRefusedAndGarbageNeverThrows()
        {
            Assert.IsFalse(ProfileStore.TryParse("{\"Version\": 3, \"Coins\": 5}", out _), "a later build's file is not read as this one");
            foreach (string bad in new[] { "", "   ", "null", "{", "}", "[]", "{\"Version\":\"x\"}", "{\"Version\":1,\"History\":7}", "\u0000\u0001" })
            {
                bool ok = ProfileStore.TryParse(bad, out PlayerProfile p);
                P($"garbage {bad.Length} chars: parsed {ok}");
                if (ok) Assert.IsNotNull(p);
            }
        }

        [Test]
        public void V2PartsRoundTripThroughDisk()
        {
            var store = new ProfileStore(_dir);
            PlayerProfile p = store.Load();
            ClassMastery m = p.MasteryOf(ClassId.Healer);
            m.Xp = MasteryRules.TotalFor(11) + 5;
            m.Matches = 9;
            m.Healing = 4321;
            ClassLoadout l = p.Loadout(ClassId.Healer);
            l.Title = 4;   // Lifesaver at 11
            l.Frame = 2;   // Silver at 10
            l.Trim = 2;    // Slate at 8
            l.Badge = 3;   // Shield at 9
            for (int i = 0; i < 60; i++)
                MatchHistory.Add(p, new MatchRecord { Class = "Healer", DateUtc = MatchHistory.Stamp(new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(i)), Score = i, ReplayFile = "../../evil/" + i + ".cmreplay" });
            p.Achievement("hill-holder").Best = 42;
            Assert.IsTrue(store.Save(p));
            PlayerProfile q = new ProfileStore(_dir).Load();
            P($"round trip: healer xp {q.MasteryOf(ClassId.Healer).Xp} level {q.MasteryOf(ClassId.Healer).Level}, look {q.Loadout(ClassId.Healer).Title}/{q.Loadout(ClassId.Healer).Frame}/{q.Loadout(ClassId.Healer).Trim}/{q.Loadout(ClassId.Healer).Badge}, history {q.History.Length} newest score {q.History[0].Score}, replay '{q.History[0].ReplayFile}'");
            Assert.AreEqual(11, q.MasteryOf(ClassId.Healer).Level);
            Assert.AreEqual(4321, q.MasteryOf(ClassId.Healer).Healing);
            Assert.AreEqual(4, q.Loadout(ClassId.Healer).Title);
            Assert.AreEqual(2, q.Loadout(ClassId.Healer).Frame);
            Assert.AreEqual(2, q.Loadout(ClassId.Healer).Trim);
            Assert.AreEqual(3, q.Loadout(ClassId.Healer).Badge);
            Assert.AreEqual(MatchHistory.Keep, q.History.Length, "the newest 50 are kept");
            Assert.AreEqual(59, q.History[0].Score, "newest first");
            Assert.AreEqual("59.cmreplay", q.History[0].ReplayFile, "a stored path can never leave the replay folder");
            Assert.AreEqual(42, q.Achievement("hill-holder").Best);
        }

        [Test]
        public void AHandEditedLookAboveTheMasteryIsClearedOnLoad()
        {
            var store = new ProfileStore(_dir);
            PlayerProfile p = store.Load();
            p.MasteryOf(ClassId.Miner).Xp = MasteryRules.TotalFor(4);
            Assert.IsTrue(store.Save(p));
            string json = File.ReadAllText(store.MainPath);
            // a hand edit: a level-20 crown frame and a title id that does not exist, on a mastery-4 Miner
            json = json.Replace("\"Loadouts\": [", "\"Loadouts\": [ { \"Class\": \"Miner\", \"Title\": 99, \"Frame\": 4, \"Trim\": 1, \"Badge\": 2 },");
            File.WriteAllText(store.MainPath, json);
            PlayerProfile q = new ProfileStore(_dir).Load();
            ClassLoadout l = q.Loadout(ClassId.Miner);
            P($"hand edit on mastery {q.MasteryOf(ClassId.Miner).Level}: title {l.Title} frame {l.Frame} trim {l.Trim} badge {l.Badge}");
            Assert.AreEqual(0, l.Title);
            Assert.AreEqual(0, l.Frame);
            Assert.AreEqual(1, l.Trim, "trim 1 unlocks at 4: kept");
            Assert.AreEqual(0, l.Badge, "emblem 2 unlocks at 6: cleared on a mastery-4 class");
        }
    }
}
