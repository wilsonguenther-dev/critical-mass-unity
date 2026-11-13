using System.Collections.Generic;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.Menus;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>The motion-comic story intro's shot table (Resources "Story/motion-comic") and its player.</summary>
    [TestFixture]
    public sealed class MotionComicTests
    {
        static McTable Table()
        {
            McTable t = MotionComic.LoadTable();
            Assert.IsNotNull(t, "Resources/" + MotionComic.TableResource + ".json loads and parses");
            return t;
        }

        [Test]
        public void IntroHasEightToTwelveShotsAndRunsFortyToFiftyFiveSeconds()
        {
            McTable t = Table();
            McShot[] intro = MotionComic.Shots(t, MotionComic.IntroSequence);
            Assert.IsNotNull(intro);
            Assert.That(intro.Length, Is.InRange(8, 12), "shot count");
            // Story intro 2: cut on the narration's phrases (58.5 s with 13 s of dead air before; about 48 s now).
            Assert.That(MotionComic.TotalSeconds(intro), Is.InRange(40f, 55f), "intro length in seconds");
            var ids = new HashSet<string>();
            foreach (McShot s in t.shots)
            {
                Assert.IsTrue(ids.Add(s.id), "shot ids are unique: " + s.id);
                Assert.That(s.seconds, Is.InRange(2f, 8f), s.id + " length");
                Assert.IsNotEmpty(s.layers, s.id + " has layers");
            }
        }

        [Test]
        public void EveryLayerTextureResolves()
        {
            McTable t = Table();
            var names = new List<string>();
            foreach (McShot s in t.shots) MotionComic.CollectTextures(s, names);
            var missing = new List<string>();
            foreach (string n in names)
                if (Resources.Load<Texture2D>(MotionComic.TexturePath(n)) == null) missing.Add(MotionComic.TexturePath(n));
            Assert.IsEmpty(missing, "missing layer textures under Resources: " + string.Join(", ", missing));
        }

        [Test]
        public void KeysAreOrderedInsideTheirShotAndUseKnownEases()
        {
            foreach (McShot s in Table().shots)
            {
                foreach (McLayer l in s.layers)
                {
                    for (int i = 0; i < l.keys.Length; i++)
                    {
                        McKey k = l.keys[i];
                        Assert.That(k.t, Is.InRange(0f, s.seconds + 1e-4f), s.id + "/" + l.tex + " key time");
                        if (i > 0) Assert.GreaterOrEqual(k.t, l.keys[i - 1].t, s.id + "/" + l.tex + " keys in order");
                        Assert.IsTrue(MotionComic.IsKnownEase(k.ease), s.id + "/" + l.tex + " ease " + k.ease);
                        Assert.Greater(k.s, 0f);
                    }
                    Assert.Greater(l.w, 0f, s.id + "/" + l.tex + " width");
                    if (l.frames.Length > 0) Assert.Greater(l.fps, 0f);
                }
                for (int i = 0; i < s.cam.Length; i++)
                {
                    Assert.IsTrue(MotionComic.IsKnownEase(s.cam[i].ease), s.id + " camera ease");
                    if (i > 0) Assert.GreaterOrEqual(s.cam[i].t, s.cam[i - 1].t, s.id + " camera keys in order");
                    Assert.Greater(s.cam[i].zoom, 0f);
                }
                foreach (McTitle ti in s.titles)
                {
                    Assert.IsFalse(string.IsNullOrWhiteSpace(ti.text), s.id + " title text");
                    Assert.Less(ti.at, ti.@out, s.id + " title in before out");
                    Assert.LessOrEqual(ti.@out, s.seconds + 1e-4f, s.id + " title inside the shot");
                }
                Assert.LessOrEqual(s.titles.Length, MotionComic.MaxTitles, s.id + " titles per shot");
            }
        }

        [Test]
        public void NarrationClipsAndSoundCuesResolve()
        {
            McTable t = Table();
            foreach (McShot s in t.shots)
            {
                foreach (McVo v in s.vo)
                {
                    Assert.IsNotNull(Resources.Load<AudioClip>(v.clip), s.id + " narration " + v.clip);
                    Assert.That(v.at, Is.InRange(0f, s.seconds), s.id + " narration time");
                }
                foreach (McSfx f in s.sfx)
                {
                    Assert.IsNotNull(MotionComic.Cue(t, f.cue), s.id + " cue '" + f.cue + "' is in the cue list");
                    Assert.That(f.at, Is.InRange(0f, s.seconds), s.id + " cue time");
                }
            }
            foreach (McCue c in t.cues)
            {
                int found = 0;
                foreach (string file in c.files) if (Resources.Load<AudioClip>(MotionComic.SfxRoot + file) != null) found++;
                Assert.Greater(found, 0, "cue " + c.id + " has at least one clip under Resources/" + MotionComic.SfxRoot + " (the fallback)");
                if (!string.IsNullOrEmpty(c.synth)) Assert.IsNotNull(SynthRecipes.Find(c.synth), "cue " + c.id + " recipe " + c.synth);
            }
        }

        [Test]
        public void SequencesAndCodexEntriesResolveToShots()
        {
            McTable t = Table();
            foreach (McSequence seq in t.sequences)
            {
                McShot[] shots = MotionComic.Shots(t, seq.id);
                Assert.IsNotNull(shots, seq.id);
                Assert.AreEqual(seq.shots.Length, shots.Length, seq.id + ": every shot id exists");
            }
            Assert.IsNotNull(MotionComic.Shots(t, MotionComic.AttractSequence), "attract loop");
            foreach (McSequence c in t.codex)
                Assert.AreEqual(c.shots.Length, MotionComic.Shots(t, c.id).Length, "codex " + c.id);
            Assert.IsNull(MotionComic.Shots(t, "no-such-sequence"));
        }

        [Test]
        public void TimelineSkipEndsAtOnceAndPlaysToTheEnd()
        {
            McShot[] intro = MotionComic.Shots(Table(), MotionComic.IntroSequence);
            var secs = new float[intro.Length];
            for (int i = 0; i < secs.Length; i++) secs[i] = intro[i].seconds;
            var tl = new StoryTimeline(secs);
            Assert.AreEqual(MotionComic.TotalSeconds(intro), tl.Total, 1e-3);
            tl.Skip();
            Assert.IsTrue(tl.Done);
            Assert.IsTrue(tl.Skipped);
            Assert.IsFalse(tl.Advance(1.0 / 60.0), "nothing moves after a skip");
            Assert.AreEqual(0.0, tl.Time);

            var tl2 = new StoryTimeline(secs);
            for (int f = 0; f < 60 * 70 && !tl2.Done; f++) tl2.Advance(1.0 / 60.0);
            Assert.IsTrue(tl2.Done);
            Assert.IsFalse(tl2.Skipped, "played to the end");
        }

        /// <summary>Runs a gate at 60 fps from t0 to t1 with the key held or not; true if it skipped.</summary>
        static bool Run(SkipGate g, ref double t, double until, bool down)
        {
            bool skipped = false;
            for (; t < until - 1e-9; t += 1.0 / 60.0) skipped |= g.Update(t, down);
            return skipped;
        }

        [Test]
        public void SkipNeedsADeliberatePressAfterOneAndAHalfSeconds()
        {
            Assert.AreEqual(1.5f, Table().skipPromptAfter, 1e-4f, "the table's skip lock is 1.5 s");
            Assert.AreEqual(1.5f, SkipGate.LockSeconds, 1e-4f);

            // A key held from the first frame (the Enter that chose WATCH INTRO) never skips, however long it is held.
            var held = new SkipGate();
            double t = 0;
            Assert.IsFalse(Run(held, ref t, 4.0, true), "a key held from before the film never skips");
            Assert.IsFalse(Run(held, ref t, 4.2, false));
            Assert.IsTrue(held.Update(t, true), "released and pressed again after the lock: skips");

            // Presses inside the lock do nothing, a press held through the lock does nothing, a fresh press after it skips.
            var g = new SkipGate();
            t = 0;
            Assert.IsFalse(Run(g, ref t, 0.3, false));
            Assert.IsFalse(Run(g, ref t, 0.4, true), "a tap at 0.3 s does not skip");
            Assert.IsFalse(Run(g, ref t, 1.2, false));
            Assert.IsFalse(Run(g, ref t, 1.8, true), "a press at 1.2 s held through the lock does not skip");
            Assert.IsFalse(Run(g, ref t, 2.0, false));
            Assert.IsTrue(g.Update(t, true), "a fresh press after the lock skips on its frame");

            // Frame 0 and the frame the lock ends.
            var g0 = new SkipGate();
            Assert.IsFalse(g0.Update(0.0, true), "frame 0 never skips");
            var g1 = new SkipGate();
            g1.Update(1.49, false);
            Assert.IsTrue(g1.Update(1.5, true), "a press on the frame the lock ends skips");

            Assert.AreEqual(0f, MotionComic.SkipPromptAlpha(0.0, SkipGate.LockSeconds));
            Assert.AreEqual(0f, MotionComic.SkipPromptAlpha(1.49, SkipGate.LockSeconds), "no skip prompt before skipping works");
            Assert.Greater(MotionComic.SkipPromptAlpha(1.9, SkipGate.LockSeconds), 0.99f, "the prompt is up once skipping works");
            Assert.IsFalse(new SkipGate().PromptOn(1.4));
            Assert.IsTrue(new SkipGate().PromptOn(1.5));
        }

        static readonly float[] Aspects = { 4f / 3f, 3024f / 1964f, 16f / 10f, 16f / 9f, 21f / 9f };

        static float TexAspect(McLayer l, Dictionary<string, float> cache)
        {
            if (l.tex == MotionComic.Solid) return 1f;
            if (cache.TryGetValue(l.tex, out float a)) return a;
            Texture2D t = Resources.Load<Texture2D>(MotionComic.TexturePath(l.tex));
            a = t != null ? (float)t.height / t.width : 1f;
            cache[l.tex] = a;
            return a;
        }

        [Test]
        public void NoFrameShowsUncoveredPixelsAtFiveAspects()
        {
            McTable t = Table();
            McShot[] intro = MotionComic.Shots(t, MotionComic.IntroSequence);
            var cache = new Dictionary<string, float>();
            var problems = new List<string>();
            foreach (McShot s in intro)
            {
                bool hasCover = false;
                foreach (McLayer l in s.layers) hasCover |= l.cover;
                Assert.IsTrue(hasCover, s.id + " marks its opaque plate(s) cover");
            }
            foreach (float aspect in Aspects)
            {
                var view = new Vector2(1080f * aspect, 1080f);
                foreach (McShot s in intro)
                {
                    foreach (float when in new[] { 0f, s.seconds * 0.5f, s.seconds })
                    {
                        Rect vis = MotionComic.VisibleStage(t, view, MotionComic.SampleBars(s.bars, when));
                        MotionComic.Cam cam = MotionComic.ShotCam(s, when, false);
                        Vector2 shake = MotionComic.Shake(s, when);
                        var covers = new List<MotionComic.Pose>();
                        for (int i = 0; i < s.layers.Length; i++)
                        {
                            McLayer l = s.layers[i];
                            if (!l.cover) continue;
                            MotionComic.Pose p = MotionComic.LayerPose(t, l, i, cam, shake, when, when, false, l.keys[l.keys.Length - 1].t,
                                TexAspect(l, cache), view, CriticalMass.Runtime.Hud.TitleScreen.LogoY);
                            if (p.On && p.A >= 0.98f) covers.Add(p);
                        }
                        int miss = 0;
                        for (int gx = 0; gx <= 24; gx++)
                        for (int gy = 0; gy <= 14; gy++)
                        {
                            var q = new Vector2(vis.xMin + vis.width * gx / 24f, vis.yMin + vis.height * gy / 14f);
                            bool hit = false;
                            foreach (MotionComic.Pose p in covers) if (MotionComic.Contains(p, q)) { hit = true; break; }
                            if (!hit) miss++;
                        }
                        if (miss > 0) problems.Add($"{aspect:0.00}: {s.id} at {when:0.00}s, {miss} uncovered sample(s)");
                    }
                }
            }
            Assert.IsEmpty(problems, "uncovered pixels:\n" + string.Join("\n", problems));
        }

        [Test]
        public void BarsKeepOneFilmWindowAndTheStageNeverStretches()
        {
            McTable t = Table();
            foreach (float aspect in Aspects)
            {
                var view = new Vector2(1080f * aspect, 1080f);
                float s = MotionComic.StageScale(t, view);
                Assert.GreaterOrEqual(t.stageW * s, view.x - 1e-3f, aspect + ": the stage covers the width");
                Assert.GreaterOrEqual(t.stageH * s, view.y - 1e-3f, aspect + ": the stage covers the height");
                Rect vis = MotionComic.VisibleStage(t, view, 1f);
                Assert.LessOrEqual(vis.height, MotionComic.WindowH(t) + 1e-2f, aspect + ": full bars show at most the film window");
                Assert.GreaterOrEqual(vis.width * 0.5f, 720f - 1e-2f, aspect + ": at least 1440 stage units across");
            }
            Assert.AreEqual(0f, MotionComic.BarHeight(t, new Vector2(1080f * 21f / 9f, 1080f), 1f), 1e-3f, "21:9 is already the film's shape: no bars");
            Assert.AreEqual(0f, MotionComic.BarHeight(t, new Vector2(1920f, 1080f), 0f), 1e-3f, "bars open");
        }

        [Test]
        public void TitlesAndSubjectsStayInsideTheSafeBoxes()
        {
            McTable t = Table();
            var cache = new Dictionary<string, float>();
            var owner = new Vector2(1080f * 3024f / 1964f, 1080f);
            var problems = new List<string>();
            foreach (McShot s in MotionComic.Shots(t, MotionComic.IntroSequence))
            {
                foreach (McTitle ti in s.titles)
                {
                    // Display type at about 0.66 em per glyph plus its settled tracking (the capture logs the real widths).
                    float w = ti.text.Length * ti.size * 0.66f + (ti.text.Length - 1) * ti.track1;
                    if (Mathf.Abs(ti.x) + w * 0.5f > MotionComic.SafeHalfW || Mathf.Abs(ti.y) + ti.size * 0.5f > MotionComic.SafeHalfH)
                        problems.Add($"{s.id}: title '{ti.text}' leaves the title-safe box");
                }
                MotionComic.Cam cam = MotionComic.ShotCam(s, s.seconds, false);
                for (int i = 0; i < s.layers.Length; i++)
                {
                    McLayer l = s.layers[i];
                    if (!l.subject) continue;
                    MotionComic.Pose p = MotionComic.LayerPose(t, l, i, cam, Vector2.zero, s.seconds, s.seconds, false, l.keys[l.keys.Length - 1].t,
                        TexAspect(l, cache), owner, CriticalMass.Runtime.Hud.TitleScreen.LogoY);
                    float over = Mathf.Abs(p.P.x) + p.W * 0.5f - MotionComic.SubjectHalfW;
                    if (over > 0.15f * p.W) problems.Add($"{s.id}: subject {l.tex} loses {over:0} of {p.W:0} units at 1.54");
                }
            }
            Assert.IsEmpty(problems, string.Join("\n", problems));
        }

        [Test]
        public void TheFilmsLogoLandsWhereTheTitleDrawsItsLogo()
        {
            McTable t = Table();
            McShot hero = MotionComic.Shot(t, "hero");
            McLayer logo = null;
            int li = -1;
            for (int i = 0; i < hero.layers.Length; i++)
                if (hero.layers[i].anchor == "titleLogo" && hero.layers[i].subject) { logo = hero.layers[i]; li = i; }
            Assert.IsNotNull(logo, "the hero shot's logo is anchored to the title's logo");
            foreach (Vector2Int px in new[] { new Vector2Int(3024, 1964), new Vector2Int(1920, 1080), new Vector2Int(2560, 1600), new Vector2Int(3440, 1440), new Vector2Int(1600, 1200) })
            {
                var view = new Vector2(1080f * px.x / px.y, 1080f);
                MotionComic.Pose p = MotionComic.LayerPose(t, logo, li, MotionComic.ShotCam(hero, hero.seconds, false), Vector2.zero, hero.seconds, hero.seconds, false,
                    logo.keys[logo.keys.Length - 1].t, CriticalMass.Runtime.Hud.TitleScreen.LogoSize.y / CriticalMass.Runtime.Hud.TitleScreen.LogoSize.x, view,
                    CriticalMass.Runtime.Hud.TitleScreen.LogoY);
                // Pixels on screen: the film's stage units -> canvas units (stage scale) -> pixels (h / 1080).
                float filmPx = MotionComic.StageScale(t, view) * px.y / 1080f;
                float titlePx = CriticalMass.Runtime.Hud.HudLayout.Scale(px.x, px.y);
                Assert.AreEqual(CriticalMass.Runtime.Hud.TitleScreen.LogoY * titlePx, p.P.y * filmPx, 0.5f, px + ": logo height");
                Assert.AreEqual(0f, p.P.x * filmPx, 0.5f, px + ": logo centred");
                Assert.AreEqual(CriticalMass.Runtime.Hud.TitleScreen.LogoSize.x * titlePx, p.W * filmPx, 0.5f, px + ": logo width");
            }
        }

        [Test]
        public void WatchIntroIsOnTheTitleAndOnHome()
        {
            var go = new GameObject("Story Intro 2 Title Test");
            try
            {
                CriticalMass.Runtime.Hud.TitleScreen title = CriticalMass.Runtime.Hud.TitleScreen.Show(go, null);
                var watch = title.WatchIntroItem;
                Assert.IsNotNull(watch, "the title has WATCH INTRO");
                Assert.AreEqual("WATCH INTRO", watch.Label.text);
                Assert.IsTrue(watch.interactable);
                Assert.AreSame(watch, title.PlayNowItem.navigation.selectOnUp, "Up from PLAY NOW reaches WATCH INTRO");
                Assert.AreSame(title.PlayNowItem, watch.navigation.selectOnDown, "and Down comes back");
                var m = go.AddComponent<MainMenu>();
                m.EnsureBuilt();
                Assert.IsNotNull(m.Item("intro"), "home has WATCH INTRO");
                Assert.IsTrue(m.Item("intro").interactable);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void IntroPlaysAtEveryLaunchUnlessSettingsTurnItOff()
        {
            bool had = PlayerPrefs.HasKey(CriticalMass.Runtime.Menu.GameSettings.IntroAtLaunchKey);
            int saved = PlayerPrefs.GetInt(CriticalMass.Runtime.Menu.GameSettings.IntroAtLaunchKey, 1);
            int seenOld = PlayerPrefs.GetInt("cm.story.seen", 0);
            try
            {
                PlayerPrefs.DeleteKey(CriticalMass.Runtime.Menu.GameSettings.IntroAtLaunchKey);
                Assert.IsTrue(CriticalMass.Runtime.Menu.GameSettings.IntroAtLaunch, "PLAY INTRO AT LAUNCH defaults to on");
                PlayerPrefs.SetInt("cm.story.seen", 1);   // the owner's profile from earlier builds
                PlayerPrefs.SetInt(StoryIntro.SeenKey, 1);
                var title = CriticalMass.Runtime.Flow.FlowState.Title;
                Assert.IsTrue(StoryIntro.ShouldPlayAtLaunch(true, true, title, false, CriticalMass.Runtime.Menu.GameSettings.IntroAtLaunch),
                    "a seen flag from an earlier build no longer stops it");
                Assert.IsFalse(StoryIntro.ShouldPlayAtLaunch(true, true, title, false, false), "Settings turned it off");
                Assert.IsFalse(StoryIntro.ShouldPlayAtLaunch(true, true, title, true, true), "never in autoplay");
                Assert.IsFalse(StoryIntro.ShouldPlayAtLaunch(true, true, CriticalMass.Runtime.Flow.FlowState.Match, false, true), "never into a match");
                Assert.IsFalse(StoryIntro.ShouldPlayAtLaunch(true, false, title, false, true), "not in the editor or batch runs");
                CollectionAssert.Contains(CriticalMass.Runtime.Menu.GameSettings.AllKeys, CriticalMass.Runtime.Menu.GameSettings.IntroAtLaunchKey);
            }
            finally
            {
                if (had) PlayerPrefs.SetInt(CriticalMass.Runtime.Menu.GameSettings.IntroAtLaunchKey, saved);
                else PlayerPrefs.DeleteKey(CriticalMass.Runtime.Menu.GameSettings.IntroAtLaunchKey);
                PlayerPrefs.SetInt("cm.story.seen", seenOld);
            }
        }

        [Test]
        public void EasesStartAtZeroAndLandOnOne()
        {
            foreach (McEase e in System.Enum.GetValues(typeof(McEase)))
            {
                Assert.AreEqual(0f, MotionComic.Ease(e, 0f), 1e-4f, e + " at 0");
                Assert.AreEqual(1f, MotionComic.Ease(e, 1f), 1e-4f, e + " at 1");
            }
            Assert.Greater(MotionComic.Ease(McEase.OutBack, 0.7f), 1f, "OutBack overshoots before it settles");
            Assert.That(MotionComic.Ease(McEase.OutBackSoft, 0.8f), Is.InRange(1f, 1.06f), "OutBackSoft overshoots a little");
            Assert.Less(MotionComic.Ease(McEase.InOutBack, 0.1f), 0f, "InOutBack anticipates (pulls back first)");
            Assert.Greater(MotionComic.Ease(McEase.InOutBack, 0.9f), 1f, "InOutBack overshoots, then settles");
        }

        [Test]
        public void ParallaxCameraMovesDeepLayersLess()
        {
            var cam = new MotionComic.Cam { Zoom = 1f, Dolly = 1f, X = 100f };
            Vector2 far = MotionComic.ToScreen(cam, 0.2f, 0f, 0f, out float sf);
            Vector2 near = MotionComic.ToScreen(cam, 1f, 0f, 0f, out float sn);
            Assert.AreEqual(-20f, far.x, 1e-4f);
            Assert.AreEqual(-100f, near.x, 1e-4f);
            Assert.AreEqual(1f, sf, 1e-5f);
            var zoom = new MotionComic.Cam { Zoom = 2f, Dolly = 1f, Zx = 50f, Zy = 50f };
            Vector2 pivot = MotionComic.ToScreen(zoom, 0.5f, 50f, 50f, out float sz);
            Assert.AreEqual(50f, pivot.x, 1e-4f, "the lens zooms about its pivot");
            Assert.AreEqual(2f, sz, 1e-5f);
        }

        [Test]
        public void ParseRejectsBrokenTables()
        {
            Assert.IsNull(MotionComic.Parse(null));
            Assert.IsNull(MotionComic.Parse("not json"));
            Assert.IsNull(MotionComic.Parse("{}"), "a table without shots is unusable");
            McTable t = MotionComic.Parse("{\"shots\":[{\"id\":\"a\",\"seconds\":2,\"layers\":[{\"tex\":\"egg\",\"w\":100}]}]}");
            Assert.IsNotNull(t);
            Assert.AreEqual(1, t.shots[0].layers[0].keys.Length, "a layer without keys gets one resting key");
            Assert.AreEqual(1f, t.shots[0].cam[0].zoom);
        }

        [Test]
        public void PlayerPreparesAndRendersEveryFrameOfTheIntro()
        {
            McTable t = Table();
            McShot[] intro = MotionComic.Shots(t, MotionComic.IntroSequence);
            var go = new GameObject("Motion Comic Test");
            try
            {
                MotionComicPlayer p = go.AddComponent<MotionComicPlayer>();
                Assert.IsTrue(p.Prepare(t, intro));
                Assert.AreEqual(0, p.MissingTextures);
                p.Show();
                double total = 0;
                for (int i = 0; i < intro.Length; i++)
                {
                    for (float s = 0f; s <= intro[i].seconds; s += 1f / 30f) p.Render(i, s, total + s, false);
                    Assert.AreEqual(i, p.ShownShot);
                    total += intro[i].seconds;
                }
                p.Reduced = true;
                p.ReduceFlashing = true;
                for (int i = 0; i < intro.Length; i++) p.Render(i, intro[i].seconds * 0.5f, 10.0, false);
                p.Hide();
                p.Release();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
