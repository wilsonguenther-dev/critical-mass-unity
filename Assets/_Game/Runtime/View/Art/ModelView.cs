using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>
    /// One instance of an imported model with a controller-free clip mixer (Playables, manual update): the caller sets each
    /// clip's time and weight every frame from sim state and calls Evaluate. No AnimatorController assets, no state machine
    /// of its own, nothing written back to the sim. Pooled by the views; never destroyed in the hot path.
    /// </summary>
    public sealed class ModelView
    {
        public readonly ArtEntry Entry;
        public readonly ModelSpec Spec;
        public readonly GameObject Root;
        public readonly Transform Transform;
        public readonly Renderer[] Renderers;

        private readonly Animator _animator;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private readonly AnimationClipPlayable[] _clips;
        private readonly float[] _length;
        private readonly float[] _weight;
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>();
        private Material _material;
        // Physics audit 2026-11-12 (m5): one material array per renderer, filled in place (reading sharedMaterials
        // allocates a new array on every call, which happened on every hit flash).
        private readonly Material[][] _slots;
        private readonly FighterLod.Role[] _roles;
        private readonly bool[] _isBody;
        private readonly bool _hasProxy, _hasLod0, _hasLod1;
        private int _tierApplied = int.MinValue;
        private bool _justActivated = true;
        private int _lod = -1;
        private bool _hero, _lod1Outline;

        /// <summary>The LODGroup the model importer built from _LOD0/_LOD1 names, if any (contract C4); null otherwise.</summary>
        public readonly LODGroup Lod;

        /// <summary>The LOD drawn now (0 or 1; -1 before the first pick) and whether the close-camera Hero mesh is shown.</summary>
        public int CurrentLod => _lod;
        public bool HeroShown => _hero;
        /// <summary>The model follows the art LOD contract (has a _LOD1 mesh).</summary>
        public bool HasLodChain => _hasLod1;

        /// <summary>The name the views gave the root last (compared by reference, so a respawn never rebuilds it).</summary>
        public string AssignedName;

        /// <summary>Poses evaluated and skipped since creation (tests, perf report).</summary>
        public int Evaluations { get; private set; }
        public int SkippedEvaluations { get; private set; }

        public ModelView(ArtEntry entry, Transform parent, string name)
        {
            Entry = entry;
            Spec = ArtMapping.Spec(entry.Key);
            Root = Object.Instantiate(entry.Model, parent, false);
            Root.name = name;
            AssignedName = name;
            Transform = Root.transform;
            Renderers = Root.GetComponentsInChildren<Renderer>(true);
            _slots = new Material[Renderers.Length][];
            _roles = new FighterLod.Role[Renderers.Length];
            _isBody = new bool[Renderers.Length];
            string body = Spec != null && Spec.Meshes != null && Spec.Meshes.Length > 0 ? Spec.Meshes[0] : null;
            for (int i = 0; i < Renderers.Length; i++)
            {
                Renderer r = Renderers[i];
                _slots[i] = r.sharedMaterials;
                _roles[i] = FighterLod.RoleOf(r.name);
                FighterLod.Role role = _roles[i];
                _isBody[i] = role == FighterLod.Role.Hero || role == FighterLod.Role.Lod0 || role == FighterLod.Role.Lod1 ||
                             (body != null && r.name == body);
                _hasProxy |= role == FighterLod.Role.Shadow;
                _hasLod0 |= role == FighterLod.Role.Lod0;
                _hasLod1 |= role == FighterLod.Role.Lod1;
                r.receiveShadows = role != FighterLod.Role.Shadow;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }
            Lod = Root.GetComponentInChildren<LODGroup>(true);
            ApplyTier();
            SetLod(0);

            int n = entry.Clips != null ? entry.Clips.Length : 0;
            _clips = new AnimationClipPlayable[n];
            _length = new float[n];
            _weight = new float[n];
            if (n == 0) return;

            _animator = Root.GetComponent<Animator>();
            if (_animator == null) _animator = Root.AddComponent<Animator>();
            _animator.runtimeAnimatorController = null;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullCompletely;
            _graph = PlayableGraph.Create(name);
            _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(_graph, "anim", _animator);
            _mixer = AnimationMixerPlayable.Create(_graph, n);
            for (int i = 0; i < n; i++)
            {
                AnimationClip c = entry.Clips[i];
                _clips[i] = AnimationClipPlayable.Create(_graph, c);
                _clips[i].SetApplyFootIK(false);
                _clips[i].SetSpeed(0);
                _graph.Connect(_clips[i], 0, _mixer, i);
                _length[i] = Mathf.Max(1e-3f, c.length);
                _index[c.name] = i;
            }
            output.SetSourcePlayable(_mixer);
            _graph.Play();
        }

        public bool Active => Root.activeSelf;

        /// <summary>
        /// Turn the model to a view rotation (for actors: Euler(0, SimSpace.YawDeg(facing), 0)), adding the art's own yaw
        /// (ModelSpec.ArtYawDeg) so the face leads. The one place a model's forward axis is mapped onto the sim's.
        /// </summary>
        public void SetFacing(Quaternion rotation)
        {
            float art = Spec != null ? Spec.ArtYawDeg : 0f;
            Transform.localRotation = art == 0f ? rotation : rotation * Quaternion.Euler(0f, art, 0f);
        }

        public void SetActive(bool on)
        {
            if (Root.activeSelf == on) return;
            Root.SetActive(on);
            if (on) _justActivated = true;
        }

        /// <summary>
        /// Re-applies the quality row (fighter shadow policy) when RenderTier changed since the last call; cheap when not.
        /// Shadow proxy (the art LOD contract) casts alone at Medium and High; nothing casts at Low (blob shadows instead).
        /// </summary>
        public void ApplyTier()
        {
            int level = RenderTier.Level;
            if (level == _tierApplied) return;
            _tierApplied = level;
            QualityRow row = QualityTable.For(level);
            // C4 point 5: the import's shadow mode is respected only through this table; ModelView no longer forces On.
            for (int i = 0; i < Renderers.Length; i++)
            {
                Renderer r = Renderers[i];
                if (r == null) continue;
                var mode = FighterLod.ShadowMode(_roles[i], row.FighterShadows, _hasProxy);
                if (r.shadowCastingMode != mode) r.shadowCastingMode = mode;
                if (_roles[i] == FighterLod.Role.Shadow)
                {
                    bool on = FighterLod.ProxyEnabled(row.FighterShadows) && !_hero;
                    if (r.enabled != on) r.enabled = on;
                }
            }
            if (_lod1Outline != row.Lod1Outline)
            {
                _lod1Outline = row.Lod1Outline;
                Material m = _material;
                _material = null;
                if (m != null) SetMaterial(m);   // re-picks LOD1's outline or no-outline copies
            }
        }

        /// <summary>
        /// Draws LOD 0 or 1 (contract C4; AnimLod.PickLod decides). Drives the importer's LODGroup when there is one, else
        /// switches the renderers. A model without a LOD chain ignores the call (its meshes are LOD0 at every distance).
        /// </summary>
        public void SetLod(int lod)
        {
            lod = lod <= 0 ? 0 : 1;
            if (lod == _lod) return;
            _lod = lod;
            if (!_hasLod1 && !_hasLod0 && Lod == null) return;
            if (Lod != null && !_hero) Lod.ForceLOD(lod);
            ApplyDraws();
        }

        /// <summary>
        /// Close cameras only (class select, story, results, Barracks, photo mode): shows the _Hero mesh and hides the
        /// in-match LODs and the shadow proxy. Never on in a match (C4 point 2). No-op visually for a model without a Hero.
        /// </summary>
        public void SetHero(bool on)
        {
            if (on == _hero) return;
            _hero = on;
            if (Lod != null) Lod.enabled = !on;
            _tierApplied = int.MinValue;
            ApplyTier();
            ApplyDraws();
        }

        private void ApplyDraws()
        {
            for (int i = 0; i < Renderers.Length; i++)
            {
                Renderer r = Renderers[i];
                if (r == null || _roles[i] == FighterLod.Role.Shadow) continue;
                if (_roles[i] == FighterLod.Role.Shared && Spec != null && Spec.Kind == ArtKind.Prop) continue;   // barricade states
                bool on = FighterLod.Draws(_roles[i], _hero, _lod < 0 ? 0 : _lod, _hasLod0, _hasLod1);
                if (r.enabled != on) r.enabled = on;
            }
        }

        /// <summary>true when a drawn renderer was visible to any camera last frame (shadow casting counts).</summary>
        public bool AnyVisible
        {
            get
            {
                for (int i = 0; i < Renderers.Length; i++)
                {
                    Renderer r = Renderers[i];
                    if (r != null && r.enabled && r.isVisible) return true;
                }
                return false;
            }
        }

        /// <summary>true on the first frame after the model was activated (no visibility result exists yet).</summary>
        public bool JustActivated => _justActivated;

        public bool HasClip(string clip) => _index.ContainsKey(clip);

        public float ClipLength(string clip) => _index.TryGetValue(clip, out int i) ? _length[i] : 0f;

        public void SetMaterial(Material m)
        {
            if (_material == m) return;
            _material = m;
            // A model with a face slot (Miner CM_FaceDecal) draws that submesh of its body with the face material.
            Material face = ArtMaterials.Face(Spec, m);
            for (int k = 0; k < Renderers.Length; k++)
            {
                Renderer r = Renderers[k];
                if (r == null) continue;
                Material[] mats = _slots[k];
                bool lod1 = _roles[k] == FighterLod.Role.Lod1 && !_lod1Outline;
                bool same = true;
                for (int i = 0; i < mats.Length; i++) same &= mats[i] == SlotMaterial(m, face, _isBody[k], i, lod1);
                if (same) continue;
                for (int i = 0; i < mats.Length; i++) mats[i] = SlotMaterial(m, face, _isBody[k], i, lod1);
                if (mats.Length == 1) r.sharedMaterial = mats[0];
                else r.sharedMaterials = mats;
            }
        }

        private Material SlotMaterial(Material m, Material face, bool body, int slot, bool lod1)
        {
            Material s = face != null && body && slot == Spec.FaceSlot ? face : m;
            return lod1 ? FighterLod.WithoutOutline(s) : s;
        }

        /// <summary>Starts a frame's pose: every clip weight back to 0.</summary>
        public void BeginPose()
        {
            for (int i = 0; i < _weight.Length; i++) _weight[i] = 0f;
        }

        /// <summary>Adds weight to a clip at a time in seconds (looping clips wrap, one-shots clamp). False if the clip is missing.</summary>
        public bool Pose(string clip, double seconds, float weight, bool loop)
        {
            if (weight <= 0f || !_index.TryGetValue(clip, out int i)) return false;
            double len = _length[i];
            double t = loop ? seconds - len * System.Math.Floor(seconds / len) : System.Math.Min(System.Math.Max(seconds, 0.0), len - 1e-3);
            _clips[i].SetTime(t);
            _weight[i] += weight;
            return true;
        }

        /// <summary>
        /// Evaluates when <paramref name="evaluate"/> is true (AnimLod: off-screen models hold their pose, distant ones are
        /// throttled); otherwise the clip times set this frame are kept and the pose from the last evaluation stays.
        /// </summary>
        public void EvaluateIf(bool evaluate)
        {
            if (evaluate) Evaluate();
            else SkippedEvaluations++;
        }

        /// <summary>Normalises the weights set since BeginPose and evaluates the graph (no-op for static models).</summary>
        public void Evaluate()
        {
            if (!_graph.IsValid() || !Root.activeInHierarchy) return;
            _justActivated = false;
            Evaluations++;
            float sum = 0f;
            for (int i = 0; i < _weight.Length; i++) sum += _weight[i];
            if (sum <= 0f)
            {
                if (!_index.TryGetValue(ArtMapping.Idle, out int idle)) return;
                _weight[idle] = 1f;
                sum = 1f;
            }
            for (int i = 0; i < _weight.Length; i++) _mixer.SetInputWeight(i, _weight[i] / sum);
            _graph.Evaluate(0f);
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (Root != null) Object.Destroy(Root);
        }
    }
}
