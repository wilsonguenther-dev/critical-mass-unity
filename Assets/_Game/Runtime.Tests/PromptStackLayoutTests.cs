using CriticalMass.Runtime.Hud;
using NUnit.Framework;
using UnityEngine;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b14 (owner's screenshot: prompts stacked on each other, text cut): the one prompt stack. At most two prompts show,
    /// by priority (one while the intro line holds the lower lane), and no two prompt rects that can show together overlap
    /// each other or the captions, subtitles, player card, minimap, class kit, coin and power-up chips (nor the lower lane
    /// for the bottom slot) at 1280x720, 1920x1080, 2560x1080 and 3440x1440.
    /// </summary>
    public sealed class PromptStackLayoutTests
    {
        static readonly Vector2Int[] Screens = { new Vector2Int(1280, 720), new Vector2Int(1920, 1080), new Vector2Int(2560, 1080), new Vector2Int(3440, 1440) };

        [Test]
        public void AtMostTwoShowByPriority()
        {
            bool[] all = { true, true, true };
            Assert.AreEqual(0, PromptStack.Assign(all, 0, 2));
            Assert.AreEqual(1, PromptStack.Assign(all, 1, 2));
            Assert.AreEqual(-1, PromptStack.Assign(all, 2, 2), "the third waits");
            bool[] noStation = { false, true, true };
            Assert.AreEqual(0, PromptStack.Assign(noStation, 1, 2));
            Assert.AreEqual(1, PromptStack.Assign(noStation, 2, 2), "it moves up when the station prompt goes");
            Assert.AreEqual(-1, PromptStack.Assign(noStation, 0, 2), "not wanted = not shown");
            Assert.AreEqual(1, PromptStack.Visible(true), "one prompt while the lower lane is held (slot 1 is its bottom)");
            Assert.AreEqual(-1, PromptStack.Assign(all, 1, PromptStack.Visible(true)));
            Assert.AreEqual(2, PromptStack.Visible(false));
        }

        static Rect Px(Rect r, float w, float h) => HudFrames.PixelRect(r, w, h, Vector4.zero);

        [Test]
        public void NoPromptRectsOverlapAtAnyScreenSize()
        {
            var sources = new[] { PromptStack.Source.Station, PromptStack.Source.Pill, PromptStack.Source.Strip };
            foreach (Vector2Int s in Screens)
            {
                float w = s.x, h = s.y;
                Rect[] others = { Px(HudZones.Subtitles, w, h), Px(HudZones.Caption(0), w, h), Px(HudZones.Caption(1), w, h), Px(HudZones.PlayerCard, w, h),
                                  Px(HudZones.Minimap, w, h), Px(HudZones.Kit, w, h), Px(HudZones.Coins, w, h), Px(HudZones.Power, w, h) };
                string[] names = { "subtitles", "caption 0", "caption 1", "player card", "minimap", "class kit", "coins", "power-up timer" };
                Rect lower = Px(HudZones.Lower, w, h);
                foreach (PromptStack.Source one in sources)
                {
                    Rect r0 = PromptStack.ScreenRect(one, 0, w, h);
                    Assert.IsFalse(r0.Overlaps(lower), $"{w}x{h}: {one} (alone, slot 0) overlaps the lower lane {lower}");
                }
                // every pair that can be on screen together: two sources in slots 0 and 1
                for (int a = 0; a < sources.Length; a++)
                    for (int b = 0; b < sources.Length; b++)
                    {
                        if (a == b) continue;
                        Rect ra = PromptStack.ScreenRect(sources[a], 0, w, h), rb = PromptStack.ScreenRect(sources[b], 1, w, h);
                        Debug.Log($"[CM.PromptStack] {w}x{h}: {sources[a]} slot 0 {ra}, {sources[b]} slot 1 {rb}");
                        Assert.IsFalse(ra.Overlaps(rb), $"{w}x{h}: {sources[a]} and {sources[b]} overlap");
                        Assert.IsTrue(ra.xMin >= 0 && ra.xMax <= w && rb.xMin >= 0 && rb.xMax <= w && rb.yMin >= 0 && ra.yMax <= h, $"{w}x{h}: on screen");
                        for (int o = 0; o < others.Length; o++)
                        {
                            Assert.IsFalse(ra.Overlaps(others[o]), $"{w}x{h}: {sources[a]} (slot 0) overlaps the {names[o]} {others[o]}");
                            Assert.IsFalse(rb.Overlaps(others[o]), $"{w}x{h}: {sources[b]} (slot 1) overlaps the {names[o]} {others[o]}");
                        }
                    }
            }
        }
    }
}
