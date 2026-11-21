using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.B16
{
    /// <summary>
    /// b16 [11] (physics audit item 11: a jump at the island edge was silently eaten). The sim raises SimEventType.JumpRefused
    /// once when a press runs out its buffer without a jump (Value 1: no clear landing, 2: past the coyote window). The view
    /// answers with three small cues, all view only: a stumble (a short sideways shake of the drawn body, StumbleSeconds,
    /// ActorViews adds Offset), the soft "refused" sound (SoundTable routes the event for the local player only), and a brief
    /// brass highlight on the ground at the edge in front of the local player (UnderRings draws Edge).
    /// Reads the sim's event ring lazily, once per tick, from the first caller of the tick. No per-frame allocation (arrays
    /// grow once per world).
    /// </summary>
    public static class JumpRefusedView
    {
        public const float StumbleSeconds = 0.3f, StumbleAmp = 0.06f, StumbleHz = 14f, EdgeSeconds = 0.6f, EdgeAhead = 0.6f;

        static World _w;
        static int _tick = int.MinValue, _head;
        static int[] _at = new int[0];
        static float[] _ex = new float[0], _ez = new float[0], _fx = new float[0], _fz = new float[0];

        /// <summary>Reads the JumpRefused events raised since the last call (once per tick).</summary>
        public static void Scan(World w)
        {
            if (w == null) return;
            if (!ReferenceEquals(w, _w))
            {
                _w = w;
                _head = w.EventHead;
                _tick = int.MinValue;
                int n = w.ActorCount;
                if (_at.Length < n) { _at = new int[n]; _ex = new float[n]; _ez = new float[n]; _fx = new float[n]; _fz = new float[n]; }
                for (int i = 0; i < _at.Length; i++) _at[i] = int.MinValue;
            }
            if (_tick == w.Tick) return;
            _tick = w.Tick;
            int head = w.EventHead;
            if (head - _head > World.EventCapacity) _head = head - World.EventCapacity;
            for (; _head < head; _head++)
            {
                SimEvent e = w.Events[_head % World.EventCapacity];
                if (e == null || e.Type != SimEventType.JumpRefused || e.A < 0 || e.A >= _at.Length) continue;
                int i = e.A;
                _at[i] = w.Tick;
                double f = i < w.ActorCount ? w.Actors[i].Facing : 0;
                _fx[i] = Mathf.Cos((float)f);
                _fz[i] = Mathf.Sin((float)f);
                _ex[i] = (float)(e.X + _fx[i] * EdgeAhead);
                _ez[i] = (float)(e.Z + _fz[i] * EdgeAhead);
            }
        }

        /// <summary>Seconds since actor i's last refused jump at the render alpha, or -1 (none, or older than the longest cue).</summary>
        public static float Age(World w, int i, double alpha)
        {
            Scan(w);
            if (i < 0 || i >= _at.Length || _at[i] == int.MinValue) return -1f;
            float age = (float)((w.Tick + alpha - _at[i]) / w.Config.SimHz);
            return age < 0f || age > EdgeSeconds ? -1f : age;
        }

        /// <summary>The stumble shake of actor i (sim units on the ground plane, plus a small dip), zero when none.</summary>
        public static Vector3 Offset(World w, int i, double alpha)
        {
            float age = Age(w, i, alpha);
            if (age < 0f || age >= StumbleSeconds) return Vector3.zero;
            float t = age / StumbleSeconds, k = (1f - t) * (1f - t);
            float s = StumbleAmp * k * Mathf.Sin(2f * Mathf.PI * StumbleHz * age);
            // across the facing (sim x / z), then into world space the way SimSpace maps a ground offset (x, -z)
            float ax = -_fz[i] * s, az = _fx[i] * s;
            return new Vector3(ax, -0.04f * k, -az);
        }

        /// <summary>The local player's edge highlight: sim (x, z) just past the edge it refused at, and its fade 1..0.</summary>
        public static bool Edge(World w, int i, double alpha, out double x, out double z, out float fade)
        {
            x = z = 0;
            fade = 0f;
            float age = Age(w, i, alpha);
            if (age < 0f || age >= EdgeSeconds) return false;
            x = _ex[i];
            z = _ez[i];
            float t = age / EdgeSeconds;
            fade = Mathf.Clamp01(t < 0.15f ? t / 0.15f : 1f - (t - 0.15f) / 0.85f);
            return fade > 0f;
        }
    }
}
