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
