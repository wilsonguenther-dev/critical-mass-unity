using System.Globalization;
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
    /// b15: the words and places of the comeback tag and the hut vote (pure; tests). Both sit in the top-centre lane under
    /// its stack (job line, team stock, map-event bar): the RALLY tag first, the vote's two cards under it.
    /// </summary>
    public static class B15Hud
    {
        /// <summary>The vote keys: [ and ] on the keyboard (free in every scheme), D-pad left / right on a controller.</summary>
        public const string KeyLeft = "[", KeyRight = "]", PadLeft = "D-PAD <", PadRight = "D-PAD >";
        // b15 view polish (Editor review item 2): the cards read at ~8 px (232 x 78, best-fit text shrinking to 9) stacked
        // under the RALLY tag. Now 300 x 96 at the 1080p reference with type that never shrinks below its size (name 18,
        // line 16 / floor 15, votes and key 15), the key to press, the countdown in the head, a LEADING mark on the card the
        // team leads, and their OWN anchor (VoteTop), so a taller RALLY tag can never push them or sit on them. The canvas
        // scales with the screen (HudLayout.Scale), so the sizes hold at every resolution relative to 1080p.
        public const float CardW = 300f, CardH = 96f, CardGap = 12f, HeadH = 30f, HeadGap = 6f;
        public const int HeadFont = 18, NameFont = 18, LineFont = 16, LineFloor = 15, VotesFont = 15, KeyFont = 15;
        /// <summary>The vote's own anchor: a fixed line under the RALLY tag's lane (not RallyRect.yMax).</summary>
        public const float VoteTop = 344f;

        // b15 view polish (item 8): the RALLY tag text has the same 16 px floor (was 16 shrinking to 10).
        public const float RallyW = 380f, RallyH = 36f;
        public const int RallyFont = 18, RallyFloor = 16;

        public static Rect RallyRect => new Rect((HudLayout.RefW - RallyW) * 0.5f, HudZones.EventY + HudZones.EventH + 8f, RallyW, RallyH);

        public static Rect VoteRect => new Rect((HudLayout.RefW - (2f * CardW + CardGap)) * 0.5f, VoteTop, 2f * CardW + CardGap, HeadH + HeadGap + CardH);

        /// <summary>One card's rect in reference px (c 0 left, 1 right).</summary>
        public static Rect CardRect(int c) => new Rect(VoteRect.x + c * (CardW + CardGap), VoteRect.y + HeadH + HeadGap, CardW, CardH);

        /// <summary>The card the team leads (1 or 2), 0 on a tie.</summary>
        public static int Leading(int votesA, int votesB) => votesA > votesB ? 1 : votesB > votesA ? 2 : 0;
        public const string LeadingWord = "LEADING";

        // b15 view polish (item 8): "NO SCORE: YOUR KAIJU" / "THEIR KAIJU" as a pill directly under the hill meter (the hill
        // bar ends at 106 in the match band), above the objective line (HudZones.JobY 140): warning amber fill with navy ink
        // for YOUR Kaiju (your own Kaiju costs you the hill), calm panel navy with cream ink for THEIR Kaiju; 18 px type,
        // 16 px floor; a short scale pulse when the state begins (no brightness flash unless intense flashing is on).
        public const float KaijuPillW = 340f, KaijuPillH = 28f, KaijuPillTop = 108f;
        public const int KaijuPillFont = 18, KaijuPillFloor = 16;
        public const string KaijuPillWarnHex = "#F2A93B";
        public const float PulseSeconds = 0.45f, PulseScale = 0.10f;

        public static Rect KaijuPillRect => new Rect((HudLayout.RefW - KaijuPillW) * 0.5f, KaijuPillTop, KaijuPillW, KaijuPillH);

        /// <summary>The pulse scale at t seconds after the state began (1 outside the pulse; one smooth bump).</summary>
        public static float Pulse(float t) => t < 0f || t >= PulseSeconds ? 1f : 1f + PulseScale * Mathf.Sin(Mathf.PI * t / PulseSeconds);

        /// <summary>The pill's fill brightening toward cream during the pulse: only with intense flashing allowed.</summary>
        public static float PulseFlash(float t, bool intenseAllowed) => intenseAllowed && t >= 0f && t < PulseSeconds ? 0.35f * Mathf.Sin(Mathf.PI * t / PulseSeconds) : 0f;

        /// <summary>Rendered type size in screen px for a reference size on a w x h screen (the HUD canvas scale).</summary>
        public static float ScreenPx(int refSize, float w, float h) => refSize * HudLayout.Scale(w, h);

        public static string RallyLine(bool own, double multiplier, int secondsLeft) =>
            (own ? ComebackNames.Rally : ComebackNames.EnemyRally) + " x" + multiplier.ToString("0.##", CultureInfo.InvariantCulture) + "   " + secondsLeft + " s left";

        public static string VoteHead(int level, int secondsLeft) => string.Format(CultureInfo.InvariantCulture, HutChoiceNames.Vote, level) + "   " + secondsLeft + " s";

        public const string RallyPointLine = "RALLY POINT: you come back closer to the hill";
        public const string TurnReason = "Knocked down, the Kaiju gets up for the team that beat it.";

        public static string TurnHeadline(bool ours) => ours ? "THE KAIJU JOINS YOU" : "THE KAIJU TURNS ON YOU";

        public static string TiringLine(bool ours, double share) =>
            (ours ? "YOUR HILL TIRES x" : "THEIR HILL TIRES x") + share.ToString("0.00", CultureInfo.InvariantCulture);

        public static string Votes(int n) => n == 1 ? "1 vote" : n + " votes";

        public static string Result(int level, HutOption o) => string.Format(CultureInfo.InvariantCulture, HutChoiceNames.Chosen, level, HutChoiceNames.Name[(int)o]);

        public static string ResultReason(HutOption o, int a, int b, bool tie) =>
            HutChoiceNames.Line[(int)o] + (tie ? (a + b == 0 ? " (no votes: drawn by lot)" : " (a tie: drawn by lot)") : " (" + System.Math.Max(a, b) + " to " + System.Math.Min(a, b) + ")");

        /// <summary>The scoreboard's line of a team's picks, level order ("HUT  QUICK RETURN / SHIELD WALL"); empty with none.</summary>
        public static string Picks(World w, TeamId team)
        {
            if (w == null || !HutChoices.On(w)) return string.Empty;
            string s = string.Empty;
            for (int l = 1; l <= HutChoiceState.Levels; l++)
            {
                HutOption o = HutChoices.Chosen(w, team, l);
                if (o == HutOption.None) continue;
                s += (s.Length == 0 ? "HUT  " : " / ") + HutChoiceNames.Name[(int)o];
            }
            return s;
        }

        public static int SecondsLeft(int ticks, double hz) => hz > 0 ? (int)System.Math.Ceiling(ticks / hz) : 0;
    }

    /// <summary>
    /// b15 HUD: the RALLY tag (whichever team is rallying, with the cap left; brighter on the ticks it earns), the hut vote's
    /// two cards with the real keys, live vote counts and the time left (own team only, the enemy sees nothing until it is
    /// decided), the vote keys themselves ([ / ], D-pad left / right: InputFrame.StationBuy 1 / 2 through DepthInputLatch),
    /// a banner with the team's result, a feed line with the enemy's pick, and a one-time RALLY banner per team. View only;
    /// never writes the sim except through the input latch. Added to the driver beside DepthHud by MechanicsWorldView.
    /// </summary>
    public sealed class B15RulesHud : MonoBehaviour
    {
        public const int SortingOrder = 15;

        SimDriverBehaviour _driver;
        Canvas _canvas;
        RectTransform _root, _space, _rally, _vote;
        VectorShape _rallyPanel;
        Text _rallyText, _voteHead;
        readonly RectTransform[] _card = new RectTransform[2];
        readonly VectorShape[] _cardPanel = new VectorShape[2];
        readonly Text[] _cardKey = new Text[2], _cardName = new Text[2], _cardLine = new Text[2], _cardVotes = new Text[2], _cardLead = new Text[2];
        int _leadShown = -1;

        // caches: strings rebuilt only when their numbers change
        int _epoch = -1, _rallyKey = int.MinValue, _voteKey = int.MinValue, _cardsLevel = -1, _keysDevice = -1;
        readonly int[] _votesShown = { -1, -1 }, _resultTick = { -1, -1 }, _rallyBanner = { 0, 0 };
        int _myVoteShown = -1, _turnsShown = -1;

        /// <summary>Shown flags (tests and captures).</summary>
        public bool RallyShown => _rally != null && _rally.gameObject.activeSelf;
        public bool VoteShown => _vote != null && _vote.gameObject.activeSelf;

        /// <summary>True while the local team's hut vote is open: the D-pad left / right are the vote keys, so the ping wheel and
        /// the minimap zoom (their usual controller binds) ignore D-pad presses until the vote closes (HudInteract reads this).</summary>
        public static bool VoteOwnsDpad { get; private set; }

        void OnDisable() => VoteOwnsDpad = false;

        void Awake()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            Build();
        }

        static VectorShape Chip(Transform p, string n, float x, float y, float w, float h, Color fill, float r = 10f)
        {
            VectorShape s = MenuUi.Shape(p, n, ShapeKind.RoundRect, x, y, w, h, fill, r);
            s.OutlinePx = 3f;
            s.RimPx = 0f;
            return s;
        }

        void Build()
        {
            _canvas = InteractUi.MakeHudCanvas(transform, "B15 Rules HUD Canvas", SortingOrder, out _root, out _space);
            Color cream = CMTokens.WarmCream.Color, brass = CMTokens.ToolBrass.Color, panel = CMTokens.PanelNavy.Color;

            Rect r = B15Hud.RallyRect;
            _rally = MenuUi.Rt(_space, "Rally Tag");
            HudZones.RegisterPanel(_rally);
            MenuUi.Place(_rally, r.x, r.y, r.width, r.height);
            _rallyPanel = Chip(_rally, "Panel", 0f, 0f, r.width, r.height, CMTokens.WithAlpha(panel, 0.92f), 10f);
            _rallyPanel.RimPx = 2f;
            _rallyText = MenuUi.Fit(MenuUi.Label(_rally, "Text", CMTokens.Display, B15Hud.RallyFont, cream, TextAnchor.MiddleCenter, 8f, 0f, r.width - 16f, r.height, "", 2f), B15Hud.RallyFloor);
            _rally.gameObject.SetActive(false);

            Rect v = B15Hud.VoteRect;
            _vote = MenuUi.Rt(_space, "Hut Vote");
            HudZones.RegisterPanel(_vote);
            MenuUi.Place(_vote, v.x, v.y, v.width, v.height);
            VectorShape hp = Chip(_vote, "Head Panel", 0f, 0f, v.width, B15Hud.HeadH, CMTokens.WithAlpha(panel, 0.94f), 8f);
            hp.RimPx = 2f;
            hp.RimColor = brass;
            _voteHead = MenuUi.Fit(MenuUi.Label(_vote, "Head", CMTokens.Display, B15Hud.HeadFont, brass, TextAnchor.MiddleCenter, 8f, 0f, v.width - 16f, B15Hud.HeadH, "", 2f), B15Hud.HeadFont);
            for (int c = 0; c < 2; c++)
            {
                const float W = B15Hud.CardW, H = B15Hud.CardH;
                float x = c * (W + B15Hud.CardGap);
                RectTransform card = MenuUi.Rt(_vote, "Card " + c);
                MenuUi.Place(card, x, B15Hud.HeadH + B15Hud.HeadGap, W, H);
                _card[c] = card;
                _cardPanel[c] = Chip(card, "Panel", 0f, 0f, W, H, CMTokens.WithAlpha(panel, 0.96f), 12f);
                _cardPanel[c].RimPx = 2f;
                Chip(card, "Key Chip", 10f, 10f, 76f, 30f, CMTokens.WithAlpha(brass, 0.95f), 6f);
                _cardKey[c] = MenuUi.Fit(MenuUi.Label(card, "Key", CMTokens.Mono, B15Hud.KeyFont, CMTokens.VoidNavy.Color, TextAnchor.MiddleCenter, 10f, 10f, 76f, 30f, "", 0f), B15Hud.KeyFont);
                _cardName[c] = MenuUi.Fit(MenuUi.Label(card, "Name", CMTokens.Display, B15Hud.NameFont, cream, TextAnchor.MiddleLeft, 96f, 8f, W - 106f, 34f, "", 2f), B15Hud.NameFont);
                _cardLine[c] = MenuUi.Fit(MenuUi.Label(card, "Line", CMTokens.Ui, B15Hud.LineFont, cream, TextAnchor.MiddleLeft, 12f, 44f, W - 24f, 24f, "", 1.5f), B15Hud.LineFloor);
                // b15 Editor review r2: LEADING at the name size (the Display face at 15 read about 8 px tall at 1080p, 6 px on a laptop)
                _cardLead[c] = MenuUi.Fit(MenuUi.Label(card, "Leading", CMTokens.Display, B15Hud.NameFont, brass, TextAnchor.MiddleLeft, 12f, 70f, 120f, 22f, "", 1.5f), B15Hud.NameFont);
                _cardVotes[c] = MenuUi.Fit(MenuUi.Label(card, "Votes", CMTokens.Mono, B15Hud.VotesFont, CMTokens.WithAlpha(cream, 0.9f), TextAnchor.MiddleRight, W - 132f, 70f, 120f, 20f, "", 1.5f), B15Hud.VotesFont);
            }
            _vote.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            DepthWorldView view = DepthWorldView.Active;
            if (_canvas == null) return;
            InteractUi.UpdateScale(_canvas);
            if (_canvas.worldCamera == null) _canvas.worldCamera = UnityEngine.Camera.main;
            bool hide = w == null || view == null || w.Match.Phase == MatchPhase.Ended || Scoreboard.AnyOpen || CriticalMass.Runtime.Menus.MenuGate.AnyOpen;
            if (_canvas.enabled == hide) _canvas.enabled = !hide;
            if (hide) { VoteOwnsDpad = false; return; }
            int me = view.Feed.LocalActor;
            if (me < 0 || me >= w.ActorCount) return;
            if (w.Epoch != _epoch)
            {
                _epoch = w.Epoch;
                _rallyKey = _voteKey = int.MinValue;
                _cardsLevel = -1;
                _resultTick[0] = _resultTick[1] = -1;
                _rallyBanner[0] = _rallyBanner[1] = 0;
                _votesShown[0] = _votesShown[1] = -1;
                _myVoteShown = -1;
                _turnsShown = -1;
                _kaijuHillBanner = false;
            }
            TeamId team = w.Actors[me].Team;
            TickRally(w, team);
            TickKaijuHill(w, team);
            TickVote(w, me, team);
            TickResults(w, team);
        }

        bool _kaijuHillBanner;

        /// <summary>b15 D0: once a match, the first time a team's own Kaiju stands on the hill it holds (so that team scores
        /// nothing), a banner for both teams from their own side; the hill word under the hill bar carries it after that.</summary>
        void TickKaijuHill(World w, TeamId team)
        {
            if (_kaijuHillBanner) return;
            int t = Comeback.KaijuBlocking(w);
            if (t < 0) return;
            _kaijuHillBanner = true;
            bool own = t == (int)team;
            Banner.Show(new BannerSpec
            {
                Headline = own ? ComebackNames.KaijuHillOwnHead : ComebackNames.KaijuHillEnemyHead,
                Reason = own ? ComebackNames.KaijuHillOwnLine : ComebackNames.KaijuHillEnemyLine,
                Style = t == 0 ? BannerStyle.Azure : BannerStyle.Crimson,
                Priority = 4,
                HoldSeconds = 2.5,
            });
        }

        void TickRally(World w, TeamId team)
        {
            TeamId enemy = team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
            bool ownTrails = Comeback.Trailing(w, team), enemyTrails = Comeback.Trailing(w, enemy);
            bool show = ownTrails || enemyTrails;
            if (show && !_rally.gameObject.activeSelf) _rally.gameObject.SetActive(true);   // hidden by TickLevers
            // a one-time banner per team, the first tick that team earns RALLY credit (the announcer line, both teams see it)
            for (int t = 0; t < 2; t++)
            {
                if (_rallyBanner[t] != 0 || w.Rally.FirstTick[t] < 0) continue;
                _rallyBanner[t] = 1;
                bool own = (TeamId)t == team;
                Banner.Show(new BannerSpec
                {
                    Headline = own ? "RALLY!" : ((TeamId)t == TeamId.Azure ? "AZURE" : "CRIMSON") + " RALLIES",
                    Reason = own ? ComebackNames.Hint : "They are behind: their hill time counts more. Keep them off it.",
                    Style = (TeamId)t == TeamId.Azure ? BannerStyle.Azure : BannerStyle.Crimson,
                    Priority = 4,
                    HoldSeconds = 2.0,
                });
            }
            if (!show) { TickLevers(w, team, enemy); return; }
            TeamId who = ownTrails ? team : enemy;
            int left = (int)System.Math.Ceiling(Comeback.CapLeftSeconds(w, who));
            bool earning = Comeback.Earning(w, who);
            int key = (who == team ? 1 : 2) * 100000 + left * 2 + (earning ? 1 : 0);
            if (key == _rallyKey) return;
            _rallyKey = key;
            HudText.Set(_rallyText, B15Hud.RallyLine(who == team, Comeback.Multiplier(w), left));
            Color ink = CMTokens.TeamInk(who);
            _rallyText.color = earning ? CMTokens.WarmCream.Color : ink;
            _rallyPanel.RimColor = ink;
            _rallyPanel.SetFill(earning ? CMTokens.WithAlpha(CMTokens.Team(who), 0.85f) : CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.92f));
        }

        /// <summary>b15 pass 2: with no RALLY to show, the same tag shows the hill tiring (HOLD FATIGUE) or the trailing team's
        /// RALLY POINT (FORWARD RETURN); a turned Kaiju gets a banner for both teams.</summary>
        void TickLevers(World w, TeamId team, TeamId enemy)
        {
            KaijuState ks = w.Kaiju.State;
            if ((ks == KaijuState.Azure || ks == KaijuState.Crimson) && w.Rally.Turns != _turnsShown)
            {
                if (_turnsShown >= 0 && w.Rally.Turns > _turnsShown)
                {
                    TeamId to = ks == KaijuState.Azure ? TeamId.Azure : TeamId.Crimson;
                    Banner.Show(new BannerSpec
                    {
                        Headline = B15Hud.TurnHeadline(to == team),
                        Reason = B15Hud.TurnReason,
                        Style = to == TeamId.Azure ? BannerStyle.Azure : BannerStyle.Crimson,
                        Priority = 5,
                        HoldSeconds = 2.0,
                    });
                }
                _turnsShown = w.Rally.Turns;
            }
            TeamId owner = w.Match.HillState == HillState.Azure ? TeamId.Azure : TeamId.Crimson;
            bool owned = w.Match.HillState == HillState.Azure || w.Match.HillState == HillState.Crimson;
            double share = owned ? Comeback.HoldShare(w, owner) : 1.0;
            bool forward = Comeback.ForwardActive(w, team);
            int key;
            string line;
            TeamId who;
            if (share < 0.995) { who = owner; key = 300000 + (owner == team ? 1000 : 0) + (int)System.Math.Round(share * 100); line = B15Hud.TiringLine(owner == team, share); }
            else if (forward) { who = team; key = 400000; line = B15Hud.RallyPointLine; }
            else
            {
                if (_rally.gameObject.activeSelf) _rally.gameObject.SetActive(false);
                _rallyKey = int.MinValue;
                return;
            }
            if (!_rally.gameObject.activeSelf) _rally.gameObject.SetActive(true);
            if (key == _rallyKey) return;
            _rallyKey = key;
            HudText.Set(_rallyText, line);
            Color ink = CMTokens.TeamInk(who);
            _rallyText.color = ink;
            _rallyPanel.RimColor = ink;
            _rallyPanel.SetFill(CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.92f));
        }

        void TickVote(World w, int me, TeamId team)
        {
            int level = HutChoices.VoteLevel(w, team);
            bool show = level > 0;
            VoteOwnsDpad = show;
            if (_vote.gameObject.activeSelf != show) _vote.gameObject.SetActive(show);
            if (!show) { _voteKey = int.MinValue; return; }

            // the keys: one press each, the last press counts (the sim takes StationBuy 1 or 2 while the vote is open)
            int press = 0;
            Keyboard kb = Keyboard.current;
            Gamepad gp = Gamepad.current;
            bool typing = CriticalMass.Runtime.Online.OnlineChatHud.Typing || CriticalMass.Runtime.Menu.CharacterPanel.IsTyping;
            if (typing) kb = null;
            if (kb != null && kb.leftBracketKey.wasPressedThisFrame) press = 1;
            else if (kb != null && kb.rightBracketKey.wasPressedThisFrame) press = 2;
            else if (gp != null && gp.dpad.left.wasPressedThisFrame) press = 1;
            else if (gp != null && gp.dpad.right.wasPressedThisFrame) press = 2;
            if (press > 0) DepthInputLatch.QueueStationBuy(press - 1);

            int device = (int)CriticalMass.Runtime.Input.ControlBindings.ActiveDevice;
            if (level != _cardsLevel || device != _keysDevice)
            {
                _cardsLevel = level;
                _keysDevice = device;
                bool pad = CriticalMass.Runtime.Input.ControlBindings.ActiveDevice == CriticalMass.Runtime.Input.ControlDevice.Gamepad;
                for (int c = 0; c < 2; c++)
                {
                    HutOption o = HutChoices.OptionAt(level, c + 1);
                    HudText.Set(_cardKey[c], c == 0 ? (pad ? B15Hud.PadLeft : B15Hud.KeyLeft) : (pad ? B15Hud.PadRight : B15Hud.KeyRight));
                    HudText.Set(_cardName[c], HutChoiceNames.Name[(int)o]);
                    HudText.Set(_cardLine[c], HutChoiceNames.Line[(int)o]);
                }
                _votesShown[0] = _votesShown[1] = -1;
                _myVoteShown = -1;
                _leadShown = -1;
            }
            int secs = B15Hud.SecondsLeft(HutChoices.VoteTicksLeft(w, team), w.Config.SimHz);
            int key = level * 1000 + secs;
            if (key != _voteKey)
            {
                _voteKey = key;
                HudText.Set(_voteHead, B15Hud.VoteHead(level, secs));
            }
            for (int c = 0; c < 2; c++)
            {
                int n = HutChoices.Votes(w, team, c + 1);
                if (n == _votesShown[c]) continue;
                _votesShown[c] = n;
                HudText.Set(_cardVotes[c], B15Hud.Votes(n));
            }
            int lead = B15Hud.Leading(_votesShown[0], _votesShown[1]);
            if (lead != _leadShown)
            {
                _leadShown = lead;
                for (int c = 0; c < 2; c++) HudText.Set(_cardLead[c], lead == c + 1 ? B15Hud.LeadingWord : string.Empty);
            }
            int mine = HutChoices.VoteOf(w, me);
            if (mine != _myVoteShown)
            {
                _myVoteShown = mine;
                for (int c = 0; c < 2; c++)
                {
                    bool picked = mine == c + 1;
                    _cardPanel[c].RimColor = picked ? CMTokens.ToolBrass.Color : CMTokens.WarmCream.Color;
                    _cardPanel[c].RimPx = picked ? 4f : 2f;
                    _cardPanel[c].SetVerticesDirty();
                }
            }
        }

        void TickResults(World w, TeamId team)
        {
            if (!HutChoices.On(w)) return;
            HutChoiceState s = w.HutVote;
            for (int t = 0; t < 2; t++)
            {
                int tick = s.LastTick[t];
                if (tick < 0 || tick == _resultTick[t]) continue;
                bool first = _resultTick[t] == -1 && w.Tick - tick > 90; // joined late: no stale banner
                _resultTick[t] = tick;
                if (first) continue;
                HutOption o = HutChoices.OptionAt(s.LastLevel[t], s.LastCard[t]);
                if (o == HutOption.None) continue;
                if ((TeamId)t == team)
                {
                    Banner.Show(new BannerSpec
                    {
                        Headline = B15Hud.Result(s.LastLevel[t], o),
                        Reason = B15Hud.ResultReason(o, s.LastA[t], s.LastB[t], s.LastDefault[t] != 0),
                        Style = team == TeamId.Azure ? BannerStyle.Azure : BannerStyle.Crimson,
                        Priority = 4,
                        HoldSeconds = 2.0,
                    });
                }
                else
                {
                    HudInteract hi = HudInteract.Instance;
                    if (hi != null)
                        hi.Feed.AddLine(((TeamId)t == TeamId.Azure ? "AZURE" : "CRIMSON") + " HUT: " + HutChoiceNames.Name[(int)o], ShapeKind.Hexagon, CMTokens.TeamInk((TeamId)t), Time.unscaledTime);
                }
            }
        }
    }
}
