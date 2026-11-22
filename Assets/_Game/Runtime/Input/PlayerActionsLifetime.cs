using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// Review defect 0 (a NullReferenceException inside the Input System on the real left Shift after about a dozen match
    /// starts, with "Map/Control/Binding index out of range"): one owner for the life of the Player action map.
    ///
    /// Rules, held here so no caller can break them:
    ///   1. The game never creates an InputAction, map or asset of its own for play; it uses the project-wide asset
    ///      (InputSystem.actions), which the Input System enables at play start and owns for the whole session.
    ///   2. Every change to that asset's shape or bindings (adding actions or bindings, loading, applying or removing
    ///      binding overrides, presets) runs through ChangeWhileDisabled: everything enabled is disabled first, the change
    ///      is made, then exactly what was enabled before is enabled again (per action, so gates such as InteractCore that
    ///      switch single actions off stay as they were). Nothing is re-resolved while enabled.
    ///   3. Each HumanInputSource takes the map with Acquire in OnEnable and gives it back with Release in OnDisable
    ///      (OnDestroy always runs OnDisable first). Acquire never re-creates anything that exists; Enable is idempotent.
    ///      The map stays enabled when the last source goes: replay, spectator and menu code read it between matches,
    ///      and the Input System enabled it, not the source.
    /// PxInputLifetimeTests builds and tears this down 15 times with presets and overrides in between, then presses left
    /// Shift and expects Dodge to fire with nothing logged.
    /// </summary>
    public static class PlayerActionsLifetime
    {
        public const string MapName = "Player";

        static int _holders;
        static InputActionMap _map;

        /// <summary>Live HumanInputSources holding the map (tests).</summary>
        public static int Holders => _holders;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            // Domain reload is off in Play Mode: no count survives from the last session.
            _holders = 0;
            _map = null;
        }

        /// <summary>The Player map, with Dodge and Cancel present (added while disabled when an old asset lacks them) and
        /// enabled. Null when there is no project-wide asset.</summary>
        public static InputActionMap Acquire() => Acquire(InputSystem.actions);

        /// <summary>Acquire on a given asset (tests).</summary>
        public static InputActionMap Acquire(InputActionAsset asset)
        {
            if (asset == null) return null;
            InputActionMap map = asset.FindActionMap(MapName, true);
            if (_map != null && !ReferenceEquals(_map, map)) _holders = 0;
            _map = map;
            if (map.FindAction("Dodge", false) == null || map.FindAction("Cancel", false) == null)
                ChangeWhileDisabled(asset, () =>
                {
                    HumanInputSource.EnsureDodgeAction(map);
                    HumanInputSource.EnsureCancelAction(map);
                });
            _holders++;
            map.Enable();
            return map;
        }

        /// <summary>Gives the map back (OnDisable). Unknown or already released maps are ignored.</summary>
        public static void Release(InputActionMap map)
        {
            if (map == null || !ReferenceEquals(map, _map) || _holders <= 0) return;
            _holders--;
        }

        /// <summary>
        /// Runs change with every enabled action of the asset disabled, then enables again exactly the actions that were on.
        /// The one door for binding and shape changes (ControlBindings, ControlPresets, HumanInputSource.Ensure*).
        /// </summary>
        public static void ChangeWhileDisabled(InputActionAsset asset, Action change)
        {
            if (change == null) return;
            if (asset == null)
            {
                change();
                return;
            }
            List<InputAction> on = null;
            foreach (InputActionMap m in asset.actionMaps)
                foreach (InputAction a in m.actions)
                    if (a.enabled) (on ??= new List<InputAction>()).Add(a);
            if (on != null) asset.Disable();
            try
            {
                change();
            }
            finally
            {
                if (on != null)
                    foreach (InputAction a in on)
                        if (a.actionMap != null && a.actionMap.asset == asset && !a.enabled) a.Enable();
            }
        }
    }
}
