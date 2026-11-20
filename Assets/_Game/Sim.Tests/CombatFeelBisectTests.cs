using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
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
        private static readonly string[] ArmNames =
        {
            "off", "everywhere (mirror-fair)", "Kaiju 6", "Kaiju 6 + bounded 4/6s", "Kaiju 6 + bounded 3/6s",
        };

        private static void Arm(int arm, GameConfig c)
        {
            CombatFeelConfig f = c.Depth.CombatFeel;
            f.BotUnjam = arm != 0;
            f.BotUnjamKaijuClear = arm >= 2 ? 6 : 0;
            f.BotUnjamHillClear = -1;
            f.BotUnjamHillZone = 0;
            f.BotUnjamBudget = arm == 3 ? 4 : arm == 4 ? 3 : 0;
            f.BotUnjamBudgetSeconds = 6;
        }

        [Test]
        public void Pass2RegressionsPerArm()
        {
            int[] seeds = WinShare.Seeds16;
            for (int arm = 0; arm < ArmNames.Length; arm++)
            {
                double onSum = 0, onSum24 = 0;
                int used = 0, used24 = 0, worstLock = 0, anyRun = 0, maxProofs = 0, proofs1337 = 0;
                var stuckList = new List<int>();
                var stuckCells = new Dictionary<int, int>();
                var zone = new int[5];   // side-step starts: on the disc, ring band (<= 13 u), <= 20 u, beyond, within 6 u of the Kaiju
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
                    var sx = new double[n];
                    var sz = new double[n];
                    var sat = new int[n];
                    int stuck = 0;
                    for (int i = 0; i < n; i++) sat[i] = -1;
                    var side0 = new int[n];
                    BotsState bst = BotBrain.GetBotsState(w);
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
                            Actor sa = w.Actors[i];
                            InputFrame sf = w.Inputs[i];
                            bool pressing = sa.Alive && sa.StandUp <= 0 && sf.MoveX * sf.MoveX + sf.MoveZ * sf.MoveZ > 0.01;
                            double ddx = w.X[i] - sx[i], ddz = w.Z[i] - sz[i];
                            if (!pressing || sat[i] < 0 || ddx * ddx + ddz * ddz >= 0.16)
                            {
                                sx[i] = w.X[i];
                                sz[i] = w.Z[i];
                                sat[i] = pressing ? w.Tick : -1;
                            }
                            else if (w.Tick - sat[i] >= 90)
                            {
                                stuck++;
                                int cell = (int)Math.Floor(w.X[i]) * 1000 + (int)Math.Floor(w.Z[i]);
                                stuckCells.TryGetValue(cell, out int sc);
                                stuckCells[cell] = sc + 1;
                                sat[i] = w.Tick;
                            }
                            if (bst.SideUntil[i] != side0[i])
                            {
                                side0[i] = bst.SideUntil[i];
                                if (side0[i] > 0)
                                {
                                    double hx2 = w.X[i] - hill.X, hz2 = w.Z[i] - hill.Z, d2 = hx2 * hx2 + hz2 * hz2;
                                    zone[d2 <= hill.R * hill.R ? 0 : d2 <= 169 ? 1 : d2 <= 400 ? 2 : 3]++;
                                    if (w.Match.Hatched && Math.Abs(w.Kaiju.X - w.X[i]) + Math.Abs(w.Kaiju.Z - w.Z[i]) < 6) zone[4]++;
                                }
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
                    if (w.Depth.Feel.MaxLockRun > anyRun) anyRun = w.Depth.Feel.MaxLockRun;
                    if (seed == 1337 || SimProfile.RouteProofs > 150) proofs.Add(seed + ":" + SimProfile.RouteProofs + "/" + SimProfile.RouteMemoHits);
                    if (seed == 1337) proofs1337 = (int)SimProfile.RouteProofs;
                    if (SimProfile.RouteProofs > maxProofs) maxProofs = (int)SimProfile.RouteProofs;
                    stuckList.Add(stuck);
                    bool order = Array.IndexOf(WinShare.OrderSeeds16, seed) >= 0;
                    if (owned > 60)
                    {
                        if (order)
                        {
                            used++;
                            onSum += onHill / owned;
                        }
                        used24++;
                        onSum24 += onHill / owned;
                    }
                    Console.WriteLine("{\"item\":\"b15 bisect match\",\"arm\":\"" + ArmNames[arm] + "\",\"seed\":" + seed + ",\"ownedS\":" + owned.ToString("0") +
                                      ",\"onShare\":" + (owned > 0 ? onHill / owned : 0).ToString("0.000") + ",\"offWithTarget\":" + (offTarget * dt).ToString("0") +
                                      ",\"offNoTarget\":" + (offNoTarget * dt).ToString("0") + ",\"routeProofs\":" + SimProfile.RouteProofs + ",\"memoHits\":" + SimProfile.RouteMemoHits + ",\"stuck\":" + stuck + "}");
                }
                int idle5 = 0;
                int a5 = arm;
                for (int s = 201; s < 401; s++)
                    if (OutcomeRunner.RunOutcomeMatch(s, 5, HumanMode.Passive, c => { c.ApplyRules(RulesProfile.Full); Arm(a5, c); }).Winner == MatchResult.Azure) idle5++;
                var ks = new System.Text.StringBuilder();
                for (int c = 0; c < 6; c++) ks.Append((ClassId)c).Append('=').Append((kills[c] / Math.Max(1, allKills)).ToString("0.000")).Append(' ');
                stuckList.Sort();
                int hot = 0;
                foreach (int v in stuckCells.Values) if (v > hot) hot = v;
                double med = stuckList.Count % 2 == 1 ? stuckList[stuckList.Count / 2] : (stuckList[stuckList.Count / 2 - 1] + stuckList[stuckList.Count / 2]) / 2.0;
                Console.WriteLine("{\"item\":\"b15 bisect arm\",\"arm\":\"" + ArmNames[arm] + "\",\"kaijuOnShareMean\":" + (used > 0 ? onSum / used : 0).ToString("0.000") +
                                  ",\"onShare24\":" + (used24 > 0 ? onSum24 / used24 : 0).ToString("0.000") + ",\"seeds24Used\":" + used24 + ",\"proofs1337\":" + proofs1337 + ",\"maxProofs\":" + maxProofs +
                                  ",\"stuckMedian\":" + med.ToString("0.0") + ",\"hotCell\":" + hot + ",\"sideStepZones\":\"disc " + zone[0] + " band " + zone[1] + " near " + zone[2] + " far " + zone[3] + " kaiju6 " + zone[4] + "\"" +
                                  ",\"seedsUsed\":" + used + ",\"routeProofs\":\"" + string.Join(" ", proofs) + "\",\"longestMeleeLockTicks\":" + worstLock + ",\"longestAnyStunRunTicks\":" + anyRun +
                                  ",\"worstLock\":\"" + worstLockAt + "\",\"idle5v5AzureWinsOf200\":" + idle5 + ",\"killShares\":\"" + ks + "\"}");
            }
            Assert.Pass();
        }
    }
}
