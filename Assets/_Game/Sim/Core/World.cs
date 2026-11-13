using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;

namespace CriticalMass.Sim.Core
{
    /// <summary>Kaiju navigation scratch (CM-08 P1), allocated once per World so two worlds never share it.</summary>
    public sealed class KaijuNav
    {
        /// <summary>Shadow map for ComputeFlowField and the steering step: only clearance cells are walkable.</summary>
        public MapDef Clear;
        /// <summary>World.GridVersion the shadow cells were built for (-1 = never).</summary>
        public int ClearGridVersion;
        /// <summary>Reused flow-field buffer toward the current goal cell.</summary>
        public FlowField Field;
        /// <summary>World.WallVersion the field was built for (-1 = never).</summary>
        public int FieldWallVersion;
        /// <summary>Goal cell of the current field (-1 = none).</summary>
        public int GoalCx;
        public int GoalCz;
        /// <summary>Seconds until the field may be rebuilt for a new goal cell (1 s retarget).</summary>
        public double RepathIn;
        /// <summary>Seconds until the next target selection (0.5 s).</summary>
        public double RetargetIn;
        /// <summary>Hatch point (the neutral Kaiju idles here).</summary>
        public double HomeX;
        public double HomeZ;
    }

    /// <summary>CM-30 miner dig / place state (browser TerraformState), per-actor arrays at ActorCapacity.</summary>
    public sealed class TerraformState
    {
        /// <summary>Blocks each actor carries (0 = empty-handed; at most MinerTerraform.CarryMax, DG-09).</summary>
        public byte[] Carried;
        /// <summary>InputFrame.Terraform as it was last step (1 = held), for the rising edge.</summary>
        public byte[] PrevPressed;
        /// <summary>World.Tick from which each team may change terrain again, indexed by (int)TeamId.</summary>
        public int[] TeamReadyTick;
        /// <summary>DG-09 (U-053): each actor's dig / place mode (0 = Dig, 1 = Place; see TerraformMode).</summary>
        public byte[] Mode;
    }

    /// <summary>CM-31 knock-down decoration state (browser ToppleState), one slot per map decoration.</summary>
    public sealed class ToppleState
    {
        /// <summary>Swings left before the piece falls (HitsToTopple standing; 0 once fallen).</summary>
        public byte[] Hp;
        /// <summary>Topple.Standing (0), Topple.Ramp (1) or Topple.Block (2).</summary>
        public byte[] Fallen;
        /// <summary>World.Tick the piece fell (the view animates the fall over Topple.FallTicks).</summary>
        public int[] FallTick;
    }

    /// <summary>CM-36 shrine cleanse state (browser CleanseState), allocated once per World and reset in place. All timers count sim ticks.</summary>
    public sealed class CleanseState
    {
        /// <summary>Per team (0 azure, 1 crimson): sim ticks until that team may cleanse again; 0 = ready.</summary>
        public int[] Cooldown;
        /// <summary>Sim ticks left of the cleanse in flight; 0 = none.</summary>
        public int ActiveTicks;
        /// <summary>Team index of the cleanse in flight (-1 none).</summary>
        public int Team;
        /// <summary>Actor that started the cleanse in flight (-1 none or scripted).</summary>
        public int Actor;
        /// <summary>Instability the cleanse in flight drains per tick (fixed at the start).</summary>
        public double PerTick;
        /// <summary>Instability the cleanse in flight has drained so far.</summary>
        public double Drained;
        /// <summary>Per actor: consecutive ticks of interact held empty-handed at the shrine while a cleanse is possible.</summary>
        public ushort[] Hold;
        /// <summary>Per actor: 1 once interact was released since the actor last carried a load.</summary>
        public byte[] Armed;
        /// <summary>Cleanses started this match (both teams).</summary>
        public int Count;
        /// <summary>Instability drained by cleanses this match.</summary>
        public double Removed;
    }

    /// <summary>The whole simulation state; per-actor arrays are allocated once at ActorCapacity and reused by ResetWorld.</summary>
    public sealed class World
    {
        public WorldOptions Opts;
        /// <summary>The config every Sim function reads (set once by CreateWorld from WorldOptions.Config).</summary>
        public GameConfig Config;
        public Rng Rng;
        public MapDef Map;
        /// <summary>Bumps on every grid change (render watches it).</summary>
        public int GridVersion;
        /// <summary>Bumps only on non-barricade grid changes (breakable walls; bot flow fields watch it).</summary>
        public int WallVersion;
        /// <summary>CM-30: World.Tick of the last SetCell that bumped WallVersion (wall breaks, terraforming, topples); NeverTick after a (re)start.</summary>
        public int WallChangeTick;
        /// <summary>CM-30: miner dig / place state.</summary>
        public TerraformState Terraform;
        /// <summary>CM-31: knock-down decoration state, one slot per map decoration.</summary>
        public ToppleState Topple;
        /// <summary>CM-36: the shrine cleanse (Cleanse.cs).</summary>
        public CleanseState Cleanse;
        /// <summary>DG-22: land mines, thrown TNT and their counters (Demolitions.cs).</summary>
        public DemolitionsState Demolitions;
        /// <summary>Bumps on every ResetWorld. Views must drop cached per-match state when it changes.</summary>
        public int Epoch;
        public int Tick;
        public int ActorCount;
        public double[] X;
        public double[] Z;
        public double[] PrevX;
        public double[] PrevZ;
        /// <summary>CM-21: sim velocity (units / s) per actor.</summary>
        public double[] Vx;
        public double[] Vz;
        public Actor[] Actors;
        public InputFrame[] Inputs;
        /// <summary>1 when Inputs[id] carries a real aim point, 0 for "no aim".</summary>
        public byte[] HasAim;
        public ResourceNode[] Nodes;
        /// <summary>CM-35: loose meat orbs, fixed capacity OrbCapacity; inactive slots have Active = false. Only Combat.DropCorpseMeat and ResourcesSystem write it.</summary>
        public LooseOrb[] Orbs;
        /// <summary>CM-35: per actor, 1 once the current death's meat orb has dropped, back to 0 while the actor is alive.</summary>
        public byte[] MeatDropped;
        /// <summary>CM-35 telemetry: times SpawnLooseOrb found the pool full and recycled the orb with the least time left.</summary>
        public int OrbOverflows;
        /// <summary>Fixed capacity BarricadeCapacity; inactive slots have Active = false.</summary>
        public Barricade[] Barricades;
        public Kaiju Kaiju;
        /// <summary>Debug: hatch now through the same code path as the natural hatch.</summary>
        public bool ForceHatch;
        public KaijuNav KaijuNav;
        public MatchState Match;
        /// <summary>Fixed ring of EventCapacity slots.</summary>
        public SimEvent[] Events;
        /// <summary>Monotonically increasing write counter; slot = EventHead % EventCapacity. Reset to 0 by ResetWorld.</summary>
        public int EventHead;
        public SpatialHash Hash;

        /// <summary>Browser ACTOR_CAPACITY.</summary>
        public static int ActorCapacity(GameConfig c)
        {
            return 2 * c.TeamSizeMax;
        }

        /// <summary>Browser EVENT_CAPACITY.</summary>
        public const int EventCapacity = 512;

        /// <summary>CM-35 browser ORB_CAPACITY: two live orbs per actor (a death is at least Respawn.DelaySeconds apart).</summary>
        public static int OrbCapacity(GameConfig c)
        {
            return 2 * ActorCapacity(c);
        }

        /// <summary>Browser BARRICADE_CAPACITY.</summary>
        public static int BarricadeCapacity(GameConfig c)
        {
            return 2 * c.TeamSizeMax * (int)c.Abilities.MinerBarricade.MaxActivePerMiner;
        }

        /// <summary>CM-30 browser NEVER_TICK: the WallChangeTick after a (re)start (far in the past).</summary>
        public const int NeverTick = -1_000_000;

        /// <summary>Golden-angle spawn spiral around the team pad.</summary>
        private const double SpawnAngleStep = 2.39996;
        private const double SpawnRadiusBase = 1;
        private const double SpawnRadiusStep = 0.7;

        /// <summary>Team size clamped to 1..TeamSizeMax (an int is always finite and integral).</summary>
        public static int ClampTeamSize(GameConfig c, int n)
        {
            int v = n;
            if (v < 1) return 1;
            if (v > c.TeamSizeMax) return c.TeamSizeMax;
            return v;
        }

        /// <summary>The other team.</summary>
        public static TeamId OtherTeam(TeamId t)
        {
            return t == TeamId.Azure ? TeamId.Crimson : TeamId.Azure;
        }

        /// <summary>Spawn point for slot (0-based index within the team) of team.</summary>
        public static void SpawnPoint(MapDef map, TeamId team, int slot, out double x, out double z)
        {
            PadDef pad = map.Pads[(int)team];
            double a = slot * SpawnAngleStep;
            double r = SpawnRadiusBase + (slot % 3) * SpawnRadiusStep;
            x = pad.X + JsMath.Cos(a) * r;
            z = pad.Z + JsMath.Sin(a) * r;
        }

        /// <summary>Team-local slot of an actor id (ids 0..N-1 are the human's team, N..2N-1 the other team).</summary>
        public static int TeamSlot(World w, int id)
        {
            int n = w.ActorCount >> 1;
            return id < n ? id : id - n;
        }

        /// <summary>BuildRoster scratch, one slot per class (allocation-free per call).</summary>
        private static readonly int[] RosterCounts = new int[ClassIds.All.Count];
        private static readonly double[] RosterFrac = new double[ClassIds.All.Count];

        /// <summary>Class roster for one team of n players (largest remainder, deterministic), written into output[0..n-1].</summary>
        public static void BuildRoster(GameConfig c, int n, ClassId? humanClass, ClassId[] output)
        {
            int classCount = ClassIds.All.Count;
            int[] counts = RosterCounts;
            double[] frac = RosterFrac;
            Array.Fill(counts, 0);
            Array.Fill(frac, 0.0);
            int used = 0;
            for (int k = 0; k < classCount; k++)
            {
                double q = n * c.ClassMix(ClassIds.All[k]);
                counts[k] = JsMath.FloorToInt(q + 1e-9);
                frac[k] = q - counts[k];
                used += counts[k];
            }
            for (int left = n - used; left > 0; left--)
            {
                int best = 0;
                for (int k = 1; k < classCount; k++) if (frac[k] > frac[best] + 1e-9) best = k;
                counts[best]++;
                frac[best] = -1;
            }
            int w = 0;
            if (humanClass.HasValue)
            {
                int hk = (int)humanClass.Value;
                if (counts[hk] == 0)
                {
                    int donor = 0;
                    for (int k = 1; k < classCount; k++) if (counts[k] > counts[donor]) donor = k;
                    counts[donor]--;
                    counts[hk]++;
                }
                output[w++] = humanClass.Value;
                counts[hk]--;
            }
            for (int k = 0; k < classCount; k++) for (int cc = 0; cc < counts[k]; cc++) output[w++] = ClassIds.All[k];
        }

        private static Actor BlankActor(int id)
        {
            return new Actor
            {
                Id = id,
                Team = TeamId.Azure,
                Cls = ClassId.Warrior,
                IsHuman = false,
                Alive = false,
                Hp = 0,
                MaxHp = 0,
                Facing = 0,
                AttackCd = 0,
                AbilityCd = 0,
                AbilityActive = 0,
                SlowTimer = 0,
                SpawnProtect = 0,
                RespawnIn = 0,
                Carry = CarryKind.None,
                CarryAmount = 0,
                CarryMeat = 0,
                MeatPickup = true,
                GatherProgress = 0,
                GatherNodeId = -1,
                LastAttacker = -1,
                LostCargo = new LostCargo { Kind = CarryKind.None, Amount = 0 },
                Kills = 0,
                Deaths = 0,
                HillSeconds = 0,
                AiState = AiState.None,
                AnimAction = 0,
                Moving = false,
                PendingClass = null,
                // ITEM CM-84: added by its owner (ClassSwitchCd = 0, ClassSwitches = 0)
                StandUp = 0,
                AirLen = 0,
                AirTick = 0,
                AirFromX = 0,
                AirFromZ = 0,
                AirToX = 0,
                AirToZ = 0,
                JumpBuffer = 0,
                UnsupportedTicks = 0,
                LastCause = DamageCause.None,
                DeliverTicks = 0,
            };
        }

        private static InputFrame BlankInput()
        {
            return new InputFrame
            {
                MoveX = 0,
                MoveZ = 0,
                AimX = 0,
                AimZ = 0,
                Attack = false,
                Ability = false,
                Interact = false,
                ClassPick = -1,
                Terraform = false,
                Jump = false,
            };
        }

        private static MatchState BlankMatch()
        {
            var hillSeconds = new PerTeam();
            hillSeconds.Set(0, 0);
            var hillCount = new PerTeam();
            hillCount.Set(0, 0);
            var allegiance = new PerTeam();
            allegiance.Set(0, 0);
            var intercepted = new PerTeam();
            intercepted.Set(0, 0);
            return new MatchState
            {
                Time = 0,
                Phase = MatchPhase.Playing,
                HillSeconds = hillSeconds,
                HillState = HillState.Neutral,
                HillCount = hillCount,
                Allegiance = allegiance,
                Delivered = new[]
                {
                    new DeliveredTally { Biomass = 0, Corruption = 0, Plant = 0, Meat = 0 },
                    new DeliveredTally { Biomass = 0, Corruption = 0, Plant = 0, Meat = 0 },
                },
                Instability = 0,
                BerserkLocked = false,
                Hatched = false,
                Winner = MatchResult.None,
                Intercepted = intercepted,
            };
        }

        /// <summary>Module-scope scratch for init (never touched by step); grown to TeamSizeMax on first use.</summary>
        private static ClassId[] RosterScratch = new ClassId[0];

        /// <summary>(Re)initialise every piece of match state in place. Shared by CreateWorld and ResetWorld.</summary>
        private static void InitWorld(World world, WorldOptions opts)
        {
            GameConfig cfg = world.Config;
            int n = ClampTeamSize(cfg, opts.TeamSize);
            world.Opts.Seed = opts.Seed;
            world.Opts.TeamSize = n;
            world.Opts.HumanTeam = opts.HumanTeam;
            world.Opts.HumanClass = opts.HumanClass;
            world.Opts.BotDifficulty = opts.BotDifficulty; // DG-21
            world.Opts.AllyBotDifficulty = opts.AllyBotDifficulty;
            Rng.SeedRng(world.Rng, unchecked((uint)opts.Seed));
            world.Tick = 0;
            world.ActorCount = 2 * n;
            world.EventHead = 0;
            // CM-30: no wall change yet; every carry, edge and team cooldown cleared (DG-09: every mode back to Dig).
            world.WallChangeTick = NeverTick;
            Array.Clear(world.Terraform.Carried, 0, world.Terraform.Carried.Length);
            Array.Clear(world.Terraform.PrevPressed, 0, world.Terraform.PrevPressed.Length);
            Array.Clear(world.Terraform.Mode, 0, world.Terraform.Mode.Length);
            world.Terraform.TeamReadyTick[0] = 0;
            world.Terraform.TeamReadyTick[1] = 0;
            // CM-31: every decoration stands again (PaintSkybreakBasin restores the cells a topple changed).
            Array.Fill(world.Topple.Hp, (byte)cfg.Abilities.Topple.HitsToTopple);
            Array.Clear(world.Topple.Fallen, 0, world.Topple.Fallen.Length);
            Array.Clear(world.Topple.FallTick, 0, world.Topple.FallTick.Length);

            if (RosterScratch.Length < cfg.TeamSizeMax) RosterScratch = new ClassId[cfg.TeamSizeMax];

            MapDef map = world.Map;
            int cap = ActorCapacity(cfg);
            for (int team = 0; team < 2; team++)
            {
                TeamId teamId = team == 0 ? opts.HumanTeam : OtherTeam(opts.HumanTeam);
                BuildRoster(cfg, n, team == 0 ? opts.HumanClass : (ClassId?)null, RosterScratch);
                for (int slot = 0; slot < n; slot++)
                {
                    int id = team * n + slot;
                    Actor a = world.Actors[id];
                    ClassId cls = RosterScratch[slot];
                    ClassConfig cc = cfg.Class(cls);
                    ResetActorFields(a);
                    a.Team = teamId;
                    a.Cls = cls;
                    a.IsHuman = id == 0;
                    a.Alive = true;
                    a.Hp = cc.MaxHp;
                    a.MaxHp = cc.MaxHp;
                    a.SpawnProtect = cfg.Respawn.ProtectionSeconds;
                    SpawnPoint(map, teamId, slot, out double sx, out double sz);
                    world.X[id] = JsMath.F32(sx);
                    world.Z[id] = JsMath.F32(sz);
                    world.PrevX[id] = JsMath.F32(sx);
                    world.PrevZ[id] = JsMath.F32(sz);
                    world.Vx[id] = JsMath.F32(0);
                    world.Vz[id] = JsMath.F32(0);
                    a.Facing = JsMath.Atan2(map.Hill.Z - sz, map.Hill.X - sx);
                    InputFrame inp = world.Inputs[id];
                    ResetInput(inp);
                    inp.AimX = sx;
                    inp.AimZ = sz;
                    world.HasAim[id] = 0;
                }
            }
            for (int id = world.ActorCount; id < cap; id++)
            {
                ResetActorFields(world.Actors[id]);
                world.X[id] = JsMath.F32(0);
                world.Z[id] = JsMath.F32(0);
                world.PrevX[id] = JsMath.F32(0);
                world.PrevZ[id] = JsMath.F32(0);
                world.Vx[id] = JsMath.F32(0);
                world.Vz[id] = JsMath.F32(0);
                ResetInput(world.Inputs[id]);
                world.HasAim[id] = 0;
            }

            int nid = 0;
            foreach (PointXZ p in map.BiomassNodes)
            {
                ResourceNode node = world.Nodes[nid];
                node.Id = nid++;
                node.Kind = NodeKind.Biomass;
                node.X = p.X;
                node.Z = p.Z;
                node.MaxCharges = (int)cfg.Resources.BiomassNodeCharges;
                node.Charges = node.MaxCharges;
                node.RegrowIn = 0;
            }
            foreach (PointXZ p in map.CorruptionNodes)
            {
                ResourceNode node = world.Nodes[nid];
                node.Id = nid++;
                node.Kind = NodeKind.Corruption;
                node.X = p.X;
                node.Z = p.Z;
                node.MaxCharges = (int)cfg.Resources.CorruptionNodeCharges;
                node.Charges = node.MaxCharges;
                node.RegrowIn = 0;
            }

            // CM-35: every orb inactive, every drop flag clear.
            foreach (LooseOrb o in world.Orbs)
            {
                o.Active = false;
                o.Kind = BiomassType.Meat;
                o.X = 0;
                o.Z = 0;
                o.Units = 0;
                o.Ttl = 0;
                o.Source = -1;
            }
            Array.Clear(world.MeatDropped, 0, world.MeatDropped.Length);
            world.OrbOverflows = 0;

            foreach (Barricade b in world.Barricades)
            {
                b.Active = false;
                b.Owner = -1;
                b.Team = TeamId.Azure;
                b.Cx = -1;
                b.Cz = -1;
                b.Hp = 0;
                b.Ttl = 0;
                b.PrevCell = 0;
            }

            Kaiju k = world.Kaiju;
            k.State = KaijuState.Egg;
            k.X = map.Shrine.X;
            k.Z = map.Shrine.Z;
            k.PrevX = k.X;
            k.PrevZ = k.Z;
            k.Facing = Math.PI / 2; // facing south, toward the hill
            // CM-08 P2: Kaiju HP scales with team size.
            k.MaxHp = cfg.Kaiju.HpPerTeamMember * n;
            k.Hp = k.MaxHp;
            k.KoIn = 0;
            k.MeleeCd = 0;
            k.SlamCd = 0;
            k.TelegraphIn = 0;
            k.TelegraphX = 0;
            k.TelegraphZ = 0;
            k.MeleeWindupIn = 0;
            k.MeleeTargetId = -1;
            k.TargetId = -1;
            Array.Fill(k.Threat, JsMath.F32(0));
            world.ForceHatch = false;
            KaijuNav nav = world.KaijuNav;
            nav.ClearGridVersion = -1;
            nav.FieldWallVersion = -1;
            nav.GoalCx = -1;
            nav.GoalCz = -1;
            nav.RepathIn = 0;
            nav.RetargetIn = 0;
            nav.HomeX = map.Shrine.X;
            nav.HomeZ = map.Shrine.Z;

            MatchState m = world.Match;
            m.Time = 0;
            m.Phase = MatchPhase.Playing;
            m.HillSeconds.Azure = 0;
            m.HillSeconds.Crimson = 0;
            m.HillState = HillState.Neutral;
            m.HillCount.Azure = 0;
            m.HillCount.Crimson = 0;
            m.Allegiance.Azure = 0;
            m.Allegiance.Crimson = 0;
            m.Delivered[(int)TeamId.Azure].Biomass = 0;
            m.Delivered[(int)TeamId.Azure].Corruption = 0;
            m.Delivered[(int)TeamId.Crimson].Biomass = 0;
            m.Delivered[(int)TeamId.Crimson].Corruption = 0;
            m.Delivered[(int)TeamId.Azure].Plant = 0;
            m.Delivered[(int)TeamId.Azure].Meat = 0;
            m.Delivered[(int)TeamId.Crimson].Plant = 0;
            m.Delivered[(int)TeamId.Crimson].Meat = 0;
            m.Instability = 0;
            m.BerserkLocked = false;
            m.Hatched = false;
            m.Winner = MatchResult.None;
            m.Intercepted.Azure = 0;
            m.Intercepted.Crimson = 0;

            // CM-36: the shrine cleanse, reset in place.
            CleanseState cl = world.Cleanse;
            Array.Clear(cl.Cooldown, 0, cl.Cooldown.Length);
            cl.ActiveTicks = 0;
            cl.Team = -1;
            cl.Actor = -1;
            cl.PerTick = 0;
            cl.Drained = 0;
            Array.Clear(cl.Hold, 0, cl.Hold.Length);
            Array.Clear(cl.Armed, 0, cl.Armed.Length);
            cl.Count = 0;
            cl.Removed = 0;

            // DG-22: mines, bombs and counters, reset in place.
            world.Demolitions.Reset();
            // DG-24: every piece gone (the repaint restored its cells), banks full again.
            world.Builder.Reset();
            world.Healer.Reset(); // DG-40

            SpatialHash.Rebuild(world.Hash, world.X, world.Z, world.Actors, world.ActorCount);
        }

        private static void ResetActorFields(Actor a)
        {
            a.Team = TeamId.Azure;
            a.Cls = ClassId.Warrior;
            a.IsHuman = false;
            a.Alive = false;
            a.Hp = 0;
            a.MaxHp = 0;
            a.Facing = 0;
            a.AttackCd = 0;
            a.AbilityCd = 0;
            a.AbilityActive = 0;
            a.SlowTimer = 0;
            a.SpawnProtect = 0;
            a.RespawnIn = 0;
            a.Carry = CarryKind.None;
            a.CarryAmount = 0;
            a.CarryMeat = 0;
            a.MeatPickup = true;
            a.GatherProgress = 0;
            a.GatherNodeId = -1;
            a.LastAttacker = -1;
            a.LostCargo.Kind = CarryKind.None;
            a.LostCargo.Amount = 0;
            a.Kills = 0;
            a.Deaths = 0;
            a.HillSeconds = 0;
            a.AiState = AiState.None;
            a.AnimAction = 0;
            a.Moving = false;
            a.PendingClass = null;
            // ITEM CM-84: added by its owner (ClassSwitchCd = 0, ClassSwitches = 0)
            a.StandUp = 0;
            a.AirLen = 0;
            a.AirTick = 0;
            a.AirFromX = 0;
            a.AirFromZ = 0;
            a.AirToX = 0;
            a.AirToZ = 0;
            a.JumpBuffer = 0;
            a.UnsupportedTicks = 0;
            a.LastCause = DamageCause.None;
            a.DeliverTicks = 0;
        }

        private static void ResetInput(InputFrame f)
        {
            f.MoveX = 0;
            f.MoveZ = 0;
            f.AimX = 0;
            f.AimZ = 0;
            f.Attack = false;
            f.Ability = false;
            f.Interact = false;
            f.ClassPick = -1;
            f.Terraform = false;
            f.Jump = false;
            f.TerraformSwitch = false;
        }

        /// <summary>Build a world for opts: a fresh Skybreak Basin map and every buffer allocated at capacity.</summary>
        public static World CreateWorld(WorldOptions opts)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            if (opts.Config == null) throw new ArgumentNullException(nameof(opts), "WorldOptions.Config is null");
            GameConfig cfg = opts.Config;
            MapDef built = SkybreakBasinMap.BuildSkybreakBasin(cfg);
            MapDef map = built.WithCells((byte[])built.Cells.Clone());
            int cap = ActorCapacity(cfg);
            var actors = new Actor[cap];
            var inputs = new InputFrame[cap];
            for (int i = 0; i < cap; i++)
            {
                actors[i] = BlankActor(i);
                inputs[i] = BlankInput();
            }
            int nodeCount = map.BiomassNodes.Length + map.CorruptionNodes.Length;
            var nodes = new ResourceNode[nodeCount];
            for (int i = 0; i < nodeCount; i++)
                nodes[i] = new ResourceNode { Id = i, Kind = NodeKind.Biomass, X = 0, Z = 0, Charges = 0, MaxCharges = 0, RegrowIn = 0 };
            int barricadeCap = BarricadeCapacity(cfg);
            int decorationCount = map.Decorations != null ? map.Decorations.Length : 0;
            var barricades = new Barricade[barricadeCap];
            for (int i = 0; i < barricadeCap; i++)
                barricades[i] = new Barricade { Active = false, Owner = -1, Team = TeamId.Azure, Cx = -1, Cz = -1, Hp = 0, Ttl = 0, PrevCell = 0 };
            int orbCap = OrbCapacity(cfg);
            var orbs = new LooseOrb[orbCap];
            for (int i = 0; i < orbCap; i++) orbs[i] = new LooseOrb { Active = false, Kind = BiomassType.Meat, X = 0, Z = 0, Units = 0, Ttl = 0, Source = -1 };
            var events = new SimEvent[EventCapacity];
            for (int i = 0; i < EventCapacity; i++)
                events[i] = new SimEvent { Type = SimEventType.Hit, A = -1, B = -1, X = 0, Z = 0, Value = 0, Bio = null, Cause = DamageCause.None };
            var world = new World
            {
                Opts = new WorldOptions
                {
                    Seed = opts.Seed,
                    TeamSize = opts.TeamSize,
                    HumanTeam = opts.HumanTeam,
                    HumanClass = opts.HumanClass,
                    Config = cfg,
                    BotDifficulty = opts.BotDifficulty,
                    AllyBotDifficulty = opts.AllyBotDifficulty,
                },
                Config = cfg,
                Rng = Rng.CreateRng(unchecked((uint)opts.Seed)),
                Map = map,
                GridVersion = 0,
                WallVersion = 0,
                WallChangeTick = NeverTick,
                Terraform = new TerraformState
                {
                    Carried = new byte[cap],
                    PrevPressed = new byte[cap],
                    TeamReadyTick = new int[2],
                    Mode = new byte[cap],
                },
                Topple = new ToppleState
                {
                    Hp = new byte[decorationCount],
                    Fallen = new byte[decorationCount],
                    FallTick = new int[decorationCount],
                },
                Cleanse = new CleanseState
                {
                    Cooldown = new int[2],
                    ActiveTicks = 0,
                    Team = -1,
                    Actor = -1,
                    PerTick = 0,
                    Drained = 0,
                    Hold = new ushort[cap],
                    Armed = new byte[cap],
                    Count = 0,
                    Removed = 0,
                },
                Demolitions = DemolitionsState.Create(cfg),
                Epoch = 0,
                Tick = 0,
                ActorCount = 0,
                X = new double[cap],
                Z = new double[cap],
                PrevX = new double[cap],
                PrevZ = new double[cap],
                Vx = new double[cap],
                Vz = new double[cap],
                Actors = actors,
                Inputs = inputs,
                HasAim = new byte[cap],
                Nodes = nodes,
                Orbs = orbs,
                MeatDropped = new byte[cap],
                OrbOverflows = 0,
                Barricades = barricades,
                Kaiju = new Kaiju
                {
                    State = KaijuState.Egg,
                    X = 0,
                    Z = 0,
                    PrevX = 0,
                    PrevZ = 0,
                    Facing = 0,
                    Hp = 0,
                    MaxHp = 0,
                    KoIn = 0,
                    MeleeCd = 0,
                    SlamCd = 0,
                    TelegraphIn = 0,
                    TelegraphX = 0,
                    TelegraphZ = 0,
                    MeleeWindupIn = 0,
                    MeleeTargetId = -1,
                    TargetId = -1,
                    Threat = new double[cap],
                },
                ForceHatch = false,
                KaijuNav = new KaijuNav
                {
                    Clear = map.WithCells(new byte[map.Width * map.Height]),
                    ClearGridVersion = -1,
                    Field = new FlowField { Dist = new double[map.Width * map.Height], Version = -1 },
                    FieldWallVersion = -1,
                    GoalCx = -1,
                    GoalCz = -1,
                    RepathIn = 0,
                    RetargetIn = 0,
                    HomeX = map.Shrine.X,
                    HomeZ = map.Shrine.Z,
                },
                Match = BlankMatch(),
                Events = events,
                EventHead = 0,
                Hash = SpatialHash.CreateSpatialHash(map.Width, map.Height, cap),
                Builder = BuilderState.Create(cfg),
                Healer = HealerState.Create(cfg),
            };
            InitWorld(world, opts);
            return world;
        }

        /// <summary>Restart in place: no per-actor array is reallocated; the map is repainted; epoch, GridVersion and WallVersion bump.</summary>
        /// <remarks>Reads world.Config (fixed at CreateWorld, like the browser's GAME_CONFIG); opts.Config is not read.</remarks>
        public static void ResetWorld(World w, WorldOptions opts)
        {
            SkybreakBasinMap.PaintSkybreakBasin(w.Map, w.Config);
            w.GridVersion++;
            w.WallVersion++;
            w.Epoch++;
            InitWorld(w, opts);
        }

        /// <summary>Write one event into the ring without allocating.</summary>
        public static void PushEvent(World w, SimEventType type, int a, int b, double x, double z, double value)
        {
            PushEvent(w, type, a, b, x, z, value, null, DamageCause.None);
        }

        /// <summary>
        /// Write one event into the ring with its CM-35 biomass type (DeliverBiomass only, else null) and its DG-16 damage
        /// cause (Hit and Death only, else None). Every push writes both, so a reused slot never keeps a stale value.
        /// </summary>
        public static void PushEvent(World w, SimEventType type, int a, int b, double x, double z, double value, BiomassType? bio, DamageCause cause)
        {
            PushEvent(w, type, a, b, x, z, value, bio, cause, 0);
        }

        /// <summary>DG-22: PushEvent with the effect radius (SimEvent.Radius) of a Demolitions event; every push writes it.</summary>
        public static void PushEvent(World w, SimEventType type, int a, int b, double x, double z, double value, BiomassType? bio, DamageCause cause, double radius)
        {
            SimEvent e = w.Events[w.EventHead % EventCapacity];
            e.Type = type;
            e.A = a;
            e.B = b;
            e.X = x;
            e.Z = z;
            e.Value = value;
            e.Bio = bio;
            e.Cause = cause;
            e.Radius = radius;
            w.EventHead++;
        }

        /// <summary>
        /// CM-35: drop a loose meat orb of units at (x, z), lasting ttl sim ticks. Takes the first inactive slot; a full pool
        /// recycles the active orb with the least ttl (lowest index on a tie: deterministic) and counts it in OrbOverflows.
        /// Allocation-free. Returns the slot.
        /// </summary>
        public static int SpawnLooseOrb(World w, double x, double z, int units, int ttl, int source)
        {
            LooseOrb[] orbs = w.Orbs;
            int slot = -1;
            int least = int.MaxValue;
            int leastSlot = 0;
            for (int i = 0; i < orbs.Length; i++)
            {
                LooseOrb o = orbs[i];
                if (!o.Active)
                {
                    slot = i;
                    break;
                }
                if (o.Ttl < least)
                {
                    least = o.Ttl;
                    leastSlot = i;
                }
            }
            if (slot < 0)
            {
                slot = leastSlot;
                w.OrbOverflows++;
            }
            LooseOrb orb = orbs[slot];
            orb.Active = true;
            orb.Kind = BiomassType.Meat;
            orb.X = x;
            orb.Z = z;
            orb.Units = units;
            orb.Ttl = ttl;
            orb.Source = source;
            return slot;
        }

        // DG-16b additions follow (appended).

        /// <summary>DG-24: ladders and rope bridges, each team's spent biomass and the carry state (BuiltPieces.cs).</summary>
        public BuilderState Builder;
        /// <summary>DG-40: the Healer's purify cooldowns and counters (Healer.cs).</summary>
        public HealerState Healer;
    }
}
