using UnityEngine;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// controls-1 hygiene: the system cursor hides while the player plays on the keyboard alone or on a controller and comes
    /// back the moment the mouse moves or clicks (ControlBindings.ActiveScheme). Never locked or confined: alt-tab and the
    /// window edges behave normally. Installed once with the game (as SystemEvents); off in batch mode (tests, tools).
    /// </summary>
    public sealed class CursorAutoHide : MonoBehaviour
    {
        static GameObject _host;

        /// <summary>The cursor should show for this scheme (pure; tests).</summary>
        public static bool CursorWanted(ControlScheme scheme) => scheme == ControlScheme.KeyboardMouse;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            if (_host != null || Application.isBatchMode) return;
            _host = new GameObject("CriticalMass CursorAutoHide");
            DontDestroyOnLoad(_host);
            _host.AddComponent<CursorAutoHide>();
        }

        void Update()
        {
            ControlBindings.Sample();
            bool want = CursorWanted(ControlBindings.ActiveScheme) || !Application.isFocused;
            if (Cursor.visible != want) Cursor.visible = want;
        }

        void OnDisable() => Cursor.visible = true;
    }
}
