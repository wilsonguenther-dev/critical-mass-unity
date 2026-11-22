using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Player-experience wave (2026-11-22), lane D: the local player's combat HUD, what the player is TOLD. GameHud builds
    /// one at Build (world layer + screen root) and ticks it once per LateUpdate after the camera has moved. It reads the
    /// sim event ring with its own cursor (one pass, allocation-free) and hands each event the local player cares about to
    /// the piece that shows it; every piece reports to the PlayerFeedback ledger at the moment its cue becomes visible.
    /// Threat hierarchy: MY hits, MY hurts, MY kills get the strong cues; other people's events stay quiet.
    /// View only: never writes the sim.
    /// </summary>
    public sealed class PlayerHud
    {
        public static PlayerHud Current { get; private set; }

        public readonly HealthBars Bars;

        World _world;
        int _epoch = -1;
        int _cursor = -1;

        /// <summary>Sim events dropped because the HUD fell a whole ring behind (tests).</summary>
        public int Dropped { get; private set; }

        public PlayerHud(RectTransform root, RectTransform world, RectTransform playerCard)
        {
            Bars = new HealthBars(world);
            Current = this;
        }

        /// <summary>One frame: events since the last frame, then every piece's animation and placement.</summary>
        public void Tick(World w, HudState s, double alpha, UnityEngine.Camera cam, float scale, float dt)
        {
            if (w == null || cam == null) return;
            int me = HudBinder.LocalActor;
            if (!ReferenceEquals(w, _world) || w.Epoch != _epoch)
            {
                _world = w;
                _epoch = w.Epoch;
                _cursor = w.EventHead;
                Bars.Clear();
            }
            if (_cursor < 0 || _cursor > w.EventHead) _cursor = w.EventHead;
            if (w.EventHead - _cursor > World.EventCapacity)
            {
                Dropped += w.EventHead - World.EventCapacity - _cursor;
                _cursor = w.EventHead - World.EventCapacity;
            }
            for (; _cursor < w.EventHead; _cursor++) OnEvent(w, w.Events[_cursor % World.EventCapacity], me);
            Bars.Tick(w, me, alpha, cam, scale, dt);
        }

        void OnEvent(World w, SimEvent e, int me)
        {
            switch (e.Type)
            {
                case SimEventType.Hit:
                    if (e.B == me && e.A != me && e.A >= 0) Bars.OnMyHit(e.A, w.Match.Time);
                    break;
                case SimEventType.StructureHit:
                    if (e.A == me) Bars.OnStructureHit(w, e.X, e.Z, (float)e.Radius, e.B);
                    break;
                case SimEventType.KaijuDamaged:
                    if (e.A == me) Bars.OnMyKaijuHit(w.Match.Time);
                    break;
            }
        }
    }
}
