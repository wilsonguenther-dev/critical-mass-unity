using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>The fixed-step entry point (browser step.ts): the same seed and the same input frames give the same world.</summary>
    public static class SimStep
    {
        /// <summary>Seconds per sim step (browser <c>DT</c>).</summary>
        public static double Dt(GameConfig c) => 1.0 / c.SimHz;

        /// <summary>One step of <see cref="Dt"/> seconds (browser <c>step(world)</c>).</summary>
        public static void Step(World w)
        {
            Step(w, Dt(w.Config));
        }

        /// <summary>One fixed step of every system, in the order of step.ts on 09e0904.</summary>
        public static void Step(World w, double dt)
        {
            if (w.Match.Phase == MatchPhase.Ended) return;
            w.Tick++;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                w.PrevX[i] = w.X[i];
                w.PrevZ[i] = w.Z[i];
            }
            Kaiju k = w.Kaiju;
            k.PrevX = k.X;
            k.PrevZ = k.Z;

            Abilities.AbilitiesSystem(w, dt);
            Movement.MovementSystem(w, dt);
            SpatialHash.Rebuild(w.Hash, w.X, w.Z, w.Actors, n);
            Combat.CombatSystem(w, dt);
            Topple.ToppleSystem(w); // DG-09: ToppleDown events when a fall animation time has passed
            BuiltPieces.PiecesSystem(w); // DG-24: broken / expired ladders and bridges, after this step's digs and craters
            Barricades.BarricadesSystem(w, dt);
            SimResources.ResourcesSystem(w, dt);
            Kaiju.KaijuSystem(w, dt);
            Respawn.RespawnSystem(w, dt);
            // CM-37: after respawn (a fresh respawn is already at full HP), before the hill / match bookkeeping.
            Heal.HealSystem(w);
            Hill.HillSystem(w, dt);
            Match.MatchSystem(w, dt);

            w.Match.Time += dt;
        }
    }
}
