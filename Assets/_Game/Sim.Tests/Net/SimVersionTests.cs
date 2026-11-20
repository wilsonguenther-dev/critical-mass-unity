using System;
using CriticalMass.Net;
using CriticalMass.Net.Replay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Net
{
    /// <summary>
    /// b15-rejoin, versioning. SimVersion.Schema used to stay at 1 across 1,146 commits, so every old replay passed the
    /// header check and then broke at its first fingerprint. Now a fixed set of reference matches (default rules, bots and
    /// autopilot, 16v16 seed 4242 for 80 s and 5v5 seed 42 for 2 minutes) is fingerprinted with the full-state StateHash
    /// every 10 s; the fold must equal SimVersion.RulesDigest. Any change to what the sim does or holds moves it, and the
    /// failure says exactly what to do: bump Schema, paste the printed digest. Two tests then prove what a bump buys:
    /// a replay from another schema is refused in plain words, and a room refuses a joiner with other rules.
    /// </summary>
    public sealed class SimVersionTests
    {
        public static ulong ComputeRulesDigest()
        {
            const ulong Prime = 1099511628211UL;
            ulong h = 14695981039346656037UL;
            var runs = new[] { (seed: 4242, team: 16, ticks: 2400), (seed: 42, team: 5, ticks: 3600) };
            foreach (var r in runs)
            {
                World w = World.CreateWorld(new WorldOptions
                {
                    Seed = r.seed, TeamSize = r.team, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
                });
                InputFrame scratch = SimInput.EmptyInput();
                for (int t = 0; t < r.ticks && w.Match.Phase == MatchPhase.Playing; t++)
                {
                    MatchTick.Advance(w, null, scratch);
                    if ((t + 1) % 300 == 0) h = unchecked((h ^ StateHash.Of(w)) * Prime);
                }
                h = unchecked((h ^ StateHash.Of(w)) * Prime);
            }
            return h;
        }

        [Test]
        public void TheRulesDigestIsPinnedForThisSchema()
        {
            ulong d = ComputeRulesDigest();
            ulong again = ComputeRulesDigest();
            Console.WriteLine($"[sim version] schema {SimVersion.Schema}: rules digest 0x{d:X16}, pinned 0x{SimVersion.RulesDigest:X16} " +
                              $"(limit: equal; same digest twice in one process: {(d == again ? "yes" : "NO")})");
            Assert.AreEqual(d, again, "the reference matches must be deterministic");
            Assert.AreEqual(SimVersion.RulesDigest, d,
                $"The sim's rules or state changed. Old replays and other builds now play differently: bump SimVersion.Schema to " +
                $"{SimVersion.Schema + 1} and set SimVersion.RulesDigest = 0x{d:X16}UL in Assets/_Game/Sim/SimVersion.cs.");
        }

        [Test]
        public void AReplayFromAnotherSchemaIsRefusedInPlainWords()
        {
            var old = new ReplayHeader { SimSchema = SimVersion.Schema - 1, BuildVersion = "", BuildCommit = "" };
            ReplayCompatibility c = ReplayCompat.Check(old, "", "");
            string msg = ReplayCompat.Message(c, old);
            Console.WriteLine($"[sim version] a schema {old.SimSchema} replay on schema {SimVersion.Schema}: {c}, \"{msg}\" (limit: OtherVersion)");
            Assert.AreEqual(ReplayCompatibility.OtherVersion, c);
            StringAssert.StartsWith(ReplayCompat.OtherVersionText, msg);
            var same = new ReplayHeader { SimSchema = SimVersion.Schema, BuildVersion = "", BuildCommit = "" };
            Assert.AreEqual(ReplayCompatibility.Ok, ReplayCompat.Check(same, "", ""));
        }
    }
}
