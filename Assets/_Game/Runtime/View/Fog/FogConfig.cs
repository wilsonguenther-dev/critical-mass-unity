using System;
using UnityEngine;

namespace CriticalMass.Runtime.View.Fog
{
    /// <summary>
    /// Batch 12 item P (Travis): fog of war, VIEW ONLY. Every number the fog uses lives here, in one place.
    /// <para>What each machine sees is computed from the sim state it already has (no sim rule, no checksum row, no
    /// lockstep traffic; bots are unchanged and still see everything). Sight: SightTeammate around every living teammate
    /// (you included), SightHeldHill around the hill while your team holds it, and your whole base (BaseSight, at least
    /// the base's own zones). Outside sight a haze darkens and desaturates the ground, enemy fighters and their overhead
    /// UI are not drawn, and the minimap and full map grey out. The Kaiju, the egg, resource nodes, terrain and hill
    /// ownership colours stay drawn (the neutral ones under the haze). Sounds are unchanged.</para>
    /// <para>On by default in the Full profile (an owner rule choice: the default), off in Classic (DepthConfig.Classic,
    /// read only). The ACCESS settings tab has the "FOG OF WAR" on / off row (SettingsExtras.FogOfWar). Spectators and the
    /// results screen see everything.</para>
    /// </summary>
    public static class FogConfig
    {
        // ---------------------------------------------------------------- sight (sim units)
        /// <summary>Sight radius around each living teammate, you included.</summary>
        public const double SightTeammate = 12.0;
        /// <summary>Sight radius around the hill while your team holds it.</summary>
        public const double SightHeldHill = 10.0;
        /// <summary>Your whole base is seen: a disc on your pad at least this wide (or the pad / station / bank zone if larger).</summary>
        public const double BaseSight = 9.0;

        // ---------------------------------------------------------------- the visibility map
        /// <summary>The visibility map is Resolution x Resolution cells, one byte each, stretched over the map's sim bounds.</summary>
        public const int Resolution = 128;
        /// <summary>Visibility map updates per second (not every frame).</summary>
        public const double UpdateHz = 10.0;
        public const double UpdateSeconds = 1.0 / UpdateHz;
        /// <summary>The haze and each enemy fade over this long, so nothing pops.</summary>
        public const double FadeSeconds = 0.3;
        /// <summary>Width of the soft edge of a sight disc, in visibility cells.</summary>
        public const double SoftCells = 1.0;
        /// <summary>A cell counts as seen at this byte or above (half visible; the disc's exact radius).</summary>
        public const byte SeenByte = 128;
        /// <summary>Per update, a shown cell moves this far toward its target (255 over FadeSeconds at UpdateHz).</summary>
        public static readonly byte FadeStep = (byte)Math.Ceiling(255.0 * UpdateSeconds / FadeSeconds);
        /// <summary>An enemy is drawn while its own fade is at or above this share (crossed half-way through the fade).</summary>
        public const float EnemyDrawnAt = 0.5f;

        // ---------------------------------------------------------------- the 3D haze
        /// <summary>Haze tone: a dark, low-saturation navy; blending toward it darkens and desaturates what is behind.</summary>
        public static readonly Color Haze = new Color(0.075f, 0.09f, 0.15f, 1f);
        /// <summary>Top-down: haze opacity where nothing is seen (the one ground-level quad).</summary>
        public const float TopDownAlpha = 0.62f;
        /// <summary>Third and first person: the height-fog band, layers above the haze floor (u) and each layer's opacity.</summary>
        public static readonly float[] BandHeights = { 0f, 0.8f, 1.7f };
        public static readonly float[] BandAlphas = { 0.5f, 0.32f, 0.24f };
        /// <summary>The haze floor sits this far above the highest walkable ground (so it lies over every island).</summary>
        public const float HazeLift = 0.06f;

        // ---------------------------------------------------------------- the minimap and full map
        public static readonly Color MapGrey = new Color(0.16f, 0.17f, 0.21f, 1f);
        /// <summary>Opacity of the grey over the minimap and the full map where nothing is seen.</summary>
        public const float MapGreyAlpha = 0.58f;

        // ---------------------------------------------------------------- switches
        /// <summary>The fog draws only when every switch allows it: the setting, the Full profile, not spectating, not on the
        /// results screen (match ended), not behind the title. Pure (tests).</summary>
        public static bool WantFog(bool settingOn, bool classicProfile, bool spectating, bool matchEnded, bool title) =>
            settingOn && !classicProfile && !spectating && !matchEnded && !title;

        /// <summary>The Full profile allows fog; Classic (DepthConfig.Classic) does not. A profile read, not a sim rule.</summary>
        public static bool ProfileAllows(CriticalMass.Sim.Config.GameConfig cfg) => cfg != null && cfg.Depth != null && !cfg.Depth.Classic;

        // ---------------------------------------------------------------- automation
        /// <summary>Capture flag: the staged fog stills (FogShots); forces the fog on.</summary>
        public const string ShotsFlag = "-fogShots";
        /// <summary>"-fog on" / "-fog off": pins the fog for frame-cost control runs.</summary>
        public const string PinFlag = "-fog";

        /// <summary>
        /// The command line's say over the setting: true / false to pin it, null for none. -fogShots forces on; "-fog on|off"
        /// pins; otherwise a batch-mode run (tests and gates) keeps the fog off so every older test and capture sees what it
        /// saw before. Pure (tests).
        /// </summary>
        public static bool? CommandLinePin(string[] args, bool batchMode)
        {
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], ShotsFlag, StringComparison.OrdinalIgnoreCase)) return true;
                    if (string.Equals(args[i], PinFlag, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        string v = args[i + 1];
                        if (string.Equals(v, "on", StringComparison.OrdinalIgnoreCase)) return true;
                        if (string.Equals(v, "off", StringComparison.OrdinalIgnoreCase)) return false;
                    }
                }
            }
            return batchMode ? false : (bool?)null;
        }

        static bool? _pin;
        static bool _pinRead;

        /// <summary>The command-line pin of this run (read once).</summary>
        public static bool? Pin
        {
            get
            {
                if (!_pinRead) { _pin = CommandLinePin(Environment.GetCommandLineArgs(), Application.isBatchMode); _pinRead = true; }
                return _pin;
            }
        }

        /// <summary>Capture runs only (FogShots): forces the fog on (true) or off (false) over everything but the profile.</summary>
        public static bool? CaptureOverride;
    }
}
