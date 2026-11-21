using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 item 6 (systems audit defect 3): no menu promises a look that is not in the build. Art presence is probed
    /// through UnlockHonesty.HasArt, swapped here so the tests do not depend on which art files the CI checkout has; one
    /// test also reads the real build and logs what it finds.
    /// </summary>
    [TestFixture]
    public sealed class ProfileHygieneTests
    {
        [TearDown]
        public void Reset() => UnlockHonesty.ResetProbes();

        /// <summary>The audit's build: no headgear or emblem art, only the level-1 "Cheer" emote.</summary>
        static void AuditBuild()
        {
            UnlockHonesty.HasArt = it => it != null && it.Slot == CosmeticSlot.Emote && it.Id == "Cheer";
            UnlockHonesty.ArtIds = (c, s) => new string[0];
        }

        [Test]
        public void ALookWithNoArtIsNeverListedOrPromised()
        {
            AuditBuild();
            foreach (ClassId c in ClassIds.All)
            {
                Assert.AreEqual(0, UnlockHonesty.RealItems(c, CosmeticSlot.Head).Count, c + " headgear");
                Assert.AreEqual(0, UnlockHonesty.RealItems(c, CosmeticSlot.Emblem).Count, c + " emblems");
                Assert.AreEqual(1, UnlockHonesty.RealItems(c, CosmeticSlot.Emote).Count, c + " emotes: Cheer only");
                Assert.AreEqual(1, UnlockHonesty.LastRealLevel(c));
                for (int level = 1; level <= Leveling.MaxLevel; level++)
                {
                    string next = CharacterPanel.NextUnlockText(c, level);
                    Assert.AreEqual("Levels past 1 unlock nothing yet.", next, $"{c} at level {level}");
                }
                Assert.IsFalse(UnlockHonesty.MoreToUnlock(c, 1));
            }
            for (int from = 2; from <= Leveling.MaxLevel; from++)
                Assert.AreEqual(string.Empty, RewardsPanel.UnlockLine(from, from), $"level {from} unlocks nothing real, so the level-up line names nothing");
        }

        [Test]
        public void MoreLooksAreComingIsSaidOnceAndTheLevelPromiseOnlyWhileRealLooksRemain()
        {
            AuditBuild();
            string note = UnlockHonesty.OutfitNote(ClassId.Builder, 1);
            Debug.Log("[b15 profile] outfit note with the audit's art: " + note);
            Assert.AreEqual(1, Count(note, MatchWords.MoreComing), "said once");
            StringAssert.DoesNotContain("unlock as you level", note, "no promise of looks that do not exist");

            // with the Flex emote's art in (level 4), a level-1 player is promised it and a level-4 player is not
            UnlockHonesty.HasArt = it => it != null && it.Slot == CosmeticSlot.Emote && (it.Id == "Cheer" || it.Id == "Cheer_Flex");
            Assert.AreEqual(MatchWords.OutfitNoteMore, UnlockHonesty.OutfitNote(ClassId.Builder, 1));
            StringAssert.StartsWith("LEVEL 4: FLEX (victory emote)", CharacterPanel.NextUnlockText(ClassId.Builder, 1));
            Assert.AreEqual("Levels past 4 unlock nothing yet.", CharacterPanel.NextUnlockText(ClassId.Builder, 4));
            StringAssert.Contains("FLEX", RewardsPanel.UnlockLine(2, 5));
            Assert.AreEqual(1, Count(UnlockHonesty.OutfitNote(ClassId.Builder, 4), MatchWords.MoreComing));
        }

        [Test]
        public void AnExtraArtFileIsARealLookAtItsLevel()
        {
            UnlockHonesty.HasArt = it => it != null && it.Extra;
            UnlockHonesty.ArtIds = (c, s) => c == ClassId.Miner && s == CosmeticSlot.Head ? new[] { "MinersHelmet" } : new string[0];
            List<CosmeticItem> heads = UnlockHonesty.RealItems(ClassId.Miner, CosmeticSlot.Head);
            Assert.AreEqual(1, heads.Count);
            Assert.AreEqual(CosmeticItems.ExtraItemLevel, UnlockHonesty.LastRealLevel(ClassId.Miner));
            StringAssert.StartsWith("LEVEL 10: ", CharacterPanel.NextUnlockText(ClassId.Miner, 3));
        }

        [Test]
        public void TheBuildsRealArtIsReported()
        {
            int heads = 0, emblems = 0, emotes = 0, table = 0;
            foreach (ClassId c in ClassIds.All)
            {
                heads += UnlockHonesty.RealItems(c, CosmeticSlot.Head).Count;
                emblems += UnlockHonesty.RealItems(c, CosmeticSlot.Emblem).Count;
                emotes += UnlockHonesty.RealItems(c, CosmeticSlot.Emote).Count;
                table += CosmeticItems.Table(c, CosmeticSlot.Head).Count + CosmeticItems.Table(c, CosmeticSlot.Emblem).Count + CosmeticItems.Table(c, CosmeticSlot.Emote).Count;
            }
            Debug.Log($"[b15 profile] table looks {table}; wearable in this build: headgear {heads}, emblems {emblems}, emotes {emotes}; " +
                      $"Warrior next at level 1: \"{CharacterPanel.NextUnlockText(ClassId.Warrior, 1)}\"");
            Assert.AreEqual(72, table, "the table the audit counted");
        }

        static int Count(string s, string part)
        {
            int n = 0;
            for (int i = s.IndexOf(part, System.StringComparison.Ordinal); i >= 0; i = s.IndexOf(part, i + part.Length, System.StringComparison.Ordinal)) n++;
            return n;
        }
    }
}
