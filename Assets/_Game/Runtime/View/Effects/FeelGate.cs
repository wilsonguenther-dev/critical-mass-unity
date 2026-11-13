using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Menu;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>What an effect is gated by (physics audit 2026-11-12, runtime seat M11).</summary>
    [Flags]
    public enum FeelGating
    {
        /// <summary>Nothing to gate: no flashing, no camera motion (sounds, decals, small sparks).</summary>
        None = 0,
        /// <summary>Bright or fast-changing light: ReduceFlashing tones it down (tint instead of white, no pulse above 2 Hz).</summary>
        Flashing = 1,
        /// <summary>Moves the camera or the whole view: ReduceMotion turns it off.</summary>
        Motion = 2,
        /// <summary>Camera shake: scaled by ShakeScale, off with ReduceMotion.</summary>
        Shake = 4,
    }

    /// <summary>View effects that are not an FxKind (continuous or camera effects); each must have a gating entry.</summary>
    public enum FeelEffect
    {
        HitFlash, DamageVignette, SlamTelegraphPulse, MeleeTelegraph, BerserkPulse, ShieldPulse, CameraShake, CombatZoom,
        KaijuSpotlight, BlobShadow,
    }

    /// <summary>
    /// The accessibility switches every effect reads (cached from GameSettings, refreshed when a setting changes, so no
    /// effect reads PlayerPrefs per frame) and the gating table: every FxKind and every FeelEffect declares what gates it,
    /// and FeelGateTests fail any effect without an entry. View only; nothing here reaches the sim.
    /// </summary>
    public static class FeelGate
    {
        /// <summary>Pulses on screen never exceed this (photosensitivity guidance: no more than 3 flashes per second).</summary>
        public const float MaxPulseHz = 3f;
        /// <summary>With ReduceFlashing, pulses stay at or under this.</summary>
        public const float ReducedPulseHz = 2f;
        /// <summary>With ReduceFlashing the hit flash is a 30% tint toward white instead of full white.</summary>
        public const float ReducedFlashTint = 0.3f;

        static bool _loaded;
        static float _shake = 1f;
        static bool _reduceFlashing, _reduceMotion;

        /// <summary>Tests and captures may pin the switches (null = read GameSettings).</summary>
        public static float? ShakeOverride;
        public static bool? ReduceFlashingOverride, ReduceMotionOverride;

        static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            GameSettings.Changed -= Reload;
            GameSettings.Changed += Reload;
            Reload();
        }

        static void Reload()
        {
            _shake = GameSettings.ShakeScale;
            _reduceFlashing = GameSettings.ReduceFlashing;
            _reduceMotion = GameSettings.ReduceMotion;
        }

        /// <summary>Camera shake multiplier 0..1 (0 with ReduceMotion).</summary>
        public static float ShakeScale
        {
            get
            {
                Load();
                if (ReduceMotion) return 0f;
                float s = ShakeOverride ?? _shake;
                return s < 0f ? 0f : s > 1f ? 1f : s;
            }
        }

        public static bool ReduceFlashing
        {
            get { Load(); return ReduceFlashingOverride ?? _reduceFlashing; }
        }

        public static bool ReduceMotion
        {
            get { Load(); return ReduceMotionOverride ?? _reduceMotion; }
        }

        /// <summary>The pulse ceiling in force (Hz on screen).</summary>
        public static float PulseCeilingHz => ReduceFlashing ? ReducedPulseHz : MaxPulseHz;

        /// <summary>
        /// A 0..1 pulse at a wall-clock frequency, from SIM time and the match pace (sim seconds per wall second), so the
        /// on-screen rate is what is asked whatever the pace (the old slam pulse was 18 rad per sim second, 5.7 Hz on screen
        /// at pace 2). The frequency is clamped to the ceiling in force.
        /// </summary>
        public static float Pulse(double simNow, double pace, float wallHz)
        {
            float hz = Math.Min(wallHz, PulseCeilingHz);
            double wall = pace > 0 ? simNow / pace : simNow;
            return (float)(0.5 + 0.5 * Math.Sin(wall * hz * 2.0 * Math.PI));
        }

        /// <summary>The on-screen frequency of a pulse request (pure; tests).</summary>
        public static float PulseHz(float wallHz, bool reduceFlashing) => Math.Min(wallHz, reduceFlashing ? ReducedPulseHz : MaxPulseHz);

        static readonly Dictionary<FxKind, FeelGating> KindTable = new Dictionary<FxKind, FeelGating>
        {
            { FxKind.Hit, FeelGating.Flashing | FeelGating.Shake },   // white flash on the victim; local heavy hits shake
            { FxKind.Block, FeelGating.Flashing },
            { FxKind.Death, FeelGating.Flashing | FeelGating.Shake },
            { FxKind.Respawn, FeelGating.None },
            { FxKind.Swing, FeelGating.None },
            { FxKind.Ability, FeelGating.None },
            { FxKind.KaijuHit, FeelGating.Flashing },
            { FxKind.KaijuSlam, FeelGating.Flashing | FeelGating.Shake },
            { FxKind.KaijuRoar, FeelGating.None },
            { FxKind.KaijuKO, FeelGating.Shake },
            { FxKind.BerserkLock, FeelGating.Flashing | FeelGating.Shake },
            { FxKind.Hatch, FeelGating.Shake | FeelGating.Motion },
            { FxKind.Gather, FeelGating.None },
            { FxKind.DeliverBiomass, FeelGating.None },
            { FxKind.DeliverCorruption, FeelGating.None },
            { FxKind.BarricadePlaced, FeelGating.None },
            { FxKind.BarricadeRefused, FeelGating.None },
            { FxKind.BarricadeBroken, FeelGating.None },
            { FxKind.WallCrack, FeelGating.None },
            { FxKind.WallBreak, FeelGating.Shake },
            { FxKind.HillChange, FeelGating.None },
        };

        static readonly Dictionary<FeelEffect, FeelGating> EffectTable = new Dictionary<FeelEffect, FeelGating>
        {
            { FeelEffect.HitFlash, FeelGating.Flashing },
            { FeelEffect.DamageVignette, FeelGating.Flashing },
            { FeelEffect.SlamTelegraphPulse, FeelGating.Flashing },
            { FeelEffect.MeleeTelegraph, FeelGating.Flashing },
            { FeelEffect.BerserkPulse, FeelGating.Flashing },
            { FeelEffect.ShieldPulse, FeelGating.Flashing },
            { FeelEffect.CameraShake, FeelGating.Shake | FeelGating.Motion },
            { FeelEffect.CombatZoom, FeelGating.Motion },
            { FeelEffect.KaijuSpotlight, FeelGating.Motion | FeelGating.Flashing },
            { FeelEffect.BlobShadow, FeelGating.None },
        };

        /// <summary>The gating of an effect kind; false when the table has no entry (a test fails that).</summary>
        public static bool TryGating(FxKind kind, out FeelGating g) => KindTable.TryGetValue(kind, out g);
        public static bool TryGating(FeelEffect effect, out FeelGating g) => EffectTable.TryGetValue(effect, out g);

        /// <summary>true when the effect may run with the switches in force (a Motion effect is off with ReduceMotion).</summary>
        public static bool Allowed(FeelEffect effect)
        {
            if (!EffectTable.TryGetValue(effect, out FeelGating g)) return false;
            if ((g & FeelGating.Motion) != 0 && ReduceMotion) return false;
            if ((g & FeelGating.Shake) != 0 && ShakeScale <= 0f) return false;
            return true;
        }
    }
}
