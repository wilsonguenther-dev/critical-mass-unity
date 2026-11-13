using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Progression;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Art
{
    /// <summary>
    /// Cosmetics on a character model (DG-27). The art convention, so art is added with no code:
    /// <code>
    /// Assets/_Game/Art/Cosmetics/&lt;Class&gt;/Head_&lt;Name&gt;.fbx    headgear, rigid on the "head" joint
    /// Assets/_Game/Art/Cosmetics/&lt;Class&gt;/Emblem_&lt;Name&gt;.png  emblem texture, drawn on the class's emblem mount
    /// Assets/_Game/Art/Cosmetics/&lt;Class&gt;/Mount_Emblem.fbx      the plate (shield face or pack) the emblem sits on
    /// </code>
    /// &lt;Class&gt; is the ClassId name (Warrior, Farmer, Miner, Demolitions, Builder); &lt;Name&gt; is PascalCase and is the
    /// item id (CosmeticItems). Headgear and mounts are modelled in the class's .blend in BIND POSE, in the same space as
    /// the body, and exported alone with the same settings: AttachRigid uses the body's bind pose of the joint, so the
    /// piece lands where it was modelled. A model may instead carry an empty named "EmblemSocket"; the mount then sits on
    /// it at identity. Victory emotes are clips on the class's clip source (ArtCatalog), by name.
    /// Team-colour channels stay locked: headgear and mounts are drawn with the wearer's own team material, so their
    /// team-cloth vertex codes swap with the side like the body and cannot be recoloured by a cosmetic.
    /// </summary>
    public static class Cosmetics
    {
        public const string Folder = "Assets/_Game/Art/Cosmetics";
        public const string HeadSlot = "Head", EmblemSlot = "Emblem", MountSlot = "Mount";
        public const string MountName = "Emblem";
        public const string HeadJoint = "head";
        public const string EmblemSocket = "EmblemSocket";

        /// <summary>
        /// Hook for the actor views (optional, see the PR): returns the live model root of an actor id, or null. When unset,
        /// LocalCosmetics finds actor 0's model under ActorViews by its object name "Actor 0 &lt;key&gt;".
        /// </summary>
        public static Func<int, Transform> ActorModelResolver;

        /// <summary>Parses Folder/&lt;Class&gt;/&lt;Slot&gt;_&lt;Name&gt;.(fbx|png); false for anything else.</summary>
        public static bool TryParsePath(string assetPath, out ClassId cls, out string slot, out string name)
        {
            cls = ClassId.Warrior;
            slot = name = null;
            if (string.IsNullOrEmpty(assetPath)) return false;
            string p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Folder + "/", StringComparison.Ordinal)) return false;
            string[] parts = p.Substring(Folder.Length + 1).Split('/');
            if (parts.Length != 2 || !Enum.TryParse(parts[0], false, out cls) || !Enum.IsDefined(typeof(ClassId), cls)) return false;
            string file = parts[1];
            int dot = file.LastIndexOf('.');
            if (dot <= 0) return false;
            string ext = file.Substring(dot + 1).ToLowerInvariant();
            string stem = file.Substring(0, dot);
            int us = stem.IndexOf('_');
            if (us <= 0 || us == stem.Length - 1) return false;
            slot = stem.Substring(0, us);
            name = stem.Substring(us + 1);
            bool ok = (slot == HeadSlot && ext == "fbx") || (slot == MountSlot && ext == "fbx") || (slot == EmblemSlot && ext == "png");
            if (!ok) slot = name = null;
            return ok;
        }

        public static string PathFor(ClassId cls, string slot, string name, string ext) => Folder + "/" + cls + "/" + slot + "_" + name + "." + ext;

        /// <summary>The joint an emblem mount rides on when the model has no EmblemSocket: the Warrior's shield hand, else the chest.</summary>
        public static string EmblemBone(ClassId cls) => cls == ClassId.Warrior ? "hand.l" : "chest";

        static string SlotName(CosmeticSlot s) => s == CosmeticSlot.Head ? HeadSlot : EmblemSlot;

        /// <summary>Is the art for this item present? Head: the mesh. Emblem: the texture and the class mount. Emote: the clip.</summary>
        public static bool Has(ClassId cls, CosmeticSlot slot, string id)
        {
            if (string.IsNullOrEmpty(id)) return false;
            if (slot == CosmeticSlot.Emote)
            {
                string key = ArtMapping.ClassModel(cls);
                ArtEntry e = key != null ? ArtCatalog.Load()?.Find(key) : null;
                return e != null && e.Clip(id) != null;
            }
            CosmeticsCatalog cat = CosmeticsCatalog.Load();
            if (cat == null) return false;
            string c = cls.ToString();
            CosmeticAsset a = cat.Find(c, SlotName(slot), id);
            if (a == null) return false;
            if (slot == CosmeticSlot.Head) return a.Model != null;
            return a.Texture != null && cat.Find(c, MountSlot, MountName)?.Model != null;
        }

        /// <summary>Art ids present for a class slot that the item table may not name (menus; allocates).</summary>
        public static List<string> ArtIds(ClassId cls, CosmeticSlot slot)
        {
            if (slot == CosmeticSlot.Emote) return new List<string>();
            CosmeticsCatalog cat = CosmeticsCatalog.Load();
            return cat != null ? cat.Ids(cls.ToString(), SlotName(slot)) : new List<string>();
        }

        /// <summary>The item a loadout slot resolves to: the saved id when it is unlocked and present, else null (bare head, no emblem).</summary>
        public static CosmeticItem Resolve(ClassId cls, CosmeticSlot slot, string savedId, int level)
        {
            CosmeticItem it = CosmeticItems.Find(cls, slot, savedId);
            if (it == null && !string.IsNullOrEmpty(savedId) && slot != CosmeticSlot.Emote)
                it = new CosmeticItem { Class = cls, Slot = slot, Id = savedId, DisplayName = CosmeticItems.DisplayName(savedId), UnlockLevel = CosmeticItems.ExtraItemLevel, Extra = true };
            if (it == null || !CosmeticItems.Unlocked(it, level) || !Has(cls, slot, it.Id)) return null;
            return it;
        }

        /// <summary>The victory emote clip name for a class (the saved one when usable, else the level-1 Cheer), or null.</summary>
        public static string EmoteClip(ClassId cls, ClassLoadout loadout, int level)
        {
            CosmeticItem it = Resolve(cls, CosmeticSlot.Emote, loadout != null ? loadout.Emote : null, level);
            if (it != null) return it.Id;
            CosmeticItem d = CosmeticItems.Default(cls, CosmeticSlot.Emote);
            return d != null && Has(cls, CosmeticSlot.Emote, d.Id) ? d.Id : null;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform t = FindDeep(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }

        /// <summary>
        /// The model root's bind-pose frame expressed in a joint's space (from the skinned body's bind poses), so a piece
        /// modelled in bind pose in body space and parented to the joint with this local transform lands where it was
        /// modelled. Identity when no skinned mesh binds the joint. Allocates (bind poses, bones): call on attach only.
        /// </summary>
        public static Matrix4x4 RootInJoint(Transform modelRoot, Transform joint)
        {
            foreach (SkinnedMeshRenderer smr in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Transform[] bones = smr.bones;
                int idx = Array.IndexOf(bones, joint);
                Mesh mesh = smr.sharedMesh;
                if (idx < 0 || mesh == null) continue;
                Matrix4x4[] bind = mesh.bindposes;
                if (idx >= bind.Length) continue;
                Matrix4x4 meshToRoot = modelRoot.worldToLocalMatrix * smr.transform.localToWorldMatrix;
                return bind[idx] * meshToRoot.inverse;
            }
            return Matrix4x4.identity;
        }

        public static void SetLocal(Transform t, Matrix4x4 m)
        {
            t.localPosition = m.GetColumn(3);
            t.localRotation = m.rotation;
            t.localScale = m.lossyScale;
        }

        /// <summary>The first renderer material of the model's body (the team material ActorViews set), or null.</summary>
        public static Material BodyMaterial(Transform modelRoot)
        {
            foreach (SkinnedMeshRenderer smr in modelRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMaterial != null) return smr.sharedMaterial;
            Renderer r = modelRoot.GetComponentInChildren<Renderer>(true);
            return r != null ? r.sharedMaterial : null;
        }

        static readonly Dictionary<string, Material> EmblemMaterials = new Dictionary<string, Material>();

        /// <summary>The body material with the emblem as its decal and no hull (ArtMaterials.Face's recipe); cached per pair.</summary>
        public static Material EmblemMaterial(Material body, Texture2D emblem)
        {
            if (body == null || emblem == null) return body;
            string key = body.GetInstanceID() + "|" + emblem.GetInstanceID();
            if (EmblemMaterials.TryGetValue(key, out Material m) && m != null) return m;
            m = new Material(body) { name = "CMToonArt emblem " + emblem.name };
            m.SetFloat("_DecalOn", 1f);
            m.SetTexture("_FaceTex", emblem);
            m.SetFloat("_OutlinePx", 0f);
            m.SetFloat("_OutlineWeightUV4", 0f);
            m.SetShaderPassEnabled("SRPDefaultUnlit", false);
            EmblemMaterials[key] = m;
            return m;
        }

        static void SetMaterials(GameObject go, Material m)
        {
            if (m == null) return;
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        /// <summary>
        /// Puts a class loadout on a model (live actor or preview): headgear on the head joint, the emblem on its mount.
        /// Pieces are pooled in the dresser and only rebuilt when the model, the team material or the choice changes.
        /// </summary>
        public static void Apply(CosmeticsDresser d, Transform modelRoot, ClassId cls, ClassLoadout loadout, int level)
        {
            if (d == null) return;
            if (modelRoot == null || loadout == null)
            {
                d.Detach();
                return;
            }
            CosmeticItem head = Resolve(cls, CosmeticSlot.Head, loadout.Head, level);
            CosmeticItem emblem = Resolve(cls, CosmeticSlot.Emblem, loadout.Emblem, level);
            Material body = BodyMaterial(modelRoot);
            string headId = head != null ? head.Id : null, emblemId = emblem != null ? emblem.Id : null;
            if (d.Host == modelRoot && d.HeadId == headId && d.EmblemId == emblemId && d.BodyMaterial == body && d.Class == cls) return;
            d.Detach();
            d.Host = modelRoot;
            d.Class = cls;
            d.BodyMaterial = body;
            d.HeadId = headId;
            d.EmblemId = emblemId;
            CosmeticsCatalog cat = CosmeticsCatalog.Load();
            if (cat == null) return;
            string c = cls.ToString();
            if (head != null)
            {
                Transform joint = FindDeep(modelRoot, HeadJoint);
                CosmeticAsset a = cat.Find(c, HeadSlot, head.Id);
                if (joint != null && a != null && a.Model != null)
                {
                    d.Head = d.Take(a.Model, "Headgear " + head.Id);
                    d.Head.transform.SetParent(joint, false);
                    SetLocal(d.Head.transform, RootInJoint(modelRoot, joint));
                    SetMaterials(d.Head, body);
                    d.Head.SetActive(true);
                }
            }
            if (emblem != null)
            {
                CosmeticAsset mount = cat.Find(c, MountSlot, MountName);
                CosmeticAsset tex = cat.Find(c, EmblemSlot, emblem.Id);
                Transform socket = FindDeep(modelRoot, EmblemSocket);
                Transform joint = socket != null ? socket : FindDeep(modelRoot, EmblemBone(cls));
                if (joint != null && mount != null && mount.Model != null && tex != null && tex.Texture != null)
                {
                    d.Mount = d.Take(mount.Model, "Emblem Mount");
                    d.Mount.transform.SetParent(joint, false);
                    SetLocal(d.Mount.transform, socket != null ? Matrix4x4.identity : RootInJoint(modelRoot, joint));
                    SetMaterials(d.Mount, EmblemMaterial(body, tex.Texture));
                    d.Mount.SetActive(true);
                }
            }
        }
    }

    /// <summary>The cosmetic pieces one model wears, pooled by prefab so re-dressing after a respawn instantiates nothing new.</summary>
    public sealed class CosmeticsDresser
    {
        public Transform Host;
        public ClassId Class;
        public Material BodyMaterial;
        public string HeadId, EmblemId;
        public GameObject Head, Mount;

        /// <summary>Where detached pieces wait (inactive). Set by the owner.</summary>
        public readonly Transform Holder;
        readonly Dictionary<GameObject, GameObject> _pool = new Dictionary<GameObject, GameObject>();
        /// <summary>Hard cap on pooled pieces (15 headgear + 5 mounts is the full first set).</summary>
        public const int MaxPooled = 32;

        public CosmeticsDresser(Transform holder)
        {
            Holder = holder;
        }

        public GameObject Take(GameObject prefab, string name)
        {
            if (_pool.TryGetValue(prefab, out GameObject go) && go != null) return go;
            if (_pool.Count >= MaxPooled) Clear();
            go = UnityEngine.Object.Instantiate(prefab, Holder, false);
            go.name = name;
            go.SetActive(false);
            _pool[prefab] = go;
            return go;
        }

        /// <summary>Takes the pieces off (back to the holder, inactive); the host keeps nothing of ours.</summary>
        public void Detach()
        {
            Park(Head);
            Park(Mount);
            Head = Mount = null;
            Host = null;
            BodyMaterial = null;
            HeadId = EmblemId = null;
        }

        void Park(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            if (Holder != null) go.transform.SetParent(Holder, false);
        }

        /// <summary>Destroys every pooled piece.</summary>
        public void Clear()
        {
            Detach();
            foreach (GameObject go in _pool.Values) if (go != null) UnityEngine.Object.Destroy(go);
            _pool.Clear();
        }
    }

    /// <summary>
    /// Dresses the LOCAL player's model only (actor 0), never anyone else, from the profile's loadout. Read-only on the
    /// sim. Finds the model through Cosmetics.ActorModelResolver when set, else by the ActorViews object name
    /// "Actor 0 &lt;key&gt;" (searched only when the model changed: a respawn, a class change; never per frame). Each frame it
    /// checks, without allocating, that the dressed model is still active and still at actor 0's position, so a pooled
    /// model handed to another actor loses our pieces before it is seen.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 33, Scope = CriticalMass.Runtime.Flow.OverlayScope.Always)]   // installed once by GameBootstrap through the overlay registry
    public sealed class LocalCosmetics : MonoBehaviour
    {
        const int Local = 0;
        const float SearchEverySeconds = 0.25f;
        const float SamePlaceSqr = 2.25f;

        SimDriverBehaviour _driver;
        CosmeticsDresser _dresser;
        Transform _holder;
        Transform _model;
        ClassId _modelClass;
        float _nextSearch;
        bool _dirty = true;
        static string[] _names;

        public static LocalCosmetics Instance { get; private set; }
        public CosmeticsDresser Dresser => _dresser;

        void OnEnable()
        {
            Instance = this;
            Profile.Changed += MarkDirty;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
            Profile.Changed -= MarkDirty;
        }

        void MarkDirty() => _dirty = true;

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            var h = new GameObject("Cosmetics Pool");
            h.transform.SetParent(transform, false);
            h.SetActive(false);
            _holder = h.transform;
            _dresser = new CosmeticsDresser(_holder);
        }

        void OnDestroy()
        {
            _dresser?.Clear();
        }

        void LateUpdate()
        {
            if (_driver == null || _dresser == null) return;
            World w = _driver.World;
            if (w == null || w.ActorCount <= Local) return;
            Actor me = w.Actors[Local];
            if (_model != null && (!me.Alive || me.Cls != _modelClass || !_model.gameObject.activeInHierarchy || !SamePlace(w)))
            {
                _dresser.Detach();
                _model = null;
            }
            if (!me.Alive) return;
            if (_model == null)
            {
                if (Time.unscaledTime < _nextSearch) return;
                _nextSearch = Time.unscaledTime + SearchEverySeconds;
                _model = FindModel(me.Cls);
                if (_model == null || !SamePlace(w))
                {
                    _model = null;
                    return;
                }
                _modelClass = me.Cls;
                _dirty = true;
            }
            if (!_dirty) return;
            _dirty = false;
            PlayerProfile p = Profile.Current;
            Cosmetics.Apply(_dresser, _model, me.Cls, p.Loadout(me.Cls), p.Level);
        }

        bool SamePlace(World w)
        {
            Vector3 want = SimSpace.ToWorld(w.X[Local], 0, w.Z[Local]);
            Vector3 at = _model.localPosition;
            float dx = at.x - want.x, dz = at.z - want.z;
            return dx * dx + dz * dz < SamePlaceSqr;
        }

        Transform FindModel(ClassId cls)
        {
            if (Cosmetics.ActorModelResolver != null) return Cosmetics.ActorModelResolver(Local);
            string key = ArtMapping.ClassModel(cls);
            if (key == null) return null;
            if (_names == null)
            {
                _names = new string[ClassIds.All.Count];
                foreach (ClassId c in ClassIds.All)
                {
                    string k = ArtMapping.ClassModel(c);
                    _names[(int)c] = k != null ? "Actor " + Local + " " + k : null;
                }
            }
            string want = _names[(int)cls];
            ActorViews views = GetComponent<ActorViews>();
            Transform parent = views != null ? views.transform : transform;
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform t = parent.GetChild(i);
                if (t.gameObject.activeSelf && t.name == want) return t;
            }
            return null;
        }
    }
}
