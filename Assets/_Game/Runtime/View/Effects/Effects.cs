using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>What blew up: picks the explosion's layers (fire or dust, debris material, scorch, screen trauma).</summary>
    public enum ExplosionKind : byte
    {
        /// <summary>Full fireball, wood and charred debris, embers, scorch.</summary>
        Tnt,
        /// <summary>Smaller fireball, a dirt geyser (dirt and rock chunks, grit), scorch.</summary>
        Mine,
        /// <summary>Big orange fireball, wooden staves, many embers, scorch.</summary>
        Barrel,
        /// <summary>No fire: stone chunks, a dust column and ring (wall and breakable destroyed).</summary>
        WallBreak,
        /// <summary>No fire: the Kaiju slam shockwave, ground crack, dust ring, rock chunks, dust column, pale pulse.</summary>
        KaijuSlam,
    }

    /// <summary>
    /// The effects system's public static API (view only: nothing here reaches the sim). Effects are stamped with the sim
    /// tick current when you call (World.Tick), so call from code that runs on SimDriverBehaviour.Ticked (an event decode)
    /// to have a replay draw the same frame. Every call returns false when no CombatFx is running (menus, tests without one).
    /// <para>Inside namespace CriticalMass.Runtime.View the name Effects means this namespace: write
    /// <c>Effects.Effects.Explode(...)</c> there, or add <c>using Fx = CriticalMass.Runtime.View.Effects.Effects;</c>.</para>
    /// </summary>
    public static class Effects
    {
        /// <summary>Camera shake setting (PlayerPrefs int, the same key GameSettings writes). Default OFF (OD-6).</summary>
        public const string ShakeKey = CriticalMass.Runtime.Menu.GameSettings.ShakeKey;
        public const string BloodKey = BloodSetting.Key;
        /// <summary>GORE intensity setting (GoreGate is the one switch).</summary>
        public const string GoreKey = CriticalMass.Runtime.Menu.GameSettings.GoreKey;

        /// <summary>The running CombatFx (set in its Start, cleared in OnDestroy).</summary>
        public static CombatFx Active { get; internal set; }

        /// <summary>Camera shake allowed: GameSettings.CameraShake, the one source of truth (default off).</summary>
        public static bool ShakeEnabled => CriticalMass.Runtime.Menu.GameSettings.CameraShake;

        /// <summary>The world offset applied to the gameplay camera for the frame being drawn (zero when shake is off).</summary>
        public static Vector3 ShakeOffset { get; internal set; }

        /// <summary>
        /// A layered toon explosion at a world position (TNT, mines, barrels, wall breaks, the Kaiju slam). radius in world
        /// units (clamped 0.5..6): it scales the fireball, the shock ring, the debris throw, the scorch and the trauma.
        /// Knock-outs within 1.3 x radius in the same or the previous tick are gibbed (GORE Full only).
        /// </summary>
        public static bool Explode(Vector3 position, float radius, ExplosionKind kind)
        {
            SimSpace.ToSim(position, out double x, out double z);
            return Explode(x, z, radius, kind);
        }

        /// <summary>Explode at a sim position (the rules lane's event coordinates).</summary>
        public static bool Explode(double simX, double simZ, double radius, ExplosionKind kind) =>
            Active != null && Active.Inject(FxKind.Explosion, simX, simZ, radius, (int)kind, -1);

        /// <summary>A water splash at a world position (something fell into water or off the rim). size 0.3..3.</summary>
        public static bool Splash(Vector3 position, float size)
        {
            SimSpace.ToSim(position, out double x, out double z);
            return Active != null && Active.Inject(FxKind.Splash, x, z, size, -1, -1);
        }

        /// <summary>Landing puffs (and plank dust on a bridge) under actor `actor`, for the jump view until CM_HAS_JUMP decodes it.</summary>
        public static bool Land(int actor) => Active != null && Active.InjectLand(actor);

        /// <summary>Play the effects gallery at the local player (development builds and the editor only; F6 does the same).</summary>
        public static bool PlayGallery() => Active != null && Active.PlayGallery();

        /// <summary>Weapon blood tint 0..1 for actor i (fades 2.5 s after it last landed a hit; 0 at GORE Off).</summary>
        public static float WeaponBlood(int actor) => Active != null ? Active.WeaponBlood(actor) : 0f;

        /// <summary>Kaiju fist blood tint 0..1 (fades 2.5 s after its last melee hit; 0 at GORE Off).</summary>
        public static float KaijuFistBlood => Active != null ? Active.KaijuFistBlood : 0f;
    }
}
