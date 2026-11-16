using System;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.Affordance
{
    /// <summary>
    /// b12-jk (Jeisil's j and k: "grenades that read as grenades" and "clear signs"): EVERY number of the grenade read and the
    /// can-dig / can-build / can-take-down signs, in ONE place. View only: the throw range, blast radius, fuse and flight are
    /// never copied here, they are read from GameConfig.Abilities.DemolitionsTnt and the live Bomb slots each frame.
    /// </summary>
    public static class GrenadeTuning
    {
        /// <summary>The on-screen name of the Demolitions throwable. TNT IS the grenade; to call it GRENADE change this one line.</summary>
        public const string Name = DemolitionsNames.Tnt;

        // ------------------------------------------------------------------ aim arc
        /// <summary>Points on the aim arc (hand to landing), pooled once.</summary>
        public const int ArcPoints = 16;
        /// <summary>Height of the arc's first point above the ground (the hand) and of its last (just over the landing ring).</summary>
        public const float HandLift = 1.05f, LandLift = 0.06f;
        /// <summary>Half width of the arc ribbon (world units).</summary>
        public const float ArcHalfWidth = 0.05f;
        /// <summary>Arc opacity when the throw is ready, and when it is not (cooldown, getting up).</summary>
        public const float ArcAlphaReady = 0.9f, ArcAlphaWait = 0.4f;
        /// <summary>Marching speed of the arc's bright dashes (segments per second; 0 under Reduce motion).</summary>
        public const float ArcMarch = 6f;

        // ------------------------------------------------------------------ rings
        /// <summary>The atlas ring (CellRing) is drawn at 28 of a cell's 32 px half size: quad half = radius x this.</summary>
        public const float RingQuadScale = 32f / 28f;
        /// <summary>The fuse ring's blink period at the throw and at the blast (seconds; it shortens linearly with the fuse).</summary>
        public const float BlinkSlowSeconds = 0.8f, BlinkFastSeconds = 0.1f;
        /// <summary>Fuse ring opacity on and off the beat (steady at the mean under Reduce flashing).</summary>
        public const float BlinkOnAlpha = 0.95f, BlinkOffAlpha = 0.28f;
        /// <summary>The warning "!" over the ring centre: height above the ground and size (world units).</summary>
        public const float BangLift = 1.25f, BangSize = 0.26f;
        /// <summary>Show the warning to the thrower's own team too (TNT does not hurt them in the sim, so off).</summary>
        public const bool WarnOwnTeam = false;
        /// <summary>The owner team's small mine ring (radius, world units) and its opacity.</summary>
        public const float MineRingRadius = 0.42f, MineRingAlpha = 0.8f;
        /// <summary>Live fuse rings drawn at most (a 32-player fight keeps its quad budget).</summary>
        public const int MaxLiveRings = 8, MaxMineRings = 16;

        // ------------------------------------------------------------------ signs
        /// <summary>Signs show within SignRadius of the player, fully opaque inside SignFadeStart, fading to nothing at SignRadius.</summary>
        public const double SignRadius = 14, SignFadeStart = 9;
        /// <summary>Signs never sit on the player's own cell (world units; the next cell is 1 u away).</summary>
        public const double SignMinDistance = 0.9;
        /// <summary>Signs per kind (pick, hammer, crack), nearest first.</summary>
        public const int SignsPerKind = 8;
        /// <summary>Sign height above its object's top, size (half, world units) and opacity at full.</summary>
        public const float SignLift = 0.55f, SignSize = 0.3f, SignAlpha = 0.92f;
        /// <summary>New dig-route proofs the sign pass may run per frame (the marker scan's cache is shared).</summary>
        public const int SignRouteBudget = 2;
    }

    /// <summary>Pure grenade-read math (read only on the Sim; the tests call these directly).</summary>
    public static class GrenadeRead
    {
        /// <summary>Where a throw from (ox, oz) at aim (aimX, aimZ) lands: the Sim's own clamp (Demolitions.ThrowTnt) with the config's ThrowRange.</summary>
        public static void Landing(double ox, double oz, double aimX, double aimZ, double facing, DemolitionsTntConfig cfg, out double x, out double z)
        {
            Abilities.ClampToRange(ox, oz, aimX, aimZ, facing, cfg.ThrowRange, out x, out z);
        }

        /// <summary>The landing / fuse ring's radius: the blast radius, read from config.</summary>
        public static float RingRadius(DemolitionsTntConfig cfg) => (float)cfg.BlastRadius;

        /// <summary>Half size of the ring quad so the drawn ring sits exactly on radius r.</summary>
        public static float RingQuadHalf(float r) => r * GrenadeTuning.RingQuadScale;

        /// <summary>Point i of n on the arc from (ox, oz) to (lx, lz): ground position and height above the ground (TntArc's flight curve plus the hand lift).</summary>
        public static void ArcPoint(int i, int n, double ox, double oz, double lx, double lz, out double x, out double z, out float lift)
        {
            float p = n > 1 ? Mathf.Clamp01(i / (float)(n - 1)) : 1f;
            x = ox + (lx - ox) * p;
            z = oz + (lz - oz) * p;
            float dist = (float)Math.Sqrt((lx - ox) * (lx - ox) + (lz - oz) * (lz - oz));
            lift = TntArc.Height(p, dist) + Mathf.Lerp(GrenadeTuning.HandLift, GrenadeTuning.LandLift, p);
        }

        /// <summary>Fuse left 0..1 from the throw (1) to the blast (0), at a sim time in ticks.</summary>
        public static float FuseLeft01(double nowTicks, int thrownTick, int blastTick)
        {
            double span = blastTick - thrownTick;
            if (span <= 0) return 0f;
            double f = (blastTick - nowTicks) / span;
            return f <= 0 ? 0f : f >= 1 ? 1f : (float)f;
        }

        /// <summary>The blink period at fuse left f (1 = just thrown: slow; 0 = blast: fast). Strictly increasing in f.</summary>
        public static float BlinkPeriod(float fuseLeft01) =>
            GrenadeTuning.BlinkFastSeconds + (GrenadeTuning.BlinkSlowSeconds - GrenadeTuning.BlinkFastSeconds) * Mathf.Clamp01(fuseLeft01);

        /// <summary>
        /// Blinks since the throw, for a fuse of totalSeconds with secondsLeft to go: the integral of 1 / BlinkPeriod over the
        /// time gone, in closed form (no per-bomb state, the same on every frame rate and after a pause).
        /// </summary>
        public static double BlinkPhase(double secondsLeft, double totalSeconds)
        {
            if (!(totalSeconds > 0)) return 0;
            double s = Math.Max(0, Math.Min(totalSeconds, secondsLeft));
            double slow = GrenadeTuning.BlinkSlowSeconds, fast = GrenadeTuning.BlinkFastSeconds;
            double k = (slow - fast) / totalSeconds;
            double p = fast + k * s;
            if (k < 1e-9) return (totalSeconds - s) / slow;
            return Math.Log(slow / p) / k;
        }

        /// <summary>The ring is on the bright half of its beat now.</summary>
        public static bool BlinkOn(double secondsLeft, double totalSeconds)
        {
            double ph = BlinkPhase(secondsLeft, totalSeconds);
            return ph - Math.Floor(ph) < 0.5;
        }

        /// <summary>An actor of radius actorRadius at (px, pz) is inside a blast of radius blast at (bx, bz): the Sim's rule (Demolitions.BlastActors).</summary>
        public static bool Inside(double px, double pz, double actorRadius, double bx, double bz, double blast)
        {
            double r = blast + actorRadius;
            return (px - bx) * (px - bx) + (pz - bz) * (pz - bz) <= r * r;
        }

        /// <summary>The viewer sees the warning colour and the "!" : inside the ring of a bomb that can hurt him (an enemy's).</summary>
        public static bool Warns(TeamId viewer, TeamId bombTeam, bool inside) => inside && (viewer != bombTeam || GrenadeTuning.WarnOwnTeam);

        /// <summary>The small mine ring: the owner's team only (enemies must never see a mine marked; MineSight's rule).</summary>
        public static bool MineRingVisible(TeamId mineTeam, TeamId viewer) => MineSight.OnMap(mineTeam, viewer);

        /// <summary>Mine rings the viewer's team would see right now (the view draws exactly these).</summary>
        public static int MineRingsFor(World w, TeamId viewer)
        {
            Mine[] ms = w != null && w.Demolitions != null ? w.Demolitions.Mines : null;
            if (ms == null) return 0;
            int n = 0;
            for (int k = 0; k < ms.Length; k++) if (ms[k].Active && MineRingVisible(ms[k].Team, viewer)) n++;
            return n;
        }
    }

    /// <summary>The three sign kinds: the Miner's pick (can dig / can barricade), the Builder's hammer (can build), everyone's crack (can take down).</summary>
    public enum SignKind : byte { Pick = 0, Hammer = 1, Crack = 2 }

    /// <summary>One sign: kind, the place it marks (cell index or -1), where it floats and how far it is.</summary>
    public struct AffSign
    {
        public SignKind Kind;
        public int Cell;
        public double X, Z;
        /// <summary>Height of the object's top above the ground (the sign floats SignLift over it).</summary>
        public float Top;
        public double Dist;
    }

    /// <summary>
    /// b12-jk signs: for the local player's class, the NEAREST SignsPerKind of each kind within SignRadius (fading from
    /// SignFadeStart), decided by the Sim's own rules: pick on the cells a Miner can dig (Terraform's dig rule with the route
    /// proof from AffordanceScan's cache) and on the spot his barricade would go; hammer on the ladders and bridges a Builder
    /// can build from where he stands (BuiltPieces.BuildRefusal); crack, for EVERY class, on what that class can take down:
    /// enemy barricades (every swing chips them, TNT blasts them), breakable walls (the Miner's pick, the Demolitions TNT) and
    /// enemy ladders and bridges (a Miner digs or a TNT crater carves a bridge cell). Own-class build signs only. Read only;
    /// allocation-free.
    /// </summary>
    public sealed class SignScan
    {
        public const int Kinds = 3;
        public readonly AffSign[] Signs = new AffSign[Kinds * GrenadeTuning.SignsPerKind];
        /// <summary>Signs kept per kind (at most SignsPerKind) and candidates seen per kind (before the nearest-N cut).</summary>
        public readonly int[] Count = new int[Kinds];
        public readonly int[] Candidates = new int[Kinds];

        public AffSign Get(SignKind k, int i) => Signs[(int)k * GrenadeTuning.SignsPerKind + i];

        /// <summary>The class's own build sign: pick for the Miner, hammer for the Builder.</summary>
        public static bool BuildSign(ClassId c, SignKind k) => k == SignKind.Pick ? c == ClassId.Miner : k == SignKind.Hammer && c == ClassId.Builder;

        /// <summary>Can class c take down a breakable wall? (the Miner's pick, Abilities.MinerWallBreak; the TNT blast).</summary>
        public static bool TakesDownWall(ClassId c) => c == ClassId.Miner || c == ClassId.Demolitions;

        /// <summary>Can class c take down an enemy barricade? (every swing chips it, Combat; the TNT blast).</summary>
        public static bool TakesDownBarricade(ClassId c) => true;

        /// <summary>Can class c take down an enemy ladder or bridge? (a dug or cratered bridge cell breaks it, BuiltPieces.PiecesSystem).</summary>
        public static bool TakesDownPiece(ClassId c) => c == ClassId.Miner || c == ClassId.Demolitions;

        /// <summary>Opacity factor for a sign d units away: 1 inside SignFadeStart, 0 at SignRadius and past it.</summary>
        public static float Fade(double d)
        {
            if (d >= GrenadeTuning.SignRadius) return 0f;
            if (d <= GrenadeTuning.SignFadeStart) return 1f;
            return (float)(1 - (d - GrenadeTuning.SignFadeStart) / (GrenadeTuning.SignRadius - GrenadeTuning.SignFadeStart));
        }

        public void Clear()
        {
            for (int k = 0; k < Kinds; k++) { Count[k] = 0; Candidates[k] = 0; }
        }

        /// <summary>Keeps the nearest SignsPerKind of kind k (insertion into a short sorted list; no allocation).</summary>
        void Offer(SignKind kind, int cell, double x, double z, float top, double d)
        {
            if (d > GrenadeTuning.SignRadius || d < GrenadeTuning.SignMinDistance) return;
            int k = (int)kind, n = Count[k], b = k * GrenadeTuning.SignsPerKind;
            Candidates[k]++;
            for (int q = 0; q < n; q++) if (cell >= 0 && Signs[b + q].Cell == cell) return;
            if (n == GrenadeTuning.SignsPerKind && d >= Signs[b + n - 1].Dist) return;
            int at = n < GrenadeTuning.SignsPerKind ? n : n - 1;
            while (at > 0 && Signs[b + at - 1].Dist > d)
            {
                Signs[b + at] = Signs[b + at - 1];
                at--;
            }
            Signs[b + at] = new AffSign { Kind = kind, Cell = cell, X = x, Z = z, Top = top, Dist = d };
            if (n < GrenadeTuning.SignsPerKind) Count[k] = n + 1;
        }

        /// <summary>Height of a wall cell's top above its ground (GridChunkMeshView.CellBox; as TerrainAffordanceView.WallTop).</summary>
        public static float WallTop(byte t) => t == Cell.BreakableCracked ? 0.77f : t == Cell.BreakableIntact || t == Cell.Wall ? 1.52f : 0f;

        /// <summary>
        /// One pass for actor id. aff = the marker scan run this frame for the same actor (its route cache, and for a Miner its
        /// barricade target); may be null for classes without one.
        /// </summary>
        public void Scan(World w, int id, AffordanceScan aff)
        {
            Clear();
            if (w == null || w.Map == null || id < 0 || id >= w.ActorCount) return;
            Actor a = w.Actors[id];
            if (a == null || !a.Alive) return;
            MapDef m = w.Map;
            ClassId cls = a.Cls;
            double ox = w.X[id], oz = w.Z[id];
            double R = GrenadeTuning.SignRadius;
            int x0 = Math.Max(0, JsMath.FloorToInt(ox - R)), x1 = Math.Min(m.Width - 1, JsMath.FloorToInt(ox + R));
            int z0 = Math.Max(0, JsMath.FloorToInt(oz - R)), z1 = Math.Min(m.Height - 1, JsMath.FloorToInt(oz + R));
            bool walls = TakesDownWall(cls), dig = cls == ClassId.Miner && aff != null;
            if (dig) aff.GrantRouteBudget(GrenadeTuning.SignRouteBudget);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    double dx = cx + 0.5 - ox, dz = cz + 0.5 - oz;
                    double d2 = dx * dx + dz * dz;
                    if (d2 > R * R) continue;
                    int i = cz * m.Width + cx;
                    byte t = m.Cells[i];
                    if (walls && (t == Cell.BreakableIntact || t == Cell.BreakableCracked))
                    {
                        Offer(SignKind.Crack, i, cx + 0.5, cz + 0.5, WallTop(t), Math.Sqrt(d2));
                        continue;
                    }
                    if (dig && (t == Cell.Floor || t == Cell.Bridge) && aff.DigProven(w, cx, cz))
                        Offer(SignKind.Pick, i, cx + 0.5, cz + 0.5, 0f, Math.Sqrt(d2));
                }
            }
            // The Miner's barricade spot (the cell one Space press would barricade, when the Sim would accept it).
            if (cls == ClassId.Miner && aff != null && aff.Secondary.Has && aff.Secondary.Action == AffAction.Barricade && aff.Secondary.Valid
                && MapGrid.InBounds(m, aff.Secondary.Cx, aff.Secondary.Cz))
            {
                AffTarget s = aff.Secondary;
                double d = Math.Sqrt((s.X - ox) * (s.X - ox) + (s.Z - oz) * (s.Z - oz));
                Offer(SignKind.Pick, s.Cz * m.Width + s.Cx, s.X, s.Z, 0f, d);
            }
            // The Builder's ladders and bridges he can build from here: one hammer per plan, over its middle cell.
            if (cls == ClassId.Builder && w.Builder != null && w.Builder.CarryPiece[id] < 0)
            {
                for (int k = 0; k < 4; k++)
                {
                    int ddx = k == 0 ? 1 : k == 1 ? -1 : 0, ddz = k == 2 ? 1 : k == 3 ? -1 : 0;
                    if (BuiltPieces.BuildRefusal(w, id, ddx, ddz) != 0) continue;
                    int n = BuiltPieces.LastPlanCount;
                    if (n <= 0) continue;
                    int c = BuiltPieces.LastPlanCell(n / 2);
                    double px = c % m.Width + 0.5, pz = c / m.Width + 0.5;
                    Offer(SignKind.Hammer, c, px, pz, 0.1f, Math.Sqrt((px - ox) * (px - ox) + (pz - oz) * (pz - oz)));
                }
            }
            // Take-down signs on enemy barricades (every class) and enemy ladders and bridges (Miner, Demolitions).
            Barricade[] bs = w.Barricades;
            if (bs != null && TakesDownBarricade(cls))
            {
                for (int k = 0; k < bs.Length; k++)
                {
                    Barricade b = bs[k];
                    if (b == null || !b.Active || b.Team == a.Team || !MapGrid.InBounds(m, b.Cx, b.Cz)) continue;
                    double bx = b.Cx + 0.5, bz = b.Cz + 0.5;
                    Offer(SignKind.Crack, b.Cz * m.Width + b.Cx, bx, bz, 1.2f, Math.Sqrt((bx - ox) * (bx - ox) + (bz - oz) * (bz - oz)));
                }
            }
            BuiltPiece[] ps = w.Builder != null ? w.Builder.Pieces : null;
            if (ps != null && TakesDownPiece(cls))
            {
                for (int k = 0; k < ps.Length; k++)
                {
                    BuiltPiece p = ps[k];
                    if (p == null || !p.Active || p.Carried || p.Broken || p.Team == a.Team) continue;
                    Offer(SignKind.Crack, -1 - k, p.X, p.Z, 0.35f, Math.Sqrt((p.X - ox) * (p.X - ox) + (p.Z - oz) * (p.Z - oz)));
                }
            }
        }
    }
}
