using System;
using UnityEngine;

namespace CriticalMass.Runtime.Audio.Sound
{
    /// <summary>
    /// Player-experience wave (lane F, 2026-11-22): the local player's low-health heartbeat (pure; tests). On below
    /// OnBelow of max health, off above OffAbove (hysteresis, so a heal tick at the line never flickers it), off while
    /// dead. Rate: SlowHz at OnBelow rising linearly to FastHz at CriticalBelow and under. SoundFeed updates it every sim
    /// tick; SoundDirector plays one "heartbeat" lub-dub every 1 / Rate seconds and ducks the music slightly while it is on.
    /// </summary>
    public sealed class Heartbeat
    {
        public const float OnBelow = 0.30f, OffAbove = 0.35f, CriticalBelow = 0.15f, SlowHz = 1.2f, FastHz = 1.8f;
        /// <summary>The music's gain while the heartbeat is on (MusicDirector.LowHealthDuck): about -1.9 dB, "slightly".</summary>
        public const float MusicDuck = 0.8f;

        public bool On { get; private set; }
        /// <summary>Beats per second now (0 = off).</summary>
        public float Rate { get; private set; }
        /// <summary>Times it switched on (tests; each a fresh warning).</summary>
        public int Starts { get; private set; }

        public void Reset()
        {
            On = false;
            Rate = 0f;
        }

        public void Update(float healthShare, bool alive)
        {
            if (!alive) { On = false; Rate = 0f; return; }
            if (On) { if (healthShare > OffAbove) On = false; }
            else if (healthShare < OnBelow) { On = true; Starts++; }
            Rate = On ? RateFor(healthShare) : 0f;
        }

        /// <summary>The beat rate for a health share while on (1.2 Hz at 30% and above, 1.8 Hz at 15% and under).</summary>
        public static float RateFor(float healthShare)
        {
            if (healthShare <= CriticalBelow) return FastHz;
            if (healthShare >= OnBelow) return SlowHz;
            float t = (OnBelow - healthShare) / (OnBelow - CriticalBelow);
            return SlowHz + (FastHz - SlowHz) * t;
        }
    }

    /// <summary>
    /// Player-experience wave (lane F): sound preferences that are not volumes. HitConfirm: the hit-confirm tick on my
    /// landed hits (default on). The settings row that toggles it belongs to the settings lane (FINDINGS "Needs from other
    /// lanes"); the key is read once and cached, and Set writes it.
    /// </summary>
    public static class SoundPrefs
    {
        public const string HitConfirmKey = "cm.audio.hitconfirm";
        static int _hitConfirm = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _hitConfirm = -1;

        public static bool HitConfirm
        {
            get
            {
                if (_hitConfirm < 0)
                {
                    try { _hitConfirm = PlayerPrefs.GetInt(HitConfirmKey, 1) != 0 ? 1 : 0; }
                    catch (Exception) { _hitConfirm = 1; }   // off the main thread (never in game): the default
                }
                return _hitConfirm != 0;
            }
            set
            {
                _hitConfirm = value ? 1 : 0;
                PlayerPrefs.SetInt(HitConfirmKey, _hitConfirm);
                PlayerPrefs.Save();
            }
        }
    }
}
