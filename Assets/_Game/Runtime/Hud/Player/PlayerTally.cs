using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan item 12 (agency audit 4.1 b, P5): the live personal tally, always on, on the player card under the health
    /// plate: knock-outs, assists, damage dealt, healing done and hill seconds ("KO 3  AST 1  DMG 412  HEAL 0  HILL 34s").
    /// KO and hill seconds are the sim's own counters (Actor.Kills, Actor.HillSeconds: the scoreboard's numbers), damage and
    /// healing MatchStats (the results screen's numbers), assists the HUD's AssistTracker. The line is rebuilt only when a
    /// number changes. Size, px at 1080p: Mono 16 (shrinks to 12 to fit), box 500 x 20 at card x 124, y -28 (just above
    /// the card and the LOW HEALTH row; a 38-46 character line does not fit any free row inside the card).
    /// </summary>
    public sealed class PlayerTally
    {
        public const float X = 124f, Y = -28f, W = 500f, H = 20f;   // above the card (the card has no 38-character row free)

        /// <summary>The tally line (pure; tests).</summary>
        public static string Compose(int ko, int assists, int damage, int healing, int hillSeconds) => Compose(ko, assists, 0, damage, healing, hillSeconds);

        /// <summary>The tally line with criticals (CRIT n shows once there is one; pure; tests).</summary>
        public static string Compose(int ko, int assists, int crits, int damage, int healing, int hillSeconds) =>
            "KO " + ko + "  AST " + assists + (crits > 0 ? "  CRIT " + crits : "") + "  DMG " + damage + "  HEAL " + healing + "  HILL " + hillSeconds + "s";

        readonly Text _line;
        int _ko = -1, _ast = -1, _dmg = -1, _heal = -1, _hill = -1, _crit = -1;
        public int Crits => _crit;

        public Text Line => _line;
        public int Kills => _ko;
        public int Assists => _ast;
        public int Damage => _dmg;
        public int Healing => _heal;
        public int HillSeconds => _hill;

        public PlayerTally(RectTransform playerCard)
        {
            _line = PxUi.Label(playerCard, "Personal Tally", CMTokens.Mono, UiScale.TypeMin, PxPalette.Cream, TextAnchor.MiddleLeft, X, Y, W, H, 2f);
            CriticalMass.Runtime.Menu.MenuUi.Fit(_line, 12);
        }

        public void Clear() => _ko = _ast = _dmg = _heal = _hill = _crit = -1;

        public void Tick(World w, int me, AssistTracker assists) => Tick(w, me, assists, 0);

        public void Tick(World w, int me, AssistTracker assists, int crits)
        {
            if (me < 0 || me >= w.ActorCount) { PxUi.Show(_line, false); return; }
            PxUi.Show(_line, true);
            Actor a = w.Actors[me];
            MatchStats ms = MatchStatsRecorder.Current;
            ActorStats st = ms != null && me < ms.ActorCount && ms.Actors != null && me < ms.Actors.Length ? ms.Actors[me] : null;
            int ko = a.Kills, ast = assists.Assists(me);
            int dmg = st != null ? Mathf.RoundToInt((float)st.Damage) : 0, heal = st != null ? Mathf.RoundToInt((float)st.HealingDone) : 0;
            int hill = (int)System.Math.Floor(a.HillSeconds);
            if (ko == _ko && ast == _ast && dmg == _dmg && heal == _heal && hill == _hill && crits == _crit) return;
            bool koChanged = ko != _ko || ast != _ast || crits != _crit;
            _crit = crits;
            _ko = ko; _ast = ast; _dmg = dmg; _heal = heal; _hill = hill;
            HudText.Set(_line, Compose(ko, ast, crits, dmg, heal, hill));
            if (koChanged) PlayerFeedback.Report(FeedbackChannel.PersonalTally, me, ko, _line);
        }
    }
}
