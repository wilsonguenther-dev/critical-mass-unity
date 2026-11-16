using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b12 r2 integrator diagnostic for the b12-jz TNT-on-attack RULE: with the rule, MovementShiverTests' seed-1337 Kaiju never
    /// reached the hill disc (on 0.0 s, near share 0.54 against 0.62 without it). Same seeds, 16v16 default (Full) rules,
    /// rule on against the control (DemolitionsTnt.ThrowOnAttack off): per match the owned Kaiju's on-hill and near-hill
    /// seconds and shares (MovementShiverTests' measure), TNT thrown, cells carved, hill cells that are Void at the end, and
    /// the winner. Print-only: the decision is read from the lines.
    /// </summary>
    [TestFixture]
    public sealed class TntOnAttackKaijuHillTests
    {
        [Test, Category("Slow")]
        public void KaijuHillTimeWithAndWithoutTntOnAttack()
        {
            var sum = new double[2, 2];
            int[] seeds = { 1337, 4242, 1, 2, 3, 101 };
            foreach (int seed in seeds)
                for (int arm = 0; arm < 2; arm++)
                {
                    GameConfig cfg = GameConfig.CreateDefault();
                    cfg.Abilities.DemolitionsTnt.ThrowOnAttack = arm == 0;
                    World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                    InputFrame scratch = SimInput.EmptyInput();
                    HillDef hill = w.Map.Hill;
                    double dt = 1.0 / cfg.SimHz, nearR = cfg.Physics.KaijuHillFightRadius, owned = 0, on = 0, near = 0;
                    int max = (int)(cfg.SimHz * 60 * 30);
                    for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
                    {
                        MatchTick.Advance(w, null, scratch);
                        Kaiju k = w.Kaiju;
                        if (!w.Match.Hatched || k.State == KaijuState.Egg) continue;
                        if (!((k.State == KaijuState.Azure || k.State == KaijuState.Crimson) && k.KoIn <= 0)) continue;
                        double hx = k.X - hill.X, hz = k.Z - hill.Z, d2 = hx * hx + hz * hz;
                        owned += dt;
                        if (d2 <= hill.R * hill.R) on += dt;
                        if (d2 <= nearR * nearR) near += dt;
                    }
                    int voidCells = 0;
                    for (int cz = (int)Math.Floor(hill.Z - hill.R); cz <= (int)Math.Ceiling(hill.Z + hill.R); cz++)
                        for (int cx = (int)Math.Floor(hill.X - hill.R); cx <= (int)Math.Ceiling(hill.X + hill.R); cx++)
                        {
                            double ox = cx + 0.5 - hill.X, oz = cz + 0.5 - hill.Z;
                            if (ox * ox + oz * oz <= hill.R * hill.R && cx >= 0 && cz >= 0 && cx < w.Map.Width && cz < w.Map.Height && w.Map.Cells[cz * w.Map.Width + cx] == (byte)Cell.Void) voidCells++;
                        }
                    double onS = owned > 0 ? on / owned : 0, nearS = owned > 0 ? near / owned : 0;
                    sum[arm, 0] += onS; sum[arm, 1] += nearS;
                    var s = w.Demolitions.Stats;
                    Console.WriteLine("{\"item\":\"b12-jz tnt kaiju hill\",\"seed\":" + seed + ",\"rule\":\"" + (arm == 0 ? "on" : "off (control)") + "\",\"ownedS\":" + owned.ToString("F1") +
                                      ",\"onHillS\":" + on.ToString("F1") + ",\"onShare\":" + onS.ToString("F2") + ",\"nearShare\":" + nearS.ToString("F2") +
                                      ",\"tntThrown\":" + s.TntThrown + ",\"cellsCarved\":" + s.CellsCarved + ",\"hillVoidCellsAtEnd\":" + voidCells + ",\"winner\":\"" + w.Match.Winner + "\"}");
                }
            for (int arm = 0; arm < 2; arm++)
                Console.WriteLine("{\"item\":\"b12-jz tnt kaiju hill mean\",\"rule\":\"" + (arm == 0 ? "on" : "off (control)") + "\",\"seeds\":" + seeds.Length +
                                  ",\"onShare\":" + (sum[arm, 0] / seeds.Length).ToString("F3") + ",\"nearShare\":" + (sum[arm, 1] / seeds.Length).ToString("F3") + "}");
            Assert.Pass("print-only diagnostic");
        }
    }
}
