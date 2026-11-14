using System;
using System.Collections.Generic;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Moments
{
    /// <summary>Every big moment the director knows (order = recipe table index; append only).</summary>
    public enum MomentId
    {
        None, OpeningStrike, TwoDown, ThreeDown, Wildfire, HillCleared, BatteringRam, Overcharged, Hatch, AllegianceTurns,
        Berserk, Quake, KaijuDown, Turnaround, AtTheWire, MatchPoint,
        /// <summary>Batch 3: a TNT or mine blast that hurts three or more (DG-22).</summary>
        BlastRadius,
        /// <summary>Batch 3: the local Healer's Healing Wings landing on three or more allies at once (DG-40).</summary>
        Lifeline,
        Finisher,
    }

    /// <summary>A full-screen takeover, or a small corner stinger that never covers the play space.</summary>
    public enum MomentScale { Takeover, Stinger }

    /// <summary>The layers a recipe is built from (each tones down under the reduced-motion and flash settings).</summary>
    [Flags]
    public enum MomentLayers
    {
        None = 0,
        Band = 1 << 0,
        Portrait = 1 << 1,
        Title = 1 << 2,
        SpeedLines = 1 << 3,
        Burst = 1 << 4,
        Flash = 1 << 5,
        Invert = 1 << 6,
        Freeze = 1 << 7,
        Aura = 1 << 8,
        EdgeGlow = 1 << 9,
        Cracks = 1 << 10,
        Grade = 1 << 11,
        EndFrame = 1 << 12,
    }

    /// <summary>Which cut-in art the band carries.</summary>
    public enum MomentArtKind { Actor, Kaiju, Team }

    public enum BandStyle { Diagonal, Horizontal }

    /// <summary>The text-free title plate under the title (Resources/UI/BigMoments/bm-plate-*.png).</summary>
    public enum PlateKind { Takeover, Kaiju, Finisher, Stinger }

    /// <summary>
    /// One big moment as data (a plain table, no asset needed; the integrator can retune numbers here). Seconds is the
    /// takeover length before the 1.0..2.2 s clamp; the stinger length is fixed (MomentQueue.StingerSeconds).
    /// </summary>
    public sealed class MomentRecipe
    {
        public MomentId Id;
        /// <summary>Built-in English title (the words pack id is "bm-" + the id in kebab case: MomentWords).</summary>
        public string Title;
        public MomentScale Scale;
        /// <summary>Higher wins the queue.</summary>
        public int Priority;
        public float Seconds;
        /// <summary>Wall seconds before the same moment may fire again.</summary>
        public float Cooldown;
        public MomentLayers Layers;
        public BandStyle Band;
        public PlateKind Plate;
        public MomentArtKind Art;
        /// <summary>Picture hold at the start (Freeze layer), wall seconds.</summary>
        public float FreezeSeconds;
        /// <summary>Once-a-match beats (hatch, end frame) skip the 20 s takeover gap; they still never overlap.</summary>
        public bool IgnoresGap;
        /// <summary>Plays over an open banner (the banner is held and enters fresh afterwards).</summary>
        public bool PreemptsBanner;
        /// <summary>Never dropped for waiting too long (the end frame and the hatch).</summary>
        public bool NeverStale;
    }

    /// <summary>The recipe table.</summary>
    public static class MomentRecipes
    {
        const MomentLayers Hero = MomentLayers.Band | MomentLayers.Portrait | MomentLayers.Title | MomentLayers.SpeedLines |
                                  MomentLayers.Burst | MomentLayers.Flash;
        const MomentLayers Impact = Hero | MomentLayers.Freeze | MomentLayers.Invert | MomentLayers.Grade;
        const MomentLayers KaijuL = MomentLayers.Band | MomentLayers.Portrait | MomentLayers.Title | MomentLayers.SpeedLines |
                                    MomentLayers.Flash | MomentLayers.Cracks | MomentLayers.EdgeGlow;
        const MomentLayers Corner = MomentLayers.Title;

        public static readonly MomentRecipe[] All = Build();

        public static MomentRecipe Get(MomentId id)
        {
            int i = (int)id;
            return i > 0 && i < All.Length ? All[i] : null;
        }

        public static int Count => All.Length;

        static MomentRecipe[] Build()
        {
            var a = new MomentRecipe[(int)MomentId.Finisher + 1];
            void Add(MomentId id, string title, MomentScale scale, int prio, float secs, float cd, MomentLayers layers,
                BandStyle band, PlateKind plate, MomentArtKind art, float freeze = 0f, bool gap = false, bool preempt = false,
                bool never = false)
            {
                a[(int)id] = new MomentRecipe
                {
                    Id = id, Title = title, Scale = scale, Priority = prio, Seconds = secs, Cooldown = cd, Layers = layers, Band = band,
                    Plate = plate, Art = art, FreezeSeconds = freeze, IgnoresGap = gap, PreemptsBanner = preempt, NeverStale = never,
                };
            }
            Add(MomentId.OpeningStrike, "OPENING STRIKE", MomentScale.Takeover, 40, 1.6f, 9999f, Impact, BandStyle.Diagonal, PlateKind.Takeover, MomentArtKind.Actor, 0.1f);
            Add(MomentId.TwoDown, "TWO DOWN", MomentScale.Stinger, 20, 1.6f, 6f, Corner, BandStyle.Horizontal, PlateKind.Stinger, MomentArtKind.Actor);
            Add(MomentId.ThreeDown, "THREE DOWN", MomentScale.Stinger, 22, 1.6f, 6f, Corner, BandStyle.Horizontal, PlateKind.Stinger, MomentArtKind.Actor);
            Add(MomentId.Wildfire, "WILDFIRE", MomentScale.Stinger, 24, 1.6f, 20f, Corner, BandStyle.Horizontal, PlateKind.Stinger, MomentArtKind.Actor);
            Add(MomentId.HillCleared, "HILL CLEARED", MomentScale.Takeover, 55, 1.8f, 45f, Impact, BandStyle.Diagonal, PlateKind.Takeover, MomentArtKind.Actor, 0.1f);
            Add(MomentId.BatteringRam, "BATTERING RAM", MomentScale.Takeover, 45, 1.6f, 30f, Impact, BandStyle.Diagonal, PlateKind.Takeover, MomentArtKind.Actor, 0.12f);
            Add(MomentId.Overcharged, "OVERCHARGED", MomentScale.Takeover, 35, 1.6f, 40f,
                MomentLayers.Band | MomentLayers.Portrait | MomentLayers.Title | MomentLayers.Aura | MomentLayers.EdgeGlow | MomentLayers.Burst,
                BandStyle.Horizontal, PlateKind.Takeover, MomentArtKind.Actor);
            Add(MomentId.Hatch, "IT HATCHES", MomentScale.Takeover, 90, 2.2f, 9999f, KaijuL | MomentLayers.Burst, BandStyle.Horizontal, PlateKind.Kaiju, MomentArtKind.Kaiju, 0f, true, true, true);
            Add(MomentId.AllegianceTurns, "ALLEGIANCE TURNS", MomentScale.Takeover, 75, 2.0f, 25f, KaijuL, BandStyle.Diagonal, PlateKind.Kaiju, MomentArtKind.Kaiju, 0f, false, true);
            Add(MomentId.Berserk, "BERSERK", MomentScale.Takeover, 85, 2.2f, 60f, KaijuL | MomentLayers.Invert, BandStyle.Diagonal, PlateKind.Kaiju, MomentArtKind.Kaiju, 0f, true, true);
            Add(MomentId.Quake, "QUAKE", MomentScale.Takeover, 65, 1.6f, 30f, KaijuL | MomentLayers.Freeze | MomentLayers.Grade, BandStyle.Diagonal, PlateKind.Kaiju, MomentArtKind.Kaiju, 0.12f);
            Add(MomentId.KaijuDown, "KAIJU DOWN", MomentScale.Takeover, 60, 1.8f, 30f, Impact, BandStyle.Diagonal, PlateKind.Kaiju, MomentArtKind.Actor, 0.1f, false, true);
            Add(MomentId.Turnaround, "TURNAROUND", MomentScale.Takeover, 98, 2.2f, 9999f, Hero | MomentLayers.EndFrame | MomentLayers.EdgeGlow, BandStyle.Horizontal, PlateKind.Finisher, MomentArtKind.Team, 0f, true, true, true);
            Add(MomentId.AtTheWire, "AT THE WIRE", MomentScale.Takeover, 99, 2.2f, 9999f, Hero | MomentLayers.EndFrame | MomentLayers.EdgeGlow | MomentLayers.Invert, BandStyle.Horizontal, PlateKind.Finisher, MomentArtKind.Team, 0f, true, true, true);
            Add(MomentId.MatchPoint, "MATCH POINT", MomentScale.Takeover, 70, 1.8f, 9999f, MomentLayers.Band | MomentLayers.Title | MomentLayers.SpeedLines | MomentLayers.EdgeGlow, BandStyle.Horizontal, PlateKind.Finisher, MomentArtKind.Team, 0f, true, false);
            Add(MomentId.BlastRadius, "BLAST RADIUS", MomentScale.Takeover, 50, 1.6f, 30f, Impact, BandStyle.Diagonal, PlateKind.Takeover, MomentArtKind.Actor, 0.12f);
            Add(MomentId.Lifeline, "LIFELINE", MomentScale.Stinger, 21, 1.6f, 15f, Corner, BandStyle.Horizontal, PlateKind.Stinger, MomentArtKind.Actor);
            Add(MomentId.Finisher, "HILL TAKEN", MomentScale.Takeover, 97, 2.2f, 9999f, Hero | MomentLayers.EndFrame | MomentLayers.EdgeGlow, BandStyle.Horizontal, PlateKind.Finisher, MomentArtKind.Team, 0f, true, true, true);
            return a;
        }

        /// <summary>Every id that has a recipe (the debug gallery plays them in this order).</summary>
        public static MomentId[] Gallery()
        {
            var list = new List<MomentId>();
            for (int i = 1; i < All.Length; i++) if (All[i] != null) list.Add((MomentId)i);
            return list.ToArray();
        }
    }

    /// <summary>Words for titles: the words pack (Lore/*.json key "bm-<kebab id>") when present, else the built-in title.</summary>
    public static class MomentWords
    {
        static readonly Dictionary<MomentId, string> Override = new Dictionary<MomentId, string>();

        /// <summary>The on-screen title (no allocation: cached strings).</summary>
        public static string Title(MomentId id)
        {
            if (Override.TryGetValue(id, out string s) && !string.IsNullOrEmpty(s)) return s;
            MomentRecipe r = MomentRecipes.Get(id);
            return r != null ? r.Title : string.Empty;
        }

        /// <summary>Renames a title (words pack, localisation); null or empty restores the built-in one.</summary>
        public static void Set(MomentId id, string title)
        {
            if (string.IsNullOrEmpty(title)) Override.Remove(id);
            else Override[id] = title;
        }

        public static string TeamWord(TeamId t) => t == TeamId.Azure ? "AZURE" : "CRIMSON";
    }

    public enum BigMomentsMode { Full = 0, Reduced = 1, Off = 2 }

    /// <summary>The look a takeover plays with, resolved once at its start from the settings.</summary>
    public struct MomentLook
    {
        /// <summary>Slides, spins, scrolling lines and rising particles (false: fades only, everything still).</summary>
        public bool Motion;
        /// <summary>The colour-invert frame (never with reduce flashing).</summary>
        public bool Invert;
        public float FlashAlpha;
        public float FlashSeconds;
        public float SpeedLineAlpha;
        /// <summary>Camera shake: always off for big moments (kept as a field so the rule is testable).</summary>
        public bool Shake;
        public bool SlowMotion;

        public static MomentLook Resolve(bool reducedMotion, bool reduceFlashing, bool slowMotion) => new MomentLook
        {
            Motion = !reducedMotion,
            Invert = !reduceFlashing && !reducedMotion,
            FlashAlpha = reduceFlashing ? 0.22f : reducedMotion ? 0.45f : 0.85f,
            FlashSeconds = reduceFlashing ? 0.3f : 0.14f,
            SpeedLineAlpha = reducedMotion ? 0.25f : 0.7f,
            Shake = false,
            SlowMotion = slowMotion && !reducedMotion,
        };
    }

    /// <summary>
    /// Player settings for big moments (PlayerPrefs). Mode: "cm.bigMoments" 0 full, 1 reduced (corner stingers only), 2 off.
    /// Reduced motion and reduce flashing are the game-wide keys other items already use ("cm.reducedMotion" from the story
    /// intro, "cm.access.reduceFlashing" from the options item); slow motion "cm.bigMoments.slowMotion" defaults off.
    /// Overrides are for tests.
    /// </summary>
    public static class BigMomentsSettings
    {
        public const string ModeKey = "cm.bigMoments";
        public const string SlowMotionKey = "cm.bigMoments.slowMotion";
        public const string ReducedMotionKey = "cm.reducedMotion";
        public const string ReduceFlashingKey = "cm.access.reduceFlashing";

        public static BigMomentsMode? ModeOverride;
        public static bool? ReducedMotionOverride, ReduceFlashingOverride, SlowMotionOverride;

        public static event Action Changed;

        public static BigMomentsMode Mode
        {
            get
            {
                if (ModeOverride.HasValue) return ModeOverride.Value;
                int v = PlayerPrefs.GetInt(ModeKey, 0);
                return v == 1 ? BigMomentsMode.Reduced : v == 2 ? BigMomentsMode.Off : BigMomentsMode.Full;
            }
            set
            {
                PlayerPrefs.SetInt(ModeKey, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static bool ReducedMotion => ReducedMotionOverride ?? PlayerPrefs.GetInt(ReducedMotionKey, 0) != 0;
        public static bool ReduceFlashing => ReduceFlashingOverride ?? PlayerPrefs.GetInt(ReduceFlashingKey, 0) != 0;

        public static bool SlowMotion
        {
            get => SlowMotionOverride ?? PlayerPrefs.GetInt(SlowMotionKey, 0) != 0;
            set
            {
                PlayerPrefs.SetInt(SlowMotionKey, value ? 1 : 0);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static string Label(BigMomentsMode m) => m == BigMomentsMode.Full ? "FULL" : m == BigMomentsMode.Reduced ? "REDUCED" : "OFF";

        /// <summary>The next mode for a settings stepper (dir +1 / -1, wraps).</summary>
        public static BigMomentsMode Step(BigMomentsMode m, int dir)
        {
            int v = ((int)m + (dir < 0 ? 2 : 1)) % 3;
            return (BigMomentsMode)v;
        }

        public static MomentLook Look() => MomentLook.Resolve(ReducedMotion, ReduceFlashing, SlowMotion);

        public static void ClearOverrides()
        {
            ModeOverride = null;
            ReducedMotionOverride = ReduceFlashingOverride = SlowMotionOverride = null;
        }
    }

    /// <summary>What happened, who did it, to whom (from the sim's events; view only).</summary>
    public struct MomentRequest
    {
        public MomentId Id;
        /// <summary>The actor the cut-in shows (attacker, caster), or -1.</summary>
        public int Actor;
        /// <summary>The other party (victim), or -1.</summary>
        public int Victim;
        public TeamId Team;
        public ClassId Cls;
        public KaijuState Kaiju;
        /// <summary>The local player did it (or it happened to the local player's team at the end).</summary>
        public bool PlayerOwned;
        /// <summary>Kills in the streak, hits in the swing, deaths in the slam.</summary>
        public int Count;
        public double X, Z;
        /// <summary>Sim tick it was raised on.</summary>
        public int Tick;

        public static MomentRequest Of(MomentId id, TeamId team) => new MomentRequest { Id = id, Actor = -1, Victim = -1, Team = team };
    }

    /// <summary>A moment on screen.</summary>
    public struct ActiveMoment
    {
        public MomentRequest Request;
        public MomentScale Scale;
        public float Seconds;
        public double StartedAt;
        /// <summary>Shortened because it was the player's own moment in a tight fight.</summary>
        public bool Shortened;
    }

    public enum OfferResult { Off, Cooldown, Queued, QueuedAsStinger, Full, Unknown }

    /// <summary>
    /// The one priority queue for every full-screen moment, plus the corner stinger slot (pure, testable, no allocation).
    /// Rules: per-type cooldowns; at most one full takeover per TakeoverGapSeconds (else it plays as a corner stinger, once-a-
    /// match beats excepted); a moment that is not the player's own while the player is in a tight fight plays as a stinger;
    /// the player's own moment in a tight fight is shortened; takeovers last 1.0..2.2 s; stingers wait while a takeover
    /// plays; waiting moments go stale after WaitTimeout. Reduced mode turns every takeover into a stinger; off drops all.
    /// The clock is the director's visual clock (wall seconds, paused with the game); the sim never sees any of it.
    /// </summary>
    public sealed class MomentQueue
    {
        public const int Capacity = 6, StingerCapacity = 3;
        public const double TakeoverGapSeconds = 20, WaitTimeout = 3.0, StingerWaitTimeout = 4.0;
        // hud-art-1 (owner, 2026-11-13: a cut-in vanished before he could read or screenshot it): takeovers 1.0..2.2 s
        // (major moments 2.0..2.2, class cards 1.6..1.8 unshortened), stingers 1.6 s.
        public const float MinSeconds = 1.0f, MaxSeconds = 2.2f, StingerSeconds = 1.6f, PlayerShorten = 0.65f;
        /// <summary>The unshortened length bands (tests hold every recipe to them).</summary>
        public const float MajorMin = 2.0f, MajorMax = 2.2f, CardMin = 1.6f, CardMax = 1.8f;

        private readonly double[] _lastAt;
        private double _lastTakeoverAt = double.NegativeInfinity;
        private readonly MomentRequest[] _wait = new MomentRequest[Capacity];
        private readonly ActiveMoment[] _waitMoment = new ActiveMoment[Capacity];
        private readonly double[] _waitSince = new double[Capacity];
        private int _count;
        private readonly ActiveMoment[] _sting = new ActiveMoment[StingerCapacity];
        private readonly double[] _stingSince = new double[StingerCapacity];
        private int _stingCount;

        public bool Playing { get; private set; }
        public ActiveMoment Current { get; private set; }
        public int Version { get; private set; }
        public bool StingerPlaying { get; private set; }
        public ActiveMoment CurrentStinger { get; private set; }
        public int StingerVersion { get; private set; }
        public int WaitingCount => _count;
        public int StingersWaiting => _stingCount;
        public double LastTakeoverAt => _lastTakeoverAt;

        public MomentQueue()
        {
            _lastAt = new double[MomentRecipes.Count];
            Clear();
        }

        public void Clear()
        {
            for (int i = 0; i < _lastAt.Length; i++) _lastAt[i] = double.NegativeInfinity;
            _lastTakeoverAt = double.NegativeInfinity;
            _count = 0;
            _stingCount = 0;
            if (Playing) Version++;
            if (StingerPlaying) StingerVersion++;
            Playing = false;
            StingerPlaying = false;
        }

        /// <summary>The takeover length for a recipe: clamped 1.0..2.2 s, shortened for the player's own tight-fight moment.</summary>
        public static float TakeoverSeconds(MomentRecipe r, bool shorten)
        {
            float s = r.Seconds * (shorten ? PlayerShorten : 1f);
            return Mathf.Clamp(s, MinSeconds, MaxSeconds);
        }

        /// <summary>Offers a moment at now (visual clock). tightFight: the local player is mid-fight right now.</summary>
        public OfferResult Offer(in MomentRequest r, double now, BigMomentsMode mode, bool tightFight)
        {
            MomentRecipe recipe = MomentRecipes.Get(r.Id);
            if (recipe == null) return OfferResult.Unknown;
            if (mode == BigMomentsMode.Off) return OfferResult.Off;
            int id = (int)r.Id;
            if (now - _lastAt[id] < recipe.Cooldown) return OfferResult.Cooldown;

            MomentScale scale = recipe.Scale;
            if (scale == MomentScale.Takeover)
            {
                if (mode == BigMomentsMode.Reduced) scale = MomentScale.Stinger;
                else if (tightFight && !r.PlayerOwned && !recipe.NeverStale) scale = MomentScale.Stinger;
                else if (!recipe.IgnoresGap && now - _lastTakeoverAt < TakeoverGapSeconds) scale = MomentScale.Stinger;
            }

            var m = new ActiveMoment { Request = r, Scale = scale };
            if (scale == MomentScale.Takeover)
            {
                bool shorten = r.PlayerOwned && tightFight;
                m.Seconds = TakeoverSeconds(recipe, shorten);
                m.Shortened = shorten;
                if (!Push(m, now)) return OfferResult.Full;
                _lastTakeoverAt = now;
            }
            else
            {
                m.Seconds = StingerSeconds;
                if (!PushStinger(m, now)) return OfferResult.Full;
            }
            _lastAt[id] = now;
            return scale == MomentScale.Takeover ? OfferResult.Queued : OfferResult.QueuedAsStinger;
        }

        private bool Push(in ActiveMoment m, double now)
        {
            int prio = MomentRecipes.Get(m.Request.Id).Priority;
            if (_count == Capacity)
            {
                int low = 0;
                for (int i = 1; i < _count; i++) if (Prio(_waitMoment[i]) < Prio(_waitMoment[low])) low = i;
                if (Prio(_waitMoment[low]) >= prio) return false;
                RemoveAt(low);
            }
            _waitMoment[_count] = m;
            _wait[_count] = m.Request;
            _waitSince[_count] = now;
            _count++;
            return true;
        }

        private bool PushStinger(in ActiveMoment m, double now)
        {
            if (_stingCount == StingerCapacity)
            {
                // the oldest stinger makes room: callouts are about now
                for (int i = 0; i < _stingCount - 1; i++)
                {
                    _sting[i] = _sting[i + 1];
                    _stingSince[i] = _stingSince[i + 1];
                }
                _stingCount--;
            }
            _sting[_stingCount] = m;
            _stingSince[_stingCount] = now;
            _stingCount++;
            return true;
        }

        private static int Prio(in ActiveMoment m) => MomentRecipes.Get(m.Request.Id).Priority;

        private void RemoveAt(int i)
        {
            for (int k = i; k < _count - 1; k++)
            {
                _waitMoment[k] = _waitMoment[k + 1];
                _wait[k] = _wait[k + 1];
                _waitSince[k] = _waitSince[k + 1];
            }
            _count--;
        }

        /// <summary>
        /// Advance at now. bannerOpen: a centre banner is on screen (only recipes that preempt banners may start over it;
        /// the director then holds the banner).
        /// </summary>
        public void Update(double now, bool bannerOpen)
        {
            if (Playing && now >= Current.StartedAt + Current.Seconds)
            {
                Playing = false;
                Version++;
            }
            if (StingerPlaying && now >= CurrentStinger.StartedAt + CurrentStinger.Seconds)
            {
                StingerPlaying = false;
                StingerVersion++;
            }

            for (int i = _count - 1; i >= 0; i--)
            {
                if (!MomentRecipes.Get(_waitMoment[i].Request.Id).NeverStale && now - _waitSince[i] > WaitTimeout) RemoveAt(i);
            }
            for (int i = _stingCount - 1; i >= 0; i--)
            {
                if (now - _stingSince[i] <= StingerWaitTimeout) continue;
                for (int k = i; k < _stingCount - 1; k++)
                {
                    _sting[k] = _sting[k + 1];
                    _stingSince[k] = _stingSince[k + 1];
                }
                _stingCount--;
            }

            if (!Playing && _count > 0)
            {
                int best = -1;
                for (int i = 0; i < _count; i++)
                {
                    MomentRecipe r = MomentRecipes.Get(_waitMoment[i].Request.Id);
                    if (bannerOpen && !r.PreemptsBanner) continue;
                    if (best < 0 || r.Priority > Prio(_waitMoment[best])) best = i;
                }
                if (best >= 0)
                {
                    ActiveMoment m = _waitMoment[best];
                    m.StartedAt = now;
                    RemoveAt(best);
                    Current = m;
                    Playing = true;
                    Version++;
                    if (StingerPlaying)
                    {
                        StingerPlaying = false;   // a takeover covers the corner: the stinger ends at once
                        StingerVersion++;
                    }
                }
            }

            if (!Playing && !StingerPlaying && _stingCount > 0)
            {
                ActiveMoment s = _sting[0];
                for (int k = 0; k < _stingCount - 1; k++)
                {
                    _sting[k] = _sting[k + 1];
                    _stingSince[k] = _stingSince[k + 1];
                }
                _stingCount--;
                s.StartedAt = now;
                CurrentStinger = s;
                StingerPlaying = true;
                StingerVersion++;
            }
        }

        /// <summary>0..1 through the takeover on screen (0 when none).</summary>
        public float Progress(double now) => Playing ? Mathf.Clamp01((float)((now - Current.StartedAt) / Current.Seconds)) : 0f;

        public float StingerProgress(double now) =>
            StingerPlaying ? Mathf.Clamp01((float)((now - CurrentStinger.StartedAt) / CurrentStinger.Seconds)) : 0f;

        /// <summary>A waiting takeover may play over a banner (the director holds the banner for it).</summary>
        public bool HasWaitingPreempt
        {
            get
            {
                for (int i = 0; i < _count; i++) if (MomentRecipes.Get(_waitMoment[i].Request.Id).PreemptsBanner) return true;
                return false;
            }
        }

        /// <summary>Starts a moment now, skipping every rule (debug gallery, captures, tests). A takeover replaces the one on screen.</summary>
        public void Force(in MomentRequest r, MomentScale scale, double now)
        {
            MomentRecipe recipe = MomentRecipes.Get(r.Id);
            if (recipe == null) return;
            var m = new ActiveMoment { Request = r, Scale = scale, StartedAt = now };
            if (scale == MomentScale.Takeover)
            {
                m.Seconds = TakeoverSeconds(recipe, false);
                Current = m;
                Playing = true;
                Version++;
                if (StingerPlaying)
                {
                    StingerPlaying = false;
                    StingerVersion++;
                }
            }
            else
            {
                m.Seconds = StingerSeconds;
                CurrentStinger = m;
                StingerPlaying = true;
                StingerVersion++;
            }
        }

        /// <summary>Ends the takeover on screen now (the gallery's skip).</summary>
        public void EndCurrent()
        {
            if (!Playing) return;
            Playing = false;
            Version++;
        }
    }

    /// <summary>At most MaxPerSecond flashes (white flash or invert frame) in any one second (WCAG 2.3.1).</summary>
    public sealed class FlashLimiter
    {
        public const int MaxPerSecond = 3;
        private readonly double[] _at = new double[MaxPerSecond];
        private int _next;

        public FlashLimiter() => Reset();

        public void Reset()
        {
            for (int i = 0; i < _at.Length; i++) _at[i] = double.NegativeInfinity;
            _next = 0;
        }

        /// <summary>True (and counted) when a flash may start at now.</summary>
        public bool TryFlash(double now)
        {
            // the oldest of the last three must be at least a second old
            if (now - _at[_next] < 1.0) return false;
            _at[_next] = now;
            _next = (_next + 1) % _at.Length;
            return true;
        }
    }

    /// <summary>Where a cut-in's character art came from (best first).</summary>
    public enum PortraitSource { TeamImage, TintedImage, LiveModel, HudIcon, Shape, Painted }

    /// <summary>
    /// Art slots: every image a recipe can use, loaded by name from Resources/UI/BigMoments (brief:
    /// unity-port/briefs/big-moments-art.md). A missing file is never an error: portraits fall back to the live 3D model,
    /// then the HUD portrait icon, then a shape; sheets fall back to procedural textures; plates to vector plates.
    /// </summary>
    public static class MomentArt
    {
        public const string Folder = "UI/BigMoments/";
        public const string SpeedRadial = "bm-speedlines-radial", SpeedBand = "bm-speedlines-band", Burst = "bm-burst",
            Band = "bm-band", CracksCorner = "bm-cracks-corner", AuraRing = "bm-aura-ring", Spark = "bm-spark",
            EdgeGlow = "bm-edge-glow";
        public static readonly string[] Sheets = { SpeedRadial, SpeedBand, Burst, Band, CracksCorner, AuraRing, Spark, EdgeGlow };
        public static readonly string[] Plates = { "bm-title-plate", "bm-title-plate-impact", "bm-title-plate-ribbon", "bm-stinger-plate" };
        /// <summary>hud-art-1: the accent bar and underline drawn over a takeover plate, tinted by the moment's accent.</summary>
        public const string PlateAccent = "bm-title-plate-accent";

        /// <summary>Loads a texture by file name (no folder, no extension); null when missing. Tests replace it.</summary>
        public static Func<string, Texture2D> Loader = DefaultLoad;

        private static readonly Dictionary<string, Texture2D> Cache = new Dictionary<string, Texture2D>();

        private static Texture2D DefaultLoad(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out Texture2D t)) return t;
            t = name.StartsWith(StoryPrefix, StringComparison.Ordinal)
                ? Resources.Load<Texture2D>(StoryFolder + name.Substring(StoryPrefix.Length))
                : Resources.Load<Texture2D>(Folder + name);
            if (t != null) t.wrapMode = name == SpeedBand || name == Band ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            Cache[name] = t;
            return t;
        }

        public static Texture2D Load(string name) => Loader != null ? Loader(name) : null;

        public static void ResetLoader()
        {
            Loader = DefaultLoad;
        }

        /// <summary>The sim class name in lower case ("warrior", ..., "healer" once the sim has it).</summary>
        public static string ClassName(ClassId c) => c.ToString().ToLowerInvariant();

        public static string TeamName(TeamId t) => t == TeamId.Azure ? "azure" : "crimson";

        /// <summary>
        /// Integration batch 4: the story's painted cut-ins (PR #62, Resources/Story/cutin_*.jpg, 16:9) fill the portrait slot
        /// when no bm-portrait image exists, ahead of the live model. Loaded through Loader with this prefix (tests stub it).
        /// </summary>
        public const string StoryPrefix = "story:", StoryFolder = "Story/";
        public static string PaintedName(ClassId c, TeamId t) => StoryPrefix + "cutin_" + ClassName(c) + "_" + TeamName(t);
        public const string PaintedKaiju = StoryPrefix + "cutin_kaiju";
        /// <summary>The square of a 16:9 painting the 660 px portrait shows: the subject stands right of centre.</summary>
        public static Rect PaintedCrop(Texture tex)
        {
            if (tex == null || tex.width <= tex.height) return new Rect(0f, 0f, 1f, 1f);
            float w = (float)tex.height / tex.width;
            float x = Mathf.Clamp(0.62f - w * 0.5f, 0f, 1f - w);
            return new Rect(x, 0f, w, 1f);
        }

        public static string PortraitName(ClassId c, TeamId t) => "bm-portrait-" + ClassName(c) + "-" + TeamName(t);
        public static string NeutralPortraitName(ClassId c) => "bm-portrait-" + ClassName(c);
        public static string TeamMaskName(ClassId c) => "bm-portrait-" + ClassName(c) + "-teammask";
        public static string PlateName(PlateKind p) => Plates[(int)p];

        public static string KaijuName(KaijuState s)
        {
            switch (s)
            {
                case KaijuState.Azure: return "bm-kaiju-azure";
                case KaijuState.Crimson: return "bm-kaiju-crimson";
                case KaijuState.Berserk: return "bm-kaiju-berserk";
                default: return "bm-kaiju-neutral";
            }
        }

        /// <summary>
        /// Resolves a class portrait: the team image, else the neutral image (+ optional team mask to tint), else the live
        /// model when one can be rendered, else the HUD portrait icon when it exists, else a shape.
        /// </summary>
        public static PortraitSource ResolvePortrait(ClassId c, TeamId t, bool liveModel, bool hudIcon, out Texture2D image, out Texture2D mask)
        {
            mask = null;
            image = Load(PortraitName(c, t));
            if (image != null) return PortraitSource.TeamImage;
            image = Load(NeutralPortraitName(c));
            if (image != null)
            {
                mask = Load(TeamMaskName(c));
                return PortraitSource.TintedImage;
            }
            image = Load(PaintedName(c, t));
            if (image != null) return PortraitSource.Painted;
            if (liveModel) return PortraitSource.LiveModel;
            return hudIcon ? PortraitSource.HudIcon : PortraitSource.Shape;
        }

        /// <summary>Resolves the Kaiju cut-in: the state image, else the live model, else the HUD icon, else a shape.</summary>
        public static PortraitSource ResolveKaiju(KaijuState s, bool liveModel, bool hudIcon, out Texture2D image)
        {
            image = Load(KaijuName(s));
            if (image != null) return PortraitSource.TeamImage;
            image = Load(PaintedKaiju);
            if (image != null) return PortraitSource.Painted;
            if (liveModel) return PortraitSource.LiveModel;
            return hudIcon ? PortraitSource.HudIcon : PortraitSource.Shape;
        }
    }
}
