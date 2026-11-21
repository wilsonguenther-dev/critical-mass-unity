using System;
using System.Collections.Generic;
using System.IO;
using CriticalMass.Net.Replay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 tools (balance report, tooling only): the seed pools, the interval maths, the switch setter, the probe's bookkeeping on a
    /// short bot match, replay re-simulation equal to the live match, the duel table, compare and targets. No rule is touched.
    /// </summary>
    [TestFixture]
    public sealed class BalanceReportTests
    {
        [TearDown]
        public void ClearTap() => MatchTick.Tap = null;

        [Test]
        public void TheSeedPoolStartsWithTheWinSharePoolAndGrowsWithoutRepeats()
        {
            CollectionAssert.AreEqual(WinShare.Seeds16, BalanceSeeds.Pool(24));
            int[] big = BalanceSeeds.Pool(72);
            Assert.AreEqual(72, big.Length);
            Assert.AreEqual(72, new HashSet<int>(big).Count);
            for (int i = 0; i < 24; i++) Assert.AreEqual(WinShare.Seeds16[i], big[i]);
        }

        [Test]
        public void WilsonIntervalOfTwelveInTwentyFourIsTheTextbookValue()
        {
            BalanceStat.Wilson(12, 24, out double p, out double lo, out double hi);
            Assert.AreEqual(0.5, p, 1e-12);
            Assert.AreEqual(0.3143, lo, 1e-3);
            Assert.AreEqual(0.6857, hi, 1e-3);
            BalanceStat.Wilson(0, 0, out p, out lo, out hi);
            Assert.IsTrue(double.IsNaN(p));
        }

        [Test]
        public void MedianIntervalUsesTheOrderStatistics()
        {
            var s = new List<double>();
            for (int i = 1; i <= 24; i++) s.Add(i);
            BalanceStat.Median(s, out double med, out double lo, out double hi);
            Assert.AreEqual(12.5, med, 1e-12);
            Assert.AreEqual(7, lo, 1e-12);
            Assert.AreEqual(18, hi, 1e-12);
            BalanceStat.Mean(new List<double> { 1, 2, 3, 4 }, out double m, out double mlo, out double mhi, out double sd);
            Assert.AreEqual(2.5, m, 1e-12);
            Assert.AreEqual(2.5 - 3.182 * sd / 2, mlo, 1e-9);
        }

        [Test]
        public void SwitchesSetNestedFieldsArraysAndClassRowsAndRefuseUnknownPaths()
        {
            GameConfig cfg = BalanceRunner.Config(RulesProfile.Full, new[] { "Depth.Hut.On=false", "Classes[Warrior].MaxHp=170", "Depth.Hut.LevelCost[4]=20", "Hill.Radius=7", "Depth.Hut.OreAnchors=1,2,3" });
            Assert.IsFalse(cfg.Depth.Hut.On);
            Assert.AreEqual(170, cfg.Classes[(int)ClassId.Warrior].MaxHp);
            Assert.AreEqual(20, cfg.Depth.Hut.LevelCost[4]);
            Assert.AreEqual(7, cfg.Hill.Radius);
            CollectionAssert.AreEqual(new double[] { 1, 2, 3 }, cfg.Depth.Hut.OreAnchors);
            Assert.AreEqual(RulesProfile.Full, cfg.Rules);
            Assert.AreEqual(160, GameConfig.CreateBrowser5v5().Classes[0].MaxHp, "the factory's own rows are untouched");
            Assert.Throws<ArgumentException>(() => BalanceRunner.Config(RulesProfile.Full, new[] { "Depth.Hut.NoSuchDial=1" }));
            Assert.Throws<ArgumentException>(() => BalanceRunner.Config(RulesProfile.Full, new[] { "Hill.Radius=wide" }));
        }

        [Test]
        public void TheProbeAgreesWithTheSimsOwnCountersOnAShortBotMatch()
        {
            GameConfig cfg = BalanceRunner.Config(RulesProfile.Full, null);
            int kills = 0, deaths = 0, n = 0;
            MatchRecord r = BalanceRunner.BotMatch(cfg, 1337, 5, 120, null, w =>
            {
                n = w.ActorCount;
                for (int i = 0; i < w.ActorCount; i++)
                {
                    kills += w.Actors[i].Kills;
                    deaths += w.Actors[i].Deaths;
                }
            });
            double probeKills = 0, probeDeaths = 0, actorSec = 0;
            for (int c = 0; c < MatchRecord.Classes; c++)
            {
                probeKills += r.Kills[c];
                probeDeaths += r.Deaths[c];
                actorSec += r.ActorSec[c];
                Assert.LessOrEqual(r.DeadSec[c] + r.WalkSec[c] + r.FightSec[c], r.ActorSec[c] + 1e-6, "time buckets of class " + c);
            }
            Assert.Greater(deaths, 0, "two minutes of 5v5 has knock-outs");
            Assert.AreEqual(kills, probeKills, "kills");
            Assert.AreEqual(deaths, probeDeaths, "deaths");
            Assert.AreEqual(r.Ticks * n / cfg.SimHz, actorSec, 1e-6, "actor seconds");
            Assert.AreEqual(0, r.EventsLost);
            Assert.GreaterOrEqual(r.Top10DamageShare, 0.999, "10 of 10 bodies hold all the damage");
        }

        [Test]
        public void AReplayReSimulatesToTheSameNumbersAsTheLiveMatch()
        {
            GameConfig cfg = BalanceRunner.Config(RulesProfile.Full, null);
            var rec = new ReplayRecorder();
            MatchRecord live = BalanceRunner.BotMatch(cfg, 4242, 5, 90, w =>
            {
                ReplayStart s = ReplayStart.From(BalanceRunner.Options(cfg, 4242, 5), ReplayKind.Offline, ConfigBasis.Browser5v5, (int)RulesProfile.Full, 0, 0, 0, 0);
                Assert.IsTrue(rec.Begin(w, new ReplayHeader { BuildVersion = "test", BuildCommit = "test", Start = s }), rec.Refused);
                MatchTick.Tap = rec;
            }, w => MatchTick.Tap = null);
            ReplayData data = rec.Finish(false, 638000000000000000L);
            byte[] file = ReplayFile.Write(data);
            Assert.AreEqual(ReplayReadStatus.Ok, ReplayFile.TryRead(file, out ReplayData back, out string why), why);
            MatchRecord again = BalanceRunner.Replay(back, "replay");
            Assert.IsFalse(again.Desynced);
            Assert.AreEqual(live.Ticks, again.Ticks);
            Assert.AreEqual(live.HillAzure, again.HillAzure);
            Assert.AreEqual(live.HillCrimson, again.HillCrimson);
            Assert.AreEqual(live.LeadChanges, again.LeadChanges);
            Assert.AreEqual(live.Stuck, again.Stuck);
            CollectionAssert.AreEqual(live.Damage, again.Damage);
            CollectionAssert.AreEqual(live.Kills, again.Kills);
            CollectionAssert.AreEqual(live.DeadSec, again.DeadSec);
            CollectionAssert.AreEqual(live.WalkSec, again.WalkSec);
        }

        [Test]
        public void TheDuelTableKillsWithWeaponsAndNeverWithTheHealer()
        {
            DuelRunner.Cell[,] ttk = DuelRunner.Ttk(RulesProfile.Full, null, 1);
            DuelRunner.Cell wf = ttk[(int)ClassId.Warrior, (int)ClassId.Farmer];
            Assert.AreEqual(1, wf.Kills, "a Warrior kills a passive Farmer");
            Assert.That(wf.Ttk[0], Is.InRange(1.0, 15.0));
            // b15 combat feel (PR #215) gives the Healer a self-defence lantern swing (Healer TTK row <= 6.20 s), so under Full
            // she can kill a passive target; with the combat feel off she still has no weapon.
            DuelRunner.Cell[,] plain = DuelRunner.Ttk(RulesProfile.Full, new[] { "Depth.CombatFeel.Enabled=false" }, 1);
            for (int b = 0; b < 6; b++) Assert.AreEqual(0, plain[(int)ClassId.Healer, b].Kills, "without the combat feel the Healer has no weapon");
        }

        static BalanceReport OneRow(string key, int k, int n)
        {
            var r = new BalanceReport();
            BalanceStat.Wilson(k, n, out double p, out double lo, out double hi);
            r.Rows.Add(new BalanceRow { Key = key, Label = key, Kind = "rate", Value = p, Lo = lo, Hi = hi, N = n });
            return r;
        }

        [Test]
        public void CompareFlagsOnlyDifferencesOutsideTheNoise()
        {
            BalanceReport b = OneRow("win.azure", 12, 24);
            Assert.AreEqual("OUTSIDE noise", BalanceCompare.Lines(b, OneRow("win.azure", 23, 24))[0].Verdict);
            Assert.AreEqual("within noise", BalanceCompare.Lines(b, OneRow("win.azure", 14, 24))[0].Verdict);
            BalanceReport t = OneRow("tick.mean.us", 1, 2);
            t.Rows[0].Comparable = false;
            Assert.AreEqual("not judged", BalanceCompare.Lines(t, t)[0].Verdict);
        }

        [Test]
        public void TheTargetsFileHasTheAuditBandsOnRowsTheReportWrites()
        {
            List<BalanceTarget> targets = BalanceTarget.FromJson(File.ReadAllText("docs/balance/TARGETS.json"));
            Assert.AreEqual(9, targets.Count);   // b15 wave two: + warriordmg (dmgshare.Warrior); deadwalk reads time.deadwalkback.all
            GameConfig cfg = BalanceRunner.Config(RulesProfile.Full, null);
            var ms = new List<MatchRecord> { BalanceRunner.BotMatch(cfg, 7, 5, 30) };
            BalanceReport r = BalanceAggregate.Build(ms, DuelRunner.Ttk(RulesProfile.Full, null, 1), DuelRunner.Duels(RulesProfile.Full, null, 1));
            foreach (BalanceTarget t in targets) Assert.IsNotNull(r.Row(t.Row), "target " + t.Id + " names row " + t.Row);
            var stuck = new BalanceTarget { Max = 5, MaxExclusive = true };
            Assert.AreEqual("missed", stuck.Verdict(new BalanceRow { Value = 5 }, out string _));
            Assert.AreEqual("met", stuck.Verdict(new BalanceRow { Value = 4 }, out string _));
            var band = new BalanceTarget { Min = 0.6, Max = 0.8 };
            Assert.AreEqual("met", band.Verdict(new BalanceRow { Value = 0.7, Lo = 0.5, Hi = 0.9 }, out string note));
            Assert.AreEqual("95% interval crosses the bound", note);
            string md = BalanceWriter.Markdown(r, targets);
            StringAssert.Contains("## Targets", md);
            BalanceReport back = BalanceWriter.FromJson(BalanceWriter.Json(r));
            Assert.AreEqual(r.Rows.Count, back.Rows.Count);
            for (int i = 0; i < r.Rows.Count; i++)
            {
                Assert.AreEqual(r.Rows[i].Key, back.Rows[i].Key);
                Assert.AreEqual(r.Rows[i].N, back.Rows[i].N);
                if (double.IsNaN(r.Rows[i].Value)) Assert.IsTrue(double.IsNaN(back.Rows[i].Value), r.Rows[i].Key);
                else Assert.AreEqual(r.Rows[i].Value, back.Rows[i].Value, 0, r.Rows[i].Key);
            }
        }
    }
}
