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

        public ModelView(ArtEntry entry, Transform parent, string name)
        {
            Entry = entry;
            Spec = ArtMapping.Spec(entry.Key);
            Root = Object.Instantiate(entry.Model, parent, false);
            Root.name = name;
            Transform = Root.transform;
            Renderers = Root.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in Renderers)
            {
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                r.receiveShadows = true;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = false;
            }

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

        public void SetActive(bool on)
        {
            if (Root.activeSelf != on) Root.SetActive(on);
        }

        public bool HasClip(string clip) => _index.ContainsKey(clip);

        public float ClipLength(string clip) => _index.TryGetValue(clip, out int i) ? _length[i] : 0f;

        public void SetMaterial(Material m)
        {
            if (_material == m) return;
            _material = m;
            foreach (Renderer r in Renderers)
            {
                Material[] mats = r.sharedMaterials;
                bool same = true;
                for (int i = 0; i < mats.Length; i++) same &= mats[i] == m;
                if (same) continue;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
            }
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

        /// <summary>Normalises the weights set since BeginPose and evaluates the graph (no-op for static models).</summary>
        public void Evaluate()
        {
            if (!_graph.IsValid() || !Root.activeInHierarchy) return;
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
