using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// b15 combat feel: per-world state, allocated once at capacity (DepthState.Create) and reset in place (DepthState.Reset).
    /// Every field is an int or a byte written in actor-id order inside the tick: deterministic, nothing allocates per tick.
    /// Not hashed by Net/StateHash; a desync still shows through HP, positions and cells, which are.
    /// </summary>
    public sealed class CombatFeelState
    {
        /// <summary>World.Tick of the last attack press that was not taken yet (CombatFeel.NoTick = none).</summary>
        public int[] BufferTick;
        /// <summary>Staggers taken inside the current diminishing-returns window, and the tick that window ends.</summary>
        public int[] StaggerCount;
        public int[] StaggerWindowEnd;
        /// <summary>First tick the actor may be staggered again after StaggerMaxInWindow staggers (immunity).</summary>
        public int[] ImmuneUntil;
        /// <summary>Last tick of the post-stagger slow.</summary>
        public int[] SlowUntil;
        /// <summary>Last tick of the Miner's SUNDER on this actor.</summary>
        public int[] SunderUntil;
        /// <summary>The tick of the last landed melee strike on this actor and who struck it (-1 none). Read by the view only
        /// (GroundView keeps the victim of your own hit out of the see-through dither); never read by the sim.</summary>
        public int[] StruckTick, StruckBy;
        /// <summary>First tick of the actor's current run of melee staggers (StaggerMaxLockSeconds caps the run).</summary>
        public int[] LockStart;
        /// <summary>Last tick the actor's current run of melee staggers holds it (a new stagger by the tick after continues the run).</summary>
        public int[] LockUntil;
        /// <summary>Ticks in a row this actor has been staggered (HitStunTicks above 0) and the longest such run this match.</summary>
        public int[] LockRun;
        public int MaxLockRun;
        /// <summary>b16 outnumbered: last tick of the armour break (a second enemy hit an armoured body within OutnumberedSeconds).</summary>
        public int[] ArmourBreakUntil;
        /// <summary>b16 CANCEL: first tick a new feint is allowed (FeintLockoutSeconds after the last one).</summary>
        public int[] FeintLockUntil;
        /// <summary>b16 CANCEL: the SwingTick of the swing that already used its cancel (one per attack).</summary>
        public int[] CancelUsed;
        /// <summary>b16 CANCEL: the SwingTick of the last swing that landed (a hit-cancel needs it; a whiff never sets it).</summary>
        public int[] LandedSwing;
        /// <summary>px S1 [4][5]: last tick of the WOUNDED window (a landed melee hit opens WoundedSeconds of it).</summary>
        public int[] WoundedUntil;
        /// <summary>px S1 [9] PUNISH: last tick of the recovery of this actor's last WHIFFED swing (NoTick = none).</summary>
        public int[] WhiffRecoverUntil;
        /// <summary>px S1 [9]: the tick, striker and kind of the critical hit DamageMul read for this target (ApplyStrike raises it).</summary>
        public int[] CritTick, CritFrom, CritKind;
        /// <summary>px S1 [9]: critical hits raised this match (a probe / test counter).</summary>
        public int Crits;
        /// <summary>1 on every cell of a built ladder lane (a Bridge cell laid over a Wall), rebuilt once per tick.</summary>
        public byte[] Ladder;
        public int[] LadderCells;
        public int LadderCount;
        public int LadderTick;

        /// <summary>Counters the tests and the balance print read.</summary>
        public int Swings, Whiffs, Staggers, StaggerDiminished, StaggerImmune, StaggerArmoured, Buffered, LadderFalls, TntSelfHits;
        /// <summary>b16: feints, hit-cancels, outnumbered armour breaks.</summary>
        public int Feints, HitCancels, ArmourBreaks;
        public double TntSelfDamage;

        public static CombatFeelState Create(GameConfig cfg, MapDef map)
        {
            int cap = World.ActorCapacity(cfg);
            int cells = map.Width * map.Height;
            var s = new CombatFeelState
            {
                BufferTick = new int[cap],
                StaggerCount = new int[cap],
                StaggerWindowEnd = new int[cap],
                ImmuneUntil = new int[cap],
                SlowUntil = new int[cap],
                SunderUntil = new int[cap],
                StruckTick = new int[cap],
                StruckBy = new int[cap],
                LockStart = new int[cap],
                LockUntil = new int[cap],
                LockRun = new int[cap],
                ArmourBreakUntil = new int[cap],
                FeintLockUntil = new int[cap],
                CancelUsed = new int[cap],
                LandedSwing = new int[cap],
                WoundedUntil = new int[cap],
                WhiffRecoverUntil = new int[cap],
                CritTick = new int[cap],
                CritFrom = new int[cap],
                CritKind = new int[cap],
                Ladder = new byte[cells],
                LadderCells = new int[cells],
            };
            s.Reset();
            return s;
        }

        /// <summary>Back to the start of a match, in place.</summary>
        public void Reset()
        {
            for (int i = 0; i < BufferTick.Length; i++)
            {
                BufferTick[i] = CombatFeel.NoTick;
                StaggerCount[i] = 0;
                StaggerWindowEnd[i] = CombatFeel.NoTick;
                ImmuneUntil[i] = CombatFeel.NoTick;
                SlowUntil[i] = CombatFeel.NoTick;
                SunderUntil[i] = CombatFeel.NoTick;
                StruckTick[i] = CombatFeel.NoTick;
                StruckBy[i] = -1;
                LockStart[i] = CombatFeel.NoTick;
                LockUntil[i] = CombatFeel.NoTick;
                LockRun[i] = 0;
                ArmourBreakUntil[i] = CombatFeel.NoTick;
                FeintLockUntil[i] = CombatFeel.NoTick;
                CancelUsed[i] = CombatFeel.NoTick;
                LandedSwing[i] = CombatFeel.NoTick;
                WoundedUntil[i] = CombatFeel.NoTick;
                WhiffRecoverUntil[i] = CombatFeel.NoTick;
                CritTick[i] = CombatFeel.NoTick;
                CritFrom[i] = -1;
                CritKind[i] = 0;
            }
            for (int k = 0; k < LadderCount; k++) Ladder[LadderCells[k]] = 0;
            LadderCount = 0;
            LadderTick = CombatFeel.NoTick;
            MaxLockRun = 0;
            Crits = 0;
            Swings = Whiffs = Staggers = StaggerDiminished = StaggerImmune = StaggerArmoured = Buffered = LadderFalls = TntSelfHits = 0;
            Feints = HitCancels = ArmourBreaks = 0;
            TntSelfDamage = 0;
        }
    }

    /// <summary>
    /// b15 combat feel rules (numbers: CombatFeelConfig, the one tuning block). On only in the Full rules with
    /// CombatFeelConfig.Enabled (see CombatFeelConfig for the rule list). Call sites: Combat (buffer, strike, whiff, telegraph
    /// fields), Damage.Knocks (melee hits carry knock and tier), Movement (swing / stagger / ladder speed), Collide (ladder
    /// pass-through), Demolitions (TNT cooldown, self-risk), Healer (mend), BotSteer (Healer stand-off, narrow-spot steering),
    /// BotFsm (unstick to open ground). Fixed rules only: no RNG, no float, no allocation, actor-id order.
    /// </summary>
    public static class CombatFeel
    {
        /// <summary>"Never" for the tick fields (far in the past, no overflow when a window is added).</summary>
        public const int NoTick = int.MinValue / 4;

        /// <summary>True when the rule runs in config c.</summary>
        public static bool On(GameConfig c) =>
            c.Depth.CombatFeel != null && c.Depth.CombatFeel.Enabled && !c.Depth.Classic && !c.Physics.Classic;

        public static bool On(World w) => On(w.Config);

        public static CombatFeelConfig Cfg(World w) => w.Config.Depth.CombatFeel;

        /// <summary>Real seconds to ticks at this world's pace (like every reaction window, Durations.RealSeconds).</summary>
        public static int Ticks(World w, double realSeconds) => Durations.RealSeconds(w.Config, realSeconds, w.Dur.Pace);

        // ------------------------------------------------------------------------------------------------ A. input buffer

        /// <summary>
        /// Combat: does actor i want to swing this tick? The held press, or a press kept from at most BufferSeconds ago that was
        /// not taken yet. Records this tick's press. Call once per actor per tick before the readiness checks.
        /// </summary>
        public static bool WantsAttack(World w, int i, bool pressed)
        {
            CombatFeelState s = w.Depth.Feel;
            if (pressed)
            {
                s.BufferTick[i] = w.Tick;
                return true;
            }
            int t = s.BufferTick[i];
            if (t == NoTick) return false;
            if (w.Tick - t <= Ticks(w, Cfg(w).BufferSeconds)) return true;
            s.BufferTick[i] = NoTick;
            return false;
        }

        /// <summary>Combat: the press was taken (a swing started or a mend landed); a buffered press counts once.</summary>
        public static void TookPress(World w, int i)
        {
            CombatFeelState s = w.Depth.Feel;
            if (s.BufferTick[i] != NoTick && s.BufferTick[i] != w.Tick) s.Buffered++;
            s.BufferTick[i] = NoTick;
        }

        // ------------------------------------------------------------------------------------------- C. damage modifiers

        /// <summary>
        /// The damage multiplier of a melee hit by source on target standing distance d apart (centre to centre): SUNDER on the
        /// target, the Farmer's close-range weakness, the Miner's heavy pierce, the Builder at home.
        /// </summary>
        public static double DamageMul(World w, int source, int target, double d)
        {
            CombatFeelConfig f = Cfg(w);
            Actor s = w.Actors[source];
            double m = 1;
            if (w.Tick <= w.Depth.Feel.SunderUntil[target]) m *= 1 + f.SunderDamageTaken;
            // b16 solo guard: one on one (no other enemy struck the target within OutnumberedSeconds) it shrugs off a share.
            if (f.SoloGuard != null)
            {
                int tc = (int)w.Actors[target].Cls;
                if (tc < f.SoloGuard.Length && f.SoloGuard[tc] > 0)
                {
                    int by = w.Depth.Feel.StruckBy[target];
                    if (by < 0 || by == source || w.Tick - w.Depth.Feel.StruckTick[target] > Ticks(w, f.OutnumberedSeconds)) m *= 1 - f.SoloGuard[tc];
                }
            }
            switch (s.Cls)
            {
                case ClassId.Farmer:
                    if (d < f.FarmerCloseRange) m *= f.FarmerCloseShare;
                    break;
                case ClassId.Miner:
                    // b16: MinerPierceWarrior false = the pierce spares the Warrior (the fighter wins his duels).
                    if (w.Config.Class(w.Actors[target].Cls).Mass >= f.MinerHeavyMass && (f.MinerPierceWarrior || w.Actors[target].Cls != ClassId.Warrior)) m *= f.MinerHeavyPierce;
                    break;
                case ClassId.Builder:
                    if (NearHome(w, source)) m *= f.BuilderHomeMultiplier;
                    break;
            }
            // px S1 [9] critical strike, last (kinds do not stack). Read on the queue, before any hit of this tick lands.
            int kind = CritKindOf(w, source, target);
            if (kind > 0)
            {
                m *= f.CritDamageMul;
                CombatFeelState st = w.Depth.Feel;
                st.CritTick[target] = w.Tick;
                st.CritFrom[target] = source;
                st.CritKind[target] = kind;
            }
            return m;
        }

        /// <summary>
        /// px S1 [9]: the critical kind of a melee hit by source on target now (0 none, 1 COUNTER: target is in his own wind-up,
        /// 2 PUNISH: target is in the recovery of a whiffed swing, 3 BACK: source stands in target's rear arc). Earned, never rolled.
        /// </summary>
        public static int CritKindOf(World w, int source, int target)
        {
            CombatFeelConfig f = Cfg(w);
            if (!f.CritEnabled || source < 0 || target < 0 || source == target) return 0;
            Actor t = w.Actors[target];
            if (!t.Alive) return 0;
            // px S3: a critical strike is a hit on an enemy (melee never lands on an ally); also keeps the back-arc bearing
            // off the per-tick cost probe's ally pairs.
            if (w.Actors[source].Team == t.Team) return 0;
            // px S3: a victim whose own swing reaches contact THIS tick is trading blows, not caught winding up: no COUNTER
            // (the queue is read in actor-id order, so the lower id used to crit every same-tick trade: S35 16-0 mirrors).
            // fix/mv-trade-crit (class moves on, a person involved): a victim whose own contact lands within
            // MovesHooks.OffenceTradeTicks is trading, not caught winding up: no COUNTER (the earlier hitter of every Miner trade
            // took it, 0.80 of mirror trades). Every switch off and bot-only matches: unchanged.
            if (t.SwingPending && t.SwingTick + t.SwingWindup > w.Tick
                && !(w.Moves != null && t.SwingTick + t.SwingWindup - w.Tick <= MovesHooks.OffenceTradeTicks && MovesHooks.TradeCounterSkip(w, source, target))) return 1;
            if (w.Tick <= w.Depth.Feel.WhiffRecoverUntil[target]) return 2;
            if (f.CritBackArcDeg > 0)
            {
                double dx = w.X[source] - w.X[target], dz = w.Z[source] - w.Z[target];
                double l = Math.Sqrt(dx * dx + dz * dz);
                if (l > 1e-9)
                {
                    // the rear arc of CritBackArcDeg: the angle between his facing and the way to the attacker > 180 - arc/2.
                    // px S3: one fdlibm call (the bearing), not three (cos and sin of his facing, cos of the half-arc): the
                    // rule's own per-tick cost went 2.5 -> 25 us with crits on (CombatFeelTests.TickCost, limit 0.05 of a tick).
                    double off = JsMath.Atan2(dz, dx) - t.Facing;
                    off -= 2 * Math.PI * Math.Floor((off + Math.PI) / (2 * Math.PI));
                    if (Math.Abs(off) > Math.PI - f.CritBackArcDeg * 0.5 * Math.PI / 180 + 1e-12) return 3;
                }
            }
            return 0;
        }

        /// <summary>True when one of the Builder's team's walls or barricades stands within BuilderHomeRadius of him.</summary>
        public static bool NearHome(World w, int id)
        {
            double r = Cfg(w).BuilderHomeRadius;
            double r2 = r * r;
            double x = w.X[id], z = w.Z[id];
            TeamId team = w.Actors[id].Team;
            Barricade[] list = w.Barricades;
            for (int k = 0; k < list.Length; k++)
            {
                Barricade b = list[k];
                if (!b.Active || b.Team != team) continue;
                double dx = b.Cx + 0.5 - x, dz = b.Cz + 0.5 - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        /// <summary>Structure damage multiplier of a swing by class cls (the Miner's pick breaks walls twice as fast).</summary>
        public static double StructureMul(World w, ClassId cls) => cls == ClassId.Miner ? Cfg(w).MinerStructureMultiplier : 1;

        // ------------------------------------------------------------------------------------------- B. the landed strike

        /// <summary>
        /// The stagger (ticks) a melee hit of amount would give target now, without writing anything: 0 while immune, 0 for a
        /// hit under the Poise of an armoured class winding up, else StaggerSeconds (HeavyStaggerSeconds at or over Poise)
        /// times StaggerDecay per earlier stagger in the window (at least 1 tick).
        /// </summary>
        public static int StaggerFor(World w, int target, double amount, out bool armoured, out bool immune) => StaggerFor(w, target, amount, -1, out armoured, out immune);

        /// <summary>StaggerFor with the striker (px S1 [6]: LightHitInterrupts reads its class; -1 = none).</summary>
        public static int StaggerFor(World w, int target, double amount, int source, out bool armoured, out bool immune)
        {
            CombatFeelConfig f = Cfg(w);
            CombatFeelState s = w.Depth.Feel;
            Actor t = w.Actors[target];
            armoured = false;
            immune = w.Tick < s.ImmuneUntil[target];
            if (immune) return 0;
            bool heavy = amount >= w.Config.Class(t.Cls).Poise;
            // Armour break: a SUNDERED body (a Miner hit it in the last SunderSeconds) loses its armoured wind-up.
            // b16: SunderBreaksArmour false = the armour holds through SUNDER; outnumbered (a second enemy inside
            // OutnumberedSeconds, OutnumberedBreaksArmour) breaks it instead.
            if (!heavy && t.SwingPending && f.Strikes[(int)t.Cls].ArmouredWindup && (w.Tick > s.SunderUntil[target] || !f.SunderBreaksArmour)
                && w.Tick > s.ArmourBreakUntil[target])
            {
                armoured = true;
                return 0;
            }
            // px S1 [6]: a light hit (under the victim's Poise) no longer silently eats a wind-up of any class: the swing goes on
            // (damage and knock still land); only a heavy hit staggers and cancels it (raising AttackCancel with who did it).
            // LightHitKeepFromShare: only once this share of the wind-up has run (the swing is committed); before it a light hit
            // still interrupts (the Farmer's quick thrust keeps its job in the duel order).
            bool interrupts = source >= 0 && f.LightHitInterrupts != null && (int)w.Actors[source].Cls < f.LightHitInterrupts.Length && f.LightHitInterrupts[(int)w.Actors[source].Cls];
            if (!heavy && t.SwingPending && f.LightHitsKeepWindup && !interrupts && w.Tick - t.SwingTick >= f.LightHitKeepFromShare * t.SwingWindup) return 0;
            int baseTicks = Ticks(w, heavy ? f.HeavyStaggerSeconds : f.StaggerSeconds);
            if (baseTicks <= 0) return 0;
            int count = w.Tick <= s.StaggerWindowEnd[target] ? s.StaggerCount[target] : 0;
            double k = 1;
            for (int c = 0; c < count; c++) k *= f.StaggerDecay;
            int ticks = (int)Math.Floor(baseTicks * k + 0.5);
            if (ticks < 1) ticks = 1;
            // No stun-lock: inside a running stagger the run may not pass StaggerMaxLockSeconds from its first tick.
            if (InLock(s, target, w.Tick))
            {
                int cap = s.LockStart[target] + Ticks(w, f.StaggerMaxLockSeconds) - w.Tick;
                if (ticks > cap) ticks = cap < 0 ? 0 : cap;
            }
            return ticks;
        }

        /// <summary>True while target's run of melee staggers is still going at tick (stunned now, or its last stunned tick was the one before).</summary>
        private static bool InLock(CombatFeelState s, int target, int tick) =>
            s.LockStart[target] != NoTick && tick <= s.LockUntil[target] + 1;

        /// <summary>
        /// A landed melee hit of amount (already multiplied by DamageMul) by source standing at (srcX, srcZ): damage through the
        /// one entry point with this class's knock and the stagger above, then the bookkeeping (diminishing returns, immunity,
        /// slow, SUNDER, the ladder fall). Returns the damage dealt.
        /// </summary>
        public static double ApplyStrike(World w, int target, double amount, int source, double srcX, double srcZ)
        {
            if (source < 0 || source >= w.ActorCount || target < 0 || target >= w.ActorCount) return Damage.ApplyMelee(w, target, amount, source, srcX, srcZ);
            CombatFeelConfig f = Cfg(w);
            CombatFeelState s = w.Depth.Feel;
            Actor src = w.Actors[source];
            Actor t = w.Actors[target];
            // b16 outnumbered: a second enemy's hit inside OutnumberedSeconds of another's breaks an armoured body's armour.
            if (f.OutnumberedBreaksArmour && f.Strikes[(int)t.Cls].ArmouredWindup && s.StruckBy[target] >= 0 && s.StruckBy[target] != source
                && w.Tick - s.StruckTick[target] <= Ticks(w, f.OutnumberedSeconds))
            {
                if (w.Tick > s.ArmourBreakUntil[target]) s.ArmourBreaks++;
                s.ArmourBreakUntil[target] = w.Tick + Ticks(w, f.OutnumberedSeconds);
            }
            s.StruckTick[target] = w.Tick;
            s.StruckBy[target] = source;
            Knock.Dir(w, target, srcX, srcZ, JsMath.Cos(src.SwingFacing), JsMath.Sin(src.SwingFacing), out double dx, out double dz);
            int stun = StaggerFor(w, target, amount, source, out bool armoured, out bool immune);
            if (f.StrikeStaggers != null && (int)src.Cls < f.StrikeStaggers.Length && !f.StrikeStaggers[(int)src.Cls]) stun = 0; // b16: this class's hits do not stagger
            // px DM (review defect 6): a hit the Warrior's raised shield cuts (frontal) is BLOCKED, not a stagger
            if (stun > 0 && t.Cls == ClassId.Warrior && t.AbilityActive > 0 && PlayerSeat.On(w) && PlayerSeat.Cfg(w).ShieldStopsStagger && Damage.IsFrontal(w, target, srcX, srcZ)) stun = 0;
            double knock = f.Strikes[(int)src.Cls].Knock; MovesHooks.StrikeKnock(w, source, target, ref knock, ref stun);   // mv-hook (lane 1's line in lane 2's file)
            bool locked = InLock(s, target, w.Tick);
            bool clipped = locked && !immune && w.Tick + stun >= s.LockStart[target] + Ticks(w, f.StaggerMaxLockSeconds);
            if (stun > 0)
            {
                if (!locked) s.LockStart[target] = w.Tick;
                if (w.Tick + stun - 1 > s.LockUntil[target] || !locked) s.LockUntil[target] = w.Tick + stun - 1;
            }
            bool onLadder = OnLadder(w, target);
            int head0 = w.EventHead;
            double dealt = Damage.ApplyHit(w, target, amount, source, srcX, srcZ, DamageCause.Melee, dx, dz, knock, stun);   // mv-hook: knock (was the class row's Knock, unchanged when off)
            if (dealt > 0 && s.CritTick[target] == w.Tick && s.CritFrom[target] == source && s.CritKind[target] > 0)
            {
                // px S1 [9]: the strike's Hit event reads critical (Tier 2) and one CriticalHit follows it.
                for (int e = head0; e < w.EventHead; e++)
                {
                    SimEvent ev = w.Events[e % World.EventCapacity];
                    if (ev.Type == SimEventType.Hit && ev.A == target) ev.Tier = 2;
                }
                double bonus = f.CritDamageMul > 0 ? dealt - dealt / f.CritDamageMul : 0;
                World.PushEvent(w, SimEventType.CriticalHit, target, source, w.X[target], w.Z[target], s.CritKind[target], null, DamageCause.Melee, bonus);
                s.Crits++;
                s.CritTick[target] = NoTick;
            }
            if (!(dealt > 0) || !t.Alive) return dealt;
            if (f.WoundedSeconds > 0) s.WoundedUntil[target] = w.Tick + Ticks(w, f.WoundedSeconds);   // px S1 [4][5]
            if (armoured) s.StaggerArmoured++;
            if (immune) s.StaggerImmune++;
            if (clipped)
            {
                s.ImmuneUntil[target] = w.Tick + stun + Ticks(w, f.StaggerImmuneSeconds);
                s.StaggerCount[target] = 0;
                s.StaggerWindowEnd[target] = NoTick;
            }
            if (stun > 0)
            {
                if (w.Tick > s.StaggerWindowEnd[target])
                {
                    s.StaggerCount[target] = 0;
                    s.StaggerWindowEnd[target] = w.Tick + Ticks(w, f.StaggerWindowSeconds);
                }
                if (s.StaggerCount[target] > 0) s.StaggerDiminished++;
                s.StaggerCount[target]++;
                s.Staggers++;
                if (s.StaggerCount[target] >= f.StaggerMaxInWindow || clipped)
                {
                    s.ImmuneUntil[target] = w.Tick + stun + Ticks(w, f.StaggerImmuneSeconds);
                    s.StaggerCount[target] = 0;
                    s.StaggerWindowEnd[target] = NoTick;
                }
                s.SlowUntil[target] = w.Tick + stun + Ticks(w, f.StaggerSlowSeconds);
            }
            if (src.Cls == ClassId.Miner) s.SunderUntil[target] = w.Tick + Ticks(w, f.SunderSeconds);
            if (onLadder && f.LadderFallKnock > 0)
            {
                // F: a hit on the ladder knocks the climber back the way it came (against its climb), else away from the hit.
                double vx = w.Vx[target], vz = w.Vz[target];
                double l = Math.Sqrt(vx * vx + vz * vz);
                double fx = l > 1e-6 ? -vx / l : dx, fz = l > 1e-6 ? -vz / l : dz;
                Knock.Push(w, target, source, fx, fz, Knock.Effective(w, target, f.LadderFallKnock, srcX, srcZ), 0, 0, DamageCause.Melee);
                s.LadderFalls++;
            }
            return dealt;
        }

        /// <summary>Combat, at the contact tick of actor i's swing: a whiff adds WhiffRecoverySeconds; the cooldown never ends before the recovery.</summary>
        public static void AfterContact(World w, int i, int hits)
        {
            CombatFeelState s = w.Depth.Feel;
            Actor a = w.Actors[i];
            s.Swings++;
            if (hits <= 0)
            {
                s.Whiffs++;
                double extra = Cfg(w).WhiffExtraSeconds != null && (int)a.Cls < Cfg(w).WhiffExtraSeconds.Length ? Cfg(w).WhiffExtraSeconds[(int)a.Cls] : 0;
                a.SwingRecovery += Ticks(w, Cfg(w).WhiffRecoverySeconds + extra);
                s.WhiffRecoverUntil[i] = w.Tick + a.SwingRecovery;   // px S1 [9] PUNISH window
            }
            else s.LandedSwing[i] = a.SwingTick; // b16 CANCEL: this swing landed (a hit-cancel may cut its recovery)
            double left = a.SwingRecovery / w.Config.SimHz;
            if (a.AttackCd < left) a.AttackCd = left;
        }

        // ------------------------------------------------------------------------------------------------ px S1 wounded

        /// <summary>px S1 [4][5]: true while actor id is WOUNDED (a melee hit landed on it within the last WoundedSeconds).</summary>
        public static bool Wounded(World w, int id) => On(w) && w.Tick <= w.Depth.Feel.WoundedUntil[id];

        /// <summary>px S1 [4]: the share of a heal actor id receives now: WoundedHealShare (mend, pulse) or WoundedFountainShare
        /// (the fountain) while wounded, else 1 (and 1 with the rule off).</summary>
        public static double HealShare(World w, int id, bool fountain)
        {
            if (!Wounded(w, id)) return 1;
            return fountain ? Cfg(w).WoundedFountainShare : Cfg(w).WoundedHealShare;
        }

        // ------------------------------------------------------------------------------------------------ px S1 swing tracking

        /// <summary>
        /// px S1 [1]: during the wind-up the swing direction (Actor.SwingFacing, read live by the view's telegraph) turns toward
        /// the actor's current Facing (its aim, set by Movement this tick) by at most the class's SwingTrackDegPerSecond, until
        /// SwingTrackLockTicks before the contact tick: from then on it is locked, so a late side-step or a dodge still beats it.
        /// Plain arithmetic on doubles in actor-id order: deterministic, no allocation.
        /// </summary>
        /// <summary>px DM (call 18): bot i's swing is at a human seat, or its target is not known (no bot state, no target): the
        /// human-limit tracking applies; at the Kaiju or another actor that is not a human seat it does not.</summary>
        static bool SwingAtHuman(World w, int i)
        {
            if (!Bots.BotBrain.TryGetBotsState(w, out Bots.BotsState bst) || bst.Target == null || i >= bst.Target.Length) return true;
            int t = bst.Target[i];
            if (t == Bots.BotsState.TargetKaiju) return false;
            if (t < 0 || t >= w.ActorCount) return true;
            return w.Actors[t].IsHuman;
        }

        public static void TrackSwing(World w, int i)
        {
            CombatFeelConfig f = Cfg(w);
            Actor a = w.Actors[i];
            if (!a.Alive || a.StandUp > 0 || a.HitStunTicks > 0) return;
            int contact = a.SwingTick + a.SwingWindup;
            if (contact - w.Tick <= f.SwingTrackLockTicks) return;
            double deg = f.SwingTrackDegPerSecond;
            int c = (int)a.Cls;
            if (f.SwingTrackDegPerSecondByClass != null && c < f.SwingTrackDegPerSecondByClass.Length && f.SwingTrackDegPerSecondByClass[c] > 0)
                deg = f.SwingTrackDegPerSecondByClass[c];
            // [2] human limits: a bot corrects its wound-up swing at BotSwingTrackScale of the rate (by difficulty).
            if (!a.IsHuman && f.BotHumanLimits && f.BotSwingTrackScale != null
                && (!f.BotSwingTrackHumansOnly || SwingAtHuman(w, i)))
            {
                int k = (int)Bots.BotSkill.Of(w, i);
                if (k < f.BotSwingTrackScale.Length) deg *= f.BotSwingTrackScale[k];
            }
            if (!(deg > 0)) return;
            double lim = deg / w.Config.SimHz * Math.PI / 180;
            double diff = a.Facing - a.SwingFacing;
            while (diff > Math.PI) diff -= 2 * Math.PI;
            while (diff < -Math.PI) diff += 2 * Math.PI;
            if (diff > lim) diff = lim;
            else if (diff < -lim) diff = -lim;
            if (diff == 0) return;
            double s = a.SwingFacing + diff;
            if (s > Math.PI) s -= 2 * Math.PI;
            else if (s < -Math.PI) s += 2 * Math.PI;
            a.SwingFacing = s;
        }

        // ------------------------------------------------------------------------------------------------ b16 CANCEL

        /// <summary>
        /// Combat, first thing per actor each tick: the CANCEL press (one-shot, read and cleared here). During the wind-up
        /// (strictly before the contact tick) it is a FEINT: the swing is dropped with no hit, the attacker cannot swing for
        /// FeintRecoverySeconds, FeintRefund of the cooldown left comes back, and no second feint for FeintLockoutSeconds.
        /// During the recovery after a swing that LANDED (after the contact tick; never on a whiff) it is a HIT-CANCEL: the
        /// recovery left is cut by HitCancelShare and the cooldown by the same time. One cancel per attack. Feint and
        /// hit-cancel push SimEventType.Feint (Value 1 = feint, 2 = hit-cancel); the wind-up cue goes with SwingPending.
        /// Returns 1 for a feint, 2 for a hit-cancel, 0 when the press did nothing.
        /// </summary>
        public static int TakeCancel(World w, int i)
        {
            InputFrame inp = w.Inputs[i];
            if (!inp.Cancel) return 0;
            inp.Cancel = false; // read and cleared: a stored true never cancels twice
            CombatFeelConfig f = Cfg(w);
            if (!f.CancelEnabled) return 0;
            Actor a = w.Actors[i];
            if (!a.Alive || a.StandUp > 0) return 0;
            CombatFeelState s = w.Depth.Feel;
            if (s.CancelUsed[i] == a.SwingTick) return 0;
            int contact = a.SwingTick + a.SwingWindup;
            double hz = w.Config.SimHz;
            if (a.SwingPending && w.Tick < contact)
            {
                if (w.Tick < s.FeintLockUntil[i]) return 0;
                int rec = Ticks(w, f.FeintRecoverySeconds);
                a.SwingPending = false;
                a.SwingWindup = w.Tick - a.SwingTick;
                a.SwingRecovery = rec;
                double left = a.AttackCd * (1 - f.FeintRefund);
                a.AttackCd = left > rec / hz ? left : rec / hz;
                s.CancelUsed[i] = a.SwingTick;
                s.FeintLockUntil[i] = w.Tick + Ticks(w, f.FeintLockoutSeconds);
                s.BufferTick[i] = NoTick;
                s.Feints++;
                World.PushEvent(w, SimEventType.Feint, i, -1, w.X[i], w.Z[i], 1);
                MovesHooks.Feinted(w, i);   // mv-hook (lane 1's line in lane 2's file): FeintBaited
                return 1;
            }
            if (!a.SwingPending && s.LandedSwing[i] == a.SwingTick && w.Tick > contact)
            {
                int remaining = contact + a.SwingRecovery - w.Tick;
                if (remaining <= 0) return 0;
                int cut = (int)Math.Floor(remaining * f.HitCancelShare + 0.5);
                if (cut <= 0) return 0;
                a.SwingRecovery -= cut;
                double cd = a.AttackCd - cut / hz;
                a.AttackCd = cd > Combat.TimerEps ? cd : 0;
                s.CancelUsed[i] = a.SwingTick;
                s.HitCancels++;
                World.PushEvent(w, SimEventType.Feint, i, -1, w.X[i], w.Z[i], 2);
                return 2;
            }
            return 0;
        }

        /// <summary>b16 CANCEL (read by the view's input layer between ticks): would a cancel press do something for actor id
        /// now (a feint during its wind-up, or a hit-cancel in the recovery after a landed hit, its one use not spent)?</summary>
        public static bool CancelWindow(World w, int id)
        {
            if (w == null || id < 0 || id >= w.ActorCount || !On(w) || !Cfg(w).CancelEnabled) return false;
            Actor a = w.Actors[id];
            CombatFeelState s = w.Depth.Feel;
            if (!a.Alive || a.StandUp > 0 || s.CancelUsed[id] == a.SwingTick) return false;
            int contact = a.SwingTick + a.SwingWindup;
            if (a.SwingPending) return w.Tick < contact && w.Tick >= s.FeintLockUntil[id];
            return s.LandedSwing[id] == a.SwingTick && w.Tick > contact && w.Tick < contact + a.SwingRecovery;
        }

        /// <summary>b16 CANCEL: true while actor id is in the short fixed recovery after a feint (no swing, no dodge).</summary>
        public static bool FeintRecovering(World w, int id)
        {
            CombatFeelState s = w.Depth.Feel;
            Actor a = w.Actors[id];
            return s.CancelUsed[id] == a.SwingTick && !a.SwingPending && s.LandedSwing[id] != a.SwingTick
                && w.Tick < a.SwingTick + a.SwingWindup + a.SwingRecovery;
        }

        // ------------------------------------------------------------------------------------------------ movement

        /// <summary>
        /// Movement: the speed scale of a moving actor i this tick: wind-up, recovery, the post-stagger slow and the ladder.
        /// Replaces PhysicsConfig.SwingMoveMultiplier while the rule is on.
        /// </summary>
        public static double SpeedScale(World w, int i)
        {
            CombatFeelConfig f = Cfg(w);
            Actor a = w.Actors[i];
            double k = 1;
            int since = w.Tick - a.SwingTick;
            if (a.SwingWindup + a.SwingRecovery > 0 && since > 0)
            {
                if (since <= a.SwingWindup) k *= f.WindupMoveMultiplier;
                else if (since <= a.SwingWindup + a.SwingRecovery) k *= f.RecoveryMoveMultiplier;
            }
            if (w.Tick <= w.Depth.Feel.SlowUntil[i]) k *= f.StaggerSlowMultiplier;
            if (OnLadder(w, i)) k *= f.LadderSpeedMultiplier;
            // px S1 [5]: a wounded body (hit in the last WoundedSeconds) below WoundedSlowHpShare of its HP is slowed: a
            // fleeing target can be caught, sometimes (it still outruns a slow chaser once the window closes).
            if (f.WoundedSlowMultiplier != 1 && Wounded(w, i) && a.Hp < f.WoundedSlowHpShare * a.MaxHp) k *= f.WoundedSlowMultiplier;
            return k;
        }

        /// <summary>Movement, once per tick: the longest-stagger-run bookkeeping (no stun-lock proof) and the ladder mask.</summary>
        public static void BeginMovement(World w)
        {
            CombatFeelState s = w.Depth.Feel;
            int capTicks = Ticks(w, Cfg(w).StaggerChainCapSeconds);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                // b15 pass 4: the chain cap is a hard stop. Movement counts a stun down only on grounded ticks (an airborne body,
                // a dodge step or a fall keeps it), so a knock-up inside a run carried it to 25 ticks (0.83 s, run 44253108383)
                // although Knock.Push had clipped every stun to the room left. The stun lands after this step's movement, so the
                // run already holds LockRun + 1 stunned ticks here: at capTicks - 1 counted it has had capTicks and ends (0.53 s
                // measured with ">= capTicks", CombatFeelBalanceTests run 44837692193).
                if (a.Alive && a.HitStunTicks > 0 && capTicks > 0 && s.LockRun[i] >= capTicks - 1)
                {
                    // pass 4b: and the victim gets StaggerImmuneSeconds at once, or the next melee hit this same tick restarts the
                    // stun with a full room and the lock runs on across the reset (0.63 s measured, slow run 44841109806).
                    a.HitStunTicks = 0;
                    s.LockRun[i] = 0;
                    int until = w.Tick + Ticks(w, Cfg(w).StaggerImmuneSeconds);
                    if (until > s.ImmuneUntil[i]) s.ImmuneUntil[i] = until;
                }
                else if (a.Alive && a.HitStunTicks > 0)
                {
                    int r = ++s.LockRun[i];
                    if (r > s.MaxLockRun) s.MaxLockRun = r;
                }
                else s.LockRun[i] = 0;
            }
            RefreshLadders(w);
        }

        /// <summary>Rebuilds the ladder mask from the live pieces (once per tick; a few hundred writes at most).</summary>
        public static void RefreshLadders(World w)
        {
            CombatFeelState s = w.Depth.Feel;
            if (s.LadderTick == w.Tick) return;
            s.LadderTick = w.Tick;
            for (int k = 0; k < s.LadderCount; k++) s.Ladder[s.LadderCells[k]] = 0;
            s.LadderCount = 0;
            BuiltPiece[] pieces = w.Builder.Pieces;
            byte[] cells = w.Map.Cells;
            for (int p = 0; p < pieces.Length; p++)
            {
                BuiltPiece bp = pieces[p];
                if (!bp.Active || bp.Carried || bp.Kind != BuiltPieces.Ladder) continue;
                for (int k = 0; k < bp.Count; k++)
                {
                    int c = bp.Cells[k];
                    if (c < 0 || c >= cells.Length || cells[c] != Cell.Bridge || bp.Prev[k] != Cell.Wall || s.Ladder[c] != 0) continue;
                    s.Ladder[c] = 1;
                    s.LadderCells[s.LadderCount++] = c;
                }
            }
        }

        /// <summary>True when actor id stands on a ladder lane cell (as of this tick's mask).</summary>
        public static bool OnLadder(World w, int id)
        {
            CombatFeelState s = w.Depth.Feel;
            if (s.LadderCount == 0) return false;
            int cx = JsMath.FloorToInt(w.X[id]), cz = JsMath.FloorToInt(w.Z[id]);
            MapDef map = w.Map;
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return false;
            return s.Ladder[cz * map.Width + cx] != 0;
        }

        /// <summary>Collide: teammates do not push each other while either stands on a ladder lane (two-way traffic).</summary>
        public static bool AllyPasses(World w, int i, int j) => Cfg(w).LadderAllyPass && (OnLadder(w, i) || OnLadder(w, j));

        // ------------------------------------------------------------------------------------------------ Demolitions

        /// <summary>The TNT cooldown in sim ticks at this config's SimHz (through Durations.T30, like every *Ticks field).</summary>
        public static int TntCooldown(GameConfig c) => Durations.T30(c, On(c) ? c.Depth.CombatFeel.TntCooldownTicks : c.Abilities.DemolitionsTnt.CooldownTicks);

        /// <summary>
        /// Self-risk: the thrower inside her own TNT's radius takes TntSelfShare of the blast damage at her distance (full at the
        /// centre falling to BlastEdgeDamageShare at the edge, the shockwave rule), from no actor (nobody is credited).
        /// </summary>
        public static void TntSelfBlast(World w, int owner, double bx, double bz, double radius, double damage)
        {
            if (owner < 0 || owner >= w.ActorCount) return;
            Actor a = w.Actors[owner];
            if (!a.Alive || a.AirLen > 0) return;
            double dx = w.X[owner] - bx, dz = w.Z[owner] - bz;
            double d2 = dx * dx + dz * dz;
            if (!(d2 < radius * radius)) return;
            double share = 1;
            if (Phys.On(w, w.Config.Physics.BlastShockwave)) share = 1 - (1 - w.Config.Physics.BlastEdgeDamageShare) * (Math.Sqrt(d2) / radius);
            double amount = damage * share * Cfg(w).TntSelfShare;
            double dealt = Damage.ApplyDamage(w, owner, amount, -1, bx, bz, DamageCause.Explosion);
            if (dealt > 0)
            {
                w.Depth.Feel.TntSelfHits++;
                w.Depth.Feel.TntSelfDamage += dealt;
            }
        }

        // ------------------------------------------------------------------------------------------------ Healer

        /// <summary>The mend reach and heal in config c.</summary>
        public static double MendRange(GameConfig c) => On(c) ? c.Depth.CombatFeel.MendRange : c.Abilities.Healer.MendRange;

        public static double MendHeal(GameConfig c) => On(c) ? c.Depth.CombatFeel.MendHeal : c.Abilities.Healer.MendHeal;

        // ------------------------------------------------------------------------------------------------ bots

        /// <summary>BotSteer: how close a bot of class cls fights (its weapon reach; the Healer's stand-off while the rule is on).</summary>
        public static double BotReach(World w, ClassId cls, double attackRange) =>
            cls == ClassId.Healer && On(w) ? Cfg(w).HealerBotStandOff : attackRange;

        /// <summary>
        /// BotSteer: a narrow spot, where the blended flow steered bots into corners: a convex corner touches the cell (a
        /// non-walkable diagonal neighbour whose two orthogonal neighbours are walkable), or the cell is a one-cell corridor.
        /// </summary>
        public static bool Narrow(MapDef map, double x, double z)
        {
            int cx = JsMath.FloorToInt(x), cz = JsMath.FloorToInt(z);
            bool e = MapGrid.IsWalkable(map, cx + 1, cz), wv = MapGrid.IsWalkable(map, cx - 1, cz);
            bool n = MapGrid.IsWalkable(map, cx, cz + 1), s = MapGrid.IsWalkable(map, cx, cz - 1);
            if ((!e && !wv) || (!n && !s)) return true;
            if (e && n && !MapGrid.IsWalkable(map, cx + 1, cz + 1)) return true;
            if (e && s && !MapGrid.IsWalkable(map, cx + 1, cz - 1)) return true;
            if (wv && n && !MapGrid.IsWalkable(map, cx - 1, cz + 1)) return true;
            if (wv && s && !MapGrid.IsWalkable(map, cx - 1, cz - 1)) return true;
            return false;
        }

        /// <summary>BotFsm unstick: true when every one of the 8 neighbours of cell (cx, cz) is walkable (open ground).</summary>
        public static bool OpenCell(MapDef map, int cx, int cz)
        {
            for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    if (!MapGrid.IsWalkable(map, cx + dx, cz + dz)) return false;
            return true;
        }
    }
}
