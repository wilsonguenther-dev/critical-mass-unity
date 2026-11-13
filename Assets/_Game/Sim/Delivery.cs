using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Contestable deliveries (CM-64, browser branch cm/CM-64 src/sim/delivery.ts). Travis, Nov 4 call: "there's no way ... to
    /// kill them before they drop off the resources." A delivery is no longer instant on arrival: the carrier stands in the
    /// shrine delivery zone (KaijuFeed.ShrineInteractRange) holding interact, with cargo or meat, while the Kaiju is an egg,
    /// for KaijuFeed.DeliverWindupTicks sim ticks, and ResourcesSystem hands the load in on the pass the wind-up reaches it.
    ///
    /// Wind-up state is Actor.DeliverTicks: 0 = not winding (leaving the zone, releasing E, dying, the get-up, an empty load
    /// or the hatch puts it back to 0 with no event); 1..N-1 = winding; <see cref="Interrupted"/> (-1) = an enemy hit landed
    /// during the wind-up (Damage.ApplyDamage calls InterruptDelivery): one DeliverIntercepted event and
    /// MatchState.Intercepted[carrier team] += 1; the next qualifying pass only re-arms, so a hit always costs a full fresh
    /// wind-up. The cargo stays with the carrier: the delivery is lost, the load only if the carrier dies.
    /// Deterministic and allocation-free: integer tick counts only.
    /// </summary>
    public static class Delivery
    {
        /// <summary>Actor.DeliverTicks for the rest of the tick an enemy hit interrupted the wind-up.</summary>
        public const int Interrupted = -1;

        /// <summary>The validated wind-up length in sim ticks: KaijuFeed.DeliverWindupTicks when it is a whole number &gt;= 1, else 1.</summary>
        public static int WindupTicks(GameConfig c)
        {
            double v = c.KaijuFeed.DeliverWindupTicks;
            if (v >= 1 && v == System.Math.Floor(v) && v < int.MaxValue) return (int)v;
            return 1;
        }

        /// <summary>Not winding any more, nothing contested.</summary>
        public static void CancelDelivery(Actor a)
        {
            a.DeliverTicks = 0;
        }

        /// <summary>One qualifying pass of a (alive, standing, E held, in the zone, carrying, egg). True on the pass the wind-up completes; the pass after an interruption only re-arms.</summary>
        public static bool StepDeliveryWindup(World w, Actor a)
        {
            if (a.DeliverTicks < 0)
            {
                a.DeliverTicks = 0;
                return false;
            }
            a.DeliverTicks++;
            return a.DeliverTicks >= WindupTicks(w.Config);
        }

        /// <summary>
        /// A hit landed on actor id (Damage.ApplyDamage, after the hostile-source and spawn-protection checks). When it was
        /// winding up a delivery the wind-up is lost: DeliverTicks = Interrupted, MatchState.Intercepted[team] += 1 and one
        /// DeliverIntercepted event (A = carrier, B = attacker id or -1, at the carrier, Value = wind-up ticks reached).
        /// False (nothing written) when the actor was not winding.
        /// </summary>
        public static bool InterruptDelivery(World w, int id, int attacker)
        {
            Actor a = w.Actors[id];
            if (!(a.DeliverTicks > 0)) return false;
            int reached = a.DeliverTicks;
            a.DeliverTicks = Interrupted;
            w.Match.Intercepted[a.Team]++;
            World.PushEvent(w, SimEventType.DeliverIntercepted, id, attacker, w.X[id], w.Z[id], reached);
            return true;
        }
    }
}
