using System;

namespace CriticalMass.Runtime.Audio.Sound
{
    /// <summary>
    /// Distance maths for the top-down camera (pure; tests). Distances are world units on the ground plane from the
    /// gameplay camera's focus point. Far sounds lose highs (one-pole low-pass on each voice) and big blasts arrive late.
    /// </summary>
    public static class SoundSpace
    {
        public const float OpenCutoff = 22000f, FarCutoff = 1400f;
        /// <summary>Blasts farther than DelayStart arrive (d - DelayStart) / DelaySpeed seconds late, at most MaxDelay.</summary>
        public const float DelayStart = 14f, DelaySpeed = 260f, MaxDelay = 0.22f;
        public const float PanWidth = 0.75f;

        /// <summary>Near and far distances of a range (Ui has no attenuation).</summary>
        public static void Range(SoundRange r, out float near, out float far)
        {
            switch (r)
            {
                case SoundRange.Near: near = 6f; far = 28f; return;
                case SoundRange.Mid: near = 9f; far = 40f; return;
                case SoundRange.Far: near = 14f; far = 70f; return;
                case SoundRange.Huge: near = 22f; far = 140f; return;
                default: near = 1e6f; far = 2e6f; return;
            }
        }

        /// <summary>1 within near, 0 at far, a smooth square falloff between.</summary>
        public static float Gain(float d, float near, float far)
        {
            if (d <= near) return 1f;
            if (d >= far) return 0f;
            float t = (d - near) / (far - near);
            return (1f - t) * (1f - t);
        }

        /// <summary>Stereo pan -1..1 from the ground offset (dx, dz) to the sound and the camera's flattened right vector.</summary>
        public static float Pan(float dx, float dz, float rightX, float rightZ)
        {
            float d = (float)Math.Sqrt(dx * dx + dz * dz);
            if (d < 0.5f) return 0f;
            float rl = (float)Math.Sqrt(rightX * rightX + rightZ * rightZ);
            if (rl < 1e-5f) return 0f;
            float p = (dx * rightX + dz * rightZ) / (d * rl);
            // Fade the pan in over the first few units so sounds at the focus stay centred.
            float spread = d < 4f ? d / 4f : 1f;
            return Clamp(p * PanWidth * spread, -1f, 1f);
        }

        /// <summary>Low-pass cutoff (Hz) for a distance: open within near, sliding to FarCutoff at far.</summary>
        public static float LowPass(float d, float near, float far)
        {
            if (d <= near) return OpenCutoff;
            float t = d >= far ? 1f : (d - near) / (far - near);
            return OpenCutoff * (float)Math.Pow(FarCutoff / OpenCutoff, t);
        }

        /// <summary>Arrival delay (s) of a distant blast.</summary>
        public static float Delay(float d) => d <= DelayStart ? 0f : Math.Min(MaxDelay, (d - DelayStart) / DelaySpeed);

        /// <summary>
        /// Voice priority: the cue's base, +40 for the player's own actions, +25 for a threat to the player (a wind-up at
        /// them, a blast or mine beside them), +20 x how loud it will be after distance.
        /// </summary>
        public static float Priority(int basePriority, float audibleGain, bool own, bool threat) =>
            basePriority + (own ? 60f : 0f) + (threat ? 30f : 0f) + 20f * Clamp(audibleGain, 0f, 1f);   // integration batch 4: +60 / +30 so a quiet own footstep and a near wind-up both beat a full-volume crowd hit (45 + 20), as SoundCoverageTests asks

        /// <summary>
        /// Crowd gain: with more than CrowdFree other voices playing, everything that is not the player's own sound is
        /// scaled by sqrt(CrowdFree / busy) so 32 fighters add up to a mix, not a wall.
        /// </summary>
        public const int CrowdFree = 8;
        public static float Crowd(int busy, bool own) => own || busy <= CrowdFree ? 1f : (float)Math.Sqrt(CrowdFree / (double)busy);

        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }

    /// <summary>Why the planner turned a request down (tests, the budget report).</summary>
    public enum VoiceRefusal : byte { None, Cooldown, CueCap, FrameCap, Outranked, Inaudible, Muted }

    /// <summary>
    /// The voice limiter (pure; tests): a fixed number of voices, per-cue voice caps and cooldowns, a cap on new voices
    /// per frame, and priority stealing. The player's own actions skip cooldowns (a 30 ms repeat guard stays) and steal
    /// the oldest voice of their own cue when it is at its cap. Times are wall seconds. No allocation after construction.
    /// </summary>
    public sealed class VoicePlanner
    {
        public const double OwnRepeatGuard = 0.03;
        public const int StartsPerFrame = 8;

        private struct Slot { public int Cue; public float Priority; public double Started, Ends; }

        private readonly Slot[] _slots;
        private readonly double[] _lastPlay;
        private readonly int[] _maxVoices;
        private readonly float[] _cooldown;
        private int _frame = int.MinValue, _startsThisFrame;

        public int Capacity => _slots.Length;
        public int Started { get; private set; }
        public int Refused { get; private set; }
        public int Stolen { get; private set; }
        public VoiceRefusal LastRefusal { get; private set; }

        public VoicePlanner(int voices, int[] maxVoicesPerCue, float[] cooldownPerCue)
        {
            _slots = new Slot[Math.Max(1, voices)];
            for (int i = 0; i < _slots.Length; i++) { _slots[i].Cue = -1; _slots[i].Ends = double.NegativeInfinity; }
            _maxVoices = maxVoicesPerCue;
            _cooldown = cooldownPerCue;
            _lastPlay = new double[maxVoicesPerCue.Length];
            for (int i = 0; i < _lastPlay.Length; i++) _lastPlay[i] = double.NegativeInfinity;
        }

        /// <summary>Builds a planner from the sound table's caps and cooldowns.</summary>
        public static VoicePlanner FromTable(int voices)
        {
            int n = SoundTable.Cues.Length;
            var max = new int[n];
            var cd = new float[n];
            for (int i = 0; i < n; i++) { max[i] = Math.Max(1, SoundTable.Cues[i].MaxVoices); cd[i] = SoundTable.Cues[i].Cooldown; }
            return new VoicePlanner(voices, max, cd);
        }

        public bool Busy(int slot, double now) => slot >= 0 && slot < _slots.Length && _slots[slot].Ends > now;
        public int CueOf(int slot) => slot >= 0 && slot < _slots.Length ? _slots[slot].Cue : -1;
        public float PriorityOf(int slot) => slot >= 0 && slot < _slots.Length ? _slots[slot].Priority : 0f;

        public int BusyCount(double now)
        {
            int n = 0;
            for (int i = 0; i < _slots.Length; i++) if (_slots[i].Ends > now) n++;
            return n;
        }

        public int Playing(int cue, double now)
        {
            int n = 0;
            for (int i = 0; i < _slots.Length; i++) if (_slots[i].Cue == cue && _slots[i].Ends > now) n++;
            return n;
        }

        /// <summary>Marks a frame boundary for the per-frame start cap.</summary>
        public void BeginFrame(int frame)
        {
            if (frame == _frame) return;
            _frame = frame;
            _startsThisFrame = 0;
        }

        /// <summary>
        /// Asks for a voice for cue at priority, lasting length seconds from now. Returns the slot (its previous sound, if
        /// any, is stopped by the caller) or -1 with LastRefusal set.
        /// </summary>
        public int Request(int cue, float priority, double now, double length, bool own)
        {
            LastRefusal = VoiceRefusal.None;
            if (cue < 0 || cue >= _lastPlay.Length) return Refuse(VoiceRefusal.Inaudible);
            double since = now - _lastPlay[cue];
            if (own ? since < OwnRepeatGuard : since < _cooldown[cue]) return Refuse(VoiceRefusal.Cooldown);
            if (!own && _startsThisFrame >= StartsPerFrame) return Refuse(VoiceRefusal.FrameCap);

            int same = 0, oldestSame = -1, free = -1, weakest = -1;
            double oldestStart = double.MaxValue;
            float weakestP = float.MaxValue;
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i].Ends <= now)
                {
                    if (free < 0) free = i;
                    continue;
                }
                if (_slots[i].Cue == cue)
                {
                    same++;
                    if (_slots[i].Started < oldestStart) { oldestStart = _slots[i].Started; oldestSame = i; }
                }
                if (_slots[i].Priority < weakestP) { weakestP = _slots[i].Priority; weakest = i; }
            }

            int slot;
            if (same >= _maxVoices[cue])
            {
                if (!own) return Refuse(VoiceRefusal.CueCap);
                slot = oldestSame; // the player's own sound replaces its oldest copy
                Stolen++;
            }
            else if (free >= 0) slot = free;
            else if (weakest >= 0 && weakestP < priority)
            {
                slot = weakest;
                Stolen++;
            }
            else return Refuse(VoiceRefusal.Outranked);

            _slots[slot].Cue = cue;
            _slots[slot].Priority = priority;
            _slots[slot].Started = now;
            _slots[slot].Ends = now + Math.Max(0.01, length);
            _lastPlay[cue] = now;
            _startsThisFrame++;
            Started++;
            return slot;
        }

        /// <summary>The voice stopped early (pause flush, mute, a loop ended).</summary>
        public void Release(int slot)
        {
            if (slot < 0 || slot >= _slots.Length) return;
            _slots[slot].Ends = double.NegativeInfinity;
            _slots[slot].Cue = -1;
        }

        public void ReleaseAll()
        {
            for (int i = 0; i < _slots.Length; i++) Release(i);
        }

        private int Refuse(VoiceRefusal why)
        {
            LastRefusal = why;
            Refused++;
            return -1;
        }
    }

    /// <summary>
    /// Ducking targets (pure; tests): effects dip under announcer lines and harder under a big-moment takeover; ambience
    /// dips more; the player's own sounds and stingers dip least. Smoothed by the director.
    /// </summary>
    public static class DuckMix
    {
        public const float AnnouncerSfx = 0.72f, AnnouncerAmbience = 0.6f, MomentSfx = 0.5f, MomentAmbience = 0.35f, OwnFloor = 0.82f;
        /// <summary>Gain moves toward its target at this rate (per second, exponential).</summary>
        public const float Speed = 10f;

        public static float Target(SoundBus bus, bool own, bool announcer, bool moment)
        {
            if (bus == SoundBus.Stinger || bus == SoundBus.Ui) return 1f;
            float g = 1f;
            if (bus == SoundBus.Ambience)
            {
                if (announcer) g = Math.Min(g, AnnouncerAmbience);
                if (moment) g = Math.Min(g, MomentAmbience);
                return g;
            }
            if (announcer) g = Math.Min(g, AnnouncerSfx);
            if (moment) g = Math.Min(g, MomentSfx);
            if (own) g = Math.Max(g, OwnFloor);
            return g;
        }

        public static float Step(float current, float target, float dt)
        {
            float k = 1f - (float)Math.Exp(-Speed * Math.Max(0f, dt));
            return current + (target - current) * k;
        }
    }
}
