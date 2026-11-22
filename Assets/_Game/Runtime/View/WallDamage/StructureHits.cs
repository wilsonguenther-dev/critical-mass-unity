using System;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.WallDamage
{
    /// <summary>
    /// px lane C (2026-11-22): the sim's StructureHit events (lane S2: an enemy barricade, a built wall, a breakable cell or a
    /// ladder / bridge piece took a swing or a blast) as hit pulses for the wall views: the wall shake, the crack step and
    /// the knock sound key off the EVENT, so barricade and built-wall damage no longer depends on HP polling once the event
    /// flows (DamageHits.Latest prefers these; the polled pulses stay the fallback for a build without the event).
    /// StructureSolid (map rock, the hill ring, own walls: Value 0, cannot be broken) gets FX only (FxScene), no pulse.
    /// Fixed ring, no allocation; written by CombatFx on decode, read by the structure views each frame.
    /// </summary>
    public static class StructureHits
    {
        public const int Capacity = 32;
        static readonly ReactPulse[] Ring = new ReactPulse[Capacity];
        static int _n;
        static bool _seen;

        /// <summary>True once any StructureHit event arrived this world (the polled hit pulses then stand down).</summary>
        public static bool EventsSeen => _seen;
        /// <summary>Pulses pushed since the last Clear (tests).</summary>
        public static int Pushed => _n;

        /// <summary>Pulse strength 0.35..1 from the damage (a Warrior's 24 on a 60 HP short wall: 0.75).</summary>
        public static double Strength(double damage) => Math.Max(0.35, Math.Min(1.0, 0.35 + damage / 60.0));

        public static ReactKind KindOf(int structureKind) =>
            structureKind == PlayerReasons.StructureBreakable ? ReactKind.Wall
            : structureKind == PlayerReasons.StructurePiece ? ReactKind.Piece
            : ReactKind.Barricade;   // barricades and Builder walls are both drawn by BarricadeDamage

        public static void Push(ref FxEvent e)
        {
            _seen = true;
            if (e.B == PlayerReasons.StructureSolid || e.Value <= 0) return;
            ref ReactPulse p = ref Ring[_n++ % Capacity];
            p.Kind = KindOf(e.B);
            p.Phase = (byte)(e.Extra <= 1e-6 ? 1 : 0);
            p.X = e.X;
            p.Z = e.Z;
            p.DirX = e.DirX;
            p.DirZ = e.DirZ;
            p.Strength = Strength(e.Value);
            p.T0 = Time.unscaledTimeAsDouble;
            p.Size = 0.5;
            p.Seed = e.Tick * 31 + e.A;
        }

        /// <summary>The newest event pulse of kind/phase within radius of (x, z), younger than maxAge (s).</summary>
        public static bool Latest(ReactKind kind, byte phase, double x, double z, double radius, double now, double maxAge, out ReactPulse best)
        {
            best = default;
            bool found = false;
            double r2 = radius * radius;
            int count = Math.Min(_n, Capacity);
            for (int i = 0; i < count; i++)
            {
                ReactPulse p = Ring[i];
                if (p.Kind != kind || p.Phase != phase) continue;
                double age = now - p.T0;
                if (!(age >= 0 && age < maxAge)) continue;
                double dx = p.X - x, dz = p.Z - z;
                if (dx * dx + dz * dz > r2) continue;
                if (!found || p.T0 > best.T0) { best = p; found = true; }
            }
            return found;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Clear()
        {
            for (int i = 0; i < Capacity; i++) Ring[i] = default;
            _n = 0;
            _seen = false;
        }
    }
}
