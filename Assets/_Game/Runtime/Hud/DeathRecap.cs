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
        public const float CardW = 820f, CardH = 156f, PopSeconds = 0.2f;
        /// <summary>
        /// Owner walk 1: the card's top, reference px. It lives in the centre lane under the clock (it takes the lane at Recap
        /// priority, so a banner waits and the job line steps back). It used to sit in the lower lane, where the respawn
        /// class tiles drew straight over it.
        /// </summary>
        public const float TopY = HudZones.CalloutTop + (HudZones.CalloutH - CardH) * 0.5f;

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
        Text _lookTitle;   // b15 profile: the killer's mastery title (cosmetic)
        Text _killerState;
        int _killer = -1, _killerHp, _killerMax, _assistKey = -1;
        /// <summary>The killer / assist line as shown (tests).</summary>
        public string KillerState => _killerState != null ? _killerState.text : string.Empty;
        float _age;
        int _tier = 2;

        void Awake() => Build();
        void OnDisable() => Unsubscribe();

        public void Build()
        {
            if (Canvas != null) return;
            KillWords.Init(null);
            Canvas = FeedUi.MakeCanvas(transform, "Death Recap Canvas", 12);
            _card = FeedUi.Anchored(Canvas.transform, "Death Recap", new Vector2(0.5f, 1f), 0f, -TopY, CardW, CardH);
            _group = _card.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;
            MenuUi.Panel(_card, "Panel", 0f, 0f, CardW, CardH);
            _frame = MenuUi.Shape(_card, "Portrait Frame", ShapeKind.Chamfer, 18f, 18f, 120f, 120f, CMTokens.VoidNavy.Color, 18f);
            _portrait = HudIcon.Make(_frame.transform, "Portrait", 7f, 7f, 106f, 106f);
            _line = MenuUi.Label(_card, "Line", CMTokens.Ui, UiScale.TypeHeading, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 160f, 22f, 640f, 44f, "", 3f);
            _lookTitle = MenuUi.Fit(MenuUi.Label(_card, "Look Title", CMTokens.Display, UiScale.TypeMin, CMTokens.ToolBrass.Color, TextAnchor.MiddleRight, 560f, 62f, 240f, 18f, "", 2f), 12);
            // player-experience lane D (P4): the killer's class and the health he had left, and who assisted, under the line
            _killerState = MenuUi.Fit(MenuUi.Label(_card, "Killer State", CMTokens.Ui, UiScale.TypeMin, CMTokens.WarmCream.Color, TextAnchor.MiddleLeft, 160f, 60f, 400f, 20f, "", 2f), 12);
            _tipLabel = MenuUi.Label(_card, "Tip Label", CMTokens.Display, UiScale.TypeSmall, CMTokens.ToolBrass.Color, TextAnchor.UpperLeft, 160f, 80f, 60f, 26f, "TIP", 2f);
            _tip = MenuUi.Fit(MenuUi.Label(_card, "Tip", CMTokens.Ui, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.UpperLeft, 220f, 78f, 580f, 64f, "", 2f), 21);
            _tip.horizontalOverflow = HorizontalWrapMode.Wrap;
            // best fit needs bounded overflow: a long name ("Demolitions Expert") shrinks to fit one line
            _line.resizeTextMinSize = 22;
            _line.resizeTextMaxSize = 32;
            MenuUi.Contain(_line, 1);   // hud-containment-1
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
            if (CriticalMass.Runtime.Flow.GameFlow.MenuIdle) return;   // batch 4: hidden behind a menu over a backdrop match
            int tier = FeedUi.Refresh(Canvas);
            if (tier != _tier) { _tier = tier; _portrait.SetTier(tier); }
            bool hide = _log == null || _log.Suppressed;
            if (Canvas.enabled == hide) Canvas.enabled = !hide;
            if (!IsOpen) return;
            if (!hide) { HudZones.Hold(HudZone.Callout, HudPriority.Recap); HudZones.HoldFullPanel(); }
            World w = _log != null && _log.Driver != null ? _log.Driver.World : null;
            int local = _log != null ? _log.LocalActor : HudBinder.LocalActor;
            if (w != null && (w.Match.Phase == MatchPhase.Ended || (local < w.ActorCount && w.Actors[local].Alive))) { Hide(); return; }
            Animate(_log != null && _log.Paused ? 0f : Time.unscaledDeltaTime);
            if (_age < 0.6f && w != null && _killer >= 0 && _killer < w.ActorCount) RefreshKillerState(w.Actors[_killer].Cls);   // the HUD's assist tracker reads the death this frame too
        }

        /// <summary>"WARRIOR: 42/115 HP LEFT   ASSIST: JUNO, FARMER" (rebuilt only when the assisters change).</summary>
        void RefreshKillerState(ClassId killerClass)
        {
            if (_killerState == null) return;
            CriticalMass.Runtime.Hud.Player.AssistTracker t = CriticalMass.Runtime.Hud.Player.PlayerHud.Current != null ? CriticalMass.Runtime.Hud.Player.PlayerHud.Current.Assists : null;
            int n = t != null ? t.RecapCount : 0;
            int key = n;
            for (int i = 0; i < n; i++) key = key * 37 + t.RecapAt(i) + 1;
            if (key == _assistKey) return;
            _assistKey = key;
            HudText.Set(_killerState, KillerStateLine(_killer, killerClass, _killerHp, _killerMax, t, n));
        }

        /// <summary>The killer / assist line (pure apart from the bot names; tests).</summary>
        public static string KillerStateLine(int killer, ClassId killerClass, int hp, int maxHp, CriticalMass.Runtime.Hud.Player.AssistTracker t, int n)
        {
            var sb = new System.Text.StringBuilder(96);
            if (killer >= 0)
            {
                sb.Append(KillWords.ClassUpper[Mathf.Clamp((int)killerClass, 0, KillWords.ClassUpper.Length - 1)]);
                if (maxHp > 0) sb.Append(": ").Append(hp).Append('/').Append(maxHp).Append(" HP LEFT");
            }
            for (int i = 0; i < n; i++)
            {
                int a = t.RecapAt(i);
                if (a == killer) continue;
                sb.Append(i == 0 ? (sb.Length > 0 ? "   ASSIST: " : "ASSIST: ") : ", ");
                sb.Append(CriticalMass.Runtime.View.Mechanics.DepthNames.OrFallback(a, "FIGHTER"));
            }
            return sb.ToString();
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
            // b15 profile: a human killer's frame ring and title (cosmetic look; bots and the Kaiju show none)
            CriticalMass.Net.CosmeticLook look = CriticalMass.Runtime.Progression.LookRegistry.For(e.Other);
            CriticalMass.Runtime.Progression.LookView.Ring(_frame, e.Other >= 0 ? look.Frame : 0, CMTokens.OutlinePx, CMTokens.VoidNavy.Color);
            HudText.Set(_lookTitle, e.Other >= 0 && look.Title > 0 ? CriticalMass.Runtime.Progression.MetaWords.Title(e.OtherClass, look.Title) : string.Empty);
            World kw = _log != null && _log.Driver != null ? _log.Driver.World : null;
            _killer = e.Other;
            _killerHp = _killerMax = 0;
            if (kw != null && e.Other >= 0 && e.Other < kw.ActorCount)
            {
                _killerHp = Mathf.CeilToInt((float)System.Math.Max(0, kw.Actors[e.Other].Hp));
                _killerMax = Mathf.CeilToInt((float)kw.Actors[e.Other].MaxHp);
            }
            _assistKey = -1;
            RefreshKillerState(e.OtherClass);
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
