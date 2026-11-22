// CM b13 item J: bot jump and dodge reflexes (a stateless hash of seed, tick and id, never the shared RNG).
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Bots
{
    /// <summary>
    /// CM b13 item J (RULE CHANGE with Dodge.On): runs after SteerBot (BotBrain.FillBotInput). Threats, first found wins:
    /// (1) a Kaiju slam telegraph whose circle covers the bot with SlamTicksLeft or fewer ticks left: dodge straight away
    /// from its centre; (2) an enemy TNT that has landed with TntFuseTicksLeft or fewer fuse ticks, the bot within its
    /// BlastRadius + TntSlack: away; (3) a Kaiju claw wind-up aimed at the bot: backwards (away from the Kaiju); (4) an enemy
    /// melee wind-up aimed at the bot within the attacker's reach + MeleeSlack: sideways, toward the side with more clear
    /// cells. A bot reacts only once the threat has existed ReactionTicks (Easy 12, Normal 8, Hard 5), decides ONCE per
    /// threat (DodgeState.SeenKey / Decided), and dodges with DodgeChance (0.15 / 0.35 / 0.60) from
    /// SimResources.SeedUniform of a hash of (seed, tick, id). Jump: the steering is blocked by a LowWall within JumpProbe
    /// ahead and the cell past it is walkable: press Jump, at most once per JumpEveryTicks. Crystal carriers never dodge.
    /// </summary>
    public static class BotReflex
    {
        public const int KindSlam = 1, KindTnt = 2, KindClaw = 3, KindMelee = 4;
        /// <summary>b13 r3: no attack cooldown is longer than this (seconds), so an older swing is skipped before any lookup.</summary>
        private const double MaxCycleSeconds = 1.5;

        /// <summary>Writes bot id's jump and dodge presses into o after SteerBot (nothing with the rule off).</summary>
        public static void Fill(World w, BotsState st, int id, InputFrame o)
        {
            if (!Dodge.On(w)) return;
            Actor a = w.Actors[id];
            if (!a.Alive || a.AirLen > 0 || a.StandUp > 0) return;
            DodgeState s = w.Dodges;
            s.Ensure(w);
            DodgeConfig dc = w.Config.Depth.Dodge;
            JumpOverLowWall(w, s, dc, id, o);
            if (a.Carry == CarryKind.Corruption || !Scan(w, st, dc, id, out int key, out int age, out double dx, out double dz, out double ax, out double az, out double dmg))
            {
                s.SeenKey[id] = 0;
                return;
            }
            int kind = key & 7;
            if (key != s.SeenKey[id])
            {
                s.SeenKey[id] = key;
                s.SeenTick[id] = w.Tick;
                s.Decided[id] = 0;
                s.SeenByKind[kind]++;
            }
            int seen = w.Tick - s.SeenTick[id];
            if (seen > age) age = seen;
            if (s.Decided[id] != 0) return;
            int d = DiffIndex(w, id);
            if (age < dc.ReactionTicks[d]) return;
            if (Dodge.Active(w, id) || Dodge.CooldownTicks(w, id) > 0 || !Dodge.CanStart(w, id)) return; // not able yet: decide when able
            s.Decided[id] = 1;
            s.DecidedByKind[kind]++;
            double chance = kind == KindTnt ? dc.TntChance[d] : dc.DodgeChance[d];   // b13 r3: TNT has its own, lower roll
            // px S1 [3]: bots by human limits: an average human sidesteps few melee swings (BotMeleeDodgeChance, Easy/Normal/Hard).
            double[] md = BotAim.On(w) ? w.Config.Depth.CombatFeel.BotMeleeDodgeChance : null;
            if (kind == KindMelee && md != null && d < md.Length) chance = md[d];
            if (!(Roll(w.Opts.Seed, w.Tick, id) < chance)) return;
            if (!HillAware(w, s, dc, id, ax, az, dmg, ref dx, ref dz)) return;   // b13 r3: never off the hill unless lethal
            s.PressedByKind[kind]++;
            o.Dodge = true;
            o.MoveX = dx;
            o.MoveZ = dz;
        }

        // Candidate turns of the threat direction (cos, sin): 0, +-45, +-90, +-135, 180 degrees. Constants (no trig).
        private const double H = 0.70710678118654757;
        private static readonly double[] TurnC = { 1, H, H, 0, 0, -H, -H, -1 };
        private static readonly double[] TurnS = { 0, H, -H, 1, -1, H, -H, 0 };

        /// <summary>
        /// b13 r3: every bot dodge ends on open ground (end and half-way cells walkable), and a bot inside the hill ring
        /// keeps its dodge on the hill. Tries the threat direction (dx, dz), then turned
        /// 45, 90, 135 and 180 degrees (the side by id parity first), and takes the first whose end (Distance along it) is
        /// walkable, at least HillMargin inside the ring, and never toward the threat (its dot with (ax, az), the unit
        /// direction away from the threat, is not below -0.05). None fits: false (the bot holds the hill and takes the hit)
        /// unless dmg would knock it out, then the threat direction (off the hill). Off the hill: the direction unchanged.
        /// </summary>
        /// <summary>
        /// b13 r3 integrator (sim cost): called by MatchTick once per tick before the bot fills. Lists the actors that swung in the
        /// last MaxCycleSeconds (or are winding up) and the landed bombs inside the dodge fuse window, in id / slot order, so Scan
        /// walks those instead of every actor and the whole bomb pool for every bot. Nothing between BeginFills and EndFills
        /// changes a swing, a bomb or who is alive (input fills and ApplyInput only copy inputs), so the candidates are the same.
        /// </summary>
        public static void BeginFills(World w)
        {
            if (!Dodge.On(w)) return;
            DodgeState s = w.Dodges;
            s.Ensure(w);
            DodgeConfig dc = w.Config.Depth.Dodge;
            if (s.ScanSwing.Length < w.ActorCount) s.ScanSwing = new int[w.Actors.Length > w.ActorCount ? w.Actors.Length : w.ActorCount];
            int maxCycle = (int)(MaxCycleSeconds * w.Config.SimHz + 0.5);
            int n = 0;
            for (int e = 0; e < w.ActorCount; e++)
            {
                Actor en = w.Actors[e];
                if (!en.Alive) continue;
                if (!en.SwingPending && (en.SwingTick <= w.Tick - maxCycle || en.SwingTick > w.Tick)) continue;
                s.ScanSwing[n++] = e;
            }
            s.ScanSwingN = n;
            Bomb[] bombs = w.Demolitions != null ? w.Demolitions.Bombs : null;
            n = 0;
            if (bombs != null)
            {
                if (s.ScanBomb.Length < bombs.Length) s.ScanBomb = new int[bombs.Length];
                for (int b = 0; b < bombs.Length; b++)
                {
                    Bomb bomb = bombs[b];
                    if (bomb == null || !bomb.Active || !bomb.Landed) continue;
                    int fuse = bomb.BlastTick - w.Tick;
                    if (fuse < 0 || fuse > dc.TntFuseTicksLeft) continue;
                    s.ScanBomb[n++] = b;
                }
            }
            s.ScanBombN = n;
            s.ScanTick = w.Tick;
            s.ScanCached = true;
        }

        /// <summary>b13 r3 integrator: the end of MatchTick's fill phase (the lists go stale once the step runs).</summary>
        public static void EndFills(World w)
        {
            if (w.Dodges != null) w.Dodges.ScanCached = false;
        }

        public static bool HillAware(World w, DodgeState s, DodgeConfig dc, int id, double ax, double az, double dmg, ref double dx, ref double dz)
        {
            HillDef hill = w.Map != null ? w.Map.Hill : null;
            double x = w.X[id], z = w.Z[id];
            bool onHill = false;
            if (dc.HillAware && hill != null)
            {
                double hx = x - hill.X, hz = z - hill.Z;
                onHill = hx * hx + hz * hz < hill.R * hill.R;
            }
            double keep = onHill ? hill.R - dc.HillMargin : 0;
            if (keep < 0) keep = 0;
            double sign = (id & 1) == 0 ? 1 : -1;
            for (int t = 0; t < TurnC.Length; t++)
            {
                double c = TurnC[t], sn = TurnS[t] * sign;
                double cx = dx * c - dz * sn, cz = dx * sn + dz * c;
                if (cx * ax + cz * az < -0.05) continue;   // never toward the threat
                double ex = x + cx * dc.Distance, ez = z + cz * dc.Distance;
                if (onHill)
                {
                    double ox = ex - hill.X, oz = ez - hill.Z;
                    if (ox * ox + oz * oz > keep * keep) continue;
                }
                // b13 r3: open ground at the end and half way (a dash into a wall is refused or cut short, so a bot by a
                // wall turns toward the open side instead of losing its dodge)
                if (!MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(ex), JsMath.FloorToInt(ez))) continue;
                if (!MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(x + cx * dc.Distance * 0.5), JsMath.FloorToInt(z + cz * dc.Distance * 0.5))) continue;
                if (t > 0) { if (onHill) s.HillTurned++; else s.OpenTurned++; }
                dx = cx;
                dz = cz;
                return true;
            }
            if (!onHill) return true;   // off the hill nothing fits: the threat direction (Dodge plans and may cut it short)
            if (dmg >= w.Actors[id].Hp)
            {
                s.HillLethalOut++;
                return true;
            }
            s.HillHeld++;
            return false;
        }

        /// <summary>Easy 0, Normal 1, Hard 2 (BotSkill.Of; never by the enum's integer value).</summary>
        public static int DiffIndex(World w, int id)
        {
            BotDifficulty d = BotSkill.Of(w, id);
            return d == BotDifficulty.Easy ? 0 : d == BotDifficulty.Hard ? 2 : 1;
        }

        /// <summary>The stateless roll in [0, 1) for (seed, tick, id): SimResources.SeedUniform of a mixed integer.</summary>
        public static double Roll(int seed, int tick, int id)
        {
            int h = unchecked(seed * 73856093 ^ (tick + 1) * 19349663 ^ (id + 1) * 83492791);
            return SimResources.SeedUniform(h);
        }

        /// <summary>The first threat to bot id (see the class summary): its key, age in ticks and the unit dodge direction.</summary>
        public static bool FindThreat(World w, BotsState st, DodgeConfig dc, int id, out int key, out int age, out double dx, out double dz) =>
            Scan(w, st, dc, id, out key, out age, out dx, out dz, out double _, out double _, out double _);

        /// <summary>FindThreat plus (ax, az), the unit direction away from the threat, and dmg, the hit it would deal.</summary>
        public static bool Scan(World w, BotsState st, DodgeConfig dc, int id, out int key, out int age, out double dx, out double dz, out double ax, out double az, out double dmg)
        {
            ax = 0;
            az = 0;
            dmg = 0;
            key = 0;
            age = 0;
            dx = 0;
            dz = 0;
            Actor me = w.Actors[id];
            GameConfig cfg = w.Config;
            double x = w.X[id], z = w.Z[id];
            double myR = cfg.Class(me.Cls).Radius;
            Kaiju k = w.Kaiju;
            bool hatched = k.State != KaijuState.Egg;
            // (1) slam telegraph
            if (hatched && k.TelegraphIn > 0)
            {
                int left = (int)System.Math.Ceiling(k.TelegraphIn * cfg.SimHz - 1e-6);
                double r = cfg.Kaiju.SlamRadius + myR;
                double ox = x - k.TelegraphX, oz = z - k.TelegraphZ;
                if (left <= dc.SlamTicksLeft && ox * ox + oz * oz <= r * r)
                {
                    int full = (int)JsMath.Round(cfg.Kaiju.SlamTelegraphSeconds * cfg.SimHz);
                    if (w.Dur.SlamTelegraph > full) full = w.Dur.SlamTelegraph;
                    key = ((w.Tick + left) << 3) | KindSlam;
                    age = full - left;
                    Away(me, ox, oz, out dx, out dz);
                    ax = dx; az = dz; dmg = cfg.Kaiju.SlamDamage;
                    return true;
                }
            }
            // (2) enemy TNT, landed, short fuse
            Bomb[] bombs = w.Demolitions != null ? w.Demolitions.Bombs : null;
            DodgeState cache = w.Dodges;
            bool cached = cache != null && cache.ScanCached && cache.ScanTick == w.Tick;
            if (bombs != null)
            {
                double br = cfg.Abilities.DemolitionsTnt.BlastRadius + dc.TntSlack;
                int nb = cached ? cache.ScanBombN : bombs.Length;
                for (int bi = 0; bi < nb; bi++)
                {
                    int b = cached ? cache.ScanBomb[bi] : bi;
                    Bomb bomb = bombs[b];
                    if (bomb == null || !bomb.Active || !bomb.Landed || bomb.Team == me.Team) continue;
                    int fuse = bomb.BlastTick - w.Tick;
                    if (fuse < 0 || fuse > dc.TntFuseTicksLeft) continue;
                    double ox = x - bomb.X, oz = z - bomb.Z;
                    if (ox * ox + oz * oz > br * br) continue;
                    key = ((bomb.BlastTick * 64 + b) << 3) | KindTnt;
                    age = w.Tick - bomb.LandTick;
                    Away(me, ox, oz, out dx, out dz);
                    ax = dx; az = dz; dmg = cfg.Abilities.DemolitionsTnt.Damage;
                    return true;
                }
            }
            // (3) Kaiju claw wind-up aimed at this bot: backwards
            if (hatched && k.MeleeWindupIn > 0 && k.MeleeTargetId == id)
            {
                key = ((w.Tick + k.MeleeWindupIn) << 3) | KindClaw;
                age = 0; // its age is how long this bot has seen it (Fill)
                Away(me, x - k.X, z - k.Z, out dx, out dz);
                ax = dx; az = dz; dmg = KaijuTypes.MeleeDamage(w, cfg.Kaiju.MeleeDamage);
                return true;
            }
            // (4) enemy melee wind-up aimed at this bot: sideways, the side with more room
            int maxCycle = (int)(MaxCycleSeconds * cfg.SimHz + 0.5);
            int ne = cached ? cache.ScanSwingN : w.ActorCount;
            for (int ei = 0; ei < ne; ei++)
            {
                int e = cached ? cache.ScanSwing[ei] : ei;
                Actor en = w.Actors[e];
                if (e == id || !en.Alive || en.Team == me.Team) continue;
                if (!en.SwingPending && (en.SwingTick <= w.Tick - maxCycle || en.SwingTick > w.Tick)) continue;   // cheap: no recent swing
                LightWeapon lw = LightWeapons.Of(w, en.Cls); // the swing's own reach on this base (b13-reach's Combat.ReachOf is a separate merge)
                if (!en.SwingPending)
                {
                    // b13 r3: the swing cycle (wind-up, then the cooldown to the next swing) is the readable threat
                    if (!dc.MeleeThreatIsTheCycle || en.StandUp > 0) continue;
                    double cdSec = lw != null ? lw.CooldownSeconds : cfg.Class(en.Cls).AttackCooldown;
                    int cycle = (int)(cdSec * cfg.SimHz + 0.5);
                    if (en.SwingTick <= w.Tick - cycle || en.SwingTick > w.Tick) continue;
                }
                double ox = x - w.X[e], oz = z - w.Z[e];
                double reach = (lw != null ? lw.Range : cfg.Class(en.Cls).AttackRange) + dc.MeleeSlack + myR;
                double d2 = ox * ox + oz * oz;
                if (d2 > reach * reach) continue;
                bool aimed;
                if (!en.IsHuman && st != null && st.Target != null && e < st.Target.Length) aimed = st.Target[e] == id;
                else
                {
                    double l = System.Math.Sqrt(d2);
                    aimed = l < 1e-9 || (JsMath.Cos(en.SwingFacing) * ox + JsMath.Sin(en.SwingFacing) * oz) / l >= 0.7;
                }
                if (!aimed) continue;
                key = ((en.SwingTick * 64 + e) << 3) | KindMelee;
                age = w.Tick - en.SwingTick;
                Sideways(w, id, ox, oz, out dx, out dz);
                Away(me, ox, oz, out ax, out az);
                dmg = lw != null ? lw.Damage : cfg.Class(en.Cls).AttackDamage;
                return true;
            }
            return false;
        }

        /// <summary>Unit direction along (ox, oz); at the centre, backwards from the facing.</summary>
        private static void Away(Actor me, double ox, double oz, out double dx, out double dz)
        {
            double l = System.Math.Sqrt(ox * ox + oz * oz);
            if (l > 1e-6)
            {
                dx = ox / l;
                dz = oz / l;
                return;
            }
            dx = -JsMath.Cos(me.Facing);
            dz = -JsMath.Sin(me.Facing);
        }

        /// <summary>Perpendicular to the attacker line, toward the side with more walkable cells within 1 to 3 u (ties: id parity).</summary>
        private static void Sideways(World w, int id, double ox, double oz, out double dx, out double dz)
        {
            Away(w.Actors[id], ox, oz, out double ax, out double az);
            double lx = -az, lz = ax;
            int left = 0, right = 0;
            for (int s = 1; s <= 3; s++)
            {
                if (MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(w.X[id] + lx * s), JsMath.FloorToInt(w.Z[id] + lz * s))) left++;
                if (MapGrid.IsWalkable(w.Map, JsMath.FloorToInt(w.X[id] - lx * s), JsMath.FloorToInt(w.Z[id] - lz * s))) right++;
            }
            bool goLeft = left > right || (left == right && (id & 1) == 0);
            dx = goLeft ? lx : -lx;
            dz = goLeft ? lz : -lz;
        }

        /// <summary>Jump a short wall the steering runs into (LowWall within JumpProbe ahead, walkable cell past it).</summary>
        public static bool JumpOverLowWall(World w, DodgeState s, DodgeConfig dc, int id, InputFrame o)
        {
            double mx = o.MoveX, mz = o.MoveZ;
            double ml = System.Math.Sqrt(mx * mx + mz * mz);
            if (!(ml > 0.3)) return false;
            if (w.Tick - s.LastJump[id] < dc.JumpEveryTicks) return false;
            mx /= ml;
            mz /= ml;
            MapDef map = w.Map;
            double x = w.X[id], z = w.Z[id];
            for (int q = 1; q <= 2; q++)
            {
                double d = dc.JumpProbe * q * 0.5;
                int cx = JsMath.FloorToInt(x + mx * d), cz = JsMath.FloorToInt(z + mz * d);
                if (!MapGrid.InBounds(map, cx, cz)) return false;
                byte c = map.Cells[cz * map.Width + cx];
                if (MapGrid.IsWalkableType(c)) continue;
                if (c != Cell.LowWall) return false;
                int fx = JsMath.FloorToInt(x + mx * (d + 1.0)), fz = JsMath.FloorToInt(z + mz * (d + 1.0));
                if (!MapGrid.IsWalkable(map, fx, fz)) return false;
                o.Jump = true;
                s.LastJump[id] = w.Tick;
                return true;
            }
            return false;
        }
    }
}
