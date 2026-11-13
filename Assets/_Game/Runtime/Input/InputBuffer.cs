using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CriticalMass.Runtime.Input
{
    /// <summary>
    /// DG-14 input buffer for one button (attack or class ability), counted in sim steps, never in wall time. A press is
    /// delivered on the step after it as before (so the sim sees exactly what it saw without the buffer); if the sim
    /// would refuse it on that step (cooldown still running, getting up after a respawn), the press is also kept for up
    /// to MaxEarlyTicks more steps and delivered once, on the first step the sim would accept it. Nothing else changes:
    /// a press that never meets a ready step inside the window is dropped, exactly as the sim dropped it before.
    /// Pure C#, allocation-free; HumanInputState owns one per buffered button and calls Step once per Fill.
    /// </summary>
    public sealed class InputBuffer
    {
        /// <summary>The design window: a press up to this early still counts (gap list DG-14, 0.15 s).</summary>
        public const double WindowSeconds = 0.15;

        /// <summary>Steps since the buffered press was first delivered; -1 = nothing buffered.</summary>
        int _age = -1;

        /// <summary>A press is waiting for a ready step.</summary>
        public bool Pending => _age >= 0;

        /// <summary>Steps since the waiting press was first delivered (-1 when none).</summary>
        public int Age => _age;

        /// <summary>The window in whole sim steps at simHz: floor(0.15 x simHz), 4 at 30 Hz.</summary>
        public static int MaxEarlyTicks(double simHz)
        {
            if (!(simHz > 0)) return 0;
            return Math.Max(0, (int)Math.Floor(WindowSeconds * simHz + 1e-9));
        }

        public void Clear() => _age = -1;

        /// <summary>
        /// One sim step. pressed: a fresh press latched since the last step. canBuffer: the world is known (false = the
        /// plain one-step latch of before). ready: the sim accepts this button on this step. Returns the button's value
        /// for this step's frame (the held state is ORed in by the caller).
        /// </summary>
        public bool Step(bool pressed, bool canBuffer, bool ready, int maxEarlyTicks)
        {
            if (pressed)
            {
                // Delivered now, as without the buffer. Kept only when the sim would refuse it on this very step.
                _age = canBuffer && !ready && maxEarlyTicks > 0 ? 0 : -1;
                return true;
            }
            if (_age < 0) return false;
            _age++;
            if (!canBuffer || _age > maxEarlyTicks)
            {
                _age = -1;
                return false;
            }
            if (!ready) return false;
            _age = -1;
            return true;
        }
    }

    /// <summary>
    /// When the sim accepts an attack or an ability press from an actor on the NEXT step, read from the world as it is
    /// between two steps (read-only). Mirrors the gates exactly: SimStep runs Abilities first (AbilityCd is not ticked
    /// before the check), then Combat, which ticks AttackCd down by one step before checking it (Combat.TickDown); both
    /// skip dead actors and actors still getting up (StandUp &gt; 0, ticked later in the step by Respawn).
    /// </summary>
    public static class InputReadiness
    {
        public static bool Attack(World w, int id)
        {
            if (!Valid(w, id)) return false;
            Actor a = w.Actors[id];
            return a.Alive && a.StandUp <= 0 && Combat.TickDown(a.AttackCd, SimStep.Dt(w.Config)) <= 0;
        }

        public static bool Ability(World w, int id)
        {
            if (!Valid(w, id)) return false;
            Actor a = w.Actors[id];
            return a.Alive && a.StandUp <= 0 && a.AbilityCd <= 0;
        }

        static bool Valid(World w, int id) =>
            w != null && w.Config != null && w.Match != null && w.Match.Phase != MatchPhase.Ended && id >= 0 && id < w.ActorCount;
    }

    /// <summary>
    /// Hands the driver's World to HumanInputState.BufferWorld before the driver ticks (execution order -50, the driver
    /// runs at 0), so the buffer reads readiness from the live world. Follows a new World when the game flow creates one
    /// per match. View side only: it never writes the sim.
    /// </summary>
    [DefaultExecutionOrder(-50)]
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 20, Scope = CriticalMass.Runtime.Flow.OverlayScope.Always)]   // installed once by GameBootstrap through the overlay registry
    public sealed class InputBufferBinder : MonoBehaviour
    {
        HumanInputSource _input;
        SimDriverBehaviour _driver;

        void Update()
        {
            if (_input == null) _input = GetComponent<HumanInputSource>();
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            if (_input == null) return;
            World w = _driver != null ? _driver.World : null;
            if (!ReferenceEquals(_input.State.BufferWorld, w)) _input.State.BufferWorld = w;
        }

        void OnDisable()
        {
            if (_input != null) _input.State.BufferWorld = null;
        }
    }

    /// <summary>Attaches InputBufferBinder to the game's HumanInputSource object on boot and after every scene load
    /// (the MenuInstaller pattern; GameBootstrap is not edited).</summary>
    public static class InputBufferInstaller
    {
        // Integration batch 2: installed by GameBootstrap through the DG-01 overlay registry ([GameOverlay]); Attach stays for tests.
        /// <summary>Adds the binder next to the HumanInputSource (none: no game, nothing done). Returns it.</summary>
        public static InputBufferBinder Attach(Scene scene)
        {
            GameBootstrap.Build(scene);
            HumanInputSource input = UnityEngine.Object.FindAnyObjectByType<HumanInputSource>();
            if (input == null) return null;
            InputBufferBinder b = input.GetComponent<InputBufferBinder>();
            if (b == null) b = input.gameObject.AddComponent<InputBufferBinder>();
            return b;
        }
    }
}
