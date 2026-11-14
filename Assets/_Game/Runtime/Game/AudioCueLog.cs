using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using UnityEngine;

namespace CriticalMass.Runtime.Game
{
    /// <summary>
    /// feat/flow-capture-1 (with -soundVolume in an automated run): a log of every music and sound cue, so audio gaps and
    /// abrupt cuts can be checked from Player.log. Reads only: it changes no audio code and plays nothing.
    /// <list type="bullet">
    /// <item>Each AudioSource that starts or stops playing (its clip, or "one-shot" when the source plays PlayOneShot
    /// sounds without a clip), and each that fades past audible (volume 0.02) while playing:
    /// "[CM.Audio] t=12.34 match=5.1 flow=Match START music &lt;clip&gt; on &lt;source&gt; vol 0.80".</item>
    /// <item>What reaches the ears: an output tap on the AudioListener measures the mix level per audio buffer; a stretch
    /// under SilenceRms that lasts GapSeconds or more is logged when it ends: "GAP 1.84 s of silence ending at t=...".</item>
    /// </list>
    /// Sources are re-found once a second (scenes reload between screens); the per-frame check walks a fixed list.
    /// "t" is real seconds since launch; "match" is the match clock when a match is loaded.
    /// </summary>
    public sealed class AudioCueLog : MonoBehaviour
    {
        public const string LogTag = "[CM.Audio]";
        public const float AudibleVolume = 0.02f, SilenceRms = 0.0005f, GapSeconds = 0.75f;

        struct Seen
        {
            public AudioSource Src;
            public bool Playing, Audible;
            public AudioClip Clip;
        }

        readonly List<Seen> _seen = new List<Seen>(128);
        float _nextScan;
        int _starts, _stops, _gaps;
        float _silentSince = -1f;
        ListenerTap _tap;

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now >= _nextScan)
            {
                _nextScan = now + 1f;
                Rescan();
            }
            for (int i = 0; i < _seen.Count; i++)
            {
                Seen s = _seen[i];
                if (s.Src == null)
                {
                    if (s.Playing) { _stops++; Line(now, "STOP (destroyed)", s.Clip, null, 0f); }
                    _seen.RemoveAt(i--);
                    continue;
                }
                bool playing = s.Src.isPlaying;
                bool audible = playing && s.Src.volume >= AudibleVolume && !s.Src.mute;
                AudioClip clip = s.Src.clip;
                if (playing && (!s.Playing || clip != s.Clip)) { _starts++; Line(now, "START", clip, s.Src, s.Src.volume); }
                else if (!playing && s.Playing) { _stops++; Line(now, "STOP", s.Clip, s.Src, s.Src.volume); }
                else if (playing && audible != s.Audible) Line(now, audible ? "FADE IN" : "FADE OUT", clip, s.Src, s.Src.volume);
                s.Playing = playing;
                s.Audible = audible;
                s.Clip = clip;
                _seen[i] = s;
            }
            CheckSilence(now);
        }

        void Rescan()
        {
            AudioSource[] all = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < all.Length; i++)
            {
                bool known = false;
                for (int k = 0; k < _seen.Count && !known; k++) known = _seen[k].Src == all[i];
                if (!known) _seen.Add(new Seen { Src = all[i] });
            }
            if (_tap == null)
            {
                AudioListener l = FindAnyObjectByType<AudioListener>();
                if (l != null)
                {
                    _tap = l.GetComponent<ListenerTap>();
                    if (_tap == null) _tap = l.gameObject.AddComponent<ListenerTap>();
                }
            }
        }

        void CheckSilence(float now)
        {
            if (_tap == null) return;
            bool silent = _tap.Rms < SilenceRms;
            if (silent && _silentSince < 0f) _silentSince = now;
            else if (!silent && _silentSince >= 0f)
            {
                float len = now - _silentSince;
                if (len >= GapSeconds)
                {
                    _gaps++;
                    Debug.Log($"{LogTag} t={now:F2} GAP {len:F2} s of silence ending here ({Where()})");
                }
                _silentSince = -1f;
            }
        }

        void OnApplicationQuit()
        {
            float now = Time.realtimeSinceStartup;
            if (_silentSince >= 0f && now - _silentSince >= GapSeconds) _gaps++;
            Debug.Log($"{LogTag} t={now:F2} summary: {_starts} starts, {_stops} stops, {_gaps} silent gaps of {GapSeconds} s or more");
        }

        static string Where()
        {
            FlowDirector d = FlowDirector.Current;
            var w = d != null && d.Driver != null ? d.Driver.World : null;
            return w != null ? $"match={w.Match.Time:F1} flow={GameFlow.State}" : $"flow={GameFlow.State}";
        }

        static void Line(float now, string what, AudioClip clip, AudioSource src, float vol)
        {
            string name = clip != null ? clip.name : "one-shot";
            string on = src != null ? src.gameObject.name : "?";
            string group = src != null && src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "";
            Debug.Log($"{LogTag} t={now:F2} {Where()} {what} {Kind(on + " " + group, name)} {name} on {on}{(group.Length > 0 ? " (" + group + ")" : "")} vol {vol:F2}");
        }

        /// <summary>"music" for the music director's sources (or the Music mixer group), "voice" for the announcer, "ui" for
        /// menu sounds, else "sound" (pure; tests). sourceName may carry the mixer group name after a space.</summary>
        public static string Kind(string sourceName, string clipName)
        {
            string s = ((sourceName ?? "") + " " + (clipName ?? "")).ToLowerInvariant();
            if (s.Contains("music") || s.Contains("sting")) return "music";
            if (s.Contains("announcer") || s.Contains("voice")) return "voice";
            if (s.Contains("ui ") || s.StartsWith("ui", StringComparison.Ordinal) || s.Contains("menu")) return "ui";
            return "sound";
        }

        /// <summary>Measures what the listener hears: RMS of each output buffer (audio thread; one float, no allocation).</summary>
        public sealed class ListenerTap : MonoBehaviour
        {
            volatile float _rms;
            public float Rms => _rms;

            void OnAudioFilterRead(float[] data, int channels)
            {
                double sum = 0;
                for (int i = 0; i < data.Length; i++) sum += data[i] * data[i];
                _rms = data.Length > 0 ? (float)Math.Sqrt(sum / data.Length) : 0f;
            }
        }
    }
}
