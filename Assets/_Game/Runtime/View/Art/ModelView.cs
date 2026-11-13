using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>
    /// One instance of an imported model with a controller-free clip mixer (Playables, manual update): the caller sets each
    /// clip's time and weight every frame from sim state and calls Evaluate. No AnimatorController assets, no state machine
    /// of its own, nothing written back to the sim. Pooled by the views; never destroyed in the hot path.
    /// <para>Animation pass (2026-11-12, DG-73): rigged characters with a "spine" bone get two layers: layer 0 is the full
    /// body (locomotion, jumps, get-up, heavy hits, gather), layer 1 overrides the bones from "spine" up through an
    /// AvatarMask built from this instance's own transform paths (attacks, abilities, light and blocked hits, the carry
    /// pose), so legs keep their stride while the arms swing. Each upper clip has two playables (slot 0 and 1) so a
    /// re-triggered attack cross-fades from the pose it was in instead of popping. Models without a spine (the Kaiju, props)
    /// keep the single flat mixer; PoseUpper falls back to layer 0 there.</para>
    /// <para>Animation cost (DG-108): Evaluate(false) sets the weights but skips the graph (the bones keep last frame's
    /// pose); the caller decides with AnyVisible and the distance stagger in Anim.AnimLod.</para>
    /// </summary>
    public sealed class ModelView
    {
        /// <summary>The first bone of the upper-body layer's mask (it and every transform under it).</summary>
        public const string UpperBodyRootBone = "spine";

        public readonly ArtEntry Entry;
        public readonly ModelSpec Spec;
        public readonly GameObject Root;
        public readonly Transform Transform;
        public readonly Renderer[] Renderers;

        private readonly Animator _animator;
        private PlayableGraph _graph;
        private AnimationMixerPlayable _mixer;
        private AnimationLayerMixerPlayable _layers;
        private AnimationMixerPlayable _upper;
        private readonly bool _hasUpper;
        private readonly AnimationClipPlayable[] _clips;
        private readonly float[] _length;
        private readonly float[] _weight;
        private readonly Dictionary<string, int> _index = new Dictionary<string, int>();
        // Upper layer: 2 playables per upper clip, at _upperIndex[name] * 2 + slot.
        private readonly AnimationClipPlayable[] _upperClips;
        private readonly float[] _upperW;
        private readonly Dictionary<string, int> _upperIndex = new Dictionary<string, int>();
        private float _upperLayerWeight;
        private AvatarMask _mask;
        private Dictionary<string, Transform> _bones;
        private Material _material;

        public ModelView(ArtEntry entry, Transform parent, string name)
        {
            Entry = entry;
            Spec = ArtMapping.Spec(entry.Key);
            Root = Object.Instantiate(entry.Model, parent, false);
            Root.name = name;
            Transform = Root.transform;
            Renderers = Root.GetComponentsInChildren<Renderer>(true);
            // DG-108 / contract C4: the import decides each renderer's shadow mode (a future _Shadow proxy is Shadows Only,
            // LODs may be off); this no longer forces On.
            foreach (Renderer r in Renderers)
            {
                r.receiveShadows = true;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }

            int n = entry.Clips != null ? entry.Clips.Length : 0;
            _clips = new AnimationClipPlayable[n];
            _length = new float[n];
            _weight = new float[n];
            _upperClips = new AnimationClipPlayable[0];
            _upperW = new float[0];
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

            Transform spine = Spec != null && Spec.Kind == ArtKind.Character ? FindDeep(Transform, UpperBodyRootBone) : null;
            int upperCount = 0;
            if (spine != null)
                foreach (string u in ArtMapping.UpperBodyClips)
                    if (_index.ContainsKey(u)) _upperIndex[u] = upperCount++;
            _hasUpper = upperCount > 0;
            if (!_hasUpper)
            {
                output.SetSourcePlayable(_mixer);
                _graph.Play();
                return;
            }

            _upperClips = new AnimationClipPlayable[upperCount * 2];
            _upperW = new float[upperCount * 2];
            _upper = AnimationMixerPlayable.Create(_graph, upperCount * 2);
            foreach (KeyValuePair<string, int> kv in _upperIndex)
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = kv.Value * 2 + slot;
                    _upperClips[k] = AnimationClipPlayable.Create(_graph, entry.Clips[_index[kv.Key]]);
                    _upperClips[k].SetApplyFootIK(false);
                    _upperClips[k].SetSpeed(0);
                    _graph.Connect(_upperClips[k], 0, _upper, k);
                }
            _mask = BuildUpperMask(Transform, spine);
            _layers = AnimationLayerMixerPlayable.Create(_graph, 2);
            _graph.Connect(_mixer, 0, _layers, 0);
            _graph.Connect(_upper, 0, _layers, 1);
            _layers.SetInputWeight(0, 1f);
            _layers.SetInputWeight(1, 0f);
            _layers.SetLayerMaskFromAvatarMask(1, _mask);
            output.SetSourcePlayable(_layers);
            _graph.Play();
        }

        /// <summary>A Generic transform mask: every transform of this instance, active from `spine` down its subtree.</summary>
        private static AvatarMask BuildUpperMask(Transform root, Transform spine)
        {
            Transform[] all = root.GetComponentsInChildren<Transform>(true);
            var mask = new AvatarMask();
            mask.transformCount = all.Length;
            for (int i = 0; i < all.Length; i++)
            {
                mask.SetTransformPath(i, PathOf(root, all[i]));
                mask.SetTransformActive(i, all[i] == spine || all[i].IsChildOf(spine));
            }
            return mask;
        }

        /// <summary>Path of t relative to root ("" for root itself), the form Generic clips and masks bind by.</summary>
        public static string PathOf(Transform root, Transform t)
        {
            if (t == root) return "";
            string p = t.name;
            for (Transform x = t.parent; x != null && x != root; x = x.parent) p = x.name + "/" + p;
            return p;
        }

        private static Transform FindDeep(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                Transform r = FindDeep(t.GetChild(i), name);
                if (r != null) return r;
            }
            return null;
        }

        public bool Active => Root.activeSelf;

        /// <summary>True when this model has the masked upper-body layer (rigged characters with a spine).</summary>
        public bool HasUpperLayer => _hasUpper;

        /// <summary>A bone (any transform under the root) by name, cached; null when the rig has none.</summary>
        public Transform Bone(string name)
        {
            if (_bones == null)
            {
                _bones = new Dictionary<string, Transform>();
                foreach (Transform t in Root.GetComponentsInChildren<Transform>(true))
                    if (!_bones.ContainsKey(t.name)) _bones[t.name] = t;
            }
            return _bones.TryGetValue(name, out Transform b) ? b : null;
        }

        /// <summary>True when a renderer of this model was drawn by some camera last frame (shadow passes count).</summary>
        public bool AnyVisible
        {
            get
            {
                foreach (Renderer r in Renderers) if (r != null && r.enabled && r.isVisible) return true;
                return false;
            }
        }

        /// <summary>True when the animator is set to always animate (the sanity tests force this: never throttle then).</summary>
        public bool AlwaysAnimate => _animator != null && _animator.cullingMode == AnimatorCullingMode.AlwaysAnimate;

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
            if (Root.activeSelf != on) Root.SetActive(on);
        }

        public bool HasClip(string clip) => clip != null && _index.ContainsKey(clip);

        public float ClipLength(string clip) => clip != null && _index.TryGetValue(clip, out int i) ? _length[i] : 0f;

        public void SetMaterial(Material m)
        {
            if (_material == m) return;
            _material = m;
            // A model with a face slot (Miner CM_FaceDecal) draws that submesh of its body with the face material.
            Material face = ArtMaterials.Face(Spec, m);
            foreach (Renderer r in Renderers)
            {
                Material[] mats = r.sharedMaterials;
                bool body = Spec != null && Spec.Meshes.Length > 0 && r.name == Spec.Meshes[0];
                bool same = true;
                for (int i = 0; i < mats.Length; i++) same &= mats[i] == SlotMaterial(m, face, body, i);
                if (same) continue;
                for (int i = 0; i < mats.Length; i++) mats[i] = SlotMaterial(m, face, body, i);
                r.sharedMaterials = mats;
            }
        }

        private Material SlotMaterial(Material m, Material face, bool body, int slot) =>
            face != null && body && slot == Spec.FaceSlot ? face : m;

        /// <summary>Starts a frame's pose: every clip weight back to 0, upper layer off.</summary>
        public void BeginPose()
        {
            for (int i = 0; i < _weight.Length; i++) _weight[i] = 0f;
            for (int i = 0; i < _upperW.Length; i++) _upperW[i] = 0f;
            _upperLayerWeight = 0f;
        }

        /// <summary>Adds weight to a clip at a time in seconds (looping clips wrap, one-shots clamp). False if the clip is missing.</summary>
        public bool Pose(string clip, double seconds, float weight, bool loop)
        {
            if (weight <= 0f || clip == null || !_index.TryGetValue(clip, out int i)) return false;
            _clips[i].SetTime(WrapOrClamp(seconds, _length[i], loop));
            _weight[i] += weight;
            return true;
        }

        /// <summary>
        /// Adds weight to a clip on the upper-body layer (slot 0 or 1). The layer's own weights are normalised; its
        /// strength over the legs is UpperWeight. Without an upper layer the clip goes to the full body (old behaviour)
        /// at the same weight. False if the clip is missing everywhere.
        /// </summary>
        public bool PoseUpper(string clip, double seconds, float weight, bool loop, int slot = 0)
        {
            if (weight <= 0f || clip == null) return false;
            if (!_hasUpper || !_upperIndex.TryGetValue(clip, out int u)) return Pose(clip, seconds, weight * 3f, loop);
            int k = u * 2 + (slot & 1);
            _upperClips[k].SetTime(WrapOrClamp(seconds, _length[_index[clip]], loop));
            _upperW[k] += weight;
            return true;
        }

        /// <summary>The upper layer's strength this frame (0 legs-and-arms locomotion only, 1 arms fully the upper clips).</summary>
        public float UpperWeight
        {
            get => _upperLayerWeight;
            set => _upperLayerWeight = Mathf.Clamp01(value);
        }

        private static double WrapOrClamp(double seconds, double len, bool loop) =>
            loop ? seconds - len * System.Math.Floor(seconds / len) : System.Math.Min(System.Math.Max(seconds, 0.0), len - 1e-3);

        /// <summary>Normalises the weights set since BeginPose and evaluates the graph (no-op for static models).</summary>
        public void Evaluate() => Evaluate(true);

        /// <summary>
        /// Normalises and applies the weights; evaluates the graph only when evaluateGraph (the animation level of detail
        /// may skip a frame: the bones then hold last frame's pose). Returns true when the bones were written.
        /// </summary>
        public bool Evaluate(bool evaluateGraph)
        {
            if (!_graph.IsValid() || !Root.activeInHierarchy) return false;
            float sum = 0f;
            for (int i = 0; i < _weight.Length; i++) sum += _weight[i];
            if (sum <= 0f)
            {
                if (!_index.TryGetValue(ArtMapping.Idle, out int idle)) return false;
                _weight[idle] = 1f;
                sum = 1f;
            }
            for (int i = 0; i < _weight.Length; i++) _mixer.SetInputWeight(i, _weight[i] / sum);
            if (_hasUpper)
            {
                float us = 0f;
                for (int i = 0; i < _upperW.Length; i++) us += _upperW[i];
                for (int i = 0; i < _upperW.Length; i++) _upper.SetInputWeight(i, us > 0f ? _upperW[i] / us : 0f);
                _layers.SetInputWeight(1, us > 0f ? _upperLayerWeight : 0f);
            }
            if (!evaluateGraph) return false;
            _graph.Evaluate(0f);
            return true;
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_mask != null) Object.Destroy(_mask);
            if (Root != null) Object.Destroy(Root);
        }
    }
}
