using CriticalMass.Runtime.Codex;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Progression;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// b15 item 3 (systems audit W2, defect 15): the first time a profile plays each class, one plain line names that
    /// class's job and the real key for it in the control scheme in use (TrialText tokens, so a rebind or a controller
    /// shows), at match start and again on every respawn, until the player has done the goal once. Done is read from sim
    /// events only (never written); the done flag is a bit per class in the profile (PlayerProfile.ClassGoalsDone).
    /// Every word is in MatchWords.ClassGoals. Pure helpers here; ClassGoalView shows them.
    /// </summary>
    public static class ClassGoals
    {
        /// <summary>The goal line of a class with its key filled in for device.</summary>
        public static string Line(ClassId cls, ControlDevice device)
        {
            int i = (int)cls;
            if (i < 0 || i >= MatchWords.ClassGoals.Length) return string.Empty;
            return TrialText.Fill(MatchWords.ClassGoals[i], device);
        }

        public static bool IsDone(PlayerProfile p, ClassId cls) => p != null && (p.ClassGoalsDone & (1 << (int)cls)) != 0;

        /// <summary>Sets the class's bit; true when it was not set before.</summary>
        public static bool MarkDone(PlayerProfile p, ClassId cls)
        {
            if (p == null || IsDone(p, cls)) return false;
            p.ClassGoalsDone |= 1 << (int)cls;
            return true;
        }

        /// <summary>
        /// True when event e shows actor me doing its class's goal. Warrior: a knock-out inside the hill. Farmer: food into
        /// the egg. Miner: ore banked at the hut. Demolitionist: a blast that hurt an enemy, or an enemy structure it broke.
        /// Builder: a ladder, bridge or wall put down. Healer: a heal on a teammate.
        /// </summary>
        public static bool Done(ClassId cls, SimEvent e, World w, int me)
        {
            if (e == null || w == null || me < 0) return false;
            switch (cls)
            {
                case ClassId.Warrior:
                    if (e.Type != SimEventType.Death || e.B != me) return false;
                    double dx = e.X - w.Map.Hill.X, dz = e.Z - w.Map.Hill.Z, r = w.Config.Hill.Radius;
                    return dx * dx + dz * dz < r * r;
                case ClassId.Farmer:
                    return e.Type == SimEventType.DeliverBiomass && e.A == me && e.Value > 0;
                case ClassId.Miner:
                    return e.Type == SimEventType.BankDeposit && e.A == me && e.Bio == BiomassType.Ore && e.Value > 0;
                case ClassId.Demolitions:
                    return e.Type == SimEventType.Explosion && e.A == me && e.Value >= 1;
                case ClassId.Builder:
                    return (e.Type == SimEventType.PiecePlaced && e.A == me) || (e.Type == SimEventType.WallBuilt && e.A == me && e.B >= 0 && e.Value > 0);
                case ClassId.Healer:
                    return e.Type == SimEventType.HealLanded && e.B == me && e.A != me && e.Value > 0;
                default:
                    return false;
            }
        }
    }

    /// <summary>
    /// b15 item 3: shows ClassGoals on the caption bar (the HUD's one-line slot under the middle, so it adds no new HUD
    /// panel): at the first tick of a match and on each of the player's respawns while the class goal is not done, then
    /// "Goal done" once when it is. Never in a replay, the guided match (it teaches its own steps) or while spectating.
    /// Reads the world's event ring after each tick; writes only the profile. Added by FlowDirector.Install.
    /// </summary>
    public sealed class ClassGoalView : MonoBehaviour
    {
        public const float StartSeconds = 8f, RespawnSeconds = 6f, DoneSeconds = 3f;

        SimDriverBehaviour _driver;
        System.Action<World> _onTick;
        World _world;
        int _head, _lastTick = -1;

        /// <summary>The last line shown (tests).</summary>
        public string LastShown { get; private set; } = string.Empty;

        void Awake()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            _onTick = OnTick;
        }

        void OnEnable() { if (_driver != null) _driver.Ticked += _onTick; }
        void OnDisable() { if (_driver != null) _driver.Ticked -= _onTick; }

        bool Quiet() =>
            (_driver != null && _driver.IsReplay) || SpectatorMode.IsSpectating || GetComponent<TrialGuide>() != null ||
            (GameFlow.State != FlowState.Match && GameFlow.State != FlowState.Off);

        void OnTick(World w)
        {
            if (w == null) return;
            bool fresh = w != _world || w.Tick < _lastTick;
            if (fresh)
            {
                _world = w;
                _head = w.EventHead;
            }
            _lastTick = w.Tick;
            int me = LocalSeat.Actor;
            if (me < 0 || me >= w.ActorCount || Quiet()) { _head = w.EventHead; return; }
            PlayerProfile p = Profile.Current;
            ClassId cls = w.Actors[me].Cls;
            if (p == null || ClassGoals.IsDone(p, cls)) { _head = w.EventHead; return; }
            if (fresh) Show(ClassGoals.Line(cls, ControlBindings.ActiveDevice), StartSeconds);
            int head = w.EventHead;
            if (head - _head > World.EventCapacity) _head = head - World.EventCapacity;
            for (; _head < head; _head++)
            {
                SimEvent e = w.Events[_head % World.EventCapacity];
                if (ClassGoals.Done(cls, e, w, me))
                {
                    if (ClassGoals.MarkDone(p, cls)) Profile.Commit();
                    Show(MatchWords.GoalDone, DoneSeconds);
                    _head = head;
                    return;
                }
                if (e.Type == SimEventType.Respawn && e.A == me) Show(ClassGoals.Line(cls, ControlBindings.ActiveDevice), RespawnSeconds);
            }
        }

        void Show(string line, float seconds)
        {
            if (string.IsNullOrEmpty(line)) return;
            LastShown = line;
            CaptionBar.Show(line, seconds);
        }
    }
}
