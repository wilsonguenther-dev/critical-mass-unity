using System.Collections.Generic;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Online;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Tests
{
    /// <summary>
    /// b11-af: the room screen's notice and chat box (OnlineChatHud on OnlineLayout.ChatLane) shows only whole lines. At
    /// 1280 x 720 and 1920 x 1080 (the box and the font at their drawn screen sizes, as OnlineLayoutTests measures), a box
    /// filled with 12 notices of mixed length shows the newest lines whose full height fits: the top edge of the first
    /// shown line is inside the box (Unity's own line layout, else the measured height), the shown block is never taller
    /// than the box, and nothing is cut with an ellipsis. A tight box proves the drop happens on a whole line and keeps
    /// as many lines as fit; a plain label with every notice proves the top-line check sees a cut line.
    /// </summary>
    public sealed class LobbyNoticeFitTests
    {
        static readonly string[] Notices =
        {
            "SHAYDEN joined.",
            "CALEB joined.",
            "The host changed the map to the canyon arena, so everyone check your class before the match starts.",
            "JEISIL left the room.",
            "No player called BROOK.",
            "MAX joined.",
            "AIDAN is muted for you.",
            "Everyone is ready. Bots take the other seats, and the host can start the match whenever the room is set.",
            "TRAVIS joined.",
            "AIDAN can be heard again.",
            "A very long notice that keeps going so it wraps over more than one line inside the room screen's chat box at the smallest screen.",
            "WILSON joined.",
        };

        static List<string> Texts()
        {
            var l = new List<string>(Notices.Length);
            foreach (string n in Notices) l.Add("· " + n);   // system notices, as OnlineChatHud.Redraw writes them
            return l;
        }

        static float Scale(int w, int h) => Mathf.Min(w / OnlineLayout.RefW, h / OnlineLayout.RefH);

        /// <summary>The room's chat text box at a screen size: lane width x (lane height - typing line), in screen px.</summary>
        static Vector2 Box(int w, int h)
        {
            Rect lane = OnlineLayout.ChatLane;
            float s = Scale(w, h);
            return new Vector2(lane.width * s, (lane.height - HudZones.ChatInputH - 6f) * s);
        }

        static Text Body(Transform root, int size, Vector2 box)
        {
            // built as OnlineChatHud.Start builds it, at the drawn size (off a canvas: one unit = one screen px)
            Text t = MenuUi.Label(root, "Lines", CMTokens.Ui, size, CMTokens.WarmCream.Color, TextAnchor.LowerLeft, 0f, 0f, box.x, box.y, "", 2f);
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.supportRichText = false;
            MenuUi.Contain(t, OnlineChatHud.MaxLines);
            MenuUi.Place(t.rectTransform, 0f, 0f, box.x, box.y);
            return t;
        }

        static void AssertWholeLines(Text t, List<string> texts, List<string> picked, string shown, Vector2 box, string where)
        {
            Assert.IsNotNull(t.font, "the font loads");
            Assert.GreaterOrEqual(picked.Count, 1, where + ": the newest notice always shows");
            Assert.AreEqual(texts[texts.Count - 1], picked[picked.Count - 1], where + ": the newest notice is the bottom line");
            int first = texts.Count - picked.Count;
            for (int i = 0; i < picked.Count; i++)
                Assert.AreEqual(texts[first + i], picked[i], where + ": the shown lines are the newest ones, in order");
            Assert.AreEqual(string.Join("\n", picked), shown, where + ": the box shows exactly the picked lines");
            Assert.AreEqual(shown, t.text, where + ": the text rule did not cut a shown line (no ellipsis)");
            Assert.IsTrue(OnlineChatHud.TopLineInside(t), where + ": the top edge of the first shown line is inside the box");
            float drawn = TextFit.HeightOf(t, t.text, t.fontSize, box.x, true);
            Assert.LessOrEqual(drawn, box.y + 0.5f, where + $": the shown block ({drawn:0.0} px) fits the box ({box.y:0.0} px)");
            float lineH = TextFit.LineHeightOf(t, t.fontSize);
            Assert.LessOrEqual(Mathf.RoundToInt(drawn / lineH), OnlineChatHud.MaxLines, where + ": at most MaxLines drawn lines");
        }

        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        public void TwelveMixedNoticesShowOnlyWholeLines(int w, int h)
        {
            Vector2 box = Box(w, h);
            int size = Mathf.Max(1, Mathf.RoundToInt(UiScale.TypeSmall * Scale(w, h)));
            var root = new GameObject("Lobby Notice Probe", typeof(RectTransform));
            try
            {
                Text t = Body(root.transform, size, box);
                List<string> texts = Texts();
                var picked = new List<string>();
                string shown = OnlineChatHud.ShowWholeLines(t, texts, size, box.x, box.y, OnlineChatHud.MaxLines, picked);
                AssertWholeLines(t, texts, picked, shown, box, $"{w}x{h}");
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>A box only about 2.6 lines tall at the same width: lines still drop whole, and as many as fit are kept.</summary>
        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        public void ATightBoxDropsTheOldestLineWholeAndKeepsAllThatFit(int w, int h)
        {
            int size = Mathf.Max(1, Mathf.RoundToInt(UiScale.TypeSmall * Scale(w, h)));
            var root = new GameObject("Lobby Notice Probe", typeof(RectTransform));
            try
            {
                Text t = Body(root.transform, size, Box(w, h));
                Vector2 box = new Vector2(Box(w, h).x, TextFit.LineHeightOf(t, size) * 2.6f);
                MenuUi.Place(t.rectTransform, 0f, 0f, box.x, box.y);
                List<string> texts = Texts();
                var picked = new List<string>();
                string shown = OnlineChatHud.ShowWholeLines(t, texts, size, box.x, box.y, OnlineChatHud.MaxLines, picked);
                AssertWholeLines(t, texts, picked, shown, box, $"{w}x{h} tight");
                Assert.Less(picked.Count, texts.Count, "the tight box cannot hold every notice");
                string next = texts[texts.Count - picked.Count - 1] + "\n" + shown;
                Assert.Greater(TextFit.HeightOf(t, next, size, box.x, true), box.y + 0.5f, "the next older line would not fit whole (none dropped early)");
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>The check is not vacuous: every notice on a plain bottom-aligned label in the tight box pokes out the top.</summary>
        [TestCase(1280, 720)]
        [TestCase(1920, 1080)]
        public void TheTopLineCheckSeesACutLine(int w, int h)
        {
            int size = Mathf.Max(1, Mathf.RoundToInt(UiScale.TypeSmall * Scale(w, h)));
            var go = new GameObject("Plain Notice Probe", typeof(RectTransform));
            try
            {
                Text t = go.AddComponent<Text>();
                t.font = CMTokens.Font(CMTokens.Ui);
                t.fontSize = size;
                t.alignment = TextAnchor.LowerLeft;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                t.supportRichText = false;
                Vector2 box = new Vector2(Box(w, h).x, TextFit.LineHeightOf(t, size) * 2.6f);
                MenuUi.Place(t.rectTransform, 0f, 0f, box.x, box.y);
                t.text = string.Join("\n", Texts());
                Assert.IsFalse(OnlineChatHud.TopLineInside(t), "twelve notices in a box under three lines tall cut the top line");
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
