// b15 rule B (owner approved 2026-11-17): each hut level is a team vote between two side-grades. Every number lives in
// Sim/Config/HutChoicesConfig.cs; the words in HutChoiceNames below.
using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// b15 rule B: the ten side-grades, two per hut level, in card order (level L: left card = 2L - 1, right card = 2L). Never
    /// reordered: hashed, recorded and shown by value.
    /// </summary>
    public enum HutOption
    {
        None = 0,
        QuickReturn = 1, ToughWalls = 2,
        RichVeins = 3, ShieldWall = 4,
        ShortFuse = 5, BlastGuard = 6,
        WideMend = 7, KaijuHunters = 8,
        HillAnchor = 9, SwiftFeet = 10,
    }

    /// <summary>b15 rule B state on the World (World.HutVote). Fixed arrays, sized once; reset in place. Integers only.</summary>
    public sealed class HutChoiceState
    {
        public const int Levels = 5;
        /// <summary>[team x Levels + level - 1]: 0 undecided, 1 the left card, 2 the right card.</summary>
        public readonly byte[] Pick = new byte[2 * Levels];
        /// <summary>Per team: levels decided; the level being voted (0 = no vote open); the vote's open and closing ticks.</summary>
        public readonly int[] Decided = new int[2], VoteLevel = new int[2], OpenTick = new int[2], EndTick = new int[2];
        /// <summary>Per team: bit (int)HutOption set for every option the team holds.</summary>
        public readonly int[] Mask = new int[2];
        /// <summary>Per team: the last decided vote (HUD toast): its tick (-1 none), level, card, votes for each card, default used.</summary>
        public readonly int[] LastTick = { -1, -1 }, LastLevel = new int[2], LastCard = new int[2], LastA = new int[2], LastB = new int[2];
        public readonly byte[] LastDefault = new byte[2];
        /// <summary>Per actor: this vote's card (0 none, 1, 2); the tick a bot casts its vote (-1 = done or not a bot).</summary>
        public byte[] Vote;
        public int[] BotTick;
        /// <summary>Counters (tests and the soak): votes opened, closed, closed with no vote cast, the longest vote in ticks.</summary>
        public int Opened, Closed, Empty, LongestTicks;

        public void Init(World w)
        {
            int cap = World.ActorCapacity(w.Config);
            Vote = new byte[cap];
            BotTick = new int[cap];
            Clear();
        }

        public void Reset(World w) => Clear();

        void Clear()
        {
            Array.Clear(Pick, 0, Pick.Length);
            Array.Clear(Decided, 0, 2);
            Array.Clear(VoteLevel, 0, 2);
            Array.Clear(OpenTick, 0, 2);
            Array.Clear(EndTick, 0, 2);
            Array.Clear(Mask, 0, 2);
            LastTick[0] = -1;
            LastTick[1] = -1;
            Array.Clear(LastLevel, 0, 2);
            Array.Clear(LastCard, 0, 2);
            Array.Clear(LastA, 0, 2);
            Array.Clear(LastB, 0, 2);
            Array.Clear(LastDefault, 0, 2);
            Opened = Closed = Empty = LongestTicks = 0;
            if (Vote == null) return;
            Array.Clear(Vote, 0, Vote.Length);
            Array.Fill(BotTick, -1);
        }
    }

    /// <summary>
    /// b15 rule B: HUT AS A TEAM CHOICE. While the rule is on, a hut level gives no flat +5% (Blacksmith.StatScale is 1);
    /// instead each level the team reaches opens a VoteSeconds vote between that level's two cards (HutOption). Humans vote with
    /// InputFrame.StationBuy = 1 (left) or 2 (right) while their team's vote is open; bots vote by team need (BotNeed) after a
    /// seeded think. A strict majority of the team closes the vote at once; otherwise at the end the card with more votes wins
    /// and a tie or no votes takes the seeded default. Effects read Has() where the stat is used (B13Hooks damage and speed,
    /// the respawn wait, walls, mining, TNT, MEND, the hill tie). Deterministic (actor-id order, no shared RNG), no allocation.
    /// </summary>
    public static class HutChoices
    {
        /// <summary>The rule is on: Full profile, HutChoicesConfig.Enabled and the Blacksmith hut on.</summary>
        public static bool On(World w) => DepthRules.On(w, w.Config.Depth.HutChoices.Enabled) && Blacksmith.On(w);

        /// <summary>The flat +5% per level is replaced (config only: Stations / Blacksmith MaxHpAt read it).</summary>
        public static bool FlatOff(GameConfig c) => DepthRules.On(c, c.Depth.HutChoices.Enabled) && DepthRules.On(c, c.Depth.Hut.On);

        /// <summary>The option on card (1 left, 2 right) of a hut level 1..5.</summary>
        public static HutOption OptionAt(int level, int card) =>
            level < 1 || level > HutChoiceState.Levels || card < 1 || card > 2 ? HutOption.None : (HutOption)((level - 1) * 2 + card);

        /// <summary>The level an option belongs to (1..5; 0 for None).</summary>
        public static int LevelOf(HutOption o) => o == HutOption.None ? 0 : ((int)o + 1) / 2;

        /// <summary>The team holds option o (the mask is read first: it stays 0 while the rule is off, so the common case is one read).</summary>
        public static bool Has(World w, TeamId team, HutOption o) => (w.HutVote.Mask[(int)team] & (1 << (int)o)) != 0 && On(w);

        /// <summary>The option the team chose at a level (None while undecided or with the rule off).</summary>
        public static HutOption Chosen(World w, TeamId team, int level)
        {
            if (!On(w) || level < 1 || level > HutChoiceState.Levels) return HutOption.None;
            return OptionAt(level, w.HutVote.Pick[(int)team * HutChoiceState.Levels + level - 1]);
        }

        /// <summary>The level the team is voting on now (0 = none open).</summary>
        public static int VoteLevel(World w, TeamId team) => On(w) ? w.HutVote.VoteLevel[(int)team] : 0;

        /// <summary>Ticks left in the team's open vote (0 when none).</summary>
        public static int VoteTicksLeft(World w, TeamId team) =>
            VoteLevel(w, team) > 0 ? Math.Max(0, w.HutVote.EndTick[(int)team] - w.Tick) : 0;

        /// <summary>Votes for card (1 or 2) in the team's open vote.</summary>
        public static int Votes(World w, TeamId team, int card)
        {
            if (VoteLevel(w, team) <= 0) return 0;
            int n = 0;
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Team == team && w.HutVote.Vote[i] == card) n++;
            return n;
        }

        /// <summary>The card actor id voted for in its team's open vote (0 none).</summary>
        public static int VoteOf(World w, int id) => On(w) && id >= 0 && id < w.ActorCount ? w.HutVote.Vote[id] : 0;

        // ------------------------------------------------------------------ effects (each 1 / false with the rule off)

        /// <summary>TOUGH WALLS: HP and life multiplier of the team's Builder walls and blocks (BuilderWalls).</summary>
        public static double WallScale(World w, TeamId team) => Has(w, team, HutOption.ToughWalls) ? w.Config.Depth.HutChoices.ToughWallScale : 1.0;

        /// <summary>RICH VEINS: share of the mining channel one ore takes for the team (Blacksmith).</summary>
        public static double MineScale(World w, TeamId team) => Has(w, team, HutOption.RichVeins) ? w.Config.Depth.HutChoices.RichVeinsMineScale : 1.0;

        /// <summary>SHORT FUSE: share of the TNT fuse for the team (Demolitions).</summary>
        public static double FuseScale(World w, TeamId team) => Has(w, team, HutOption.ShortFuse) ? w.Config.Depth.HutChoices.ShortFuseScale : 1.0;

        /// <summary>WIDE MEND: MEND reach multiplier for the team's Healers (Healer).</summary>
        public static double MendScale(World w, TeamId team) => Has(w, team, HutOption.WideMend) ? w.Config.Depth.HutChoices.WideMendScale : 1.0;

        /// <summary>
        /// B13Hooks.DamageScale: SHIELD WALL (a shielded Warrior of the target's team), BLAST GUARD (an explosion into the target's
        /// team), KAIJU HUNTERS (target -1 = the Kaiju, the source's team). 1 with the rule off.
        /// </summary>
        public static double DamageScale(World w, int source, int target, DamageCause cause)
        {
            int[] mask = w.HutVote.Mask;
            if ((mask[0] | mask[1]) == 0 || !On(w)) return 1.0;
            HutChoicesConfig h = w.Config.Depth.HutChoices;
            double s = 1.0;
            if (target >= 0 && target < w.ActorCount)
            {
                Actor t = w.Actors[target];
                if (t.Cls == ClassId.Warrior && t.AbilityActive > 0 && Has(w, t.Team, HutOption.ShieldWall)) s *= h.ShieldWallScale;
                if (cause == DamageCause.Explosion && Has(w, t.Team, HutOption.BlastGuard)) s *= h.BlastGuardScale;
            }
            else if (target == -1 && source >= 0 && source < w.ActorCount && Has(w, w.Actors[source].Team, HutOption.KaijuHunters))
            {
                s *= h.KaijuHunterScale;
            }
            return s;
        }

        /// <summary>B13Hooks.SpeedScale: SWIFT FEET off the hill (beyond its radius + SwiftHillMargin). 1 with the rule off.</summary>
        public static double SpeedScale(World w, int id)
        {
            if (id < 0 || id >= w.ActorCount || !Has(w, w.Actors[id].Team, HutOption.SwiftFeet)) return 1.0;
            HutChoicesConfig h = w.Config.Depth.HutChoices;
            double r = w.Config.Hill.Radius + h.SwiftHillMargin;
            double dx = w.X[id] - w.Map.Hill.X, dz = w.Z[id] - w.Map.Hill.Z;
            return dx * dx + dz * dz > r * r ? h.SwiftFeetScale : 1.0;
        }

        /// <summary>Hill.HillSystem, equal bodies on the hill: 1 Azure owns (HILL ANCHOR), 2 Crimson owns, 0 contested.</summary>
        public static int AnchorTie(World w)
        {
            bool a = Has(w, TeamId.Azure, HutOption.HillAnchor), c = Has(w, TeamId.Crimson, HutOption.HillAnchor);
            return a == c ? 0 : a ? 1 : 2;
        }

        // ------------------------------------------------------------------ the vote

        static int Ticks(GameConfig c, double seconds) => (int)Math.Floor(seconds * c.SimHz + 0.5);

        /// <summary>The seeded default card when a vote ties or nobody voted.</summary>
        public static int DefaultCard(World w, TeamId team, int level) => DepthRules.Hash01(w.Opts.Seed, 7700 + level, (int)team) < 0.5 ? 1 : 2;

        /// <summary>Per tick, at the end of Blacksmith.HutSystem: open, count and close each team's vote.</summary>
        public static void VoteSystem(World w)
        {
            if (!On(w)) return;
            for (int t = 0; t < 2; t++) TeamVote(w, (TeamId)t);
        }

        static void TeamVote(World w, TeamId team)
        {
            HutChoiceState s = w.HutVote;
            int t = (int)team;
            if (s.VoteLevel[t] == 0)
            {
                if (s.Decided[t] >= Math.Min(HutChoiceState.Levels, w.Hut.Level[t])) return;
                Open(w, team, s.Decided[t] + 1);
            }
            int level = s.VoteLevel[t];
            int members = 0, a = 0, b = 0;
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor x = w.Actors[i];
                if (x.Team != team) continue;
                members++;
                InputFrame inp = w.Inputs[i];
                int press = inp.StationBuy;
                if (press == 1 || press == 2)
                {
                    s.Vote[i] = (byte)press; // a human may change his mind inside the window: the last press counts
                    inp.StationBuy = 0;      // taken: the station never sees a vote press
                }
                else if (!x.IsHuman && s.Vote[i] == 0 && s.BotTick[i] >= 0 && w.Tick >= s.BotTick[i])
                {
                    s.Vote[i] = (byte)BotCard(w, i, level);
                    s.BotTick[i] = -1;
                }
                if (s.Vote[i] == 1) a++;
                else if (s.Vote[i] == 2) b++;
            }
            if (2 * a > members || 2 * b > members || w.Tick >= s.EndTick[t]) Close(w, team, level, a, b);
        }

        static void Open(World w, TeamId team, int level)
        {
            HutChoiceState s = w.HutVote;
            HutChoicesConfig h = w.Config.Depth.HutChoices;
            int t = (int)team;
            s.VoteLevel[t] = level;
            s.OpenTick[t] = w.Tick;
            s.EndTick[t] = w.Tick + Math.Max(1, Ticks(w.Config, h.VoteSeconds));
            s.Opened++;
            int think = Ticks(w.Config, h.BotThinkSeconds), spread = Ticks(w.Config, h.BotThinkSpreadSeconds);
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Team != team) continue;
                s.Vote[i] = 0;
                s.BotTick[i] = w.Actors[i].IsHuman ? -1
                    : w.Tick + think + (int)Math.Floor(DepthRules.Hash01(w.Opts.Seed, i * 16 + level, 9001 + t) * (spread + 1));
            }
        }

        static void Close(World w, TeamId team, int level, int a, int b)
        {
            HutChoiceState s = w.HutVote;
            int t = (int)team;
            bool tie = a == b;
            int card = a > b ? 1 : b > a ? 2 : DefaultCard(w, team, level);
            s.Pick[t * HutChoiceState.Levels + level - 1] = (byte)card;
            s.Mask[t] |= 1 << (int)OptionAt(level, card);
            s.Decided[t] = level;
            s.VoteLevel[t] = 0;
            s.Closed++;
            if (a + b == 0) s.Empty++;
            s.LongestTicks = Math.Max(s.LongestTicks, w.Tick - s.OpenTick[t]);
            s.LastTick[t] = w.Tick;
            s.LastLevel[t] = level;
            s.LastCard[t] = card;
            s.LastA[t] = a;
            s.LastB[t] = b;
            s.LastDefault[t] = (byte)(tie ? 1 : 0);
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (w.Actors[i].Team != team) continue;
                s.Vote[i] = 0;
                s.BotTick[i] = -1;
            }
        }

        /// <summary>A bot's card: the team-need card, or (seeded, BotContrarianShare) the other one.</summary>
        public static int BotCard(World w, int id, int level)
        {
            TeamId team = w.Actors[id].Team;
            int need = BotNeed(w, team, level);
            bool contrary = DepthRules.Hash01(w.Opts.Seed, id * 16 + level, 4242 + (int)team) < w.Config.Depth.HutChoices.BotContrarianShare;
            return contrary ? 3 - need : need;
        }

        static int Count(World w, TeamId team, ClassId cls)
        {
            int n = 0;
            for (int i = 0; i < w.ActorCount; i++) if (w.Actors[i].Team == team && w.Actors[i].Cls == cls) n++;
            return n;
        }

        /// <summary>
        /// What the team needs at a level (1 left card, 2 right card), read from the match as it stands. L1: ahead with a Builder
        /// = TOUGH WALLS (dig in), else QUICK RETURN. L2: hut not ahead in the first half = RICH VEINS, else SHIELD WALL with 3+
        /// Warriors, else RICH VEINS. L3: the enemy took SHORT FUSE, or we have fewer Demolitions = BLAST GUARD, else SHORT FUSE.
        /// L4: the Kaiju fights for the enemy or nobody = KAIJU HUNTERS; ours or still an egg with a Healer = WIDE MEND. L5:
        /// ahead or level = HILL ANCHOR (keep it), behind = SWIFT FEET (rotate and get back faster).
        /// </summary>
        public static int BotNeed(World w, TeamId team, int level)
        {
            TeamId enemy = team == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
            double lead = -Comeback.Deficit(w, team);
            switch (level)
            {
                case 1:
                    return lead > 0 && Count(w, team, ClassId.Builder) > 0 ? 2 : 1;
                case 2:
                {
                    bool early = w.Match.Time < w.Config.MatchSeconds * 0.5;
                    if (early && w.Hut.Level[(int)team] <= w.Hut.Level[(int)enemy]) return 1;
                    return Count(w, team, ClassId.Warrior) >= 3 ? 2 : 1;
                }
                case 3:
                {
                    if (Has(w, enemy, HutOption.ShortFuse)) return 2;
                    int mine = Count(w, team, ClassId.Demolitions), theirs = Count(w, enemy, ClassId.Demolitions);
                    return mine > 0 && mine >= theirs ? 1 : 2;
                }
                case 4:
                {
                    KaijuState k = w.Kaiju != null ? w.Kaiju.State : KaijuState.Egg;
                    bool ours = (team == TeamId.Azure && k == KaijuState.Azure) || (team == TeamId.Crimson && k == KaijuState.Crimson);
                    if (ours) return 1;
                    if (k == KaijuState.Egg) return Count(w, team, ClassId.Healer) > 0 ? 1 : 2;
                    return 2;
                }
                default:
                    return lead >= 0 ? 1 : 2;
            }
        }
    }

    /// <summary>b15 rule B on-screen words, in ONE place for the owner to rename (the HUD reads these). Data only.</summary>
    public static class HutChoiceNames
    {
        /// <summary>Indexed by (int)HutOption.</summary>
        public static readonly string[] Name =
        {
            "", "QUICK RETURN", "TOUGH WALLS", "RICH VEINS", "SHIELD WALL", "SHORT FUSE", "BLAST GUARD", "WIDE MEND", "KAIJU HUNTERS",
            "HILL ANCHOR", "SWIFT FEET",
        };

        /// <summary>One line each, what it does (indexed by (int)HutOption).</summary>
        public static readonly string[] Line =
        {
            "", "Back in the fight 1.5 s sooner", "Walls: +50% HP, last 50% longer", "Mining takes 40% less time",
            "Shielded Warriors take 20% less", "TNT blows 30% sooner", "25% less from blasts", "Healers mend from 40% further",
            "+25% damage to the Kaiju", "Win ties on the hill", "10% faster off the hill",
        };

        public const string Vote = "HUT LEVEL {0}: VOTE";
        public const string Chosen = "HUT LEVEL {0}: {1}";
    }
}
