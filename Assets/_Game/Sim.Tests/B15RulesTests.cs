using System;
using System.Diagnostics;
using CriticalMass.Net;
using CriticalMass.Net.Replay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 rule A (the comeback rule: RALLY and the respawn curve) and rule B (each hut level is a team vote between two
    /// side-grades): the rule pieces, the switches, the vote, every option's effect, bots with every combination, allocation,
    /// tick cost and the replay round trip. Every test prints one plain line per measured case: what, value, limit. The
    /// 24-seed 16v16 balance proof is B15RulesSoakTests (Slow).
    /// </summary>
    public sealed class B15RulesTests
    {
        static void Line(string what, object value, string limit) => Console.WriteLine("[b15 rules] " + what + ": " + value + " (limit " + limit + ")");

        static GameConfig Full(bool comeback = true, bool choices = true)
        {
            GameConfig c = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            c.Depth.Comeback.Enabled = comeback;
            c.Depth.HutChoices.Enabled = choices;
            return c;
        }

        static World Make(GameConfig cfg, int seed = 63, int teamSize = 5) =>
            World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });

        static void Give(World w, TeamId team, HutOption o) => w.HutVote.Mask[(int)team] |= 1 << (int)o;

        /// <summary>A Field world (only actor 0 alive) with every Azure actor a human (they vote only through StationBuy).</summary>
        static World HumanAzure(GameConfig cfg)
        {
            World w = DepthKit.Field(cfg, ClassId.Warrior, 20, 20);
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Team == TeamId.Azure) w.Actors[i].IsHuman = true;
            return w;
        }

        static void LevelUp(World w, TeamId team)
        {
            w.Hut.Ore[(int)team] += Blacksmith.NextCost(w, team);
            Blacksmith.LevelWhileStock(w, team);
        }

        // ------------------------------------------------------------------ switches

        [Test]
        public void BothSwitchesOffLeaveTheFullMatchBitIdenticalToTheBase()
        {
            // The Full golden of the base (integration/batch-13-cand1 a67be2bc, SimPhysicsSoakTests before b15). With both b15
            // switches off every hook returns its input, so the Full match must hash to it bit for bit. Re-pin these two values
            // together with the Full golden row whenever another rule item moves that row.
            const ulong Base16 = 0xC0111682BA7C9FD6UL, Base5 = 0x75A137C63CDE1377UL;
            Action<GameConfig> off = c => { c.ApplyRules(RulesProfile.Full); c.Depth.Comeback.Enabled = false; c.Depth.HutChoices.Enabled = false; };
            ulong a = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, off);
            ulong b = MatchHash.Run(42, 5, 14500, ClassId.Warrior, null, off);
            Line("Full with Comeback.Enabled and HutChoices.Enabled false, 16v16 seed 1 2:00", "0x" + a.ToString("X16"), "0x" + Base16.ToString("X16"));
            Line("Full with both off, 5v5 seed 42 whole match", "0x" + b.ToString("X16"), "0x" + Base5.ToString("X16"));
            Assert.AreEqual(Base16, a, "16v16 Full with both b15 switches off");
            Assert.AreEqual(Base5, b, "5v5 Full with both b15 switches off");
        }

        [Test]
        public void ClassicAndPhysicsOnlyNeverSeeEitherRule()
        {
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                ulong on = MatchHash.Run(1, 16, 1800, ClassId.Warrior, null, c => c.ApplyRules(p));
                ulong off = MatchHash.Run(1, 16, 1800, ClassId.Warrior, null, c => { c.ApplyRules(p); c.Depth.Comeback.Enabled = false; c.Depth.HutChoices.Enabled = false; });
                GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(p);
                World w = Make(cfg);
                Line(p + " 16v16 seed 1 1:00 switches on / off", "0x" + on.ToString("X16") + " / 0x" + off.ToString("X16") + ", Comeback.On " + Comeback.On(w) + ", HutChoices.On " + HutChoices.On(w), "equal, both false");
                Assert.AreEqual(off, on, p + ": the b15 switches change nothing");
                Assert.IsFalse(Comeback.On(w));
                Assert.IsFalse(HutChoices.On(w));
            }
            Assert.IsTrue(Comeback.On(Make(Full())) && HutChoices.On(Make(Full())), "both default ON in Full");
            Assert.IsTrue(Comeback.On(Make(GameConfig.CreateDefault())) && HutChoices.On(Make(GameConfig.CreateDefault())), "the game's default config runs both");
        }

        // ------------------------------------------------------------------ rule A: RALLY

        static double Hold(World w, int ticks)
        {
            double before = w.Match.HillSeconds.Azure;
            DepthKit.Steps(w, ticks);
            return w.Match.HillSeconds.Azure - before;
        }

        [Test]
        public void RallyCountsTheTrailingOwnersHillTimeOneAndAQuarterTimes()
        {
            GameConfig cfg = Full();
            World w = DepthKit.Field(cfg, ClassId.Warrior, 56, 40);
            double hz = cfg.SimHz;
            w.Match.HillSeconds.Crimson = 40;   // Azure 40 s behind (threshold 15 s)
            double got = Hold(w, (int)hz);
            Line("one second on the hill, 40 s behind", got.ToString("0.000000") + " s, bonus " + Comeback.BonusSeconds(w, TeamId.Azure).ToString("0.000") + " s", "1.250 s");
            Assert.AreEqual(1.25, got, 1e-9);
            Assert.IsTrue(Comeback.Earning(w, TeamId.Azure));
            Assert.AreEqual(w.Tick - (int)hz + 1, w.Rally.FirstTick[0], "first rally tick recorded once");

            World x = DepthKit.Field(cfg, ClassId.Warrior, 56, 40);
            x.Match.HillSeconds.Crimson = 10;   // behind, but not by the threshold
            double plain = Hold(x, (int)hz);
            Line("one second on the hill, 10 s behind", plain.ToString("0.000000") + " s", "1.000 s (no rally inside the threshold)");
            Assert.AreEqual(1.0, plain, 1e-9);
            Assert.IsFalse(Comeback.Trailing(x, TeamId.Azure));

            World y = DepthKit.Field(Full(comeback: false), ClassId.Warrior, 56, 40);
            y.Match.HillSeconds.Crimson = 40;
            double off = Hold(y, (int)hz);
            Line("one second on the hill, 40 s behind, Comeback.Enabled false", off.ToString("0.000000") + " s", "1.000 s");
            Assert.AreEqual(1.0, off, 1e-9);
        }

        [Test]
        public void RallyStopsAtTheCapAndNeverCarriesATeamIntoTheLead()
        {
            GameConfig cfg = Full();
            double hz = cfg.SimHz;
            World w = DepthKit.Field(cfg, ClassId.Warrior, 56, 40);
            w.Match.HillSeconds.Crimson = 1000;   // far behind: only the cap limits it
            DepthKit.Steps(w, (int)(400 * hz));
            double bonus = Comeback.BonusSeconds(w, TeamId.Azure);
            Line("400 s held 1000 s behind: rally bonus", bonus.ToString("0.000") + " s, cap left " + Comeback.CapLeftSeconds(w, TeamId.Azure).ToString("0.000"), "45.000 s, 0 left");
            Assert.AreEqual(45.0, bonus, 1e-9);
            Assert.IsFalse(Comeback.Trailing(w, TeamId.Azure), "cap spent: no longer shown as rallying");

            World z = DepthKit.Field(cfg, ClassId.Warrior, 56, 40);
            z.Match.HillSeconds.Crimson = 30;   // 30 s behind: rally closes it to the threshold, then plain seconds
            double minGap = double.PositiveInfinity;
            int flips = 0;
            for (int t = 0; t < (int)(60 * hz); t++)
            {
                double gapBefore = Comeback.Deficit(z, TeamId.Azure);
                DepthKit.Steps(z, 1);
                double gapAfter = Comeback.Deficit(z, TeamId.Azure);
                if (gapBefore > cfg.Depth.Comeback.TrailThresholdSeconds && gapAfter <= cfg.Depth.Comeback.TrailThresholdSeconds) flips++;
                minGap = Math.Min(minGap, gapAfter);
            }
            double rallied = Comeback.BonusSeconds(z, TeamId.Azure);
            Line("60 s held from 30 s behind: rally bonus, then the gap", rallied.ToString("0.000") + " s bonus, gap " + Comeback.Deficit(z, TeamId.Azure).ToString("0.000") + " s (crossed the threshold " + flips + " time)",
                "bonus ~3 s (15 s closed at x1.25 = 0.25 x 12 s), the lead taken only by plain seconds");
            Assert.LessOrEqual(rallied, 3.0 + 1.0 / hz);
            Assert.AreEqual(1, flips);
        }

        // ------------------------------------------------------------------ rule A: the respawn curve

        [Test]
        public void TheRespawnWaitGrowsFromThreeToEightSecondsOverTheMatch()
        {
            GameConfig cfg = Full();
            World w = Make(cfg);
            int end = (int)Math.Round(cfg.MatchSeconds * cfg.SimHz);
            double[] at = new double[3];
            int[] ticks = { 0, end / 2, end };
            for (int k = 0; k < 3; k++) { w.Tick = ticks[k]; at[k] = Comeback.RespawnDelay(w, TeamId.Azure); }
            Line("respawn wait at 0:00 / 4:00 / 8:00 (linear)", at[0] + " / " + at[1] + " / " + at[2] + " s", "3 / 5.5 / 8 s");
            Assert.AreEqual(3.0, at[0], 1e-9);
            Assert.AreEqual(5.5, at[1], 1e-9);
            Assert.AreEqual(8.0, at[2], 1e-9);

            cfg.Depth.Comeback.CurvePower = 2;
            w.Tick = end / 2;
            double quad = Comeback.RespawnDelay(w, TeamId.Azure);
            Line("respawn wait at 4:00 with CurvePower 2", quad.ToString("0.0000") + " s", "127 ticks = 4.2333 s (90 + 150 x 1/4, integer ticks)");
            Assert.AreEqual(127 / cfg.SimHz, quad, 1e-9);
            cfg.Depth.Comeback.CurvePower = 1;

            Give(w, TeamId.Azure, HutOption.QuickReturn);
            w.Tick = 0;
            double quickEarly = Comeback.RespawnDelay(w, TeamId.Azure);
            w.Tick = end;
            double quickLate = Comeback.RespawnDelay(w, TeamId.Azure), crimsonLate = Comeback.RespawnDelay(w, TeamId.Crimson);
            Line("QUICK RETURN at 0:00 / 8:00, the other team at 8:00", quickEarly + " / " + quickLate + " / " + crimsonLate + " s", "2 (floor) / 6.5 / 8 s");
            Assert.AreEqual(2.0, quickEarly, 1e-9);
            Assert.AreEqual(6.5, quickLate, 1e-9);
            Assert.AreEqual(8.0, crimsonLate, 1e-9);

            World off = Make(Full(comeback: false, choices: false));
            off.Tick = end;
            Assert.AreEqual(off.Config.Respawn.DelaySeconds, Comeback.RespawnDelay(off, TeamId.Azure), "both off: exactly the flat delay");
            World offQuick = Make(Full(comeback: false));
            Give(offQuick, TeamId.Azure, HutOption.QuickReturn);
            Line("QUICK RETURN with the comeback rule off", Comeback.RespawnDelay(offQuick, TeamId.Azure) + " s", "3.5 s (5 - 1.5)");
            Assert.AreEqual(3.5, Comeback.RespawnDelay(offQuick, TeamId.Azure), 1e-9);
        }

        [Test]
        public void AKnockedOutFighterWaitsTheCurveNotTheFlatFiveSeconds()
        {
            GameConfig cfg = Full();
            World w = DepthKit.Field(cfg, ClassId.Warrior, 30, 30);
            int enemy = DepthKit.Enemy(w, 0);
            DepthKit.Put(w, enemy, 31, 30);
            w.Tick = 10;
            Damage.ApplyDamage(w, 0, 1e6, enemy, 31, 30);
            Line("knock-out at tick 10: RespawnIn", w.Actors[0].RespawnIn.ToString("0.000") + " s", "about 3 s");
            Assert.IsFalse(w.Actors[0].Alive);
            Assert.AreEqual(Comeback.RespawnDelay(w, TeamId.Azure), w.Actors[0].RespawnIn, 1e-12);
            Assert.Less(w.Actors[0].RespawnIn, 3.01);
        }

        // ------------------------------------------------------------------ rule B: the vote

        [Test]
        public void AHutLevelOpensAVoteThatBotsDecideInsideTheWindow()
        {
            GameConfig cfg = Full();
            World w = Make(cfg, 63, 16);
            LevelUp(w, TeamId.Crimson);   // Crimson: 16 bots
            int window = (int)Math.Round(cfg.Depth.HutChoices.VoteSeconds * cfg.SimHz);
            DepthKit.Steps(w, 1);
            Assert.AreEqual(1, HutChoices.VoteLevel(w, TeamId.Crimson), "the vote opened with the level");
            int ticks = 1;
            while (HutChoices.VoteLevel(w, TeamId.Crimson) != 0 && ticks <= window + 2) { DepthKit.Steps(w, 1); ticks++; }
            HutOption got = HutChoices.Chosen(w, TeamId.Crimson, 1);
            Line("16 bots vote on level 1: decided after", ticks + " ticks, " + HutChoiceNames.Name[(int)got] + " (" + w.HutVote.LastA[1] + " / " + w.HutVote.LastB[1] + " votes)", "within " + window + " ticks");
            Assert.LessOrEqual(ticks, window + 1);
            Assert.IsTrue(got == HutOption.QuickReturn || got == HutOption.ToughWalls);
            Assert.IsTrue(HutChoices.Has(w, TeamId.Crimson, got));
            Assert.AreEqual(1, w.HutVote.Decided[1]);
            Assert.Greater(w.HutVote.LastA[1] + w.HutVote.LastB[1], 8, "a strict majority of the 16 voted");
            Assert.AreEqual(0, w.HutVote.Decided[0], "the other team has no level and no vote");
        }

        [Test]
        public void HumanVotesWinByMajorityAndCloseTheVoteAtOnce()
        {
            World w = HumanAzure(Full());
            LevelUp(w, TeamId.Azure);
            for (int i = 0; i < 3; i++) w.Inputs[i].StationBuy = 2;   // 3 of the 5 Azure players press the right card
            DepthKit.Steps(w, 1);
            Line("3 of 5 humans press the right card", HutChoiceNames.Name[(int)HutChoices.Chosen(w, TeamId.Azure, 1)] + ", vote open " + HutChoices.VoteLevel(w, TeamId.Azure), "TOUGH WALLS, closed at once");
            Assert.AreEqual(HutOption.ToughWalls, HutChoices.Chosen(w, TeamId.Azure, 1));
            Assert.AreEqual(0, HutChoices.VoteLevel(w, TeamId.Azure));
            for (int i = 0; i < 3; i++) Assert.AreEqual(0, w.Inputs[i].StationBuy, "the vote press is taken");

            World x = HumanAzure(Full());
            LevelUp(x, TeamId.Azure);
            x.Inputs[0].StationBuy = 1;
            x.Inputs[1].StationBuy = 2;
            x.Inputs[2].StationBuy = 2;
            DepthKit.Steps(x, 1);
            x.Inputs[1].StationBuy = 1;   // a change of mind inside the window: 2 left, 1 right, no majority of 5 yet
            DepthKit.Steps(x, 1);
            Assert.AreEqual(1, HutChoices.VoteLevel(x, TeamId.Azure), "2 of 5 is not a majority: the window runs on");
            Assert.AreEqual(2, HutChoices.Votes(x, TeamId.Azure, 1));
            DepthKit.Steps(x, (int)Math.Round(x.Config.Depth.HutChoices.VoteSeconds * x.Config.SimHz));
            Line("2 left / 1 right after a change of mind, at the end of the window", HutChoiceNames.Name[(int)HutChoices.Chosen(x, TeamId.Azure, 1)], "QUICK RETURN");
            Assert.AreEqual(HutOption.QuickReturn, HutChoices.Chosen(x, TeamId.Azure, 1));
        }

        [Test]
        public void ATieOrNoVotesTakesTheSeededDefault()
        {
            int lefts = 0;
            for (int seed = 1; seed <= 8; seed++)
            {
                World w = DepthKit.Field(Full(), ClassId.Warrior, 20, 20, seed);
                for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Team == TeamId.Azure) w.Actors[i].IsHuman = true;
                LevelUp(w, TeamId.Azure);
                int window = (int)Math.Round(w.Config.Depth.HutChoices.VoteSeconds * w.Config.SimHz);
                DepthKit.Steps(w, window + 1);
                int card = w.HutVote.Pick[0];
                Assert.AreEqual(HutChoices.DefaultCard(w, TeamId.Azure, 1), card, "seed " + seed);
                Assert.AreEqual(1, w.HutVote.Empty);
                Assert.AreEqual(1, w.HutVote.LastDefault[0]);
                if (card == 1) lefts++;
            }
            Line("no votes cast over 8 seeds: the seeded default took the left card", lefts + " of 8", "the default, both cards used");
            Assert.That(lefts, Is.InRange(1, 7));
        }

        [Test]
        public void LevelsQueueTheirVotesAndTheFlatBumpIsGone()
        {
            World w = HumanAzure(Full());
            for (int l = 0; l < 3; l++) LevelUp(w, TeamId.Azure);   // three levels banked at once
            Assert.AreEqual(3, Blacksmith.Level(w, TeamId.Azure));
            int window = (int)Math.Round(w.Config.Depth.HutChoices.VoteSeconds * w.Config.SimHz);
            DepthKit.Steps(w, 3 * (window + 1) + 3);
            Line("three levels at once: decided, scale, Warrior max HP", w.HutVote.Decided[0] + ", " + Blacksmith.StatScale(w, TeamId.Azure) + ", " + w.Actors[0].MaxHp,
                "3, 1 (no flat +15%), the class row " + w.Config.Class(ClassId.Warrior).MaxHp);
            Assert.AreEqual(3, w.HutVote.Decided[0]);
            Assert.AreEqual(1.0, Blacksmith.StatScale(w, TeamId.Azure));
            Assert.AreEqual(w.Config.Class(ClassId.Warrior).MaxHp, w.Actors[0].MaxHp);
            for (int l = 1; l <= 3; l++) Assert.AreEqual(l, HutChoices.LevelOf(HutChoices.Chosen(w, TeamId.Azure, l)));

            World flat = DepthKit.Field(Full(choices: false), ClassId.Warrior, 20, 20);
            for (int l = 0; l < 3; l++) LevelUp(flat, TeamId.Azure);
            Line("same with HutChoices.Enabled false: scale", Blacksmith.StatScale(flat, TeamId.Azure), "1.15 (the b13 flat bump)");
            Assert.AreEqual(1.15, Blacksmith.StatScale(flat, TeamId.Azure), 1e-12);
        }

        // ------------------------------------------------------------------ rule B: every option's effect

        [Test]
        public void EveryOptionDoesWhatItsCardSays()
        {
            GameConfig cfg = Full();
            HutChoicesConfig h = cfg.Depth.HutChoices;
            World w = DepthKit.Field(cfg, ClassId.Warrior, 20, 20);
            int enemy = DepthKit.Enemy(w, 0);
            foreach (HutOption o in (HutOption[])Enum.GetValues(typeof(HutOption))) Assert.IsFalse(HutChoices.Has(w, TeamId.Azure, o));
            Assert.AreEqual(1.0, HutChoices.WallScale(w, TeamId.Azure));
            Give(w, TeamId.Azure, HutOption.ToughWalls);
            Give(w, TeamId.Azure, HutOption.RichVeins);
            Give(w, TeamId.Azure, HutOption.ShortFuse);
            Give(w, TeamId.Azure, HutOption.WideMend);
            Line("TOUGH WALLS / RICH VEINS mine ticks / SHORT FUSE / WIDE MEND", HutChoices.WallScale(w, TeamId.Azure) + " / " + Blacksmith.MineTicksFor(w, TeamId.Azure) + " vs "
                + Blacksmith.MineTicksFor(w, TeamId.Crimson) + " / " + HutChoices.FuseScale(w, TeamId.Azure) + " / " + HutChoices.MendScale(w, TeamId.Azure), "1.5 / 18 vs 30 / 0.7 / 1.4");
            Assert.AreEqual(h.ToughWallScale, HutChoices.WallScale(w, TeamId.Azure));
            Assert.AreEqual(1.0, HutChoices.WallScale(w, TeamId.Crimson));
            Assert.AreEqual(Durations.T30(cfg, cfg.Depth.Hut.MineTicks * h.RichVeinsMineScale), Blacksmith.MineTicksFor(w, TeamId.Azure));
            Assert.AreEqual(Blacksmith.MineTicks(cfg), Blacksmith.MineTicksFor(w, TeamId.Crimson));
            Assert.AreEqual(h.ShortFuseScale, HutChoices.FuseScale(w, TeamId.Azure));
            Assert.AreEqual(h.WideMendScale, HutChoices.MendScale(w, TeamId.Azure));

            // SHIELD WALL, BLAST GUARD, KAIJU HUNTERS through the one damage hook
            w.Actors[0].AbilityActive = 1;
            double plainShield = B13Hooks.DamageScale(w, enemy, 0, DamageCause.Melee);
            Give(w, TeamId.Azure, HutOption.ShieldWall);
            double shield = B13Hooks.DamageScale(w, enemy, 0, DamageCause.Melee);
            w.Actors[0].AbilityActive = 0;
            double unshielded = B13Hooks.DamageScale(w, enemy, 0, DamageCause.Melee);
            Give(w, TeamId.Azure, HutOption.BlastGuard);
            double blast = B13Hooks.DamageScale(w, enemy, 0, DamageCause.Explosion), blastFromKaiju = B13Hooks.DamageScale(w, Damage.KaijuSource, 0, DamageCause.Explosion);
            double kaiju0 = B13Hooks.DamageScale(w, 0, -1, DamageCause.None);
            Give(w, TeamId.Azure, HutOption.KaijuHunters);
            double kaiju = B13Hooks.DamageScale(w, 0, -1, DamageCause.None), kaijuEnemy = B13Hooks.DamageScale(w, enemy, -1, DamageCause.None);
            Line("SHIELD WALL shielded / plain / unshielded", shield + " / " + plainShield + " / " + unshielded, "0.8 / 1 / 1");
            Line("BLAST GUARD explosion from a fighter / from the world", blast + " / " + blastFromKaiju, "0.75 / 0.75");
            Line("KAIJU HUNTERS damage to the Kaiju: before / after / the other team", kaiju0 + " / " + kaiju + " / " + kaijuEnemy, "1 / 1.25 / 1");
            Assert.AreEqual(1.0, plainShield);
            Assert.AreEqual(h.ShieldWallScale, shield, 1e-12);
            Assert.AreEqual(1.0, unshielded);
            Assert.AreEqual(h.BlastGuardScale, blast, 1e-12);
            Assert.AreEqual(h.BlastGuardScale, blastFromKaiju, 1e-12);
            Assert.AreEqual(1.0, kaiju0);
            Assert.AreEqual(h.KaijuHunterScale, kaiju, 1e-12);
            Assert.AreEqual(1.0, kaijuEnemy);

            // SWIFT FEET off the hill only
            Give(w, TeamId.Azure, HutOption.SwiftFeet);
            double off = HutChoices.SpeedScale(w, 0);
            DepthKit.Put(w, 0, w.Map.Hill.X, w.Map.Hill.Z);
            double on = HutChoices.SpeedScale(w, 0);
            Line("SWIFT FEET off the hill / on the hill", off + " / " + on, "1.1 / 1");
            Assert.AreEqual(h.SwiftFeetScale, off, 1e-12);
            Assert.AreEqual(1.0, on);
        }

        [Test]
        public void HillAnchorWinsTiesOnTheHill()
        {
            HillState[] got = new HillState[3];
            for (int k = 0; k < 3; k++)
            {
                World w = DepthKit.Field(Full(), ClassId.Warrior, 56, 40);
                int enemy = DepthKit.Enemy(w, 0);
                DepthKit.Put(w, enemy, w.Map.Hill.X + 1, w.Map.Hill.Z);
                if (k == 1) Give(w, TeamId.Azure, HutOption.HillAnchor);
                if (k == 2) { Give(w, TeamId.Azure, HutOption.HillAnchor); Give(w, TeamId.Crimson, HutOption.HillAnchor); }
                DepthKit.Steps(w, 1);
                got[k] = w.Match.HillState;
            }
            Line("one against one on the hill: no anchor / Azure anchor / both anchors", got[0] + " / " + got[1] + " / " + got[2], "Contested / Azure / Contested");
            Assert.AreEqual(HillState.Contested, got[0]);
            Assert.AreEqual(HillState.Azure, got[1]);
            Assert.AreEqual(HillState.Contested, got[2]);
        }

        /// <summary>BuilderWallTests' staging: 16v16 seed 1337 Full, only actor 0 (a Builder) alive, nodes moved off the map.</summary>
        static World WallField()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = Full() });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1;
                w.Depth.WasAlive[j] = 0;
            }
            w.Actors[0].Cls = ClassId.Builder;
            foreach (ResourceNode nd in w.Nodes) { nd.X = -100; nd.Z = -100; }
            if (w.Depth.Sources != null) foreach (SourceNode sn in w.Depth.Sources) if (sn != null) { sn.X = -100; sn.Z = -100; }
            w.Tick = 100;
            for (int j = 0; j < w.Walls.Tool.Length; j++) w.Walls.Tool[j] = 1;
            int[] t = w.Config.Depth.Build.TierAtHutLevel;
            for (int k = 0; k < t.Length; k++) t[k] = 0;
            return w;
        }

        [Test]
        public void ToughWallsBuildWallsWithMoreHpAndLife()
        {
            double[] hp = new double[2], life = new double[2];
            for (int arm = 0; arm < 2; arm++)
            {
                World w = WallField();
                if (arm == 1) Give(w, TeamId.Azure, HutOption.ToughWalls);
                CriticalMass.Sim.Grid.MapDef m = w.Map;
                int ox = -1, oz = -1, r = 4;
                double best = double.MaxValue;
                for (int cz = r + 1; cz < m.Height - r - 1; cz++)
                for (int cx = r + 1; cx < m.Width - r - 1; cx++)
                {
                    bool ok = true;
                    for (int dz = -r; dz <= r && ok; dz++)
                    for (int dx = -r; dx <= r && ok; dx++)
                        ok = m.Cells[(cz + dz) * m.Width + cx + dx] == Cell.Floor && BuilderWalls.CellRefusal(w, cx + dx, cz + dz) == 0;
                    if (!ok) continue;
                    double d = (cx - m.Width / 2.0) * (cx - m.Width / 2.0) + (cz - m.Height / 2.0) * (cz - m.Height / 2.0);
                    if (d < best) { best = d; ox = cx; oz = cz; }
                }
                Assert.GreaterOrEqual(ox, 0, "an open buildable area exists");
                DepthKit.Put(w, 0, ox + 0.5, oz + 0.5);
                Assert.AreEqual(BuilderWalls.Built, BuilderWalls.Build(w, 0, ox + 2.5, oz + 0.5));
                for (int s2 = 0; s2 < w.Barricades.Length; s2++)
                {
                    if (!BuilderWalls.IsWallSlot(w, s2) || w.Barricades[s2].Cx != ox + 2 || w.Barricades[s2].Cz != oz) continue;
                    hp[arm] = w.Barricades[s2].Hp;
                    life[arm] = w.Barricades[s2].Ttl;
                }
            }
            Line("a short wall's HP and life without / with TOUGH WALLS", hp[0] + " / " + hp[1] + " HP, " + life[0].ToString("0.0") + " / " + life[1].ToString("0.0") + " s", "x1.5 both");
            Assert.AreEqual(hp[0] * 1.5, hp[1], 1e-9);
            Assert.Greater(hp[0], 0);
            Assert.AreEqual(life[0] * 1.5, life[1], 1e-9);
        }

        // ------------------------------------------------------------------ bots with every combination

        [Test]
        public void BotsPlayWithEveryCombinationOfPicks()
        {
            int combos = 1 << HutChoiceState.Levels, played = 0, minKills = int.MaxValue;
            double minOwned = double.PositiveInfinity;
            for (int k = 0; k < combos; k++)
            {
                World w = Make(Full(), 100 + k, 5);
                for (int l = 1; l <= HutChoiceState.Levels; l++)
                {
                    Give(w, TeamId.Azure, HutChoices.OptionAt(l, ((k >> (l - 1)) & 1) + 1));
                    Give(w, TeamId.Crimson, HutChoices.OptionAt(l, 2 - ((k >> (l - 1)) & 1)));
                }
                int kills = 0;
                DepthKit.RunBots(w, 3600, e => { if (e.Type == SimEventType.Death) kills++; });
                double owned = w.Match.HillSeconds.Azure + w.Match.HillSeconds.Crimson;
                Assert.AreEqual(3600, w.Tick, "combination " + k + ": the match ran");
                Assert.Greater(kills, 0, "combination " + k + ": bots fought");
                Assert.Greater(owned, 10, "combination " + k + ": bots held the hill");
                minKills = Math.Min(minKills, kills);
                minOwned = Math.Min(minOwned, owned);
                played++;
            }
            Line("5v5 2:00 with each of the 32 pick combinations (Azure one side, Crimson the mirror)", played + " played, fewest knock-outs " + minKills + ", least hill owned " + minOwned.ToString("0.0") + " s", "every one plays: knock-outs > 0, hill > 10 s");
        }

        // ------------------------------------------------------------------ allocation and cost

        [Test]
        public void AVoteAndARallyAllocateNothingAndCostLittle()
        {
            double[] us = new double[2];
            long bytes = -1, offBytes = -1;
            int votes = 0;
            double rally = 0;
            for (int arm = 0; arm < 2; arm++)
            {
                bool on = arm == 0;
                World w = Make(Full(on, on), 4242, 16);
                InputFrame scratch = SimInput.EmptyInput();
                for (int s = 0; s < 600; s++)
                {
                    if (s == 100) { LevelUp(w, TeamId.Azure); LevelUp(w, TeamId.Crimson); }   // one full vote in the warm-up
                    MatchTick.Advance(w, null, scratch);
                }
                if (on)
                {
                    LevelUp(w, TeamId.Azure);   // a second vote and a rally in the measured window
                    w.Match.HillSeconds.Crimson += 60;
                }
                long b0 = GC.GetAllocatedBytesForCurrentThread();
                long t0 = Stopwatch.GetTimestamp();
                for (int s = 0; s < 300; s++) MatchTick.Advance(w, null, scratch);
                long t1 = Stopwatch.GetTimestamp();
                long b1 = GC.GetAllocatedBytesForCurrentThread();
                us[arm] = (t1 - t0) * 1e6 / Stopwatch.Frequency / 300.0;
                if (on) { bytes = b1 - b0; votes = w.HutVote.Closed; rally = Comeback.BonusSeconds(w, TeamId.Azure); }
                else offBytes = b1 - b0;
            }
            Line("16v16 seed 4242 Full, 600 warm-up ticks then 300 ticks with a vote open and RALLY possible: allocated", bytes + " B (votes closed " + votes + ", rally " + rally.ToString("0.00") + " s)", "0 B over the control");
            Line("same 300 ticks with both rules off (the base's own allocation)", offBytes + " B", "printed");
            Line("mean sim microseconds per tick on / off (different matches; printed)", us[0].ToString("0.0") + " / " + us[1].ToString("0.0"), "printed");
            Assert.LessOrEqual(bytes, offBytes, "the b15 rules add no allocation over the control");
            Assert.GreaterOrEqual(votes, 2);
        }

        [Test]
        public void TheB15CodeCostsUnderThreePercentOfATick()
        {
            // Same job, same world: time one 16v16 match tick, then the b15 work of a tick on its own, as often as a tick does it:
            // the vote and RALLY once, the speed hook for every actor, the damage hook for 8 hits, the respawn wait for 1
            // knock-out (bot matches: about 0.7 knock-outs and 3 hits a tick at 16v16), and print the share.
            World w = Make(Full(), 4242, 16);
            InputFrame scratch = SimInput.EmptyInput();
            for (int s = 0; s < 1800; s++) MatchTick.Advance(w, null, scratch);
            LevelUp(w, TeamId.Azure);
            for (int i = 0; i < 2; i++) Give(w, (TeamId)i, HutOption.SwiftFeet);
            const int N = 600;
            long t0 = Stopwatch.GetTimestamp();
            for (int s = 0; s < N; s++) MatchTick.Advance(w, null, scratch);
            double tick = (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency / N;
            double sink = 0;
            // warm the b15 paths once (JIT), then time them
            sink += B15Work(w, 1);
            long t1 = Stopwatch.GetTimestamp();
            sink += B15Work(w, N);
            double b15 = (Stopwatch.GetTimestamp() - t1) * 1e6 / Stopwatch.Frequency / N;
            double share = b15 / Math.Max(1e-9, tick);
            Line("16v16 seed 4242 Full: one tick / the b15 calls of a tick timed on their own (the worst case: a vote open every tick, SWIFT FEET on both teams; a guard, the +3 % gate is the 24-match soak)",
                tick.ToString("0.0") + " us / " + b15.ToString("0.00") + " us = " + (share * 100).ToString("0.00") + " % (sink " + (sink != 0 ? "ok" : "0") + ")", "10 % or less (guard)");
            Assert.LessOrEqual(share, 0.10);
            // Printed: the same job runs the same 2:00 16v16 match with both rules off and both on, interleaved three times each, the
            // fastest of each compared. The two matches play differently (3 s respawns early keep more fighters up), so one match
            // is not a gate; the +3 % gate is the whole-match mean over the 24-seed soak (B15RulesSoakTests).
            double bestOff = double.MaxValue, bestOn = double.MaxValue;
            for (int rep = 0; rep < 3; rep++)
            {
                bestOff = Math.Min(bestOff, TimeMatch(Full(false, false)));
                bestOn = Math.Min(bestOn, TimeMatch(Full()));
            }
            double extra = bestOn / bestOff - 1;
            Line("16v16 seed 4242 Full 2:00, same job, best of 3 interleaved: microseconds per tick both off / both on",
                bestOff.ToString("0.0") + " / " + bestOn.ToString("0.0") + " = " + (extra >= 0 ? "+" : "") + (extra * 100).ToString("0.00") + " %", "printed (one match)");
        }

        static double TimeMatch(GameConfig cfg)
        {
            World w = Make(cfg, 4242, 16);
            InputFrame scratch = SimInput.EmptyInput();
            for (int s = 0; s < 30; s++) MatchTick.Advance(w, null, scratch);
            const int N = 3600;
            long t0 = Stopwatch.GetTimestamp();
            for (int s = 0; s < N; s++) MatchTick.Advance(w, null, scratch);
            return (Stopwatch.GetTimestamp() - t0) * 1e6 / Stopwatch.Frequency / N;
        }

        /// <summary>The b15 code a tick runs, n times: the vote, RALLY, the hill tie, the speed hook per actor, 8 hits, 1 knock-out, the mine time.</summary>
        static double B15Work(World w, int n)
        {
            double sink = 0;
            for (int s = 0; s < n; s++)
            {
                HutChoices.VoteSystem(w);
                sink += Comeback.RallyCredit(w, HillState.Crimson, 0) + HutChoices.AnchorTie(w);
                for (int i = 0; i < w.ActorCount; i++) sink += HutChoices.SpeedScale(w, i);
                for (int i = 0; i < 8; i++) sink += HutChoices.DamageScale(w, i, w.ActorCount - 1 - i, DamageCause.Melee);
                sink += Comeback.RespawnDelay(w, TeamId.Azure) + HutChoices.MineScale(w, TeamId.Azure) + HutChoices.MineScale(w, TeamId.Crimson);
            }
            return sink;
        }

        // ------------------------------------------------------------------ determinism and replay

        [Test]
        public void SameSeedTwiceIsIdenticalWithBothRulesOn()
        {
            Action<GameConfig> full = c => c.ApplyRules(RulesProfile.Full);
            ulong a = MatchHash.Run(4242, 16, 9000, ClassId.Warrior, null, full);
            ulong b = MatchHash.Run(4242, 16, 9000, ClassId.Warrior, null, full);
            Line("16v16 seed 4242 Full 5:00 twice", "0x" + a.ToString("X16") + " / 0x" + b.ToString("X16"), "equal");
            Assert.AreEqual(a, b);
        }

        [Test]
        public void AReplayWithVotesAndRallyPlaysBackToTheSameFingerprints()
        {
            var s = new ReplayStart
            {
                Kind = ReplayKind.Offline,
                Basis = ConfigBasis.Default,
                RulesProfileId = (int)RulesProfile.Full,
                Seed = 4242,
                TeamSize = 16,
                HumanTeam = TeamId.Azure,
                HumanClass = ClassId.Warrior,
                Pace = 1,
                ExtraHumans = new int[0],
                ExtraHumanClasses = new ClassId[0],
            };
            var header = new ReplayHeader { BuildVersion = "1.0.test", BuildCommit = "b15rules", MapName = "SKYBREAK BASIN", ModeLabel = "CUSTOM", Start = s };
            World w = World.CreateWorld(s.ToWorldOptions(s.BuildConfig()));
            Assert.IsTrue(Comeback.On(w) && HutChoices.On(w), "the replay's Full config runs both rules");
            var rec = new ReplayRecorder();
            Assert.IsTrue(rec.Begin(w, header), rec.Refused);
            MatchTick.Tap = rec;
            InputFrame scratch = SimInput.EmptyInput(), human = SimInput.EmptyInput();
            int presses = 0;
            try
            {
                for (int t = 0; t < 10800 && w.Match.Phase != MatchPhase.Ended; t++)
                {
                    bool voting = HutChoices.VoteLevel(w, TeamId.Azure) > 0;
                    if (voting)
                    {
                        // the human plays himself during a vote: stands still and presses a card once a second
                        human.MoveX = 0;
                        human.MoveZ = 0;
                        human.AimX = w.X[0];
                        human.AimZ = w.Z[0];
                        human.Attack = human.Ability = human.Interact = human.Terraform = human.Jump = human.TerraformSwitch = human.Dodge = false;
                        human.ClassPick = -1;
                        human.PingKind = 0;
                        human.StationBuy = t % 30 == 0 ? 1 + (t / 30) % 2 : 0;
                        if (human.StationBuy != 0) presses++;
                        MatchTick.Advance(w, human, scratch);
                    }
                    else MatchTick.Advance(w, null, scratch);   // autopilot the rest of the time
                }
            }
            finally { MatchTick.Tap = null; }
            ulong live = StateHash.Of(w);
            ReplayData d = rec.Finish(w.Match.Phase == MatchPhase.Ended, 638000000000000000L);
            var p = new ReplayPlayer(d);
            p.RunToEnd();
            Line("16v16 seed 4242 Full 6:00 recorded with human vote presses and played back", "votes closed " + w.HutVote.Closed + ", human presses " + presses
                + ", rally bonus " + Comeback.BonusSeconds(w, TeamId.Azure).ToString("0.0") + " / " + Comeback.BonusSeconds(w, TeamId.Crimson).ToString("0.0") + " s, fingerprints "
                + p.FingerprintsChecked + "/" + d.FingerprintCount + ", desync tick " + p.DesyncTick + ", final 0x" + live.ToString("X16") + " replay 0x" + StateHash.Of(p.World).ToString("X16"), "identical");
            Assert.IsTrue(p.ConfigMatches);
            Assert.IsFalse(p.Desynced, "desynced at tick " + p.DesyncTick);
            Assert.AreEqual(d.FingerprintCount, p.FingerprintsChecked);
            Assert.AreEqual(live, StateHash.Of(p.World));
            Assert.AreEqual(w.HutVote.Mask[0], p.World.HutVote.Mask[0], "the same picks");
            Assert.AreEqual(w.HutVote.Mask[1], p.World.HutVote.Mask[1]);
            Assert.Greater(w.HutVote.Closed, 0, "a vote happened inside the recording");
        }

        [Test]
        public void TheStateHashSeesAVoteTheTickItIsCast()
        {
            World a = HumanAzure(Full()), b = HumanAzure(Full());
            LevelUp(a, TeamId.Azure);
            LevelUp(b, TeamId.Azure);
            DepthKit.Steps(a, 1);
            DepthKit.Steps(b, 1);
            a.Inputs[0].StationBuy = 1;
            b.Inputs[0].StationBuy = 2;
            DepthKit.Steps(a, 1);
            DepthKit.Steps(b, 1);
            ulong ha = StateHash.Of(a), hb = StateHash.Of(b);
            Line("two worlds that differ only in one vote: state hashes", "0x" + ha.ToString("X16") + " / 0x" + hb.ToString("X16"), "different");
            Assert.AreNotEqual(ha, hb);
        }

        // ------------------------------------------------------------------ pass 2 levers (each set explicitly)

        static GameConfig Levers(Action<ComebackConfig> set)
        {
            GameConfig c = Full();
            B15SweepTests.Bare(c);
            set(c.Depth.Comeback);
            return c;
        }

        [Test]
        public void WavesBringTheKnockedOutBackOnTheTeamBeat()
        {
            World w = Make(Levers(k => k.WaveSeconds = 8), 63, 5);
            double hz = w.Config.SimHz;
            int[] at = { 100, 230, 239, 240, 1000 };
            string got = "";
            foreach (int t in at)
            {
                w.Tick = t;
                double d = Comeback.RespawnDelay(w, TeamId.Azure);
                long back = t + (long)Math.Round(d * hz);
                got += (got.Length > 0 ? " " : "") + back;
                Assert.AreEqual(0, back % 240, "back on a beat (tick " + t + ")");
                Assert.GreaterOrEqual(d, w.Config.Respawn.DelaySeconds - 1e-9, "never sooner than the own wait");
                Assert.Less(d, w.Config.Respawn.DelaySeconds + 8 + 1e-9);
            }
            Line("WAVES 8 s: knock-outs at ticks 100 / 230 / 239 / 240 / 1000 come back at", got, "multiples of 240, at least 5 s later");
        }

        [Test]
        public void MajorityNeedsAClearLeadOfBodies()
        {
            World w = Make(Levers(k => k.MajorityMargin = 2), 63, 5);
            Assert.AreEqual(HillState.Contested, Comeback.Majority(w, HillState.Azure, 3, 2));
            Assert.AreEqual(HillState.Azure, Comeback.Majority(w, HillState.Azure, 4, 2));
            Assert.AreEqual(HillState.Azure, Comeback.Majority(w, HillState.Azure, 1, 0), "alone on the hill owns it");
            World off = Make(Levers(k => { }), 63, 5);
            Assert.AreEqual(HillState.Azure, Comeback.Majority(off, HillState.Azure, 3, 2), "margin 1 = today's rule");
            Line("MAJORITY 2: 3 v 2 / 4 v 2 / 1 v 0", "Contested / Azure / Azure", "Contested / Azure / Azure");
        }

        [Test]
        public void HoldFatigueTiresAnUnbrokenHoldAndAChangeResetsIt()
        {
            World w = Make(Levers(k => { k.HoldFatigueOn = true; k.HoldGraceSeconds = 30; k.HoldSpanSeconds = 60; k.HoldMinPerMille = 500; }), 63, 5);
            double first = 0, mid = 0, last = 0;
            for (int t = 0; t < 30 * 120; t++)
            {
                double r = Comeback.HoldRate(w, HillState.Crimson);
                if (t == 0) first = r;
                if (t == 30 * 60) mid = r;
                last = r;
            }
            double after = Comeback.HoldRate(w, HillState.Azure);
            double back = Comeback.HoldRate(w, HillState.Crimson);
            Line("HOLD FATIGUE 30 s + 60 s to 0.5: share at 0 s / 60 s / 120 s, then Azure takes it, then Crimson again", first + " / " + mid + " / " + last + ", " + after + ", " + back, "1 / 0.75 / 0.5, 1, 1");
            Assert.AreEqual(1.0, first);
            Assert.AreEqual(0.75, mid, 0.002);
            Assert.AreEqual(0.5, last);
            Assert.AreEqual(1.0, after);
            Assert.AreEqual(1.0, back);
        }

        [Test]
        public void ATurncoatKaijuGetsUpOnTheSideThatKnockedItDown()
        {
            World w = Make(Levers(k => k.KaijuTurncoat = true), 63, 5);
            int crimson = DepthKit.Enemy(w, 0);
            w.Kaiju.State = KaijuState.Azure;
            Comeback.OnKaijuKo(w, 0);
            Assert.AreEqual(-1, w.Rally.KoTeam, "its own side cannot claim it");
            Comeback.OnKaijuKo(w, crimson);
            Comeback.OnKaijuRecover(w);
            Assert.AreEqual(KaijuState.Crimson, w.Kaiju.State);
            Assert.AreEqual(1, w.Rally.Turns);
            w.Kaiju.State = KaijuState.Berserk;
            Comeback.OnKaijuKo(w, 0);
            Comeback.OnKaijuRecover(w);
            Assert.AreEqual(KaijuState.Berserk, w.Kaiju.State, "a berserk Kaiju never turns");
            World off = Make(Levers(k => { }), 63, 5);
            off.Kaiju.State = KaijuState.Azure;
            Comeback.OnKaijuKo(off, DepthKit.Enemy(off, 0));
            Comeback.OnKaijuRecover(off);
            Assert.AreEqual(KaijuState.Azure, off.Kaiju.State, "lever off: unchanged");
            Line("TURNCOAT: Azure Kaiju knocked down by Crimson gets up", "Crimson (turns 1); berserk stays berserk; off stays Azure", "same");
        }

        [Test]
        public void ForwardReturnBringsATrailingTeamBackCloserToTheHill()
        {
            World w = Make(Levers(k => { k.ForwardReturnOn = true; k.ForwardShare = 0.4; }), 1, 16);
            w.Match.HillSeconds.Azure = 0;
            w.Match.HillSeconds.Crimson = 40;
            int moved = 0, slots = 0;
            double before = 0, after = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Team != TeamId.Azure) continue;
                World.SpawnPoint(w.Map, TeamId.Azure, World.TeamSlot(w, i), out double x, out double z);
                double x0 = x, z0 = z;
                Comeback.ForwardSpawn(w, i, 0.5, ref x, ref z);
                slots++;
                double d0 = Math.Sqrt((x0 - w.Map.Hill.X) * (x0 - w.Map.Hill.X) + (z0 - w.Map.Hill.Z) * (z0 - w.Map.Hill.Z));
                double d1 = Math.Sqrt((x - w.Map.Hill.X) * (x - w.Map.Hill.X) + (z - w.Map.Hill.Z) * (z - w.Map.Hill.Z));
                before += d0; after += d1;
                if (d1 < d0 - 1) moved++;
                Assert.LessOrEqual(d1, d0 + 1e-9);
            }
            int leader = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Team != TeamId.Crimson) continue;
                World.SpawnPoint(w.Map, TeamId.Crimson, World.TeamSlot(w, i), out double x, out double z);
                double x0 = x;
                Comeback.ForwardSpawn(w, i, 0.5, ref x, ref z);
                if (x != x0) leader++;
            }
            Line("FORWARD RETURN 0.4, Azure 40 s behind: slots moved forward, mean distance to the hill before / after; leading team slots moved",
                moved + " of " + slots + ", " + (before / slots).ToString("0.0") + " / " + (after / slots).ToString("0.0") + " u; " + leader, "most moved; 0");
            Assert.Greater(moved, slots / 2);
            Assert.AreEqual(0, leader);
        }
    }
}
