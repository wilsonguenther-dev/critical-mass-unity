using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Reactions
{
    /// <summary>
    /// Ground paint (DG-102, audit finding 6): one map-wide RGBA8 texture (linear; 512 at Low, 1024 at Medium and High)
    /// that CM/WorldGround samples (_CMPaintTex, _CMPaintRect). FxScene draws each still mark once into Builder (blood once
    /// spread, scorch, slam cracks: the same toon shapes, so the look does not change) and drops it from the per-frame
    /// ground mesh; this component flushes the builder into the texture in LateUpdate. RGB is the colour, A the remaining
    /// life: a mark is shown fully until 30% of its life is left, then fades (the shader), and a quarter-second fade pass
    /// takes 0.25 / PaintSeconds off every texel's life. At High, fighters leave footprints on grass and dirt, and the
    /// Kaiju's steps (KaijuStep forces) leave big ones. View only. No allocation after Start.
    /// </summary>
    [DefaultExecutionOrder(70)]
    public sealed class GroundPaint : MonoBehaviour, IGroundPaintTarget, IWorldForceListener
    {
        public static readonly int PaintTexId = Shader.PropertyToID("_CMPaintTex"), PaintRectId = Shader.PropertyToID("_CMPaintRect"),
            RectId = Shader.PropertyToID("_PaintRect"), StepId = Shader.PropertyToID("_Step");
        public const int BuilderVerts = 12000;
        public const float FadeInterval = 0.25f, StrideFighter = 0.9f;
        public static readonly Color32 PrintGrass = new Color32(86, 118, 52, 120), PrintDirt = new Color32(122, 84, 50, 130);

        private SimDriverBehaviour _driver;
        private CombatFx _fx;
        private World _world;
        private ReactionBudget _budget;
        private RenderTexture _rt;
        private Material _mat;
        private Mesh _quad;
        private CommandBuffer _cmd;
        private FxMeshBuilder _builder;
        private WorldForceBus _subscribed;
        private float _fadeAt;
        private double[] _px, _pz, _stride;
        private bool[] _has;
        private byte[] _foot;

        public bool Accepts => _rt != null && isActiveAndEnabled;
        public int Frame { get; private set; }
        public FxMeshBuilder Builder => _builder;
        public RenderTexture Texture => _rt;
        /// <summary>Stamp vertices flushed into the texture since Start (tests).</summary>
        public long Flushed { get; private set; }

        private void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            Shader sh = Resources.Load<Shader>("CMReactionStamp");
            if (sh == null) sh = Shader.Find("CM/ReactionStamp");
            if (sh != null) _mat = new Material(sh) { name = "Ground Paint", hideFlags = HideFlags.DontSave };
            _quad = ForceField.FullScreenQuad("Ground Paint Quad");
            _cmd = new CommandBuffer { name = "CM Ground Paint" };
            _builder = new FxMeshBuilder("Ground Paint Stamps", BuilderVerts, BuilderVerts * 3);
        }

        private void OnDestroy()
        {
            if (_fx != null && _fx.Scene != null && ReferenceEquals(_fx.Scene.Paint, this)) _fx.Scene.Paint = null;
            if (_subscribed != null) _subscribed.Unsubscribe(this);
            ReleaseRt();
            if (_mat != null) Destroy(_mat);
            if (_quad != null) Destroy(_quad);
            if (_builder != null) Destroy(_builder.Mesh);
            _cmd?.Release();
            Shader.SetGlobalVector(PaintRectId, Vector4.zero);
        }

        private void ReleaseRt()
        {
            if (_rt == null) return;
            _rt.Release();
            Destroy(_rt);
            _rt = null;
        }

        private void Setup(World w)
        {
            _world = w;
            _budget = ReactionBudget.Current;
            ReleaseRt();
            if (_mat == null || w == null || w.Map == null) return;
            int n = _budget.PaintSize;
            _rt = new RenderTexture(n, n, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "Ground Paint", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, useMipMap = false, autoGenerateMips = false,
            };
            _rt.Create();
            var prev = RenderTexture.active;
            RenderTexture.active = _rt;
            GL.Clear(false, true, new Color(0, 0, 0, 0));
            RenderTexture.active = prev;
            int cap = World.ActorCapacity(_driver.Config);
            _px = new double[cap];
            _pz = new double[cap];
            _stride = new double[cap];
            _has = new bool[cap];
            _foot = new byte[cap];
            Vector4 rect = new Vector4(0f, 0f, 1f / Mathf.Max(1, w.Map.Width), 1f / Mathf.Max(1, w.Map.Height));
            _mat.SetVector(RectId, rect);
            Shader.SetGlobalTexture(PaintTexId, _rt);
            Shader.SetGlobalVector(PaintRectId, rect);
            _builder.Clear();
            _driver.Ticked -= OnTicked;
            _driver.Ticked += OnTicked;
        }

        private void OnDisable()
        {
            if (_driver != null) _driver.Ticked -= OnTicked;
        }

        private void LateUpdate()
        {
            if (CriticalMass.Runtime.Flow.GameFlow.MenuIdle) return;   // batch 4: hidden behind a menu over a backdrop match
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            if (w == null) return;
            if (w != _world) Setup(w);
            if (_fx == null) _fx = GetComponent<CombatFx>();
            if (_fx != null && _fx.Scene != null && _fx.Scene.Paint == null) _fx.Scene.Paint = this;
            WorldForceBus bus = WorldForceBus.Active;
            if (bus != _subscribed)
            {
                if (_subscribed != null) _subscribed.Unsubscribe(this);
                _subscribed = bus;
                bus?.Subscribe(this);
            }
            if (_rt == null) return;
            Flush();
        }

        /// <summary>Draw what the builder holds into the texture and run the quarter-second fade; Frame moves on.</summary>
        public void Flush()
        {
            if (_rt == null || _mat == null) return;
            _cmd.Clear();
            bool any = false;
            if (_builder.VertCount > 0)
            {
                _builder.Upload();
                _cmd.SetRenderTarget(_rt);
                _cmd.DrawMesh(_builder.Mesh, Matrix4x4.identity, _mat, 0, 2);
                Flushed += _builder.VertCount;
                any = true;
            }
            if (Time.unscaledTime >= _fadeAt)
            {
                _fadeAt = Time.unscaledTime + FadeInterval;
                _mat.SetFloat(StepId, FadeInterval / Mathf.Max(1f, _budget.PaintSeconds));
                _cmd.SetRenderTarget(_rt);
                _cmd.DrawMesh(_quad, Matrix4x4.identity, _mat, 0, 3);
                any = true;
            }
            if (any) Graphics.ExecuteCommandBuffer(_cmd);
            _builder.Clear();
            Frame++;
        }

        /// <summary>Kaiju steps leave a big print (High), every tier stamps nothing else here: FxScene sends the rest.</summary>
        public void OnForce(ref WorldForce f)
        {
            if (!_budget.Footprints || _rt == null || f.Source != ForceSource.KaijuStep) return;
            if (f.Surface != Effects.Surface.Grass && f.Surface != Effects.Surface.Dirt) return;
            Print(f.X, f.Z, f.Y, 0.55f, (f.Seed & 1) != 0 ? 0.4f : -0.4f, f.DirX, f.DirZ, f.Surface);
        }

        /// <summary>Fighter footprints (High): every 0.9 u of travel on grass or dirt, alternating feet.</summary>
        private void OnTicked(World w)
        {
            if (!_budget.Footprints || _rt == null || _px == null) return;
            int n = Mathf.Min(w.ActorCount, _px.Length);
            WorldForceBus bus = WorldForceBus.Active;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                double x = w.X[i], z = w.Z[i];
                if (!a.Alive || a.AirLen > 0) { _has[i] = false; continue; }
                if (!_has[i]) { _px[i] = x; _pz[i] = z; _has[i] = true; continue; }
                double dx = x - _px[i], dz = z - _pz[i], d = System.Math.Sqrt(dx * dx + dz * dz);
                _px[i] = x;
                _pz[i] = z;
                if (d > 3.0 || d < 1e-6) continue;
                _stride[i] += d;
                if (_stride[i] < StrideFighter) continue;
                _stride[i] = 0;
                Effects.Surface s = bus != null ? bus.SurfaceAt(w, x, z) : WorldForceBus.SurfaceOf(w.Map.Cells[(int)z * w.Map.Width + (int)x]);
                if (s != Effects.Surface.Grass && s != Effects.Surface.Dirt) continue;
                _foot[i] ^= 1;
                float size = a.Cls == ClassId.Warrior ? 0.16f : 0.13f;
                Print((float)x, (float)z, 0f, size, _foot[i] == 0 ? 0.12f : -0.12f, (float)(dx / d), (float)(dz / d), s);
            }
        }

        /// <summary>One footprint (sim position, sideways offset along the right of dir): a sole and a heel, flat ellipses.</summary>
        private void Print(float x, float z, float y, float size, float side, float dirX, float dirZ, Effects.Surface s)
        {
            float len = Mathf.Sqrt(dirX * dirX + dirZ * dirZ);
            Vector3 fwd = len > 1e-4f ? SimSpace.ToWorld(dirX / len, 0, dirZ / len) : Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            Vector3 c = SimSpace.ToWorld(x, y, z) + right * side;
            Color32 col = s == Effects.Surface.Dirt ? PrintDirt : PrintGrass;
            _builder.Blob(c + fwd * size * 0.5f, right * 0.75f, fwd * 1.2f, size, null, 8, 0f, col);
            _builder.Blob(c - fwd * size * 0.9f, right * 0.7f, fwd * 0.8f, size * 0.7f, null, 7, 0f, col);
        }
    }
}
