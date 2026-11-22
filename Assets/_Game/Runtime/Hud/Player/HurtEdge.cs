using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan item 10 (P4): the personal hurt tier and the low-health warning. (b) OWN-HURT VIGNETTE: a short edge pulse in
    /// the hurt colour on every hit on me, peak alpha by damage share (0.22..0.7), gone in 0.35 s; inside the shared flash
    /// budget (PxFlashBudget, at most 3 onsets a second: a pulse over budget joins the one showing instead of re-flashing).
    /// (c) LOW HEALTH: below LowEnter of max HP a slow edge pulse at LowHz (1.2 Hz), the player card's bar turns the hurt
    /// colour and pulses, and the word LOW HEALTH shows over the health plate once per entry for 2.5 s; it all ends above
    /// LowExit (hysteresis) or on death. REDUCE FLASHING (on by default): the hurt peak is 0.75 x and the low-health edge
    /// holds a steady glow instead of pulsing: calmer, never nothing. Edge bands, px at 1080p: hurt 22% of the half screen
    /// (about 119 px top/bottom, 211 px sides), low 16%.
    /// </summary>
    public sealed class HurtEdge
    {
        public const float LowEnter = 0.30f, LowExit = 0.35f, LowHz = 1.2f, HurtSeconds = 0.35f, WordSeconds = 2.5f;
        public const float HurtMin = 0.22f, HurtMax = 0.7f, LowMin = 0.16f, LowMax = 0.42f, LowSteady = 0.3f;
        public const string LowWord = "LOW HEALTH";

        /// <summary>The low-health state after one step (pure; tests): enter below LowEnter, leave at or above LowExit.</summary>
        public static bool LowState(bool wasLow, float hp01, bool alive) => alive && (wasLow ? hp01 < LowExit : hp01 < LowEnter);

        /// <summary>Peak alpha of the hurt pulse for a damage share (pure; tests).</summary>
        public static float HurtPeak(float share, bool reduceFlashing) => Mathf.Clamp(HurtMin + share * 1.8f, HurtMin, HurtMax) * (reduceFlashing ? 0.75f : 1f);

        readonly PxEdgeGlow _hurt, _low;
        readonly CanvasGroup _hurtGroup, _lowGroup;
        readonly Text _word;
        float _hurtA, _hurtRate, _lowT, _wordAge = WordSeconds, _lastCycle = -1f;
        bool _isLow;

        /// <summary>True while the low-health warning is on (GameHud tints the card's bar).</summary>
        public bool Low => _isLow;
        /// <summary>The card bar colour while low (pulsing unless REDUCE FLASHING).</summary>
        public Color LowBarColour { get; private set; } = PxPalette.Hurt;
        public PxEdgeGlow HurtGlow => _hurt;
        public Text Word => _word;

        public HurtEdge(RectTransform root, RectTransform playerCard)
        {
            _hurt = Edge(root, "Hurt Vignette", 0.22f, out _hurtGroup);
            _low = Edge(root, "Low Health Edge", 0.16f, out _lowGroup);
            if (playerCard != null)
            {
                // over the health plate (it starts at y 22 of the card), clear of the ability names (x >= 392)
                _word = PxUi.Label(playerCard, "Low Health Word", CMTokens.Display, UiScale.TypeSmall, PxPalette.Hurt, TextAnchor.MiddleLeft, 136f, -2f, 240f, 24f, 2.5f);
                _word.text = LowWord;
                _word.gameObject.SetActive(false);
            }
        }

        static PxEdgeGlow Edge(RectTransform root, string name, float band, out CanvasGroup g)
        {
            RectTransform rt = PxUi.Rt(root, name);
            CriticalMass.Runtime.Menu.MenuUi.Stretch(rt);
            rt.SetSiblingIndex(1);   // over the world and the death wash, under every HUD cluster
            g = PxUi.Group(rt);
            g.alpha = 0f;
            PxEdgeGlow e = rt.gameObject.AddComponent<PxEdgeGlow>();
            e.raycastTarget = false;
            e.Band = band;
            e.color = PxPalette.Hurt;
            rt.gameObject.SetActive(false);
            return e;
        }

        /// <summary>A hit on me of share = damage / max HP.</summary>
        public void OnHurt(int me, float share)
        {
            bool calm = CriticalMass.Runtime.Menu.GameSettings.ReduceFlashing;
            float peak = HurtPeak(share, calm);
            float now = Time.unscaledTime;
            if (_hurtA <= 0.02f || PxFlashBudget.TryOnset(now)) _hurtA = Mathf.Max(_hurtA, peak);
            else _hurtA = Mathf.Max(_hurtA, peak * 0.6f);   // over budget: the showing pulse holds, no new flash
            _hurtRate = _hurtA / HurtSeconds;
            if (!_hurt.gameObject.activeSelf) _hurt.gameObject.SetActive(true);
            PxUi.Alpha(_hurtGroup, _hurtA);
            PlayerFeedback.Report(FeedbackChannel.OwnHurtVignette, me, _hurtA, _hurt);
        }

        public void Clear()
        {
            _hurtA = 0f;
            _isLow = false;
            PxUi.Show(_hurt, false);
            PxUi.Show(_low, false);
            if (_word != null) PxUi.Show(_word, false);
        }

        public void Tick(int me, float hp01, bool alive, float dt)
        {
            if (_hurtA > 0f)
            {
                _hurtA = Mathf.Max(0f, _hurtA - _hurtRate * dt);
                PxUi.Alpha(_hurtGroup, _hurtA);
                if (_hurtA <= 0f) PxUi.Show(_hurt, false);
            }
            bool was = _isLow;
            _isLow = LowState(was, hp01, alive);
            bool calm = CriticalMass.Runtime.Menu.GameSettings.ReduceFlashing;
            if (_isLow && !was)
            {
                _lowT = 0f;
                _wordAge = 0f;
                PxUi.Show(_low, true);
                if (_word != null) PxUi.Show(_word, true);
                PlayerFeedback.Report(FeedbackChannel.LowHealthCue, me, hp01, _low);
            }
            if (!_isLow)
            {
                if (was) { PxUi.Show(_low, false); if (_word != null) PxUi.Show(_word, false); }
                return;
            }
            _lowT += dt;
            float phase = _lowT * LowHz;
            float wave = 0.5f - 0.5f * Mathf.Cos(phase * Mathf.PI * 2f);
            if (!calm && Mathf.Floor(phase) != _lastCycle) { _lastCycle = Mathf.Floor(phase); PxFlashBudget.TryOnset(Time.unscaledTime); }
            PxUi.Alpha(_lowGroup, calm ? LowSteady : Mathf.Lerp(LowMin, LowMax, wave));
            LowBarColour = calm ? PxPalette.Hurt : Color.Lerp(PxPalette.Hurt, PxPalette.StrikeInk, wave * 0.6f);
            if (_word != null && _wordAge < WordSeconds)
            {
                _wordAge += dt;
                if (_wordAge >= WordSeconds) PxUi.Show(_word, false);
            }
        }
    }
}
