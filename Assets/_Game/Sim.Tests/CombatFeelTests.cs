using System;
using System.Collections.Generic;
using System.Diagnostics;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b15 combat feel (Sim/CombatFeel.cs, numbers in Sim/Config/CombatFeelConfig.cs): one test per rule, the 36-duel
    /// time-to-kill matrix printed before (rule off) and after (rule on), the rule-off golden, determinism, allocation and cost.
    /// Every measured case prints one JSON line (what, value, limit). The 24-seed 16v16 balance lives in CombatFeelBalanceTests.
    /// </summary>
    [TestFixture]
    public sealed class CombatFeelTests
    {
        internal const double X0 = 56.5, Z0 = 40.5;

        /// <summary>The game's Full rules (CreateBrowser5v5 + ApplyRules(Full), as the golden test), the rule on or off.</summary>
        internal static GameConfig Full(bool feel, bool quiet = true)
        {
            GameConfig c = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            c.Depth.CombatFeel.Enabled = feel;
            if (quiet)
            {
                c.Depth.Pickups.Enabled = false; // no power-ups in a duel
                c.Depth.Events.Enabled = false;
            }
            return c;
        }

        internal static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            a.Hp = a.MaxHp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            a.AbilityActive = 0;
            a.AttackCd = 0;
            a.KVx = 0;
            a.KVz = 0;
            a.HitStunTicks = 0;
            w.X[j] = x;
            w.Z[j] = z;
            w.PrevX[j] = x;
            w.PrevZ[j] = z;
            w.Vx[j] = 0;
            w.Vz[j] = 0;
        }

        internal static void SetClass(World w, int j, ClassId cls)
        {
            Actor a = w.Actors[j];
            a.Cls = cls;
            a.MaxHp = w.Config.Class(cls).MaxHp;
            a.Hp = a.MaxHp;
        }

        internal static int Foe(World w, int k = 0) => w.ActorCount / 2 + k;

        /// <summary>A 5v5 Full world with everyone dead but the human (cls) and one enemy (foeCls) on carved open floor.</summary>
        internal static World Arena(ClassId cls, ClassId foeCls, bool feel = true, GameConfig cfg = null)
        {
            cfg = cfg ?? Full(feel);
            World w = World.CreateWorld(new WorldOptions { Seed = 41, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = cls, Config = cfg });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
            }
            for (int cz = (int)Z0 - 4; cz <= (int)Z0 + 4; cz++)
                for (int cx = (int)X0 - 8; cx <= (int)X0 + 8; cx++) w.Map.Cells[cz * w.Map.Width + cx] = Cell.Floor;
            SetClass(w, 0, cls);
            Put(w, 0, X0 - 1.5, Z0);
            w.Actors[0].Facing = 0;
            int e = Foe(w);
            SetClass(w, e, foeCls);
            Put(w, e, X0 + 1.5, Z0);
            w.Actors[e].Facing = Math.PI;
            w.Tick = 100;
            w.Match.Time = 100; // past any passive window, long before the hatch
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            return w;
        }

        private static readonly InputFrame Idle = SimInput.EmptyInput();

        /// <summary>One tick: frames for actor 0 and the enemy (null = no input), then the full sim step.</summary>
        internal static void Tick(World w, InputFrame me, InputFrame foe)
        {
            SimInput.ApplyInput(w, 0, me ?? Idle);
            SimInput.ApplyInput(w, Foe(w), foe ?? Idle);
            SimStep.Step(w);
        }

        /// <summary>The duel controller: aim at the foe, close to the weapon's reach (the Farmer holds her staff's tip), press in reach.</summary>
        internal static InputFrame Duel(World w, int me, int foe, InputFrame f)
        {
            ClassConfig cc = w.Config.Class(w.Actors[me].Cls);
            double r = w.Config.Class(w.Actors[foe].Cls).Radius;
            double dx = w.X[foe] - w.X[me], dz = w.Z[foe] - w.Z[me];
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-9) d = 1e-9;
            double reach = cc.AttackRange + r;
            bool farmer = w.Actors[me].Cls == ClassId.Farmer;
            double want = farmer ? reach - 0.35 : reach * 0.75;
            // The Demolitions expert plays her kit: with the TNT ready she holds 3 u off and throws it, else she closes and pries.
            bool tnt = w.Actors[me].Cls == ClassId.Demolitions && Demolitions.TntWaitTicks(w, me) <= 0;
            if (tnt) want = 3.0;
            f.MoveX = 0;
            f.MoveZ = 0;
            f.AimX = w.X[foe];
            f.AimZ = w.Z[foe];
            f.Attack = tnt ? d >= w.Config.Abilities.DemolitionsTnt.MinThrowRange + 0.1 && d <= 4.5 : d <= reach - 0.05;
            f.Ability = false;
            f.Terraform = false;
            f.Jump = false;
            f.Dodge = false;
            if (d > want)
            {
                f.MoveX = dx / d;
                f.MoveZ = dz / d;
            }
            else if ((farmer && d < want - 0.6) || (tnt && d < want - 0.4))
            {
                f.MoveX = -dx / d;
                f.MoveZ = -dz / d;
            }
            return f;
        }

        internal struct DuelResult
        {
            public double Seconds;   // time to the first kill (NaN = nobody died within the limit)
            public int Winner;       // 0 = actor 0, 1 = the enemy, -1 = none
            public int Hits;         // hits landed by the winner (passive: by the attacker)
            public double HpLeft;    // the winner's HP left
        }

        /// <summary>A duel: actor 0 (cls) against the enemy (foeCls); fightBack = the enemy swings too, else it stands still.</summary>
        internal static DuelResult RunDuel(ClassId cls, ClassId foeCls, bool feel, bool fightBack, double limitSeconds = 30, GameConfig cfg = null)
        {
            World w = Arena(cls, foeCls, feel, cfg);
            int e = Foe(w);
            InputFrame fa = SimInput.EmptyInput(), fb = SimInput.EmptyInput();
            int head = w.EventHead, hits0 = 0, hits1 = 0;
            int limit = (int)(limitSeconds * w.Config.SimHz);
            for (int t = 1; t <= limit; t++)
            {
                Tick(w, Duel(w, 0, e, fa), fightBack ? Duel(w, e, 0, fb) : null);
                for (; head < w.EventHead; head++)
                {
                    SimEvent ev = w.Events[head % World.EventCapacity];
                    if (ev.Type != SimEventType.Hit) continue;
                    if (ev.A == e) hits0++;
                    else if (ev.A == 0) hits1++;
                }
                bool d0 = !w.Actors[0].Alive, d1 = !w.Actors[e].Alive;
                if (d0 || d1)
                {
                    int win = d1 && !d0 ? 0 : d0 && !d1 ? 1 : -1;
                    return new DuelResult
                    {
                        Seconds = t / w.Config.SimHz, Winner = win, Hits = win == 1 ? hits1 : hits0,
                        HpLeft = win == 0 ? w.Actors[0].Hp : win == 1 ? w.Actors[e].Hp : 0,
                    };
                }
            }
            return new DuelResult { Seconds = double.NaN, Winner = -1, Hits = hits0, HpLeft = 0 };
        }

        private static readonly ClassId[] Six = { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder, ClassId.Healer };

        private static string S(double v) => double.IsNaN(v) ? "\"never\"" : v.ToString("0.00");

        // ------------------------------------------------------------------------------------------------ the matrix

        /// <summary>
        /// The 36-duel time-to-kill matrix (attacker row on a defender standing still), before (rule off) and after (rule on),
        /// plus the 15 fight-back match-ups (both swing). Limits after: no "never"; every passive time 2.5 to 6.5 s outside the
        /// Healer's row (support: at most 10 s); no class wins every one of its five fight-back match-ups.
        /// </summary>
        [Test]
        public void TimeToKillMatrixBeforeAndAfter()
        {
            var bad = new List<string>();
            foreach (bool feel in new[] { false, true })
            {
                string arm = feel ? "after" : "before";
                foreach (ClassId atk in Six)
                {
                    var row = new System.Text.StringBuilder();
                    foreach (ClassId def in Six)
                    {
                        DuelResult r = RunDuel(atk, def, feel, false);
                        row.Append(def).Append('=').Append(double.IsNaN(r.Seconds) ? "never" : r.Seconds.ToString("0.00") + "s/" + r.Hits).Append(' ');
                        Console.WriteLine("{\"item\":\"b15 ttk passive\",\"arm\":\"" + arm + "\",\"attacker\":\"" + atk + "\",\"defender\":\"" + def +
                                          "\",\"seconds\":" + S(r.Seconds) + ",\"hits\":" + r.Hits + ",\"limit\":\"3-6 (Healer row <=7)\"}");
                        if (!feel) continue;
                        // pass 2 (coordinator): 3-6 s; the Healer's row (support, she mends first) at most 7 s.
                        double hi = atk == ClassId.Healer ? 7 : 6;
                        if (double.IsNaN(r.Seconds) || r.Seconds < 3 || r.Seconds > hi + 1e-9) bad.Add(atk + " on " + def + ": " + S(r.Seconds) + " s");
                    }
                    Console.WriteLine("[ttk " + arm + "] " + atk + ": " + row);
                }
                var wins = new Dictionary<ClassId, int>();
                foreach (ClassId c in Six) wins[c] = 0;
                for (int i = 0; i < Six.Length; i++)
                    for (int j = i + 1; j < Six.Length; j++)
                    {
                        DuelResult r = RunDuel(Six[i], Six[j], feel, true);
                        string who = r.Winner == 0 ? Six[i].ToString() : r.Winner == 1 ? Six[j].ToString() : "none";
                        if (r.Winner == 0) wins[Six[i]]++;
                        if (r.Winner == 1) wins[Six[j]]++;
                        if (feel && r.Winner < 0) bad.Add(Six[i] + " v " + Six[j] + ": nobody wins in 30 s");
                        Console.WriteLine("{\"item\":\"b15 ttk fight\",\"arm\":\"" + arm + "\",\"a\":\"" + Six[i] + "\",\"b\":\"" + Six[j] + "\",\"winner\":\"" + who +
                                          "\",\"seconds\":" + S(r.Seconds) + ",\"winnerHpLeft\":" + r.HpLeft.ToString("0.0") + "}");
                    }
                foreach (ClassId c in Six)
                {
                    Console.WriteLine("{\"item\":\"b15 fight wins\",\"arm\":\"" + arm + "\",\"class\":\"" + c + "\",\"wins\":" + wins[c] + ",\"of\":5,\"limit\":\"<5 after\"}");
                    if (feel && wins[c] >= 5) bad.Add(c + " wins every fight-back match-up");
                }
            }
            Assert.IsEmpty(bad, string.Join("\n", bad));
        }

        // ------------------------------------------------------------------------------------------------ A. phases

        [Test]
        public void EveryClassWindsUpForItsOwnReadableTimeAndTheCooldownCoversRecoveryAndWhiff()
        {
            World w = Arena(ClassId.Warrior, ClassId.Farmer);
            CombatFeelConfig f = w.Config.Depth.CombatFeel;
            foreach (ClassId c in Six)
            {
                ClassConfig cc = w.Config.Class(c);
                int wu = w.Dur.AttackWindup[(int)c], rec = w.Dur.Recovery[(int)c];
                Console.WriteLine("{\"item\":\"b15 phases\",\"class\":\"" + c + "\",\"windupTicks\":" + wu + ",\"recoveryTicks\":" + rec + ",\"cooldownS\":" + cc.AttackCooldown +
                                  ",\"limit\":\"wind-up >= 6 ticks (0.20 s); Warrior >= 12; cooldown >= wind-up + recovery\"}");
                Assert.GreaterOrEqual(wu, 6, c + " wind-up");
                Assert.LessOrEqual(wu + rec, (int)Math.Round(cc.AttackCooldown * w.Config.SimHz), c + " cooldown covers wind-up + recovery");
                Assert.AreSame(f.Classes[(int)c], cc, "GameConfig.Class returns the rule's row");
            }
            Assert.GreaterOrEqual(w.Dur.AttackWindup[(int)ClassId.Warrior], 12, "Warrior heavy swing 0.40 s");
            Assert.AreSame(Full(false).Classes[0].GetType(), Full(false).Class(ClassId.Warrior).GetType());
            Assert.AreEqual(22, Full(false).Class(ClassId.Warrior).AttackDamage, "rule off: the shipped row");
        }

        [Test]
        public void TheTelegraphCarriesTheRealHitShapeAndTheEdgeOfItIsTheEdgeOfTheHit()
        {
            foreach (ClassId c in Six)
            {
                foreach (bool inside in new[] { true, false })
                {
                    World w = Arena(c, ClassId.Miner);
                    ClassConfig cc = w.Config.Class(c);
                    int e = Foe(w);
                    double r = w.Config.Class(ClassId.Miner).Radius;
                    double edge = cc.AttackRange + r;
                    Put(w, e, w.X[0] + edge + (inside ? -0.03 : 0.06), Z0);
                    int head = w.EventHead;
                    var press = SimInput.EmptyInput();
                    press.Attack = true;
                    press.AimX = w.X[e];
                    press.AimZ = w.Z[e];
                    if (c == ClassId.Demolitions) w.Demolitions.TntReadyTick[0] = int.MaxValue; // the pry bar, not the TNT
                    Tick(w, press, null);
                    SimEvent start = null;
                    for (int h = head; h < w.EventHead; h++)
                    {
                        SimEvent ev = w.Events[h % World.EventCapacity];
                        if (ev.Type == SimEventType.AttackStart && ev.A == 0) start = ev;
                    }
                    Assert.NotNull(start, c + ": AttackStart");
                    Assert.AreEqual(cc.AttackRange, start.Radius, 1e-9, c + ": telegraph reach = hit reach");
                    Assert.AreEqual(cc.AttackArcDeg, start.Impulse, 1e-9, c + ": telegraph arc = hit arc");
                    double hp0 = w.Actors[e].Hp;
                    for (int t = 0; t < start.Windup + 1; t++) Tick(w, null, null);
                    bool hit = w.Actors[e].Hp < hp0;
                    Console.WriteLine("{\"item\":\"b15 telegraph edge\",\"class\":\"" + c + "\",\"case\":\"" + (inside ? "0.03 inside" : "0.06 outside") + "\",\"hit\":" + (hit ? "true" : "false") + "}");
                    Assert.AreEqual(inside, hit, c + (inside ? ": just inside the drawn edge is hit" : ": just outside is not"));
                }
            }
        }

        [Test]
        public void APressUpToTheBufferBeforeTheSwingIsReadyFiresAndAnEarlierOneDoesNot()
        {
            foreach (int early in new[] { 4, 12 })
            {
                World w = Arena(ClassId.Miner, ClassId.Farmer);
                int buffer = CombatFeel.Ticks(w, w.Config.Depth.CombatFeel.BufferSeconds);
                w.Actors[0].AttackCd = (early + 0.5) / w.Config.SimHz;
                var tap = SimInput.EmptyInput();
                tap.Attack = true;
                int swingAt = -1;
                for (int t = 0; t < 30; t++)
                {
                    int before = w.Actors[0].SwingTick;
                    Tick(w, t == 0 ? tap : null, null);
                    if (w.Actors[0].SwingTick != before && swingAt < 0) swingAt = t;
                }
                Console.WriteLine("{\"item\":\"b15 buffer\",\"pressTicksEarly\":" + early + ",\"bufferTicks\":" + buffer + ",\"swingTick\":" + swingAt + "}");
                if (early <= buffer) Assert.GreaterOrEqual(swingAt, 0, "a tap " + early + " ticks early fires");
                else Assert.AreEqual(-1, swingAt, "a tap " + early + " ticks early is dropped");
            }
        }

        [Test]
        public void AWhiffAddsRecoveryAndSlowsTheSwinger()
        {
            World w = Arena(ClassId.Warrior, ClassId.Farmer);
            int e = Foe(w);
            Put(w, e, X0 + 6, Z0); // far away: a whiff
            var press = SimInput.EmptyInput();
            press.Attack = true;
            press.AimX = X0 + 6;
            press.AimZ = Z0;
            Tick(w, press, null);
            Actor a = w.Actors[0];
            for (int t = 0; t < a.SwingWindup + 1; t++) Tick(w, null, null);
            int whiff = CombatFeel.Ticks(w, w.Config.Depth.CombatFeel.WhiffRecoverySeconds);
            Console.WriteLine("{\"item\":\"b15 whiff\",\"recovery\":" + a.SwingRecovery + ",\"expected\":" + (w.Dur.Recovery[0] + whiff) + ",\"cdS\":" + a.AttackCd.ToString("0.000") + "}");
            Assert.AreEqual(w.Dur.Recovery[0] + whiff, a.SwingRecovery, "whiff recovery");
            Assert.AreEqual(1, w.Depth.Feel.Whiffs);
            Assert.AreEqual(w.Config.Depth.CombatFeel.RecoveryMoveMultiplier, CombatFeel.SpeedScale(w, 0), 1e-9, "recovery move speed is the config's");
        }

        // ------------------------------------------------------------------------------------------------ B. hit reactions

        [Test]
        public void EveryHitStaggersAndKnocksBackWithDiminishingReturnsThenImmunity()
        {
            World w = Arena(ClassId.Miner, ClassId.Farmer);
            int e = Foe(w);
            CombatFeelConfig f = w.Config.Depth.CombatFeel;
            var stuns = new List<int>();
            for (int k = 0; k < 4; k++)
            {
                Put(w, 0, X0 - 1.0, Z0);
                w.Actors[e].Alive = true;
                w.Actors[e].Hp = w.Actors[e].MaxHp;
                w.Actors[e].HitStunTicks = 0;
                w.Actors[e].KVx = 0;
                w.Actors[e].KVz = 0;
                w.X[e] = X0 + 0.3;
                w.Z[e] = Z0;
                w.Actors[0].Facing = 0;
                double kv = 0;
                int stun = 0;
                w.Actors[0].AttackCd = 0;
                var press = SimInput.EmptyInput();
                press.Attack = true;
                press.AimX = w.X[e];
                press.AimZ = Z0;
                Tick(w, press, null);
                for (int t = 0; t < w.Actors[0].SwingWindup; t++)
                {
                    Tick(w, null, null);
                    if (w.Actors[e].HitStunTicks > stun) stun = w.Actors[e].HitStunTicks;
                    if (w.Actors[e].KVx > kv) kv = w.Actors[e].KVx;
                }
                stuns.Add(stun); // the hit lands in Combat, after this tick's Movement countdown: the full value is seen
                Console.WriteLine("{\"item\":\"b15 stagger\",\"hit\":" + (k + 1) + ",\"stunTicks\":" + stun + ",\"knockVx\":" + kv.ToString("0.000") + ",\"immuneUntil\":" + w.Depth.Feel.ImmuneUntil[e] + ",\"tick\":" + w.Tick + "}");
                if (k == 0) Assert.Greater(kv, 0, "the first hit knocks the target away (+x)");
            }
            int base0 = CombatFeel.Ticks(w, f.StaggerSeconds);
            Assert.AreEqual(base0, stuns[0], "first hit: the full stagger");
            Assert.Less(stuns[1], stuns[0], "second hit inside the window: diminished");
            Assert.LessOrEqual(stuns[2], stuns[1], "third hit: diminished again");
            Assert.AreEqual(0, stuns[3], "fourth hit: immune (no stagger)");
            Assert.GreaterOrEqual(w.Depth.Feel.StaggerImmune, 1);
        }

        [Test]
        public void TheWarriorsHeavySwingIsArmouredAgainstLightHits()
        {
            World w = Arena(ClassId.Warrior, ClassId.Miner);
            int e = Foe(w);
            Put(w, e, X0 + 0.0, Z0);
            Put(w, 0, X0 - 1.2, Z0);
            var press = SimInput.EmptyInput();
            press.Attack = true;
            press.AimX = w.X[e];
            press.AimZ = Z0;
            var foe = SimInput.EmptyInput();
            foe.Attack = true;
            foe.AimX = w.X[0];
            foe.AimZ = Z0;
            w.Actors[e].Facing = Math.PI;
            Tick(w, press, foe); // both press: the Miner's 0.20 s pick lands inside the Warrior's 0.40 s wind-up
            bool landed = false;
            double hp0 = w.Actors[e].Hp;
            for (int t = 0; t < 14; t++)
            {
                Tick(w, null, null);
                if (w.Actors[e].Hp < hp0) landed = true;
            }
            Console.WriteLine("{\"item\":\"b15 armour\",\"warriorHp\":" + w.Actors[0].Hp + ",\"warriorSwingLanded\":" + (landed ? "true" : "false") + ",\"armoured\":" + w.Depth.Feel.StaggerArmoured + "}");
            Assert.Less(w.Actors[0].Hp, w.Actors[0].MaxHp, "the Miner hit him first");
            Assert.IsTrue(landed, "his swing still landed");
            Assert.GreaterOrEqual(w.Depth.Feel.StaggerArmoured, 1);
        }

        [Test]
        public void KnockbackStopsAtAVoidEdgeNoRingOut()
        {
            World w = Arena(ClassId.Builder, ClassId.Farmer);
            int e = Foe(w);
            int edgeX = (int)X0 + 2;
            for (int cz = (int)Z0 - 4; cz <= (int)Z0 + 4; cz++) w.Map.Cells[cz * w.Map.Width + edgeX] = Cell.Void;
            Put(w, e, edgeX - 0.45, Z0); // touching the void
            Put(w, 0, edgeX - 1.9, Z0);
            var press = SimInput.EmptyInput();
            press.Attack = true;
            press.AimX = w.X[e];
            press.AimZ = Z0;
            Tick(w, press, null);
            double maxX = 0;
            for (int t = 0; t < 40; t++)
            {
                Tick(w, null, null);
                if (w.X[e] > maxX) maxX = w.X[e];
            }
            Console.WriteLine("{\"item\":\"b15 edge\",\"targetMaxX\":" + maxX.ToString("0.000") + ",\"voidAt\":" + edgeX + ",\"alive\":" + (w.Actors[e].Alive ? "true" : "false") + "}");
            Assert.Less(maxX, edgeX, "never pushed into the void");
            Assert.IsTrue(w.Actors[e].Alive);
        }

        // ------------------------------------------------------------------------------------------------ C. classes

        private static double OneHit(ClassId atk, ClassId def, double dist, Action<World> setup = null)
        {
            World w = Arena(atk, def);
            int e = Foe(w);
            Put(w, 0, X0 - dist, Z0);
            Put(w, e, X0, Z0);
            setup?.Invoke(w);
            if (atk == ClassId.Demolitions) w.Demolitions.TntReadyTick[0] = int.MaxValue;
            var press = SimInput.EmptyInput();
            press.Attack = true;
            press.AimX = X0;
            press.AimZ = Z0;
            double hp0 = w.Actors[e].Hp;
            Tick(w, press, null);
            for (int t = 0; t < w.Actors[0].SwingWindup + 1; t++) Tick(w, null, null);
            return hp0 - w.Actors[e].Hp;
        }

        [Test]
        public void MinerPiercesHeavyBodiesAndSundersFarmerIsWeakUpCloseBuilderIsStrongAtHome()
        {
            CombatFeelConfig f = Full(true).Depth.CombatFeel;
            double minerOnWarrior = OneHit(ClassId.Miner, ClassId.Warrior, 1.2);
            double minerOnFarmer = OneHit(ClassId.Miner, ClassId.Farmer, 1.2);
            double farmerFar = OneHit(ClassId.Farmer, ClassId.Miner, 2.0);
            double farmerClose = OneHit(ClassId.Farmer, ClassId.Miner, 0.9);
            double builderOpen = OneHit(ClassId.Builder, ClassId.Farmer, 1.2);
            double builderHome = OneHit(ClassId.Builder, ClassId.Farmer, 1.2, w =>
            {
                Barricade b = w.Barricades[0];
                b.Active = true;
                b.Team = TeamId.Azure;
                b.Cx = (int)X0 - 3;
                b.Cz = (int)Z0 + 2;
                b.Hp = 100;
                b.Ttl = 1e6;
                b.PrevCell = Cell.Floor;
                w.Map.Cells[b.Cz * w.Map.Width + b.Cx] = Cell.Barricade;
            });
            Console.WriteLine("{\"item\":\"b15 class mods\",\"minerOnWarrior\":" + minerOnWarrior + ",\"minerOnFarmer\":" + minerOnFarmer + ",\"farmerFar\":" + farmerFar +
                              ",\"farmerClose\":" + farmerClose + ",\"builderOpen\":" + builderOpen + ",\"builderHome\":" + builderHome + "}");
            Assert.AreEqual(f.Classes[(int)ClassId.Miner].AttackDamage * f.MinerHeavyPierce, minerOnWarrior, 1e-6, "Miner pierces the Warrior");
            Assert.AreEqual(f.Classes[(int)ClassId.Miner].AttackDamage, minerOnFarmer, 1e-6);
            Assert.AreEqual(20, farmerFar, 1e-6);
            Assert.AreEqual(20 * f.FarmerCloseShare, farmerClose, 1e-6, "Farmer weak up close");
            Assert.AreEqual(f.Classes[(int)ClassId.Builder].AttackDamage, builderOpen, 1e-6);
            Assert.AreEqual(f.Classes[(int)ClassId.Builder].AttackDamage * f.BuilderHomeMultiplier, builderHome, 1e-6, "Builder at home");

            // SUNDER: the second Miner hit on the Warrior takes the bonus.
            World s = Arena(ClassId.Miner, ClassId.Warrior);
            int e = Foe(s);
            Put(s, 0, X0 - 1.2, Z0);
            Put(s, e, X0, Z0);
            var press = SimInput.EmptyInput();
            press.Attack = true;
            press.AimX = X0;
            press.AimZ = Z0;
            var hits = new List<double>();
            double last = s.Actors[e].Hp;
            for (int t = 0; t < 40 && hits.Count < 2; t++)
            {
                press.AimX = s.X[e];
                Tick(s, press, null);
                if (s.Actors[e].Hp < last)
                {
                    hits.Add(last - s.Actors[e].Hp);
                    last = s.Actors[e].Hp;
                }
            }
            Console.WriteLine("{\"item\":\"b15 sunder\",\"first\":" + hits[0] + ",\"second\":" + (hits.Count > 1 ? hits[1] : -1) + "}");
            Assert.AreEqual(f.Classes[(int)ClassId.Miner].AttackDamage * f.MinerHeavyPierce * (1 + f.SunderDamageTaken), hits[1], 1e-6, "sundered");
        }

        [Test]
        public void TheHealersRowIsHerRealStrikeAndABotHealerMendsTheMostHurtAllyInRange()
        {
            World w = Arena(ClassId.Healer, ClassId.Miner);
            ClassConfig h = w.Config.Class(ClassId.Healer);
            Assert.Greater(h.AttackDamage, 0, "her row deals what her strike deals");
            Assert.AreEqual(h.AttackRange, OneHitReach(ClassId.Healer), 0.06, "row reach = measured reach");
            // Mend: two hurt allies in range; a bot picks the lower HP share, a player the one nearest her aim.
            int a1 = 1, a2 = 2;
            Put(w, a1, X0 - 3, Z0);
            Put(w, a2, X0 - 1, Z0 + 4.5);
            w.Actors[a1].Hp = w.Actors[a1].MaxHp * 0.8;
            w.Actors[a2].Hp = w.Actors[a2].MaxHp * 0.3;
            w.Actors[0].IsHuman = false;
            Assert.AreEqual(a2, Healer.MendTarget(w, 0, X0 - 3, Z0), "bot: most hurt");
            w.Actors[0].IsHuman = true;
            Assert.AreEqual(a1, Healer.MendTarget(w, 0, X0 - 3, Z0), "player: nearest the aim");
            double before = w.Actors[a1].Hp;
            Assert.IsTrue(Healer.TryMend(w, 0));
            Assert.AreEqual(w.Config.Depth.CombatFeel.MendHeal, w.Actors[a1].Hp - before, 1e-9, "MendHeal");
            Console.WriteLine("{\"item\":\"b15 healer\",\"rowReach\":" + h.AttackRange + ",\"rowDamage\":" + h.AttackDamage + ",\"mendRange\":" + CombatFeel.MendRange(w.Config) +
                              ",\"mendHeal\":" + CombatFeel.MendHeal(w.Config) + "}");
        }

        private static double OneHitReach(ClassId c)
        {
            double lo = 0.3, hi = 4;
            for (int k = 0; k < 14; k++)
            {
                double mid = (lo + hi) / 2;
                double d = OneHit(c, ClassId.Farmer, mid);
                if (d > 0) lo = mid;
                else hi = mid;
            }
            return lo - Full(true).Class(ClassId.Farmer).Radius;
        }

        [Test]
        public void HerOwnTntHurtsTheDemolitionsExpertWhenSheStandsInIt()
        {
            World w = Arena(ClassId.Demolitions, ClassId.Farmer);
            Put(w, Foe(w), X0 + 6, Z0);
            Demolitions.ThrowTnt(w, 0, w.X[0] + 0.5, Z0);
            double hp0 = w.Actors[0].Hp;
            for (int t = 0; t < 60; t++) Tick(w, null, null);
            double lost = hp0 - w.Actors[0].Hp;
            Console.WriteLine("{\"item\":\"b15 tnt self\",\"lost\":" + lost.ToString("0.00") + ",\"selfHits\":" + w.Depth.Feel.TntSelfHits + ",\"tntReadyIn\":" + Demolitions.TntWaitTicks(w, 0) + "}");
            Assert.Greater(lost, 0, "self-risk");
            Assert.Less(lost, w.Config.Abilities.DemolitionsTnt.Damage * w.Config.Depth.CombatFeel.TntSelfShare + 1e-9);
            Assert.AreEqual(1, w.Depth.Feel.TntSelfHits);
        }

        // ------------------------------------------------------------------------------------------------ F. ladders

        /// <summary>A built ladder of the real maximum length (LadderMaxCells = 2) at x = 57..58 on row 40 (Bridge over Wall, as
        /// BuiltPieces lays one), wall beside it, the enemy dead.</summary>
        private static World LadderArena()
        {
            World w = Arena(ClassId.Warrior, ClassId.Farmer);
            int W = w.Map.Width, lz = (int)Z0, l0 = (int)X0 + 1;
            BuiltPiece p = w.Builder.Pieces[0];
            p.Active = true;
            p.Carried = false;
            p.Kind = BuiltPieces.Ladder;
            p.Team = TeamId.Azure;
            p.Count = 2;
            for (int k = 0; k < 2; k++)
            {
                w.Map.Cells[(lz - 1) * W + l0 + k] = Cell.Wall;
                w.Map.Cells[(lz + 1) * W + l0 + k] = Cell.Wall;
                w.Map.Cells[lz * W + l0 + k] = Cell.Bridge;
                p.Cells[k] = lz * W + l0 + k;
                p.Prev[k] = Cell.Wall;
            }
            p.ExpireTick = int.MaxValue;
            w.Actors[Foe(w)].Alive = false;
            return w;
        }

        [Test]
        public void ALadderClimbTakesAReadableTimeAndTeammatesPassEachOtherOnIt()
        {
            World w = LadderArena();
            double l0 = (int)X0 + 1, l1 = l0 + 2;
            Put(w, 0, l0 - 1.5, Z0);
            var go = SimInput.EmptyInput();
            go.MoveX = 1;
            go.AimX = l1 + 5;
            go.AimZ = Z0;
            int t0 = -1, t1 = -1;
            for (int t = 0; t < 150 && t1 < 0; t++)
            {
                Tick(w, go, null);
                if (t0 < 0 && w.X[0] >= l0) t0 = t;
                if (t0 >= 0 && w.X[0] >= l1) t1 = t;
            }
            double secs = t1 < 0 || t0 < 0 ? -1 : (t1 - t0) / w.Config.SimHz;
            Console.WriteLine("{\"item\":\"b15 ladder climb\",\"cells\":2,\"seconds\":" + secs.ToString("0.00") + ",\"x\":" + w.X[0].ToString("0.00") + ",\"limit\":\"0.75-1.2\"}");
            Assert.GreaterOrEqual(secs, 0.75);
            Assert.LessOrEqual(secs, 1.2);

            // Two teammates head-on over the one-cell-wide ladder both get through (neither blocks the lane).
            World v = LadderArena();
            int mate = 1;
            Put(v, 0, l0 - 1.5, Z0);
            Put(v, mate, l1 + 1.5, Z0);
            v.Actors[mate].Team = v.Actors[0].Team;
            var east = SimInput.EmptyInput();
            east.MoveX = 1;
            east.AimX = l1 + 5;
            east.AimZ = Z0;
            var west = SimInput.EmptyInput();
            west.MoveX = -1;
            west.AimX = l0 - 5;
            west.AimZ = Z0;
            for (int t = 0; t < 150; t++)
            {
                SimInput.ApplyInput(v, mate, west);
                Tick(v, east, null);
            }
            Console.WriteLine("{\"item\":\"b15 ladder traffic\",\"eastX\":" + v.X[0].ToString("0.00") + ",\"westX\":" + v.X[mate].ToString("0.00") + ",\"laneFrom\":" + l0 + ",\"laneTo\":" + l1 + "}");
            Assert.Greater(v.X[0], l1, "the east-bound climber got through");
            Assert.Less(v.X[mate], l0, "the west-bound climber got through");
        }

        // ------------------------------------------------------------------------------------------------ rule off, determinism, cost

        [Test]
        public void TheRuleOffIsTheShippedFullGameBitForBit()
        {
            // SimPhysicsSoakTests.Golden Full row before b15 (b14-c, sim run 44159219254).
            ulong off = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, c => { c.ApplyRules(RulesProfile.Full); c.Depth.CombatFeel.Enabled = false; });
            ulong on = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, c => c.ApplyRules(RulesProfile.Full));
            ulong on2 = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, c => c.ApplyRules(RulesProfile.Full));
            ulong cls = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, c => c.ApplyRules(RulesProfile.Classic));
            ulong phys = MatchHash.Run(1, 16, 3600, ClassId.Warrior, null, c => c.ApplyRules(RulesProfile.PhysicsOnly));
            Console.WriteLine("{\"item\":\"b15 hashes\",\"fullOff\":\"0x" + off.ToString("X16") + "\",\"fullOn\":\"0x" + on.ToString("X16") + "\",\"classic\":\"0x" + cls.ToString("X16") +
                              "\",\"physicsOnly\":\"0x" + phys.ToString("X16") + "\"}");
            Assert.AreEqual(0xC0111682BA7C9FD6UL, off, "CombatFeel.Enabled = false is the b14 Full game");
            Assert.AreEqual(0x5C912AEED16BC302UL, cls, "Classic unchanged");
            Assert.AreEqual(0x0836DA5FD21C4854UL, phys, "PhysicsOnly unchanged");
            Assert.AreEqual(on, on2, "same seed, same match (determinism)");
            Assert.AreNotEqual(off, on, "the rule changes the Full match");
        }

        [Test]
        public void TheRulesOwnCallsAllocateNothingPerTickInA16v16()
        {
            // Whole-tick bytes are printed for both arms (the world allocates elsewhere, e.g. route rebuilds, and the two arms play
            // different fights); the rule's own per-tick entry points, called for every actor every tick, must be 0 B.
            var whole = new long[2];
            long own = 0;
            int swings = 0, staggers = 0;
            foreach (bool feel in new[] { false, true })
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
                cfg.Depth.CombatFeel.Enabled = feel;
                World w = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                InputFrame scratch = SimInput.EmptyInput();
                for (int t = 0; t < 600; t++) MatchTick.Advance(w, null, scratch);
                if (feel) OwnCalls(w); // first-call set-up (JIT, statics) outside the window
                for (int t = 0; t < 300; t++)
                {
                    long a0 = GC.GetAllocatedBytesForCurrentThread();
                    MatchTick.Advance(w, null, scratch);
                    whole[feel ? 1 : 0] += GC.GetAllocatedBytesForCurrentThread() - a0;
                    if (!feel) continue;
                    long o0 = GC.GetAllocatedBytesForCurrentThread();
                    OwnCalls(w);
                    own += GC.GetAllocatedBytesForCurrentThread() - o0;
                }
                if (feel)
                {
                    swings = w.Depth.Feel.Swings;
                    staggers = w.Depth.Feel.Staggers;
                }
            }
            Console.WriteLine("{\"item\":\"b15 alloc\",\"ruleOwnCallsBytes\":" + own + ",\"limit\":0,\"wholeTickBytesOn\":" + whole[1] + ",\"wholeTickBytesOff\":" + whole[0] +
                              ",\"swings\":" + swings + ",\"staggers\":" + staggers + "}");
            Assert.AreEqual(0, own, "the rule's own calls allocate nothing");
            Assert.Greater(swings, 0);
        }

        /// <summary>The rule's per-tick entry points. gameCalls = only where the game calls them (MendTarget for Healers, NearHome
        /// for Builders, Narrow with BotNarrowRawStep), still for every such actor every tick; false = every call for every actor.</summary>
        private static void OwnCalls(World w, bool gameCalls = false)
        {
            CombatFeel.RefreshLadders(w);
            bool narrow = !gameCalls || w.Config.Depth.CombatFeel.BotNarrowRawStep;
            for (int i = 0; i < w.ActorCount; i++)
            {
                int j = (i + 1) % w.ActorCount;
                ClassId c = w.Actors[i].Cls;
                CombatFeel.DamageMul(w, i, j, 1.0);
                CombatFeel.StaggerFor(w, j, 10, out bool _, out bool _);
                CombatFeel.SpeedScale(w, i);
                CombatFeel.OnLadder(w, i);
                if (!gameCalls || c == ClassId.Builder) CombatFeel.NearHome(w, i);
                if (narrow) CombatFeel.Narrow(w.Map, w.X[i], w.Z[i]);
                CombatFeel.BotReach(w, c, 1.5);
                if (!gameCalls || c == ClassId.Healer) Healer.MendTarget(w, i, double.NaN, double.NaN);
            }
        }

        /// <summary>
        /// The shiver gate's match (MovementShiverTests: seed 7, 16v16, Kaiju out at 0:30, two minutes) counted for the rule off,
        /// on, and on with one part taken back, so a flag count names its part. Printed only (the gate itself asserts).
        /// </summary>
        [Test]
        public void ShiverFlagsOfTheGateMatchByPartOfTheRule()
        {
            string[] names = { "off", "on", "on, raw step in narrow spots", "on, no stagger or knock", "on, shipped wind-ups", "on, move slow 0.6/0.55/0.6 in swings" };
            for (int a = 0; a < names.Length; a++)
            {
                GameConfig cfg = GameConfig.CreateDefault();
                cfg.HatchSeconds = 30;
                CombatFeelConfig f = cfg.Depth.CombatFeel;
                f.Enabled = a != 0;
                if (a == 2) f.BotNarrowRawStep = true;
                if (a == 3)
                {
                    f.StaggerSeconds = 0;
                    f.HeavyStaggerSeconds = 0;
                    for (int c = 0; c < 6; c++) f.Strikes[c].Knock = 0;
                }
                if (a == 4)
                {
                    GameConfig shipped = Full(false);
                    for (int c = 0; c < 6; c++)
                    {
                        f.Classes[c].AttackWindupSeconds = shipped.Classes[c].AttackWindupSeconds;
                        f.Classes[c].RecoverySeconds = shipped.Classes[c].RecoverySeconds;
                    }
                }
                if (a == 5)
                {
                    f.WindupMoveMultiplier = 0.6;
                    f.RecoveryMoveMultiplier = 0.55;
                    f.StaggerSlowMultiplier = 0.6;
                }
                World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                var watch = new ShiverWatch();
                InputFrame scratch = SimInput.EmptyInput();
                for (int t = 0; t < 3600 && w.Match.Phase != MatchPhase.Ended; t++)
                {
                    MatchTick.Advance(w, null, scratch);
                    watch.Observe(w);
                }
                int kaiju = 0, hard = 0, fightRev = 0;
                foreach (ShiverFlag fl in watch.Flags)
                {
                    if (fl.Actor == ShiverWatch.KaijuId) { kaiju++; continue; }
                    if (fl.Kind == ShiverKind.Reversals || (fl.Kind == ShiverKind.Dither && fl.Reversals >= 4))
                    {
                        hard++;
                        if (fl.State == AiState.Fight) fightRev++;
                    }
                }
                Console.WriteLine("{\"item\":\"b15 shiver by part\",\"arm\":\"" + names[a] + "\",\"flags\":" + watch.Count + ",\"hard\":" + hard + ",\"hardInFight\":" + fightRev +
                                  ",\"kaiju\":" + kaiju + ",\"dodges\":" + w.Dodges.Count + "}");
            }
        }

        /// <summary>
        /// Sim cost per tick, same job, arms stepped in turn tick by tick (B13CombinedCostTests' method: JIT and runner drift fall
        /// on every arm alike; trimmed mean drops the slowest 2%). Arms: off; on; and on with one part of the rule taken back,
        /// so a cost over budget names its part. The rule changes the fight, so on and off are different matches.
        /// </summary>
        [Test]
        public void TickCostWithTheRuleOnIsWithinTheBudget()
        {
            // Four seeds, so one arm landing in a 30-body pile-up (run 44233001957: on 104 us vs 52 us the run before, same
            // code) does not decide it; the arms of one seed step in turn.
            string[] names = { "off", "on", "on, shipped wind-ups", "on, no stagger or knock", "on, raw step in narrow spots" };
            int n = names.Length;
            const int Warm = 7500, Ticks = 900;
            int[] seeds = { 4242, 7, 11, 1337 };
            var us = new double[n];
            foreach (int seed in seeds)
            {
            var worlds = new World[n];
            var scratch = new InputFrame[n];
            for (int a = 0; a < n; a++)
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
                CombatFeelConfig f = cfg.Depth.CombatFeel;
                f.Enabled = a != 0;
                if (a == 2)
                {
                    GameConfig shipped = Full(false);
                    for (int c = 0; c < 6; c++)
                    {
                        f.Classes[c].AttackWindupSeconds = shipped.Classes[c].AttackWindupSeconds;
                        f.Classes[c].RecoverySeconds = shipped.Classes[c].RecoverySeconds;
                    }
                }
                if (a == 3)
                {
                    f.StaggerSeconds = 0;
                    f.HeavyStaggerSeconds = 0;
                    for (int c = 0; c < 6; c++) f.Strikes[c].Knock = 0;
                }
                if (a == 4) f.BotNarrowRawStep = true;
                worlds[a] = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                scratch[a] = SimInput.EmptyInput();
            }
            for (int t = 0; t < Warm; t++)
                for (int a = 0; a < n; a++) MatchTick.Advance(worlds[a], null, scratch[a]);
            var per = new long[n][];
            for (int a = 0; a < n; a++) per[a] = new long[Ticks];
            for (int t = 0; t < Ticks; t++)
                for (int a = 0; a < n; a++)
                {
                    long c0 = Stopwatch.GetTimestamp();
                    MatchTick.Advance(worlds[a], null, scratch[a]);
                    per[a][t] = Stopwatch.GetTimestamp() - c0;
                }
            int keep = Ticks - Ticks / 50;
            var one = new double[n];
            for (int a = 0; a < n; a++)
            {
                long[] s = (long[])per[a].Clone();
                Array.Sort(s);
                long sum = 0;
                for (int i = 0; i < keep; i++) sum += s[i];
                one[a] = sum * 1e6 / Stopwatch.Frequency / keep;
                us[a] += one[a] / seeds.Length;
                Console.WriteLine("{\"item\":\"b15 tick cost seed\",\"seed\":" + seed + ",\"arm\":\"" + names[a] + "\",\"usPerTick\":" + one[a].ToString("0.0") + ",\"vsOff\":" + (one[a] / one[0] - 1).ToString("0.000") + "}");
            }
            }
            for (int a = 0; a < n; a++)
                Console.WriteLine("{\"item\":\"b15 tick cost arm\",\"arm\":\"" + names[a] + "\",\"usPerTick\":" + us[a].ToString("0.0") + ",\"vsOff\":" + (us[a] / us[0] - 1).ToString("0.000") + ",\"seeds\":4}");
            double delta = us[1] / us[0] - 1;
            // Same job: the rule's own per-tick code, each entry point called for every actor of the class the game calls it for,
            // every tick (an upper bound: in the game DamageMul, StaggerFor and NearHome run per hit only, MendTarget per press), timed on the same world against its whole tick.
            GameConfig oc = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
            World ow = World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = oc });
            InputFrame os = SimInput.EmptyInput();
            for (int t = 0; t < Warm; t++) MatchTick.Advance(ow, null, os);
            long tick = 0, own = 0;
            for (int t = 0; t < Ticks; t++)
            {
                long c0 = Stopwatch.GetTimestamp();
                MatchTick.Advance(ow, null, os);
                long c1 = Stopwatch.GetTimestamp();
                OwnCalls(ow, true);
                long c2 = Stopwatch.GetTimestamp();
                tick += c1 - c0;
                own += c2 - c1;
            }
            double share = (double)own / Math.Max(1, tick);
            Console.WriteLine("{\"item\":\"b15 tick cost\",\"offUs\":" + us[0].ToString("0.0") + ",\"onUs\":" + us[1].ToString("0.0") + ",\"wholeTickDelta\":" + delta.ToString("0.000") +
                              ",\"ruleOwnUsPerTick\":" + (own * 1e6 / Stopwatch.Frequency / Ticks).ToString("0.00") + ",\"ruleOwnShare\":" + share.ToString("0.000") +
                              ",\"limit\":0.05,\"note\":\"whole-tick arms play different fights (seed-to-seed spread is -15% to +62% even with the shipped wind-ups); the asserted number is the rule's own code on the same world\"}");
            Assert.LessOrEqual(share, 0.05, "the rule's own per-tick code, same job");
        }
    }
}
