using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// mv-6 readability-4 item 4 (pass 4: "THIRD PERSON  AUTO FOLLOW  F5 TOP-DOWN" drew over the settings panel and the other
    /// bottom rows): the view-hint row is a learner's hint. Under the class moves it hides while any menu or panel is open
    /// (MenuGate), while a toast / caption owns the bottom line, and, from a player's third match on, after the first
    /// LearnSeconds of the match. The row itself is drawn by GroundView.OnGUI (Runtime/Camera, the camera builder's file):
    /// that draw reads Show. Off (switches off): always true, nothing changes.
    /// </summary>
    public static class ViewHintGate
    {
        public const float LearnSeconds = 60f;
        public const int LearnMatches = 2;   // matches already played before the hint starts timing out (the third match)

        /// <summary>Tests: force the matches-played count (null = the profile's).</summary>
        public static int? MatchesForTests;
        static CriticalMass.Runtime.Game.SimDriverBehaviour _driver;
        static int _look = int.MinValue;

        public static bool Show
        {
            get
            {
                if (!CriticalMass.Runtime.Hud.Player.WorldWords.MovesCap) return true;
                if (CriticalMass.Runtime.Menus.MenuGate.AnyOpen) return false;
                if (CriticalMass.Runtime.Audio.Announcer.IsSpeaking) return false;   // the toast line is the banner's
                int played = MatchesForTests ?? Played();
                if (_driver == null && Time.frameCount >= _look) { _driver = Object.FindFirstObjectByType<CriticalMass.Runtime.Game.SimDriverBehaviour>(); _look = Time.frameCount + 60; }
                double t = _driver != null && _driver.World != null ? _driver.World.Match.Time : 0.0;
                return Rule(played, t);
            }
        }

        /// <summary>Pure (tests): shown in the first two matches always, later only in a match's first LearnSeconds.</summary>
        public static bool Rule(int matchesPlayed, double matchSeconds) => matchesPlayed < LearnMatches || matchSeconds < LearnSeconds;

        static int Played()
        {
            var p = CriticalMass.Runtime.Progression.Profile.Current;
            return p != null ? p.MatchesPlayed : 0;
        }
    }
}
