using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Mechanics;
using CriticalMass.Runtime.Audio;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>b15 HUD words and places (pure): the RALLY tag, the vote cards and keys, the result, the scoreboard picks.</summary>
    public sealed class B15RulesHudTests
    {
        [Test]
        public void TheRallyTagAndTheVoteWordsReadPlainly()
        {
            Assert.AreEqual("RALLY x1.25   45 s left", B15Hud.RallyLine(true, 1.25, 45));
            Assert.AreEqual("ENEMY RALLY x1.25   3 s left", B15Hud.RallyLine(false, 1.25, 3));
            Assert.AreEqual("1 vote", B15Hud.Votes(1));
            Assert.AreEqual("0 votes", B15Hud.Votes(0));
            Assert.AreEqual(8, B15Hud.SecondsLeft(240, 30));
            Assert.AreEqual(1, B15Hud.SecondsLeft(1, 30));
            StringAssert.Contains("QUICK RETURN", B15Hud.Result(1, HutOption.QuickReturn));
            StringAssert.Contains("drawn by lot", B15Hud.ResultReason(HutOption.ShortFuse, 0, 0, true));
            StringAssert.Contains("3 to 1", B15Hud.ResultReason(HutOption.ShortFuse, 3, 1, false));
            for (int o = 1; o <= 10; o++)
            {
                Assert.IsFalse(string.IsNullOrEmpty(HutChoiceNames.Name[o]), "option " + o + " has a name");
                Assert.IsFalse(string.IsNullOrEmpty(HutChoiceNames.Line[o]), "option " + o + " has a line");
                Assert.LessOrEqual(HutChoiceNames.Name[o].Length, 14, "the name fits its card");
                Assert.LessOrEqual(HutChoiceNames.Line[o].Length, 34, "the line fits its card");
            }
        }

        [Test]
        public void TheTagAndTheCardsSitUnderTheCentreStackOnScreen()
        {
            Rect r = B15Hud.RallyRect, v = B15Hud.VoteRect;
            Assert.GreaterOrEqual(r.y, HudZones.EventY + HudZones.EventH, "under the map-event bar");
            Assert.GreaterOrEqual(v.y, r.yMax, "the cards under the tag");
            Assert.Greater(v.x, 0f);
            Assert.Less(v.xMax, HudLayout.RefW);
            Assert.Less(v.yMax, HudZones.Lower.y, "above the lower lane");
            Assert.IsFalse(v.Overlaps(HudZones.Feed), "clear of the kill feed");
            Assert.IsFalse(r.Overlaps(DepthHudRect()), "clear of the hut badge");
        }

        static Rect DepthHudRect() => B13Hud.HutRect;

        [Test]
        public void TheRallyLineIsInTheTableOnce()
        {
            AnnouncerLine l = AnnouncerLines.For(AnnouncerTrigger.Rally);
            Assert.AreEqual("an-23", l.Id);
            Assert.AreEqual("rally", AnnouncerLines.TriggerNames[(int)AnnouncerTrigger.Rally]);
        }
    }
}
