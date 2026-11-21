using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>b15 fix round probe 3 (probe branch only): who sits in CombatFeelBalance's hot stuck cells with the feel on.</summary>
    [TestFixture, Category("Probe")]
    public sealed class B15FixProbe3Tests
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string F(double v) => v.ToString("0.00", Inv);

        static char CellChar(byte t)
        {
            switch (t)
            {
                case Cell.Void: return ' ';
                case Cell.Floor: return '.';
                case Cell.Wall: return '#';
                case Cell.Bridge: return '=';
                case Cell.Barricade: return 'B';
                case Cell.LowWall: return 'w';
                case Cell.Water: return '~';
                default: return '?';
            }
        }

        static void DumpArea(World w, string label, int x0, int x1, int z0, int z1)
        {
            MapDef map = w.Map;
            var sb = new StringBuilder();
            sb.Append("[area] " + label + " x " + x0 + ".." + x1 + " z " + z0 + ".." + z1 + " (cell | lift)\n");
            for (int z = z0; z <= z1; z++)
            {
                sb.Append(z.ToString("00", Inv)).Append(' ');
                for (int x = x0; x <= x1; x++) sb.Append(MapGrid.InBounds(map, x, z) ? CellChar(map.Cells[z * map.Width + x]) : '!');
                sb.Append("  |  ");
                for (int x = x0; x <= x1; x++)
                {
                    if (!MapGrid.InBounds(map, x, z) || map.Lift == null) { sb.Append('-'); continue; }
                    int l = map.Lift[z * map.Width + x];
                    sb.Append(l == 0 ? '.' : l < 0 ? (char)('a' - 1 - l) : (char)('0' + Math.Min(9, l)));
                }
                sb.Append('\n');
            }
            Console.Write(sb.ToString());
        }

        [Test]
        public void HotStuckCellsWithTheFeelOn()
        {
            foreach (int seed in new[] { 9, 11, 4242, 5 })
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
                World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = cfg });
                InputFrame scratch = SimInput.EmptyInput();
                int n = w.ActorCount, window = (int)Math.Round(3 * cfg.SimHz);
                var ax = new double[n]; var az = new double[n]; var at = new int[n]; var printed = new int[n];
                for (int i = 0; i < n; i++) at[i] = -1;
                var byActor = new Dictionary<string, int>();
                bool dumped = false;
                Console.WriteLine("[hot] seed " + seed + " hill (" + F(w.Map.Hill.X) + ", " + F(w.Map.Hill.Z) + ") r " + F(w.Map.Hill.R));
                while (w.Match.Phase != MatchPhase.Ended && w.Tick < 15000)
                {
                    MatchTick.Advance(w, null, scratch);
                    BotsState st = BotBrain.GetBotsState(w);
                    for (int i = 0; i < n; i++)
                    {
                        Actor a = w.Actors[i];
                        InputFrame f = w.Inputs[i];
                        bool pressing = a.Alive && a.StandUp <= 0 && f.MoveX * f.MoveX + f.MoveZ * f.MoveZ > 0.01;
                        double dx = w.X[i] - ax[i], dz = w.Z[i] - az[i];
                        if (!pressing || at[i] < 0 || dx * dx + dz * dz >= 0.16)
                        {
                            ax[i] = w.X[i]; az[i] = w.Z[i]; at[i] = pressing ? w.Tick : -1;
                            continue;
                        }
                        if (w.Tick - at[i] < window) continue;
                        at[i] = w.Tick;
                        string key = "id " + i + " " + a.Team + " " + a.Cls + " cell " + (int)Math.Floor(w.X[i]) + "," + (int)Math.Floor(w.Z[i]);
                        byActor.TryGetValue(key, out int c);
                        byActor[key] = c + 1;
                        if (printed[i] >= 3) continue;
                        printed[i]++;
                        double nearE = 1e9; int touching = 0;
                        for (int j = 0; j < n; j++)
                        {
                            if (j == i || !w.Actors[j].Alive) continue;
                            double ex = w.X[j] - w.X[i], ez = w.Z[j] - w.Z[i], d = Math.Sqrt(ex * ex + ez * ez);
                            if (w.Actors[j].Team != a.Team && d < nearE) nearE = d;
                            if (d < 1.0) touching++;
                        }
                        Console.WriteLine("[hot] seed " + seed + " t " + F(w.Match.Time) + " " + key + " pos (" + F(w.X[i]) + ", " + F(w.Z[i]) + ") ai " + a.AiState
                            + " task " + (st != null && st.TaskKind != null ? st.TaskKind[i] : -1) + " move (" + F(f.MoveX) + ", " + F(f.MoveZ) + ") attack " + f.Attack
                            + " stun " + a.HitStunTicks + " nearEnemy " + F(nearE) + " bodiesWithin1 " + touching + " wantMove " + (st != null ? st.WantMove[i] : -1));
                        if (!dumped)
                        {
                            dumped = true;
                            int cx = (int)Math.Floor(w.X[i]), cz = (int)Math.Floor(w.Z[i]);
                            DumpArea(w, "seed " + seed + " at " + F(w.Match.Time), cx - 8, cx + 8, cz - 7, cz + 7);
                        }
                    }
                }
                var list = new List<KeyValuePair<string, int>>(byActor);
                list.Sort((p, q) => q.Value.CompareTo(p.Value));
                for (int k = 0; k < Math.Min(8, list.Count); k++) Console.WriteLine("[hot] seed " + seed + " top " + list[k].Key + " x" + list[k].Value);
            }
        }
    }
}
