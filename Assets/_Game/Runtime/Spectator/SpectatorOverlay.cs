using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Spectator
{
    /// <summary>
    /// What the spectator sees (SpectatorMode adds it): a SPECTATING badge with both teams' scores (top left), a slim event
    /// ticker under it (hill changes, the hatch, the Kaiju going down and getting up, berserk, the followed player's
    /// knock-outs), the followed player's card where the player card was (portrait, team and class, health bar, knock-outs,
    /// times down, hill time; or the objective / free camera), and two prompt lines of the spectator controls drawn as
    /// glyphs for the device in use. While playing, the offer "[V] SPECTATE WHILE YOU WAIT" shows during a knock-out
    /// (V, Y on a controller, or a click). Built in code on its own Screen Space - Camera canvas (HUD tokens, Void Navy
    /// outlines); texts change only when their value changes, the ticker's lines are a fixed pool (no growth).
    /// </summary>
    public sealed class SpectatorOverlay : MonoBehaviour
    {
        public const int SortingOrder = 14;
        public const int TickerLines = 4;
        public const float TickerSeconds = 6f;
        public const float FadeSeconds = 0.18f;

        public SpectatorMode Mode;

        Canvas _canvas;
        CanvasGroup _watchGroup;
        GameObject _watch, _offer;
        Text _azureScore, _crimsonScore, _badgeSub;
        HudIcon _portrait;
        Text _name, _sub, _stats;
        VectorShape _hpBar;
        RectTransform _hpRt;
        PromptStrip _help1, _help2, _offerStrip;
        MenuItem _offerItem;
        readonly Text[] _ticker = new Text[TickerLines];
        readonly float[] _tickerAt = new float[TickerLines];
        int _tickerNext;
        int _tier = 2;
        float _alpha;

        SimDriverBehaviour _driver;
        bool _subscribed;
        int _cursor = -1;
        int _cursorEpoch = int.MinValue;

        // change detection
        int _lastAzure = -1, _lastCrimson = -1;
        int _lastFocusKey = int.MinValue, _lastKills = -1, _lastDeaths = -1, _lastHill = -1;
        ControlDevice _helpDevice;
        bool _helpBuilt;
        ClassId _helpClass;
        bool _offerBuilt;
        ControlDevice _offerDevice;
        string _lastSub;

        static string[,] _names;
        static readonly string[] ObjectiveNames = { "THE HILL", "THE SHRINE", "AZURE BASE", "CRIMSON BASE", "THE KAIJU" };

        /// <summary>The ticker's current lines, newest first (tests).</summary>
        public string TickerLine(int i) => _ticker[i] != null ? _ticker[i].text : string.Empty;
        public bool OfferVisible => _offer != null && _offer.activeSelf;
        public bool WatchVisible => _watch != null && _watch.activeSelf;
        public Canvas Canvas => _canvas;
        /// <summary>The followed card's name line (tests).</summary>
        public string FollowName => _name != null ? _name.text : string.Empty;

        /// <summary>"AZURE MINER" for a team and class (words pack class names when present).</summary>
        public static string ActorName(TeamId t, ClassId c)
        {
            if (_names == null)
            {
                int nc = ClassIds.All.Count;
                _names = new string[2, nc];
                for (int ti = 0; ti < 2; ti++)
                    for (int ci = 0; ci < nc; ci++)
                        _names[ti, ci] = (KillWords.Team[ti] + " " + KillWords.ClassName((ClassId)ci)).ToUpperInvariant();
            }
            return _names[Mathf.Clamp((int)t, 0, 1), Mathf.Clamp((int)c, 0, _names.GetLength(1) - 1)];
        }

        void OnDisable()
        {
            if (_driver != null && _subscribed) _driver.Ticked -= OnTicked;
            _subscribed = false;
        }

        void Build()
        {
            if (_canvas != null) return;
            _canvas = FeedUi.MakeCanvas(transform, "Spectator Canvas", SortingOrder);
            _canvas.gameObject.AddComponent<GraphicRaycaster>();
            PauseMenu.EnsureEventSystem(transform);
            Color navy = CMTokens.VoidNavy.Color, cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color;

            // ---- watching ----
            RectTransform watch = MenuUi.Rt(_canvas.transform, "Watching");
            MenuUi.Stretch(watch);   // corner pieces follow the real screen edges at any aspect
            _watch = watch.gameObject;
            _watchGroup = _watch.AddComponent<CanvasGroup>();
            _watchGroup.blocksRaycasts = false;
            _watchGroup.interactable = false;

            RectTransform badge = FeedUi.Anchored(watch, "Badge", new Vector2(0f, 1f), HudLayout.Margin, -HudLayout.Margin, 420f, 96f);
            VectorShape bp = MenuUi.Shape(badge, "Plate", ShapeKind.RoundRect, 0f, 0f, 420f, 96f, CMTokens.PanelNavy.Color, 16f);
            bp.OutlinePx = 4f;
            bp.RimPx = 2f;
            VectorShape tag = MenuUi.Shape(badge, "Tag", ShapeKind.RoundRect, 14f, 12f, 200f, 36f, brass, -1f);
            tag.RimPx = 0f;
            MenuUi.Label(badge, "Spectating", CMTokens.Display, UiScale.TypeBody, navy, TextAnchor.MiddleCenter, 14f, 12f, 200f, 36f, "SPECTATING", 0f);
            _azureScore = MenuUi.Fit(MenuUi.Label(badge, "Azure Score", CMTokens.Mono, UiScale.TypeButton, CMTokens.Azure.Color, TextAnchor.MiddleRight, 222f, 12f, 80f, 36f, "0", 2f), 26);
            MenuUi.Label(badge, "Dash", CMTokens.Mono, UiScale.TypeBody, cream, TextAnchor.MiddleCenter, 302f, 12f, 28f, 36f, "-", 2f);
            _crimsonScore = MenuUi.Fit(MenuUi.Label(badge, "Crimson Score", CMTokens.Mono, UiScale.TypeButton, CMTokens.CrimsonInk.Color, TextAnchor.MiddleLeft, 330f, 12f, 80f, 36f, "0", 2f), 26);
            _badgeSub = MenuUi.Label(badge, "Sub", CMTokens.Ui, UiScale.TypeMin, cream, TextAnchor.MiddleLeft, 18f, 54f, 390f, 30f, "", 2f);

            RectTransform ticker = FeedUi.Anchored(watch, "Ticker", new Vector2(0f, 1f), HudLayout.Margin + 6f, -HudLayout.Margin - 108f, 520f, TickerLines * 30f);
            for (int i = 0; i < TickerLines; i++)
            {
                _ticker[i] = MenuUi.Label(ticker, "Line " + i, CMTokens.Ui, UiScale.TypeSmall, cream, TextAnchor.MiddleLeft, 0f, i * 30f, 520f, 28f, "", 2f);
                _tickerAt[i] = -100f;
            }

            Vector2 ps = HudLayout.PlayerSize;
            RectTransform card = FeedUi.Anchored(watch, "Follow Card", new Vector2(0f, 0f), HudLayout.Margin, HudLayout.Margin, ps.x, ps.y);
            VectorShape cp = MenuUi.Shape(card, "Plate", ShapeKind.RoundRect, 0f, 0f, ps.x, ps.y, CMTokens.PanelNavy.Color, 18f);
            cp.OutlinePx = 4f;
            cp.RimPx = 2f;
            VectorShape frame = MenuUi.Shape(card, "Portrait Frame", ShapeKind.RoundRect, 12f, 12f, 108f, 108f, navy, 14f);
            frame.RimPx = 2f;
            _portrait = HudIcon.Make(frame.transform, "Portrait", 2f, 2f, 104f, 104f);
            _name = MenuUi.Label(card, "Name", CMTokens.Display, UiScale.TypeButton, cream, TextAnchor.MiddleLeft, 136f, 10f, 490f, 40f, "", 3f);
            _sub = MenuUi.Label(card, "Sub", CMTokens.Ui, UiScale.TypeSmall, cream, TextAnchor.MiddleLeft, 138f, 48f, 490f, 26f, "", 2f);
            VectorShape hpBack = MenuUi.Shape(card, "HP Back", ShapeKind.RoundRect, 138f, 78f, 300f, 18f, CMTokens.MeterEmpty.Color, -1f);
            hpBack.RimPx = 0f;
            _hpBar = MenuUi.Shape(card, "HP Bar", ShapeKind.RoundRect, 138f, 78f, 300f, 18f, CMTokens.Azure.Color, -1f);
            _hpBar.RimPx = 0f;
            _hpBar.EmptyColor = new Color(0, 0, 0, 0);
            _hpRt = _hpBar.rectTransform;
            _stats = MenuUi.Label(card, "Stats", CMTokens.Ui, UiScale.TypeMin, cream, TextAnchor.MiddleLeft, 138f, 100f, 490f, 26f, "", 2f);

            RectTransform help = FeedUi.Anchored(watch, "Help", new Vector2(0.5f, 0f), 0f, HudLayout.Margin, 1500f, 76f);
            _help1 = PromptStrip.Make(help, "Help 1", 28f, 18, cream);
            Place(_help1.Rect, 0f, 40f, 1500f, 34f);
            _help2 = PromptStrip.Make(help, "Help 2", 28f, 18, cream);
            Place(_help2.Rect, 0f, 2f, 1500f, 34f);

            // ---- the offer while knocked out ----
            RectTransform offer = FeedUi.Anchored(_canvas.transform, "Spectate Offer", new Vector2(0.5f, 0f), 0f, 176f, 460f, 56f);
            _offer = offer.gameObject;
            _offerItem = MenuUi.Item(offer, "Offer", 0f, 0f, 460f, 56f, "", MenuItem.Kind.Button, UiScale.TypeSmall, true);
            _offerItem.Body.Radius = -1f;
            _offerItem.Submitted = () => SpectatorMode.Enter();
            _offerStrip = PromptStrip.Make(_offerItem.transform, "Offer Prompt", 30f, 20, cream);
            Place(_offerStrip.Rect, 0f, 0f, 460f, 56f);

            _watch.SetActive(false);
            _offer.SetActive(false);
        }

        /// <summary>Bottom-left anchored placement inside a parent of known size (y up).</summary>
        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, y);
        }

        void LateUpdate()
        {
            if (Mode == null) Mode = GetComponent<SpectatorMode>();
            if (_driver == null)
            {
                _driver = GetComponent<SimDriverBehaviour>();
                if (_driver == null) return;
            }
            if (!_subscribed)
            {
                _driver.Ticked += OnTicked;
                _subscribed = true;
            }
            World w = _driver.World;
            if (w == null || w.ActorCount <= 0) return;
            bool watching = Mode != null && Mode.Active;
            bool paused = Mode != null && Mode.Input != null && Mode.Input.State.Paused;
            bool down = !w.Actors[0].Alive;
            bool offer = !watching && down && w.Match.Phase == MatchPhase.Playing && !paused && !SimDriverBehaviour.CaptureMode &&
                         GetComponent<TitleScreen>() == null;
            if (!watching && !offer && _canvas == null) return;
            Build();
            int tier = FeedUi.Refresh(_canvas);
            if (tier != _tier)
            {
                _tier = tier;
                _lastFocusKey = int.MinValue;
            }

            if (_offer.activeSelf != offer) _offer.SetActive(offer);
            if (offer) UpdateOffer();

            if (watching && !_watch.activeSelf)
            {
                _watch.SetActive(true);
                _alpha = 0f;
                _lastFocusKey = int.MinValue;
                _helpBuilt = false;
            }
            else if (!watching && _watch.activeSelf && _alpha <= 0f) _watch.SetActive(false);
            if (!_watch.activeSelf) return;
            _alpha = Mathf.MoveTowards(_alpha, watching ? 1f : 0f, Time.unscaledDeltaTime / FadeSeconds);
            _watchGroup.alpha = _alpha;
            if (!watching) return;
            UpdateWatch(w);
        }

        void UpdateOffer()
        {
            ControlDevice d = ControlBindings.ActiveDevice;
            if (!_offerBuilt || d != _offerDevice)
            {
                _offerBuilt = true;
                _offerDevice = d;
                _offerStrip.Begin();
                _offerStrip.AddGlyph(InputGlyphs.ForPath(d == ControlDevice.Gamepad ? "<Gamepad>/buttonNorth" : "<Keyboard>/v"));
                _offerStrip.AddText("SPECTATE WHILE YOU WAIT");
                _offerStrip.End();
            }
            Keyboard kb = Keyboard.current;
            Gamepad pad = Gamepad.current;
            if ((kb != null && kb.vKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) SpectatorMode.Enter();
        }

        void UpdateWatch(World w)
        {
            MatchState m = w.Match;
            int az = (int)System.Math.Floor(m.HillSeconds.Azure), cr = (int)System.Math.Floor(m.HillSeconds.Crimson);
            if (az != _lastAzure)
            {
                _lastAzure = az;
                HudText.Set(_azureScore, NumberCache.Int(az));
            }
            if (cr != _lastCrimson)
            {
                _lastCrimson = cr;
                HudText.Set(_crimsonScore, NumberCache.Int(cr));
            }

            SpectatorRig rig = Mode.Rig;
            ControlDevice d = ControlBindings.ActiveDevice;
            ClassId rejoinAs = SpectatorMode.RejoinClass();
            if (!_helpBuilt || d != _helpDevice || rejoinAs != _helpClass)
            {
                _helpBuilt = true;
                _helpDevice = d;
                _helpClass = rejoinAs;
                BuildHelp(d, rejoinAs);
            }
            string sub = Mode.RejoinPending ? "BACK IN THE FIGHT AT YOUR RESPAWN" :
                rig.Speed == 1 ? "YOUR FIGHTER PLAYS ITSELF" : rig.Speed < 1 ? "SPEED 0.5X" : "SPEED 2X";
            if (!ReferenceEquals(sub, _lastSub))
            {
                _lastSub = sub;
                HudText.Set(_badgeSub, sub);
            }

            for (int i = 0; i < TickerLines; i++)
            {
                if (_ticker[i].text.Length == 0) continue;
                float age = Time.unscaledTime - _tickerAt[i];
                float a = age < TickerSeconds - 1f ? 1f : Mathf.Clamp01(TickerSeconds - age);
                Color c = _ticker[i].color;
                if (Mathf.Abs(c.a - a) > 0.01f)
                {
                    c.a = a;
                    _ticker[i].color = c;
                }
                if (age >= TickerSeconds) HudText.Set(_ticker[i], string.Empty);
            }

            UpdateCard(w, rig);
        }

        void BuildHelp(ControlDevice d, ClassId rejoinAs)
        {
            bool pad = d == ControlDevice.Gamepad;
            _help1.Begin();
            if (pad)
            {
                _help1.AddGlyph(InputGlyphs.ForPath("<Gamepad>/leftShoulder"));
                _help1.AddGlyph(InputGlyphs.ForPath("<Gamepad>/rightShoulder"));
            }
            else
            {
                _help1.AddGlyph(InputGlyphs.ForPath("<Keyboard>/q"));
                _help1.AddGlyph(InputGlyphs.ForPath("<Keyboard>/e"));
                _help1.AddText("OR");
                _help1.AddGlyph(InputGlyphs.ForPath("<Mouse>/leftButton"));
            }
            _help1.AddText("PLAYERS");
            _help1.AddGap(22f);
            if (pad) _help1.AddGlyph(InputGlyphs.ForPath("<Gamepad>/dpad"));
            else
            {
                _help1.AddGlyph(InputGlyphs.ForPath("<Keyboard>/1"));
                _help1.AddText("-");
                _help1.AddGlyph(InputGlyphs.ForPath("<Keyboard>/5"));
            }
            _help1.AddText(pad ? "HILL, SHRINE, BASES" : "HILL, SHRINE, BASES, KAIJU");
            if (pad)
            {
                _help1.AddGlyph(InputGlyphs.ForPath("<Gamepad>/rightStickPress"));
                _help1.AddText("KAIJU");
            }
            _help1.AddGap(22f);
            _help1.AddGlyph(InputGlyphs.ForAction("Move", d));
            _help1.AddText("FLY");
            _help1.End();

            _help2.Begin();
            if (pad)
            {
                _help2.AddGlyph(InputGlyphs.ForPath("<Gamepad>/leftTrigger"));
                _help2.AddGlyph(InputGlyphs.ForPath("<Gamepad>/rightTrigger"));
            }
            else _help2.AddGlyph(InputGlyphs.ForPath("<Mouse>/scroll/y"));
            _help2.AddText("ZOOM");
            _help2.AddGap(22f);
            _help2.AddGlyph(InputGlyphs.ForPath(pad ? "<Gamepad>/buttonNorth" : "<Keyboard>/t"));
            _help2.AddText("MAP VIEW");
            if (Mode.Rig != null && Mode.Driver != null && Mode.Driver.TimeScale > 0)
            {
                _help2.AddGap(22f);
                if (pad) _help2.AddGlyph(InputGlyphs.ForPath("<Gamepad>/leftStickPress"));
                else
                {
                    _help2.AddGlyph(InputGlyphs.ForPath("<Keyboard>/minus"));
                    _help2.AddGlyph(InputGlyphs.ForPath("<Keyboard>/equals"));
                }
                _help2.AddText("SPEED");
            }
            _help2.AddGap(22f);
            _help2.AddGlyph(InputGlyphs.ForPath(pad ? "<Gamepad>/buttonWest" : "<Keyboard>/r"));
            _help2.AddText("REJOIN AS " + KillWords.ClassName(rejoinAs).ToUpperInvariant());
            _help2.End();
        }

        void UpdateCard(World w, SpectatorRig rig)
        {
            int focusKey;
            if (rig.Focus == SpectatorFocus.Player) focusKey = rig.Followed;
            else if (rig.Focus == SpectatorFocus.Objective) focusKey = -10 - (int)rig.Objective;
            else focusKey = -1;

            if (rig.Focus == SpectatorFocus.Player && rig.Followed < w.ActorCount)
            {
                int id = rig.Followed;
                Actor a = w.Actors[id];
                int classKey = id * 8 + (int)a.Cls;   // a class change at a respawn redraws the card
                if (classKey != _lastFocusKey)
                {
                    _lastFocusKey = classKey;
                    _lastKills = _lastDeaths = _lastHill = -1;
                    int t = (int)a.Team;
                    _portrait.Image.enabled = true;
                    _portrait.Set(UiIcons.Portrait[(int)a.Cls, t], _tier);
                    HudText.Set(_name, ActorName(a.Team, a.Cls));
                    _name.color = CMTokens.TeamInk(a.Team);
                    HudText.Set(_sub, id == 0 ? "YOUR FIGHTER, ON AUTOPILOT" : a.Team == TeamId.Azure ? "AZURE TEAM" : "CRIMSON TEAM");
                    _hpBar.SetFill(CMTokens.Team(a.Team));
                    _hpBar.gameObject.SetActive(true);
                }
                float hp01 = a.MaxHp > 0 ? Mathf.Clamp01((float)(a.Hp / a.MaxHp)) : 0f;
                if (!a.Alive) hp01 = 0f;
                _hpBar.Fill01 = hp01;
                int hill = (int)System.Math.Floor(a.HillSeconds);
                if (a.Kills != _lastKills || a.Deaths != _lastDeaths || hill != _lastHill)
                {
                    _lastKills = a.Kills;
                    _lastDeaths = a.Deaths;
                    _lastHill = hill;
                    HudText.Set(_stats, NumberCache.Int(a.Kills) + " KNOCK-OUTS   ·   " + NumberCache.Int(a.Deaths) + " DOWNED   ·   " +
                                        NumberCache.Mmss(hill) + " ON THE HILL");
                }
                return;
            }

            if (focusKey == _lastFocusKey) return;
            _lastFocusKey = focusKey;
            _hpBar.gameObject.SetActive(false);
            _portrait.Image.enabled = false;
            _name.color = CMTokens.WarmCream.Color;
            if (rig.Focus == SpectatorFocus.Objective)
            {
                HudText.Set(_name, ObjectiveNames[(int)rig.Objective]);
                HudText.Set(_sub, "OBJECTIVE CAMERA");
            }
            else
            {
                HudText.Set(_name, "FREE CAMERA");
                HudText.Set(_sub, "FLY ANYWHERE; PICK A PLAYER TO FOLLOW");
            }
            HudText.Set(_stats, string.Empty);
        }

        // ---------------------------------------------------------------- ticker (read-only event ring)

        void OnTicked(World w)
        {
            if (w == null) return;
            if (_cursor < 0 || w.Epoch != _cursorEpoch || _cursor > w.EventHead)
            {
                _cursor = w.EventHead;
                _cursorEpoch = w.Epoch;
                return;
            }
            if (Mode == null || !Mode.Active || _canvas == null)
            {
                _cursor = w.EventHead;
                return;
            }
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;
            int followed = Mode.Rig.Focus == SpectatorFocus.Player ? Mode.Rig.Followed : -1;
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                string line = LineFor(w, e, followed);
                if (line != null) Push(line);
            }
        }

        static readonly string[] HillLines = { "THE HILL IS NEUTRAL", "AZURE TAKE THE HILL", "CRIMSON TAKE THE HILL", "THE HILL IS CONTESTED" };
        static readonly string[] HatchLines = { "THE KAIJU HATCHES", "THE KAIJU HATCHES FOR AZURE", "THE KAIJU HATCHES FOR CRIMSON", "THE KAIJU HATCHES BERSERK" };

        /// <summary>The ticker line for an event, or null (most events stay off the ticker; the kill feed has the rest).</summary>
        public static string LineFor(World w, SimEvent e, int followed)
        {
            switch (e.Type)
            {
                case SimEventType.HillOwnerChange: return HillLines[Mathf.Clamp(e.A, 0, 3)];
                case SimEventType.Hatch: return HatchLines[Mathf.Clamp((int)e.Value, 0, 3)];
                case SimEventType.KaijuKO: return "THE KAIJU IS DOWN";
                case SimEventType.KaijuRecover: return "THE KAIJU IS BACK UP";
                case SimEventType.BerserkLocked: return "THE KAIJU GOES BERSERK";
                case SimEventType.MatchEnd: return "MATCH OVER";
                case SimEventType.Death:
                    if (followed < 0 || w == null) return null;
                    if (e.B == followed && e.A >= 0 && e.A < w.ActorCount)
                        return ActorName(w.Actors[followed].Team, w.Actors[followed].Cls) + " KNOCKED OUT " +
                               ActorName(w.Actors[e.A].Team, w.Actors[e.A].Cls);
                    if (e.A == followed) return ActorName(w.Actors[followed].Team, w.Actors[followed].Cls) + " IS DOWN";
                    return null;
            }
            return null;
        }

        void Push(string line)
        {
            // Newest on top: shift the pool down by one (fixed size, no growth).
            for (int i = TickerLines - 1; i > 0; i--)
            {
                HudText.Set(_ticker[i], _ticker[i - 1].text);
                _tickerAt[i] = _tickerAt[i - 1];
                _ticker[i].color = _ticker[i - 1].color;
            }
            HudText.Set(_ticker[0], line);
            _tickerAt[0] = Time.unscaledTime;
            Color c = _ticker[0].color;
            c.a = 1f;
            _ticker[0].color = c;
            _tickerNext++;
        }
    }
}
