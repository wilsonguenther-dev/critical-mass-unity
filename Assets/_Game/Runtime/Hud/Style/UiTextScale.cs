using CriticalMass.Runtime.Menu;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Style
{
    /// <summary>
    /// Polish pass 1: the UI scale (text size) setting for screens that are laid out in the fixed 1920x1080 space (menus,
    /// results, pause pages). Their boxes cannot grow without leaving the frame, so the setting grows the TEXT inside its
    /// box: every Text gets size x scale with best-fit bounded by its own rect, so at 130 % a line is as big as its box
    /// allows and never spills (the HUD clusters scale as a whole instead, AccessibilityApplier). Captured once when a
    /// screen is built (one array), applied only when the setting changes, so nothing runs per frame.
    /// </summary>
    public sealed class UiTextScale
    {
        Text[] _texts;
        int[] _base, _min;
        float _applied = -1f;

        /// <summary>The scale last applied (tests).</summary>
        public float Applied => _applied;
        public int Count => _texts != null ? _texts.Length : 0;

        /// <summary>Records every Text under <paramref name="root"/> with its built size.</summary>
        public static UiTextScale Capture(Transform root)
        {
            var s = new UiTextScale();
            s._texts = root.GetComponentsInChildren<Text>(true);
            s._base = new int[s._texts.Length];
            s._min = new int[s._texts.Length];
            for (int i = 0; i < s._texts.Length; i++)
            {
                Text t = s._texts[i];
                s._base[i] = t.fontSize;
                s._min[i] = t.resizeTextForBestFit ? Mathf.Min(t.resizeTextMinSize, t.fontSize) : Mathf.Min(UiScale.TypeMin, t.fontSize);
            }
            return s;
        }

        /// <summary>The player's setting (0.8 .. 1.3).</summary>
        public static float Setting => SettingsExtras.UiScale;

        /// <summary>Applies <paramref name="scale"/> (no work when it is the one already applied).</summary>
        public void Apply(float scale)
        {
            if (_texts == null || Mathf.Abs(scale - _applied) < 1e-4f) return;
            _applied = scale;
            for (int i = 0; i < _texts.Length; i++)
            {
                Text t = _texts[i];
                if (t == null) continue;
                int size = Mathf.Max(UiScale.TypeMin, Mathf.RoundToInt(_base[i] * scale));
                if (scale <= 1f && !t.resizeTextForBestFit)
                {
                    t.fontSize = Mathf.Min(_base[i], size);
                    continue;
                }
                // Bigger than built: best-fit inside the rect, never below the size it was built to shrink to.
                t.fontSize = size;
                t.horizontalOverflow = HorizontalWrapMode.Wrap;
                t.verticalOverflow = VerticalWrapMode.Truncate;
                t.resizeTextMaxSize = size;
                t.resizeTextMinSize = Mathf.Min(_min[i], size);
                t.resizeTextForBestFit = true;
            }
        }
    }
}
