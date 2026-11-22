using System;
using System.Collections.Generic;
using System.Globalization;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Bots
{
    /// <summary>What a <see cref="ShiverWatch"/> flag found.</summary>
    public enum ShiverKind : byte
    {
        /// <summary>Moved a lot in the window and ended up close to where it started (back and forth).</summary>
        Dither = 0,
        /// <summary>Its direction of motion (or the Kaiju's facing) reversed more than MaxReversals times in one window.</summary>
        Reversals = 1,
        /// <summary>Wanted to move for PressSeconds but covered less than PressShare of its walking distance (pressed on something).</summary>
        Pressed = 2,
    }

    /// <summary>One flag: who, where, what it measured and what it wanted.</summary>
    public struct ShiverFlag
    {
        public int Tick;
        /// <summary>Actor id, or <see cref="ShiverWatch.KaijuId"/> for the Kaiju.</summary>
        public int Actor;
        public ShiverKind Kind;
        public ClassId Cls;
        public TeamId Team;
        public AiState State;
        public double X;
        public double Z;
        public double Path;
        public double Net;
        public int Reversals;
        /// <summary>The goal it was steering for (bot target / Kaiju target id; -1 none).</summary>
        public int WantTarget;
    }

    /// <summary>
    /// fix/movement-shiver-1, the "no shiver" detector (owner 2026-11-14: "make sure they never do something like this").
    /// A read-only observer: call <see cref="Observe"/> once after every sim tick; it never writes the World, so it cannot change
    /// a checksum. For every living, grounded actor and the hatched Kaiju it measures, over a WindowTicks window, the path walked
    /// against the net displacement and the number of direction reversals (a reversal: two consecutive real steps more than
    /// 120 degrees apart; the Kaiju's facing counts too), and for a bot that wants to move, how far it got in PressSeconds.
    /// Flags: Dither (path at least MinPath, net under NetShare of it, at least 2 reversals), Reversals (more than MaxReversals
    /// in one window), Pressed (wanted to move PressSeconds, covered under PressShare of its speed times that time). A teleport
    /// (respawn, more than TeleportStep in one tick), a death or a flight resets the actor. Preallocated; the only allocation is
    /// <see cref="Format"/>, called by a log line or a failing test.
    /// </summary>
    public sealed class ShiverWatch
    {
        public const int KaijuId = -2;

        public int WindowTicks = 30;
        public double MinPath = 1.0;
        public double NetShare = 0.35;
        public int MaxReversals = 3;
        public double PressSeconds = 2.0;
        public double PressShare = 0.15;
        /// <summary>A step shorter than this does not count for direction (separation nudges, rounding).</summary>
        public double MinStep = 0.02;
        public double TeleportStep = 2.0;
        /// <summary>Flags kept (the count keeps going).</summary>
        public int MaxFlags = 256;

        public readonly List<ShiverFlag> Flags = new List<ShiverFlag>(256);
        public int Count { get; private set; }
        public int[] CountByKind = new int[3];
        /// <summary>Flags raised by the last Observe call (for a runtime log line).</summary>
        public int NewThisTick { get; private set; }

        private int _cap;
        private double[] _sx, _sz, _path, _ldx, _ldz, _pressT, _pressPath, _lastFacing;
        private int[] _rev, _ticks, _lastRevTick;

        /// <summary>
        /// fix/movement-shiver-3, the melee-contact rule: while a bot fights a target within its attack reach + ContactSlack,
        /// a direction reversal counts only when it comes at most RapidTicks after the previous one. Footwork against a target
        /// that moves reverses by nature, at the pace of the opponent (several tenths of a second apart); a reversal every
        /// RapidTicks ticks or faster is an oscillation of about 4 Hz or more, which reads as vibration, not as a dodge.
        /// Outside contact every reversal counts, as before.
        /// </summary>
        public int RapidTicks = 4;
        public double ContactSlack = 1.0;

        private void Ensure(int n)
        {
            if (_cap >= n) return;
            _cap = n;
            _sx = new double[n];
            _sz = new double[n];
            _path = new double[n];
            _ldx = new double[n];
            _ldz = new double[n];
            _pressT = new double[n];
            _pressPath = new double[n];
            _lastFacing = new double[n];
            _rev = new int[n];
            _ticks = new int[n];
            _lastRevTick = new int[n];
            for (int i = 0; i < n; i++) _ticks[i] = -1;
        }

        public void Clear()
        {
            Flags.Clear();
            Count = 0;
            Array.Clear(CountByKind, 0, CountByKind.Length);
            if (_ticks != null) for (int i = 0; i < _ticks.Length; i++) _ticks[i] = -1;
        }

        private void Reset(int s, double x, double z, double facing)
        {
            _sx[s] = x;
            _sz[s] = z;
            _path[s] = 0;
            _ldx[s] = 0;
            _ldz[s] = 0;
            _rev[s] = 0;
            _ticks[s] = 0;
            _pressT[s] = 0;
            _pressPath[s] = 0;
            _lastFacing[s] = facing;
            _lastRevTick[s] = int.MinValue / 2;
        }

        /// <summary>Measure one tick (call after SimStep.Step / MatchTick.Advance).</summary>
        public void Observe(World w)
        {
            NewThisTick = 0;
            int n = w.ActorCount;
            Ensure(n + 1);
            double dt = SimStep.Dt(w.Config);
            BotsState st = BotBrain.GetBotsState(w);
            for (int i = 0; i < n; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.AirLen > 0)
                {
                    _ticks[i] = -1;
                    continue;
                }
                bool bot = st != null && BotsState.IsBot(st, w, i);
                // Stunned or getting up it cannot move however much it wants to: not "pressed". Slowed, it walks slower.
                bool wants = bot && st.WantMove[i] != 0 && a.AiState != AiState.HoldHill && a.StandUp <= 0 && a.HitStunTicks <= 0;
                double speed = w.Config.Class(a.Cls).MoveSpeed * (a.SlowTimer > 0 ? 0.5 : 1);
                // b15 combat feel: a swing, a recovery or a stagger slow walks slower (CombatFeel.SpeedScale), and a body being
                // knocked back is moved by the hit, not by its legs: that step is not a reversal (Knocked below).
                bool feel = CombatFeel.On(w);
                if (feel) speed *= CombatFeel.SpeedScale(w, i);
                _knocked = feel && Knocked(a);
                _contact = bot && a.AiState == AiState.Fight && InContact(w, st, i);
                Sample(w, i, w.X[i], w.Z[i], w.PrevX[i], w.PrevZ[i], a.Facing, false, wants, speed, dt,
                    a.Cls, a.Team, a.AiState, bot ? st.Target[i] : -1);
                _contact = false;
                _knocked = false;
            }
            Kaiju k = w.Kaiju;
            bool hatched = w.Match.Hatched && k.State != KaijuState.Egg;
            if (!hatched || k.KoIn > 0)
            {
                _ticks[n] = -1;
                return;
            }
            bool kWants = k.TargetId >= 0 && k.TelegraphIn <= 0 && k.MeleeWindupIn <= 0 && KaijuWantsToWalk(w);
            // b15 combat feel: while the Kaiju's target is knocked around (by fighters' hits) the Kaiju turning after it is the
            // chase, not a door shiver.
            _knocked = CombatFeel.On(w) && k.TargetId >= 0 && k.TargetId < n && Knocked(w.Actors[k.TargetId]);
            // b16 [2]: a Kaiju sliding from a blast is moved by the blast, not by its legs (PushVx / PushVz are 0 without the rule).
            if (k.PushVx != 0 || k.PushVz != 0) _knocked = true;
            Sample(w, n, k.X, k.Z, k.PrevX, k.PrevZ, k.Facing, true, kWants, KaijuTypes.MoveSpeed(w), dt,
                ClassId.Warrior, TeamId.Azure, AiState.None, k.TargetId);
            _knocked = false;
        }

        private bool _contact, _knocked;

        private static bool Knocked(Actor a) => a.KVx != 0 || a.KVz != 0 || a.HitStunTicks > 0;

        private bool InContact(World w, BotsState st, int i)
        {
            int t = st.Target[i];
            double reach = w.Config.Class(w.Actors[i].Cls).AttackRange + ContactSlack;
            double tx, tz;
            if (t == BotsState.TargetKaiju)
            {
                tx = w.Kaiju.X;
                tz = w.Kaiju.Z;
                reach += w.Config.Kaiju.Radius;
            }
            else if (t >= 0 && t < w.ActorCount && w.Actors[t].Alive)
            {
                tx = w.X[t];
                tz = w.Z[t];
            }
            else return false;
            double dx = tx - w.X[i];
            double dz = tz - w.Z[i];
            return dx * dx + dz * dz <= reach * reach;
        }

        private static bool KaijuWantsToWalk(World w)
        {
            Kaiju k = w.Kaiju;
            int t = k.TargetId;
            if (t < 0 || t >= w.ActorCount) return false;
            double dx = w.X[t] - k.X;
            double dz = w.Z[t] - k.Z;
            double stop = w.Config.Kaiju.MeleeRange; // inside claw range it may stand still
            return dx * dx + dz * dz > stop * stop;
        }

        private void Sample(World w, int s, double x, double z, double px, double pz, double facing, bool kaiju, bool wants,
            double speed, double dt, ClassId cls, TeamId team, AiState state, int target)
        {
            double dx = x - px;
            double dz = z - pz;
            double step = Math.Sqrt(dx * dx + dz * dz);
            if (_ticks[s] < 0 || step > TeleportStep)
            {
                Reset(s, x, z, facing);
                return;
            }
            // b16 [3]: under the physics-feel rule a knocked step (a TNT shove, a hit's knockback, the Kaiju's blast slide) is the
            // hit's distance, not the legs': it is left out of the walked path too, as it already was out of the reversals. A
            // bot thrown 5 m by a blast and walking 1 m back read as a Dither (5 of the 12 flags in the fast gate's match).
            if (!_knocked || !PhysicsFeel.On(w)) _path[s] += step;
            if (_knocked)
            {
                _ldx[s] = 0; // the next step of its own is not compared with the knock
                _ldz[s] = 0;
                if (kaiju) _lastFacing[s] = facing;
            }
            else if (step >= MinStep)
            {
                double ux = dx / step;
                double uz = dz / step;
                if ((_ldx[s] != 0 || _ldz[s] != 0) && ux * _ldx[s] + uz * _ldz[s] < -0.5)
                {
                    if (!_contact || w.Tick - _lastRevTick[s] <= RapidTicks) _rev[s]++;
                    _lastRevTick[s] = w.Tick;
                }
                _ldx[s] = ux;
                _ldz[s] = uz;
            }
            if (kaiju && !_knocked)
            {
                // The Kaiju's facing drives its drawn body: a facing flip over 120 degrees in one tick is a reversal too.
                double df = Math.Abs(facing - _lastFacing[s]) % (2 * Math.PI);
                if (df > Math.PI) df = 2 * Math.PI - df;
                if (df > Math.PI * 2 / 3) _rev[s]++;
                _lastFacing[s] = facing;
            }
            if (wants)
            {
                _pressT[s] += dt;
                _pressPath[s] += step;
                if (_pressT[s] >= PressSeconds - 1e-9)
                {
                    if (_pressPath[s] < PressShare * speed * _pressT[s]) Raise(w, s, ShiverKind.Pressed, x, z, _pressPath[s], 0, _rev[s], kaiju, cls, team, state, target);
                    _pressT[s] = 0;
                    _pressPath[s] = 0;
                }
            }
            else
            {
                _pressT[s] = 0;
                _pressPath[s] = 0;
            }
            _ticks[s]++;
            if (_ticks[s] < WindowTicks) return;
            double nx = x - _sx[s];
            double nz = z - _sz[s];
            double net = Math.Sqrt(nx * nx + nz * nz);
            if (_rev[s] > MaxReversals) Raise(w, s, ShiverKind.Reversals, x, z, _path[s], net, _rev[s], kaiju, cls, team, state, target);
            else if (_path[s] >= MinPath && net < NetShare * _path[s] && _rev[s] >= 2) Raise(w, s, ShiverKind.Dither, x, z, _path[s], net, _rev[s], kaiju, cls, team, state, target);
            double pt = _pressT[s];
            double pp = _pressPath[s];
            Reset(s, x, z, facing);
            _pressT[s] = pt;
            _pressPath[s] = pp;
        }

        private void Raise(World w, int s, ShiverKind kind, double x, double z, double path, double net, int rev, bool kaiju,
            ClassId cls, TeamId team, AiState state, int target)
        {
            Count++;
            CountByKind[(int)kind]++;
            NewThisTick++;
            if (Flags.Count >= MaxFlags) return;
            Flags.Add(new ShiverFlag
            {
                Tick = w.Tick,
                Actor = kaiju ? KaijuId : s,
                Kind = kind,
                Cls = cls,
                Team = team,
                State = state,
                X = x,
                Z = z,
                Path = path,
                Net = net,
                Reversals = rev,
                WantTarget = target,
            });
        }

        /// <summary>The play-test log line: [CM.Shiver] actor, class, position, what it measured, what it wanted.</summary>
        public static string Format(in ShiverFlag f)
        {
            CultureInfo ic = CultureInfo.InvariantCulture;
            string who = f.Actor == KaijuId ? "Kaiju" : "actor " + f.Actor.ToString(ic) + " " + f.Cls + " " + f.Team;
            string want = f.Actor == KaijuId
                ? (f.WantTarget >= 0 ? "chase actor " + f.WantTarget.ToString(ic) : "no target")
                : f.State + (f.WantTarget >= 0 ? " target " + f.WantTarget.ToString(ic) : "");
            return "[CM.Shiver] " + who + " " + f.Kind + " tick " + f.Tick.ToString(ic) + " at (" + f.X.ToString("F1", ic) + ", " +
                   f.Z.ToString("F1", ic) + ") walked " + f.Path.ToString("F2", ic) + " m, net " + f.Net.ToString("F2", ic) +
                   " m, reversals " + f.Reversals.ToString(ic) + "; wanted: " + want;
        }
    }
}
