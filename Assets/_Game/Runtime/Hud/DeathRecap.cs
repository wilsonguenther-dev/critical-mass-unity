using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// DG-11 death recap: while the local player is down, a card under GameHud's "RESPAWN IN" count shows who did it
    /// ("Knocked out by Crimson Warrior: Cleave") with the killer's portrait, and one tip picked from how it happened.
    /// Opens on the local player's knock-out event, closes on respawn or match end. View only.
    /// </summary>
    public sealed class DeathRecap : MonoBehaviour
    {
        public const float CardW = 820f, CardH = 156f, OffsetY = -190f, PopSeconds = 0.2f;

        public Canvas Canvas { get; private set; }
        public bool IsOpen { get; private set; }
        /// <summary>The recap line as shown.</summary>
        public string Line => _line != null ? _line.text : string.Empty;
        public string TipText => _tip != null ? _tip.text : string.Empty;
        /// <summary>The portrait icon name shown (null when none).</summary>
        public string PortraitName => _portrait != null ? _portrait.Name : null;

        /// <summary>Raised when the card opens / closes (game-flow overlays can listen).</summary>
        public static event System.Action<DeathRecap> Opened, Closed;

        CombatLog _log;
        bool _subscribed;
        RectTransform _card;
        CanvasGroup _group;
        HudIcon _portrait;
        VectorShape _frame;
        Text _line, _tip, _tipLabel;
        float _age;
        int _tier = 2;

        void Awake() => Build();
        void OnDisable() => Unsubscribe();

        public void Build()
        {
            if (Canvas != null) return;
            KillWords.Init(null);
            Canvas = FeedUi.MakeCanvas(transform, "Death Recap Canvas", 12);
            _card = FeedUi.Anchored(Canvas.transform, "Death Recap", new Vector2(0.5f, 0.5f), 0f, OffsetY, CardW, CardH);
            _group = _card.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            MenuUi.Panel(_card, "Panel", 0f, 0f, CardW, CardH);
            _frame = MenuUi.Shape(_card, "Portrait Frame", ShapeKind.Chamfer, 18f, 18f, 120f, 120f, CMTokens.VoidNavy.Color, 18f);
            _portrait = HudIcon.Make(_frame.transform, "Portrait", 7f, 7f, 106f, 106f);
            _line = MenuUi.Label(_card, "Line", CMTokens.Ui, UiScale.TypeHeading, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 160f, 22f, 640f, 44f, "", 3f);
            _tipLabel = MenuUi.Label(_card, "Tip Label", CMTokens.Display, UiScale.TypeSmall, CMTokens.ToolBrass.Color, TextAnchor.UpperLeft, 160f, 80f, 60f, 26f, "TIP", 2f);
            _tip = MenuUi.Fit(MenuUi.Label(_card, "Tip", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.UpperLeft, 220f, 78f, 580f, 64f, "", 2f), 21);
            _tip.horizontalOverflow = HorizontalWrapMode.Wrap;
            // best fit needs bounded overflow: a long name ("Demolitions Expert") shrinks to fit one line
            _line.horizontalOverflow = HorizontalWrapMode.Wrap;
            _line.verticalOverflow = VerticalWrapMode.Truncate;
            _line.resizeTextForBestFit = true;
            _line.resizeTextMinSize = 22;
            _line.resizeTextMaxSize = 32;
            _card.gameObject.SetActive(false);
        }

        public void Bind(CombatLog log)
        {
            if (log == _log) { Subscribe(); return; }
            Unsubscribe();
            _log = log;
            Subscribe();
        }

        void Subscribe()
        {
            if (_subscribed || _log == null) return;
            _log.Log.Emitted += OnEvent;
            _log.Log.MatchReset += Hide;
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed || _log == null) return;
            _log.Log.Emitted -= OnEvent;
            _log.Log.MatchReset -= Hide;
            _subscribed = false;
        }

        void LateUpdate()
        {
            if (_log == null) Bind(GetComponent<CombatLog>());
            else Subscribe();
            int tier = FeedUi.Refresh(Canvas);
            if (tier != _tier) { _tier = tier; _portrait.SetTier(tier); }
            bool hide = _log == null || _log.Suppressed;
            if (Canvas.enabled == hide) Canvas.enabled = !hide;
            if (!IsOpen) return;
            if (!hide) HudZones.Hold(HudZone.Lower, HudPriority.Recap);
            World w = _log != null && _log.Driver != null ? _log.Driver.World : null;
            int local = _log != null ? _log.LocalActor : HudBinder.LocalActor;
            if (w != null && (w.Match.Phase == MatchPhase.Ended || (local < w.ActorCount && w.Actors[local].Alive))) { Hide(); return; }
            Animate(_log != null && _log.Paused ? 0f : Time.unscaledDeltaTime);
        }

        void OnEvent(CombatEvent e)
        {
            int local = _log != null ? _log.LocalActor : HudBinder.LocalActor;
            if (e.Kind == CombatEventKind.Respawn && e.Actor == local) { Hide(); return; }
            if (e.Kind != CombatEventKind.Knockout || e.Actor != local) return;
            World w = _log != null && _log.Driver != null ? _log.Driver.World : null;
            bool lostCargo = w != null && local < w.ActorCount && w.Actors[local].LostCargo.Kind != CarryKind.None;
            int deaths = w != null && local < w.ActorCount ? w.Actors[local].Deaths : 1;
            Show(e, w != null ? w.Kaiju.State : KaijuState.Neutral, lostCargo, deaths);
        }

        /// <summary>Opens the card for knock-out e of the local player (tests call this with a scripted event).</summary>
        public void Show(CombatEvent e, KaijuState kaiju, bool lostCargo, int deaths)
        {
            if (Canvas == null) Build();
            HudText.Set(_line, KillWords.Recap(e.Other, e.OtherTeam, e.OtherClass));
            HudText.Set(_tip, TipFor(e.Other, e.ActorClass, lostCargo, kaiju, deaths) ?? string.Empty);
            bool hasTip = _tip.text.Length > 0;
            if (_tipLabel.gameObject.activeSelf != hasTip) _tipLabel.gameObject.SetActive(hasTip);
            string portrait = PortraitFor(e.Other, e.OtherTeam, e.OtherClass, kaiju);
            _portrait.Set(portrait, _tier);
            _frame.SetFill(e.Other >= 0 ? CMTokens.Team(e.OtherTeam) : CMTokens.VoidNavy.Color);
            _age = 0f;
            _card.gameObject.SetActive(true);
            Animate(0f);
            bool was = IsOpen;
            IsOpen = true;
            if (!was) Opened?.Invoke(this);
        }

        public void Hide()
        {
            if (_card != null && _card.gameObject.activeSelf) _card.gameObject.SetActive(false);
            if (!IsOpen) return;
            IsOpen = false;
            Closed?.Invoke(this);
        }

        void Animate(float dt)
        {
            _age += dt;
            float t = FeedUi.Ease(Mathf.Clamp01(_age / PopSeconds));
            float s = Mathf.Lerp(0.92f, 1f, t);
            _card.localScale = new Vector3(s, s, 1f);
            if (Mathf.Abs(_group.alpha - t) > 1e-3f) _group.alpha = t;
        }

        /// <summary>The killer's portrait: the class portrait in its team colour, the Kaiju's art, or none when unknown.</summary>
        public static string PortraitFor(int killer, TeamId team, ClassId cls, KaijuState kaiju)
        {
            if (killer >= 0) return UiIcons.Portrait[Mathf.Clamp((int)cls, 0, UiIcons.Classes.Length - 1), (int)team];
            if (!KillSource.IsKaiju(killer)) return null;
            switch (kaiju)
            {
                case KaijuState.Azure: return UiIcons.Adult[1];
                case KaijuState.Crimson: return UiIcons.Adult[2];
                case KaijuState.Berserk: return UiIcons.Adult[3];
                case KaijuState.Neutral: return UiIcons.Adult[0];
                default: return UiIcons.Hatchling;
            }
        }

        static readonly string[] GeneralTips = { "tip-01", "tip-02", "tip-18" };

        /// <summary>
        /// One tip for how the player went down: the slam circle for a slam, the berserk rule for a berserk Kaiju, carriers
        /// for lost cargo, the shield for a Warrior, the respawn rule on the first knock-out, else a hill tip in rotation.
        /// </summary>
        public static string TipFor(int killer, ClassId victimClass, bool lostCargo, KaijuState kaiju, int deaths)
        {
            string id;
            if (killer == KillSource.KaijuSlam) id = "tip-15";
            else if (killer == KillSource.KaijuMelee) id = kaiju == KaijuState.Berserk ? "tip-08" : "tip-15";
            else if (lostCargo) id = "tip-19";
            else if (deaths <= 1) id = "tip-14";
            else if (victimClass == ClassId.Warrior && deaths % 3 == 2) id = "tip-11";
            else id = GeneralTips[(deaths < 0 ? 0 : deaths) % GeneralTips.Length];
            return KillWords.Tip(id) ?? KillWords.Tip("tip-02");
        }
    }
}
