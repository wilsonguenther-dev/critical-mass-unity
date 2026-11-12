using System;
using System.Globalization;
using System.Text;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// Phase 0 debug overlay: the Sim values the browser HUD shows (clock, hill, kaiju, allegiance, instability,
    /// deliveries), pause and winner, the held scoreboard and the last six Sim events, in one IMGUI box at top-left.
    /// The text is rebuilt at 10 Hz (the browser HUD rate); OnGUI only draws the cached string.
    /// </summary>
    public sealed class DebugHud : MonoBehaviour
    {
        /// <summary>The driver whose World is shown (set by GameBootstrap: GetComponent&lt;SimDriverBehaviour&gt;()).</summary>
        public SimDriverBehaviour Driver;

        /// <summary>Text refresh rate in Hz (browser HUD rate).</summary>
        public const double RefreshHz = 10;
        /// <summary>IMGUI font size of the box.</summary>
        public const int FontSize = 14;
        /// <summary>How many of the newest Sim events are listed.</summary>
        public const int EventLines = 6;

        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        readonly StringBuilder _sb = new StringBuilder(2048);
        readonly GUIContent _content = new GUIContent(string.Empty);
        HumanInputSource _input;
        GUIStyle _style;
        float _nextRefresh;

        /// <summary>The text the box currently shows (rebuilt at RefreshHz).</summary>
        public string Text => _content.text;

        void Update()
        {
            float now = Time.unscaledTime;
            if (now < _nextRefresh) return;
            _nextRefresh = now + (float)(1.0 / RefreshHz);
            _content.text = Compose();
        }

        void OnGUI()
        {
            if (_content.text.Length == 0) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.box)
                {
                    fontSize = FontSize,
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                    richText = false,
                };
            }
            Vector2 size = _style.CalcSize(_content);
            GUI.Box(new Rect(8f, 8f, size.x, size.y), _content, _style);
        }

        string Compose()
        {
            if (Driver == null) Driver = GetComponent<SimDriverBehaviour>();
            if (Driver == null) return string.Empty;
            World w = Driver.World;
            if (w == null) return string.Empty;
            if (_input == null) _input = GetComponent<HumanInputSource>();

            MatchState m = w.Match;
            Kaiju k = w.Kaiju;
            MatchClock clock = Match.GetMatchClock(w);
            StringBuilder sb = _sb;
            sb.Clear();

            // 1. clock, hatch countdown, speed
            sb.Append(Mmss(clock.Remaining));
            if (!m.Hatched) sb.Append("  hatch in ").Append(Mmss(clock.UntilHatch));
            sb.Append("  x").Append(Driver.TimeScale.ToString(Inv)).Append('\n');

            // 2. hill
            sb.AppendFormat(Inv, "HILL {0}  azure {1:F1}  crimson {2:F1}\n",
                m.HillState, m.HillSeconds.Azure, m.HillSeconds.Crimson);

            // 3. kaiju, allegiance, instability
            sb.AppendFormat(Inv, "KAIJU {0}  hp {1:F0}/{2:F0}  allegiance {3:F0}/{4:F0}  instability {5:F1}\n",
                k.State, k.Hp, k.MaxHp, m.Allegiance.Azure, m.Allegiance.Crimson, m.Instability);

            // 4. deliveries
            sb.AppendFormat(Inv, "delivered biomass {0:F0}/{1:F0}\n",
                m.Delivered[(int)TeamId.Azure].Biomass, m.Delivered[(int)TeamId.Crimson].Biomass);

            // 5. pause, winner
            bool paused = _input != null && _input.State.Paused;
            bool ended = m.Phase == MatchPhase.Ended;
            if (paused) sb.Append("PAUSED");
            if (paused && ended) sb.Append("  ");
            if (ended) sb.Append("WINNER ").Append(m.Winner.ToString());
            if (paused || ended) sb.Append('\n');

            // 6. scoreboard while held
            if (_input != null && _input.State.ScoreboardHeld)
            {
                for (int i = 0; i < w.ActorCount; i++)
                {
                    Actor a = w.Actors[i];
                    sb.AppendFormat(Inv, "{0} {1} {2} {3}/{4} {5}\n",
                        a.Id, a.Team, a.Cls, a.Kills, a.Deaths, a.AiState);
                }
            }

            // last events, newest first (EventHead is the write counter; slot = index % EventCapacity)
            for (int n = 0; n < EventLines; n++)
            {
                int idx = w.EventHead - 1 - n;
                if (idx < 0) break;
                SimEvent e = w.Events[idx % World.EventCapacity];
                sb.AppendFormat(Inv, "{0} {1} {2}\n", e.Type, e.A, e.B);
            }

            if (sb.Length > 0 && sb[sb.Length - 1] == '\n') sb.Length -= 1;
            return sb.ToString();
        }

        /// <summary>'mm:ss', rounding up (browser src/ui/hud/format.ts mmss).</summary>
        public static string Mmss(double seconds)
        {
            double clamped = double.IsFinite(seconds) && seconds > 0 ? seconds : 0;
            int t = (int)Math.Ceiling(clamped - 1e-6);
            if (t < 0) t = 0;
            int mm = t / 60;
            int ss = t % 60;
            return mm.ToString("00", Inv) + ":" + ss.ToString("00", Inv);
        }
    }
}
