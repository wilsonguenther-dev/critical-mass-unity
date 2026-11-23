using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// The one pool of short words shown WHERE something happened (ActionTags: blocked / interrupted / refused; WorldPops:
    /// objective credit). A word is a pill (Panel Navy 0.92, 2 px dark outline, 2 px rim in the word's family colour) with
    /// one line of type, anchored to an actor (it follows the body) or to a world point; it rises 0.45 u over its life
    /// (no rise under REDUCE MOTION), shows at full strength at once and fades over the last 0.25 s. Words over the same anchor stack upward
    /// by a pill's height so two never overlap. Duplicates are rate limited by key (one identical word a second).
    /// Sizes, px at 1080p: pill height 34, type 22 (strong) or 18 (refusal), width = text + 28. Pooled, no per-frame allocation.
    /// </summary>
    public sealed class WorldWords
    {
        public const float PillH = 34f, PadX = 14f, Rise = 0.45f, FadeOut = 0.25f, RepeatSeconds = 1f;
        public const int StrongSize = UiScale.TypeBody, SoftSize = UiScale.TypeSmall;

        sealed class Slot
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public VectorShape Plate;
            public Text Text;
            public float Age, Life;
            public int Actor;
            public Vector3 Point;
            public float Lift;
            public int Stack;
            public bool Row;
            public int Key;
            public bool Active;
            public int Rank;   // mv-6: the word's priority (Rank) while MovesCap is on
        }

        readonly Slot[] _slots;
        const int Keys = 24;
        readonly int[] _key = new int[Keys];
        readonly float[] _keyAt = new float[Keys];
        int _keyNext;

        public WorldWords(RectTransform world, string name, int count)
        {
            _slots = new Slot[count];
            for (int i = 0; i < count; i++)
            {
                var s = new Slot();
                s.Rt = PxUi.Centred(world, name + " " + i, 200f, PillH);
                s.Group = PxUi.Group(s.Rt);
                s.Plate = PxUi.Shape(s.Rt, "Pill", ShapeKind.RoundRect, 0f, 0f, 200f, PillH, CMTokens.WithAlpha(CMTokens.PanelNavy.Color, 0.92f), -1f, 2f, 2f);
                s.Text = PxUi.Label(s.Rt, "Word", CMTokens.Ui, StrongSize, PxPalette.Cream, TextAnchor.MiddleCenter, 0f, 0f, 200f, PillH, 2f);
                s.Rt.gameObject.SetActive(false);
                _slots[i] = s;
            }
        }

        /// <summary>True (and remembered) when key has not been shown in the last RepeatSeconds (pure but for the clock).</summary>
        public bool Allow(int key, float now) => Allow(key, now, RepeatSeconds);

        public bool Allow(int key, float now, float repeat)
        {
            for (int i = 0; i < Keys; i++)
                if (_key[i] == key && _keyAt[i] > 0f && now - _keyAt[i] < repeat) return false;
            _key[_keyNext] = key;
            _keyAt[_keyNext] = now <= 0f ? 1e-4f : now;
            _keyNext = (_keyNext + 1) % Keys;
            return true;
        }

        public int ActiveCount
        {
            get { int n = 0; for (int i = 0; i < _slots.Length; i++) if (_slots[i].Active) n++; return n; }
        }

        /// <summary>Shows text; returns the label (the ledger's source) or null when rate limited.</summary>
        public Text Show(string text, int key, int actor, Vector3 point, float lift, Color ink, Color rim, bool strong, float seconds)
            => Show(text, key, actor, point, lift, ink, rim, strong, seconds, RepeatSeconds);

        /// <summary>As Show, with its own duplicate window. A word on a body sits in that body's word row above the name tag
        /// (UnitStack), whatever lift is passed; a word on a point keeps its lift.</summary>
        public Text Show(string text, int key, int actor, Vector3 point, float lift, Color ink, Color rim, bool strong, float seconds, float repeat)
        {
            if (string.IsNullOrEmpty(text) || !Allow(key, Time.unscaledTime, repeat)) return null;
            Slot s = null;
            float oldest = -1f;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (!_slots[i].Active) { s = _slots[i]; break; }
                if (_slots[i].Age / _slots[i].Life > oldest) { oldest = _slots[i].Age / _slots[i].Life; s = _slots[i]; }
            }
            int stack = 0;
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot o = _slots[i];
                if (o == s || !o.Active || o.Age > 0.6f * o.Life) continue;
                bool same = actor >= 0 ? o.Actor == actor : o.Actor < 0 && (o.Point - point).sqrMagnitude < 1.5f;
                if (same && o.Stack >= stack) stack = o.Stack + 1;
            }
            int rank = Rank(text);
            if (MovesCap && !Capped(s, actor, point, rank)) { NextSize = 0; return null; }   // mv-6 (sets s.Stack)
            s.Rank = rank;
            s.Active = true;
            s.Age = 0f;
            s.Life = seconds;
            s.Actor = actor;
            s.Point = point;
            s.Row = actor >= 0;
            s.Lift = s.Row ? HealthBars.Lift : lift;
            if (!MovesCap) s.Stack = Mathf.Min(stack, 3);
            int size = NextSize > 0 ? NextSize : strong ? StrongSize : SoftSize;   // review fix round 3: PERFECT a little larger
            NextSize = 0;
            s.Key = key;
            s.Text.fontSize = size;
            s.Text.color = ink;
            ContainedText.Of(s.Text)?.Configure(size, Mathf.Min(size, 14), 1);
            float w = Mathf.Clamp(TextFit.WidthOf(s.Text, text, size) + 2f * PadX, 80f, 520f);
            s.Rt.sizeDelta = new Vector2(w, PillH);
            s.Plate.rectTransform.sizeDelta = new Vector2(w, PillH);
            s.Plate.rectTransform.anchoredPosition = new Vector2(w * 0.5f, -PillH * 0.5f);
            s.Text.rectTransform.sizeDelta = new Vector2(w, PillH);
            s.Text.rectTransform.anchoredPosition = new Vector2(w * 0.5f, -PillH * 0.5f);
            s.Plate.SetRim(rim);
            HudText.Set(s.Text, text);
            ContainedText.Of(s.Text)?.SetSource(text);
            s.Group.alpha = 1f;   // visible the frame it is raised (the ledger reports it then)
            if (!s.Rt.gameObject.activeSelf) s.Rt.gameObject.SetActive(true);
            return s.Text;
        }

        /// <summary>
        /// Class moves readability (Editor review item 2): BLOCKED / STUNNED / PERFECT / SECOND WIND used to pile up on one
        /// body. While MovesCap is on (MovesViews sets it each frame while World.Moves exists; off = today's stacking exactly),
        /// at most MaxPerAnchor words show on one body (or one point): a new word pushes out the lowest-ranked one (ties: the
        /// oldest), a new word ranked below both that already show is not shown, and the two are staggered one row apart with
        /// the higher rank on top (ties: the newest).
        /// </summary>
        public static bool MovesCap;
        public const int MaxPerAnchor = 2;

        /// <summary>Pure: a word's rank (higher shows first): PARRY / PARRIED 5, GUARD BREAK / GUARD BROKEN 4, FINISHER 3,
        /// BLOCKED 2, anything else 1.</summary>
        public static int Rank(string text)
        {
            switch (text)
            {
                case "PARRY": case "PARRIED": return 5;
                case "GUARD BREAK": case "GUARD BROKEN": return 4;
                case "FINISHER": return 3;
                case "BLOCKED": return 2;
                default: return 1;
            }
        }

        readonly Slot[] _near = new Slot[8];

        /// <summary>MovesCap: makes room for a word of rank on this anchor; false = the word is outranked and not shown.
        /// Sets s's and the kept neighbour's Stack (higher rank on top).</summary>
        bool Capped(Slot s, int actor, Vector3 point, int rank)
        {
            int n = 0;
            for (int i = 0; i < _slots.Length && n < _near.Length; i++)
            {
                Slot o = _slots[i];
                if (o == s || !o.Active) continue;
                bool same = actor >= 0 ? o.Actor == actor : o.Actor < 0 && (o.Point - point).sqrMagnitude < 1.5f;
                if (same) _near[n++] = o;
            }
            while (n >= MaxPerAnchor)
            {
                int low = 0;
                for (int k = 1; k < n; k++)
                    if (_near[k].Rank < _near[low].Rank || (_near[k].Rank == _near[low].Rank && _near[k].Age > _near[low].Age)) low = k;
                if (_near[low].Rank > rank) return false;   // both showing outrank the new word
                _near[low].Active = false;
                PxUi.Show(_near[low].Rt, false);
                _near[low] = _near[n - 1];
                n--;
            }
            if (n == 1)
            {
                bool newOnTop = rank >= _near[0].Rank;
                s.Stack = newOnTop ? 1 : 0;
                _near[0].Stack = newOnTop ? 0 : 1;
            }
            else s.Stack = 0;
            for (int k = 0; k < _near.Length; k++) _near[k] = null;
            return true;
        }

        /// <summary>One-shot font size for the next Show (0 = the strong / soft size).</summary>
        public int NextSize;

        /// <summary>Review fix round 3: takes down the words shown under key (BLOCKED wins over a TOO CLOSE on the same hit).</summary>
        public void Hide(int key)
        {
            for (int i = 0; i < _slots.Length; i++)
                if (_slots[i].Active && _slots[i].Key == key) { _slots[i].Active = false; PxUi.Show(_slots[i].Rt, false); }
        }

        public void Clear()
        {
            for (int i = 0; i < _slots.Length; i++) { _slots[i].Active = false; PxUi.Show(_slots[i].Rt, false); }
            for (int i = 0; i < Keys; i++) _keyAt[i] = 0f;
        }

        public void Tick(World w, double alpha, UnityEngine.Camera cam, float scale, float dt)
        {
            bool calm = CriticalMass.Runtime.Menu.GameSettings.ReduceMotion;
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot s = _slots[i];
                if (!s.Active) continue;
                s.Age += dt;
                if (s.Age >= s.Life) { s.Active = false; PxUi.Show(s.Rt, false); continue; }
                float t = s.Age / s.Life;
                if (s.Row && s.Actor < w.ActorCount)
                {
                    // the body's word row: above the name tag, rows stacking up, no rise (UnitStack)
                    Vector3 head = PxProject.Actor(w, s.Actor, alpha, HealthBars.Lift);
                    if (!PxProject.Screen(cam, head, 0f, out Vector2 hp)) { PxUi.Alpha(s.Group, 0f); continue; }
                    float tag = PxProject.ThirdPerson ? CriticalMass.Runtime.Camera.TagLens.Scale((head - cam.transform.position).magnitude) : 1f;
                    s.Rt.anchoredPosition = hp / scale + new Vector2(0f, UnitStack.TagRowUp(tag, s.Stack));
                }
                else
                {
                    float lift = s.Lift + (calm ? 0f : Rise * t);
                    Vector3 at = s.Point + Vector3.up * (lift - s.Lift);
                    if (!PxProject.Screen(cam, at, 0f, out Vector2 p)) { PxUi.Alpha(s.Group, 0f); continue; }
                    s.Rt.anchoredPosition = p / scale + new Vector2(0f, s.Stack * (PillH + 4f));
                }
                float a = Mathf.Clamp01((s.Life - s.Age) / FadeOut);
                PxUi.Alpha(s.Group, a);
            }
        }
    }
}
