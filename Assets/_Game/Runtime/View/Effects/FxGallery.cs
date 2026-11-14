#if UNITY_EDITOR || DEVELOPMENT_BUILD
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.View.Effects
{
    /// <summary>
    /// The effects gallery (development builds and the editor only): F6, or Effects.PlayGallery(), plays every effect in a
    /// row beside the local player, one every 1.1 s wall, so the integrator captures them all in one run. Steps are spawned
    /// into the scene directly (no sim event, no vignette, no pose hold) at the current sim tick. The last three steps
    /// repeat the hit tiers at GORE Off, then two at Reduced, to show the levels; the GORE level is restored afterwards.
    /// </summary>
    public sealed class FxGallery
    {
        public enum Step
        {
            HitLight, HitHeavy, HitCritical, Finisher, KaijuClaw, Block,
            SwingWarrior, SwingFarmer, SwingMiner, SwingDemolitions, SwingBuilder,
            BloodSpray, KnockOutPool, Gibs, Drips,
            Tnt, Mine, Barrel, WallBreak, KaijuSlam,
            Splash, Landing, FootPuff, PlankDust, Rustle,
            BiomassGather, BiomassDeliver, CorruptionDeliver, CorruptionBubbles, Respawn,
            BloodOffLight, BloodOffHeavy, BloodOffFinisher,
            GoreReducedHeavy, GoreReducedFinisher,
        }

        public static readonly Step[] Order = (Step[])System.Enum.GetValues(typeof(Step));

        private readonly FxScene _scene;
        private readonly CombatAudio _audio;
        private double _start = double.NaN;
        private int _next;
        private GoreLevel _goreWas;
        private double _ox, _oz;

        public FxGallery(FxScene scene, CombatAudio audio)
        {
            _scene = scene;
            _audio = audio;
        }

        public bool Running => !double.IsNaN(_start);
        /// <summary>The step that fires next (captures name their frames with it).</summary>
        public Step Current => _next < Order.Length ? Order[_next] : Order[Order.Length - 1];

        public void Begin(double now, World w)
        {
            if (Running) _scene.Gore = _goreWas;
            _start = now;
            _next = 0;
            _goreWas = _scene.Gore;
            _ox = w.ActorCount > 0 ? w.X[0] : w.Map.Width * 0.5;
            _oz = w.ActorCount > 0 ? w.Z[0] : w.Map.Height * 0.5;
        }

        /// <summary>Fire every step whose time has come; true while the gallery is running (CombatFx then skips its
        /// once-a-second blood re-read so the blood-off steps stay off).</summary>
        public bool Update(double now, World w)
        {
            if (!Running) return false;
            double gap = _scene.S(1.1);
            while (_next < Order.Length && now >= _start + _next * gap)
            {
                Fire(Order[_next], w);
                _next++;
            }
            if (_next >= Order.Length && now >= _start + Order.Length * gap)
            {
                _scene.Gore = _goreWas;
                _start = double.NaN;
                return false;
            }
            return true;
        }

        private FxEvent Ev(FxKind kind, World w, double x, double z, double value)
        {
            return new FxEvent
            {
                Kind = kind, Tick = w.Tick, A = -1, B = -1, X = x, Z = z, Value = value, DirX = 1, DirZ = 0,
                Team = TeamId.Azure, Cls = ClassId.Miner, Tier = CombatFeed.TierOf(value, kind == FxKind.Death),
                Heavy = value >= CombatFeed.HeavyDamage,
            };
        }

        private void Send(ref FxEvent e, World w)
        {
            _scene.OnFx(ref e, w);
            _audio?.OnFx(ref e, w);
        }

        private void Hit(World w, double x, double z, double dmg, bool death, ClassId attacker)
        {
            FxEvent e = Ev(death ? FxKind.Death : FxKind.Hit, w, x, z, dmg);
            e.HasAttacker = true;
            e.AttCls = attacker;
            Send(ref e, w);
        }

        private void Fire(Step s, World w)
        {
            double x = _ox + 2.5, z = _oz, far = _ox + 5.0;
            double t = w.Tick / w.Config.SimHz;
            switch (s)
            {
                case Step.HitLight: Hit(w, x, z, 12, false, ClassId.Farmer); break;
                case Step.HitHeavy: Hit(w, x, z, 22, false, ClassId.Warrior); break;
                case Step.HitCritical: Hit(w, x, z, 45, false, ClassId.Demolitions); break;
                case Step.Finisher: Hit(w, x, z, 22, true, ClassId.Warrior); break;
                case Step.KaijuClaw:
                {
                    FxEvent e = Ev(FxKind.Hit, w, x, z, 30);
                    e.B = Damage.KaijuSource;
                    Send(ref e, w);
                    break;
                }
                case Step.Block:
                {
                    FxEvent e = Ev(FxKind.Block, w, x, z, 22);
                    e.DirX = -1;
                    Send(ref e, w);
                    break;
                }
                case Step.SwingWarrior:
                case Step.SwingFarmer:
                case Step.SwingMiner:
                case Step.SwingDemolitions:
                case Step.SwingBuilder:
                {
                    ClassId c = s == Step.SwingWarrior ? ClassId.Warrior : s == Step.SwingFarmer ? ClassId.Farmer :
                        s == Step.SwingMiner ? ClassId.Miner : s == Step.SwingDemolitions ? ClassId.Demolitions : ClassId.Builder;
                    FxEvent e = Ev(FxKind.Swing, w, x, z, w.Config.Class(c).AttackArcDeg);
                    e.Cls = c;
                    Send(ref e, w);
                    break;
                }
                case Step.BloodSpray: Hit(w, x, z, 28, false, ClassId.Warrior); Hit(w, x, z + 0.6, 28, false, ClassId.Miner); break;
                case Step.KnockOutPool: Hit(w, x, z, 30, true, ClassId.Miner); break;
                case Step.Gibs: Hit(w, x, z, 45, true, ClassId.Demolitions); _scene.DebugGibs(t, x, z); break;
                case Step.Drips: _scene.DebugDrips(t, x - 1.5, z, 8); break;
                case Step.Tnt: Boom(w, far, z, 2.2, ExplosionKind.Tnt); break;
                case Step.Mine: Boom(w, far, z, 1.6, ExplosionKind.Mine); break;
                case Step.Barrel: Boom(w, far, z, 2.0, ExplosionKind.Barrel); break;
                case Step.WallBreak: Boom(w, far, z, 1.1, ExplosionKind.WallBreak); break;
                case Step.KaijuSlam: Boom(w, far, z, w.Config.Kaiju.SlamRadius, ExplosionKind.KaijuSlam); break;
                case Step.Splash: { FxEvent e = Ev(FxKind.Splash, w, x, z, 1.2); Send(ref e, w); break; }
                case Step.Landing: { FxEvent e = Ev(FxKind.Land, w, x, z, 1); Send(ref e, w); break; }
                case Step.FootPuff: _scene.DebugFootPuff(t, x, z); break;
                case Step.PlankDust: _scene.DebugPlankDust(t, x, z); break;
                case Step.Rustle: _scene.DebugRustle(t, _ox, _oz); break;
                case Step.BiomassGather:
                {
                    FxEvent e = Ev(FxKind.Gather, w, x, z, 1);
                    e.A = w.ActorCount > 0 ? 0 : -1;
                    _scene.OnFx(ref e, w); // no audio, no carry check on actor 0 (it may carry anything)
                    break;
                }
                case Step.BiomassDeliver: { FxEvent e = Ev(FxKind.DeliverBiomass, w, x, z, 1); Send(ref e, w); break; }
                case Step.CorruptionDeliver: { FxEvent e = Ev(FxKind.DeliverCorruption, w, x, z, 1); Send(ref e, w); break; }
                case Step.CorruptionBubbles: _scene.DebugBubbles(t, x, z); break;
                case Step.Respawn: { FxEvent e = Ev(FxKind.Respawn, w, x, z, 0); Send(ref e, w); break; }
                case Step.BloodOffLight: _scene.Blood = false; Hit(w, x, z, 12, false, ClassId.Farmer); break;
                case Step.BloodOffHeavy: _scene.Blood = false; Hit(w, x, z, 22, false, ClassId.Warrior); break;
                case Step.BloodOffFinisher: _scene.Blood = false; Hit(w, x, z, 22, true, ClassId.Warrior); break;
                case Step.GoreReducedHeavy: _scene.Gore = GoreLevel.Reduced; Hit(w, x, z, 22, false, ClassId.Warrior); break;
                case Step.GoreReducedFinisher: _scene.Gore = GoreLevel.Reduced; Hit(w, x, z, 22, true, ClassId.Warrior); break;
            }
        }

        private void Boom(World w, double x, double z, double r, ExplosionKind k)
        {
            FxEvent e = Ev(FxKind.Explosion, w, x, z, r);
            e.B = (int)k;
            Send(ref e, w);
        }
    }

    /// <summary>Gallery-only entry points into effects that normally come from world state (development builds only).</summary>
    public sealed partial class FxScene
    {
        internal void DebugGibs(double t, double x, double z)
        {
            if (Blood) AddGibs(t, x, z, Ground(x, z), Hash((uint)(t * 1000), 1u, 2u), 1, 0);
        }

        internal void DebugDrips(double t, double x, double z, int n)
        {
            if (!Blood) return;
            for (int k = 0; k < n; k++)
                if (SurfaceTop(x + k * 0.35, z, out double top)) AddDrip(t + S(0.08 * k), W(x + k * 0.35, top, z), Hash((uint)k, 3u, 4u));
        }

        internal void DebugFootPuff(double t, double x, double z) => FootPuff(t, x, z, Ground(x, z), Hash((uint)(t * 1000), 5u, 6u), 1, 0);

        internal void DebugPlankDust(double t, double x, double z) => PlankDust(t, x, z, Ground(x, z), Hash((uint)(t * 1000), 7u, 8u), 1f);

        internal void DebugRustle(double t, double x, double z) => RustleArea(x, z, 4.0, t, Hash((uint)(t * 1000), 9u, 10u));

        internal void DebugBubbles(double t, double x, double z)
        {
            double h = Ground(x, z);
            for (int k = 0; k < 6; k++)
            {
                uint s = Hash((uint)k, 11u, 12u);
                AddBubble(t + S(0.1 * k), W(x + (k % 3) * 0.3, h + 1.0, z), s, 0.12f + 0.06f * R01(s, 1), 1.3f, S(0.75));
            }
        }
    }
}
#endif
