using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// DG-01 main menu over the backdrop bot match: the logo on the right, a column of chunky stickers on the left. Lines
    /// come from OverlayRegistry.MenuEntries: Play, How to play, Barracks, Controls, Settings, Codex, Credits, Quit. Lines
    /// with no screen yet (How to play until the guided match registers its start, Barracks, Codex, Credits) show dimmed
    /// with COMING SOON and are skipped by navigation and the mouse. Controls and Settings open the pause menu's settings
    /// page (Controls tab, Audio tab); its Back returns here. Back: the title.
    /// </summary>
    public sealed class MainMenu : FlowScreen
    {
        public const float ColX = 150f, ColY = 250f, ItemW = 560f, ItemH = 64f, ItemStep = 76f;   // 9 lines (integration batch 2 added Spectate)

        readonly Dictionary<string, MenuItem> _items = new Dictionary<string, MenuItem>();
        readonly List<MenuItem> _order = new List<MenuItem>();
        List<MenuEntry> _entries;
        Text _tip;

        /// <summary>The menu's lines, in order (tests).</summary>
        public IReadOnlyList<MenuEntry> Entries => _entries;
        public IReadOnlyList<MenuItem> Items => _order;

        /// <summary>The line of an entry id ("play", "settings", ...), or null.</summary>
        public MenuItem Item(string id)
        {
            EnsureBuilt();
            return _items.TryGetValue(id, out MenuItem it) ? it : null;
        }

        public void FocusEntry(string id) => Focus(Item(id));

        protected override string HintKeys => "ENTER SELECT   ·   ESC TITLE";
        protected override string HintPad => "A SELECT   ·   B TITLE";
        protected override float WashAlpha => 0.45f;

        protected override void BuildContent(RectTransform space)
        {
            // Left: a navy band behind the column so the stickers read over any part of the backdrop.
            VectorShape band = MenuUi.Shape(space, "Column Band", ShapeKind.RoundRect, ColX - 40f, ColY - 110f, ItemW + 80f, 860f,
                CMTokens.WithAlpha(Navy, 0.55f), 22f);
            band.OutlinePx = 0f;
            band.RimPx = 0f;
            MenuUi.Label(space, "Heading", CMTokens.Display, 44, Cream, TextAnchor.MiddleLeft, ColX, ColY - 90f, ItemW, 60f, "MAIN MENU", 4f);

            _entries = OverlayRegistry.MenuEntries();
            for (int i = 0; i < _entries.Count; i++)
            {
                MenuEntry e = _entries[i];
                MenuEntry entry = e;
                MenuItem it = MenuUi.Item(space, "Entry " + e.Id, ColX, ColY + i * ItemStep, ItemW, ItemH, e.Label, MenuItem.Kind.Button, 32,
                    false, CMTokens.Display);
                it.Body.Radius = 16f;
                it.Body.RimPx = 3f;
                if (e.Available) it.Submitted = () => Choose(entry);
                else MarkComingSoon(it, ItemW, ItemH);
                _items[e.Id] = it;
                _order.Add(it);
            }
            ChainVertical(_order);
            DefaultFocus = Item(OverlayRegistry.Play);

            // Right: the logo (the bible logo when the logo lane's export is in, else the redrawn one, else the name).
            Texture2D logo = Resources.Load<Texture2D>("UI/Logo/critical-mass-bible-logo@2x");
            if (logo == null) logo = Resources.Load<Texture2D>("UI/Logo/critical-mass-logo@2x");
            if (logo != null)
            {
                RawImage img = MenuUi.Rt(space, "Logo").gameObject.AddComponent<RawImage>();
                img.texture = logo;
                img.raycastTarget = false;
                float w = 640f, h = w * logo.height / Mathf.Max(1f, logo.width);
                MenuUi.Place(img.rectTransform, 1300f - w * 0.5f, 380f - h * 0.5f, w, h);
            }
            else
            {
                MenuUi.Label(space, "Logo Text", CMTokens.Display, 88, Cream, TextAnchor.MiddleCenter, 900f, 300f, 800f, 120f, "CRITICAL MASS", 5f);
            }
            MenuUi.Label(space, "Tagline", CMTokens.Ui, 26, Cream, TextAnchor.MiddleCenter, 900f, 690f, 800f, 40f,
                "16 v 16. Hold the hill. Turn the Kaiju.", 2f);
            _tip = MenuUi.Label(space, "Status", CMTokens.Ui, 20, CMTokens.WithAlpha(Cream, 0.85f), TextAnchor.MiddleCenter, 900f, 740f,
                800f, 34f, "", 2f);
        }

        protected override void OnShow()
        {
            int n = GameFlow.MatchesStarted;
            HudText.Set(_tip, n == 0 ? "" : n == 1 ? "1 match played this session" : n + " matches played this session");
        }

        void Choose(MenuEntry e)
        {
            switch (e.Id)
            {
                case OverlayRegistry.Play:
                    if (Spectator.SpectatorMode.SpectateNextMatch) Spectator.SpectatorMode.SpectateNextMatch = false;
                    Director.ShowSetup();
                    return;
                case OverlayRegistry.Spectate:
                    // Match setup opens with the team line on SPECTATE and the start button reading WATCH MATCH.
                    Spectator.SpectatorMode.SpectateNextMatch = true;
                    Director.ShowSetup();
                    return;
                case OverlayRegistry.Codex:
                    if (e.ScreenType == null)
                    {
                        Director.OpenPausePage(Codex.CodexPage.Label, e.Id);
                        return;
                    }
                    break;
                case OverlayRegistry.Controls:
                    Director.OpenSettings(PauseMenu.Tab.Controls, e.Id);
                    return;
                case OverlayRegistry.Settings:
                    Director.OpenSettings(PauseMenu.Tab.Audio, e.Id);
                    return;
                case OverlayRegistry.Quit:
                    PauseMenu.QuitGame();
                    return;
                case OverlayRegistry.HowTo:
                    if (e.ScreenType == null)
                    {
                        if (OverlayRegistry.StartTutorial()) Hide();
                        return;
                    }
                    break;
            }
            Director.OpenRegistered(e);
        }

        public override void Back() => Director.ShowTitle();
    }
}
