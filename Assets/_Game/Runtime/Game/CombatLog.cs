using System;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Game
{
    /// <summary>What a CombatEvent reports (view-side translation of the sim's event ring).</summary>
    public enum CombatEventKind
    {
        /// <summary>Actor = victim, Other = killer (an actor id or a KillSource constant), Blow = knock-outs in this blow so far.</summary>
        Knockout,
        /// <summary>Actor = deliverer, Amount = units.</summary>
        DeliverBiomass,
        /// <summary>Actor = deliverer, Amount = units.</summary>
        DeliverCorruption,
        /// <summary>Amount = hatch code (0 neutral, 1 Azure, 2 Crimson, 3 berserk; Kaiju.Hatch.HatchCode).</summary>
        Hatch,
        /// <summary>Other = the actor whose hit knocked the Kaiju out (-1 when none).</summary>
        KaijuKnockout,
        /// <summary>Actor = the actor that came back.</summary>
        Respawn,
        /// <summary>Amount = (int)MatchResult of the winner.</summary>
        MatchEnd,
    }

    /// <summary>Killer ids beyond actor ids. KaijuSlam is inferred view-side: the sim credits a slam with -1.</summary>
    public static class KillSource
    {
        public const int Unknown = -1;
        /// <summary>Same value as Damage.KaijuSource (the Kaiju's melee swing).</summary>
        public const int KaijuMelee = Damage.KaijuSource;
        public const int KaijuSlam = -3;

        public static bool IsKaiju(int killer) => killer == KaijuMelee || killer == KaijuSlam;
    }

    /// <summary>One translated event. A struct passed by value to subscribers (no allocation).</summary>
    public struct CombatEvent
    {
        public CombatEventKind Kind;
        public int Actor;
        public int Other;
        public int Amount;
        public int Blow;
        /// <summary>Teams and classes of Actor and Other at read time (meaningful only when the id is an actor).</summary>
        public TeamId ActorTeam, OtherTeam;
        public ClassId ActorClass, OtherClass;
        /// <summary>Match time (sim seconds) of the frame that read the event.</summary>
        public double Time;

        public bool OtherIsActor => Other >= 0;
    }

    /// <summary>One scoreboard row, read from the sim plus the delivery tally (DG-11 columns).</summary>
    public struct ActorTally
    {
        public int Id;
        public TeamId Team;
        public ClassId Cls;
        public bool IsHuman;
        public bool Alive;
        public int Knockouts;
        public int Deaths;
        public double HillSeconds;
        public int BiomassDelivered;
        public int CorruptionDelivered;
        /// <summary>Knock-outs since this actor was last knocked out.</summary>
        public int Streak;
        public int BestStreak;
        /// <summary>Who knocked this actor out last (actor id or KillSource), KillSource.Unknown before its first death.</summary>
        public int LastKiller;
    }

    /// <summary>
    /// DG-11 per-actor stat tracking, pure C# (no Unity object): reads the World's event ring from a cursor, never writes the
    /// World. Knock-outs, deaths and hill seconds are the sim's own counters (Actor.Kills, Actor.Deaths, Actor.HillSeconds),
    /// so the scoreboard always matches the sim; deliveries, streaks and last killer are tallied from events here.
    /// Resets itself when the World object or its Epoch changes (a new match). Allocation-free after construction.
    /// The game-flow item's MatchStats can adopt this: read Tally(id) or subscribe to Emitted.
    /// </summary>
    public sealed class CombatTally
    {
        /// <summary>Hard cap on tracked actors (the sim's ActorCapacity is 2 x TeamSizeMax = 32).</summary>
        public const int MaxActors = 64;

        /// <summary>Raised for every translated event, in sim order.</summary>
        public event Action<CombatEvent> Emitted;
        /// <summary>Raised when the tally starts over (new World or ResetWorld).</summary>
        public event Action MatchReset;

        public World World { get; private set; }
        public int Epoch { get; private set; } = -1;
        /// <summary>The first knock-out of the match has happened.</summary>
        public bool AnyKnockout { get; private set; }
        public int ActorCount => World == null ? 0 : Math.Min(World.ActorCount, MaxActors);

        readonly int[] _biomass = new int[MaxActors];
        readonly int[] _corruption = new int[MaxActors];
        readonly int[] _streak = new int[MaxActors];
        readonly int[] _best = new int[MaxActors];
        readonly int[] _lastKiller = new int[MaxActors];
        int _cursor;
        int _blowKiller = int.MinValue;
        int _blowCount;

        public CombatTally() => Clear();

        void Clear()
        {
            for (int i = 0; i < MaxActors; i++)
            {
                _biomass[i] = 0;
                _corruption[i] = 0;
                _streak[i] = 0;
                _best[i] = 0;
                _lastKiller[i] = KillSource.Unknown;
            }
            AnyKnockout = false;
            _blowKiller = int.MinValue;
            _blowCount = 0;
        }

        /// <summary>Starts tracking w from its current event head (a new world at tick 0 has head 0, so nothing is missed).</summary>
        public void Bind(World w)
        {
            World = w;
            Epoch = w != null ? w.Epoch : -1;
            _cursor = w != null ? w.EventHead : 0;
            Clear();
            MatchReset?.Invoke();
        }

        /// <summary>Reads every event written since the last call and raises Emitted for each. Call once per frame, after the ticks.</summary>
        public void Pump(World w)
        {
            if (w == null) return;
            if (!ReferenceEquals(w, World) || w.Epoch != Epoch || _cursor > w.EventHead) Bind(w);
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;
            // A tick never splits across frames (the driver runs whole ticks in Update), so a blow ends with the frame.
            _blowKiller = int.MinValue;
            _blowCount = 0;
            double time = w.Match.Time;
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                switch (e.Type)
                {
                    case SimEventType.Hit:
                        // A slam hit (-1) keeps the slam's blow going (its deaths are re-credited to KaijuSlam below).
                        if (e.B != _blowKiller && !(e.B == KillSource.Unknown && _blowKiller == KillSource.KaijuSlam))
                        {
                            _blowKiller = e.B;
                            _blowCount = 0;
                        }
                        break;
                    case SimEventType.Death:
                        OnDeath(w, e, time);
                        break;
                    case SimEventType.DeliverBiomass:
                        EndBlow();
                        if (Valid(e.A)) _biomass[e.A] += (int)Math.Round(e.Value);
                        Emit(w, CombatEventKind.DeliverBiomass, e.A, -1, (int)Math.Round(e.Value), 0, time);
                        break;
                    case SimEventType.DeliverCorruption:
                        EndBlow();
                        if (Valid(e.A)) _corruption[e.A] += (int)Math.Round(e.Value);
                        Emit(w, CombatEventKind.DeliverCorruption, e.A, -1, (int)Math.Round(e.Value), 0, time);
                        break;
                    case SimEventType.Hatch:
                        EndBlow();
                        Emit(w, CombatEventKind.Hatch, -1, -1, (int)Math.Round(e.Value), 0, time);
                        break;
                    case SimEventType.KaijuKO:
                        EndBlow();
                        Emit(w, CombatEventKind.KaijuKnockout, -1, e.B, 0, 0, time);
                        break;
                    case SimEventType.Respawn:
                        EndBlow();
                        Emit(w, CombatEventKind.Respawn, e.A, -1, 0, 0, time);
                        break;
                    case SimEventType.MatchEnd:
                        EndBlow();
                        Emit(w, CombatEventKind.MatchEnd, -1, -1, (int)w.Match.Winner, 0, time);
                        break;
                    default:
                        EndBlow();
                        break;
                }
            }
        }

        void EndBlow()
        {
            _blowKiller = int.MinValue;
            _blowCount = 0;
        }

        void OnDeath(World w, SimEvent e, double time)
        {
            int victim = e.A;
            int killer = e.B;
            if (killer == KillSource.Unknown && SlamFollows(w, _cursor)) killer = KillSource.KaijuSlam;
            if (killer != _blowKiller) { _blowKiller = killer; _blowCount = 0; }
            _blowCount++;
            if (Valid(victim))
            {
                _streak[victim] = 0;
                _lastKiller[victim] = killer;
            }
            if (Valid(killer))
            {
                _streak[killer]++;
                if (_streak[killer] > _best[killer]) _best[killer] = _streak[killer];
            }
            AnyKnockout = true;
            Emit(w, CombatEventKind.Knockout, victim, killer, 0, _blowCount, time);
        }

        /// <summary>
        /// Kaiju.Ai.ResolveSlam damages every actor in the circle with source -1 and then pushes KaijuSlam, with nothing else
        /// in between: a -1 death followed only by hits and deaths up to a KaijuSlam is a slam death.
        /// </summary>
        static bool SlamFollows(World w, int from)
        {
            for (int j = from + 1; j < w.EventHead; j++)
            {
                SimEventType t = w.Events[j % World.EventCapacity].Type;
                if (t == SimEventType.KaijuSlam) return true;
                if (t != SimEventType.Hit && t != SimEventType.Death) return false;
            }
            return false;
        }

        bool Valid(int id) => World != null && id >= 0 && id < ActorCount;

        void Emit(World w, CombatEventKind kind, int actor, int other, int amount, int blow, double time)
        {
            if (Emitted == null) return;
            var ce = new CombatEvent { Kind = kind, Actor = actor, Other = other, Amount = amount, Blow = blow, Time = time };
            if (actor >= 0 && actor < w.ActorCount) { ce.ActorTeam = w.Actors[actor].Team; ce.ActorClass = w.Actors[actor].Cls; }
            if (other >= 0 && other < w.ActorCount) { ce.OtherTeam = w.Actors[other].Team; ce.OtherClass = w.Actors[other].Cls; }
            Emitted(ce);
        }

        /// <summary>The scoreboard row of actor id (default when out of range).</summary>
        public ActorTally Tally(int id)
        {
            var t = new ActorTally { Id = id, LastKiller = KillSource.Unknown };
            if (!Valid(id)) return t;
            Actor a = World.Actors[id];
            t.Team = a.Team;
            t.Cls = a.Cls;
            t.IsHuman = a.IsHuman;
            t.Alive = a.Alive;
            t.Knockouts = a.Kills;
            t.Deaths = a.Deaths;
            t.HillSeconds = a.HillSeconds;
            t.BiomassDelivered = _biomass[id];
            t.CorruptionDelivered = _corruption[id];
            t.Streak = _streak[id];
            t.BestStreak = _best[id];
            t.LastKiller = _lastKiller[id];
            return t;
        }

        public int BiomassDelivered(int id) => Valid(id) ? _biomass[id] : 0;
        public int CorruptionDelivered(int id) => Valid(id) ? _corruption[id] : 0;
        public int LastKiller(int id) => Valid(id) ? _lastKiller[id] : KillSource.Unknown;
    }

    /// <summary>
    /// The live host of the DG-11 / DG-38 layer: one "Match Feed" object per scene holding this component plus KillFeed,
    /// Scoreboard, DeathRecap and AchievementToast. It finds the SimDriverBehaviour itself (looked up each frame while there is none,
    /// so a world created later on Play is picked up; a new World on the same driver resets the tally), pumps the tally in LateUpdate (after the
    /// driver's Update ticks) and exposes the static API below. Read-only on the sim.
    /// Self-installs from [RuntimeInitializeOnLoadMethod] like PauseMenu; the game-flow item can call Attach instead.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    public sealed class CombatLog : MonoBehaviour
    {
        /// <summary>The live log (null before install).</summary>
        public static CombatLog Instance { get; private set; }
        /// <summary>The live tally (null before install). MatchStats / results read Tally(id) from it.</summary>
        public static CombatTally Current => Instance != null ? Instance.Log : null;

        public readonly CombatTally Log = new CombatTally();
        public SimDriverBehaviour Driver;
        /// <summary>The local player's actor id (HudBinder.LocalActor).</summary>
        public int LocalActor = HudBinder.LocalActor;

        HumanInputSource _input;

        /// <summary>True while overlays should stay hidden: no match, the title screen is up, or the game is paused.</summary>
        public bool Suppressed { get; private set; } = true;
        /// <summary>The Scoreboard action is held (Tab / View), from HumanInputState.</summary>
        public bool ScoreboardHeld => _input != null && _input.State.ScoreboardHeld;
        public bool Paused => _input != null && _input.State.Paused;
        /// <summary>The title screen is over the match (the match behind it is attract play: no achievements).</summary>
        public bool TitleUp { get; private set; }

        void OnEnable()
        {
            if (Instance == null) Instance = this;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void LateUpdate()
        {
            if (Instance == null) Instance = this;
            if (Driver == null)
            {
                // Only while there is no match (menus, or the frame a new world appears): one lookup, no allocation.
                Driver = FindAnyObjectByType<SimDriverBehaviour>();
                _input = Driver != null ? Driver.GetComponent<HumanInputSource>() : null;
            }
            World w = Driver != null ? Driver.World : null;
            if (w == null) { Suppressed = true; return; }
            if (_input == null) _input = Driver.GetComponent<HumanInputSource>();
            // TryGetComponent: GetComponent of a missing type allocates a placeholder in the Editor.
            TitleUp = Driver.TryGetComponent(out TitleScreen _);
            Suppressed = Paused || TitleUp;
            Log.Pump(w);
        }

        // ---------------------------------------------------------------- install

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            // Domain reload is off on entering Play Mode: unsubscribe before subscribing (GameBootstrap does the same).
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            Attach(SceneManager.GetActiveScene());
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Attach(scene);

        /// <summary>
        /// Creates the "Match Feed" object in the Boot scene (once) with the log, kill feed, scoreboard, death recap and
        /// achievement toast. Returns the log, or null when the scene is not the Boot scene.
        /// </summary>
        public static CombatLog Attach(Scene scene)
        {
            CombatLog existing = FindAnyObjectByType<CombatLog>();
            if (existing != null) return existing;
            if (scene.path != GameBootstrap.BootScenePath) return null;
            var go = new GameObject("Match Feed");
            if (scene.IsValid() && scene.isLoaded && go.scene != scene) SceneManager.MoveGameObjectToScene(go, scene);
            return Attach(go);
        }

        /// <summary>Adds the whole layer to host (the game-flow item may call this with its own object).</summary>
        public static CombatLog Attach(GameObject host)
        {
            CombatLog log = host.GetComponent<CombatLog>();
            if (log == null) log = host.AddComponent<CombatLog>();
            if (host.GetComponent<KillFeed>() == null) host.AddComponent<KillFeed>();
            if (host.GetComponent<DeathRecap>() == null) host.AddComponent<DeathRecap>();
            if (host.GetComponent<Scoreboard>() == null) host.AddComponent<Scoreboard>();
            if (host.GetComponent<AchievementToast>() == null) host.AddComponent<AchievementToast>();
            return log;
        }
    }
}
