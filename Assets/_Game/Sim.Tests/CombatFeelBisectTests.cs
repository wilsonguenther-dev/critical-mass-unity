using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 pass 3 bisect (TEMPORARY measuring test, removed before the PR leaves draft): the pass-2 regressions measured per
    /// arm on the same seeds. Arms: current; side-step off; pass-1 class rows and no stagger cap (side-step on); pass-1 rows,
    /// no cap, side-step off (= the pass-1 sim). Per arm: the KaijuHill six-seed on-disc share, the route proofs per whole
    /// 16v16 match, the longest melee lock with its causes, the class kill shares, and the 5v5 idle-player wins (200 seeds).
    /// </summary>
    [TestFixture]
    public sealed class CombatFeelBisectTests
    {
        private static void Pass1Rows(CombatFeelConfig f)
        {
            ClassConfig wr = f.Classes[(int)ClassId.Warrior], mn = f.Classes[(int)ClassId.Miner], dm = f.Classes[(int)ClassId.Demolitions];
            wr.AttackDamage = 27; wr.MaxHp = 160; wr.AttackCooldown = 1.05;
            mn.AttackDamage = 12; mn.MaxHp = 120; mn.AttackCooldown = 0.55;
            dm.AttackCooldown = 0.80;
            f.SunderDamageTaken = 0.25;
            f.StaggerMaxLockSeconds = 99;
        }

        private static readonly string[] ArmNames = { "current", "side-step off", "pass-1 rows, no cap", "pass-1 rows, no cap, side-step off" };

        private static void Arm(int arm, GameConfig c)
        {
            CombatFeelConfig f = c.Depth.CombatFeel;
            if (arm >= 2) Pass1Rows(f);
            if (arm == 1 || arm == 3) f.BotUnjam = false;
        }

        [Test]
        public void Pass2RegressionsPerArm()
        {
            int[] seeds = { 1337, 4242, 7, 99, 2024, 31337 };
            for (int arm = 0; arm < ArmNames.Length; arm++)
            {
                double onSum = 0;
                int used = 0, worstLock = 0;
                string worstLockAt = "";
                var kills = new double[6];
                double allKills = 0;
                var proofs = new List<string>();
                foreach (int seed in seeds)
                {
                    GameConfig cfg = GameConfig.CreateDefault();
                    Arm(arm, cfg);
                    World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                    InputFrame scratch = SimInput.EmptyInput();
                    HillDef hill = w.Map.Hill;
                    double dt = 1.0 / cfg.SimHz, owned = 0, onHill = 0;
                    int n = w.ActorCount, head = w.EventHead, offTarget = 0, offNoTarget = 0;
                    var lk = new int[n];
                    var causes = new int[n];
                    SimProfile.ResetCounters();
                    for (int t = 0; t < 14400 && w.Match.Phase == MatchPhase.Playing; t++)
                    {
                        MatchTick.Advance(w, null, scratch);
                        Kaiju k = w.Kaiju;
                        if (w.Match.Hatched && k.State != KaijuState.Egg)
                        {
                            double hx = k.X - hill.X, hz = k.Z - hill.Z;
                            bool inHill = hx * hx + hz * hz <= hill.R * hill.R;
                            if ((k.State == KaijuState.Azure || k.State == KaijuState.Crimson) && k.KoIn <= 0)
                            {
                                owned += dt;
                                if (inHill) onHill += dt;
                                else if (k.TargetId >= 0) offTarget++;
                                else offNoTarget++;
                            }
                        }
                        for (int i = 0; i < n; i++)
                        {
                            Actor a = w.Actors[i];
                            bool locked = a.Alive && a.HitStunTicks > 0 && a.LastCause == DamageCause.Melee;
                            if (locked)
                            {
                                if (lk[i] == 0) causes[i] = 0;
                                lk[i]++;
                                causes[i] |= 1 << (int)a.LastCause;
                                if (a.KVx != 0 || a.KVz != 0) causes[i] |= 1 << 20;
                                if (lk[i] > worstLock)
                                {
                                    worstLock = lk[i];
                                    CombatFeelState fs = w.Depth.Feel;
                                    worstLockAt = "seed " + seed + " t " + w.Tick + " actor " + i + " " + a.Cls + " stun " + a.HitStunTicks + " lockStart " + fs.LockStart[i] +
                                                  " lockUntil " + fs.LockUntil[i] + " immuneUntil " + fs.ImmuneUntil[i] + " standUp " + a.StandUp.ToString("0.00") +
                                                  " knocked " + ((causes[i] >> 20) & 1) + " kaijuStunNear " + (Math.Abs(w.Kaiju.X - w.X[i]) + Math.Abs(w.Kaiju.Z - w.Z[i]) < 6 ? 1 : 0);
                                }
                            }
                            else if (!(a.Alive && a.HitStunTicks > 0)) lk[i] = 0;
                        }
                        for (; head < w.EventHead; head++)
                        {
                            SimEvent e = w.Events[head % World.EventCapacity];
                            if (e.Type != SimEventType.Death) continue;
                            allKills++;
                            if (e.B >= 0 && e.B < n) kills[(int)w.Actors[e.B].Cls]++;
                        }
                    }
                    proofs.Add(seed + ":" + SimProfile.RouteProofs + "/" + SimProfile.RouteMemoHits);
                    if (owned > 60)
                    {
                        used++;
                        onSum += onHill / owned;
                    }
                    Console.WriteLine("{\"item\":\"b15 bisect match\",\"arm\":\"" + ArmNames[arm] + "\",\"seed\":" + seed + ",\"ownedS\":" + owned.ToString("0") +
                                      ",\"onShare\":" + (owned > 0 ? onHill / owned : 0).ToString("0.000") + ",\"offWithTarget\":" + (offTarget * dt).ToString("0") +
                                      ",\"offNoTarget\":" + (offNoTarget * dt).ToString("0") + ",\"routeProofs\":" + SimProfile.RouteProofs + ",\"memoHits\":" + SimProfile.RouteMemoHits + "}");
                }
                int idle5 = 0;
                int a5 = arm;
                for (int s = 201; s < 401; s++)
                    if (OutcomeRunner.RunOutcomeMatch(s, 5, HumanMode.Passive, c => { c.ApplyRules(RulesProfile.Full); Arm(a5, c); }).Winner == MatchResult.Azure) idle5++;
                var ks = new System.Text.StringBuilder();
                for (int c = 0; c < 6; c++) ks.Append((ClassId)c).Append('=').Append((kills[c] / Math.Max(1, allKills)).ToString("0.000")).Append(' ');
                Console.WriteLine("{\"item\":\"b15 bisect arm\",\"arm\":\"" + ArmNames[arm] + "\",\"kaijuOnShareMean\":" + (used > 0 ? onSum / used : 0).ToString("0.000") +
                                  ",\"seedsUsed\":" + used + ",\"routeProofs\":\"" + string.Join(" ", proofs) + "\",\"longestMeleeLockTicks\":" + worstLock +
                                  ",\"worstLock\":\"" + worstLockAt + "\",\"idle5v5AzureWinsOf200\":" + idle5 + ",\"killShares\":\"" + ks + "\"}");
            }
            Assert.Pass();
        }
    }
}
