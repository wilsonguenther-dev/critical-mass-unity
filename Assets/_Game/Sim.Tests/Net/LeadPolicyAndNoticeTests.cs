using System;
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

        // ------------------------------------------------------------------ b12-x2: the joiner cap

        [Test]
        public void TheJoinerCapIsHostLeadPlus4OrAtLeast12AndNeverUnderTheFloor()
        {
            // host lead -> expected ceiling at 30 Hz with the 700 ms maximum (21 ticks)
            int[,] rows = { { 3, 12 }, { 6, 12 }, { 8, 12 }, { 10, 14 }, { 12, 16 }, { 19, 21 } };
            for (int i = 0; i < rows.GetLength(0); i++)
            {
                LeadPolicy p = LeadPolicy.For(rows[i, 0], Tick30, 700);
                TestContext.WriteLine($"[b12-x2 cap] host lead {rows[i, 0]}: joiner starts at {p.Lead}, floor {p.Floor}, ceiling {p.Ceiling} (expected {rows[i, 1]})");
                Assert.AreEqual(rows[i, 1], p.Ceiling, "host lead " + rows[i, 0]);
                Assert.AreEqual(Math.Max(p.Floor, rows[i, 0]), p.Lead, "the joiner still starts at the host's lead");
                Assert.GreaterOrEqual(p.Ceiling, Math.Min(rows[i, 0] + LeadPolicy.JoinerCapOverHost, 21), "the cap never cuts the host's own lead (+4, unless the old 700 ms ceiling is lower)");
            }
            LeadPolicy tiny = LeadPolicy.For(3, Tick30, 50);
            TestContext.WriteLine($"[b12-x2 cap] a 50 ms maximum: floor {tiny.Floor}, ceiling {tiny.Ceiling} (the floor wins)");
            Assert.AreEqual(tiny.Floor, tiny.Ceiling, "the ceiling is never under the floor");
            Assert.AreEqual(3, tiny.Floor, "today's floor (100 ms at 30 Hz) is unchanged");
        }

        [Test]
        public void AJitteryJoinerWithHostLead10NeverGoesOverTheCapAndRecovers()
        {
            const int hostLead = 10, cap = 14, recoverWithinMs = 12_000, spikeFrom = 10_000, spikeTo = 14_000, settleBound = 6 + LeadPolicy.SafetyTicks + 1;
            Assert.AreEqual(cap, LeadPolicy.JoinerCap(hostLead));
            LeadPolicy p = LeadPolicy.For(hostLead, Tick30, 700);
            LeadPolicy old = new LeadPolicy(hostLead, p.Floor, 21);   // the same script under the old ceiling only
            Assert.AreEqual(hostLead, p.Lead, "starts at the host's lead");
            Assert.AreEqual(cap, p.Ceiling);
            uint rng = 12345;
            long recoveredAt = -1;
            int worstBeforeSpike = 0;
            for (long t = 0; t <= 30_000; t += 30)
            {
                rng = rng * 1664525u + 1013904223u;
                int need = 4 + (int)((rng >> 16) % 3);                                  // a round trip needing 4 to 6 ticks
                if (t < spikeFrom && (rng >> 8) % 300 == 0) need = 12;                    // a rare one-packet spike
                if (t >= spikeFrom && t < spikeTo) need = 20;                             // a 4 s spike far over the cap
                foreach (LeadPolicy q in new[] { p, old })
                {
                    int margin = q.Lead - need;
                    if (margin < 0) q.OnLate(t);
                    q.OnMargin(margin, t);
                }
                if (t < spikeFrom) worstBeforeSpike = Math.Max(worstBeforeSpike, p.Lead);
                if (t >= spikeTo && recoveredAt < 0 && p.Lead <= settleBound) recoveredAt = t;
                if (t % 250 == 0)
                    TestContext.WriteLine($"[b12-x2 lead] t {t} ms need {need} lead {p.Lead} (old ceiling 21: {old.Lead}) cap {cap} floor {p.Floor}");
                Assert.LessOrEqual(p.Lead, cap, "over the cap at " + t + " ms");
                Assert.GreaterOrEqual(p.Lead, p.Floor, "under the floor at " + t + " ms");
            }
            TestContext.WriteLine($"[b12-x2 lead] leadmin {p.Min} leadmax {p.Max} (limit {cap}); old ceiling's leadmax {old.Max}; " +
                                  $"worst before the spike {worstBeforeSpike}; back to {settleBound} or less {(recoveredAt < 0 ? "never" : (recoveredAt - spikeTo) + " ms")} after the spike (limit {recoverWithinMs} ms); grown {p.Grown} shrunk {p.Shrunk}");
            Assert.AreEqual(cap, p.Max, "the spike drove it to the cap and no further");
            Assert.Greater(old.Max, cap, "the script really pushes past the cap (the old ceiling let it)");
            Assert.GreaterOrEqual(recoveredAt, spikeTo, "it came back down after the spike");
            Assert.LessOrEqual(recoveredAt - spikeTo, recoverWithinMs, "recovers within the limit");
            Assert.AreEqual(10, RoomHost.StartDelay(2 * (10 * 25 - 60), 25.0), "the host's lead (the room's start delay) is computed as before");
        }

        static RoomMember M(int peer, string name) => new RoomMember { Peer = peer, Name = name, Confirmed = true };
    }
}
