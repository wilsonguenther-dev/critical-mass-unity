using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// b12 r2 integrator: same-seed balance arm for the b12-jz bridge RULE (BuilderPieces.BridgeMaxCells 6 -> 12). Whole bot
    /// matches with the shipped cap (12) against a CONTROL with the old cap (6), everything else identical (the Full rules,
    /// so the Demolitionist's TNT-on-attack rule is on in both arms): 5v5 with the depth lane on, seeds 1 to 20, and 16v16
    /// (the game's default), eight seeds. Prints one line per match and arm and one summary per size and arm, and fails when
    /// either team's win share moves more than WinShareLimit (0.15) from the control's at either size.
    /// b13 r3 integrator: 16v16 runs 24 seeds (was 8). With 8, one match is 0.125 of share, so two chaotic winner flips
    /// (0.25, seen after the batch 13 bot tuning with both arms on the same rules) failed a limit of 0.15; the owner's walls
    /// ruling (6 or 8 16v16 matches is too small a sample) applies the same way here. Same limit, larger sample.
    /// </summary>
    [TestFixture]
    public sealed class BridgeSpanBalanceTests
    {
        const double WinShareLimit = 0.15;

        static GameConfig Cfg(int teamSize, double bridgeCap)
        {
            GameConfig c = teamSize >= 16 ? GameConfig.CreateDefault() : GameConfig.CreateBrowser5v5();
            c.Depth.Classic = false;
            c.Abilities.BuilderPieces.BridgeMaxCells = bridgeCap;
            return c;
        }

        static MatchResult Play(int seed, int teamSize, double bridgeCap, out double hatchAt)
        {
            GameConfig cfg = Cfg(teamSize, bridgeCap);
            World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = teamSize, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz;
            hatchAt = -1;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                MatchTick.Advance(w, null, scratch);
                if (hatchAt < 0 && w.Match.Hatched) hatchAt = w.Match.Time;
            }
            return w.Match.Winner;
        }

        [Test, Category("Slow")]
        public void LongerBridgesKeepTheControlsWinShareOnTheSameSeeds()
        {
            var failures = new System.Collections.Generic.List<string>();
            foreach (int size in new[] { 5, 16 })
            {
                int[] seeds = size == 16 ? new[] { 1337, 4242, 1, 2, 3, 4, 5, 101, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 99 } : new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20 };
                double[] caps = { 12, 6 };   // arm 0 = the rule (shipped), arm 1 = the control (1.0.868's cap)
                var wins = new int[2, 3];
                int differ = 0;
                foreach (int seed in seeds)
                {
                    var res = new MatchResult[2];
                    for (int arm = 0; arm < 2; arm++)
                    {
                        res[arm] = Play(seed, size, caps[arm], out double hatch);
                        wins[arm, res[arm] == MatchResult.Azure ? 0 : res[arm] == MatchResult.Crimson ? 1 : 2]++;
                        Console.WriteLine("{\"item\":\"b12-jz bridge balance match\",\"size\":" + size + ",\"seed\":" + seed + ",\"bridgeMaxCells\":" + caps[arm] +
                                          ",\"winner\":\"" + res[arm] + "\",\"hatchAt\":" + hatch.ToString("F0") + "}");
                    }
                    if (res[0] != res[1]) differ++;
                }
                int n = seeds.Length;
                for (int arm = 0; arm < 2; arm++)
                    Console.WriteLine("{\"item\":\"b12-jz bridge balance\",\"size\":" + size + ",\"bridgeMaxCells\":" + caps[arm] + ",\"matches\":" + n +
                                      ",\"azureWins\":" + wins[arm, 0] + ",\"crimsonWins\":" + wins[arm, 1] + ",\"draws\":" + wins[arm, 2] +
                                      ",\"azureShare\":" + ((double)wins[arm, 0] / n).ToString("F3") + ",\"crimsonShare\":" + ((double)wins[arm, 1] / n).ToString("F3") +
                                      ",\"seedsWithADifferentWinner\":" + differ + "}");
                for (int team = 0; team < 2; team++)
                {
                    double on = (double)wins[0, team] / n, off = (double)wins[1, team] / n;
                    string name = team == 0 ? "Azure" : "Crimson";
                    Console.WriteLine("{\"item\":\"b12-jz bridge balance win share\",\"size\":" + size + ",\"team\":\"" + name + "\",\"cap12\":" + on.ToString("F3") +
                                      ",\"cap6\":" + off.ToString("F3") + ",\"shift\":" + Math.Abs(on - off).ToString("F3") + ",\"limit\":" + WinShareLimit.ToString("F2") + "}");
                    if (Math.Abs(on - off) > WinShareLimit + 1e-9)
                        failures.Add(size + "v" + size + " " + name + " win share " + on.ToString("F3") + " with 12-cell bridges against " + off.ToString("F3") + " with 6 (limit " + WinShareLimit + ")");
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}
