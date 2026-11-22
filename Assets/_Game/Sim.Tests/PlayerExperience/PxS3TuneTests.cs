using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.PlayerSeat;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests.PlayerExperience
{
    /// <summary>
    /// Lane S3 (player-experience wave, 2026-11-22): the Miner's tap-to-full mining (lead's call) and the same-tick trade rule.
    /// Staging is test-kit writes before the measured window; every press goes through the human seat (MatchTick.AdvanceHumans).
    /// </summary>
    public class PxS3TuneTests
    {
        static Seat AtVein(out int vein)
        {
            Seat s = PlayerSeatRig.Field(ClassId.Miner);
            World w = s.W;
            vein = -1;
            for (int i = 0; i < w.Depth.Sources.Length; i++)
            {
                SourceNode sn = w.Depth.Sources[i];
                if (sn != null && sn.Kind == BiomassType.Ore && sn.Charges >= 3) { vein = i; PlayerSeatRig.Place(w, 0, ClassId.Miner, sn.X - 1.0, sn.Z); break; }
            }
            return s;
        }

        static void Steps(Seat s, int n, double x, double z)
        {
            for (int i = 0; i < n; i++)
            {
                s.Human.AimOverrideX = x;
                s.Human.AimOverrideZ = z;
                s.Step();
            }
        }

        [Test]
        public void OneTapAtAVein_MinesUntilTheCargoIsFull()
        {
            Seat s = AtVein(out int vein);
            World w = s.W;
            Assume.That(vein >= 0, "the map has an ore vein with 3 charges");
            SourceNode sn = w.Depth.Sources[vein];
            int cap = (int)w.Config.Depth.Hut.OreCarry, unit = Blacksmith.MineTicks(w.Config);
            int mark = s.Spy.Mark;
            s.Human.AimOverrideX = sn.X;
            s.Human.AimOverrideZ = sn.Z;
            s.Human.PressDigPlace = true;
            s.Step();
            Steps(s, cap * (unit + 2) + 6, sn.X, sn.Z);
            int mined = s.Spy.Since(mark, SimEventType.OreMined, e => e.A == 0 && e.Value > 0).Count;
            int starts = s.Spy.Since(mark, SimEventType.WorldAction, e => e.A == 0 && e.B == PlayerReasons.WorldMineOre && e.Value <= 0).Count;
            Assert.AreEqual(cap, Blacksmith.OreOf(w.Actors[0]), "one tap fills the cargo");
            Assert.AreEqual(cap, mined, "one OreMined per unit");
            Assert.AreEqual(cap, starts, "each unit's channel start is announced (WorldAction Value 0)");
        }

        [Test]
        public void TheTapChannelStops_WhenTheMinerMoves()
        {
            Seat s = AtVein(out int vein);
            World w = s.W;
            Assume.That(vein >= 0, "the map has an ore vein with 3 charges");
            SourceNode sn = w.Depth.Sources[vein];
            int unit = Blacksmith.MineTicks(w.Config);
            s.Human.AimOverrideX = sn.X;
            s.Human.AimOverrideZ = sn.Z;
            s.Human.PressDigPlace = true;
            s.Step();
            Steps(s, unit + 3, sn.X, sn.Z);
            Assert.AreEqual(1, Blacksmith.OreOf(w.Actors[0]), "the first unit");
            // a deliberate step sideways (stays within reach for a tick or two): the channel ends, no second unit
            s.Human.MoveOverrideX = 0;
            s.Human.MoveOverrideZ = 1;
            s.Human.AimOverrideX = sn.X;
            s.Human.AimOverrideZ = sn.Z;
            s.Step();
            Steps(s, 2 * unit + 4, sn.X, sn.Z);
            Assert.AreEqual(1, Blacksmith.OreOf(w.Actors[0]), "moving stopped the tapped channel");
        }

        [Test]
        public void TheTapRuleOff_MinesOneUnitPerTap()
        {
            Seat s = AtVein(out int vein);
            World w = s.W;
            Assume.That(vein >= 0, "the map has an ore vein with 3 charges");
            w.Config.Depth.Seat.MinerTapMinesToFull = false;
            SourceNode sn = w.Depth.Sources[vein];
            int unit = Blacksmith.MineTicks(w.Config);
            s.Human.AimOverrideX = sn.X;
            s.Human.AimOverrideZ = sn.Z;
            s.Human.PressDigPlace = true;
            s.Step();
            Steps(s, 3 * unit + 6, sn.X, sn.Z);
            Assert.AreEqual(1, Blacksmith.OreOf(w.Actors[0]), "S2's one unit per tap");
        }
    }
}
