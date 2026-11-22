// Player-seat telemetry probe (px lane T, 2026-11-22). Permanent tool, not part of the game build (lives under tools/).
// Plays staged duels or full matches with the player-seat gate's human model (Sim.Tests/PlayerSeat/PlayerSeatRig.cs
// HumanModel) on the seat through MatchTick.AdvanceHumans, and records every input edge with the shared recorder
// (Sim/Diagnostics/PlayerSeatLog.cs) as JSON lines. Compiled and run with no Editor by tools/px-telemetry/run.sh.
// Docs: docs/testing/PLAYER-SEAT-TELEMETRY.md.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Diagnostics;
using CriticalMass.Sim.Tests.PlayerSeat;

public static class PlayerSeatTelemetryProbe
{
    sealed class FileSink : IPlayerSeatLogSink
    {
        readonly StreamWriter _w;
        public int Lines;
        public FileSink(string path) { _w = new StreamWriter(path, false) { NewLine = "\n" }; }
        public void WriteLine(string json) { _w.WriteLine(json); Lines++; }
        public void Close() => _w.Dispose();
    }

    sealed class Opts
    {
        public List<ClassId> Classes = new List<ClassId>(), Opponents = new List<ClassId>();
        public int Seeds = 8, Seed0 = 4242, Reaction = 6, AimHelp = 0, Ticks = 0;
        public string Mode = "duel", Skill = "normal", Verbs = "all", Out, Label = "";
        public BotDifficulty Bots = BotDifficulty.Normal;
    }

    static readonly ClassId[] All = { ClassId.Warrior, ClassId.Farmer, ClassId.Builder, ClassId.Demolitions, ClassId.Miner, ClassId.Healer };

    public static int Main(string[] args)
    {
        Opts o;
        try { o = Parse(args); }
        catch (Exception e) { Console.Error.WriteLine("px-telemetry: " + e.Message); Usage(); return 64; }
        Directory.CreateDirectory(o.Out);
        int files = 0, records = 0;
        foreach (ClassId me in o.Classes)
        {
            List<ClassId> opp = o.Mode == "duel" ? o.Opponents : new List<ClassId> { me };
            foreach (ClassId them in opp)
            {
                string name = o.Mode == "duel" ? $"duel-{me}-vs-{them}.jsonl" : $"{o.Mode}-{me}.jsonl";
                var sink = new FileSink(Path.Combine(o.Out, name));
                int presses = 0, nothing = 0, refused = 0;
                for (int k = 0; k < o.Seeds; k++)
                {
                    PlayerSeatLog log = o.Mode == "duel" ? Duel(o, me, them, k, sink) : Match(o, me, k, sink);
                    presses += log.Presses; nothing += log.Nothing; refused += log.Refused;
                }
                sink.Close();
                files++;
                records += sink.Lines;
                Console.WriteLine($"px-telemetry: {name}: {o.Seeds} runs, {sink.Lines} records, {presses} presses, {refused} refused, {nothing} resolved to nothing");
            }
        }
        Console.WriteLine($"px-telemetry: wrote {files} files, {records} records to {o.Out}");
        return 0;
    }

    static void Usage()
    {
        Console.Error.WriteLine("usage: run.sh [--class Warrior|all[,..]] [--vs Warrior|all[,..]] [--seeds 8] [--seed0 4242] [--reaction 6] [--aim-help 0|1|2]\n" +
            "              [--mode duel|5v5|16v16] [--ticks N] [--skill first|normal|strong] [--verbs all|attack] [--bots Normal|Easy|Hard] [--out DIR] [--label TEXT]");
    }

    static List<ClassId> Classes(string v)
    {
        var r = new List<ClassId>();
        if (v.Equals("all", StringComparison.OrdinalIgnoreCase)) { r.AddRange(All); return r; }
        foreach (string s in v.Split(',')) r.Add((ClassId)Enum.Parse(typeof(ClassId), s.Trim(), true));
        return r;
    }

    static Opts Parse(string[] a)
    {
        var o = new Opts();
        for (int i = 0; i < a.Length; i++)
        {
            string k = a[i];
            string v = i + 1 < a.Length ? a[i + 1] : throw new ArgumentException("missing value after " + k);
            i++;
            switch (k)
            {
                case "--class": o.Classes = Classes(v); break;
                case "--vs": o.Opponents = Classes(v); break;
                case "--seeds": o.Seeds = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "--seed0": o.Seed0 = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "--reaction": o.Reaction = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "--aim-help": o.AimHelp = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "--ticks": o.Ticks = int.Parse(v, CultureInfo.InvariantCulture); break;
                case "--mode": o.Mode = v; break;
                case "--skill": o.Skill = v; break;
                case "--verbs": o.Verbs = v; break;
                case "--bots": o.Bots = (BotDifficulty)Enum.Parse(typeof(BotDifficulty), v, true); break;
                case "--out": o.Out = v; break;
                case "--label": o.Label = v; break;
                default: throw new ArgumentException("unknown option " + k);
            }
        }
        if (o.Classes.Count == 0) o.Classes.Add(ClassId.Warrior);
        if (o.Opponents.Count == 0) o.Opponents.AddRange(All);
        if (o.Mode != "duel" && o.Mode != "5v5" && o.Mode != "16v16") throw new ArgumentException("mode must be duel, 5v5 or 16v16");
        if (o.Out == null) throw new ArgumentException("--out DIR is required (run.sh fills the default)");
        return o;
    }

    /// <summary>The gate's human model with the chosen skill on top.</summary>
    static void Shape(HumanModel h, Opts o)
    {
        h.LagTicks = o.Reaction;
        h.AimHelp = o.AimHelp;
        if (o.Skill == "first") { h.HalfMiss = true; }
        else if (o.Skill == "strong") { h.Feints = true; h.HitCancels = true; h.Dodges = true; h.KeepRange = false; }
    }

    /// <summary>
    /// The other buttons, on a fixed schedule so every class's whole kit is pressed (offsets keep two buttons off the same
    /// tick): ability every 4 s, F (dig / place / mine / build) every 3 s, interact held 1 s every 6 s, jump every 5 s, dodge
    /// every 7 s, cancel every 5 s. "attack" presses only the primary (the gate model as is).
    /// </summary>
    static void Verbs(Seat s, Opts o, int t)
    {
        if (o.Verbs != "all") return;
        HumanModel h = s.Human;
        int hz = (int)s.W.Config.SimHz;
        if (t % (4 * hz) == hz / 2) h.PressAbility = true;
        if (t % (3 * hz) == hz + 3) h.PressDigPlace = true;
        int ip = t % (6 * hz);
        h.HoldInteract = ip >= 2 * hz + 7 && ip < 3 * hz + 7;
        if (t % (5 * hz) == 3 * hz + 11) h.PressJump = true;
        if (t % (7 * hz) == 5 * hz + 13) h.PressDodge = true;
        if (t % (5 * hz) == 4 * hz + 17) h.PressCancel = true;
    }

    static PlayerSeatLog NewLog(Opts o, Seat s, int seed, IPlayerSeatLogSink sink, string mode)
    {
        var log = new PlayerSeatLog(s.Me, sink) { Seed = seed, Mode = mode, Source = "headless", Label = o.Label };
        log.Begin(s.W);
        return log;
    }

    static PlayerSeatLog Duel(Opts o, ClassId me, ClassId them, int k, IPlayerSeatLogSink sink)
    {
        Seat s = PlayerSeatRig.Duel(me, them, TargetMode.RealBot, PlayerSeatRig.AngleOf(k, o.Seeds), k, o.Reaction, bots: o.Bots);
        Shape(s.Human, o);
        s.AllowAutopilot = true;
        var log = NewLog(o, s, 4242 + k, sink, $"duel vs {them} ({o.Bots}) skill {o.Skill} reaction {o.Reaction} aim-help {o.AimHelp} verbs {o.Verbs}");
        int max = o.Ticks > 0 ? o.Ticks : s.Ticks(PlayerSeatTargets.DuelMaxSeconds);
        for (int t = 0; t < max; t++)
        {
            Verbs(s, o, t);
            s.Step();
            log.Observe(s.W, s.Frames[s.Me]);
            if (!s.W.Actors[s.Me].Alive || !s.W.Actors[s.Them].Alive) break;
        }
        // a few ticks more so the last press gets its answer
        for (int t = 0; t < 3; t++) { s.Step(); log.Observe(s.W, s.Frames[s.Me]); }
        log.Finish(s.W);
        return log;
    }

    static PlayerSeatLog Match(Opts o, ClassId me, int k, IPlayerSeatLogSink sink)
    {
        int team = o.Mode == "16v16" ? 16 : 5;
        GameConfig c = team >= 16 ? GameConfig.CreateDefault().ApplyRules(RulesProfile.Full) : GameConfig.CreateBrowser5v5().ApplyRules(RulesProfile.Full);
        c.TeamSize = team;
        int seed = o.Seed0 + k;
        World w = World.CreateWorld(new WorldOptions { Seed = seed, TeamSize = team, HumanTeam = TeamId.Azure, HumanClass = me, Config = c, BotDifficulty = o.Bots, AllyBotDifficulty = o.Bots });
        var s = new Seat { W = w, Me = 0, Spy = new EventSpy(w), Frames = new InputFrame[w.ActorCount], AllowAutopilot = true };
        s.Human = new HumanModel(0, -1);
        Shape(s.Human, o);
        var log = NewLog(o, s, seed, sink, $"{o.Mode} match ({o.Bots} bots) skill {o.Skill} reaction {o.Reaction} aim-help {o.AimHelp} verbs {o.Verbs}");
        int max = o.Ticks > 0 ? o.Ticks : (int)Math.Ceiling(w.Config.MatchSeconds * w.Config.SimHz);
        for (int t = 0; t < max && w.Match.Winner == MatchResult.None; t++)
        {
            s.Human.Target = NearestEnemy(w, 0);
            Verbs(s, o, t);
            s.Step();
            log.Observe(w, s.Frames[s.Me]);
        }
        log.Finish(w);
        return log;
    }

    static int NearestEnemy(World w, int me)
    {
        int best = -1;
        double bd = double.MaxValue;
        for (int j = 0; j < w.ActorCount; j++)
        {
            if (j == me || !w.Actors[j].Alive || w.Actors[j].Team == w.Actors[me].Team) continue;
            double dx = w.X[j] - w.X[me], dz = w.Z[j] - w.Z[me], d = dx * dx + dz * dz;
            if (d < bd) { bd = d; best = j; }
        }
        return best;
    }
}
