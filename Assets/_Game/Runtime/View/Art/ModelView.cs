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
        /// <summary>The Kaiju rig's upper-body root and the clips it layers over its walk (fix/kaiju-alive-1).</summary>
        public const string KaijuUpperBodyRootBone = "Spine";
        public static readonly string[] KaijuUpperBodyClips = { ArtMapping.Swipe, ArtMapping.Roar };

        public readonly ArtEntry Entry;
        public readonly ModelSpec Spec;
        public readonly GameObject Root;
        public readonly Transform Transform;
        public readonly Renderer[] Renderers;

        /// <summary>perf-smooth-1: each renderer's object name, read once (Object.name makes a new string on every read).</summary>
        public readonly string[] RendererNames;

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

        /// <summary>
        /// The root's scale as imported (fix/model-scale). Every drawn size is BaseScale x an effect factor rebuilt each frame
        /// (Anim.ModelScale); nothing multiplies the root's current scale.
        /// </summary>
        public readonly Vector3 BaseScale;

        private Dictionary<Transform, Vector3> _restScale;

        /// <summary>A bone's scale as imported (captured the first time any bone is asked for, from the prefab).</summary>
        public Vector3 RestScale(Transform bone)
        {
            if (bone == null) return Vector3.one;
            if (_restScale == null)
            {
                _restScale = new Dictionary<Transform, Vector3>();
                Transform prefab = Entry != null && Entry.Model != null ? Entry.Model.transform : null;
                foreach (Transform t in Root.GetComponentsInChildren<Transform>(true))
                {
                    if (t == Transform) continue;
                    // Only the imported hierarchy: attachments added at runtime (cosmetics, carried props) own their scale.
                    Transform src = prefab != null ? prefab.Find(PathOf(Root.transform, t)) : null;
                    if (src != null) _restScale[t] = src.localScale;
                }
            }
            return _restScale.TryGetValue(bone, out Vector3 s) ? s : Vector3.one;
        }

        /// <summary>
        /// Back to the imported size (fix/model-scale): the root at BaseScale and every bone at its rest scale. ActorViews
        /// calls it when a model goes back to the pool and when it is taken out (respawn, class switch, pool reuse).
        /// </summary>
        public void ResetScale()
        {
            Transform.localScale = BaseScale;
            if (_restScale == null) return;
            foreach (KeyValuePair<Transform, Vector3> kv in _restScale)
                if (kv.Key != null && kv.Key.localScale != kv.Value) kv.Key.localScale = kv.Value;
        }

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
            // fix/model-scale: the base size is stored once, from the imported prefab, before anything poses the model.
            BaseScale = Transform.localScale;
            Renderers = Root.GetComponentsInChildren<Renderer>(true);
            RendererNames = new string[Renderers.Length];
            for (int i = 0; i < Renderers.Length; i++) RendererNames[i] = Renderers[i].name;
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
            // fix/fighters-always-visible (2026-11-13): the game is the ONE owner of which LOD draws. The importer's LODGroup
            // (built from the _LOD0/_LOD1 names, screen heights 0.25 / 0.01) picked LOD1 at the gameplay camera's distance
            // while AnimLod.PickLod had LOD0 on (your fighter, everyone within 25 u): LOD0 was culled by the group, LOD1 was
            // disabled by us, so the fighter drew nothing but its ring, name tag and shadow proxy. ForceLOD was meant to tie
            // them together but it does not survive the pool (deactivate, reactivate) and SetLod skipped re-forcing an
            // unchanged LOD. A disabled LODGroup culls nothing; ApplyDraws switches the renderers directly.
            if (Lod != null) Lod.enabled = false;
            ApplyTier();
            SetLod(0);

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

            // fix/kaiju-alive-1: the Kaiju gets the upper layer too (its "Spine" subtree: torso, arms, neck, head; the legs and
            // tail hang off Hips), so a claw or a roar plays over a walking stride instead of freezing the legs mid-step.
            bool kaijuUpper = Spec != null && Spec.Kind == ArtKind.Kaiju && Spec.Key != ArtMapping.KaijuEgg;
            Transform spine = Spec != null && Spec.Kind == ArtKind.Character ? FindDeep(Transform, UpperBodyRootBone)
                : kaijuUpper ? FindDeep(Transform, KaijuUpperBodyRootBone) : null;
            int upperCount = 0;
            if (spine != null)
                foreach (string u in kaijuUpper ? KaijuUpperBodyClips : ArtMapping.UpperBodyClips)
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
            if (Root.activeSelf == on) return;
            Root.SetActive(on);
            if (!on) return;
            _justActivated = true;
            // A pooled model comes back exactly as the game last chose (fix/fighters-always-visible): no LODGroup decides.
            if (Lod != null && Lod.enabled) Lod.enabled = false;
            if (_hasLod0 || _hasLod1) ApplyDraws();
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
        /// Draws LOD 0 or 1 (contract C4; AnimLod.PickLod decides) by switching the renderers; the importer's LODGroup is
        /// kept disabled (fix/fighters-always-visible). A model without a LOD chain ignores the call (its meshes are LOD0 at
        /// every distance).
        /// </summary>
        public void SetLod(int lod)
        {
            lod = lod <= 0 ? 0 : 1;
            if (lod == _lod) return;
            _lod = lod;
            if (!_hasLod1 && !_hasLod0) return;
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
            if (Lod != null && Lod.enabled) Lod.enabled = false;   // never re-enabled: see the constructor
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


        /// <summary>true on the first frame after the model was activated (no visibility result exists yet).</summary>
        public bool JustActivated => _justActivated;

        public bool HasClip(string clip) => clip != null && _index.ContainsKey(clip);

        public float ClipLength(string clip) => clip != null && _index.TryGetValue(clip, out int i) ? _length[i] : 0f;

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
        /// Evaluates when <paramref name="evaluate"/> is true (perf M4); otherwise the pose from the last evaluation stays.
        /// </summary>
        public void EvaluateIf(bool evaluate) => Evaluate(evaluate);

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
            _justActivated = false;
            if (evaluateGraph) Evaluations++; else SkippedEvaluations++;
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

        /// <summary>
        /// The clips weighted in the last pose with their normalised weights and times, the upper layer's, and the
        /// evaluation counters (test diagnostics; allocates, never call it per frame in the game).
        /// </summary>
        public string DescribePose()
        {
            var sb = new System.Text.StringBuilder();
            float sum = 0f;
            for (int i = 0; i < _weight.Length; i++) sum += _weight[i];
            foreach (KeyValuePair<string, int> kv in _index)
            {
                float w = _weight[kv.Value];
                if (w <= 0f) continue;
                sb.Append(kv.Key).Append(' ').Append((w / Mathf.Max(1e-6f, sum)).ToString("0.00")).Append('@')
                  .Append(_clips[kv.Value].GetTime().ToString("0.000")).Append(' ');
            }
            if (_hasUpper)
            {
                sb.Append("| upper ").Append(_upperLayerWeight.ToString("0.00")).Append(':');
                foreach (KeyValuePair<string, int> kv in _upperIndex)
                    for (int slot = 0; slot < 2; slot++)
                    {
                        int k = kv.Value * 2 + slot;
                        if (_upperW[k] > 0f) sb.Append(' ').Append(kv.Key).Append(' ').Append(_upperW[k].ToString("0.00"));
                    }
            }
            sb.Append(" | evaluated ").Append(Evaluations).Append(", skipped ").Append(SkippedEvaluations);
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_graph.IsValid()) _graph.Destroy();
            if (_mask != null) Kill(_mask);
            if (Root != null) Kill(Root);
        }

        /// <summary>Integration batch 5: Destroy in play, DestroyImmediate in edit mode (the Barracks class chips swap the
        /// preview model in EditMode tests; Destroy there logs an error and leaks the model).</summary>
        static void Kill(Object o)
        {
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
