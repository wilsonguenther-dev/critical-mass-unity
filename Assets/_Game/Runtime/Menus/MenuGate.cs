namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// b15 view polish (Editor review item 4): ONE "a menu or overlay is open" gate. In-world prompts (the target prompt
    /// "F DIG", the terrain strip) and the sound captions are screen-space overlay canvases, which Unity draws over every
    /// camera-space menu canvas whatever their sorting order, so each one used to keep its own list of menus to hide under
    /// (TerrainAffordanceView.MenuCovers) and missed class select, the profile, pause and the feedback card. Everything that
    /// must hide under a menu reads this. View only.
    /// </summary>
    public static class MenuGate
    {
        /// <summary>Tests: force the gate (null = the real state).</summary>
        public static bool? OverrideForTests;

        /// <summary>True while any full menu or overlay covers the match view.</summary>
        public static bool AnyOpen => OverrideForTests ?? Compute();

        static bool Compute() =>
            OverlayGuard.Active                                                     // profile, feedback card, credits, characters, replays, notices, story
            || ClassSelect.SetupOpen
            || CriticalMass.Runtime.Menu.PauseMenu.AnyOpen
            || CriticalMass.Runtime.Hud.Scoreboard.AnyOpen
            || ProfileMenuScreen.AnyOpen
            || FeedbackCard.IsShowing
            || CriticalMass.Runtime.Online.OnlineMenuScreen.AnyOpen
            || LoadingScreen.IsOpen
            || CriticalMass.Runtime.Menus.Characters.CharactersScreen.IsOpenNow || ControlsCardScreen.IsOpen
            || CriticalMass.Runtime.Codex.LoreCard.IsOpen || CriticalMass.Runtime.Menu.CharacterPanel.IsOpen
            || CriticalMass.Runtime.Menu.RewardsOverlay.IsOpen || Credits.IsOpen
            || CriticalMass.Runtime.Menu.ResultsHighlights.IsOpen;
    }
}
