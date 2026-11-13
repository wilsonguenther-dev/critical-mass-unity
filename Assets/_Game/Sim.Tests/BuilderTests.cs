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
    /// DG-24 Builder ladders and rope bridges (U-060; the oracle has none). A hand-painted strip: the pad island, a 4-cell
    /// void gap, a middle island, a 1-cell wall column, the hill island, and a long way round along the south so the pads
    /// always reach the hill without any piece. All time is sim ticks.
    /// </summary>
    [TestFixture]
    public sealed class BuilderTests
    {
        private const int Bld = 0;
        private static GameConfig Cfg => GameConfig.CreateBrowser5v5();
        private static BuilderPiecesConfig B => Cfg.Abilities.BuilderPieces;

        private static void Paint(World w, int x0, int z0, int x1, int z1, byte t)
        {
            for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) w.Map.Cells[z * w.Map.Width + x] = t;
        }

        private static byte CellAt(World w, int cx, int cz) => w.Map.Cells[cz * w.Map.Width + cx];

        private static List<SimEvent> Since(World w, int head, SimEventType type)
        {
            var list = new List<SimEvent>();
            for (int i = Math.Max(head, w.EventHead - World.EventCapacity); i < w.EventHead; i++)
            {
                SimEvent e = w.Events[i % World.EventCapacity];
                if (e.Type == type) list.Add(new SimEvent { Type = e.Type, A = e.A, B = e.B, X = e.X, Z = e.Z, Value = e.Value, Radius = e.Radius });
            }
            return list;
        }

        private static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true;
            a.Hp = a.MaxHp;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            w.X[j] = x;
            w.Z[j] = z;
        }

        private static World Strip()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 24, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = Cfg });
            MapDef map = w.Map;
            Array.Fill(map.Cells, Cell.Void);
            Array.Clear(map.WallHp, 0, map.WallHp.Length);
            Paint(w, 2, 10, 8, 16, Cell.Floor);   // pad island
            Paint(w, 13, 8, 20, 18, Cell.Floor);  // middle island (x 9..12 is the void gap)
            Paint(w, 21, 8, 21, 18, Cell.Wall);   // the cliff
            Paint(w, 22, 8, 34, 18, Cell.Floor);  // hill island
            Paint(w, 2, 20, 34, 22, Cell.Floor);  // the long way round
            Paint(w, 2, 17, 4, 19, Cell.Floor);
            Paint(w, 13, 19, 15, 19, Cell.Floor);
            Paint(w, 30, 19, 32, 19, Cell.Floor);
            for (int t = 0; t < 2; t++)
            {
                map.Pads[t].X = 5.5;
                map.Pads[t].Z = 13.5;
            }
            map.Hill.X = 28.5;
            map.Hill.Z = 13.5;
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
            Put(w, Bld, 7.5, 13.5);
            w.Actors[Bld].Facing = 0;
            w.Actors[Bld].AbilityCd = 0;
            w.HasAim[Bld] = 1;
            w.Inputs[Bld].AimX = 20;
            w.Inputs[Bld].AimZ = 13.5;
            w.Tick = 100;
            Assert.IsTrue(Terraform.PadsReachHill(map), "the strip's long way round");
            return w;
        }

        private static int Ally(World w, int k = 0)
        {
            int seen = 0;
            for (int j = 1; j < w.ActorCount; j++)
            {
                if (w.Actors[j].Team != TeamId.Azure) continue;
                if (seen++ == k) return j;
            }
            throw new InvalidOperationException();
        }

        private static int Enemy(World w)
        {
            for (int j = 1; j < w.ActorCount; j++) if (w.Actors[j].Team == TeamId.Crimson) return j;
            throw new InvalidOperationException();
        }

        private static void Run(World w, int toTick)
        {
            while (w.Tick < toTick)
            {
                w.Tick++;
                BuiltPieces.PiecesSystem(w);
            }
        }

        [Test]
        public void DG24SpaceOverAVoidGapBuildsAThreeWideRopeBridgeToTheLandingPaysTwoBiomassAndSpendsTheCooldown()
        {
            World w = Strip();
            w.Inputs[Bld].Ability = true;
            int head = w.EventHead;
            int gv = w.GridVersion, wv = w.WallVersion;
            Abilities.AbilitiesSystem(w, SimStep.Dt(w.Config));
            for (int x = 9; x <= 12; x++) for (int z = 12; z <= 14; z++) Assert.AreEqual(Cell.Bridge, CellAt(w, x, z), $"({x},{z})");
            Assert.AreEqual(Cell.Void, CellAt(w, 9, 11));
            Assert.AreEqual(Cell.Void, CellAt(w, 9, 15));
            Assert.AreEqual(B.StartBiomass - B.BridgeCost, BuiltPieces.Bank(w, TeamId.Azure), 1e-9);
            Assert.AreEqual(w.Config.Class(ClassId.Builder).AbilityCooldown, w.Actors[Bld].AbilityCd);
            Assert.Greater(w.GridVersion, gv);
            Assert.Greater(w.WallVersion, wv, "bot fields rebuild");
            List<SimEvent> placed = Since(w, head, SimEventType.PiecePlaced);
            Assert.AreEqual(1, placed.Count);
            Assert.AreEqual(Bld, placed[0].A);
            Assert.AreEqual(11.0, placed[0].X, 1e-9, "centre of cells 9..12");
            Assert.AreEqual(13.5, placed[0].Z, 1e-9);
            Assert.AreEqual(2.0, placed[0].Radius, 1e-9, "half of 4 cells");
            Assert.AreEqual(100 + (int)B.LifetimeTicks, (int)placed[0].Value);
            BuiltPiece p = w.Builder.Pieces[placed[0].B];
            Assert.AreEqual(BuiltPieces.Bridge, p.Kind);
            Assert.AreEqual(4, p.Length);
            Assert.AreEqual(12, p.Count);
            Assert.AreEqual(1, p.Dx);
            Assert.AreEqual(BuiltPieces.BuiltBridgeCode, (int)Since(w, head, SimEventType.Ability).Single().Value);
        }

        [Test]
        public void DG24SpaceAtAOneCellCliffBuildsALadderForOneBiomassAndTheWallBecomesWalkable()
        {
            World w = Strip();
            Put(w, Bld, 19.5, 13.5);
            w.Inputs[Bld].AimX = 30;
            int head = w.EventHead;
            Assert.AreEqual(BuiltPieces.BuiltLadderCode, BuiltPieces.Build(w, Bld, 30, 13.5));
            for (int z = 12; z <= 14; z++) Assert.AreEqual(Cell.Bridge, CellAt(w, 21, z));
            Assert.AreEqual(Cell.Wall, CellAt(w, 21, 11));
            Assert.AreEqual(B.StartBiomass - B.LadderCost, BuiltPieces.Bank(w, TeamId.Azure), 1e-9);
            SimEvent e = Since(w, head, SimEventType.PiecePlaced).Single();
            Assert.AreEqual(21.5, e.X, 1e-9);
            Assert.AreEqual(BuiltPieces.Ladder, w.Builder.Pieces[e.B].Kind);
            Assert.IsTrue(Terraform.PadsReachHill(w.Map));
        }

        [Test]
        public void DG24RefusalsLeaveTheGridAndBankAloneLockSpaceBrieflyAndSayWhy()
        {
            World w = Strip();
            byte[] before = (byte[])w.Map.Cells.Clone();
            // Facing open ground: nothing to span.
            Assert.AreEqual(BuiltPieces.RefusedTarget, BuiltPieces.Build(w, Bld, 7.5, 0));
            Assert.AreEqual(B.RefuseLockSeconds, w.Actors[Bld].AbilityCd, 1e-9);
            w.Actors[Bld].AbilityCd = 0;
            // A 7-cell gap is longer than BridgeMaxCells (6).
            Paint(w, 13, 8, 15, 18, Cell.Void);
            before = (byte[])w.Map.Cells.Clone();
            Assert.AreEqual(BuiltPieces.RefusedLength, BuiltPieces.Build(w, Bld, 20, 13.5));
            Paint(w, 13, 8, 15, 18, Cell.Floor);
            before = (byte[])w.Map.Cells.Clone();
            // An empty bank.
            w.Builder.Spent[0] = B.StartBiomass;
            int head = w.EventHead;
            Assert.AreEqual(BuiltPieces.RefusedBiomass, BuiltPieces.Build(w, Bld, 20, 13.5));
            Assert.AreEqual(BuiltPieces.RefusedBiomass, (int)Since(w, head, SimEventType.Ability).Single().Value);
            w.Builder.Spent[0] = 0;
            // The CM-30 world gap.
            w.WallChangeTick = w.Tick;
            Assert.AreEqual(BuiltPieces.RefusedRate, BuiltPieces.Build(w, Bld, 20, 13.5));
            CollectionAssert.AreEqual(before, w.Map.Cells);
            Assert.AreEqual(0, w.Builder.Spent[0]);
            Assert.IsEmpty(Since(w, head, SimEventType.PiecePlaced));
            // Not a Builder.
            w.WallChangeTick = World.NeverTick;
            w.Actors[Bld].Cls = ClassId.Warrior;
            Assert.AreEqual(BuiltPieces.RefusedClass, BuiltPieces.Build(w, Bld, 20, 13.5));
            w.Actors[Bld].Cls = ClassId.Builder;
            // The per-Builder limit (2).
            w.Match.Delivered[0].Biomass = 10;
            Assert.AreEqual(BuiltPieces.BuiltBridgeCode, BuiltPieces.Build(w, Bld, 20, 13.5));
            Put(w, Bld, 19.5, 13.5);
            w.Tick += 20;
            Assert.AreEqual(BuiltPieces.BuiltLadderCode, BuiltPieces.Build(w, Bld, 30, 13.5));
            Put(w, Bld, 19.5, 10.5);
            w.Tick += 20;
            Assert.AreEqual(BuiltPieces.RefusedLimit, BuiltPieces.Build(w, Bld, 30, 10.5));
        }

        [Test]
        public void DG24APieceWarnsThenCrumblesAtItsLifetimeAndTheCellsGoBackToVoid()
        {
            World w = Strip();
            BuiltPieces.Build(w, Bld, 20, 13.5);
            int expire = 100 + (int)B.LifetimeTicks;
            int head = w.EventHead;
            Run(w, expire - (int)B.WarnTicks - 1);
            Assert.IsEmpty(Since(w, head, SimEventType.PieceCrumbling));
            Run(w, expire - (int)B.WarnTicks);
            SimEvent warn = Since(w, head, SimEventType.PieceCrumbling).Single();
            Assert.AreEqual(B.WarnTicks, warn.Value, 1e-9);
            Assert.AreEqual(11.0, warn.X, 1e-9);
            Run(w, expire - 1);
            Assert.AreEqual(Cell.Bridge, CellAt(w, 10, 13));
            Run(w, expire);
            for (int x = 9; x <= 12; x++) for (int z = 12; z <= 14; z++) Assert.AreEqual(Cell.Void, CellAt(w, x, z));
            SimEvent end = Since(w, head, SimEventType.PieceExpired).Single();
            Assert.AreEqual(BuiltPieces.EndExpired, (int)end.Value);
            Assert.AreEqual(Bld, end.A);
            Assert.IsFalse(w.Builder.Pieces[end.B].Active);
            Assert.AreEqual(0, BuiltPieces.OwnedCount(w, Bld));
        }

        [Test]
        public void DG24ACrumbleWaitsWhileAnyoneStandsOnThePieceSoNobodyIsLeftOverTheVoid()
        {
            World w = Strip();
            BuiltPieces.Build(w, Bld, 20, 13.5);
            int a = Ally(w);
            Put(w, a, 10.5, 13.5);
            int expire = 100 + (int)B.LifetimeTicks;
            Run(w, expire + 30);
            Assert.AreEqual(Cell.Bridge, CellAt(w, 10, 13), "still there under the ally");
            Assert.Greater(w.Builder.Stats.Deferred, 0);
            Put(w, a, 5.5, 15.5);
            int head = w.EventHead;
            Run(w, expire + 31);
            Assert.AreEqual(Cell.Void, CellAt(w, 10, 13));
            Assert.AreEqual(1, Since(w, head, SimEventType.PieceExpired).Count);
        }

        [Test]
        public void DG24ACrumbleThatWouldCutThePadFromTheHillWaitsTheRouteGuardHolds()
        {
            World w = Strip();
            BuiltPieces.Build(w, Bld, 20, 13.5);
            Paint(w, 8, 20, 10, 22, Cell.Void); // cut the long way west of the middle island: the bridge is now the only route
            Assert.IsTrue(Terraform.PadsReachHill(w.Map));
            int expire = 100 + (int)B.LifetimeTicks;
            Run(w, expire + 60);
            Assert.AreEqual(Cell.Bridge, CellAt(w, 10, 13));
            Assert.IsTrue(Terraform.PadsReachHill(w.Map), "a piece never seals a base from the hill");
            Assert.Greater(w.Builder.Stats.RouteDeferred, 0);
            Paint(w, 8, 20, 10, 22, Cell.Floor); // the other way opens again: now it may go
            Run(w, expire + 61);
            Assert.AreEqual(Cell.Void, CellAt(w, 10, 13));
            Assert.IsTrue(Terraform.PadsReachHill(w.Map));
        }

        [Test]
        public void DG24AMinerDiggingOneCellBreaksThePieceTheRestIsTakenAwayAfterTheWorldGap()
        {
            World w = Strip();
            BuiltPieces.Build(w, Bld, 20, 13.5);
            int m = Ally(w);
            w.Actors[m].Cls = ClassId.Miner;
            Put(w, m, 8.5, 15.6);
            w.Tick = 200;
            Assert.AreEqual(Terraform.Dug, Terraform.Dig(w, m, 9.5, 14.5));
            int head = w.EventHead;
            Run(w, 201);
            SimEvent br = Since(w, head, SimEventType.PieceDestroyed).Single();
            Assert.AreEqual(9.5, br.X, 1e-9);
            Assert.AreEqual(14.5, br.Z, 1e-9);
            Assert.AreEqual(Cell.Bridge, CellAt(w, 10, 13), "the world gap first");
            Run(w, 200 + (int)w.Config.Abilities.MinerTerraform.MinGapTicks);
            Assert.AreEqual(Cell.Void, CellAt(w, 10, 13));
            Assert.AreEqual(BuiltPieces.EndBroken, (int)Since(w, head, SimEventType.PieceExpired).Single().Value);
            Assert.AreEqual(1, w.Builder.Stats.Destroyed);
        }

        [Test]
        public void DG24AnyClassOfTheTeamLiftsAPieceWithInteractCarriesItAndPutsItDownAcrossAnotherSpot()
        {
            World w = Strip();
            BuiltPieces.Build(w, Bld, 20, 13.5);
            int e = Enemy(w);
            Put(w, e, 8.5, 11.5);
            w.Inputs[e].Interact = true;
            BuiltPieces.CarryInputSystem(w);
            Assert.AreEqual(-1, w.Builder.CarryPiece[e], "an enemy cannot take it");
            w.Actors[e].Alive = false;
            int a = Ally(w);
            Assert.AreNotEqual(ClassId.Builder, w.Actors[a].Cls, "a non-Builder carries it");
            Put(w, a, 8.5, 11.5);
            w.Tick = 150;
            w.Inputs[a].Interact = true;
            int head = w.EventHead;
            BuiltPieces.CarryInputSystem(w);
            SimEvent up = Since(w, head, SimEventType.PiecePickedUp).Single();
            Assert.AreEqual(a, up.A);
            Assert.AreEqual(11.0, up.X, 1e-9);
            Assert.AreEqual(Cell.Void, CellAt(w, 10, 13), "lifted: the gap is open again");
            Assert.AreEqual(up.B, w.Builder.CarryPiece[a]);
            // Held key: no second action. Release, walk to the far side, press again facing west.
            BuiltPieces.CarryInputSystem(w);
            w.Inputs[a].Interact = false;
            BuiltPieces.CarryInputSystem(w);
            Put(w, a, 14.5, 13.5);
            w.HasAim[a] = 1;
            w.Inputs[a].AimX = 5;
            w.Inputs[a].AimZ = 13.5;
            w.Tick = 200;
            w.Inputs[a].Interact = true;
            head = w.EventHead;
            BuiltPieces.CarryInputSystem(w);
            SimEvent down = Since(w, head, SimEventType.PiecePlaced).Single();
            Assert.AreEqual(a, down.A);
            Assert.AreEqual(up.B, down.B);
            for (int x = 9; x <= 12; x++) Assert.AreEqual(Cell.Bridge, CellAt(w, x, 13));
            Assert.AreEqual(-1, w.Builder.CarryPiece[a]);
            Assert.AreEqual(100 + (int)B.LifetimeTicks, (int)down.Value, "carried time counts");
            Assert.AreEqual(BuiltPieces.Bridge, w.Builder.Pieces[down.B].Kind);
            Assert.AreEqual(-1, w.Builder.Pieces[down.B].Dx);
        }

        [Test]
        public void DG24ACarriedBridgeCannotGoOverACliffAndACarrierThatDiesDropsIt()
        {
            World w = Strip();
            BuiltPieces.Build(w, Bld, 20, 13.5);
            int a = Ally(w);
            Put(w, a, 8.5, 11.5);
            w.Tick = 150;
            w.Inputs[a].Interact = true;
            BuiltPieces.CarryInputSystem(w);
            int slot = w.Builder.CarryPiece[a];
            Assert.GreaterOrEqual(slot, 0);
            w.Inputs[a].Interact = false;
            BuiltPieces.CarryInputSystem(w);
            Put(w, a, 19.5, 13.5);
            w.HasAim[a] = 1;
            w.Inputs[a].AimX = 30;
            w.Inputs[a].AimZ = 13.5;
            w.Inputs[a].Interact = true;
            int head = w.EventHead;
            BuiltPieces.CarryInputSystem(w);
            SimEvent r = Since(w, head, SimEventType.Ability).Single();
            Assert.AreEqual(BuiltPieces.RefusedTarget, (int)r.Value);
            Assert.AreEqual(slot, r.B, "B names the carried piece");
            Assert.AreEqual(Cell.Wall, CellAt(w, 21, 13));
            w.Actors[a].Alive = false;
            head = w.EventHead;
            Run(w, w.Tick + 1);
            Assert.AreEqual(BuiltPieces.EndDropped, (int)Since(w, head, SimEventType.PieceExpired).Single().Value);
            Assert.AreEqual(-1, w.Builder.CarryPiece[a]);
            Assert.IsFalse(w.Builder.Pieces[slot].Active);
        }

        [Test]
        public void DG24ResetWorldClearsPiecesBanksAndCarries()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 3, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = Cfg });
            w.Builder.Pieces[0].Active = true;
            w.Builder.Spent[1] = 3;
            w.Builder.CarryPiece[2] = 0;
            World.ResetWorld(w, w.Opts);
            Assert.IsFalse(w.Builder.Pieces.Any(p => p.Active));
            Assert.AreEqual(0, w.Builder.Spent[1]);
            Assert.AreEqual(-1, w.Builder.CarryPiece[2]);
            Assert.AreEqual(B.StartBiomass, BuiltPieces.Bank(w, TeamId.Crimson), 1e-9);
        }

        [Test]
        public void DG24NoBuilderOnTheFieldMeansNoPieceEventAtAll()
        {
            GameConfig cfg = Cfg;
            World w = World.CreateWorld(new WorldOptions { Seed = 9, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            for (int t = 0; t < 1800; t++) MatchTick.Advance(w, null, scratch);
            Assert.IsFalse(w.Builder.Pieces.Any(p => p.Active));
            Assert.AreEqual(0, w.Builder.Stats.Ladders + w.Builder.Stats.Bridges + w.Builder.Stats.Refused);
        }
    }
}
