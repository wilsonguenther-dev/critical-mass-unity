using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan item 8 (agency audit A8, hit-feel defect 1b): the attacker-side confirm. On every Hit by me a hit marker (four
    /// diagonal ticks round an open centre) flashes for Life s: at the reticle in the third-person view (screen centre,
    /// where GroundView draws its dot), on the victim's chest in the top-down view. Heavy tier is larger and longer; a kill
    /// has its own variant (damage-magenta ticks plus a ring) so "it landed" and "it finished him" never look alike.
    /// Before the press: corner brackets on AimAssist.HittableTarget ("press now and it lands"; with the ground ring
    /// enlarged by UnitRings) and a faint thin bracket on AimAssist.LockTarget. Sizes, px at 1080p: ticks gap 9, length 12,
    /// stroke 3 (heavy x 1.3 = 16 px arms), kill ticks gap 12 length 16 stroke 4 plus a 26 px ring; brackets at least
    /// 36 x 48, otherwise 0.62 x / 1.08 x the body's projected height. Reduce motion: no pop scale (the marker just shows).
    /// </summary>
    public sealed class HitMarker
    {
        public const float Life = 0.12f, HeavyLife = 0.16f, KillLife = 0.36f, PopScale = 1.35f, HeavyScale = 1.3f;
        public const float BracketMinW = 36f, BracketMinH = 48f, ChestLift = 1.2f;

        readonly RectTransform _rt, _killRt;
        readonly CanvasGroup _group, _killGroup;
        readonly PxTicks _ticks, _killTicks;
        readonly PxArc _killRing;
        readonly PxBracket _hittable, _lock;
        float _age = 99f, _life = Life, _base = 1f, _killAge = 99f;
        int _victim = -1, _killVictim = -1, _hitShown = -1, _lockShown = -1;
        Vector3 _at, _killAt;

        public PxTicks Ticks => _ticks;
        public PxBracket HittableBracket => _hittable;
        public bool Showing => _rt.gameObject.activeSelf;

        public HitMarker(RectTransform world)
        {
            _rt = PxUi.Centred(world, "Hit Marker", 64f, 64f);
            _group = PxUi.Group(_rt);
            _ticks = PxUi.Graphic<PxTicks>(_rt, "Ticks", 64f, 64f, PxPalette.Cream);
            _rt.gameObject.SetActive(false);
            _killRt = PxUi.Centred(world, "Kill Marker", 96f, 96f);
            _killGroup = PxUi.Group(_killRt);
            _killRing = PxUi.Graphic<PxArc>(_killRt, "Ring", 96f, 96f, PxPalette.Strike);
            _killRing.RadiusPx = 24f;
            _killRing.ThicknessPx = 3f;
            _killRing.SpanDeg = 360f;
            _killTicks = PxUi.Graphic<PxTicks>(_killRt, "Ticks", 96f, 96f, PxPalette.Strike);
            _killTicks.GapPx = 12f;
            _killTicks.LengthPx = 16f;
            _killTicks.WidthPx = 4f;
            _killRt.gameObject.SetActive(false);
            _hittable = MakeBracket(world, "Hittable Bracket", 3f, 12f);
            _lock = MakeBracket(world, "Lock Bracket", 2f, 9f);
            _lock.color = CriticalMass.Runtime.Hud.Style.CMTokens.WithAlpha(PxPalette.Cream, 0.5f);
        }

        static PxBracket MakeBracket(RectTransform world, string name, float width, float corner)
        {
            RectTransform rt = PxUi.Centred(world, name, BracketMinW, BracketMinH);
            PxBracket b = rt.gameObject.AddComponent<PxBracket>();
            b.raycastTarget = false;
            b.color = PxPalette.Cream;
            b.WidthPx = width;
            b.CornerPx = corner;
            rt.gameObject.SetActive(false);
            return b;
        }

        /// <summary>A Hit by me on victim (tier 0 light, 1 heavy, 2 critical).</summary>
        public void OnMyHit(World w, int victim, int tier, double alpha)
        {
            _victim = victim;
            _age = 0f;
            _life = tier > 0 ? HeavyLife : Life;
            _base = tier > 0 ? HeavyScale : 1f;
            _at = victim >= 0 && victim < w.ActorCount ? PxProject.Actor(w, victim, alpha, ChestLift) : Vector3.zero;
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            PxUi.Alpha(_group, 1f);
            PlayerFeedback.Report(FeedbackChannel.HitMarker, victim, tier, _ticks);
        }

        /// <summary>A hit by me on a structure or the Kaiju (no victim actor): the marker at the contact point.</summary>
        public void OnMyHitAt(Vector3 world, int tier)
        {
            _victim = -1;
            _age = 0f;
            _life = tier > 0 ? HeavyLife : Life;
            _base = tier > 0 ? HeavyScale : 1f;
            _at = world;
            if (!_rt.gameObject.activeSelf) _rt.gameObject.SetActive(true);
            PxUi.Alpha(_group, 1f);
            PlayerFeedback.Report(FeedbackChannel.HitMarker, -1, tier, _ticks);
        }

        /// <summary>A Death credited to me (the kill variant).</summary>
        public void OnMyKill(World w, int victim, double x, double z)
        {
            _killVictim = victim;
            _killAge = 0f;
            _killAt = PxProject.Point(w, x, z, ChestLift);
            if (!_killRt.gameObject.activeSelf) _killRt.gameObject.SetActive(true);
            PxUi.Alpha(_killGroup, 1f);
        }

        public void Clear()
        {
            _age = _killAge = 99f;
            PxUi.Show(_rt, false);
            PxUi.Show(_killRt, false);
            PxUi.Show(_hittable, false);
            PxUi.Show(_lock, false);
            _hitShown = _lockShown = -1;
        }

        public void Tick(World w, int me, double alpha, UnityEngine.Camera cam, float scale, float dt)
        {
            bool third = PxProject.ThirdPerson;
            bool calm = CriticalMass.Runtime.Menu.GameSettings.ReduceMotion;
            Vector2 centre = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f) / scale;
            if (_rt.gameObject.activeSelf)
            {
                _age += dt;
                if (_age >= _life) _rt.gameObject.SetActive(false);
                else
                {
                    Vector3 at = _victim >= 0 && _victim < w.ActorCount && w.Actors[_victim].Alive ? PxProject.Actor(w, _victim, alpha, ChestLift) : _at;
                    if (third) _rt.anchoredPosition = centre;
                    else if (PxProject.Screen(cam, at, 0f, out Vector2 p)) _rt.anchoredPosition = p / scale;
                    float t = _age / _life;
                    float k = _base * (calm ? 1f : Mathf.Lerp(PopScale, 1f, Mathf.Clamp01(t / 0.6f)));
                    _rt.localScale = new Vector3(k, k, 1f);
                    PxUi.Alpha(_group, t < 0.6f ? 1f : (1f - t) / 0.4f);
                }
            }
            if (_killRt.gameObject.activeSelf)
            {
                _killAge += dt;
                if (_killAge >= KillLife) _killRt.gameObject.SetActive(false);
                else
                {
                    if (third) _killRt.anchoredPosition = centre;
                    else if (PxProject.Screen(cam, _killAt, 0f, out Vector2 p)) _killRt.anchoredPosition = p / scale;
                    float t = _killAge / KillLife;
                    float k = calm ? 1f : Mathf.Lerp(1.5f, 1f, Mathf.Clamp01(t / 0.35f));
                    _killRt.localScale = new Vector3(k, k, 1f);
                    PxUi.Alpha(_killGroup, t < 0.65f ? 1f : (1f - t) / 0.35f);
                }
            }
            int hit = AimAssist.HittableTarget, lk = AimAssist.LockTarget;
            bool hitOn = Place(_hittable, w, hit, me, alpha, cam, scale);
            if (hitOn && hit != _hitShown) PlayerFeedback.Report(FeedbackChannel.HittableCue, hit, 1f, _hittable);
            _hitShown = hitOn ? hit : -1;
            bool lockOn = lk != hit && Place(_lock, w, lk, me, alpha, cam, scale);
            if (lockOn && lk != _lockShown) PlayerFeedback.Report(FeedbackChannel.AimAssistLock, lk, AimAssist.Strength, _lock);
            _lockShown = lockOn ? lk : (lk == hit && hitOn ? lk : -1);
        }

        /// <summary>Places a bracket round actor id's body; false (hidden) when there is nothing valid to frame.</summary>
        static bool Place(PxBracket b, World w, int id, int me, double alpha, UnityEngine.Camera cam, float scale)
        {
            bool ok = id >= 0 && id < w.ActorCount && id != me && w.Actors[id].Alive && !global::CriticalMass.Runtime.View.Fog.FogOfWar.Hides(id);
            Vector2 feet = default, head = default;
            ok = ok && PxProject.Screen(cam, PxProject.Actor(w, id, alpha, 0.05f), 0f, out feet) & PxProject.Screen(cam, PxProject.Actor(w, id, alpha, 2.0f), 0f, out head);
            if (!ok) { PxUi.Show(b, false); return false; }
            float h = Mathf.Max(BracketMinH, Mathf.Abs(head.y - feet.y) / scale * 1.08f);
            float wd = Mathf.Max(BracketMinW, h * 0.62f);
            RectTransform rt = b.rectTransform;
            Vector2 size = new Vector2(wd, h);
            if ((rt.sizeDelta - size).sqrMagnitude > 1f) rt.sizeDelta = size;
            rt.anchoredPosition = (feet + head) * 0.5f / scale;
            PxUi.Show(b, true);
            return true;
        }
    }
}
