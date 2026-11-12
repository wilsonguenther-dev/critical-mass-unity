using System.Linq;
using CriticalMass.Sim.Config;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>Port of src/config/game.config.test.ts (game.config invariants) plus the U-004 default and a full value check.</summary>
    [TestFixture]
    public sealed class GameConfigTests
    {
        [Test]
        public void HatchesStrictlyInsideTheMatchDefault400Of800()
        {
            var c = GameConfig.CreateBrowser5v5();
            Assert.AreEqual(480d, c.MatchSeconds);
            Assert.AreEqual(240d, c.HatchSeconds);
            Assert.Greater(c.HatchSeconds, 0d);
            Assert.Less(c.HatchSeconds, c.MatchSeconds);
        }

        [Test]
        public void DefaultsTo5v5AndAllows16v16()
        {
            var c = GameConfig.CreateBrowser5v5();
            Assert.AreEqual(5, c.TeamSize);
            CollectionAssert.Contains(c.TeamSizeOptions, 16);
            Assert.GreaterOrEqual(c.TeamSizeMax, c.TeamSizeOptions.Max());
        }

        [Test]
        public void CorruptionGivesLessAllegianceThanBiomass()
        {
            var f = GameConfig.CreateBrowser5v5().KaijuFeed;
            Assert.Less(f.CorruptionAllegiancePerUnit, f.BiomassAllegiancePerUnit);
            Assert.Greater(f.CorruptionInstabilityPerUnit, 0d);
        }

        [Test]
        public void HasExactlyFiveClassesWithAFarmerThatGathersFasterAndCarriesMore()
        {
            var c = GameConfig.CreateBrowser5v5();
            Assert.AreEqual(5, c.Classes.Length);
            CollectionAssert.AreEqual(
                new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder },
                ClassIds.All);
            Assert.Less(c.Class(ClassId.Farmer).GatherSeconds, c.Class(ClassId.Warrior).GatherSeconds);
            Assert.Greater(c.Class(ClassId.Farmer).CarryCapacity, c.Class(ClassId.Miner).CarryCapacity);
            Assert.Greater(c.Class(ClassId.Warrior).MaxHp, c.Class(ClassId.Farmer).MaxHp);
        }

        [Test]
        public void BotClassMixSumsTo1()
        {
            var c = GameConfig.CreateBrowser5v5();
            double sum = 0;
            foreach (var id in ClassIds.All) sum += c.ClassMix(id);
            Assert.AreEqual(1d, sum, 0.5e-6);
        }

        [Test]
        public void CM56CLASSIDSListsAllFiveClassesInCanonicalOrderOneConfigRowEach()
        {
            var c = GameConfig.CreateBrowser5v5();
            CollectionAssert.AreEqual(
                new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Miner, ClassId.Demolitions, ClassId.Builder },
                ClassIds.All);
            Assert.AreEqual(ClassIds.All.Count, c.Classes.Length);
            Assert.AreEqual(ClassIds.All.Count, c.Bots.ClassMix.Length);
            foreach (var id in ClassIds.All) Assert.IsNotNull(c.Class(id));
        }

        [Test]
        public void CM56ThePlaceholderClassesNeverFillABotSeatClassMix0AndCopyTheMinerStatRow()
        {
            var c = GameConfig.CreateBrowser5v5();
            Assert.AreEqual(0d, c.ClassMix(ClassId.Demolitions));
            Assert.AreEqual(0d, c.ClassMix(ClassId.Builder));
            Assert.AreEqual(1d, c.ClassMix(ClassId.Warrior) + c.ClassMix(ClassId.Farmer) + c.ClassMix(ClassId.Miner), 0.5e-6);
            Assert.IsTrue(c.Class(ClassId.Demolitions).SameStats(c.Class(ClassId.Miner)));
            Assert.IsTrue(c.Class(ClassId.Builder).SameStats(c.Class(ClassId.Miner)));
        }

        [Test]
        public void KaijuKOLasts60S()
        {
            Assert.AreEqual(60d, GameConfig.CreateBrowser5v5().Kaiju.KoSeconds);
        }

        [Test]
        public void PaletteMatchesTheAssetBible()
        {
            Assert.AreEqual("#18233B", Palette.VoidNavy);
            Assert.AreEqual("#16C7D9", Palette.Azure);
            Assert.AreEqual("#FF5A5F", Palette.Crimson);
            Assert.AreEqual("#F7F0E5", Palette.Cream);
            Assert.AreEqual("#B7F238", Palette.Biomass);
            Assert.AreEqual("#8E3FD1", Palette.Corruption);
            Assert.AreEqual("#6F7282", Palette.Stone);
            Assert.AreEqual("#C79245", Palette.Brass);
            Assert.AreEqual("#6FCBEA", Palette.SkyTop);
            Assert.AreEqual("#CDEFF8", Palette.SkyHorizon);
            Assert.AreEqual("#F06C47", Palette.Meat);
        }

        [Test]
        public void DefaultIs16v16At2xPace()
        {
            var c = GameConfig.CreateDefault();
            Assert.AreEqual(16, c.TeamSize);
            Assert.AreEqual(2d, c.DefaultSpeed);
        }

        [Test]
        public void EveryTunableMatchesTheBrowserValue()
        {
            var c = GameConfig.CreateBrowser5v5();

            Assert.AreEqual(1337, c.Seed);
            Assert.AreEqual(30d, c.SimHz);
            Assert.AreEqual(5, c.TeamSize);
            CollectionAssert.AreEqual(new[] { 5, 16 }, c.TeamSizeOptions);
            Assert.AreEqual(16, c.TeamSizeMax);
            Assert.AreEqual(1d, c.DefaultSpeed);
            Assert.AreEqual(480d, c.MatchSeconds);
            Assert.AreEqual(240d, c.HatchSeconds);
            Assert.AreEqual(0.1, c.DrawEpsilonSeconds);

            Assert.AreEqual(6d, c.Hill.Radius);

            Assert.AreEqual(0.12, c.Movement.AccelSeconds);
            Assert.AreEqual(0.08, c.Movement.BrakeSeconds);

            Assert.AreEqual(18d, c.Jump.AirTicks);
            Assert.AreEqual(0.8, c.Jump.ApexUnits);
            Assert.AreEqual(6d, c.Jump.MinAirTicks);

            Assert.AreEqual(5d, c.Respawn.DelaySeconds);
            Assert.AreEqual(3d, c.Respawn.ProtectionSeconds);
            Assert.AreEqual(0.8, c.Respawn.StandUpSeconds);
            Assert.AreEqual(150d, c.Respawn.ClassSwitchCooldownTicks);

            Assert.AreEqual(1d, c.Heal.HpPerTick);
            Assert.AreEqual(3.75, c.Heal.ZoneRadius);
            Assert.AreEqual(0.3, c.Heal.SeekHpShare);

            Assert.AreEqual(10d, c.KaijuFeed.BiomassAllegiancePerUnit);
            Assert.AreEqual(6d, c.KaijuFeed.MeatAllegiancePerUnit);
            Assert.AreEqual(4d, c.KaijuFeed.CorruptionAllegiancePerUnit);
            Assert.AreEqual(9d, c.KaijuFeed.CorruptionInstabilityPerUnit);
            Assert.AreEqual(0.125, c.KaijuFeed.BiomassInstabilityPerUnit);
            Assert.AreEqual(100d, c.KaijuFeed.BerserkThreshold);
            Assert.AreEqual(3d, c.KaijuFeed.ShrineInteractRange);

            Assert.AreEqual(1.8, c.Resources.NodeInteractRange);
            Assert.AreEqual(6d, c.Resources.BiomassNodeCharges);
            Assert.AreEqual(20d, c.Resources.BiomassRegrowSeconds);
            Assert.AreEqual(3d, c.Resources.CorruptionNodeCharges);
            Assert.AreEqual(30d, c.Resources.CorruptionRegrowSeconds);
            Assert.AreEqual(2d, c.Resources.MeatUnitsPerKill);
            Assert.AreEqual(600d, c.Resources.MeatOrbLifetimeTicks);
            Assert.AreEqual(1.2, c.Resources.MeatPickupRange);
            Assert.AreEqual(2d, c.Resources.MeatCarryCapacity);

            Assert.AreEqual(400d, c.Kaiju.HpPerTeamMember);
            Assert.AreEqual(2.6, c.Kaiju.MoveSpeed);
            Assert.AreEqual(1.64, c.Kaiju.Radius);
            Assert.AreEqual(0.7, c.Kaiju.GridRadius);
            Assert.AreEqual(30d, c.Kaiju.MeleeDamage);
            Assert.AreEqual(2.6, c.Kaiju.MeleeRange);
            Assert.AreEqual(1.6, c.Kaiju.MeleeCooldown);
            Assert.AreEqual(11d, c.Kaiju.MeleeWindupTicks);
            Assert.AreEqual(45d, c.Kaiju.SlamDamage);
            Assert.AreEqual(5d, c.Kaiju.SlamRadius);
            Assert.AreEqual(1.2, c.Kaiju.SlamTelegraphSeconds);
            Assert.AreEqual(8d, c.Kaiju.SlamCooldown);
            Assert.AreEqual(8d, c.Kaiju.NeutralThreatMemorySeconds);
            Assert.AreEqual(60d, c.Kaiju.KoSeconds);

            AssertClassRow(c.Class(ClassId.Warrior), 160, 3.8, 0.45, 22, 1.7, 120, 0.7, 10, 2, 1);
            AssertClassRow(c.Class(ClassId.Farmer), 100, 4.2, 0.4, 12, 1.4, 90, 0.6, 9, 1, 3);
            AssertClassRow(c.Class(ClassId.Miner), 120, 4.0, 0.45, 16, 1.5, 90, 0.65, 12, 2, 1);
            AssertClassRow(c.Class(ClassId.Demolitions), 120, 4.0, 0.45, 16, 1.5, 90, 0.65, 12, 2, 1);
            AssertClassRow(c.Class(ClassId.Builder), 120, 4.0, 0.45, 16, 1.5, 90, 0.65, 12, 2, 1);

            var a = c.Abilities;
            Assert.AreEqual(2.5, a.WarriorShield.DurationSeconds);
            Assert.AreEqual(0.6, a.WarriorShield.DamageReduction);
            Assert.AreEqual(0.7, a.WarriorShield.MoveSpeedMultiplier);

            Assert.AreEqual(6d, a.FarmerRoots.CastRange);
            Assert.AreEqual(2.5, a.FarmerRoots.Radius);
            Assert.AreEqual(0.5, a.FarmerRoots.SlowMultiplier);
            Assert.AreEqual(3d, a.FarmerRoots.SlowSeconds);

            Assert.AreEqual(1d, a.MinerWallBreak.HitsPerState);
            Assert.AreEqual(1.6, a.MinerWallBreak.Reach);

            Assert.AreEqual(2d, a.MinerBarricade.MaxActivePerMiner);
            Assert.AreEqual(20d, a.MinerBarricade.LifetimeSeconds);
            Assert.AreEqual(120d, a.MinerBarricade.MaxHp);
            Assert.AreEqual(0.9, a.MinerBarricade.MinDistanceFromActor);
            Assert.AreEqual(2d, a.MinerBarricade.PlaceRange);

            Assert.AreEqual(1.6, a.MinerTerraform.Reach);
            Assert.AreEqual(30d, a.MinerTerraform.TeamCooldownTicks);
            Assert.AreEqual(15d, a.MinerTerraform.MinGapTicks);
            Assert.AreEqual(0.05, a.MinerTerraform.ActorMargin);

            Assert.AreEqual(3d, a.Topple.HitsToTopple);
            Assert.AreEqual(4d, a.Topple.LengthCells);
            Assert.AreEqual(3d, a.Topple.WidthCells);
            Assert.AreEqual(18d, a.Topple.FallTicks);
            Assert.AreEqual(0.5, a.Topple.Reach);

            Assert.AreEqual(1.6, a.DemolitionsMine.PlaceRange);
            Assert.AreEqual(2d, a.DemolitionsMine.MaxActivePerOwner);
            Assert.AreEqual(15d, a.DemolitionsMine.ArmTicks);
            Assert.AreEqual(70d, a.DemolitionsMine.Damage);
            Assert.AreEqual(1.2, a.DemolitionsMine.BlastRadius);
            Assert.AreEqual(1800d, a.DemolitionsMine.LifetimeTicks);

            Assert.AreEqual(5d, a.DemolitionsTnt.ThrowRange);
            Assert.AreEqual(45d, a.DemolitionsTnt.FuseTicks);
            Assert.AreEqual(2d, a.DemolitionsTnt.BlastRadius);
            Assert.AreEqual(55d, a.DemolitionsTnt.Damage);
            Assert.AreEqual(1d, a.DemolitionsTnt.CarveRadius);
            Assert.AreEqual(150d, a.DemolitionsTnt.CooldownTicks);

            Assert.AreEqual(4d, c.Bots.ThinkHz);
            Assert.AreEqual(2d, c.Bots.DirectorPeriodSeconds);
            Assert.AreEqual(0.25, c.Bots.InertiaBonus);
            Assert.AreEqual(2d, c.Bots.MinStateSeconds);
            Assert.AreEqual(0.3, c.Bots.HillMinFraction);
            Assert.AreEqual(0.25, c.Bots.KaijuGroupFraction);
            Assert.AreEqual(10d, c.Bots.CorruptWhenBehindBy);
            CollectionAssert.AreEqual(new[] { 0.4, 0.3, 0.3, 0d, 0d }, c.Bots.ClassMix);
            Assert.AreEqual(1.5, c.Bots.StuckSeconds);
            Assert.AreEqual(300d, c.Bots.TurnRateDegPerSec);

            Assert.AreEqual(38d, c.Render.CameraPitchDeg);
            Assert.AreEqual(45d, c.Render.CameraYawDeg);
            Assert.AreEqual(60d, c.Render.CameraDistance);
            Assert.AreEqual(20d, c.Render.ViewHeightUnits);
            Assert.AreEqual(0.8, c.Render.WheelFactorMin);
            Assert.AreEqual(1.25, c.Render.WheelFactorMax);
            Assert.AreEqual(0.0015, c.Render.WheelSensitivity);
            Assert.AreEqual(6d, c.Render.CameraBoundsInset);
            Assert.AreEqual(2.0, c.Render.CharacterHeight);
            Assert.AreEqual(5.0, c.Render.KaijuHeight);
            Assert.AreEqual(8d, c.Render.CameraFollowSharpness);
            Assert.AreEqual(1d, c.Render.DprMin);
            Assert.AreEqual(2d, c.Render.DprMax);
            Assert.AreEqual(2048d, c.Render.ShadowMapSize);
            Assert.AreEqual(50d, c.Render.AutoDropFps);
            Assert.AreEqual(1.45, c.Render.StandUpClipStart);

            CollectionAssert.AreEqual(new[] { 1d, 2d, 4d, 8d }, c.Debug.ClockSpeedOptions);
        }

        private static void AssertClassRow(
            ClassConfig row,
            double maxHp,
            double moveSpeed,
            double radius,
            double attackDamage,
            double attackRange,
            double attackArcDeg,
            double attackCooldown,
            double abilityCooldown,
            double gatherSeconds,
            double carryCapacity)
        {
            Assert.AreEqual(maxHp, row.MaxHp);
            Assert.AreEqual(moveSpeed, row.MoveSpeed);
            Assert.AreEqual(radius, row.Radius);
            Assert.AreEqual(attackDamage, row.AttackDamage);
            Assert.AreEqual(attackRange, row.AttackRange);
            Assert.AreEqual(attackArcDeg, row.AttackArcDeg);
            Assert.AreEqual(attackCooldown, row.AttackCooldown);
            Assert.AreEqual(abilityCooldown, row.AbilityCooldown);
            Assert.AreEqual(gatherSeconds, row.GatherSeconds);
            Assert.AreEqual(carryCapacity, row.CarryCapacity);
        }
    }
}
