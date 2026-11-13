using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CriticalMass.Runtime.Menus.Story;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// Story intro 3 (plan intro-film-1, acceptance checklist): the measurable items that live in the shot table the film
    /// was rendered from (Resources/Story/intro-film.json), in the engine-side playback rules, and in the offline
    /// soundtrack measurement (tools/film/acceptance/soundtrack.json, written by tools/film/check_soundtrack.py).
    /// </summary>
    [TestFixture]
    public sealed class IntroFilmTests
    {
        const float Frame = 1f / 30f;
        static IntroFilm.Table T => IntroFilm.LoadTable();

        static bool OnGrid(float t, float step, float offset) =>
            Mathf.Abs((t - offset) / step - Mathf.Round((t - offset) / step)) * step <= 2f * Frame + 1e-4f;

        [Test]
        public void TableLoadsAndMatchesTheFilm()
        {
            Assert.IsNotNull(T, "Resources/Story/intro-film.json");
            Assert.AreEqual("cm.story.introfilm/1", T.schema);
            Assert.AreEqual(30, T.fps);
            Assert.AreEqual(IntroFilm.Seconds, T.seconds, 0.01f);
            Assert.AreEqual(2560, T.width);
            Assert.AreEqual(1440, T.height);
            Assert.AreEqual(2f, T.picture.aspect, 1e-4f, "a 2.0:1 picture inside the 16:9 container");
            Assert.AreEqual(80, T.picture.bar);
            Assert.AreEqual(IntroFilm.HandoffSeconds, T.handoff_dissolve, 0.01f);
            Assert.AreEqual(1.5f, T.skip_lock_seconds, 1e-4f);
        }

        // B11: every cut on a beat of the 80 BPM grid (+-2 frames); hero hits on downbeats.
        [Test]
        public void CutsLandOnTheBeatAndHeroHitsOnDownbeats()
        {
            Assert.AreEqual(80, T.bpm);
            Assert.AreEqual(0.75f, T.beat, 1e-4f);
            foreach (float c in T.cuts) Assert.IsTrue(OnGrid(c, 0.75f, 0f), "cut at " + c + " is off the beat grid");
            float[] hits = { 4.50f, 10.50f, 22.50f, 31.50f, 34.50f, 37.50f, 40.50f, 43.50f };
            foreach (float h in hits) Assert.IsTrue(OnGrid(h, 3f, 1.5f), "hero hit at " + h + " is off the downbeats");
            Assert.AreEqual(7, T.hard_cuts.Length, "9 hard cuts became 7: crack and rise are one continuous move");
        }

        // B12 (table side): no shot is shorter than a bar's half or longer than 8.25 s, and the shots tile the film.
        [Test]
        public void ShotsTileTheFilmWithoutGaps()
        {
            for (int i = 1; i < T.shots.Length; i++)
                Assert.AreEqual(T.shots[i - 1].t1, T.shots[i].t0, 1e-4f, "gap before " + T.shots[i].id);
            Assert.AreEqual(0f, T.shots[0].t0, 1e-4f);
            Assert.AreEqual(IntroFilm.Seconds, T.shots[T.shots.Length - 1].t1, 0.01f);
            foreach (var s in T.shots) Assert.LessOrEqual(s.t1 - s.t0, 8.26f, s.id + " holds too long");
        }

        // C17: exactly 2 light events (the crack and the hatch), the hatch the bigger one.
        [Test]
        public void ExactlyTwoLightEvents()
        {
            Assert.AreEqual(2, T.light_events.Length);
            Assert.AreEqual(4.50f, T.light_events[0].t, 1e-4f);
            Assert.AreEqual(31.50f, T.light_events[1].t, 1e-4f);
            Assert.Greater(T.light_events[1].peak, T.light_events[0].peak);
            foreach (var l in T.light_events) Assert.LessOrEqual(l.peak, 0.6f + 1e-4f, "a light event is never a white sheet");
        }

        // C18: at most 5 shakes, none above 10 px at 1080, none during narration.
        [Test]
        public void ShakesAreFewSmallAndNeverUnderNarration()
        {
            Assert.LessOrEqual(T.shakes.Length, 5);
            foreach (var s in T.shakes)
            {
                Assert.LessOrEqual(s.px, 10f, "shake at " + s.t);
                foreach (var sub in T.subtitles)
                    Assert.IsFalse(s.t < sub.t_out && s.t + s.dur > sub.t_in, "shake at " + s.t + " runs under \"" + sub.text + "\"");
            }
        }

        // D22: titles inside 90% title-safe, never starting within 8 frames of a cut, no slam (fade-and-track only).
        [Test]
        public void TitlesStayTitleSafeAndAwayFromCuts()
        {
            foreach (var ti in T.titles)
            {
                float w = ti.text.Length * ti.size * 0.66f;
                Assert.LessOrEqual(Mathf.Abs(ti.x) + w * 0.5f, 0.45f * 1920f, ti.text + " leaves title-safe horizontally");
                Assert.LessOrEqual(Mathf.Abs(ti.y) + ti.size * 0.6f, 0.45f * 960f, ti.text + " leaves title-safe vertically");
                Assert.AreEqual("fade-track", ti.style, ti.text + " must not slam");
                foreach (float c in T.cuts)
                    Assert.IsFalse(ti.t_in > c - 1e-4f && ti.t_in < c + 8f * Frame, ti.text + " starts within 8 frames of the cut at " + c);
            }
            Assert.AreEqual(7, T.titles.Length, "11 titles became 7");
        }

        // D23: every title fully opaque for at least max(1.5 s, 0.3 s per word); every subtitle up for its whole line.
        [Test]
        public void TitlesAndSubtitlesStayUpLongEnough()
        {
            foreach (var ti in T.titles)
            {
                float opaque = (ti.t_out - 0.4f) - (ti.t_in + 0.4f);
                int words = ti.text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                Assert.GreaterOrEqual(opaque + 1e-3f, Mathf.Max(1.5f, 0.3f * words), ti.text);
            }
            foreach (var s in T.subtitles)
            {
                var clip = Resources.Load<AudioClip>("CMVoice/" + s.clip);
                if (clip != null) Assert.GreaterOrEqual(s.t_out - s.t_in + 0.12f, clip.length, s.text + " ends before its line");
            }
        }

        // D24: no line appears as both a title and a subtitle (FEED IT / OR CORRUPT IT are the labelled exception).
        [Test]
        public void NoTitleRepeatsTheNarration()
        {
            string Norm(string x) => Regex.Replace(x.ToLowerInvariant(), "[^a-z ]", "").Trim();
            var sentences = new HashSet<string>();
            foreach (var s in T.subtitles)
                foreach (string part in Regex.Split(s.text, @"(?<=[.!?])\s+")) sentences.Add(Norm(part));
            foreach (var ti in T.titles)
            {
                if (ti.text == "FEED IT" || ti.text == "OR CORRUPT IT") continue;
                Assert.IsFalse(sentences.Contains(Norm(ti.text)), ti.text + " repeats a narrated line");
            }
            foreach (string gone in new[] { "TWO BANNERS.", "ONE HILL.", "HOLD THE HILL.", "RAISE THE KAIJU." })
                Assert.IsFalse(T.titles.Any(x => x.text == gone), gone + " was deleted by the plan");
        }

        // E26: contained frame on the owner's 1512x982 and on 1920x1080: fixed black bars, nothing uncovered.
        [Test]
        public void FitInsideKeepsTheFrameContained()
        {
            IntroFilm.FitInside(1512f, 982f, out Rect v, out Rect p);
            Assert.AreEqual(1512f, v.width, 0.01f);
            Assert.AreEqual(850.5f, v.height, 0.01f);
            Assert.AreEqual(756f, p.height, 0.01f);
            Assert.AreEqual(113f, p.y, 0.01f, "113 px of black above and below the picture on the owner's screen");
            IntroFilm.FitInside(1920f, 1080f, out v, out p);
            Assert.AreEqual(960f, p.height, 0.01f);
            Assert.AreEqual(60f, p.y, 0.01f);
            IntroFilm.FitInside(2560f, 1080f, out v, out p);   // 21:9: black at the sides too
            Assert.Greater(v.x, 0f);
        }

        // B14 / B15: hand-off and skip rules.
        [Test]
        public void HandoffAndSkipRules()
        {
            Assert.AreEqual(1f, IntroFilm.HandoffAlpha(46.4), 1e-4f);
            Assert.AreEqual(0f, IntroFilm.HandoffAlpha(IntroFilm.Seconds), 1e-4f);
            Assert.AreEqual(0f, IntroFilm.WorldGainAfter(0f), 1e-4f);
            Assert.Less(IntroFilm.WorldGainAfter(1.4f), 1f, "world sounds ramp over at least 1.5 s");
            Assert.AreEqual(1f, IntroFilm.WorldGainAfter(1.5f), 1e-4f);
            Assert.AreEqual(0.3f, IntroFilm.SkipPictureFade, 1e-4f);
            Assert.AreEqual(0.4f, IntroFilm.SkipAudioFade, 1e-4f);
            Assert.AreEqual(29.533f, IntroFilm.TitleMusicOffsetAtEnd, 0.01f);
            for (double t = 43.0; t < 47.4; t += 0.02)
            {
                IntroFilm.LogoPose(t, out float s, out float a);
                Assert.LessOrEqual(s, IntroFilm.LogoFrom + 1e-4f);
                if (t >= 44.0) Assert.AreEqual(1f, s, 1e-3f, "the logo has settled on the title's own scale by 44.0");
                if (t >= 43.5) Assert.AreEqual(1f, a, 1e-4f);
            }
            IntroFilm.LogoRect(new Vector2(1920f, 1080f), 70f, out Vector2 c, out Vector2 size);
            Assert.AreEqual(new Vector2(0f, 70f), c);
            Assert.AreEqual(CriticalMass.Runtime.Hud.TitleScreen.LogoSize, size);
        }

        [Test]
        public void SubtitleClockFollowsTheVideoTime()
        {
            Assert.IsNull(IntroFilm.SubtitleAt(T, 2.0, out _));
            Assert.AreEqual("The sky cracked. And the land rose to meet it.", IntroFilm.SubtitleAt(T, 6.0, out float a));
            Assert.AreEqual(1f, a, 1e-4f);
            Assert.IsNull(IntroFilm.SubtitleAt(T, 44.5, out _), "the spoken title line has no subtitle: the logo carries it");
        }

        // E28: the film ships in StreamingAssets, at most 60 MB (GitHub's 100 MB cap, no LFS rule for mp4).
        [Test]
        public void FilmFileShipsAndFitsTheBudget()
        {
            Assert.IsTrue(File.Exists(IntroFilm.Url), IntroFilm.Url);
            Assert.AreEqual(IntroFilm.PlatformDecodes(Application.platform), IntroFilm.Available);
            Assert.IsTrue(IntroFilm.PlatformDecodes(RuntimePlatform.OSXPlayer));
            Assert.IsTrue(IntroFilm.PlatformDecodes(RuntimePlatform.WindowsPlayer));
            Assert.IsFalse(IntroFilm.PlatformDecodes(RuntimePlatform.LinuxPlayer), "Linux has no H.264: the comic plays");
            long bytes = new FileInfo(IntroFilm.Url).Length;
            Assert.LessOrEqual(bytes, 60L * 1024 * 1024, "film is " + bytes / 1048576 + " MB");
            Assert.Greater(bytes, 5L * 1024 * 1024);
        }

        // A1-A10: the offline soundtrack measurement (ffmpeg ebur128, spectral flux, cue renders) all passed.
        [Test]
        public void SoundtrackAcceptanceItemsPass()
        {
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "tools", "film", "acceptance", "soundtrack.json"));
            Assert.IsTrue(File.Exists(path), path);
            string json = File.ReadAllText(path);
            for (int i = 1; i <= 10; i++)
            {
                Match m = Regex.Match(json, "\"A" + i + "\"\\s*:\\s*\\{\\s*\"ok\"\\s*:\\s*(true|false)");
                Assert.IsTrue(m.Success, "A" + i + " missing from " + path);
                Assert.AreEqual("true", m.Groups[1].Value, "soundtrack item A" + i + " failed (see " + path + ")");
            }
        }

        [Test]
        public void MusicDirectorResumeIsSafeWithoutMusic()
        {
            Audio.MusicDirector.FilmBed = 0f;
            Audio.MusicDirector.ResumeTitleAt(IntroFilm.TitleMusicOffsetAtEnd);
            Assert.AreEqual(1f, Audio.MusicDirector.FilmBed, "the bed comes back even when no music director runs");
        }
    }
}
