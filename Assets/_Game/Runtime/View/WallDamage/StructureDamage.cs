using System.Collections.Generic;
using CriticalMass.Runtime.View.Affordance;
using CriticalMass.Runtime.View.Art;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.View.WallDamage
{
    /// <summary>Reads the hit pulses WorldReactions already decodes (it is the one event reader for structures).</summary>
    public static class DamageHits
    {
        /// <summary>The newest pulse of kind/phase within radius of (x, z) and younger than maxAge (s). Allocation-free.</summary>
        public static bool Latest(ReactKind kind, byte phase, double x, double z, double radius, double now, double maxAge, out ReactPulse best)
        {
            best = default;
            WorldReactions rx = WorldReactions.Current;
            if (rx == null) return false;
            bool found = false;
            double r2 = radius * radius;
            ReactPulse[] ps = rx.Pulses;
            for (int i = 0; i < ps.Length; i++)
            {
                ReactPulse p = ps[i];
                if (p.Kind != kind || p.Phase != phase) continue;
                double age = now - p.T0;
                if (!(age >= 0 && age < maxAge)) continue;
                double dx = p.X - x, dz = p.Z - z;
                if (dx * dx + dz * dz > r2) continue;
                if (!found || p.T0 > best.T0) { best = p; found = true; }
            }
            return found;
        }

        public static Vector3 WorldDir(in ReactPulse p, int seed)
        {
            Vector3 d = SimSpace.ToWorld(p.DirX, 0, p.DirZ);
            if (d.sqrMagnitude < 1e-6f)
            {
                float a = DamageLook.Hash01(seed) * Mathf.PI * 2f;
                d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            }
            return d.normalized;
        }
    }

    /// <summary>
    /// Pooled rubble (ART-DIRECTION: debris persists 20 s, at most 60 pieces): a broken barricade's planks and a broken wall
    /// cell's chunks fly outward from the last hit, land, rest 20 s and sink out over the last 1.5 s. A fixed ring of
    /// DamageLook.MaxRubble objects made once; the oldest is reused past the cap. Costs nothing while no piece is alive.
    /// </summary>
    public sealed class RubblePool : MonoBehaviour
    {
        static RubblePool _inst;
        Transform[] _t;
        MeshFilter[] _mf;
        MeshRenderer[] _mr;
        readonly double[] _start = new double[DamageLook.MaxRubble];
        readonly Vector3[] _vel = new Vector3[DamageLook.MaxRubble], _spin = new Vector3[DamageLook.MaxRubble], _scale = new Vector3[DamageLook.MaxRubble];
        readonly float[] _ground = new float[DamageLook.MaxRubble];
        readonly bool[] _resting = new bool[DamageLook.MaxRubble];
        int _alive;

        /// <summary>Pieces alive now (tests and the capture log).</summary>
        public static int Alive => _inst != null ? _inst._alive : 0;

        static RubblePool Get()
        {
            if (_inst != null) return _inst;
            var go = new GameObject("CM Rubble");
            _inst = go.AddComponent<RubblePool>();
            _inst.Make();
            return _inst;
        }

        void Make()
        {
            int n = DamageLook.MaxRubble;
            _t = new Transform[n];
            _mf = new MeshFilter[n];
            _mr = new MeshRenderer[n];
            for (int i = 0; i < n; i++)
            {
                var g = new GameObject("Rubble " + i);
                g.transform.SetParent(transform, false);
                _t[i] = g.transform;
                _mf[i] = g.AddComponent<MeshFilter>();
                _mr[i] = g.AddComponent<MeshRenderer>();
                _mr[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                g.SetActive(false);
                _start[i] = double.NegativeInfinity;
            }
        }

        void OnDestroy() { if (_inst == this) _inst = null; }

        /// <summary>Throws one piece: world pose, velocity (u/s), spin (deg/s), the ground height it lands on.</summary>
        public static void Spawn(Mesh mesh, Material[] mats, Vector3 pos, Quaternion rot, Vector3 scale, Vector3 vel, Vector3 spin, float groundY)
        {
            if (mesh == null || mats == null) return;
            RubblePool p = Get();
            double now = Time.unscaledTimeAsDouble;
            int i = PoolPick.Pick(p._start, now, DamageLook.RubbleSeconds);
            if (double.IsNegativeInfinity(p._start[i]) || now - p._start[i] >= DamageLook.RubbleSeconds) p._alive++;
            p._start[i] = now;
            p._mf[i].sharedMesh = mesh;
            p._mr[i].sharedMaterials = mats;
            p._t[i].SetPositionAndRotation(pos, rot);
            p._t[i].localScale = scale;
            p._scale[i] = scale;
            p._vel[i] = vel;
            p._spin[i] = spin;
            p._ground[i] = groundY;
            p._resting[i] = false;
            if (!p._t[i].gameObject.activeSelf) p._t[i].gameObject.SetActive(true);
        }

        void Update()
        {
            if (_alive <= 0) return;
            double now = Time.unscaledTimeAsDouble;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            int alive = 0;
            for (int i = 0; i < _start.Length; i++)
            {
                if (double.IsNegativeInfinity(_start[i])) continue;
                float age = (float)(now - _start[i]);
                if (!DamageLook.RubbleAlive(age))
                {
                    _start[i] = double.NegativeInfinity;
                    _t[i].gameObject.SetActive(false);
                    continue;
                }
                alive++;
                Transform t = _t[i];
                Vector3 p = t.position;
                if (!_resting[i])
                {
                    _vel[i] += Vector3.down * 9.8f * dt;
                    p += _vel[i] * dt;
                    t.rotation = Quaternion.Euler(_spin[i] * dt) * t.rotation;
                    if (p.y <= _ground[i])
                    {
                        p.y = _ground[i];
                        if (_vel[i].y < -2f) { _vel[i] = new Vector3(_vel[i].x * 0.35f, -_vel[i].y * 0.25f, _vel[i].z * 0.35f); _spin[i] *= 0.4f; }
                        else _resting[i] = true;
                    }
                }
                float sink = DamageLook.RubbleSink(age);
                if (sink > 0f)
                {
                    p.y = _ground[i] - sink * 0.35f;
                    t.localScale = _scale[i] * (1f - 0.6f * sink);
                }
                t.position = p;
            }
            _alive = alive;
        }
    }

    /// <summary>
    /// The Miner barricade's damage look, applied by ActorViews after it places the authored model: from the first lost hit
    /// the authored deployed mesh is drawn as its own planks and posts (cut once at load, shared by every slot), each knocked
    /// askew by a seeded pose per stage, with crack strips on their faces, some knocked out at the late stages and the whole
    /// thing leaning at the last; each hit shakes it 0.12 s along the blow and plays a wood knock; the break throws the
    /// pieces outward from the last hit as rubble. When the mesh is not readable only the lean and shake show.
    /// </summary>
    public static class BarricadeDamage
    {
        sealed class Slot
        {
            public GameObject Root;
            public Transform[] Pieces;
            public MeshRenderer[] Renderers;
            public MeshFilter[] CrackFilters;
            public int Stage = -1;
            public bool WasOn;
            public double LastHitT0 = double.NegativeInfinity;
            public Vector3 LastDir = Vector3.forward;
            public Material[] Mats;
        }

        static MeshPiece[] _pieces;
        static bool _tried;
        static Slot[] _slots = new Slot[0];
        /// <summary>Tests and the capture: stage last shown per slot.</summary>
        public static int StageShown(int slot) => slot >= 0 && slot < _slots.Length && _slots[slot] != null ? _slots[slot].Stage : -1;

        static MeshPiece[] Pieces(ModelView m)
        {
            if (_tried) return _pieces;
            _tried = true;
            for (int k = 0; k < m.Renderers.Length; k++)
            {
                if (m.RendererNames[k] != ArtMapping.BarricadeDeployed || m.Renderers[k] == null) continue;
                MeshFilter mf = m.Renderers[k].GetComponent<MeshFilter>();
                if (mf != null) _pieces = MeshPieces.TryIslands(mf.sharedMesh, DamageLook.MaxPiecesPerBreak, 4242);
            }
            return _pieces;
        }

        static Renderer Deployed(ModelView m)
        {
            for (int k = 0; k < m.Renderers.Length; k++) if (m.RendererNames[k] == ArtMapping.BarricadeDeployed) return m.Renderers[k];
            return null;
        }

        static Slot Ensure(int i, ModelView m)
        {
            if (_slots.Length <= i) System.Array.Resize(ref _slots, i + 1);
            Slot s = _slots[i];
            Renderer depNow = Deployed(m);
            // Reuse the slot's pieces unless the model was rebuilt (a new match or scene destroyed the old ones).
            if (s != null && (s.Pieces == null || (s.Root != null && depNow != null && s.Root.transform.parent == depNow.transform))) return s;
            s = _slots[i] = new Slot();
            MeshPiece[] ps = Pieces(m);
            Renderer dep = Deployed(m);
            if (ps == null || dep == null) return s;
            s.Root = new GameObject("Barricade pieces");
            Transform parent = dep.transform;
            s.Root.transform.SetParent(parent, false);
            s.Pieces = new Transform[ps.Length];
            s.Renderers = new MeshRenderer[ps.Length];
            s.CrackFilters = new MeshFilter[ps.Length];
            Material crack = MeshPieces.CrackMaterial();
            for (int k = 0; k < ps.Length; k++)
            {
                var g = new GameObject("Piece " + k);
                g.transform.SetParent(s.Root.transform, false);
                g.transform.localPosition = ps[k].Pivot;
                g.AddComponent<MeshFilter>().sharedMesh = ps[k].Mesh;
                s.Renderers[k] = g.AddComponent<MeshRenderer>();
                s.Pieces[k] = g.transform;
                var cg = new GameObject("Cracks");
                cg.transform.SetParent(g.transform, false);
                s.CrackFilters[k] = cg.AddComponent<MeshFilter>();
                var cr = cg.AddComponent<MeshRenderer>();
                cr.sharedMaterial = crack;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            s.Root.SetActive(false);
            return s;
        }

        /// <summary>Called by ActorViews.SyncBarricades for slot i after it placed the model (on = the barricade stands).</summary>
        public static void Sync(int i, ModelView m, Barricade b, double maxHp, bool on)
        {
            if (m == null || b == null) return;
            Slot s = Ensure(i, m);
            double now = Time.unscaledTimeAsDouble;
            double x = b.Cx + 0.5, z = b.Cz + 0.5;
            if (!on)
            {
                if (s.WasOn) Break(s, m, x, z, i);
                s.WasOn = false;
                s.Stage = -1;
                if (s.Root != null && s.Root.activeSelf) s.Root.SetActive(false);
                return;
            }
            if (!s.WasOn) { s.LastHitT0 = now; s.Stage = -1; }
            s.WasOn = true;
            int stage = DamageLook.Stage(b.Hp / System.Math.Max(1, maxHp));
            // The newest hit on this barricade (or a Kaiju contact): direction, shake, a wood knock once per hit.
            float shake = 0f;
            if (DamageHits.Latest(ReactKind.Barricade, 0, x, z, 0.6, now, 1.0, out ReactPulse p))
            {
                s.LastDir = DamageHits.WorldDir(p, i * 31 + 5);
                shake = DamageLook.Shake((float)(now - p.T0), (float)p.Strength);
                if (p.T0 > s.LastHitT0)
                {
                    s.LastHitT0 = p.T0;
                    CriticalMass.Runtime.Audio.Sound.SoundDirector.PlayCueAt(DamageLook.CueFor(DamageMaterial.Wood, false), x, z);
                }
            }
            Transform t = m.Transform;
            if (shake != 0f) t.localPosition += s.LastDir * shake;
            float lean = DamageLook.LeanDeg(stage);
            if (lean > 0f) t.localRotation = Quaternion.AngleAxis(lean, Vector3.Cross(Vector3.up, s.LastDir)) * t.localRotation;
            if (s.Root == null) { s.Stage = stage; return; }
            bool pieces = stage >= 1;
            if (s.Root.activeSelf != pieces) s.Root.SetActive(pieces);
            if (!pieces) { s.Stage = stage; return; }
            // The pieces stand in for the authored mesh: hide both authored states, wear its (team) materials.
            Renderer dep = Deployed(m);
            for (int k = 0; k < m.Renderers.Length; k++) if (m.Renderers[k] != null && m.Renderers[k].enabled) m.Renderers[k].enabled = false;
            if (stage != s.Stage)
            {
                if (dep != null) s.Mats = dep.sharedMaterials;
                MeshPiece[] ps = _pieces;
                for (int k = 0; k < s.Pieces.Length; k++)
                {
                    DamageLook.PiecePose(i * 977 + 13, k, stage, out Vector3 tilt, out Vector3 slide, out bool missing);
                    Transform pt = s.Pieces[k];
                    pt.localPosition = ps[k].Pivot + slide;
                    pt.localRotation = Quaternion.Euler(tilt);
                    if (s.Mats != null) s.Renderers[k].sharedMaterials = s.Mats;
                    bool show = !missing;
                    if (pt.gameObject.activeSelf != show)
                    {
                        // A plank knocked out: it drops off as rubble, never just vanishes.
                        if (!show) Throw(ps[k], s.Mats, pt, s.LastDir, 0.6f, t.position.y);
                        pt.gameObject.SetActive(show);
                    }
                    s.CrackFilters[k].sharedMesh = ps[k].Cracks != null ? ps[k].Cracks[Mathf.Clamp(stage, 1, 3) - 1] : null;
                }
                s.Stage = stage;
            }
        }

        static void Throw(MeshPiece piece, Material[] mats, Transform from, Vector3 dir, float power, float groundY)
        {
            if (piece == null || mats == null) return;
            int h = from.GetInstanceID();
            float a = DamageLook.Hash01(h), c = DamageLook.Hash01(h + 7);
            Vector3 outward = (from.position - from.parent.position);
            outward.y = 0f;
            Vector3 v = (dir * (1.4f + a) + outward.normalized * 1.2f) * power + Vector3.up * (2.2f + 1.5f * c) * power;
            Vector3 spin = new Vector3((a - 0.5f) * 540f, (c - 0.5f) * 360f, (a - c) * 540f);
            RubblePool.Spawn(piece.Mesh, mats, from.position, from.rotation, from.lossyScale, v, spin, groundY + 0.04f);
        }

        static void Break(Slot s, ModelView m, double x, double z, int i)
        {
            float groundY = m.Transform.position.y;
            if (s.Pieces != null)
            {
                if (s.Mats == null) { Renderer dep = Deployed(m); if (dep != null) s.Mats = dep.sharedMaterials; }
                for (int k = 0; k < s.Pieces.Length; k++)
                {
                    if (s.Stage >= 1 && !s.Pieces[k].gameObject.activeSelf) continue;   // already knocked out
                    if (s.Stage < 1) { s.Pieces[k].localPosition = _pieces[k].Pivot; s.Pieces[k].localRotation = Quaternion.identity; }
                    Throw(_pieces[k], s.Mats, s.Pieces[k], s.LastDir, 1f, groundY);
                    s.Pieces[k].gameObject.SetActive(true);
                }
            }
            // The break sound itself (barricade_break, wood) is played by the sound feed from the Sim's BarricadeBroken event.
        }
    }

    /// <summary>
    /// One authored breakable wall section drawn as one piece PER SIM CELL (the root fix for "one broken cell flips the
    /// whole section"): each cell shows its own slice of the authored intact, cracked or destroyed model, chosen from that
    /// cell's type only, so neighbours keep their own look. A cracked or chipped cell carries crack strips; a hit shakes only
    /// that cell 0.12 s along the blow; a broken cell throws its slice outward as rubble and shows the authored rubble and
    /// the gap. Built once at load from readable bases meshes; null (the old whole-section swap) when they are not.
    /// </summary>
    public sealed class BreakableCellSet
    {
        public readonly int[] Cells;
        readonly MeshPiece[][] _byLook;        // [look 1..3 -> 0..2][cell]
        readonly MeshFilter[] _mf;
        readonly MeshRenderer[] _mr;
        readonly MeshFilter[] _crack;
        readonly Transform[] _t;
        readonly byte[] _look;
        readonly int[] _crackStage;
        readonly MeshPiece[][] _chunks;        // per cell: islands of its intact slice, thrown on the break
        Material[] _mats;
        readonly Transform _section;
        readonly bool[] _shaking;
        /// <summary>wall-damage-2: the cracked stage's darker tint, and each cell's resting pose (a cracked block sits askew).</summary>
        readonly MeshFilter[] _tint;
        readonly Vector3[] _basePos;
        readonly Quaternion[] _baseRot;
        /// <summary>Darker tint over a cracked cell (drawn with the crack material: unlit, see-through).</summary>
        // capture-gaps-1: alpha 0x6A to 0x9C, so a cracked block is clearly darker than its intact neighbours from the gameplay camera.
        public static readonly Color32 CrackedTint = new Color32(0x16, 0x12, 0x1A, 0x9C);

        static BreakableCellSet[] _byCell = new BreakableCellSet[0];
        static int[] _localByCell = new int[0];
        static readonly List<BreakableCellSet> _all = new List<BreakableCellSet>();

        /// <summary>Look currently drawn for map cell i (tests and the capture): None when no set draws it.</summary>
        public static CellLook Shown(int i) =>
            i >= 0 && i < _byCell.Length && _byCell[i] != null ? (CellLook)_byCell[i]._look[_localByCell[i]] : CellLook.None;

        public static void ClearAll()
        {
            // batch 8: the cut meshes (cell slices, their crack overlays, the break chunks) belong to the set: free them with it,
            // or every world rebuild leaks them (TechHardeningPlayTests.TenWorldRebuildsEndWithTheSameMeshAndMaterialCounts).
            foreach (BreakableCellSet set in _all)
            {
                if (set._byLook != null) foreach (MeshPiece[] ps in set._byLook) FreePieces(ps);
                if (set._chunks != null) foreach (MeshPiece[] ps in set._chunks) FreePieces(ps);
            }
            _all.Clear();
            System.Array.Clear(_byCell, 0, _byCell.Length);
        }

        static void FreePieces(MeshPiece[] ps)
        {
            if (ps == null) return;
            foreach (MeshPiece p in ps)
            {
                if (p == null) continue;
                FreeMesh(p.Mesh);
                FreeMesh(p.Tint);
                if (p.Cracks != null) foreach (Mesh m in p.Cracks) FreeMesh(m);
            }
        }

        static void FreeMesh(Mesh m)
        {
            if (m == null) return;
            if (Application.isPlaying) Object.Destroy(m); else Object.DestroyImmediate(m);
        }

        BreakableCellSet(int[] cells, MeshPiece[][] byLook, MeshPiece[][] chunks, Transform section)
        {
            Cells = cells;
            _byLook = byLook;
            _chunks = chunks;
            _section = section;
            int n = cells.Length;
            _mf = new MeshFilter[n];
            _mr = new MeshRenderer[n];
            _crack = new MeshFilter[n];
            _t = new Transform[n];
            _look = new byte[n];
            _crackStage = new int[n];
            _shaking = new bool[n];
            _tint = new MeshFilter[n];
            _basePos = new Vector3[n];
            _baseRot = new Quaternion[n];
            Material crack = MeshPieces.CrackMaterial();
            for (int c = 0; c < n; c++)
            {
                var g = new GameObject("Cell " + cells[c]);
                g.transform.SetParent(section, false);
                _t[c] = g.transform;
                _mf[c] = g.AddComponent<MeshFilter>();
                _mr[c] = g.AddComponent<MeshRenderer>();
                var cg = new GameObject("Cracks");
                cg.transform.SetParent(g.transform, false);
                _crack[c] = cg.AddComponent<MeshFilter>();
                var cr = cg.AddComponent<MeshRenderer>();
                cr.sharedMaterial = crack;
                cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _crackStage[c] = -1;
                var tg = new GameObject("Tint");
                tg.transform.SetParent(g.transform, false);
                _tint[c] = tg.AddComponent<MeshFilter>();
                var tr = tg.AddComponent<MeshRenderer>();
                tr.sharedMaterial = crack;
                tr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _baseRot[c] = Quaternion.identity;
            }
        }

        /// <summary>Cuts the three state meshes by cell. section = the section's transform (already placed). Null if unreadable.</summary>
        public static BreakableCellSet TryBuild(List<int> cells, int width, Mesh intact, Mesh cracked, Mesh destroyed, Transform section, int mapCells)
        {
            if (cells == null || cells.Count == 0 || intact == null || !intact.isReadable) return null;
            Matrix4x4 toWorld = section.localToWorldMatrix;
            System.Func<Vector3, Vector2> toSim = v =>
            {
                SimSpace.ToSim(toWorld.MultiplyPoint3x4(v), out double sx, out double sz);
                return new Vector2((float)sx, (float)sz);
            };
            int seed = cells[0] * 7 + 1;
            var byLook = new MeshPiece[3][];
            byLook[0] = MeshPieces.TryCells(intact, toSim, cells, width, seed);
            byLook[1] = cracked != null && cracked.isReadable ? MeshPieces.TryCells(cracked, toSim, cells, width, seed + 1) : null;
            byLook[2] = destroyed != null && destroyed.isReadable ? MeshPieces.TryCells(destroyed, toSim, cells, width, seed + 2) : null;
            if (byLook[0] == null) return null;
            // wall-damage-2: the break throws several chunky pieces (a 2 x 2 x 2 cut of the cell's slice, in the wall's own
            // materials), not the whole block as one slab; the cracked stage gets a darker tint shell.
            var chunks = new MeshPiece[cells.Count][];
            for (int c = 0; c < cells.Count; c++)
                chunks[c] = byLook[0][c] != null ? MeshPieces.TryGrid(byLook[0][c].Mesh, 2, seed + c * 3) : null;
            for (int k = 0; k < 2; k++)
                if (byLook[k] != null)
                    foreach (MeshPiece pc in byLook[k])
                        if (pc != null) pc.Tint = MeshPieces.TintShell(pc.Mesh, CrackedTint, 0.012f);
            var set = new BreakableCellSet(cells.ToArray(), byLook, chunks, section);
            if (_byCell.Length != mapCells) { _byCell = new BreakableCellSet[mapCells]; _localByCell = new int[mapCells]; }
            for (int c = 0; c < cells.Count; c++)
                if (cells[c] >= 0 && cells[c] < mapCells) { _byCell[cells[c]] = set; _localByCell[cells[c]] = c; }
            _all.Add(set);
            return set;
        }

        /// <summary>Per-cell look from the current grid (WorldView.SyncBreakables). mats = the section's resolved materials.</summary>
        public void Sync(World w, Material[] mats, bool first)
        {
            _mats = mats;
            byte[] cells = w.Map.Cells;
            for (int c = 0; c < Cells.Length; c++)
            {
                int i = Cells[c];
                CellLook look = DamageLook.LookOf(cells[i]);
                if (look == CellLook.None) look = CellLook.Rubble;   // dug or changed: the authored rubble at most
                CellLook was = (CellLook)_look[c];
                if (look != was || first)
                {
                    MeshPiece piece = Piece(look, c);
                    _mf[c].sharedMesh = piece != null ? piece.Mesh : null;
                    _mr[c].sharedMaterials = mats;
                    if (!first && look == CellLook.Rubble && was != CellLook.Rubble && was != CellLook.None) BreakCell(w, c);
                    if (!first && look == CellLook.Cracked && was == CellLook.Intact) Chip(w, c);
                    _look[c] = (byte)look;
                }
                // Crack strips: from the hit points left. wall-damage-2: a cracked cell (one hit from breaking with the usual 2
                // hits) shows the heaviest stage at once: the widest strokes, the dark tint and the block knocked askew.
                double left = WorldReactions.WallLeft01(w, i);
                int stage = look == CellLook.Rubble ? 0 : System.Math.Max(DamageLook.Stage(left), look == CellLook.Cracked ? 3 : 0);
                if (stage != _crackStage[c])
                {
                    MeshPiece piece = Piece(look, c);
                    _crack[c].sharedMesh = stage > 0 && piece != null && piece.Cracks != null ? piece.Cracks[Mathf.Clamp(stage, 1, 3) - 1] : null;
                    _tint[c].sharedMesh = stage >= 2 && piece != null ? piece.Tint : null;
                    Pose(w, c, stage >= 2 ? stage : 0, piece);
                    _crackStage[c] = stage;
                }
            }
        }

        /// <summary>Rests a damaged cell's block askew: tipped about its base away from the last blow, sunk and slid a little.</summary>
        void Pose(World w, int c, int stage, MeshPiece piece)
        {
            _basePos[c] = Vector3.zero;
            _baseRot[c] = Quaternion.identity;
            if (stage >= 2 && piece != null && piece.Mesh != null)
            {
                int i = Cells[c];
                double x = i % w.Map.Width + 0.5, z = i / w.Map.Width + 0.5;
                Vector3 d = DamageHits.Latest(ReactKind.Wall, 0, x, z, 0.75, Time.unscaledTimeAsDouble, 3.0, out ReactPulse p) ? DamageHits.WorldDir(p, i) : DamageHits.WorldDir(new ReactPulse(), i);
                Vector3 dl = _section.InverseTransformDirection(d).normalized, ul = _section.InverseTransformDirection(Vector3.up).normalized;
                Bounds b = piece.Mesh.bounds;   // cell slices keep the section's space
                Vector3 pivot = new Vector3(b.center.x, b.min.y, b.center.z);
                float deg = stage >= 3 ? DamageLook.CrackedLeanDeg : DamageLook.DamagedLeanDeg;
                Quaternion r = Quaternion.AngleAxis(deg, Vector3.Cross(ul, dl));
                _baseRot[c] = r;
                _basePos[c] = pivot - r * pivot + dl * 0.08f - ul * 0.08f;   // capture-gaps-1: knocked further back and down (was 0.06, 0.05)
            }
            _t[c].localPosition = _basePos[c];
            _t[c].localRotation = _baseRot[c];
        }

        /// <summary>The first hit knocks two small chips off the block (thrown as rubble), so the hit cell visibly loses stone.</summary>
        void Chip(World w, int c)
        {
            MeshPiece[] chunks = _chunks[c];
            if (chunks == null || chunks.Length == 0 || _mats == null) return;
            int i = Cells[c];
            double x = i % w.Map.Width + 0.5, z = i / w.Map.Width + 0.5;
            double now = Time.unscaledTimeAsDouble;
            Vector3 dir = DamageHits.Latest(ReactKind.Wall, 0, x, z, 0.75, now, 1.0, out ReactPulse p) ? DamageHits.WorldDir(p, i) : DamageHits.WorldDir(new ReactPulse(), i);
            float groundY = (float)GroundHeight.At(w.Map, x, z) + 0.04f;
            Matrix4x4 m = _section.localToWorldMatrix;
            for (int k = 0; k < 2; k++)
            {
                MeshPiece ch = chunks[(int)(DamageLook.Hash01(i * 29 + k) * chunks.Length) % chunks.Length];
                Vector3 pos = m.MultiplyPoint3x4(ch.Pivot);
                float a = DamageLook.Hash01(i * 31 + k);
                Vector3 v = dir * (1.2f + a) + Vector3.up * (2.4f + a);
                RubblePool.Spawn(ch.Mesh, _mats, pos, _section.rotation, _section.lossyScale * 0.45f, v, new Vector3(400f * a, 200f, -300f * a), groundY);
            }
        }

        MeshPiece Piece(CellLook look, int c)
        {
            int k = look == CellLook.Intact ? 0 : look == CellLook.Cracked ? 1 : 2;
            MeshPiece[] arr = _byLook[k];
            if (arr == null && k == 1) arr = _byLook[0];   // no cracked model: the intact slice with cracks
            return arr != null ? arr[c] : null;
        }

        void BreakCell(World w, int c)
        {
            MeshPiece[] chunks = _chunks[c];
            if (chunks == null || _mats == null) return;
            int i = Cells[c];
            double x = i % w.Map.Width + 0.5, z = i / w.Map.Width + 0.5;
            double now = Time.unscaledTimeAsDouble;
            Vector3 dir = DamageHits.Latest(ReactKind.Wall, 1, x, z, 0.75, now, 1.0, out ReactPulse p) ? DamageHits.WorldDir(p, i) : DamageHits.WorldDir(new ReactPulse(), i);
            float groundY = (float)GroundHeight.At(w.Map, x, z) + 0.05f;
            Matrix4x4 m = _section.localToWorldMatrix;
            Vector3 cellCentre = SimSpace.ToWorld(x, groundY, z);
            for (int k = 0; k < chunks.Length; k++)
            {
                Vector3 pos = m.MultiplyPoint3x4(chunks[k].Pivot + (_byLook[0][c] != null ? _byLook[0][c].Pivot : Vector3.zero));
                float a = DamageLook.Hash01(i * 17 + k), b = DamageLook.Hash01(i * 23 + k + 5);
                Vector3 outward = pos - cellCentre;
                outward.y = 0f;
                Vector3 v = dir * (1.6f + 1.4f * a) + outward.normalized * 1.0f + Vector3.up * (2.0f + 2.0f * b);
                Vector3 spin = new Vector3((a - 0.5f) * 500f, (b - 0.5f) * 300f, (a - b) * 500f);
                RubblePool.Spawn(chunks[k].Mesh, _mats, pos, _section.rotation, _section.lossyScale, v, spin, groundY);
            }
            // The break sound (wall_break, now stone) is played by the sound feed from the Sim's WallBreak event.
        }

        /// <summary>Per frame (WorldView.Update): shakes ONLY the cells a fresh wall pulse hit, 0.12 s along the blow. No allocation.</summary>
        public static void TickShake(World w)
        {
            if (_all.Count == 0 || w == null) return;
            WorldReactions rx = WorldReactions.Current;
            double now = Time.unscaledTimeAsDouble;
            for (int s = 0; s < _all.Count; s++)
            {
                BreakableCellSet set = _all[s];
                for (int c = 0; c < set._shaking.Length; c++)
                    if (set._shaking[c]) { set._t[c].localPosition = set._basePos[c]; set._shaking[c] = false; }
            }
            if (rx == null) return;
            ReactPulse[] ps = rx.Pulses;
            int width = w.Map.Width;
            for (int k = 0; k < ps.Length; k++)
            {
                ReactPulse p = ps[k];
                if (p.Kind != ReactKind.Wall || p.Phase != 0) continue;
                float age = (float)(now - p.T0);
                if (!(age >= 0f && age < DamageLook.ShakeSeconds)) continue;
                int cx = (int)System.Math.Floor(p.X), cz = (int)System.Math.Floor(p.Z);
                if (cx < 0 || cz < 0 || cx >= width || cz >= w.Map.Height) continue;
                int i = cz * width + cx;
                if (i >= _byCell.Length || _byCell[i] == null) continue;
                BreakableCellSet set = _byCell[i];
                int c = _localByCell[i];
                Vector3 d = DamageHits.WorldDir(p, i);
                // The section may be rotated: push along the blow in the section's own space.
                Vector3 local = set._section.InverseTransformDirection(d);
                set._t[c].localPosition = set._basePos[c] + local * DamageLook.Shake(age, (float)p.Strength);
                set._shaking[c] = true;
            }
        }
    }
}
