using System;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Menu;
using UnityEngine;

namespace CriticalMass.Runtime.Menus
{
    /// <summary>
    /// Integration batch 2: fills the main menu's BARRACKS line with DG-27's Barracks (the character panel, editable, on
    /// the Outfit tab, for the class chosen last). Closing the panel (its Back button, Escape or B) brings the main menu
    /// back. The main menu adds this component to the Game root when the line is chosen.
    /// </summary>
    [MenuEntry(OverlayRegistry.Barracks, "BARRACKS", 30)]
    public sealed class BarracksMenuScreen : MonoBehaviour, IMenuScreen, IMenuBack
    {
        Action _onClosed;

        public void Open(Action onClosed)
        {
            _onClosed = onClosed;
            CharacterPanel.Closed -= Done;
            CharacterPanel.Closed += Done;
            Barracks.Show(ClassSelect.SelectedClass);
            if (!CharacterPanel.IsOpen) Done();
        }

        /// <summary>The pause key while the Barracks are open: close them (the panel stops a name edit first).</summary>
        public void Back()
        {
            if (CharacterPanel.IsTyping) return;   // Escape ends the name edit inside the panel
            CharacterPanel.Hide();
        }

        void Done()
        {
            CharacterPanel.Closed -= Done;
            Action a = _onClosed;
            _onClosed = null;
            a?.Invoke();
        }

        void OnDestroy() => CharacterPanel.Closed -= Done;
    }
}
