using System;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Interact;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Runtime.View.Mechanics
{
    /// <summary>
    /// Where the depth feed's side effects go: banners (the existing Banner queue, which waits for big moments), sounds
    /// through named hooks (ItemHooks.Sound: a safe no-op when no sound item is in the build), team feed lines and local
    /// prompts (the HUD subscribes). Tests subscribe to the events to count what a tick produced.
    /// </summary>
    public static class DepthOut
    {
        /// <summary>A team feed line (text, PickupKind or -1, call or -1) for the ping feed column.</summary>
        public static event Action<string, int, int> Line;
        /// <summary>A short local prompt (refusals): text, seconds to show.</summary>
        public static event Action<string, float> Prompt;
        /// <summary>A whole-match announcement (headline, reason); the banner is shown here, this is for tests and the HUD.</summary>
        public static event Action<string, string> Announced;
        /// <summary>Muted by tests that drive the feed without a scene.</summary>
        public static bool Quiet;

        public static void Say(string line, int pickup = -1, int call = -1) { if (!string.IsNullOrEmpty(line)) Line?.Invoke(line, pickup, call); }
        public static void Tell(string prompt, float seconds = 2.5f) { if (!string.IsNullOrEmpty(prompt)) Prompt?.Invoke(prompt, seconds); }

        /// <summary>A named sound cue ("depth-chop", "depth-coin", ...); nothing when no sound item is present.</summary>
        public static void Sound(string cue)
        {
            if (Quiet || string.IsNullOrEmpty(cue)) return;
            ItemHooks.Sound(cue);
        }

        /// <summary>A banner through the shared queue (it holds while a big moment plays). Screen-wide text, no shake.</summary>
        public static void Announce(string headline, string reason, BannerStyle style, int priority = 2, double hold = 4.0)
        {
            Announced?.Invoke(headline, reason);
            if (Quiet) return;
            Banner.Show(new BannerSpec { Headline = headline, Reason = reason, Style = style, Priority = priority, HoldSeconds = hold });
        }
    }

    /// <summary>
    /// Reads the sim's event ring once per tick (after MechFeed, from the same Ticked call) for the mechanics-depth events
    /// and keeps the short-lived facts the world view and HUD draw: hand-off arcs, bank and withdraw flights, gather pops,
    /// pickup fades, aura breaks, ward ripples, upgrade pulses, bot acknowledgements and the map-event clocks. Fixed rings,
    /// no allocation per tick (feed words are cached in DepthLines). Times are sim seconds. Never writes the sim.
    /// </summary>
    public sealed class DepthFeed
    {
        public const int Arcs = 24, Pops = 32, Acks = 12, Pulses = 8, FadeSlots = 128, CoinFlights = 8;

        public const int ArcHandoff = 0, ArcBank = 1, ArcWithdraw = 2;
        public const int PopChop = 0, PopSplash = 1, PopCoins = 2, PopTaken = 3, PopWard = 4, PopShatter = 5, PopSwitch = 6, PopCool = 7,
            PopDust = 8, PopClaim = 9, PopRepair = 10, PopPlanks = 11, PopAuraEnd = 12, PopPlus = 13, PopBlocked = 14;

        public struct Arc { public double FromX, FromZ, ToX, ToZ, T; public int From, To, Kind, Cargo; }
        public struct Pop { public double X, Z, R, T; public int Kind, Actor, Value; }
        public struct Ack { public int Bot, Call; public double X, Z, T; }
        public struct Pulse { public int Team, Cls; public double T; }
        public struct Fade { public double X, Z, T; public int Kind; }

        public readonly Arc[] ArcRing = new Arc[Arcs];
        public readonly Pop[] PopRing = new Pop[Pops];
        public readonly Ack[] AckRing = new Ack[Acks];
        public readonly Pulse[] PulseRing = new Pulse[Pulses];
        public readonly Fade[] FadeRing = new Fade[FadeSlots];
        /// <summary>Per pickup slot: the sim time it spawned (power-ups draw their arming ring from it).</summary>
        public readonly double[] SpawnedAt = new double[FadeSlots];
        public readonly int[] SpawnedReady = new int[FadeSlots];
        int _arc, _pop, _ack, _pulse;

        /// <summary>The local player's coin pick-ups (sim time, value), for the HUD's coins that fly to the counter.</summary>
        public readonly double[] CoinAt = new double[CoinFlights];
        public readonly int[] CoinValue = new int[CoinFlights];
        public int CoinCount { get; private set; }

        /// <summary>Map-event clocks (sim seconds; -999 = never).</summary>
        public double DropIncomingAt = -999, DropLandedAt = -999, DropClaimedAt = -999, DropGoneAt = -999;
        public double EggExposedAt = -999, EggSealedAt = -999, BridgeWarnAt = -999, BridgeFallAt = -999, BridgeRepairAt = -999;
        public int DropLandTick, BridgeFallTick, BridgeSelfRepairTick, BridgeSpan = -1, EggEndTick;
        public double DropX, DropZ, DropRadius = 2.5, BridgeX, BridgeZ, BridgeRadius;
        /// <summary>Who stands on the drop (MapEvents.Contest*), from SupplyDropContest.</summary>
        public int DropContest;
        /// <summary>The last class-switch answer for the local player: sim time, code (0 accepted), class.</summary>
        public double SwitchAt = -999;
        public int SwitchCode = -1, SwitchClass = -1;
        /// <summary>The last ping refusal of the local player: sim time and wait ticks (the wheel greys).</summary>
        public double PingRefusedAt = -999;
        public int PingRefusedWait;
        /// <summary>Counters (tests, the debug overlay).</summary>
        public int Seen, Unmapped;

        public int LocalActor;
        public double Pace = 2;
        int _cursor = -1, _epoch = int.MinValue;

        public void Reset()
        {
            for (int i = 0; i < Arcs; i++) ArcRing[i].T = -999;
            for (int i = 0; i < Pops; i++) PopRing[i].T = -999;
            for (int i = 0; i < Acks; i++) AckRing[i].T = -999;
            for (int i = 0; i < Pulses; i++) PulseRing[i].T = -999;
            for (int i = 0; i < FadeSlots; i++) { FadeRing[i].T = -999; SpawnedAt[i] = -999; SpawnedReady[i] = 0; }
            for (int i = 0; i < CoinFlights; i++) CoinAt[i] = -999;
            CoinCount = 0;
            DropIncomingAt = DropLandedAt = DropClaimedAt = DropGoneAt = -999;
            EggExposedAt = EggSealedAt = BridgeWarnAt = BridgeFallAt = BridgeRepairAt = -999;
            SwitchAt = PingRefusedAt = -999;
            SwitchCode = SwitchClass = -1;
            DropContest = 0;
            BridgeSpan = -1;
            Seen = Unmapped = 0;
        }

        public double Wall(double simAge) => Pace > 0 ? simAge / Pace : simAge;

        static bool Ok(World w, int id) => id >= 0 && id < w.ActorCount;
        static TeamId TeamOf(World w, int id) => Ok(w, id) ? w.Actors[id].Team : TeamId.Azure;
        static string ClassWord(World w, int id) => Ok(w, id) ? KillWords.ClassUpper[Math.Max(0, Math.Min(KillWords.ClassUpper.Length - 1, (int)w.Actors[id].Cls))] : string.Empty;

        TeamId Mine(World w) => TeamOf(w, LocalActor);

        /// <summary>Reads every event pushed since the last call (call after each sim tick).</summary>
        public void Read(World w)
        {
            if (w == null) return;
            if (w.Epoch != _epoch)
            {
                _epoch = w.Epoch;
                Reset();
                _cursor = w.EventHead;
            }
            if (_cursor < 0 || _cursor > w.EventHead) _cursor = Math.Max(0, w.EventHead - World.EventCapacity);
            if (w.EventHead - _cursor > World.EventCapacity) _cursor = w.EventHead - World.EventCapacity;
            DepthNames.Bind(w);
            double now = w.Tick / w.Config.SimHz;
            for (; _cursor < w.EventHead; _cursor++)
            {
                SimEvent e = w.Events[_cursor % World.EventCapacity];
                if (e == null) continue;
                if (e.Type == SimEventType.DeliverBiomass)
                {
                    if (DepthEvents.DeliveryVisual(e.Bio) == DepthVisual.CoolPulse) { AddPop(e.X, e.Z, 2.4, now, PopCool, e.A, 0); DepthOut.Sound("depth-egg-cool"); }
                    continue;
                }
                int ix = DepthEvents.IndexOf((int)e.Type);
                if (ix < 0) continue;
                Seen++;
                int bio = e.Bio.HasValue ? (int)e.Bio.Value : -1;
                if (DepthEvents.VisualFor(ix, e.B, e.Value, bio) == DepthVisual.Unmapped) { Unmapped++; continue; }
                OnEvent(w, ix, e, bio, now);
            }
        }

        void OnEvent(World w, int ix, SimEvent e, int bio, double now)
        {
            int me = LocalActor;
            TeamId mine = Mine(w);
            int v = (int)Math.Round(e.Value);
            int slot = DepthRead.Slot(e);
            switch (ix)
            {
                case DepthEvents.IxDeliverRefused:
                    if (e.A != me) break;
                    DepthOut.Tell(e.B == 2 ? DepthWords.RefusedNotRaider : DepthWords.RefusedNotFeeder);
                    DepthOut.Sound("depth-no");
                    break;
                case DepthEvents.IxHandoff:
                    AddArc(w, e.A, e.B, 0, 0, 0, 0, now, ArcHandoff, bio);
                    DepthOut.Sound("depth-handoff");
                    if (e.A == me || e.B == me)
                        DepthOut.Say(DepthLines.Handoff(w.Epoch, e.A, e.B, me, v, BioWord(bio), ClassWord(w, e.A), ClassWord(w, e.B)));
                    break;
                case DepthEvents.IxSourceGather:
                    {
                        bool water = bio >= 0 && bio == DepthEvents.BioWater;
                        AddPop(e.X, e.Z, 0.9, now, water ? PopSplash : PopChop, e.A, 0);
                        if (e.A == me) { AddPop(e.X, e.Z, 0, now, PopPlus, e.A, 1); DepthOut.Sound(water ? "depth-splash" : "depth-chop"); }
                        break;
                    }
                case DepthEvents.IxBankDeposit:
                    AddArc(w, e.A, -1, 0, 0, e.X, e.Z, now, ArcBank, bio);
                    if (TeamOf(w, e.A) == mine) DepthOut.Sound("depth-bank");
                    break;
                case DepthEvents.IxBankWithdraw:
                    AddArc(w, -1, e.A, e.X, e.Z, 0, 0, now, ArcWithdraw, bio);
                    if (e.A == me) DepthOut.Sound("depth-withdraw");
                    break;
                case DepthEvents.IxClassSwitch:
                    if (e.B == 0 && Ok(w, e.A) && w.Actors[e.A].Alive) { AddPop(e.X, e.Z, 1.2, now, PopSwitch, e.A, v); DepthNames.StyleChanged(e.A); }
                    if (e.A == me)
                    {
                        SwitchAt = now;
                        SwitchCode = e.B;
                        SwitchClass = v;
                        if (e.B == 2) DepthOut.Tell(DepthWords.GoToStation);
                        else if (e.B == 1) DepthOut.Tell(DepthLines.Wait(0, StationRules.Seconds(DepthRead.SwitchCdTicks(w, me), w.Config.SimHz)));
                        DepthOut.Sound(e.B == 0 ? "depth-switch" : "depth-no");
                    }
                    break;
                case DepthEvents.IxUpgrade:
                    if (v >= 2)
                    {
                        TeamId t = TeamOf(w, e.A);
                        PulseRing[_pulse] = new Pulse { Team = (int)t, Cls = e.B, T = now };
                        _pulse = (_pulse + 1) % Pulses;
                        if (t == mine)
                        {
                            int c = Math.Max(0, Math.Min(KillWords.ClassUpper.Length - 1, e.B));
                            DepthOut.Announce(DepthLines.LevelUp(w.Epoch, (int)t, c, v, KillWords.ClassUpper[c]), DepthWords.Upgrade,
                                t == TeamId.Azure ? BannerStyle.Azure : BannerStyle.Crimson, 2, 3.0);
                            DepthOut.Sound("depth-upgrade");
                        }
                    }
                    else if (e.A == me)
                    {
                        DepthOut.Tell(StationRules.Words(StationRules.FromSimCode(v)));
                        DepthOut.Sound("depth-no");
                    }
                    break;
                case DepthEvents.IxPickupSpawned:
                    if (slot >= 0 && slot < FadeSlots) { SpawnedAt[slot] = now; SpawnedReady[slot] = e.B == 0 ? 0 : v; FadeRing[slot].T = -999; }
                    if (e.B == 0) AddPop(e.X, e.Z, 0.7, now, PopCoins, e.A, v);
                    else if (e.A < 0)
                    {
                        int secs = StationRules.Seconds(v - w.Tick, w.Config.SimHz);
                        DepthOut.Announce(DepthLines.PowerIn(w.Epoch, e.B, secs), DepthWords.Counter(e.B), BannerStyle.Neutral, 1, 2.5);
                        DepthOut.Sound("depth-power-spawn");
                    }
                    break;
                case DepthEvents.IxPickupTaken:
                    if (slot >= 0 && slot < FadeSlots) SpawnedAt[slot] = -999;
                    if (e.B == 0)
                    {
                        if (e.A == me)
                        {
                            AddPop(e.X, e.Z, 0, now, PopPlus, e.A, v);
                            CoinAt[CoinCount % CoinFlights] = now;
                            CoinValue[CoinCount % CoinFlights] = v;
                            CoinCount++;
                            DepthOut.Sound("depth-coin");
                        }
                    }
                    else
                    {
                        AddPop(e.X, e.Z, 1.0, now, PopTaken, e.A, e.B);
                        DepthOut.Say(DepthLines.Took(w.Epoch, e.A, me, e.B, ClassWord(w, e.A)), e.B);
                        DepthOut.Sound(e.A == me ? "depth-power-take" : "depth-power-other");
                    }
                    break;
                case DepthEvents.IxPickupExpired:
                    if (slot >= 0 && slot < FadeSlots) { FadeRing[slot] = new Fade { X = e.X, Z = e.Z, T = now, Kind = e.B }; SpawnedAt[slot] = -999; }
                    break;
                case DepthEvents.IxPowerUpEnded:
                    AddPop(e.X, e.Z, 1.0, now, v == 1 ? PopShatter : PopAuraEnd, e.A, e.B * 8 + v);
                    if (e.A == me) DepthOut.Sound(v == 1 ? "depth-ward-break" : "depth-power-end");
                    break;
                case DepthEvents.IxWardAbsorb:
                    AddPop(e.X, e.Z, 0.9, now, PopWard, e.A, v);
                    AddPop(e.X, e.Z, 0, now, PopBlocked, e.A, v);
                    break;
                case DepthEvents.IxPing:
                    if (TeamOf(w, e.A) != mine) break;   // only the local team's pings
                    Pings.Mirror(w, (PingKind)Math.Max(0, Math.Min(3, e.B)), e.X, e.Z, e.A, v / w.Config.SimHz);
                    break;
                case DepthEvents.IxPingRefused:
                    if (e.A != me) break;
                    PingRefusedAt = now;
                    PingRefusedWait = v;
                    Pings.NotifyRefused(v / w.Config.SimHz);
                    break;
                case DepthEvents.IxPingAnswered:
                    if (TeamOf(w, e.A) != mine) break;
                    {
                        double px = e.X, pz = e.Z;
                        LatestPing(e.B, ref px, ref pz);
                        AckRing[_ack] = new Ack { Bot = e.A, Call = v, X = px, Z = pz, T = now };
                        _ack = (_ack + 1) % Acks;
                        DepthOut.Say(DepthLines.Call(w.Epoch, e.A, me, v, ClassWord(w, e.A)), -1, v);
                        DepthOut.Sound("depth-ack");
                    }
                    break;
                case DepthEvents.IxDropIncoming:
                    DropIncomingAt = now; DropLandTick = v; DropX = e.X; DropZ = e.Z; DropRadius = e.Radius > 0 ? e.Radius : DropRadius; DropContest = 0;
                    DepthOut.Announce(DepthWords.DropIncoming, null, BannerStyle.Neutral, 3, 3.5);
                    DepthOut.Sound("depth-drop-incoming");
                    break;
                case DepthEvents.IxDropLanded:
                    DropLandedAt = now; DropX = e.X; DropZ = e.Z;
                    AddPop(e.X, e.Z, 2.2, now, PopDust, -1, 0);
                    DepthOut.Sound("depth-drop-thud");
                    break;
                case DepthEvents.IxDropContest:
                    DropContest = v;
                    break;
                case DepthEvents.IxDropClaimed:
                    DropClaimedAt = now;
                    AddPop(e.X, e.Z, 2.0, now, PopClaim, e.A, e.B);
                    DepthOut.Announce(DepthWords.DropClaimed, DepthLines.Claimed(w.Epoch, e.A, me, ClassWord(w, e.A)),
                        e.B == 0 ? BannerStyle.Azure : BannerStyle.Crimson, 3, 3.5);
                    DepthOut.Sound("depth-drop-claim");
                    break;
                case DepthEvents.IxDropExpired:
                    DropGoneAt = now;
                    break;
                case DepthEvents.IxEggExposed:
                    EggExposedAt = now; EggEndTick = v;
                    DepthOut.Announce(DepthWords.EggExposed, null, BannerStyle.Warning, 3, 4.0);
                    DepthOut.Sound("depth-egg-exposed");
                    break;
                case DepthEvents.IxEggEnded:
                    EggSealedAt = now;
                    if (e.B != 1) { DepthOut.Announce(DepthWords.EggSealed, null, BannerStyle.Neutral, 2, 2.5); DepthOut.Sound("depth-egg-sealed"); }
                    break;
                case DepthEvents.IxBridgeWarning:
                    BridgeWarnAt = now; BridgeFallTick = v; BridgeSpan = e.B; BridgeX = e.X; BridgeZ = e.Z; BridgeRadius = e.Radius;
                    DepthOut.Announce(DepthWords.BridgeWarning, null, BannerStyle.Warning, 3, 3.5);
                    DepthOut.Sound("depth-bridge-creak");
                    break;
                case DepthEvents.IxBridgeCollapsed:
                    BridgeFallAt = now; BridgeSelfRepairTick = v;
                    AddPop(e.X, e.Z, Math.Max(1, BridgeRadius), now, PopPlanks, -1, 0);
                    DepthOut.Announce(DepthWords.BridgeCollapsed, DepthWords.RepairPrompt, BannerStyle.Warning, 2, 3.0);
                    DepthOut.Sound("depth-bridge-fall");
                    break;
                case DepthEvents.IxBridgeRepaired:
                    if (v == -2)
                    {
                        if (e.A == me) { DepthOut.Tell(DepthWords.RepairNeedWood); DepthOut.Sound("depth-no"); }
                        break;
                    }
                    BridgeRepairAt = now;
                    BridgeSpan = -1;
                    if (v >= 0)
                    {
                        AddPop(e.X, e.Z, Math.Max(1, BridgeRadius), now, PopRepair, e.A, v);
                        DepthOut.Announce(DepthWords.BridgeRepaired, null, BannerStyle.Neutral, 2, 2.5);
                        DepthOut.Sound("depth-hammer");
                    }
                    break;
            }
        }

        static string BioWord(int bio)
        {
            if (bio >= 0 && bio == DepthEvents.BioWood) return DepthWords.Wood;
            if (bio >= 0 && bio == DepthEvents.BioWater) return DepthWords.Water;
            return DepthWords.Food;
        }

        /// <summary>The newest live ping of owner in the book (the answering bot's line goes to it); else the bot's own spot.</summary>
        static void LatestPing(int owner, ref double x, ref double z)
        {
            int best = -1;
            int serial = -1;
            for (int i = 0; i < PingBook.Capacity; i++)
            {
                Ping p = Pings.Book.Get(i);
                if (p.Active && p.Owner == owner && p.Serial > serial) { serial = p.Serial; best = i; }
            }
            if (best < 0) return;
            Ping q = Pings.Book.Get(best);
            x = q.X;
            z = q.Z;
        }

        void AddArc(World w, int from, int to, double fx, double fz, double tx, double tz, double now, int kind, int cargo)
        {
            if (Ok(w, from)) { fx = w.X[from]; fz = w.Z[from]; }
            if (Ok(w, to)) { tx = w.X[to]; tz = w.Z[to]; }
            ArcRing[_arc] = new Arc { From = from, To = to, FromX = fx, FromZ = fz, ToX = tx, ToZ = tz, T = now, Kind = kind, Cargo = cargo };
            _arc = (_arc + 1) % Arcs;
        }

        void AddPop(double x, double z, double r, double now, int kind, int actor, int value)
        {
            PopRing[_pop] = new Pop { X = x, Z = z, R = r, T = now, Kind = kind, Actor = actor, Value = value };
            _pop = (_pop + 1) % Pops;
        }

        /// <summary>True while a level pulse for (team, class) plays (wall seconds under 0.8).</summary>
        public bool Pulsing(int team, int cls, double now, out float age)
        {
            age = 0f;
            for (int i = 0; i < Pulses; i++)
            {
                Pulse p = PulseRing[i];
                if (p.Team != team || p.Cls != cls) continue;
                float a = (float)Wall(now - p.T);
                if (a >= 0f && a < 0.8f) { age = a; return true; }
            }
            return false;
        }
    }
}
