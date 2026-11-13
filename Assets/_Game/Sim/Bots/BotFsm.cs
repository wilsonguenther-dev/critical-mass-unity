using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// Per-bot FSM with utility-scored transitions (CM-09, browser src/bots/fsm.ts). The chosen state is written to
    /// Actor.AiState. Each bot thinks at Bots.ThinkHz, staggered by id (BotBrain). Allocation-free.
    /// </summary>
    public static class BotFsm
    {
        public const double FightRange = 6, FightKeepRange = 8, UrgentRange = 3, KaijuEngageRange = 12, KaijuFightUtility = 1, CockyHillFactor = 0.3, HillUtility = 0.7, KaijuRoleHillUtility = 0.2, NearbyNodeDist = 20, UrgentFloorSeconds = 1, UnstickSeconds = 1, ProgressRadius = 1.0, ProgressSeconds = 2;

        private const double StuckDist2 = 0.3 * 0.3;

        /// <summary>CM-30 progress detector radius squared.</summary>
        private const double ProgressDist2 = ProgressRadius * ProgressRadius;

        /// <summary>CM-64: fight utility against a contestable enemy carrier; must beat hill + inertia (0.95) and gather (0.8 x greed).</summary>
        public const double InterceptUtility = 1;
        /// <summary>CM-64: a defender must itself be within this many InterceptShrineRadius of the shrine.</summary>
        public const double InterceptDefenderFactor = 2;
        /// <summary>CM-36: under deliver (0.9) so cargo goes in first, over any gather (0.8 x greed) and the hill (0.7).</summary>
        public const double CleanseUtility = 0.88;

        /// <summary>
        /// CM-64: the enemy carrier bot id should contest at the shrine, or -1: an alive, unprotected enemy holding cargo or
        /// meat within Bots.InterceptShrineRadius of the shrine centre, nearest the shrine first (lowest id on a tie). Bots
        /// carrying cargo never intercept, nor bots further than InterceptDefenderFactor x the radius from the shrine.
        /// </summary>
        public static int InterceptTarget(World w, int id)
        {
            Actor a = w.Actors[id];
            if (a.CarryAmount > 0) return -1;
            double sx = w.Map.Shrine.X;
            double sz = w.Map.Shrine.Z;
            double r = w.Config.Bots.InterceptShrineRadius;
            double r2 = r * r;
            double reach = r * InterceptDefenderFactor;
            double bx = w.X[id] - sx;
            double bz = w.Z[id] - sz;
            if (bx * bx + bz * bz > reach * reach) return -1;
            int best = -1;
            double bestD2 = double.PositiveInfinity;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (!t.Alive || t.Team == a.Team || t.SpawnProtect > 0) continue;
                if (t.CarryAmount <= 0 && t.CarryMeat <= 0) continue;
                if (PassiveTarget(w, j)) continue; // DG-16 passive window
                double dx = w.X[j] - sx;
                double dz = w.Z[j] - sz;
                double d2 = dx * dx + dz * dz;
                if (d2 > r2 || d2 >= bestD2) continue;
                best = j;
                bestD2 = d2;
            }
            return best;
        }

        /// <summary>
        /// DG-16 (Bots.PassiveSeconds, the guided first match): true while bots must leave target alone, that is target is the
        /// human slot and match time is still inside the passive window. Default 0 = never (every decision is unchanged).
        /// </summary>
        public static bool PassiveTarget(World w, int target)
        {
            return w.Actors[target].IsHuman && w.Match.Time < w.Config.Bots.PassiveSeconds;
        }

        /// <summary>CM-64: only a Kaiju-duty bot contests a carrier; the hill crew keeps the CM-09 rule.</summary>
        public static bool InterceptorRole(int role)
        {
            return role == BotsState.RoleKaiju;
        }

        /// <summary>CM-37: the CM-09 retreat threshold, kept in Heal.SeekHpShare (0.3).</summary>
        public static double LowHp(GameConfig c) => c.Heal.SeekHpShare;


        private static int[] Near = new int[0];

        /// <summary>Perception scratch, filled by Perceive().</summary>
        private sealed class Perception
        {
            public int Enemy = -1;
            public double EnemyD2 = double.PositiveInfinity;
            public int Within3;
            public int Within6;
            public int Within8;
            /// <summary>Centroid of enemies within 8 (for barricades).</summary>
            public double Cx;
            public double Cz;
            public bool KaijuHostile;
            public double KaijuD2 = double.PositiveInfinity;
        }

        private static readonly Perception P = new Perception();

        private static void EnsureNear(World w)
        {
            int cap = World.ActorCapacity(w.Config);
            if (Near.Length < cap) Near = new int[cap];
        }

        /// <summary>DG-21: perception with target choice. sk null or FocusCarriers off = nearest enemy (the browser rule).</summary>
        private static void Perceive(World w, BotsState st, int id, double fightRange, BotSkillConfig sk)
        {
            bool focus = sk != null && sk.FocusCarriers;
            double bestScore = double.PositiveInfinity;
            Actor a = w.Actors[id];
            double x = w.X[id];
            double z = w.Z[id];
            P.Enemy = -1;
            P.EnemyD2 = double.PositiveInfinity;
            P.Within3 = 0;
            P.Within6 = 0;
            P.Within8 = 0;
            P.Cx = 0;
            P.Cz = 0;
            EnsureNear(w);
            int[] near = Near;
            int cnt = SpatialHash.QueryCircle(w.Hash, x, z, FightKeepRange, near);
            double fr2 = fightRange * fightRange;
            for (int k = 0; k < cnt; k++)
            {
                int j = near[k];
                if (j >= w.ActorCount) continue;
                Actor t = w.Actors[j];
                if (!t.Alive || t.Team == a.Team || t.SpawnProtect > 0) continue;
                if (PassiveTarget(w, j)) continue; // DG-16 passive window: the human is not an enemy yet
                double dx = w.X[j] - x;
                double dz = w.Z[j] - z;
                double d2 = dx * dx + dz * dz;
                if (d2 > FightKeepRange * FightKeepRange) continue;
                P.Within8++;
                P.Cx += w.X[j];
                P.Cz += w.Z[j];
                if (d2 <= FightRange * FightRange) P.Within6++;
                if (d2 <= UrgentRange * UrgentRange) P.Within3++;
                if (d2 > fr2) continue;
                if (focus)
                {
                    // DG-21 Hard: carriers and hurt enemies first (BotSkill.FocusScore), lowest id on a tie.
                    double sc = BotSkill.FocusScore(w, sk, j, d2);
                    if (sc < bestScore || (sc == bestScore && j < P.Enemy))
                    {
                        bestScore = sc;
                        P.Enemy = j;
                        P.EnemyD2 = d2;
                    }
                }
                else if (d2 < P.EnemyD2 || (d2 == P.EnemyD2 && j < P.Enemy))
                {
                    P.Enemy = j;
                    P.EnemyD2 = d2;
                }
            }
            if (P.Within8 > 0)
            {
                P.Cx /= P.Within8;
                P.Cz /= P.Within8;
            }
            Kaiju kj = w.Kaiju;
            bool hostile = kj.State == KaijuState.Berserk
                || (kj.State != KaijuState.Neutral && kj.State != KaijuState.Egg && kj.State != SimEnumMap.ToKaijuState(a.Team));
            P.KaijuHostile = hostile && kj.KoIn <= 0;
            double kx = kj.X - x;
            double kz = kj.Z - z;
            P.KaijuD2 = kx * kx + kz * kz;
        }

        /// <summary>Argmax scratch (no closure per think).</summary>
        private sealed class Selection
        {
            public AiState Cur = AiState.None;
            public AiState Best = AiState.None;
            public double U;
        }

        private static readonly Selection Sel = new Selection();

        private static void Consider(World w, AiState s, double u)
        {
            if (u <= 0) return;
            double v = u + (s == Sel.Cur ? w.Config.Bots.InertiaBonus : 0);
            if (v > Sel.U)
            {
                Sel.U = v;
                Sel.Best = s;
            }
        }

        private static double Cap1(double v)
        {
            return v > 1 ? 1 : v;
        }

        /// <summary>Best node of kind with charges for bot id by field distance (greedy: nearest). Returns node id or -1.</summary>
        private static int BestNode(World w, BotsState st, int id, NodeKind kind, double maxDist)
        {
            BotFields f = st.Fields;
            double x = w.X[id];
            double z = w.Z[id];
            int best = -1;
            double bestD = maxDist;
            for (int i = 0; i < w.Nodes.Length; i++)
            {
                ResourceNode node = w.Nodes[i];
                if (node.Kind != kind || node.Charges <= 0) continue;
                int fi = f.FieldOfNode[i];
                if (fi < 0) continue;
                double d = BotFields.FieldDistAt(f, fi, x, z);
                if (kind == NodeKind.Corruption)
                {
                    // The combined field gives the distance to the nearest corruption node; rank the others by straight line.
                    double dx = node.X - x;
                    double dz = node.Z - z;
                    d = Math.Max(d, Math.Sqrt(dx * dx + dz * dz));
                }
                if (d < bestD)
                {
                    bestD = d;
                    best = i;
                }
            }
            return best;
        }

        private static void SetState(World w, BotsState st, int id, AiState s, double now)
        {
            Actor a = w.Actors[id];
            if (a.AiState == s) return;
            a.AiState = s;
            st.StateSince[id] = now;
            st.StateChanges++;
        }

        private static bool IsHillState(AiState s)
        {
            return s == AiState.ToHill || s == AiState.HoldHill;
        }

        private static void PickPost(World w, BotsState st, int id, double now)
        {
            HillDef hill = w.Map.Hill;
            double ang = Rng.NextFloat(st.Rng) * Math.PI * 2;
            double r = Math.Sqrt(Rng.NextFloat(st.Rng)) * BotFields.HoldRadius(w.Config);
            st.PostX[id] = JsMath.F32(hill.X + JsMath.Cos(ang) * r);
            st.PostZ[id] = JsMath.F32(hill.Z + JsMath.Sin(ang) * r);
            st.PostUntil[id] = now + 2 + 2 * Rng.NextFloat(st.Rng);
        }

        private static void StartUnstick(World w, BotsState st, int id, double now)
        {
            double x = w.X[id];
            double z = w.Z[id];
            int ccx = JsMath.FloorToInt(x);
            int ccz = JsMath.FloorToInt(z);
            double tx = x;
            double tz = z;
            for (int tries = 0; tries < 12; tries++)
            {
                double ang = Rng.NextFloat(st.Rng) * Math.PI * 2;
                double r = 2 + Rng.NextFloat(st.Rng);
                int cx = JsMath.FloorToInt(x + JsMath.Cos(ang) * r);
                int cz = JsMath.FloorToInt(z + JsMath.Sin(ang) * r);
                if ((cx == ccx && cz == ccz) || !MapGrid.IsWalkable(w.Map, cx, cz)) continue;
                tx = cx + 0.5;
                tz = cz + 0.5;
                break;
            }
            st.UnstickX[id] = JsMath.F32(tx);
            st.UnstickZ[id] = JsMath.F32(tz);
            st.UnstickUntil[id] = now + UnstickSeconds;
            st.AnchorX[id] = JsMath.F32(x);
            st.AnchorZ[id] = JsMath.F32(z);
            st.AnchorTime[id] = now;
            st.ProgressX[id] = JsMath.F32(x);
            st.ProgressZ[id] = JsMath.F32(z);
            st.ProgressTime[id] = now;
            SetState(w, st, id, AiState.Unstick, now);
        }

        /// <summary>Ability decisions (sent by the next FillBotInput as one press).</summary>
        private static void DecideAbility(World w, BotsState st, int id, AiState state, BotSkillConfig sk)
        {
            Actor a = w.Actors[id];
            st.AbilityReq[id] = 0;
            if (a.Cls == ClassId.Demolitions)
            {
                DecideDemolitions(w, st, id, state, sk); // DG-22 (CM-38)
                return;
            }
            if (a.Cls == ClassId.Healer)
            {
                DecideHealer(w, st, id, sk); // DG-40 (U-061)
                return;
            }
            if (a.AbilityCd > 0) return;
            if (a.Cls == ClassId.Builder)
            {
                DecideBuilder(w, st, id, state, sk); // DG-24 (U-060)
                return;
            }
            double x = w.X[id];
            double z = w.Z[id];
            if (a.Cls == ClassId.Warrior)
            {
                if (state == AiState.Fight && a.AbilityActive <= 0 && (P.Within3 >= (sk == null ? 2 : sk.ShieldCrowd) || a.Hp < 0.5 * a.MaxHp) && P.Enemy >= 0)
                {
                    st.AbilityReq[id] = 1;
                    st.AbilityX[id] = JsMath.F32(w.X[P.Enemy]);
                    st.AbilityZ[id] = JsMath.F32(w.Z[P.Enemy]);
                }
                return;
            }
            if (a.Cls == ClassId.Farmer)
            {
                if (P.Within6 < 2) return;
                // Densest enemy cluster within cast range: the enemy with the most enemies inside the root radius around it.
                FarmerRootsConfig R = w.Config.Abilities.FarmerRoots;
                EnsureNear(w);
                int[] near = Near;
                int cnt = SpatialHash.QueryCircle(w.Hash, x, z, R.CastRange, near);
                int best = -1;
                int bestN = 1;
                double rr = R.Radius * R.Radius;
                for (int p = 0; p < cnt; p++)
                {
                    int i = near[p];
                    if (i >= w.ActorCount) continue;
                    Actor ti = w.Actors[i];
                    if (!ti.Alive || ti.Team == a.Team) continue;
                    int c = 0;
                    for (int q = 0; q < cnt; q++)
                    {
                        int j = near[q];
                        if (j >= w.ActorCount) continue;
                        Actor tj = w.Actors[j];
                        if (!tj.Alive || tj.Team == a.Team) continue;
                        double ddx = w.X[j] - w.X[i];
                        double ddz = w.Z[j] - w.Z[i];
                        if (ddx * ddx + ddz * ddz <= rr) c++;
                    }
                    if (c > bestN)
                    {
                        bestN = c;
                        best = i;
                    }
                }
                if (best >= 0)
                {
                    st.AbilityReq[id] = 1;
                    st.AbilityX[id] = JsMath.F32(w.X[best]);
                    st.AbilityZ[id] = JsMath.F32(w.Z[best]);
                    // DG-21 aim error: an Easy farmer's roots land up to AimErrorUnits off (no change for Normal).
                    double jx = st.AbilityX[id];
                    double jz = st.AbilityZ[id];
                    BotSkill.Jitter(w, id, sk, ref jx, ref jz);
                    st.AbilityX[id] = jx;
                    st.AbilityZ[id] = jz;
                }
                return;
            }
            // Miner barricade: only while holding the hill with ge 2 enemies approaching within 8, on the cell 2 ahead of
            // the miner toward them; the sim's own rule (CanPlaceBarricade) decides, so a press never produces a rejected cast.
            if (state != AiState.HoldHill || P.Within8 < 2) return;
            double dx = P.Cx - x;
            double dz = P.Cz - z;
            double len = Math.Sqrt(dx * dx + dz * dz);
            if (len < 1e-6) return;
            dx /= len;
            dz /= len;
            double tx = JsMath.FloorToInt(x + dx * 2) + 0.5;
            double tz = JsMath.FloorToInt(z + dz * 2) + 0.5;
            if (!Barricades.CanPlaceBarricade(w, id, tx, tz)) return;
            st.AbilityReq[id] = 1;
            st.AbilityX[id] = JsMath.F32(tx);
            st.AbilityZ[id] = JsMath.F32(tz);
        }

        /// <summary>
        /// DG-22 (CM-38) Demolitions Expert (no melee: its attack press does nothing). TNT first (its own cooldown,
        /// Demolitions.TntWaitTicks): at the chosen enemy when it is within DemolitionsTnt.ThrowRange, else at the centroid of
        /// the enemies within 8 when that is in range; queued with Demolitions.RequestBotTnt and pressed by FillBotInput as one
        /// press. Else a land mine (Space) on the bot's own cell while holding the hill, or while fighting / heading to it with an
        /// enemy within 8; Demolitions.CanPlaceMine is asked first, so a press never produces a refused cast. One press per think.
        /// DG-21: Hard leads a moving target by TntLeadFraction x fuse time; Easy throws AimErrorUnits off and only on
        /// AbilityChance of its thinks (Normal is the browser rule).
        /// </summary>
        private static void DecideDemolitions(World w, BotsState st, int id, AiState state, BotSkillConfig sk)
        {
            double x = w.X[id];
            double z = w.Z[id];
            if (P.Within8 > 0 && Demolitions.TntWaitTicks(w, id) == 0 && BotSkill.UsesAbilityNow(w, id, sk))
            {
                DemolitionsTntConfig T = w.Config.Abilities.DemolitionsTnt;
                double tr2 = T.ThrowRange * T.ThrowRange;
                double tx = double.NaN;
                double tz = double.NaN;
                if (P.Enemy >= 0 && P.EnemyD2 <= tr2)
                {
                    tx = w.X[P.Enemy];
                    tz = w.Z[P.Enemy];
                    if (sk != null && sk.TntLeadFraction > 0)
                    {
                        double lead = sk.TntLeadFraction * T.FuseTicks / w.Config.SimHz;
                        tx += w.Vx[P.Enemy] * lead;
                        tz += w.Vz[P.Enemy] * lead;
                    }
                }
                else
                {
                    double dx = P.Cx - x;
                    double dz = P.Cz - z;
                    if (dx * dx + dz * dz <= tr2)
                    {
                        tx = P.Cx;
                        tz = P.Cz;
                    }
                }
                if (double.IsFinite(tx))
                {
                    BotSkill.Jitter(w, id, sk, ref tx, ref tz);
                    Demolitions.RequestBotTnt(w, id, tx, tz);
                    return;
                }
            }
            if (state != AiState.HoldHill && !((state == AiState.Fight || state == AiState.ToHill) && P.Within8 > 0)) return;
            if (!Demolitions.CanPlaceMine(w, id, x, z)) return;
            st.AbilityReq[id] = 1;
            st.AbilityX[id] = JsMath.F32(x);
            st.AbilityZ[id] = JsMath.F32(z);
        }

        /// <summary>DG-24: a Builder bot builds only when the piece cuts at least this many cells off its walk to its goal.</summary>
        public const double MinBuildGainCells = 2;

        /// <summary>
        /// DG-24 (U-060, not in the browser) Builder bot: on its way to the hill (ToHill, Unstick) or the shrine (Deliver,
        /// Corrupt), it tries the four directions; a build the sim would accept (BuiltPieces.BuildRefusal: limit, bank, gap,
        /// plan) whose landing is reachable and closer to the goal by MinBuildGainCells or more (team flow-field distance at
        /// the landing + the cells to it, against the distance where it stands; a bot cut off from its goal takes any landing
        /// that reaches it) is pressed toward the best one. Bots never carry pieces. DG-21: Easy builds only on AbilityChance of
        /// its thinks; Normal and Hard always.
        /// </summary>
        private static void DecideBuilder(World w, BotsState st, int id, AiState state, BotSkillConfig sk)
        {
            int field;
            if (state == AiState.ToHill || state == AiState.Unstick) field = BotFields.FieldHill;
            else if (state == AiState.Deliver || state == AiState.Corrupt) field = BotFields.FieldShrine;
            else return;
            if (BuiltPieces.OwnedCount(w, id) >= w.Config.Abilities.BuilderPieces.MaxActivePerBuilder) return;
            double x = w.X[id];
            double z = w.Z[id];
            double d0 = BotFields.FieldDistAt(st.Fields, field, x, z);
            double bestGain = MinBuildGainCells;
            int bdx = 0;
            int bdz = 0;
            for (int k = 0; k < 4; k++)
            {
                int dx = k == 0 ? 1 : k == 1 ? -1 : 0;
                int dz = k == 2 ? 1 : k == 3 ? -1 : 0;
                if (BuiltPieces.BuildRefusal(w, id, dx, dz) != 0) continue;
                BuiltPieces.LastPlanLanding(w, id, dx, dz, out double lx, out double lz, out int cells);
                double d1 = BotFields.FieldDistAt(st.Fields, field, lx, lz);
                if (!double.IsFinite(d1)) continue;
                double gain = double.IsFinite(d0) ? d0 - (d1 + cells) : 1e9;
                if (gain >= bestGain && (bdx != 0 || bdz != 0 ? gain > bestGain : true))
                {
                    bestGain = gain;
                    bdx = dx;
                    bdz = dz;
                }
            }
            if (bdx == 0 && bdz == 0) return;
            if (!BotSkill.UsesAbilityNow(w, id, sk)) return;
            st.AbilityReq[id] = 1;
            st.AbilityX[id] = JsMath.F32(x + bdx * 1.5);
            st.AbilityZ[id] = JsMath.F32(z + bdz * 1.5);
        }

        /// <summary>
        /// DG-40 (U-061, not in the browser) Healer bot. Her attack presses come from the usual steering (she closes to her
        /// AttackRange of the enemy and presses attack: the sim mends the hurt ally nearest that enemy). PURIFY when the sim
        /// would accept it (Healer.CanPurify: at the shrine, egg open, corruption in it) and her team is not behind in
        /// allegiance or instability is at least half the berserk threshold. HEALING PULSE (Space) centred on the hurt ally in
        /// cast range (or herself) whose PulseRadius holds the most missing HP (each ally counted up to PulseHeal), cast when
        /// that is at least PulseHeal or one ally there is under a third of its max HP. DG-21: Easy acts only on AbilityChance
        /// of its thinks.
        /// </summary>
        private static void DecideHealer(World w, BotsState st, int id, BotSkillConfig sk)
        {
            Actor a = w.Actors[id];
            if (Healer.CanPurify(w, id))
            {
                MatchState m = w.Match;
                double mine = a.Team == TeamId.Azure ? m.Allegiance.Azure : m.Allegiance.Crimson;
                double theirs = a.Team == TeamId.Azure ? m.Allegiance.Crimson : m.Allegiance.Azure;
                if ((mine >= theirs || m.Instability >= 0.5 * w.Config.KaijuFeed.BerserkThreshold) && BotSkill.UsesAbilityNow(w, id, sk))
                {
                    Healer.RequestBotPurify(w, id);
                    return;
                }
            }
            if (a.AbilityCd > 0) return;
            HealerConfig H = w.Config.Abilities.Healer;
            double hx = w.X[id];
            double hz = w.Z[id];
            double cast2 = H.PulseCastRange * H.PulseCastRange;
            double r2 = H.PulseRadius * H.PulseRadius;
            int best = -1;
            double bestSum = 0;
            bool urgent = false;
            for (int c = 0; c < w.ActorCount; c++)
            {
                Actor ca = w.Actors[c];
                if (!ca.Alive || ca.Team != a.Team || !(ca.Hp < ca.MaxHp)) continue;
                double cdx = w.X[c] - hx;
                double cdz = w.Z[c] - hz;
                if (cdx * cdx + cdz * cdz > cast2) continue;
                double sum = 0;
                bool low = false;
                for (int j = 0; j < w.ActorCount; j++)
                {
                    Actor t = w.Actors[j];
                    if (!t.Alive || t.Team != a.Team) continue;
                    double dx = w.X[j] - w.X[c];
                    double dz = w.Z[j] - w.Z[c];
                    if (dx * dx + dz * dz > r2) continue;
                    double miss = t.MaxHp - t.Hp;
                    sum += miss < H.PulseHeal ? miss : H.PulseHeal;
                    if (t.Hp < t.MaxHp / 3) low = true;
                }
                if (sum > bestSum)
                {
                    bestSum = sum;
                    best = c;
                    urgent = low;
                }
            }
            if (best < 0 || (bestSum < H.PulseHeal && !urgent)) return;
            if (!BotSkill.UsesAbilityNow(w, id, sk)) return;
            st.AbilityReq[id] = 1;
            st.AbilityX[id] = JsMath.F32(w.X[best]);
            st.AbilityZ[id] = JsMath.F32(w.Z[best]);
        }

        /// <summary>
        /// U-063: the allied carrier bot id should escort, or -1: a living feeder of its team holding cargo or meat within
        /// Roles.EscortSeekRange (straight line), nearest first, lowest id on a tie.
        /// </summary>
        public static int EscortTarget(World w, int id)
        {
            Actor a = w.Actors[id];
            double r = w.Config.Depth.Roles.EscortSeekRange;
            double bestD2 = r * r;
            int best = -1;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == id) continue;
                Actor t = w.Actors[j];
                if (!t.Alive || t.Team != a.Team || (t.CarryAmount <= 0 && t.CarryMeat <= 0) || !Roles.MayFeed(w, t.Cls)) continue;
                double dx = w.X[j] - w.X[id];
                double dz = w.Z[j] - w.Z[id];
                double d2 = dx * dx + dz * dz;
                if (d2 >= bestD2) continue;
                best = j;
                bestD2 = d2;
            }
            return best;
        }

        /// <summary>One think for bot id: perception, stuck check, utility selection, targets, ability decision.</summary>
        public static void ThinkBot(World w, BotsState st, int id)
        {
            Actor a = w.Actors[id];
            if (!a.Alive) return;
            BotsConfig B = w.Config.Bots;
            double lowHp = LowHp(w.Config);
            double now = w.Match.Time;
            double x = w.X[id];
            double z = w.Z[id];
            AiState cur = a.AiState;
            if (cur == AiState.None)
            {
                st.StateSince[id] = double.NegativeInfinity;
                st.AnchorX[id] = JsMath.F32(x);
                st.AnchorZ[id] = JsMath.F32(z);
                st.AnchorTime[id] = now;
                st.ProgressX[id] = JsMath.F32(x);
                st.ProgressZ[id] = JsMath.F32(z);
                st.ProgressTime[id] = now;
            }

            // Unstick runs its full second.
            if (cur == AiState.Unstick && now < st.UnstickUntil[id]) return;

            // Stuck detector.
            double sdx = x - st.AnchorX[id];
            double sdz = z - st.AnchorZ[id];
            if (sdx * sdx + sdz * sdz >= StuckDist2 || st.WantMove[id] == 0)
            {
                st.AnchorX[id] = JsMath.F32(x);
                st.AnchorZ[id] = JsMath.F32(z);
                st.AnchorTime[id] = now;
            }
            else if (now - st.AnchorTime[id] >= B.StuckSeconds && now - st.StateSince[id] >= UrgentFloorSeconds)
            {
                StartUnstick(w, st, id, now);
                return;
            }

            // Progress detector (CM-30): travelling but never leaving a ProgressRadius circle.
            double progDx = x - st.ProgressX[id];
            double progDz = z - st.ProgressZ[id];
            bool travelling = cur == AiState.ToHill || cur == AiState.Deliver || cur == AiState.Retreat || cur == AiState.Supply;
            if (!travelling || st.WantMove[id] == 0 || progDx * progDx + progDz * progDz >= ProgressDist2)
            {
                st.ProgressX[id] = JsMath.F32(x);
                st.ProgressZ[id] = JsMath.F32(z);
                st.ProgressTime[id] = now;
            }
            else if (now - st.ProgressTime[id] >= ProgressSeconds && now - st.StateSince[id] >= UrgentFloorSeconds)
            {
                StartUnstick(w, st, id, now);
                return;
            }

            bool fighting = cur == AiState.Fight;
            bool cocky = BotDirector.IsCocky(w, a.Team);
            BotSkillConfig sk = BotSkill.Profile(w, id); // DG-21: null = Normal (the browser bots)
            Perceive(w, st, id, fighting || cocky ? FightKeepRange : FightRange, sk);
            int role = st.Role[id];
            ClassConfig cc = w.Config.Class(a.Cls);
            double hpFrac = a.MaxHp > 0 ? a.Hp / a.MaxHp : 0;
            bool egg = w.Kaiju.State == KaijuState.Egg && !w.Match.Hatched;
            HillDef hill = w.Map.Hill;
            double hdx = x - hill.X;
            double hdz = z - hill.Z;
            double hillD = Math.Sqrt(hdx * hdx + hdz * hdz);

            // --- utilities ---
            double uFight = 0;
            int fightTarget = -1;
            if (role == BotsState.RoleFightKaiju && P.KaijuHostile && P.KaijuD2 <= KaijuEngageRange * KaijuEngageRange)
            {
                // CM-19: 1.0 above LowHp (0.95 x hp never beat holdHill 0.7 + 0.25 inertia under strict gt).
                uFight = hpFrac >= lowHp ? KaijuFightUtility : 0;
                fightTarget = BotsState.TargetKaiju;
            }
            else if (P.Enemy >= 0)
            {
                uFight = Cap1(hpFrac * (role == BotsState.RoleHill ? 1 : 0.6) * st.Aggression[id]);
                fightTarget = P.Enemy;
                if (cur == AiState.HoldHill && !cocky)
                {
                    double ex = w.X[P.Enemy] - hill.X;
                    double ez = w.Z[P.Enemy] - hill.Z;
                    if (ex * ex + ez * ez > (hill.R + 1) * (hill.R + 1)) uFight *= sk == null ? 0.5 : sk.OffHillChaseFactor; // DG-21 discipline
                }
            }

            // CM-64: contest an enemy carrier at the shrine before it hands its load in (Delivery wind-up). Only Kaiju-duty bots.
            if (egg && InterceptorRole(role) && hpFrac >= lowHp && uFight < InterceptUtility)
            {
                int carrier = InterceptTarget(w, id);
                if (carrier >= 0)
                {
                    uFight = InterceptUtility;
                    fightTarget = carrier;
                }
            }

            double uHill = role == BotsState.RoleKaiju ? KaijuRoleHillUtility : cocky ? HillUtility * CockyHillFactor : HillUtility;
            bool holding = cur == AiState.HoldHill ? hillD < hill.R + 2 : hillD < hill.R - 1;
            AiState hillState = holding ? AiState.HoldHill : AiState.ToHill;

            double uGather = 0;
            int gatherNode = -1;
            if (role == BotsState.RoleKaiju && st.Corrupt[id] == 0 && egg && a.CarryAmount < cc.CarryCapacity && a.Carry != CarryKind.Corruption && Roles.MayFeed(w, a.Cls))
            {
                gatherNode = BestNode(w, st, id, NodeKind.Biomass, double.PositiveInfinity);
                if (gatherNode >= 0) uGather = Cap1(0.8 * st.Greed[id]);
            }

            double uDeliver = 0;
            if (a.CarryAmount > 0 && egg && Roles.MayDeliver(w, a, a.Carry))
            {
                NodeKind kind = a.Carry == CarryKind.Corruption ? NodeKind.Corruption : NodeKind.Biomass;
                if (a.CarryAmount >= cc.CarryCapacity || BestNode(w, st, id, kind, NearbyNodeDist) < 0) uDeliver = 0.9;
            }

            double uCorrupt = 0;
            int corruptNode = -1;
            if (st.Corrupt[id] != 0 && egg && a.Carry != CarryKind.Biomass && a.CarryAmount < cc.CarryCapacity && Roles.MayCorrupt(w, a.Cls))
            {
                corruptNode = BestNode(w, st, id, NodeKind.Corruption, double.PositiveInfinity);
                if (corruptNode >= 0 && BotFields.FieldDistAt(st.Fields, BotFields.FieldCorruption, x, z) < double.PositiveInfinity)
                    uCorrupt = Cap1(0.85 * st.Greed[id]);
            }

            // U-063 class roles: an egg-duty bot that may not feed (and is not on a corruption run) escorts the nearest allied carrier.
            double uEscort = 0;
            int escortOf = -1;
            if (role == BotsState.RoleKaiju && egg && st.Corrupt[id] == 0 && a.CarryAmount == 0 && hpFrac >= lowHp && Roles.On(w) && !Roles.MayFeed(w, a.Cls))
            {
                escortOf = EscortTarget(w, id);
                if (escortOf >= 0) uEscort = w.Config.Depth.Roles.EscortUtility;
            }

            // U-064 supply duty: fill up at the nearest grove or spring, then bank it at the base. A bot left holding wood or
            // water (the duty moved on) still walks it home.
            double uSupply = 0;
            int supplySource = -1;
            bool holdsSupply = a.CarryAmount > 0 && (a.Carry == CarryKind.Wood || a.Carry == CarryKind.Water);
            if (Sources.On(w) && ((st.Supply[id] != 0 && egg && a.CarryAmount == 0) || holdsSupply))
            {
                if (st.Supply[id] != 0 && egg && a.CarryAmount < cc.CarryCapacity)
                {
                    // Upgrades need water too (U-065): with three wood banked per water (plus one), fetch water when a spring has some.
                    bool wantWater = Sources.Stock(w, a.Team, Sources.StockWood) >= 3 * (Sources.Stock(w, a.Team, Sources.StockWater) + 1);
                    if (wantWater) supplySource = Sources.BestSource(w, id, true);
                    if (supplySource < 0) supplySource = Sources.BestSource(w, id);
                }
                if (supplySource >= 0 || holdsSupply) uSupply = w.Config.Depth.Sources.SupplyUtility;
            }

            // U-066: a power-up in sight and close: go for it (not while holding the hill, carrying, or hurt).
            double uGrab = 0;
            double grabX = 0;
            double grabZ = 0;
            if (Pickups.On(w) && cur != AiState.HoldHill && a.CarryAmount == 0 && hpFrac >= lowHp && Pickups.BotGrabSpot(w, id, out grabX, out grabZ))
                uGrab = w.Config.Depth.Pickups.BotGrabUtility;

            // CM-36: the shrine cleanse is the player's tool. Only the human slot on autopilot (IsHuman) uses it, and only for the
            // team that leads delivered biomass, once BotNetUnits net corruption units sit in the egg. Pure bots never cleanse
            // (D-060 retune). Reads delivery counts and cleanse timers only, never instability.
            double uCleanse = 0;
            if (a.IsHuman && st.Corrupt[id] == 0 && egg && a.CarryAmount == 0)
            {
                int teamIdx = a.Team == TeamId.Azure ? 0 : 1;
                double own = w.Match.Delivered[teamIdx].Biomass;
                double other = w.Match.Delivered[1 - teamIdx].Biomass;
                if (own > other && Cleanse.NetCorruptionUnits(w) >= w.Config.KaijuFeed.Cleanse.BotNetUnits && Cleanse.CleanseReady(w, teamIdx))
                    uCleanse = CleanseUtility;
            }

            double uRetreat = hpFrac < lowHp && (sk == null ? P.Within6 : WithinRange(sk.RetreatRange)) > 0 ? 0.9 : 0;

            // --- current state's own utility (0 = its task is over) ---
            double curU;
            switch (cur)
            {
                case AiState.Fight:
                    curU = uFight;
                    break;
                case AiState.ToHill:
                case AiState.HoldHill:
                    curU = uHill;
                    break;
                case AiState.Gather:
                    curU = uGather;
                    break;
                case AiState.Deliver:
                    curU = uDeliver;
                    break;
                case AiState.Corrupt:
                    curU = uCorrupt;
                    break;
                case AiState.Cleanse:
                    curU = uCleanse;
                    break;
                case AiState.Escort:
                    curU = uEscort;
                    break;
                case AiState.Supply:
                    curU = uSupply;
                    break;
                case AiState.Grab:
                    curU = uGrab;
                    break;
                case AiState.Retreat:
                    curU = uRetreat;
                    break;
                default:
                    curU = 0;
                    break;
            }

            // --- argmax with inertia ---
            Sel.Cur = cur;
            Sel.Best = hillState;
            Sel.U = uHill + (IsHillState(cur) ? B.InertiaBonus : 0);
            Consider(w, AiState.Fight, uFight);
            Consider(w, AiState.Gather, uGather);
            Consider(w, AiState.Deliver, uDeliver);
            Consider(w, AiState.Corrupt, uCorrupt);
            Consider(w, AiState.Cleanse, uCleanse);
            Consider(w, AiState.Escort, uEscort);
            Consider(w, AiState.Supply, uSupply);
            Consider(w, AiState.Grab, uGrab);
            Consider(w, AiState.Retreat, uRetreat);
            AiState best = Sel.Best;

            // --- switch rule ---
            AiState next = cur;
            if (best != cur)
            {
                double elapsed = now - st.StateSince[id];
                bool urgent = hpFrac < lowHp || P.Within3 > 0;
                bool free = elapsed >= B.MinStateSeconds;
                bool early = elapsed >= UrgentFloorSeconds && (urgent || curU <= 0 || (IsHillState(cur) && IsHillState(best)));
                if (cur == AiState.None || cur == AiState.Unstick || free || early) next = best;
            }
            SetState(w, st, id, next, now);

            // --- targets ---
            st.Target[id] = next == AiState.Fight ? fightTarget : -1;
            st.EscortOf[id] = next == AiState.Escort ? escortOf : -1;
            st.SupplySource[id] = next == AiState.Supply ? supplySource : -1;
            if (next == AiState.Grab)
            {
                st.GrabX[id] = grabX;
                st.GrabZ[id] = grabZ;
            }
            if (next == AiState.Gather) st.TargetNode[id] = gatherNode;
            else if (next == AiState.Corrupt) st.TargetNode[id] = corruptNode;
            else st.TargetNode[id] = -1;
            if (next == AiState.HoldHill)
            {
                double pdx = st.PostX[id] - x;
                double pdz = st.PostZ[id] - z;
                if (now >= st.PostUntil[id] || (pdx * pdx + pdz * pdz < 0.36 && now + 0.5 >= st.PostUntil[id])) PickPost(w, st, id, now);
            }
            else if (next == AiState.ToHill)
            {
                st.PostUntil[id] = 0;
            }

            DecideAbility(w, st, id, next, sk);
            // DG-21 ability use: an Easy bot sends a decided press only on AbilityChance of its thinks (Normal always).
            if (st.AbilityReq[id] == 1 && !BotSkill.UsesAbilityNow(w, id, sk)) st.AbilityReq[id] = 0;
        }

        /// <summary>DG-21: enemies counted by the last Perceive within r (3, 6 or 8; ranges between round up).</summary>
        private static int WithinRange(double r)
        {
            if (r <= UrgentRange) return P.Within3;
            if (r <= FightRange) return P.Within6;
            return P.Within8;
        }
    }
}
