using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// DG-23: the moment rules on a scripted event stream (first knock-out, hatch, biggest multi-knock-out, each stamped
    /// with the tick it was read on), and the photo path: the framed picture (Void Navy frame, brass rule, logo band),
    /// the PNG encoder (decoded back by Unity, upright) and the atomic write (no temp file left).
    /// </summary>
    public sealed class MomentsTests
    {
        static World NewWorld() => World.CreateWorld(new WorldOptions
        {
            Seed = 1337, TeamSize = 16, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, Config = GameConfig.CreateDefault(),
        });

        static void At(World w, int tick)
        {
            w.Tick = tick;
            w.Match.Time = tick / 30.0;
        }

        [Test]
        public void TheThreeMomentsCarryTheTickTheyHappenedOn()
        {
            World w = NewWorld();
            var log = new MomentLog();
            log.Reset(w);
            Assert.AreEqual(0, log.Poll(w));

            At(w, 300);
            World.PushEvent(w, SimEventType.Death, 20, 3, 50, 40, 22);      // actor 3 knocks out 20: first knock-out
            Assert.AreEqual(1 << (int)MomentKind.FirstKnockOut, log.Poll(w));
            Assert.AreEqual(300, log.Get(MomentKind.FirstKnockOut).Tick);
            Assert.IsFalse(log.Has(MomentKind.MultiKnockOut), "one knock-out is not a multi");

            At(w, 360);                                                      // 2 s later, same killer: double
            World.PushEvent(w, SimEventType.Death, 21, 3, 50, 40, 22);
            Assert.AreEqual(1 << (int)MomentKind.MultiKnockOut, log.Poll(w));
            Assert.AreEqual(2, log.Get(MomentKind.MultiKnockOut).Count);
            Assert.AreEqual(360, log.Get(MomentKind.MultiKnockOut).Tick);

            At(w, 7200);
            World.PushEvent(w, SimEventType.Hatch, -1, -1, 56, 12, 1);
            Assert.AreEqual(1 << (int)MomentKind.Hatch, log.Poll(w));
            Assert.AreEqual(7200, log.Get(MomentKind.Hatch).Tick);
            Assert.AreEqual(1, log.Get(MomentKind.Hatch).Code);

            // Actor 7: three within the window replace the double; a gap over 4 s breaks a chain.
            At(w, 8000); World.PushEvent(w, SimEventType.Death, 22, 7, 0, 0, 1); log.Poll(w);
            At(w, 8030); World.PushEvent(w, SimEventType.Death, 23, 7, 0, 0, 1); log.Poll(w);
            Assert.AreEqual(360, log.Get(MomentKind.MultiKnockOut).Tick, "an equal double does not replace the first double");
            At(w, 8090); World.PushEvent(w, SimEventType.Death, 24, 7, 0, 0, 1);
            Assert.AreEqual(1 << (int)MomentKind.MultiKnockOut, log.Poll(w));
            Assert.AreEqual(3, log.Get(MomentKind.MultiKnockOut).Count);
            Assert.AreEqual(8090, log.Get(MomentKind.MultiKnockOut).Tick);
            At(w, 8090 + 5 * 30); World.PushEvent(w, SimEventType.Death, 25, 7, 0, 0, 1); log.Poll(w);
            Assert.AreEqual(3, log.Get(MomentKind.MultiKnockOut).Count, "a chain broken by 5 s does not grow");
            Assert.AreEqual(300, log.Get(MomentKind.FirstKnockOut).Tick, "the first knock-out never moves");

            log.Reset(w);
            Assert.IsFalse(log.Has(MomentKind.FirstKnockOut) || log.Has(MomentKind.Hatch) || log.Has(MomentKind.MultiKnockOut), "reset clears");
        }

        [Test]
        public void TheFramedPictureHasTheNavyFrameTheBrassRuleAndTheLogo()
        {
            // A 40 x 20 red shot (rows bottom-up, the top row green) and a 10 x 10 opaque white logo.
            const int w = 40, h = 20;
            var shot = new byte[w * h * 4];
            for (int i = 0; i < w * h; i++) { shot[i * 4] = 255; shot[i * 4 + 3] = 255; }
            for (int x = 0; x < w; x++) { int p = ((h - 1) * w + x) * 4; shot[p] = 0; shot[p + 1] = 255; }
            var logo = new byte[10 * 10 * 4];
            for (int i = 0; i < logo.Length; i++) logo[i] = 255;
            PhotoFrame.Layout l = PhotoFrame.ForPhoto(new PhotoFrame.Tile { Rgba = shot, W = w, H = h, BottomUp = true });
            l.Logo = logo;
            l.LogoW = 10;
            l.LogoH = 10;
            byte[] o = PhotoFrame.Compose(l);
            Color32 navy = Hex(Palette.VoidNavy), brass = Hex(Palette.Brass);
            Assert.AreEqual(navy, Px(o, l.Width, 0, 0), "frame corner is Void Navy");
            Assert.AreEqual(new Color32(0, 255, 0, 255), Px(o, l.Width, l.Margin + 1, l.Margin), "top row of the shot lands at the top");
            Assert.AreEqual(new Color32(255, 0, 0, 255), Px(o, l.Width, l.Margin + 1, l.Margin + 5));
            Assert.AreEqual(brass, Px(o, l.Width, l.Width / 2, l.RuleY), "brass rule");
            Assert.AreEqual(new Color32(255, 255, 255, 255), Px(o, l.Width, l.Margin + 2, l.BandY + l.BandH / 2), "logo in the band");
            Assert.AreEqual(l.Width * l.Height * 4, o.Length);

            byte[] png = PngWriter.Encode(o, l.Width, l.Height);
            var tex = new Texture2D(2, 2);
            Assert.IsTrue(tex.LoadImage(png), "Unity decodes the PNG");
            Assert.AreEqual(l.Width, tex.width);
            Assert.AreEqual(l.Height, tex.height);
            // Texture rows run bottom-up: the top-left of the picture is (0, height - 1).
            Assert.AreEqual(navy, (Color32)tex.GetPixel(0, tex.height - 1));
            Assert.AreEqual(new Color32(0, 255, 0, 255), (Color32)tex.GetPixel(l.Margin + 1, tex.height - 1 - l.Margin));
            Object.DestroyImmediate(tex);
        }

        [Test]
        public void TheHighlightsCardLaysOutThreeStillsAndPlatesTheMissingOne()
        {
            var a = new byte[64 * 36 * 4];
            for (int i = 0; i < a.Length; i++) a[i] = 200;
            var tiles = new[]
            {
                new PhotoFrame.Tile { Rgba = a, W = 64, H = 36, BottomUp = true },
                new PhotoFrame.Tile { Rgba = null, W = 64, H = 36 },
                new PhotoFrame.Tile { Rgba = a, W = 64, H = 36, BottomUp = true },
            };
            PhotoFrame.Layout l = PhotoFrame.ForCard(tiles);
            Assert.AreEqual(2 * 32 + 3 * 64 + 2 * 24, l.Width);
            byte[] o = PhotoFrame.Compose(l);
            Assert.AreEqual(new Color32(200, 200, 200, 255), Px(o, l.Width, tiles[0].X + 3, tiles[0].Y + 3));
            Assert.AreEqual(new Color32(0x1B, 0x31, 0x4D, 255), Px(o, l.Width, tiles[1].X + 3, tiles[1].Y + 3), "missing still: Panel Navy plate");
        }

        [Test]
        public void PhotosAreWrittenAtomically()
        {
            string dir = Path.Combine(Path.GetTempPath(), "cm-photo-test-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                byte[] png = PngWriter.Encode(new byte[4 * 4 * 4], 4, 4);
                string a = PngWriter.WriteAtomic(dir, "Shot", png);
                string b = PngWriter.WriteAtomic(dir, "Shot", png);
                Assert.AreNotEqual(a, b, "never overwrites");
                Assert.IsTrue(File.Exists(a) && File.Exists(b));
                Assert.AreEqual(0, Directory.GetFiles(dir, "*.tmp").Length, "no temp file left");
                CollectionAssert.AreEqual(png, File.ReadAllBytes(a));
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        static Color32 Px(byte[] o, int w, int x, int y)
        {
            int p = (y * w + x) * 4;
            return new Color32(o[p], o[p + 1], o[p + 2], o[p + 3]);
        }

        static Color32 Hex(string h)
        {
            int v = System.Convert.ToInt32(h.Substring(1), 16);
            return new Color32((byte)(v >> 16), (byte)(v >> 8), (byte)v, 255);
        }
    }
}
