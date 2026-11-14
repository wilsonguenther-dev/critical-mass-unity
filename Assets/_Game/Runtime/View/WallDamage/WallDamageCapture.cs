using System.Collections;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
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

        /// <summary>
        /// capture-gaps-1 (owner: "an unidentified dark pyramid shape and a white ring near the wall" in the b81 close-ups):
        /// names everything drawn within 4.5 u of the wall cell, so the next capture says what they are instead of a guess.
        /// Each renderer: object path, mesh, material, bounds centre and size. Then the sim things there: resource nodes
        /// (kind, charges), actors (class, team, state) and the Miner's own aim targets. Capture only (allocates once).
        /// Reading of the code so far: no view code builds a pyramid primitive; the candidates are the corruption node model
        /// (prop_corruption_growth_s/l, a dark crystal; with no model the node falls back to a navy cube), a bot standing
        /// there, and the cream ring of a pickup or supply drop (DepthWorldView) or a point target ring (TerrainAffordanceView,
        /// which CaptureForce turns on). This log settles it.
        /// </summary>
        static void Census(World w, double x, double z)
        {
            Vector3 c = SimSpace.ToWorld(x, 0, z);
            const float R = 4.5f;
            var sb = new System.Text.StringBuilder("census around wall cell (" + x + ", " + z + "):\n");
            foreach (Renderer r in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                Bounds b = r.bounds;
                Vector3 d = b.center - c;
                d.y = 0f;
                if (d.magnitude - Mathf.Max(b.extents.x, b.extents.z) > R) continue;
                if (b.extents.x > 12f || b.extents.z > 12f) continue;   // whole-map meshes (ground, effects) are listed by name only below
                string path = r.name;
                for (Transform t = r.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
                MeshFilter mf = r.GetComponent<MeshFilter>();
                sb.Append("  ").Append(path).Append(" mesh=").Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.name : "-")
                  .Append(" mat=").Append(r.sharedMaterial != null ? r.sharedMaterial.name : "-")
                  .Append(" at=").Append(b.center.ToString("0.0")).Append(" size=").Append(b.size.ToString("0.0")).Append('\n');
            }
            foreach (ResourceNode n in w.Nodes)
                if (System.Math.Abs(n.X - x) <= R && System.Math.Abs(n.Z - z) <= R)
                    sb.Append("  node ").Append(n.Kind).Append(" charges ").Append(n.Charges).Append(" at (").Append(n.X.ToString("0.0")).Append(", ").Append(n.Z.ToString("0.0")).Append(")\n");
            for (int i = 0; i < w.Actors.Length; i++)
                if (System.Math.Abs(w.X[i] - x) <= R && System.Math.Abs(w.Z[i] - z) <= R)
                    sb.Append("  actor ").Append(i).Append(' ').Append(w.Actors[i].Cls).Append(' ').Append(w.Actors[i].Team)
                      .Append(" alive ").Append(w.Actors[i].Alive).Append(" at (").Append(w.X[i].ToString("0.0")).Append(", ").Append(w.Z[i].ToString("0.0")).Append(")\n");
            Log(sb.ToString());
        }

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
            // wall-damage-2: the affordance markers and the target outline/ring drew over the wall in batch 8's stills; the
            // capture turns them off (settings are restored at the end) so the damage itself is what is judged.
            bool markers = WorldFeedbackSettings.Markers, outline = WorldFeedbackSettings.Outline;
            WorldFeedbackSettings.Markers = false;
            WorldFeedbackSettings.Outline = false;
            Stage();
            yield return new WaitForSecondsRealtime(3f);   // the world view rebuilds after a reset
            yield return BarricadeStrip();
            Stage();
            yield return new WaitForSecondsRealtime(3f);   // the world view rebuilds after a reset
            yield return WallStrip();
            WorldFeedbackSettings.Markers = markers;
            WorldFeedbackSettings.Outline = outline;
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

        /// <summary>
        /// wall-damage-2: a legal barricade tile in world terms. Batch 8's run placed none (the Miner stood in spawn ground,
        /// and the hill refuses too). Walks rings outward from a point 4 u outside the hill edge, on the side away from the
        /// Azure pad, stands the Miner 2 cells from the tile and asks the Sim's own refusal check before placing.
        /// </summary>
        bool PlaceLegal(World w, out int slot, out int tx, out int tz)
        {
            slot = -1; tx = tz = -1;
            MapDef m = w.Map;
            double hx = m.Hill.X, hz = m.Hill.Z, r = m.Hill.R + 4;
            double ax = m.Pads[(int)TeamId.Azure].X - hx, az = m.Pads[(int)TeamId.Azure].Z - hz;
            double al = System.Math.Sqrt(ax * ax + az * az);
            if (al < 1e-6) { ax = 1; az = 0; al = 1; }
            // Start on the hill's flank, a quarter turn from the Azure pad (not in either base).
            int sx = (int)System.Math.Floor(hx - az / al * r), sz = (int)System.Math.Floor(hz + ax / al * r);
            int[] ox = { 2, -2, 0, 0 }, oz = { 0, 0, 2, -2 };
            for (int ring = 0; ring <= 10; ring++)
            for (int dz = -ring; dz <= ring; dz++)
            for (int dx = -ring; dx <= ring; dx++)
            {
                if (System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dz)) != ring) continue;
                int cx = sx + dx, cz = sz + dz;
                if (!MapGrid.InBounds(m, cx, cz) || m.Cells[cz * m.Width + cx] != Cell.Floor) continue;
                for (int k = 0; k < 4; k++)
                {
                    int mx = cx + ox[k], mz = cz + oz[k];
                    if (!MapGrid.InBounds(m, mx, mz) || m.Cells[mz * m.Width + mx] != Cell.Floor) continue;
                    Put(w, 0, mx + 0.5, mz + 0.5, System.Math.Atan2(cz - mz, cx - mx));
                    int why = Barricades.BarricadeRefusal(w, 0, cx + 0.5, cz + 0.5);
                    if (why != 0) continue;
                    if (!Barricades.TryPlaceBarricade(w, 0, cx + 0.5, cz + 0.5)) { Log("barricade: tile (" + cx + ", " + cz + ") passed the check but TryPlace refused"); continue; }
                    for (int i = 0; i < w.Barricades.Length; i++)
                        if (w.Barricades[i].Active && w.Barricades[i].Cx == cx && w.Barricades[i].Cz == cz) slot = i;
                    tx = cx; tz = cz;
                    return slot >= 0;
                }
            }
            return false;
        }

        IEnumerator BarricadeStrip()
        {
            World w = _driver.World;
            if (!PlaceLegal(w, out int slot, out int tx, out int tz)) { Log("barricade: PLACEMENT FAILED (no legal tile found near the hill flank)"); yield break; }
            Log("barricade: ACCEPTED at tile (" + tx + ", " + tz + ") slot " + slot + "; miner at (" + w.X[0] + ", " + w.Z[0] + ")");
            w.Actors[0].SpawnProtect = 30;
            _driver.StepTicks(1);
            yield return Shot("barricade-0-intact", 1.2f);
            // Every hit to destruction, as an enemy Warrior's swing takes it (AttackDamage per hit).
            double max = w.Config.Abilities.MinerBarricade.MaxHp;
            double dmg = System.Math.Max(1, w.Config.Class(ClassId.Warrior).AttackDamage);
            int hits = 0;
            while (w.Barricades[slot].Active && w.Barricades[slot].Hp - dmg > 1e-9 && hits < 20)
            {
                w.Barricades[slot].Hp -= dmg;
                hits++;
                if (hits == 1) yield return Shot("barricade-hit1-shake", 0.04f);
                yield return Shot("barricade-hit" + hits, 0.35f);
                Log("barricade hit " + hits + ": hp " + w.Barricades[slot].Hp.ToString("0") + "/" + max.ToString("0") + " stage shown " + BarricadeDamage.StageShown(slot));
            }
            w.Barricades[slot].Hp = 0;
            _driver.StepTicks(1);
            Log("barricade: broken after " + (hits + 1) + " hits; active=" + w.Barricades[slot].Active);
            yield return Shot("barricade-break", 0.15f);
            yield return Shot("barricade-rubble-1s", 0.85f);
            yield return Shot("barricade-rubble-6s", 5f);
        }

        /// <summary>The breakable cell with breakable neighbours on both sides (so the per-cell look can be judged), or -1.</summary>
        static bool Cluttered(World w, int cx, int cz)
        {
            MapDef m = w.Map;
            if (m.Decorations != null)
                foreach (DecorationDef d in m.Decorations)
                    if (System.Math.Abs(d.Cx - cx) <= 3 && System.Math.Abs(d.Cz - cz) <= 3) return true;
            foreach (ResourceNode nd in w.Nodes)
                if (System.Math.Abs(nd.X - (cx + 0.5)) <= 3.5 && System.Math.Abs(nd.Z - (cz + 0.5)) <= 3.5) return true;
            return false;
        }

        static int PickWallCell(World w, out int n1, out int n2)
        {
            MapDef m = w.Map;
            n1 = n2 = -1;
            for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < m.Cells.Length; i++)
            {
                if (m.Cells[i] != Cell.BreakableIntact) continue;
                if (pass == 0 && Cluttered(w, i % m.Width, i / m.Width)) continue;
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
            int i = PickWallCell(w, out int n1, out int n2);
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
            Log("wall cell " + i + " (" + cx + ", " + cz + ") neighbours " + n1 + ", " + n2 + "; miner at (" + bx + ", " + bz + "); cluttered=" + Cluttered(w, cx, cz));
            if (m.Decorations != null)
                foreach (DecorationDef d in m.Decorations)
                    if (System.Math.Abs(d.Cx - cx) <= 4 && System.Math.Abs(d.Cz - cz) <= 4) Log("  in frame: decoration " + d.Kind + " at (" + d.Cx + ", " + d.Cz + ")");
            yield return Shot("wall-0-intact", 0.6f);
            Census(w, cx + 0.5, cz + 0.5);
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
