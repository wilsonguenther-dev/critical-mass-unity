using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// hud-containment-2: the toast lane (HudZones.Toast, the right column under the stingers) with a cap and a queue, like
    /// the sound captions. One short notice shows at a time ("VIEW: THIRD PERSON", "AUTO-DIRECTOR ON"); an achievement toast
    /// holds the lane (HudZones.Hold at Banner) and a notice waits behind it instead of drawing on top. The same words posted
    /// again refresh the one queued or shown (pressing the view key five times is one notice, not five). When the queue is
    /// full the oldest waiting notice is dropped. The camera notice used to print across the whole screen at 22% height,
    /// over the top band and the callouts. No allocations after the first post: fixed ring of strings.
    /// </summary>
    public static class ToastLane
    {
        public const int Cap = 3;
        public const float Seconds = 2f;

        static readonly string[] _queue = new string[Cap];
        static int _head, _count;
        static string _shown;
        static float _until = -1f;

        /// <summary>Waiting notices (tests and captures).</summary>
        public static int Queued => _count;

        /// <summary>Adds a notice; the same words already shown or waiting are refreshed in place.</summary>
        public static void Post(string s, float now)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (s == _shown && now < _until) { _until = now + Seconds; return; }
            for (int i = 0; i < _count; i++) if (_queue[(_head + i) % Cap] == s) return;
            if (_count == Cap) { _head = (_head + 1) % Cap; _count--; }
            _queue[(_head + _count) % Cap] = s;
            _count++;
        }

        /// <summary>The notice on the lane now, or null. Waits while something of higher priority holds the toast zone.</summary>
        public static string Current(float now)
        {
            if (_shown != null && now < _until) return _shown;
            _shown = null;
            if (_count == 0 || HudZones.Blocked(HudZone.Toast, HudPriority.Tip)) return null;
            _shown = _queue[_head];
            _queue[_head] = null;
            _head = (_head + 1) % Cap;
            _count--;
            _until = now + Seconds;
            return _shown;
        }

        /// <summary>Empties the lane (a new match, tests).</summary>
        public static void Clear()
        {
            for (int i = 0; i < Cap; i++) _queue[i] = null;
            _head = _count = 0;
            _shown = null;
            _until = -1f;
        }

        /// <summary>The notice's rect on a w x h screen, device px, top-left origin: the top of the toast zone.</summary>
        public static Rect PixelRect(float w, float h, float lineH)
        {
            Rect z = HudZones.PixelRect(HudZone.Toast, w, h);
            return new Rect(z.x, z.y, z.width, Mathf.Min(z.height, lineH));
        }
    }
}
