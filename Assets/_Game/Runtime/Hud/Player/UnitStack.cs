using UnityEngine;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Review fix round, defect 4: the ONE layout owner of what stacks over a unit, so nothing overlaps at either camera.
    /// From the name-tag point up (screen px at 1080p, y up): the name tag (another lane draws it; 26 px tall x tag scale,
    /// at most 2 x NameHalfWMax wide), then UNDER it the health bar (BarGap below the name), and ABOVE it one row of word
    /// pills (blocked / refused / critical words: TagGap above the name, PillH tall, further rows stack up). Damage
    /// numbers do not live in that column: they rise from the BODY (chest height), to the side of the column (left edge
    /// NameHalfWMax + NumberSide right of the body), and are clamped under the word row, so a number can never cover a
    /// name, a class, a bar or a word. Pure; PxHudLayoutTests checks the rectangles at both cameras.
    /// </summary>
    public static class UnitStack
    {
        public const float NameHalfH = 13f, NameHalfWMax = 70f, BarGap = 3f, TagGap = 6f, PillH = WorldWords.PillH, RowGap = 4f;
        public const float NumberSide = 10f, ChestLift = 1.2f, DigitW = 0.62f;

        /// <summary>Centre of the health bar under the name tag at tagPx.</summary>
        public static Vector2 BarCentre(Vector2 tagPx, float tagScale, float barH) =>
            new Vector2(tagPx.x, tagPx.y - NameHalfH * tagScale - BarGap - barH * 0.5f);

        /// <summary>Screen offset (up) from the name-tag point to the centre of word row n (0 = first row).</summary>
        public static float TagRowUp(float tagScale, int row) => NameHalfH * tagScale + TagGap + PillH * 0.5f + row * (PillH + RowGap);

        /// <summary>The y the bottom of the word row sits at (numbers stay under it).</summary>
        public static float TagRowBottom(Vector2 tagPx, float tagScale) => tagPx.y + NameHalfH * tagScale + TagGap;

        /// <summary>
        /// Left-middle point of a damage number: chestPx = the body at ChestLift, risePx = its rise so far, stack = how many
        /// newer numbers on this body sit under it. Clamped so the number's top stays RowGap under the word row.
        /// </summary>
        public static Vector2 NumberLeftMid(Vector2 chestPx, Vector2 tagPx, float tagScale, int size, float risePx, int stack)
        {
            float x = chestPx.x + NameHalfWMax + NumberSide;
            float y = chestPx.y + risePx + stack * size * 0.9f;
            float top = y + size * 0.6f, limit = TagRowBottom(tagPx, tagScale) - RowGap;
            if (top > limit) y -= top - limit;
            return new Vector2(x, y);
        }

        /// <summary>
        /// Review fix round 2 (pass-2 defect 4): the same, kept clear of MY fighter. <paramref name="mine"/> is my fighter's
        /// screen rect (body, name and bar, MineRect). The number goes on the enemy's side away from me: right of his name
        /// column when I am to his left, left of it when I am to his right; if it still touches my rect it is pushed past
        /// my rect's far edge on that side. Never over my fighter, my name or my bar.
        /// </summary>
        public static Vector2 NumberLeftMid(Vector2 chestPx, Vector2 tagPx, float tagScale, int size, float risePx, int stack, Rect mine, int digits)
        {
            Vector2 p = NumberLeftMid(chestPx, tagPx, tagScale, size, risePx, stack);
            if (mine.width <= 0f) return p;
            float w = digits * size * DigitW + 6f;
            bool meRight = mine.center.x >= chestPx.x;
            float x = meRight ? chestPx.x - NameHalfWMax - NumberSide - w : chestPx.x + NameHalfWMax + NumberSide;
            Rect r = NumberRect(new Vector2(x, p.y), size, digits);
            if (r.Overlaps(mine)) x = meRight ? mine.xMin - NumberSide - w : mine.xMax + NumberSide;
            return new Vector2(x, p.y);
        }

        /// <summary>My fighter's screen rect: feet to the top of the name, as wide as the name column (or the body).</summary>
        public static Rect MineRect(Vector2 feetPx, Vector2 tagPx, float tagScale, float halfBodyW)
        {
            float half = Mathf.Max(NameHalfWMax, halfBodyW);
            float top = tagPx.y + NameHalfH * tagScale, bottom = Mathf.Min(feetPx.y, tagPx.y) - 4f;
            float cx = (feetPx.x + tagPx.x) * 0.5f;
            return new Rect(cx - half, bottom, 2f * half, top - bottom);
        }

        public static Rect NameRect(Vector2 tagPx, float tagScale) =>
            new Rect(tagPx.x - NameHalfWMax, tagPx.y - NameHalfH * tagScale, 2f * NameHalfWMax, 2f * NameHalfH * tagScale);

        public static Rect BarRect(Vector2 tagPx, float tagScale, float barW, float barH)
        {
            Vector2 c = BarCentre(tagPx, tagScale, barH);
            return new Rect(c.x - barW * 0.5f, c.y - barH * 0.5f, barW, barH);
        }

        public static Rect TagRect(Vector2 tagPx, float tagScale, int row, float pillW) =>
            new Rect(tagPx.x - pillW * 0.5f, tagPx.y + TagRowUp(tagScale, row) - PillH * 0.5f, pillW, PillH);

        public static Rect NumberRect(Vector2 leftMid, int size, int digits) =>
            new Rect(leftMid.x, leftMid.y - size * 0.6f, digits * size * DigitW + 6f, size * 1.2f);
    }
}
