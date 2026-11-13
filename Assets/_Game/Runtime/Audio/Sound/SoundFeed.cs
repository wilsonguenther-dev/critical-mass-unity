using System;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.Audio.Sound
{
    /// <summary>One sound to start: a cue at a sim position, whose it is (own) and whether it threatens the player.</summary>
    public struct SoundRequest
    {
        public int Cue;
        public double X, Z;
        /// <summary>The local player (actor 0) did it or it happened to them: wins voices, skips cooldowns.</summary>
        public bool Own;
        /// <summary>A danger to the local player (a wind-up at them, a blast or armed enemy mine beside them).</summary>
        public bool Threat;
        public float Gain, Rate;
        public int Tick;
        /// <summary>The actor it belongs to, or -1.</summary>
        public int Actor;
    }

    /// <summary>Where SoundFeed sends what it decodes (the director, or a recorder in tests).</summary>
    public interface ISoundSink
    {
        void Play(in SoundRequest r);
        /// <summary>A TNT fuse burns at (x, z) until endTick (bomb slot keys it).</summary>
        void Fuse(int slot, double x, double z, int endTick, int cue);
        /// <summary>The berserk loop starts or stops.</summary>
        void Berserk(bool on, int cue);
    }

    /// <summary>
    /// Decodes, once per sim tick, every event in the World's event ring into sounds through the SoundTable routes, plus
    /// the few tick edges the sim raises no event for (jump takeoff and landing from Actor.AirLen, the local player's
    /// meat pickup from CarryMeat, the berserk state). Event driven: it reads only what the last tick produced, never
    /// scans for change between ticks. Reads the world only; nothing here reaches the sim. No allocation after construction.
    /// </summary>
    public sealed class SoundFeed
    {
        /// <summary>A blast, an armed enemy mine or a slam telegraph within this many units of the player is a threat.</summary>
        public const double ThreatRadius = 6.0;
        /// <summary>Damage at or above this on a shield is the heavy block.</summary>
        public const double HeavyBlock = 20.0;

        private readonly int _capacity;
        private readonly int[] _airLen;
        private int _head;
        private int _meat = -1;
        private int _hill;
        private bool _berserk;

        /// <summary>Events read and sounds requested since construction (tests, budget report).</summary>
        public long Decoded { get; private set; }
        public long Requested { get; private set; }
        public long Silenced { get; private set; }

        public SoundFeed(int actorCapacity)
        {
            _capacity = Math.Max(1, actorCapacity);
            _airLen = new int[_capacity];
        }

        public void Reset()
        {
            _head = 0;
            _meat = -1;
            _hill = 0;
            _berserk = false;
            Array.Clear(_airLen, 0, _airLen.Length);
        }

        /// <summary>Call after every sim tick (SimDriverBehaviour.Ticked).</summary>
        public void OnTick(World w, ISoundSink sink)
        {
            if (w == null || sink == null) return;
            if (w.EventHead < _head) Reset();
            int from = _head;
            if (w.EventHead - from > World.EventCapacity) from = w.EventHead - World.EventCapacity;
            for (int n = from; n < w.EventHead; n++)
            {
                Decoded++;
                Route(w.Events[n % World.EventCapacity], w, sink);
            }
            _head = w.EventHead;
            Edges(w, sink);
        }

        // ------------------------------------------------------------------ tick edges

        private void Edges(World w, ISoundSink sink)
        {
            for (int i = 0; i < w.ActorCount && i < _capacity; i++)
            {
                Actor a = w.Actors[i];
                int air = a.AirLen;
                if (_airLen[i] == 0 && air > 0 && a.Alive)
                    Emit(sink, SoundTable.EdgeCue(SoundEdge.JumpTakeoff), w, w.X[i], w.Z[i], i == 0, false, 1f, Weight(a.Cls), i);
                else if (_airLen[i] > 0 && air == 0 && a.Alive)
                {
                    bool heavy = a.Cls == ClassId.Warrior || a.Cls == ClassId.Miner;
                    Emit(sink, SoundTable.EdgeCue(heavy ? SoundEdge.JumpLandHeavy : SoundEdge.JumpLand), w, w.X[i], w.Z[i], i == 0, false, 1f, 1f, i);
                }
                _airLen[i] = air;
            }
            if (w.ActorCount > 0)
            {
                int meat = w.Actors[0].Alive ? w.Actors[0].CarryMeat : 0;
                if (_meat >= 0 && meat > _meat) Emit(sink, SoundTable.EdgeCue(SoundEdge.MeatPickup), w, w.X[0], w.Z[0], true, false, 1f, 1f, 0);
                _meat = meat;
            }
            bool berserk = w.Kaiju != null && w.Kaiju.State == KaijuState.Berserk && w.Match.Phase != MatchPhase.Ended;
            if (berserk != _berserk)
            {
                _berserk = berserk;
                sink.Berserk(berserk, SoundTable.EdgeCue(SoundEdge.BerserkLoop));
            }
        }

        /// <summary>Swing / jump pitch by class weight: heavy classes lower, light classes higher.</summary>
        public static float Weight(ClassId c)
        {
            switch (c)
            {
                case ClassId.Warrior: return 0.9f;
                case ClassId.Miner: return 0.93f;
                case ClassId.Healer: return 1.1f;
                case ClassId.Builder: return 1.04f;
                default: return 1f;
            }
        }

        // ------------------------------------------------------------------ the event ring

        private void Route(SimEvent ev, World w, ISoundSink sink)
        {
            int[] cues = SoundTable.SimCues(ev.Type, out Pick pick);
            if (pick == Pick.Silence || cues.Length == 0) { Silenced++; return; }
            bool ownA = ev.A == 0;
            switch (pick)
            {
                case Pick.Fixed:
                    Fixed(ev, w, sink, cues[0]);
                    return;
                case Pick.Hit:
                case Pick.Death:
                    Hit(ev, w, sink, cues, pick == Pick.Death);
                    return;
                case Pick.Swing:
                {
                    int a = ev.A;
                    if (a < 0 || a >= w.ActorCount) return;
                    ClassId c = w.Actors[a].Cls;
                    int k = (int)c;
                    int cue = k >= 0 && k < cues.Length ? cues[k] : cues[cues.Length - 1];
                    Emit(sink, cue, w, w.X[a], w.Z[a], ownA, false, 1f, Weight(c), a);
                    return;
                }
                case Pick.Bio:
                    Emit(sink, cues[ev.Bio == BiomassType.Meat && cues.Length > 1 ? 1 : 0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    return;
                case Pick.Wall:
                    Emit(sink, cues[ev.Value == Cell.BreakableCracked ? 0 : 1], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    return;
                case Pick.Hill:
                    Hill(ev, w, sink, cues);
                    return;
                case Pick.Ability:
                {
                    if (ev.Value <= 0)
                    {
                        if (ownA && cues.Length > 2) Emit(sink, cues[2], w, ev.X, ev.Z, true, false, 1f, 1f, 0);
                        return;
                    }
                    if (ev.A < 0 || ev.A >= w.ActorCount) return;
                    ClassId c = w.Actors[ev.A].Cls;
                    // Demolitions, Builder and Healer successes also raise their own events (MinePlaced, PiecePlaced, ...).
                    if (c == ClassId.Warrior) Emit(sink, cues[0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    else if (c == ClassId.Farmer) Emit(sink, cues[1], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    return;
                }
                case Pick.Terraform:
                    if (ev.Value == Terraform.Dug) Emit(sink, cues[0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    else if (ev.Value == Terraform.Placed) Emit(sink, cues[1], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    else if (ownA && cues.Length > 2) Emit(sink, cues[2], w, ev.X, ev.Z, true, false, 1f, 1f, 0);
                    return;
                case Pick.Topple:
                    // A refused topple (negative code) is silent: the chip (ToppleHit) already sounded.
                    if (ev.Value > 0) Emit(sink, cues[0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    return;
                case Pick.Fuse:
                    Emit(sink, cues[0], w, ev.X, ev.Z, ownA, NearPlayer(w, ev.X, ev.Z) && !SameTeam(w, ev.A), 1f, 1f, ev.A);
                    if (cues.Length > 1) sink.Fuse(ev.B, ev.X, ev.Z, (int)ev.Value, cues[1]);
                    return;
                case Pick.Blast:
                {
                    int cue = ev.B == Demolitions.ExplosionMine && cues.Length > 1 ? cues[1] : cues[0];
                    float size = (float)Math.Max(0.7, Math.Min(1.3, ev.Radius / 2.5));
                    Emit(sink, cue, w, ev.X, ev.Z, ownA, NearPlayer(w, ev.X, ev.Z), 1f, 1.15f - 0.15f * size, ev.A);
                    return;
                }
                case Pick.Piece:
                {
                    int kind = PieceKind(w, ev.B);
                    Emit(sink, cues[kind == BuiltPieces.Bridge && cues.Length > 1 ? 1 : 0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    return;
                }
                case Pick.PieceEnd:
                    if (ev.Value == BuiltPieces.EndExpired) Emit(sink, cues[0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    else if (ev.Value == BuiltPieces.EndDropped && cues.Length > 1) Emit(sink, cues[1], w, ev.X, ev.Z, ownA, false, 1f, 0.85f, ev.A);
                    else Silenced++; // EndBroken: PieceDestroyed already played the break
                    return;
                case Pick.Heal:
                    Emit(sink, cues[ev.B == Healer.KindPulse && cues.Length > 1 ? 1 : 0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    return;
                default:
                    Silenced++;
                    return;
            }
        }

        private void Fixed(SimEvent ev, World w, ISoundSink sink, int cue)
        {
            bool ownA = ev.A == 0;
            switch (ev.Type)
            {
                case SimEventType.KaijuMeleeWindup:
                    // A = the target: a wind-up at you is the loudest telegraph in the game.
                    Emit(sink, cue, w, w.Kaiju.X, w.Kaiju.Z, false, ownA, ownA ? 1.15f : 1f, 1f, -1);
                    return;
                case SimEventType.KaijuTelegraph:
                    Emit(sink, cue, w, ev.X, ev.Z, false, Within(w, ev.X, ev.Z, w.Config.Kaiju.SlamRadius + 1.0), 1f, 1f, -1);
                    return;
                case SimEventType.KaijuSlam:
                case SimEventType.Hatch:
                case SimEventType.KaijuKO:
                case SimEventType.KaijuRecover:
                case SimEventType.BerserkLocked:
                    Emit(sink, cue, w, ev.X, ev.Z, false, false, 1f, 1f, -1);
                    return;
                case SimEventType.KaijuDamaged:
                    Emit(sink, cue, w, ev.X, ev.Z, ownA, false, 1f, (float)(1.05 - Math.Min(0.15, ev.Value / 300.0)), ev.A);
                    return;
                case SimEventType.MineArmed:
                    // Owner A: an enemy mine beside you is a threat; your own is a confirmation.
                    Emit(sink, cue, w, ev.X, ev.Z, ownA, !SameTeam(w, ev.A) && NearPlayer(w, ev.X, ev.Z), 1f, 1f, ev.A);
                    return;
                case SimEventType.MineTriggered:
                    Emit(sink, cue, w, ev.X, ev.Z, ownA || ev.B == 0, ownA, 1f, 1f, ev.A);
                    return;
                case SimEventType.Heal:
                    // Fountain ticks: only your own (sixteen healing allies would hiss).
                    if (ownA) Emit(sink, cue, w, ev.X, ev.Z, true, false, 1f, 1f, 0);
                    return;
                case SimEventType.HealLanded:
                    Emit(sink, cue, w, ev.X, ev.Z, ownA || ev.B == 0, false, ownA ? 1.1f : 0.8f, 1f, ev.A);
                    return;
                case SimEventType.Gather:
                case SimEventType.Respawn:
                case SimEventType.MineRemoved:
                case SimEventType.PiecePickedUp:
                    // Small personal sounds: yours, or quietly from the crowd.
                    Emit(sink, cue, w, ev.X, ev.Z, ownA, false, ownA ? 1f : 0.6f, 1f, ev.A);
                    return;
                default:
                    Emit(sink, cue, w, ev.X, ev.Z, ownA || ev.B == 0, false, 1f, 1f, ev.A);
                    return;
            }
        }

        private void Hit(SimEvent ev, World w, ISoundSink sink, int[] cues, bool death)
        {
            int t = ev.A;
            if (t < 0 || t >= w.ActorCount) return;
            bool own = t == 0 || ev.B == 0;
            bool threat = t == 0;
            byte tier = CombatFeed.TierOf(ev.Value, death);
            if (death)
            {
                // cues: death, finishing hit, Kaiju blow.
                Emit(sink, cues[0], w, ev.X, ev.Z, own, threat, 1f, 1f, t);
                if (ev.Cause == DamageCause.KaijuMelee || ev.B == Damage.KaijuSource) Emit(sink, cues[2], w, ev.X, ev.Z, own, threat, 0.9f, 1f, t);
                else if (ev.Cause == DamageCause.Melee) Emit(sink, cues[1], w, ev.X, ev.Z, own, threat, 0.85f, 1f, t);
                return;
            }
            // cues: flesh light, heavy, crit, armour, shield block, heavy block, Kaiju blow, knockback grunt.
            // Blasts, slams and falls already have their own big sound: their hits only grunt.
            bool melee = ev.Cause == DamageCause.Melee || ev.Cause == DamageCause.KaijuMelee || ev.Cause == DamageCause.None;
            Source(w, ev.B, ev.X, ev.Z, out double sx, out double sz);
            bool blocked = w.Actors[t].Cls == ClassId.Warrior && w.Actors[t].AbilityActive > 0 && Damage.IsFrontal(w, t, sx, sz);
            if (blocked)
            {
                Emit(sink, cues[ev.Value >= HeavyBlock ? 5 : 4], w, ev.X, ev.Z, own, false, 1f, 1f, t);
                return;
            }
            if (melee)
            {
                int cue;
                if (ev.B == Damage.KaijuSource || ev.Cause == DamageCause.KaijuMelee) cue = cues[6];
                else if (w.Actors[t].Cls == ClassId.Warrior) cue = tier >= 2 ? cues[2] : cues[3];
                else cue = cues[tier >= 2 ? 2 : tier == 1 ? 1 : 0];
                Emit(sink, cue, w, ev.X, ev.Z, own, threat, 1f, 1f, t);
            }
            if (tier >= 1) Emit(sink, cues[7], w, ev.X, ev.Z, t == 0, false, 0.85f, SoundFeed.Weight(w.Actors[t].Cls), t);
        }

        private void Hill(SimEvent ev, World w, ISoundSink sink, int[] cues)
        {
            // A = state code: 0 neutral, 1 azure, 2 crimson, 3 contested. Cues: captured, lost, contested, neutral.
            int next = ev.A, prev = _hill;
            _hill = next;
            if (w.ActorCount == 0) return;
            int mine = w.Actors[0].Team == TeamId.Azure ? 1 : 2;
            int cue;
            if (next == mine) cue = cues[0];
            else if (next == 3) cue = cues[2];
            else if (next != 0) cue = cues[1];
            else cue = prev == mine ? cues[1] : cues[3];
            Emit(sink, cue, w, ev.X, ev.Z, true, false, 1f, 1f, -1);
        }

        private void Emit(ISoundSink sink, int cue, World w, double x, double z, bool own, bool threat, float gain, float rate, int actor)
        {
            if (cue < 0) { Silenced++; return; }
            var r = new SoundRequest { Cue = cue, X = x, Z = z, Own = own, Threat = threat, Gain = gain, Rate = rate, Tick = w.Tick, Actor = actor };
            Requested++;
            sink.Play(in r);
        }

        private static bool NearPlayer(World w, double x, double z) => Within(w, x, z, ThreatRadius);

        private static bool Within(World w, double x, double z, double r)
        {
            if (w.ActorCount == 0 || !w.Actors[0].Alive) return false;
            double dx = w.X[0] - x, dz = w.Z[0] - z;
            return dx * dx + dz * dz <= r * r;
        }

        private static bool SameTeam(World w, int a) => w.ActorCount > 0 && a >= 0 && a < w.ActorCount && w.Actors[a].Team == w.Actors[0].Team;

        private static int PieceKind(World w, int slot)
        {
            BuiltPiece[] ps = w.Builder != null ? w.Builder.Pieces : null;
            return ps != null && slot >= 0 && slot < ps.Length && ps[slot] != null ? ps[slot].Kind : BuiltPieces.Ladder;
        }

        /// <summary>Where a hit came from (the attacker, the Kaiju, else the slam centre): CombatFeed's rule.</summary>
        private static void Source(World w, int b, double tx, double tz, out double sx, out double sz)
        {
            if (b >= 0 && b < w.ActorCount) { sx = w.X[b]; sz = w.Z[b]; return; }
            if (b == Damage.KaijuSource) { sx = w.Kaiju.X; sz = w.Kaiju.Z; return; }
            if (w.Kaiju.State != KaijuState.Egg) { sx = w.Kaiju.TelegraphX; sz = w.Kaiju.TelegraphZ; return; }
            sx = tx;
            sz = tz;
        }
    }
}
