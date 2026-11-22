using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan item 12 (agency audit A6, P3 "every kill by me: a centre confirm within 100 ms"): KNOCKED OUT  NAME under the
    /// reticle for Seconds, raised the same frame the HUD reads the Death credited to me (never queued behind the kill
    /// feed). Kills inside StreakSeconds of each other count up: DOUBLE, TRIPLE, QUADRUPLE, RAMPAGE (a line over it).
    /// ASSIST  NAME (smaller, under it) when I damaged a victim someone else knocked out. Sizes, px at 1080p: the confirm is
    /// Display 30 px brass on a 760 x 44 line centred 68 px under the screen centre (y 608), the streak word Display 22 px
    /// at y 574, the assist Ui 22 px cream at y 646. Pop 1.2 to 1 over 0.12 s (none under REDUCE MOTION).
    /// </summary>
    public sealed class KillConfirm
    {
        public const float Seconds = 1.2f, AssistSeconds = 1.0f, StreakSeconds = 4f, PopSeconds = 0.12f, Fade = 0.25f;
        public const float ConfirmY = -68f, StreakY = -34f, AssistY = -106f;
        public const string KnockedOut = "KNOCKED OUT  ", Assist = "ASSIST  ";
        static readonly string[] StreakWords = { "", "", "DOUBLE", "TRIPLE", "QUADRUPLE", "RAMPAGE" };

        /// <summary>The streak word for n kills in a row (pure; tests).</summary>
        public static string StreakWord(int n) => n < 2 ? string.Empty : StreakWords[Mathf.Min(n, StreakWords.Length - 1)];

        readonly Text _line, _streak, _assist;
        readonly CanvasGroup _lineGroup, _assistGroup;
        float _age = 99f, _assistAge = 99f;
        double _lastKillAt = -100;
        int _streakN;

        public Text Line => _line;
        public Text AssistLine => _assist;
        public int Streak => _streakN;
        /// <summary>Kills confirmed and assists shown this match (tests: the tally must equal these).</summary>
        public int Confirmed { get; private set; }
        public int AssistsShown { get; private set; }

        public KillConfirm(RectTransform root)
        {
            RectTransform r = PxUi.Middle(root, "Kill Confirm", 760f, 44f);
            r.anchoredPosition = new Vector2(0f, ConfirmY);
            _lineGroup = PxUi.Group(r);
            _line = PxUi.Label(r, "Line", CMTokens.Display, UiScale.TypeHeading, PxPalette.Brass, TextAnchor.MiddleCenter, 0f, 0f, 760f, 44f, 3f);
            _streak = PxUi.Label(r, "Streak", CMTokens.Display, UiScale.TypeSmall + 4, PxPalette.Cream, TextAnchor.MiddleCenter, 0f, 22f - (StreakY - ConfirmY) - 14f, 760f, 28f, 3f);
            r.gameObject.SetActive(false);
            RectTransform a = PxUi.Middle(root, "Assist Confirm", 600f, 32f);
            a.anchoredPosition = new Vector2(0f, AssistY);
            _assistGroup = PxUi.Group(a);
            _assist = PxUi.Label(a, "Line", CMTokens.Ui, UiScale.TypeBody, PxPalette.Cream, TextAnchor.MiddleCenter, 0f, 0f, 600f, 32f, 2.5f);
            a.gameObject.SetActive(false);
        }

        static string NameOf(World w, int id)
        {
            ClassId cls = id >= 0 && id < w.ActorCount ? w.Actors[id].Cls : ClassId.Warrior;
            string word = KillWords.ClassUpper[Mathf.Clamp((int)cls, 0, KillWords.ClassUpper.Length - 1)];
            string name = CriticalMass.Runtime.View.Mechanics.DepthNames.OrFallback(id, word);
            return name == word ? word : name + " (" + word + ")";
        }

        /// <summary>A Death credited to me.</summary>
        public void OnMyKill(World w, int victim)
        {
            double now = w.Match.Time;
            _streakN = now - _lastKillAt <= StreakSeconds ? _streakN + 1 : 1;
            _lastKillAt = now;
            HudText.Set(_line, KnockedOut + NameOf(w, victim));
            HudText.Set(_streak, StreakWord(_streakN));
            PxUi.Show(_streak, _streakN >= 2);
            _age = 0f;
            PxUi.Show(_line.transform.parent, true);
            PxUi.Alpha(_lineGroup, 1f);
            Confirmed++;
            PlayerFeedback.Report(FeedbackChannel.KillConfirm, victim, _streakN, _line);
        }

        /// <summary>An assist credited to me on victim.</summary>
        public void OnMyAssist(World w, int victim)
        {
            HudText.Set(_assist, Assist + NameOf(w, victim));
            _assistAge = 0f;
            PxUi.Show(_assist.transform.parent, true);
            PxUi.Alpha(_assistGroup, 1f);
            AssistsShown++;
            PlayerFeedback.Report(FeedbackChannel.AssistCredit, victim, AssistsShown, _assist);
        }

        public void Clear()
        {
            _age = _assistAge = 99f;
            _streakN = 0;
            _lastKillAt = -100;
            Confirmed = AssistsShown = 0;
            PxUi.Show(_line.transform.parent, false);
            PxUi.Show(_assist.transform.parent, false);
        }

        public void Tick(float dt)
        {
            bool calm = CriticalMass.Runtime.Menu.GameSettings.ReduceMotion;
            Transform lr = _line.transform.parent;
            if (lr.gameObject.activeSelf)
            {
                _age += dt;
                if (_age >= Seconds) lr.gameObject.SetActive(false);
                else
                {
                    float k = calm ? 1f : Mathf.Lerp(1.2f, 1f, Mathf.Clamp01(_age / PopSeconds));
                    lr.localScale = new Vector3(k, k, 1f);
                    PxUi.Alpha(_lineGroup, Mathf.Clamp01((Seconds - _age) / Fade));
                }
            }
            Transform ar = _assist.transform.parent;
            if (ar.gameObject.activeSelf)
            {
                _assistAge += dt;
                if (_assistAge >= AssistSeconds) ar.gameObject.SetActive(false);
                else PxUi.Alpha(_assistGroup, Mathf.Clamp01((AssistSeconds - _assistAge) / Fade));
            }
        }
    }
}
