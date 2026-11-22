// Player-experience wave (2026-11-22), lane S2: "every press either does what the player expects or raises a reason".
// Numbers: Sim/Config/PlayerSeatConfig.cs. Codes: Sim/Core/PlayerReasons.cs. Events: SimEnums ActionRefused / HitBlocked /
// StructureHit / WorldAction / AutopilotChanged. Full rules only (On), so the Classic and PhysicsOnly checksums never move.
using System;
using CriticalMass.Sim.Config;

namespace CriticalMass.Sim.Core
{
    /// <summary>
    /// Lane S2 per-actor state (hung on MechState.Seat, sized once, reset in place). Integers and doubles only; every field
    /// only decides whether a REPORTING event is pushed (and the idle anchor), never a rule outcome.
    /// </summary>
    public sealed class PlayerSeatState
    {
        public const int Buttons = 9; // index = PlayerReasons.Button* (1..8); 0 unused
        /// <summary>Per actor: the buttons held on the previous tick (bit = PlayerReasons.Button*).</summary>
        public int[] PrevMask;
        /// <summary>Per actor x Buttons: the last tick an ActionRefused was raised for that button (rate limit).</summary>
        public int[] RefusedTick;
        /// <summary>Per actor: the tick of a Cancel press waiting for its verdict (NoTick none), and the feint + hit-cancel count then.</summary>
        public int[] CancelTick, CancelMark;
        /// <summary>Per actor: the aim relative to the body (aim minus position) and its direction (radians) when input was last counted (idle rule).</summary>
        public double[] AimX, AimZ, AimDir;
        /// <summary>Per actor: 1 once the idle anchor holds a real aim.</summary>
        public byte[] AimSet;

        public const int NoTick = int.MinValue / 4;

        public void Init(World w)
        {
            int cap = World.ActorCapacity(w.Config);
            PrevMask = new int[cap];
            RefusedTick = new int[cap * Buttons];
            CancelTick = new int[cap];
            CancelMark = new int[cap];
            AimX = new double[cap];
            AimZ = new double[cap];
            AimDir = new double[cap];
            AimSet = new byte[cap];
            Clear();
        }

        public void Clear()
        {
            if (PrevMask == null) return;
            Array.Clear(PrevMask, 0, PrevMask.Length);
            for (int i = 0; i < RefusedTick.Length; i++) RefusedTick[i] = NoTick;
            for (int i = 0; i < CancelTick.Length; i++) CancelTick[i] = NoTick;
            Array.Clear(CancelMark, 0, CancelMark.Length);
            Array.Clear(AimX, 0, AimX.Length);
            Array.Clear(AimZ, 0, AimZ.Length);
            Array.Clear(AimDir, 0, AimDir.Length);
            Array.Clear(AimSet, 0, AimSet.Length);
        }
    }

    /// <summary>
    /// Lane S2: the human seat's never-silent presses (ActionRefused) and the shared helpers of the class repairs.
    /// Deterministic (actor-id order, integer ticks, no RNG), allocation-free in a step.
    /// </summary>
    public static class PlayerSeat
    {
        public static PlayerSeatConfig Cfg(World w) => w.Config.Depth.Seat;

        /// <summary>The lane runs: Full rules (combat feel on, not Depth.Classic) and PlayerSeatConfig.On.</summary>
        public static bool On(World w) =>
            w.Config.Depth.Seat != null && DepthRules.On(w, w.Config.Depth.Seat.On) && CombatFeel.On(w);

        /// <summary>Actor id is a person at the controls right now: a human seat that the idle autopilot is not playing.</summary>
        public static bool Human(World w, int id) =>
            On(w) && id >= 0 && id < w.ActorCount && w.Actors[id].IsHuman && !MechRules.Autopiloted(w, id);

        static PlayerSeatState S(World w) => w.Mech != null ? w.Mech.Seat : null;

        static int Ticks(World w, double seconds) => (int)Math.Floor(seconds * w.Config.SimHz + 0.5);

        /// <summary>
        /// Push ActionRefused (A = actor, B = button, (X, Z) = the actor, Value = why) for a human seat, at most once per
        /// RefuseRepeatSeconds per actor and button. Returns true when pushed.
        /// </summary>
        public static bool Refuse(World w, int id, int button, int why)
        {
            if (!Human(w, id)) return false;
            PlayerSeatState s = S(w);
            if (s == null || s.RefusedTick == null || button <= 0 || button >= PlayerSeatState.Buttons) return false;
            int k = id * PlayerSeatState.Buttons + button;
            if (k >= s.RefusedTick.Length) return false;
            if (s.RefusedTick[k] != PlayerSeatState.NoTick && w.Tick - s.RefusedTick[k] < Math.Max(1, Ticks(w, Cfg(w).RefuseRepeatSeconds))) return false;
            s.RefusedTick[k] = w.Tick;
            World.PushEvent(w, SimEventType.ActionRefused, id, button, w.X[id], w.Z[id], why);
            return true;
        }

        static int Mask(InputFrame f)
        {
            int m = 0;
            if (f.Attack) m |= 1 << PlayerReasons.ButtonAttack;
            if (f.Ability) m |= 1 << PlayerReasons.ButtonAbility;
            if (f.Terraform) m |= 1 << PlayerReasons.ButtonDigPlace;
            if (f.Interact) m |= 1 << PlayerReasons.ButtonInteract;
            if (f.Dodge) m |= 1 << PlayerReasons.ButtonDodge;
            if (f.Cancel) m |= 1 << PlayerReasons.ButtonCancel;
            if (f.Jump) m |= 1 << PlayerReasons.ButtonJump;
            return m;
        }

        /// <summary>
        /// The first thing Abilities.AbilitiesSystem does each tick (inputs applied, nothing consumed yet): for every human seat,
        /// each button's RISING edge that no system will act on and no other event will explain raises ActionRefused. The
        /// predicates are the systems' own refusals, read before they run, and skip every path that already reports (OreMined,
        /// Terraform, JumpRefused, DeliverRefused, Ability / Upgrade refusal codes, PingRefused, BridgeRepaired -2).
        /// </summary>
        public static void PressSystem(World w)
        {
            if (!On(w)) return;
            PlayerSeatState s = S(w);
            if (s == null || s.PrevMask == null) return;
            int n = w.ActorCount;
            for (int i = 0; i < n && i < s.PrevMask.Length; i++)
            {
                Actor a = w.Actors[i];
                InputFrame inp = w.Inputs[i];
                int mask = Mask(inp);
                int rise = mask & ~s.PrevMask[i];
                s.PrevMask[i] = mask;
                // a Cancel press from the last tick: nothing was cancelled (no feint, no hit-cancel since)
                if (s.CancelTick[i] != PlayerSeatState.NoTick && w.Tick > s.CancelTick[i])
                {
                    s.CancelTick[i] = PlayerSeatState.NoTick;
                    if (FeelCount(w) == s.CancelMark[i] && a.Alive) Refuse(w, i, PlayerReasons.ButtonCancel, a.SwingPending ? PlayerReasons.RefusedCooldown : PlayerReasons.RefusedNothingToCancel);
                }
                if (rise == 0 || !a.Alive || !Human(w, i)) continue;
                if ((rise & (1 << PlayerReasons.ButtonAttack)) != 0) PressAttack(w, i);
                if ((rise & (1 << PlayerReasons.ButtonAbility)) != 0) PressAbility(w, i);
                if ((rise & (1 << PlayerReasons.ButtonDigPlace)) != 0) PressDigPlace(w, i);
                if ((rise & (1 << PlayerReasons.ButtonInteract)) != 0) PressInteract(w, i);
                if ((rise & (1 << PlayerReasons.ButtonDodge)) != 0) PressDodge(w, i);
                if ((rise & (1 << PlayerReasons.ButtonJump)) != 0) PressJump(w, i);
                if ((rise & (1 << PlayerReasons.ButtonCancel)) != 0 && CombatFeel.Cfg(w).CancelEnabled)
                {
                    s.CancelTick[i] = w.Tick;
                    s.CancelMark[i] = FeelCount(w);
                }
            }
        }

        /// <summary>Lane S2: actor id held Cancel this tick (read from the press mask, since Combat clears the input).</summary>
        public static bool PressedCancel(World w, int id)
        {
            PlayerSeatState s = S(w);
            return s != null && s.PrevMask != null && id >= 0 && id < s.PrevMask.Length && (s.PrevMask[id] & (1 << PlayerReasons.ButtonCancel)) != 0;
        }

        /// <summary>Lane S3: actor id held button (a PlayerReasons.Button* code) this tick (the press mask; Combat clears the input).</summary>
        public static bool Pressed(World w, int id, int button)
        {
            PlayerSeatState s = S(w);
            return s != null && s.PrevMask != null && id >= 0 && id < s.PrevMask.Length && (s.PrevMask[id] & (1 << button)) != 0;
        }

        static int FeelCount(World w) => w.Depth.Feel.Feints + w.Depth.Feel.HitCancels;

        static bool Staggered(World w, Actor a) => a.StandUp > 0 || (a.HitStunTicks > 0 && Phys.On(w, true));

        /// <summary>Attack: cooldown is never reported (the buffer keeps the press). Reported: a stagger, get-up or dodge that
        /// outlasts the input buffer (the press will be dropped).</summary>
        static void PressAttack(World w, int i)
        {
            Actor a = w.Actors[i];
            int buffer = CombatFeel.Ticks(w, CombatFeel.Cfg(w).BufferSeconds);
            int busy = Math.Max(a.StandUp > 0 ? (int)Math.Ceiling(a.StandUp * w.Config.SimHz) : 0, Phys.On(w, true) ? a.HitStunTicks : 0);
            busy = Math.Max(busy, Dodge.TicksLeft(w, i, false));
            if (busy > buffer) Refuse(w, i, PlayerReasons.ButtonAttack, PlayerReasons.RefusedStaggered);
        }

        /// <summary>Ability: Abilities.AbilitiesSystem drops a press while staggered, mid-dodge or cooling down, silently.</summary>
        static void PressAbility(World w, int i)
        {
            Actor a = w.Actors[i];
            if (Staggered(w, a) || Dodge.Active(w, i)) Refuse(w, i, PlayerReasons.ButtonAbility, PlayerReasons.RefusedStaggered);
            else if (a.AbilityCd > 0) Refuse(w, i, PlayerReasons.ButtonAbility, PlayerReasons.RefusedCooldown);
        }

        /// <summary>DigPlace (F): per class. Miner (OreMined / Terraform) and Builder (Terraform / WallBuilt refusals) report
        /// themselves; the rest is here.</summary>
        static void PressDigPlace(World w, int i)
        {
            Actor a = w.Actors[i];
            switch (a.Cls)
            {
                case ClassId.Warrior:
                case ClassId.Farmer:
                    Refuse(w, i, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedNotYourClass);
                    break;
                case ClassId.Builder:
                    if (a.StandUp > 0) Refuse(w, i, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedStaggered);
                    break;
                case ClassId.Demolitions:
                    if (a.StandUp > 0) Refuse(w, i, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedStaggered);
                    else if (w.Tick < w.Demolitions.TntReadyTick[i]) Refuse(w, i, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedCooldown);
                    break;
                case ClassId.Healer:
                    // Purify keeps its own Ability refusal codes (-1 out of range, -2 closed); the lead asked for the uniform word
                    // too, so away from the shrine the press also raises RefusedOutOfRange, and on cooldown (silent today) RefusedCooldown.
                    if (a.StandUp > 0) Refuse(w, i, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedStaggered);
                    else if (w.Tick < w.Healer.PurifyReadyTick[i]) Refuse(w, i, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedCooldown);
                    else
                    {
                        int why = Healer.PurifyRefusal(w, i);
                        if (why == Healer.PurifyRefusedRange) Refuse(w, i, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedOutOfRange);
                        else if (why == Healer.PurifyRefusedClosed) Refuse(w, i, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedNoTarget);
                    }
                    break;
            }
        }

        /// <summary>Interact (E): refused only when NOTHING an interact system could take is in reach (with slack): a node,
        /// a grove / spring / vein, the shrine, the own station, a piece of the team or one carried, an ally to hand cargo to,
        /// a fallen bridge for a Builder. Getting up: staggered.</summary>
        static void PressInteract(World w, int i)
        {
            Actor a = w.Actors[i];
            if (a.StandUp > 0)
            {
                Refuse(w, i, PlayerReasons.ButtonInteract, PlayerReasons.RefusedStaggered);
                return;
            }
            double slack = Cfg(w).InteractSlack;
            double x = w.X[i], z = w.Z[i];
            // a gather the next systems will start (SimResources: a plant node; Sources: a grove or spring) is answered NOW
            // with a channel-start WorldAction (Value 0 = started, Radius = seconds to the first unit), unless the shrine takes
            // the load first (delivery comes before gathering)
            bool atShrine = Near(x, z, w.Map.Shrine.X, w.Map.Shrine.Z, w.Config.KaijuFeed.ShrineInteractRange + slack);
            if (!(atShrine && (a.CarryAmount > 0 || a.CarryMeat > 0)))
            {
                int gn = SimResources.GatherNode(w, i);
                if (gn >= 0)
                {
                    World.PushEvent(w, SimEventType.WorldAction, i, PlayerReasons.WorldGather, w.Nodes[gn].X, w.Nodes[gn].Z, 0, null, DamageCause.None, w.Config.Class(a.Cls).GatherSeconds);
                    return;
                }
                int sn = Sources.On(w) ? Sources.SourceInReach(w, i) : -1;
                if (sn >= 0)
                {
                    World.PushEvent(w, SimEventType.WorldAction, i, PlayerReasons.WorldGather, w.Depth.Sources[sn].X, w.Depth.Sources[sn].Z, 0, null, DamageCause.None, 0);
                    return;
                }
            }
            double nodeReach = w.Config.Resources.NodeInteractRange + Collide.ActorRadius(w, i) + slack;
            bool nodeNear = false;
            ResourceNode[] nodes = w.Nodes;
            if (nodes != null)
                for (int k = 0; k < nodes.Length && !nodeNear; k++)
                    nodeNear = Near(x, z, nodes[k].X, nodes[k].Z, nodeReach);
            SourceNode[] src = w.Depth.Sources;
            if (src != null)
                for (int k = 0; k < src.Length && !nodeNear; k++)
                    nodeNear = Near(x, z, src[k].X, src[k].Z, nodeReach);
            bool other = atShrine
                || Stations.InStation(w, i)
                || BuiltPieces.PieceInReach(w, i) >= 0
                || (w.Builder != null && w.Builder.CarryPiece != null && i < w.Builder.CarryPiece.Length && w.Builder.CarryPiece[i] >= 0)
                || (a.Cls == ClassId.Builder && w.Depth.BridgeState == MapEvents.BridgeBroken);
            if (!other && (a.CarryAmount > 0 || a.CarryMeat > 0))
            {
                for (int j = 0; j < w.ActorCount && !other; j++)
                    other = j != i && w.Actors[j].Alive && w.Actors[j].Team == a.Team && Near(x, z, w.X[j], w.Z[j], 2.5);
            }
            if (other) return;
            // a node in reach that takes nothing from you: your hands are full, or it is empty / not yours to gather
            if (nodeNear && a.CarryAmount >= w.Config.Class(a.Cls).CarryCapacity) Refuse(w, i, PlayerReasons.ButtonInteract, PlayerReasons.RefusedCarryFull);
            else Refuse(w, i, PlayerReasons.ButtonInteract, PlayerReasons.RefusedNoTarget);
        }

        static bool Near(double x, double z, double px, double pz, double r)
        {
            double dx = px - x, dz = pz - z;
            return dx * dx + dz * dz <= r * r;
        }

        /// <summary>Dodge: Dodge.CanStart's refusals, in its order (the path refusal is raised by Dodge.Step itself).</summary>
        static void PressDodge(World w, int i)
        {
            if (!Dodge.On(w)) return;
            Actor a = w.Actors[i];
            if (a.AirLen > 0 || a.StandUp > 0 || a.HitStunTicks > 0 || a.KVx != 0 || a.KVz != 0 || a.SwingPending || (CombatFeel.FeintRecovering(w, i)))
                Refuse(w, i, PlayerReasons.ButtonDodge, PlayerReasons.RefusedStaggered);
            else if (a.Carry == CarryKind.Corruption) Refuse(w, i, PlayerReasons.ButtonDodge, PlayerReasons.RefusedCarrying);
            else if (w.Tick < w.Dodges.Ready[i]) Refuse(w, i, PlayerReasons.ButtonDodge, PlayerReasons.RefusedCooldown);
        }

        /// <summary>Jump: Movement drops a press silently while staggered, getting up or mid-dodge, and lets the buffer run out
        /// mid-air. JumpRefused (no landing, coyote) stays Movement's.</summary>
        static void PressJump(World w, int i)
        {
            Actor a = w.Actors[i];
            int buffer = Jump.BufferTicks(w.Config) + 1;
            if (a.StandUp > 0 || (a.HitStunTicks > buffer && Phys.On(w, true)) || Dodge.TicksLeft(w, i, true) > buffer)
                Refuse(w, i, PlayerReasons.ButtonJump, PlayerReasons.RefusedStaggered);
            else if (a.AirLen > 0 && a.AirLen - a.AirTick > buffer) Refuse(w, i, PlayerReasons.ButtonJump, PlayerReasons.RefusedInAir);
        }

        // ------------------------------------------------------------------ helpers for the class repairs

        /// <summary>The direction actor id aims along (to its aim point when it has one more than 1e-6 away, else its facing).</summary>
        public static void AimDir(World w, int id, out double fx, out double fz)
        {
            if (w.HasAim[id] == 1)
            {
                double dx = w.Inputs[id].AimX - w.X[id], dz = w.Inputs[id].AimZ - w.Z[id];
                double l = Math.Sqrt(dx * dx + dz * dz);
                if (l > 1e-6)
                {
                    fx = dx / l;
                    fz = dz / l;
                    return;
                }
            }
            fx = JsMath.Cos(w.Actors[id].Facing);
            fz = JsMath.Sin(w.Actors[id].Facing);
        }

        /// <summary>The lowest-id living enemy of id within maxDist (centre to centre) inside its class arc along (fx, fz), the
        /// arc widened by the target's body as Combat does; -1 none.</summary>
        public static int EnemyInArc(World w, int id, double maxDist, double fx, double fz)
        {
            Actor a = w.Actors[id];
            double halfRad = w.Config.Class(a.Cls).AttackArcDeg / 2 * Math.PI / 180;
            for (int j = 0; j < w.ActorCount; j++)
            {
                Actor t = w.Actors[j];
                if (j == id || !t.Alive || t.Team == a.Team) continue;
                double dx = w.X[j] - w.X[id], dz = w.Z[j] - w.Z[id], d2 = dx * dx + dz * dz;
                if (d2 > maxDist * maxDist) continue;
                if (d2 <= 1e-12) return j;
                double d = Math.Sqrt(d2);
                if ((dx * fx + dz * fz) / d >= Combat.WidenedCos(halfRad, w.Config.Class(t.Cls).Radius, d) - 1e-9) return j;
            }
            return -1;
        }

        // ------------------------------------------------------------------ the idle rule sees aim (MechRules)

        /// <summary>
        /// MechRules.HasInput's lane S2 half. The aim is read RELATIVE to the body (aim minus position: with the camera on the
        /// fighter a still mouse or a still camera keeps it, whatever the fighter does): it counts as input when it moved
        /// IdleAimUnits from the anchor or turned IdleAimDegrees. The anchor moves to the aim whenever input counts. While the
        /// autopilot plays (auto), the aim alone never counts (it would follow the bot's own walking); a move or a button hands
        /// the fighter back on that tick.
        /// </summary>
        public static bool AimMoved(World w, int id, InputFrame f, bool otherInput, bool auto)
        {
            PlayerSeatState s = S(w);
            if (!On(w) || s == null || s.AimSet == null || id < 0 || id >= s.AimSet.Length) return false;
            if (!double.IsFinite(f.AimX) || !double.IsFinite(f.AimZ)) return false;
            double rx = f.AimX - w.X[id], rz = f.AimZ - w.Z[id];
            double dir = rx * rx + rz * rz > 1e-6 ? JsMath.Atan2(rz, rx) : s.AimDir[id];
            bool moved = false;
            if (s.AimSet[id] != 0 && !auto)
            {
                double mx = rx - s.AimX[id], mz = rz - s.AimZ[id];
                double u = Cfg(w).IdleAimUnits;
                double turn = Math.Abs(dir - s.AimDir[id]);
                if (turn > Math.PI) turn = 2 * Math.PI - turn;
                moved = mx * mx + mz * mz > u * u || turn > Cfg(w).IdleAimDegrees * Math.PI / 180;
            }
            if (moved || otherInput || auto || s.AimSet[id] == 0)
            {
                s.AimX[id] = rx;
                s.AimZ[id] = rz;
                s.AimDir[id] = dir;
                s.AimSet[id] = 1;
            }
            return moved;
        }
    }
}
