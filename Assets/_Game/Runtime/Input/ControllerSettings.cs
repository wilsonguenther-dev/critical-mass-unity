using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Input
{
    /// <summary>The button-label family of a controller: A B X Y, the shapes (cross, circle, square, triangle), or unknown.</summary>
    public enum PadFamily { Generic, Xbox, PlayStation }

    /// <summary>
    /// controls-1: the player's controller settings, kept between launches (PlayerPrefs, like GameSettings): stick dead
    /// zone, stick sensitivity (a response curve on the stick's length), invert aim up/down, and vibration. Applied view
    /// side only (HumanInputSource shapes the stick values before they become a move intent or an aim direction;
    /// CameraResponse asks Vibration before it rumbles), so the sim never sees a setting. Also names the connected
    /// controller's family for the glyphs (no brand names on screen: the family only picks button labels and colours).
    /// </summary>
    public static class ControllerSettings
    {
        public const string DeadZoneKey = "cm.pad.deadzone";
        public const string SensitivityKey = "cm.pad.sensitivity";
        public const string InvertKey = "cm.pad.invertAimY";
        public const string VibrationKey = "cm.pad.vibration";
        public static readonly string[] AllKeys = { DeadZoneKey, SensitivityKey, InvertKey, VibrationKey };

        /// <summary>Inner dead zones the player can pick (raw stick length); the bindings' own floor is 0.2.</summary>
        public static readonly float[] DeadZones = { 0.2f, 0.3f, 0.4f };
        public static readonly string[] DeadZoneNames = { "SMALL", "MEDIUM", "LARGE" };
        /// <summary>Response exponents: low sensitivity needs a longer push for the same speed.</summary>
        public static readonly float[] Curves = { 1.6f, 1f, 0.7f };
        public static readonly string[] SensitivityNames = { "LOW", "NORMAL", "HIGH" };

        /// <summary>The bindings' StickDeadzone floor and ceiling (Controls.inputactions, left stick).</summary>
        public const float BindingFloor = 0.2f, BindingCeiling = 0.95f;

        public static event Action Changed;

        public static int DeadZoneIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(DeadZoneKey, 0), 0, DeadZones.Length - 1);
            set { PlayerPrefs.SetInt(DeadZoneKey, Mathf.Clamp(value, 0, DeadZones.Length - 1)); Save(); }
        }

        public static int SensitivityIndex
        {
            get => Mathf.Clamp(PlayerPrefs.GetInt(SensitivityKey, 1), 0, Curves.Length - 1);
            set { PlayerPrefs.SetInt(SensitivityKey, Mathf.Clamp(value, 0, Curves.Length - 1)); Save(); }
        }

        public static bool InvertAimY
        {
            get => PlayerPrefs.GetInt(InvertKey, 0) == 1;
            set { PlayerPrefs.SetInt(InvertKey, value ? 1 : 0); Save(); }
        }

        public static bool Vibration
        {
            get => PlayerPrefs.GetInt(VibrationKey, 1) == 1;
            set
            {
                PlayerPrefs.SetInt(VibrationKey, value ? 1 : 0);
                if (!value && Gamepad.current != null) Gamepad.current.ResetHaptics();
                Save();
            }
        }

        static void Save()
        {
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        /// <summary>Every controller setting back to its default.</summary>
        public static void ResetAll()
        {
            for (int i = 0; i < AllKeys.Length; i++) PlayerPrefs.DeleteKey(AllKeys[i]);
            Save();
        }

        /// <summary>
        /// A stick value that already passed the binding's StickDeadzone(0.2, 0.95) (length 0..1) through the player's dead
        /// zone and curve: the extra dead zone is re-expressed on the processed scale, then the length is rescaled to 0..1
        /// and raised to the curve exponent. Direction is kept. Pure.
        /// </summary>
        public static Vector2 Shape(Vector2 v, float rawDeadZone, float exponent)
        {
            float len = v.magnitude;
            if (len <= 1e-6f) return Vector2.zero;
            float extra = Mathf.Clamp01((rawDeadZone - BindingFloor) / (BindingCeiling - BindingFloor));
            if (len <= extra) return Vector2.zero;
            float t = Mathf.Clamp01((len - extra) / Mathf.Max(1e-4f, 1f - extra));
            t = Mathf.Pow(t, Mathf.Max(0.1f, exponent));
            return v / len * t;
        }

        /// <summary>The left stick as the player set it.</summary>
        public static Vector2 ShapeMove(Vector2 v) => Shape(v, DeadZones[DeadZoneIndex], Curves[SensitivityIndex]);

        /// <summary>The right stick as the player set it (dead zone and invert; the aim uses its direction only).</summary>
        public static Vector2 ShapeAim(Vector2 v)
        {
            Vector2 s = Shape(v, DeadZones[DeadZoneIndex], 1f);
            if (InvertAimY) s.y = -s.y;
            return s;
        }

        // ---- controller family ----

        /// <summary>The family of a layout or product name (pure; tests pin each known name).</summary>
        public static PadFamily FamilyOf(string layout, string product = null, string manufacturer = null)
        {
            string all = ((layout ?? "") + " " + (product ?? "") + " " + (manufacturer ?? "")).ToLowerInvariant();
            if (all.Contains("dualshock") || all.Contains("dualsense") || all.Contains("sony") || all.Contains("playstation") ||
                all.Contains("wireless controller")) return PadFamily.PlayStation;
            if (all.Contains("xinput") || all.Contains("xbox") || all.Contains("microsoft")) return PadFamily.Xbox;
            return PadFamily.Generic;
        }

        /// <summary>The family of a connected controller (its layout chain first, then its product strings).</summary>
        public static PadFamily FamilyOf(Gamepad pad)
        {
            if (pad == null) return PadFamily.Generic;
            string layout = pad.layout;
            try
            {
                if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "DualShockGamepad")) return PadFamily.PlayStation;
                if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "XInputController")) return PadFamily.Xbox;
            }
            catch (ArgumentException)
            {
                // a layout this platform never registered: fall back to the names
            }
            return FamilyOf(layout, pad.description.product, pad.description.manufacturer);
        }

        static PadFamily? _forced;

        /// <summary>The family the glyphs draw now: the current controller's, Xbox labels when none is connected.</summary>
        public static PadFamily CurrentFamily
        {
            get
            {
                if (_forced.HasValue) return _forced.Value;
                Gamepad pad = Gamepad.current;
                if (pad == null) return PadFamily.Xbox;
                PadFamily f = FamilyOf(pad);
                return f == PadFamily.Generic ? PadFamily.Xbox : f;
            }
        }

        /// <summary>Tests and captures: draw one family whatever is connected (null = follow the controller).</summary>
        public static void ForceFamily(PadFamily? f) => _forced = f;

        /// <summary>Plain words for the connected controller, no brand names ("Controller: cross, circle, square, triangle buttons").</summary>
        public static string Describe(Gamepad pad)
        {
            if (pad == null) return "No controller connected";
            switch (FamilyOf(pad))
            {
                case PadFamily.PlayStation: return "Controller connected: cross, circle, square and triangle buttons";
                case PadFamily.Xbox: return "Controller connected: A, B, X and Y buttons";
                default: return "Controller connected: standard layout (shown with A, B, X and Y)";
            }
        }
    }
}
