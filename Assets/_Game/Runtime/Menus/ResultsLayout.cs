using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using UnityEngine;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>The four pages of the one results layout.</summary>
    public enum ResultsTab { Summary = 0, Rewards = 1, Achievements = 2, Highlights = 3 }

    /// <summary>
    /// The one results layout (2026-11-12, UI rough spots), in 1920x1080 reference px, y down from the top-left. Five
    /// zones stacked top to bottom that never overlap: the headline (VICTORY, its line, the reason and the hill split bar),
    /// the tab row, the content area (one page at a time: the summary cards with the MVPs, the rewards, the achievements or
    /// the highlights), the way-out button row (always visible) and the footer. Each page's panels sit inside the content
    /// area and never overlap each other. Pure numbers, so the EditMode layout tests check them at 1280x720 and 2560x1440.
    /// </summary>
    public static class ResultsLayout
    {
        public const float Left = 120f, Width = 1680f;

        public static readonly Rect Title = new Rect(260f, 12f, 1400f, 108f);
        public static readonly Rect Line = new Rect(260f, 120f, 1400f, 32f);
        public static readonly Rect Reason = new Rect(160f, 156f, 1600f, 40f);
        public static readonly Rect HillBar = new Rect(400f, 202f, 1120f, 28f);
        public static readonly Rect AzureSecs = new Rect(220f, 198f, 160f, 36f);
        public static readonly Rect CrimsonSecs = new Rect(1540f, 198f, 160f, 36f);
        /// <summary>The whole headline zone.</summary>
        public static readonly Rect Header = new Rect(Left, 12f, Width, 224f);

        public const int TabCount = 4;
        public const float TabW = 300f, TabH = 52f, TabGap = UiScale.ButtonGap;
        public static readonly Rect Tabs = new Rect(Left, 244f, Width, TabH);

        public static readonly Rect Content = new Rect(Left, 308f, Width, 556f);

        public const float ButtonY = 880f, ButtonH = UiScale.ButtonRowH;
        public static readonly Rect Buttons = new Rect(Left, ButtonY, Width, ButtonH);
        public static readonly Rect Footer = new Rect(1180f, 976f, 600f, 30f);

        /// <summary>Tab i's rect (the row is centred).</summary>
        public static Rect Tab(int i)
        {
            float total = TabCount * TabW + (TabCount - 1) * TabGap;
            float x0 = 960f - total * 0.5f;
            return new Rect(x0 + i * (TabW + TabGap), Tabs.y, TabW, TabH);
        }

        // ---- the way out: MAIN MENU left, CHANGE CLASS and PLAY AGAIN right ----
        public static readonly Rect MainMenuButton = new Rect(Left + 40f, ButtonY, 320f, ButtonH);
        public static readonly Rect ChangeClassButton = new Rect(1100f, ButtonY, 300f, ButtonH);
        public static readonly Rect PlayAgainButton = new Rect(1420f, ButtonY, 340f, ButtonH);

        // ---- summary: three cards ----
        public const float CardW = 520f, CardH = 510f, CardGap = 60f;
        public static float CardsTop => Content.y + (Content.height - CardH) * 0.5f;
        public static Rect Card(int i) => new Rect(Left + i * (CardW + CardGap), CardsTop, CardW, CardH);

        // ---- rewards: the progression panel, scaled down (never up) to fit the content area ----
        public static float FitScale(float w, float h) => Mathf.Min(1f, Mathf.Min(Content.width / w, Content.height / h));

        public static Rect Fitted(float w, float h)
        {
            float s = FitScale(w, h);
            return new Rect(Content.center.x - w * s * 0.5f, Content.y + (Content.height - h * s) * 0.5f, w * s, h * s);
        }

        // ---- achievements: two columns of five ----
        public const int AchCols = 2, AchRowsPerCol = 5;
        public const float AchCellW = 820f, AchCellH = 84f, AchPitch = 92f, AchColGap = 40f;
        public static readonly Rect AchCount = new Rect(Left, 316f, Width, 40f);
        public static Rect AchCell(int i)
        {
            int col = i / AchRowsPerCol, row = i % AchRowsPerCol;
            return new Rect(Left + col * (AchCellW + AchColGap), 368f + row * AchPitch, AchCellW, AchCellH);
        }

        // ---- highlights: three 16:9 stills, labels, SAVE PHOTO ----
        public const float TileW = 480f, TileH = 270f, TileGap = 40f, TileY = 332f;
        public static Rect Tile(int k)
        {
            float total = 3 * TileW + 2 * TileGap;
            return new Rect(960f - total * 0.5f + k * (TileW + TileGap), TileY, TileW, TileH);
        }
        public static Rect TileLabel(int k) { Rect t = Tile(k); return new Rect(t.x, t.yMax + 12f, t.width, 34f); }
        public static Rect TileTime(int k) { Rect t = Tile(k); return new Rect(t.x, t.yMax + 48f, t.width, 28f); }
        public static readonly Rect SaveButton = new Rect(960f - 230f, 704f, 460f, 64f);
        public static readonly Rect SaveStatus = new Rect(Left, 780f, Width, 32f);

        /// <summary>The fixed zones, top to bottom (they must never overlap).</summary>
        public static IEnumerable<KeyValuePair<string, Rect>> Zones()
        {
            yield return new KeyValuePair<string, Rect>("header", Header);
            yield return new KeyValuePair<string, Rect>("tabs", Tabs);
            yield return new KeyValuePair<string, Rect>("content", Content);
            yield return new KeyValuePair<string, Rect>("buttons", Buttons);
            yield return new KeyValuePair<string, Rect>("footer", Footer);
        }

        /// <summary>The panels a page draws inside the content area (they must stay inside it and never overlap).</summary>
        public static List<KeyValuePair<string, Rect>> Panels(ResultsTab tab, float rewardsW, float rewardsH)
        {
            var list = new List<KeyValuePair<string, Rect>>();
            switch (tab)
            {
                case ResultsTab.Summary:
                    list.Add(new KeyValuePair<string, Rect>("kaiju card", Card(0)));
                    list.Add(new KeyValuePair<string, Rect>("you card", Card(1)));
                    list.Add(new KeyValuePair<string, Rect>("mvp card", Card(2)));
                    break;
                case ResultsTab.Rewards:
                    list.Add(new KeyValuePair<string, Rect>("rewards", Fitted(rewardsW, rewardsH)));
                    break;
                case ResultsTab.Achievements:
                    list.Add(new KeyValuePair<string, Rect>("count", AchCount));
                    for (int i = 0; i < AchCols * AchRowsPerCol; i++) list.Add(new KeyValuePair<string, Rect>("achievement " + i, AchCell(i)));
                    break;
                case ResultsTab.Highlights:
                    for (int k = 0; k < 3; k++)
                    {
                        list.Add(new KeyValuePair<string, Rect>("tile " + k, Tile(k)));
                        list.Add(new KeyValuePair<string, Rect>("tile label " + k, TileLabel(k)));
                        list.Add(new KeyValuePair<string, Rect>("tile time " + k, TileTime(k)));
                    }
                    list.Add(new KeyValuePair<string, Rect>("save photo", SaveButton));
                    list.Add(new KeyValuePair<string, Rect>("save status", SaveStatus));
                    break;
            }
            return list;
        }

        /// <summary>
        /// A reference rect on screen at w x h (the canvas scale is min(w/1920, h/1080), the 1920x1080 space centred), in
        /// screen px with y down.
        /// </summary>
        public static Rect OnScreen(Rect r, int w, int h)
        {
            float s = Mathf.Min(w / 1920f, h / 1080f);
            float ox = (w - 1920f * s) * 0.5f, oy = (h - 1080f * s) * 0.5f;
            return new Rect(ox + r.x * s, oy + r.y * s, r.width * s, r.height * s);
        }
    }
}
