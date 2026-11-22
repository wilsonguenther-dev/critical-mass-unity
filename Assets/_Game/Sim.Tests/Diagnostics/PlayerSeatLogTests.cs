using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CriticalMass.Net;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Diagnostics;
using CriticalMass.Sim.Tests.PlayerSeat;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.Diagnostics
{
    /// <summary>
    /// px lane T: the player-seat telemetry recorder (Sim/Diagnostics/PlayerSeatLog.cs, docs/testing/PLAYER-SEAT-TELEMETRY.md)
    /// is a pure observer (the match hash is the same with and without it, under every rules profile) and accounts for every
    /// press (exactly one record with a resolution per press edge).
    /// </summary>
    public sealed class PlayerSeatLogTests
    {
        static readonly Regex Kind = new Regex("\"kind\":\"([a-z_]+)\"");
        static readonly Regex Tick = new Regex("\"tick\":(-?\\d+)");
        static readonly Regex Button = new Regex("\"button\":\"([a-z_]+)\"");
        static readonly Regex Resolved = new Regex("\"resolved\":\"([A-Za-z_]+)\"");
        static readonly Regex Outcome = new Regex("\"outcome\":\"([a-z_]+)\"");

        /// <summary>One 5v5 match, seat 0 played by the gate's human model with every button pressed on a schedule.</summary>
        static ulong[] Play(RulesProfile profile, bool record, int ticks, out PlayerSeatLog log)
        {
            GameConfig c = GameConfig.CreateBrowser5v5().ApplyRules(profile);
            World w = World.CreateWorld(new WorldOptions { Seed = 777, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = c });
            var s = new Seat { W = w, Me = 0, Spy = new EventSpy(w), Frames = new InputFrame[w.ActorCount], AllowAutopilot = true };
            s.Human = new HumanModel(0, w.ActorCount / 2) { LagTicks = 6 };
            log = record ? new PlayerSeatLog(0, new ListLogSink()) { Seed = 777, Mode = "test" } : null;
            var hashes = new List<ulong>();
            for (int t = 0; t < ticks; t++)
            {
                if (t % 40 == 5) s.Human.PressAbility = true;
                if (t % 40 == 15) s.Human.PressDigPlace = true;
                if (t % 40 == 25) s.Human.PressDodge = true;
                if (t % 40 == 30) s.Human.PressJump = true;
                if (t % 40 == 35) s.Human.PressCancel = true;
                s.Human.HoldInteract = t % 90 >= 60 && t % 90 < 70;
                s.Step();
                log?.Observe(w, s.Frames[0]);
                if ((t + 1) % 150 == 0) hashes.Add(StateHash.Of(w));
            }
            log?.Finish(w);
            hashes.Add(StateHash.Of(w));
            return hashes.ToArray();
        }

        [Test]
        public void TheRecorderNeverChangesTheMatch_SameStateHashWithAndWithoutIt_EveryRulesProfile()
        {
            foreach (RulesProfile p in new[] { RulesProfile.Full, RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                ulong[] off = Play(p, false, 900, out _);
                ulong[] on = Play(p, true, 900, out PlayerSeatLog log);
                Console.WriteLine($"[px-T] {p}: final StateHash off 0x{off[off.Length - 1]:X16} on 0x{on[on.Length - 1]:X16}, {log.RecordsWritten} records, {log.Presses} presses (limit: every checkpoint equal)");
                Assert.AreEqual(off, on, p + ": the recorder changed the match (StateHash differs at a checkpoint)");
                Assert.Greater(log.Presses, 20, p + ": the scripted seat pressed");
            }
        }

        [Test]
        public void EveryPressInAScriptedSequenceGivesExactlyOneRecordWithAResolution()
        {
            // a staged duel: the Warrior seat taps each button on its own tick, the target is a passive dummy in reach
            Seat s = PlayerSeatRig.Duel(ClassId.Warrior, ClassId.Farmer, TargetMode.Passive, 0, 0, distance: 1.6);
            s.Human.Fight = false;
            var sink = new ListLogSink();
            var log = new PlayerSeatLog(0, sink) { Seed = 4242, Mode = "scripted" };
            var expected = new List<(int tick, string button)>();
            string[] names = { "attack", "ability", "dig_place", "interact", "dodge", "cancel", "jump" };
            for (int t = 0; t < 7 * 3 * 25 + 40; t++)
            {
                int k = t / 25;
                if (t % 25 == 2 && k < 21)
                {
                    string b = names[k % 7];
                    expected.Add((s.W.Tick, b));
                    switch (b)
                    {
                        case "attack": s.Human.PressAttack = true; break;
                        case "ability": s.Human.PressAbility = true; break;
                        case "dig_place": s.Human.PressDigPlace = true; break;
                        case "interact": s.Human.HoldInteract = true; break;
                        case "dodge": s.Human.PressDodge = true; break;
                        case "cancel": s.Human.PressCancel = true; break;
                        case "jump": s.Human.PressJump = true; break;
                    }
                }
                else s.Human.HoldInteract = false;
                s.Step();
                log.Observe(s.W, s.Frames[0]);
            }
            log.Finish(s.W);
            var presses = new List<(int tick, string button, string resolved, string line)>();
            int releases = 0;
            foreach (string l in sink.Lines)
            {
                string kind = Kind.Match(l).Groups[1].Value;
                if (kind == "release") releases++;
                if (kind != "press") continue;
                System.Text.RegularExpressions.Match r = Resolved.Match(l);
                Assert.IsTrue(r.Success, "a press record without a resolution: " + l);
                presses.Add((int.Parse(Tick.Match(l).Groups[1].Value, CultureInfo.InvariantCulture), Button.Match(l).Groups[1].Value, r.Groups[1].Value, l));
                if (presses[presses.Count - 1].button == "attack") Assert.IsFalse(Outcome.Match(l).Groups[1].Value == "pending", "an attack's swing never got an outcome: " + l);
            }
            presses.Sort((a, b) => a.tick.CompareTo(b.tick));
            foreach (var p in presses) Console.WriteLine($"[px-T] tick {p.tick} {p.button} -> {p.resolved}");
            Assert.AreEqual(expected.Count, presses.Count, "one press record per press edge");
            Assert.AreEqual(expected.Count, releases, "one release record per release edge");
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.AreEqual(expected[i].tick, presses[i].tick, "press " + i + " tick");
                Assert.AreEqual(expected[i].button, presses[i].button, "press " + i + " button");
                Assert.IsNotEmpty(presses[i].resolved);
            }
            Assert.AreEqual(expected.Count, log.Presses);
            StringAssert.Contains("\"kind\":\"header\"", sink.Lines[0]);
            StringAssert.Contains("\"kind\":\"summary\"", sink.Lines[sink.Lines.Count - 1]);
        }
    }
}
