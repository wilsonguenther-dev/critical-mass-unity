using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Hud.Player
{
    /// <summary>
    /// Player-experience wave (2026-11-22), lane D: the local player's combat HUD, what the player is TOLD. GameHud builds
    /// one at Build (world layer + screen root) and ticks it once per LateUpdate after the camera has moved. It reads the
    /// sim event ring with its own cursor (one pass, allocation-free) and hands each event the local player cares about to
    /// the piece that shows it; every piece reports to the PlayerFeedback ledger at the moment its cue becomes visible.
    /// Threat hierarchy: MY hits, MY hurts, MY kills get the strong cues; other people's events stay quiet.
    /// View only: never writes the sim.
    /// </summary>
    public sealed class PlayerHud
    {
        public static PlayerHud Current { get; private set; }

        public readonly HealthBars Bars;
        public readonly HitMarker Marker;
        public readonly DamageDirection Direction;
        public readonly HurtEdge Hurt;
        public readonly KillConfirm Kills;
        public readonly ActionTags Tags;
        public readonly DamageNumbers Numbers;
        public readonly AssistTracker Assists = new AssistTracker();

        World _world;
        int _epoch = -1;
        int _cursor = -1;
        double _alpha = 1.0;

        /// <summary>Sim events dropped because the HUD fell a whole ring behind (tests).</summary>
        public int Dropped { get; private set; }

        public PlayerHud(RectTransform root, RectTransform world, RectTransform playerCard)
        {
            Bars = new HealthBars(world);
            Marker = new HitMarker(world);
            Direction = new DamageDirection(world);
            Hurt = new HurtEdge(root, playerCard);
            Kills = new KillConfirm(root);
            Tags = new ActionTags(world);
            Numbers = new DamageNumbers(world);
            Current = this;
        }

        /// <summary>One frame: events since the last frame, then every piece's animation and placement.</summary>
        public void Tick(World w, HudState s, double alpha, UnityEngine.Camera cam, float scale, float dt)
        {
            if (w == null || cam == null) return;
            int me = HudBinder.LocalActor;
            _alpha = alpha;
            if (!ReferenceEquals(w, _world) || w.Epoch != _epoch)
            {
                _world = w;
                _epoch = w.Epoch;
                _cursor = w.EventHead;
                Bars.Clear();
                Marker.Clear();
                Direction.Clear();
                Hurt.Clear();
                Kills.Clear();
                Tags.Clear();
                Numbers.Clear();
                Assists.Clear();
            }
            if (_cursor < 0 || _cursor > w.EventHead) _cursor = w.EventHead;
            if (w.EventHead - _cursor > World.EventCapacity)
            {
                Dropped += w.EventHead - World.EventCapacity - _cursor;
                RuntimeCounters.HudEventsDropped += w.EventHead - World.EventCapacity - _cursor;   // DG-60 (was GameHud's loop)
                _cursor = w.EventHead - World.EventCapacity;
            }
            ScanBlasts(w);
            for (; _cursor < w.EventHead; _cursor++) OnEvent(w, w.Events[_cursor % World.EventCapacity], me);
            Bars.Tick(w, me, alpha, cam, scale, dt);
            Marker.Tick(w, me, alpha, cam, scale, dt);
            Direction.Tick(w, me, alpha, cam, scale, dt);
            Kills.Tick(dt);
            Tags.Tick(w, alpha, cam, scale, dt);
            Numbers.Tick(w, alpha, cam, scale, dt);
            Hurt.Tick(me, s != null && s.HasPlayer ? s.Hp01 : 1f, s != null && s.HasPlayer && s.Alive, dt);
        }

        void OnEvent(World w, SimEvent e, int me)
        {
            switch (e.Type)
            {
                case SimEventType.Hit:
                    if (e.A >= 0 && e.B >= 0) Assists.OnHit(e.A, e.B, w.Match.Time);
                    if (e.B == me && e.A != me && e.A >= 0)
                    {
                        Bars.OnMyHit(e.A, w.Match.Time);
                        Marker.OnMyHit(w, e.A, e.Tier, _alpha);
                        Numbers.Add(e.A, PxProject.Actor(w, e.A, _alpha, DamageNumbers.NumberLift), (float)e.Value,
                            e.Tier >= 2 ? DamageNumbers.Kind.Crit : e.Tier == 1 ? DamageNumbers.Kind.Heavy : DamageNumbers.Kind.Hit);
                    }
                    else if (e.A == me && e.B != me && me >= 0 && me < w.ActorCount) OnHurt(w, e, me);
                    break;
                case SimEventType.Death:
                {
                    bool assist = Assists.OnDeath(e.A, e.B, w.Match.Time, me, me);
                    if (e.B == me && e.A != me && e.A >= 0)
                    {
                        Marker.OnMyKill(w, e.A, e.X, e.Z);
                        Kills.OnMyKill(w, e.A);
                    }
                    else if (assist && e.A != me) Kills.OnMyAssist(w, e.A);
                    break;
                }
                case SimEventType.HitBlocked:
                    if (e.B == me && e.A != me) Tags.OnBlocked(w, e.A, (int)e.Value, e.X, e.Z, e.Radius);
                    break;
                case SimEventType.AttackCancel:
                    if (e.A == me) Tags.OnInterrupted(w, me);
                    break;
                case SimEventType.Feint:
                    if (e.A == me && e.Value < 1.5) Tags.OnFeint(w, me);
                    break;
                case SimEventType.ActionRefused:
                    if (e.A == me) Tags.OnActionRefused(w, me, e.B, (int)e.Value);
                    break;
                case SimEventType.OreMined:
                    if (e.A == me && e.Value < 0) { int code = (int)e.Value; Tags.OnRefused(w, me, ActionTags.OreRefusal(code, out int why), why, 900 - code); }
                    break;
                case SimEventType.WallBuilt:
                    if (e.A == me && e.Value < 0) { int code = (int)e.Value; Tags.OnRefused(w, me, ActionTags.WallRefusal(code, out int why), why, 920 - code); }
                    break;
                case SimEventType.DeliverRefused:
                    if (e.A == me) Tags.OnRefused(w, me, ActionTags.DeliverRefusal(e.B), PlayerReasons.RefusedNotYourClass, 940 + e.B);
                    break;
                case SimEventType.JumpRefused:
                    if (e.A == me) Tags.OnRefused(w, me, ActionTags.JumpRefusal((int)e.Value), PlayerReasons.RefusedBlockedPlace, 950 + (int)e.Value);
                    break;
                case SimEventType.BridgeRepaired:
                    if (e.A == me && e.Value <= -1.5) Tags.OnRefused(w, me, ActionTags.NeedWoodRepair, PlayerReasons.RefusedNoResource, 960);
                    break;
                case SimEventType.StructureHit:
                    if (e.A == me && e.B == PlayerReasons.StructureSolid && e.Value <= 0) Tags.OnCantBreak(w, e.X, e.Z, e.B);
                    if (e.A == me && e.Value > 0)
                    {
                        Bars.OnStructureHit(w, e.X, e.Z, (float)e.Radius, e.B);
                        Marker.OnMyHitAt(PxProject.Point(w, e.X, e.Z, 1.2f), 0);
                        Numbers.Add(-1, PxProject.Point(w, e.X, e.Z, 1.9f), (float)e.Value, DamageNumbers.Kind.Structure);
                    }
                    break;
                case SimEventType.KaijuDamaged:
                    if (e.A == me)
                    {
                        Bars.OnMyKaijuHit(w.Match.Time);
                        Marker.OnMyHitAt(PxProject.Point(w, e.X, e.Z, 1.6f), e.Value >= 20 ? 1 : 0);
                        Numbers.Add(-1, PxProject.Point(w, e.X, e.Z, 2.6f), (float)e.Value, DamageNumbers.Kind.Kaiju);
                    }
                    break;
                case SimEventType.HealLanded:
                    if (e.B == me && e.A != me) Numbers.AddHeal(e.A, (float)e.Value);
                    if (e.A == me) Numbers.AddHeal(me, (float)e.Value);
                    break;
                case SimEventType.Heal:
                    if (e.A == me) Numbers.AddHeal(me, (float)e.Value);
                    break;
            }
        }

        // ---- the receiving end

        const int BlastCap = 8;
        readonly int[] _blastOwner = new int[BlastCap];
        readonly double[] _blastX = new double[BlastCap], _blastZ = new double[BlastCap];
        int _blasts;

        /// <summary>This batch's explosions first (a blast's hits may precede its Explosion event in the ring).</summary>
        void ScanBlasts(World w)
        {
            _blasts = 0;
            for (int k = _cursor; k < w.EventHead && _blasts < BlastCap; k++)
            {
                SimEvent e = w.Events[k % World.EventCapacity];
                if (e.Type != SimEventType.Explosion) continue;
                _blastOwner[_blasts] = e.A;
                _blastX[_blasts] = e.X;
                _blastZ[_blasts] = e.Z;
                _blasts++;
            }
        }

        void OnHurt(World w, SimEvent e, int me)
        {
            float share = w.Actors[me].MaxHp > 0 ? (float)(e.Value / w.Actors[me].MaxHp) : 0.1f;
            int src = DamageDirection.SourcePoint;
            Vector3 point = PxProject.Point(w, e.X - e.DirX * 2.0, e.Z - e.DirZ * 2.0, 1.0f);
            int blast = -1;
            if (e.Cause == DamageCause.Explosion)
                for (int i = 0; i < _blasts; i++) if (_blastOwner[i] == e.B || blast < 0) blast = i;
            if (blast >= 0) point = PxProject.Point(w, _blastX[blast], _blastZ[blast], 1.0f);
            else if (e.B == Damage.KaijuSource || e.B == Damage.KaijuSlamSource || e.Cause == DamageCause.KaijuMelee || e.Cause == DamageCause.KaijuSlam) src = DamageDirection.SourceKaiju;
            else if (e.B >= 0 && e.B < w.ActorCount) src = e.B;
            Direction.OnHurt(src, point, share);
            Hurt.OnHurt(me, share);
        }
    }
}
