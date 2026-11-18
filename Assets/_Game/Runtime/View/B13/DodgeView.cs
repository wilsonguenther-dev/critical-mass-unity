using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.B13
{
    /// <summary>
    /// CM b13 item J: world-space view of the dodge. A dashing fighter leaves a short team-tinted streak: pooled flat quads
    /// (PerTeam per team, made once in Awake, each alive StreakSeconds and shrinking), one per dash tick per fighter, laid
    /// along the dash direction at the interpolated position. The lean itself is drawn by ActorViews through LeanDeg.
    /// Visual only: reads Dodge.Phase / DodgeState at the render alpha, never writes the sim. No per-frame allocation
    /// (fixed pools, one shared material, one MaterialPropertyBlock). Adds at most 12 quads (24 triangles, 12 draw calls,
    /// none while nobody dodges). With -dodgeShots it also adds the DodgeShots capture (Effects/DodgeShots.cs).
    /// </summary>
    public sealed class DodgeView : MonoBehaviour
    {
        public const int PerTeam = 6;
        public const float StreakSeconds = 0.2f, StreakLength = 1.1f, StreakWidth = 0.35f, LeanMaxDeg = 14f;
        static readonly Color AzureTint = new Color(0.35f, 0.70f, 1.00f), CrimsonTint = new Color(1.00f, 0.38f, 0.32f);

        SimDriverBehaviour _driver;
        readonly Transform[] _quads = new Transform[PerTeam * 2];
        readonly Renderer[] _renderers = new Renderer[PerTeam * 2];
        readonly float[] _born = new float[PerTeam * 2];
        readonly int[] _next = new int[2];
        int[] _lastTick = new int[64];
        // b14 polish (draw-call budget): one shared material per team instead of one material plus a property block per
        // quad: a property block takes each renderer out of the SRP Batcher, so 12 streaks cost up to 12 batches.
        Material _mat, _matCrimson;

        /// <summary>Forward lean (degrees) of actor id at render alpha: a hump over the dash, 0 when not dashing.</summary>
        public static float LeanDeg(World w, int id, double alpha)
        {
            double p = Dodge.Phase(w, id, alpha);
            if (p < 0) return 0f;
            return (float)(LeanMaxDeg * 4 * p * (1 - p) + LeanMaxDeg * 0.25 * (1 - p));
        }

        static void Tint(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        void OnDestroy()
        {
            if (_mat != null) Destroy(_mat);
            if (_matCrimson != null) Destroy(_matCrimson);
        }

        void Awake()
        {
            _driver = GetComponentInParent<SimDriverBehaviour>();
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Unlit/Color");
            _mat = sh != null ? new Material(sh) { name = "Dodge Streak Azure" } : null;
            _matCrimson = sh != null ? new Material(sh) { name = "Dodge Streak Crimson" } : null;
            Tint(_mat, AzureTint);
            Tint(_matCrimson, CrimsonTint);
            for (int i = 0; i < _quads.Length; i++)
            {
                GameObject q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "Dodge Streak " + i;
                Collider c = q.GetComponent<Collider>();
                if (c != null) Destroy(c);
                q.transform.SetParent(transform, false);
                Renderer r = q.GetComponent<Renderer>();
                Material team = i < PerTeam ? _mat : _matCrimson;
                if (team != null) r.sharedMaterial = team;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _quads[i] = q.transform;
                _renderers[i] = r;
                _born[i] = -1f;
                q.SetActive(false);
            }
            for (int i = 0; i < _lastTick.Length; i++) _lastTick[i] = -1;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-dodgeShots") >= 0) gameObject.AddComponent<Effects.DodgeShots>();
        }

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponentInParent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            float now = Time.time;
            for (int i = 0; i < _quads.Length; i++)
            {
                if (_born[i] < 0) continue;
                float age = now - _born[i];
                if (age >= StreakSeconds)
                {
                    _born[i] = -1f;
                    _quads[i].gameObject.SetActive(false);
                    continue;
                }
                float s = 1f - age / StreakSeconds;
                _quads[i].localScale = new Vector3(StreakWidth * s, StreakLength * (0.4f + 0.6f * s), 1f);
            }
            if (w == null || !Dodge.On(w)) return;
            if (_lastTick.Length < w.ActorCount)
            {
                _lastTick = new int[w.ActorCount]; // warm-up only (the actor count is fixed per match)
                for (int i = 0; i < _lastTick.Length; i++) _lastTick[i] = -1;
            }
            double alpha = _driver.Alpha;
            DodgeState ds = w.Dodges;
            for (int id = 0; id < w.ActorCount; id++)
            {
                if (Dodge.Phase(w, id, alpha) < 0 || _lastTick[id] == w.Tick) continue;
                _lastTick[id] = w.Tick;
                int team = w.Actors[id].Team == TeamId.Azure ? 0 : 1;
                int slot = team * PerTeam + _next[team];
                _next[team] = (_next[team] + 1) % PerTeam;
                double x = w.PrevX[id] + (w.X[id] - w.PrevX[id]) * alpha;
                double z = w.PrevZ[id] + (w.Z[id] - w.PrevZ[id]) * alpha;
                double dx = ds.DirX[id], dz = ds.DirZ[id];
                // the streak trails behind the fighter, flat on the ground
                Vector3 p = SimSpace.ToWorld(x - dx * StreakLength * 0.5, 0.06, z - dz * StreakLength * 0.5);
                Transform t = _quads[slot];
                t.localPosition = p;
                t.localRotation = Quaternion.Euler(90f, SimSpace.YawDeg(System.Math.Atan2(dz, dx)), 0f);
                t.localScale = new Vector3(StreakWidth, StreakLength, 1f);
                _born[slot] = now;
                t.gameObject.SetActive(true);
            }
        }
    }
}
