using System;
using System.Collections.Generic;
using System.Diagnostics;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// CM b13 item H: the Miner only mines. F mines an ore vein (30-tick channel, a hit restarts it, 3 ore max, other classes
    /// and other cargo refused), Space is HAUL, his presses never dig, place or barricade while the rule is on (bots too),
    /// the veins and groves sit where the placement put them, and the rule allocates nothing per tick. Every case prints.
    /// </summary>
    [TestFixture]
    public sealed class MinerRoleTests
    {
        static GameConfig Cfg => DepthKit.Cfg();

        static void Line(string what, object value, string limit) => Console.WriteLine("[b13 H] " + what + ": " + value + " (limit " + limit + ")");

        static SourceNode FirstVein(World w)
        {
            foreach (SourceNode s in w.Depth.Sources) if (s.Kind == BiomassType.Ore) return s;
            return null;
        }

        /// <summary>A Full world, only actor 0 (cls) alive, 1.0 u west of the first ore vein, facing it, no aim.</summary>
        static World AtVein(ClassId cls, out SourceNode s)
        {
            World w = DepthKit.Field(Cfg, cls);
            s = FirstVein(w);
            Assert.IsNotNull(s, "no ore vein placed");
            DepthKit.Put(w, 0, s.X - 1.0, s.Z);
            w.Actors[0].Facing = 0;
            w.HasAim[0] = 0;
            return w;
        }

        /// <summary>Steps until the first OreMined event with Value &gt; 0 (its tick count from the first step), or -1.</summary>
        static int TicksToOre(World w, int maxTicks, Action<int> before = null)
        {
            int head = w.EventHead;
            for (int t = 1; t <= maxTicks; t++)
            {
                before?.Invoke(t);
                SimStep.Step(w);
                foreach (SimEvent e in DepthKit.Since(w, head, SimEventType.OreMined)) if (e.Value > 0) return t;
            }
            return -1;
        }

        [Test]
        public void MiningAVeinIsAThirtyTickChannelAHitRestartsItAndTheCargoStopsAtThree()
        {
            World w = AtVein(ClassId.Miner, out SourceNode s);
            int charges = s.Charges;
            w.Inputs[0].Terraform = true;
            int need = Blacksmith.MineTicks(w.Config);
            int first = TicksToOre(w, 200);
            Line("ticks from the F press to the first ore", first, need + " (HutConfig.MineTicks 30 at 30 Hz)");
            Assert.AreEqual(need, first);
            DepthKit.Steps(w, 150);
            Actor a = w.Actors[0];
            Line("ore carried after holding F 180 ticks / kind / vein charges used", a.CarryAmount + " / " + a.Carry + " / " + (charges - s.Charges), "3 / Ore / 3");
            Assert.AreEqual(3, a.CarryAmount);
            Assert.AreEqual(CarryKind.Ore, a.Carry);
            Assert.AreEqual(charges - 3, s.Charges);
            Assert.AreEqual(-1, Blacksmith.ChannelVein(w, 0), "a full Miner stops channelling");

            World h = AtVein(ClassId.Miner, out _);
            h.Inputs[0].Terraform = true;
            int hitAt = TicksToOre(h, 200, t => { if (t == 21) h.Actors[0].Hp -= 1; });
            Line("ticks to the first ore with a hit before tick 21", hitAt, (21 + need) + " (20 + the hit + " + need + ")");
            Assert.AreEqual(21 + need, hitAt);

            World r = AtVein(ClassId.Miner, out _);
            r.Actors[0].IsHuman = false; // lane S2 (gate S15): only a bot still needs F held; a player's tap runs the channel
            r.Inputs[0].Terraform = true;
            DepthKit.Steps(r, 15);
            r.Inputs[0].Terraform = false;
            DepthKit.Steps(r, 1);
            Line("releasing F cancels a bot's channel (vein / progress)", Blacksmith.ChannelVein(r, 0) + " / " + Blacksmith.ChannelPhase(r, 0), "-1 / -1");
            Assert.AreEqual(-1, Blacksmith.ChannelVein(r, 0));

            World t = AtVein(ClassId.Miner, out _);   // lane S2: a player's one-tick tap mines one unit
            t.Inputs[0].Terraform = true;
            int tapAt = TicksToOre(t, need + 10, k => { if (k == 2) t.Inputs[0].Terraform = false; });
            Line("a player's one-tick F tap at a vein: ticks to the ore", tapAt, need + " or so");
            Assert.Greater(tapAt, 0, "a tap mines one unit");
            Assert.AreEqual(1, t.Actors[0].CarryAmount);
            Assert.AreEqual(-1, Blacksmith.ChannelVein(t, 0), "the tap's channel ends after its unit");
        }

        [Test]
        public void OtherClassesAndOtherCargoAreRefusedAndEMinesNothing()
        {
            foreach (ClassId c in new[] { ClassId.Warrior, ClassId.Farmer, ClassId.Demolitions, ClassId.Builder, ClassId.Healer })
            {
                World w = AtVein(c, out SourceNode s);
                int charges = s.Charges;
                w.Inputs[0].Terraform = true;
                w.Inputs[0].Interact = true;
                DepthKit.Steps(w, 120);
                Line(c + " holding F and E at a vein 120 ticks: ore carried / charges used", Blacksmith.OreOf(w.Actors[0]) + " / " + (charges - s.Charges), "0 / 0");
                Assert.AreEqual(0, Blacksmith.OreOf(w.Actors[0]));
                Assert.AreEqual(charges, s.Charges);
            }
            World m = AtVein(ClassId.Miner, out _);
            m.Actors[0].Carry = CarryKind.Wood;
            m.Actors[0].CarryAmount = 1;
            int head = m.EventHead;
            m.Inputs[0].Terraform = true;
            DepthKit.Steps(m, 60);
            List<SimEvent> ev = DepthKit.Since(m, head, SimEventType.OreMined);
            Line("Miner carrying wood presses F: OreMined events / first Value / wood kept", ev.Count + " / " + (ev.Count > 0 ? ev[0].Value : 0) + " / " + m.Actors[0].Carry, "1 / " + Blacksmith.RefusedOtherCargo + " / Wood");
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(Blacksmith.RefusedOtherCargo, (int)ev[0].Value);
            Assert.AreEqual(CarryKind.Wood, m.Actors[0].Carry);

            World far = DepthKit.Field(Cfg, ClassId.Miner, 56, 40);
            far.Actors[0].IsHuman = false; // lane S2: a HUMAN Miner digs away from a vein (PxClassRepairTests); a bot never does
            head = far.EventHead;
            far.Inputs[0].Terraform = true;
            DepthKit.Steps(far, 3);
            int terra = DepthKit.Since(far, head, SimEventType.Terraform).Count;
            ev = DepthKit.Since(far, head, SimEventType.OreMined);
            Line("Miner F with no vein in reach: Terraform events / OreMined refusal", terra + " / " + (ev.Count > 0 ? ev[0].Value : 0), "0 / " + Blacksmith.RefusedNoVein);
            Assert.AreEqual(0, terra);
            Assert.AreEqual(1, ev.Count);
            Assert.AreEqual(Blacksmith.RefusedNoVein, (int)ev[0].Value);
        }

        [Test]
        public void HaulIsFiveSecondsOfOnePointTwoSpeedOnTheClassCooldownAndNeverABarricade()
        {
            World w = DepthKit.Field(Cfg, ClassId.Miner, 56, 40);
            int head = w.EventHead;
            w.Inputs[0].Ability = true;
            DepthKit.Steps(w, 1);
            w.Inputs[0].Ability = false;
            Actor a = w.Actors[0];
            double cd = a.AbilityCd;
            double s1 = Blacksmith.SpeedScale(w, 0);
            int haul = Durations.T30(w.Config, w.Config.Depth.Hut.HaulTicks);
            DepthKit.Steps(w, haul - 2);
            double s149 = Blacksmith.SpeedScale(w, 0);
            DepthKit.Steps(w, 1);
            double s150 = Blacksmith.SpeedScale(w, 0);
            // lane S2: HAUL now raises its own cast Ability event (Value 1); a barricade or a barricade refusal is still never one
            int bar = DepthKit.Since(w, head, SimEventType.BarricadePlaced).Count + DepthKit.Since(w, head, SimEventType.Ability).FindAll(e => e.Value != 1).Count;
            double row = w.Config.Class(ClassId.Miner).AbilityCooldown;
            Line("HAUL (" + haul + " ticks) speed after 1 / " + (haul - 1) + " / " + haul + " ticks, cooldown after the press, barricade or ability events", s1 + " / " + s149 + " / " + s150 + " / " + cd.ToString("0.000") + " / " + bar,
                "1.2 / 1.2 / 1 / " + row + " minus one tick / 0");
            Assert.AreEqual(1.2, s1, 1e-12);
            Assert.AreEqual(1.2, s149, 1e-12);
            Assert.AreEqual(1.0, s150, 1e-12);
            Assert.AreEqual(row, cd, 0.05);
            Assert.AreEqual(0, bar);
        }

        [Test]
        public void TheVeinAndGroveCellsArePrinted()
        {
            World w = DepthKit.Field(Cfg, ClassId.Warrior);
            int veins = 0, groves = 0;
            foreach (SourceNode s in w.Depth.Sources)
            {
                if (s.Kind == BiomassType.Water) continue;
                int cx = JsMath.FloorToInt(s.X), cz = JsMath.FloorToInt(s.Z);
                Line((s.Kind == BiomassType.Ore ? "ore vein " : "grove ") + s.Id + " placed cell", "(" + cx + "," + cz + ") charges " + s.MaxCharges, "open floor, mirrored");
                Assert.AreEqual(Cell.Floor, w.Map.Cells[cz * w.Map.Width + cx]);
                if (s.Kind == BiomassType.Ore) veins++; else groves++;
            }
            Line("ore veins / groves", veins + " / " + groves, "6 / 10");
            Assert.AreEqual(6, veins);
            Assert.AreEqual(10, groves);
        }

        [Test]
        public void MinerPressesNeverDigPlaceOrBarricadeInA16v16BotMatch()
        {
            World w = DepthKit.Make(Cfg, ClassId.Warrior, 4242, 16);
            int digs = 0, bars = 0, ore = 0, banks = 0, refused = 0;
            DepthKit.RunBots(w, 5400, e =>
            {
                bool miner = e.A >= 0 && e.A < w.ActorCount && w.Actors[e.A].Cls == ClassId.Miner;
                if (e.Type == SimEventType.Terraform && miner && (e.Value == Terraform.Dug || e.Value == Terraform.Placed)) digs++;
                if (e.Type == SimEventType.BarricadePlaced && miner) bars++;
                if (e.Type == SimEventType.OreMined) { if (e.Value > 0) ore++; else refused++; }
                if (e.Type == SimEventType.BankDeposit && e.Bio == BiomassType.Ore) banks++;
            });
            Line("16v16 seed 4242, 3 minutes of bots: Miner digs+places / Miner barricades / ore mined / ore refusals / ore bank presses / hut levels azure,crimson",
                digs + " / " + bars + " / " + ore + " / " + refused + " / " + banks + " / " + Blacksmith.Level(w, TeamId.Azure) + "," + Blacksmith.Level(w, TeamId.Crimson), "0 / 0 / more than 0 / any / more than 0 / any");
            Assert.AreEqual(0, digs);
            Assert.AreEqual(0, bars);
            Assert.Greater(ore, 0);
            Assert.Greater(banks, 0);
        }

        static bool AnyChanneling(World w)
        {
            for (int i = 0; i < w.ActorCount; i++) if (Blacksmith.ChannelVein(w, i) >= 0) return true;
            return false;
        }

        static long Measure(bool hutOn, out int channelTicks, out long ruleBytes, out double usPerTick)
        {
            GameConfig cfg = DepthKit.Cfg(d => d.Hut.On = hutOn);
            World w = DepthKit.Make(cfg, ClassId.Warrior, 4242, 16);
            InputFrame scratch = SimInput.EmptyInput(), probe = SimInput.EmptyInput();
            for (int t = 0; t < 600; t++) MatchTick.Advance(w, null, scratch);
            // b13 integrator: the window must hold a Miner mining, so warm-up runs on (at most 2400 more ticks) until one channels.
            for (int t = 0; hutOn && t < 2400 && !AnyChanneling(w); t++) MatchTick.Advance(w, null, scratch);
            BotsState st = BotBrain.GetBotsState(w);
            channelTicks = 0;
            ruleBytes = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            long t0 = Stopwatch.GetTimestamp();
            for (int t = 0; t < 300; t++)
            {
                MatchTick.Advance(w, null, scratch);
                // The rule's own paths once more, measured alone (the base sim allocates on its own; see the whole-sim line).
                long r0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < w.ActorCount; i++)
                {
                    if (Blacksmith.ChannelVein(w, i) >= 0) channelTicks++;
                    Blacksmith.SpeedScale(w, i);
                    Stations.MaxHp(w, w.Actors[i].Team, w.Actors[i].Cls);
                    if (w.Actors[i].Cls != ClassId.Miner || i == 0) continue;
                    Blacksmith.VeinInReach(w, i);
                    BotMine.BestVein(w, i, out _);
                    BotMine.DecideAbility(w, st, i, w.Actors[i].AiState);
                    BotMine.Act(w, st, i, probe);
                }
                ruleBytes += GC.GetAllocatedBytesForCurrentThread() - r0;
            }
            long bytes = GC.GetAllocatedBytesForCurrentThread() - before - ruleBytes;
            usPerTick = 1e6 * (Stopwatch.GetTimestamp() - t0) / Stopwatch.Frequency / 300.0;
            return bytes;
        }

        [Test]
        public void TheRuleAllocatesNothingOverThreeHundredTicksWithMinersMining()
        {
            long off = Measure(false, out _, out _, out double usOff);
            long on = Measure(true, out int mining, out long rule, out double usOn);
            Line("16v16 seed 4242 Full, 600 warm-up ticks then 300 with Miners mining: bytes allocated by the hut rule's own paths (HutSystem runs inside the step; the probes re-run VeinInReach, BestVein, DecideAbility, Act, SpeedScale, MaxHp)", rule + " B", "0 B");
            Line("Miner channel-ticks in the window", mining, "more than 0");
            Line("whole-sim bytes in the window, hut on / control (the base sim allocates on its own; information)", on + " B / " + off + " B", "on at most control + 0 B is not required");
            Line("mean sim microseconds per tick on / off (one 300-tick sample; the balance test asserts +10% over every match)", usOn.ToString("0.0") + " / " + usOff.ToString("0.0"), "+10%");
            Assert.AreEqual(0, rule);
            Assert.Greater(mining, 0);
        }
    }
}
