using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Audio;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// Sound coverage (2026-11-12): combat sound moved to the one sound player, Audio.Sound.SoundDirector, which decodes
    /// every sim event through Audio.Sound.SoundTable (the 42 Kenney CC0 sounds in Assets/_Game/Audio/Resources/CMSfx plus
    /// procedural cues for everything the files do not cover). This class stays as the effects lane's IFxSink so CombatFx,
    /// FxGallery and tests keep compiling: it plays nothing itself (the director already hears every decoded event, so
    /// playing here would double each sound); it forwards PlayUi to the director.
    /// </summary>
    public sealed class CombatAudio : IFxSink, IDisposable
    {
        public const float NearDistance = 9f, FarDistance = 36f;
        public const string MixerResource = CriticalMass.Runtime.Menu.GameSettings.MixerResource, ClipFolder = SoundDirector.ClipFolder;

        /// <summary>The recorded files the sound table uses, one row per cue (tests: all 42 credited sounds have a cue).</summary>
        public static readonly (string Id, string[] Files)[] Table = BuildTable();

        private static (string Id, string[] Files)[] BuildTable()
        {
            var rows = new List<(string, string[])>();
            foreach (CueDef c in SoundTable.Cues) if (c.Files.Length > 0) rows.Add((c.Id, c.Files));
            return rows.ToArray();
        }

        private readonly GameObject _root;

        public AudioMixer Mixer { get; private set; }
        public AudioMixerGroup SfxGroup { get; private set; }
        public AudioMixerGroup MusicGroup { get; private set; }
        /// <summary>Kept for callers that report it: the director's loaded clip count (0 before it exists).</summary>
        public int ClipsLoaded => SoundDirector.Instance != null ? SoundDirector.Instance.ClipsLoaded : 0;
        public int Played => SoundDirector.Instance != null ? SoundDirector.Instance.Played : 0;
        public int Refused => SoundDirector.Instance != null ? SoundDirector.Instance.Refused : 0;

        public CombatAudio(Transform parent, GameConfig cfg)
        {
            Mixer = CriticalMass.Runtime.Menu.GameSettings.Mixer;
            SfxGroup = CriticalMass.Runtime.Menu.GameSettings.EffectsGroup;
            MusicGroup = CriticalMass.Runtime.Menu.GameSettings.MusicGroup;
            _root = new GameObject("Fx Audio");
            _root.transform.SetParent(parent, false);
            if (UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null && UnityEngine.Camera.main != null)
                UnityEngine.Camera.main.gameObject.AddComponent<AudioListener>();
        }

        public void Dispose()
        {
            if (_root == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(_root);
            else UnityEngine.Object.DestroyImmediate(_root);
        }

        /// <summary>Plays a named UI cue ("ui_click", "ability_denied", "hill_capture", ...) through the sound director.</summary>
        public static void PlayUi(string id) => SoundDirector.PlayNamed(id);

        /// <summary>Per-frame hook CombatFx still calls: nothing to do (the director mixes in its own LateUpdate).</summary>
        public void Update(double now, World w) { }

        /// <summary>
        /// Nothing: sim events reach the director through its own ring decode, and view-injected effects (Effects.Splash,
        /// Explode, Land) reach it as world forces (SoundTable.Forces), so playing here would double them.
        /// </summary>
        public void OnFx(ref FxEvent e, World w) { }

        /// <summary>Distance gain from the listener: 1 within NearDistance, 0 at FarDistance (SoundSpace's falloff).</summary>
        public static float Falloff(float d) => SoundSpace.Gain(d, NearDistance, FarDistance);
    }
}
