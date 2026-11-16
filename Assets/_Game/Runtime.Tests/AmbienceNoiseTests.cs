using System;
using CriticalMass.Runtime.Audio;
using CriticalMass.Runtime.Audio.Sound;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b12-jz item 1 ("it's playing white noise, especially at boot"). The wind bed was white noise through one 6 dB per
    /// octave 500 Hz low-pass plus a 600 to 1400 Hz white band, peak-normalised and looped under the title's attract match
    /// from the first frame. These tests render the shipped ambience recipes and assert their played level (RMS ceiling)
    /// and that they are not full-band noise (share of energy above 1 kHz and 2 kHz), and that nothing world-side plays on
    /// the title before the title's own audio. Each case prints one evidence line.
    /// </summary>
    public sealed class AmbienceNoiseTests
    {
        /// <summary>Played RMS ceiling (linear, before the mixer): 0.03 = about -30 dBFS.</summary>
        const float RmsCeiling = 0.03f;
        /// <summary>Most energy a bed may carry above 2 kHz / 1 kHz (white noise at 24 kHz carries about 83% / 92%).</summary>
        const float Above2kCeiling = 0.01f, Above1kCeiling = 0.05f;

        static double Rms(float[] x)
        {
            double e = 0;
            for (int i = 0; i < x.Length; i++) e += (double)x[i] * x[i];
            return Math.Sqrt(e / Math.Max(1, x.Length));
        }

        /// <summary>Share of the signal's energy above fc: two cascaded one-pole high-passes (12 dB per octave).</summary>
        static double ShareAbove(float[] x, double fc, int sr)
        {
            double rc = 1.0 / (2.0 * Math.PI * fc), a = rc / (rc + 1.0 / sr);
            double y1 = 0, x1 = 0, y2 = 0, x2 = 0, eh = 0, et = 0;
            for (int i = 0; i < x.Length; i++)
            {
                double v = x[i];
                double h1 = a * (y1 + v - x1); x1 = v; y1 = h1;
                double h2 = a * (y2 + h1 - x2); x2 = h1; y2 = h2;
                if (i < sr / 10) continue;   // skip the filters' settling
                eh += h2 * h2;
                et += v * v;
            }
            return et > 0 ? eh / et : 0;
        }

        /// <summary>The 1.0.868 wind recipe, rebuilt here for the printed before/after contrast (not asserted).</summary>
        static SynthRecipe OldWind() => new SynthRecipe
        {
            Id = "amb_wind_1_0_868", Length = 6f, Loop = true, Layers = new[]
            {
                new SynthLayer { Wave = Wave.Noise, F0 = 1, F1 = 1, Gain = 0.7f, Lp0 = 500, Hp = 80, Am = 0.5f, AmRate = 1f / 6f },
                new SynthLayer { Wave = Wave.Noise, F0 = 1, F1 = 1, Gain = 0.2f, Lp0 = 1400, Hp = 600, Am = 0.7f, AmRate = 2f / 6f },
            },
        };

        static void Measure(string label, SynthRecipe r, float playedGain, out double rms, out double a1, out double a2)
        {
            float[] x = SoundSynth.Render(r, 0);
            rms = Rms(x) * playedGain;
            a1 = ShareAbove(x, 1000, SoundSynth.SampleRate);
            a2 = ShareAbove(x, 2000, SoundSynth.SampleRate);
            Debug.Log($"[b12-jz ambience] {label}: played RMS {rms:0.0000} ({20 * Math.Log10(Math.Max(1e-9, rms)):0.0} dBFS, limit {RmsCeiling}) " +
                      $"energy above 1 kHz {a1:P2} (limit {Above1kCeiling:P0}) above 2 kHz {a2:P2} (limit {Above2kCeiling:P0}) samples {x.Length} at {SoundSynth.SampleRate} Hz");
        }

        [Test]
        public void TheWindBedIsSoftAndNotFullBandNoise()
        {
            Measure("OLD wind 1.0.868 at its old title gain 0.22x0.7 (contrast only)", OldWind(), 0.22f * 0.7f, out _, out _, out _);
            int cue = SoundTable.EdgeCue(SoundEdge.Wind);
            SynthRecipe r = SynthRecipes.Find("amb_wind");
            Assert.IsNotNull(r, "amb_wind recipe");
            Measure("NEW wind at its match gain", r, SoundTable.Cue(cue).Gain, out double rms, out double a1, out double a2);
            Assert.LessOrEqual(rms, RmsCeiling, "wind RMS ceiling");
            Assert.LessOrEqual(a1, Above1kCeiling, "wind energy above 1 kHz");
            Assert.LessOrEqual(a2, Above2kCeiling, "wind energy above 2 kHz (white noise would be ~83%)");
        }

        [Test]
        public void TheWaterBedIsSoftAndHasNoOpenHiss()
        {
            int cue = SoundTable.EdgeCue(SoundEdge.Water);
            SynthRecipe r = SynthRecipes.Find("amb_water");
            Assert.IsNotNull(r, "amb_water recipe");
            // Water is heard at full only within 4 m of a waterfall; this is that nearest, loudest case.
            Measure("NEW water at full proximity", r, SoundTable.Cue(cue).Gain, out double rms, out _, out double a2);
            Assert.LessOrEqual(rms, 0.08, "water RMS ceiling at the fall itself");
            Assert.LessOrEqual(a2, 0.15, "water energy above 2 kHz (white noise would be ~83%)");
        }

        [Test]
        public void NothingWorldSidePlaysOnTheTitleBeforeItsOwnAudio()
        {
            float windTitle = SoundDirector.WindGain(true, 0.08f, 1f, 1f);
            float windMatch = SoundDirector.WindGain(false, 0.08f, 1f, 1f);
            float bootSilent = SoundDirector.TitleWorldGain(true, MusicState.Silent, false);
            float withMusic = SoundDirector.TitleWorldGain(true, MusicState.Title, false);
            float withFilm = SoundDirector.TitleWorldGain(true, MusicState.Silent, true);
            float match = SoundDirector.TitleWorldGain(false, MusicState.Silent, false);
            Debug.Log($"[b12-jz ambience] wind on title {windTitle} (limit 0), wind in match {windMatch}; world one-shots on title before music {bootSilent} (limit 0), " +
                      $"with title music {withMusic}, under story {withFilm}, in match {match}");
            Assert.AreEqual(0f, windTitle, "no wind bed on the title");
            Assert.AreEqual(0f, bootSilent, "no world sound at boot before the title's own audio");
            Assert.AreEqual(SoundDirector.TitleGain, withMusic);
            Assert.AreEqual(1f, match);
            Assert.Greater(windMatch, 0f, "the wind still plays (softly) in a match");
        }
    }
}
