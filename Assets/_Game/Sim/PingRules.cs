using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>U-067: the four calls of the ping wheel, in the HUD's wheel order (Runtime PingKind has the same values).</summary>
    public enum PingCall { Attack = 0, OnMyWay = 1, Defend = 2, Help = 3 }

    /// <summary>
    /// U-067 pings the bots answer (DG-21's unbuilt half; our rule, written to the spec the HUD lane left in
    /// Runtime/Hud/Interact/Pings.cs). The browser has no pings.
    /// <para>A PING is an input: InputFrame.PingKind = (int)PingCall + 1 with PingX / PingZ, set for ONE tick. The sim accepts
    /// at most RateCount pings per actor in any RateWindowTicks (Ping event) and refuses the rest (PingRefused).</para>
    /// <para>WHO ANSWERS: living bots of the pinger's team within AnswerRange of the ping, nearest first (a bot's play style
    /// shortens the distance for the calls it likes), at most MaxAttack / MaxDefend / MaxHelp, none for On my way. Never a
    /// bot that carries anything, already follows an order, is in the air, or holds the hill with fewer than HillMinMates
    /// teammates on it; a class with no weapon never answers Attack or Help. Each answer pushes PingAnswered.</para>
    /// <para>WHAT THEY DO. ATTACK: fight the nearest enemy within AttackSeekRange of the ping, else walk to the ping and fight
    /// on arrival; over when the spot is clear or after AttackTicks. DEFEND: walk within DefendHoldRange of the ping and hold
    /// DefendTicks, fighting what comes close. HELP: go to the pinger and fight its last attacker for HelpTicks. A hurt bot
    /// (below the retreat line) or one that picked cargo up drops the order.</para>
    /// <para>BOTS CALL TOO: a bot under BotHelpHpFraction with a living enemy on it calls Help; a bot fighting an enemy
    /// carrier before the hatch calls Attack on it. One call per bot per BotPingCooldownTicks, one per team per
    /// BotTeamPingGapTicks; a bot's call recruits at most BotPingResponders.</para>
    /// Deterministic (actor-id order, no RNG); allocation-free after each route slot's first build.
    /// </summary>
    public static class PingRules
    {
        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.Pings.Enabled);

        private static readonly PointXZ[] Goal = new PointXZ[1];

        public static bool HasOrder(World w, int id) => w.Depth.OrderKind[id] != 0;
        public static PingCall OrderCall(World w, int id) => (PingCall)(w.Depth.OrderKind[id] - 1);

        /// <summary>Bot id's order is over.</summary>
        public static void Drop(World w, int id)
        {
            w.Depth.OrderKind[id] = 0;
        }

        /// <summary>Ticks until actor id may ping again (0 = now).</summary>
        public static int WaitTicks(World w, int id)
        {
            DepthState d = w.Depth;
            PingsConfig P = w.Config.Depth.Pings;
            int n = d.PingRing;
            int oldest = d.PingTicks[id * n + d.PingNext[id]];
            int wait = oldest + (int)P.RateWindowTicks - w.Tick;
            return wait > 0 ? wait : 0;
        }

        private static int MaxFor(PingsConfig P, PingCall call)
        {
            return call == PingCall.Attack ? (int)P.MaxAttack : call == PingCall.Defend ? (int)P.MaxDefend : call == PingCall.Help ? (int)P.MaxHelp : 0;
        }

        private static int TicksFor(PingsConfig P, PingCall call)
        {
            return call == PingCall.Attack ? (int)P.AttackTicks : call == PingCall.Defend ? (int)P.DefendTicks : (int)P.HelpTicks;
        }

        private static int MatesOnHill(World w, int id)
        {
            HillDef hill = w.Map.Hill;
            int n = 0;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (j == id || !t.Alive || t.Team != w.Actors[id].Team) continue;
                double dx = w.X[j] - hill.X;
                double dz = w.Z[j] - hill.Z;
                if (dx * dx + dz * dz <= hill.R * hill.R) n++;
            }
            return n;
        }

        private static bool MayAnswer(World w, int j, int pinger, PingCall call)
        {
            Actor b = w.Actors[j];
            if (j == pinger || b.IsHuman || !b.Alive || b.StandUp > 0 || b.AirLen > 0 || b.Team != w.Actors[pinger].Team) return false;
            if (w.Depth.OrderKind[j] != 0 || b.CarryAmount > 0 || b.CarryMeat > 0) return false;
            if (call != PingCall.Defend && !(w.Config.Class(b.Cls).AttackDamage > 0)) return false;
            if (b.AiState == AiState.HoldHill && MatesOnHill(w, j) < (int)w.Config.Depth.Pings.HillMinMates) return false;
            return true;
        }

        /// <summary>
        /// Actor id pings. False (one PingRefused event: A = pinger, B = call, (X, Z), Value = ticks to wait) inside the rate
        /// limit. Else one Ping event (A = pinger, B = call, (X, Z) clamped to the map, Value = ticks the marker lives) and one
        /// PingAnswered per bot that takes it (A = bot, B = pinger, (X, Z) = the bot, Value = call). maxAnswers below 0 = the call's own cap.
        /// </summary>
        public static bool Place(World w, int id, PingCall call, double x, double z, int maxAnswers = -1)
        {
            DepthState d = w.Depth;
            PingsConfig P = w.Config.Depth.Pings;
            MapDef map = w.Map;
            if (!(x >= 0.5)) x = 0.5;
            if (x > map.Width - 0.5) x = map.Width - 0.5;
            if (!(z >= 0.5)) z = 0.5;
            if (z > map.Height - 0.5) z = map.Height - 0.5;
            int wait = WaitTicks(w, id);
            if (wait > 0)
            {
                d.PingsRefused++;
                World.PushEvent(w, SimEventType.PingRefused, id, (int)call, x, z, wait);
                return false;
            }
            int ring = d.PingRing;
            d.PingTicks[id * ring + d.PingNext[id]] = w.Tick;
            d.PingNext[id] = (byte)((d.PingNext[id] + 1) % ring);
            d.PingsAccepted++;
            World.PushEvent(w, SimEventType.Ping, id, (int)call, x, z, P.PingLifeTicks);

            int max = maxAnswers >= 0 ? Math.Min(maxAnswers, MaxFor(P, call)) : MaxFor(P, call);
            if (max <= 0) return true;
            Actor pinger = w.Actors[id];
            if (call == PingCall.Help && !pinger.Alive) return true;
            int team = (int)pinger.Team;
            int slot = -1;
            int serial = 0;
            for (int k = 0; k < max; k++)
            {
                int best = -1;
                double bestD = P.AnswerRange * P.AnswerRange;
                for (int j = 0; j < w.ActorCount; j++)
                {
                    if (!MayAnswer(w, j, id, call)) continue;
                    double dx = w.X[j] - x;
                    double dz = w.Z[j] - z;
                    double d2 = dx * dx + dz * dz;
                    if (d2 > P.AnswerRange * P.AnswerRange) continue;
                    double bias = BotNames.AnswerBias(w, j, call);
                    d2 *= bias * bias;
                    if (d2 >= bestD) continue;
                    bestD = d2;
                    best = j;
                }
                if (best < 0) break;
                if (slot < 0)
                {
                    // One route per ping, shared by its answerers: the team's next route slot (round robin).
                    int per = d.PingFields.Length / 2;
                    slot = team * per + d.PingFieldNext[team];
                    d.PingFieldNext[team] = (d.PingFieldNext[team] + 1) % per;
                    Goal[0] = call == PingCall.Help ? new PointXZ(w.X[id], w.Z[id]) : new PointXZ(x, z);
                    d.PingFields[slot] = FlowField.ComputeFlowField(map, Goal, d.PingFields[slot], w.WallVersion);
                    serial = ++d.PingFieldSerial[slot];
                }
                d.OrderKind[best] = (byte)((int)call + 1);
                d.OrderX[best] = x;
                d.OrderZ[best] = z;
                d.OrderBy[best] = id;
                d.OrderEnd[best] = w.Tick + TicksFor(P, call);
                d.OrderField[best] = slot;
                d.OrderSerial[best] = serial;
                w.Actors[best].AiState = AiState.Order; // obeyed on this tick; the bot's next think keeps or refines it
                d.PingAnswers++;
                World.PushEvent(w, SimEventType.PingAnswered, best, id, w.X[best], w.Z[best], (int)call);
            }
            return true;
        }

        /// <summary>The route of bot id's order, or null when its slot was reused by a later ping (the bot then walks straight).</summary>
        public static FlowField OrderRoute(World w, int id)
        {
            DepthState d = w.Depth;
            int slot = d.OrderField[id];
            if (slot < 0 || slot >= d.PingFields.Length || d.PingFieldSerial[slot] != d.OrderSerial[id]) return null;
            return d.PingFields[slot];
        }

        /// <summary>Where bot id's order sends it and how close it stops: Help = the pinger, Defend = inside the hold ring, Attack = the spot.</summary>
        public static void OrderGoal(World w, int id, out double gx, out double gz, out double stop)
        {
            DepthState d = w.Depth;
            PingsConfig P = w.Config.Depth.Pings;
            gx = d.OrderX[id];
            gz = d.OrderZ[id];
            stop = P.ArriveRange;
            PingCall call = OrderCall(w, id);
            if (call == PingCall.Defend) stop = P.DefendHoldRange - 1;
            else if (call == PingCall.Help)
            {
                int by = d.OrderBy[id];
                if (by >= 0 && by < w.ActorCount)
                {
                    gx = w.X[by];
                    gz = w.Z[by];
                }
                stop = P.HelpKeepDistance;
            }
        }

        /// <summary>
        /// The enemy bot id's order tells it to fight right now, or -1. Attack: the nearest living enemy within AttackSeekRange
        /// of the spot. Defend: the nearest within DefendHoldRange + DefendReach of the spot. Help: the pinger's last attacker
        /// while it lives. Only one within sightRange of the bot counts (it must be close enough to engage). Lowest id on a tie.
        /// </summary>
        public static int OrderEnemy(World w, int id, double sightRange)
        {
            DepthState d = w.Depth;
            PingsConfig P = w.Config.Depth.Pings;
            PingCall call = OrderCall(w, id);
            Actor a = w.Actors[id];
            double s2 = sightRange * sightRange;
            if (call == PingCall.Help)
            {
                int by = d.OrderBy[id];
                if (by < 0 || by >= w.ActorCount) return -1;
                int la = w.Actors[by].LastAttacker;
                if (la < 0 || la >= w.ActorCount || !w.Actors[la].Alive || w.Actors[la].Team == a.Team) return -1;
                double hx = w.X[la] - w.X[id];
                double hz = w.Z[la] - w.Z[id];
                return hx * hx + hz * hz <= s2 ? la : -1;
            }
            double r = call == PingCall.Attack ? P.AttackSeekRange : P.DefendHoldRange + P.DefendReach;
            double best = r * r;
            int pick = -1;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (!t.Alive || t.Team == a.Team || t.SpawnProtect > 0) continue;
                double dx = w.X[j] - d.OrderX[id];
                double dz = w.Z[j] - d.OrderZ[id];
                double d2 = dx * dx + dz * dz;
                if (d2 > best) continue;
                double bx = w.X[j] - w.X[id];
                double bz = w.Z[j] - w.Z[id];
                if (bx * bx + bz * bz > s2) continue;
                if (d2 == best && pick >= 0) continue;
                best = d2;
                pick = j;
            }
            return pick;
        }

        /// <summary>One step of pings (SimStep): expire orders, take every ping input, then the bots' own calls.</summary>
        public static void PingSystem(World w)
        {
            bool on = On(w);
            DepthState d = w.Depth;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                InputFrame inp = w.Inputs[i];
                int k = inp.PingKind;
                if (k != 0)
                {
                    inp.PingKind = 0; // one-shot
                    if (on && k >= 1 && k <= 4) Place(w, i, (PingCall)(k - 1), inp.PingX, inp.PingZ);
                }
                if (d.OrderKind[i] == 0) continue;
                int by = d.OrderBy[i];
                bool helpGone = d.OrderKind[i] == (int)PingCall.Help + 1 && (by < 0 || by >= n || !w.Actors[by].Alive);
                if (!on || !w.Actors[i].Alive || w.Tick >= d.OrderEnd[i] || helpGone) d.OrderKind[i] = 0;
            }
            if (!on || !w.Config.Depth.Pings.BotCalls) return;
            PingsConfig P = w.Config.Depth.Pings;
            BotsState st = BotBrain.GetBotsState(w);
            bool egg = w.Kaiju.State == KaijuState.Egg && !w.Match.Hatched;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (a.IsHuman || !a.Alive || w.Tick < d.BotPingReady[i] || w.Tick < d.TeamBotPingReady[(int)a.Team]) continue;
                PingCall call;
                double px;
                double pz;
                int la = a.LastAttacker;
                if (a.MaxHp > 0 && a.Hp / a.MaxHp < P.BotHelpHpFraction && la >= 0 && la < n && w.Actors[la].Alive && w.Actors[la].Team != a.Team
                    && (w.X[la] - w.X[i]) * (w.X[la] - w.X[i]) + (w.Z[la] - w.Z[i]) * (w.Z[la] - w.Z[i]) <= P.AttackSeekRange * P.AttackSeekRange)
                {
                    call = PingCall.Help;
                    px = w.X[i];
                    pz = w.Z[i];
                }
                else if (egg && st != null && a.AiState == AiState.Fight && st.Target[i] >= 0 && st.Target[i] < n && w.Actors[st.Target[i]].Alive && w.Actors[st.Target[i]].CarryAmount > 0)
                {
                    call = PingCall.Attack;
                    px = w.X[st.Target[i]];
                    pz = w.Z[st.Target[i]];
                }
                else continue;
                d.BotPingReady[i] = w.Tick + (int)P.BotPingCooldownTicks;
                d.TeamBotPingReady[(int)a.Team] = w.Tick + (int)P.BotTeamPingGapTicks;
                d.BotPings++;
                Place(w, i, call, px, pz, (int)P.BotPingResponders);
            }
        }
    }

    /// <summary>
    /// U-067 named bots with play styles (DG-21's unbuilt half; our rule). Every actor slot gets a NAME for the scoreboard
    /// and the ping feed (unique within a match up to 37 actors, fixed by the match seed and the slot) and every bot a
    /// STYLE from its class: Warriors are Brawlers or Guards, Farmers Harvesters, Miners and Demolitions Sappers, Builders
    /// and Healers Guards. A style scales how readily the bot fights (BotStyle FightScale) and which calls it answers first
    /// (AnswerBias: under 1 = it counts as closer). Data and pure functions; no state.
    /// </summary>
    public static class BotNames
    {
        public const int Brawler = 0, Guard = 1, Harvester = 2, Sapper = 3;
        public static readonly string[] StyleName = { "BRAWLER", "GUARD", "HARVESTER", "SAPPER" };
        public static readonly string[] StyleLine =
        {
            "Picks fights. First to answer Attack.",
            "Holds ground. First to answer Defend and Help.",
            "Keeps the food moving. Fights last.",
            "Digs, builds and blasts. Answers when close.",
        };
        /// <summary>The name pool, in ONE place for the owner to edit (keep 37 or another prime count so names stay unique).</summary>
        public static readonly string[] Pool =
        {
            "Brook", "Ash", "Wren", "Flint", "Moss", "Pike", "Sable", "Thorn", "Vale", "Reed", "Juno", "Kestrel", "Onyx",
            "Rook", "Sparrow", "Tarn", "Umber", "Vesper", "Willow", "Yarrow", "Zephyr", "Alder", "Birch", "Cinder", "Dune",
            "Ember", "Fern", "Gale", "Heron", "Iris", "Jasper", "Lark", "Marsh", "Nettle", "Orrin", "Quill", "Rune",
        };

        /// <summary>The name of actor slot id this match.</summary>
        public static string NameOf(World w, int id) => Pool[NameIndex(w.Opts.Seed, id)];

        public static int NameIndex(int seed, int id)
        {
            int n = Pool.Length;
            int offset = DepthRules.HashInt(seed, 21, 0, n);
            int stride = 1 + DepthRules.HashInt(seed, 22, 0, n - 1);
            return (int)(((long)offset + (long)id * stride) % n);
        }

        /// <summary>The play style of actor id (from its current class; a Warrior's is fixed per match by seed and slot).</summary>
        public static int StyleOf(World w, int id)
        {
            switch (w.Actors[id].Cls)
            {
                case ClassId.Warrior:
                    return DepthRules.Hash01(w.Opts.Seed, 23, id) < w.Config.Depth.Pings.WarriorGuardShare ? Guard : Brawler;
                case ClassId.Farmer:
                    return Harvester;
                case ClassId.Miner:
                case ClassId.Demolitions:
                    return Sapper;
                default:
                    return Guard;
            }
        }

        /// <summary>BotFsm: the style's fight multiplier (exactly 1 with the rule off or for a human seat).</summary>
        public static double FightScale(World w, int id)
        {
            if (!PingRules.On(w) || w.Actors[id].IsHuman) return 1;
            return w.Config.Depth.Pings.StyleFightScale[StyleOf(w, id)];
        }

        /// <summary>PingRules: the style's distance multiplier when choosing who answers a call.</summary>
        public static double AnswerBias(World w, int id, PingCall call)
        {
            PingsConfig P = w.Config.Depth.Pings;
            int s = StyleOf(w, id);
            if (call == PingCall.Attack) return P.StyleAttackBias[s];
            return P.StyleDefendBias[s];
        }
    }
}
