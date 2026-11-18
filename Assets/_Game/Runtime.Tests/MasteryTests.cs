using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Net;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Menus;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b15 profile: class mastery maths (the curve, the role weights, per-stat and per-match caps), the anti-farm rules
    /// (bots-only matches at half, a daily allowance, practice modes and unfinished matches at nothing, an idle player
    /// gets no finish bonus, three counted knock-outs per victim, only teammates' use of a built piece, no self-heals),
    /// the reward table in data and the trim colour rules. Pure: no scene, no profile on disk.
    /// </summary>
    public sealed class MasteryTests
    {
        static void P(string s) => Debug.Log("[b15-mastery] " + s);

        static MasteryContext Ctx(MatchOutcome o = MatchOutcome.Win, bool bots = false, bool practice = false, bool done = true, int day = 20261117) =>
            new MasteryContext { Completed = done, Outcome = o, BotOnly = bots, PracticeMode = practice, Day = day };

        [Test]
        public void TheCurveHasClearThresholds()
        {
            var lines = new List<string>();
            for (int l = 1; l <= MasteryRules.MaxLevel; l++) lines.Add($"L{l}={MasteryRules.TotalFor(l)}");
            P("thresholds: " + string.Join(" ", lines));
            Assert.AreEqual(0, MasteryRules.TotalFor(1));
            Assert.AreEqual(400, MasteryRules.TotalFor(2));
            Assert.AreEqual(7200, MasteryRules.TotalFor(10));
            Assert.AreEqual(24700, MasteryRules.TotalFor(20));
            Assert.AreEqual(24700, MasteryRules.MaxXp);
            for (int l = 1; l < MasteryRules.MaxLevel; l++)
            {
                Assert.AreEqual(l, MasteryRules.LevelFor(MasteryRules.TotalFor(l + 1) - 1));
                Assert.AreEqual(l + 1, MasteryRules.LevelFor(MasteryRules.TotalFor(l + 1)));
            }
            Assert.AreEqual(20, MasteryRules.LevelFor(int.MaxValue));
            Assert.AreEqual(1, MasteryRules.LevelFor(-5));
            Assert.AreEqual(0f, MasteryRules.Progress(0));
            Assert.AreEqual(0.5f, MasteryRules.Progress(200), 1e-4f);
            Assert.AreEqual(1f, MasteryRules.Progress(MasteryRules.MaxXp));
        }

        [Test]
        public void EachClassEarnsFromItsJob()
        {
            var healer = new RoleStats { Healing = 900, HillSeconds = 120, KnockOuts = 2 };
            MasteryAward a = MasteryRules.Compute(ClassId.Healer, healer, Ctx());
            P($"healer 900 healing, 120 s hill, 2 KO, win: raw {a.Raw} xp {a.Xp} lines " + string.Join(", ", a.Lines.Select(l => $"{l.Stat} {l.Counted}={l.Xp}")));
            Assert.AreEqual(900 * 6 / 10 + 120 * 15 / 10 + 2 * 100 / 10 + MasteryRules.FinishXp + MasteryRules.WinXp, a.Xp);
            // the same numbers on a Warrior: healing is not a Warrior's job and earns nothing
            MasteryAward w = MasteryRules.Compute(ClassId.Warrior, healer, Ctx());
            P($"same stats as a warrior: xp {w.Xp}");
            Assert.Less(w.Xp, a.Xp);
            Assert.IsFalse(w.Lines.Any(l => l.Stat == RoleStat.Healing && l.Xp > 0));
            // every class has a job that pays
            for (int c = 0; c < 6; c++)
            {
                RoleStat[] job = MasteryRules.Job[c];
                Assert.GreaterOrEqual(job.Length, 2);
                foreach (RoleStat s in job) Assert.Greater(MasteryRules.Weight10[c][(int)s], 0, $"{(ClassId)c} {s}");
            }
            // the outcome bonus
            Assert.AreEqual(MasteryRules.WinXp - MasteryRules.LossXp,
                MasteryRules.Compute(ClassId.Healer, healer, Ctx()).Xp - MasteryRules.Compute(ClassId.Healer, healer, Ctx(MatchOutcome.Loss)).Xp);
        }

        [Test]
        public void PerStatAndPerMatchCapsHold()
        {
            var huge = new RoleStats { KnockOuts = 500, HillSeconds = 9000, Delivered = 900, Healing = 99999, OreBanked = 900, PiecesUsed = 900, PiecesBuilt = 900, ExplosiveHits = 900, Demolished = 900, Purified = 99999 };
            for (int c = 0; c < 6; c++)
            {
                MasteryAward a = MasteryRules.Compute((ClassId)c, huge, Ctx());
                P($"{(ClassId)c} maxed: raw {a.Raw}, capped {a.Capped}, xp {a.Xp}, why {a.Why}");
                Assert.LessOrEqual(a.Xp, MasteryRules.PerMatchCap);
                foreach (MasteryLine l in a.Lines) Assert.LessOrEqual(l.Counted, MasteryRules.Cap[(int)l.Stat]);
                if (a.Raw > MasteryRules.PerMatchCap) Assert.AreEqual(MasteryWhy.MatchCap, a.Why);
            }
            // the fastest honest path to 20 still takes many matches
            int matches = (int)Math.Ceiling(MasteryRules.MaxXp / (double)MasteryRules.PerMatchCap);
            P($"at the match cap every match, mastery 20 takes at least {matches} matches");
            Assert.GreaterOrEqual(matches, 16);
        }

        [Test]
        public void BotsOnlyMatchesEarnHalfUpToADailyAllowance()
        {
            var s = new RoleStats { KnockOuts = 15, HillSeconds = 300 };
            MasteryAward people = MasteryRules.Compute(ClassId.Warrior, s, Ctx());
            MasteryAward bots = MasteryRules.Compute(ClassId.Warrior, s, Ctx(bots: true));
            P($"same play: with people {people.Xp}, bots only {bots.Xp} ({bots.Percent}%)");
            Assert.AreEqual(people.Capped * MasteryRules.BotMatchPercent / 100, bots.Xp);
            Assert.AreEqual(MasteryWhy.BotMatch, bots.Why);

            var m = new ClassMastery { Class = "Warrior" };
            int total = 0, n = 0;
            for (; n < 20; n++)
            {
                MasteryAward a = MasteryRules.Compute(ClassId.Warrior, s, Ctx(bots: true));
                MasteryRules.Apply(a, m, Ctx(bots: true));
                total += a.Xp;
                if (a.Xp == 0) break;
            }
            P($"bots-only grinding one day: {total} xp after {n} matches (allowance {MasteryRules.BotDailyAllowance})");
            Assert.AreEqual(MasteryRules.BotDailyAllowance, total);
            MasteryAward next = MasteryRules.Compute(ClassId.Warrior, s, Ctx(bots: true));
            MasteryRules.Apply(next, m, Ctx(bots: true));
            Assert.AreEqual(0, next.Xp);
            Assert.AreEqual(MasteryWhy.BotDailyAllowanceUsed, next.Why);
            // a match with people still pays the same day, and the next day the allowance is back
            MasteryAward real = MasteryRules.Compute(ClassId.Warrior, s, Ctx());
            MasteryRules.Apply(real, m, Ctx());
            Assert.AreEqual(people.Xp, real.Xp);
            MasteryAward tomorrow = MasteryRules.Compute(ClassId.Warrior, s, Ctx(bots: true, day: 20261118));
            MasteryRules.Apply(tomorrow, m, Ctx(bots: true, day: 20261118));
            P($"same day with people {real.Xp}; next day bots only {tomorrow.Xp}");
            Assert.AreEqual(bots.Xp, tomorrow.Xp);
        }

        [Test]
        public void PracticeUnfinishedAndIdleMatchesEarnNothing()
        {
            var s = new RoleStats { KnockOuts = 10, HillSeconds = 200 };
            MasteryAward practice = MasteryRules.Compute(ClassId.Miner, s, Ctx(practice: true));
            MasteryAward quit = MasteryRules.Compute(ClassId.Miner, s, Ctx(done: false));
            MasteryAward idle = MasteryRules.Compute(ClassId.Miner, default, Ctx());
            P($"practice {practice.Xp} ({practice.Why}), unfinished {quit.Xp} ({quit.Why}), idle win {idle.Xp}");
            Assert.AreEqual(0, practice.Xp);
            Assert.AreEqual(MasteryWhy.PracticeMode, practice.Why);
            Assert.AreEqual(0, quit.Xp);
            Assert.AreEqual(MasteryWhy.NotFinished, quit.Why);
            Assert.AreEqual(0, idle.Xp, "standing still through a win earns no finish or win bonus");
            var m = new ClassMastery { Xp = MasteryRules.MaxXp - 10 };
            MasteryAward top = MasteryRules.Compute(ClassId.Miner, s, Ctx());
            MasteryRules.Apply(top, m, Ctx());
            Assert.AreEqual(MasteryRules.MaxXp, m.Xp);
            Assert.AreEqual(10, top.Xp, "the award shows what was really added");
        }

        static RoleTally Tally(params TeamId[] teams)
        {
            var t = new RoleTally();
            t.SetTeamsForTests(teams);
            return t;
        }

        [Test]
        public void KnockOutsCountThreePerVictimAndOnlyOnEnemies()
        {
            RoleTally t = Tally(TeamId.Azure, TeamId.Crimson, TeamId.Crimson, TeamId.Azure);
            for (int i = 0; i < 10; i++) t.Feed(new SimEvent { Type = SimEventType.Death, A = 1, B = 0 });   // the same idle foe ten times
            t.Feed(new SimEvent { Type = SimEventType.Death, A = 2, B = 0 });
            t.Feed(new SimEvent { Type = SimEventType.Death, A = 3, B = 0 });                                // a teammate
            t.Feed(new SimEvent { Type = SimEventType.Death, A = 0, B = 0 });                                // self
            t.Feed(new SimEvent { Type = SimEventType.Death, A = 1, B = -1 });                               // the world
            RoleStats s = t.For(0);
            P($"10 KOs on one foe + 1 on another + 1 teammate + self: counted {s.KnockOuts}, raw {t.RawKnockOuts(0)}, deaths of actor 1: {t.For(1).Deaths}");
            Assert.AreEqual(RoleTally.KnockOutsPerVictim + 1, s.KnockOuts);
            Assert.AreEqual(11, t.RawKnockOuts(0));
            Assert.AreEqual(11, t.For(1).Deaths);
        }

        [Test]
        public void BuiltPiecesCountWhenATeammateUsesThemAndHealsOnlyOnOthers()
        {
            RoleTally t = Tally(TeamId.Azure, TeamId.Azure, TeamId.Crimson);
            t.Feed(new SimEvent { Type = SimEventType.PiecePlaced, A = 0, B = 7 });
            t.Feed(new SimEvent { Type = SimEventType.PiecePickedUp, A = 0, B = 7 });   // the builder itself
            t.Feed(new SimEvent { Type = SimEventType.PiecePickedUp, A = 2, B = 7 });   // an enemy
            t.Feed(new SimEvent { Type = SimEventType.PiecePickedUp, A = 1, B = 7 });   // a teammate: counts
            t.Feed(new SimEvent { Type = SimEventType.PiecePickedUp, A = 1, B = 300 }); // out of range slot
            t.Feed(new SimEvent { Type = SimEventType.HealLanded, A = 0, B = 0, Value = 50 });   // self-heal
            t.Feed(new SimEvent { Type = SimEventType.HealLanded, A = 2, B = 0, Value = 50 });   // healing an enemy
            t.Feed(new SimEvent { Type = SimEventType.HealLanded, A = 1, B = 0, Value = 40 });   // a teammate: counts
            t.Feed(new SimEvent { Type = SimEventType.BankDeposit, A = 0, Value = 12, Bio = BiomassType.Ore });
            t.Feed(new SimEvent { Type = SimEventType.BankDeposit, A = 0, Value = 99, Bio = null });
            t.Feed(null);
            RoleStats s = t.For(0);
            P($"builder: built {s.PiecesBuilt}, used by mates {s.PiecesUsed}; healing counted {s.Healing}; ore {s.OreBanked}");
            Assert.AreEqual(1, s.PiecesBuilt);
            Assert.AreEqual(1, s.PiecesUsed);
            Assert.AreEqual(40, s.Healing);
            Assert.AreEqual(12, s.OreBanked);
        }

        [Test]
        public void TheRewardTableCoversEveryLevelOfEveryClass()
        {
            for (int c = 0; c < 6; c++)
            {
                var cls = (ClassId)c;
                IReadOnlyList<MasteryReward> rows = MasteryRewards.For(cls);
                Assert.AreEqual(21, rows.Count, cls.ToString());
                for (int l = 1; l <= MasteryRules.MaxLevel; l++) Assert.IsTrue(rows.Any(r => r.Level == l), $"{cls} level {l} unlocks something");
                foreach (MasteryReward r in rows)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(r.Name), $"{cls} {r.Kind} {r.Id}");
                    Assert.IsTrue(MasteryRewards.Unlocked(r.Kind, r.Id, r.Level));
                    Assert.IsFalse(MasteryRewards.Unlocked(r.Kind, r.Id, r.Level - 1));
                }
                if (c == 0) P("warrior table: " + string.Join(" | ", rows.Select(r => $"{r.Level} {r.KindName} {r.Name}")));
            }
            Assert.AreEqual(CosmeticLook.TitleCount, MasteryRewards.Count(RewardKind.Title));
            Assert.AreEqual(CosmeticLook.FrameCount, MasteryRewards.Count(RewardKind.Frame));
            Assert.AreEqual(CosmeticLook.TrimCount, MasteryRewards.Count(RewardKind.Trim));
            Assert.AreEqual(CosmeticLook.EmblemCount, MasteryRewards.Count(RewardKind.Emblem));
            Assert.AreEqual(7, CosmeticLook.TitleCount);
            List<MasteryReward> between = MasteryRewards.Between(ClassId.Builder, 3, 5);
            P("builder 3 -> 5 unlocks: " + string.Join(", ", between.Select(r => r.Level + " " + r.Name)));
            Assert.AreEqual(new[] { 4, 5 }, between.Select(r => r.Level).ToArray());
            Assert.IsNull(MasteryRewards.NextAfter(ClassId.Builder, 20));
            Assert.AreEqual(2, MasteryRewards.NextAfter(ClassId.Builder, 1).Value.Level);
        }

        [Test]
        public void TheLookSteppersOnlyLandOnUnlockedItems()
        {
            var seen = new List<int>();
            int v = 0;
            for (int i = 0; i < 8; i++) { v = ProfileMenuScreen.NextUnlocked(RewardKind.Frame, v, 1, 10); seen.Add(v); }
            P("frame stepper at mastery 10: " + string.Join(",", seen));
            foreach (int id in seen) Assert.That(id, Is.InRange(0, 2), "only frames unlocked by mastery 10");
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2 }, seen.Distinct().ToArray(), "every unlocked frame is reachable");
            Assert.AreEqual(2, ProfileMenuScreen.NextUnlocked(RewardKind.Frame, 0, -1, 10), "left wraps to the last unlocked");
            Assert.AreEqual(0, ProfileMenuScreen.NextUnlocked(RewardKind.Trim, 0, 1, 1), "nothing unlocked yet stays at stock");
        }

        static float Hue(Color c)
        {
            Color.RGBToHSV(c, out float h, out _, out _);
            return h * 360f;
        }

        static float HueGap(float a, float b)
        {
            float d = Mathf.Abs(a - b) % 360f;
            return d > 180f ? 360f - d : d;
        }

        [Test]
        public void TrimsStayClearOfTeamAndThreatColours()
        {
            var avoid = new List<(string, float)>
            {
                ("azure", Hue(CMTokens.Team(TeamId.Azure))), ("crimson", Hue(CMTokens.Team(TeamId.Crimson))),
                ("colour-blind blue", 211f), ("colour-blind amber", 41f), ("corruption violet", 273f),
            };
            for (int i = 1; i < MasteryRewards.Trims.Length; i++)
            {
                TrimSpec t = MasteryRewards.Trims[i];
                Color.RGBToHSV(t.Tint, out float h, out float sat, out _);
                float hue = h * 360f;
                string gaps = string.Join(", ", avoid.Select(a => $"{a.Item1} {HueGap(hue, a.Item2):0}"));
                P($"trim {i} {t.Hex}: hue {hue:0}, saturation {sat:0.00}, strength {t.Strength:0.00}; gaps {gaps}");
                Assert.LessOrEqual(t.Strength, 0.7f);
                if (sat < 0.2f) continue;   // a grey, bone or soot reads as no hue at all
                foreach ((string name, float avoidHue) in avoid)
                    Assert.GreaterOrEqual(HueGap(hue, avoidHue), 35f, $"trim {i} vs {name}");
            }
            // the tint only ever applies to fixed colours: a team colour on a trimmed model is not touched by the shader
            // (mask code 0 only), which the shader keeps; here the CPU copy of the maths stays a gentle mix
            Color leather = new Color(0.54f, 0.35f, 0.24f);
            for (int i = 1; i < MasteryRewards.Trims.Length; i++)
            {
                Color o = MasteryRewards.Trims[i].Apply(leather);
                float dist = Mathf.Abs(o.r - leather.r) + Mathf.Abs(o.g - leather.g) + Mathf.Abs(o.b - leather.b);
                Assert.Greater(dist, 0.02f, $"trim {i} is visible");
            }
        }
    }
}
