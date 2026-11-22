using System;
using System.Collections.Generic;
using System.Linq;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using NUnit.Framework;
using T = CriticalMass.Sim.Tests.PlayerSeat.PlayerSeatTargets;

namespace CriticalMass.Sim.Tests.PlayerSeat
{
    /// <summary>
    /// Player-seat gate, tier S, the press itself (player-agency audit 6.4: S1, S2, S4, S7, S8, S16, S18, S25, S26, S30; the
    /// lead's S33, S34, S46, S47). Every press goes through a HumanModel frame and MatchTick.AdvanceHumans, as a player's does.
    /// </summary>
    [TestFixture, Category("PlayerSeat")]
    public sealed class PlayerSeatSimTests
    {
        static GameConfig Cfg => PlayerSeatRig.Config();
        static double Range(ClassId x) => Cfg.Class(x).AttackRange;

        /// <summary>Me and a passive dummy of class them, dist apart across the hill centre; the model aims at the dummy but presses nothing by itself.</summary>
        static Seat Pair(ClassId me, ClassId them, double dist)
        {
            Seat s = PlayerSeatRig.Duel(me, them, TargetMode.Passive, 0, 0, distance: dist);
            s.Human.Fight = false;
            return s;
        }

        static int Windup(Seat s, ClassId x) => s.W.Dur.AttackWindup[(int)x];

        // ------------------------------------------------------------------ S1

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S01_Primary_PassiveTarget_DamageWithinWindup(ClassId x)
        {
            Seat s = Pair(x, ClassId.Farmer, 0.8 * Range(x));
            s.NoTnt = true; // the primary SWING (S2 / S18 hold the TNT press)
            int wu = Windup(s, x);
            int mark = s.Spy.Mark;
            s.Human.PressAttack = true;
            s.Steps(wu + 2);
            List<SeatEvent> hits = s.Spy.Hits(s.Me, s.Them, mark);
            double want = s.W.Config.Class(x).AttackDamage;
            List<SeatEvent> kn = s.Spy.Knocks(s.Them, mark);
            double knockWant = s.W.Config.Depth.CombatFeel.Strikes[(int)x].Knock / s.W.Config.Class(ClassId.Farmer).Mass;
            bool dmgOk = hits.Count == 1 && Math.Abs(hits[0].Value - want) <= T.DamageTolerance * want;
            bool knockOk = kn.Count > 0 && kn[0].Impulse >= T.KnockShareMin * knockWant;
            bool ok = PxLog.Line("S01 Primary_PassiveTarget_DamageWithinWindup", x.ToString(),
                "hits " + hits.Count + (hits.Count > 0 ? " dmg " + PxLog.F(hits[0].Value, "0.0") + " at +" + (hits[0].Tick - (s.W.Tick - wu - 2)) + " ticks" : "") + ", knock " + (kn.Count > 0 ? PxLog.F(kn[0].Impulse) : "none"),
                "1 hit of " + PxLog.F(want, "0.0") + " within " + (wu + 1) + " ticks, knock >= " + PxLog.F(T.KnockShareMin * knockWant), dmgOk && knockOk);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S2

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S02_Primary_InFace_DealsDamage(ClassId x)
        {
            Seat s = Pair(x, ClassId.Farmer, T.InFaceDistance);
            if (x == ClassId.Healer)
            {
                // a hurt ally 3 u behind her: the press must still hit the enemy in her face
                PlayerSeatRig.Place(s.W, 1, ClassId.Warrior, s.W.X[0] - 3, s.W.Z[0]);
                s.W.Actors[1].Hp = s.W.Actors[1].MaxHp * 0.5;
            }
            int wu = Windup(s, x);
            int mark = s.Spy.Mark;
            s.Human.PressAttack = true;
            s.Steps(wu + 2);
            List<SeatEvent> hits = s.Spy.Hits(s.Me, s.Them, mark);
            double want = s.W.Config.Class(x).AttackDamage;
            double got = hits.Sum(h => h.Value);
            bool ok = PxLog.Line("S02 Primary_InFace_DealsDamage", x + " at " + T.InFaceDistance + " u" + (x == ClassId.Demolitions ? " (TNT ready)" : x == ClassId.Healer ? " (hurt ally 3 u)" : ""),
                "dealt " + PxLog.F(got, "0.0") + " within " + (wu + 1) + " ticks" + (s.Spy.Since(mark, SimEventType.TntThrown, e => e.A == 0).Count > 0 ? " (the press threw TNT)" : "") + (s.Spy.Since(mark, SimEventType.HealCast, e => e.A == 0).Count > 0 ? " (the press mended)" : ""),
                ">= " + PxLog.F(T.InFaceDamageShareMin * want, "0.0"), got >= T.InFaceDamageShareMin * want);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S4

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S04_Kill_PassiveTarget_WithinTtk(ClassId x)
        {
            Seat s = PlayerSeatRig.Duel(x, ClassId.Farmer, TargetMode.Passive, 0, 0, distance: 0.8 * Range(x));
            s.NoTnt = true;
            World w = s.W;
            ClassConfig cc = w.Config.Class(x);
            double hz = w.Config.SimHz;
            int hits = (int)Math.Ceiling(w.Config.Class(ClassId.Farmer).MaxHp / cc.AttackDamage);
            double cd = Math.Ceiling(cc.AttackCooldown * hz - 1e-9) / hz;
            double cycle = Math.Max(cd, (w.Dur.AttackWindup[(int)x] + w.Dur.Recovery[(int)x]) / hz);
            double ttk = w.Dur.AttackWindup[(int)x] / hz + (hits - 1) * cycle;
            DuelResult r = PlayerSeatRig.Play(s);
            bool credited = w.Actors[0].Kills == 1 && s.Spy.Deaths(0).Count == 1;
            bool ok = PxLog.Line("S04 Kill_PassiveTarget_WithinTtk", x + " on Farmer", "killed " + r.Won + " in " + PxLog.F(r.Seconds) + " s, credited " + credited, "<= " + PxLog.F(ttk + T.TtkSlackSeconds) + " s (" + hits + " hits)", r.Won && credited && r.Seconds <= ttk + T.TtkSlackSeconds);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S7

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S07_Ability_PressProducesCastEvent(ClassId x)
        {
            Seat s = PlayerSeatRig.Field(x);
            s.Steps(2);
            s.W.Actors[0].AbilityCd = 0;
            int mark = s.Spy.Mark;
            s.Human.AimOverrideX = s.W.X[0] + 3;
            s.Human.AimOverrideZ = s.W.Z[0];
            s.Human.PressAbility = true;
            s.Steps(1 + T.PressAnswerTicks);
            var cast = s.Spy.Since(mark, SimEventType.AttackStart, e => e.A == 0 && e.B != Combat.KindAttack).Concat(s.Spy.Since(mark, SimEventType.Ability, e => e.A == 0)).ToList();
            bool ok = PxLog.Line("S07 Ability_PressProducesCastEvent", x.ToString(), cast.Count > 0 ? string.Join(" ", cast) : "no cast event (AbilityCd " + PxLog.F(s.W.Actors[0].AbilityCd) + ")", "an AttackStart (ability kind) or Ability event", cast.Count > 0);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S8

        static readonly string[] BlockCases = { "spawn", "dodge", "ward", "wall" };

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S08_BlockedHit_IsReported(ClassId x)
        {
            int bad = 0;
            foreach (string why in BlockCases)
            {
                double reach = Range(x) + Cfg.Class(ClassId.Farmer).Radius;
                double dist = why == "wall" ? 1.9 : 0.8 * Range(x);
                if (why == "wall" && dist > reach - 0.03)
                {
                    PxLog.Line("S08 BlockedHit_IsReported", x + " " + why, "not staged: a one-cell wall needs 1.9 u between centres, past the reach " + PxLog.F(reach), "n/a", true);
                    continue;
                }
                Seat s = Pair(x, ClassId.Farmer, dist);
                s.NoTnt = true;
                World w = s.W;
                int code = 0;
                if (why == "spawn")
                {
                    PadDef pad = w.Map.Pads[(int)TeamId.Crimson];
                    PlayerSeatRig.Place(w, s.Them, ClassId.Farmer, pad.X, pad.Z);
                    PlayerSeatRig.Place(w, 0, x, pad.X - dist, pad.Z);
                    s.BeforeTick = q => q.W.Actors[q.Them].SpawnProtect = 3;
                    code = PlayerReasons.BlockedSpawnProtect;
                }
                else if (why == "ward")
                {
                    w.Depth.PowerKind[s.Them] = (byte)PickupKind.Ward;
                    w.Depth.WardLeft[s.Them] = 1000;
                    w.Depth.PowerEndTick[s.Them] = int.MaxValue / 2;
                    code = PlayerReasons.BlockedWard;
                }
                else if (why == "wall")
                {
                    int cx = (int)Math.Floor(w.Map.Hill.X), cz = (int)Math.Floor(w.Map.Hill.Z);
                    MapGrid.SetCell(w, cx, cz, Cell.Wall);
                    PlayerSeatRig.Place(w, 0, x, cx + 0.5 - dist / 2, cz + 0.5);
                    PlayerSeatRig.Place(w, s.Them, ClassId.Farmer, cx + 0.5 + dist / 2, cz + 0.5);
                    code = PlayerReasons.BlockedWall;
                }
                else code = PlayerReasons.BlockedDodge;
                int wu = Windup(s, x);
                int mark = s.Spy.Mark;
                double dContact = double.NaN;
                s.Human.PressAttack = true;
                for (int t = 0; t < wu + 2; t++)
                {
                    if (why == "dodge" && t == wu - 1)
                    {
                        // the dummy dodges TOWARD the attacker one tick before contact: it stays in reach, inside its i-frames
                        double dx = w.X[0] - w.X[s.Them], dz = w.Z[0] - w.Z[s.Them], d = Math.Sqrt(dx * dx + dz * dz);
                        s.Dummy.PressDodge = true;
                        s.Dummy.MoveX = dx / d;
                        s.Dummy.MoveZ = dz / d;
                    }
                    s.Step();
                    s.Dummy.MoveX = s.Dummy.MoveZ = 0;
                    if (double.IsNaN(dContact) && s.Spy.Contacts(0, mark).Count > 0) dContact = Math.Sqrt((w.X[0] - w.X[s.Them]) * (w.X[0] - w.X[s.Them]) + (w.Z[0] - w.Z[s.Them]) * (w.Z[0] - w.Z[s.Them]));
                }
                int landed = s.Spy.Hits(0, s.Them, mark).Count;
                int contacts = s.Spy.Contacts(0, mark).Count;
                var blocked = s.Spy.Since(mark, SimEventType.HitBlocked, e => e.A == s.Them && e.B == 0);
                bool staged = landed == 0 && contacts == 1 && dContact <= reach && (why != "dodge" || s.Spy.Since(mark, SimEventType.Dodge, e => e.A == s.Them).Count > 0);
                bool pass = staged && blocked.Count == 1 && (int)blocked[0].Value == code;
                PxLog.Line("S08 BlockedHit_IsReported", x + " " + why, "HitBlocked " + (blocked.Count > 0 ? "value " + blocked[0].Value : "none") + " (staging: contact " + contacts + " at " + PxLog.F(dContact) + " u of reach " + PxLog.F(reach) + ", hits " + landed + (why == "ward" ? ", WardAbsorb " + s.Spy.Since(mark, SimEventType.WardAbsorb).Count : "") + ")", "HitBlocked reason " + code + ", no hit", pass);
                if (!pass) bad++;
            }
            Assert.AreEqual(0, bad, x + ": blocked hits not reported");
        }

        // ------------------------------------------------------------------ S16 / S18 / S25 / S26

        [Test]
        public void S16_Miner_F_AwayFromVein_DoesWhatTheBindingSays()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Miner, x: 30, z: 40);
            s.Steps(2);
            int mark = s.Spy.Mark;
            s.Human.AimOverrideX = s.W.X[0] + 1.5;
            s.Human.AimOverrideZ = s.W.Z[0];
            s.Human.PressDigPlace = true;
            s.Steps(3);
            var dug = s.Spy.Since(mark, SimEventType.Terraform, e => e.A == 0 && e.Value > 0);
            var ore = s.Spy.Since(mark, SimEventType.OreMined, e => e.A == 0);
            bool ok = PxLog.Line("S16 Miner_F_AwayFromVein_DoesWhatTheBindingSays", "Miner F on open ground", dug.Count > 0 ? "dug" : "no dig; OreMined " + (ore.Count > 0 ? "refusal code " + ore[0].Value : "none"), "a dig (the binding row says Dig / place; F18 checks the words)", dug.Count > 0);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S18_Demolitions_PointBlank_DealsDamageFast()
        {
            Seat s = Pair(ClassId.Demolitions, ClassId.Farmer, 1.0);
            int mark = s.Spy.Mark;
            s.Human.PressAttack = true;
            int limit = s.Ticks(0.3);
            s.Steps(limit);
            var hits = s.Spy.Hits(0, s.Them, mark);
            bool ok = PxLog.Line("S18 Demolitions_PointBlank_DealsDamageFast", "enemy 1.0 u, TNT ready", hits.Count > 0 ? "hit " + PxLog.F(hits[0].Value, "0") : "no hit (TNT thrown " + s.Spy.Since(mark, SimEventType.TntThrown).Count + ")", "a hit within 0.3 s (" + limit + " ticks)", hits.Count > 0);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S25_Healer_Mend_TargetsMostHurtWhenNotAiming()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Healer);
            World w = s.W;
            PlayerSeatRig.Place(w, 1, ClassId.Warrior, w.X[0] + 2, w.Z[0] + 1);
            PlayerSeatRig.Place(w, 2, ClassId.Farmer, w.X[0] - 2, w.Z[0] + 1);
            w.Actors[1].Hp = 0.9 * w.Actors[1].MaxHp;
            w.Actors[2].Hp = 0.3 * w.Actors[2].MaxHp;
            int mark = s.Spy.Mark;
            s.Human.AimOverrideX = w.X[0] + 2.5; // aim nearer the 90% ally's side, on neither body
            s.Human.AimOverrideZ = w.Z[0] - 3;
            s.Human.PressAttack = true;
            s.Steps(3);
            var heal = s.Spy.Since(mark, SimEventType.HealLanded, e => e.B == 0);
            bool ok = PxLog.Line("S25 Healer_Mend_TargetsMostHurtWhenNotAiming", "allies 90% and 30%, aim on neither", heal.Count > 0 ? "mended actor " + heal[0].A + (heal[0].A == 2 ? " (the 30% one)" : " (the 90% one)") : "no mend", "the 30% ally (actor 2)", heal.Count > 0 && heal[0].A == 2);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S26_Healer_AimAtEnemy_SwingsLantern()
        {
            Seat s = Pair(ClassId.Healer, ClassId.Farmer, 1.2);
            World w = s.W;
            PlayerSeatRig.Place(w, 1, ClassId.Warrior, w.X[0] - 4, w.Z[0]);
            w.Actors[1].Hp = 0.5 * w.Actors[1].MaxHp;
            int mark = s.Spy.Mark;
            s.Human.PressAttack = true;
            s.Steps(Windup(s, ClassId.Healer) + 2);
            var hits = s.Spy.Hits(0, s.Them, mark);
            bool ok = PxLog.Line("S26 Healer_AimAtEnemy_SwingsLantern", "hurt ally 4 u behind, aim on an enemy 1.2 u", hits.Count > 0 ? "lantern hit " + PxLog.F(hits[0].Value, "0") : "no hit (mends " + s.Spy.Since(mark, SimEventType.HealCast, e => e.A == 0).Count + ")", "a lantern hit", hits.Count > 0);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S30 / S47 (the idle rule)

        static Seat IdleSeat()
        {
            Seat s = PlayerSeatRig.Field(ClassId.Warrior);
            s.AllowAutopilot = true;
            return s;
        }

        [Test]
        public void S30_Idle_AimingPlayer_NotAutopiloted()
        {
            Seat s = IdleSeat();
            double cx = s.W.X[0], cz = s.W.Z[0];
            s.BeforeTick = q =>
            {
                // a mouse player waiting with the cursor: the aim point wanders 2 to 6 u around the fighter
                double a = q.W.Tick * 0.07, r = 4 + 2 * Math.Sin(q.W.Tick * 0.05);
                q.Human.AimOverrideX = cx + Math.Cos(a) * r;
                q.Human.AimOverrideZ = cz + Math.Sin(a) * r;
            };
            s.Steps(s.Ticks(T.AimOnlySeconds));
            bool ok = PxLog.Line("S30 Idle_AimingPlayer_NotAutopiloted", "mouse aim only, " + T.AimOnlySeconds + " s", "autopiloted " + s.AutopilotTicks + " ticks (phase " + s.W.Match.Phase + ")", "0 ticks", s.AutopilotTicks == 0);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S47_Autopilot_NeverTakesAnAimingPlayer()
        {
            Seat s = IdleSeat();
            s.BeforeTick = q =>
            {
                // third-person style: aim = body + 4 u along the camera yaw, the yaw turning 3 degrees a tick back and forth
                double yaw = Math.Sin(q.W.Tick * 0.02) * Math.PI;
                q.Human.AimOverrideX = q.W.X[0] + Math.Cos(yaw) * 4;
                q.Human.AimOverrideZ = q.W.Z[0] + Math.Sin(yaw) * 4;
            };
            s.Steps(s.Ticks(T.AimOnlySeconds));
            bool ok = PxLog.Line("S47 Autopilot_NeverTakesAnAimingPlayer", "third-person yaw aim only, " + T.AimOnlySeconds + " s", "autopiloted " + s.AutopilotTicks + " ticks", "0 ticks", s.AutopilotTicks == 0);
            Assert.IsTrue(ok);
        }

        [Test]
        public void S47_Autopilot_HandsBackOnFirstInput_AndIsAnnounced()
        {
            Seat s = IdleSeat();
            World w = s.W;
            int limit = s.Ticks(w.Config.Depth.Mech.AfkSeconds + 2);
            int takeTick = -1, mark = s.Spy.Mark;
            for (int t = 0; t < limit && takeTick < 0; t++)
            {
                s.Step();
                if (MechRules.Autopiloted(w, 0)) takeTick = w.Tick;
            }
            // an announcement = any event about actor 0 on the takeover tick (no such event type exists today)
            var said = takeTick < 0 ? new List<SeatEvent>() : s.Spy.Since(mark, SimEventType.ActionRefused, e => e.A == 0 && e.Tick == takeTick).Concat(s.Spy.Since(mark, SimEventType.WorldAction, e => e.A == 0 && e.Tick == takeTick)).ToList();
            s.Human.MoveOverrideX = 1;
            s.Human.MoveOverrideZ = 0;
            s.Step();
            bool back = !MechRules.Autopiloted(w, 0);
            bool okTake = PxLog.Line("S47 Autopilot_HandsBackOnFirstInput_AndIsAnnounced", "takeover after " + w.Config.Depth.Mech.AfkSeconds + " s idle", takeTick < 0 ? "no takeover" : "took over at tick " + takeTick + ", announced by an event: " + (said.Count > 0), "a takeover event the HUD can show", takeTick >= 0 && said.Count > 0);
            bool okBack = PxLog.Line("S47 Autopilot_HandsBackOnFirstInput_AndIsAnnounced", "hand back", "back at the controls on the first input frame: " + back, "true", back);
            Assert.IsTrue(okTake && okBack);
        }

        // ------------------------------------------------------------------ S33: no silent press

        static readonly SimEventType[] NotAnAnswer =
        {
            SimEventType.Hit, SimEventType.Knock, SimEventType.HitStun, SimEventType.Heal, SimEventType.Death, SimEventType.Respawn,
            SimEventType.PickupTaken, SimEventType.PowerUpEnded, SimEventType.Fall, SimEventType.HealLanded, SimEventType.WardAbsorb,
            SimEventType.HitBlocked, SimEventType.MeatPop, SimEventType.HillOwnerChange, SimEventType.MatchEnd,
        };

        static readonly string[] Buttons = { "Attack", "Ability", "DigPlace", "Interact", "Dodge", "Cancel" };

        /// <summary>One press of button in scene; returns the answering events (A = me, within PressAnswerTicks of the press tick).</summary>
        static List<SeatEvent> PressOnce(ClassId x, string button, string scene, out string where)
        {
            Seat s = PlayerSeatRig.Field(x, x: 40, z: 40);
            World w = s.W;
            where = "open field";
            int enemy = PlayerSeatRig.EnemySlot(w);
            if (scene != "open")
            {
                double px = w.X[0], pz = w.Z[0];
                if (button == "DigPlace" && x == ClassId.Miner)
                {
                    foreach (SourceNode sn in w.Depth.Sources) if (sn != null && sn.Kind == BiomassType.Ore && sn.Charges > 0) { px = sn.X - 1.0; pz = sn.Z; where = "1 u from an ore vein"; break; }
                }
                else if (button == "DigPlace" && x == ClassId.Healer) { px = w.Map.Shrine.X; pz = w.Map.Shrine.Z + 3; where = "3 u from the shrine"; }
                else if (button == "Interact" && w.Nodes.Length > 0) { px = w.Nodes[0].X + 0.5; pz = w.Nodes[0].Z; where = "on a plant node"; }
                else where = "enemy 1.2 u in front";
                PlayerSeatRig.Place(w, 0, x, px, pz);
                PlayerSeatRig.Place(w, enemy, ClassId.Farmer, px + 1.2, pz);
                s.Them = enemy;
                s.Dummy = new DummyModel { Me = enemy, Around = 0 };
            }
            s.NoTnt = button == "Cancel" && scene == "valid"; // her press must wind up (the pry bar) for a cancel to have something to drop
            s.Human.Target = -1;
            s.Steps(2);
            double ax = scene != "open" ? w.X[0] + 1.2 : w.X[0] + 2, az = w.Z[0];
            if (where.StartsWith("1 u from an ore")) ax = w.X[0] + 1.0;
            Action<HumanModel> press = h =>
            {
                h.AimOverrideX = ax;
                h.AimOverrideZ = az;
                if (button == "Attack") h.PressAttack = true;
                else if (button == "Ability") h.PressAbility = true;
                else if (button == "DigPlace") h.PressDigPlace = true;
                else if (button == "Interact") h.HoldInteract = true;
                else if (button == "Dodge") h.PressDodge = true;
                else if (button == "Cancel") h.PressCancel = true;
            };
            if (scene == "cooldown" || (button == "Cancel" && scene == "valid"))
            {
                // the same button (Cancel: an attack) once first; the measured press comes 3 ticks later
                if (button == "Cancel") { s.Human.AimOverrideX = ax; s.Human.AimOverrideZ = az; s.Human.PressAttack = true; where += ", inside its own wind-up"; }
                else { press(s.Human); where += ", 3 ticks after a first press (cooling down)"; }
                s.Steps(3);
            }
            int mark = s.Spy.Mark;
            int pressTick = w.Tick + 1;
            press(s.Human);
            s.Step();
            for (int t = 0; t < T.PressAnswerTicks; t++)
            {
                if (button == "Interact") s.Human.HoldInteract = true;
                s.Human.AimOverrideX = ax;
                s.Human.AimOverrideZ = az;
                s.Step();
            }
            return s.Spy.AboutActor(0, mark).Where(e => Array.IndexOf(NotAnAnswer, e.Type) < 0 && e.Tick >= pressTick && e.Tick <= pressTick + T.PressAnswerTicks).ToList();
        }

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S33_Press_NeverSilent(ClassId x)
        {
            var silent = new List<string>();
            foreach (string b in Buttons)
                foreach (string scene in new[] { "open", "valid", "cooldown" })
                {
                    if (scene == "cooldown" && b != "Attack" && b != "Ability" && b != "Dodge") continue;
                    List<SeatEvent> said = PressOnce(x, b, scene, out string where);
                    bool ok = said.Count > 0;
                    PxLog.Line("S33 Press_NeverSilent", x + " " + b + " (" + where + ")", ok ? string.Join(" ", said.Select(e => e.Type + (e.Type == SimEventType.ActionRefused || e.Value < 0 ? "[" + e.Value + "]" : ""))) : "SILENT", "an action or refusal event within " + T.PressAnswerTicks + " ticks", ok);
                    if (!ok) silent.Add(b + " (" + where + ")");
                }
            Assert.IsEmpty(silent, x + " silent presses: " + string.Join("; ", silent));
        }

        // ------------------------------------------------------------------ S34

        [Test]
        public void S34_StaggerCancel_IsReported()
        {
            // a human Miner (11-tick wind-up) and a Farmer dummy (9-tick) press on the same tick 1.6 u apart: the Farmer's
            // contact staggers the Miner before his own contact
            Seat s = Pair(ClassId.Miner, ClassId.Farmer, 1.6);
            int mark = s.Spy.Mark;
            s.Human.PressAttack = true;
            s.Dummy.PressAttack = true;
            s.Steps(Windup(s, ClassId.Miner) + 2);
            var cancel = s.Spy.Since(mark, SimEventType.AttackCancel, e => e.A == 0);
            int myContacts = s.Spy.Contacts(0, mark).Count, theirHits = s.Spy.Hits(s.Them, 0, mark).Count;
            bool ok = PxLog.Line("S34 StaggerCancel_IsReported", "Miner wind-up cut by a Farmer hit", cancel.Count > 0 ? "AttackCancel B " + cancel[0].B : "no AttackCancel (their hits " + theirHits + ", my contacts " + myContacts + ")", "AttackCancel A 0, B " + s.Them, cancel.Count == 1 && cancel[0].B == s.Them && myContacts == 0);
            Assert.IsTrue(ok);
        }

        // ------------------------------------------------------------------ S46

        [TestCase(ClassId.Warrior)]
        [TestCase(ClassId.Farmer)]
        [TestCase(ClassId.Miner)]
        [TestCase(ClassId.Demolitions)]
        [TestCase(ClassId.Builder)]
        [TestCase(ClassId.Healer)]
        public void S46_TapDuringRecovery_IsBuffered(ClassId x)
        {
            Seat probe = Pair(x, ClassId.Farmer, 0.8 * Range(x));
            probe.NoTnt = true;
            probe.Human.PressAttack = true;
            probe.Step();
            int ready = 0;
            while (!Combat.AttackReady(probe.W, 0) && ready < 200) { probe.Step(); ready++; }
            int window = (int)Math.Ceiling(0.35 * probe.Hz);
            var lost = new List<int>();
            for (int k = 1; k <= window; k++)
            {
                Seat s = Pair(x, ClassId.Farmer, 0.8 * Range(x));
                s.NoTnt = true;
                int mark = s.Spy.Mark;
                s.Human.PressAttack = true;
                s.Step();
                s.Steps(ready - k);
                s.Human.PressAttack = true; // one 1-tick tap, k ticks before the swing could start
                s.Step();
                s.Steps(k + 20);
                int swings = s.Spy.Since(mark, SimEventType.AttackStart, e => e.A == 0 && e.B == Combat.KindAttack).Count;
                if (swings != 2) lost.Add(k);
            }
            bool ok = PxLog.Line("S46 TapDuringRecovery_IsBuffered", x.ToString(), lost.Count == 0 ? "every tap kept" : "taps lost at " + string.Join(",", lost) + " ticks early (of 1.." + window + ")", "every tap in the last 0.35 s gives exactly one swing", lost.Count == 0);
            Assert.IsTrue(ok);
        }
    }
}
