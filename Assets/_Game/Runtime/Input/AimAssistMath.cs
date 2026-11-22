using System;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Input
{
    /// <summary>AIM HELP strength for one device class (Settings, ACCESS). Off leaves the raw aim untouched bit for bit.</summary>
    public enum AimHelpLevel { Off = 0, Low = 1, High = 2 }

    /// <summary>The device class whose AIM HELP setting applies (the device that aims right now).</summary>
    public enum AimDevice { Mouse = 0, Controller = 1, Keys = 2 }

    /// <summary>What the press this frame would do, so the assist knows what to bend toward.</summary>
    public enum AimPressKind
    {
        /// <summary>A melee swing (every class's attack, the Demolitionist's pry bar while her TNT cools).</summary>
        Melee = 0,
        /// <summary>The Demolitionist's attack press with TNT ready: a throw, led by min(fuse, TntLeadMaxSeconds).</summary>
        Tnt = 1,
        /// <summary>The Healer's attack press: swing at an enemy in lantern reach, else mend the hurt ally nearest the aim.</summary>
        Healer = 2,
        /// <summary>An aimed class ability (roots, pulse, mine): bent only onto a candidate inside the cone.</summary>
        Ability = 3,
    }

    /// <summary>
    /// Player-experience wave, lane I (2026-11-22): every number of the aim help, named. Defaults per the master plan, call 8:
    /// mouse Low, controller and keyboard-only High, Off available.
    /// </summary>
    public static class AimAssistTunables
    {
        /// <summary>High: cone half-angle (degrees) around the raw aim direction, and range past the class reach + body.</summary>
        public const double HighConeDeg = 50, HighRangeExtra = 2.5;
        /// <summary>Low: the same, narrower and shorter.</summary>
        public const double LowConeDeg = 25, LowRangeExtra = 1.5;
        /// <summary>Share of the angle to the candidate the aim turns while attacking (1 = onto the candidate).</summary>
        public const double AttackBend = 1.0;
        /// <summary>Share turned outside attacks (a gentle pull, the facing stays the player's own). Third person: 0 (the
        /// auto-follow camera follows the facing while standing, so an idle pull would steer the camera).</summary>
        public const double IdleBendLow = 0.0, IdleBendHigh = 0.2;
        /// <summary>After the attack ends (button up and the wind-up resolved) the bend holds this long (s).</summary>
        public const double StickySeconds = 0.4;
        /// <summary>The bots' own lead (CombatFeelConfig.BotLeadSeconds): the aim goes to position + velocity x lead.</summary>
        public const double LeadSeconds = 0.13;
        /// <summary>TNT lead cap (s): min(fuse, this).</summary>
        public const double TntLeadMaxSeconds = 0.5;
        /// <summary>A target already locked stays locked inside cone x this and range + KeepRangeExtra (no flicker between
        /// two enemies through one wind-up).</summary>
        public const double KeepConeMul = 1.4, KeepRangeExtra = 0.75;
        /// <summary>High only, "in your face": with nothing in the cone, an enemy inside the bare class reach + body + this
        /// margin is picked at any angle (God of War / Batman convention: an attack with no target ahead goes to the enemy
        /// touching you). Low and Off never do this.</summary>
        public const double CloseConeDeg = 180, CloseMargin = 0.25;
        /// <summary>Score = angle / cone + DistanceWeight x distance / range + BlockedPenalty when a wall is between (a clear
        /// line preferred; a blocked one is still picked when nothing else is in the cone, so the facing still turns).</summary>
        public const double DistanceWeight = 0.35, BlockedPenalty = 0.75;
        /// <summary>The sim's facing dead zone (Movement.AimDeadzone): nearer aim points do not turn the facing.</summary>
        public const double AimDeadzone = 0.05;

        public static AimHelpLevel DefaultFor(AimDevice d) => d == AimDevice.Mouse ? AimHelpLevel.Low : AimHelpLevel.High;
    }

    /// <summary>One tuning row (built from AimAssistTunables by level; tests and the probe may build their own).</summary>
    public struct AimAssistTuning
    {
        public double ConeDeg, RangeExtra, AttackBend, IdleBend, StickySeconds, LeadSeconds;
        /// <summary>The "in your face" fallback cone (0 = none; High: CloseConeDeg).</summary>
        public double CloseConeDeg;
        public bool Off => !(ConeDeg > 0) || (!(AttackBend > 0) && !(IdleBend > 0));

        public static AimAssistTuning For(AimHelpLevel level)
        {
            var t = new AimAssistTuning { AttackBend = AimAssistTunables.AttackBend, StickySeconds = AimAssistTunables.StickySeconds, LeadSeconds = AimAssistTunables.LeadSeconds };
            switch (level)
            {
                case AimHelpLevel.High:
                    t.ConeDeg = AimAssistTunables.HighConeDeg; t.RangeExtra = AimAssistTunables.HighRangeExtra; t.IdleBend = AimAssistTunables.IdleBendHigh;
                    t.CloseConeDeg = AimAssistTunables.CloseConeDeg;
                    break;
                case AimHelpLevel.Low:
                    t.ConeDeg = AimAssistTunables.LowConeDeg; t.RangeExtra = AimAssistTunables.LowRangeExtra; t.IdleBend = AimAssistTunables.IdleBendLow;
                    break;
                default:
                    t.ConeDeg = 0; t.RangeExtra = 0; t.AttackBend = 0; t.IdleBend = 0;
                    break;
            }
            return t;
        }
    }

    /// <summary>One actor the assist may consider (copied from the World by the caller, read-only).</summary>
    public struct AimCandidate
    {
        public int Id;
        public double X, Z, Vx, Vz, Radius;
        /// <summary>Other team (false = an ally: only the Healer's mend looks at allies).</summary>
        public bool Enemy;
        public bool Alive;
        /// <summary>Hidden from this player (fog of war): never a candidate, never hittable.</summary>
        public bool Hidden;
        /// <summary>The sim's own wall test from the fighter to its centre (SegmentClear.Clear) is clear.</summary>
        public bool ClearLine;
        /// <summary>Hp below MaxHp (the mend rule's "hurt").</summary>
        public bool Hurt;
    }

    /// <summary>This frame's input to the assist.</summary>
    public struct AimAssistFrame
    {
        /// <summary>Fighter position and current sim facing (radians).</summary>
        public double Px, Pz, Facing;
        /// <summary>The raw aim point (mouse ground point, stick/keys point, or the third-person camera point).</summary>
        public bool HasRaw;
        public double RawX, RawZ;
        /// <summary>Optional intent direction (unit, sim XZ) the cone is measured around instead of the raw aim: the move
        /// direction of a keys or one-stick player (keyboard-only top-down, and the third-person auto camera, whose yaw lags
        /// the travel). Not set for the mouse or a pushed right stick: there the aim is the intent.</summary>
        public bool HasIntent;
        public double IntentX, IntentZ;
        /// <summary>An attack press or hold this frame, or the sim's SwingPending (read-only): the wind-up is running.</summary>
        public bool Attacking;
        /// <summary>Seconds since the previous Step (render frame or sim tick).</summary>
        public double Dt;
        /// <summary>What a press does now, the class reach (AttackRange) and arc (degrees).</summary>
        public AimPressKind Kind;
        public double Reach, ArcDeg;
        /// <summary>Tnt: throw range + blast radius, and the lead (s). Healer: mend range.</summary>
        public double TntRange, TntLead, MendRange;
        /// <summary>The auto-follow third-person view is playing: no idle bend.</summary>
        public bool ThirdPerson;
    }

    /// <summary>
    /// Player-experience wave, lane I (2026-11-22): the aim help maths. Pure (no UnityEngine, no Runtime types; Sim maths
    /// only), so the same file compiles into the plain-C# human-seat probe that measured it. Never touches the camera: it
    /// only moves the aim point the sim receives. One instance per local seat (it keeps the lock and the stickiness).
    /// </summary>
    public sealed class AimAssistBrain
    {
        double _sticky;
        int _lock = -1;

        /// <summary>The candidate id locked on the last Step (-1 none); an enemy unless LockIsAlly.</summary>
        public int Lock => _lock;
        /// <summary>The last lock is an ally (the Healer's mend).</summary>
        public bool LockIsAlly { get; private set; }
        /// <summary>The last Step engaged a new lock (a different target, or none before): report it once.</summary>
        public bool JustEngaged { get; private set; }
        /// <summary>The share of the angle the last Step bent (0 = raw aim).</summary>
        public double BendUsed { get; private set; }

        public void Reset()
        {
            _sticky = 0;
            _lock = -1;
            LockIsAlly = false;
            JustEngaged = false;
            BendUsed = 0;
        }

        /// <summary>
        /// One frame. Returns the final aim point (ax, az): the raw point when the assist is off, has no raw aim, or finds no
        /// candidate (then bit for bit the raw values); else the raw direction turned toward the candidate's position +
        /// velocity x lead by the bend share, never by more than the cone it was picked in.
        /// </summary>
        public void Step(in AimAssistFrame f, AimCandidate[] c, int n, in AimAssistTuning t, out double ax, out double az)
        {
            ax = f.RawX;
            az = f.RawZ;
            int was = _lock;
            JustEngaged = false;
            BendUsed = 0;
            if (t.Off || !f.HasRaw || c == null)
            {
                _lock = -1;
                LockIsAlly = false;
                _sticky = 0;
                return;
            }
            if (f.Attacking) _sticky = t.StickySeconds;
            else if (_sticky > 0) _sticky -= f.Dt > 0 ? f.Dt : 0;
            bool attackPhase = f.Attacking || _sticky > 0;
            double bend = attackPhase ? t.AttackBend : f.ThirdPerson ? 0 : t.IdleBend;
            // Abilities bend only on the press itself (Attacking carries the ability press for them).
            if (f.Kind == AimPressKind.Ability && !f.Attacking) bend = 0;
            if (!(bend > 0))
            {
                _lock = -1;
                LockIsAlly = false;
                return;
            }

            double rdx = f.RawX - f.Px, rdz = f.RawZ - f.Pz;
            double rlen = Math.Sqrt(rdx * rdx + rdz * rdz);
            double ux, uz;
            if (rlen > 1e-9) { ux = rdx / rlen; uz = rdz / rlen; }
            else { ux = Math.Cos(f.Facing); uz = Math.Sin(f.Facing); }

            // The cone is measured around the intent (the move direction) when the caller gives one.
            double cx0 = ux, cz0 = uz;
            if (f.HasIntent)
            {
                double il = Math.Sqrt(f.IntentX * f.IntentX + f.IntentZ * f.IntentZ);
                if (il > 1e-9) { cx0 = f.IntentX / il; cz0 = f.IntentZ / il; }
            }
            double cone = t.ConeDeg;
            bool ally = false;
            int pick = PickEnemy(f, c, n, t, cx0, cz0, was, LockIsAlly, out double pickCone);
            if (f.Kind == AimPressKind.Healer)
            {
                // Swing only when the enemy is inside her lantern reach (S2's rule: aim within 1.2 u of such an enemy swings);
                // else the hurt ally nearest the raw aim inside the cone; else she still faces the enemy she aims near.
                bool inLantern = false;
                if (pick >= 0)
                {
                    double ex = c[pick].X - f.Px, ez = c[pick].Z - f.Pz;
                    double lr = f.Reach + c[pick].Radius;
                    inLantern = ex * ex + ez * ez <= lr * lr;
                }
                if (!inLantern)
                {
                    int mate = PickHurtAlly(f, c, n, t, cx0, cz0, was, out double mateCone);
                    if (mate >= 0)
                    {
                        pick = mate;
                        pickCone = mateCone;
                        ally = true;
                    }
                }
            }
            if (pick < 0)
            {
                _lock = -1;
                LockIsAlly = false;
                return;
            }
            cone = pickCone;
            double lead = f.Kind == AimPressKind.Tnt ? f.TntLead : ally ? 0 : t.LeadSeconds;
            double tx = c[pick].X + c[pick].Vx * lead, tz = c[pick].Z + c[pick].Vz * lead;
            // The led point must stay inside the cone it was picked in (measured from the intent); else aim at the body.
            if (!InCone(f.Px, f.Pz, tx, tz, cx0, cz0, cone)) { tx = c[pick].X; tz = c[pick].Z; }
            double tdx = tx - f.Px, tdz = tz - f.Pz;
            double tlen = Math.Sqrt(tdx * tdx + tdz * tdz);
            if (tlen < 1e-9) { _lock = -1; LockIsAlly = false; return; }
            if (bend >= 1)
            {
                ax = tx;
                az = tz;
            }
            else
            {
                // A gentle pull: the raw direction turned toward the target by the bend share, at the raw distance.
                double cross = ux * (tdz / tlen) - uz * (tdx / tlen), dot = ux * (tdx / tlen) + uz * (tdz / tlen);
                double turn = Math.Atan2(cross, dot) * bend;
                double dist = rlen > 1e-9 ? rlen : tlen;
                double cs = Math.Cos(turn), sn = Math.Sin(turn);
                ax = f.Px + (ux * cs - uz * sn) * dist;
                az = f.Pz + (ux * sn + uz * cs) * dist;
            }
            BendUsed = Math.Min(1, bend);
            int id = c[pick].Id;
            JustEngaged = id != was || ally != LockIsAlly;
            _lock = id;
            LockIsAlly = ally;
        }

        static int PickEnemy(in AimAssistFrame f, AimCandidate[] c, int n, in AimAssistTuning t, double ux, double uz, int was, bool wasAlly, out double pickCone)
        {
            int best = -1;
            double bestScore = double.PositiveInfinity;
            pickCone = t.ConeDeg;
            for (int i = 0; i < n && i < c.Length; i++)
            {
                AimCandidate k = c[i];
                if (!k.Enemy || !k.Alive || k.Hidden) continue;
                double range = (f.Kind == AimPressKind.Tnt ? f.TntRange : f.Reach + k.Radius) + t.RangeExtra;
                double coneDeg = t.ConeDeg;
                if (k.Id == was && !wasAlly) { coneDeg *= AimAssistTunables.KeepConeMul; range += AimAssistTunables.KeepRangeExtra; }
                if (!Inside(f, k, ux, uz, coneDeg, range, out double ang, out double d)) continue;
                double score = ang / coneDeg + AimAssistTunables.DistanceWeight * d / range + (k.ClearLine ? 0 : AimAssistTunables.BlockedPenalty);
                if (score < bestScore - 1e-12)
                {
                    bestScore = score;
                    best = i;
                    pickCone = coneDeg;
                }
            }
            if (best >= 0 || !(t.CloseConeDeg > 0) || f.Kind == AimPressKind.Tnt || f.Kind == AimPressKind.Ability) return best;
            // High: nothing ahead, so the enemy in your face (inside the bare reach + body), nearest first, any side.
            double bestD = double.PositiveInfinity;
            for (int i = 0; i < n && i < c.Length; i++)
            {
                AimCandidate k = c[i];
                if (!k.Enemy || !k.Alive || k.Hidden || !k.ClearLine) continue;
                double range = f.Reach + k.Radius + AimAssistTunables.CloseMargin;
                if (!Inside(f, k, ux, uz, t.CloseConeDeg, range, out _, out double d)) continue;
                if (d < bestD) { bestD = d; best = i; pickCone = t.CloseConeDeg; }
            }
            return best;
        }

        /// <summary>The point (x, z) seen from (px, pz) lies within coneDeg of the unit direction (ux, uz).</summary>
        public static bool InCone(double px, double pz, double x, double z, double ux, double uz, double coneDeg)
        {
            double dx = x - px, dz = z - pz, d = Math.Sqrt(dx * dx + dz * dz);
            if (d < 1e-9) return true;
            return Math.Acos(Math.Max(-1, Math.Min(1, (dx * ux + dz * uz) / d))) * 180 / Math.PI <= coneDeg + 1e-9;
        }

        static int PickHurtAlly(in AimAssistFrame f, AimCandidate[] c, int n, in AimAssistTuning t, double ux, double uz, int was, out double pickCone)
        {
            int best = -1;
            double bestAng = double.PositiveInfinity;
            pickCone = t.ConeDeg;
            for (int i = 0; i < n && i < c.Length; i++)
            {
                AimCandidate k = c[i];
                if (k.Enemy || !k.Alive || k.Hidden || !k.Hurt) continue;
                double coneDeg = t.ConeDeg * (k.Id == was ? AimAssistTunables.KeepConeMul : 1);
                if (!Inside(f, k, ux, uz, coneDeg, f.MendRange, out double ang, out _)) continue;
                if (ang < bestAng - 1e-12)
                {
                    bestAng = ang;
                    best = i;
                    pickCone = coneDeg;
                }
            }
            return best;
        }

        /// <summary>Within range (centre distance) and within the cone half-angle (degrees) of the unit raw direction.</summary>
        public static bool Inside(in AimAssistFrame f, in AimCandidate k, double ux, double uz, double coneDeg, double range, out double angDeg, out double d)
        {
            double dx = k.X - f.Px, dz = k.Z - f.Pz;
            d = Math.Sqrt(dx * dx + dz * dz);
            angDeg = 0;
            if (d > range) return false;
            if (d < 1e-9) return true;
            double cosA = (dx * ux + dz * uz) / d;
            angDeg = Math.Acos(Math.Max(-1, Math.Min(1, cosA))) * 180 / Math.PI;
            return angDeg <= coneDeg + 1e-9;
        }
    }

    /// <summary>The sim's hit shape, read-only (Combat.ResolveSwing): the same maths, the same functions.</summary>
    public static class AimHitTest
    {
        /// <summary>
        /// The facing the sim will take from this aim point (Movement: atan2 toward the aim when it is farther than the dead
        /// zone, else the current facing).
        /// </summary>
        public static double FacingFor(double px, double pz, double aimX, double aimZ, double facing)
        {
            double ax = aimX - px, az = aimZ - pz;
            return ax * ax + az * az > AimAssistTunables.AimDeadzone * AimAssistTunables.AimDeadzone ? JsMath.Atan2(az, ax) : facing;
        }

        /// <summary>
        /// Would a swing from (px, pz) along facing (radians) with range and arc (degrees) hit a target of radius r at (tx, tz)?
        /// ResolveSwing: d &lt;= range + r; dot &gt;= WidenedCos(half, r, d) - 1e-9 when body is on (ArcCoversBody), else cos(half);
        /// clearLine stands for SegmentClear.Clear when walls block (WallsBlockHits).
        /// </summary>
        public static bool SwingHits(double px, double pz, double facing, double range, double arcDeg, bool body, bool walls,
            double tx, double tz, double r, bool clearLine)
        {
            double fx = JsMath.Cos(facing), fz = JsMath.Sin(facing);
            double cosHalf = JsMath.Cos(arcDeg / 2 * Math.PI / 180), halfRad = arcDeg / 2 * Math.PI / 180;
            double dx = tx - px, dz = tz - pz;
            double reach = range + r;
            double d2 = dx * dx + dz * dz;
            if (d2 > reach * reach) return false;
            if (d2 > 1e-12)
            {
                double dot = (dx * fx + dz * fz) / Math.Sqrt(d2);
                if (dot < (body ? Combat.WidenedCos(halfRad, r, Math.Sqrt(d2)) : cosHalf) - 1e-9) return false;
            }
            if (walls && !clearLine) return false;
            return true;
        }

        /// <summary>The nearest living, visible enemy a swing along facing would hit (index into c), -1 none.</summary>
        public static int Nearest(double px, double pz, double facing, double range, double arcDeg, bool body, bool walls, AimCandidate[] c, int n)
        {
            int best = -1;
            double bestD = double.PositiveInfinity;
            for (int i = 0; i < n && i < c.Length; i++)
            {
                AimCandidate k = c[i];
                if (!k.Enemy || !k.Alive || k.Hidden) continue;
                if (!SwingHits(px, pz, facing, range, arcDeg, body, walls, k.X, k.Z, k.Radius, k.ClearLine)) continue;
                double dx = k.X - px, dz = k.Z - pz, d2 = dx * dx + dz * dz;
                if (d2 < bestD) { bestD = d2; best = i; }
            }
            return best;
        }
    }

    /// <summary>
    /// Lane I item 2: cast distance on stick and keys (pure). Stick: deflection 0..1 maps MinCast .. the ability's max range.
    /// Keys: holding the ability button extends the reach from DefaultCast to max over HoldExtendSeconds; release casts.
    /// </summary>
    public static class CastDistance
    {
        /// <summary>Nearest cast distance (stick barely pushed) and the default (a quick tap), sim units.</summary>
        public const double MinCast = 1.0, DefaultCast = 3.0;
        /// <summary>Keys: seconds of holding to reach the maximum range.</summary>
        public const double HoldExtendSeconds = 0.5;
        /// <summary>A key press shorter than this is a tap: cast at once at the default (or at the lock candidate).</summary>
        public const double TapSeconds = 0.12;
        /// <summary>Stick deflection under this (after the dead zone) counts as "no distance chosen": the default.</summary>
        public const double StickChooseMin = 0.25;

        public static double FromStick(double deflection, double maxRange)
        {
            if (!(maxRange > MinCast)) return Math.Max(0, maxRange);
            if (!(deflection >= StickChooseMin)) return Math.Min(DefaultCast, maxRange);
            double k = Math.Min(1, (deflection - StickChooseMin) / (1 - StickChooseMin));
            return MinCast + (maxRange - MinCast) * k;
        }

        public static double FromHold(double heldSeconds, double maxRange)
        {
            double start = Math.Min(DefaultCast, maxRange);
            if (!(heldSeconds > 0)) return start;
            double k = Math.Min(1, heldSeconds / HoldExtendSeconds);
            return start + (Math.Max(start, maxRange) - start) * k;
        }
    }
}
