namespace CriticalMass.Runtime.PlayTests.PlayerSeat
{
    /// <summary>
    /// Player-seat gate, tier F: EVERY threshold of the feedback tests in one place (PLAN.md section 3, the gate brief).
    /// The lead tunes these; a fix builder changes the game, never a test, and a number here only with the lead's sign-off.
    /// </summary>
    public static class PlayerSeatFeedbackTargets
    {
        /// <summary>"Within 100 ms": the cue shows within this many sim ticks after the Hit ...</summary>
        public const int CueTicks = 3;
        /// <summary>... plus this many rendered frames in all (3 ticks + 3 more frames = 6 frames at 60 fps).</summary>
        public const int CueFrames = 6;
        /// <summary>F1 / F3: the press must land within the class wind-up plus this many ticks.</summary>
        public const int ContactSlackTicks = 3;
        /// <summary>F1-F4: the enemy stands at this share of the class attack range, centre to centre.</summary>
        public const double StandShareOfRange = 0.8;
        /// <summary>F3: the victim's model moves at least this share of the sim knock (Knock event Impulse).</summary>
        public const double KnockVisibleShare = 0.8;
        /// <summary>F20: the damage-direction indicator points within this many degrees of the attacker.</summary>
        public const double DirectionMaxDegrees = 20;
        /// <summary>F22: below this share of max health the low-health cue runs ...</summary>
        public const double LowHealthShare = 0.30;
        /// <summary>... pulsing at most this often (inside the 3 flashes per second photosensitivity limit).</summary>
        public const double LowHealthMaxHz = 2;
        /// <summary>F30: the swing pose weight reaches this within SwingPoseFrames of the sim accepting the press.</summary>
        public const double SwingPoseWeight = 0.8;
        public const int SwingPoseFrames = 2;
        /// <summary>F6 / F7 / F31: share of swings that land on a moving Normal bot with only a stick, only keys, or a lagging cursor.</summary>
        public const double AssistLandMin = 0.60;
        /// <summary>F6 / F7 / F31: swings measured (or the time cap below, whichever first).</summary>
        public const int AssistSwings = 12;
        public const double AssistMaxSeconds = 25;
        /// <summary>F6: the stick is held this many degrees off the enemy.</summary>
        public const double StickOffDegrees = 20;
        /// <summary>F7: the cursor sits where the enemy was this many ticks ago (0.2 s).</summary>
        public const int CursorLagTicks = 6;
        /// <summary>F33: camera kick on a hit by me, on a hit on me, and with reduce motion on (u, peak offset).</summary>
        public const double CameraKickMine = 0.03, CameraKickOnMe = 0.06, CameraKickReduced = 0.0;
        /// <summary>F9 / F32: a kill confirm shows within this many frames after the Death.</summary>
        public const int KillConfirmFrames = 6;
        /// <summary>F10: kills by others in the second before mine.</summary>
        public const int FeedFlood = 8;
        /// <summary>F14 / F15 / F12 / F24 / F34: a screen element appears within this many frames.</summary>
        public const int ScreenFrames = 45;
        /// <summary>F16: the allegiance one delivery of 3 adds.</summary>
        public const double DeliveryAllegiance = 37.5;
        /// <summary>F34: sim seconds of no input after which the autopilot may take over (MechRules: 15 s), plus slack.</summary>
        public const double IdleSeconds = 16;
        /// <summary>D2 (review defect 2) / D2b (fix/px-combat-camera): in third person at 0.8 and 1.0 x reach at least this share
        /// of the enemy's screen box is not covered by my own fighter's box. The boxes are the class silhouettes (FightFrame.
        /// Silhouette: body radius + 0.15, class height) projected through the live camera: the renderer bounds (skinned-mesh
        /// AABBs, ~1.9 u wide for a 2.15 u Farmer in the review logs, ground rings included) cannot reach 0.6 at 1.12 u centre to
        /// centre from ANY camera angle (two 1.9 u boxes 1.12 u apart overlap 0.78 u even side-on), so they are logged, not
        /// asserted. Was 0.5 on the renderer boxes.</summary>
        public const double EnemyVisibleShareMin = 0.6;
        /// <summary>D2b: with the combat camera on, the enemy's on-screen height at least this many times what the camera draws
        /// at the same distance with it off (the old fight framing drew him smaller still: it pulled back 12%).</summary>
        public const double CombatHeightGainMin = 1.4;
        /// <summary>D2 / D2b: paced 60 fps frames to wait for the framing to settle (in 0.3 s, plus the yaw chase) or release
        /// (1.0 s hold + 0.6 s out).</summary>
        public const int CombatSettleFrames = 45, CombatReleaseFrames = 120;
    }
}
