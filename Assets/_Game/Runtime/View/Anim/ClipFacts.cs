using System;
using System.Collections.Generic;
using CriticalMass.Runtime.View.Art;
using UnityEngine;

namespace CriticalMass.Runtime.View.Anim
{
    /// <summary>One clip's measured facts (Resources/Anim/clip_facts.json, written by tools/art/measure_clips.py).</summary>
    [Serializable]
    public sealed class ClipFact
    {
        public string clip;
        public string source;
        public float length;
        public bool loop;
        /// <summary>Planted-foot ground speed, u/s; 0 for clips that do not travel.</summary>
        public float groundSpeed;
        /// <summary>Direction the body travels relative to its facing, degrees, + = to the character's left.</summary>
        public float moveDeg;
        /// <summary>Normalised phase of the left foot's mid-stance (the sync marker), or -1.</summary>
        public float leftPlant = -1f;
        /// <summary>Normalised phases where the left and right toes touch down, or -1.</summary>
        public float[] touchdown = { -1f, -1f };
        /// <summary>Seconds: the weapon hand's peak speed (the hit frame).</summary>
        public float contact;
        public float lowest;
        public string swingPlane;
        /// <summary>Hips XZ travel over the clip, u (looping clips must stay under RootDriftLimit).</summary>
        public float hipsDrift;
        public float headStart, headEnd;

        /// <summary>Distance travelled per cycle at rate 1 (u); 0 when the clip does not travel.</summary>
        public float Stride => groundSpeed * length;
    }

    [Serializable]
    sealed class ClipFactsDoc
    {
        public string source;
        public int fps;
        public ClipFact[] clips = new ClipFact[0];
    }

    /// <summary>
    /// The clip facts table (DG-72): every speed, phase and contact time the animation code uses comes from here, measured
    /// on the KayKit source by forward kinematics (never estimated). Loaded once from Resources; if the JSON is missing the
    /// built-in rows below (the same measurements, 2026-11-12, 120 Hz) keep the game correct. The KayKit clips carry
    /// translation and scale keys on every bone, so a game model plays the Knight's exact stride whatever its mesh height.
    /// </summary>
    public static class ClipFacts
    {
        public const string ResourcePath = "Anim/clip_facts";
        /// <summary>Root-motion policy (A24): the sim owns translation; a looping clip may drift its hips at most this per cycle.</summary>
        public const float RootDriftLimit = 0.05f;

        static Dictionary<string, ClipFact> _byName;

        /// <summary>Built-in rows: clip, length, ground speed, move deg, left plant, touchdown L, touchdown R, contact.</summary>
        static readonly (string, float, float, float, float, float, float, float)[] Builtin =
        {
            (ArtMapping.Idle, 1.067f, 0f, 0f, -1f, -1f, -1f, 0.925f),
            (ArtMapping.Walk, 1.067f, 0.86f, 0f, 0.281f, 0.0938f, 0.5938f, 0.267f),
            (ArtMapping.Run, 0.8f, 4.57f, 0f, 0.203f, 0.1667f, 0.6667f, 0.067f),
            (ArtMapping.StrafeL, 0.8f, 4.54f, 60.1f, 0.203f, 0.1667f, 0.6667f, 0.158f),
            (ArtMapping.StrafeR, 0.8f, 4.50f, -60.1f, 0.203f, 0.1667f, 0.6562f, 0.292f),
            (ArtMapping.WalkBack, 1.067f, 0.86f, -180f, 0.719f, 0.5312f, 0.0547f, 0.833f),
            (ArtMapping.Attack, 1.067f, 0f, 0f, -1f, -1f, -1f, 0.567f),
            (ArtMapping.AttackSlice, 1.067f, 0f, 0f, -1f, -1f, -1f, 0.242f),
            (ArtMapping.Attack2HChop, 1.633f, 0f, 0f, -1f, -1f, -1f, 0.700f),
            (ArtMapping.Attack2HSlice, 1.1f, 0f, 0f, -1f, -1f, -1f, 0.367f),
            (ArtMapping.Cast, 0.933f, 0f, 0f, -1f, -1f, -1f, 0.067f),
            (ArtMapping.UseItem, 1.6f, 0f, 0f, -1f, -1f, -1f, 0.167f),
            (ArtMapping.Throw, 1.367f, 0f, 0f, -1f, -1f, -1f, 0.700f),
            (ArtMapping.Block, 1.067f, 0f, 0f, -1f, -1f, -1f, 0.167f),
            (ArtMapping.Hit, 0.667f, 0f, 0f, -1f, -1f, -1f, 0.050f),
            (ArtMapping.HitHeavy, 0.867f, 0f, 0f, -1f, -1f, -1f, 0.058f),
            (ArtMapping.GetUp, 2.333f, 0f, 0f, -1f, -1f, -1f, 0.458f),
            (ArtMapping.JumpStart, 0.6f, 0f, 0f, -1f, -1f, -1f, 0.425f),
            (ArtMapping.JumpLand, 0.667f, 0f, 0f, -1f, -1f, -1f, 0.258f),
        };

        /// <summary>The fact row for a game clip name, or null (no row and no built-in).</summary>
        public static ClipFact Get(string clip)
        {
            if (clip == null) return null;
            if (_byName == null) Load();
            return _byName.TryGetValue(clip, out ClipFact f) ? f : null;
        }

        /// <summary>Ground speed of a locomotion clip, u/s, or fallback.</summary>
        public static float GroundSpeed(string clip, float fallback) { ClipFact f = Get(clip); return f != null && f.groundSpeed > 0.05f ? f.groundSpeed : fallback; }

        /// <summary>Contact (hit frame) seconds of an attack clip, or fallback.</summary>
        public static float Contact(string clip, float fallback) { ClipFact f = Get(clip); return f != null && f.contact > 0f ? f.contact : fallback; }

        /// <summary>Left mid-stance phase of a locomotion clip, or fallback.</summary>
        public static float LeftPlant(string clip, float fallback) { ClipFact f = Get(clip); return f != null && f.leftPlant >= 0f ? f.leftPlant : fallback; }

        /// <summary>Drops the cache (tests).</summary>
        public static void ResetForTests() => _byName = null;

        static void Load()
        {
            var d = new Dictionary<string, ClipFact>();
            foreach (var b in Builtin)
                d[b.Item1] = new ClipFact { clip = b.Item1, length = b.Item2, groundSpeed = b.Item3, moveDeg = b.Item4, leftPlant = b.Item5,
                    touchdown = new[] { b.Item6, b.Item7 }, contact = b.Item8 };
            var text = Resources.Load<TextAsset>(ResourcePath);
            if (text != null)
            {
                ClipFactsDoc doc = null;
                try { doc = JsonUtility.FromJson<ClipFactsDoc>(text.text); }
                catch (ArgumentException e) { Debug.LogWarning("[ClipFacts] " + ResourcePath + " unreadable, built-in rows kept: " + e.Message); }
                if (doc?.clips != null)
                    foreach (ClipFact f in doc.clips)
                        if (f != null && !string.IsNullOrEmpty(f.clip)) d[f.clip] = f;
            }
            _byName = d;
        }
    }
}
