using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.WallDamage
{
    /// <summary>
    /// wall-damage-1 capture director (player flag <c>-wallDamageShots</c>, kept apart from the affordance, building and skill
    /// capture flags): frames ONE Miner barricade and ONE map breakable wall cell and steps each through every stage to
    /// destruction and rubble, taking a still at each (files "shot-walldmg-*.png" in GamePaths.DataDir) and logging what the
    /// view shows ("[CM.WallDmg]" lines: stage shown, per-cell looks of the hit cell and its neighbours, rubble alive).
    /// </summary>
    public sealed class WallDamageCapture : MonoBehaviour
    {
        public const string Flag = "-wallDamageShots";
        public const string Prefix = "shot-walldmg-";

        SimDriverBehaviour _driver;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            if (_driver == null) { enabled = false; return; }
            StartCoroutine(Run());
        }

        static void Log(string s) => Debug.Log("[CM.WallDmg] " + s);

        IEnumerator Shot(string name, float settle)
        {
            yield return new WaitForSecondsRealtime(settle);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(GamePaths.DataDir, Prefix + name + ".png"));
            Log("shot " + name + " rubble=" + RubblePool.Alive);
            yield return null;
        }

        IEnumerator Run()
        {
            float t0 = Time.realtimeSinceStartup;
            while (_driver.World == null || _driver.World.Match.Phase != MatchPhase.Playing || _driver.World.Match.Time < 3)
            {
                if (Time.realtimeSinceStartup - t0 > 120f) { Log("no match started; giving up"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
            TerrainAffordanceView.CaptureForce = true;
            Stage();
            yield return new WaitForSecondsRealtime(3f);   // the world view rebuilds after a reset
            yield return BarricadeStrip();
            Stage();
            yield return new WaitForSecondsRealtime(3f);   // the world view rebuilds after a reset
            yield return WallStrip();
            TerrainAffordanceView.CaptureForce = false;
            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit(0);
        }

        void Stage()
        {
            World w = _driver.World;
            World.ResetWorld(w, new WorldOptions { Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Miner, Config = _driver.Config });
            _driver.HumanAutopilot = false;
            _driver.TimeScale = 0;
            _driver.StepTicks(3);
        }

        static void Put(World w, int id, double x, double z, double facing)
        {
            w.X[id] = w.PrevX[id] = x;
            w.Z[id] = w.PrevZ[id] = z;
            w.Actors[id].Facing = facing;
        }

        IEnumerator BarricadeStrip()
        {
            World w = _driver.World;
            int slot = -1;
            for (int f = 0; f < 4 && slot < 0; f++)
            {
                double face = w.Actors[0].Facing + f * System.Math.PI / 2;
                if (Barricades.TryPlaceBarricade(w, 0, w.X[0] + System.Math.Cos(face) * 1.5, w.Z[0] + System.Math.Sin(face) * 1.5))
                    for (int i = 0; i < w.Barricades.Length; i++) if (w.Barricades[i].Active) slot = i;
            }
            if (slot < 0) { Log("barricade: none could be placed here"); yield break; }
            double max = w.Config.Abilities.MinerBarricade.MaxHp;
            double[] left = { 1.0, 0.8, 0.5, 0.2 };
            for (int k = 0; k < left.Length; k++)
            {
                w.Barricades[slot].Hp = max * left[k];
                yield return Shot("barricade-stage-" + k, 0.35f);
                Log("barricade stage " + k + " shown=" + BarricadeDamage.StageShown(slot));
            }
            w.Barricades[slot].Hp = max * 0.1;
            yield return Shot("barricade-hit-shake", 0.04f);
            w.Barricades[slot].Hp = 0;
            _driver.StepTicks(1);
            yield return Shot("barricade-break", 0.15f);
            yield return Shot("barricade-rubble-1s", 0.85f);
            yield return Shot("barricade-rubble-6s", 5f);
        }

        /// <summary>The breakable cell with breakable neighbours on both sides (so the per-cell look can be judged), or -1.</summary>
        static int PickWallCell(MapDef m, out int n1, out int n2)
        {
            n1 = n2 = -1;
            for (int i = 0; i < m.Cells.Length; i++)
            {
                if (m.Cells[i] != Cell.BreakableIntact) continue;
                int cx = i % m.Width, cz = i / m.Width;
                if (cx > 0 && cx < m.Width - 1 && m.Cells[i - 1] == Cell.BreakableIntact && m.Cells[i + 1] == Cell.BreakableIntact) { n1 = i - 1; n2 = i + 1; return i; }
                if (cz > 0 && cz < m.Height - 1 && m.Cells[i - m.Width] == Cell.BreakableIntact && m.Cells[i + m.Width] == Cell.BreakableIntact) { n1 = i - m.Width; n2 = i + m.Width; return i; }
            }
            for (int i = 0; i < m.Cells.Length; i++) if (m.Cells[i] == Cell.BreakableIntact) return i;
            return -1;
        }

        /// <summary>One Miner hit on exactly one cell (the same arithmetic as Abilities.MinerWallBreak, for that cell only).</summary>
        static void HitCell(World w, int i)
        {
            MapDef map = w.Map;
            byte t = map.Cells[i];
            if (t != Cell.BreakableIntact && t != Cell.BreakableCracked) return;
            double hps = w.Config.Abilities.MinerWallBreak.HitsPerState;
            double hp = map.WallHp[i];
            if (hp == 0) hp = t == Cell.BreakableIntact ? 2 * hps : hps;
            hp--;
            map.WallHp[i] = unchecked((byte)(int)hp);
            byte next = hp > hps ? Cell.BreakableIntact : hp > 0 ? Cell.BreakableCracked : Cell.BreakableDestroyed;
            int cx = i % map.Width, cz = i / map.Width;
            if (next != t) MapGrid.SetCell(w, cx, cz, next);
            World.PushEvent(w, SimEventType.WallBreak, 0, i, cx + 0.5, cz + 0.5, next);
        }

        IEnumerator WallStrip()
        {
            World w = _driver.World;
            MapDef m = w.Map;
            int i = PickWallCell(m, out int n1, out int n2);
            if (i < 0) { Log("wall: no breakable cell on this map"); yield break; }
            int cx = i % m.Width, cz = i / m.Width;
            // Stand the Miner on the nearest floor cell beside it, facing it (the camera follows him).
            double bx = cx + 0.5, bz = cz + 0.5, bd = double.MaxValue;
            for (int dz = -3; dz <= 3; dz++)
            for (int dx = -3; dx <= 3; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (!MapGrid.InBounds(m, x, z) || m.Cells[z * m.Width + x] != Cell.Floor) continue;
                double d = dx * dx + dz * dz;
                if (d >= 2 && d < bd) { bd = d; bx = x + 0.5; bz = z + 0.5; }
            }
            Put(w, 0, bx, bz, System.Math.Atan2(cz + 0.5 - bz, cx + 0.5 - bx));
            w.Actors[0].SpawnProtect = 30;
            _driver.StepTicks(1);
            Log("wall cell " + i + " (" + cx + ", " + cz + ") neighbours " + n1 + ", " + n2 + "; miner at (" + bx + ", " + bz + ")");
            yield return Shot("wall-0-intact", 0.6f);
            int hits = 0;
            while (m.Cells[i] == Cell.BreakableIntact || m.Cells[i] == Cell.BreakableCracked)
            {
                HitCell(w, i);
                w.GridVersion++;
                hits++;
                bool gone = m.Cells[i] == Cell.BreakableDestroyed;
                if (!gone) yield return Shot("wall-hit" + hits + "-shake", 0.04f);
                yield return Shot("wall-hit" + hits + (gone ? "-break" : "-settled"), gone ? 0.15f : 0.4f);
                Log("wall after hit " + hits + ": cell " + BreakableCellSet.Shown(i) + ", neighbours " + BreakableCellSet.Shown(n1) + " / " + BreakableCellSet.Shown(n2));
                if (hits > 12) break;
            }
            yield return Shot("wall-rubble-1s", 0.85f);
            yield return Shot("wall-rubble-6s", 5f);
        }
    }
}
