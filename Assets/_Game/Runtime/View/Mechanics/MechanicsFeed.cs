using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// Reads the sim's event ring once per tick (SimDriverBehaviour.Ticked) and keeps the short-lived view facts the world
    /// view, the HUD and the poses draw from: heal beams and pulses, wind-up glints, knockback streaks, hits on the local
    /// player (direction and the recap), stuns, feed throws, slam dodges and drop pops. Fixed rings, no allocation per tick.
    /// Every time is a sim second (Tick / SimHz); views turn ages into wall seconds with the pace. Never writes the sim.
    /// </summary>
    public sealed class MechFeed
    {
        public const int Beams = 24, Bursts = 16, Sparkles = 24, Glints = 16, Streaks = 16, LocalHits = 8, MineFlashes = 8;
        /// <summary>Glints and streaks are drawn only this close to the local player (sim units).</summary>
        public const double NearUnits = 20;
        /// <summary>Burst kinds.</summary>
        public const int BurstPulse = 0, BurstPurify = 1, BurstDrop = 2, BurstDodge = 3, BurstMine = 4;
        /// <summary>Without a sim stun (no HitStun event on this base) a slam hit plays this short stagger, wall seconds.</summary>
        public const double FallbackStaggerWall = 0.3;

        public struct Beam { public int From, To; public double T; }
        public struct Burst { public double X, Z, R, T; public int Kind; public TeamId Team; }
        public struct Sparkle { public int Actor; public double T, Amount; }
        public struct Glint { public int Actor; public double X, Z, Facing, T; public bool Contact; public bool Cancelled; }
        public struct Streak { public int Victim; public double Dx, Dz, T, Dmg; }

        public struct LocalHit
        {
            public int Attacker;
            public TeamId Team;
            public ClassId Cls;
            public DamageCause Cause;
            public int BlastKind;
            public double SrcX, SrcZ, T, Dmg;
            public bool Fatal;
        }

        public readonly Beam[] BeamRing = new Beam[Beams];
        public readonly Burst[] BurstRing = new Burst[Bursts];
        public readonly Sparkle[] SparkleRing = new Sparkle[Sparkles];
        public readonly Glint[] GlintRing = new Glint[Glints];
        public readonly Streak[] StreakRing = new Streak[Streaks];
        public readonly LocalHit[] HitRing = new LocalHit[LocalHits];
        int _beam, _burst, _sparkle, _glint, _streak;

        /// <summary>Hits on the local player so far this match (HitRing[(HitCount - 1) % LocalHits] is the newest).</summary>
        public int HitCount { get; private set; }
        /// <summary>Per actor: the stun's start and length (sim seconds), the knock direction (sim x, z).</summary>
        public double[] StunStart = new double[0], StunLen = new double[0], StunDirX = new double[0], StunDirZ = new double[0];
        /// <summary>Per actor: when the last feed throw (a delivery) happened.</summary>
        public double[] ThrowAt = new double[0];
        /// <summary>The last slam the local player dodged (sim seconds), and the last slam at all.</summary>
        public double DodgeAt = -999, SlamAt = -999;
        public int Dodges { get; private set; }
        /// <summary>True when this sim build sends its own stun events (DG-70).</summary>
        public static bool SimStuns => MechEvents.HitStunCode >= 0;

        public int LocalActor = 0;
        public double Pace = 2;

        int _cursor = -1, _epoch = int.MinValue;
        // per-tick scratch
        bool _slamThisTick, _slamInside, _slamAirborne, _slamHurt;
        int _pendingBlastHit = -1;

        public void Reset()
        {
            Array.Clear(BeamRing, 0, Beams);
            Array.Clear(BurstRing, 0, Bursts);
            Array.Clear(SparkleRing, 0, Sparkles);
            Array.Clear(GlintRing, 0, Glints);
            Array.Clear(StreakRing, 0, Streaks);
            Array.Clear(HitRing, 0, LocalHits);
            for (int i = 0; i < Beams; i++) BeamRing[i].T = -999;
            for (int i = 0; i < Bursts; i++) BurstRing[i].T = -999;
            for (int i = 0; i < Sparkles; i++) SparkleRing[i].T = -999;
            for (int i = 0; i < Glints; i++) GlintRing[i].T = -999;
            for (int i = 0; i < Streaks; i++) StreakRing[i].T = -999;
            Fill(StunStart, -999);
            Array.Clear(StunLen, 0, StunLen.Length);
            Fill(ThrowAt, -999);
            HitCount = 0;
            Dodges = 0;
            DodgeAt = SlamAt = -999;
        }

        static void Fill(double[] a, double v)
        {
            for (int i = 0; i < a.Length; i++) a[i] = v;
        }

        void Ensure(int n)
        {
            if (StunStart.Length >= n) return;
            StunStart = new double[n];
            StunLen = new double[n];
            StunDirX = new double[n];
            StunDirZ = new double[n];
            ThrowAt = new double[n];
            Fill(StunStart, -999);
            Fill(ThrowAt, -999);
        }

        /// <summary>Sim seconds of a world at a fractional tick.</summary>
        public static double Now(World w, double alpha) => (w.Tick + alpha) / w.Config.SimHz;

        /// <summary>Wall seconds between two sim times at the current pace.</summary>
        public double Wall(double simAge) => Pace > 0 ? simAge / Pace : simAge;

        /// <summary>The stun tilt of actor id at sim time now, degrees (0 when not stunned).</summary>
        public float StunTiltDeg(int id, double now)
        {
            if (id < 0 || id >= StunStart.Length) return 0f;
            return KnockPose.TiltDeg(Wall(now - StunStart[id]), Wall(StunLen[id]));
        }

        /// <summary>True while actor id is stunned (by the sim's event, or the fallback stagger).</summary>
        public bool Stunned(int id, double now) => id >= 0 && id < StunStart.Length && now >= StunStart[id] && now < StunStart[id] + StunLen[id];

        /// <summary>Stun left 0..1 for the HUD badge.</summary>
        public float StunLeft01(int id, double now)
        {
            if (!Stunned(id, now) || StunLen[id] <= 0) return 0f;
            return (float)(1 - (now - StunStart[id]) / StunLen[id]);
        }

        /// <summary>Reads every event pushed since the last call (call after each sim tick).</summary>
        public void Read(World w)
        {
            if (w == null) return;
            Ensure(w.ActorCount);
            if (w.Epoch != _epoch)
            {
                _epoch = w.Epoch;
                Reset();
                _cursor = w.EventHead;
            }
            if (_cursor < 0 || _cursor > w.EventHead) _cursor = Math.Max(0, w.EventHead - World.EventCapacity);
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;
            double now = w.Tick / w.Config.SimHz;
            _slamThisTick = _slamInside = _slamAirborne = _slamHurt = false;
            _pendingBlastHit = -1;
            double blastX = 0, blastZ = 0;
            int blastKind = 0;
            bool blast = false;
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                if (e == null) continue;
                int code = (int)e.Type;
                if (code == MechEvents.HitStunCode) { OnStun(w, e, now); continue; }
                if (code == MechEvents.AttackContactCode) { AddGlint(w, e.A, e.X, e.Z, Facing(w, e.A), now, true); continue; }
                if (code == MechEvents.AttackCancelCode) { CancelGlints(e.A); continue; }
                if (code == MechEvents.SlamDodgedCode) { if (e.A == LocalActor) MarkDodge(w, now); continue; }
                if (code == MechEvents.DropPopCode) { AddBurst(e.X, e.Z, 0.6, now, BurstDrop, TeamId.Azure); continue; }
                if (code == MechEvents.KnockbackCode) { OnKnock(w, e, now); continue; }
                switch (e.Type)
                {
                    case SimEventType.AttackStart:
                        AddGlint(w, e.A, e.X, e.Z, e.Value, now, false);
                        break;
                    case SimEventType.Hit:
                        OnHit(w, e, now);
                        break;
                    case SimEventType.Explosion:
                        blast = true;
                        blastX = e.X;
                        blastZ = e.Z;
                        blastKind = e.B;
                        break;
                    case SimEventType.KaijuSlam:
                        OnSlam(w, e, now);
                        break;
                    case SimEventType.HealCast:
                        if (e.B == Healer.KindPulse) AddBurst(e.X, e.Z, Math.Max(0.5, e.Radius), now, BurstPulse, TeamOf(w, e.A));
                        else AddBeam(e.A, NearestAlly(w, e.A, e.X, e.Z), now);
                        break;
                    case SimEventType.HealLanded:
                        SparkleRing[_sparkle] = new Sparkle { Actor = e.A, T = now, Amount = e.Value };
                        _sparkle = (_sparkle + 1) % Sparkles;
                        break;
                    case SimEventType.Purify:
                        AddBurst(e.X, e.Z, Math.Max(1, e.Radius), now, BurstPurify, e.B == 0 ? TeamId.Azure : TeamId.Crimson);
                        break;
                    case SimEventType.MineTriggered:
                        AddBurst(e.X, e.Z, Math.Max(0.5, e.Radius), now, BurstMine, TeamOf(w, e.B));
                        break;
                    case SimEventType.DeliverBiomass:
                    case SimEventType.DeliverCorruption:
                        if (e.A >= 0 && e.A < ThrowAt.Length) ThrowAt[e.A] = now;
                        break;
                }
            }
            // A blast's hits are pushed before its Explosion event: point the local hit at the blast centre.
            if (_pendingBlastHit >= 0 && blast)
            {
                int k = _pendingBlastHit % LocalHits;
                HitRing[k].SrcX = blastX;
                HitRing[k].SrcZ = blastZ;
                HitRing[k].BlastKind = blastKind;
            }
            if (_slamThisTick && SlamRead.Dodged(_slamInside, _slamAirborne, _slamHurt)) MarkDodge(w, now);
        }

        void MarkDodge(World w, double now)
        {
            if (now - DodgeAt < 0.05) return;
            DodgeAt = now;
            Dodges++;
            int me = LocalActor;
            if (me >= 0 && me < w.ActorCount) AddBurst(w.X[me], w.Z[me], 1.1, now, BurstDodge, w.Actors[me].Team);
        }

        static TeamId TeamOf(World w, int id) => id >= 0 && id < w.ActorCount ? w.Actors[id].Team : TeamId.Azure;

        static double Facing(World w, int id) => id >= 0 && id < w.ActorCount ? w.Actors[id].Facing : 0;

        bool Near(World w, int id)
        {
            int me = LocalActor;
            if (id == me) return true;
            if (id < 0 || id >= w.ActorCount || me < 0 || me >= w.ActorCount) return false;
            double dx = w.X[id] - w.X[me], dz = w.Z[id] - w.Z[me];
            return dx * dx + dz * dz <= NearUnits * NearUnits;
        }

        void AddGlint(World w, int actor, double x, double z, double facing, double now, bool contact)
        {
            if (!Near(w, actor)) return;
            GlintRing[_glint] = new Glint { Actor = actor, X = x, Z = z, Facing = facing, T = now, Contact = contact };
            _glint = (_glint + 1) % Glints;
        }

        void CancelGlints(int actor)
        {
            for (int i = 0; i < Glints; i++) if (GlintRing[i].Actor == actor) GlintRing[i].Cancelled = true;
        }

        void AddBurst(double x, double z, double r, double now, int kind, TeamId team)
        {
            BurstRing[_burst] = new Burst { X = x, Z = z, R = r, T = now, Kind = kind, Team = team };
            _burst = (_burst + 1) % Bursts;
        }

        void AddBeam(int from, int to, double now)
        {
            if (from < 0 || to < 0) return;
            BeamRing[_beam] = new Beam { From = from, To = to, T = now };
            _beam = (_beam + 1) % Beams;
        }

        static int NearestAlly(World w, int healer, double x, double z)
        {
            if (healer < 0 || healer >= w.ActorCount) return -1;
            TeamId t = w.Actors[healer].Team;
            int best = -1;
            double bd = 1.0;   // the mended ally stands at (x, z): accept within 1 unit
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (i == healer || w.Actors[i].Team != t || !w.Actors[i].Alive) continue;
                double dx = w.X[i] - x, dz = w.Z[i] - z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void Source(World w, SimEvent e, out double sx, out double sz)
        {
            if (e.B >= 0 && e.B < w.ActorCount) { sx = w.X[e.B]; sz = w.Z[e.B]; return; }
            if (e.Cause == DamageCause.KaijuSlam) { sx = w.Kaiju.TelegraphX; sz = w.Kaiju.TelegraphZ; return; }
            if (e.B == Damage.KaijuSource || e.Cause == DamageCause.KaijuMelee) { sx = w.Kaiju.X; sz = w.Kaiju.Z; return; }
            sx = e.X;
            sz = e.Z;
        }

        void OnHit(World w, SimEvent e, double now)
        {
            int v = e.A;
            if (v < 0 || v >= w.ActorCount) return;
            Source(w, e, out double sx, out double sz);
            double dx = e.X - sx, dz = e.Z - sz, d = Math.Sqrt(dx * dx + dz * dz);
            if (d > 1e-6) { dx /= d; dz /= d; }
            else { dx = -Math.Cos(w.Actors[v].Facing); dz = -Math.Sin(w.Actors[v].Facing); }
            if (MechPhysics.Push(e, out double px, out double pz, out _))
            {
                double pl = Math.Sqrt(px * px + pz * pz);
                dx = px / pl;
                dz = pz / pl;
            }
            if (Near(w, v))
            {
                StreakRing[_streak] = new Streak { Victim = v, Dx = dx, Dz = dz, T = now, Dmg = e.Value };
                _streak = (_streak + 1) % Streaks;
            }
            // The slam always reads: a short stagger, unless a real stun is already running (a HitStun event later this
            // tick overwrites it). PR #49 ships knockback and stun OFF by default, so this is what plays until they are on.
            if (e.Cause == DamageCause.KaijuSlam && !(Stunned(v, now) && StunLen[v] > FallbackStaggerWall * Pace + 1e-9))
            {
                StunStart[v] = now;
                StunLen[v] = FallbackStaggerWall * Math.Max(0.01, Pace);
                StunDirX[v] = dx;
                StunDirZ[v] = dz;
            }
            if (v != LocalActor) return;
            if (e.Cause == DamageCause.KaijuSlam) _slamHurt = true;
            int k = HitCount % LocalHits;
            Actor att = e.B >= 0 && e.B < w.ActorCount ? w.Actors[e.B] : null;
            HitRing[k] = new LocalHit
            {
                Attacker = e.B,
                Team = att != null ? att.Team : TeamId.Azure,
                Cls = att != null ? att.Cls : ClassId.Warrior,
                Cause = e.Cause,
                BlastKind = 0,
                SrcX = sx,
                SrcZ = sz,
                T = now,
                Dmg = e.Value,
                Fatal = !w.Actors[v].Alive,
            };
            if (e.Cause == DamageCause.Explosion) _pendingBlastHit = HitCount;
            HitCount++;
        }

        /// <summary>PR #49 Knock (only while knockback is on): a long streak along the push, sized by the travelled distance.</summary>
        void OnKnock(World w, SimEvent e, double now)
        {
            int v = e.A;
            if (v < 0 || v >= w.ActorCount || !Near(w, v)) return;
            double dx, dz, impulse;
            if (!MechPhysics.Push(e, out dx, out dz, out impulse))
            {
                double sx = e.B >= 0 && e.B < w.ActorCount ? w.X[e.B] : w.Kaiju.X, sz = e.B >= 0 && e.B < w.ActorCount ? w.Z[e.B] : w.Kaiju.Z;
                dx = w.X[v] - sx;
                dz = w.Z[v] - sz;
                impulse = 0.8;
            }
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-6) return;
            // Dmg doubles as the streak length driver: 40 = the longest streak; a 1 u push reads as the longest.
            StreakRing[_streak] = new Streak { Victim = v, Dx = dx / d, Dz = dz / d, T = now, Dmg = 40 * Math.Min(1.5, Math.Max(0.4, impulse)) };
            _streak = (_streak + 1) % Streaks;
        }

        void OnStun(World w, SimEvent e, double now)
        {
            int v = e.A;
            if (v < 0 || v >= StunStart.Length) return;
            // DG-70 contract: A = the actor, Value = stun ticks (assumed; 0.4 s on screen when the event carries none).
            double len = e.Value > 0 ? e.Value / w.Config.SimHz : 0.4 * Math.Max(0.01, Pace);
            StunStart[v] = now;
            StunLen[v] = len;
            double sx = e.B >= 0 && e.B < w.ActorCount ? w.X[e.B] : e.X, sz = e.B >= 0 && e.B < w.ActorCount ? w.Z[e.B] : e.Z;
            double dx = w.X[v] - sx, dz = w.Z[v] - sz, d = Math.Sqrt(dx * dx + dz * dz);
            StunDirX[v] = d > 1e-6 ? dx / d : -Math.Cos(w.Actors[v].Facing);
            StunDirZ[v] = d > 1e-6 ? dz / d : -Math.Sin(w.Actors[v].Facing);
        }

        void OnSlam(World w, SimEvent e, double now)
        {
            SlamAt = now;
            int me = LocalActor;
            if (me < 0 || me >= w.ActorCount || !w.Actors[me].Alive) return;
            double r = e.Value > 0 ? e.Value : w.Config.Kaiju.SlamRadius;
            _slamThisTick = true;
            _slamInside = SlamRead.Inside(w.X[me], w.Z[me], e.X, e.Z, r);
            _slamAirborne = w.Actors[me].AirLen > 0;
        }
    }
}
