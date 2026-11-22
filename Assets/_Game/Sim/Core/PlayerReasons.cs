namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Player-experience wave (2026-11-22): the reason and kind codes carried by the reporting events HitBlocked,
    /// StructureHit, ActionRefused and WorldAction (SimEnums.cs). One place, so the sim, the HUD words and the tests agree.
    /// Append only; never renumber (replays and tests keep the numbers).
    /// </summary>
    public static class PlayerReasons
    {
        // ---- HitBlocked.Value
        public const int BlockedSpawnProtect = 1;   // the target is still spawn protected
        public const int BlockedDodge = 2;          // dodge i-frames
        public const int BlockedWard = 3;           // a Ward soaked the whole hit
        public const int BlockedWall = 4;           // a wall or barricade stands between
        public const int BlockedPassive = 5;        // the passive window (a bot's swing on the human slot)
        public const int BlockedImmune = 6;         // any other immunity (B13Hooks.Immune)
        public const int BlockedShield = 7;         // a raised shield cut the hit (the hit still landed; informational)
        public const int BlockedGlancing = 8;       // lane S2: the hit landed reduced (the Farmer's staff inside her close range,
                                                    // "TOO CLOSE"); Radius = the damage lost; informational like BlockedShield

        // ---- StructureHit.B
        public const int StructureBarricade = 1;
        public const int StructureBuiltWall = 2;
        public const int StructureBreakable = 3;
        public const int StructurePiece = 4;        // ladder / bridge piece
        public const int StructureSolid = 5;        // rock or a wall nobody can damage (Value 0: "cannot be broken")

        // ---- ActionRefused.B (which button)
        public const int ButtonAttack = 1;
        public const int ButtonAbility = 2;
        public const int ButtonDigPlace = 3;
        public const int ButtonInteract = 4;
        public const int ButtonDodge = 5;
        public const int ButtonCancel = 6;
        public const int ButtonJump = 7;
        public const int ButtonStationBuy = 8;  // lane S2: InputFrame.StationBuy (a HUD click, not a held key)

        // ---- ActionRefused.Value (why)
        public const int RefusedCooldown = 1;       // still cooling down
        public const int RefusedNoTarget = 2;       // nothing in reach to act on (no vein, no node, nobody to mend)
        public const int RefusedNotYourClass = 3;   // this class has no such action
        public const int RefusedNoResource = 4;     // not enough wood / ore / carry
        public const int RefusedBlockedPlace = 5;   // the spot is not allowed (keep-out, occupied, off the map)
        public const int RefusedStaggered = 6;      // staggered, knocked down or mid-dodge
        public const int RefusedCarryFull = 7;
        public const int RefusedOutOfRange = 8;
        public const int RefusedNothingToCancel = 9;
        public const int RefusedTeamLimit = 10;     // a team-wide cap or rate limit
        public const int RefusedCarrying = 11;      // lane S2: the load you carry forbids it (a corruption carrier cannot dodge)
        public const int RefusedInAir = 12;         // lane S2: already in the air (a jump pressed mid-flight that will not land in time)
        public const int RefusedRuleOff = 13;       // lane S2: this match's rules retired the action (the hut replaced the station ladder)

        // ---- WorldAction.B
        public const int WorldDig = 1;
        public const int WorldPlace = 2;
        public const int WorldBuild = 3;
        public const int WorldMineOre = 4;
        public const int WorldBank = 5;
        public const int WorldFeed = 6;
        public const int WorldCorrupt = 7;
        public const int WorldPurify = 8;
        public const int WorldRepair = 9;
        public const int WorldGather = 10;          // lane S2: a gather channel started at a node / grove / spring (Value 0 =
                                                    // started, Radius = seconds to the first unit, 0 when unknown); the unit
                                                    // itself is the Gather / SourceGather event as before
    }
}
