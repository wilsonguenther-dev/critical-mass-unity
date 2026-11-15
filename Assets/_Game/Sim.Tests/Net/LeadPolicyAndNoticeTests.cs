using System.Collections.Generic;
using CriticalMass.Net;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>online-rooms-9: the adaptive input lead, the room's start delay and the join and leave notices.</summary>
    public sealed class LeadPolicyAndNoticeTests
    {
        const double Tick30 = 1000.0 / 30;

        [Test]
        public void TheLeadHasAFloorOf100MsAndACeilingOfMaxLead()
        {
            LeadPolicy p = LeadPolicy.For(19, Tick30, 700);
            Assert.AreEqual(3, p.Floor, "100 ms at 30 Hz");
            Assert.AreEqual(21, p.Ceiling, "700 ms at 30 Hz");
            Assert.AreEqual(19, p.Lead);
            Assert.AreEqual(3, LeadPolicy.For(1, Tick30, 700).Lead, "a start under the floor is raised to it");
            Assert.AreEqual(21, LeadPolicy.For(40, Tick30, 700).Lead, "a start over the ceiling is cut to it");
        }

        [Test]
        public void ALinkWithRoomToSpareShrinksTheLeadToTheFloorAndNoLower()
        {
            LeadPolicy p = LeadPolicy.For(19, Tick30, 700);
            long t = 0;
            for (; t < 40_000; t += 30) p.OnMargin(19, t);   // far more margin than needed the whole time
            Assert.AreEqual(p.Floor, p.Lead);
            Assert.AreEqual(19 - p.Floor, p.Shrunk);
            Assert.AreEqual(0, p.Grown);
        }

        [Test]
        public void ShrinkingIsOneTickASecondAtMost()
        {
            LeadPolicy p = LeadPolicy.For(19, Tick30, 700);
            for (long t = 0; t <= 10_000; t += 30) p.OnMargin(19, t);
            Assert.GreaterOrEqual(p.Lead, 19 - 10, "no more than one tick per second");
            Assert.Less(p.Lead, 19, "but it does come down");
        }

        [Test]
        public void ItStopsShrinkingWhereTheMarginRunsOut()
        {
            // The margin is what the lead buys beyond the round trip: lead - 6 here (a 200 ms round trip at 30 Hz).
            LeadPolicy p = LeadPolicy.For(19, Tick30, 700);
            for (long t = 0; t < 60_000; t += 30) p.OnMargin(p.Lead - 6, t);
            Assert.AreEqual(6 + LeadPolicy.SafetyTicks, p.Lead, "settles with the safety margin kept");
        }

        [Test]
        public void LateInputsGrowItOneTickPerHalfSecondUpToTheCeiling()
        {
            LeadPolicy p = LeadPolicy.For(5, Tick30, 700);
            for (long t = 0; t < 1_000; t += 100) p.OnLate(t);
            Assert.LessOrEqual(p.Lead, 7, "at most one tick per 500 ms");
            Assert.Greater(p.Lead, 5);
            for (long t = 1_000; t < 30_000; t += 100) p.OnLate(t);
            Assert.AreEqual(p.Ceiling, p.Lead);
            Assert.AreEqual(p.Ceiling, p.Max);
        }

        [Test]
        public void AJustInTimeMarginGrowsItAndALateInputHoldsShrinkingOff()
        {
            LeadPolicy p = LeadPolicy.For(8, Tick30, 700);
            p.OnMargin(0, 0);
            Assert.AreEqual(9, p.Lead, "a margin of 0 means no slack at all");
            p = LeadPolicy.For(8, Tick30, 700);
            p.OnLate(0);
            int after = p.Lead;
            for (long t = 30; t < LeadPolicy.QuietMs - 30; t += 30) p.OnMargin(15, t);
            Assert.AreEqual(after, p.Lead, "no shrinking for QuietMs after a late input");
        }

        [Test]
        public void TheStartDelayIsHalfTheWorstRoundTripPlus60MsBetween100And250Ms()
        {
            Assert.AreEqual(3, RoomHost.StartDelay(0, Tick30), "floor 100 ms");
            Assert.AreEqual(3, RoomHost.StartDelay(52, Tick30), "86 ms: under the floor, so 3 ticks (100 ms)");
            Assert.AreEqual(6, RoomHost.StartDelay(260, Tick30), "one 260 ms ping: 190 ms, not the old 320 ms (10 ticks)");
            Assert.AreEqual(8, RoomHost.StartDelay(2000, Tick30), "ceiling 250 ms");
        }

        [Test]
        public void JoinersSeeJoinsAndLeavesButNotTheFirstRosterOrThemselves()
        {
            var link = new LoopbackNetwork(1, 0, 0, 0).CreatePeer();
            var chat = new ChatChannel(link);
            var known = new Dictionary<int, string>();
            var first = new List<RoomMember> { M(0, "WILSON"), M(1, "JEISIL"), M(2, "ME") };
            RoomNotices.Roster(known, first, chat, 0, 2, false);
            Assert.AreEqual(0, chat.Lines.Count, "the roster I arrive to is not news");
            RoomNotices.Roster(known, new List<RoomMember> { M(0, "WILSON"), M(2, "ME"), M(3, "AIDAN") }, chat, 10, 2, true);
            var texts = new List<string>();
            foreach (ChatLine l in chat.Lines) { Assert.AreEqual(ChatScope.System, l.Scope); texts.Add(l.Text); }
            CollectionAssert.AreEquivalent(new[] { "AIDAN joined.", "JEISIL left the room." }, texts);
            RoomNotices.Roster(known, new List<RoomMember> { M(0, "WILSON"), M(2, "ME"), M(3, "AIDAN") }, chat, 20, 2, true);
            Assert.AreEqual(2, chat.Lines.Count, "an unchanged roster posts nothing");
        }

        static RoomMember M(int peer, string name) => new RoomMember { Peer = peer, Name = name, Confirmed = true };
    }
}
