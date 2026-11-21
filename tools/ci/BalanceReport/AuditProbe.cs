// AUDIT ONLY (audit/mechanics-2026-11-21, probe branches ci/balance/audit-mech-*; never merged). A second, independent
// observer of a bot match for the mechanics audit: zones, activity by minute, the comeback levers' size, hut votes, Kaiju,
// economy, terrain use, spawn deaths. It also plays the audit's scripted arms: an idle actor 0, an asymmetric class lineup for
// one team, and a team that never feeds. The sim code is untouched; the arms write only bot roles, carry and class at tick 0.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Tests.Harness;

namespace CriticalMass.Balance
{
    public sealed class AuditOptions
    {
        public bool Idle;            // actor 0 receives no input at all (AdvanceNoHuman)
        public int MixTeam = -1;     // 0 Azure, 1 Crimson: that team's lineup becomes Mix (counts per class, id order)
        public int[] Mix;
        public int NoFeedTeam = -1;  // that team never feeds: egg-duty bots go to the hill, food in hand vanishes
        public readonly StringBuilder Lines = new StringBuilder();
    }

    public static class AuditRunner
    {
        const int Zones = 8; // 0 hill, 1 hill ring (to 15 u), 2 own base (18 u of own pad), 3 enemy base, 4 bridge, 5 flank (|z - hill z| > 18), 6 own mid, 7 enemy mid
        const int Acts = 7;  // 0 dead/getting up, 1 fighting, 2 on hill, 3 carrying, 4 gathering/mining, 5 walking, 6 standing
        const int Minutes = 8;

        static string N(double v) => double.IsNaN(v) || double.IsInfinity(v) ? "null" : v.ToString("0.###", CultureInfo.InvariantCulture);

        static void ApplyMix(World w, AuditOptions o)
        {
            if (o.MixTeam < 0 || o.Mix == null) return;
            var list = new List<ClassId>();
            for (int k = 0; k < o.Mix.Length && k < 6; k++)
                for (int c = 0; c < o.Mix[k]; c++) list.Add((ClassId)k);
            int slot = 0;
            for (int i = 0; i < w.ActorCount && slot < list.Count; i++)
            {
                Actor a = w.Actors[i];
                if ((int)a.Team != o.MixTeam) continue;
                ClassId cls = list[slot++];
                a.Cls = cls;
                a.MaxHp = w.Config.Class(cls).MaxHp;
                a.Hp = a.MaxHp;
            }
        }

        static void NoFeed(World w, AuditOptions o)
        {
            if (o.NoFeedTeam < 0) return;
            BotsState st = BotBrain.GetBotsState(w);
            for (int i = 0; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if ((int)a.Team != o.NoFeedTeam) continue;
                if (st != null && st.Role != null && i < st.Role.Length && st.Role[i] == BotsState.RoleKaiju) st.Role[i] = BotsState.RoleHill;
                if (a.Carry == CarryKind.Biomass)
                {
                    a.Carry = CarryKind.None;
                    a.CarryAmount = 0;
                }
                a.CarryMeat = 0;
            }
        }

        public static MatchRecord Match(GameConfig cfg, int seed, int teamSize, AuditOptions o)
        {
            World w = World.CreateWorld(BalanceRunner.Options(cfg, seed, teamSize));
            ApplyMix(w, o);
            var probe = new BalanceProbe();
            probe.Begin(w, "bot seed " + seed.ToString(CultureInfo.InvariantCulture), seed);
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(cfg.MatchSeconds * cfg.SimHz) + (int)cfg.SimHz;
            double usPerTick = 1e6 / Stopwatch.Frequency;
            int n = w.ActorCount, hz = (int)Math.Round(cfg.SimHz);
            double dt = 1.0 / cfg.SimHz;
            int cursor = w.EventHead;
            int[] lastFight = new int[n];
            int[] respawnTick = new int[n];
            for (int i = 0; i < n; i++) { lastFight[i] = int.MinValue / 2; respawnTick[i] = -1; }
            double[] px = new double[n], pz = new double[n];
            for (int i = 0; i < n; i++) { px[i] = w.X[i]; pz[i] = w.Z[i]; }
            double[,] zone = new double[6, Zones];          // class x zone (alive seconds, sampled every 15 ticks)
            double[,] act = new double[Minutes * 6, Acts];  // (minute*6+class) x activity
            double[] lead = new double[Minutes + 1];        // Azure minus Crimson hill seconds at each minute
            double[] fatigueLost = new double[2], forwardTicks = new double[2], contested = new double[1], neutral = new double[1];
            double kaijuBlockSec = 0, kaijuBlockOwnSec = 0, kaijuSidedUpSec = 0, kaijuInRingSidedSec = 0;
            double hatchAt = double.NaN, firstKoAt = double.NaN;
            int kaijuKos = 0;
            double hillAtHatchA = double.NaN, hillAtHatchC = double.NaN;
            int[,] hutAt = new int[2, 3]; // level at 2:00, 4:00, 6:00
            int[,] stockMax = new int[2, 3];
            var votes = new StringBuilder();
            int[] prevVoteLevel = new int[2];
            var r2f = new List<double>[6]; for (int c = 0; c < 6; c++) r2f[c] = new List<double>();
            int[] deathsByCause = new int[8];
            int[] deathsByKillerCls = new int[8]; // 0..5 class, 6 Kaiju or none
            int deathsNearOwnPad = 0, deathsSoonAfterRespawn = 0, deathsTotal = 0;
            int dug = 0, placed = 0, terraRefused = 0, tnt = 0, explosions = 0, minesTriggered = 0;
            int pingsBot = 0, pingsHuman = 0, classSwitches = 0, powerTaken = 0, coinTaken = 0, cleanses = 0, purifies = 0, handoffs = 0, intercepts = 0;
            var deliveredByTeamKind = new double[2, 6];
            int[] kills = new int[n];
            double a0Hill = 0; int a0Deaths = 0;
            for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
            {
                NoFeed(w, o);
                long t0 = Stopwatch.GetTimestamp();
                if (o.Idle) MatchTick.AdvanceNoHuman(w, scratch);
                else MatchTick.Advance(w, null, scratch);
                double us = (Stopwatch.GetTimestamp() - t0) * usPerTick;
                probe.WatchHatch(w);
                probe.AfterTick(w, us);
                int tick = w.Tick;
                double now = w.Match.Time;
                // events
                if (w.EventHead - cursor > World.EventCapacity) cursor = w.EventHead - World.EventCapacity;
                for (; cursor < w.EventHead; cursor++)
                {
                    SimEvent e = w.Events[cursor % World.EventCapacity];
                    switch (e.Type)
                    {
                        case SimEventType.Hit:
                            if (e.A >= 0 && e.A < n) { lastFight[e.A] = tick; if (respawnTick[e.A] >= 0) { r2f[(int)w.Actors[e.A].Cls].Add((tick - respawnTick[e.A]) * dt); respawnTick[e.A] = -1; } }
                            if (e.B >= 0 && e.B < n) { lastFight[e.B] = tick; if (respawnTick[e.B] >= 0) { r2f[(int)w.Actors[e.B].Cls].Add((tick - respawnTick[e.B]) * dt); respawnTick[e.B] = -1; } }
                            break;
                        case SimEventType.AttackStart:
                            if (e.A >= 0 && e.A < n) lastFight[e.A] = tick;
                            break;
                        case SimEventType.Respawn:
                            if (e.A >= 0 && e.A < n) respawnTick[e.A] = tick;
                            break;
                        case SimEventType.Death:
                            deathsTotal++;
                            deathsByCause[Math.Max(0, Math.Min(7, (int)e.Cause))]++;
                            if (e.B >= 0 && e.B < n) { deathsByKillerCls[(int)w.Actors[e.B].Cls]++; kills[e.B]++; } else deathsByKillerCls[6]++;
                            if (e.A >= 0 && e.A < n)
                            {
                                Actor v = w.Actors[e.A];
                                var pad = w.Map.Pads[(int)v.Team];
                                double ddx = e.X - pad.X, ddz = e.Z - pad.Z;
                                if (ddx * ddx + ddz * ddz < 12 * 12) deathsNearOwnPad++;
                                if (respawnTick[e.A] >= 0 && tick - respawnTick[e.A] <= 4 * hz) deathsSoonAfterRespawn++;
                                respawnTick[e.A] = -1;
                                if (e.A == 0) a0Deaths++;
                            }
                            break;
                        case SimEventType.KaijuKO:
                            kaijuKos++;
                            if (double.IsNaN(firstKoAt)) firstKoAt = now;
                            break;
                        case SimEventType.Hatch:
                            if (double.IsNaN(hatchAt)) { hatchAt = now; hillAtHatchA = w.Match.HillSeconds.Azure; hillAtHatchC = w.Match.HillSeconds.Crimson; }
                            break;
                        case SimEventType.Terraform:
                            if (e.Value == 1) dug++; else if (e.Value == 2) placed++; else terraRefused++;
                            break;
                        case SimEventType.TntThrown: tnt++; break;
                        case SimEventType.Explosion: explosions++; break;
                        case SimEventType.MineTriggered: minesTriggered++; break;
                        case SimEventType.Ping:
                            if (e.A >= 0 && e.A < n && w.Actors[e.A].IsHuman) pingsHuman++; else pingsBot++;
                            break;
                        case SimEventType.ClassSwitch:
                            if (e.B == 0) classSwitches++;
                            break;
                        case SimEventType.PickupTaken:
                            if (e.B == 0) coinTaken++; else powerTaken++;
                            break;
                        case SimEventType.Cleanse: cleanses++; break;
                        case SimEventType.Purify: purifies++; break;
                        case SimEventType.Handoff: handoffs++; break;
                        case SimEventType.DeliverIntercepted: intercepts++; break;
                        case SimEventType.DeliverBiomass:
                            if (e.A >= 0 && e.A < n)
                            {
                                int kind = e.Bio.HasValue ? (int)e.Bio.Value : 5;
                                deliveredByTeamKind[(int)w.Actors[e.A].Team, Math.Max(0, Math.Min(5, kind))] += e.Value;
                            }
                            break;
                        case SimEventType.DeliverCorruption:
                            if (e.A >= 0 && e.A < n) deliveredByTeamKind[(int)w.Actors[e.A].Team, 4] += e.Value;
                            break;
                    }
                }
                // hill and comeback (every tick)
                HillState hs = w.Match.HillState;
                if (hs == HillState.Azure || hs == HillState.Crimson)
                {
                    int ow = hs == HillState.Azure ? 0 : 1;
                    fatigueLost[ow] += dt * (1.0 - Comeback.HoldShare(w, (TeamId)ow));
                }
                else if (hs == HillState.Contested) contested[0] += dt;
                else neutral[0] += dt;
                for (int tm = 0; tm < 2; tm++) if (Comeback.ForwardActive(w, (TeamId)tm)) forwardTicks[tm] += dt;
                int blk = Comeback.KaijuBlocking(w);
                if (blk >= 0) kaijuBlockSec += dt;
                Kaiju kj = w.Kaiju;
                if ((kj.State == KaijuState.Azure || kj.State == KaijuState.Crimson) && kj.KoIn <= 0)
                {
                    kaijuSidedUpSec += dt;
                    double rr = w.Config.Hill.Radius + w.Config.Kaiju.Radius;
                    double kx = kj.X - w.Map.Hill.X, kz = kj.Z - w.Map.Hill.Z;
                    if (kx * kx + kz * kz < rr * rr) kaijuInRingSidedSec += dt;
                }
                // per minute marks
                if (tick % (60 * hz) == 0)
                {
                    int mm = tick / (60 * hz);
                    if (mm >= 0 && mm <= Minutes) lead[mm] = w.Match.HillSeconds.Azure - w.Match.HillSeconds.Crimson;
                }
                if (tick == 120 * hz || tick == 240 * hz || tick == 360 * hz)
                {
                    int k = tick == 120 * hz ? 0 : tick == 240 * hz ? 1 : 2;
                    hutAt[0, k] = w.Hut != null ? w.Hut.Level[0] : 0;
                    hutAt[1, k] = w.Hut != null ? w.Hut.Level[1] : 0;
                }
                for (int tm = 0; tm < 2; tm++)
                    for (int s = 0; s < 3; s++)
                    {
                        int v = Sources.Stock(w, (TeamId)tm, s);
                        if (v > stockMax[tm, s]) stockMax[tm, s] = v;
                    }
                // hut votes (closed this tick)
                if (w.HutVote != null)
                    for (int tm = 0; tm < 2; tm++)
                    {
                        int vl = w.HutVote.VoteLevel[tm];
                        if (prevVoteLevel[tm] > 0 && vl != prevVoteLevel[tm] && w.HutVote.LastTick[tm] == tick)
                        {
                            if (votes.Length > 0) votes.Append(',');
                            votes.Append("{\"team\":").Append(tm).Append(",\"level\":").Append(w.HutVote.LastLevel[tm])
                                 .Append(",\"card\":").Append(w.HutVote.LastCard[tm]).Append(",\"a\":").Append(w.HutVote.LastA[tm])
                                 .Append(",\"b\":").Append(w.HutVote.LastB[tm]).Append(",\"tie\":").Append(w.HutVote.LastDefault[tm])
                                 .Append(",\"at\":").Append(N(now)).Append('}');
                        }
                        prevVoteLevel[tm] = vl;
                    }
                // bodies (every 15 ticks)
                if (tick % 15 == 0)
                {
                    double sdt = 15 * dt;
                    int minute = Math.Min(Minutes - 1, (int)(now / 60));
                    double hx0 = w.Map.Hill.X, hz0 = w.Map.Hill.Z, hr = w.Map.Hill.R;
                    for (int i = 0; i < n; i++)
                    {
                        Actor a = w.Actors[i];
                        int c = (int)a.Cls;
                        double dx = w.X[i] - px[i], dz = w.Z[i] - pz[i];
                        bool moving = dx * dx + dz * dz > (0.5 * sdt) * (0.5 * sdt);
                        px[i] = w.X[i]; pz[i] = w.Z[i];
                        int ac;
                        double hx = w.X[i] - hx0, hzz = w.Z[i] - hz0;
                        double hd2 = hx * hx + hzz * hzz;
                        if (!a.Alive || a.StandUp > 0) ac = 0;
                        else if (tick - lastFight[i] <= hz) ac = 1;
                        else if (hd2 < hr * hr) ac = 2;
                        else if (a.CarryAmount > 0) ac = 3;
                        else if (a.GatherProgress > 0 || Blacksmith.ChannelVein(w, i) >= 0) ac = 4;
                        else if (moving) ac = 5;
                        else ac = 6;
                        act[minute * 6 + c, ac] += sdt;
                        if (i == 0 && hd2 < hr * hr && a.Alive) a0Hill += sdt;
                        if (!a.Alive) continue;
                        int zn;
                        var own = w.Map.Pads[(int)a.Team];
                        var foe = w.Map.Pads[1 - (int)a.Team];
                        double ox = w.X[i] - own.X, oz = w.Z[i] - own.Z, fx = w.X[i] - foe.X, fz = w.Z[i] - foe.Z;
                        int cx = (int)Math.Floor(w.X[i]), cz = (int)Math.Floor(w.Z[i]);
                        byte cell = cx >= 0 && cz >= 0 && cx < w.Map.Width && cz < w.Map.Height ? w.Map.Cells[cz * w.Map.Width + cx] : (byte)0;
                        if (hd2 < hr * hr) zn = 0;
                        else if (hd2 < 15 * 15) zn = 1;
                        else if (ox * ox + oz * oz < 18 * 18) zn = 2;
                        else if (fx * fx + fz * fz < 18 * 18) zn = 3;
                        else if (cell == Cell.Bridge) zn = 4;
                        else if (Math.Abs(w.Z[i] - hz0) > 18) zn = 5;
                        else zn = (Math.Abs(w.X[i] - own.X) < Math.Abs(w.X[i] - foe.X)) ? 6 : 7;
                        zone[c, zn] += sdt;
                    }
                }
            }
            MatchRecord rec = probe.Finish(w);
            // one JSON line
            var j = new StringBuilder();
            j.Append("{\"seed\":").Append(seed).Append(",\"teamSize\":").Append(teamSize)
             .Append(",\"winner\":\"").Append(w.Match.Winner).Append('"')
             .Append(",\"hillA\":").Append(N(w.Match.HillSeconds.Azure)).Append(",\"hillC\":").Append(N(w.Match.HillSeconds.Crimson))
             .Append(",\"leadChanges\":").Append(rec.LeadChanges).Append(",\"decidedAt\":").Append(N(rec.DecidedAt)).Append(",\"leaderAtHalf\":").Append(rec.LeaderAtHalf)
             .Append(",\"lead\":[");
            for (int m = 0; m <= Minutes; m++) { if (m > 0) j.Append(','); j.Append(N(lead[m])); }
            j.Append("],\"fatigueLost\":[").Append(N(fatigueLost[0])).Append(',').Append(N(fatigueLost[1])).Append(']')
             .Append(",\"rallyBonus\":[").Append(N(Comeback.BonusSeconds(w, TeamId.Azure))).Append(',').Append(N(Comeback.BonusSeconds(w, TeamId.Crimson))).Append(']')
             .Append(",\"forwardSec\":[").Append(N(forwardTicks[0])).Append(',').Append(N(forwardTicks[1])).Append(']')
             .Append(",\"contested\":").Append(N(contested[0])).Append(",\"neutral\":").Append(N(neutral[0]))
             .Append(",\"kaijuBlockSec\":").Append(N(kaijuBlockSec)).Append(",\"kaijuSidedUpSec\":").Append(N(kaijuSidedUpSec)).Append(",\"kaijuInRingSidedSec\":").Append(N(kaijuInRingSidedSec))
             .Append(",\"hatchAt\":").Append(N(hatchAt)).Append(",\"hillAtHatch\":[").Append(N(hillAtHatchA)).Append(',').Append(N(hillAtHatchC)).Append(']')
             .Append(",\"kaijuType\":\"").Append(rec.KaijuType).Append("\",\"kaijuOwner\":\"").Append(rec.Hatched ? rec.KaijuOwner.ToString() : "").Append('"')
             .Append(",\"mix\":[");
            for (int k = 0; k < 4; k++) { if (k > 0) j.Append(','); j.Append(N(w.KaijuKind != null ? w.KaijuKind.Mix[k] : double.NaN)); }
            j.Append("],\"kaijuKos\":").Append(kaijuKos).Append(",\"firstKoAt\":").Append(N(firstKoAt))
             .Append(",\"allegiance\":[").Append(N(w.Match.Allegiance.Azure)).Append(',').Append(N(w.Match.Allegiance.Crimson)).Append(']')
             .Append(",\"hutAt\":[[").Append(hutAt[0, 0]).Append(',').Append(hutAt[0, 1]).Append(',').Append(hutAt[0, 2]).Append("],[")
             .Append(hutAt[1, 0]).Append(',').Append(hutAt[1, 1]).Append(',').Append(hutAt[1, 2]).Append("]]")
             .Append(",\"hutEnd\":[").Append(w.Hut != null ? w.Hut.Level[0] : 0).Append(',').Append(w.Hut != null ? w.Hut.Level[1] : 0).Append(']')
             .Append(",\"oreBanked\":[").Append(w.Hut != null ? w.Hut.Banked[0] : 0).Append(',').Append(w.Hut != null ? w.Hut.Banked[1] : 0).Append(']')
             .Append(",\"votes\":[").Append(votes).Append(']')
             .Append(",\"stockMax\":[[").Append(stockMax[0, 0]).Append(',').Append(stockMax[0, 1]).Append(',').Append(stockMax[0, 2]).Append("],[")
             .Append(stockMax[1, 0]).Append(',').Append(stockMax[1, 1]).Append(',').Append(stockMax[1, 2]).Append("]]")
             .Append(",\"stockEnd\":[[");
            for (int tm = 0; tm < 2; tm++)
            {
                if (tm > 0) j.Append("],[");
                for (int s = 0; s < 3; s++) { if (s > 0) j.Append(','); j.Append(Sources.Stock(w, (TeamId)tm, s)); }
            }
            j.Append("]],\"delivered\":[[");
            for (int tm = 0; tm < 2; tm++)
            {
                if (tm > 0) j.Append("],[");
                for (int k = 0; k < 6; k++) { if (k > 0) j.Append(','); j.Append(N(deliveredByTeamKind[tm, k])); }
            }
            int coinsA = 0, coinsC = 0;
            for (int i = 0; i < n; i++) { if (w.Actors[i].Team == TeamId.Azure) coinsA += w.Actors[i].CoinsPicked; else coinsC += w.Actors[i].CoinsPicked; }
            j.Append("]],\"coins\":[").Append(coinsA).Append(',').Append(coinsC).Append(']')
             .Append(",\"deaths\":").Append(deathsTotal).Append(",\"deathsByCause\":[");
            for (int k = 0; k < 8; k++) { if (k > 0) j.Append(','); j.Append(deathsByCause[k]); }
            j.Append("],\"deathsByKillerCls\":[");
            for (int k = 0; k < 7; k++) { if (k > 0) j.Append(','); j.Append(deathsByKillerCls[k]); }
            j.Append("],\"deathsNearOwnPad\":").Append(deathsNearOwnPad).Append(",\"deathsSoonAfterRespawn\":").Append(deathsSoonAfterRespawn)
             .Append(",\"dug\":").Append(dug).Append(",\"placed\":").Append(placed).Append(",\"terraRefused\":").Append(terraRefused)
             .Append(",\"walls\":").Append(rec.WallsBuilt).Append(",\"pieces\":").Append(rec.PiecesPlaced)
             .Append(",\"tnt\":").Append(tnt).Append(",\"explosions\":").Append(explosions).Append(",\"minesTriggered\":").Append(minesTriggered)
             .Append(",\"pingsBot\":").Append(pingsBot).Append(",\"pingsHuman\":").Append(pingsHuman).Append(",\"classSwitches\":").Append(classSwitches)
             .Append(",\"powerTaken\":").Append(powerTaken).Append(",\"coinTaken\":").Append(coinTaken)
             .Append(",\"cleanses\":").Append(cleanses).Append(",\"purifies\":").Append(purifies).Append(",\"handoffs\":").Append(handoffs).Append(",\"intercepts\":").Append(intercepts)
             .Append(",\"a0\":{\"team\":").Append((int)w.Actors[0].Team).Append(",\"cls\":").Append((int)w.Actors[0].Cls).Append(",\"hill\":").Append(N(a0Hill))
             .Append(",\"deaths\":").Append(a0Deaths).Append(",\"kills\":").Append(kills[0]).Append('}')
             .Append(",\"respawnToFight\":[");
            for (int c = 0; c < 6; c++)
            {
                if (c > 0) j.Append(',');
                var l = r2f[c];
                l.Sort();
                double med = l.Count > 0 ? l[l.Count / 2] : double.NaN;
                double sum = 0; foreach (double v in l) sum += v;
                j.Append("[").Append(l.Count).Append(',').Append(N(med)).Append(',').Append(N(l.Count > 0 ? sum / l.Count : double.NaN)).Append(']');
            }
            j.Append("],\"zone\":[");
            for (int c = 0; c < 6; c++)
            {
                if (c > 0) j.Append(',');
                j.Append('[');
                for (int z = 0; z < Zones; z++) { if (z > 0) j.Append(','); j.Append(N(zone[c, z])); }
                j.Append(']');
            }
            j.Append("],\"act\":[");
            for (int r = 0; r < Minutes * 6; r++)
            {
                if (r > 0) j.Append(',');
                j.Append('[');
                for (int k = 0; k < Acts; k++) { if (k > 0) j.Append(','); j.Append(N(act[r, k])); }
                j.Append(']');
            }
            j.Append("],\"body\":[");
            for (int c = 0; c < 6; c++) { if (c > 0) j.Append(','); j.Append(N(rec.Body[c])); }
            j.Append("],\"kills\":[");
            for (int c = 0; c < 6; c++) { if (c > 0) j.Append(','); j.Append(N(rec.Kills[c])); }
            j.Append("],\"deathsCls\":[");
            for (int c = 0; c < 6; c++) { if (c > 0) j.Append(','); j.Append(N(rec.Deaths[c])); }
            j.Append("],\"objective\":[");
            for (int c = 0; c < 6; c++) { if (c > 0) j.Append(','); j.Append(N(rec.Objective[c])); }
            j.Append("]}");
            o.Lines.Append(j).Append('\n');
            return rec;
        }
    }
}
