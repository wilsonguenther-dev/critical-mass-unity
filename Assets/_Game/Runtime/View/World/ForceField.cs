using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Reactions
{
    /// <summary>
    /// The Force Field (DG-78, contract C1 shader globals): what makes the world bend. Every frame it
    /// <list type="bullet">
    /// <item>publishes the 16 live WorldForceBus slots as _CMForces[32] / _CMForceCount, the wind (_CMWind) and the sway
    /// parameters (_CMSwayParams: amplitude with reduced motion, bridge sag, texture on, view time);</item>
    /// <item>at Medium and High, keeps a square RGBA-half force texture over a window that follows the gameplay camera's
    /// ground point: one full-screen pass decays the previous frame (x 0.88 at 60 fps, which is the spring-back trail) and
    /// shifts it when the window scrolls, then one draw adds every stamp: a brush under each walker (grass parts round
    /// fighters, bridge decks sag: Warrior 1.5x, Kaiju 4x pressure), an expanding ring for each radial force (a slam
    /// flattens a ring that springs back) and a cone for each hit.</item>
    /// </list>
    /// CMToonCommon.hlsl's CMToonSwayOffset reads all of it in every pass. View only: reads World and the bus, writes
    /// shader globals and its own render textures. One command buffer, two draws, no allocation after Start.
    /// </summary>
    [DefaultExecutionOrder(60)]
    public sealed class ForceField : MonoBehaviour
    {
        public static readonly int ForcesId = Shader.PropertyToID("_CMForces"), ForceCountId = Shader.PropertyToID("_CMForceCount"),
            ForceTexId = Shader.PropertyToID("_CMForceTex"), WindowId = Shader.PropertyToID("_CMForceTexWindow"),
            WindId = Shader.PropertyToID("_CMWind"), SwayId = Shader.PropertyToID("_CMSwayParams"),
            PrevId = Shader.PropertyToID("_CMForcePrev"), DecayId = Shader.PropertyToID("_Decay"), ShiftId = Shader.PropertyToID("_Shift");

        /// <summary>Bridge deck sag at full pressure (u): a Warrior (pressure 0.375) dips about 0.09 u, the Kaiju 0.25 u.</summary>
        public const float BridgeSag = 0.25f;
        public const float Decay60 = 0.88f;
        private const int MaxStamps = 33 + WorldForceBus.SlotCount;

        /// <summary>Wind direction (world xz, normalised on set), strength 0..1 and gust frequency (Hz). Weather may change it.</summary>
        public static Vector2 WindDir = new Vector2(0.8f, 0.6f);
        public static float WindStrength = 0.6f, WindGustHz = 0.22f;

        private SimDriverBehaviour _driver;
        private ReactionBudget _budget;
        private readonly Vector4[] _forces = new Vector4[WorldForceBus.SlotCount * 2];
        private RenderTexture _a, _b;
        private Material _mat;
        private Mesh _quad, _stamps;
        private CommandBuffer _cmd;
        private readonly List<Vector3> _v = new List<Vector3>(MaxStamps * 4);
        private readonly List<Vector4> _uv0 = new List<Vector4>(MaxStamps * 4), _uv1 = new List<Vector4>(MaxStamps * 4);
        private readonly List<int> _tri = new List<int>(MaxStamps * 6);
        private Vector2 _origin;
        private bool _hasOrigin;
        private int _budgetCheck;

        /// <summary>Window origin (world x, z) and side, and stamps drawn last frame (tests, captures).</summary>
        public Vector2 Origin => _origin;
        public float Window => _budget.ForceWindow;
        public int LastStamps { get; private set; }
        public RenderTexture Texture => _a;
        public ReactionBudget Budget => _budget;

        private void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _budget = ReactionBudget.Current;
            Shader sh = Resources.Load<Shader>("CMReactionStamp");
            if (sh == null) sh = Shader.Find("CM/ReactionStamp");
            if (sh != null) _mat = new Material(sh) { name = "Force Field Stamps", hideFlags = HideFlags.DontSave };
            _quad = FullScreenQuad("Force Field Quad");
            _stamps = new Mesh { name = "Force Field Stamps" };
            _stamps.MarkDynamic();
            _cmd = new CommandBuffer { name = "CM Force Field" };
            Allocate();
        }

        private void OnDestroy()
        {
            Release();
            if (_mat != null) Destroy(_mat);
            if (_quad != null) Destroy(_quad);
            if (_stamps != null) Destroy(_stamps);
            _cmd?.Release();
            Shader.SetGlobalVector(SwayId, Vector4.zero);
            Shader.SetGlobalFloat(ForceCountId, 0f);
        }

        /// <summary>A quad covering clip space (-1..1), the decay and fade passes draw it.</summary>
        public static Mesh FullScreenQuad(string name)
        {
            var m = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            m.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            m.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
            return m;
        }

        private void Allocate()
        {
            Release();
            int n = _budget.ForceTexSize;
            if (n <= 0 || _mat == null || !SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf)) return;
            _a = MakeRt(n, "Force Field A");
            _b = MakeRt(n, "Force Field B");
            _hasOrigin = false;
        }

        private static RenderTexture MakeRt(int n, string name)
        {
            var rt = new RenderTexture(n, n, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear)
            {
                name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, autoGenerateMips = false,
            };
            rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = prev;
            return rt;
        }

        private void Release()
        {
            if (_a != null) { _a.Release(); Destroy(_a); _a = null; }
            if (_b != null) { _b.Release(); Destroy(_b); _b = null; }
        }

        private void LateUpdate()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if (++_budgetCheck % 120 == 0)
            {
                ReactionBudget b = ReactionBudget.Current;
                if (b.Tier != _budget.Tier) { _budget = b; Allocate(); }
            }
            World w = _driver != null ? _driver.World : null;
            WorldForceBus bus = WorldForceBus.Active;
            double now = w != null ? (w.Tick + _driver.Alpha) / _driver.Config.SimHz : 0.0;
            if (bus != null) bus.PerTickCap = _budget.PerTickCap;

            int count = bus != null ? bus.CopySlots(now, _forces) : 0;
            if (bus == null) for (int i = 0; i < _forces.Length; i++) _forces[i] = Vector4.zero;
            Shader.SetGlobalVectorArray(ForcesId, _forces);
            Shader.SetGlobalFloat(ForceCountId, count);
            Vector2 wd = WindDir.sqrMagnitude > 1e-6f ? WindDir.normalized : Vector2.right;
            Shader.SetGlobalVector(WindId, new Vector4(wd.x, wd.y, WindStrength, WindGustHz));

            bool tex = _a != null && w != null && bus != null;
            if (tex) DrawTexture(w, bus, now);
            Shader.SetGlobalVector(SwayId, new Vector4(ReactionBudget.MotionScale, BridgeSag, tex ? 1f : 0f, Time.time));
        }

        private void DrawTexture(World w, WorldForceBus bus, double now)
        {
            float win = _budget.ForceWindow;
            float texel = win / _budget.ForceTexSize;
            Vector2 centre = CentreXZ(w);
            var origin = new Vector2(Mathf.Floor((centre.x - win * 0.5f) / texel) * texel, Mathf.Floor((centre.y - win * 0.5f) / texel) * texel);
            Vector2 shift = _hasOrigin ? (origin - _origin) / win : Vector2.zero;
            if (Mathf.Abs(shift.x) >= 1f || Mathf.Abs(shift.y) >= 1f) shift = new Vector2(2f, 2f); // jumped: start clean
            _origin = origin;
            _hasOrigin = true;

            float frames = Mathf.Clamp(Time.deltaTime * 60f, 0.25f, 4f);
            float decay = Mathf.Pow(Decay60, frames);
            BuildStamps(w, bus, now, origin, win, decay, frames);

            _mat.SetFloat(DecayId, decay);
            _mat.SetVector(ShiftId, new Vector4(shift.x, shift.y, 0, 0));
            _cmd.Clear();
            _cmd.SetRenderTarget(_b);
            _cmd.SetGlobalTexture(PrevId, _a);
            _cmd.DrawMesh(_quad, Matrix4x4.identity, _mat, 0, 0);
            if (LastStamps > 0) _cmd.DrawMesh(_stamps, Matrix4x4.identity, _mat, 0, 1);
            Graphics.ExecuteCommandBuffer(_cmd);
            RenderTexture t = _a;
            _a = _b;
            _b = t;
            Shader.SetGlobalTexture(ForceTexId, _a);
            Shader.SetGlobalVector(WindowId, new Vector4(origin.x, origin.y, win, 0f));
        }

        /// <summary>Where the gameplay camera looks at the ground (y = 0 plane), else the local player.</summary>
        private Vector2 CentreXZ(World w)
        {
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                Ray r = new Ray(cam.transform.position, cam.transform.forward);
                if (Mathf.Abs(r.direction.y) > 1e-3f)
                {
                    float t = -r.origin.y / r.direction.y;
                    if (t > 0f && t < 500f) { Vector3 p = r.GetPoint(t); return new Vector2(p.x, p.z); }
                }
            }
            if (w.ActorCount > 0) return new Vector2((float)w.X[0], (float)-w.Z[0]);
            return Vector2.zero;
        }

        private void BuildStamps(World w, WorldForceBus bus, double now, Vector2 origin, float win, float decay, float frames)
        {
            _v.Clear();
            _uv0.Clear();
            _uv1.Clear();
            _tri.Clear();
            int n = 0;
            double a = _driver.Alpha;
            float keep = 1f - decay; // walkers: steady-state equals the brush strength whatever the frame rate
            int walkers = Mathf.Min(_budget.Walkers, w.ActorCount);
            GameConfig cfg = _driver.Config;
            for (int i = 0; i < walkers && n < MaxStamps; i++)
            {
                Actor ac = w.Actors[i];
                if (!ac.Alive) continue;
                float x = (float)(w.PrevX[i] + (w.X[i] - w.PrevX[i]) * a), z = -(float)(w.PrevZ[i] + (w.Z[i] - w.PrevZ[i]) * a);
                float pressure = ac.Cls == ClassId.Warrior ? 0.375f : 0.25f;
                n += Quad(origin, win, x, z, 0.6f, -1f, 0f, Vector2.zero, 0.5f * keep, pressure * keep);
            }
            Kaiju k = w.Kaiju;
            if (k.State != KaijuState.Egg && n < MaxStamps && _budget.Walkers > 0)
            {
                float x = (float)(k.PrevX + (k.X - k.PrevX) * a), z = -(float)(k.PrevZ + (k.Z - k.PrevZ) * a);
                n += Quad(origin, win, x, z, (float)cfg.Kaiju.Radius * 1.3f, -1f, 0f, Vector2.zero, 0.7f * keep, 1f * keep);
            }
            for (int s = 0; s < WorldForceBus.SlotCount && n < MaxStamps; s++)
            {
                if (!bus.TryGetSlot(s, now, out WorldForce f, out float age01, out float ageWall)) continue;
                float x = f.X, z = -f.Z;
                var dir = new Vector2(f.DirX, -f.DirZ);
                float fade = (1f - age01) * frames;
                if (f.Shape == ForceShape.Cone || f.Shape == ForceShape.Point)
                {
                    if (ageWall > 0.15f) continue; // a hit shoves once
                    n += Quad(origin, win, x, z, f.Radius, -1f, f.Shape == ForceShape.Cone && dir.sqrMagnitude > 1e-4f ? 2f : 0f, dir,
                        0.6f * f.Strength * fade, 0.3f * f.Strength * fade);
                }
                else
                {
                    // Expanding front: reaches the full radius in 0.25 s + 0.03 s per unit, then holds while it fades.
                    float grow = Mathf.Clamp01(ageWall / (0.25f + 0.03f * f.Radius));
                    n += Quad(origin, win, x, z, f.Radius, Mathf.Max(0.05f, grow), 1f, Vector2.zero,
                        0.5f * f.Strength * fade, 0.4f * f.Strength * fade);
                }
            }
            LastStamps = n;
            _stamps.Clear();
            if (n == 0) return;
            _stamps.SetVertices(_v);
            _stamps.SetUVs(0, _uv0);
            _stamps.SetUVs(1, _uv1);
            _stamps.SetTriangles(_tri, 0, false);
            _stamps.bounds = new Bounds(Vector3.zero, Vector3.one * 4f);
        }

        /// <summary>One stamp quad of radius r (u) at world (x, z); 0 when it lies outside the window.</summary>
        private int Quad(Vector2 origin, float win, float x, float z, float r, float ring, float shape, Vector2 dir, float push, float pressure)
        {
            if (r <= 0f || (push <= 0f && pressure <= 0f)) return 0;
            float x0 = (x - r - origin.x) / win, x1 = (x + r - origin.x) / win;
            float z0 = (z - r - origin.y) / win, z1 = (z + r - origin.y) / win;
            if (x1 <= 0f || z1 <= 0f || x0 >= 1f || z0 >= 1f) return 0;
            int b = _v.Count;
            _v.Add(new Vector3(x0 * 2f - 1f, z0 * 2f - 1f, 0f));
            _v.Add(new Vector3(x1 * 2f - 1f, z0 * 2f - 1f, 0f));
            _v.Add(new Vector3(x1 * 2f - 1f, z1 * 2f - 1f, 0f));
            _v.Add(new Vector3(x0 * 2f - 1f, z1 * 2f - 1f, 0f));
            _uv0.Add(new Vector4(-1f, -1f, ring, shape));
            _uv0.Add(new Vector4(1f, -1f, ring, shape));
            _uv0.Add(new Vector4(1f, 1f, ring, shape));
            _uv0.Add(new Vector4(-1f, 1f, ring, shape));
            var u1 = new Vector4(dir.x, dir.y, push, pressure);
            _uv1.Add(u1);
            _uv1.Add(u1);
            _uv1.Add(u1);
            _uv1.Add(u1);
            _tri.Add(b); _tri.Add(b + 1); _tri.Add(b + 2);
            _tri.Add(b); _tri.Add(b + 2); _tri.Add(b + 3);
            return 1;
        }
    }
}
