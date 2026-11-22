using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// b14 (owner's screenshot: a "Base station" prompt sat on another bar and cut its text to "...BLOCKS 0/3", and a third
    /// prompt stacked just below): ONE stacking region for the bottom-centre prompts, with a queue. At most MaxVisible show,
    /// by priority (the station prompt, then the terrain pill, then the Builder's tool strip); the rest wait their turn.
    /// Slot 0 is the reserved prompt row (HudZones.PromptLane, under the lower lane, above the sound captions); slot 1 is
    /// the row right above it, the bottom of the lower lane, so it steps back (one prompt only) while the intro line or
    /// anything above it holds the lower lane (HudZones.Hold(HudZone.Lower, ...)). Every piece is anchored to the bottom centre of its canvas at the HUD scale
    /// (HudLayout.Scale), so the slots line up on every canvas and at every screen shape.
    /// <para>Each source calls Want every frame it has something to say, then asks SlotOf. Wants count for this frame and
    /// the last, so the order the sources update in never makes two of them take the same slot.</para>
    /// </summary>
    public static class PromptStack
    {
        public enum Source { Station = 0, Pill = 1, Strip = 2 }

        public const int Sources = 3, MaxVisible = 2;
        /// <summary>Canvas units between two stacked prompts.</summary>
        public const float Gap = 8f;
        /// <summary>One slot's height (the pill's reserved row).</summary>
        public const float SlotH = HudZones.PromptLaneH;
        /// <summary>Nominal size and centre offset (canvas units) of each source, for the layout test and the placement.</summary>
        public static readonly Vector2[] Size = { new Vector2(HudZones.PromptW, HudZones.PromptH), new Vector2(720f, SlotH), new Vector2(620f, 46f) };
        /// <remarks>All centred: the tool strip used to sit 190 right of centre, and one row up it ran into the power-up timer at 1280x720.</remarks>
        public static readonly float[] CentreX = { 0f, 0f, 0f };

        static readonly int[] _wantFrame = { int.MinValue, int.MinValue, int.MinValue };
        static readonly bool[] _wanted = new bool[Sources];

        /// <summary>The bottom of slot 0, canvas units above the screen bottom (the prompt row's bottom).</summary>
        public static float BaseBottom => HudLayout.RefH - HudZones.PromptLane.yMax;

        /// <summary>Source s has a prompt this frame.</summary>
        public static void Want(Source s) => _wantFrame[(int)s] = Time.frameCount;

        /// <summary>Source s has nothing to say (its slot frees at once).</summary>
        public static void Release(Source s) => _wantFrame[(int)s] = int.MinValue;

        /// <summary>The slot source s shows in (0 = the prompt row, 1 = the row above), or -1 = queued (hidden).</summary>
        public static int SlotOf(Source s)
        {
            int f = Time.frameCount;
            for (int i = 0; i < Sources; i++) _wanted[i] = _wantFrame[i] >= f - 1;
            // review fix round 2 (pass-2 defect 5): ONE prompt line in a fight (PxFight), the most relevant (station first)
            int visible = CriticalMass.Runtime.Hud.Player.PxFight.Active ? 1 : Visible(HudZones.Blocked(HudZone.Lower, HudPriority.Guide));
            return Assign(_wanted, (int)s, visible);
        }

        /// <summary>How many prompts may show: one while the lower lane is held (slot 1 is its bottom), else MaxVisible.</summary>
        public static int Visible(bool lowerLaneHeld) => lowerLaneHeld ? 1 : MaxVisible;

        /// <summary>Pure: the slot of source among the wanted ones, by priority (lower index first); -1 when it is not wanted
        /// or is past maxVisible.</summary>
        public static int Assign(bool[] wanted, int source, int maxVisible)
        {
            if (wanted == null || source < 0 || source >= wanted.Length || !wanted[source]) return -1;
            int rank = 0;
            for (int i = 0; i < source; i++) if (wanted[i]) rank++;
            return rank < maxVisible ? rank : -1;
        }

        /// <summary>The bottom of slot k (canvas units above the screen bottom); a shorter piece is centred in its slot.</summary>
        public static float Bottom(int slot, float height) => BaseBottom + slot * (SlotH + Gap) + Mathf.Max(0f, (SlotH - height) * 0.5f);

        /// <summary>Source s in slot k on a w x h screen, in device pixels (top-left origin), at its nominal size.</summary>
        public static Rect ScreenRect(Source s, int slot, float w, float h)
        {
            float sc = HudLayout.Scale(w, h);
            Vector2 size = Size[(int)s];
            float width = Mathf.Min(size.x, HudZones.BottomBandW);
            float b = Bottom(slot, size.y);
            return new Rect(0.5f * w + (CentreX[(int)s] - width * 0.5f) * sc, h - (b + size.y) * sc, width * sc, size.y * sc);
        }
    }
}
