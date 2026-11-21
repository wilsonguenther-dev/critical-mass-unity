using System;
using System.Globalization;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>b15 fix round probe 2 (probe branch only; category Probe never runs in the gates): why an idle actor 0 costs its 5v5 team nothing.</summary>
    [TestFixture, Category("Probe")]
    public sealed class B15FixProbe2Tests
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.00", Inv);

        sealed class Sum
        {
            public int Wins, Matches;
            public double HillA, HillC, Contested, OnHillA, OnHillC, EnemyOnTarget0, EnemyNear0, Dead0, DeathsA, DeathsC, HutA, HutC, OreA, OreC, FatigueA, FatigueC, RallyA;
        }

        static void Run(int seed, bool idle, Sum s)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5();
            cfg.ApplyRules(RulesProfile.Full);
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            double hz = cfg.SimHz, dt = 1.0 / hz;
            int max = (int)Math.Ceiling(cfg.MatchSeconds * hz) + (int)hz;
            BotsState st = null;
            for (int t = 0; t < max && w.Match.Phase == MatchPhase.Playing; t++)
            {
                if (idle) MatchTick.AdvanceNoHuman(w, scratch); else MatchTick.Advance(w, null, scratch);
                if (st == null) st = BotBrain.GetBotsState(w);
                HillState h = w.Match.HillState;
                if (h == HillState.Contested) s.Contested += dt;
                s.OnHillA += w.Match.HillCount.Azure * dt;
                s.OnHillC += w.Match.HillCount.Crimson * dt;
                if (!w.Actors[0].Alive) s.Dead0 += dt;
                if (h == HillState.Azure) s.FatigueA += (1 - Comeback.HoldShare(w, TeamId.Azure)) * dt;
                if (h == HillState.Crimson) s.FatigueC += (1 - Comeback.HoldShare(w, TeamId.Crimson)) * dt;
                for (int i = 1; i < w.ActorCount; i++)
                {
                    Actor a = w.Actors[i];
                    if (!a.Alive || a.Team != TeamId.Crimson) continue;
                    if (st != null && st.Target != null && i < st.Target.Length && st.Target[i] == 0) s.EnemyOnTarget0 += dt;
                    double dx = w.X[i] - w.X[0], dz = w.Z[i] - w.Z[0];
                    if (w.Actors[0].Alive && dx * dx + dz * dz < 64) s.EnemyNear0 += dt;
                }
            }
            s.Matches++;
            if (w.Match.Winner == MatchResult.Azure) s.Wins++;
            s.HillA += w.Match.HillSeconds.Azure;
            s.HillC += w.Match.HillSeconds.Crimson;
            for (int i = 0; i < w.ActorCount; i++)
                if (w.Actors[i].Team == TeamId.Azure) s.DeathsA += w.Actors[i].Deaths; else s.DeathsC += w.Actors[i].Deaths;
            if (w.Hut != null) { s.HutA += w.Hut.Level[0]; s.HutC += w.Hut.Level[1]; s.OreA += w.Hut.Banked[0]; s.OreC += w.Hut.Banked[1]; }
        }

        static void Print(string tag, Sum s)
        {
            double n = s.Matches;
            Console.WriteLine("[idle2] " + tag + ": Azure wins " + s.Wins + " of " + s.Matches + "; hill s A " + F(s.HillA / n) + " C " + F(s.HillC / n)
                + "; contested s " + F(s.Contested / n) + "; on-hill body-s A " + F(s.OnHillA / n) + " C " + F(s.OnHillC / n)
                + "; Crimson bot-s targeting actor0 " + F(s.EnemyOnTarget0 / n) + ", within 8 u of actor0 " + F(s.EnemyNear0 / n)
                + "; actor0 dead s " + F(s.Dead0 / n) + "; deaths A " + F(s.DeathsA / n) + " C " + F(s.DeathsC / n)
                + "; hut level A " + F(s.HutA / n) + " C " + F(s.HutC / n) + "; banked ore A " + F(s.OreA / n) + " C " + F(s.OreC / n)
                + "; fatigue-lost s A " + F(s.FatigueA / n) + " C " + F(s.FatigueC / n));
        }

        [Test]
        public void IdleActorZeroWhatCompensates()
        {
            var idle = new Sum();
            var bot = new Sum();
            for (int seed = 201; seed < 401; seed++)
            {
                Run(seed, true, idle);
                Run(seed, false, bot);
            }
            Print("idle actor 0", idle);
            Print("bot actor 0", bot);
        }
    }
}
