using System.Collections.Generic;
using CriticalMass.Runtime.Hud.Style;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// The illustrated HUD icon set (bible p15, v2): vector art drawn by Assets/_Game/Art/UI/Icons/_source~/build_icons.py,
    /// exported at @1x (the size shown on a 1920x1080 screen), @2x and @4x into Resources/UI/Icons. The HUD picks the tier
    /// for its canvas scale so an icon is always drawn from a texture at least as large as its screen size (sharp at 720p,
    /// 1080p and 4K) and mipmapped down, never stretched up.
    /// </summary>
    public static class UiIcons
    {
        public const string Folder = "UI/Icons/";
        public static readonly int[] Tiers = { 1, 2, 4 };

        static readonly Dictionary<string, Texture2D>[] Cache =
            { new Dictionary<string, Texture2D>(), new Dictionary<string, Texture2D>(), new Dictionary<string, Texture2D>() };

        /// <summary>1 below 0.75 canvas scale (720p), 2 up to 1.5 (1080p, 1440p), 4 above (4K).</summary>
        public static int TierFor(float canvasScale) => canvasScale <= 0.75f ? 1 : canvasScale <= 1.5f ? 2 : 4;

        static int Index(int tier) => tier <= 1 ? 0 : tier == 2 ? 1 : 2;

        /// <summary>The texture of icon name at tier (cached; null if the file is missing).</summary>
        public static Texture2D Get(string name, int tier)
        {
            if (string.IsNullOrEmpty(name)) return null;
            Dictionary<string, Texture2D> d = Cache[Index(tier)];
            if (d.TryGetValue(name, out Texture2D t)) return t;
            t = Resources.Load<Texture2D>(Folder + name + "@" + (tier <= 1 ? 1 : tier == 2 ? 2 : 4) + "x");
            if (t != null)
            {
                t.wrapMode = TextureWrapMode.Clamp;
                t.filterMode = FilterMode.Trilinear;
            }
            d[name] = t;
            return t;
        }

        // ---- names, built once so the per-frame HUD never concatenates strings

        public static readonly string[] Teams = { "azure", "crimson" };
        public static readonly string[] Classes = { "warrior", "farmer", "miner", "demolitions", "builder", "healer" };
        /// <summary>Icon rows by ClassId (hud-art-1: the Healer has its own portrait, attack, ability and class icons).</summary>
        static readonly string[] GridClasses = { "warrior", "farmer", "miner", "demolitions", "builder", "healer" };
        public static readonly string[] Crest = { "crest-azure", "crest-crimson" };
        public static readonly string[] Banner = { "map-banner-azure", "map-banner-crimson" };
        public static readonly string[] Heart = { "heart-azure", "heart-crimson" };
        public const string HeartEmpty = "heart-empty";
        public static readonly string[,] Portrait = Grid("portrait-");
        public static readonly string[,] ClassBadge = Grid("class-");
        public static readonly string[,] Attack = Grid("attack-");
        public static readonly string[,] Ability = Grid("ability-");
        public const string Biomass = "carry-biomass", Corruption = "carry-corruption", CarryEmpty = "carry-empty";
        public const string Crown = "crown", MapCrown = "map-crown", Egg = "kaiju-egg", MapEgg = "map-egg", You = "map-you";
        public const string Hatchling = "kaiju-hatchling";
        /// <summary>hud-art-1: the egg in its last seconds before the hatch (GameHud.CrackSeconds).</summary>
        public const string EggCracking = "kaiju-egg-cracking";
        /// <summary>hud-art-1: resource icons (coin chip, team stock).</summary>
        public const string Coin = "res-coin", Wood = "res-wood", Water = "res-water", Food = "res-food";
        public static readonly string[] Stock = { Wood, Water, Food };
        /// <summary>hud-art-1: power-up icons by PickupKind - 1 (Haste, Might, Ward).</summary>
        public static readonly string[] Items = { "item-haste", "item-might", "item-ward" };
        public static readonly string[] Adult = { "kaiju-adult-neutral", "kaiju-adult-azure", "kaiju-adult-crimson", "kaiju-adult-berserk" };

        static string[,] Grid(string prefix)
        {
            var a = new string[GridClasses.Length, Teams.Length];
            for (int c = 0; c < GridClasses.Length; c++)
            for (int t = 0; t < Teams.Length; t++)
                a[c, t] = prefix + GridClasses[c] + "-" + Teams[t];
            return a;
        }

        /// <summary>Every icon name the HUD can show (tests check each ships at every tier).</summary>
        public static IEnumerable<string> All()
        {
            foreach (string s in Crest) yield return s;
            foreach (string s in Banner) yield return s;
            foreach (string s in Heart) yield return s;
            yield return HeartEmpty;
            foreach (string[,] g in new[] { Portrait, ClassBadge, Attack, Ability })
                foreach (string s in g) yield return s;
            yield return Biomass; yield return Corruption; yield return CarryEmpty;
            yield return Crown; yield return MapCrown; yield return Egg; yield return MapEgg; yield return You; yield return Hatchling;
            yield return EggCracking; yield return Coin;
            foreach (string s in Stock) yield return s;
            foreach (string s in Items) yield return s;
            foreach (string s in Adult) yield return s;
        }
    }

    /// <summary>One icon on the HUD: a RawImage that swaps texture by name and by tier, with an optional horizontal fill
    /// (hearts) done by cropping the rect and its uvRect, so partial fills keep the art's outline and tone steps.</summary>
    public sealed class HudIcon
    {
        public readonly RawImage Image;
        public string Name { get; private set; }
        int _tier = -1;
        float _fill = 1f;
        float _w;

        public HudIcon(RawImage image) { Image = image; _w = image.rectTransform.sizeDelta.x; }

        public RectTransform Rt => Image.rectTransform;

        public void Set(string name, int tier)
        {
            if (ReferenceEquals(name, Name) && tier == _tier) return;
            Name = name;
            _tier = tier;
            Texture2D t = UiIcons.Get(name, tier);
            if (Image.texture != t) Image.texture = t;
            Image.enabled = t != null;
        }

        public void SetTier(int tier) { if (Name != null) Set(Name, tier); }

        /// <summary>0..1 from the left edge (pivot stays where Build put it; the rect is left-anchored for fills).</summary>
        public float Fill01
        {
            get => _fill;
            set
            {
                float f = Mathf.Clamp01(value);
                if (Mathf.Abs(f - _fill) < 1e-4f) return;
                _fill = f;
                RectTransform rt = Image.rectTransform;
                rt.sizeDelta = new Vector2(_w * f, rt.sizeDelta.y);
                Image.uvRect = new Rect(0f, 0f, f, 1f);
            }
        }

        public static HudIcon Make(Transform parent, string goName, float x, float y, float w, float h, bool leftPivot = false)
        {
            var go = new GameObject(goName, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            RawImage img = go.AddComponent<RawImage>();
            img.raycastTarget = false;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = leftPivot ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x + (leftPivot ? 0f : w * 0.5f), -(y + h * 0.5f));
            return new HudIcon(img);
        }
    }

    /// <summary>
    /// Death state colour grade (art direction 6.3, v2): a full-screen graphic under the HUD clusters whose shader reads
    /// URP's opaque texture (the world before the UI is drawn) and writes it back at 70% brightness and 60% saturation,
    /// so the HUD drawn after it stays at full brightness and colour. View only. GameHud asks the camera for the opaque
    /// texture only while the player is dead.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class DeathGrade : MaskableGraphic
    {
        public const string ShaderResource = "UI/Shaders/CMDeathGrade";
        public const float Brightness = 0.7f, Saturation = 0.6f;
        static Material _mat;

        public static Material GradeMaterial
        {
            get
            {
                if (_mat == null)
                {
                    Shader sh = Resources.Load<Shader>(ShaderResource);
                    if (sh == null) return null;
                    _mat = new Material(sh) { name = "CM Death Grade", hideFlags = HideFlags.DontSave };
                    _mat.SetFloat("_Brightness", Brightness);
                    _mat.SetFloat("_Saturation", Saturation);
                }
                return _mat;
            }
        }

        protected override void Awake()
        {
            base.Awake();
            material = GradeMaterial;
            color = Color.white;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            Color c = Color.white;
            vh.AddVert(new Vector3(r.xMin, r.yMin), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMin), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), c, Vector4.zero);
            vh.AddVert(new Vector3(r.xMin, r.yMax), c, Vector4.zero);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(0, 2, 3);
        }
    }
}
