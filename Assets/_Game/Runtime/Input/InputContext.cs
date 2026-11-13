namespace CriticalMass.Runtime.Input
{
    /// <summary>Who owns the shared controls right now (2026-11-12, UI rough spots).</summary>
    public enum InputContextKind { Play = 0, MapOpen = 1, Spectating = 2 }

    /// <summary>
    /// One rule for the controls several actions share, resolved by context instead of firing everything at once
    /// (integration batch 2, known defect 6):
    /// <para>Controller d-pad: in play it is Map (up), Ping (left), HUD cursor (down) and Minimap zoom (right); while
    /// spectating it belongs to the spectator's objective jumps (Hill, Shrine, bases), so the HUD's d-pad actions wait
    /// (their keyboard keys M, G, Left Alt, N still work).</para>
    /// <para>Mouse wheel (and the bumpers that share its action): in play it switches the Miner's dig / place mode; with the
    /// full map open it zooms the map only; while spectating it zooms the spectator camera only.</para>
    /// Pure functions of the context, so EditMode tests pin every pair.
    /// </summary>
    public static class InputContext
    {
        public static InputContextKind Resolve(bool mapOpen, bool spectating) =>
            spectating ? InputContextKind.Spectating : mapOpen ? InputContextKind.MapOpen : InputContextKind.Play;

        /// <summary>May the HUD's d-pad actions (map, ping, HUD cursor, minimap zoom) take a controller press?</summary>
        public static bool HudOwnsDpad(InputContextKind k) => k != InputContextKind.Spectating;

        /// <summary>May the dig / place switch (wheel or bumper) fire?</summary>
        public static bool DigSwitchOwnsWheel(InputContextKind k) => k == InputContextKind.Play;

        /// <summary>Does the wheel zoom the full map?</summary>
        public static bool MapOwnsWheel(InputContextKind k) => k == InputContextKind.MapOpen;

        /// <summary>Does the wheel zoom the spectator camera?</summary>
        public static bool SpectatorOwnsWheel(InputContextKind k) => k == InputContextKind.Spectating;

        /// <summary>The live context (full map and spectator state read from their owners).</summary>
        public static InputContextKind Current =>
            Resolve(CriticalMass.Runtime.Hud.Interact.FullMap.IsOpen, CriticalMass.Runtime.Spectator.SpectatorMode.IsSpectating);
    }
}
