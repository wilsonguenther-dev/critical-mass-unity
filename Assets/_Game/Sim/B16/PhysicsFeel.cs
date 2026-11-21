// b16 physics fixes (physics audit 2026-11-21). Every number lives in Sim/Config/PhysicsFeelConfig.cs.
using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// b16: the physics audit's sim fixes, Full rules only (On: PhysicsFeelConfig.Enabled, DepthConfig.Classic false,
    /// PhysicsConfig.Classic false), so the Classic and PhysicsOnly rows never move. The rules themselves sit where they act
    /// (Collide, Kaiju, Demolitions, Movement, BotSteer); this class holds the switch, the small shared helpers and the
    /// Kaiju's blast slide. Allocation-free after the first tick (scratch arrays grow once).
    /// </summary>
    public static class PhysicsFeel
    {
        public static bool On(GameConfig c) => c.Depth.PhysicsFeel != null && c.Depth.PhysicsFeel.Enabled && !c.Depth.Classic && !c.Physics.Classic;

        public static bool On(World w) => On(w.Config);

        public static PhysicsFeelConfig Cfg(World w) => w.Config.Depth.PhysicsFeel;

        /// <summary>[2] blasts shove under their own switch.</summary>
        public static bool BlastOn(World w) => On(w) && Cfg(w).BlastShove;

        // ------------------------------------------------------------------------------------------------ [8] Kaiju turn rate

        /// <summary>The Kaiju's largest facing change in one tick (radians): KaijuTurnDegPerSec at the match pace.</summary>
        public static double KaijuTurnStep(World w)
        {
            double ticksPerRealSecond = w.Config.SimHz * (w.Dur.Pace > 0 ? w.Dur.Pace : 1);
            return Cfg(w).KaijuTurnDegPerSec * (Math.PI / 180.0) / ticksPerRealSecond;
        }

        /// <summary>a - b wrapped into (-pi, pi].</summary>
        public static double AngleDelta(double a, double b)
        {
            double d = a - b;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d <= -Math.PI) d += 2 * Math.PI;
            return d;
        }

        /// <summary>From facing f0 toward f1 by at most max radians; the result stays in (-pi, pi].</summary>
        public static double CapTurn(double f0, double f1, double max)
        {
            double d = AngleDelta(f1, f0);
            if (Math.Abs(d) <= max) return f1;
            double r = f0 + (d > 0 ? max : -max);
            if (r > Math.PI) r -= 2 * Math.PI;
            else if (r <= -Math.PI) r += 2 * Math.PI;
            return r;
        }

        // ------------------------------------------------------------------------------------------------ [2] blasts

        /// <summary>Stagger ticks at the blast centre (BlastStunSeconds at the match pace).</summary>
        public static int BlastStunTicks(World w) => Durations.RealSeconds(w.Config, Cfg(w).BlastStunSeconds, w.Dur.Pace);

        /// <summary>
        /// [2] The knock distance (u, mass 1) to hand Damage.ApplyHit / Knock.Effective for a body of class cls at blast share
        /// u (0 centre, 1 edge): Effective divides by mass, so this is centre x (1 - u) x sqrt(mass) and the body travels
        /// centre x (1 - u) / sqrt(mass).
        /// </summary>
        public static double BlastKnock(World w, ClassId cls, double u)
        {
            double mass = Math.Max(0.1, w.Config.Class(cls).Mass);
            double share = 1 - Math.Min(1, Math.Max(0, u));
            return Cfg(w).BlastShoveCentre * share * Math.Sqrt(mass);
        }

        /// <summary>[2] Stagger ticks at blast share u (0 centre, 1 edge).</summary>
        public static int BlastStun(World w, double u)
        {
            double share = 1 - Math.Min(1, Math.Max(0, u));
            return (int)Math.Floor(BlastStunTicks(w) * share + 0.5);
        }

        /// <summary>[2] A TNT blast at (x, z) whose Kaiju reach is kr gives the hatched Kaiju a small slide away from it.</summary>
        public static void BlastKaiju(World w, double x, double z, double kr)
        {
            Kaiju k = w.Kaiju;
            if (!BlastOn(w) || !w.Match.Hatched || k.State == KaijuState.Egg || kr <= 0) return;
            double dx = k.X - x, dz = k.Z - z;
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d > kr) return;
            double ux = d > 1e-6 ? dx / d : 1, uz = d > 1e-6 ? dz / d : 0;
            double dist = Cfg(w).BlastKaijuShove * (1 - d / kr);
            if (dist <= 0) return;
            double dt = 1.0 / w.Config.SimHz;
            double v0 = dist * (1 - KaijuDecay(w)) / dt;
            k.PushVx = JsMath.F32(k.PushVx + ux * v0);
            k.PushVz = JsMath.F32(k.PushVz + uz * v0);
        }

        /// <summary>The Kaiju's blast slide speed kept per tick (KaijuPushDecay re-derived for SimHz, like Knock.DecayPerTick).</summary>
        static double KaijuDecay(World w)
        {
            double e = Durations.ReferenceHz / w.Config.SimHz;
            double d = Cfg(w).KaijuPushDecay;
            return e == 1.0 ? d : Math.Pow(d, e);
        }

        /// <summary>[2] One tick of the Kaiju's blast slide, through its wall collider (Kaiju.ShoveKaiju), then decay.</summary>
        public static void KaijuPushStep(World w, double dt)
        {
            Kaiju k = w.Kaiju;
            if (k.PushVx == 0 && k.PushVz == 0) return;
            if (!On(w) || !w.Match.Hatched || k.State == KaijuState.Egg)
            {
                k.PushVx = 0;
                k.PushVz = 0;
                return;
            }
            Kaiju.ShoveKaiju(w, k.PushVx * dt, k.PushVz * dt);
            double f = KaijuDecay(w);
            double vx = k.PushVx * f, vz = k.PushVz * f;
            if (vx * vx + vz * vz < 0.05 * 0.05)
            {
                vx = 0;
                vz = 0;
            }
            k.PushVx = JsMath.F32(vx);
            k.PushVz = JsMath.F32(vz);
        }

        // ------------------------------------------------------------------------------------------------ [5] one-cell gaps

        /// <summary>1 when cell (cx, cz) is a walkable one-cell passage along x (blocked at z - 1 and z + 1), 2 along z, else 0.</summary>
        public static byte GapAt(MapDef map, int cx, int cz)
        {
            if (!MapGrid.IsWalkable(map, cx, cz)) return 0;
            if (!MapGrid.IsWalkable(map, cx, cz - 1) && !MapGrid.IsWalkable(map, cx, cz + 1)) return 1;
            if (!MapGrid.IsWalkable(map, cx - 1, cz) && !MapGrid.IsWalkable(map, cx + 1, cz)) return 2;
            return 0;
        }

        /// <summary>
        /// [5] Actor i's one-cell passage axis (1 along x, 2 along z, 0 none): its own cell, else the next cell along the
        /// larger part of its velocity (the mouth it is walking into).
        /// </summary>
        public static byte GapOf(World w, int i)
        {
            MapDef map = w.Map;
            int cx = JsMath.FloorToInt(w.X[i]), cz = JsMath.FloorToInt(w.Z[i]);
            byte g = GapAt(map, cx, cz);
            if (g != 0) return g;
            double vx = w.Vx[i], vz = w.Vz[i];
            double ax = Math.Abs(vx), az = Math.Abs(vz);
            if (ax < 0.1 && az < 0.1) return 0;
            if (ax >= az) return GapAt(map, cx + (vx > 0 ? 1 : -1), cz);
            return GapAt(map, cx, cz + (vz > 0 ? 1 : -1));
        }

        /// <summary>[6] The share of a teammate pair's push at overlap o (u): AllyPushShare, ramping to 1 from AllyPushRampFrom to AllyPushFullAt.</summary>
        public static double AllyShare(World w, double o, double baseShare)
        {
            PhysicsFeelConfig c = Cfg(w);
            if (o <= c.AllyPushRampFrom) return baseShare;
            if (o >= c.AllyPushFullAt || c.AllyPushFullAt <= c.AllyPushRampFrom) return 1;
            double t = (o - c.AllyPushRampFrom) / (c.AllyPushFullAt - c.AllyPushRampFrom);
            return baseShare + (1 - baseShare) * t;
        }

        // ------------------------------------------------------------------------------------------------ [15] cell health

        /// <summary>
        /// [15] The health of a blocking cell: a breakable wall's MapDef.WallHp; a LowWall's or Barricade's health from its
        /// Barricade record (built walls and crates keep their HP there, MapDef.WallHp is breakables only and reads 0 for
        /// them); 0 for anything else or a cell with no live record. Read only: tests, tools and the view.
        /// </summary>
        public static double CellHp(World w, int cx, int cz)
        {
            MapDef map = w.Map;
            if (!MapGrid.InBounds(map, cx, cz)) return 0;
            int i = cz * map.Width + cx;
            byte t = map.Cells[i];
            if (t == Cell.BreakableIntact || t == Cell.BreakableCracked) return map.WallHp != null ? map.WallHp[i] : 0;
            if (t != Cell.LowWall && t != Cell.Barricade) return 0;
            Barricade[] list = w.Barricades;
            if (list == null) return 0;
            for (int s = 0; s < list.Length; s++)
            {
                Barricade b = list[s];
                if (b != null && b.Active && b.Cx == cx && b.Cz == cz) return Math.Max(0, b.Hp);
            }
            return 0;
        }
    }
}
