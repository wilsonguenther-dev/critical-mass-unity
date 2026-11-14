using System;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// The gameplay camera's heading (RenderConfig.CameraYawDeg for the match). The creator's angle (135, 2026-11-13: "rotate
    /// it so red base is bottom left", the eye travels up and to the right toward the Azure base) is the default; the
    /// classic diamond (45: Azure top-left, Crimson bottom-right) stays a setting so the two can be compared. Which one is
    /// the default is CreatorAngleIsDefault (today: the classic diamond, until the owner signs off the creator's angle).
    /// SimDriverBehaviour writes the chosen heading into the match config (an automation -cameraYaw still wins) and sets
    /// Active, which the screen-relative controls, the minimap and ground view read, so the screen, the stick and the
    /// minimap always agree. View only: the Sim never reads a heading.
    /// </summary>
    public static class CameraHeading
    {
        public const double Creator = 135, Classic = 45;
        public const string Key = "cm.camera.heading";   // 0 = creator's angle, 1 = classic diamond

        /// <summary>
        /// THE ONE LINE THAT FLIPS THE DEFAULT: false keeps today's classic diamond until the owner has seen the creator's
        /// angle (batch 8, 2026-11-13); true makes the creator's angle the default. Either way both are in Settings, Game, CAMERA.
        /// </summary>
        public const bool CreatorAngleIsDefault = true;

        /// <summary>Tests pin a heading here (SetUpFixture: legacy tests describe the classic 45).</summary>
        public static double? Pin;

        public static bool ClassicChosen
        {
            get => PlayerPrefs.GetInt(Key, CreatorAngleIsDefault ? 0 : 1) == 1;
            set { PlayerPrefs.SetInt(Key, value ? 1 : 0); PlayerPrefs.Save(); }
        }

        /// <summary>The heading the next match uses.</summary>
        public static double Chosen => Pin ?? (ClassicChosen ? Classic : Creator);

        static double? _active;

        /// <summary>The heading of the camera on screen now (the running match's config yaw), else the chosen one.</summary>
        public static double Active
        {
            get => Pin ?? _active ?? Chosen;
            set { _active = Normalize(value); Version++; }
        }

        /// <summary>Bumped whenever Active is set: caches keyed on the heading compare it.</summary>
        public static int Version { get; private set; }

        public static double Normalize(double deg) => ((deg % 360) + 360) % 360;

        /// <summary>Unity camera yaw for a heading (GameplayCamera.Rotation uses 360 - heading).</summary>
        public static double UnityYaw(double heading) => Normalize(360 - heading);
    }
}
