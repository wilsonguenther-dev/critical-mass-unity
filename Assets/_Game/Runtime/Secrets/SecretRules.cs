using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Secrets
{
    /// <summary>
    /// The five easter eggs of DG-26 (words pack eastereggs.json) and whether each is live in this build. An egg whose
    /// mechanic is not on main yet is written but switches itself off until that mechanic appears:
    /// egg-01 (title egg) waits for the title screen to hand over its egg (TitleEgg.Attach, DG-01);
    /// egg-02 (Crown of the Hill) waits for jumping in the sim (JumpGate, DG-02);
    /// egg-05 (credits gag) waits for the credits screen to place it (CreditsGag.Build, DG-29);
    /// egg-03 (hill alone 60 s) and egg-04 (exact allegiance tie) need nothing new and are live.
    /// </summary>
    public static class SecretsBoard
    {
        public const string TitleEgg = "egg-01", Crown = "egg-02", HillAlone = "egg-03", ExactTie = "egg-04", CreditsGag = "egg-05";
        public static readonly string[] All = { TitleEgg, Crown, HillAlone, ExactTie, CreditsGag };

        /// <summary>Raised when a secret fires, with its id.</summary>
        public static event Action<string> Triggered;

        /// <summary>
        /// Raised with an announcer line id when a secret wants a line spoken (egg-03 raises "an-22"); the announcer
        /// (DG-18) voices it. The caption is shown here, under the subtitles option.
        /// </summary>
        public static event Action<string> AnnouncerLine;

        /// <summary>The Crown of the Hill was taken this match (unit rings, DG-03, may add the brass glint to the ring).</summary>
        public static bool CrownWorn;

        internal static bool TitleEggAttached, CreditsGagPlaced;

        public static bool IsActive(string id)
        {
            switch (id)
            {
                case TitleEgg: return TitleEggAttached;
                case Crown: return JumpGate.Available;
                case HillAlone:
                case ExactTie: return true;
                case CreditsGag: return CreditsGagPlaced;
                default: return false;
            }
        }

        internal static void Fire(string id) => Triggered?.Invoke(id);
        internal static void Announce(string lineId) => AnnouncerLine?.Invoke(lineId);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            CrownWorn = false;
            TitleEggAttached = false;
            CreditsGagPlaced = false;
            JumpGate.Override = null;
        }
    }

    /// <summary>
    /// Whether the sim has jumping. DG-02 adds the airborne fields to Actor at its cut marker ("AirLen, AirTick, ..."),
    /// so their presence switches the jump-only secret on with no edit here. Override is for tests.
    /// </summary>
    public static class JumpGate
    {
        public static bool? Override;
        static bool? _detected;

        public static bool Available => Override ?? (_detected ?? (_detected = Detect()).Value);

        static bool Detect()
        {
            Type t = typeof(Actor);
            return t.GetField("AirLen") != null || t.GetField("AirTick") != null || t.GetProperty("Airborne") != null;
        }
    }

    /// <summary>
    /// egg-03: actor 0 alive on the hill with no living ally on it, for HoldSeconds of sim time without a break; fires
    /// once per match. Timing comes from World.Match.Time, so a replay of the same match fires on the same tick.
    /// </summary>
    public sealed class HillAloneTracker
    {
        public const double HoldSeconds = 60;
        public int Actor = 0;
        double _since = -1;
        public bool Fired { get; private set; }
        /// <summary>Sim seconds held alone so far (0 when not alone).</summary>
        public double Held { get; private set; }

        public void Reset()
        {
            _since = -1;
            Held = 0;
            Fired = false;
        }

        /// <summary>Call once per frame (or tick); true on the one call where the secret fires.</summary>
        public bool Step(World w)
        {
            if (Fired || w == null || w.Match == null || w.Map?.Hill == null) return false;
            if (!AloneOnHill(w, Actor))
            {
                _since = -1;
                Held = 0;
                return false;
            }
            double now = w.Match.Time;
            if (_since < 0) _since = now;
            Held = now - _since;
            if (Held + 1e-9 < HoldSeconds) return false;
            Fired = true;
            return true;
        }

        public static bool OnHill(World w, int id)
        {
            double dx = w.X[id] - w.Map.Hill.X, dz = w.Z[id] - w.Map.Hill.Z, r = w.Map.Hill.R;
            return dx * dx + dz * dz <= r * r;
        }

        public static bool AloneOnHill(World w, int id)
        {
            if (id < 0 || id >= w.ActorCount || !w.Actors[id].Alive || !OnHill(w, id)) return false;
            TeamId team = w.Actors[id].Team;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (i == id) continue;
                Actor a = w.Actors[i];
                if (a.Alive && a.Team == team && OnHill(w, i)) return false;
            }
            return true;
        }
    }

    /// <summary>Pure rules shared by the secrets and their tests.</summary>
    public static class SecretRules
    {
        /// <summary>egg-04: the hatch resolved neutral (event value 0, Kaiju.HatchCode) on an exactly tied allegiance, not berserk.</summary>
        public static bool IsExactTie(int hatchCode, MatchState m)
        {
            return hatchCode == 0 && m != null && !m.BerserkLocked && m.Allegiance != null && m.Allegiance.Azure == m.Allegiance.Crimson;
        }
    }

    /// <summary>egg-01's counter: HitsToHatch hits hatch it, once.</summary>
    public sealed class TitleEggCounter
    {
        public const int HitsToHatch = 10;
        public int Hits { get; private set; }
        public bool Hatched { get; private set; }

        /// <summary>One hit; true on the hit that hatches it.</summary>
        public bool Hit()
        {
            if (Hatched) return false;
            Hits++;
            if (Hits < HitsToHatch) return false;
            Hatched = true;
            return true;
        }
    }
}
