using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Sim.Core
{
    /// <summary>DG-22: one land mine slot (browser Mine). Fixed pool DemolitionsState.Mines; inactive slots have Active = false.</summary>
    public sealed class Mine
    {
        public bool Active;
        public int Owner;
        public TeamId Team;
        public int Cx;
        public int Cz;
        /// <summary>First World.Tick the mine can trigger.</summary>
        public int ArmTick;
        /// <summary>World.Tick the mine is removed if nothing triggered it.</summary>
        public int ExpireTick;
        /// <summary>World.Tick it was placed (oldest-first recycling).</summary>
        public int PlacedTick;
        /// <summary>DG-22 (not in the browser): true once the MineArmed event went out (the view's ring warning).</summary>
        public bool Armed;
    }

    /// <summary>DG-22: one thrown TNT bundle (browser Bomb). Fixed pool DemolitionsState.Bombs.</summary>
    public sealed class Bomb
    {
        public bool Active;
        public int Owner;
        public TeamId Team;
        /// <summary>Where it lands and blows.</summary>
        public double X;
        public double Z;
        /// <summary>DG-22 (view): where it was thrown from (the thrower at the throw tick).</summary>
        public double FromX;
        public double FromZ;
        public int ThrownTick;
        /// <summary>DG-22 (not in the browser, U-059): World.Tick the bundle touches down (ThrownTick + FlightTicks, at most BlastTick).</summary>
        public int LandTick;
        public bool Landed;
        /// <summary>World.Tick the bomb blows (ThrownTick + FuseTicks).</summary>
        public int BlastTick;
    }

    /// <summary>Per-match Demolitions counters (browser DemolitionsStats); the soak reports them, a debug overlay may read them.</summary>
    public sealed class DemolitionsStats
    {
        public int MinesPlaced;
        public int MinesTriggered;
        public int TntThrown;
        public int TntBlasts;
        /// <summary>Damage actually dealt to actors by mines and TNT (after the shield).</summary>
        public double Damage;
        public int Kills;
        public int WallsBroken;
        public int CellsCarved;
        /// <summary>Crater cells refused because they would cut the last pad-to-hill (or shrine) route.</summary>
        public int RouteRefusals;
        /// <summary>Craters refused because the CM-30 world gap had not run out.</summary>
        public int RateRefusals;
        /// <summary>Throws refused for a full bomb pool (cannot happen with the shipped config).</summary>
        public int PoolRefusals;

        public void Clear()
        {
            MinesPlaced = 0;
            MinesTriggered = 0;
            TntThrown = 0;
            TntBlasts = 0;
            Damage = 0;
            Kills = 0;
            WallsBroken = 0;
            CellsCarved = 0;
            RouteRefusals = 0;
            RateRefusals = 0;
            PoolRefusals = 0;
        }
    }

    /// <summary>DG-22 Demolitions state (browser DemolitionsState), allocated once per World at capacity and reset in place.</summary>
    public sealed class DemolitionsState
    {
        public Mine[] Mines;
        public Bomb[] Bombs;
        /// <summary>Per actor id: first World.Tick another TNT may be thrown.</summary>
        public int[] TntReadyTick;
        /// <summary>Per actor id: InputFrame.Terraform of the previous step (rising edge).</summary>
        public byte[] PrevPressed;
        /// <summary>Per actor id: a bot's queued TNT throw (BotFsm sets it, BotBrain.FillBotInput turns it into one press).</summary>
        public byte[] BotTnt;
        public double[] BotTntX;
        public double[] BotTntZ;
        public DemolitionsStats Stats;

        public static DemolitionsState Create(GameConfig cfg)
        {
            int cap = World.ActorCapacity(cfg);
            var s = new DemolitionsState
            {
                Mines = new Mine[cap * (int)cfg.Abilities.DemolitionsMine.MaxActivePerOwner],
                Bombs = new Bomb[cap],
                TntReadyTick = new int[cap],
                PrevPressed = new byte[cap],
                BotTnt = new byte[cap],
                BotTntX = new double[cap],
                BotTntZ = new double[cap],
                Stats = new DemolitionsStats(),
            };
            for (int i = 0; i < s.Mines.Length; i++) s.Mines[i] = new Mine { Owner = -1, Cx = -1, Cz = -1 };
            for (int i = 0; i < s.Bombs.Length; i++) s.Bombs[i] = new Bomb { Owner = -1 };
            return s;
        }

        /// <summary>Back to the start of a match, in place (World.InitWorld).</summary>
        public void Reset()
        {
            for (int i = 0; i < Mines.Length; i++) { Mines[i].Active = false; Mines[i].Armed = false; }
            for (int i = 0; i < Bombs.Length; i++) { Bombs[i].Active = false; Bombs[i].Landed = false; }
            Array.Clear(TntReadyTick, 0, TntReadyTick.Length);
            Array.Clear(PrevPressed, 0, PrevPressed.Length);
            Array.Clear(BotTnt, 0, BotTnt.Length);
            Stats.Clear();
        }
    }

    /// <summary>
    /// DG-22 on-screen names of the Demolitions Expert and its abilities, in ONE place for the owner to rename (GameHud's
    /// AbilityNames and the class cards should read these). Data only: the sim never shows text.
    /// </summary>
    public static class DemolitionsNames
    {
        public const string ClassName = "DEMOLITIONS";
        /// <summary>The class ability (Space / the ability button): the land mine.</summary>
        public const string Mine = "MINE";
        /// <summary>The second ability (the DigPlace binding: F, Y on a controller): the TNT throw.</summary>
        public const string Tnt = "TNT";
    }

    /// <summary>
    /// Demolitions Expert (DG-22, port of src/sim/demolitions.ts on 09e0904, CM-38 / D-068). Travis 2026-11-06: "Land mine
    /// special (loose dirt texture) / Bomb/TNT to attack/destroy blocks/affect terrain / No regular attack".
    /// <para>Controls (the bindings already on main): attack does nothing for this class (Combat skips the swing). Ability
    /// (Space, AbilityCooldown 12 s): a LAND MINE on the cell under the aim point pulled back to DemolitionsMine.PlaceRange
    /// (no aim: straight ahead). The cell must be walkable ground with no mine, outside both pad keep-outs; a refused spot
    /// spends no cooldown and pushes Ability with a refusal code. It arms after ArmTicks, lives LifetimeTicks, and triggers on
    /// the FIRST living, grounded, unprotected ENEMY whose centre is in its cell; it then hits every enemy within BlastRadius.
    /// An owner at MaxActivePerOwner recycles its oldest mine. DigPlace (F; Y on a controller), rising edge: TNT thrown at the
    /// aim point pulled back to ThrowRange; it blows FuseTicks after the throw: enemies within BlastRadius (+ their radius),
    /// the hatched Kaiju when the owner may hurt it, enemy barricades, every breakable wall in the radius (destroyed), and a
    /// crater: every Floor or Bridge cell (placed ground is Floor) within CarveRadius becomes Void through the CM-30 dig rules
    /// (Terraform.DigRefusal: keep-outs, actors on it, the route guard), applied with MapGrid.SetCell. Inside the CM-30 world
    /// gap the crater is refused (Terraform.RefusedRate), the blast still hurts.</para>
    /// <para>Damage goes through Damage.ApplyDamage with DamageCause.Explosion, credited to the owner (meat drops where the unit
    /// died, Combat.DropCorpseMeat). Events: the browser's Ability events (cast and refusal codes; blasts with A = -1, B =
    /// owner) plus the DG-22 view events MinePlaced, MineArmed, MineTriggered, MineRemoved, TntThrown, TntLanded and Explosion,
    /// each with its position and SimEvent.Radius. Every loop is in index order and all timing is World.Tick: deterministic.
    /// Allocation-free (fixed pools in World.Demolitions).</para>
    /// </summary>
    public static class Demolitions
    {
        /// <summary>Ability event values (successes positive; refusals reuse the barricade codes so the HUD toast reads right).</summary>
        public const int MinePlacedCode = 1, TntThrownCode = 2, MineBlastCode = 3, TntBlastCode = 4;
        /// <summary>Not walkable ground / a mine already there / a pad keep-out ("Occupied").</summary>
        public const int MineRefusedOccupied = -1;
        /// <summary>The clamped point is off the map, or over Void / Water ("Out of range").</summary>
        public const int MineRefusedRange = -4;
        /// <summary>ThrowTnt return codes (no event: a press on cooldown is silent).</summary>
        public const int TntRefusedCooldown = -5, TntRefusedClass = -6, TntRefusedPool = -7;

        /// <summary>Explosion event kinds (SimEvent.B): Effects.Explode(position, radius, kind).</summary>
        public const int ExplosionMine = 1, ExplosionTnt = 2;

        /// <summary>MineRemoved reasons (SimEvent.Value).</summary>
        public const int MineExpired = 0, MineGroundGone = 1, MineRecycled = 2;

        private static double PtX;
        private static double PtZ;

        private static void ClampAim(World w, int id, double aimX, double aimZ, double range)
        {
            Abilities.ClampToRange(w.X[id], w.Z[id], aimX, aimZ, w.Actors[id].Facing, range, out PtX, out PtZ);
        }

        // ------------------------------------------------------------------------------------------------- land mine

        /// <summary>0 when a mine may go on cell (cx, cz) right now (ignoring cooldown), else a MineRefused* code.</summary>
        public static int MineCellRefusal(World w, int cx, int cz)
        {
            MapDef map = w.Map;
            if (cx < 0 || cz < 0 || cx >= map.Width || cz >= map.Height) return MineRefusedRange;
            byte t = map.Cells[cz * map.Width + cx];
            if (t == Cell.Void || t == Cell.Water) return MineRefusedRange;
            if (!MapGrid.IsWalkableType(t)) return MineRefusedOccupied;
            double x = cx + 0.5;
            double z = cz + 0.5;
            const double k2 = Barricades.PadKeepout * Barricades.PadKeepout;
            for (int team = 0; team < 2; team++)
            {
                PadDef p = map.Pads[team];
                if ((x - p.X) * (x - p.X) + (z - p.Z) * (z - p.Z) <= k2) return MineRefusedOccupied;
            }
            Mine[] mines = w.Demolitions.Mines;
            for (int s = 0; s < mines.Length; s++)
            {
                Mine m = mines[s];
                if (m.Active && m.Cx == cx && m.Cz == cz) return MineRefusedOccupied;
            }
            return 0;
        }

        /// <summary>Would actor id's mine for aim (aimX, aimZ) be accepted? (bots ask first, so they never press a refused cast)</summary>
        public static bool CanPlaceMine(World w, int id, double aimX, double aimZ)
        {
            Actor a = w.Actors[id];
            if (a == null || !a.Alive || a.Cls != ClassId.Demolitions || a.AbilityCd > 0) return false;
            ClampAim(w, id, aimX, aimZ, w.Config.Abilities.DemolitionsMine.PlaceRange);
            return MineCellRefusal(w, JsMath.FloorToInt(PtX), JsMath.FloorToInt(PtZ)) == 0;
        }

        /// <summary>Actor id (a living Demolitions Expert) lays a mine for aim (aimX, aimZ). Returns MinePlacedCode or the refusal code; the cooldown is spent only on success.</summary>
        public static int PlaceMine(World w, int id, double aimX, double aimZ)
        {
            Actor a = w.Actors[id];
            DemolitionsMineConfig cfg = w.Config.Abilities.DemolitionsMine;
            ClampAim(w, id, aimX, aimZ, cfg.PlaceRange);
            double tx = PtX;
            double tz = PtZ;
            if (a == null || !a.Alive || a.Cls != ClassId.Demolitions) return MineRefusedOccupied;
            int cx = JsMath.FloorToInt(tx);
            int cz = JsMath.FloorToInt(tz);
            int why = MineCellRefusal(w, cx, cz);
            if (why != 0)
            {
                World.PushEvent(w, SimEventType.Ability, id, -1, tx, tz, why);
                return why;
            }
            DemolitionsState s = w.Demolitions;
            // A free slot; an owner at its cap recycles its oldest mine (lowest slot on a tie).
            int slot = -1;
            int own = 0;
            int oldest = -1;
            for (int k = 0; k < s.Mines.Length; k++)
            {
                Mine m = s.Mines[k];
                if (!m.Active)
                {
                    if (slot < 0) slot = k;
                    continue;
                }
                if (m.Owner != id) continue;
                own++;
                if (oldest < 0 || m.PlacedTick < s.Mines[oldest].PlacedTick) oldest = k;
            }
            if (own >= cfg.MaxActivePerOwner && oldest >= 0)
            {
                slot = oldest;
                Mine old = s.Mines[oldest];
                World.PushEvent(w, SimEventType.MineRemoved, old.Owner, oldest, old.Cx + 0.5, old.Cz + 0.5, MineRecycled, null, DamageCause.None, cfg.BlastRadius);
            }
            if (slot < 0)
            {
                // Unreachable: the pool is ActorCapacity x MaxActivePerOwner.
                World.PushEvent(w, SimEventType.Ability, id, -1, tx, tz, MineRefusedOccupied);
                return MineRefusedOccupied;
            }
            Mine mine = s.Mines[slot];
            mine.Active = true;
            mine.Armed = false;
            mine.Owner = id;
            mine.Team = a.Team;
            mine.Cx = cx;
            mine.Cz = cz;
            mine.PlacedTick = w.Tick;
            mine.ArmTick = w.Tick + Durations.T30(w.Config, cfg.ArmTicks);
            mine.ExpireTick = w.Tick + Durations.T30(w.Config, cfg.LifetimeTicks);
            a.AbilityCd = w.Config.Class(ClassId.Demolitions).AbilityCooldown;
            a.AnimAction = 0;
            s.Stats.MinesPlaced++;
            World.PushEvent(w, SimEventType.Ability, id, -1, cx + 0.5, cz + 0.5, MinePlacedCode);
            World.PushEvent(w, SimEventType.MinePlaced, id, slot, cx + 0.5, cz + 0.5, mine.ArmTick, null, DamageCause.None, cfg.BlastRadius);
            return MinePlacedCode;
        }

        /// <summary>Damage every living enemy of team within radius (+ its own radius) of (x, z), credited to owner. Returns how many were hit.</summary>
        private static int BlastActors(World w, int owner, TeamId team, double x, double z, double radius, double damage)
        {
            DemolitionsStats st = w.Demolitions.Stats;
            bool ownerIsBot = owner >= 0 && owner < w.ActorCount && !w.Actors[owner].IsHuman;
            int hit = 0;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (!t.Alive || t.Team == team) continue;
                // DG-16 passive window (Bots.PassiveSeconds, default 0): a bot's blast never hurts the human slot yet.
                if (ownerIsBot && t.IsHuman && w.Match.Time < w.Config.Bots.PassiveSeconds) continue;
                double r = radius + w.Config.Class(t.Cls).Radius;
                double dx = w.X[j] - x;
                double dz = w.Z[j] - z;
                if (dx * dx + dz * dz > r * r) continue;
                double dealt = Damage.ApplyDamage(w, j, damage, owner, x, z, DamageCause.Explosion);
                st.Damage += dealt;
                if (dealt > 0) hit++;
                if (dealt > 0 && !t.Alive) st.Kills++;
            }
            return hit;
        }

        private static void MinesTick(World w, DemolitionsState s)
        {
            MapDef map = w.Map;
            DemolitionsMineConfig cfg = w.Config.Abilities.DemolitionsMine;
            for (int k = 0; k < s.Mines.Length; k++)
            {
                Mine m = s.Mines[k];
                if (!m.Active) continue;
                double x = m.Cx + 0.5;
                double z = m.Cz + 0.5;
                // Expired, or its ground is gone (dug, cratered, barricaded): removed without a blast.
                bool expired = w.Tick >= m.ExpireTick;
                if (expired || !MapGrid.IsWalkableType(map.Cells[m.Cz * map.Width + m.Cx]))
                {
                    m.Active = false;
                    World.PushEvent(w, SimEventType.MineRemoved, m.Owner, k, x, z, expired ? MineExpired : MineGroundGone, null, DamageCause.None, cfg.BlastRadius);
                    continue;
                }
                if (w.Tick < m.ArmTick) continue;
                if (!m.Armed)
                {
                    m.Armed = true;
                    World.PushEvent(w, SimEventType.MineArmed, m.Owner, k, x, z, m.ExpireTick, null, DamageCause.None, cfg.BlastRadius);
                }
                int trigger = -1;
                for (int j = 0; j < w.ActorCount; j++)
                {
                    Actor t = w.Actors[j];
                    if (!t.Alive || t.Team == m.Team || t.SpawnProtect > 0 || t.AirLen > 0) continue;
                    if (JsMath.FloorToInt(w.X[j]) == m.Cx && JsMath.FloorToInt(w.Z[j]) == m.Cz)
                    {
                        trigger = j;
                        break;
                    }
                }
                if (trigger < 0) continue;
                m.Active = false;
                s.Stats.MinesTriggered++;
                World.PushEvent(w, SimEventType.MineTriggered, trigger, m.Owner, x, z, k, null, DamageCause.None, cfg.BlastRadius);
                int hit = BlastActors(w, m.Owner, m.Team, x, z, cfg.BlastRadius, cfg.Damage);
                World.PushEvent(w, SimEventType.Ability, -1, m.Owner, x, z, MineBlastCode);
                World.PushEvent(w, SimEventType.Explosion, m.Owner, ExplosionMine, x, z, hit, null, DamageCause.Explosion, cfg.BlastRadius);
            }
        }

        // ------------------------------------------------------------------------------------------------------ TNT

        /// <summary>Ticks until actor id may throw TNT again (0 = ready).</summary>
        public static int TntWaitTicks(World w, int id)
        {
            int left = w.Demolitions.TntReadyTick[id] - w.Tick;
            return left > 0 ? left : 0;
        }

        /// <summary>Actor id (a living Demolitions Expert) throws TNT at aim (aimX, aimZ). Returns TntThrownCode or a TntRefused* code (silent). Throwing ends spawn protection.</summary>
        public static int ThrowTnt(World w, int id, double aimX, double aimZ)
        {
            Actor a = w.Actors[id];
            if (a == null || !a.Alive || a.Cls != ClassId.Demolitions) return TntRefusedClass;
            DemolitionsState s = w.Demolitions;
            if (w.Tick < s.TntReadyTick[id]) return TntRefusedCooldown;
            int slot = -1;
            for (int k = 0; k < s.Bombs.Length; k++)
            {
                if (!s.Bombs[k].Active)
                {
                    slot = k;
                    break;
                }
            }
            if (slot < 0)
            {
                s.Stats.PoolRefusals++;
                return TntRefusedPool;
            }
            DemolitionsTntConfig cfg = w.Config.Abilities.DemolitionsTnt;
            ClampAim(w, id, aimX, aimZ, cfg.ThrowRange);
            Bomb b = s.Bombs[slot];
            b.Active = true;
            b.Landed = false;
            b.Owner = id;
            b.Team = a.Team;
            b.X = PtX;
            b.Z = PtZ;
            b.FromX = w.X[id];
            b.FromZ = w.Z[id];
            b.ThrownTick = w.Tick;
            b.BlastTick = w.Tick + Durations.T30(w.Config, cfg.FuseTicks);
            b.LandTick = Math.Min(b.BlastTick, w.Tick + Durations.T30(w.Config, cfg.FlightTicks));
            s.TntReadyTick[id] = w.Tick + Durations.T30(w.Config, cfg.CooldownTicks);
            s.Stats.TntThrown++;
            a.AnimAction = 0;
            a.SpawnProtect = 0;
            World.PushEvent(w, SimEventType.Ability, id, -1, b.X, b.Z, TntThrownCode);
            World.PushEvent(w, SimEventType.TntThrown, id, slot, b.X, b.Z, b.LandTick, null, DamageCause.None, cfg.BlastRadius);
            return TntThrownCode;
        }

        /// <summary>The blast of bomb b: actors, Kaiju, barricades, breakable walls, then the crater through the CM-30 rules.</summary>
        private static void BlowBomb(World w, int slot, Bomb b, DemolitionsState s)
        {
            MapDef map = w.Map;
            DemolitionsTntConfig cfg = w.Config.Abilities.DemolitionsTnt;
            double bx = b.X;
            double bz = b.Z;
            bool gapOk = w.Tick - w.WallChangeTick >= Durations.T30(w.Config, w.Config.Abilities.MinerTerraform.MinGapTicks); // before this blast's own breaks
            s.Stats.TntBlasts++;
            int hit = BlastActors(w, b.Owner, b.Team, bx, bz, cfg.BlastRadius, cfg.Damage);
            Kaiju k = w.Kaiju;
            if (k.State != KaijuState.Egg)
            {
                double kr = cfg.BlastRadius + w.Config.Kaiju.Radius;
                if ((k.X - bx) * (k.X - bx) + (k.Z - bz) * (k.Z - bz) <= kr * kr) Kaiju.DamageKaiju(w, b.Owner, cfg.Damage);
            }
            Barricades.DamageBarricadesInArc(w, b.Owner, bx, bz, 1, 0, cfg.BlastRadius, -1, cfg.Damage);

            // Breakable walls in the radius: destroyed outright (they only ever ADD walkable ground, so no route check).
            double R = cfg.BlastRadius;
            int x0 = Math.Max(0, JsMath.FloorToInt(bx - R));
            int x1 = Math.Min(map.Width - 1, JsMath.FloorToInt(bx + R));
            int z0 = Math.Max(0, JsMath.FloorToInt(bz - R));
            int z1 = Math.Min(map.Height - 1, JsMath.FloorToInt(bz + R));
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    int i = cz * map.Width + cx;
                    byte t = map.Cells[i];
                    if (t != Cell.BreakableIntact && t != Cell.BreakableCracked) continue;
                    double dx = cx + 0.5 - bx;
                    double dz = cz + 0.5 - bz;
                    if (dx * dx + dz * dz > R * R) continue;
                    map.WallHp[i] = 0;
                    MapGrid.SetCell(w, cx, cz, Cell.BreakableDestroyed);
                    s.Stats.WallsBroken++;
                    World.PushEvent(w, SimEventType.WallBreak, b.Owner, i, cx + 0.5, cz + 0.5, Cell.BreakableDestroyed);
                }
            }

            // The crater: Floor / Bridge cells within CarveRadius, row-major, each decided by the CM-30 dig rules.
            double C = cfg.CarveRadius;
            bool inMap = bx >= 0 && bz >= 0 && bx < map.Width && bz < map.Height;
            if (inMap && !gapOk)
            {
                s.Stats.RateRefusals++;
                World.PushEvent(w, SimEventType.Terraform, b.Owner, -1, bx, bz, Terraform.RefusedRate);
            }
            else if (inMap)
            {
                int cx0 = Math.Max(0, JsMath.FloorToInt(bx - C));
                int cx1 = Math.Min(map.Width - 1, JsMath.FloorToInt(bx + C));
                int cz0 = Math.Max(0, JsMath.FloorToInt(bz - C));
                int cz1 = Math.Min(map.Height - 1, JsMath.FloorToInt(bz + C));
                for (int cz = cz0; cz <= cz1; cz++)
                {
                    for (int cx = cx0; cx <= cx1; cx++)
                    {
                        int i = cz * map.Width + cx;
                        byte t = map.Cells[i];
                        if (t != Cell.Floor && t != Cell.Bridge) continue;
                        double dx = cx + 0.5 - bx;
                        double dz = cz + 0.5 - bz;
                        if (dx * dx + dz * dz > C * C + 1e-9) continue;
                        int why = Terraform.DigRefusal(w, cx, cz);
                        if (why != 0)
                        {
                            if (why == Terraform.RefusedRoute) s.Stats.RouteRefusals++;
                            World.PushEvent(w, SimEventType.Terraform, b.Owner, i, cx + 0.5, cz + 0.5, why);
                            continue;
                        }
                        MapGrid.SetCell(w, cx, cz, Cell.Void);
                        s.Stats.CellsCarved++;
                        World.PushEvent(w, SimEventType.Terraform, b.Owner, i, cx + 0.5, cz + 0.5, Terraform.Dug);
                    }
                }
            }
            World.PushEvent(w, SimEventType.Ability, -1, b.Owner, bx, bz, TntBlastCode);
            World.PushEvent(w, SimEventType.Explosion, b.Owner, ExplosionTnt, bx, bz, hit, null, DamageCause.Explosion, cfg.BlastRadius);
        }

        private static void BombsTick(World w, DemolitionsState s)
        {
            double radius = w.Config.Abilities.DemolitionsTnt.BlastRadius;
            for (int k = 0; k < s.Bombs.Length; k++)
            {
                Bomb b = s.Bombs[k];
                if (!b.Active) continue;
                if (!b.Landed && w.Tick >= b.LandTick)
                {
                    b.Landed = true;
                    World.PushEvent(w, SimEventType.TntLanded, b.Owner, k, b.X, b.Z, b.BlastTick, null, DamageCause.None, radius);
                }
                if (w.Tick < b.BlastTick) continue;
                b.Active = false;
                BlowBomb(w, k, b, s);
            }
        }

        // -------------------------------------------------------------------------------------------------- systems

        /// <summary>
        /// Input half (end of Abilities.AbilitiesSystem, before movement): a living, standing Demolitions Expert throws TNT on
        /// the RISING edge of InputFrame.Terraform (the DigPlace binding). The aim follows World.HasAim (no aim: ahead).
        /// </summary>
        public static void DemolitionsInputSystem(World w)
        {
            DemolitionsState s = w.Demolitions;
            int n = w.ActorCount;
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                byte pressed = w.Inputs[i].Terraform ? (byte)1 : (byte)0;
                bool rising = pressed == 1 && s.PrevPressed[i] == 0;
                s.PrevPressed[i] = pressed;
                if (!rising || !a.Alive || a.Cls != ClassId.Demolitions || a.StandUp > 0) continue;
                bool hasAim = w.HasAim[i] == 1;
                ThrowTnt(w, i, hasAim ? w.Inputs[i].AimX : double.NaN, hasAim ? w.Inputs[i].AimZ : double.NaN);
            }
        }

        /// <summary>
        /// Blast half (Combat.CombatSystem, after movement and the melee swings, before the corpse-meat drop so a mine or TNT
        /// kill leaves its meat): mines arm and trigger on this step's positions, then due bombs land and blow.
        /// </summary>
        public static void DemolitionsBlastSystem(World w)
        {
            DemolitionsState s = w.Demolitions;
            MinesTick(w, s);
            BombsTick(w, s);
        }

        /// <summary>The Space press of a Demolitions Expert (Abilities.AbilitiesSystem): a mine at its aim, or straight ahead without one.</summary>
        public static void CastMine(World w, int id)
        {
            bool hasAim = w.HasAim[id] == 1;
            PlaceMine(w, id, hasAim ? w.Inputs[id].AimX : double.NaN, hasAim ? w.Inputs[id].AimZ : double.NaN);
        }

        /// <summary>Bots: queue one TNT throw at (x, z) for bot id (BotFsm). BotBrain.FillBotInput turns it into one press.</summary>
        public static void RequestBotTnt(World w, int id, double x, double z)
        {
            DemolitionsState s = w.Demolitions;
            s.BotTnt[id] = 1;
            s.BotTntX[id] = JsMath.F32(x);
            s.BotTntZ[id] = JsMath.F32(z);
        }

        /// <summary>Bots: take bot id's queued throw into output (aim + Terraform press); false when none is queued or it died.</summary>
        public static bool TakeBotTnt(World w, int id, InputFrame output)
        {
            DemolitionsState s = w.Demolitions;
            if (s.BotTnt[id] != 1) return false;
            s.BotTnt[id] = 0;
            if (!w.Actors[id].Alive) return false;
            output.AimX = s.BotTntX[id];
            output.AimZ = s.BotTntZ[id];
            output.Terraform = true;
            return true;
        }
    }
}
