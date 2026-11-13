using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>How a force spreads (contract C1, physics build list 2026-11-12).</summary>
    public enum ForceShape : byte { Point, Radial, Cone, Footfall, Landing, Break }

    /// <summary>What made a force (contract C1; Topple and Trample added by the sim lane).</summary>
    public enum ForceSource : byte
    {
        HitLight, HitHeavy, HitCritical, Death, Block, KaijuMelee, KaijuStep, KaijuSlam, Explosion, WallCrack, WallBreak,
        BarricadeBreak, Land, Splash, Hatch, Topple, Trample,
    }

    /// <summary>The surface under a force (per-cell surface byte; Grass until DG-105 bakes the real one).</summary>
    public enum Surface : byte { Grass, Dirt, Stone, Wood, Water, Void }

    /// <summary>
    /// The single world-force event (contract C1): one struct every reaction listens to (force field, effect pools,
    /// ground paint, audio, camera response, springs, death launch). View side only: built from decoded sim events, never
    /// written back. Positions are SIM coordinates (x east, z south) plus the surface height in Y.
    /// </summary>
    public struct WorldForce
    {
        /// <summary>Exact sim tick and the order inside it (replay-identical).</summary>
        public int Tick, Seq;
        public float X, Y, Z;
        /// <summary>Unit push direction in sim space; (0, 0) = radial.</summary>
        public float DirX, DirZ;
        /// <summary>World units the push reaches.</summary>
        public float Radius;
        /// <summary>0..1 push for world reactions (ForceTable only).</summary>
        public float Strength;
        /// <summary>0..1 felt intensity before distance falloff (camera response, rumble, bass).</summary>
        public float Shock;
        public ForceShape Shape;
        public ForceSource Source;
        public Surface Surface;
        /// <summary>0 light, 1 heavy, 2 critical or finishing (CombatFeed.TierOf thresholds).</summary>
        public byte Tier;
        /// <summary>Victim or caster; attacker (-2 Kaiju melee, -3 Kaiju slam, -1 none).</summary>
        public int Actor, Other;
        public TeamId Team;
        /// <summary>The sim's knock distance in u (0 until DG-70 adds it to Hit events).</summary>
        public float Impulse;
        /// <summary>Hash(tick, seq, source): deterministic variation shared by every listener.</summary>
        public uint Seed;
    }

    /// <summary>A WorldForce listener. Takes the struct by ref and reads only what it needs; never keeps the ref.</summary>
    public interface IWorldForceListener
    {
        void OnForce(ref WorldForce f);
    }

    /// <summary>
    /// The strength table (contract C1): the only place these numbers live. Strength and radius are the audit's values;
    /// Shock is the audit's where it gives one (Kaiju step 0.35, slam 0.9, explosions the kind's trauma peak) and this
    /// table's own choice elsewhere (marked "WR" for tuning).
    /// </summary>
    public static class ForceTable
    {
        /// <summary>Strength, radius (u) and shock of a source. size: slam radius, explosion radius, splash size or topple length.</summary>
        public static void Of(ForceSource s, float size, ExplosionKind kind, out float strength, out float radius, out float shock)
        {
            switch (s)
            {
                case ForceSource.HitLight: strength = 0.25f; radius = 0.9f; shock = 0.10f; return;            // shock WR
                case ForceSource.HitHeavy: strength = 0.5f; radius = 1.4f; shock = 0.20f; return;             // shock WR
                case ForceSource.HitCritical: strength = 0.75f; radius = 1.9f; shock = 0.30f; return;         // shock WR
                case ForceSource.Death: strength = 0.75f; radius = 1.9f; shock = 0.30f; return;               // as critical
                case ForceSource.Block: strength = 0.2f; radius = 0.8f; shock = 0.15f; return;                // WR
                case ForceSource.KaijuMelee: strength = 0.8f; radius = 2.0f; shock = 0.5f; return;            // shock WR
                case ForceSource.Land: strength = 0.4f; radius = 1.2f; shock = 0.15f; return;                 // shock WR
                case ForceSource.KaijuStep: strength = 0.6f; radius = 3.0f; shock = 0.35f; return;
                case ForceSource.KaijuSlam: strength = 1.0f; radius = (size > 0 ? size : 5f) * 1.15f; shock = 0.9f; return;
                case ForceSource.Explosion:
                {
                    float r = size > 0 ? size : 2f;
                    strength = System.Math.Min(1f, 0.6f + 0.08f * r);
                    radius = 1.7f * r;
                    shock = FxScene.TraumaPeak(kind);
                    return;
                }
                case ForceSource.WallBreak: strength = 0.55f; radius = 2.0f; shock = 0.25f; return;
                case ForceSource.WallCrack: strength = 0.3f; radius = 1.2f; shock = 0.12f; return;            // WR
                case ForceSource.BarricadeBreak: strength = 0.5f; radius = 1.6f; shock = 0.2f; return;        // WR
                case ForceSource.Splash: strength = 0.3f; radius = size > 0 ? size : 1f; shock = 0.1f; return;
                case ForceSource.Hatch: strength = 0.7f; radius = 4.0f; shock = 0.6f; return;                 // WR
                case ForceSource.Topple: strength = 0.7f; radius = size > 0 ? size : 3f; shock = 0.5f; return; // shock WR
                case ForceSource.Trample: strength = 0.3f; radius = 1.5f; shock = 0.2f; return;               // shock WR
                default: strength = 0.25f; radius = 1f; shock = 0f; return;
            }
        }

        /// <summary>Death launch distance along the hit direction by what dealt the blow (audit finding 5).</summary>
        public static float LaunchDistance(byte tier, int other)
        {
            if (other == Damage.KaijuSource) return 3f;   // Kaiju melee
            if (other < 0) return 4f;                     // the slam (radial) or any blast
            return tier == 0 ? 0.4f : tier == 1 ? 1.2f : 2.2f;
        }
    }

    /// <summary>
    /// Where FxScene sends still ground marks (Reactions.GroundPaint implements it): Builder takes world-space effect
    /// geometry, Frame counts paint flushes (a mark leaves the per-frame mesh once Frame has moved past its stamp).
    /// </summary>
    public interface IGroundPaintTarget
    {
        bool Accepts { get; }
        int Frame { get; }
        FxMeshBuilder Builder { get; }
    }
}
