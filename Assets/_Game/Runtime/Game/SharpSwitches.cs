using System;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// Batch 12 item AB (art audit, sharpness): five view-only changes, each ON by default and each with its own off
    /// switch so a before / after in-game picture can attribute it. Read once from the command line:
    /// <c>-sharpOff normals,rim,upscale,msaa,lod0</c> (any comma list, or <c>all</c>; a bare <c>-sharpOff</c> = all).
    /// <list type="bullet">
    /// <item>normals: character lighting from by-position smoothed normals (60 degrees, Editor/Art/SharpNormalsPostprocessor
    /// writes them into UV3; CM/Toon _SmoothNormals lerps imported to smoothed). Off: _SmoothNormals 0, the imported
    /// flat-per-triangle normals as before.</item>
    /// <item>rim: the team rim strength x <see cref="RimScale"/> (0.6 to 0.3). Off: ModelSpec.Rim as authored.</item>
    /// <item>upscale: Medium render scale 0.85 instead of 0.75 (FramePolicy.MidRenderScale). URP's FSR filter was the
    /// first choice but cannot take effect at run time here: URP 17 runs non-bilinear upscaling only in its FinalPost
    /// pass, which needs post-processing on the camera (UniversalRendererRenderGraph.cs, applyFinalPostProcessing), and
    /// the game's camera renders with post-processing off, so URP would keep upscaling bilinear. Off: 0.75.</item>
    /// <item>msaa: 2x MSAA on the High URP asset (High, Very High, Ultra share it), player only. Off: 1 (as authored).</item>
    /// <item>lod0: Medium LOD0 crowd budget 16 instead of 10 (QualityTable.MediumLod0Budget). Off: 10.</item>
    /// </list>
    /// Every number lives here (one config place). View only: nothing here reaches the simulation or a checksum.
    /// </summary>
    public static class SharpSwitches
    {
        [Flags]
        public enum Switch
        {
            None = 0,
            Normals = 1,
            Rim = 2,
            Upscale = 4,
            Msaa = 8,
            Lod0 = 16,
            All = Normals | Rim | Upscale | Msaa | Lod0,
        }

        public const string Flag = "-sharpOff";

        // normals
        public const float SmoothNormalsAngleDeg = 60f;
        // rim
        public const float RimScale = 0.5f;
        // upscale
        public const float MidRenderScaleSharp = 0.85f, MidRenderScaleOld = 0.75f;
        // msaa
        public const int HighMsaaSharp = 2, MsaaOld = 1;
        // lod0
        public const int MediumLod0BudgetSharp = 16, MediumLod0BudgetOld = 10;

        private static Switch _off;
        private static bool _parsed;

        /// <summary>The switches turned off on this run (parsed once from the command line).</summary>
        public static Switch Off
        {
            get
            {
                if (!_parsed)
                {
                    _off = Parse(Environment.GetCommandLineArgs());
                    _parsed = true;
                }
                return _off;
            }
        }

        public static bool Normals => (Off & Switch.Normals) == 0;
        public static bool Rim => (Off & Switch.Rim) == 0;
        public static bool Upscale => (Off & Switch.Upscale) == 0;
        public static bool Msaa => (Off & Switch.Msaa) == 0;
        public static bool Lod0 => (Off & Switch.Lod0) == 0;

        /// <summary>The switches a command line turns off (pure; tests).</summary>
        public static Switch Parse(string[] args)
        {
            if (args == null) return Switch.None;
            int i = Array.IndexOf(args, Flag);
            if (i < 0) return Switch.None;
            if (i + 1 >= args.Length || args[i + 1].StartsWith("-", StringComparison.Ordinal)) return Switch.All;
            return ParseList(args[i + 1]);
        }

        /// <summary>One comma list: normals, rim, upscale, msaa, lod0, all (case and spaces ignored; unknown names ignored).</summary>
        public static Switch ParseList(string list)
        {
            Switch off = Switch.None;
            if (string.IsNullOrEmpty(list)) return off;
            foreach (string raw in list.Split(','))
            {
                switch (raw.Trim().ToLowerInvariant())
                {
                    case "normals": off |= Switch.Normals; break;
                    case "rim": off |= Switch.Rim; break;
                    case "upscale": off |= Switch.Upscale; break;
                    case "msaa": off |= Switch.Msaa; break;
                    case "lod0": off |= Switch.Lod0; break;
                    case "all": off |= Switch.All; break;
                }
            }
            return off;
        }

        /// <summary>"normals=on rim=off ..." for the log line.</summary>
        public static string Describe(Switch off) =>
            "normals=" + OnOff(off, Switch.Normals) + " rim=" + OnOff(off, Switch.Rim) + " upscale=" + OnOff(off, Switch.Upscale) +
            " msaa=" + OnOff(off, Switch.Msaa) + " lod0=" + OnOff(off, Switch.Lod0);

        static string OnOff(Switch off, Switch s) => (off & s) == 0 ? "on" : "off";

        // ---------------------------------------------------------------------------------------- the values (pure)

        /// <summary>CM/Toon _SmoothNormals for a character material: 1 smoothed, 0 the imported normals.</summary>
        public static float SmoothNormalsFor(bool on) => on ? 1f : 0f;

        /// <summary>The material rim strength for a model's authored ModelSpec.Rim.</summary>
        public static float RimFor(float specRim, bool on) => on ? specRim * RimScale : specRim;

        /// <summary>Medium's first-launch render scale (FramePolicy.MidRenderScale).</summary>
        public static float MidRenderScaleFor(bool on) => on ? MidRenderScaleSharp : MidRenderScaleOld;

        /// <summary>MSAA samples of the URP asset a quality level uses: 2 at High and above when on, else 1.</summary>
        public static int MsaaFor(int level, bool on) => on && level >= FramePolicy.LevelHigh ? HighMsaaSharp : MsaaOld;

        /// <summary>Medium's LOD0 crowd budget (QualityTable.MediumLod0Budget).</summary>
        public static int MediumLod0BudgetFor(bool on) => on ? MediumLod0BudgetSharp : MediumLod0BudgetOld;

        /// <summary>Tests only: force the off set (null-free; ResetForTests reads the command line again).</summary>
        public static void SetForTests(Switch off)
        {
            _off = off;
            _parsed = true;
        }

        public static void ResetForTests() => _parsed = false;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void LogOnce() => Debug.Log("[CM.Sharp] " + Describe(Off) + " (" + Flag + " <list> turns them off)");

        // ---------------------------------------------------------------------------------------- consumers

        /// <summary>
        /// Sharpness switch "msaa": 2x MSAA on the URP asset the High level uses (Very High and Ultra share it), set once at
        /// start in a player. Players only, like FrameGovernor's render scale: in a player the asset is an in-memory object,
        /// so the project's .asset file never changes (in the editor nothing is touched). Off: nothing is touched (authored 1).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void ApplyMsaa()
        {
            if (Application.isEditor || !Msaa) return;
            int levels = QualitySettings.names.Length;
            for (int i = 0; i < levels; i++)
            {
                if (QualitySettings.GetRenderPipelineAssetAt(i) is UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset a)
                {
                    int want = MsaaFor(i, true);
                    if (i >= FramePolicy.LevelHigh || a.msaaSampleCount != want) a.msaaSampleCount = Mathf.Max(a.msaaSampleCount, want);
                }
            }
            Debug.Log("[CM.Sharp] msaa: High URP asset " + MsaaFor(FramePolicy.LevelHigh, true) + "x, Medium and Low unchanged");
        }
    }
}
