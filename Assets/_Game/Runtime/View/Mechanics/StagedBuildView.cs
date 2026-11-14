using System.Collections.Generic;
using CriticalMass.Runtime.Audio.Sound;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.View.Anim;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// feat/staged-building-1: built ladders and rope bridges draw as the authored BuilderLadder and BuilderRopeBridge models
    /// (scaled to the span, repeated along it), carried ones as the authored carried pose, and every new ladder, bridge or
    /// Miner barricade plays a short construction (StagedBuildCore): posts or stakes first, then planks, rungs or beams one
    /// after another with a drop and settle, a knock and a dust puff each, the BuilderBuildGlow shrinking away at the end,
    /// and the builder turned toward the work swinging his arm (a procedural swing on the KayKit rig, upperarm.r). View only:
    /// it reads the World and never writes it. Costs nothing while no build plays beyond drawing the finished pieces
    /// (RenderMesh, no GameObjects, no allocations per frame). Installed by MechanicsWorldViewInstaller.
    /// </summary>
    public sealed class StagedBuildView : MonoBehaviour, IPostPoseListener
    {
        public static StagedBuildView Active { get; private set; }
        /// <summary>Captures and tests may force the switch (null = WorldFeedbackSettings.BuildAnimations).</summary>
        public static bool? EnabledOverride;

        public static bool AnimationsOn => EnabledOverride ?? WorldFeedbackSettings.BuildAnimations;

        public readonly StagedBuildCore.Tracker Tracker = new StagedBuildCore.Tracker();

        /// <summary>A dust puff (wall clock start, world position, radius); MechanicsWorldView draws them.</summary>
        public struct Puff { public Vector3 P; public float T; public float R; }
        public const int PuffCount = 32;
        public const float PuffLife = 0.55f;
        public readonly Puff[] Puffs = new Puff[PuffCount];
        int _puffNext;

        /// <summary>One authored mesh ready to draw: the whole mesh, its staged cut, its import transform and team materials.</summary>
        sealed class Model
        {
            public Mesh Whole;
            public Mesh[] Stages;
            public Vector3[] StageCenter;
            public Matrix4x4 Local = Matrix4x4.identity;
            public Bounds Bounds;
            public Material Azure, Crimson;
            public float TopSign = -1f;
            public int Kind;
            public bool Cuttable, CutTried;
            public int StageCount => Stages != null ? Stages.Length : 1;
            public Material For(TeamId t) => t == TeamId.Azure ? Azure : Crimson;
        }

        Model _ladder, _bridge, _barricade, _ladderCarried, _bridgeCarried, _glow;
        bool _loaded;
        int _observedFrame = -1;
        SimDriverBehaviour _driver;
        ActorViews _actorViews;
        int _actorViewsWait;
        Transform[] _barTf = new Transform[0];
        Vector3[] _barScale = new Vector3[0];
        bool[] _barHidden = new bool[0];
        string[] _barNames = new string[0];
        int _knockFrame = -1;

        /// <summary>Counters for tests and captures.</summary>
        public int PiecesDrawn { get; private set; }
        public int StagesDrawn { get; private set; }
        public bool ModelsReady => _bridge != null && _ladder != null;
        public bool Split => _bridge != null && _bridge.Stages != null;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            Active = this;
            if (StagedBuildCapture.Requested && GetComponent<StagedBuildCapture>() == null) gameObject.AddComponent<StagedBuildCapture>();
        }

        void OnEnable() => PostPoseHooks.Register(this);

        void OnDisable() => PostPoseHooks.Unregister(this);

        void OnDestroy()
        {
            if (Active == this) Active = null;
            RestoreAllBarricades();
            // the cut meshes live in CutCache (made once per process, shared by every world): nothing to destroy here
        }

        float Now => Time.unscaledTime;

        static bool ReduceMotion => FeelGate.ReduceMotion;

        // ------------------------------------------------------------------ loading (once per match view)

        void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            ArtCatalog cat = ArtCatalog.Load();
            if (cat == null) return;
            _ladder = Load(cat, StagedBuildCore.KindLadder, StagedBuildCore.PlacedMesh(StagedBuildCore.KindLadder), true);
            _bridge = Load(cat, StagedBuildCore.KindBridge, StagedBuildCore.PlacedMesh(StagedBuildCore.KindBridge), true);
            _barricade = Load(cat, StagedBuildCore.KindBarricade, StagedBuildCore.PlacedMesh(StagedBuildCore.KindBarricade), true);
            _ladderCarried = Load(cat, StagedBuildCore.KindLadder, ArtMapping.LadderCarried, false);
            _bridgeCarried = Load(cat, StagedBuildCore.KindBridge, ArtMapping.RopeBridgeCarried, false);
            ArtEntry ge = cat.Find(ArtMapping.BuilderBuildGlow);
            if (ge != null) _glow = FromEntry(ge, ArtMapping.BuilderBuildGlow, ArtMapping.BuilderBuildGlow, false, 0);
        }

        static Model Load(ArtCatalog cat, int kind, string meshName, bool split)
        {
            string key = StagedBuildCore.ModelKey(kind);
            ArtEntry e = key != null ? cat.Find(key) : null;
            return e != null ? FromEntry(e, key, meshName, split, kind) : null;
        }

        static Model FromEntry(ArtEntry e, string key, string meshName, bool split, int kind)
        {
            ModelSpec spec = ArtMapping.Spec(key);
            if (e.Model == null || spec == null) return null;
            MeshFilter found = null;
            foreach (MeshFilter mf in e.Model.GetComponentsInChildren<MeshFilter>(true))
                if (mf.name == meshName && mf.sharedMesh != null) { found = mf; break; }
            if (found == null) return null;
            var m = new Model
            {
                Whole = found.sharedMesh,
                Local = e.Model.transform.worldToLocalMatrix * found.transform.localToWorldMatrix,
                Bounds = found.sharedMesh.bounds,
                Azure = ArtMaterials.ForTeam(spec, TeamId.Azure),
                Crimson = ArtMaterials.ForTeam(spec, TeamId.Crimson),
            };
            // the root's own import scale (ModelView keeps it as BaseScale)
            m.Local = Matrix4x4.Scale(e.Model.transform.localScale) * m.Local;
            m.Kind = kind;
            m.Cuttable = split && m.Whole.isReadable;
            if (m.Cuttable && kind == StagedBuildCore.KindLadder) m.TopSign = LadderTopSign(m.Whole, m.Bounds);
            return m;
        }

        /// <summary>Cuts a readable mesh into stage meshes (StagedBuildCore.Partition), keeping every vertex stream.</summary>
        /// <summary>
        /// The cut stage meshes per source mesh, made on the first staged build that needs them and kept for the process,
        /// so world rebuilds never add meshes (TechHardeningPlayTests) and nothing is cut while no build plays.
        /// </summary>
        static readonly Dictionary<Mesh, Mesh[]> CutCache = new Dictionary<Mesh, Mesh[]>();
        static readonly Dictionary<Mesh, Vector3[]> CutCentres = new Dictionary<Mesh, Vector3[]>();

        static void EnsureCut(Model m)
        {
            if (m == null || m.CutTried) return;
            m.CutTried = true;
            if (!m.Cuttable) return;
            if (CutCache.TryGetValue(m.Whole, out Mesh[] cached) && Alive(cached))
            {
                m.Stages = cached;
                m.StageCenter = CutCentres[m.Whole];
                return;
            }
            Cut(m, m.Kind);
            CutCache[m.Whole] = m.Stages;
            CutCentres[m.Whole] = m.StageCenter;
        }

        /// <summary>False when a cached cut was destroyed (domain reload off: an earlier play session's meshes).</summary>
        static bool Alive(Mesh[] stages)
        {
            if (stages == null) return true;
            foreach (Mesh s in stages) if (s == null) return false;
            return true;
        }

        /// <summary>Which way the ladder climbs (art +Z or -Z): the mean z of its top verts against its foot verts.</summary>
        static float LadderTopSign(Mesh mesh, Bounds b)
        {
            var v = new List<Vector3>();
            mesh.GetVertices(v);
            float top = b.max.y, zt = 0f, zb = 0f; int nt = 0, nb = 0;
            for (int i = 0; i < v.Count; i++)
            {
                if (v[i].y > top * 0.85f) { zt += v[i].z; nt++; }
                else if (v[i].y < top * 0.15f) { zb += v[i].z; nb++; }
            }
            return nt > 0 && nb > 0 && zt / nt > zb / nb ? 1f : -1f;
        }

        static void Cut(Model m, int kind)
        {
            Mesh src = m.Whole;
            var v = new List<Vector3>(); src.GetVertices(v);
            var n = new List<Vector3>(); src.GetNormals(n);
            var col = new List<Color>(); src.GetColors(col);
            var uvs = new List<Vector4>[4];
            for (int ch = 0; ch < 4; ch++) { uvs[ch] = new List<Vector4>(); src.GetUVs(ch, uvs[ch]); }
            int sub = src.subMeshCount;
            var all = new List<int>();
            var triSub = new List<int>();
            var tmp = new List<int>();
            for (int s = 0; s < sub; s++)
            {
                tmp.Clear();
                src.GetTriangles(tmp, s);
                all.AddRange(tmp);
                for (int i = 0; i < tmp.Count / 3; i++) triSub.Add(s);
            }
            var triStage = new int[all.Count / 3];
            int stages = StagedBuildCore.Partition(v, all, kind, StagedBuildCore.MaxStages, triStage);
            if (stages <= 1) return;
            m.Stages = new Mesh[stages];
            m.StageCenter = new Vector3[stages];
            for (int st = 0; st < stages; st++)
            {
                var map = new Dictionary<int, int>();
                var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nc = new List<Color>();
                var nu = new List<Vector4>[4];
                for (int ch = 0; ch < 4; ch++) nu[ch] = new List<Vector4>();
                var subTris = new List<int>[sub];
                for (int s = 0; s < sub; s++) subTris[s] = new List<int>();
                for (int t = 0; t < triStage.Length; t++)
                {
                    if (triStage[t] != st) continue;
                    for (int k = 0; k < 3; k++)
                    {
                        int o = all[t * 3 + k];
                        if (!map.TryGetValue(o, out int ni))
                        {
                            ni = nv.Count;
                            map[o] = ni;
                            nv.Add(v[o]);
                            if (n.Count == v.Count) nn.Add(n[o]);
                            if (col.Count == v.Count) nc.Add(col[o]);
                            for (int ch = 0; ch < 4; ch++) if (uvs[ch].Count == v.Count) nu[ch].Add(uvs[ch][o]);
                        }
                        subTris[triSub[t]].Add(ni);
                    }
                }
                var mesh = new Mesh { name = src.name + " stage " + st };
                mesh.SetVertices(nv);
                if (nn.Count == nv.Count) mesh.SetNormals(nn);
                if (nc.Count == nv.Count) mesh.SetColors(nc);
                for (int ch = 0; ch < 4; ch++) if (nu[ch].Count == nv.Count && nv.Count > 0) mesh.SetUVs(ch, nu[ch]);
                mesh.subMeshCount = sub;
                for (int s = 0; s < sub; s++) mesh.SetTriangles(subTris[s], s, false);
                mesh.RecalculateBounds();
                m.Stages[st] = mesh;
                m.StageCenter[st] = mesh.bounds.center;
            }
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Looks for new builds once per frame (whoever draws first calls it; the sim has ticked by LateUpdate).</summary>
        void EnsureObserved(World w)
        {
            if (_observedFrame == Time.frameCount) return;
            _observedFrame = Time.frameCount;
            EnsureLoaded();
            Tracker.Observe(w, Now, AnimationsOn, 0, 0);
        }

        void LateUpdate()
        {
            if (_driver == null || _driver.World == null) return;
            World w = _driver.World;
            EnsureObserved(w);
            PiecesDrawn = 0;
            StagesDrawn = 0;
            DrawBarricades(w);
            Tracker.Expire(Now);
        }

        /// <summary>
        /// Draws a laid ladder or bridge as its authored model. c is the span centre (world), along the unit span direction,
        /// len its length and widthCells its lane width. hidden skips the draw (the warning blink's off beat). progress is
        /// the construction progress 0..1 while sequencing is true. Returns false when the model is missing (the caller then
        /// draws its procedural planks).
        /// </summary>
        public bool DrawPiece(World w, int slot, BuiltPiece p, Vector3 c, Vector3 along, float len, float widthCells, bool hidden,
            out float progress, out bool sequencing)
        {
            progress = 1f;
            sequencing = false;
            EnsureObserved(w);
            Model m = p.Kind == BuiltPieces.Bridge ? _bridge : _ladder;
            if (m == null) return false;
            float t = float.MaxValue;
            bool reduce = ReduceMotion;
            int groups = p.Kind == BuiltPieces.Bridge ? BridgeSegments(m, len) : 2;
            if (slot >= 0 && slot < Tracker.Pieces.Length && Tracker.Pieces[slot].On)
            {
                ref StagedBuildCore.Seq s = ref Tracker.Pieces[slot];
                if (s.Arrivals <= 0) { EnsureCut(m); s.Arrivals = groups * m.StageCount; s.Dur = StagedBuildCore.Duration(s.Arrivals); }
                t = Now - s.Start;
                if (StagedBuildCore.Done(t, s.Dur)) s.On = false;
                else
                {
                    sequencing = true;
                    progress = StagedBuildCore.Progress(t, s.Dur);
                    if (s.LastT < 0f)
                    {
                        AddPuff(c - along * (len * 0.5f), 0.55f);
                        AddPuff(c + along * (len * 0.5f), 0.55f);
                    }
                }
            }
            if (hidden && !sequencing) return true;
            int stages = m.StageCount;
            Material mat = m.For(p.Team);
            Quaternion q = Quaternion.LookRotation(along, Vector3.up);
            Vector3 across = Vector3.Cross(Vector3.up, along);
            ref StagedBuildCore.Seq seq = ref (slot >= 0 && slot < Tracker.Pieces.Length ? ref Tracker.Pieces[slot] : ref _none);
            int kind = p.Kind == BuiltPieces.Bridge ? StagedBuildCore.KindBridge : StagedBuildCore.KindLadder;
            if (p.Kind == BuiltPieces.Bridge)
            {
                Vector3 size = m.Bounds.size;
                float segLen = len / groups;
                float sz = segLen / Mathf.Max(0.1f, size.z);
                float sx = Mathf.Max(0.6f, (widthCells - 0.24f) / Mathf.Max(0.1f, size.x));
                Vector3 start = c - along * (len * 0.5f);
                for (int j = 0; j < groups; j++)
                {
                    Vector3 o = start + along * (j * segLen - m.Bounds.min.z * sz);
                    Matrix4x4 baseM = Matrix4x4.TRS(o, q, new Vector3(sx, 1f, sz));
                    DrawStaged(m, mat, baseM, j * stages, groups * stages, sequencing, t, reduce, ref seq, kind);
                }
            }
            else
            {
                // An A-frame over the wall: one leaning ladder pair up the near face, one down the far face, two lanes wide.
                float run = Mathf.Max(0.1f, m.Bounds.size.z), rise = Mathf.Max(0.1f, m.Bounds.max.y);
                float d = len * 0.5f + 0.35f;
                var scale = new Vector3(1.7f, 1.6f / rise, d / run);
                float footZ = m.TopSign < 0f ? m.Bounds.max.z : m.Bounds.min.z;
                for (int side = 0; side < 2; side++)
                {
                    float sgn = side == 0 ? -1f : 1f;
                    Vector3 foot = c + along * (d * sgn);
                    Vector3 toCentre = -along * sgn;
                    Quaternion qs = Quaternion.LookRotation(toCentre * m.TopSign, Vector3.up);
                    Vector3 o = foot - qs * new Vector3(0f, 0f, footZ * scale.z);
                    for (int lane = -1; lane <= 1; lane += 2)
                    {
                        Matrix4x4 baseM = Matrix4x4.TRS(o + across * (0.6f * lane), qs, scale);
                        DrawStaged(m, mat, baseM, side * stages, 2 * stages, sequencing, t, reduce, ref seq, kind);
                    }
                }
            }
            if (sequencing)
            {
                DrawGlow(c, p.Team, new Vector3(widthCells, 1f, len + 0.6f), q, t, seq.Dur);
                seq.LastT = t;
            }
            PiecesDrawn++;
            return true;
        }

        StagedBuildCore.Seq _none;

        int BridgeSegments(Model m, float len) => Mathf.Clamp(Mathf.CeilToInt(len / (Mathf.Max(0.5f, m.Bounds.size.z) * 1.15f)), 1, 4);

        void DrawStaged(Model m, Material mat, Matrix4x4 baseM, int firstArrival, int arrivals, bool sequencing, float t, bool reduce,
            ref StagedBuildCore.Seq seq, int kind)
        {
            if (!sequencing)
            {
                // finished: the whole authored mesh in one draw per segment (the cut only matters while it assembles)
                Render(m.Whole, mat, baseM * m.Local);
                return;
            }
            if (m.Stages == null)
            {
                float drop = 0f;
                if (sequencing)
                {
                    float at = StagedBuildCore.ArriveAt(firstArrival, arrivals, seq.Dur);
                    if (!StagedBuildCore.Pose(t, at, reduce, out drop)) return;
                    Landed(seq, t, at, baseM.MultiplyPoint3x4(m.Bounds.center), kind);
                }
                Render(m.Whole, mat, Lift(drop) * baseM * m.Local);
                return;
            }
            for (int s = 0; s < m.Stages.Length; s++)
            {
                float drop = 0f;
                if (sequencing)
                {
                    float at = StagedBuildCore.ArriveAt(firstArrival + s, arrivals, seq.Dur);
                    if (!StagedBuildCore.Pose(t, at, reduce, out drop)) continue;
                    Landed(seq, t, at, (baseM * m.Local).MultiplyPoint3x4(m.StageCenter[s]), kind);
                }
                Render(m.Stages[s], mat, Lift(drop) * baseM * m.Local);
            }
        }

        static Matrix4x4 Lift(float drop) => drop == 0f ? Matrix4x4.identity : Matrix4x4.Translate(new Vector3(0f, drop, 0f));

        /// <summary>The knock and the dust when an arrival lands this frame.</summary>
        void Landed(in StagedBuildCore.Seq seq, float t, float arrive, Vector3 at, int kind)
        {
            if (!StagedBuildCore.LandedBetween(seq.LastT, t, arrive)) return;
            AddPuff(at, 0.35f);
            if (_knockFrame == Time.frameCount) return;   // one knock per frame is plenty (two lanes land together)
            _knockFrame = Time.frameCount;
            Vector3 sim = new Vector3(at.x, 0f, -at.z);   // SimSpace.ToWorld flips z
            float pitch = 0.9f + 0.25f * Mathf.Repeat(arrive * 7.31f, 1f);
            SoundDirector.PlayCueAt(StagedBuildCore.KnockCue(kind), sim.x, sim.z, 0.55f, pitch);
        }

        void Render(Mesh mesh, Material mat, Matrix4x4 world)
        {
            if (mesh == null || mat == null) return;
            var rp = new RenderParams(mat)
            {
                shadowCastingMode = ShadowCastingMode.On,
                receiveShadows = true,
                layer = gameObject.layer,
            };
            for (int s = 0; s < mesh.subMeshCount; s++) Graphics.RenderMesh(rp, mesh, s, world);
            StagesDrawn++;
        }

        void DrawGlow(Vector3 c, TeamId team, Vector3 footprint, Quaternion q, float t, float dur)
        {
            if (_glow == null) return;
            float g = StagedBuildCore.GlowScale(t, dur);
            if (g <= 0.001f) return;
            Vector3 size = _glow.Bounds.size;
            var sc = new Vector3(footprint.x / Mathf.Max(0.1f, size.x) * g, g * footprint.y, footprint.z / Mathf.Max(0.1f, size.z) * g);
            Render(_glow.Whole, _glow.For(team), Matrix4x4.TRS(c, q, sc) * _glow.Local);
        }

        /// <summary>Draws a carried ladder or bridge as the authored carried pose over the carrier's head. False without the model.</summary>
        public bool DrawCarried(BuiltPiece p, Vector3 feet, float facingRad, bool hidden)
        {
            EnsureLoaded();
            Model m = p.Kind == BuiltPieces.Bridge ? _bridgeCarried : _ladderCarried;
            if (m == null) return false;
            if (hidden) return true;
            Vector3 dir = SimSpace.ToWorld(Mathf.Cos(facingRad), 0, Mathf.Sin(facingRad));
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
            Render(m.Whole, m.For(p.Team), Matrix4x4.TRS(feet + Vector3.up * 1.95f, Quaternion.LookRotation(dir, Vector3.up), Vector3.one) * m.Local);
            return true;
        }

        // ------------------------------------------------------------------ the Miner barricade: stakes, then cross-beams

        void DrawBarricades(World w)
        {
            StagedBuildCore.Seq[] seqs = Tracker.Barricades;
            if (_barTf.Length != seqs.Length)
            {
                RestoreAllBarricades();
                _barTf = new Transform[seqs.Length];
                _barScale = new Vector3[seqs.Length];
                _barHidden = new bool[seqs.Length];
                _barNames = new string[seqs.Length];
                for (int i = 0; i < seqs.Length; i++) _barNames[i] = "Barricade " + i;   // ActorViews.SyncBarricades names
            }
            bool reduce = ReduceMotion;
            for (int i = 0; i < seqs.Length; i++)
            {
                ref StagedBuildCore.Seq s = ref seqs[i];
                if (s.On && _barricade != null && s.Arrivals <= 0)
                {
                    EnsureCut(_barricade);
                    s.Arrivals = _barricade.StageCount;
                    s.Dur = StagedBuildCore.Duration(s.Arrivals);
                }
                float t = Now - s.Start;
                if (!s.On || _barricade == null || StagedBuildCore.Done(t, s.Dur))
                {
                    if (_barHidden[i]) Restore(i);
                    continue;
                }
                // hide the authored barricade ActorViews draws (scale only; it never writes scale) while ours assembles
                Transform tf = BarricadeTransform(i);
                if (tf != null && !_barHidden[i]) { _barScale[i] = tf.localScale; tf.localScale = Vector3.zero; _barHidden[i] = true; }
                Vector3 c = SimSpace.ToWorld(s.X, CriticalMass.Runtime.GroundHeight.At(w.Map, s.X, s.Z), s.Z);
                if (s.LastT < 0f) AddPuff(c, 0.6f);
                Matrix4x4 baseM = Matrix4x4.Translate(c);
                DrawStaged(_barricade, _barricade.For(s.Team), baseM, 0, _barricade.StageCount, true, t, reduce, ref s, StagedBuildCore.KindBarricade);
                DrawGlow(c, s.Team, new Vector3(1.6f, 1f, 1.2f), Quaternion.identity, t, s.Dur);
                s.LastT = t;
            }
        }

        Transform BarricadeTransform(int i)
        {
            if (_barTf[i] != null) return _barTf[i];
            if (_actorViews == null)
            {
                if (--_actorViewsWait > 0) return null;
                _actorViewsWait = 30;
                _actorViews = FindAnyObjectByType<ActorViews>();
                if (_actorViews == null) return null;
            }
            _barTf[i] = _actorViews.transform.Find(_barNames[i]);
            return _barTf[i];
        }

        void Restore(int i)
        {
            if (i < _barTf.Length && _barTf[i] != null && _barHidden[i]) _barTf[i].localScale = _barScale[i];
            if (i < _barHidden.Length) _barHidden[i] = false;
        }

        void RestoreAllBarricades()
        {
            for (int i = 0; i < _barHidden.Length; i++) if (_barHidden[i]) Restore(i);
        }

        // ------------------------------------------------------------------ dust

        void AddPuff(Vector3 p, float r)
        {
            Puffs[_puffNext] = new Puff { P = p, T = Now, R = r };
            _puffNext = (_puffNext + 1) % PuffCount;
        }

        /// <summary>Age of puff i in seconds (negative or past PuffLife: not shown).</summary>
        public float PuffAge(int i) => Puffs[i].R <= 0f ? -1f : Now - Puffs[i].T;

        /// <summary>Running construction progress at a barricade slot (false when none plays).</summary>
        public bool BarricadeProgress(int i, out float progress, out Vector3 at, out TeamId team)
        {
            progress = 0f; at = Vector3.zero; team = TeamId.Azure;
            if (i < 0 || i >= Tracker.Barricades.Length || !Tracker.Barricades[i].On) return false;
            StagedBuildCore.Seq s = Tracker.Barricades[i];
            progress = StagedBuildCore.Progress(Now - s.Start, s.Dur);
            at = new Vector3((float)s.X, 0f, (float)s.Z);
            team = s.Team;
            return true;
        }

        // ------------------------------------------------------------------ the builder at work

        public void OnPostPose(int actor, ModelView mv, in ActorPoseState s)
        {
            if (actor < 0 || s.IsKaiju || s.Corpse || mv == null) return;
            if (!Tracker.BuildingBy(actor, out StagedBuildCore.Seq seq)) return;
            float t = Now - seq.Start;
            if (t < 0f || t >= seq.Dur) return;
            float env = Mathf.Clamp01(t / 0.08f) * Mathf.Clamp01((seq.Dur - t) / 0.1f);
            Transform root = mv.Transform;
            Vector3 fwd = Quaternion.Euler(0f, s.Yaw, 0f) * Vector3.forward;
            Vector3 site = SimSpace.ToWorld(seq.X, 0, seq.Z);
            Vector3 to = site - root.position;
            to.y = 0f;
            if (to.sqrMagnitude > 0.04f)
            {
                float yaw = Vector3.SignedAngle(fwd, to, Vector3.up);
                root.rotation = Quaternion.AngleAxis(yaw * env, Vector3.up) * root.rotation;
                fwd = Quaternion.AngleAxis(yaw * env, Vector3.up) * fwd;
            }
            if (ReduceMotion || !s.Evaluated) return;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            // one hammer stroke per arrival: a slow lift, a quick strike
            float period = Mathf.Max(0.14f, seq.Dur / Mathf.Max(1, seq.Arrivals > 0 ? Mathf.Min(seq.Arrivals, 8) : 6));
            float ph = Mathf.Repeat(t, period) / period;
            float raise = ph < 0.7f ? ph / 0.7f : 1f - (ph - 0.7f) / 0.3f;
            root.rotation = Quaternion.AngleAxis(7f * env * (1f - raise), right) * root.rotation;
            Transform arm = mv.Bone("upperarm.r") ?? mv.Bone("UpperArm.R");
            if (arm != null) arm.rotation = Quaternion.AngleAxis(-125f * raise * env, right) * arm.rotation;
            Transform fore = mv.Bone("lowerarm.r") ?? mv.Bone("LowerArm.R");
            if (fore != null) fore.rotation = Quaternion.AngleAxis(-35f * raise * env, right) * fore.rotation;
        }
    }
}
