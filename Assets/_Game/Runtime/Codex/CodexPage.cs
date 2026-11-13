using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Codex
{
    /// <summary>
    /// The pause-menu Codex (DG-25), a page of the pause menu (PauseMenu.AddPage, root entry "CODEX"): "BASIN RELICS",
    /// "3 OF 8 FOUND", and a 2 x 4 grid of relic plates. A found relic shows its name and both lore lines; an unfound one
    /// shows "UNDISCOVERED" and a hint. The secret Crown of the Hill adds a brass line under the header once found.
    /// View-only: the one focusable element is BACK (Enter / A / click, or Esc / B anywhere on the page).
    /// </summary>
    public static class CodexPage
    {
        public const string Label = "CODEX";
        public const string Title = "CODEX";

        const float GridX = PauseMenu.ContentX, GridY = PauseMenu.PanelY + 116f;
        const float TileW = 630f, TileH = 150f, GapX = 20f, GapY = 14f;

        sealed class Tile
        {
            public VectorShape Plate, Badge;
            public Text Name, Line1, Line2;
        }

        static Tile[] _tiles;
        static Text _count, _crown;

        /// <summary>Text currently shown on tile <paramref name="i"/> (tests), name then lines.</summary>
        public static string TileText(int i) =>
            _tiles != null && i >= 0 && i < _tiles.Length && _tiles[i].Name != null
                ? _tiles[i].Name.text + "|" + _tiles[i].Line1.text + "|" + _tiles[i].Line2.text
                : string.Empty;

        public static string CountText => _count != null ? _count.text : string.Empty;

        public static Selectable Build(PauseMenu menu, RectTransform page)
        {
            MenuUi.Panel(page, "Panel", PauseMenu.PanelX, PauseMenu.PanelY, PauseMenu.PanelW, PauseMenu.PanelH);
            MenuUi.Fit(MenuUi.Label(page, "Heading", CMTokens.Display, UiScale.TypeLead, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, PauseMenu.ContentX,
                PauseMenu.PanelY + 30f, 600f, 60f, "BASIN RELICS", 3f), 34);
            _count = MenuUi.Fit(MenuUi.Label(page, "Count", CMTokens.Display, UiScale.TypeButton, CMTokens.ToolBrass.Color, TextAnchor.MiddleRight,
                PauseMenu.PanelX + PauseMenu.PanelW - 40f - 200f - 24f - 300f - 24f - 300f, PauseMenu.PanelY + 30f, 300f, 60f, "", 3f), 26);
            _crown = MenuUi.Label(page, "Crown", CMTokens.Ui, UiScale.TypeSmall, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, PauseMenu.ContentX + 330f,
                PauseMenu.PanelY + 30f, 400f, 60f, "", 2f);
            MenuItem back = MenuUi.Item(page, "Back", PauseMenu.PanelX + PauseMenu.PanelW - 40f - 200f, PauseMenu.PanelY + 30f, 200f, 60f,
                "BACK", MenuItem.Kind.Button, UiScale.TypeButton, true, CMTokens.Display);
            back.Submitted = menu.ShowRoot;
            // The story's motion comic, from the same player as the intro (Esc / B / any key ends it and returns here).
            MenuItem story = MenuUi.Item(page, "Story", PauseMenu.PanelX + PauseMenu.PanelW - 40f - 200f - 24f - 300f, PauseMenu.PanelY + 30f,
                300f, 60f, "WATCH STORY", MenuItem.Kind.Button, UiScale.TypeButton, true, CMTokens.Display);
            story.Submitted = () => Menus.StoryIntro.Play();
            back.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = story };
            story.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = back };

            _tiles = new Tile[CodexProgress.RelicCount];
            for (int i = 0; i < _tiles.Length; i++)
            {
                int col = i % 2, row = i / 2;
                float x = GridX + col * (TileW + GapX), y = GridY + row * (TileH + GapY);
                RectTransform rt = MenuUi.Rt(page, "Relic " + (i + 1));
                MenuUi.Place(rt, x, y, TileW, TileH);
                var t = new Tile();
                t.Plate = MenuUi.Shape(rt, "Plate", ShapeKind.RoundRect, 0f, 0f, TileW, TileH, CMTokens.VoidNavy.Color, 14f);
                t.Plate.RimPx = 2f;
                t.Badge = MenuUi.Shape(rt, "Badge", ShapeKind.Diamond, 26f, 42f, 50f, 60f, CMTokens.ToolBrass.Color, -1f);
                t.Name = MenuUi.Label(rt, "Name", CMTokens.Display, UiScale.TypeBody, CMTokens.ToolBrass.Color, TextAnchor.MiddleLeft, 100f, 14f, TileW - 120f, 40f, "", 3f);
                t.Line1 = MenuUi.Label(rt, "Line 1", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 100f, 60f, TileW - 120f, 32f, "", 2f);
                t.Line2 = MenuUi.Label(rt, "Line 2", CMTokens.Ui, UiScale.TypeSmall, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 100f, 94f, TileW - 120f, 32f, "", 2f);
                _tiles[i] = t;
            }
            Refresh();
            return back;
        }

        /// <summary>Re-reads CodexProgress (every time the page opens).</summary>
        public static void Refresh()
        {
            if (_tiles == null || _tiles[0].Name == null) return;
            HudText.Set(_count, CodexProgress.CountLabel(CodexProgress.FoundCount) + " FOUND");
            if (CodexProgress.IsFound(CodexProgress.CrownIndex))
                HudText.Set(_crown, "+ " + LoreText.GetRelic(CodexProgress.IdOf(CodexProgress.CrownIndex)).Name.ToUpperInvariant());
            else HudText.Set(_crown, string.Empty);
            for (int i = 0; i < _tiles.Length; i++)
            {
                Tile t = _tiles[i];
                bool found = CodexProgress.IsFound(i);
                if (found)
                {
                    LoreText.Relic r = LoreText.GetRelic(CodexProgress.IdOf(i));
                    HudText.Set(t.Name, r.Name.ToUpperInvariant());
                    HudText.Set(t.Line1, r.Line1);
                    HudText.Set(t.Line2, r.Line2);
                }
                else
                {
                    HudText.Set(t.Name, "UNDISCOVERED");
                    HudText.Set(t.Line1, "Somewhere off the beaten path.");
                    HudText.Set(t.Line2, string.Empty);
                }
                t.Name.color = found ? CMTokens.ToolBrass.Color : CMTokens.Stone.Color;
                t.Badge.SetFill(found ? CMTokens.ToolBrass.Color : CMTokens.MeterEmpty.Color);
                t.Plate.SetFill(found ? CMTokens.PanelNavy.Color : CMTokens.VoidNavy.Color);
            }
        }
    }
}
