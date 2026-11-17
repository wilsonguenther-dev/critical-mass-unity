using System;
using CriticalMass.Net;
using CriticalMass.Net.Replay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>CM b13 S0: the hook scaffold is an identity (every stub and hook), Dodge travels the wire and replay bytes, and
    /// a painted Cell.LowWall blocks a walker but not a jumper.</summary>
    [TestFixture]
    public sealed class B13ScaffoldTests
    {
        static World Full16() => World.CreateWorld(new WorldOptions
        {
            Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
        });

        static World Full16AllB13Off()
        {
            GameConfig c = GameConfig.CreateDefault();
            c.Depth.Hut.On = false; c.Depth.Build.On = false; c.Depth.Dodge.On = false; c.Depth.KaijuTypes.On = false; c.Depth.Corruption.On = false;
            return World.CreateWorld(new WorldOptions { Seed = 4242, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = c });
        }

        [Test]
        public void EveryStubAndHookIsAnIdentityOnAFull16v16World()
        {
            // b13 integrator: the items fill the stubs, so the identity is proved with every item's own On flag off.
            World w = Full16AllB13Off();
            for (int t = 0; t < 120; t++) SimStep.Step(w);
            Assert.IsNotNull(w.Hut); Assert.IsNotNull(w.Walls); Assert.IsNotNull(w.Dodges); Assert.IsNotNull(w.KaijuKind); Assert.IsNotNull(w.Corrupt);
            BotsState st = BotsState.CreateBotsState(w);
            int n = w.ActorCount, bad = 0;
            foreach (TeamId team in new[] { TeamId.Azure, TeamId.Crimson })
            {
                if (Blacksmith.Level(w, team) != 0 || Blacksmith.OreBanked(w, team) != 0 || Blacksmith.NextCost(w, team) != 0 || Blacksmith.StatScale(w, team) != 1.0) bad++;
                if (BuilderWalls.Tier(w, team) != 0) bad++;
            }
            if (Blacksmith.On(w) || BuilderWalls.On(w) || Dodge.On(w) || KaijuTypes.On(w) || Corruption.On(w)) bad++;
            if (KaijuTypes.Type(w) != KaijuType.Elder || KaijuTypes.PowerLevel(w) != 0) bad++;
            if (KaijuTypes.CloudActive(w, out double kx, out double kz, out double kr) || kx != 0 || kz != 0 || kr != 0) bad++;
            foreach (KaijuState s in (KaijuState[])Enum.GetValues(typeof(KaijuState))) if (Corruption.AtHatch(w, s) != s) bad++;
            for (int b = -1; b <= 1; b++) if (Corruption.WildTargetTeam(w, b) != b) bad++;
            if (Corruption.Pool(w) != 0 || Corruption.Chance(w) != 0 || Corruption.NextRollTick(w) != -1) bad++;
            var frame = SimInput.EmptyInput();
            var copy = SimInput.EmptyInput();
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                if (Blacksmith.SpeedScale(w, i) != 1.0 || Blacksmith.TakeMinerPress(w, i) || Blacksmith.TakeMinerAbility(w, i)) bad++;
                if (BuilderWalls.Piece(w, i) != BuildPiece.Wall || BuilderWalls.TakeBuilderPress(w, i) || BuilderWalls.TakeBuilderSwitch(w, i)) bad++;
                if (BuilderWalls.Ghost(w, i, out int gx, out int gz, out int dx, out int dz, out int len, out bool tall, out bool ok) || gx != 0 || gz != 0 || dx != 0 || dz != 0 || len != 0 || tall || ok) bad++;
                if (Dodge.Step(w, i, SimStep.Dt(w.Config)) || Dodge.Immune(w, i, DamageCause.Melee) || Dodge.Active(w, i) || Dodge.CooldownTicks(w, i) != 0 || Dodge.Phase(w, i, 0.5) != -1) bad++;
                if (KaijuTypes.BlindBlocks(w, i, j) || KaijuTypes.SightRadiusFor(w, i, 7.25) != 7.25 || Corruption.SpeedScale(w, i) != 1.0) bad++;
                if (B13Hooks.Immune(w, i, DamageCause.Explosion) || B13Hooks.DamageScale(w, i, j, DamageCause.Melee) != 1.0 || B13Hooks.HealScale(w, i) != 1.0) bad++;
                if (B13Hooks.SpeedScale(w, i) != 1.0 || B13Hooks.DodgeStep(w, i, SimStep.Dt(w.Config))) bad++;
                if (B13Hooks.TakeTerraformPress(w, i) || B13Hooks.TakeTerraformSwitch(w, i) || B13Hooks.TakeAbilityPress(w, i)) bad++;
                foreach (byte kind in new byte[] { 0, 1, 15, 25, 35 })
                {
                    st.TaskKind[i] = kind;
                    if (B13Hooks.BotTaskAct(w, st, i, frame)) bad++;
                }
                st.TaskKind[i] = 0;
                if (BotKaijuFeed.GatherScale(w, st, i) != 1.0) bad++;
                foreach (BiomassType k in (BiomassType[])Enum.GetValues(typeof(BiomassType))) if (BotKaijuFeed.SupplyFeedsEgg(w, st, i, k)) bad++;
                if (BotMine.DecideAbility(w, st, i, AiState.ToHill) || BotMine.Act(w, st, i, frame) || BotBuild.DecideBuilder(w, st, i, AiState.ToHill) || BotCorrupt.Act(w, st, i, frame)) bad++;
                frame.MoveX = 0.25; frame.Attack = true; frame.Jump = true;
                copy.CopyFrom(frame);
                BotReflex.Fill(w, st, i, frame);
                if (frame.MoveX != copy.MoveX || frame.Attack != copy.Attack || frame.Jump != copy.Jump || frame.Dodge != copy.Dodge) bad++;
            }
            if (B13Hooks.DamageScale(w, Damage.KaijuSource, 0, DamageCause.KaijuMelee) != 1.0 || B13Hooks.DamageScale(w, -1, -1, DamageCause.None) != 1.0 || B13Hooks.HealScale(w, -1) != 1.0) bad++;
            DepthConfig d = w.Config.Depth, c = d.Clone();
            bool deep = !ReferenceEquals(c.Hut, d.Hut) && !ReferenceEquals(c.Build, d.Build) && !ReferenceEquals(c.Dodge, d.Dodge) &&
                        !ReferenceEquals(c.KaijuTypes, d.KaijuTypes) && !ReferenceEquals(c.Corruption, d.Corruption);
            DepthConfig dd = GameConfig.CreateDefault().Depth;
            bool on = dd.Hut.On && dd.Build.On && dd.Dodge.On && dd.KaijuTypes.On && dd.Corruption.On;
            Console.WriteLine($"[b13 scaffold] 16v16 Full seed 4242: {n} actors, stub/hook identity failures {bad}, config On flags {on}, deep clone {deep} (limit: 0 failures, true, true)");
            Assert.AreEqual(0, bad);
            Assert.IsTrue(on && deep);
        }

        [Test]
        public void DodgeSurvivesTheWireCodecAndTheReplayButtons()
        {
            var f = SimInput.EmptyInput();
            f.Dodge = true;
            var b = new byte[InputCodec.Size];
            InputCodec.Encode(f, b, 0);
            var g = SimInput.EmptyInput();
            InputCodec.Decode(b, 0, g);
            var h = SimInput.EmptyInput();
            ReplayInputStream.SetButtons(h, ReplayInputStream.Buttons(g));
            Console.WriteLine($"[b13 scaffold] Dodge: wire byte6 0x{b[6]:X2} -> {g.Dodge}, replay buttons 0x{ReplayInputStream.Buttons(g):X2} -> {h.Dodge} (limit: true, true; Size {InputCodec.Size} = 14)");
            Assert.IsTrue(g.Dodge && h.Dodge);
            Assert.AreEqual(14, InputCodec.Size);
        }

        [Test]
        public void APaintedLowWallBlocksAWalkerAndLetsAJumperOver()
        {
            World w = Full16();
            MapDef map = w.Map;
            int fx = -1, fz = -1;
            for (int cz = 2; cz < map.Height - 2 && fx < 0; cz++)
                for (int cx = 2; cx < map.Width - 5 && fx < 0; cx++)
                {
                    bool clear = true;
                    for (int dz = -1; dz <= 1 && clear; dz++)
                        for (int dx = 0; dx <= 3 && clear; dx++)
                            clear = map.Cells[(cz + dz) * map.Width + cx + dx] == Cell.Floor;
                    if (clear) { fx = cx; fz = cz; }
                }
            Assert.GreaterOrEqual(fx, 0, "a 4 x 3 run of floor exists");
            MapGrid.SetCell(w, fx + 1, fz, Cell.LowWall);
            double startX = fx + 0.5, startZ = fz + 0.5;
            double[] endX = new double[2];
            for (int k = 0; k < 2; k++)
            {
                bool air = k == 1;
                w.X[0] = startX;
                w.Z[0] = startZ;
                for (int s = 0; s < 16; s++) Collide.MoveActor(w, 0, 0.1, 0, air);
                endX[k] = w.X[0];
            }
            double r = Collide.ActorRadius(w, 0);
            Console.WriteLine($"[b13 scaffold] LowWall at ({fx + 1},{fz}): walker from x {startX:F2} ends at {endX[0]:F3} (limit: <= {fx + 1 - r + 1e-6:F3}), jumper ends at {endX[1]:F3} (limit: >= {fx + 2:F3})");
            Assert.LessOrEqual(endX[0], fx + 1 - r + 1e-6, "walking stops at the short wall");
            Assert.GreaterOrEqual(endX[1], fx + 2.0, "an airborne actor passes over it");
        }
    }
}
