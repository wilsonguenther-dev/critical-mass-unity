using System;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>The GORE setting's three intensity levels (GameSettings.Gore, PlayerPrefs cm.gore). Full is the default.</summary>
    public enum GoreLevel : byte
    {
        /// <summary>No blood, bones or body parts anywhere: a bright silhouette flash, a star burst and sparks on a hit; a sparkle poof on a knock-out.</summary>
        Off = 0,
        /// <summary>Smaller and fewer: half the drops, smaller splats that fade in 4 s, no knock-out pool, no drips, no bones or body parts, nothing builds up on the ground.</summary>
        Reduced = 1,
        /// <summary>Today's look (the default).</summary>
        Full = 2,
    }

    /// <summary>Every piece of the game that shows blood, bones or body parts. Each one is spawned or drawn only after asking GoreGate.</summary>
    public enum GorePiece : byte
    {
        /// <summary>Red drops thrown from a hit (FxScene droplets).</summary>
        BloodDroplet,
        /// <summary>A splat where a drop lands (ground or bridge).</summary>
        BloodSplat,
        /// <summary>The big lobed puddle under a knock-out.</summary>
        KnockOutPool,
        /// <summary>Splats on wall tops left by flying gibs.</summary>
        WallTopSplat,
        /// <summary>The drip trail behind a badly hurt fighter.</summary>
        BloodDrip,
        /// <summary>Meat cubes thrown by a Kaiju slam or blast knock-out (a body part).</summary>
        GibMeat,
        /// <summary>Cartoon bones thrown with the meat (a body part).</summary>
        GibBone,
        /// <summary>A splat stamped for good into the ground paint (the stains that build up where fights happen).</summary>
        GroundStain,
        /// <summary>Weapon tinted red after landing a hit (Effects.WeaponBlood hook).</summary>
        WeaponBloodTint,
        /// <summary>The Kaiju's fist tinted red after a melee hit (Effects.KaijuFistBlood hook).</summary>
        KaijuFistBloodTint,
        /// <summary>The dark red edge vignette when the local player is hit (navy at Off).</summary>
        RedDamageVignette,
        /// <summary>World dressing props: the Kaiju skull and the bone ribs (WorldDresser; applies at the next match load).</summary>
        WorldBoneProp,
        /// <summary>The old Kaiju skeleton on the far island past the rim (WorldBeyond; applies at the next match load).</summary>
        BeyondKaijuSkeleton,
    }

    /// <summary>
    /// The one gore switch (owner, 2026-11-14: "make it a setting and a level of intensity"; Full stays the default).
    /// Every hit, death, Kaiju, status, decal, world-dressing and menu path that can show blood, bones or body parts asks
    /// Allows(piece) here, and the registry below is the complete list of such pieces (GoreSettingTests enumerate it, so a
    /// new gore piece must be added here and gated to exist at all). View only: nothing here reaches the sim, so the
    /// fixed-seed checksum is the same at every level.
    /// </summary>
    public static class GoreGate
    {
        /// <summary>Every gore piece (the registry the tests enumerate).</summary>
        public static readonly GorePiece[] All = (GorePiece[])Enum.GetValues(typeof(GorePiece));

        /// <summary>Levels in menu order (the stepper cycles through them).</summary>
        public static readonly GoreLevel[] Levels = { GoreLevel.Full, GoreLevel.Reduced, GoreLevel.Off };

        /// <summary>Tests and captures may pin the level (null = the player's setting).</summary>
        public static GoreLevel? Override;

        /// <summary>The level in force: the override, else GameSettings.Gore (PlayerPrefs; read it once per frame at most).</summary>
        public static GoreLevel Current => Override ?? FlagLevel() ?? CriticalMass.Runtime.Menu.GameSettings.Gore;

        static bool _flagRead;
        static GoreLevel? _flag;

        /// <summary>Integration batch 9: "-goreLevel full|reduced|off" pins the level for a player run, so the gore levels can be
        /// looked at in the BUILT game (GoreCapture is editor only). Honoured only with -autoplay (an automation run), so a
        /// person's launch never changes it. Read once per process; never writes the setting.</summary>
        static GoreLevel? FlagLevel()
        {
            if (_flagRead) return _flag;
            _flagRead = true;
            string[] args;
            try { args = Environment.GetCommandLineArgs(); } catch { return null; }
            if (Array.FindIndex(args, a => string.Equals(a, "-autoplay", StringComparison.OrdinalIgnoreCase)) < 0) return null;
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (!string.Equals(args[i], "-goreLevel", StringComparison.OrdinalIgnoreCase)) continue;
                string v = args[i + 1].ToLowerInvariant();
                _flag = v == "off" ? GoreLevel.Off : v == "reduced" ? GoreLevel.Reduced : v == "full" ? GoreLevel.Full : (GoreLevel?)null;
            }
            return _flag;
        }

        /// <summary>True for a piece that is a body part or bone (never shown below Full).</summary>
        public static bool IsBodyPart(GorePiece p) =>
            p == GorePiece.GibMeat || p == GorePiece.GibBone || p == GorePiece.WorldBoneProp || p == GorePiece.BeyondKaijuSkeleton;

        /// <summary>May this piece be spawned or drawn at this level?</summary>
        public static bool Allows(GorePiece p, GoreLevel level)
        {
            switch (level)
            {
                case GoreLevel.Full: return true;
                case GoreLevel.Reduced:
                    switch (p)
                    {
                        case GorePiece.BloodDroplet:
                        case GorePiece.BloodSplat:
                        case GorePiece.WeaponBloodTint:
                        case GorePiece.KaijuFistBloodTint:
                        case GorePiece.RedDamageVignette:
                            return true;
                        default:
                            return false;   // no pool, no drips, no wall splats, no stamped stains, no bones or body parts
                    }
                default: return false;
            }
        }

        /// <summary>Allows at the level in force.</summary>
        public static bool Allows(GorePiece p) => Allows(p, Current);

        /// <summary>Share of today's drop count thrown per hit (Full 1, Reduced 0.5, Off 0).</summary>
        public static float CountScale(GoreLevel l) => l == GoreLevel.Full ? 1f : l == GoreLevel.Reduced ? 0.5f : 0f;

        /// <summary>Splat lifetime scale (Full 20 s, Reduced 4 s).</summary>
        public static float LifeScale(GoreLevel l) => l == GoreLevel.Full ? 1f : 0.2f;

        /// <summary>Splat size scale (Reduced splats are smaller).</summary>
        public static float SizeScale(GoreLevel l) => l == GoreLevel.Full ? 1f : 0.7f;

        /// <summary>Menu label.</summary>
        public static string Label(GoreLevel l) => l == GoreLevel.Full ? "FULL" : l == GoreLevel.Reduced ? "REDUCED" : "OFF";

        /// <summary>The next level in menu order (d = +1 or -1, wrapping).</summary>
        public static GoreLevel Step(GoreLevel l, int d)
        {
            int i = Array.IndexOf(Levels, l);
            if (i < 0) i = 0;
            int n = Levels.Length;
            return Levels[(((i + (d < 0 ? -1 : 1)) % n) + n) % n];
        }
    }
}
