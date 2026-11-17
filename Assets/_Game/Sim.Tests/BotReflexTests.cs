using System;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using NUnit.Framework;

namespace CriticalMass.Sim.Tests
{
    /// <summary>
    /// CM b13 item J: BotReflex on staged threats (pure calls of BotReflex.Fill on a cleared field). Each threat at Easy,
    /// Normal and Hard over 200 trials (a fresh decision each, a different tick so a different hash): the dodge share is
    /// printed against DodgeConfig.DodgeChance and must sit within 0.12; the reaction delay is respected; a bot jumps a
    /// short wall its steering runs into, at most once per JumpEveryTicks. Every case prints one line.
    /// </summary>
    [TestFixture]
    public sealed class BotReflexTests
    {
        private const double X0 = 56.5, Z0 = 40.5;
        private static readonly BotDifficulty[] Diffs = { BotDifficulty.Easy, BotDifficulty.Normal, BotDifficulty.Hard };

        private static World Field(BotDifficulty d, out int bot, out int foe)
        {
            World w = World.CreateWorld(new WorldOptions { Seed = 77, TeamSize = 5, HumanTeam = TeamId.Azure, HumanClass = ClassId.Warrior, BotDifficulty = d, Config = GameConfig.CreateDefault() });
            for (int j = 0; j < w.ActorCount; j++) { w.Actors[j].Alive = false; w.Actors[j].RespawnIn = 1e9; }
            bot = w.ActorCount / 2 + 1;   // a Crimson bot (BotDifficulty)
            foe = 1;                      // an Azure fighter
            foreach (int j in new[] { bot, foe })
            {
                Actor a = w.Actors[j];
                a.Alive = true; a.Hp = a.MaxHp; a.StandUp = 0; a.AirLen = 0; a.HitStunTicks = 0; a.KVx = 0; a.KVz = 0; a.SwingPending = false; a.Carry = CarryKind.None;
            }
            for (int cz = (int)Z0 - 4; cz <= (int)Z0 + 4; cz++)
                for (int cx = (int)X0 - 5; cx <= (int)X0 + 5; cx++) w.Map.Cells[cz * w.Map.Width + cx] = Cell.Floor;
            w.X[bot] = X0; w.Z[bot] = Z0;
            w.X[foe] = X0 + 1.2; w.Z[foe] = Z0;
            w.Actors[bot].Facing = 0;
            Assert.AreEqual(TeamId.Crimson, w.Actors[bot].Team);
            Assert.AreEqual(d, BotSkill.Of(w, bot));
            return w;
        }

        /// <summary>Stages threat kind at World.Tick tick, with the threat aged age ticks.</summary>
        private static void Stage(World w, int kind, int bot, int foe, int age)
        {
            Kaiju k = w.Kaiju;
            k.State = KaijuState.Egg;
            k.TelegraphIn = 0;
            k.MeleeWindupIn = 0;
            k.MeleeTargetId = -1;
            foreach (Bomb b in w.Demolitions.Bombs) if (b != null) b.Active = false;
            w.Actors[foe].SwingPending = false;
            GameConfig c = w.Config;
            if (kind == BotReflex.KindSlam)
            {
                k.State = KaijuState.Neutral;
                k.X = X0 + 3; k.Z = Z0;
                int full = (int)JsMath.Round(c.Kaiju.SlamTelegraphSeconds * c.SimHz);
                if (w.Dur.SlamTelegraph > full) full = w.Dur.SlamTelegraph;
                int left = Math.Min(10, full - age);
                k.TelegraphIn = left / c.SimHz;
                k.TelegraphX = X0 + 1; k.TelegraphZ = Z0;
            }
            else if (kind == BotReflex.KindTnt)
            {
                if (w.Demolitions.Bombs[0] == null) w.Demolitions.Bombs[0] = new Bomb();
                Bomb b = w.Demolitions.Bombs[0];
                b.Active = true; b.Landed = true; b.Team = TeamId.Azure; b.Owner = foe;
                b.X = X0 + 0.8; b.Z = Z0;
                b.LandTick = w.Tick - age; b.BlastTick = w.Tick + 12;
            }
            else if (kind == BotReflex.KindMelee)
            {
                Actor f = w.Actors[foe];
                f.SwingPending = true; f.SwingTick = w.Tick - age; f.SwingWindup = 30; f.SwingFacing = Math.PI; // facing the bot (-x)
            }
        }

        private static void Fresh(World w, int bot)
        {
            DodgeState s = w.Dodges;
            s.SeenKey[bot] = 0; s.Decided[bot] = 0; s.Ready[bot] = 0; s.Start[bot] = -1; s.LastJump[bot] = -1000000;
        }

        [Test]
        public void EachThreatDodgesAtItsChancePerDifficulty()
        {
            int[] kinds = { BotReflex.KindSlam, BotReflex.KindTnt, BotReflex.KindMelee, BotReflex.KindClaw };
            string[] names = { "slam", "tnt", "melee", "claw" };
            var failures = new System.Collections.Generic.List<string>();
            foreach (BotDifficulty d in Diffs)
            {
                World w = Field(d, out int bot, out int foe);
                DodgeConfig dc = w.Config.Depth.Dodge;
                int di = BotReflex.DiffIndex(w, bot);
                for (int n = 0; n < kinds.Length; n++)
                {
                    int dodged = 0;
                    for (int trial = 0; trial < 200; trial++)
                    {
                        w.Tick = 1000 + trial * 97;
                        Fresh(w, bot);
                        InputFrame o = SimInput.EmptyInput();
                        if (kinds[n] == BotReflex.KindClaw)
                        {
                            Stage(w, 0, bot, foe, 0);
                            // its age is how long this bot has watched it: feed the wind-up tick by tick past the delay
                            w.Kaiju.State = KaijuState.Neutral; w.Kaiju.X = X0 + 2; w.Kaiju.Z = Z0; w.Kaiju.MeleeTargetId = bot;
                            for (int s = 0; s <= dc.ReactionTicks[di] && !o.Dodge; s++)
                            {
                                w.Kaiju.MeleeWindupIn = 30 - s;
                                BotReflex.Fill(w, null, bot, o);
                                w.Tick++;
                            }
                            w.Kaiju.MeleeWindupIn = 0;
                        }
                        else
                        {
                            Stage(w, kinds[n], bot, foe, 25);
                            BotReflex.Fill(w, null, bot, o);
                        }
                        if (o.Dodge) dodged++;
                    }
                    double share = dodged / 200.0, want = kinds[n] == BotReflex.KindTnt ? dc.TntChance[di] : dc.DodgeChance[di];   // b13 r3: TNT has its own roll
                    bool ok = Math.Abs(share - want) <= 0.12;
                    Console.WriteLine("{\"item\":\"b13-jd reflex\",\"difficulty\":\"" + d + "\",\"threat\":\"" + names[n] + "\",\"dodged\":" + dodged + ",\"trials\":200,\"share\":" + share.ToString("F3") +
                                      ",\"chance\":" + want.ToString("F2") + ",\"limit\":0.12,\"verdict\":\"" + (ok ? "PASS" : "FAIL") + "\"}");
                    if (!ok) failures.Add(d + " " + names[n] + ": " + share.ToString("F3") + " vs " + want);
                }
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void TheReactionDelayIsRespected()
        {
            foreach (BotDifficulty d in Diffs)
            {
                World w = Field(d, out int bot, out int foe);
                DodgeConfig dc = w.Config.Depth.Dodge;
                for (int i = 0; i < 3; i++) { dc.DodgeChance[i] = 1.0; dc.TntChance[i] = 1.0; }
                int delay = dc.ReactionTicks[BotReflex.DiffIndex(w, bot)];
                w.Tick = 5000;
                Fresh(w, bot);
                Stage(w, BotReflex.KindTnt, bot, foe, delay - 1);
                InputFrame early = SimInput.EmptyInput();
                BotReflex.Fill(w, null, bot, early);
                w.Tick++;
                w.Demolitions.Bombs[0].LandTick = w.Tick - delay; // same bomb, one tick older
                InputFrame late = SimInput.EmptyInput();
                BotReflex.Fill(w, null, bot, late);
                Console.WriteLine("{\"item\":\"b13-jd reaction\",\"difficulty\":\"" + d + "\",\"delay\":" + delay + ",\"dodgeAtDelayMinus1\":" + (early.Dodge ? "true" : "false") + ",\"dodgeAtDelay\":" + (late.Dodge ? "true" : "false") + "}");
                Assert.IsFalse(early.Dodge, d + ": not before the delay");
                Assert.IsTrue(late.Dodge, d + ": at the delay (chance 1)");
                Assert.Greater(late.MoveX * -1, 0.5, "away from the bomb (it lies to +x)");
            }
        }

        /// <summary>
        /// b13 r3: (a) a melee threat lasts the attacker's swing cycle (wind-up, then its cooldown), so a bot can read it after
        /// the 3-4 tick wind-up; past the cycle it is gone. (b) a Normal bot near the hill edge whose threat pushes it off the
        /// ring dodges along the ring instead (end inside R - HillMargin, never toward the threat); with every turn blocked it
        /// holds the hill on a non-lethal hit and leaves only on a lethal one. All printed.
        /// </summary>
        [Test]
        public void AMeleeCycleIsAThreatAndAHillBotDodgesAlongTheRing()
        {
            World w = Field(BotDifficulty.Normal, out int bot, out int foe);
            DodgeConfig dc = w.Config.Depth.Dodge;
            GameConfig c = w.Config;
            LightWeapon lw = LightWeapons.Of(w, w.Actors[foe].Cls);
            int cycle = (int)((lw != null ? lw.CooldownSeconds : c.Class(w.Actors[foe].Cls).AttackCooldown) * c.SimHz + 0.5);
            w.Tick = 3000;
            Fresh(w, bot);
            Stage(w, 0, bot, foe, 0);
            Actor f = w.Actors[foe];
            f.SwingPending = false; f.SwingFacing = Math.PI; f.Facing = Math.PI;
            f.SwingTick = w.Tick - (cycle - 1);
            bool inCycle = BotReflex.FindThreat(w, null, dc, bot, out int key1, out int age1, out double _, out double _);
            f.SwingTick = w.Tick - cycle;
            bool past = BotReflex.FindThreat(w, null, dc, bot, out int _, out int _, out double _, out double _);
            Console.WriteLine("{\"item\":\"b13-jd melee cycle\",\"cycleTicks\":" + cycle + ",\"threatAtCycleMinus1\":" + (inCycle ? "true" : "false") + ",\"age\":" + age1 +
                              ",\"kind\":" + (key1 & 7) + ",\"threatAtCycle\":" + (past ? "true" : "false") + ",\"reactionNormal\":" + dc.ReactionTicks[1] + "}");
            Assert.IsTrue(inCycle, "the swing cycle is a threat");
            Assert.AreEqual(BotReflex.KindMelee, key1 & 7);
            Assert.Greater(cycle, dc.ReactionTicks[1], "a Normal bot can react inside the cycle");
            Assert.IsFalse(past, "past the cycle: no threat");

            // (b) the hill ring: the bot 4.5 u east of the centre, a threat to its west pushing it east (off the ring)
            CriticalMass.Sim.Grid.HillDef hill = w.Map.Hill;
            double bx = hill.X + 4.5, bz = hill.Z;
            for (int cz = (int)bz - 5; cz <= (int)bz + 5; cz++)
                for (int cx = (int)bx - 5; cx <= (int)bx + 5; cx++) w.Map.Cells[cz * w.Map.Width + cx] = Cell.Floor;
            w.X[bot] = bx; w.Z[bot] = bz;
            DodgeState s = w.Dodges;
            double dx = 1, dz = 0;
            bool go = BotReflex.HillAware(w, s, dc, bot, 1, 0, 10, ref dx, ref dz);
            double ex = bx + dx * dc.Distance - hill.X, ez = bz + dz * dc.Distance - hill.Z;
            double endR = Math.Sqrt(ex * ex + ez * ez);
            Console.WriteLine("{\"item\":\"b13-jd hill dodge\",\"case\":\"pushed off the ring\",\"dodges\":" + (go ? "true" : "false") + ",\"dirX\":" + dx.ToString("F3") + ",\"dirZ\":" + dz.ToString("F3") +
                              ",\"endR\":" + endR.ToString("F2") + ",\"limit\":" + (hill.R - dc.HillMargin).ToString("F2") + ",\"awayDot\":" + dx.ToString("F3") + "}");
            Assert.IsTrue(go, "a turn fits");
            Assert.LessOrEqual(endR, hill.R - dc.HillMargin + 1e-9, "ends on the hill");
            Assert.GreaterOrEqual(dx, -0.05, "never toward the threat");
            // every turn blocked: walls all round the bot
            for (int cz = (int)bz - 5; cz <= (int)bz + 5; cz++)
                for (int cx = (int)bx - 5; cx <= (int)bx + 5; cx++)
                    if (cx != (int)bx || cz != (int)bz) w.Map.Cells[cz * w.Map.Width + cx] = Cell.Wall;
            dx = 1; dz = 0;
            bool hold = BotReflex.HillAware(w, s, dc, bot, 1, 0, 10, ref dx, ref dz);
            dx = 1; dz = 0;
            bool lethal = BotReflex.HillAware(w, s, dc, bot, 1, 0, w.Actors[bot].Hp, ref dx, ref dz);
            Console.WriteLine("{\"item\":\"b13-jd hill dodge\",\"case\":\"no turn fits\",\"nonLethalDodges\":" + (hold ? "true" : "false") + ",\"lethalDodges\":" + (lethal ? "true" : "false") + ",\"lethalDirX\":" + dx.ToString("F3") + "}");
            Assert.IsFalse(hold, "holds the hill on a non-lethal hit");
            Assert.IsTrue(lethal, "leaves on a lethal one");
            Assert.AreEqual(1.0, dx, 1e-9, "along the threat direction");
        }

        [Test]
        public void ABotJumpsAShortWallItsSteeringRunsIntoOncePer30Ticks()
        {
            World w = Field(BotDifficulty.Normal, out int bot, out int _);
            w.Map.Cells[(int)Z0 * w.Map.Width + (int)X0 + 1] = Cell.LowWall;
            Fresh(w, bot);
            w.Tick = 3000;
            InputFrame o = SimInput.EmptyInput();
            o.MoveX = 1;
            BotReflex.Fill(w, null, bot, o);
            bool first = o.Jump;
            w.Tick += 10;
            InputFrame o2 = SimInput.EmptyInput();
            o2.MoveX = 1;
            BotReflex.Fill(w, null, bot, o2);
            w.Tick += 25;
            InputFrame o3 = SimInput.EmptyInput();
            o3.MoveX = 1;
            BotReflex.Fill(w, null, bot, o3);
            w.Map.Cells[(int)Z0 * w.Map.Width + (int)X0 + 2] = Cell.Wall; // the far cell blocked: no jump
            w.Tick += 40;
            InputFrame o4 = SimInput.EmptyInput();
            o4.MoveX = 1;
            BotReflex.Fill(w, null, bot, o4);
            Console.WriteLine("{\"item\":\"b13-jd bot jump\",\"first\":" + (first ? "true" : "false") + ",\"after10Ticks\":" + (o2.Jump ? "true" : "false") + ",\"after35Ticks\":" + (o3.Jump ? "true" : "false") +
                              ",\"farCellBlocked\":" + (o4.Jump ? "true" : "false") + "}");
            Assert.IsTrue(first);
            Assert.IsFalse(o2.Jump, "at most once per 30 ticks");
            Assert.IsTrue(o3.Jump);
            Assert.IsFalse(o4.Jump, "never into a blocked far cell");
        }

        [Test]
        public void CarriersNeverDodgeAndTheRuleOffDoesNothing()
        {
            World w = Field(BotDifficulty.Hard, out int bot, out int foe);
            for (int i = 0; i < 3; i++) { w.Config.Depth.Dodge.DodgeChance[i] = 1.0; w.Config.Depth.Dodge.TntChance[i] = 1.0; }
            w.Tick = 7000;
            Fresh(w, bot);
            Stage(w, BotReflex.KindTnt, bot, foe, 25);
            w.Actors[bot].Carry = CarryKind.Corruption;
            InputFrame o = SimInput.EmptyInput();
            BotReflex.Fill(w, null, bot, o);
            w.Actors[bot].Carry = CarryKind.None;
            w.Config.Depth.Dodge.On = false;
            InputFrame off = SimInput.EmptyInput();
            BotReflex.Fill(w, null, bot, off);
            Console.WriteLine("{\"item\":\"b13-jd reflex refusals\",\"carrierDodged\":" + (o.Dodge ? "true" : "false") + ",\"ruleOffDodged\":" + (off.Dodge ? "true" : "false") + "}");
            Assert.IsFalse(o.Dodge);
            Assert.IsFalse(off.Dodge);
        }
    }
}
