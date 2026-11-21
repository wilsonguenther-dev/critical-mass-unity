using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Mechanics;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Mechanics
{
    /// <summary>
    /// b16: the words of the idle-player panel and the context tips, in ONE place for the owner to rename (pure; tests).
    /// </summary>
    public static class B16Words
    {
        public const string AfkHead = "A BOT IS PLAYING FOR YOU";
        public const string AfkLine = "Press any key to take over.";
        public const string HideHint = " (H hides this tip)";
        public const string PrefPrefix = "cm.tips.b16.";

        /// <summary>The tips, in the order they are checked. Ids are the PlayerPrefs suffixes (one per profile, shown once).</summary>
        public static readonly string[] Ids = { "kaijuHill", "rally", "hutVote", "forward", "hillCap", "whyDown" };

        public static string Head(string id)
        {
            switch (id)
            {
                case "kaijuHill": return "YOUR KAIJU IS ON THE CIRCLE";
                case "rally": return "RALLY: YOU ARE BEHIND";
                case "hutVote": return "HUT VOTE";
                case "forward": return "BEHIND: YOU COME BACK CLOSER";
                case "hillCap": return "THE HILL IS FULL";
                default: return "WHY YOU WENT DOWN";
            }
        }

        /// <summary>The tip's line with this match's own numbers.</summary>
        public static string Line(string id, World w, ClassId killer)
        {
            switch (id)
            {
                case "kaijuHill": return "No score while your own Kaiju stands on the hill. Lead it off and the hill counts again.";
                case "rally":
                    return "Your hill time counts x" + Comeback.Multiplier(w).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)
                        + " while you hold it, up to " + (Comeback.CapMilli(w) / 1000 / (int)Math.Max(1, w.Config.SimHz)) + " s a match.";
                case "hutVote": return "Pick a card with LEFT or RIGHT. The bots wait for you and follow the people's choice.";
                case "forward": return "While your team trails, you respawn part of the way to the hill.";
                case "hillCap":
                {
                    int cap = MechRules.HillCap(w);
                    return "Only " + cap + " count on the hill (a Warrior counts 2). More add nothing: fight on the way in.";
                }
                default: return "The last hit was a " + ClassWord(killer) + ". " + Counsel(killer);
            }
        }

        static string ClassWord(ClassId c)
        {
            switch (c)
            {
                case ClassId.Warrior: return "Warrior";
                case ClassId.Farmer: return "Farmer";
                case ClassId.Miner: return "Miner";
                case ClassId.Demolitions: return "Demolitionist";
                case ClassId.Builder: return "Builder";
                default: return "Healer";
            }
        }

        static string Counsel(ClassId c)
        {
            switch (c)
            {
                case ClassId.Warrior: return "Warriors win close duels: fight one with a teammate.";
                case ClassId.Demolitions: return "Watch for the TNT fuse ring and step out of it.";
                default: return "Stay near your team: two beat one.";
            }
        }
    }

    /// <summary>
    /// b16 view only: (1) while the autopilot plays the local player after AfkSeconds without input, a centred panel says so;
    /// (2) the context tips: once per profile, at the moment a rule that decides matches first touches the player (own Kaiju
    /// on the circle, RALLY, the hut vote, the forward respawn, the hill cap, the first knock-out), a banner says what it does.
    /// They respect the hints setting (Hints OFF: none) and are dismissed with H or the controller's View button (or time out).
    /// Nothing flashes or pulses, so the reduced-flashing setting needs nothing more. Reads the World; writes nothing to it.
    /// </summary>
    public sealed class B16MechHud : MonoBehaviour
    {
        public const int SortingOrder = 16;
        public const double TipHoldSeconds = 7;

        SimDriverBehaviour _driver;
        Canvas _canvas;
        RectTransform _root, _space, _afk;
        int _epoch = -1;
        bool _wasAlive = true, _tipOpen;
        string _shownTip;

        /// <summary>Shown flag (tests and captures).</summary>
        public bool AfkShown => _afk != null && _afk.gameObject.activeSelf;

        void Awake()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _canvas = InteractUi.MakeHudCanvas(transform, "B16 Mech HUD Canvas", SortingOrder, out _root, out _space);
            const float W = 620f, H = 96f;
            _afk = MenuUi.Rt(_space, "Autopilot Panel");
            MenuUi.Place(_afk, (HudLayout.RefW - W) * 0.5f, 300f, W, H);
            VectorShape p = MenuUi.Shape(_afk, "Panel", ShapeKind.RoundRect, 0f, 0f, W, H, CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.94f), 12f);
            p.OutlinePx = 3f;
            p.RimPx = 2f;
            p.RimColor = CMTokens.ToolBrass.Color;
            MenuUi.Fit(MenuUi.Label(_afk, "Head", CMTokens.Display, 22, CMTokens.ToolBrass.Color, TextAnchor.MiddleCenter, 12f, 8f, W - 24f, 42f, B16Words.AfkHead, 2f), 18);
            MenuUi.Fit(MenuUi.Label(_afk, "Line", CMTokens.Ui, 18, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 12f, 52f, W - 24f, 34f, B16Words.AfkLine, 1.5f), 15);
            _afk.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            DepthWorldView view = DepthWorldView.Active;
            if (_canvas == null) return;
            InteractUi.UpdateScale(_canvas);
            if (_canvas.worldCamera == null) _canvas.worldCamera = UnityEngine.Camera.main;
            bool hide = w == null || view == null || w.Match.Phase == MatchPhase.Ended || CriticalMass.Runtime.Menus.MenuGate.AnyOpen;
            if (_canvas.enabled == hide) _canvas.enabled = !hide;
            if (hide) return;
            int me = view.Feed.LocalActor;
            if (me < 0 || me >= w.ActorCount) return;
            if (w.Epoch != _epoch)
            {
                _epoch = w.Epoch;
                _wasAlive = true;
                _tipOpen = false;
                _shownTip = null;
            }
            bool afk = MechRules.Autopiloted(w, me);
            if (_afk.gameObject.activeSelf != afk) _afk.gameObject.SetActive(afk);
            TickTips(w, me);
        }

        static bool TipsOn => CriticalMass.Runtime.Menu.WorldFeedbackSettings.Hints != CriticalMass.Runtime.Menu.WorldFeedbackSettings.HintsOff;

        public static bool Seen(string id) => PlayerPrefs.GetInt(B16Words.PrefPrefix + id, 0) != 0;

        static void MarkSeen(string id)
        {
            PlayerPrefs.SetInt(B16Words.PrefPrefix + id, 1);
            if (!GamePaths.IsolatedRun) PlayerPrefs.Save();
        }

        static bool DismissPressed()
        {
            Keyboard k = Keyboard.current;
            if (k != null && k.hKey.wasPressedThisFrame) return true;
            Gamepad g = Gamepad.current;
            return g != null && g.selectButton.wasPressedThisFrame;
        }

        void TickTips(World w, int me)
        {
            if (_tipOpen)
            {
                bool stillOurs = Banner.IsOpen && Banner.Current.Headline == B16Words.Head(_shownTip);
                if (!stillOurs) _tipOpen = false;
                else if (DismissPressed())
                {
                    Banner.Hide();
                    _tipOpen = false;
                }
            }
            Actor a = w.Actors[me];
            bool died = _wasAlive && !a.Alive;
            _wasAlive = a.Alive;
            // batch-mode runs (CI tests, headless captures) never show a tip: they would change the banner every test reads
            if (!TipsOn || _tipOpen || Banner.IsOpen || Application.isBatchMode) return;
            TeamId team = a.Team;
            ClassId killer = ClassId.Warrior;
            string id = null;
            if (!Seen("kaijuHill") && Comeback.KaijuBlocking(w) == (int)team) id = "kaijuHill";
            else if (!Seen("rally") && Comeback.Earning(w, team)) id = "rally";
            else if (!Seen("hutVote") && HutChoices.VoteLevel(w, team) > 0) id = "hutVote";
            else if (!Seen("forward") && !a.Alive && Comeback.ForwardActive(w, team)) id = "forward";
            else if (!Seen("hillCap") && MechRules.AtCap(w, team)) id = "hillCap";
            else if (!Seen("whyDown") && died && a.LastAttacker >= 0 && a.LastAttacker < w.ActorCount)
            {
                id = "whyDown";
                killer = w.Actors[a.LastAttacker].Cls;
            }
            if (id == null) return;
            MarkSeen(id);
            _shownTip = id;
            _tipOpen = true;
            Banner.Show(new BannerSpec
            {
                Headline = B16Words.Head(id),
                Reason = B16Words.Line(id, w, killer) + B16Words.HideHint,
                Style = BannerStyle.Neutral,
                Priority = 2,
                HoldSeconds = TipHoldSeconds,
            });
        }
    }
}
