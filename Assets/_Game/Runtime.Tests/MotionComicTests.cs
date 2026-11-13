using System.Collections.Generic;
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
        public void IntroHasEightToTwelveShotsAndRunsFortyFiveToSixtySeconds()
        {
            McTable t = Table();
            McShot[] intro = MotionComic.Shots(t, MotionComic.IntroSequence);
            Assert.IsNotNull(intro);
            Assert.That(intro.Length, Is.InRange(8, 12), "shot count");
            Assert.That(MotionComic.TotalSeconds(intro), Is.InRange(45f, 60f), "intro length in seconds");
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
                Assert.Greater(found, 0, "cue " + c.id + " has at least one clip under Resources/" + MotionComic.SfxRoot);
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
        public void SkipEndsOnTheFirstFrameAndThePromptWaitsOneSecond()
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

            Assert.AreEqual(0f, MotionComic.SkipPromptAlpha(0.0, 1f));
            Assert.AreEqual(0f, MotionComic.SkipPromptAlpha(0.99, 1f));
            Assert.Greater(MotionComic.SkipPromptAlpha(1.4, 1f), 0.99f);
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
