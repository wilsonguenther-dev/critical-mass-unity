using System.Collections.Generic;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Dressing;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Atmosphere
{
    /// <summary>
    /// b15 world look, item 3 (WORLD-AUDIT #30 "no particles"): three tiny particle systems with HARD caps from
    /// WorldLookBudgets (per quality level; Very Low builds nothing): warm dust motes drifting with the wind around the
    /// camera's ground focus, embers rising from the braziers near it (additive), and a few leaves falling from the tree
    /// line. Emission is manual (ParticleSystem.Emit with a reused EmitParams struct) so nothing allocates per frame; one
    /// shared material (CM/FxUnlit, already in the build), one draw per system. Nothing on the play surface blocks the
    /// view: motes are 0.03 to 0.06 u, leaves 0.08 u, embers fade in under 1.6 s. Visual only.
    /// </summary>
    [DefaultExecutionOrder(920)]
    [GameOverlay(Order = 79, Scope = OverlayScope.Always)]
    public sealed class AmbientParticles : MonoBehaviour
    {
        public enum Kind { Motes = 0, Embers = 1, Leaves = 2 }

        /// <summary>Emitted per second per system at full cap (the cap bounds the live count).</summary>
        public const float MoteRate = 14f, EmberRate = 10f, LeafRate = 2.2f;
        /// <summary>Radius around the camera's ground focus where particles live (u).</summary>
        public const float FocusRadius = 16f;

        public static AmbientParticles Current { get; private set; }

        readonly ParticleSystem[] _sys = new ParticleSystem[3];
        readonly float[] _carry = new float[3];
        readonly List<Vector3> _braziers = new List<Vector3>(64);
        readonly List<Vector3> _trees = new List<Vector3>(256);
        Material _mat;
        int _builtFor = -2;
        WorldPlan _plan;
        uint _rng = 0x9E3779B9u;
        static readonly int WindId = Shader.PropertyToID("_CMWind");

        /// <summary>Live particles per system (tests, probes).</summary>
        public int Live(Kind k) => _sys[(int)k] != null ? _sys[(int)k].particleCount : 0;
        public int Cap(Kind k) => _sys[(int)k] != null ? _sys[(int)k].main.maxParticles : 0;

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
            Teardown();
            if (_mat != null) Destroy(_mat);
            _mat = null;
        }

        void Teardown()
        {
            for (int i = 0; i < _sys.Length; i++)
            {
                if (_sys[i] != null) Destroy(_sys[i].gameObject);
                _sys[i] = null;
            }
            _builtFor = -2;
        }

        float Rand()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng & 0xFFFFFF) / 16777216f;
        }

        void Build(int level)
        {
            Teardown();
            _builtFor = level;
            WorldLookBudgets.AirTier t = WorldLookBudgets.For(level);
            if (t.Particles <= 0) return;
            if (_mat == null)
            {
                Shader sh = Resources.Load<Shader>("CMFxUnlit");
                if (sh == null) return;
                _mat = new Material(sh) { name = "Ambient Particles", renderQueue = 3050 };
            }
            Make(Kind.Motes, t.Motes, 0.05f);
            Make(Kind.Embers, t.Embers, 0.05f);
            Make(Kind.Leaves, t.Leaves, 0.08f);
        }

        void Make(Kind k, int cap, float size)
        {
            if (cap <= 0) return;
            var go = new GameObject("Ambient " + k);
            go.transform.SetParent(transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = false;
            main.maxParticles = cap;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSize = size;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            ParticleSystem.EmissionModule em = ps.emission;
            em.enabled = false;   // manual Emit only
            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = false;
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            ParticleSystem.CustomDataModule cd = ps.customData;
            cd.enabled = true;
            cd.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            cd.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 1);
            cd.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(k == Kind.Embers ? 1f : 0f));   // CM/FxUnlit TEXCOORD0.x: additive amount
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.sharedMaterial = _mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.alignment = ParticleSystemRenderSpace.View;
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.Custom1X });
            ps.Play();
            _sys[(int)k] = ps;
        }

        void CollectAnchors()
        {
            _braziers.Clear();
            _trees.Clear();
            WorldView wv = FindAnyObjectByType<WorldView>();
            _plan = wv != null ? wv.Plan : null;
            if (_plan == null) return;
            foreach (WorldPlacement p in _plan.Placements)
            {
                if (p.Piece == null) continue;
                Vector3 at = p.Matrix.MultiplyPoint3x4(Vector3.zero);
                if (p.Piece.Contains("brazier") || p.Piece.Contains("lantern")) _braziers.Add(at);
                else if (p.Piece.Contains("tree")) _trees.Add(at);
            }
        }

        void LateUpdate()
        {
            int level = RenderTier.Level;
            if (level != _builtFor) Build(level);
            WorldView wv = FindPlanOwner();
            if (wv != null && wv.Plan != _plan) CollectAnchors();
            Camera cam = Camera.main;
            if (cam == null || _mat == null) return;
            Vector3 focus = Focus(cam);
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            Vector4 wind = Shader.GetGlobalVector(WindId);
            Vector3 drift = new Vector3(wind.x, 0f, wind.y) * (0.25f + 0.6f * Mathf.Clamp01(wind.z));
            TimeOfDayDirector dir = TimeOfDayDirector.Current;
            bool night = dir != null && dir.Target == TimeOfDay.Night;
            Emit(Kind.Motes, MoteRate, dt, focus, drift, night ? new Color32(190, 210, 255, 110) : new Color32(255, 240, 205, 150));
            Emit(Kind.Embers, EmberRate, dt, focus, drift, new Color32(255, 170, 80, 255));
            Emit(Kind.Leaves, LeafRate, dt, focus, drift, new Color32(118, 160, 70, 230));
        }

        WorldView _wv;
        WorldView FindPlanOwner()
        {
            if (_wv == null && Time.frameCount % 60 == 0) _wv = FindAnyObjectByType<WorldView>();
            return _wv;
        }

        static Vector3 Focus(Camera cam)
        {
            Transform t = cam.transform;
            Vector3 f = t.forward;
            if (f.y < -0.05f) return t.position + f * (-t.position.y / f.y);
            return t.position;
        }

        void Emit(Kind k, float rate, float dt, Vector3 focus, Vector3 drift, Color32 colour)
        {
            ParticleSystem ps = _sys[(int)k];
            if (ps == null) return;
            int cap = ps.main.maxParticles;
            _carry[(int)k] += rate * dt * (cap / 64f);
            int n = (int)_carry[(int)k];
            if (n <= 0) return;
            _carry[(int)k] -= n;
            if (ps.particleCount + n > cap) n = cap - ps.particleCount;
            var e = new ParticleSystem.EmitParams { startColor = colour, applyShapeToPosition = false };
            for (int i = 0; i < n; i++)
            {
                Vector3 p;
                Vector3 v;
                float life;
                if (k == Kind.Embers)
                {
                    if (!Near(_braziers, focus, out p)) return;
                    p += new Vector3(Rand() * 0.3f - 0.15f, 0.9f + Rand() * 0.2f, Rand() * 0.3f - 0.15f);
                    v = new Vector3(drift.x * 0.4f, 0.55f + Rand() * 0.4f, drift.z * 0.4f);
                    life = 0.9f + Rand() * 0.7f;
                    e.startSize = 0.03f + Rand() * 0.03f;
                }
                else if (k == Kind.Leaves)
                {
                    if (!Near(_trees, focus, out p)) return;
                    p += new Vector3(Rand() * 2f - 1f, 2.4f + Rand() * 1.2f, Rand() * 2f - 1f);
                    v = drift * 1.5f + new Vector3(0f, -0.45f - Rand() * 0.2f, 0f);
                    life = 4f + Rand() * 2f;
                    e.startSize = 0.07f + Rand() * 0.03f;
                }
                else
                {
                    float a = Rand() * 6.2831853f, r = Mathf.Sqrt(Rand()) * FocusRadius;
                    p = focus + new Vector3(Mathf.Cos(a) * r, 0.4f + Rand() * 2.2f, Mathf.Sin(a) * r);
                    v = drift + new Vector3(Rand() * 0.1f - 0.05f, Rand() * 0.06f - 0.02f, Rand() * 0.1f - 0.05f);
                    life = 5f + Rand() * 4f;
                    e.startSize = 0.03f + Rand() * 0.03f;
                }
                e.position = p;
                e.velocity = v;
                e.startLifetime = life;
                ps.Emit(e, 1);
            }
        }

        bool Near(List<Vector3> anchors, Vector3 focus, out Vector3 p)
        {
            p = default;
            int count = anchors.Count;
            if (count == 0) return false;
            // A few random tries for an anchor within the focus radius (no list built per frame).
            for (int t = 0; t < 6; t++)
            {
                Vector3 a = anchors[(int)(Rand() * count) % count];
                float dx = a.x - focus.x, dz = a.z - focus.z;
                if (dx * dx + dz * dz <= FocusRadius * FocusRadius) { p = a; return true; }
            }
            return false;
        }
    }
}
