using CriticalMass.Runtime.Camera;
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
    /// Plan item 9 (session audit P3, hit-feel defect 7): a health bar over EVERY unit in view, always on, replacing the
    /// 4-heart rows that showed for 3 s after a hit. Enemies: chamfered bar in the damage family (PxPalette.Strike) with a
    /// dark outline; allies: rounded bar in their team colour. A hit leaves a white "chunk just lost" segment that holds
    /// ChunkHold s and then drains, so a 10-26% hit is unmistakable; healing rises in green. The local player's target
    /// (aim-assist lock, else the last enemy I hit) gets a FocusScale larger bar and its class word. Hidden under fog and
    /// over HUD panels exactly like the name tags; beyond TagLens.BarMaxDistance in the low views.
    /// Sizes (reference px at 1080p): width = SizeK / camera distance clamped to [MinW, MaxW]: 52 x 7.4 at the third-person
    /// fight distance (8 u), 36 x 5.1 floor (top-down and far), 64 x 9.1 up close; focus x 1.3 (68 x 9.6 at 8 u).
    /// Also: a small bar over a wall or barricade I just hit (2 s, from StructureHit's share left) and the Kaiju's bar with
    /// its HP number. Pooled (32 + 4 + 1), no per-frame allocation. View only; never writes the sim.
    /// </summary>
    public sealed class HealthBars
    {
        public const int MaxBars = 32, MaxStructures = 4;
        /// <summary>Fill size at scale 1 (px at 1080p); the frame adds FramePx round it.</summary>
        public const float BaseW = 52f, BaseH = 7.4f, FramePx = 1.5f;
        /// <summary>px x u: width = SizeK / distance from the camera (8 u in the third-person fight = 52 px).</summary>
        public const float SizeK = 416f, MinW = 36f, MaxW = 64f, FocusScale = 1.3f;
        /// <summary>Head lift the bar hangs from (the name tags' 2.65 u) and the gap under the tag (px).</summary>
        public const float Lift = 2.65f, TagHalfPx = 13f, GapPx = 3f;
        public const float ChunkHold = 0.25f, ChunkDrain = 0.9f, HealRise = 1.6f, HealGlowSeconds = 0.45f;
        public const float StructureSeconds = 2f, KaijuW = 136f, KaijuH = 10f;
        /// <summary>Focus on the last enemy I hit for this long when the aim assist has no lock.</summary>
        public const double FocusSeconds = 4.0;

        public static float WidthFor(float distance) => Mathf.Clamp(distance > 1e-3f ? SizeK / distance : MaxW, MinW, MaxW);
        public static float HeightFor(float width) => BaseH * width / BaseW;

        /// <summary>
        /// One step of the chunk rule (pure; tests): a drop snaps the fill to the new value and leaves the ghost at the old
        /// one for ChunkHold s, then the ghost drains at ChunkDrain per second; a rise climbs at HealRise per second.
        /// Returns +1 on a drop this step, -1 on the start of a rise, 0 otherwise.
        /// </summary>
        public static int Step(ref float shown, ref float ghost, ref float hold, float target, float dt)
        {
            int ev = 0;
            if (target < shown - 1e-4f)
            {
                if (ghost < shown) ghost = shown;
                shown = target;
                hold = ChunkHold;
                ev = 1;
            }
            else if (target > shown + 1e-4f)
            {
                if (ghost <= shown + 1e-4f) ev = -1;
                shown = Mathf.Min(target, shown + HealRise * dt);
            }
            if (hold > 0f) hold -= dt;
            else if (ghost > shown) ghost = Mathf.Max(shown, ghost - ChunkDrain * dt);
            if (ghost < shown) ghost = shown;
            return ev;
        }

        sealed class Bar
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public VectorShape Frame, Chunk, Fill;
            public Text Word;
            public float Shown = -1f, Ghost, Hold, Glow, Scale = -1f;
            public bool Enemy, Focus, Visible;
            public int Team = -1;
        }

        readonly Bar[] _bars = new Bar[MaxBars];
        readonly Bar[] _walls = new Bar[MaxStructures];
        readonly Vector3[] _wallAt = new Vector3[MaxStructures];
        readonly float[] _wallAge = new float[MaxStructures];
        readonly int[] _wallKind = new int[MaxStructures];
        readonly float[] _wallShare = new float[MaxStructures];
        Bar _kaiju;
        Text _kaijuHp;
        int _kaijuHpShown = -1;
        double _kaijuMineAt = -100;
        bool _kaijuMineDrop;
        RectTransform _root;
        int _focus = -1;
        int _lastHitByMe = -1;
        double _lastHitByMeAt = -100;

        /// <summary>The actor whose bar is drawn large this frame (-1 none; tests).</summary>
        public int Focus => _focus;
        /// <summary>Bars on screen after the last tick (tests).</summary>
        public int VisibleCount { get; private set; }
        public RectTransform Root => _root;

        public HealthBars(RectTransform world)
        {
            _root = PxUi.Rt(world, "Unit Bars");
            CriticalMass.Runtime.Menu.MenuUi.Stretch(_root);
            _root.SetAsFirstSibling();   // under every word and number of the world layer
            for (int i = 0; i < MaxBars; i++) _bars[i] = Make(_root, "Unit Bar " + i, BaseW, BaseH, true);
            for (int i = 0; i < MaxStructures; i++)
            {
                _walls[i] = Make(_root, "Wall Bar " + i, BaseW, BaseH - 1.4f, false);
                _walls[i].Fill.SetFill(PxPalette.Structure);
                _wallAge[i] = StructureSeconds;
            }
            _kaiju = Make(_root, "Kaiju Bar", KaijuW, KaijuH, true);
            _kaiju.Fill.SetFill(PxPalette.Strike);
            _kaijuHp = PxUi.Label(_kaiju.Rt, "HP", CMTokens.Mono, UiScale.TypeMin, PxPalette.Cream, TextAnchor.MiddleCenter, 0f, KaijuH + 4f, KaijuW, 20f, 2f);
        }

        static Bar Make(RectTransform parent, string name, float w, float h, bool enemy)
        {
            var b = new Bar();
            b.Rt = PxUi.Centred(parent, name, w + 2f * FramePx, h + 2f * FramePx);
            b.Group = PxUi.Group(b.Rt);
            b.Frame = PxUi.Shape(b.Rt, "Frame", enemy ? ShapeKind.Chamfer : ShapeKind.RoundRect, 0f, 0f, w + 2f * FramePx, h + 2f * FramePx, PxPalette.BarBack, enemy ? 3f : 3.5f, FramePx, 0f);
            b.Chunk = PxUi.Shape(b.Rt, "Chunk", ShapeKind.RoundRect, FramePx, FramePx, w, h, PxPalette.Chunk, 1f, 0f, 0f);
            b.Fill = PxUi.Shape(b.Rt, "Fill", ShapeKind.RoundRect, FramePx, FramePx, w, h, PxPalette.Strike, 1f, 0f, 0f);
            b.Word = PxUi.Label(b.Rt, "Class", CMTokens.Ui, UiScale.TypeMin, PxPalette.StrikeInk, TextAnchor.MiddleCenter, -40f, h + 2f * FramePx + 1f, w + 80f + 2f * FramePx, 20f, 2f);
            b.Word.gameObject.SetActive(false);
            b.Enemy = enemy;
            b.Rt.gameObject.SetActive(false);
            return b;
        }

        /// <summary>A Hit by the local player landed on victim (focus falls back to it).</summary>
        public void OnMyHit(int victim, double time)
        {
            _lastHitByMe = victim;
            _lastHitByMeAt = time;
        }

        /// <summary>StructureHit by me: show (or refresh) the bar of that cell for StructureSeconds.</summary>
        public void OnStructureHit(World w, double x, double z, float share, int kind)
        {
            Vector3 at = PxProject.Point(w, x, z, 1.5f);
            int slot = -1;
            float oldest = -1f;
            for (int i = 0; i < MaxStructures; i++)
            {
                if (_wallAge[i] < StructureSeconds && (_wallAt[i] - at).sqrMagnitude < 0.8f) { slot = i; break; }
                if (_wallAge[i] > oldest) { oldest = _wallAge[i]; slot = i; }
            }
            Bar b = _walls[slot];
            if (_wallAge[slot] >= StructureSeconds || (_wallAt[slot] - at).sqrMagnitude >= 0.8f) { b.Shown = Mathf.Clamp01(share + 0.0001f); b.Ghost = b.Shown; b.Hold = 0f; _wallShare[slot] = b.Shown; }
            // the share before this hit is the bar's current value; the hit's drop goes through the chunk rule
            _wallAt[slot] = at;
            _wallAge[slot] = 0f;
            _wallKind[slot] = kind;
            _wallShare[slot] = Mathf.Clamp01(share);
        }

        /// <summary>KaijuDamaged by me (its bar comes up and reports the drop).</summary>
        public void OnMyKaijuHit(double time)
        {
            _kaijuMineAt = time;
            _kaijuMineDrop = true;
        }

        public void Clear()
        {
            for (int i = 0; i < MaxBars; i++) { _bars[i].Shown = -1f; Hide(_bars[i]); }
            for (int i = 0; i < MaxStructures; i++) { _wallAge[i] = StructureSeconds; Hide(_walls[i]); }
            _kaiju.Shown = -1f;
            Hide(_kaiju);
            _lastHitByMe = -1;
            _focus = -1;
        }

        static void Hide(Bar b)
        {
            b.Visible = false;
            if (b.Rt.gameObject.activeSelf) b.Rt.gameObject.SetActive(false);
        }

        public void Tick(World w, int me, double alpha, UnityEngine.Camera cam, float scale, float dt)
        {
            bool low = PxProject.ThirdPerson;
            Vector3 cp = cam.transform.position;
            TeamId mine = me >= 0 && me < w.ActorCount ? w.Actors[me].Team : TeamId.Azure;
            int lockT = CriticalMass.Runtime.Input.AimAssist.LockTarget;
            _focus = lockT >= 0 ? lockT : (_lastHitByMe >= 0 && w.Match.Time - _lastHitByMeAt <= FocusSeconds ? _lastHitByMe : -1);
            int n = Mathf.Min(w.ActorCount, MaxBars), shown = 0;
            for (int i = 0; i < MaxBars; i++)
            {
                Bar b = _bars[i];
                if (i >= n || i == me) { Hide(b); continue; }
                Actor a = w.Actors[i];
                float hp01 = a.MaxHp > 0 ? Mathf.Clamp01((float)(a.Hp / a.MaxHp)) : 0f;
                if (!a.Alive || global::CriticalMass.Runtime.View.Fog.FogOfWar.Hides(i)) { b.Shown = -1f; Hide(b); continue; }
                if (b.Shown < 0f) { b.Shown = b.Ghost = hp01; b.Hold = 0f; }
                int ev = Step(ref b.Shown, ref b.Ghost, ref b.Hold, hp01, dt);
                if (ev < 0) b.Glow = HealGlowSeconds;
                b.Glow = Mathf.Max(0f, b.Glow - dt);

                Vector3 head = PxProject.Actor(w, i, alpha, Lift);
                float dist = (head - cp).magnitude;
                if (low && !TagLens.Show(dist, TagLens.BarMaxDistance)) { Hide(b); continue; }
                if (!PxProject.Screen(cam, head, 0f, out Vector2 p) || HudKeepOut.Contains(p, 24f * scale)) { Hide(b); continue; }

                bool enemy = a.Team != mine;
                bool focus = enemy && i == _focus;
                if (enemy != b.Enemy)
                {
                    b.Enemy = enemy;
                    b.Frame.Kind = enemy ? ShapeKind.Chamfer : ShapeKind.RoundRect;
                    b.Frame.SetVerticesDirty();
                }
                if (!b.Rt.gameObject.activeSelf) b.Rt.gameObject.SetActive(true);
                b.Visible = true;
                shown++;
                float k = WidthFor(dist) * (focus ? FocusScale : 1f) / BaseW;
                if (Mathf.Abs(b.Scale - k) > 0.01f) { b.Scale = k; b.Rt.localScale = new Vector3(k, k, 1f); }
                float tag = low ? TagLens.Scale(dist) : 1f;
                b.Rt.anchoredPosition = p / scale - new Vector2(0f, TagHalfPx * tag + GapPx + (BaseH + 2f * FramePx) * k * 0.5f);
                Color fill = enemy ? PxPalette.Strike : CMTokens.Team(a.Team);
                if (b.Glow > 0f) fill = Color.Lerp(fill, PxPalette.Heal, b.Glow / HealGlowSeconds);
                b.Fill.SetFill(fill);
                b.Fill.Fill01 = b.Shown;
                b.Chunk.Fill01 = b.Ghost;
                PxUi.Alpha(b.Group, focus ? 1f : enemy ? 0.95f : 0.85f);
                if (focus != b.Focus)
                {
                    b.Focus = focus;
                    b.Word.gameObject.SetActive(focus);
                    if (focus) HudText.Set(b.Word, KillWords.ClassUpper[Mathf.Clamp((int)a.Cls, 0, KillWords.ClassUpper.Length - 1)]);
                }
                if (ev > 0) PlayerFeedback.Report(enemy ? FeedbackChannel.EnemyHealthBar : FeedbackChannel.AllyHealthBar, i, b.Shown, b.Fill);
            }
            VisibleCount = shown;
            TickWalls(w, cam, cp, scale, dt, low);
            TickKaiju(w, me, cam, cp, scale, dt, low);
        }

        void TickWalls(World w, UnityEngine.Camera cam, Vector3 cp, float scale, float dt, bool low)
        {
            for (int i = 0; i < MaxStructures; i++)
            {
                Bar b = _walls[i];
                if (_wallAge[i] >= StructureSeconds) { Hide(b); continue; }
                _wallAge[i] += dt;
                int ev = Step(ref b.Shown, ref b.Ghost, ref b.Hold, _wallShare[i], dt);
                float dist = (_wallAt[i] - cp).magnitude;
                if (_wallAge[i] >= StructureSeconds || !PxProject.Screen(cam, _wallAt[i], 0f, out Vector2 p)) { Hide(b); continue; }
                bool first = !b.Rt.gameObject.activeSelf;
                if (first) b.Rt.gameObject.SetActive(true);
                b.Visible = true;
                float k = WidthFor(dist) / BaseW;
                if (Mathf.Abs(b.Scale - k) > 0.01f) { b.Scale = k; b.Rt.localScale = new Vector3(k, k, 1f); }
                b.Rt.anchoredPosition = p / scale;
                b.Fill.Fill01 = b.Shown;
                b.Chunk.Fill01 = b.Ghost;
                float t = _wallAge[i] / StructureSeconds;
                PxUi.Alpha(b.Group, t < 0.8f ? 1f : (1f - t) / 0.2f);
                if (ev > 0 || first) PlayerFeedback.Report(FeedbackChannel.EnemyHealthBar, -1, b.Shown, b.Fill, _wallKind[i]);
            }
        }

        void TickKaiju(World w, int me, UnityEngine.Camera cam, Vector3 cp, float scale, float dt, bool low)
        {
            Kaiju kj = w.Kaiju;
            bool up = w.Match.Hatched && kj.State != KaijuState.Egg && kj.Hp > 0 && kj.MaxHp > 0;
            if (!up) { _kaiju.Shown = -1f; Hide(_kaiju); _kaijuMineDrop = false; return; }
            float hp01 = Mathf.Clamp01((float)(kj.Hp / kj.MaxHp));
            if (_kaiju.Shown < 0f) { _kaiju.Shown = _kaiju.Ghost = hp01; }
            int ev = Step(ref _kaiju.Shown, ref _kaiju.Ghost, ref _kaiju.Hold, hp01, dt);
            Vector3 top = PxProject.Point(w, kj.X, kj.Z, (float)w.Config.Render.KaijuHeight + 1.3f);
            double dx = kj.X - (me >= 0 && me < w.ActorCount ? w.X[me] : kj.X), dz = kj.Z - (me >= 0 && me < w.ActorCount ? w.Z[me] : kj.Z);
            bool near = dx * dx + dz * dz <= 26.0 * 26.0 || w.Match.Time - _kaijuMineAt <= 8.0;
            if (!near || !PxProject.Screen(cam, top, 0f, out Vector2 p) || HudKeepOut.Contains(p, 40f * scale)) { Hide(_kaiju); _kaijuMineDrop = false; return; }
            if (!_kaiju.Rt.gameObject.activeSelf) _kaiju.Rt.gameObject.SetActive(true);
            _kaiju.Visible = true;
            _kaiju.Rt.anchoredPosition = p / scale;
            _kaiju.Fill.Fill01 = _kaiju.Shown;
            _kaiju.Chunk.Fill01 = _kaiju.Ghost;
            int hp = Mathf.CeilToInt((float)kj.Hp);
            if (hp != _kaijuHpShown) { _kaijuHpShown = hp; HudText.Set(_kaijuHp, NumberCache.Int(hp)); }
            if (_kaijuMineDrop) { _kaijuMineDrop = false; PlayerFeedback.Report(FeedbackChannel.EnemyHealthBar, -2, hp01, _kaiju.Fill); }
        }
    }
}
