namespace CriticalMass.Sim.Config
{
    /// <summary>
    /// b16 physics fixes (physics audit 2026-11-21, ~/cm-incidents/physics-audit-2026-11-21/PHYSICS-AUDIT.md). Full rules only:
    /// a rule runs when Enabled is true, DepthConfig.Classic is false and PhysicsConfig.Classic is false (Sim/B16/PhysicsFeel.On),
    /// so the Classic and PhysicsOnly golden rows never move; Enabled = false is the game before this batch, bit for bit.
    /// Every number of the batch lives here. Audit item numbers in brackets.
    /// </summary>
    public sealed class PhysicsFeelConfig
    {
        public bool Enabled = true;
        /// <summary>px DM (two-minute walking shiver): a grounded walker touching the hatched solid Kaiju walks ALONG its body
        /// (the into-body share of its step is removed before the step), instead of stepping in and being pushed back out by
        /// Collide.KaijuBody every tick (a 3-position cycle 5 cm wide against a wall corner, MovementShiver seed 7).
        /// OFF: measured on the shiver gate's match it re-plays the fight and raises the hard flags 2 -> 8 (fighting bots on the
        /// hill reversing; data/dm-defence/shiver-slide.txt), a trade, so it stays off (lane DM, 2026-11-22).</summary>
        public bool KaijuWalkSlide = false;
        /// <summary>Contact slack (u) beyond body + actor radius inside which the walk slides round the Kaiju.</summary>
        public double KaijuWalkSlideSlack = 0.05;

        // ---- [4] corner grazes
        /// <summary>Largest overlap with a corner cell that is slid round (u; PhysicsConfig.CornerNudgeMax 0.1 otherwise). An
        /// overlap larger than this sub-step's length is slid off over several ticks: the forward part stops at the corner face
        /// and the body slides sideways by at most the step length until it clears, then walks on.</summary>
        public double CornerNudgeMax = 0.35;

        // ---- [12] wall slide
        /// <summary>Share of the step kept along a wall when one axis is blocked and the input is within WallSlideFullDeg of the
        /// wall (pure projection keeps cos(angle): 71 percent at 45 degrees). Between WallSlideFullDeg and WallSlideFadeDeg it
        /// blends back to the projection, so there is no jump in speed at any angle.</summary>
        public double WallSlideKeep = 0.92;
        public double WallSlideFullDeg = 45;
        public double WallSlideFadeDeg = 75;

        // ---- [1] Kaiju pins a fighter, [7] crowd shoves the Kaiju
        /// <summary>Most a fighter is moved in one tick by the Kaiju body's push-out and its end-of-tick depenetration together (u;
        /// with a top-speed step, 0.13 u, the whole tick stays under 0.35 u). The old ring search could place a pinned fighter
        /// 2.71 u away on the far side of the body. A fighter found deeper than this inside the body (a respawn, a landing) still
        /// takes the old ring search.</summary>
        public double KaijuPinStepMax = 0.2;
        /// <summary>A pinned fighter (no free spot on its own side within KaijuPinStepMax) holds the Kaiju: the body gives way
        /// instead of the fighter being placed through it, by what is missing (at most KaijuPinStepMax), with the Kaiju's whole
        /// move this tick (its walk plus the give-way) held under KaijuNetSpeedMax.</summary>
        public double KaijuNetSpeedMax = 3.0;
        /// <summary>Most the whole crowd of fighters wedged against it can shove the Kaiju in one tick (u, about 0.6 u/s of drift).
        /// The rest is dropped: fighters do not move the Kaiju beyond a slow drift.</summary>
        public double KaijuCrowdShoveMax = 0.02;

        // ---- [8] Kaiju turn rate
        /// <summary>The Kaiju's sim facing turns at most this fast (degrees per real second). It starts a claw only once it faces
        /// the target, so the claw cone always matches the drawn facing.</summary>
        public double KaijuTurnDegPerSec = 300;

        // ---- [2] blast shove
        /// <summary>TNT and mine blasts shove under their own switch (PhysicsConfig.Knockback, which gates every fighter knock and is
        /// off, no longer decides it).</summary>
        public bool BlastShove = true;
        /// <summary>Shove at the blast centre (u travelled for mass 1), falling linearly to 0 at the blast radius. The travelled
        /// distance is this / sqrt(mass) (a blast throws heavy and light bodies alike, the heavy a little less): 2.2 u for a Warrior,
        /// 3.1 u for a Farmer at the centre.</summary>
        public double BlastShoveCentre = 2.8;
        /// <summary>Stagger at the centre (real seconds), falling linearly to 0 at the radius.</summary>
        public double BlastStunSeconds = 0.25;
        /// <summary>The hatched Kaiju inside a TNT blast slides this far at the centre (u), falling to 0 at the edge.</summary>
        public double BlastKaijuShove = 0.35;
        /// <summary>The Kaiju's blast slide keeps this share of its speed per 1/30 s.</summary>
        public double KaijuPushDecay = 0.7;

        // ---- [3] bot fight shiver
        /// <summary>The circle-strafe keep-distance pull is zero within this band (u) of the wanted distance (no in-out flip at it).</summary>
        public double BotPullDeadband = 0.2;
        /// <summary>Separation never pushes a walking body backwards past a standstill in one tick: the part of the push against its
        /// own step this tick is capped at that step's length (lateral and forward parts are kept).</summary>
        public bool SeparationNoReversal = true;
        /// <summary>[3] A bot whose wish flips over 120 degrees off its motion turns through the side at the bot turn rate instead
        /// of stepping straight back (fighters and straight walkers; the field-following turn limit already covers the rest).</summary>
        public bool BotNoSnapReversal = true;

        // ---- [6] allies fusing, [5] one-cell gaps
        /// <summary>Teammates push at PhysicsConfig.AllyPushShare up to AllyPushRampFrom of overlap, rising to the full push at
        /// AllyPushFullAt.</summary>
        public double AllyPushRampFrom = 0.1;
        public double AllyPushFullAt = 0.25;
        /// <summary>The U-071 opposing-ally pass (no push between a retreating bot and an ally walking into it) only inside or at the
        /// mouth of a one-cell passage, where they cannot step round each other.</summary>
        public bool AllyPassInGapsOnly = true;
        /// <summary>Bodies queue through one-cell gaps: inside a gap (or one cell from its mouth along its walk) a body's push across
        /// the passage is dropped, and of a pair where only one is there, that one takes GapLeadShare of the push.</summary>
        public bool GapFlow = true;
        public double GapLeadShare = 0.2;

        // ---- [11] refused jump
        /// <summary>A jump press the sim refuses (no clear landing, past the coyote window) raises SimEventType.JumpRefused once
        /// the buffer runs out, for the view's cue.</summary>
        public bool JumpRefusedEvent = true;

        public PhysicsFeelConfig Clone() => (PhysicsFeelConfig)MemberwiseClone();
    }
}
