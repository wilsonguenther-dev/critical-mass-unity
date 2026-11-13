using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// DG-20 see-through: while a wall, a higher terrace, a tree or the Kaiju covers the player (SeeThroughProbe), the
    /// player's own renderers are drawn a second time with CM/CameraSeeThrough (ZTest Greater: only the covered pixels,
    /// a striped team-colour fill with a Warm Cream rim), so his silhouette shows through whatever is in front.
    /// Nothing in CMToon.shader changes: the pass is injected from script each frame
    /// (RenderPipelineManager.beginCameraRendering, ScriptableRenderer.EnqueuePass), so no renderer asset changes either.
    /// It also publishes _CM_SeeThroughPos / _CM_SeeThroughRadius for the optional occluder dither hook
    /// (Shaders/CMSeeThroughHook.hlsl; the CMToon owner adds one include and one call to use it).
    /// View only: reads the World, never writes it; aim is untouched (it stays the screen-to-world ray of HumanInputSource).
    /// </summary>
    public sealed class SeeThrough : MonoBehaviour
    {
        public const string ShaderResource = "CMCameraSeeThrough";
        /// <summary>Fade time of the silhouette (s, view clock).</summary>
        public const float FadeSeconds = 0.12f;
        /// <summary>Hard caps: renderers of the player, trees considered, cylinders per probe.</summary>
        public const int MaxRenderers = 16, MaxTrees = 768, MaxCylinders = 48;
        /// <summary>Tree trunks and crowns as cylinders (the kit prints no heights: chosen here, flagged in the PR).</summary>
        public const double TreeRadius = 0.9, TreeHeightTall = 5.0, TreeHeightRound = 3.2;
        /// <summary>Only trees within this distance (u) of the camera-to-player line are tested.</summary>
        public const double TreeBand = 2.0;
        /// <summary>Occluder dither hook: screen-space radius around the player in world units at his depth.</summary>
        public const float HookRadius = 2.2f;

        public SimDriverBehaviour Driver;

        /// <summary>True while the player is covered this frame (tests and the debug HUD read it).</summary>
        public bool Covered { get; private set; }
        /// <summary>0..1 silhouette strength.</summary>
        public float Fade { get; private set; }

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int RimId = Shader.PropertyToID("_RimColor");
        private static readonly int FadeId = Shader.PropertyToID("_Fade");
        private static readonly int HookPosId = Shader.PropertyToID("_CM_SeeThroughPos");
        private static readonly int HookRadiusId = Shader.PropertyToID("_CM_SeeThroughRadius");

        private Material _material;
        private SilhouettePass _pass;
        private readonly Renderer[] _renderers = new Renderer[MaxRenderers];
        private readonly int[] _submeshes = new int[MaxRenderers];
        private int _rendererCount;
        private readonly List<Renderer> _scratch = new List<Renderer>(MaxRenderers * 2);
        private Transform _playerRoot;
        private int _nextLook;
        private ActorViews _views;

        private readonly double[] _treeX = new double[MaxTrees], _treeZ = new double[MaxTrees], _treeY = new double[MaxTrees], _treeH = new double[MaxTrees];
        private int _treeCount;
        private int _treeVersion = -1;
        private WorldView _world;
        private readonly SeeThroughCylinder[] _cyl = new SeeThroughCylinder[MaxCylinders];

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            Shader.SetGlobalVector(HookPosId, Vector4.zero);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }

        private Material SeeThroughMaterial
        {
            get
            {
                if (_material == null)
                {
                    Shader sh = Resources.Load<Shader>(ShaderResource);
                    if (sh == null || !sh.isSupported) return null;
                    _material = new Material(sh) { name = "CM See Through", hideFlags = HideFlags.DontSave };
                }
                return _material;
            }
        }

        private void LateUpdate()
        {
            World w = Driver != null ? Driver.World : null;
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            bool covered = false;
            Vector3 chest = Vector3.zero;
            if (w != null && w.ActorCount > 0 && cam != null && w.Actors[0].Alive)
            {
                MapDef map = w.Map;
                double a = Driver.Alpha;
                double px = w.PrevX[0] + (w.X[0] - w.PrevX[0]) * a;
                double pz = w.PrevZ[0] + (w.Z[0] - w.PrevZ[0]) * a;
                double py = GroundHeight.At(map, px, pz);
                Vector3 cp = cam.transform.position;
                SimSpace.ToSim(cp, out double cx, out double cz);
                int n = GatherCylinders(w, cx, cz, px, pz);
                covered = SeeThroughProbe.Covered(map, cx, cp.y, cz, px, py, pz, _cyl, n);
                chest = SimSpace.ToWorld(px, py + 1.0, pz);
                if (_material != null || covered)
                {
                    Material m = SeeThroughMaterial;
                    if (m != null)
                    {
                        m.SetColor(ColorId, CMTokens.Team(w.Actors[0].Team));
                        m.SetColor(RimId, CMTokens.WarmCream.Color);
                    }
                }
            }
            Covered = covered;
            float step = FadeSeconds > 0 ? Time.unscaledDeltaTime / FadeSeconds : 1f;
            Fade = Mathf.MoveTowards(Fade, covered ? 1f : 0f, step);
            if (_material != null) _material.SetFloat(FadeId, Fade);
            Shader.SetGlobalVector(HookPosId, new Vector4(chest.x, chest.y, chest.z, Fade));
            Shader.SetGlobalFloat(HookRadiusId, HookRadius);
            if (Fade > 0f) FindPlayerRenderers();
        }

        /// <summary>Trees near the sight line plus the hatched Kaiju, into _cyl; returns the count.</summary>
        private int GatherCylinders(World w, double cx, double cz, double px, double pz)
        {
            RefreshTrees();
            int n = 0;
            for (int i = 0; i < _treeCount && n < MaxCylinders - 1; i++)
            {
                if (WorldDresser.SegDist(_treeX[i], _treeZ[i], cx, cz, px, pz) > TreeBand + TreeRadius) continue;
                _cyl[n++] = new SeeThroughCylinder { X = _treeX[i], Z = _treeZ[i], R = TreeRadius, Y0 = _treeY[i], Y1 = _treeY[i] + _treeH[i] };
            }
            Kaiju k = w.Kaiju;
            if (w.Match.Hatched && k.State != KaijuState.Egg)
            {
                double a = Driver.Alpha;
                double kx = k.PrevX + (k.X - k.PrevX) * a, kz = k.PrevZ + (k.Z - k.PrevZ) * a;
                double ky = GroundHeight.At(w.Map, kx, kz);
                _cyl[n++] = new SeeThroughCylinder { X = kx, Z = kz, R = w.Config.Kaiju.Radius, Y0 = ky, Y1 = ky + w.Config.Render.KaijuHeight };
            }
            return n;
        }

        private void RefreshTrees()
        {
            if (_world == null && Driver != null) _world = Driver.GetComponent<WorldView>();
            if (_world == null || _world.Plan == null) { _treeCount = 0; return; }
            if (_treeVersion == WorldView.DressVersion) return;
            _treeVersion = WorldView.DressVersion;
            _treeCount = 0;
            List<WorldPlacement> ps = _world.Plan.Placements;
            for (int i = 0; i < ps.Count && _treeCount < MaxTrees; i++)
            {
                WorldPlacement p = ps[i];
                if (p.Cat != "tree") continue;
                _treeX[_treeCount] = p.SimX;
                _treeZ[_treeCount] = p.SimZ;
                _treeY[_treeCount] = p.Y;
                _treeH[_treeCount] = p.Piece != null && p.Piece.IndexOf("tall", StringComparison.Ordinal) >= 0 ? TreeHeightTall : TreeHeightRound;
                _treeCount++;
            }
        }

        /// <summary>
        /// ActorViews names actor 0's object "Actor 0" (capsule) or "Actor 0 &lt;model&gt;"; it is looked up only while the
        /// cached one is missing or hidden, at most every 15 frames (Transform.name allocates).
        /// </summary>
        private void FindPlayerRenderers()
        {
            if (_playerRoot != null && _playerRoot.gameObject.activeInHierarchy) return;
            _playerRoot = null;
            _rendererCount = 0;
            if (Time.frameCount < _nextLook) return;
            _nextLook = Time.frameCount + 15;
            if (_views == null && Driver != null) _views = Driver.GetComponent<ActorViews>();
            if (_views == null) return;
            Transform root = _views.transform;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform c = root.GetChild(i);
                if (!c.gameObject.activeInHierarchy) continue;
                string name = c.name;
                if (name != "Actor 0" && !name.StartsWith("Actor 0 ", StringComparison.Ordinal)) continue;
                _playerRoot = c;
                break;
            }
            if (_playerRoot == null) return;
            _scratch.Clear();
            _playerRoot.GetComponentsInChildren(false, _scratch);
            for (int i = 0; i < _scratch.Count && _rendererCount < MaxRenderers; i++)
            {
                Renderer r = _scratch[i];
                // Contract C4: the player is always LOD0; the Hero, LOD1 and shadow-proxy meshes are never drawn as the silhouette.
                var role = CriticalMass.Runtime.View.Art.FighterLod.RoleOf(r.name);
                if (role != CriticalMass.Runtime.View.Art.FighterLod.Role.Lod0 && role != CriticalMass.Runtime.View.Art.FighterLod.Role.Shared) continue;
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else if (r is MeshRenderer)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf != null) mesh = mf.sharedMesh;
                }
                if (mesh == null) continue;
                _renderers[_rendererCount] = r;
                _submeshes[_rendererCount] = Mathf.Max(1, mesh.subMeshCount);
                _rendererCount++;
            }
        }

        private void OnBeginCamera(ScriptableRenderContext context, UnityEngine.Camera cam)
        {
            if (Fade <= 0f || _rendererCount == 0 || cam != UnityEngine.Camera.main) return;
            Material m = SeeThroughMaterial;
            if (m == null) return;
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data == null || data.scriptableRenderer == null) return;
            if (_pass == null) _pass = new SilhouettePass();
            _pass.Set(m, _renderers, _submeshes, _rendererCount);
            data.scriptableRenderer.EnqueuePass(_pass);
        }

        /// <summary>Draws the given renderers with the see-through material after the opaques, before the transparents
        /// (the Screen Space - Camera HUD is a transparent, so the silhouette stays under it).</summary>
        private sealed class SilhouettePass : ScriptableRenderPass
        {
            private Material _mat;
            private Renderer[] _renderers;
            private int[] _submeshes;
            private int _count;

            private sealed class PassData
            {
                public Material Mat;
                public Renderer[] Renderers;
                public int[] Submeshes;
                public int Count;
            }

            public SilhouettePass()
            {
                renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            }

            public void Set(Material m, Renderer[] renderers, int[] submeshes, int count)
            {
                _mat = m;
                _renderers = renderers;
                _submeshes = submeshes;
                _count = count;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                if (_mat == null || _count == 0 || !resources.activeColorTexture.IsValid() || !resources.activeDepthTexture.IsValid()) return;
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("CM See Through", out PassData data))
                {
                    data.Mat = _mat;
                    data.Renderers = _renderers;
                    data.Submeshes = _submeshes;
                    data.Count = _count;
                    builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.Write);
                    builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.Read);
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData d, RasterGraphContext ctx) =>
                    {
                        for (int i = 0; i < d.Count; i++)
                        {
                            Renderer r = d.Renderers[i];
                            if (r == null || !r.enabled || !r.gameObject.activeInHierarchy) continue;
                            for (int s = 0; s < d.Submeshes[i]; s++) ctx.cmd.DrawRenderer(r, d.Mat, s, 0);
                        }
                    });
                }
            }
        }
    }
}
