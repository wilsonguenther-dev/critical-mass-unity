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
        /// <summary>px lane F: the player-feedback channel to report when this cue really starts (FeedbackChannel + 1; 0 = none),
        /// the actor the entry is about, and its reason code (PlayerReasons.*).</summary>
        public int Feedback, FeedbackActor, Reason;
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
        // px lane F: the tick each actor last landed a hit (for MY assists: index = victim, value = my last hit tick on them),
        // and the tick each attacker's swing was already answered (HitBlocked / StructureHit / a Kaiju hit): no whiff swish.
        private readonly int[] _myHitOn, _answered, _glancedAt;
        /// <summary>px lane F: one glancing-hit tap per target per this many seconds (it rides on every Farmer close hit).</summary>
        public const double GlanceSeconds = 2.0;
        /// <summary>px lane F: an assist is a kill by someone else on an enemy I hit within this many seconds.</summary>
        public const double AssistSeconds = 5.0;
        /// <summary>px lane F: the low-health heartbeat state of the local player (updated every tick).</summary>
        public readonly Heartbeat Heart = new Heartbeat();
        /// <summary>px lane F: the last tick a StructureHit event was decoded (SoundDirector.PlayStructureHit skips its polled knock then).</summary>
        public int LastStructureTick { get; private set; } = int.MinValue;
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
            _myHitOn = new int[_capacity];
            _answered = new int[_capacity];
            _glancedAt = new int[_capacity];
            ResetTicks();
        }

        private void ResetTicks()
        {
            for (int i = 0; i < _capacity; i++) { _myHitOn[i] = int.MinValue; _answered[i] = int.MinValue; _glancedAt[i] = int.MinValue; }
            LastStructureTick = int.MinValue;
            Heart.Reset();
        }

        public void Reset()
        {
            _head = 0;
            _meat = -1;
            _hill = 0;
            _berserk = false;
            Array.Clear(_airLen, 0, _airLen.Length);
            ResetTicks();
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
                // px lane F: the heartbeat under 30% health (hysteresis to 35%), off while dead or after the whistle.
                Actor me = w.Actors[LocalSeat.Actor];
                float share = me.MaxHp > 0 ? (float)(me.Hp / me.MaxHp) : 1f;
                Heart.Update(share, me.Alive && w.Match.Phase != MatchPhase.Ended);
                int meat = w.Actors[LocalSeat.Actor].Alive ? w.Actors[LocalSeat.Actor].CarryMeat : 0;
                if (_meat >= 0 && meat > _meat) Emit(sink, SoundTable.EdgeCue(SoundEdge.MeatPickup), w, w.X[LocalSeat.Actor], w.Z[LocalSeat.Actor], true, false, 1f, 1f, 0);
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
            bool ownA = ev.A == LocalSeat.Actor;
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
                    // feat/hit-feel-1: an effort voice on every other swing of an actor (deterministic, no RNG).
                    if (_effortCue == -2) _effortCue = SoundTable.IndexOf("effort");
                    if (_effortCue >= 0 && EffortOn(a, w.Tick)) Emit(sink, _effortCue, w, w.X[a], w.Z[a], ownA, false, 0.9f, Weight(c), a);
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
                // ---- b15: the Skybreak cues for the b13 events
                case Pick.Hut:
                {
                    // A = the team, B = the Miner who paid: your team's hut is loud and wins a voice; theirs is quieter
                    if (w.ActorCount == 0) return;
                    bool mine = ev.A == (int)w.Actors[LocalSeat.Actor].Team;
                    Emit(sink, cues[0], w, ev.X, ev.Z, mine, false, mine ? 1f : 0.5f, 1f, ev.B);
                    return;
                }
                case Pick.Ore:
                    if (ev.B < 0) { if (ownA && cues.Length > 1) Emit(sink, cues[1], w, ev.X, ev.Z, true, false, 1f, 1f, 0); return; }
                    Emit(sink, cues[0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    return;
                case Pick.BuildWall:
                    if (ev.B < 0) { if (ownA && cues.Length > 2) Emit(sink, cues[2], w, ev.X, ev.Z, true, false, 1f, 1f, 0); return; }
                    Emit(sink, cues[ev.Value <= 1.0 && cues.Length > 1 ? 1 : 0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    return;
                case Pick.Bank:
                    if (ev.Bio == BiomassType.Ore) Emit(sink, cues[0], w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    else Silenced++;
                    return;
                // ---- px lane F
                case Pick.Respawn:
                    if (ownA && cues.Length > 1) Emit(sink, cues[1], w, ev.X, ev.Z, true, false, 1f, 1f, ev.A);
                    else Emit(sink, cues[0], w, ev.X, ev.Z, ownA, false, ownA ? 1f : 0.6f, 1f, ev.A);
                    return;
                case Pick.Contact:
                    Contact(ev, w, sink, cues[0]);
                    return;
                case Pick.Feint:
                    // Value 1 = a feint (the wind-up dropped), 2 = a hit-cancel (lower, softer)
                    Emit(sink, cues[0], w, ev.X, ev.Z, ownA, false, (ownA ? 1f : 0.6f) * (ev.Value >= 2 ? 0.8f : 1f), ev.Value >= 2 ? 0.85f : 1f, ev.A);
                    return;
                case Pick.Blocked:
                    Blocked(ev, w, sink, cues);
                    return;
                case Pick.Structure:
                    Structure(ev, w, sink, cues);
                    return;
                case Pick.Refused:
                    if (ownA) Emit(sink, cues[0], w, ev.X, ev.Z, true, false, 1f, 1f, ev.A, 0, ev.A, (int)ev.Value);
                    else Silenced++;   // other people's dead presses are their own business
                    return;
                case Pick.WorldAct:
                    WorldAct(ev, w, sink, cues);
                    return;
                case Pick.Autopilot:
                    // the local player's fighter was taken over (Value 1) or handed back (0); nobody else's is worth a sound
                    if (ownA) Emit(sink, cues[ev.Value > 0 ? 0 : 1], w, ev.X, ev.Z, true, false, 1f, 1f, ev.A);
                    else Silenced++;
                    return;
                case Pick.Critical:
                {
                    int me = LocalSeat.Actor;
                    bool mineCrit = ev.A == me || ev.B == me;
                    double cx = ev.X, cz = ev.Z;
                    if (ev.A >= 0 && ev.A < w.ActorCount) { cx = w.X[ev.A]; cz = w.Z[ev.A]; }
                    Emit(sink, cues[0], w, cx, cz, mineCrit, ev.A == me, mineCrit ? 1f : 0.6f, ev.Value >= 3 ? 1.08f : 1f, ev.A);
                    return;
                }
                default:
                    Silenced++;
                    return;
            }
        }

        private void Fixed(SimEvent ev, World w, ISoundSink sink, int cue)
        {
            bool ownA = ev.A == LocalSeat.Actor;
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
                    if (ev.A >= 0 && ev.A < _capacity) _answered[ev.A] = w.Tick;   // px lane F: a Kaiju hit is not a whiff
                    Emit(sink, cue, w, ev.X, ev.Z, ownA, false, 1f, (float)(1.05 - Math.Min(0.15, ev.Value / 300.0)), ev.A);
                    return;
                case SimEventType.MineArmed:
                    // Owner A: an enemy mine beside you is a threat; your own is a confirmation.
                    Emit(sink, cue, w, ev.X, ev.Z, ownA, !SameTeam(w, ev.A) && NearPlayer(w, ev.X, ev.Z), 1f, 1f, ev.A);
                    return;
                case SimEventType.MineTriggered:
                    Emit(sink, cue, w, ev.X, ev.Z, ownA || ev.B == LocalSeat.Actor, ownA, 1f, 1f, ev.A);
                    return;
                case SimEventType.Heal:
                    // Fountain ticks: only your own (sixteen healing allies would hiss).
                    if (ownA) Emit(sink, cue, w, ev.X, ev.Z, true, false, 1f, 1f, 0);
                    return;
                case SimEventType.HealLanded:
                    Emit(sink, cue, w, ev.X, ev.Z, ownA || ev.B == LocalSeat.Actor, false, ownA ? 1.1f : 0.8f, 1f, ev.A);
                    return;
                case SimEventType.JumpRefused:
                    // b16 [11]: a refused jump at the edge: your own only, soft (sixteen bots pressing jump at the edge would buzz).
                    if (ownA) Emit(sink, cue, w, ev.X, ev.Z, true, false, 0.6f, 1f, 0);
                    else Silenced++;
                    return;
                case SimEventType.Dodge:
                case SimEventType.WallBroken:
                    // b15: B is not an actor here (0 / a cell index): own = the dodger / the wall's owner only
                    Emit(sink, cue, w, ev.X, ev.Z, ownA, false, 1f, 1f, ev.A);
                    return;
                case SimEventType.Gather:
                case SimEventType.MineRemoved:
                case SimEventType.PiecePickedUp:
                    // Small personal sounds: yours, or quietly from the crowd.
                    Emit(sink, cue, w, ev.X, ev.Z, ownA, false, ownA ? 1f : 0.6f, 1f, ev.A);
                    return;
                default:
                    Emit(sink, cue, w, ev.X, ev.Z, ownA || ev.B == LocalSeat.Actor, false, 1f, 1f, ev.A);
                    return;
            }
        }

        int _effortCue = -2;

        /// <summary>feat/hit-feel-1: does this swing (actor, press tick) voice an effort? About every other swing, spread by actor.</summary>
        public static bool EffortOn(int actor, int tick) => ((uint)(actor * 7 + tick) & 1u) == 0;

        private void Hit(SimEvent ev, World w, ISoundSink sink, int[] cues, bool death)
        {
            int t = ev.A;
            if (t < 0 || t >= w.ActorCount) return;
            int me = LocalSeat.Actor;
            bool own = t == me || ev.B == me;
            bool threat = t == me;
            // px lane F: MY hit on someone else, or a hit ON me (the local player's own tier; the crowd keeps its old cues).
            bool mine = ev.B == me && t != me;
            bool onMe = t == me && ev.B != me;
            byte tier = CombatFeed.TierOf(ev.Value, death);
            if (death)
            {
                // cues: death, finishing hit, Kaiju blow; px: my death, kill sting, kill confirm, assist sting.
                Emit(sink, cues[0], w, ev.X, ev.Z, own, threat, 1f, 1f, t);
                if (ev.Cause == DamageCause.KaijuMelee || ev.B == Damage.KaijuSource) Emit(sink, cues[2], w, ev.X, ev.Z, own, threat, 0.9f, 1f, t);
                else if (ev.Cause == DamageCause.Melee) Emit(sink, cues[1], w, ev.X, ev.Z, own, threat, 0.85f, 1f, t);
                if (cues.Length > 6)
                {
                    if (t == me) Emit(sink, cues[3], w, ev.X, ev.Z, true, true, 1f, 1f, t);
                    else if (ev.B == me)
                    {
                        // every kill credited to me: the sting (Stinger bus: never queued behind the announcer) + the two-note confirm
                        Emit(sink, cues[4], w, ev.X, ev.Z, true, false, 1f, 1f, t, Fb(FeedbackChannelKill), t);
                        Emit(sink, cues[5], w, ev.X, ev.Z, true, false, 1f, 1f, t);
                    }
                    else if (IsAssist(w, t)) Emit(sink, cues[6], w, ev.X, ev.Z, true, false, 1f, 1f, t);
                }
                if (t < _capacity) _myHitOn[t] = int.MinValue;
                return;
            }
            if (mine && t < _capacity) _myHitOn[t] = w.Tick;
            bool px = cues.Length > 15;
            // px lane F fix round: the pain voice of a hit I dealt or took is its own cue (own_pain, same voices), so the
            // planner's 30 ms same-cue repeat guard never lets a crowd pain swallow mine.
            int painCue = cues.Length > 16 && (mine || onMe) ? cues[16] : cues.Length > 8 ? cues[8] : -1;
            // cues: flesh light, heavy, crit, armour, shield block, heavy block, Kaiju blow, knockback grunt, pain.
            // Blasts, slams and falls already have their own big sound: their hits only grunt.
            bool melee = ev.Cause == DamageCause.Melee || ev.Cause == DamageCause.KaijuMelee || ev.Cause == DamageCause.None;
            Source(w, ev.B, ev.X, ev.Z, out double sx, out double sz);
            bool blocked = w.Actors[t].Cls == ClassId.Warrior && w.Actors[t].AbilityActive > 0 && Damage.IsFrontal(w, t, sx, sz);
            if (blocked)
            {
                if (onMe && px) Emit(sink, cues[15], w, ev.X, ev.Z, true, true, 1f, 1f, t, Fb(FeedbackChannelHurt), t);
                else Emit(sink, cues[ev.Value >= HeavyBlock ? 5 : 4], w, ev.X, ev.Z, own, false, 1f, 1f, t);
                if (mine && px && ev.Value > 0) Emit(sink, cues[13], w, ev.X, ev.Z, true, false, 0.8f, 1f, t);
                return;
            }
            if (melee)
            {
                int cue;
                if (ev.B == Damage.KaijuSource || ev.Cause == DamageCause.KaijuMelee) cue = cues[6];
                else if (w.Actors[t].Cls == ClassId.Warrior) cue = tier >= 2 ? cues[2] : cues[3];
                else cue = cues[tier >= 2 ? 2 : tier == 1 ? 1 : 0];
                if (mine && px && ev.B >= 0 && ev.B < w.ActorCount)
                {
                    // MY landed hit: always the meaty weapon impact at own priority, plus the hit-confirm tick; what I hit
                    // (flesh or armour) stays under it at 0.6 so the victim's material still reads.
                    Emit(sink, cues[9 + WeaponImpact(w.Actors[ev.B].Cls)], w, ev.X, ev.Z, true, false, tier >= 2 ? 1.15f : 1f, 1f, t, Fb(FeedbackChannelImpact), t);
                    Emit(sink, cue, w, ev.X, ev.Z, true, false, 0.6f, 1f, t);
                    if (SoundPrefs.HitConfirm) Emit(sink, cues[13], w, ev.X, ev.Z, true, false, 1f, tier >= 2 ? 1.06f : 1f, t);
                }
                else Emit(sink, cue, w, ev.X, ev.Z, own, threat, 1f, 1f, t);
                // feat/hit-feel-1: the victim's pain voice, pitched by body (Weight) with the cue's own jitter.
                if (painCue >= 0) Emit(sink, painCue, w, ev.X, ev.Z, own, threat, 1f, SoundFeed.Weight(w.Actors[t].Cls), t);
            }
            else if (mine && px && ev.Value > 0 && SoundPrefs.HitConfirm) Emit(sink, cues[13], w, ev.X, ev.Z, true, false, 1f, 1f, t);   // my blast landed on them
            // px lane F: MY hurt thump, scaled by the share of my health the hit took (a scratch is quiet, a big blow is full).
            if (onMe && px)
            {
                Actor a = w.Actors[t];
                double share = a.MaxHp > 0 ? ev.Value / a.MaxHp : 0;
                Emit(sink, cues[14], w, ev.X, ev.Z, true, true, HurtGain(share), 1f, t, Fb(FeedbackChannelHurt), t);
            }
            if (tier >= 1) Emit(sink, cues[7], w, ev.X, ev.Z, t == me, false, 0.85f, SoundFeed.Weight(w.Actors[t].Cls), t);
        }

        // px lane F: CriticalMass.Runtime.Game.FeedbackChannel members this feed reports (+1 in SoundRequest.Feedback).
        private const int FeedbackChannelImpact = (int)CriticalMass.Runtime.Game.FeedbackChannel.ImpactSound;
        private const int FeedbackChannelWhiff = (int)CriticalMass.Runtime.Game.FeedbackChannel.WhiffCue;
        private const int FeedbackChannelHurt = (int)CriticalMass.Runtime.Game.FeedbackChannel.OwnHurtSound;
        private const int FeedbackChannelKill = (int)CriticalMass.Runtime.Game.FeedbackChannel.KillSound;
        private static int Fb(int channel) => channel + 1;

        /// <summary>px lane F (pure; tests): my hurt thump's gain for a hit that took share of my max health: 0.55 for a
        /// scratch, full at a quarter or more.</summary>
        public static float HurtGain(double share) => (float)Math.Max(0.55, Math.Min(1.0, 0.55 + 0.45 * share / 0.25));

        /// <summary>px lane F (pure; tests): my weapon's impact (0 thud: Warrior cleave, Builder hammer; 1 crack: Farmer staff,
        /// Miner pick; 2 clank: Demolitions pry bar; 3 dull bell: Healer lantern).</summary>
        public static int WeaponImpact(ClassId attacker)
        {
            switch (attacker)
            {
                case ClassId.Farmer:
                case ClassId.Miner: return 1;
                case ClassId.Demolitions: return 2;
                case ClassId.Healer: return 3;
                default: return 0;
            }
        }

        /// <summary>px lane F: a kill by someone else on an enemy I hit within AssistSeconds.</summary>
        private bool IsAssist(World w, int victim)
        {
            if (victim < 0 || victim >= _capacity || _myHitOn[victim] == int.MinValue || w.ActorCount == 0) return false;
            if (w.Actors[victim].Team == w.Actors[LocalSeat.Actor].Team) return false;
            return w.Tick - _myHitOn[victim] <= AssistSeconds * w.Config.SimHz;
        }

        /// <summary>px lane F: the contact tick. A contact with nobody in it is a light air swish only; one with hits is the
        /// impact's (Hit), and one already answered this tick (HitBlocked, StructureHit, a Kaiju hit) is that answer's.</summary>
        private void Contact(SimEvent ev, World w, ISoundSink sink, int cue)
        {
            int a = ev.A;
            if (a < 0 || a >= w.ActorCount || ev.Value > 0 || (a < _capacity && _answered[a] == w.Tick)) { Silenced++; return; }
            bool ownA = a == LocalSeat.Actor;
            Emit(sink, cue, w, ev.X, ev.Z, ownA, false, ownA ? 1f : 0.6f, Weight(w.Actors[a].Cls), a, ownA ? Fb(FeedbackChannelWhiff) : 0, a);
        }

        /// <summary>px lane F: a swing or blast that reached an enemy and dealt nothing (A target, B attacker, Value Blocked*).
        /// cues: clang, phew, wall thud.</summary>
        private void Blocked(SimEvent ev, World w, ISoundSink sink, int[] cues)
        {
            int me = LocalSeat.Actor;
            if (ev.B >= 0 && ev.B < _capacity) _answered[ev.B] = w.Tick;
            int code = (int)ev.Value;
            int cue;
            float g = 1f;
            bool ext = cues.Length > 5;   // the fix-round cues: my dodge, my shield, the glancing tap
            switch (code)
            {
                case PlayerReasons.BlockedSpawnProtect:
                case PlayerReasons.BlockedImmune: cue = cues[0]; break;
                case PlayerReasons.BlockedPassive: cue = cues[0]; g = 0.6f; break;
                // MY successful dodge has its own rising cue at own priority; their dodge of my swing is the phew
                case PlayerReasons.BlockedDodge: cue = ext && ev.A == me ? cues[3] : cues[1]; break;
                case PlayerReasons.BlockedWall: cue = cues[2]; break;
                case PlayerReasons.BlockedShield:
                    // my raised shield took it (A = me) or my swing hit a raised shield (B = me); the crowd's: the Hit's block cue
                    if (!ext || (ev.A != me && ev.B != me)) { Silenced++; return; }
                    cue = cues[4];
                    if (ev.A != me) g = 0.75f;
                    break;
                case PlayerReasons.BlockedGlancing:
                {
                    // lane S2: the hit landed reduced (the Farmer's staff inside her close range); it rides on every such hit,
                    // so one soft tap per target per GlanceSeconds.
                    int t = ev.A;
                    if (!ext || t < 0 || t >= _capacity) { Silenced++; return; }
                    int gap = (int)Math.Ceiling(GlanceSeconds * Math.Max(1.0, w.Config.SimHz));
                    if (_glancedAt[t] != int.MinValue && w.Tick - _glancedAt[t] < gap) { Silenced++; return; }
                    _glancedAt[t] = w.Tick;
                    cue = cues[5];
                    g = ev.A == me || ev.B == me ? 1f : 0.6f;
                    Emit(sink, cue, w, ev.X, ev.Z, ev.A == me || ev.B == me, false, g, 1f, ev.A, 0, ev.A, code);
                    return;
                }
                default: Silenced++; return;   // Ward: WardAbsorb plays block_heavy; newer codes: their own event
            }
            bool own = ev.A == me || ev.B == me;
            if (!own) g *= 0.7f;
            Emit(sink, cue, w, ev.X, ev.Z, own, ev.A == me && code != PlayerReasons.BlockedDodge, g, 1f, ev.A, ev.B == me ? Fb(FeedbackChannelWhiff) : 0, ev.A, code);
        }

        /// <summary>px lane F: a structure took a hit (A attacker, B Structure* kind). cues: barricade, built wall, breakable,
        /// piece, solid.</summary>
        private void Structure(SimEvent ev, World w, ISoundSink sink, int[] cues)
        {
            if (ev.A >= 0 && ev.A < _capacity) _answered[ev.A] = w.Tick;
            LastStructureTick = w.Tick;
            int k = ev.B - 1;
            if (k < 0 || k >= cues.Length) { Silenced++; return; }
            bool ownA = ev.A == LocalSeat.Actor;
            if (ev.B == PlayerReasons.StructureSolid && !ownA) { Silenced++; return; }   // only ever raised for a human swing
            Emit(sink, cues[k], w, ev.X, ev.Z, ownA, false, ownA ? 1f : 0.7f, 1f, ev.A);
        }

        /// <summary>px lane F: a world action. Value 0 = a channel started (dig scrape, pick strike, gather rustle); else the
        /// verb's own cue. cues: dig start, ore hit, gather start, dig, place, wall, bank, feed, corrupt, purify, repair, gather.</summary>
        private void WorldAct(SimEvent ev, World w, ISoundSink sink, int[] cues)
        {
            bool ownA = ev.A == LocalSeat.Actor;
            int i = WorldActCue(ev.B, ev.Value <= 0);
            if (i < 0 || i >= cues.Length) { Silenced++; return; }
            Emit(sink, cues[i], w, ev.X, ev.Z, ownA, false, ownA ? 1f : 0.5f, 1f, ev.A);
        }

        /// <summary>px lane F (pure; tests): which WorldAction cue a kind plays (start = a channel started), -1 = silent.</summary>
        public static int WorldActCue(int kind, bool start)
        {
            const int Gather = 10;   // PlayerReasons.WorldGather (feat/px-s2-class; not on this branch's PlayerReasons yet)
            if (start) return kind == PlayerReasons.WorldMineOre ? 1 : kind == Gather ? 2 : 0;
            switch (kind)
            {
                case PlayerReasons.WorldDig: return 3;
                case PlayerReasons.WorldPlace: return 4;
                case PlayerReasons.WorldBuild: return 5;
                case PlayerReasons.WorldMineOre: return 1;
                case PlayerReasons.WorldBank: return 6;
                case PlayerReasons.WorldFeed: return 7;
                case PlayerReasons.WorldCorrupt: return 8;
                case PlayerReasons.WorldPurify: return 9;
                case PlayerReasons.WorldRepair: return 10;
                case Gather: return 11;
                default: return -1;
            }
        }

        private void Hill(SimEvent ev, World w, ISoundSink sink, int[] cues)
        {
            // A = state code: 0 neutral, 1 azure, 2 crimson, 3 contested. Cues: captured, lost, contested, neutral.
            int next = ev.A, prev = _hill;
            _hill = next;
            if (w.ActorCount == 0) return;
            int mine = w.Actors[LocalSeat.Actor].Team == TeamId.Azure ? 1 : 2;
            int cue;
            if (next == mine) cue = cues[0];
            else if (next == 3) cue = cues[2];
            else if (next != 0) cue = cues[1];
            else cue = prev == mine ? cues[1] : cues[3];
            Emit(sink, cue, w, ev.X, ev.Z, true, false, 1f, 1f, -1);
        }

        private void Emit(ISoundSink sink, int cue, World w, double x, double z, bool own, bool threat, float gain, float rate, int actor,
            int feedback = 0, int feedbackActor = -1, int reason = 0)
        {
            if (cue < 0) { Silenced++; return; }
            var r = new SoundRequest
            {
                Cue = cue, X = x, Z = z, Own = own, Threat = threat, Gain = gain, Rate = rate, Tick = w.Tick, Actor = actor,
                Feedback = feedback, FeedbackActor = feedbackActor, Reason = reason,
            };
            Requested++;
            sink.Play(in r);
        }

        private static bool NearPlayer(World w, double x, double z) => Within(w, x, z, ThreatRadius);

        private static bool Within(World w, double x, double z, double r)
        {
            if (w.ActorCount == 0 || !w.Actors[LocalSeat.Actor].Alive) return false;
            double dx = w.X[LocalSeat.Actor] - x, dz = w.Z[LocalSeat.Actor] - z;
            return dx * dx + dz * dz <= r * r;
        }

        private static bool SameTeam(World w, int a) => w.ActorCount > 0 && a >= 0 && a < w.ActorCount && w.Actors[a].Team == w.Actors[LocalSeat.Actor].Team;

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
