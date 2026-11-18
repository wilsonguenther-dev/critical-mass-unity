using System;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>
    /// b15 world look, motion item 4a (WORLD-AUDIT "feet never plant"): each class's OWN baked locomotion clips, measured.
    /// ClipFacts (clip_facts.json) was measured on the KayKit Knight and the animator scaled it by ModelSpec.StrideScale
    /// (hip-height ratio); every class now plays its own clips baked with foot IK (&lt;Class&gt;_Clips.fbx), whose real
    /// planted-toe speed is 5 to 12% off that product (Warrior run 4.74 against 4.57 x 1.118 = 5.11). A wrong stride makes
    /// the planted foot slide by exactly that error. This table is the measured truth, keyed by the clips file name:
    /// tools/art/measure_class_clips.py, headless Blender 5.2.3, forward kinematics on the evaluated armature at 120 Hz
    /// (2026-11-17, branch feat/b15-world-look). Columns: clip, length s, planted-toe ground speed u/s, left mid-stance
    /// phase, max toe lift u. Re-run the script after any re-bake and paste its rows here (ClassStrideTests pins them).
    /// View only.
    /// </summary>
    public static class ClassStrides
    {
        public readonly struct Row
        {
            public readonly string Source, Clip;
            public readonly float Length, GroundSpeed, LeftPlant, Lift;
            public Row(string source, string clip, float length, float groundSpeed, float leftPlant, float lift)
            {
                Source = source; Clip = clip; Length = length; GroundSpeed = groundSpeed; LeftPlant = leftPlant; Lift = lift;
            }
        }

        public static readonly Row[] Rows =
        {
            new Row("Warrior_Clips.fbx", "Run", 0.7917f, 4.7367f, 0.2053f, 0.337f),
            new Row("Warrior_Clips.fbx", "StrafeL", 0.7917f, 4.8792f, 0.1895f, 0.295f),
            new Row("Warrior_Clips.fbx", "StrafeR", 0.7917f, 4.7727f, 0.2000f, 0.352f),
            new Row("Warrior_Clips.fbx", "Walk", 1.0417f, 1.0160f, 0.2800f, 0.157f),
            new Row("Warrior_Clips.fbx", "WalkBack", 1.0417f, 1.0378f, 0.7440f, 0.161f),
            new Row("Farmer_Clips.fbx", "Run", 0.7917f, 3.3640f, 0.1923f, 0.241f),
            new Row("Farmer_Clips.fbx", "StrafeL", 0.7917f, 3.2504f, 0.1938f, 0.207f),
            new Row("Farmer_Clips.fbx", "StrafeR", 0.7917f, 3.4367f, 0.1984f, 0.209f),
            new Row("Farmer_Clips.fbx", "Walk", 1.0417f, 0.6539f, 0.2760f, 0.083f),
            new Row("Farmer_Clips.fbx", "WalkBack", 1.0417f, 0.6879f, 0.7400f, 0.085f),
            new Row("Miner_Clips.fbx", "Run", 0.7917f, 3.5909f, 0.1989f, 0.235f),
            new Row("Miner_Clips.fbx", "StrafeL", 0.7917f, 3.4715f, 0.2053f, 0.236f),
            new Row("Miner_Clips.fbx", "StrafeR", 0.7917f, 3.7245f, 0.2368f, 0.323f),
            new Row("Miner_Clips.fbx", "Walk", 1.0417f, 0.6977f, 0.3000f, 0.098f),
            new Row("Miner_Clips.fbx", "WalkBack", 1.0417f, 0.7157f, 0.7280f, 0.100f),
            new Row("Demolitions_Clips.fbx", "Run", 0.7917f, 5.6589f, 0.1532f, 0.316f),
            new Row("Demolitions_Clips.fbx", "StrafeL", 0.7917f, 6.2511f, 0.1127f, 0.287f),
            new Row("Demolitions_Clips.fbx", "StrafeR", 0.7917f, 6.1889f, 0.1789f, 0.315f),
            new Row("Demolitions_Clips.fbx", "Walk", 1.0417f, 1.2766f, 0.2000f, 0.124f),
            new Row("Demolitions_Clips.fbx", "WalkBack", 1.0417f, 1.2932f, 0.8720f, 0.158f),
            new Row("Builder_Clips.fbx", "Run", 0.7917f, 6.8039f, 0.1947f, 0.343f),
            new Row("Builder_Clips.fbx", "StrafeL", 0.7917f, 6.5784f, 0.1535f, 0.297f),
            new Row("Builder_Clips.fbx", "StrafeR", 0.7917f, 7.9884f, 0.1895f, 0.322f),
            new Row("Builder_Clips.fbx", "Walk", 1.0417f, 1.2785f, 0.2320f, 0.183f),
            new Row("Builder_Clips.fbx", "WalkBack", 1.0417f, 1.2610f, 0.8040f, 0.215f),
            new Row("Healer_Clips.fbx", "Run", 0.7917f, 5.9432f, 0.1947f, 0.305f),
            new Row("Healer_Clips.fbx", "StrafeL", 0.7917f, 5.9901f, 0.1684f, 0.276f),
            new Row("Healer_Clips.fbx", "StrafeR", 0.7917f, 5.4837f, 0.1491f, 0.265f),
            new Row("Healer_Clips.fbx", "Walk", 1.0417f, 1.2419f, 0.2320f, 0.155f),
            new Row("Healer_Clips.fbx", "WalkBack", 1.0417f, 1.2579f, 0.8200f, 0.192f),
        };

        /// <summary>The file name of a ModelSpec.ClipSource path ("Assets/_Game/Art/Farmer/Farmer_Clips.fbx" to
        /// "Farmer_Clips.fbx"), or null.</summary>
        public static string SourceOf(string clipSource)
        {
            if (string.IsNullOrEmpty(clipSource)) return null;
            int i = clipSource.LastIndexOfAny(new[] { '/', '\\' });
            return i >= 0 ? clipSource.Substring(i + 1) : clipSource;
        }

        /// <summary>The measured row for (clips file, clip), or false when the class has no measured row (the Knight
        /// fallback with StrideScale stays in force).</summary>
        public static bool TryGet(string source, string clip, out Row row)
        {
            if (source != null && clip != null)
                for (int i = 0; i < Rows.Length; i++)
                    if (Rows[i].Clip == clip && string.Equals(Rows[i].Source, source, StringComparison.OrdinalIgnoreCase)) { row = Rows[i]; return true; }
            row = default;
            return false;
        }

        /// <summary>True when every locomotion clip of the source is measured (then the stride needs no StrideScale).</summary>
        public static bool Covers(string source) => source != null && TryGet(source, "Run", out _) && TryGet(source, "Walk", out _);
    }
}
