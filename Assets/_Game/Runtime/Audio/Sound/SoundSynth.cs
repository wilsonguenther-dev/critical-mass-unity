using System;
using System.Collections.Generic;
using UnityEngine;

namespace CriticalMass.Runtime.Audio.Sound
{
    /// <summary>The oscillator or noise source of one synth layer.</summary>
    public enum Wave : byte { Sine, Triangle, Square, Saw, Noise, Brown, Crackle }

    /// <summary>
    /// One layer of a procedural sound: a source (oscillator with an exponential pitch sweep, white or brown noise, or a
    /// crackle of random impulses), an attack / exponential-decay envelope, an optional amplitude wobble, and one-pole
    /// low-pass (swept) and high-pass filters. Times are seconds, frequencies Hz.
    /// </summary>
    public struct SynthLayer
    {
        public Wave Wave;
        public float Start, Length;
        /// <summary>Pitch sweep start and end (Hz). For Crackle, F0 is impulses per second and F1 is unused.</summary>
        public float F0, F1;
        /// <summary>Linear attack seconds, then exponential decay with this time constant (seconds; 0 = hold).</summary>
        public float Attack, Decay;
        public float Gain;
        /// <summary>Low-pass cutoff sweep (Hz; 0 = open) and high-pass cutoff (Hz; 0 = off).</summary>
        public float Lp0, Lp1, Hp;
        /// <summary>Vibrato depth (fraction of the pitch) and rate (Hz).</summary>
        public float Fm, FmRate;
        /// <summary>Amplitude wobble depth (0..1) and rate (Hz): wind gusts, tremolo.</summary>
        public float Am, AmRate;
    }

    /// <summary>A named recipe: the layers, total length, whether it loops, and a soft-clip drive (0 = none).</summary>
    public sealed class SynthRecipe
    {
        public string Id;
        public float Length;
        public bool Loop;
        public float Drive;
        public SynthLayer[] Layers;
    }

    /// <summary>
    /// A small deterministic synthesiser (no third-party code, no files): every cue that has no recorded sound is rendered
    /// from a recipe once, at load, and cached as an AudioClip. The same recipe and variant always produce the same
    /// samples on every machine (integer xorshift noise seeded from the recipe id, no System.Random, no time). Variants
    /// nudge pitch and length a few percent and reseed the noise, so a cue never repeats the exact same waveform twice in
    /// a row. Rendering allocates (at load only); playing never does.
    /// </summary>
    public static class SoundSynth
    {
        public const int SampleRate = 24000;
        public const float PeakTarget = 0.89f;
        /// <summary>Loop recipes blend their tail into their head over this many seconds (seamless loops).</summary>
        public const float LoopBlend = 0.25f;

        private static readonly Dictionary<long, AudioClip> _clips = new Dictionary<long, AudioClip>();

        /// <summary>Clips rendered since start (tests, load report).</summary>
        public static int Rendered => _clips.Count;

        /// <summary>FNV-1a 32 of a string (stable across runs and platforms).</summary>
        public static uint Fnv(string s)
        {
            uint h = 2166136261u;
            if (s == null) return h;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= s[i];
                h *= 16777619u;
            }
            return h;
        }

        /// <summary>Deterministic 0..1 from a seed and a channel.</summary>
        public static float Unit(uint seed, uint channel)
        {
            uint h = seed ^ (channel * 0x9E3779B1u);
            h ^= h >> 16; h *= 0x7FEB352Du; h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777215f;
        }

        /// <summary>Renders recipe r, variant v, at the given rate (pure; allocates the result).</summary>
        public static float[] Render(SynthRecipe r, int variant, int sampleRate = SampleRate)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            uint seed = Fnv(r.Id) ^ ((uint)variant * 0x632BE5ABu + 0x5BD1E995u);
            // Variant 0 is the recipe as written; others drift pitch by up to 6% and length by up to 4%.
            float pitch = variant == 0 ? 1f : 1f + (Unit(seed, 1) * 2f - 1f) * 0.06f;
            float stretch = variant == 0 || r.Loop ? 1f : 1f + (Unit(seed, 2) * 2f - 1f) * 0.04f;
            float length = Mathf.Max(0.02f, r.Length * stretch);
            int n = Mathf.Max(1, Mathf.RoundToInt(length * sampleRate));
            int blend = r.Loop ? Mathf.Min(n / 2, Mathf.RoundToInt(LoopBlend * sampleRate)) : 0;
            var buf = new float[n + blend];
            if (r.Layers != null)
                for (int k = 0; k < r.Layers.Length; k++)
                    AddLayer(buf, r.Layers[k], sampleRate, pitch, stretch, seed + (uint)k * 0x27D4EB2Fu, r.Loop);

            if (r.Drive > 0f)
            {
                float norm = 1f / (float)Math.Tanh(r.Drive);
                for (int i = 0; i < buf.Length; i++) buf[i] = (float)Math.Tanh(buf[i] * r.Drive) * norm;
            }

            float[] outp;
            if (blend > 0)
            {
                outp = new float[n];
                Array.Copy(buf, outp, n);
                for (int i = 0; i < blend; i++)
                {
                    float t = (float)i / blend;
                    outp[i] = buf[n + i] * (1f - t) + buf[i] * t;
                }
            }
            else outp = buf;

            float peak = 0f;
            for (int i = 0; i < outp.Length; i++)
            {
                float a = outp[i] < 0 ? -outp[i] : outp[i];
                if (a > peak) peak = a;
            }
            if (peak > 1e-6f)
            {
                float g = PeakTarget / peak;
                for (int i = 0; i < outp.Length; i++) outp[i] *= g;
            }
            if (!r.Loop)
            {
                // 4 ms fade at both ends: no click whatever the layers do.
                int f = Mathf.Min(outp.Length / 2, sampleRate / 250);
                for (int i = 0; i < f; i++)
                {
                    float t = (float)i / f;
                    outp[i] *= t;
                    outp[outp.Length - 1 - i] *= t;
                }
            }
            return outp;
        }

        private static void AddLayer(float[] buf, in SynthLayer l, int sr, float pitch, float stretch, uint seed, bool loop)
        {
            int start = Mathf.Clamp(Mathf.RoundToInt(l.Start * stretch * sr), 0, buf.Length);
            float len = l.Length > 0 ? l.Length * stretch : buf.Length / (float)sr - l.Start * stretch;
            int count = Mathf.Clamp(Mathf.RoundToInt(len * sr), 0, buf.Length - start);
            if (count <= 0) return;
            uint rng = seed | 1u;
            double phase = Unit(seed, 7);
            float f0 = Mathf.Max(0.01f, l.F0 * pitch), f1 = Mathf.Max(0.01f, (l.F1 > 0 ? l.F1 : l.F0) * pitch);
            float lp0 = l.Lp0 > 0 ? l.Lp0 : 0f, lp1 = l.Lp1 > 0 ? l.Lp1 : lp0;
            double twoPiOverSr = 2.0 * Math.PI / sr;
            float lpY = 0f, hpY = 0f, hpX = 0f, brown = 0f, crackle = 0f, alpha = 1f, hpA = 0f;
            if (l.Hp > 0f) { double rc = 1.0 / (2.0 * Math.PI * l.Hp); hpA = (float)(rc / (rc + 1.0 / sr)); }
            float crackleDecay = (float)Math.Exp(-1.0 / (0.004 * sr));
            float fadeTail = 0.005f * sr;
            // Exponential sweep by a constant per-sample ratio (no Pow per sample).
            double freq = f0, ratio = count > 1 ? Math.Pow(f1 / f0, 1.0 / (count - 1)) : 1.0;
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)sr;
                float u = count > 1 ? i / (float)(count - 1) : 0f;
                float x;
                switch (l.Wave)
                {
                    case Wave.Noise: x = Noise(ref rng); break;
                    case Wave.Brown:
                        brown = (brown + 0.02f * Noise(ref rng)) / 1.02f;
                        x = brown * 3.5f;
                        break;
                    case Wave.Crackle:
                        crackle *= crackleDecay;
                        if ((Next(ref rng) & 0xFFFFFF) / 16777215f < l.F0 / sr)
                            crackle = (Noise(ref rng) < 0 ? -1f : 1f) * (0.4f + 0.6f * ((Next(ref rng) & 0xFFFF) / 65535f));
                        x = crackle;
                        break;
                    default:
                    {
                        double f = freq;
                        freq *= ratio;
                        if (l.Fm > 0f) f *= 1.0 + l.Fm * Math.Sin(2.0 * Math.PI * l.FmRate * t);
                        phase += f / sr;
                        phase -= Math.Floor(phase);
                        x = Osc(l.Wave, (float)phase);
                        break;
                    }
                }
                if (lp0 > 0f)
                {
                    if ((i & 31) == 0)
                    {
                        double fc = lp0 * Math.Pow(lp1 / lp0, u);
                        alpha = (float)(1.0 - Math.Exp(-twoPiOverSr * Math.Min(fc, sr * 0.45)));
                    }
                    lpY += alpha * (x - lpY);
                    x = lpY;
                }
                if (hpA > 0f)
                {
                    float y = hpA * (hpY + x - hpX);
                    hpX = x;
                    hpY = y;
                    x = y;
                }
                float env;
                if (loop && l.Attack <= 0f && l.Decay <= 0f) env = 1f;
                else
                {
                    env = l.Attack > 0f && t < l.Attack ? t / l.Attack
                        : l.Decay > 0f ? (float)Math.Exp(-(t - l.Attack) / l.Decay) : 1f;
                    if (!loop) env *= Mathf.Min(1f, (count - i) / fadeTail);
                }
                if (l.Am > 0f) env *= 1f - l.Am * 0.5f * (1f - (float)Math.Cos(2.0 * Math.PI * l.AmRate * t));
                buf[start + i] += x * env * l.Gain;
            }
        }

        private static uint Next(ref uint s)
        {
            s ^= s << 13;
            s ^= s >> 17;
            s ^= s << 5;
            return s;
        }

        private static float Noise(ref uint s) => (Next(ref s) & 0xFFFFFF) / 8388607.5f - 1f;

        private static float Osc(Wave w, float p)
        {
            switch (w)
            {
                case Wave.Triangle: return p < 0.5f ? 4f * p - 1f : 3f - 4f * p;
                case Wave.Square: return p < 0.5f ? 0.8f : -0.8f;
                case Wave.Saw: return 2f * p - 1f;
                default: return Mathf.Sin(p * 2f * Mathf.PI);
            }
        }

        /// <summary>The cached clip for a recipe id and variant, rendered on first use (call at load, never per hit).</summary>
        public static AudioClip Clip(string recipeId, int variant)
        {
            long key = ((long)Fnv(recipeId) << 8) | (uint)(variant & 0xFF);
            if (_clips.TryGetValue(key, out AudioClip c) && c != null) return c;
            SynthRecipe r = SynthRecipes.Find(recipeId);
            if (r == null) return null;
            float[] data = Render(r, variant);
            c = AudioClip.Create("synth_" + recipeId + "_" + variant, data.Length, 1, SampleRate, false);
            c.SetData(data, 0);
            c.hideFlags = HideFlags.DontSave;
            _clips[key] = c;
            return c;
        }
    }

    /// <summary>
    /// Every procedural recipe (data). Each one is a stand-in until a recorded sound replaces it; SoundTable lists which
    /// cues use them. Monster sounds are built from saws, noise and sub sines: nothing imitates a person.
    /// </summary>
    public static class SynthRecipes
    {
        private static SynthLayer L(Wave w, float start, float len, float f0, float f1, float atk, float dec, float gain,
            float lp0 = 0f, float lp1 = 0f, float hp = 0f, float fm = 0f, float fmRate = 0f, float am = 0f, float amRate = 0f) =>
            new SynthLayer { Wave = w, Start = start, Length = len, F0 = f0, F1 = f1, Attack = atk, Decay = dec, Gain = gain, Lp0 = lp0, Lp1 = lp1, Hp = hp, Fm = fm, FmRate = fmRate, Am = am, AmRate = amRate };

        private static SynthRecipe R(string id, float len, float drive, params SynthLayer[] layers) =>
            new SynthRecipe { Id = id, Length = len, Drive = drive, Layers = layers };

        private static SynthRecipe Loop(string id, float len, params SynthLayer[] layers) =>
            new SynthRecipe { Id = id, Length = len, Loop = true, Layers = layers };

        private const Wave Si = Wave.Sine, Tr = Wave.Triangle, Sq = Wave.Square, Sw = Wave.Saw, Nz = Wave.Noise, Br = Wave.Brown, Cr = Wave.Crackle;

        public static readonly SynthRecipe[] All =
        {
            // ---- movement and swings
            R("whoosh_heavy", 0.34f, 0f, L(Nz, 0, 0.32f, 1, 1, 0.12f, 0.08f, 1f, 500, 2600, 150), L(Si, 0, 0.3f, 140, 70, 0.1f, 0.1f, 0.15f)),
            R("whoosh_mid", 0.28f, 0f, L(Nz, 0, 0.26f, 1, 1, 0.09f, 0.06f, 1f, 800, 3500, 250)),
            R("whoosh_light", 0.22f, 0f, L(Nz, 0, 0.2f, 1, 1, 0.07f, 0.05f, 1f, 1200, 5000, 400)),
            R("grunt", 0.2f, 0.8f, L(Nz, 0, 0.18f, 1, 1, 0.01f, 0.06f, 0.8f, 900, 500, 200), L(Sw, 0, 0.16f, 150, 110, 0.01f, 0.05f, 0.25f, 700)),
            R("jump", 0.14f, 0f, L(Nz, 0, 0.12f, 1, 1, 0.005f, 0.04f, 0.6f, 2000, 800, 300), L(Si, 0, 0.1f, 220, 330, 0.005f, 0.04f, 0.2f)),
            R("land_light", 0.18f, 0f, L(Si, 0, 0.15f, 120, 60, 0.002f, 0.04f, 1f), L(Nz, 0, 0.1f, 1, 1, 0.001f, 0.03f, 0.5f, 1500, 400)),
            R("land_heavy", 0.32f, 1.2f, L(Si, 0, 0.3f, 90, 40, 0.002f, 0.08f, 1f), L(Nz, 0, 0.25f, 1, 1, 0.001f, 0.06f, 0.6f, 1200, 200), L(Br, 0, 0.3f, 1, 1, 0.002f, 0.1f, 0.5f, 300)),
            R("step_dirt", 0.15f, 0f, L(Nz, 0, 0.14f, 1, 1, 0.002f, 0.03f, 0.8f, 1200, 500, 100), L(Br, 0, 0.14f, 1, 1, 0.002f, 0.04f, 0.4f, 300)),
            R("step_water", 0.2f, 0f, L(Nz, 0, 0.18f, 1, 1, 0.003f, 0.05f, 0.7f, 4000, 1500, 400), L(Cr, 0, 0.18f, 400, 0, 0.003f, 0.06f, 0.3f, 6000, 0, 2000)),
            R("step_weight", 0.16f, 0f, L(Si, 0, 0.15f, 90, 55, 0.002f, 0.05f, 0.8f)),
            R("fall", 0.62f, 0f, L(Nz, 0, 0.4f, 1, 1, 0.05f, 0.2f, 0.6f, 2000, 500, 200), L(Si, 0.35f, 0.25f, 100, 50, 0.002f, 0.06f, 0.8f)),
            R("splash", 0.6f, 0f, L(Nz, 0, 0.5f, 1, 1, 0.005f, 0.12f, 0.9f, 6000, 1200, 300), L(Cr, 0, 0.5f, 300, 0, 0.005f, 0.2f, 0.4f, 7000, 0, 1500)),

            // ---- hits by material
            R("hit_crit_crack", 0.25f, 1.5f, L(Nz, 0, 0.12f, 1, 1, 0.001f, 0.03f, 1f, 8000, 2000, 500), L(Cr, 0, 0.22f, 500, 0, 0.001f, 0.06f, 0.6f, 7000, 0, 800)),
            R("hit_stone", 0.26f, 0f, L(Nz, 0, 0.18f, 1, 1, 0.001f, 0.025f, 1f, 4000, 1200, 300), L(Si, 0, 0.15f, 420, 300, 0.001f, 0.03f, 0.4f), L(Cr, 0, 0.25f, 400, 0, 0.001f, 0.06f, 0.5f, 5000)),
            R("hit_wood", 0.2f, 0f, L(Tr, 0, 0.18f, 260, 180, 0.001f, 0.05f, 0.8f), L(Nz, 0, 0.12f, 1, 1, 0.001f, 0.03f, 0.5f, 2500, 800, 200)),
            R("kaiju_hurt", 0.3f, 1f, L(Si, 0, 0.25f, 110, 70, 0.002f, 0.06f, 1f), L(Nz, 0, 0.2f, 1, 1, 0.002f, 0.05f, 0.7f, 900, 300), L(Br, 0, 0.28f, 1, 1, 0.002f, 0.08f, 0.4f, 400)),
            R("death_ko", 0.45f, 1.2f, L(Si, 0, 0.4f, 80, 40, 0.002f, 0.1f, 1f), L(Nz, 0, 0.3f, 1, 1, 0.001f, 0.07f, 0.6f, 1800, 300), L(Cr, 0, 0.3f, 300, 0, 0.001f, 0.08f, 0.3f, 3000)),

            // ---- demolitions
            R("blast_near", 1.6f, 2.5f, L(Si, 0, 1.2f, 70, 30, 0.003f, 0.35f, 1f), L(Br, 0, 1.6f, 1, 1, 0.002f, 0.45f, 1f, 1200, 150),
                L(Nz, 0, 0.5f, 1, 1, 0.001f, 0.08f, 0.9f, 9000, 1500, 200), L(Cr, 0.05f, 1.35f, 120, 0, 0.01f, 0.4f, 0.5f, 6000)),
            R("blast_far", 2.2f, 1.5f, L(Si, 0, 1.6f, 55, 28, 0.02f, 0.5f, 1f), L(Br, 0, 2.2f, 1, 1, 0.03f, 0.7f, 1f, 500, 120), L(Nz, 0, 0.4f, 1, 1, 0.01f, 0.1f, 0.3f, 1200, 400)),
            R("blast_mine", 1.2f, 2f, L(Nz, 0, 0.3f, 1, 1, 0.001f, 0.05f, 1f, 10000, 2000), L(Si, 0, 0.8f, 90, 35, 0.002f, 0.22f, 1f),
                L(Br, 0, 1.2f, 1, 1, 0.002f, 0.3f, 0.8f, 900, 150), L(Cr, 0.02f, 1f, 200, 0, 0.01f, 0.25f, 0.5f, 4000)),
            R("debris", 1.2f, 0f, L(Cr, 0, 1.2f, 60, 0, 0.05f, 0.4f, 1f, 5000, 0, 300), L(Nz, 0, 1f, 1, 1, 0.05f, 0.3f, 0.2f, 3000)),
            Loop("fuse_loop", 1.0f, L(Nz, 0, 0, 1, 1, 0, 0, 0.5f, 9000, 0, 3000), L(Cr, 0, 0, 90, 0, 0, 0, 0.8f, 8000, 0, 1500)),
            R("throw", 0.24f, 0f, L(Nz, 0, 0.22f, 1, 1, 0.05f, 0.06f, 1f, 1500, 4000, 400), L(Tr, 0, 0.2f, 300, 500, 0.05f, 0.06f, 0.1f)),
            R("mine_place", 0.2f, 0f, L(Tr, 0, 0.05f, 900, 900, 0.001f, 0.02f, 0.6f), L(Nz, 0, 0.15f, 1, 1, 0.002f, 0.04f, 0.5f, 2000), L(Sq, 0.06f, 0.05f, 400, 400, 0.001f, 0.02f, 0.2f, 3000)),
            R("mine_arm", 0.24f, 0f, L(Sq, 0, 0.07f, 1320, 1320, 0.002f, 0.5f, 0.5f, 5000), L(Sq, 0.12f, 0.07f, 1760, 1760, 0.002f, 0.5f, 0.5f, 5000)),
            R("mine_trigger", 0.16f, 0f, L(Sq, 0, 0.05f, 2200, 2200, 0.001f, 0.03f, 0.6f, 6000), L(Tr, 0.05f, 0.08f, 600, 200, 0.001f, 0.03f, 0.5f)),
            R("mine_fizzle", 0.42f, 0f, L(Nz, 0, 0.4f, 1, 1, 0.01f, 0.15f, 0.5f, 3000, 800, 500)),

            // ---- the Kaiju (saws, noise and sub: no animal or person is imitated)
            R("kaiju_roar", 2.4f, 3f, L(Sw, 0, 2.2f, 75, 52, 0.25f, 1.2f, 0.9f, 900, 500, 0, 0.08f, 7f), L(Nz, 0, 2.2f, 1, 1, 0.2f, 1f, 0.7f, 1400, 600, 120),
                L(Sq, 0, 2f, 38, 30, 0.3f, 1.2f, 0.5f, 300), L(Br, 0, 2.2f, 1, 1, 0.2f, 1.2f, 0.6f, 250)),
            R("kaiju_growl", 1.2f, 2.5f, L(Sw, 0, 1.1f, 85, 65, 0.12f, 0.5f, 0.8f, 800, 450, 0, 0.1f, 8f), L(Nz, 0, 1f, 1, 1, 0.1f, 0.45f, 0.5f, 1200, 500, 120), L(Br, 0, 1.1f, 1, 1, 0.1f, 0.5f, 0.5f, 250)),
            R("kaiju_hatch", 3f, 2.5f, L(Cr, 0, 0.6f, 300, 0, 0.002f, 0.2f, 0.9f, 7000), L(Nz, 0, 0.3f, 1, 1, 0.001f, 0.06f, 0.8f, 8000, 2000),
                L(Sw, 0.5f, 1.9f, 90, 55, 0.2f, 1.2f, 0.8f, 1000, 500, 0, 0.1f, 7f), L(Si, 0, 1f, 60, 30, 0.002f, 0.3f, 1f), L(Br, 0.4f, 2.2f, 1, 1, 0.2f, 1.2f, 0.7f, 300)),
            R("kaiju_windup", 0.9f, 1.5f, L(Nz, 0, 0.88f, 1, 1, 0.7f, 0.15f, 0.8f, 400, 2500, 150), L(Sw, 0, 0.85f, 60, 120, 0.7f, 0.1f, 0.5f, 500, 1200, 0, 0.05f, 9f), L(Si, 0, 0.85f, 45, 70, 0.6f, 0.15f, 0.4f)),
            R("kaiju_slam_tele", 1.4f, 1f, L(Br, 0, 1.4f, 1, 1, 1.1f, 0.2f, 1f, 150, 400), L(Si, 0, 1.3f, 30, 50, 1f, 0.3f, 0.6f), L(Cr, 0, 1.3f, 30, 0, 1f, 0.3f, 0.3f, 2000)),
            R("kaiju_slam", 2f, 3f, L(Si, 0, 1.5f, 60, 24, 0.002f, 0.5f, 1f), L(Br, 0, 2f, 1, 1, 0.002f, 0.6f, 1f, 900, 100),
                L(Nz, 0, 0.35f, 1, 1, 0.001f, 0.08f, 0.7f, 6000, 800), L(Cr, 0.05f, 1.55f, 80, 0, 0.01f, 0.5f, 0.6f, 4000)),
            R("kaiju_step", 0.7f, 1.2f, L(Si, 0, 0.6f, 55, 30, 0.003f, 0.18f, 1f), L(Br, 0, 0.7f, 1, 1, 0.003f, 0.2f, 0.8f, 300, 120), L(Cr, 0, 0.5f, 40, 0, 0.003f, 0.2f, 0.2f, 2500)),
            R("kaiju_ko", 2f, 2f, L(Si, 0, 1.6f, 70, 28, 0.002f, 0.5f, 1f), L(Br, 0, 2f, 1, 1, 0.002f, 0.7f, 0.9f, 600, 100), L(Sw, 0, 1.6f, 60, 35, 0.05f, 0.6f, 0.4f, 500), L(Nz, 0, 0.5f, 1, 1, 0.001f, 0.15f, 0.5f, 3000, 300)),
            Loop("berserk_loop", 2f, L(Sw, 0, 0, 48, 48, 0, 0, 0.6f, 400, 0, 0, 0.04f, 5.5f), L(Br, 0, 0, 1, 1, 0, 0, 0.7f, 200), L(Sq, 0, 0, 24, 24, 0, 0, 0.35f, 160), L(Nz, 0, 0, 1, 1, 0, 0, 0.15f, 700, 0, 200, 0, 0, 0.5f, 1f)),

            // ---- egg, corruption, shrine
            R("egg_feed", 0.32f, 0f, L(Si, 0, 0.25f, 520, 780, 0.005f, 0.12f, 0.6f), L(Si, 0.06f, 0.25f, 780, 1040, 0.005f, 0.12f, 0.5f), L(Nz, 0, 0.1f, 1, 1, 0.002f, 0.03f, 0.2f, 1500)),
            R("corrupt", 0.8f, 1.2f, L(Sw, 0, 0.75f, 110, 70, 0.01f, 0.4f, 0.6f, 800, 0, 0, 0.1f, 4f), L(Nz, 0, 0.6f, 1, 1, 0.01f, 0.3f, 0.4f, 600, 0, 100), L(Si, 0, 0.75f, 55, 55, 0.01f, 0.4f, 0.5f)),
            R("intercept", 0.22f, 0f, L(Tr, 0, 0.2f, 300, 120, 0.001f, 0.08f, 0.7f), L(Nz, 0, 0.15f, 1, 1, 0.001f, 0.05f, 0.5f, 2000, 600)),
            R("cleanse", 1.2f, 0f, L(Si, 0, 1.1f, 880, 880, 0.1f, 0.6f, 0.4f, 0, 0, 0, 0.01f, 6f), L(Si, 0, 1.1f, 1320, 1320, 0.15f, 0.6f, 0.3f), L(Nz, 0, 1f, 1, 1, 0.2f, 0.5f, 0.15f, 10000, 0, 4000)),
            R("purify", 1f, 0f, L(Si, 0, 0.9f, 660, 1320, 0.05f, 0.5f, 0.5f), L(Si, 0, 0.9f, 990, 1980, 0.05f, 0.5f, 0.3f), L(Nz, 0, 0.8f, 1, 1, 0.1f, 0.4f, 0.12f, 10000, 0, 5000)),

            // ---- healer
            R("heal_cast", 0.8f, 0f, L(Si, 0, 0.5f, 660, 660, 0.005f, 0.25f, 0.5f), L(Si, 0.08f, 0.5f, 880, 880, 0.005f, 0.25f, 0.45f), L(Si, 0.16f, 0.6f, 1320, 1320, 0.005f, 0.3f, 0.4f), L(Nz, 0, 0.5f, 1, 1, 0.01f, 0.2f, 0.1f, 0, 0, 5000)),
            R("heal_pulse", 1.4f, 0f, L(Si, 0, 1.3f, 523, 523, 0.05f, 0.6f, 0.35f), L(Si, 0, 1.3f, 659, 659, 0.05f, 0.6f, 0.3f), L(Si, 0, 1.3f, 784, 784, 0.05f, 0.6f, 0.3f),
                L(Si, 0, 1.3f, 1046, 1046, 0.05f, 0.6f, 0.25f), L(Si, 0, 1.3f, 261, 261, 0.05f, 0.6f, 0.3f), L(Nz, 0, 1.2f, 1, 1, 0.1f, 0.5f, 0.2f, 9000, 0, 3000)),
            R("heal_landed", 0.36f, 0f, L(Si, 0, 0.25f, 1568, 2093, 0.003f, 0.1f, 0.5f), L(Si, 0.05f, 0.3f, 2093, 2093, 0.003f, 0.12f, 0.35f)),
            R("heal_tick", 0.16f, 0f, L(Si, 0, 0.15f, 1200, 1500, 0.003f, 0.05f, 0.3f)),

            // ---- builder pieces and terrain
            R("build_span", 0.46f, 0f, L(Tr, 0, 0.12f, 220, 200, 0.001f, 0.05f, 0.7f), L(Tr, 0.1f, 0.12f, 240, 210, 0.001f, 0.05f, 0.7f), L(Tr, 0.2f, 0.14f, 200, 180, 0.001f, 0.05f, 0.7f),
                L(Nz, 0, 0.35f, 1, 1, 0.001f, 0.03f, 0.3f, 2000)),
            R("build_bridge", 0.62f, 0f, L(Sw, 0, 0.4f, 180, 150, 0.05f, 0.15f, 0.25f, 900, 0, 300, 0.03f, 8f), L(Tr, 0.1f, 0.12f, 230, 200, 0.001f, 0.05f, 0.6f),
                L(Tr, 0.22f, 0.12f, 250, 210, 0.001f, 0.05f, 0.6f), L(Tr, 0.34f, 0.12f, 220, 190, 0.001f, 0.05f, 0.6f), L(Tr, 0.46f, 0.12f, 240, 200, 0.001f, 0.05f, 0.6f)),
            R("build_warn", 0.9f, 0f, L(Sw, 0, 0.85f, 140, 110, 0.1f, 0.3f, 0.5f, 1200, 0, 400, 0.06f, 11f), L(Cr, 0, 0.85f, 50, 0, 0.1f, 0.4f, 0.3f, 4000, 0, 800)),
            R("piece_expire", 0.9f, 0f, L(Cr, 0, 0.85f, 120, 0, 0.005f, 0.3f, 0.9f, 4500, 0, 300), L(Tr, 0, 0.3f, 180, 90, 0.002f, 0.1f, 0.5f), L(Nz, 0, 0.6f, 1, 1, 0.005f, 0.2f, 0.4f, 2000, 400)),
            R("pickup_lift", 0.2f, 0f, L(Tr, 0, 0.18f, 200, 320, 0.003f, 0.06f, 0.6f), L(Nz, 0, 0.12f, 1, 1, 0.002f, 0.05f, 0.3f, 1500)),
            R("dig_dirt", 0.26f, 0f, L(Nz, 0, 0.24f, 1, 1, 0.002f, 0.06f, 0.8f, 1800, 600, 150), L(Br, 0, 0.24f, 1, 1, 0.002f, 0.08f, 0.5f, 400)),
            R("place_block", 0.2f, 0f, L(Si, 0, 0.18f, 140, 90, 0.002f, 0.05f, 0.8f), L(Nz, 0, 0.15f, 1, 1, 0.002f, 0.04f, 0.6f, 1200)),
            R("topple_creak", 1.2f, 0f, L(Sw, 0, 1.1f, 90, 60, 0.2f, 0.5f, 0.6f, 700, 0, 120, 0.05f, 6f), L(Cr, 0, 1.1f, 70, 0, 0.2f, 0.5f, 0.4f, 3000), L(Nz, 0.4f, 0.75f, 1, 1, 0.4f, 0.3f, 0.3f, 1500, 600)),
            R("topple_crash", 1.6f, 2f, L(Si, 0, 1.2f, 70, 30, 0.002f, 0.3f, 1f), L(Br, 0, 1.6f, 1, 1, 0.002f, 0.4f, 1f, 1000, 150), L(Nz, 0, 0.4f, 1, 1, 0.001f, 0.06f, 0.8f, 7000, 900),
                L(Cr, 0, 1.5f, 150, 0, 0.005f, 0.5f, 0.7f, 5000)),

            // ---- pickups, progression, objective
            R("pickup_meat", 0.22f, 0f, L(Tr, 0, 0.12f, 300, 200, 0.002f, 0.05f, 0.6f), L(Si, 0.04f, 0.16f, 600, 900, 0.002f, 0.06f, 0.4f)),
            R("pickup_relic", 0.72f, 0f, L(Si, 0, 0.3f, 784, 784, 0.003f, 0.15f, 0.45f), L(Si, 0.07f, 0.3f, 988, 988, 0.003f, 0.15f, 0.45f),
                L(Si, 0.14f, 0.3f, 1175, 1175, 0.003f, 0.15f, 0.45f), L(Si, 0.21f, 0.45f, 1568, 1568, 0.003f, 0.2f, 0.45f), L(Nz, 0, 0.6f, 1, 1, 0.01f, 0.3f, 0.1f, 0, 0, 6000)),
            R("level_up", 1.4f, 0f, L(Tr, 0, 0.3f, 523, 523, 0.003f, 0.25f, 0.5f), L(Tr, 0.08f, 0.3f, 659, 659, 0.003f, 0.25f, 0.5f), L(Tr, 0.16f, 0.3f, 784, 784, 0.003f, 0.25f, 0.5f),
                L(Tr, 0.24f, 0.3f, 1046, 1046, 0.003f, 0.25f, 0.5f), L(Tr, 0.32f, 0.3f, 1318, 1318, 0.003f, 0.25f, 0.5f),
                L(Si, 0.4f, 1f, 1046, 1046, 0.02f, 0.6f, 0.3f), L(Si, 0.4f, 1f, 1318, 1318, 0.02f, 0.6f, 0.3f), L(Si, 0.4f, 1f, 1568, 1568, 0.02f, 0.6f, 0.3f)),
            R("hill_fanfare", 1f, 0f, L(Tr, 0, 0.3f, 523, 523, 0.003f, 0.3f, 0.5f), L(Tr, 0.09f, 0.3f, 659, 659, 0.003f, 0.3f, 0.5f), L(Tr, 0.18f, 0.3f, 784, 784, 0.003f, 0.3f, 0.5f), L(Tr, 0.27f, 0.7f, 1046, 1046, 0.003f, 0.4f, 0.5f)),
            R("hill_lost", 0.9f, 0f, L(Tr, 0, 0.3f, 784, 784, 0.003f, 0.25f, 0.5f), L(Tr, 0.12f, 0.3f, 659, 659, 0.003f, 0.25f, 0.5f), L(Tr, 0.24f, 0.6f, 523, 494, 0.003f, 0.35f, 0.5f), L(Sw, 0.24f, 0.6f, 131, 123, 0.01f, 0.35f, 0.2f, 1200)),
            R("hill_contested", 0.56f, 0f, L(Sq, 0, 0.1f, 660, 660, 0.002f, 0.08f, 0.35f, 2500), L(Sq, 0.12f, 0.1f, 550, 550, 0.002f, 0.08f, 0.35f, 2500), L(Sq, 0.24f, 0.1f, 660, 660, 0.002f, 0.08f, 0.35f, 2500), L(Sq, 0.36f, 0.1f, 550, 550, 0.002f, 0.08f, 0.35f, 2500)),

            // ---- pings and map
            R("ping_attack", 0.3f, 0f, L(Sq, 0, 0.09f, 1046, 1046, 0.002f, 0.08f, 0.4f, 4000), L(Sq, 0.11f, 0.12f, 1046, 1046, 0.002f, 0.08f, 0.4f, 4000)),
            R("ping_onmyway", 0.3f, 0f, L(Si, 0, 0.28f, 784, 1175, 0.003f, 0.15f, 0.5f)),
            R("ping_defend", 0.46f, 0f, L(Tr, 0, 0.2f, 523, 523, 0.003f, 0.2f, 0.5f), L(Tr, 0.1f, 0.34f, 784, 784, 0.003f, 0.2f, 0.5f)),
            R("ping_help", 0.46f, 0f, L(Si, 0, 0.2f, 1175, 1175, 0.003f, 0.15f, 0.5f, 0, 0, 0, 0.02f, 8f), L(Si, 0.12f, 0.32f, 880, 880, 0.003f, 0.15f, 0.5f, 0, 0, 0, 0.02f, 8f)),
            R("map_open", 0.22f, 0f, L(Nz, 0, 0.2f, 1, 1, 0.05f, 0.06f, 0.4f, 4000, 1500, 800), L(Tr, 0.05f, 0.12f, 660, 660, 0.003f, 0.05f, 0.2f)),
            R("map_close", 0.22f, 0f, L(Nz, 0, 0.2f, 1, 1, 0.05f, 0.06f, 0.4f, 1500, 4000, 800), L(Tr, 0.05f, 0.12f, 495, 495, 0.003f, 0.05f, 0.2f)),

            // ---- big-moment stingers
            R("sting_takeover", 1.6f, 2f, L(Nz, 0, 0.55f, 1, 1, 0.5f, 0.05f, 0.6f, 500, 8000, 300), L(Si, 0.55f, 1f, 80, 35, 0.002f, 0.35f, 1f), L(Br, 0.55f, 1f, 1, 1, 0.002f, 0.4f, 0.8f, 800, 150),
                L(Sw, 0.55f, 1f, 220, 220, 0.01f, 0.5f, 0.3f, 2000, 800), L(Sw, 0.55f, 1f, 277, 277, 0.01f, 0.5f, 0.3f, 2000, 800), L(Sw, 0.55f, 1f, 330, 330, 0.01f, 0.5f, 0.3f, 2000, 800)),
            R("sting_small", 0.5f, 0f, L(Tr, 0, 0.3f, 880, 880, 0.003f, 0.15f, 0.5f), L(Tr, 0.06f, 0.35f, 1320, 1320, 0.003f, 0.15f, 0.5f), L(Si, 0, 0.3f, 110, 60, 0.002f, 0.1f, 0.5f)),
            R("sting_kaiju", 1.8f, 2.5f, L(Si, 0.4f, 1.4f, 50, 25, 0.002f, 0.6f, 1f), L(Sw, 0.4f, 1.4f, 55, 55, 0.05f, 0.7f, 0.5f, 600, 0, 0, 0.05f, 6f), L(Nz, 0, 0.6f, 1, 1, 0.4f, 0.2f, 0.4f, 300, 2000)),
            R("sting_heal", 1.4f, 0f, L(Si, 0, 1.3f, 523, 523, 0.02f, 0.7f, 0.35f), L(Si, 0.08f, 1.2f, 659, 659, 0.02f, 0.7f, 0.35f), L(Si, 0.16f, 1.1f, 784, 784, 0.02f, 0.7f, 0.35f), L(Si, 0.24f, 1.1f, 1046, 1046, 0.02f, 0.7f, 0.35f)),

            // ---- low-frequency layer (replaces camera shake with shake off)
            R("sub_huge", 1.4f, 0f, L(Si, 0, 1.4f, 48, 26, 0.005f, 0.5f, 1f)),
            R("sub_big", 0.9f, 0f, L(Si, 0, 0.9f, 55, 32, 0.004f, 0.3f, 1f)),
            R("sub_step", 0.45f, 0f, L(Si, 0, 0.45f, 45, 32, 0.004f, 0.12f, 1f)),
            R("sub_small", 0.35f, 0f, L(Si, 0, 0.35f, 70, 45, 0.003f, 0.1f, 1f)),

            // ---- ambience
            Loop("amb_wind", 6f, L(Nz, 0, 0, 1, 1, 0, 0, 0.7f, 500, 0, 80, 0, 0, 0.5f, 1f / 6f), L(Nz, 0, 0, 1, 1, 0, 0, 0.2f, 1400, 0, 600, 0, 0, 0.7f, 2f / 6f)),
            Loop("amb_water", 4f, L(Nz, 0, 0, 1, 1, 0, 0, 0.5f, 2500, 0, 300), L(Cr, 0, 0, 600, 0, 0, 0, 0.4f, 6000, 0, 1500), L(Br, 0, 0, 1, 1, 0, 0, 0.4f, 300)),
            R("amb_bird", 0.36f, 0f, L(Si, 0, 0.08f, 2800, 3600, 0.005f, 0.03f, 0.5f, 0, 0, 0, 0.05f, 30f), L(Si, 0.12f, 0.08f, 3000, 4000, 0.005f, 0.03f, 0.5f, 0, 0, 0, 0.05f, 30f),
                L(Si, 0.22f, 0.1f, 2600, 3400, 0.005f, 0.03f, 0.45f, 0, 0, 0, 0.05f, 30f)),
            R("amb_bird_trill", 0.42f, 0f, L(Si, 0, 0.4f, 3400, 3300, 0.02f, 0.15f, 0.45f, 0, 0, 0, 0.08f, 25f)),
        };

        private static Dictionary<string, SynthRecipe> _byId;

        public static SynthRecipe Find(string id)
        {
            if (id == null) return null;
            if (_byId == null)
            {
                var d = new Dictionary<string, SynthRecipe>(All.Length);
                foreach (SynthRecipe r in All) d[r.Id] = r;
                _byId = d;
            }
            return _byId.TryGetValue(id, out SynthRecipe x) ? x : null;
        }
    }
}
