using System;
using System.Collections.Generic;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b14 ladder fix (owner review 2026-11-18: "the ladder is literally in the wall"). The built ladder must stand clear
    /// of the rock (feet on the ground, rails on the lip, nothing inside the wall), a fighter crossing a Skybreak ladder
    /// must be lifted to the rock top over the wall cells and set down on the landing, and a rope bridge must be walked
    /// end to end. Each case prints one evidence line.
    /// </summary>
    [TestFixture]
    public sealed class LadderClimbTests
    {
        const float Tol = 0.03f;

        [Test]
        public void BuiltLadderMeshStandsClearOfTheWallFeetOnGroundTopAtTheLip()
        {
            Mesh mesh = ChunkyLadder.Mesh;
            Vector3[] v = mesh.vertices;
            foreach (float top in new[] { LadderClimb.HillRockTop, LadderClimb.WallBoxTop })
            foreach (int cells in new[] { 1, 2 })
            for (int side = 0; side < 2; side++)
            {
                float half = cells * 0.5f;
                Matrix4x4 m = StagedBuildView.ChunkyLadderMatrix(Vector3.zero, Vector3.forward, Vector3.right, cells, side, 0, top);
                float minY = float.MaxValue, maxY = float.MinValue, deepest = 0f, nearTop = float.MaxValue;
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = m.MultiplyPoint3x4(v[i]);
                    minY = Mathf.Min(minY, p.y);
                    maxY = Mathf.Max(maxY, p.y);
                    float into = half - Mathf.Abs(p.z);          // > 0: inside the wall's run along the span
                    if (p.y < top - Tol && into > deepest) deepest = into;
                    if (p.y >= top - 0.25f) nearTop = Mathf.Min(nearTop, Mathf.Abs(p.z) - half);
                }
                Debug.Log($"[ladder fix] top {top} cells {cells} side {side}: feet y {minY:F3}, rails top {maxY:F3}, deepest into the rock {deepest:F3}, top contact {nearTop:F3} u from the face");
                Assert.That(deepest, Is.LessThanOrEqualTo(Tol), "no part of the ladder below the lip is inside the wall");
                Assert.That(minY, Is.InRange(-0.15f, 0.05f), "feet on the ground (the rail ends sink a few cm, planted)");
                Assert.That(maxY, Is.GreaterThanOrEqualTo(top), "rails reach the lip");
                Assert.That(nearTop, Is.InRange(-Tol, 0.3f), "the top rests against the face, not floating off it");
            }
        }

        [Test]
        public void ClimbProfileRisesUpTheLadderHoldsTheRockTopAndComesDown()
        {
            double half = 1.0;
            float top = LadderClimb.HillRockTop;
            double face = LadderClimb.FaceDist(half), foot = LadderClimb.FootDist(half, top);
            Assert.AreEqual(0f, LadderClimb.Profile(foot + 0.01, half, top));
            Assert.AreEqual(top, LadderClimb.Profile(0, half, top));
            Assert.AreEqual(top, LadderClimb.Profile(-face, half, top), 1e-4);
            Assert.AreEqual(top * 0.5f, LadderClimb.Profile((face + foot) * 0.5, half, top), 1e-3);
            float last = top;
            for (double u = 0; u <= foot + 0.2; u += 0.05)
            {
                float h = LadderClimb.Profile(u, half, top);
                Assert.That(h, Is.LessThanOrEqualTo(last + 1e-5f), "never rises walking away from the wall");
                last = h;
            }
        }

        static void Put(World w, int j, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Alive = true; a.Hp = a.MaxHp; a.SpawnProtect = 0; a.StandUp = 0; a.AirLen = 0; a.SlowTimer = 0; a.RespawnIn = 0;
            w.X[j] = x; w.Z[j] = z; w.Vx[j] = 0; w.Vz[j] = 0;
        }

        static World Skybreak()
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 3, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Builder, Config = GameConfig.CreateBrowser5v5() });
            for (int j = 1; j < w.ActorCount; j++) { w.Actors[j].Alive = false; w.Actors[j].RespawnIn = 1e9; }
            w.Builder.Spent[0] = -100;
            return w;
        }

        static int Build(World w, double bx, double bz, int dx, int dz)
        {
            Put(w, 0, bx, bz);
            w.Actors[0].AbilityCd = 0;
            w.WallChangeTick = World.NeverTick;
            return BuiltPieces.Build(w, 0, bx + dx * 5, bz + dz * 5);
        }

        [Test]
        public void FighterCrossingTheHillLadderReachesTheRockTopAndLandsOnTheFarSide()
        {
            World w = Skybreak();
            // the north-west hill rock: from outside the ring at (46, 34) a ladder south over (46, 35) and (46, 36) into the ring
            Assert.AreEqual(BuiltPieces.BuiltLadderCode, Build(w, 46.5, 34.5, 0, 1), "ladder over the hill rock");
            BuiltPiece p = null;
            foreach (BuiltPiece q in w.Builder.Pieces) if (q.Active) p = q;
            float top = LadderClimb.RockTop(w.Map, p.X, p.Z);
            Assert.AreEqual(LadderClimb.HillRockTop, top, "the hill ring is the tall rock");
            Put(w, 0, 46.5, 33.4);
            double dt = SimStep.Dt(w.Config);
            float maxLift = 0f, maxStep = 0f, last = 0f, onWallMin = float.MaxValue;
            int ticks = 0;
            for (; ticks < 120 && w.Z[0] < 38.2; ticks++)
            {
                for (int k = 0; k < w.ActorCount; k++) { w.Inputs[k].MoveX = 0; w.Inputs[k].MoveZ = 0; }
                w.Inputs[0].MoveZ = 1;
                Movement.MovementSystem(w, dt);
                float h = LadderClimb.Lift(w, w.X[0], w.Z[0]);
                int cell = (int)Math.Floor(w.Z[0]) * w.Map.Width + (int)Math.Floor(w.X[0]);
                for (int c = 0; c < p.Count; c++) if (p.Cells[c] == cell) onWallMin = Mathf.Min(onWallMin, h);
                maxLift = Mathf.Max(maxLift, h);
                maxStep = Mathf.Max(maxStep, Mathf.Abs(h - last));
                last = h;
            }
            Debug.Log($"[ladder fix] hill ladder crossed in {ticks} ticks to z {w.Z[0]:F2}: max lift {maxLift:F2} (rock top {top}), lowest lift over the wall cells {onWallMin:F2}, biggest per-tick change {maxStep:F2}, lift at the landing {last:F2}");
            Assert.That(w.Z[0], Is.GreaterThanOrEqualTo(38.2), "walked over onto the hill side");
            Assert.AreEqual(top, maxLift, 1e-3, "lifted to the rock top");
            Assert.AreEqual(top, onWallMin, 1e-3, "on the rock top the whole way over the wall cells (never inside the rock)");
            Assert.AreEqual(0f, last, 1e-4, "set down on the far side");
            Assert.That(maxStep, Is.LessThan(top * 0.5f), "a climb, not a teleport");
        }

        [Test]
        public void PointlessLadderAtAWallEndIsNotAShortcutAndTheRingCrossingIs()
        {
            World w = Skybreak();
            Put(w, 0, 46.5, 34.5);
            Assert.AreEqual(0, BuiltPieces.Plan(w, 0, 0, 1, BuiltPieces.Ladder));
            bool cross = LadderClimb.LastPlanIsShortcut(w, 0, 0, 1);
            Put(w, 0, 45.5, 39.5);   // in the west gap of the ring, facing north along the ring's end
            Assert.AreEqual(0, BuiltPieces.Plan(w, 0, 0, -1, BuiltPieces.Ladder));
            bool end = LadderClimb.LastPlanIsShortcut(w, 0, 0, -1);
            Debug.Log($"[ladder fix] shortcut: ring crossing {cross}, ring end {end}");
            Assert.IsTrue(cross, "over the ring into the hill is a shortcut");
            Assert.IsFalse(end, "round the end of the wall is a step, not a shortcut");
        }

        [Test]
        public void RopeBridgeOnSkybreakIsWalkedEndToEndWithoutFalling()
        {
            World w = Skybreak();
            MapDef map = w.Map;
            int[] dxs = { 1, -1, 0, 0 }, dzs = { 0, 0, 1, -1 };
            int tried = 0, walked = 0;
            var bad = new List<string>();
            for (int cz = 1; cz < map.Height - 1 && tried < 6; cz++)
            for (int cx = 1; cx < map.Width - 1 && tried < 6; cx++)
            {
                if (!MapGrid.IsWalkable(map, cx, cz) || map.Cells[cz * map.Width + cx] == Cell.Bridge) continue;
                for (int d = 0; d < 4 && tried < 6; d++)
                {
                    Put(w, 0, cx + 0.5, cz + 0.5);
                    if (!Collide.ActorFits(w, cx + 0.5, cz + 0.5, Collide.ActorRadius(w, 0))) continue;
                    if (BuiltPieces.Plan(w, 0, dxs[d], dzs[d], BuiltPieces.Bridge) != 0 || BuiltPieces.LastPlanCount < 4) continue;
                    World t = Skybreak();
                    if (Build(t, cx + 0.5, cz + 0.5, dxs[d], dzs[d]) != BuiltPieces.BuiltBridgeCode) continue;
                    BuiltPieces.LastPlanLanding(t, 0, dxs[d], dzs[d], out double lx, out double lz, out int cells);
                    tried++;
                    double dt = SimStep.Dt(t.Config);
                    bool fell = false;
                    for (int k = 0; k < 40 * cells && !fell; k++)
                    {
                        double gx = lx - t.X[0], gz = lz - t.Z[0];
                        if (gx * gx + gz * gz < 0.09) break;
                        for (int j = 0; j < t.ActorCount; j++) { t.Inputs[j].MoveX = 0; t.Inputs[j].MoveZ = 0; }
                        t.Inputs[0].MoveX = dxs[d];
                        t.Inputs[0].MoveZ = dzs[d];
                        Movement.MovementSystem(t, dt);
                        if (!MapGrid.IsWalkable(t.Map, (int)Math.Floor(t.X[0]), (int)Math.Floor(t.Z[0]))) fell = true;
                    }
                    double ex = lx - t.X[0], ez = lz - t.Z[0];
                    if (!fell && ex * ex + ez * ez < 0.36) walked++;
                    else bad.Add($"({cx},{cz}) dir ({dxs[d]},{dzs[d]}) {cells} cells: ended ({t.X[0]:F2},{t.Z[0]:F2}) fell {fell}");
                }
            }
            Debug.Log($"[ladder fix] rope bridges: {walked} of {tried} walked end to end. " + string.Join("; ", bad));
            Assert.GreaterOrEqual(tried, 1, "a rope bridge can be built on Skybreak");
            Assert.IsEmpty(bad, "every bridge walked end to end without leaving walkable ground");
        }
    }
}
