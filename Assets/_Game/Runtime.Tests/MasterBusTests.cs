using System;
using CriticalMass.Runtime.Audio;
using CriticalMass.Runtime.Audio.Sound;
using NUnit.Framework;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// CM b13 S0 (audio): the hatch moment's mix, rendered offline from the game's own synth cues at their table gains (the
    /// hatch cue, the Kaiju stinger, the roar 0.35 s later) over a full-scale music bed at the music director's match gain
    /// with its announcer/moment duck, is held at -1 dBFS by the master bus; normal play loses at most 2 dB.
    /// </summary>
    [TestFixture]
    public sealed class MasterBusTests
    {
        const int Rate = SoundSynth.SampleRate;
        const int Ch = 2;

        static float Db(float a) => a > 0f ? 20f * (float)Math.Log10(a) : -999f;

        static void AddCue(float[] mix, string cue, double at, float extraGain)
        {
            int i = SoundTable.IndexOf(cue);
            Assert.GreaterOrEqual(i, 0, cue);
            CueDef c = SoundTable.Cues[i];
            Assert.Greater(c.Synth.Length, 0, cue + " is synthesised");
            float[] mono = SoundSynth.Render(SynthRecipes.Find(c.Synth[0]), 0, Rate);
            int start = (int)(at * Rate);
            float g = c.Gain * extraGain;
            for (int k = 0; k < mono.Length; k++)
            {
                int f = start + k;
                if (f * Ch + 1 >= mix.Length) break;
                mix[f * Ch] += mono[k] * g;      // centred (pan 0): full level in both channels
                mix[f * Ch + 1] += mono[k] * g;
            }
        }

        /// <summary>A music bed peaking at full scale (two detuned tones and a pulse), times the director's gain curve.</summary>
        static void AddMusic(float[] mix, Func<double, float> gainAt)
        {
            int frames = mix.Length / Ch;
            for (int f = 0; f < frames; f++)
            {
                double t = (double)f / Rate;
                double v = 0.45 * Math.Sin(2 * Math.PI * 110 * t) + 0.35 * Math.Sin(2 * Math.PI * 164.8 * t) + 0.2 * Math.Sin(2 * Math.PI * 329.6 * t) * (t * 2 % 1 < 0.5 ? 1 : 0);
                float x = (float)v * gainAt(t);
                mix[f * Ch] += x;
                mix[f * Ch + 1] += x;
            }
        }

        static float Peak(float[] a) { float p = 0f; foreach (float x in a) p = Math.Max(p, Math.Abs(x)); return p; }
        static double Rms(float[] a) { double s = 0; foreach (float x in a) s += (double)x * x; return Math.Sqrt(s / Math.Max(1, a.Length)); }

        static float[] Run(float[] mix)
        {
            var o = (float[])mix.Clone();
            var lim = new MasterLimiter();
            const int block = 1024 * Ch;   // the audio thread hands over blocks
            var buf = new float[block];
            for (int s = 0; s < o.Length; s += block)
            {
                int n = Math.Min(block, o.Length - s);
                if (n != block) buf = new float[n];
                Array.Copy(o, s, buf, 0, n);
                lim.Process(buf, Ch, Rate);
                Array.Copy(buf, 0, o, s, n);
            }
            return o;
        }

        [Test]
        public void TheHatchMomentPeaksAtMinusOneDbfsThroughTheMasterBus()
        {
            var mix = new float[(int)(4.0 * Rate) * Ch];
            // Music: BaseGain until the hatch; the takeover duck (DuckGain) arrives over 0.15 s, released after 2.5 s.
            AddMusic(mix, t => MusicDirector.BaseGain * (float)(t < 0.5 ? 1 : t < 0.65 ? 1 - (1 - MusicDirector.DuckGain) * (t - 0.5) / 0.15 : t < 3.0 ? MusicDirector.DuckGain : MusicDirector.DuckGain));
            AddCue(mix, "kaiju_hatch", 0.5, 1f);
            AddCue(mix, "sting_kaiju", 0.5, 1f);
            AddCue(mix, "kaiju_roar", 0.85, 1f);
            float before = Peak(mix);
            float[] after = Run(mix);
            float peakAfter = Peak(after);
            Console.WriteLine($"[b13 audio] hatch moment peak: before {Db(before):F2} dBFS, after {Db(peakAfter):F2} dBFS (limit: after <= -1.00 dBFS)");
            Assert.Greater(before, 1f, "the unlimited hatch mix passes full scale (the measured fault)");
            Assert.LessOrEqual(peakAfter, MasterLimiter.Ceiling + 1e-6f);
        }

        [Test]
        public void NormalPlayIsAtMostTwoDbQuieter()
        {
            var mix = new float[(int)(6.0 * Rate) * Ch];
            AddMusic(mix, t => MusicDirector.BaseGain);
            string[] cues = { "swing_mid", "hit_wood", "step_dirt" };
            // A fight at a few metres: a swing, a hit and footsteps every 0.4 s, at half their table gain (distance and crowd).
            for (int k = 0; k < 14; k++) AddCue(mix, cues[k % cues.Length], 0.2 + 0.4 * k, 0.5f);
            float[] after = Run(mix);
            // Compare like for like: skip the limiter's 1.5 ms latency.
            int lag = MasterLimiter.LookaheadFrames(Rate) * Ch;
            var shifted = new float[mix.Length - lag];
            var src = new float[mix.Length - lag];
            Array.Copy(after, lag, shifted, 0, shifted.Length);
            Array.Copy(mix, 0, src, 0, src.Length);
            double loss = 20 * Math.Log10(Rms(shifted) / Rms(src));
            Console.WriteLine($"[b13 audio] normal play: peak before {Db(Peak(src)):F2} dBFS, after {Db(Peak(shifted)):F2} dBFS, loudness change {loss:F2} dB (limit: >= -2.00 dB)");
            Assert.GreaterOrEqual(loss, -2.0);
        }

        [Test]
        public void TheMasterBusAllocatesNothingPerBlock()
        {
            var lim = new MasterLimiter();
            var buf = new float[1024 * Ch];
            for (int i = 0; i < buf.Length; i++) buf[i] = (float)Math.Sin(i * 0.01) * 1.5f;
            lim.Process(buf, Ch, 48000);
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            for (int k = 0; k < 200; k++) lim.Process(buf, Ch, 48000);
            long d = GC.GetAllocatedBytesForCurrentThread() - a0;
            Console.WriteLine($"[b13 audio] master bus: {d} B allocated over 200 blocks (limit: 0 B)");
            Assert.AreEqual(0, d);
        }
    }
}
