using CriticalMass.Runtime.Hud.Style;
using UnityEngine;

namespace CriticalMass.Runtime.Menu
{
    /// <summary>
    /// Owner walk 1: pointer feedback. Over anything clickable the cursor turns into the game's pointer (a Tool Brass arrow
    /// with a Void Navy outline and a small click tab, drawn once in code); off it, the system arrow comes back. Every
    /// MenuItem and the home fighter call <see cref="Hover"/> on pointer enter and exit. View only; the texture is made once.
    /// </summary>
    public static class UiCursor
    {
        public const int Size = 32;
        static Texture2D _tex;
        static int _hovers;

        /// <summary>The pointer is over something clickable (tests).</summary>
        public static bool Hovering => _hovers > 0;

        public static void Hover(bool on)
        {
            bool was = _hovers > 0;
            _hovers = on ? 1 : 0;
            if (was == (_hovers > 0)) return;
            Apply();
        }

        /// <summary>Back to the system arrow (a screen closed under the pointer).</summary>
        public static void Reset() => Hover(false);

        static void Apply()
        {
            if (!Application.isPlaying || Application.isBatchMode) return;
            if (_hovers > 0)
            {
                if (_tex == null) _tex = Make();
                Cursor.SetCursor(_tex, new Vector2(2f, 1f), CursorMode.Auto);
            }
            else Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
        }

        /// <summary>The pointer texture: an arrow (tip at 2,1) filled brass, outlined navy, with a cream highlight edge.</summary>
        public static Texture2D Make()
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false) { name = "CM Pointer", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            Color fill = CMTokens.ToolBrass.Color, line = CMTokens.VoidNavy.Color, hi = CMTokens.WarmCream.Color;
            // arrow outline in texture px, y down from the top
            Vector2[] poly =
            {
                new Vector2(2f, 1f), new Vector2(2f, 25f), new Vector2(8.5f, 19.5f), new Vector2(13f, 29f), new Vector2(17.5f, 27f),
                new Vector2(13f, 17.5f), new Vector2(21.5f, 17.5f),
            };
            var px = new Color[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                float d = Distance(p, poly);
                bool inside = Inside(p, poly);
                Color c = new Color(0f, 0f, 0f, 0f);
                if (inside && d > 2.2f) c = d < 3.2f && p.x < 9f ? hi : fill;
                else if (inside || d < 1.2f) c = line;
                else if (d < 2.2f) c = new Color(line.r, line.g, line.b, Mathf.Clamp01(2.2f - d));
                px[(Size - 1 - y) * Size + x] = c;
            }
            t.SetPixels(px);
            t.Apply(false, false);
            return t;
        }

        static bool Inside(Vector2 p, Vector2[] poly)
        {
            bool c = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x) c = !c;
            return c;
        }

        static float Distance(Vector2 p, Vector2[] poly)
        {
            float best = float.MaxValue;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[j], b = poly[i], ab = b - a;
                float k = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-4f, ab.sqrMagnitude));
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * k));
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => _hovers = 0;
    }
}
