using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// DG-22 Demolitions Expert. Ports of the CM-38 cases of src/sim/demolitions.test.ts (09e0904) on the same hand-painted
    /// strip (pad island, 3-wide bridge, hill island), plus the DG-22 view events, the explosion cause and meat, and the bots.
    /// The CM-54 class-switch case of that file belongs to CM-84 (not ported yet in Unity). All time is sim ticks.
    /// </summary>
    [TestFixture]
    public sealed class DemolitionsTests
    {
        private const double PadX = 5.5, PadZ = 13.5, HillX = 36.5, HillZ = 13.5;
        private const int Demo = 0;

        private static GameConfig Cfg => SimPhysicsTests.ClassicBrowser();
        private static DemolitionsMineConfig MineCfg => Cfg.Abilities.DemolitionsMine;
        private static DemolitionsTntConfig TntCfg => Cfg.Abilities.DemolitionsTnt;
        private static double Dt => SimStep.Dt(Cfg);

        private static void Paint(World w, int x0, int z0, int x1, int z1, byte t)
        {
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) w.Map.Cells[z * w.Map.Width + x] = t;
        }

        private static byte CellAt(World w, int cx, int cz) => w.Map.Cells[cz * w.Map.Width + cx];
        private static int Idx(World w, int cx, int cz) => cz * w.Map.Width + cx;

        private static List<SimEvent> Since(World w, int head, SimEventType? type = null)
        {
            var list = new List<SimEvent>();
            for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (type == null || e.Type == type.Value)
                    list.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value, Bio = e.Bio, Cause = e.Cause, Radius = e.Radius });
            }
            return list;
        }

        /// <summary>Revive actor j at (x, z): full hp, no protection, grounded, standing.</summary>
        private static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            a.Hp = a.MaxHp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            a.AirTick = 0;
            a.AbilityActive = 0;
            w.X[j] = x;
            w.Z[j] = z;
        }

        /// <summary>The k-th actor id of team, skipping the Demolitions Expert.</summary>
        private static int Nth(World w, TeamId team, int k)
        {
            int seen = 0;
            for (int j = 1; j < w.ActorCount; j++)
            {
                if (w.Actors[j].Team != team) continue;
                if (seen == k) return j;
                seen++;
            }
            throw new InvalidOperationException("no actor " + k + " on " + team);
        }

        /// <summary>The oracle's strip: the human Demolitions Expert on the bridge at (15.5, 13.5) facing +x, everyone else dead, tick 100.</summary>
        private static World Strip()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 38, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Demolitions, Config = Cfg });
            MapDef map = w.Map;
            Array.Fill(map.Cells, Cell.Void);
            Array.Clear(map.WallHp, 0, map.WallHp.Length);
            Paint(w, 2, 10, 8, 16, Cell.Floor);
            Paint(w, 9, 12, 29, 14, Cell.Bridge);
            Paint(w, 30, 8, 42, 18, Cell.Floor);
            for (int t = 0; t < 2; t++)
            {
                map.Pads[t].X = PadX;
                map.Pads[t].Z = PadZ;
            }
            map.Hill.X = HillX;
            map.Hill.Z = HillZ;
            map.Hill.R = 3;
            map.Shrine.X = 100.5;
            map.Shrine.Z = 60.5;
            foreach (ResourceNode nd in w.Nodes)
            {
                nd.X = -100;
                nd.Z = -100;
            }
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
            }
            Put(w, Demo, 15.5, 13.5);
            w.Actors[Demo].Facing = 0;
            w.Actors[Demo].AbilityCd = 0;
            w.Tick = 100;
            Assert.AreEqual(ClassId.Demolitions, w.Actors[Demo].Cls);
            return w;
        }

        [Test]
        public void CM38TheAttackInputDoesNothingForThisClassAMinerInTheSameSpotDoesHit()
        {
            World w = Strip();
            int e = Nth(w, TeamId.Crimson, 0);
            Put(w, e, 16.5, 13.5);
            w.Inputs[Demo].Attack = true;
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            double hp0 = w.Actors[e].Hp;
            int head = w.EventHead;
            Combat.CombatSystem(w, Dt);
            Assert.AreEqual(hp0, w.Actors[e].Hp);
            Assert.AreEqual(0, w.Actors[Demo].AttackCd);
            Assert.IsEmpty(Since(w, head, SimEventType.Hit));
            Assert.IsEmpty(Since(w, head, SimEventType.AttackStart));
            // Control: the same swing from a miner lands, so the test measures the class rule, not the geometry.
            w.Actors[Demo].Cls = ClassId.Miner;
            Combat.CombatSystem(w, Dt);
            Assert.Less(w.Actors[e].Hp, hp0);
        }

        [Test]
        public void CM38SpaceLaysAMineThroughAbilitiesSystemAndSpendsTheCooldownARefusedSpotSpendsNothing()
        {
            World w = Strip();
            w.Z[Demo] = 12.2;
            w.HasAim[Demo] = 1;
            w.Inputs[Demo].AimX = 15.5;
            w.Inputs[Demo].AimZ = 10.9;
            w.Inputs[Demo].Ability = true;
            int head = w.EventHead;
            Abilities.AbilitiesSystem(w, Dt);
            Assert.AreEqual(0, w.Actors[Demo].AbilityCd);
            CollectionAssert.AreEqual(new[] { (double)Demolitions.MineRefusedRange }, Since(w, head, SimEventType.Ability).Select(e => e.Value).ToArray());

            w.Inputs[Demo].AimX = 16.5;
            w.Inputs[Demo].AimZ = 13.5;
            head = w.EventHead;
            Abilities.AbilitiesSystem(w, Dt);
            Assert.AreEqual(w.Config.Class(ClassId.Demolitions).AbilityCooldown, w.Actors[Demo].AbilityCd);
            CollectionAssert.AreEqual(new[] { (double)Demolitions.MinePlacedCode }, Since(w, head, SimEventType.Ability).Select(e => e.Value).ToArray());
            Mine[] live = w.Demolitions.Mines.Where(m => m.Active).ToArray();
            Assert.AreEqual(1, live.Length);
            Assert.AreEqual(16, live[0].Cx);
            Assert.AreEqual(13, live[0].Cz);
            Assert.AreEqual(TeamId.Azure, live[0].Team);
            // DG-22 view event: where and how big.
            SimEvent placed = Since(w, head, SimEventType.MinePlaced).Single();
            Assert.AreEqual(Demo, placed.A);
            Assert.AreEqual(16.5, placed.X);
            Assert.AreEqual(13.5, placed.Z);
            Assert.AreEqual(MineCfg.BlastRadius, placed.Radius);
            Assert.AreEqual(w.Tick + MineCfg.ArmTicks, placed.Value);
        }

        [Test]
        public void CM38TriggersOnTheFirstEnemyEnteringItsCellNeverOnAlliesOnlyOnceArmed()
        {
            World w = Strip();
            int ally = Nth(w, TeamId.Azure, 0);
            int e1 = Nth(w, TeamId.Crimson, 0);
            int e2 = Nth(w, TeamId.Crimson, 1);
            Put(w, ally, 25.5, 13.5);
            Put(w, e1, 26.5, 13.5);
            Put(w, e2, 27.5, 13.5);
            w.X[Demo] = 21.3;
            Assert.AreEqual(Demolitions.MinePlacedCode, Demolitions.PlaceMine(w, Demo, 22.5, 13.5));
            DemolitionsState st = w.Demolitions;

            // Not armed yet: an enemy on the cell does nothing.
            w.X[e1] = 22.5;
            int head = w.EventHead;
            Demolitions.DemolitionsBlastSystem(w);
            Assert.IsEmpty(Since(w, head));
            w.X[e1] = 26.5;

            w.Tick += (int)MineCfg.ArmTicks;
            // An ally walks over the armed mine: nothing but the (DG-22) armed event.
            w.X[ally] = 22.4;
            w.Z[ally] = 13.6;
            head = w.EventHead;
            Demolitions.DemolitionsBlastSystem(w);
            CollectionAssert.AreEqual(new[] { SimEventType.MineArmed }, Since(w, head).Select(e => e.Type).ToArray());
            Assert.AreEqual(1, st.Mines.Count(m => m.Active));
            Assert.AreEqual(w.Actors[ally].MaxHp, w.Actors[ally].Hp);

            // The first enemy in: hit for the mine's damage, credited to the owner, cause Explosion; the ally beside it is not.
            w.X[e1] = 22.6;
            w.Z[e1] = 13.4;
            double hp0 = w.Actors[e1].Hp;
            head = w.EventHead;
            Demolitions.DemolitionsBlastSystem(w);
            SimEvent hit = Since(w, head, SimEventType.Hit).Single();
            Assert.AreEqual(e1, hit.A);
            Assert.AreEqual(Demo, hit.B);
            Assert.AreEqual(MineCfg.Damage, hit.Value);
            Assert.AreEqual(DamageCause.Explosion, hit.Cause);
            Assert.AreEqual(hp0 - MineCfg.Damage, w.Actors[e1].Hp);
            Assert.AreEqual(w.Actors[ally].MaxHp, w.Actors[ally].Hp);
            SimEvent blast = Since(w, head, SimEventType.Ability).Single();
            Assert.AreEqual(-1, blast.A);
            Assert.AreEqual(Demo, blast.B);
            Assert.AreEqual(Demolitions.MineBlastCode, blast.Value);
            SimEvent trig = Since(w, head, SimEventType.MineTriggered).Single();
            Assert.AreEqual(e1, trig.A);
            Assert.AreEqual(Demo, trig.B);
            SimEvent boom = Since(w, head, SimEventType.Explosion).Single();
            Assert.AreEqual(Demolitions.ExplosionMine, boom.B);
            Assert.AreEqual(22.5, boom.X);
            Assert.AreEqual(13.5, boom.Z);
            Assert.AreEqual(MineCfg.BlastRadius, boom.Radius);
            Assert.AreEqual(1, boom.Value, "actors hurt");
            Assert.AreEqual(0, st.Mines.Count(m => m.Active));
            Assert.AreEqual(1, st.Stats.MinesTriggered);

            // Spent: the next enemy on the cell is not hit.
            w.X[e2] = 22.5;
            head = w.EventHead;
            Demolitions.DemolitionsBlastSystem(w);
            Assert.IsEmpty(Since(w, head, SimEventType.Hit));
        }

        [Test]
        public void CM38FThrowsOnTheRisingEdgeOnlyAndObeysItsOwnCooldownInTicks()
        {
            World w = Strip();
            w.HasAim[Demo] = 1;
            w.Inputs[Demo].AimX = 19.5;
            w.Inputs[Demo].AimZ = 13.5;
            w.Inputs[Demo].Terraform = true;
            int head = w.EventHead;
            Abilities.AbilitiesSystem(w, Dt);
            Abilities.AbilitiesSystem(w, Dt); // held: no second throw
            Assert.AreEqual(1, Since(w, head, SimEventType.Ability).Count(e => e.Value == Demolitions.TntThrownCode));
            Assert.IsEmpty(Since(w, head, SimEventType.Terraform), "the terraform system ignores a non-miner");
            Assert.AreEqual((int)TntCfg.CooldownTicks, Demolitions.TntWaitTicks(w, Demo));
            Assert.AreEqual(Demolitions.TntRefusedCooldown, Demolitions.ThrowTnt(w, Demo, 19.5, 13.5));
            w.Tick += (int)TntCfg.CooldownTicks;
            Assert.AreEqual(0, Demolitions.TntWaitTicks(w, Demo));
            // DG-22 view event of the throw: the landing point and the blast radius.
            SimEvent thrown = Since(w, head, SimEventType.TntThrown).Single();
            Assert.AreEqual(19.5, thrown.X);
            Assert.AreEqual(13.5, thrown.Z);
            Assert.AreEqual(TntCfg.BlastRadius, thrown.Radius);
        }

        [Test]
        public void CM38TntHurtsEnemiesInItsRadiusRemovesBlocksThroughTheCM30RulesAndRefusesTheCellThatWouldCutTheLastRoute()
        {
            World w = Strip();
            int e = Nth(w, TeamId.Crimson, 0);
            int far = Nth(w, TeamId.Crimson, 1);
            int ally = Nth(w, TeamId.Azure, 0);
            Put(w, e, 21.6, 13.5); // 2.1 from the blast: inside BlastRadius + radius, clear of every crater cell
            Put(w, far, 25.5, 13.5);
            Put(w, ally, 21.6, 12.6);
            Paint(w, 20, 12, 20, 12, Cell.BreakableIntact); // a breakable panel in the bridge, centre 1.41 from the blast
            var hill = new[] { new PointXZ(HillX, HillZ) };
            FlowField before = FlowField.ComputeFlowField(w.Map, hill);
            int padCell = Idx(w, (int)Math.Floor(PadX), (int)Math.Floor(PadZ));
            Assert.IsTrue(double.IsFinite(before.Dist[Idx(w, 19, 13)]));
            double padDist0 = before.Dist[padCell];
            int wv0 = w.WallVersion;
            int gv0 = w.GridVersion;
            double hpE = w.Actors[e].Hp;

            Assert.AreEqual(Demolitions.TntThrownCode, Demolitions.ThrowTnt(w, Demo, 19.5, 13.5));
            // The fuse: nothing but the (DG-22) touch-down before FuseTicks.
            w.Tick += (int)TntCfg.FuseTicks - 1;
            int head = w.EventHead;
            Demolitions.DemolitionsBlastSystem(w);
            CollectionAssert.AreEqual(new[] { SimEventType.TntLanded }, Since(w, head).Select(x => x.Type).ToArray());
            w.Tick += 1;
            head = w.EventHead;
            Demolitions.DemolitionsBlastSystem(w);

            SimEvent hit = Since(w, head, SimEventType.Hit).Single();
            Assert.AreEqual(e, hit.A);
            Assert.AreEqual(Demo, hit.B);
            Assert.AreEqual(TntCfg.Damage, hit.Value);
            Assert.AreEqual(hpE - TntCfg.Damage, w.Actors[e].Hp);
            Assert.AreEqual(w.Actors[far].MaxHp, w.Actors[far].Hp);
            Assert.AreEqual(w.Actors[ally].MaxHp, w.Actors[ally].Hp);

            // Terrain: the plus of 5 bridge cells is cratered row-major; the last cell of the x = 19 column is refused.
            Assert.AreEqual(Cell.Void, CellAt(w, 19, 12));
            Assert.AreEqual(Cell.Void, CellAt(w, 18, 13));
            Assert.AreEqual(Cell.Void, CellAt(w, 19, 13));
            Assert.AreEqual(Cell.Void, CellAt(w, 20, 13));
            Assert.AreEqual(Cell.Bridge, CellAt(w, 19, 14));
            Assert.AreEqual(Cell.BreakableDestroyed, CellAt(w, 20, 12));
            var tf = Since(w, head, SimEventType.Terraform).Select(x => (x.A, x.B, (int)x.Value)).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                (Demo, Idx(w, 19, 12), Terraform.Dug),
                (Demo, Idx(w, 18, 13), Terraform.Dug),
                (Demo, Idx(w, 19, 13), Terraform.Dug),
                (Demo, Idx(w, 20, 13), Terraform.Dug),
                (Demo, Idx(w, 19, 14), Terraform.RefusedRoute),
            }, tf);
            SimEvent wb = Since(w, head, SimEventType.WallBreak).Single();
            Assert.AreEqual(Idx(w, 20, 12), wb.B);
            Assert.AreEqual(Cell.BreakableDestroyed, wb.Value);
            SimEvent ab = Since(w, head, SimEventType.Ability).Single();
            Assert.AreEqual(-1, ab.A);
            Assert.AreEqual(Demo, ab.B);
            Assert.AreEqual(Demolitions.TntBlastCode, ab.Value);
            SimEvent boom = Since(w, head, SimEventType.Explosion).Single();
            Assert.AreEqual(Demolitions.ExplosionTnt, boom.B);
            Assert.AreEqual(19.5, boom.X);
            Assert.AreEqual(TntCfg.BlastRadius, boom.Radius);
            DemolitionsState st = w.Demolitions;
            Assert.AreEqual(4, st.Stats.CellsCarved);
            Assert.AreEqual(1, st.Stats.WallsBroken);
            Assert.AreEqual(1, st.Stats.RouteRefusals);

            // Every change went through SetCell: WallVersion and GridVersion moved once per cell.
            Assert.AreEqual(wv0 + 5, w.WallVersion);
            Assert.AreEqual(gv0 + 5, w.GridVersion);
            Assert.AreEqual(w.Tick, w.WallChangeTick);

            // The flow field repaths: the crater is unreachable, the pad still reaches the hill, the long way round.
            FlowField after = FlowField.ComputeFlowField(w.Map, hill);
            Assert.IsFalse(double.IsFinite(after.Dist[Idx(w, 19, 13)]));
            Assert.IsTrue(double.IsFinite(after.Dist[padCell]));
            Assert.Greater(after.Dist[padCell], padDist0);
            Assert.IsTrue(Terraform.PadsReachHill(w.Map));
        }

        [Test]
        public void CM38InsideTheCM30WorldGapTheBlastStillHurtsButTheCraterIsRefused()
        {
            World w = Strip();
            int e = Nth(w, TeamId.Crimson, 0);
            Put(w, e, 21.6, 13.5);
            Assert.AreEqual(Demolitions.TntThrownCode, Demolitions.ThrowTnt(w, Demo, 19.5, 13.5));
            w.Tick += (int)TntCfg.FuseTicks;
            w.WallChangeTick = w.Tick - 1; // a wall changed one tick ago
            int wv0 = w.WallVersion;
            int head = w.EventHead;
            Demolitions.DemolitionsBlastSystem(w);
            Assert.AreEqual(w.Actors[e].MaxHp - TntCfg.Damage, w.Actors[e].Hp);
            Assert.AreEqual(wv0, w.WallVersion);
            Assert.AreEqual(Cell.Bridge, CellAt(w, 19, 13));
            CollectionAssert.AreEqual(new[] { (double)Terraform.RefusedRate }, Since(w, head, SimEventType.Terraform).Select(x => x.Value).ToArray());
            Assert.AreEqual(1, w.Demolitions.Stats.RateRefusals);
        }

        [Test]
        public void DG22ABlastKillLeavesItsMeatWhereTheUnitDiedAndTheDeathSaysExplosion()
        {
            World w = Strip();
            int e = Nth(w, TeamId.Crimson, 0);
            Put(w, e, 20.4, 13.6);
            w.Actors[e].Hp = 10;
            Assert.AreEqual(Demolitions.TntThrownCode, Demolitions.ThrowTnt(w, Demo, 19.5, 13.5));
            w.Tick += (int)TntCfg.FuseTicks;
            int head = w.EventHead;
            int kills0 = w.Actors[Demo].Kills;
            Demolitions.DemolitionsBlastSystem(w);
            Combat.DropCorpseMeat(w);
            Assert.IsFalse(w.Actors[e].Alive);
            SimEvent death = Since(w, head, SimEventType.Death).Single();
            Assert.AreEqual(DamageCause.Explosion, death.Cause);
            Assert.AreEqual(Demo, death.B);
            Assert.AreEqual(kills0 + 1, w.Actors[Demo].Kills);
            Assert.AreEqual(1, w.Demolitions.Stats.Kills);
            LooseOrb orb = w.Orbs.Single(o => o.Active && o.Source == e); // the other dead actors of the strip drop theirs too
            Assert.AreEqual(20.4, orb.X);
            Assert.AreEqual(13.6, orb.Z);
        }

        [Test]
        public void DG22AMineArmsExpiresAndLeavesWithoutABlastWhenItsGroundGoesAndTheOldestIsRecycled()
        {
            World w = Strip();
            w.X[Demo] = 21.3;
            Assert.AreEqual(Demolitions.MinePlacedCode, Demolitions.PlaceMine(w, Demo, 22.5, 13.5));
            w.Actors[Demo].AbilityCd = 0;
            w.Tick += 1;
            Assert.AreEqual(Demolitions.MinePlacedCode, Demolitions.PlaceMine(w, Demo, 21.5, 12.5));
            w.Actors[Demo].AbilityCd = 0;
            w.Tick += 1;
            int head = w.EventHead;
            // A third mine recycles the oldest (22, 13).
            Assert.AreEqual(Demolitions.MinePlacedCode, Demolitions.PlaceMine(w, Demo, 21.5, 14.5));
            SimEvent rec = Since(w, head, SimEventType.MineRemoved).Single();
            Assert.AreEqual(Demolitions.MineRecycled, rec.Value);
            Assert.AreEqual(22.5, rec.X);
            Assert.AreEqual(2, w.Demolitions.Mines.Count(m => m.Active && m.Owner == Demo));
            // The ground under (21, 12) goes: that mine leaves without a blast.
            MapGrid.SetCell(w, 21, 12, Cell.Void);
            head = w.EventHead;
            Demolitions.DemolitionsBlastSystem(w);
            SimEvent gone = Since(w, head, SimEventType.MineRemoved).Single();
            Assert.AreEqual(Demolitions.MineGroundGone, gone.Value);
            Assert.IsEmpty(Since(w, head, SimEventType.Explosion));
            // The last one expires after LifetimeTicks.
            w.Tick += (int)MineCfg.LifetimeTicks;
            head = w.EventHead;
            Demolitions.DemolitionsBlastSystem(w);
            Assert.AreEqual(Demolitions.MineExpired, Since(w, head, SimEventType.MineRemoved).Single().Value);
            Assert.AreEqual(0, w.Demolitions.Mines.Count(m => m.Active));
        }

        [Test]
        public void DG22MinesAndBombsResetWithTheWorld()
        {
            World w = Strip();
            Demolitions.PlaceMine(w, Demo, 16.5, 13.5);
            Demolitions.ThrowTnt(w, Demo, 19.5, 13.5);
            World.ResetWorld(w, w.Opts);
            Assert.IsFalse(w.Demolitions.Mines.Any(m => m.Active));
            Assert.IsFalse(w.Demolitions.Bombs.Any(b => b.Active));
            Assert.AreEqual(0, w.Demolitions.Stats.MinesPlaced);
            Assert.AreEqual(0, Demolitions.TntWaitTicks(w, Demo));
        }

        // ------------------------------------------------------------------------------------------------- bots

        /// <summary>The real map, actor 0 a Demolitions Expert on autopilot west of the hill, one enemy 4 units east, everyone else dead.</summary>
        private static World Duel(BotDifficulty d, double enemyVz)
        {
            World w = World.CreateWorld(new WorldOptions
            {
                Seed = 22, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Demolitions, Config = Cfg,
                AllyBotDifficulty = d, BotDifficulty = d,
            });
            for (int j = 1; j < w.ActorCount; j++)
            {
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
            }
            HillDef hill = w.Map.Hill;
            Put(w, Demo, hill.X - 2, hill.Z);
            int e = Nth(w, TeamId.Crimson, 0);
            Put(w, e, hill.X + 2, hill.Z);
            w.Vz[e] = enemyVz;
            return w;
        }

        private static InputFrame BotTick(World w)
        {
            InputFrame f = SimInput.EmptyInput();
            BotBrain.FillBotInput(w, Demo, f); // puts actor 0 on autopilot
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, w.ActorCount);
            BotBrain.BotsThink(w, Dt);
            BotBrain.FillBotInput(w, Demo, f);
            return f;
        }

        [Test]
        public void DG22ADemolitionsBotThrowsTntAtAnEnemyInRangeAsOnePressThenLaysMinesWhileTntCoolsDown()
        {
            World w = Duel(BotDifficulty.Normal, 0);
            int e = Nth(w, TeamId.Crimson, 0);
            InputFrame f = BotTick(w);
            Assert.IsTrue(f.Terraform, "TNT press");
            Assert.AreEqual(JsMath.F32(w.X[e]), f.AimX, 1e-9);
            Assert.AreEqual(JsMath.F32(w.Z[e]), f.AimZ, 1e-9);
            SimInput.ApplyInput(w, Demo, f);
            int head = w.EventHead;
            Abilities.AbilitiesSystem(w, Dt);
            Assert.AreEqual(1, Since(w, head, SimEventType.TntThrown).Count);
            // The next frame carries no press (one press per think, rising edge).
            InputFrame g = SimInput.EmptyInput();
            BotBrain.FillBotInput(w, Demo, g);
            Assert.IsFalse(g.Terraform);
            // With TNT cooling down and an enemy within 8 on the hill, the next think lays a mine on its own cell.
            w.Actors[Demo].AiState = AiState.HoldHill;
            w.Tick += 1;
            InputFrame h = SimInput.EmptyInput();
            BotBrain.FillBotInput(w, Demo, h);
            BotsState st = BotBrain.GetBotsState(w);
            BotFsm.ThinkBot(w, st, Demo);
            BotBrain.FillBotInput(w, Demo, h);
            Assert.IsTrue(h.Ability, "mine press");
            Assert.IsTrue(Demolitions.CanPlaceMine(w, Demo, h.AimX, h.AimZ));
        }

        [Test]
        public void DG22HardLeadsAMovingTargetNormalAimsWhereItStands()
        {
            World n = Duel(BotDifficulty.Normal, 1);
            World h = Duel(BotDifficulty.Hard, 1);
            int e = Nth(n, TeamId.Crimson, 0);
            InputFrame fn = BotTick(n);
            InputFrame fh = BotTick(h);
            Assert.IsTrue(fn.Terraform);
            Assert.IsTrue(fh.Terraform);
            Assert.AreEqual(JsMath.F32(n.Z[e]), fn.AimZ, 1e-9);
            double lead = h.Config.Bots.Hard.TntLeadFraction * TntCfg.FuseTicks / h.Config.SimHz;
            Assert.AreEqual(JsMath.F32(h.Z[e] + 1 * lead), fh.AimZ, 1e-6);
        }
    }
}
