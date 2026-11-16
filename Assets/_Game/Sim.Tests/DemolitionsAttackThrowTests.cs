using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b12-jz item 3 RULE ("her grenade launcher isn't working, that should be her default attack"). In 1.0.868 her attack
    /// press was only the pry-bar swing; TNT needed the DigPlace key (F / Y) nobody found. Now the attack press throws TNT
    /// whenever it is ready (DemolitionsTnt.ThrowOnAttack, Full rules only). Every case prints one evidence line; the
    /// balance case prints the rule-off control and the rule-on arm on the same seeds.
    /// </summary>
    [TestFixture]
    public sealed class DemolitionsAttackThrowTests
    {
        private static World Duel(GameConfig cfg, double enemyX, double enemyZ, out int enemy)
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 5, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Demolitions, Config = cfg });
            enemy = -1;
            for (int j = 1; j < w.ActorCount; j++)
            {
                if (enemy < 0 && w.Actors[j].Team == TeamId.Crimson) { enemy = j; continue; }
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
            }
            foreach (int j in new[] { 0, enemy })
            {
                Actor a = w.Actors[j];
                a.Alive = true;
                a.Hp = a.MaxHp;
                a.SpawnProtect = 0;
                a.StandUp = 0;
                a.AttackCd = 0;
            }
            // Open ground in front of the Azure base (the default map): her at its centre, the enemy where asked.
            w.X[enemy] = w.X[0] + enemyX;
            w.Z[enemy] = w.Z[0] + enemyZ;
            w.Actors[0].Facing = 0;
            return w;
        }

        private static void Press(World w, bool attack, bool aim, double ax, double az)
        {
            for (int j = 0; j < w.ActorCount; j++) w.Inputs[j] = SimInput.EmptyInput();
            w.Inputs[0].Attack = attack;
            w.HasAim[0] = aim ? (byte)1 : (byte)0;
            w.Inputs[0].AimX = ax;
            w.Inputs[0].AimZ = az;
        }

        [Test]
        public void PressingAttackLobsTntThatLandsAndBlastsTheEnemy()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = Duel(cfg, 3.5, 0, out int enemy);
            double hp0 = w.Actors[enemy].Hp;
            Press(w, true, true, w.X[enemy], w.Z[enemy]);
            Assert.IsTrue(Demolitions.AttackThrowsNow(w, 0), "attack held, TNT ready: the press throws");
            int thrown0 = w.Demolitions.Stats.TntThrown;
            SimStep.Step(w);
            int thrown = w.Demolitions.Stats.TntThrown - thrown0;
            Bomb b = null;
            foreach (Bomb x in w.Demolitions.Bombs) if (x.Active) b = x;
            Console.WriteLine($"[b12-jz demo] attack press with aim on an enemy 3.5 m away: TNT thrown {thrown} (want 1), bomb at ({b?.X:F2},{b?.Z:F2}) enemy at ({w.X[enemy]:F2},{w.Z[enemy]:F2}), attack cooldown {w.Actors[0].AttackCd:F2} (no pry-bar swing this step)");
            Assert.AreEqual(1, thrown);
            for (int t = 0; t < 120; t++) { Press(w, false, true, w.X[enemy], w.Z[enemy]); SimStep.Step(w); }
            double hp1 = w.Actors[enemy].Alive ? w.Actors[enemy].Hp : 0;
            Console.WriteLine($"[b12-jz demo] after the fuse: enemy HP {hp0:F0} -> {hp1:F0} (TNT damage {cfg.Abilities.DemolitionsTnt.Damage}), blasts {w.Demolitions.Stats.TntBlasts}, her HP {w.Actors[0].Hp:F0}/{w.Actors[0].MaxHp:F0} (her own blast never hurts her)");
            Assert.Greater(w.Demolitions.Stats.TntBlasts, 0, "it landed and blew");
            Assert.Less(hp1, hp0, "the blast hurt the enemy");
            Assert.AreEqual(w.Actors[0].MaxHp, w.Actors[0].Hp, "no self-damage");
        }

        [Test]
        public void WithoutAimItAutoAimsTheNearestEnemyAndNeverLandsAtHerFeet()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = Duel(cfg, 0, 4, out int enemy);
            Press(w, true, false, double.NaN, double.NaN);
            Demolitions.AttackThrowPoint(w, 0, out double x, out double z);
            Console.WriteLine($"[b12-jz demo] no aim, enemy 4 m north: throw point ({x - w.X[0]:F2},{z - w.Z[0]:F2}) from her (want ~ (0, 4))");
            Assert.AreEqual(w.X[enemy], x, 1e-6);
            Assert.AreEqual(w.Z[enemy], z, 1e-6);
            // Aim at her own feet: pushed out to MinThrowRange along her facing.
            Press(w, true, true, w.X[0], w.Z[0]);
            Demolitions.AttackThrowPoint(w, 0, out x, out z);
            double d = Math.Sqrt((x - w.X[0]) * (x - w.X[0]) + (z - w.Z[0]) * (z - w.Z[0]));
            Console.WriteLine($"[b12-jz demo] aim on her feet: throw lands {d:F2} m away (min {cfg.Abilities.DemolitionsTnt.MinThrowRange})");
            Assert.GreaterOrEqual(d, cfg.Abilities.DemolitionsTnt.MinThrowRange - 1e-4);
        }

        [Test]
        public void BetweenThrowsTheAttackIsThePryBarAndClassicRulesAreUntouched()
        {
            GameConfig cfg = GameConfig.CreateDefault();
            World w = Duel(cfg, 3.5, 0, out int enemy);
            Press(w, true, true, w.X[enemy], w.Z[enemy]);
            SimStep.Step(w);
            Press(w, true, true, w.X[enemy], w.Z[enemy]);
            bool second = Demolitions.AttackThrowsNow(w, 0);
            Console.WriteLine($"[b12-jz demo] the step after a throw: attack throws again {second} (want False: TNT cooldown {cfg.Abilities.DemolitionsTnt.CooldownTicks} ticks at 30 Hz)");
            Assert.IsFalse(second);
            GameConfig classic = GameConfig.CreateClassic();
            World c = Duel(classic, 3.5, 0, out int e2);
            Press(c, true, true, c.X[e2], c.Z[e2]);
            bool cl = Demolitions.AttackThrowsNow(c, 0);
            Console.WriteLine($"[b12-jz demo] Classic rules: attack throws {cl} (want False: browser parity, goldens unchanged)");
            Assert.IsFalse(cl);
        }

        /// <summary>
        /// Same-seed balance: 16v16 Full rules for 2:30 on four seeds with the rule OFF (control) and ON. Prints her (the two
        /// Demolitions per team) share of all knock-outs, her TNT throws and blast damage per arm. Limit: her knock-out share
        /// with the rule on stays at or below 25% (2 of 16 per team is 12.5%) and within 8 points of the control.
        /// </summary>
        [Test]
        public void SameSeedBalanceHerKillShareBeforeVersusAfter()
        {
            int[] seeds = { 1, 7, 42, 1337 };
            double offShareSum = 0, onShareSum = 0;
            foreach (int seed in seeds)
            {
                double[] share = new double[2];
                for (int arm = 0; arm < 2; arm++)
                {
                    GameConfig cfg = GameConfig.CreateDefault();
                    cfg.Abilities.DemolitionsTnt.ThrowOnAttack = arm == 1;
                    World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Demolitions, Config = cfg });
                    InputFrame scratch = SimInput.EmptyInput();
                    int max = (int)(150 * cfg.SimHz);
                    for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++) MatchTick.Advance(w, null, scratch);
                    int all = 0, demo = 0, demos = 0;
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        all += w.Actors[i].Kills;
                        if (w.Actors[i].Cls != ClassId.Demolitions) continue;
                        demo += w.Actors[i].Kills;
                        demos++;
                    }
                    share[arm] = all > 0 ? (double)demo / all : 0;
                    DemolitionsStats s = w.Demolitions.Stats;
                    Console.WriteLine($"[b12-jz demo balance] seed {seed} rule {(arm == 1 ? "ON " : "OFF")}: Demolitions {demos} of {w.ActorCount}, knock-outs {demo}/{all} = {share[arm]:P1}, TNT thrown {s.TntThrown}, blasts {s.TntBlasts}, blast damage {s.Damage:F0}, blast kills {s.Kills}");
                }
                offShareSum += share[0];
                onShareSum += share[1];
            }
            double off = offShareSum / seeds.Length, on = onShareSum / seeds.Length;
            Console.WriteLine($"[b12-jz demo balance] mean knock-out share: OFF {off:P1}, ON {on:P1} (limits: ON <= 25.0% and ON - OFF <= 8.0 points)");
            Assert.LessOrEqual(on, 0.25);
            Assert.LessOrEqual(on - off, 0.08);
        }
    }
}
