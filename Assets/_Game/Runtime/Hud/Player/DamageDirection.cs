using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Plan item 10 (session audit 2.3 / 2.4, P4): "you were hit from there". On every Hit whose victim is me, an arc on a
    /// ring points at the attacker: around the reticle (screen centre) in the third-person view, around my fighter in the
    /// top-down view. It works the same for attackers on and off screen (the bearing is the screen direction of the ground
    /// line from me toward the source, PxProject.Bearing), follows a moving attacker for its Life, and several attackers
    /// give several arcs (one per source, refreshed). A blast points at the blast centre, the Kaiju at the Kaiju.
    /// Strength (alpha and thickness) grows with the damage share. Sizes, px at 1080p: ring radius 170 (third person) /
    /// 120 (top-down), arc 50 degrees wide, 9 to 16 px thick, 2 px dark outline; fades over 0.8 s. Colour PxPalette.Hurt.
    /// </summary>
    public sealed class DamageDirection
    {
        public const int Arcs = 6;
        public const float CritThick = 24f, Life = 0.8f, RadiusThird = 170f, RadiusTop = 120f, SpanDeg = 50f, MinThick = 9f, MaxThick = 16f;
        /// <summary>Source codes beyond actor ids.</summary>
        public const int SourceKaiju = -2, SourcePoint = -10;

        /// <summary>Strength 0..1 of an arc for a hit of share (damage / max HP) (pure; tests).</summary>
        public static float StrengthFor(float share) => Mathf.Clamp(0.45f + share * 2.5f, 0.45f, 1f);

        sealed class Arc
        {
            public PxArc G;
            public CanvasGroup Group;
            public int Source = int.MinValue;
            public Vector3 Point;
            public float Age = Life, Strength;
            public bool Reported, Crit;
        }

        readonly Arc[] _arcs = new Arc[Arcs];
        float _radius = -1f;

        /// <summary>Last bearing drawn (tests and captures).</summary>
        public float LastBearing { get; private set; }
        public int ActiveCount
        {
            get { int n = 0; for (int i = 0; i < Arcs; i++) if (_arcs[i].Age < Life) n++; return n; }
        }

        public DamageDirection(RectTransform world)
        {
            for (int i = 0; i < Arcs; i++)
            {
                RectTransform rt = PxUi.Centred(world, "Damage Arc " + i, 2f * (RadiusThird + MaxThick + 4f), 2f * (RadiusThird + MaxThick + 4f));
                var a = new Arc { Group = PxUi.Group(rt) };
                a.G = rt.gameObject.AddComponent<PxArc>();
                a.G.raycastTarget = false;
                a.G.color = PxPalette.Hurt;
                a.G.SpanDeg = SpanDeg;
                a.G.RadiusPx = RadiusThird;
                rt.gameObject.SetActive(false);
                _arcs[i] = a;
            }
        }

        /// <summary>A hit on me. source = attacker actor id, SourceKaiju, or SourcePoint with point = where it came from.</summary>
        public void OnHurt(int source, Vector3 point, float share)
        {
            Arc slot = null;
            for (int i = 0; i < Arcs && slot == null; i++) if (_arcs[i].Age < Life && _arcs[i].Source == source && source != SourcePoint) slot = _arcs[i];
            if (slot == null)
            {
                float oldest = -1f;
                for (int i = 0; i < Arcs; i++) if (_arcs[i].Age > oldest) { oldest = _arcs[i].Age; slot = _arcs[i]; }
            }
            float st = StrengthFor(share);
            if (!(slot.Age < Life && slot.Source == source)) slot.Crit = false;
            if (slot.Age < Life && slot.Source == source) st = Mathf.Max(st, slot.Strength * (1f - slot.Age / Life));
            slot.Source = source;
            slot.Point = point;
            slot.Age = 0f;
            slot.Strength = st;
            slot.Reported = false;
            float thick = slot.Crit ? CritThick : Mathf.Lerp(MinThick, MaxThick, (st - 0.45f) / 0.55f);
            if (Mathf.Abs(slot.G.ThicknessPx - thick) > 0.5f) { slot.G.ThicknessPx = thick; slot.G.SetVerticesDirty(); }
            if (!slot.G.gameObject.activeSelf) slot.G.gameObject.SetActive(true);
        }

        /// <summary>Lane S1's CriticalHit on me: the arc for that source (made if its Hit has not been read yet) goes thick and full.</summary>
        public void OnCritical(int source, Vector3 point)
        {
            Arc slot = null;
            for (int i = 0; i < Arcs && slot == null; i++) if (_arcs[i].Age < Life && _arcs[i].Source == source) slot = _arcs[i];
            if (slot == null) { OnHurt(source, point, 1f); for (int i = 0; i < Arcs && slot == null; i++) if (_arcs[i].Age < Life && _arcs[i].Source == source) slot = _arcs[i]; }
            if (slot == null) return;
            slot.Crit = true;
            slot.Strength = 1f;
            slot.Age = 0f;
            if (Mathf.Abs(slot.G.ThicknessPx - CritThick) > 0.5f) { slot.G.ThicknessPx = CritThick; slot.G.SetVerticesDirty(); }
        }

        public void Clear()
        {
            for (int i = 0; i < Arcs; i++) { _arcs[i].Age = Life; PxUi.Show(_arcs[i].G, false); }
        }

        /// <summary>
        /// Review fix round 2 (gate tier F, F20: the third-person arrow sat 20 deg off the attacker's on-screen bearing): the
        /// arc is centred on the screen, not on my feet, and the fight framing swings the camera. An attacker in front of
        /// the camera: the bearing from the arc centre to him on screen, this frame. Behind it: his ground direction against
        /// the camera's CURRENT yaw (flattened forward = screen up).
        /// </summary>
        static float ThirdPersonBearing(UnityEngine.Camera cam, Vector2 centrePx, Vector3 mePos, Vector3 src)
        {
            Vector3 sp = cam.WorldToScreenPoint(src);
            Vector2 d = new Vector2(sp.x, sp.y) - centrePx;
            if (sp.z > 0.05f && d.sqrMagnitude > 4f) return PxBearing.FromScreenDelta(d.x, d.y);
            Transform t = cam.transform;
            Vector3 fwd = t.forward;
            fwd.y = 0f;
            if (fwd.sqrMagnitude < 1e-6f) fwd = t.up;
            fwd.Normalize();
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            return PxBearing.FromBasis(src.x - mePos.x, src.z - mePos.z, fwd.x, fwd.z, right.x, right.z);
        }

        public void Tick(World w, int me, double alpha, UnityEngine.Camera cam, float scale, float dt)
        {
            bool third = PxProject.ThirdPerson;
            float radius = third ? RadiusThird : RadiusTop;
            bool resize = Mathf.Abs(radius - _radius) > 0.5f;
            _radius = radius;
            bool haveMe = me >= 0 && me < w.ActorCount;
            Vector3 mePos = haveMe ? PxProject.Actor(w, me, alpha, 1.0f) : Vector3.zero;
            Vector2 centre = new Vector2(cam.pixelWidth * 0.5f, cam.pixelHeight * 0.5f) / scale;
            if (!third && haveMe && PxProject.Screen(cam, mePos, 400f, out Vector2 mp)) centre = mp / scale;
            for (int i = 0; i < Arcs; i++)
            {
                Arc a = _arcs[i];
                if (a.Age >= Life) { PxUi.Show(a.G, false); continue; }
                a.Age += dt;
                if (a.Age >= Life || !haveMe) { a.Age = Life; PxUi.Show(a.G, false); continue; }
                if (resize) { a.G.RadiusPx = radius; a.G.SetVerticesDirty(); }
                Vector3 src = a.Point;
                if (a.Source >= 0 && a.Source < w.ActorCount && w.Actors[a.Source].Alive) src = PxProject.Actor(w, a.Source, alpha, 1.0f);
                else if (a.Source == SourceKaiju) src = PxProject.Point(w, w.Kaiju.X, w.Kaiju.Z, 1.0f);
                float bearing = third ? ThirdPersonBearing(cam, centre * scale, mePos, src) : PxProject.Bearing(cam, mePos, src.x - mePos.x, src.z - mePos.z);
                RectTransform rt = a.G.rectTransform;
                rt.anchoredPosition = centre;
                rt.localRotation = Quaternion.Euler(0f, 0f, -bearing);
                float t = a.Age / Life;
                PxUi.Alpha(a.Group, a.Strength * Mathf.Pow(1f - t, 0.7f));
                LastBearing = bearing;
                if (!a.Reported)
                {
                    a.Reported = true;
                    PlayerFeedback.Report(FeedbackChannel.DamageDirection, a.Source >= 0 ? a.Source : -1, bearing, a.G);
                }
            }
        }
    }
}
