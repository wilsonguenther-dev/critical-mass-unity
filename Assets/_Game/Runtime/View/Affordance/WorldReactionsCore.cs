using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.View.Affordance
{
    /// <summary>What reacted (the material colour, the stage rule and the object height come from it).</summary>
    public enum ReactKind : byte { Wall, Barricade, Pillar, Ground, Block, Piece, Node, Bridge }

    /// <summary>One reaction burst: a hit (shake, flash, chips), a break (pieces fall, dust) or a build (pieces settle).</summary>
    public struct ReactPulse
    {
        public ReactKind Kind;
        /// <summary>0 hit, 1 break, 2 build or repair.</summary>
        public byte Phase;
        public double X, Z;
        /// <summary>Unit push direction in sim space (from the attacker to the object; (0, 0) = radial).</summary>
        public double DirX, DirZ;
        /// <summary>0..1, scaled by the share of the object's health the hit took.</summary>
        public double Strength;
        /// <summary>World seconds (unscaled clock) it started.</summary>
        public double T0;
        /// <summary>Half the object's footprint (u): a piece's half length, 0.5 for a cell.</summary>
        public double Size;
        public int Seed;
    }

    /// <summary>
    /// World hit reactions (owner, 2026-11-13: "the object is supposed to indicate it ... how much it's breaking"):
    /// reads the Sim's events and health values (never writes them) and keeps, per damageable thing, its DAMAGE STAGE
    /// (0 intact, 1 cracked, 2 heavily damaged, 3 breaking) and a short ring of pulses the view draws.
    /// <para>Sources, all read-only: WallBreak events and MapDef.WallHp (breakable walls, HitsPerState x 2 hits),
    /// Barricade.Hp against MinerBarricade.MaxHp (polled: the Sim has no barricade-hit event), ToppleHit / Topple events
    /// and ToppleState.Hp against Topple.HitsToTopple (pillars), Terraform Dug / Placed (ground and placed blocks),
    /// PiecePlaced / PieceDestroyed / PieceExpired (ladders and bridges), BridgeCollapsed / BridgeRepaired (the bridge
    /// event), Gather / SourceGather (nodes, groves, springs).</para>
    /// Allocation-free after construction; one Read per sim tick.
    /// </summary>
    public sealed class WorldReactions
    {
        public const int MaxPulses = 48;
        /// <summary>Pulse lives (s): a hit, a break, a build.</summary>
        public const double HitLife = 0.45, BreakLife = 0.9, BuildLife = 0.7;

        public readonly ReactPulse[] Pulses = new ReactPulse[MaxPulses];
        int _next;
        int _cursor = -1, _epoch = int.MinValue;
        World _world;
        double[] _barricadeHp = new double[0];
        bool[] _barricadeActive = new bool[0];
        /// <summary>World seconds a barricade slot last lost health, and the attacker team (for the "under attack" bars).</summary>
        public double[] BarricadeHitAt = new double[0];
        /// <summary>Per decoration: world seconds of its last hit (-1 none) and who hit it.</summary>
        public double[] PillarHitAt = new double[0];
        public int[] PillarHitBy = new int[0];
        /// <summary>The last wall cell the local player hit and when.</summary>
        public int LocalWallCell = -1;
        public double LocalWallAt = -1;
        public int LocalPillar = -1;
        public double LocalPillarAt = -1;
        public int LocalBarricade = -1;
        public double LocalBarricadeAt = -1;
        /// <summary>Counters for tests and the report.</summary>
        public int Hits, Breaks, Builds;

        public int LocalActor = 0;

        /// <summary>wall-damage-1: the reactions the game view reads (the damage looks shake from its pulses).</summary>
        public static WorldReactions Current;
        /// <summary>Most cells or barricades one Kaiju slam marks with an impact (the pulse ring is shared).</summary>
        public const int KaijuContactCap = 8;

        public void Reset()
        {
            for (int i = 0; i < Pulses.Length; i++) Pulses[i].T0 = double.NegativeInfinity;
            _next = 0;
            Hits = Breaks = Builds = 0;
            LocalWallCell = LocalPillar = LocalBarricade = -1;
            LocalWallAt = LocalPillarAt = LocalBarricadeAt = -1;
        }

        public WorldReactions() { Reset(); }

        void Ensure(World w)
        {
            int nb = w.Barricades != null ? w.Barricades.Length : 0;
            if (_barricadeHp.Length != nb)
            {
                _barricadeHp = new double[nb];
                _barricadeActive = new bool[nb];
                BarricadeHitAt = new double[nb];
            }
            int nd = w.Map.Decorations != null ? w.Map.Decorations.Length : 0;
            if (PillarHitAt.Length != nd)
            {
                PillarHitAt = new double[nd];
                PillarHitBy = new int[nd];
            }
            if (!ReferenceEquals(w, _world) || w.Epoch != _epoch)
            {
                _world = w;
                _epoch = w.Epoch;
                _cursor = w.EventHead;
                for (int i = 0; i < nb; i++)
                {
                    Barricade b = w.Barricades[i];
                    _barricadeActive[i] = b.Active;
                    _barricadeHp[i] = b.Hp;
                    BarricadeHitAt[i] = -1;
                }
                for (int i = 0; i < nd; i++) { PillarHitAt[i] = -1; PillarHitBy[i] = -1; }
                Reset();
            }
        }

        public void Push(ReactKind kind, byte phase, double x, double z, double dirX, double dirZ, double strength, double now, double size = 0.5)
        {
            ReactPulse p = new ReactPulse
            {
                Kind = kind, Phase = phase, X = x, Z = z, DirX = dirX, DirZ = dirZ,
                Strength = strength < 0.15 ? 0.15 : strength > 1 ? 1 : strength, T0 = now, Size = size, Seed = _next * 7919 + (int)(x * 31 + z * 17),
            };
            Pulses[_next] = p;
            _next = (_next + 1) % MaxPulses;
            if (phase == 0) Hits++;
            else if (phase == 1) Breaks++;
            else Builds++;
        }

        public static double Life(byte phase) => phase == 0 ? HitLife : phase == 1 ? BreakLife : BuildLife;

        static void Dir(World w, int attacker, double x, double z, out double dx, out double dz)
        {
            dx = dz = 0;
            if (attacker < 0 || attacker >= w.ActorCount) return;
            dx = x - w.X[attacker];
            dz = z - w.Z[attacker];
            double l = Math.Sqrt(dx * dx + dz * dz);
            if (l < 1e-6) { dx = dz = 0; return; }
            dx /= l;
            dz /= l;
        }

        /// <summary>Consume the ticks' events since the last call and poll the health values. now = the view clock (s).</summary>
        public void Read(World w, double now)
        {
            if (w == null || w.Map == null) return;
            Current = this;
            Ensure(w);
            GameConfig c = w.Config;
            if (_cursor < 0 || _cursor > w.EventHead) _cursor = Math.Max(0, w.EventHead - World.EventCapacity);
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;
            MapDef map = w.Map;
            double hps = c.Abilities.MinerWallBreak.HitsPerState;
            double wallMax = Math.Max(1, 2 * hps);
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                switch (e.Type)
                {
                    case SimEventType.WallBreak:
                    {
                        Dir(w, e.A, e.X, e.Z, out double dx, out double dz);
                        bool gone = (int)e.Value == Cell.BreakableDestroyed;
                        Push(ReactKind.Wall, gone ? (byte)1 : (byte)0, e.X, e.Z, dx, dz, gone ? 1 : 1.0 / wallMax, now);
                        if (e.A == LocalActor) { LocalWallCell = e.B; LocalWallAt = now; }
                        break;
                    }
                    case SimEventType.ToppleHit:
                    {
                        Dir(w, e.A, e.X, e.Z, out double dx, out double dz);
                        Push(ReactKind.Pillar, 0, e.X, e.Z, dx, dz, 1.0 / Math.Max(1, c.Abilities.Topple.HitsToTopple), now);
                        if (e.B >= 0 && e.B < PillarHitAt.Length) { PillarHitAt[e.B] = now; PillarHitBy[e.B] = e.A; }
                        if (e.A == LocalActor) { LocalPillar = e.B; LocalPillarAt = now; }
                        break;
                    }
                    case SimEventType.Topple:
                        if (e.Value > 0)
                        {
                            Dir(w, e.A, e.X, e.Z, out double dx, out double dz);
                            Push(ReactKind.Pillar, 1, e.X, e.Z, dx, dz, 1, now, 1.5);
                        }
                        break;
                    case SimEventType.Terraform:
                        if (e.B >= 0 && (int)e.Value == Terraform.Dug) Push(ReactKind.Ground, 1, e.X, e.Z, 0, 0, 0.8, now);
                        else if (e.B >= 0 && (int)e.Value == Terraform.Placed) Push(ReactKind.Block, 2, e.X, e.Z, 0, 0, 0.8, now);
                        break;
                    case SimEventType.PiecePlaced:
                        Push(ReactKind.Piece, 2, e.X, e.Z, 0, 0, 0.8, now, Math.Max(0.5, e.Radius));
                        break;
                    case SimEventType.PieceDestroyed:
                    case SimEventType.PieceExpired:
                        Push(ReactKind.Piece, 1, e.X, e.Z, 0, 0, 0.8, now, Math.Max(0.5, e.Radius));
                        break;
                    case SimEventType.BarricadePlaced:
                        Push(ReactKind.Barricade, 2, e.X, e.Z, 0, 0, 0.7, now);
                        break;
                    case SimEventType.BarricadeBroken:
                        if ((int)e.Value == Barricades.BarricadeDestroyed) Push(ReactKind.Barricade, 1, e.X, e.Z, 0, 0, 1, now);
                        break;
                    case SimEventType.BridgeRepaired:
                        if (e.Value >= 0) Push(ReactKind.Bridge, 2, e.X, e.Z, 0, 0, 1, now, 2);
                        break;
                    case SimEventType.BridgeCollapsed:
                        Push(ReactKind.Bridge, 1, e.X, e.Z, 0, 0, 1, now, 2);
                        break;
                    case SimEventType.KaijuSlam:
                        KaijuContact(w, e.X, e.Z, e.Radius > 0 ? e.Radius : e.Value, now);   // one slam path stores the radius in Value
                        break;
                    case SimEventType.Gather:
                    case SimEventType.SourceGather:
                        Push(ReactKind.Node, 0, e.X, e.Z, 0, 0, 0.5, now);
                        break;
                }
            }

            // Barricades: the Sim has no hit event, so health drops are read from Barricade.Hp each tick.
            double bmax = Math.Max(1, c.Abilities.MinerBarricade.MaxHp);
            for (int i = 0; i < _barricadeHp.Length; i++)
            {
                Barricade b = w.Barricades[i];
                if (b.Active && _barricadeActive[i] && b.Hp < _barricadeHp[i] - 1e-9)
                {
                    double x = b.Cx + 0.5, z = b.Cz + 0.5;
                    int near = NearestEnemySwinger(w, b);
                    Dir(w, near, x, z, out double dx, out double dz);
                    Push(ReactKind.Barricade, 0, x, z, dx, dz, (_barricadeHp[i] - b.Hp) / bmax, now);
                    BarricadeHitAt[i] = now;
                    if (near == LocalActor) { LocalBarricade = i; LocalBarricadeAt = now; }
                }
                _barricadeActive[i] = b.Active;
                _barricadeHp[i] = b.Hp;
            }
        }

        /// <summary>
        /// wall-damage-1: the Kaiju's slam lands near walls and barricades. The rules do not let it damage them (no design doc
        /// says it does; OD-4 says walls block it), so this is an impact only: a weak hit pulse (shake and dust, no health
        /// change) on the wall cells (stone and breakable) and barricades inside the slam radius, at most KaijuContactCap of them.
        /// </summary>
        void KaijuContact(World w, double x, double z, double radius, double now)
        {
            MapDef m = w.Map;
            if (radius <= 0) return;
            int pushed = 0;
            int x0 = Math.Max(0, (int)Math.Floor(x - radius)), x1 = Math.Min(m.Width - 1, (int)Math.Floor(x + radius));
            int z0 = Math.Max(0, (int)Math.Floor(z - radius)), z1 = Math.Min(m.Height - 1, (int)Math.Floor(z + radius));
            double r2 = radius * radius;
            for (int cz = z0; cz <= z1 && pushed < KaijuContactCap; cz++)
            for (int cx = x0; cx <= x1 && pushed < KaijuContactCap; cx++)
            {
                byte t = m.Cells[cz * m.Width + cx];
                if (t != Cell.BreakableIntact && t != Cell.BreakableCracked && t != Cell.Wall) continue;
                double dx = cx + 0.5 - x, dz = cz + 0.5 - z, d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                double l = Math.Sqrt(d2);
                Push(ReactKind.Wall, 0, cx + 0.5, cz + 0.5, l > 1e-6 ? dx / l : 0, l > 1e-6 ? dz / l : 0, 0.5, now);
                pushed++;
            }
            for (int i = 0; w.Barricades != null && i < w.Barricades.Length && pushed < KaijuContactCap; i++)
            {
                Barricade b = w.Barricades[i];
                if (!b.Active) continue;
                double dx = b.Cx + 0.5 - x, dz = b.Cz + 0.5 - z, d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                double l = Math.Sqrt(d2);
                Push(ReactKind.Barricade, 0, b.Cx + 0.5, b.Cz + 0.5, l > 1e-6 ? dx / l : 0, l > 1e-6 ? dz / l : 0, 0.5, now);
                pushed++;
            }
        }

        /// <summary>The nearest living enemy of the barricade's team (its likely hitter, for the shake direction), or -1.</summary>
        static int NearestEnemySwinger(World w, Barricade b)
        {
            int best = -1;
            double bd = 9.0;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor a = w.Actors[j];
                if (!a.Alive || a.Team == b.Team) continue;
                double dx = w.X[j] - (b.Cx + 0.5), dz = w.Z[j] - (b.Cz + 0.5);
                double d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = j; }
            }
            return best;
        }

        // ------------------------------------------------------------------ damage stages

        /// <summary>
        /// Stage of a health share left (1 = full): 0 intact (full), 1 cracked (above two thirds), 2 heavily damaged (above a
        /// third), 3 breaking (a third or less, not zero). Shared by every damageable type.
        /// </summary>
        public static int StageOf(double left01)
        {
            if (left01 >= 1 - 1e-9) return 0;
            if (left01 > 2.0 / 3.0) return 1;
            if (left01 > 1.0 / 3.0) return 2;
            return 3;
        }

        /// <summary>Health share left of a breakable wall cell (MapDef.WallHp; a painted wall without hit points gets the ones its state implies, as Abilities.MinerWallBreak does). -1 = not a standing breakable.</summary>
        public static double WallLeft01(World w, int cellIndex)
        {
            MapDef m = w.Map;
            if (cellIndex < 0 || cellIndex >= m.Cells.Length) return -1;
            byte t = m.Cells[cellIndex];
            if (t != Cell.BreakableIntact && t != Cell.BreakableCracked) return -1;
            double hps = w.Config.Abilities.MinerWallBreak.HitsPerState;
            double max = Math.Max(1, 2 * hps);
            double hp = m.WallHp[cellIndex];
            if (hp == 0) hp = t == Cell.BreakableIntact ? 2 * hps : hps;
            return Math.Max(0, Math.Min(1, hp / max));
        }

        /// <summary>Pick hits left on a breakable wall cell (exact: one per swing that reaches it), -1 = none.</summary>
        public static int WallHitsLeft(World w, int cellIndex)
        {
            double l = WallLeft01(w, cellIndex);
            if (l < 0) return -1;
            return (int)Math.Round(l * Math.Max(1, 2 * w.Config.Abilities.MinerWallBreak.HitsPerState));
        }

        /// <summary>Health share left of barricade slot i (-1 = inactive).</summary>
        public static double BarricadeLeft01(World w, int i)
        {
            if (w.Barricades == null || i < 0 || i >= w.Barricades.Length || !w.Barricades[i].Active) return -1;
            return Math.Max(0, Math.Min(1, w.Barricades[i].Hp / Math.Max(1, w.Config.Abilities.MinerBarricade.MaxHp)));
        }

        /// <summary>Swings left share of pillar k (-1 = fallen or none).</summary>
        public static double PillarLeft01(World w, int k)
        {
            if (w.Topple == null || k < 0 || k >= w.Topple.Hp.Length || w.Topple.Fallen[k] != Topple.Standing) return -1;
            return Math.Max(0, Math.Min(1, w.Topple.Hp[k] / Math.Max(1, w.Config.Abilities.Topple.HitsToTopple)));
        }
    }

    /// <summary>
    /// The progress indicator's numbers for the local player (owner: "a little thing that shows you how much it's breaking"):
    /// the object he is working on now (gathering, repairing) or hit last (within FocusSeconds), its health or progress
    /// share, what one more hit takes off (the ghost segment), and the hits or seconds left when those are exact. Read-only.
    /// </summary>
    public sealed class ProgressFocus
    {
        public const double FocusSeconds = 3.0;

        public bool Has;
        public double X, Z, Lift;
        /// <summary>Health or progress share now, and after one more hit (Next01 = Value01 for timed progress).</summary>
        public double Value01, Next01;
        /// <summary>Exact hits left (-1 = not exact or not hit-based) and seconds left (-1 = not timed).</summary>
        public int HitsLeft = -1;
        public double SecondsLeft = -1;
        /// <summary>The object belongs to the local player's team (team-coloured bar), or is neutral or the enemy's.</summary>
        public TeamId Owner;
        public bool Neutral;
        /// <summary>Progress fills up (gather, repair) instead of health running down.</summary>
        public bool Filling;
        public ReactKind Kind;

        void Clear()
        {
            Has = false;
            HitsLeft = -1;
            SecondsLeft = -1;
            Neutral = true;
            Filling = false;
            Lift = 0.5;
        }

        public void Update(World w, int me, WorldReactions rx, double now)
        {
            Clear();
            if (w == null || me < 0 || me >= w.ActorCount || !w.Actors[me].Alive) return;
            Actor a = w.Actors[me];
            GameConfig c = w.Config;
            // 1. Timed work the Sim is counting for him right now.
            if (a.GatherNodeId >= 0 && a.GatherNodeId < w.Nodes.Length && a.GatherProgress > 0)
            {
                double need = c.Class(a.Cls).GatherSeconds;
                Timed(w.Nodes[a.GatherNodeId].X, w.Nodes[a.GatherNodeId].Z, a.GatherProgress, need, ReactKind.Node);
                return;
            }
            DepthState d = w.Depth;
            if (d != null && Sources.On(w) && d.SourceOf[me] >= 0 && d.SourceOf[me] < d.Sources.Length && d.SourceProgress[me] > 0)
            {
                SourceNode s = d.Sources[d.SourceOf[me]];
                double need = c.Class(a.Cls).GatherSeconds * Sources.GatherScale(c, a.Cls, s.Kind);
                Timed(s.X, s.Z, d.SourceProgress[me], need, ReactKind.Node);
                return;
            }
            if (d != null && d.BridgeState == MapEvents.BridgeBroken && d.RepairProgress > 0 && a.Cls == ClassId.Builder && w.Inputs[me].Interact)
            {
                double rr = c.Depth.Events.BridgeRepairRange;
                double ax = w.X[me] - d.BridgeAx, az = w.Z[me] - d.BridgeAz, bx = w.X[me] - d.BridgeBx, bz = w.Z[me] - d.BridgeBz;
                if (ax * ax + az * az <= rr * rr || bx * bx + bz * bz <= rr * rr)
                {
                    double need = Durations.T30(c, c.Depth.Events.BridgeRepairTicks);
                    Timed((d.BridgeAx + d.BridgeBx) / 2, (d.BridgeAz + d.BridgeBz) / 2, d.RepairProgress / c.SimHz, need / c.SimHz, ReactKind.Bridge);
                    Owner = a.Team;
                    Neutral = false;
                    return;
                }
            }
            // 2. The thing he hit last, while it still stands.
            double best = now - FocusSeconds;
            int pick = 0;
            if (rx.LocalWallAt > best && WorldReactions.WallLeft01(w, rx.LocalWallCell) >= 0) { best = rx.LocalWallAt; pick = 1; }
            if (rx.LocalPillarAt > best && WorldReactions.PillarLeft01(w, rx.LocalPillar) >= 0) { best = rx.LocalPillarAt; pick = 2; }
            if (rx.LocalBarricadeAt > best && WorldReactions.BarricadeLeft01(w, rx.LocalBarricade) >= 0) { best = rx.LocalBarricadeAt; pick = 3; }
            if (pick == 1)
            {
                int i = rx.LocalWallCell;
                double max = Math.Max(1, 2 * c.Abilities.MinerWallBreak.HitsPerState);
                Hit(i % w.Map.Width + 0.5, i / w.Map.Width + 0.5, WorldReactions.WallLeft01(w, i), 1.0 / max, ReactKind.Wall);
                HitsLeft = WorldReactions.WallHitsLeft(w, i);
                Lift = 1.9;
            }
            else if (pick == 2)
            {
                int k = rx.LocalPillar;
                DecorationDef dd = w.Map.Decorations[k];
                Hit(dd.Cx + 0.5, dd.Cz + 0.5, WorldReactions.PillarLeft01(w, k), 1.0 / Math.Max(1, c.Abilities.Topple.HitsToTopple), ReactKind.Pillar);
                HitsLeft = w.Topple.Hp[k];
                Lift = 2.6;
            }
            else if (pick == 3)
            {
                Barricade b = w.Barricades[rx.LocalBarricade];
                double dmg = c.Class(a.Cls).AttackDamage, max = Math.Max(1, c.Abilities.MinerBarricade.MaxHp);
                Hit(b.Cx + 0.5, b.Cz + 0.5, WorldReactions.BarricadeLeft01(w, rx.LocalBarricade), dmg / max, ReactKind.Barricade);
                HitsLeft = dmg > 0 ? (int)Math.Ceiling(b.Hp / dmg - 1e-9) : -1;
                Owner = b.Team;
                Neutral = false;
                Lift = 1.4;
            }
        }

        void Timed(double x, double z, double done, double need, ReactKind kind)
        {
            Has = true;
            Filling = true;
            X = x;
            Z = z;
            Kind = kind;
            Value01 = need > 0 ? Math.Max(0, Math.Min(1, done / need)) : 1;
            Next01 = Value01;
            SecondsLeft = Math.Max(0, need - done);
            Lift = 1.2;
        }

        void Hit(double x, double z, double left01, double perHit01, ReactKind kind)
        {
            Has = true;
            X = x;
            Z = z;
            Kind = kind;
            Value01 = left01;
            Next01 = Math.Max(0, left01 - perHit01);
        }
    }
}
