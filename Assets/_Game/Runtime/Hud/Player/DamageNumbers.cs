using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan item 29 / hit-feel P1.7 and Q2: damage numbers that read, moved out of GameHud. Numbers for MY hits on bodies
    /// (cream; heavy tier in the light damage ink and 1.35 x; critical in the damage magenta and 1.6 x), on the Kaiju and
    /// on walls (structure ink), and green heal numbers for the mends and pulses a Healer gives and for heals I receive
    /// (summed per target over HealWindow so the fountain's per-tick heal reads as one number). Consecutive numbers on the
    /// same target stack upward and alternate sides, so they never overlap. Honours the DAMAGE NUMBERS setting (off = none,
    /// and nothing is reported). Sizes, px at 1080p: base 30 in the top-down view, 34 in the third-person view (a victim
    /// there is about 260 px tall; the floor of 28 is held by every kind), heavy 41 / 46, critical 48 / 54, heal 28 / 32.
    /// Rises 0.6 u over 0.7 s (heavy 0.85 s); no rise under REDUCE MOTION. Pooled (16), no per-frame allocation.
    /// </summary>
    public sealed class DamageNumbers
    {
        public const int Pool = 16;
        public const int BaseTop = 30, BaseThird = 34, MinSize = 28;
        public const float HeavyScale = 1.35f, CritScale = 1.6f, HealScale = 0.94f, Life = 0.7f, HeavyLife = 0.85f, Rise = 0.6f, StackWindow = 0.45f;
        public const float HealWindow = 0.35f, NumberLift = 2.2f;
        public enum Kind { Hit, Heavy, Crit, Heal, Structure, Kaiju, Critical }
        public const float CriticalScale = 1.9f, Punch = 0.16f, PunchScale = 1.45f, PairWindow = 0.12f;

        /// <summary>Font size of a number of kind k in the given view (pure; tests).</summary>
        public static int SizeFor(Kind k, bool thirdPerson)
        {
            float b = thirdPerson ? BaseThird : BaseTop;
            float s = k == Kind.Heavy ? b * HeavyScale : k == Kind.Crit ? b * CritScale : k == Kind.Critical ? b * CriticalScale : k == Kind.Heal || k == Kind.Structure ? b * HealScale : b;
            return Mathf.Max(MinSize, Mathf.RoundToInt(s));
        }

        public static Color ColourFor(Kind k)
        {
            switch (k)
            {
                case Kind.Heavy: return PxPalette.StrikeInk;
                case Kind.Crit: return PxPalette.Strike;
                case Kind.Critical: return PxPalette.CritSkill;
                case Kind.Heal: return PxPalette.Heal;
                case Kind.Structure: return PxPalette.Structure;
                default: return PxPalette.Cream;
            }
        }

        sealed class Num
        {
            public Text Text;
            public CanvasGroup Group;
            public float Age, Life, Punch;
            public Kind Kind;
            public int Actor;
            public Vector3 Point;
            public Vector2 Offset;
            public int Stack;
            public bool Active;
            public int Value;   // mv-6 readability-3 item 2: the shown amount (merged hits add into it)
        }

        /// <summary>mv-6 readability-3 item 2: under the class moves (WorldWords.MovesCap), hits on the same body within
        /// MergeWindow become ONE number (summed, restarted, the stronger kind's colour) instead of stacking.</summary>
        public const float MergeWindow = 0.25f;

        /// <summary>Pure: the kind a merged number shows (the stronger of the two: Critical &gt; Crit &gt; Heavy &gt; the rest).</summary>
        public static Kind Stronger(Kind a, Kind b)
        {
            int Ra(Kind k) => k == Kind.Critical ? 4 : k == Kind.Crit ? 3 : k == Kind.Heavy ? 2 : 1;
            return Ra(b) > Ra(a) ? b : a;
        }

        readonly Num[] _nums = new Num[Pool];
        int _next;
        readonly float[] _healAcc = new float[HealthBars.MaxBars];
        readonly float[] _healAge = new float[HealthBars.MaxBars];
        readonly float[] _critAt = new float[HealthBars.MaxBars];
        public int CriticalsShown { get; private set; }

        /// <summary>
        /// Lane S1's CriticalHit for my hit on victim (the same tick as its Hit, either order): the newest number on victim
        /// becomes the critical style (1.9 x, the hot pink, a 0.16 s scale punch); if its Hit has not been read yet the
        /// next number on victim inside PairWindow is. Returns true when a number was upgraded now.
        /// </summary>
        public bool Critical(int victim, float bonus)
        {
            if (!CriticalMass.Runtime.Menu.GameSettings.DamageNumbers) return false;
            Num best = null;
            for (int i = 0; i < Pool; i++)
            {
                Num o = _nums[i];
                if (o.Active && o.Actor == victim && o.Kind != Kind.Heal && o.Age <= PairWindow && (best == null || o.Age < best.Age)) best = o;
            }
            if (best == null)
            {
                if (victim >= 0 && victim < _critAt.Length) _critAt[victim] = Mathf.Max(1e-4f, Time.unscaledTime);
                return false;
            }
            MakeCritical(best);
            return true;
        }

        void MakeCritical(Num n)
        {
            n.Kind = Kind.Critical;
            int size = SizeFor(Kind.Critical, PxProject.ThirdPerson);
            if (n.Text.fontSize != size) { n.Text.fontSize = size; ContainedText.Of(n.Text)?.Configure(size, MinSize, 1); }
            n.Text.color = ColourFor(Kind.Critical);
            n.Life = HeavyLife + 0.15f;
            n.Punch = Punch;
            CriticalsShown++;
            PlayerFeedback.Report(FeedbackChannel.DamageNumber, n.Actor, 1f, n.Text, 99);
        }

        public int ActiveCount
        {
            get { int n = 0; for (int i = 0; i < Pool; i++) if (_nums[i].Active) n++; return n; }
        }

        public DamageNumbers(RectTransform world)
        {
            for (int i = 0; i < Pool; i++)
            {
                var n = new Num();
                n.Text = HudText.Make(world, "Damage " + i, CMTokens.Mono, BaseThird, PxPalette.Cream, TextAnchor.MiddleCenter, 3f);
                RectTransform rt = n.Text.rectTransform;
                rt.anchorMin = rt.anchorMax = Vector2.zero;
                rt.pivot = new Vector2(0f, 0.5f);   // review fix: left-middle at UnitStack.NumberLeftMid (to the side of the name column)
                rt.sizeDelta = new Vector2(220f, 72f);
                n.Text.alignment = TextAnchor.MiddleLeft;
                n.Group = n.Text.gameObject.AddComponent<CanvasGroup>();
                n.Group.interactable = false;
                n.Group.blocksRaycasts = false;
                n.Text.gameObject.SetActive(false);
                _nums[i] = n;
            }
            for (int i = 0; i < _healAge.Length; i++) _healAge[i] = -1f;
        }

        /// <summary>Shows amount at a body (actor &gt;= 0) or a point; false when the setting is off or amount rounds to 0.</summary>
        public bool Add(int actor, Vector3 point, float amount, Kind kind)
        {
            int v = Mathf.RoundToInt(amount);
            if (v <= 0 || !CriticalMass.Runtime.Menu.GameSettings.DamageNumbers) return false;
            if (WorldWords.MovesCap && kind != Kind.Heal && actor >= 0)
            {
                for (int i = 0; i < Pool; i++)
                {
                    Num o = _nums[i];
                    if (!o.Active || o.Actor != actor || o.Kind == Kind.Heal || o.Age > MergeWindow) continue;
                    o.Value += v;
                    o.Age = 0f;
                    o.Punch = 0f;
                    Kind k = Stronger(o.Kind, kind);
                    if (k != o.Kind)
                    {
                        o.Kind = k;
                        o.Life = k == Kind.Heavy || k == Kind.Crit ? HeavyLife : Life;
                        int sz = SizeFor(k, PxProject.ThirdPerson);
                        if (o.Text.fontSize != sz) { o.Text.fontSize = sz; ContainedText.Of(o.Text)?.Configure(sz, MinSize, 1); }
                        o.Text.color = ColourFor(k);
                    }
                    HudText.Set(o.Text, NumberCache.Int(o.Value));
                    o.Group.alpha = 1f;
                    PlayerFeedback.Report(FeedbackChannel.DamageNumber, actor, v, o.Text);
                    return true;
                }
            }
            int stack = 0;
            for (int i = 0; i < Pool; i++)
            {
                Num o = _nums[i];
                if (!o.Active || o.Age > StackWindow) continue;
                bool same = actor >= 0 ? o.Actor == actor : o.Actor < 0 && (o.Point - point).sqrMagnitude < 1.5f;
                if (same) stack++;
            }
            Num n = _nums[_next];
            _next = (_next + 1) % Pool;
            int size = SizeFor(kind, PxProject.ThirdPerson);
            n.Active = true;
            n.Age = 0f;
            n.Life = kind == Kind.Heavy || kind == Kind.Crit ? HeavyLife : Life;
            n.Actor = actor;
            n.Point = point;
            n.Kind = kind;
            n.Punch = 0f;
            n.Text.rectTransform.localScale = Vector3.one;
            n.Stack = Mathf.Min(stack, 2);
            n.Offset = Vector2.zero;
            n.Value = v;
            if (n.Text.fontSize != size) { n.Text.fontSize = size; ContainedText.Of(n.Text)?.Configure(size, MinSize, 1); }
            n.Text.color = ColourFor(kind);
            HudText.Set(n.Text, kind == Kind.Heal ? HealWord(v) : NumberCache.Int(v));
            n.Group.alpha = 1f;
            if (!n.Text.gameObject.activeSelf) n.Text.gameObject.SetActive(true);
            PlayerFeedback.Report(kind == Kind.Heal ? FeedbackChannel.HealNumber : FeedbackChannel.DamageNumber, actor, v, n.Text);
            if (kind != Kind.Heal && actor >= 0 && actor < _critAt.Length && _critAt[actor] > 0f)
            {
                if (Time.unscaledTime - _critAt[actor] <= PairWindow) MakeCritical(n);
                _critAt[actor] = 0f;
            }
            return true;
        }

        static readonly string[] HealWords = new string[1000];
        static string HealWord(int v)
        {
            v = Mathf.Clamp(v, 0, HealWords.Length - 1);
            return HealWords[v] ?? (HealWords[v] = "+" + v.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        /// <summary>Heal on actor (a heal I gave or one I received): summed over HealWindow, then shown as one number.</summary>
        public void AddHeal(int actor, float amount)
        {
            if (actor < 0 || actor >= _healAcc.Length || amount <= 0f) return;
            if (_healAge[actor] < 0f) _healAge[actor] = 0f;
            _healAcc[actor] += amount;
        }

        public void Clear()
        {
            for (int i = 0; i < Pool; i++) { _nums[i].Active = false; PxUi.Show(_nums[i].Text, false); }
            for (int i = 0; i < _healAcc.Length; i++) { _healAcc[i] = 0f; _healAge[i] = -1f; _critAt[i] = 0f; }
        }

        /// <summary>My fighter's screen rect (ref px) this frame, or an empty rect (review fix round 2: numbers avoid it).</summary>
        static Rect MyRect(World w, double alpha, UnityEngine.Camera cam, float scale)
        {
            int me = HudBinder.LocalActor;
            if (cam == null || me < 0 || me >= w.ActorCount || !w.Actors[me].Alive) return default;
            Vector3 feet = PxProject.Actor(w, me, alpha, 0f), head = PxProject.Actor(w, me, alpha, HealthBars.Lift);
            if (!PxProject.Screen(cam, feet, 0f, out Vector2 fp) || !PxProject.Screen(cam, head, 0f, out Vector2 hp)) return default;
            float tag = PxProject.ThirdPerson ? CriticalMass.Runtime.Camera.TagLens.Scale((head - cam.transform.position).magnitude) : 1f;
            float halfBody = Mathf.Abs(hp.y - fp.y) / scale * 0.3f;   // a fighter is about 0.6 of his height wide on screen
            return UnitStack.MineRect(fp / scale, hp / scale, tag, halfBody);
        }

        public void Tick(World w, double alpha, UnityEngine.Camera cam, float scale, float dt)
        {
            for (int i = 0; i < _healAcc.Length; i++)
            {
                if (_healAge[i] < 0f) continue;
                _healAge[i] += dt;
                if (_healAge[i] < HealWindow) continue;
                if (i < w.ActorCount && w.Actors[i].Alive) Add(i, PxProject.Actor(w, i, alpha, NumberLift), _healAcc[i], Kind.Heal);
                _healAcc[i] = 0f;
                _healAge[i] = -1f;
            }
            bool calm = CriticalMass.Runtime.Menu.GameSettings.ReduceMotion;
            Rect mine = MyRect(w, alpha, cam, scale);
            _placedN = 0;   // mv-6 readability-4 item 3
            for (int k = 0; k < Pool; k++)
            {
                Num n = _nums[k];
                if (!n.Active) continue;
                n.Age += dt;
                if (n.Age >= n.Life) { n.Active = false; n.Text.gameObject.SetActive(false); continue; }
                float t = n.Age / n.Life;
                float up = calm ? 0f : Rise * t;
                if (n.Actor >= 0 && n.Actor < w.ActorCount && w.Actors[n.Actor].Alive)
                {
                    // review fix round (defect 4): from the BODY (chest), to the side of the name / bar / word column,
                    // clamped under the word row (UnitStack): never over a name, a class, a bar or a word
                    Vector3 chest = PxProject.Actor(w, n.Actor, alpha, UnitStack.ChestLift);
                    Vector3 head = PxProject.Actor(w, n.Actor, alpha, HealthBars.Lift);
                    Vector3 risen = PxProject.Actor(w, n.Actor, alpha, UnitStack.ChestLift + up);
                    if (!PxProject.Screen(cam, chest, 0f, out Vector2 cp) || !PxProject.Screen(cam, head, 0f, out Vector2 hp) || !PxProject.Screen(cam, risen, 0f, out Vector2 rp))
                    { PxUi.Alpha(n.Group, 0f); continue; }
                    float tag = PxProject.ThirdPerson ? CriticalMass.Runtime.Camera.TagLens.Scale((head - cam.transform.position).magnitude) : 1f;
                    int sz = n.Text.fontSize;
                    Rect avoid = n.Actor == HudBinder.LocalActor ? default : mine;   // a number on me stays beside me
                    n.Text.rectTransform.anchoredPosition = UnitStack.NumberLeftMid(cp / scale, hp / scale, tag, sz, (rp.y - cp.y) / scale, n.Stack, avoid, Mathf.Max(1, n.Text.text.Length));
                }
                else
                {
                    Vector3 at = n.Point + Vector3.up * up;
                    if (!PxProject.Screen(cam, at, 0f, out Vector2 p)) { PxUi.Alpha(n.Group, 0f); continue; }
                    n.Text.rectTransform.anchoredPosition = p / scale + n.Offset;
                }
                if (n.Punch > 0f)
                {
                    n.Punch = calm ? 0f : Mathf.Max(0f, n.Punch - dt);
                    float ps = 1f + (PunchScale - 1f) * (n.Punch / Punch);
                    n.Text.rectTransform.localScale = new Vector3(ps, ps, 1f);
                }
                if (WorldWords.MovesCap) Fan(n);   // mv-6 readability-4 item 3: numbers on different bodies never overlap
                PxUi.Alpha(n.Group, t < 0.55f ? 1f : 1f - (t - 0.55f) / 0.45f);
            }
        }

        readonly Rect[] _placed = new Rect[Pool];
        int _placedN;

        /// <summary>mv-6 readability-4 item 3: a number that would cover one already placed this frame moves up past it.</summary>
        void Fan(Num n)
        {
            RectTransform rt = n.Text.rectTransform;
            Vector2 p = rt.anchoredPosition;
            int digits = Mathf.Max(1, n.Text.text.Length);
            int size = n.Text.fontSize;
            for (int pass = 0; pass < 6; pass++)
            {
                bool moved = false;
                Rect me = UnitStack.NumberRect(p, size, digits);
                for (int i = 0; i < _placedN; i++)
                {
                    if (!me.Overlaps(_placed[i])) continue;
                    p.y += _placed[i].yMax - me.yMin + 2f;
                    me = UnitStack.NumberRect(p, size, digits);
                    moved = true;
                }
                if (!moved) break;
            }
            if (_placedN < _placed.Length) _placed[_placedN++] = UnitStack.NumberRect(p, size, digits);
            rt.anchoredPosition = p;
        }
    }
}
