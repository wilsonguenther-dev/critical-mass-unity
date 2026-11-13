using System;
using System.IO;
using System.Linq;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// DG-39 part 1 and DG-27: the level curve, the reward maths, the saved profile (atomic write, .bak, corruption,
    /// versions), the cosmetic table and art name convention, and a build of the character panel. Every profile test
    /// works in its own temporary folder; the player's real profile is never touched.
    /// </summary>
    public sealed class ProgressionTests
    {
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cm-profile-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            Profile.UseStore(new ProfileStore(_dir), true);
        }

        [TearDown]
        public void TearDown()
        {
            Profile.UseStore(null, !Application.isBatchMode);
            try { Directory.Delete(_dir, true); } catch (Exception) { /* temp */ }
        }

        // ---- level curve ----

        [Test]
        public void LevelCurveMatchesTheDesign()
        {
            Assert.AreEqual(0, Leveling.TotalFor(1));
            Assert.AreEqual(300, Leveling.TotalFor(2));
            Assert.AreEqual(660, Leveling.TotalFor(3));
            Assert.AreEqual(1560, Leveling.TotalFor(5));
            Assert.AreEqual(4860, Leveling.TotalFor(10));
            Assert.AreEqual(33060, Leveling.TotalFor(Leveling.MaxLevel));
            for (int l = 1; l < Leveling.MaxLevel; l++)
                Assert.AreEqual(Leveling.CostToNext(l), Leveling.TotalFor(l + 1) - Leveling.TotalFor(l), "level " + l);
            Assert.AreEqual(0, Leveling.CostToNext(Leveling.MaxLevel));
        }

        [Test]
        public void LevelForIsExactAtTheBoundaries()
        {
            Assert.AreEqual(1, Leveling.LevelFor(0));
            Assert.AreEqual(1, Leveling.LevelFor(299));
            Assert.AreEqual(2, Leveling.LevelFor(300));
            Assert.AreEqual(4, Leveling.LevelFor(1559));
            Assert.AreEqual(5, Leveling.LevelFor(1560));
            Assert.AreEqual(Leveling.MaxLevel, Leveling.LevelFor(int.MaxValue));
            LevelProgress p = Leveling.Progress(450);
            Assert.AreEqual(2, p.Level);
            Assert.AreEqual(150, p.Into);
            Assert.AreEqual(360, p.Needed);
            Assert.AreEqual(150f / 360f, p.Fraction, 1e-5f);
            Assert.IsTrue(Leveling.Progress(40000).IsMax);
            Assert.AreEqual(1f, Leveling.Progress(40000).Fraction);
        }

        // ---- reward maths ----

        static MatchStatsInput Sample(MatchOutcome o = MatchOutcome.Win) => new MatchStatsInput
        {
            KnockOuts = 6, Deaths = 4, BiomassDelivered = 5, CorruptionDelivered = 2, HillSeconds = 95.7, KaijuKnockOuts = 1,
            RelicsFound = 1, CoinsPicked = 0, Outcome = o, Completed = true,
        };

        [Test]
        public void RewardsForAScriptedMatchAreExact()
        {
            MatchRewards r = RewardRules.Compute(Sample());
            // coins: 6x5 + 5x2 + 2x2 + 95/10 + 20 + 10 + win 25 = 30 + 10 + 4 + 9 + 20 + 10 + 25
            Assert.AreEqual(108, r.Coins);
            // SP: 6x25 + 5x4 + 2x4 + 95/2 + 100 + 20 + finish 50 + win 150 = 150 + 20 + 8 + 47 + 100 + 20 + 50 + 150
            Assert.AreEqual(545, r.SkillPoints);
            Assert.AreEqual(r.Coins, r.Lines.Sum(l => l.Coins));
            Assert.AreEqual(r.SkillPoints, r.Lines.Sum(l => l.SkillPoints));
            CollectionAssert.AreEqual(
                new[] { RewardSource.KnockOuts, RewardSource.Biomass, RewardSource.Corruption, RewardSource.HillTime, RewardSource.KaijuKnockOut,
                        RewardSource.Relics, RewardSource.Finish, RewardSource.Result },
                r.Lines.Select(l => l.Source).ToArray());
            Assert.AreEqual(95, r.Lines.First(l => l.Source == RewardSource.HillTime).Count);
        }

        [Test]
        public void ResultBonusFollowsTheOutcome()
        {
            Assert.AreEqual(108 - RewardRules.CoinsWin + RewardRules.CoinsDraw, RewardRules.Compute(Sample(MatchOutcome.Draw)).Coins);
            Assert.AreEqual(108 - RewardRules.CoinsWin + RewardRules.CoinsLoss, RewardRules.Compute(Sample(MatchOutcome.Loss)).Coins);
            Assert.AreEqual(545 - RewardRules.SpWin + RewardRules.SpLoss, RewardRules.Compute(Sample(MatchOutcome.Loss)).SkillPoints);
        }

        [Test]
        public void AQuitMatchPaysNothing()
        {
            MatchStatsInput s = Sample();
            s.Completed = false;
            MatchRewards r = RewardRules.Compute(s);
            Assert.AreEqual(0, r.Coins);
            Assert.AreEqual(0, r.SkillPoints);
            Assert.AreEqual(0, r.Lines.Count);
            PlayerProfile p = new PlayerProfile();
            RewardRules.Apply(r, p);
            Assert.AreEqual(0, p.MatchesPlayed);
        }

        [Test]
        public void CapsStopABrokenMatchFromMintingAFortune()
        {
            var s = new MatchStatsInput
            {
                KnockOuts = 100000, BiomassDelivered = 100000, CorruptionDelivered = 100000, HillSeconds = double.PositiveInfinity,
                KaijuKnockOuts = 999, RelicsFound = 999, CoinsPicked = 99999, Outcome = MatchOutcome.Win, Completed = true,
            };
            MatchRewards r = RewardRules.Compute(s);
            int coins = RewardRules.MaxKnockOuts * RewardRules.CoinsPerKnockOut + RewardRules.MaxDeliveredUnits * RewardRules.CoinsPerDeliveredUnit +
                        RewardRules.MaxHillSeconds / RewardRules.HillSecondsPerCoin + RewardRules.MaxKaijuKnockOuts * RewardRules.CoinsPerKaijuKnockOut +
                        RewardRules.MaxRelics * RewardRules.CoinsPerRelic + RewardRules.MaxCoinsPicked + RewardRules.CoinsWin;
            Assert.AreEqual(coins, r.Coins);
            s.HillSeconds = double.NaN;
            s.KnockOuts = -5;
            MatchRewards r2 = RewardRules.Compute(s);
            Assert.IsFalse(r2.Lines.Any(l => l.Source == RewardSource.HillTime || l.Source == RewardSource.KnockOuts));
        }

        [Test]
        public void LiveCoinsIsTheEndMathsWithoutTheResult()
        {
            MatchStatsInput s = Sample();
            int live = RewardRules.LiveCoins(s);
            Assert.AreEqual(RewardRules.Compute(s).Coins - RewardRules.CoinsWin, live);
            s.Completed = false;
            Assert.AreEqual(live, RewardRules.LiveCoins(s), "the chip counts during play");
        }

        [Test]
        public void ApplyAddsToTheProfileAndRecordsBeforeAndAfter()
        {
            var p = new PlayerProfile { SkillPoints = 200, Coins = 7 };
            MatchRewards r = RewardRules.Compute(Sample());
            RewardRules.Apply(r, p);
            Assert.AreEqual(200, r.SkillPointsBefore);
            Assert.AreEqual(745, r.SkillPointsAfter);
            Assert.AreEqual(745, p.SkillPoints);
            Assert.AreEqual(115, p.Coins);
            Assert.AreEqual(1, r.LevelBefore);
            Assert.AreEqual(3, r.LevelAfter);
            Assert.AreEqual(2, r.LevelsGained);
            Assert.AreEqual(1, p.MatchesPlayed);
            Assert.AreEqual(1, p.Wins);
            Assert.AreEqual(6, p.KnockOuts);
            Assert.AreEqual(545, p.BestMatchSkillPoints);
            p.SkillPoints = Leveling.TotalFor(Leveling.MaxLevel) - 1;
            RewardRules.Apply(RewardRules.Compute(Sample()), p);
            Assert.AreEqual(Leveling.TotalFor(Leveling.MaxLevel), p.SkillPoints, "skill points stop at the cap");
        }

        [Test]
        public void OutcomeIsSeenFromTheLocalTeam()
        {
            Assert.AreEqual(MatchOutcome.Win, MatchRewardTracker.OutcomeFor(MatchResult.Azure, TeamId.Azure));
            Assert.AreEqual(MatchOutcome.Loss, MatchRewardTracker.OutcomeFor(MatchResult.Azure, TeamId.Crimson));
            Assert.AreEqual(MatchOutcome.Win, MatchRewardTracker.OutcomeFor(MatchResult.Crimson, TeamId.Crimson));
            Assert.AreEqual(MatchOutcome.Draw, MatchRewardTracker.OutcomeFor(MatchResult.Draw, TeamId.Azure));
        }

        // ---- profile save ----

        [Test]
        public void ProfileRoundTripsThroughDisk()
        {
            var store = new ProfileStore(_dir);
            PlayerProfile p = store.Load();
            Assert.AreEqual(ProfileLoadSource.New, store.LastLoadSource);
            p.SkillPoints = 1234;
            p.Coins = 56;
            p.PlayerName = "moss warden";
            p.Loadout(ClassId.Miner).Head = "DrillHelm";
            p.AddRelic("relic-03");
            Assert.IsTrue(store.Save(p));
            Assert.IsTrue(File.Exists(store.MainPath));
            Assert.IsFalse(File.Exists(store.TempPath), "no temp file left behind");
            PlayerProfile q = new ProfileStore(_dir).Load();
            Assert.AreEqual(1234, q.SkillPoints);
            Assert.AreEqual(56, q.Coins);
            Assert.AreEqual("MOSS WARDEN", q.PlayerName);
            Assert.AreEqual("DrillHelm", q.Loadout(ClassId.Miner).Head);
            Assert.IsTrue(q.HasRelic("relic-03"));
            StringAssert.Contains("\"Version\": " + PlayerProfile.CurrentVersion, File.ReadAllText(store.MainPath));
        }

        [Test]
        public void SecondSaveKeepsThePreviousFileAsBackup()
        {
            var store = new ProfileStore(_dir);
            store.Save(new PlayerProfile { Coins = 1 });
            store.Save(new PlayerProfile { Coins = 2 });
            Assert.IsTrue(File.Exists(store.BackupPath));
            Assert.IsTrue(ProfileStore.TryRead(store.BackupPath, out PlayerProfile bak));
            Assert.AreEqual(1, bak.Coins);
            Assert.AreEqual(2, store.Load().Coins);
        }

        [Test]
        public void CorruptMainFileFallsBackToTheBackup()
        {
            var store = new ProfileStore(_dir);
            store.Save(new PlayerProfile { Coins = 10 });
            store.Save(new PlayerProfile { Coins = 20 });
            File.WriteAllText(store.MainPath, "{\"Version\":1,\"Coins\":3");   // truncated mid-write
            PlayerProfile p = store.Load();
            Assert.AreEqual(ProfileLoadSource.Backup, store.LastLoadSource);
            Assert.AreEqual(10, p.Coins);
            // Saving over the corrupt main must not push the good backup out.
            p.Coins = 11;
            Assert.IsTrue(store.Save(p));
            Assert.IsTrue(ProfileStore.TryRead(store.BackupPath, out PlayerProfile bak));
            Assert.AreEqual(10, bak.Coins);
            Assert.IsTrue(File.Exists(store.CorruptPath));
            Assert.AreEqual(11, store.Load().Coins);
        }

        [Test]
        public void BothFilesCorruptStartsFreshAndKeepsTheBadFile()
        {
            var store = new ProfileStore(_dir);
            File.WriteAllText(store.MainPath, "garbage \u0000\u0001");
            File.WriteAllText(store.BackupPath, "");
            PlayerProfile p = store.Load();
            Assert.AreEqual(ProfileLoadSource.ResetAfterCorruption, store.LastLoadSource);
            Assert.AreEqual(0, p.SkillPoints);
            Assert.AreEqual(PlayerProfile.DefaultName, p.PlayerName);
            Assert.IsTrue(File.Exists(store.CorruptPath));
        }

        [Test]
        public void AFutureSchemaIsNotReadAsCurrent()
        {
            Assert.IsFalse(ProfileStore.TryParse("{\"Version\": " + (PlayerProfile.CurrentVersion + 1) + ", \"Coins\": 5}", out _));
            Assert.IsFalse(ProfileStore.TryParse("{\"Version\": 0}", out _));
            Assert.IsFalse(ProfileStore.TryParse("[1,2]", out _));
            Assert.IsTrue(ProfileStore.TryParse("{\"Version\": 1, \"Coins\": 5}", out PlayerProfile p));
            Assert.AreEqual(5, p.Coins);
        }

        [Test]
        public void SanitizeRepairsHandEdits()
        {
            Assert.IsTrue(ProfileStore.TryParse(
                "{\"Version\":1,\"Coins\":-9,\"SkillPoints\":-1,\"PlayerName\":\"  <b>x</b>!!  \",\"RelicsFound\":[\"a\",\"a\",\"\"]," +
                "\"Loadouts\":[{\"Class\":\"Farmer\",\"Head\":\"StrawHat\"},{\"Class\":\"Farmer\",\"Head\":\"Other\"},{\"Class\":\"Wizard\"}]}",
                out PlayerProfile p));
            Assert.AreEqual(0, p.Coins);
            Assert.AreEqual(0, p.SkillPoints);
            Assert.AreEqual("BXB", p.PlayerName);
            CollectionAssert.AreEqual(new[] { "a" }, p.RelicsFound);
            Assert.AreEqual(1, p.Loadouts.Length);
            Assert.AreEqual("StrawHat", p.Loadout(ClassId.Farmer).Head);
        }

        [Test]
        public void NamesAreCleaned()
        {
            Assert.AreEqual(PlayerProfile.DefaultName, PlayerProfile.CleanName(null));
            Assert.AreEqual(PlayerProfile.DefaultName, PlayerProfile.CleanName("!!!"));
            Assert.AreEqual("IRON KEEPER", PlayerProfile.CleanName("iron   keeper "));
            Assert.AreEqual(PlayerProfile.MaxNameLength, PlayerProfile.CleanName(new string('a', 40)).Length);
        }

        [Test]
        public void CommitWritesTheCurrentProfileToTheInstalledStore()
        {
            Profile.Current.Coins = 77;
            int changed = 0;
            Action h = () => changed++;
            Profile.Changed += h;
            try { Assert.IsTrue(Profile.Commit()); }
            finally { Profile.Changed -= h; }
            Assert.AreEqual(1, changed);
            Assert.AreEqual(77, new ProfileStore(_dir).Load().Coins);
        }

        // ---- cosmetics ----

        [Test]
        public void EveryClassHasThreeHeadgearSixEmblemsThreeEmotes()
        {
            foreach (ClassId c in ClassIds.All)
            {
                CollectionAssert.AreEqual(CosmeticItems.HeadLevels, CosmeticItems.Table(c, CosmeticSlot.Head).Select(i => i.UnlockLevel).ToArray());
                CollectionAssert.AreEqual(CosmeticItems.EmblemLevels, CosmeticItems.Table(c, CosmeticSlot.Emblem).Select(i => i.UnlockLevel).ToArray());
                CollectionAssert.AreEqual(CosmeticItems.EmoteLevels, CosmeticItems.Table(c, CosmeticSlot.Emote).Select(i => i.UnlockLevel).ToArray());
                Assert.AreEqual(1, CosmeticItems.Default(c, CosmeticSlot.Head).UnlockLevel);
            }
            Assert.AreEqual("Cheer", CosmeticItems.Default(ClassId.Warrior, CosmeticSlot.Emote).Id, "the level-1 emote is the clip the KayKit set ships");
        }

        [Test]
        public void ArtFilesNotInTheTableAppearAsExtras()
        {
            var list = CosmeticItems.WithExtras(ClassId.Warrior, CosmeticSlot.Head, new[] { "PirateHat", "HornedHelm", "AcornCap", "PirateHat" });
            Assert.AreEqual(5, list.Count);
            Assert.AreEqual("AcornCap", list[3].Id);
            Assert.AreEqual("PirateHat", list[4].Id);
            Assert.IsTrue(list[4].Extra);
            Assert.AreEqual(CosmeticItems.ExtraItemLevel, list[4].UnlockLevel);
            Assert.AreEqual("PIRATE HAT", list[4].DisplayName);
        }

        [Test]
        public void DisplayNamesSplitWords()
        {
            Assert.AreEqual("HORNED HELM", CosmeticItems.DisplayName("HornedHelm"));
            Assert.AreEqual("HILL CROWN", CosmeticItems.DisplayName("hill_crown"));
            Assert.AreEqual("MK2 HELM", CosmeticItems.DisplayName("Mk2Helm"));
            Assert.AreEqual(string.Empty, CosmeticItems.DisplayName(null));
        }

        [Test]
        public void ArtPathConvention()
        {
            Assert.IsTrue(Cosmetics.TryParsePath("Assets/_Game/Art/Cosmetics/Warrior/Head_HornedHelm.fbx", out ClassId c, out string slot, out string name));
            Assert.AreEqual(ClassId.Warrior, c);
            Assert.AreEqual("Head", slot);
            Assert.AreEqual("HornedHelm", name);
            Assert.IsTrue(Cosmetics.TryParsePath("Assets/_Game/Art/Cosmetics/Miner/Emblem_Sunburst.png", out c, out slot, out name));
            Assert.AreEqual(ClassId.Miner, c);
            Assert.AreEqual("Emblem", slot);
            Assert.IsTrue(Cosmetics.TryParsePath(Cosmetics.PathFor(ClassId.Farmer, Cosmetics.MountSlot, Cosmetics.MountName, "fbx"), out _, out slot, out name));
            Assert.AreEqual("Mount", slot);
            Assert.AreEqual("Emblem", name);
            Assert.IsFalse(Cosmetics.TryParsePath("Assets/_Game/Art/Cosmetics/Wizard/Head_X.fbx", out _, out _, out _), "unknown class");
            Assert.IsFalse(Cosmetics.TryParsePath("Assets/_Game/Art/Cosmetics/Warrior/Head_X.png", out _, out _, out _), "head must be a mesh");
            Assert.IsFalse(Cosmetics.TryParsePath("Assets/_Game/Art/Cosmetics/Warrior/Emblem_X.fbx", out _, out _, out _), "emblem must be a texture");
            Assert.IsFalse(Cosmetics.TryParsePath("Assets/_Game/Art/Cosmetics/Warrior/Head_.fbx", out _, out _, out _));
            Assert.IsFalse(Cosmetics.TryParsePath("Assets/_Game/Art/Cosmetics/Warrior/Sub/Head_X.fbx", out _, out _, out _));
            Assert.IsFalse(Cosmetics.TryParsePath("Assets/_Game/Art/Characters/Head_X.fbx", out _, out _, out _));
        }

        [Test]
        public void LockedOrMissingItemsResolveToNothing()
        {
            Assert.IsNull(Cosmetics.Resolve(ClassId.Warrior, CosmeticSlot.Head, "CrestedHelm", 1), "level 6 item at level 1");
            Assert.IsNull(Cosmetics.Resolve(ClassId.Builder, CosmeticSlot.Head, "HardHat", 30), "no Builder art exists");
            Assert.IsNull(Cosmetics.Resolve(ClassId.Warrior, CosmeticSlot.Head, string.Empty, 30), "bare head");
        }

        [Test]
        public void UnlockTextsNameTheNextPieces()
        {
            StringAssert.StartsWith("LEVEL 2: SUNBURST", CharacterPanel.NextUnlockText(ClassId.Warrior, 1));
            StringAssert.StartsWith("LEVEL 3: HORNED HELM", CharacterPanel.NextUnlockText(ClassId.Warrior, 2));
            Assert.AreEqual(ProgressionWords.NothingNext, CharacterPanel.NextUnlockText(ClassId.Warrior, Leveling.MaxLevel));
            StringAssert.Contains("HORNED HELM", RewardsPanel.UnlockLine(3, 3));
            Assert.AreEqual(string.Empty, RewardsPanel.UnlockLine(11, 12));
        }

        [Test]
        public void AbilityTextsReadTheSimConfig()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            StringAssert.Contains("2.5 s", ProgressionWords.AbilityText(cfg, ClassId.Warrior));
            StringAssert.Contains("60%", ProgressionWords.AbilityText(cfg, ClassId.Warrior));
            StringAssert.Contains("6 m", ProgressionWords.AbilityText(cfg, ClassId.Farmer));
            StringAssert.Contains("22 damage", ProgressionWords.AttackText(cfg, ClassId.Warrior));
        }

        // ---- character panel build ----

        [Test]
        public void CharacterPanelBuildsAndShowsTheSkillsTab()
        {
            Profile.Current.SkillPoints = 450;
            Profile.Current.Coins = 1234;
            GameObject host = null;
            try
            {
                CharacterPanel.Show(CharacterPanel.Tab.Skills, true, ClassId.Farmer);
                host = CharacterPanel.Instance.gameObject;
                Assert.IsTrue(CharacterPanel.IsOpen);
                Assert.IsTrue(CharacterPanel.Editable, "no live match: the Barracks rules apply");
                Assert.AreEqual(ClassId.Farmer, CharacterPanel.Instance.ShownClass);
                Assert.AreEqual(CharacterPanel.Tab.Skills, CharacterPanel.Instance.CurrentTab);
                Text[] texts = host.GetComponentsInChildren<Text>(true);
                Assert.IsTrue(texts.Any(t => t.text == "FARMER"));
                Assert.IsTrue(texts.Any(t => t.text == "1,234 COINS"));
                Assert.IsTrue(texts.Any(t => t.text == "150 / 360 SP"));
                Assert.IsTrue(texts.Any(t => t.text == ProgressionWords.TeamColoursLocked));
                CharacterPanel.Hide();
                Assert.IsFalse(CharacterPanel.IsOpen);
            }
            finally
            {
                if (host != null) UnityEngine.Object.DestroyImmediate(host);
            }
        }
    }
}
