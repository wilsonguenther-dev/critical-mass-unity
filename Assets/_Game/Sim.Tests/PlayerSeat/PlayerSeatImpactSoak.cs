using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>
    /// Player-seat gate, soak (Slow + PlayerSeatSoak): does ONE strong seat move a whole match? Arm A: every seat a Normal bot.
    /// Arm B: seat 0 plays at Hard. Bot difficulty is per TEAM in the sim (WorldOptions.AllyBotDifficulty), so the proxy swaps
    /// it to Hard only around seat 0's own BotBrain.FillBotInput, in MatchTick.Advance's exact order (BeginFills, seat 0,
    /// BotsThink, seats 1..n, EndFills, Step). BotsThink (the team-wide planning pass) stays Normal for everyone: seat 0's
    /// per-tick choices and reflexes are Hard, its plan is the team's. The proxy is named in every line it prints.
    /// Seeds: T.SoakSeeds (24), or CM_PX_SOAK_SEEDS for a quick local look. Per-seed rows go to $CM_PX_DATA_DIR/soak-*.csv.
    /// </summary>
    [TestFixture, Category("Slow"), Category("PlayerSeatSoak")]
    public sealed class PlayerSeatImpactSoak
    {
        const string Proxy = "Hard proxy: AllyBotDifficulty=Hard around seat 0's FillBotInput only";

        sealed class Match
        {
            public double Win;   // 1 win, 0.5 draw, 0 loss for Azure
            public int Kills0, KillsTeam;
            public double Dmg0, DmgTeam, Hill0, HillTeam;
            public int Ticks;
        }

        static int Seeds()
        {
            string s = Environment.GetEnvironmentVariable("CM_PX_SOAK_SEEDS");
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : T.SoakSeeds;
        }

        static GameConfig Config(int teamSize)
        {
            GameConfig c = teamSize >= 16 ? GameConfig.CreateDefault().ApplyRules(RulesProfile.Full) : GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            c.TeamSize = teamSize;
            return c;
        }

        static Match Play(int teamSize, int seed, bool strong)
        {
            var opts = new WorldOptions { Seed = 9000 + seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = PlayerSeatTargets.Classes[seed % 6], Config = Config(teamSize), BotDifficulty = BotDifficulty.Normal, AllyBotDifficulty = BotDifficulty.Normal };
            World w = World.CreateWorld(opts);
            var scratch = new InputFrame();
            var m = new Match();
            int head = w.EventHead;
            int cap = (int)Math.Ceiling(w.Config.MatchSeconds * w.Config.SimHz * 1.5);
            double dt = 1.0 / w.Config.SimHz;
            double hr = w.Map.Hill.R;
            for (int t = 0; t < cap && w.Match.Winner == MatchResult.None; t++)
            {
                BotReflex.BeginFills(w);
                BotDifficulty keep = w.Opts.AllyBotDifficulty;
                if (strong) w.Opts.AllyBotDifficulty = BotDifficulty.Hard;
                BotBrain.FillBotInput(w, 0, scratch);
                SimInput.ApplyInput(w, 0, scratch);
                w.Opts.AllyBotDifficulty = keep;
                BotBrain.BotsThink(w, SimStep.Dt(w.Config));
                for (int id = 1; id < w.ActorCount; id++)
                {
                    BotBrain.FillBotInput(w, id, scratch);
                    SimInput.ApplyInput(w, id, scratch);
                }
                BotReflex.EndFills(w);
                SimStep.Step(w);
                m.Ticks++;
                if (w.EventHead - head > World.EventCapacity) head = w.EventHead - World.EventCapacity;   // a lapped ring: the oldest are gone
                for (int i = head; i < w.EventHead; i++)
                {
                    SimEvent e = w.Events[i % World.EventCapacity];
                    if (e.Type != SimEventType.Hit || e.B < 0 || e.B >= w.ActorCount) continue;
                    Actor by = w.Actors[e.B], on = e.A >= 0 && e.A < w.ActorCount ? w.Actors[e.A] : null;
                    if (by.Team != TeamId.Azure || (on != null && on.Team == TeamId.Azure)) continue;
                    m.DmgTeam += e.Value;
                    if (e.B == 0) m.Dmg0 += e.Value;
                }
                head = w.EventHead;
                for (int id = 0; id < w.ActorCount; id++)
                {
                    Actor a = w.Actors[id];
                    if (!a.Alive || a.Team != TeamId.Azure) continue;
                    double dx = w.X[id] - w.Map.Hill.X, dz = w.Z[id] - w.Map.Hill.Z;
                    if (dx * dx + dz * dz > hr * hr) continue;
                    m.HillTeam += dt;
                    if (id == 0) m.Hill0 += dt;
                }
            }
            m.Win = w.Match.Winner == MatchResult.Azure ? 1 : w.Match.Winner == MatchResult.Crimson ? 0 : 0.5;
            for (int id = 0; id < w.ActorCount; id++) if (w.Actors[id].Team == TeamId.Azure) m.KillsTeam += w.Actors[id].Kills;
            m.Kills0 = w.Actors[0].Kills;
            return m;
        }

        static void Arm(int teamSize, double uplift)
        {
            int n = Seeds();
            double winA = 0, winB = 0, k0 = 0, kt = 0, d0 = 0, dtm = 0, h0 = 0, ht = 0;
            string file = "soak-" + teamSize + "v" + teamSize + ".csv";
            for (int s = 0; s < n; s++)
            {
                Match a = Play(teamSize, s, false), b = Play(teamSize, s, true);
                winA += a.Win; winB += b.Win;
                k0 += b.Kills0; kt += b.KillsTeam; d0 += b.Dmg0; dtm += b.DmgTeam; h0 += b.Hill0; ht += b.HillTeam;
                PxLog.Csv(file, "seed,class0,armA_win,armB_win,armB_kills0,armB_killsTeam,armB_dmg0,armB_dmgTeam,armB_hill0,armB_hillTeam,armA_ticks,armB_ticks",
                    s + "," + PlayerSeatTargets.Classes[s % 6] + "," + a.Win + "," + b.Win + "," + b.Kills0 + "," + b.KillsTeam + "," + PxLog.F(b.Dmg0, "0") + "," + PxLog.F(b.DmgTeam, "0") + "," + PxLog.F(b.Hill0, "0.0") + "," + PxLog.F(b.HillTeam, "0.0") + "," + a.Ticks + "," + b.Ticks);
            }
            double shareA = winA / n, shareB = winB / n, lift = shareB - shareA;
            double fair = 1.0 / teamSize, killShare = kt > 0 ? k0 / kt : 0, dmgShare = dtm > 0 ? d0 / dtm : 0, hillShare = ht > 0 ? h0 / ht : 0;
            double killTarget = T.SoakKillSeats / teamSize;
            PxLog.Line("SOAK " + teamSize + "v" + teamSize + " seat-0 shares", Proxy + ", " + n + " seeds",
                "kills " + PxLog.F(killShare) + ", damage " + PxLog.F(dmgShare) + ", hill seconds " + PxLog.F(hillShare) + " (one seat's fair share " + PxLog.F(fair) + ")",
                "kills >= " + PxLog.F(killTarget) + " (1.5 seats)", killShare >= killTarget);
            bool ok = PxLog.Line("SOAK " + teamSize + "v" + teamSize + " uplift", Proxy + ", " + n + " seeds",
                "team win share all-Normal " + PxLog.F(shareA) + ", strong seat 0 " + PxLog.F(shareB) + ", uplift " + PxLog.F(lift),
                ">= +" + PxLog.F(uplift), lift >= uplift);
            Assert.IsTrue(ok && killShare >= killTarget, "seat 0 must move the match: uplift " + PxLog.F(lift) + " (>= " + PxLog.F(uplift) + "), kill share " + PxLog.F(killShare) + " (>= " + PxLog.F(killTarget) + ")");
        }

        [Test]
        public void Soak_5v5_StrongSeat_MovesTheMatch() => Arm(5, T.SoakUplift5);

        [Test]
        public void Soak_16v16_StrongSeat_MovesTheMatch() => Arm(16, T.SoakUplift16);
    }
}
