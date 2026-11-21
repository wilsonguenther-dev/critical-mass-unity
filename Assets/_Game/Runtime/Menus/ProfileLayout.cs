using System.Collections.Generic;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using UnityEngine;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>The profile screen's three pages.</summary>
    public enum ProfileTab { Mastery = 0, History = 1, Achievements = 2 }

    /// <summary>
    /// b15 profile: the one layout of the PROFILE screen, in 1920 x 1080 reference px, y down from the top-left. Contained:
    /// nothing scrolls; the history pages (PREV / NEXT) and everything else sits inside the safe margin. Fixed zones from
    /// top to bottom never overlap: title, player line, tab row, content (one page at a time), bottom row, hint. Pure
    /// numbers, so the EditMode tests check them at 1280 x 720, 1920 x 1080 and 2560 x 1080 without a screen.
    /// </summary>
    public static class ProfileLayout
    {
        public const float Left = 120f, Width = 1680f, Right = Left + Width;

        public static readonly Rect Title = new Rect(460f, 36f, 1000f, 80f);
        public static readonly Rect PlayerLine = new Rect(Left, 120f, Width, 36f);
        public const int TabCount = 3;
        public const float TabW = 400f, TabH = 56f, TabY = 166f;
        public static readonly Rect Tabs = new Rect(Left, TabY, Width, TabH);
        public static readonly Rect Content = new Rect(Left, 240f, Width, 624f);
        public const float BottomY = 884f, BottomH = 76f;
        public static readonly Rect Bottom = new Rect(Left, BottomY, Width, BottomH);
        public static readonly Rect Hint = new Rect(Left, 972f, Width, 34f);

        public static Rect Tab(int i)
        {
            float total = TabCount * TabW + (TabCount - 1) * UiScale.ButtonGap;
            float x0 = 960f - total * 0.5f;
            return new Rect(x0 + i * (TabW + UiScale.ButtonGap), TabY, TabW, TabH);
        }

        // ---- bottom row: PREV, page, NEXT on the left (history only), BACK on the right ----
        public static readonly Rect PrevButton = new Rect(Left, BottomY, 260f, BottomH);
        public static readonly Rect PageText = new Rect(Left + 276f, BottomY, 300f, BottomH);
        public static readonly Rect NextButton = new Rect(Left + 592f, BottomY, 260f, BottomH);
        public static readonly Rect BackButton = new Rect(Right - 360f, BottomY, 360f, BottomH);

        // ---- mastery: the class list on the left, the track and the look on the right ----
        public const int ClassCount = 6;
        public const float ClassW = 320f, ClassH = 80f, ClassPitch = 92f;
        public static Rect ClassButton(int i) => new Rect(Left, Content.y + i * ClassPitch, ClassW, ClassH);
        public const float PaneX = Left + ClassW + 32f, PaneW = Right - PaneX;
        public static readonly Rect ClassHeading = new Rect(PaneX, 244f, PaneW, 52f);
        public static readonly Rect XpBar = new Rect(PaneX, 304f, PaneW, 24f);
        public static readonly Rect XpText = new Rect(PaneX, 334f, PaneW, 30f);
        public static readonly Rect EarnedBy = new Rect(PaneX, 370f, PaneW, 30f);
        public static readonly Rect NextLine = new Rect(PaneX, 406f, PaneW, 30f);
        public const float LookRowW = 800f, LookRowH = 60f, LookPitch = 70f, LookTop = 452f;
        public static Rect LookRow(int k) => new Rect(PaneX, LookTop + k * LookPitch, LookRowW, LookRowH);
        public static readonly Rect LookCard = new Rect(PaneX + LookRowW + 32f, LookTop, Right - (PaneX + LookRowW + 32f), 4 * LookPitch - 10f);
        public static readonly Rect Career = new Rect(PaneX, 742f, PaneW, 32f);
        public static readonly Rect CareerTotals = new Rect(PaneX, 780f, PaneW, 76f);

        // ---- history: eight rows and the detail line ----
        public const int HistoryRows = 8;
        public const float RowH = 60f, RowPitch = 68f;
        public static Rect HistoryRow(int i) => new Rect(Left, Content.y + i * RowPitch, Width, RowH);
        public static readonly Rect HistoryDetail = new Rect(Left, Content.y + HistoryRows * RowPitch + 8f, Width, 64f);
        public static readonly Rect HistoryEmpty = new Rect(Left + 140f, Content.y + 200f, Width - 280f, 120f);

        // ---- achievements: the count and two columns of five cells ----
        public static readonly Rect AchCount = new Rect(Left, 244f, Width, 40f);
        public const int AchCols = 2, AchRows = 5;
        public const float AchW = 820f, AchH = 98f, AchPitch = 108f, AchTop = 296f, AchColGap = 40f;
        public static Rect AchCell(int i) => new Rect(Left + (i / AchRows) * (AchW + AchColGap), AchTop + (i % AchRows) * AchPitch, AchW, AchH);

        /// <summary>Inside a cell: the title, the rule, the bar and its number.</summary>
        public static Rect AchTitle(Rect c) => new Rect(c.x + 20f, c.y + 8f, c.width - 240f, 34f);
        public static Rect AchState(Rect c) => new Rect(c.xMax - 220f, c.y + 8f, 200f, 34f);
        public static Rect AchRule(Rect c) => new Rect(c.x + 20f, c.y + 44f, c.width - 40f, 26f);
        public static Rect AchBar(Rect c) => new Rect(c.x + 20f, c.y + 76f, c.width - 40f, 12f);

        /// <summary>A rect in screen px for a screen of w x h (the canvas is scaled by HudLayout.Scale and centred).</summary>
        public static Rect ToPixels(Rect r, float w, float h)
        {
            float s = HudLayout.Scale(w, h);
            float ox = (w - HudLayout.RefW * s) * 0.5f, oy = (h - HudLayout.RefH * s) * 0.5f;
            return new Rect(ox + r.x * s, oy + r.y * s, r.width * s, r.height * s);
        }

        /// <summary>The fixed zones (never overlapping).</summary>
        public static List<(string name, Rect rect)> Zones() => new List<(string, Rect)>
        {
            ("title", Title), ("player", PlayerLine), ("tabs", Tabs), ("content", Content), ("bottom", Bottom), ("hint", Hint),
        };

        /// <summary>What a page draws inside the content area (never overlapping each other, all inside Content).</summary>
        public static List<(string name, Rect rect)> Panels(ProfileTab tab)
        {
            var list = new List<(string, Rect)>();
            switch (tab)
            {
                case ProfileTab.Mastery:
                    for (int i = 0; i < ClassCount; i++) list.Add(("class " + i, ClassButton(i)));
                    list.Add(("heading", ClassHeading));
                    list.Add(("xp bar", XpBar));
                    list.Add(("xp text", XpText));
                    list.Add(("earned by", EarnedBy));
                    list.Add(("next", NextLine));
                    for (int k = 0; k < 4; k++) list.Add(("look " + k, LookRow(k)));
                    list.Add(("look card", LookCard));
                    list.Add(("career", Career));
                    list.Add(("career totals", CareerTotals));
                    break;
                case ProfileTab.History:
                    for (int i = 0; i < HistoryRows; i++) list.Add(("row " + i, HistoryRow(i)));
                    list.Add(("detail", HistoryDetail));
                    break;
                case ProfileTab.Achievements:
                    list.Add(("count", AchCount));
                    for (int i = 0; i < AchCols * AchRows; i++) list.Add(("achievement " + i, AchCell(i)));
                    break;
            }
            return list;
        }

        /// <summary>The bottom row's buttons (all inside Bottom, never overlapping).</summary>
        public static List<(string name, Rect rect)> BottomButtons() => new List<(string, Rect)>
        {
            ("prev", PrevButton), ("page", PageText), ("next", NextButton), ("back", BackButton),
        };

        /// <summary>The smallest type the screen uses (UiScale.TypeSmall): at 1280 x 720 it still draws at 12 px.</summary>
        public const int SmallestType = UiScale.TypeSmall;
    }
}
