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
            if (a.Carry == CarryKind.Corruption || !FindThreat(w, st, dc, id, out int key, out int age, out double dx, out double dz))
            {
                s.SeenKey[id] = 0;
                return;
            }
            if (key != s.SeenKey[id])
            {
                s.SeenKey[id] = key;
                s.SeenTick[id] = w.Tick;
                s.Decided[id] = 0;
            }
            int seen = w.Tick - s.SeenTick[id];
            if (seen > age) age = seen;
            if (s.Decided[id] != 0) return;
            int d = DiffIndex(w, id);
            if (age < dc.ReactionTicks[d]) return;
            if (Dodge.Active(w, id) || Dodge.CooldownTicks(w, id) > 0 || !Dodge.CanStart(w, id)) return; // not able yet: decide when able
            s.Decided[id] = 1;
            if (!(Roll(w.Opts.Seed, w.Tick, id) < dc.DodgeChance[d])) return;
            o.Dodge = true;
            o.MoveX = dx;
            o.MoveZ = dz;
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
        public static bool FindThreat(World w, BotsState st, DodgeConfig dc, int id, out int key, out int age, out double dx, out double dz)
        {
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
                    return true;
                }
            }
            // (2) enemy TNT, landed, short fuse
            Bomb[] bombs = w.Demolitions != null ? w.Demolitions.Bombs : null;
            if (bombs != null)
            {
                double br = cfg.Abilities.DemolitionsTnt.BlastRadius + dc.TntSlack;
                for (int b = 0; b < bombs.Length; b++)
                {
                    Bomb bomb = bombs[b];
                    if (bomb == null || !bomb.Active || !bomb.Landed || bomb.Team == me.Team) continue;
                    int fuse = bomb.BlastTick - w.Tick;
                    if (fuse < 0 || fuse > dc.TntFuseTicksLeft) continue;
                    double ox = x - bomb.X, oz = z - bomb.Z;
                    if (ox * ox + oz * oz > br * br) continue;
                    key = ((bomb.BlastTick * 64 + b) << 3) | KindTnt;
                    age = w.Tick - bomb.LandTick;
                    Away(me, ox, oz, out dx, out dz);
                    return true;
                }
            }
            // (3) Kaiju claw wind-up aimed at this bot: backwards
            if (hatched && k.MeleeWindupIn > 0 && k.MeleeTargetId == id)
            {
                key = ((w.Tick + k.MeleeWindupIn) << 3) | KindClaw;
                age = 0; // its age is how long this bot has seen it (Fill)
                Away(me, x - k.X, z - k.Z, out dx, out dz);
                return true;
            }
            // (4) enemy melee wind-up aimed at this bot: sideways, the side with more room
            for (int e = 0; e < w.ActorCount; e++)
            {
                Actor en = w.Actors[e];
                if (e == id || !en.Alive || !en.SwingPending || en.Team == me.Team) continue;
                double ox = x - w.X[e], oz = z - w.Z[e];
                LightWeapon lw = LightWeapons.Of(w, en.Cls); // the swing's own reach on this base (b13-reach's Combat.ReachOf is a separate merge)
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
