using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// QUICK DEMO (title and home): the 80 second guided skirmish for first-time visitors and for showing the game to
    /// others. The match itself is an ordinary 5 v 5 request in MatchMode.Demo (PlayModes.Rules: an 80 s clock, the egg
    /// hatching at 0:36, bots leaving the player alone for the first 14 s, easy enemy bots) with the human in control from
    /// the first frame. This overlay only talks: the mascot says one line per beat (move, hit, the hill, the egg, the
    /// Kaiju), timed on the sim's own match clock so it never drifts from what is on screen, and when the sim ends the match
    /// the player lands on HOME (the rewards land on the player card there) instead of the results. View only: it reads the
    /// world and never writes to it.
    /// </summary>
    [GameOverlay(Order = 72, Scope = OverlayScope.MatchOnly)]
    public sealed class QuickDemo : MonoBehaviour
    {
        /// <summary>One beat: from this match time (sim seconds) the mascot says this line.</summary>
        public struct Beat
        {
            public double At;
            public string Id;
            public MascotReaction Reaction;
        }

        public static readonly Beat[] Beats =
        {
            new Beat { At = 0.5, Id = "demo.move", Reaction = MascotReaction.Point },
            new Beat { At = 13, Id = "demo.hit", Reaction = MascotReaction.Surprised },
            new Beat { At = 20, Id = "demo.hill", Reaction = MascotReaction.Point },
            new Beat { At = 29, Id = "demo.egg", Reaction = MascotReaction.Neutral },
            new Beat { At = 39, Id = "demo.kaiju", Reaction = MascotReaction.Wince },
            new Beat { At = 66, Id = "demo.end", Reaction = MascotReaction.Cheer },
        };

        public const string HintId = "demo.hint";

        public static QuickDemo Current { get; private set; }
        /// <summary>The beat said last (-1 before the first).</summary>
        public int BeatIndex { get; private set; } = -1;
        public bool Leaving { get; private set; }

        SimDriverBehaviour _driver;
        MascotGuide _mascot;
        static bool _registered;

        /// <summary>Built-in lines; the words pack (MascotLines.Lookup) wins when it has an id.</summary>
        public static void RegisterLines()
        {
            if (_registered) return;
            _registered = true;
            MascotLines.Register("demo.move", "Quick demo, one minute. Move with {Move}. The bots give you a few seconds to find your feet.");
            MascotLines.Register("demo.hit", "Here they come. Hit back with {Attack}.");
            MascotLines.Register("demo.hill", "See the hill in the middle? Your team scores while more of you stand on it than them.");
            MascotLines.Register("demo.egg", "That egg in the shrine hatches in a few seconds. Whoever fed it most, it fights for.");
            MascotLines.Register("demo.kaiju", "There it is. A Kaiju. Stay out from under its slam and point it at them.");
            MascotLines.Register("demo.end", "That's Critical Mass. The full battle is sixteen a side. Ready for a real one?");
            MascotLines.Register(HintId, "QUICK DEMO   ·   PAUSE TO LEAVE");
        }

        /// <summary>The beat for a match time (-1 before the first): the last one whose time has come.</summary>
        public static int BeatAt(double matchTime)
        {
            int b = -1;
            for (int i = 0; i < Beats.Length; i++)
                if (matchTime >= Beats[i].At) b = i;
            return b;
        }

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            MatchRequest r = _driver != null ? _driver.Request : null;
            if (r == null || r.Backdrop || r.Mode != MatchMode.Demo)
            {
                Destroy(this);
                return;
            }
            Current = this;
            RegisterLines();
            _mascot = MascotGuide.Ensure(gameObject);
        }

        void OnDestroy()
        {
            if (Current == this) Current = null;
        }

        void Update()
        {
            if (_driver == null || _driver.World == null || Leaving) return;
            World w = _driver.World;
            if (w.Match.Phase == MatchPhase.Ended)
            {
                // The demo lands on HOME (before the results screen would come up), where the rewards land on the card.
                Leaving = true;
                if (_mascot != null) _mascot.Hide();
                GameFlow.ReturnTo(FlowState.Menu);
                return;
            }
            int b = BeatAt(w.Match.Time);
            if (b == BeatIndex || b < 0 || _mascot == null) return;
            BeatIndex = b;
            ControlDevice device = ControlBindings.ActiveDevice;
            _mascot.SayText(TrialText.Fill(MascotLines.Resolve(Beats[b].Id), device));
            _mascot.SetHint(MascotLines.Resolve(HintId));
            _mascot.React(Beats[b].Reaction);
            _mascot.Show();
        }
    }
}
