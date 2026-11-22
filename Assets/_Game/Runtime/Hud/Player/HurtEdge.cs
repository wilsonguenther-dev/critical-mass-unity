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
        public const float LowEnter = 0.30f, LowExit = 0.35f, LowHz = 0.8f, HurtSeconds = 0.25f, WordSeconds = 2.5f, WordPx = 28f;
        public const float HurtMin = 0.22f, HurtMax = 0.7f, LowMin = 0.34f, LowMax = 0.62f, LowSteady = 0.5f, SideFloor = 0.12f;
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
        public Color LowBarColour { get; private set; } = PxPalette.LowInk;
        public PxEdgeGlow HurtGlow => _hurt;
        public Text Word => _word;

        public HurtEdge(RectTransform root, RectTransform playerCard)
        {
            _hurt = Edge(root, "Hurt Vignette", 0.18f, out _hurtGroup);
            _low = Edge(root, "Low Health Edge", 0.30f, out _lowGroup);
            _low.color = PxPalette.LowVignette;   // review fix (defect 10): a dark heartbeat vignette, not the hurt magenta
            if (playerCard != null)
            {
                // over the health plate (it starts at y 22 of the card), clear of the ability names (x >= 392)
                // review fix (defect 10): 28 px (was ~16), orange, just above the health plate and under the tally line
                _word = PxUi.Label(playerCard, "Low Health Word", CMTokens.Display, (int)WordPx, PxPalette.LowInk, TextAnchor.MiddleLeft, 136f, -14f, 300f, 34f, 3f);
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

        /// <summary>Per-edge strength of a hurt flash from bearing (degrees clockwise from screen-up; pure; tests): the edge
        /// facing the hit is full, the opposite one SideFloor.</summary>
        public static Vector4 SidesFor(float bearing)
        {
            float r = bearing * Mathf.Deg2Rad, up = Mathf.Cos(r), right = Mathf.Sin(r);
            return new Vector4(Mathf.Max(SideFloor, -up), Mathf.Max(SideFloor, right), Mathf.Max(SideFloor, up), Mathf.Max(SideFloor, -right));
        }

        /// <summary>A heartbeat: two beats a cycle (lub-dub), 0..1 (pure; tests).</summary>
        public static float Heartbeat(float phase)
        {
            float f = phase - Mathf.Floor(phase);
            float a = Mathf.Exp(-Mathf.Pow((f - 0.08f) / 0.05f, 2f)), b = 0.7f * Mathf.Exp(-Mathf.Pow((f - 0.28f) / 0.06f, 2f));
            return Mathf.Clamp01(a + b);
        }

        /// <summary>Points the showing hurt flash at the side the hit came from (DamageDirection's last bearing).</summary>
        public void SetBearing(float bearing)
        {
            if (_hurtA <= 0f) return;
            Vector4 s = SidesFor(bearing);
            if ((s - _hurt.Sides).sqrMagnitude > 0.004f) { _hurt.Sides = s; _hurt.SetVerticesDirty(); }
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
            float wave = Heartbeat(phase);
            if (!calm && Mathf.Floor(phase) != _lastCycle) { _lastCycle = Mathf.Floor(phase); PxFlashBudget.TryOnset(Time.unscaledTime); }
            PxUi.Alpha(_lowGroup, calm ? LowSteady : Mathf.Lerp(LowMin, LowMax, wave));
            LowBarColour = PxPalette.LowInk;   // steady orange: the vignette carries the beat
            if (_word != null && _wordAge < WordSeconds)
            {
                _wordAge += dt;
                if (_wordAge >= WordSeconds) PxUi.Show(_word, false);
            }
        }
    }
}
