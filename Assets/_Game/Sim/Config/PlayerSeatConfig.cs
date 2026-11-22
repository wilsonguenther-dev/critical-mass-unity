// Player-experience wave (2026-11-22), lane S2: "every press either does what the player expects or says why not".
// Every number of Sim/B16/PlayerSeat.cs and the class repairs it switches lives here. Full rules only (PlayerSeat.On).
namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// Lane S2 tunables (hung off DepthConfig.Seat). On = false is the base branch's behaviour and event stream, bit for bit.
    /// </summary>
    public sealed class PlayerSeatConfig
    {
        /// <summary>The whole lane: reporting events (HitBlocked, StructureHit, ActionRefused, WorldAction, AutopilotChanged)
        /// and the class repairs below. Runs only with the Full rules (CombatFeel.On and not Depth.Classic).</summary>
        public bool On = true;

        // ---- NO SILENT PRESS (ActionRefused)
        /// <summary>A held or mashed button raises at most one ActionRefused per this many seconds, per actor and button.</summary>
        public double RefuseRepeatSeconds = 1.0;
        /// <summary>Interact: slack (u) added to every interact reach when deciding "nothing in reach" (never refuse a press a
        /// system might still take).</summary>
        public double InteractSlack = 0.3;

        // ---- DEMOLITIONS POINT-BLANK
        /// <summary>With TNT ready, an Attack press with an enemy within this centre-to-centre distance inside her pry-bar arc
        /// swings the pry bar (the TNT stays ready). 0 = off (always throw).</summary>
        public double PointBlankRange = 1.7;

        // ---- THE HEALER CHOOSES
        /// <summary>An Attack press whose aim point is within this of an enemy inside her lantern reach swings the lantern, even
        /// with hurt allies near (human seat only). 0 = off.</summary>
        public double HealerFightAimRadius = 1.2;
        /// <summary>Human mend target: the hurt ally nearest the aim point when the aim is within this of that ally; else the
        /// MOST hurt ally in range (the bots' rule).</summary>
        public double HealerAimPickRadius = 2.0;

        // ---- THE MINER DIGS AGAIN
        /// <summary>A human Miner's DigPlace away from an ore vein digs the cell ahead with the Builder's DIG rule (no carry, no
        /// place). Bots never dig.</summary>
        public bool MinerDigsAwayFromVein = true;

        // ---- THE IDLE RULE SEES AIM (MechRules.HasInput)
        /// <summary>An aim point that moved this far (u) from where it was when input was last counted is input.</summary>
        public double IdleAimUnits = 0.5;
        /// <summary>An aim DIRECTION from the body that turned this far (degrees) is input (third person: fighter + camera yaw).</summary>
        public double IdleAimDegrees = 2.0;

        public PlayerSeatConfig Clone() => (PlayerSeatConfig)MemberwiseClone();
    }
}
