// Player-experience wave (2026-11-22), lane S2: class repairs and "never silent" reporting. One test per item of the lane's
// brief; each drives the human seat (actor 0) through MatchTick.AdvanceHumans, the real seam, on a Full-rules field with
// passive dummies. Every case prints one line (what, value, limit).
using System;
using System.Collections.Generic;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Tests.Harness;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    [TestFixture]
    public sealed class PxClassRepairTests
    {
        static void Line(string what, object value, string limit) => Console.WriteLine("[px S2] " + what + ": " + value + " (limit " + limit + ")");

        sealed class Rig
        {
            public readonly World W;
            public readonly InputFrame[] F;
            readonly InputFrame _scratch = SimInput.EmptyInput();
            public int Head;

            public Rig(ClassId me, double x = 56.5, double z = 40.5, Action<GameConfig> tweak = null)
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
                tweak?.Invoke(cfg);
                W = DepthKit.Field(cfg, me, x, z);
                F = new InputFrame[W.ActorCount];
                for (int j = 0; j < F.Length; j++)
                {
                    F[j] = SimInput.EmptyInput();
                    F[j].AimX = double.NaN;
                    F[j].AimZ = double.NaN;
                }
                Step(1);
                Head = W.EventHead;
            }

            public InputFrame Me => F[0];
            public int Foe(int k = 0) => W.ActorCount / 2 + k;
            public int Ally(int k = 0) => 1 + k;

            public void Place(int j, ClassId c, double x, double z)
            {
                DepthKit.Become(W, j, c);
                DepthKit.Put(W, j, x, z);
            }

            public void Aim(double x, double z)
            {
                Me.AimX = x;
                Me.AimZ = z;
            }

            public void Step(int n = 1)
            {
                for (int i = 0; i < n; i++) MatchTick.AdvanceHumans(W, F, _scratch);
            }

            /// <summary>One press of a button for one tick, then released, then n more ticks.</summary>
            public void Tap(Action<InputFrame, bool> set, int after)
            {
                set(Me, true);
                Step(1);
                set(Me, false);
                Step(after);
            }

            public List<SimEvent> Ev(SimEventType t) => DepthKit.Since(W, Head, t);

            public int Count(SimEventType t, Func<SimEvent, bool> pred)
            {
                int n = 0;
                foreach (SimEvent e in Ev(t)) if (pred(e)) n++;
                return n;
            }
        }

        static void Attack(InputFrame f, bool on) => f.Attack = on;

        // ---------------------------------------------------------------- 1. blocked hits are reported

        [Test]
        public void EverySwingContactOnAnEnemyInReachYieldsHitOrHitBlocked()
        {
            int bad = 0;
            void Case(string name, Action<Rig, int> setup, int wantWhy)
            {
                var r = new Rig(ClassId.Warrior);
                int foe = r.Foe();
                r.Place(foe, ClassId.Farmer, 58.0, 40.5);
                setup(r, foe);
                r.Head = r.W.EventHead;
                r.Aim(r.W.X[foe], r.W.Z[foe]);
                r.Tap(Attack, 14);
                int hits = r.Count(SimEventType.Hit, e => e.A == foe && e.B == 0);
                int blocked = r.Count(SimEventType.HitBlocked, e => e.A == foe && e.B == 0 && (int)e.Value == wantWhy);
                bool ok = wantWhy == PlayerReasons.BlockedShield ? hits == 1 && blocked == 1 : wantWhy == 0 ? hits == 1 : hits == 0 && blocked == 1;
                Line("Warrior swing on a Farmer at 1.5 u, " + name + ": Hit / HitBlocked(" + wantWhy + ")", hits + " / " + blocked, wantWhy == 0 ? "1 / 0" : wantWhy == PlayerReasons.BlockedShield ? "1 / 1" : "0 / 1");
                if (!ok) bad++;
            }
            Case("open", (r, f) => { }, 0);
            Case("spawn protected", (r, f) => r.W.Actors[f].SpawnProtect = 3, PlayerReasons.BlockedSpawnProtect);
            Case("wall between", (r, f) => MapGrid.SetCell(r.W, 57, 40, Cell.Wall), PlayerReasons.BlockedWall);
            Case("dodging (i-frames at contact)", (r, f) =>
            {
                // the dodge starts so the contact tick (press + 12) is its dash tick 1; its path stands still
                DodgeState s = r.W.Dodges;
                int b = f * s.Stride;
                for (int k = 0; k < s.Stride; k++) { s.PathX[b + k] = r.W.X[f]; s.PathZ[b + k] = r.W.Z[f]; }
                s.Start[f] = r.W.Tick + 11;
                s.Len[f] = 6;
            }, PlayerReasons.BlockedDodge);
            Case("Warrior shield up (the hit lands cut)", (r, f) =>
            {
                r.Place(f, ClassId.Warrior, 58.0, 40.5);
                r.W.Actors[f].AbilityActive = 2.5;
                r.W.Actors[f].Facing = Math.PI; // facing the attacker
            }, PlayerReasons.BlockedShield);
            Assert.AreEqual(0, bad, "every contact = Hit or HitBlocked with the reason (lines above)");
        }

        // ---------------------------------------------------------------- 2. structures answer

        [Test]
        public void ASwingOnAnEnemyBarricadeAnswersAndIsNotAWhiffARockAnswersSolidOnce()
        {
            var r = new Rig(ClassId.Warrior);
            World w = r.W;
            int foe = r.Foe();
            Barricade b = w.Barricades[0];
            b.Active = true; b.Owner = foe; b.Team = TeamId.Crimson; b.Cx = 57; b.Cz = 40;
            b.Hp = w.Config.Abilities.MinerBarricade.MaxHp; b.Ttl = 20; b.PrevCell = w.Map.Cells[40 * w.Map.Width + 57];
            MapGrid.SetCell(w, 57, 40, Cell.Barricade);
            r.Head = w.EventHead;
            int whiffs = w.Depth.Feel.Whiffs;
            r.Aim(57.5, 40.5);
            r.Tap(Attack, 14);
            List<SimEvent> sh = r.Ev(SimEventType.StructureHit);
            bool okB = sh.Count == 1 && sh[0].A == 0 && sh[0].B == PlayerReasons.StructureBarricade && sh[0].Value > 0 && sh[0].Radius < 1 && sh[0].Radius > 0;
            Line("Warrior swing on an enemy barricade: StructureHit count / kind / damage / health left; whiffs added", sh.Count + " / " + (sh.Count > 0 ? sh[0].B + " / " + sh[0].Value + " / " + sh[0].Radius.ToString("0.00") : "-") + "; " + (w.Depth.Feel.Whiffs - whiffs), "1 / 1 / >0 / (0,1); 0");
            Assert.IsTrue(okB, "barricade StructureHit");
            Assert.AreEqual(whiffs, w.Depth.Feel.Whiffs, "a swing that damaged a structure is not a whiff");

            var r2 = new Rig(ClassId.Warrior);
            MapGrid.SetCell(r2.W, 57, 40, Cell.Wall);
            r2.Head = r2.W.EventHead;
            int wh2 = r2.W.Depth.Feel.Whiffs;
            r2.Aim(57.5, 40.5);
            r2.Tap(Attack, 14);
            List<SimEvent> s2 = r2.Ev(SimEventType.StructureHit);
            Line("Warrior swing at map rock: StructureHit Solid count / Value; whiffs added", s2.Count + " / " + (s2.Count > 0 ? s2[0].B + ":" + s2[0].Value : "-") + "; " + (r2.W.Depth.Feel.Whiffs - wh2), "1 / 5:0; 1");
            Assert.AreEqual(1, s2.Count);
            Assert.AreEqual(PlayerReasons.StructureSolid, s2[0].B);
            Assert.AreEqual(0, s2[0].Value);
            Assert.AreEqual(wh2 + 1, r2.W.Depth.Feel.Whiffs, "rock: still a whiff");
        }

        // ---------------------------------------------------------------- 3. no silent press

        [Test]
        public void DeadPressesRaiseActionRefusedOncePerPressAndAHeldButtonAtMostOncePerSecond()
        {
            int bad = 0;
            void Case(string name, ClassId cls, Action<Rig> setup, Action<InputFrame, bool> set, int button, int why)
            {
                var r = new Rig(cls);
                setup?.Invoke(r);
                r.Head = r.W.EventHead;
                r.Aim(r.W.X[0] + 3, r.W.Z[0]);
                r.Tap(set, 2);
                int got = r.Count(SimEventType.ActionRefused, e => e.A == 0 && e.B == button && (int)e.Value == why);
                Line(cls + " " + name + ": ActionRefused(" + button + ", " + why + ")", got, "1");
                if (got != 1) bad++;
            }
            Case("F (DigPlace)", ClassId.Warrior, null, (f, on) => f.Terraform = on, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedNotYourClass);
            Case("F (DigPlace)", ClassId.Farmer, null, (f, on) => f.Terraform = on, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedNotYourClass);
            Case("ability on cooldown", ClassId.Farmer, r => r.W.Actors[0].AbilityCd = 5, (f, on) => f.Ability = on, PlayerReasons.ButtonAbility, PlayerReasons.RefusedCooldown);
            Case("HAUL on cooldown", ClassId.Miner, r => r.W.Actors[0].AbilityCd = 5, (f, on) => f.Ability = on, PlayerReasons.ButtonAbility, PlayerReasons.RefusedCooldown);
            Case("ability while staggered", ClassId.Warrior, r => r.W.Actors[0].HitStunTicks = 20, (f, on) => f.Ability = on, PlayerReasons.ButtonAbility, PlayerReasons.RefusedStaggered);
            Case("attack staggered past the buffer", ClassId.Warrior, r => r.W.Actors[0].HitStunTicks = 20, Attack, PlayerReasons.ButtonAttack, PlayerReasons.RefusedStaggered);
            Case("E in an open field", ClassId.Warrior, null, (f, on) => f.Interact = on, PlayerReasons.ButtonInteract, PlayerReasons.RefusedNoTarget);
            Case("dodge on cooldown", ClassId.Builder, r => r.W.Dodges.Ready[0] = r.W.Tick + 60, (f, on) => f.Dodge = on, PlayerReasons.ButtonDodge, PlayerReasons.RefusedCooldown);
            Case("cancel with nothing to cancel", ClassId.Warrior, null, (f, on) => f.Cancel = on, PlayerReasons.ButtonCancel, PlayerReasons.RefusedNothingToCancel);
            Case("TNT key while the TNT cools", ClassId.Demolitions, r => r.W.Demolitions.TntReadyTick[0] = r.W.Tick + 90, (f, on) => f.Terraform = on, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedCooldown);
            Case("purify away from the shrine", ClassId.Healer, null, (f, on) => f.Terraform = on, PlayerReasons.ButtonDigPlace, PlayerReasons.RefusedOutOfRange);

            // a held (or mashed) dead button: at most one a second
            var h = new Rig(ClassId.Warrior);
            h.Head = h.W.EventHead;
            for (int t = 0; t < 60; t++)
            {
                h.Me.Terraform = (t % 2) == 0; // mash: a rising edge every other tick for 2 s
                h.Step(1);
            }
            int mashed = h.Count(SimEventType.ActionRefused, e => e.A == 0);
            Line("Warrior mashing F for 2 s: ActionRefused raised", mashed, "2");
            if (mashed != 2) bad++;

            // bots never raise it
            var b = new Rig(ClassId.Warrior);
            b.W.Actors[0].IsHuman = false;
            b.Head = b.W.EventHead;
            b.Tap((f, on) => f.Terraform = on, 2);
            int bot = b.Count(SimEventType.ActionRefused, e => true);
            Line("a bot's dead F press: ActionRefused", bot, "0");
            if (bot != 0) bad++;
            Assert.AreEqual(0, bad, "never-silent presses (lines above)");
        }

        // ---------------------------------------------------------------- 4. Demolitions point-blank

        [Test]
        public void DemolitionsPointBlankSwingsThePryBarAndKeepsTheTntReadyAtFourUnitsSheThrows()
        {
            var r = new Rig(ClassId.Demolitions);
            int foe = r.Foe();
            r.Place(foe, ClassId.Farmer, 57.5, 40.5);
            r.Head = r.W.EventHead;
            int pressTick = r.W.Tick;
            r.Aim(57.5, 40.5);
            r.Tap(Attack, 9);
            int hitTick = -1;
            foreach (SimEvent e in r.Ev(SimEventType.Hit)) if (e.A == foe && e.B == 0 && e.Cause == DamageCause.Melee) { hitTick = 1; break; }
            int thrown = r.Ev(SimEventType.TntThrown).Count;
            bool ready = r.W.Tick >= r.W.Demolitions.TntReadyTick[0];
            Line("Demolitions, TNT ready, enemy at 1.0 u: melee Hit within 0.3 s (10 ticks) / TNT thrown / TNT still ready", (hitTick > 0) + " / " + thrown + " / " + ready, "True / 0 / True");
            Assert.IsTrue(hitTick > 0, "pry-bar hit within 0.3 s");
            Assert.AreEqual(0, thrown);
            Assert.IsTrue(ready);

            var far = new Rig(ClassId.Demolitions);
            int f2 = far.Foe();
            far.Place(f2, ClassId.Farmer, 60.5, 40.5);
            far.Head = far.W.EventHead;
            far.Aim(60.5, 40.5);
            far.Tap(Attack, 2);
            int t2 = far.Ev(SimEventType.TntThrown).Count;
            Line("Demolitions, TNT ready, enemy at 4 u: TNT thrown", t2, "1");
            Assert.AreEqual(1, t2);

            var key = new Rig(ClassId.Demolitions);
            int f3 = key.Foe();
            key.Place(f3, ClassId.Farmer, 57.5, 40.5);
            key.Head = key.W.EventHead;
            key.Aim(57.5, 40.5);
            key.Tap((f, on) => f.Terraform = on, 2);
            int t3 = key.Ev(SimEventType.TntThrown).Count;
            Line("Demolitions, TNT key with an enemy at 1.0 u: TNT thrown (the key always throws)", t3, "1");
            Assert.AreEqual(1, t3);
        }

        // ---------------------------------------------------------------- 5. the Healer chooses

        [Test]
        public void HealerMendsTheMostHurtWhenNotAimingAndSwingsAtTheEnemySheAimsAt()
        {
            // S25: two hurt allies (90%, 30%), aim on neither: the 30% one is mended
            var r = new Rig(ClassId.Healer);
            int a1 = r.Ally(0), a2 = r.Ally(1);
            r.Place(a1, ClassId.Farmer, 58.5, 40.5);
            r.Place(a2, ClassId.Farmer, 54.5, 40.5);
            r.W.Actors[a1].Hp = r.W.Actors[a1].MaxHp * 0.9;
            r.W.Actors[a2].Hp = r.W.Actors[a2].MaxHp * 0.3;
            r.Head = r.W.EventHead;
            r.Aim(56.5, 45.5); // nowhere near either
            r.Tap(Attack, 2);
            List<SimEvent> heals = r.Ev(SimEventType.HealLanded);
            int target = heals.Count > 0 ? heals[0].A : -1;
            Line("S25 human Healer, aim on neither ally: mended actor (30% one = " + a2 + ")", target, a2.ToString());
            Assert.AreEqual(a2, target);

            // aimed at the 90% ally: that one
            var r3 = new Rig(ClassId.Healer);
            r3.Place(a1, ClassId.Farmer, 58.5, 40.5);
            r3.Place(a2, ClassId.Farmer, 54.5, 40.5);
            r3.W.Actors[a1].Hp = r3.W.Actors[a1].MaxHp * 0.9;
            r3.W.Actors[a2].Hp = r3.W.Actors[a2].MaxHp * 0.3;
            r3.Head = r3.W.EventHead;
            r3.Aim(58.5, 40.7);
            r3.Tap(Attack, 2);
            List<SimEvent> h3 = r3.Ev(SimEventType.HealLanded);
            Line("human Healer aiming at the 90% ally: mended actor (" + a1 + ")", h3.Count > 0 ? h3[0].A : -1, a1.ToString());
            Assert.AreEqual(a1, h3.Count > 0 ? h3[0].A : -1);

            // S26: ally hurt at 4 u, aim on an enemy at 1.2 u: lantern Hit
            var r2 = new Rig(ClassId.Healer);
            int foe = r2.Foe();
            r2.Place(a1, ClassId.Farmer, 52.5, 40.5);
            r2.W.Actors[a1].Hp = r2.W.Actors[a1].MaxHp * 0.5;
            r2.Place(foe, ClassId.Farmer, 57.7, 40.5);
            r2.Head = r2.W.EventHead;
            r2.Aim(57.7, 40.5);
            r2.Tap(Attack, 9);
            int hit = r2.Count(SimEventType.Hit, e => e.A == foe && e.B == 0);
            int mend = r2.Ev(SimEventType.HealLanded).Count;
            Line("S26 human Healer, ally hurt at 4 u, aim on an enemy at 1.2 u: lantern Hit / mends", hit + " / " + mend, "1 / 0");
            Assert.AreEqual(1, hit);
            Assert.AreEqual(0, mend);
        }

        // ---------------------------------------------------------------- 6. the Miner digs again

        [Test]
        public void AHumanMinerDigsAwayFromAVeinWithTheBuildersRuleAndABotMinerNeverDoes()
        {
            var r = new Rig(ClassId.Miner);
            r.Aim(58.0, 40.5);
            r.Tap((f, on) => f.Terraform = on, 2);
            List<SimEvent> tf = r.Ev(SimEventType.Terraform);
            int dug = 0;
            foreach (SimEvent e in tf) if (e.A == 0 && (int)e.Value == Terraform.Dug) dug++;
            Line("human Miner F away from a vein: Terraform Dug events (values " + string.Join(",", tf.ConvertAll(e => e.Value.ToString())) + ")", dug, "1");
            Assert.AreEqual(1, dug);
            Assert.AreEqual(0, r.W.Terraform.Carried[0], "the Miner carries no spoil (no PLACE)");

            var b = new Rig(ClassId.Miner);
            b.W.Actors[0].IsHuman = false;
            b.Aim(58.0, 40.5);
            b.Tap((f, on) => f.Terraform = on, 2);
            int botDug = b.Ev(SimEventType.Terraform).Count;
            int refused = b.Count(SimEventType.OreMined, e => e.A == 0 && (int)e.Value == Blacksmith.RefusedNoVein);
            Line("bot Miner F away from a vein: Terraform events / OreMined NoVein refusal", botDug + " / " + refused, "0 / 1");
            Assert.AreEqual(0, botDug);
            Assert.AreEqual(1, refused);
        }

        // ---------------------------------------------------------------- 7. HAUL raises its cast events

        [Test]
        public void HaulRaisesItsAbilityAndAttackStartEvents()
        {
            var r = new Rig(ClassId.Miner);
            r.Tap((f, on) => f.Ability = on, 1);
            int ab = r.Count(SimEventType.Ability, e => e.A == 0 && (int)e.Value == 1);
            int st = r.Count(SimEventType.AttackStart, e => e.A == 0 && e.B == Blacksmith.KindHaul);
            Line("Miner HAUL press: Ability / AttackStart(KindHaul) events; HAUL ticks", ab + " / " + st + "; " + r.W.Hut.HaulLeft[0], "1 / 1; > 0");
            Assert.AreEqual(1, ab);
            Assert.AreEqual(1, st);
            Assert.Greater(r.W.Hut.HaulLeft[0], 0);
        }

        // ---------------------------------------------------------------- 8. the idle rule sees aim

        [Test]
        public void AnAimingPlayerIsNeverAutopilotedAndTheTakeoverAndHandBackAreEvents()
        {
            var r = new Rig(ClassId.Healer);
            World w = r.W;
            int ticks = (int)Math.Round((w.Config.Depth.Mech.AfkSeconds + 10) * w.Config.SimHz);
            int auto = 0;
            for (int t = 0; t < ticks; t++)
            {
                double a = t * 0.01; // the cursor sweeps slowly around her (0.6 deg a tick)
                r.Aim(w.X[0] + 4 * Math.Cos(a), w.Z[0] + 4 * Math.Sin(a));
                r.Step(1);
                if (MechRules.Autopiloted(w, 0)) auto++;
            }
            Line("S30 human aiming only for AfkSeconds + 10 s: ticks on autopilot", auto, "0");
            Assert.AreEqual(0, auto);

            var i = new Rig(ClassId.Warrior);
            int at = (int)Math.Round(i.W.Config.Depth.Mech.AfkSeconds * i.W.Config.SimHz);
            i.Aim(60, 40.5);
            i.Step(at + 5);
            bool on = MechRules.Autopiloted(i.W, 0);
            int onEv = i.Count(SimEventType.AutopilotChanged, e => e.A == 0 && (int)e.Value == 1);
            i.Me.MoveX = 1;
            i.Step(1);
            bool back = !MechRules.Autopiloted(i.W, 0);
            int offEv = i.Count(SimEventType.AutopilotChanged, e => e.A == 0 && (int)e.Value == 0);
            Line("idle " + i.W.Config.Depth.Mech.AfkSeconds + " s: autopilot on / on event; first move: back / off event", on + " / " + onEv + "; " + back + " / " + offEv, "True / 1; True / 1");
            Assert.IsTrue(on);
            Assert.AreEqual(1, onEv);
            Assert.IsTrue(back, "the first frame with input hands it back on that tick");
            Assert.AreEqual(1, offEv);
        }

        // ---------------------------------------------------------------- 9. the Farmer's glancing hit is visible

        [Test]
        public void TheFarmersCloseRangeHitCarriesAGlancingTag()
        {
            var r = new Rig(ClassId.Farmer);
            int foe = r.Foe();
            r.Place(foe, ClassId.Builder, 57.4, 40.5);
            r.Head = r.W.EventHead;
            r.Aim(57.4, 40.5);
            r.Tap(Attack, 11);
            List<SimEvent> hits = r.Ev(SimEventType.Hit);
            List<SimEvent> gl = r.Ev(SimEventType.HitBlocked);
            double dealt = hits.Count > 0 ? hits[0].Value : 0;
            bool ok = hits.Count == 1 && gl.Count == 1 && (int)gl[0].Value == PlayerReasons.BlockedGlancing;
            double share = r.W.Config.Depth.CombatFeel.FarmerCloseShare;
            double lost = gl.Count > 0 ? gl[0].Radius : 0;
            Line("Farmer staff at 0.9 u: Hit / HitBlocked Glancing / dealt + lost = full", hits.Count + " / " + gl.Count + " / " + (dealt + lost).ToString("0.00") + " = " + (dealt / share).ToString("0.00"), "1 / 1 / equal");
            Assert.IsTrue(ok);
            Assert.AreEqual(dealt / share, dealt + lost, 1e-9);
        }

        // ---------------------------------------------------------------- the pins' profiles never see the lane

        [Test]
        public void ClassicAndPhysicsOnlyMatchesRaiseNoLaneEvent()
        {
            foreach (RulesProfile p in new[] { RulesProfile.Classic, RulesProfile.PhysicsOnly })
            {
                GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(p);
                World w = World.CreateWorld(new WorldOptions { Seed = 7, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Miner, Config = cfg });
                int n = 0;
                DepthKit.RunBots(w, 1800, e =>
                {
                    if (e.Type == SimEventType.HitBlocked || e.Type == SimEventType.StructureHit || e.Type == SimEventType.ActionRefused
                        || e.Type == SimEventType.WorldAction || e.Type == SimEventType.AutopilotChanged) n++;
                });
                Line(p + " 5v5 1:00 bot match: lane S2 events", n, "0");
                Assert.AreEqual(0, n);
            }
        }
    }
}
