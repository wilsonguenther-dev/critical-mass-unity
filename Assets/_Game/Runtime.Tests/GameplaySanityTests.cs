using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Gameplay sanity suite, EditMode half (test/gameplay-sanity, 2026-11-12). The owner played a build where every
    /// character and the Kaiju walked BACKWARDS; rule tests and beauty shots could not see it. These checks catch the class
    /// of defect that is obvious to a person watching and invisible to a rule test: here, the model's own front against
    /// the direction the view turns it to (FACING), the art's heights against the art direction (SCALE) and the dressed
    /// world against the sim map (WORLD AGREEMENT). The PlayMode half is Runtime.PlayTests/GameplaySanityPlayTests.cs.
    /// <para>A red here means the GAME is wrong, not the test: each message says what a player would see.</para>
    /// </summary>
    public sealed class GameplaySanityTests
    {
        /// <summary>Most a visual front may differ from the facing it should show, degrees.</summary>
        const float FacingToleranceDeg = 30f;

        static IEnumerable<string> FacingModels() => new[]
        {
            ArtMapping.Warrior, ArtMapping.Miner, ArtMapping.Farmer, ArtMapping.KaijuHatchling, ArtMapping.KaijuAdult,
        };

        static ArtCatalog Catalog()
        {
            var cat = AssetDatabase.LoadAssetAtPath<ArtCatalog>(ArtCatalog.AssetPath);
            Assert.IsNotNull(cat, ArtCatalog.AssetPath + " is missing (run ProjectSetup.Apply)");
            return cat;
        }

        // ------------------------------------------------------------------------------------------------ 1. FACING

        /// <summary>
        /// ActorViews turns a model root so its local +Z is the sim facing (SimSpace.YawDeg). So the model's own visual
        /// front must be its local +Z, in the bind pose and in every frame of Idle, Walk, Run and Attack (the clips come
        /// from KayKit_Clips for the characters, so a clip that turns the rig is caught here too). KNOWN FAILING on main
        /// 2026-11-12: the owner saw every class and the Kaiju walk backwards.
        /// </summary>
        [TestCaseSource(nameof(FacingModels))]
        public void ModelFrontIsItsLocalForwardInBindPoseAndEveryLocomotionClip(string key)
        {
            ArtEntry entry = Catalog().Find(key);
            if (entry == null) Assert.Ignore(key + " has no model in the art catalog (grey box): nothing to face");
            GameObject go = Object.Instantiate(entry.Model);
            var bad = new List<string>();
            int samples = 0;
            try
            {
                go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                go.transform.localScale = Vector3.one;
                Measure(go.transform, key, "bind pose", bad, ref samples);
                foreach (string clipName in new[] { ArtMapping.Idle, ArtMapping.Walk, ArtMapping.Run, ArtMapping.Attack })
                {
                    AnimationClip clip = entry.Clip(clipName);
                    if (clip == null) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        float t = clip.length * k / 4f;
                        clip.SampleAnimation(go, t);
                        Measure(go.transform, key, $"{clipName} clip at {t:0.00} s", bad, ref samples);
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            Assert.Greater(samples, 0, key + ": no pose could be measured (the rig has no toes/foot or Head/Tail bones)");
            CollectionAssert.IsEmpty(bad, string.Join("\n", bad.Take(12)));
        }

        static void Measure(Transform root, string key, string when, List<string> bad, ref int samples)
        {
            if (!SanityRig.TryFront(root, out Vector3 front, out string cue)) return;
            samples++;
            float angle = SanityRig.AngleDeg(front, Vector3.forward);
            if (angle <= FacingToleranceDeg) return;
            string second = SanityRig.TrySideFront(root, out Vector3 side)
                ? $" (the hips say {SanityRig.AngleDeg(side, Vector3.forward):0} degrees)" : "";
            bad.Add($"{key}, {when}: the model's front ({cue}) is {angle:0} degrees from its own forward{second}. ActorViews " +
                    $"points that forward along the sim facing, so in the game this model is {SanityRig.Verdict(angle)}. " +
                    "Fix the import rotation or the view's yaw for this model, not this test.");
        }

        /// <summary>The sim-to-Unity yaw rotates local +Z exactly onto the sim facing, all the way round.</summary>
        [Test]
        public void ViewYawTurnsLocalForwardOntoTheSimFacing()
        {
            for (int k = 0; k < 8; k++)
            {
                double f = k * Math.PI / 4;
                Vector3 got = Quaternion.Euler(0f, SimSpace.YawDeg(f), 0f) * Vector3.forward;
                float angle = SanityRig.AngleDeg(got, SanityRig.SimDir(f));
                Assert.Less(angle, 0.5f, $"sim facing {k * 45} degrees: the view's yaw sends a model's forward {angle:0} degrees away from it");
            }
        }

        // ------------------------------------------------------------------------------------------------ 3. SCALE

        /// <summary>Art direction heights: characters stand RenderConfig.CharacterHeight tall, the adult Kaiju KaijuHeight.</summary>
        [Test]
        public void ModelHeightsAreInsideTheArtDirectionRange()
        {
            var render = new RenderConfig();
            var bad = new List<string>();
            foreach (ModelSpec s in ArtMapping.Models)
            {
                double want = s.Kind == ArtKind.Character && s.Meshes.Length > 0 ? render.CharacterHeight
                    : s.Key == ArtMapping.KaijuAdult ? render.KaijuHeight : 0;
                if (want <= 0) continue;
                if (s.Height < 0.8 * want || s.Height > 1.2 * want)
                    bad.Add($"{s.Key} is {s.Height:0.00} u tall in the art table; the art direction asks {want:0.0} u (+/- 20%), so it reads as the wrong size next to the others");
            }
            CollectionAssert.IsEmpty(bad, string.Join("\n", bad));
        }

        // ------------------------------------------------------------------------------------------------ 5. WORLD AGREEMENT

        static MapDef Map() => SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateDefault());

        static WorldPlan Plan(MapDef map)
        {
            WorldKitCatalog cat = WorldKitCatalog.Load();
            if (cat == null || cat.Names.Length == 0) Assert.Ignore("no world kit catalog: the grey-box view draws every cell itself");
            BasesFile bases = BasesFile.Load();
            Assert.IsNotNull(bases, "Resources/" + BasesFile.ResourcePath + " is missing");
            return WorldDresser.Plan(map, bases, cat.Centroid, cat.Body);
        }

        static byte CellAt(MapDef map, double x, double z)
        {
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return Cell.Void;
            return map.Cells[cz * map.Width + cx];
        }

        static bool LandNear(MapDef map, int cx, int cz)
        {
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int x = cx + dx, z = cz + dz;
                if (x < 0 || z < 0 || x >= map.Width || z >= map.Height) continue;
                if (WorldDresser.Solid(map.Cells[z * map.Width + x])) return true;
            }
            return false;
        }

        /// <summary>
        /// Every walkable sim cell has ground drawn under it at the sim height (an env_ground_cell tile at its centre, or
        /// a bridge deck), and no ground tile or deck sits over a void cell. Sampled on the whole grid (112 x 72).
        /// </summary>
        [Test]
        public void GroundIsDrawnUnderEveryWalkableCellAndNeverOverTheVoid()
        {
            MapDef map = Map();
            WorldPlan plan = Plan(map);
            var tiles = new Dictionary<int, double>();
            var bad = new List<string>();
            foreach (WorldPlacement p in plan.Placements)
            {
                if (!p.Surface) continue;
                int cx = (int)Math.Floor(p.SimX), cz = (int)Math.Floor(p.SimZ);
                byte under = CellAt(map, p.SimX, p.SimZ);
                if (p.Piece == "env_ground_cell") tiles[cz * map.Width + cx] = p.Y;
                if (under != Cell.Void) continue;
                // Fill triangles close the rim's diagonal corners: they start on a void cell next to land by design.
                if (p.Piece == "env_ground_fill_tri" && LandNear(map, cx, cz)) continue;
                // Bridge spans are checked against the sim's spans below.
                if (p.Piece == "env_bridge_span") continue;
                bad.Add($"{p.Piece} is drawn over the void at sim ({p.SimX:0.0}, {p.SimZ:0.0}): a player sees ground there, walks onto it and falls");
            }
            for (int cz = 0; cz < map.Height; cz++)
            for (int cx = 0; cx < map.Width; cx++)
            {
                int i = cz * map.Width + cx;
                byte t = map.Cells[i];
                if (!MapGrid.IsWalkable(map, cx, cz) || t == Cell.Bridge) continue;
                if (!tiles.TryGetValue(i, out double y))
                    bad.Add($"walkable cell {cx},{cz} has no ground drawn: a player sees a hole where the sim lets them stand");
                else if (Math.Abs(y - map.Height01[i]) > 0.05)
                    bad.Add($"cell {cx},{cz}: ground drawn at height {y:0.00}, the sim stands actors at {map.Height01[i]:0.00}: they float or sink");
            }
            Debug.Log($"[GameplaySanity] ground: {tiles.Count} tiles checked against {map.Width}x{map.Height} cells, {bad.Count} problems");
            CollectionAssert.IsEmpty(bad, $"{bad.Count} ground problems, first 15:\n" + string.Join("\n", bad.Take(15)));
        }

        /// <summary>Bridges are drawn along every sim bridge span and every sim bridge cell, and nowhere else.</summary>
        [Test]
        public void BridgesAreDrawnWhereTheSimHasBridgesAndNowhereElse()
        {
            MapDef map = Map();
            WorldPlan plan = Plan(map);
            var decks = plan.Placements.Where(p => p.Piece == "env_bridge_cell" || p.Piece == "env_bridge_span").ToList();
            var bad = new List<string>();
            double SpanDist(double x, double z)
            {
                double best = double.PositiveInfinity;
                foreach (BridgeSpan s in BridgeSpans.All) best = Math.Min(best, WorldDresser.SegDist(x, z, s.X0, s.Z0, s.X1, s.Z1));
                return best;
            }
            double DeckDist(double x, double z)
            {
                double best = double.PositiveInfinity;
                foreach (WorldPlacement p in decks) best = Math.Min(best, Math.Sqrt((p.SimX - x) * (p.SimX - x) + (p.SimZ - z) * (p.SimZ - z)));
                return best;
            }
            // Drawn where the sim has them: along each span every 1 u, and on every bridge cell.
            for (int k = 0; k < BridgeSpans.All.Count; k++)
            {
                BridgeSpan s = BridgeSpans.All[k];
                int steps = Math.Max(1, (int)Math.Ceiling(s.Len));
                for (int j = 0; j <= steps; j++)
                {
                    double x = s.X0 + (s.X1 - s.X0) * j / steps, z = s.Z0 + (s.Z1 - s.Z0) * j / steps;
                    if (WorldDresser.Solid(CellAt(map, x, z))) continue; // the span's ends rest on land
                    if (DeckDist(x, z) > 2.0)
                    {
                        bad.Add($"sim bridge span {k} ({s.X0:0.0},{s.Z0:0.0})-({s.X1:0.0},{s.Z1:0.0}) has no deck drawn near ({x:0.0}, {z:0.0}): players walk on air");
                        break;
                    }
                }
            }
            for (int i = 0; i < map.Cells.Length; i++)
            {
                if (map.Cells[i] != Cell.Bridge) continue;
                double x = i % map.Width + 0.5, z = i / map.Width + 0.5;
                if (DeckDist(x, z) > 2.0) bad.Add($"sim bridge cell {i % map.Width},{i / map.Width} has no deck drawn: players walk on air");
            }
            // Nowhere else: every deck lies on a sim bridge cell or a sim span.
            foreach (WorldPlacement p in decks)
            {
                if (CellAt(map, p.SimX, p.SimZ) == Cell.Bridge || SpanDist(p.SimX, p.SimZ) <= 1.0) continue;
                bad.Add($"{p.Piece} drawn at sim ({p.SimX:0.0}, {p.SimZ:0.0}) where the sim has no bridge: a player walks onto it and falls");
            }
            Debug.Log($"[GameplaySanity] bridges: {BridgeSpans.All.Count} sim spans, {decks.Count} drawn deck pieces, {bad.Count} problems");
            CollectionAssert.IsEmpty(bad, string.Join("\n", bad.Take(15)));
        }

        static IEnumerable<WorldPlacement> Pieces(WorldPlan plan, string piece) => plan.Placements.Where(p => p.Piece == piece);

        static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));

        /// <summary>
        /// Base pads, hill and shrine dressing are centred on their sim positions, each team's district is on its own
        /// side, and the pad-to-hill direction drawn matches the sim's (the bases source was mirrored once). The shrine and
        /// hill sites face the right way when every sim breakable cell is drawn by a breakable piece.
        /// </summary>
        [Test]
        public void BasesHillAndShrineAreCentredOnTheSimAndFaceTheRightWay()
        {
            MapDef map = Map();
            WorldPlan plan = Plan(map);
            var bad = new List<string>();
            foreach (TeamId team in new[] { TeamId.Azure, TeamId.Crimson })
            {
                PadDef pad = map.Pads[(int)team], enemy = map.Pads[1 - (int)team];
                WorldTeam wt = team == TeamId.Azure ? WorldTeam.Azure : WorldTeam.Crimson;
                var pads = Pieces(plan, "cm_respawn_pad").Where(p => p.Team == wt).ToList();
                if (pads.Count == 0) { bad.Add($"no {team} respawn pad is drawn"); continue; }
                WorldPlacement drawn = pads.OrderBy(p => Dist(p.SimX, p.SimZ, pad.X, pad.Z)).First();
                double off = Dist(drawn.SimX, drawn.SimZ, pad.X, pad.Z);
                if (off > 1.5) bad.Add($"the {team} respawn pad is drawn {off:0.0} u from where the sim spawns {team} ({pad.X:0.0}, {pad.Z:0.0})");
                double simDir = Math.Atan2(map.Hill.Z - pad.Z, map.Hill.X - pad.X), artDir = Math.Atan2(map.Hill.Z - drawn.SimZ, map.Hill.X - drawn.SimX);
                double turn = Math.Abs(Math.IEEERemainder(simDir - artDir, 2 * Math.PI)) * 180 / Math.PI;
                if (turn > 15) bad.Add($"from the drawn {team} pad the hill is {turn:0} degrees off the sim's pad-to-hill direction (mirrored base?)");
                var district = plan.Placements.Where(p => p.Cat == "base" && p.Team == wt && !p.Piece.StartsWith("cm_layout_", StringComparison.Ordinal)).ToList();
                if (district.Count > 0)
                {
                    double mx = district.Average(p => p.SimX), mz = district.Average(p => p.SimZ);
                    if (Dist(mx, mz, enemy.X, enemy.Z) < Dist(mx, mz, pad.X, pad.Z))
                        bad.Add($"the {team} district is drawn on the enemy's side (its pieces centre at {mx:0.0}, {mz:0.0}, nearer the {(team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure)} pad)");
                }
            }
            void Centred(string piece, double x, double z, double tol, string what)
            {
                var ps = Pieces(plan, piece).ToList();
                if (ps.Count == 0) { bad.Add($"{piece} ({what}) is not drawn"); return; }
                double d = ps.Min(p => Dist(p.SimX, p.SimZ, x, z));
                if (d > tol) bad.Add($"{piece} ({what}) is drawn {d:0.0} u from the sim's {what} at ({x:0.0}, {z:0.0})");
            }
            Centred("cm_hill_dais", map.Hill.X, map.Hill.Z, 1.0, "hill centre");
            Centred("cm_shrine_core", map.Shrine.X, map.Shrine.Z, 1.0, "shrine centre");
            Centred("cm_shrine_egg", map.Shrine.X, map.Shrine.Z, 1.0, "egg");
            foreach (WorldPlacement p in Pieces(plan, "cm_hill_arc_a").Concat(Pieces(plan, "cm_hill_arc_b")))
                if (Dist(p.SimX, p.SimZ, map.Hill.X, map.Hill.Z) > map.Hill.R + 6)
                    bad.Add($"{p.Piece} is drawn at ({p.SimX:0.0}, {p.SimZ:0.0}), away from the hill ring the sim scores");
            // Facing of the hill and shrine sites: their breakable walls must land on the sim's breakable cells.
            var claimed = new HashSet<int>(plan.Breakables.SelectMany(b => b.Cells));
            int unclaimed = 0;
            for (int i = 0; i < map.Cells.Length; i++)
            {
                byte t = map.Cells[i];
                if (t != Cell.BreakableIntact && t != Cell.BreakableCracked && t != Cell.BreakableDestroyed) continue;
                if (claimed.Contains(i)) continue;
                if (unclaimed++ < 6) bad.Add($"sim breakable cell {i % map.Width},{i / map.Width} has no breakable wall drawn on it (a site faces the wrong way?): players hit an invisible wall");
            }
            Debug.Log($"[GameplaySanity] bases: notes: {string.Join("; ", plan.Notes)}");
            CollectionAssert.IsEmpty(bad, string.Join("\n", bad.Take(15)));
        }
    }
}
