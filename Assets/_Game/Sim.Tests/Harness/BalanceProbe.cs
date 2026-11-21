using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using CriticalMass.Net.Replay;
using CriticalMass.Sim.Bots;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;

namespace CriticalMass.Sim.Tests.Harness
{
    /// <summary>
    /// b15 tools (balance report): the seed pools of the balance report. The first 24 are WinShare.Seeds16, the pool every rule
    /// item's 16v16 win-share limit runs on (a test pins the copy); a larger pool continues 20, 21, ... skipping seeds already in.
    /// </summary>
    public static class BalanceSeeds
    {
        public static int[] Pool(int n)
        {
            var list = new List<int>(WinShare.Seeds16);
            var seen = new HashSet<int>(list);
            for (int s = 20; list.Count < n; s++)
                if (seen.Add(s)) list.Add(s);
            return list.GetRange(0, Math.Max(0, n)).ToArray();
        }
    }

    /// <summary>
    /// b15 tools: what one match measured, read from the sim from the outside (events and state after each tick). Nothing here is
    /// read by any rule. Times are in sim seconds; per-class arrays are indexed by (int)ClassId; per-team by (int)TeamId.
    /// </summary>
    public sealed class MatchRecord
    {
        public const int Classes = 6;
        public string Source = "";
        public int Seed;
        public int TeamSize;
        public int Ticks;
        public double Length;
        public double MatchSeconds;
        public MatchResult Winner;
        public double HillAzure, HillCrimson;
        public int LeadChanges;
        /// <summary>Match time of the last lead change (the lead first taken when it never changed); NaN when nobody ever led.</summary>
        public double DecidedAt = double.NaN;
        /// <summary>Sign of the hill lead at half time: +1 Azure, -1 Crimson, 0 level.</summary>
        public int LeaderAtHalf;
        public double OwnedShare;
        public double LongestHold;
        public bool Hatched;
        public string KaijuType = "";
        public KaijuState KaijuOwner;
        /// <summary>[team, level-1]: match time the hut reached that level (NaN = never).</summary>
        public double[,] HutAt = new double[2, 5];
        public int WallsBuilt, WallsBroken, PiecesPlaced, PiecesDestroyed, BarricadesPlaced, BarricadesBroken, Topples;
        public int Stuck;
        public readonly List<int> StuckCells = new List<int>();
        public int MapWidth;
        public double TickMeanUs, TickP99Us;
        public int EventsLost;
        public bool Desynced;
        public int Bodies;
        // per class
        public double[] Body = new double[Classes];
        public double[] ActorSec = new double[Classes], DeadSec = new double[Classes], WalkSec = new double[Classes], FightSec = new double[Classes];
        /// <summary>b15 wave two (PR #219's definition, the one TARGETS.json's deadwalk band answers): time lost to a death =
        /// not alive, plus the walk back from the Respawn event until the first of hill radius + WalkBackHillMargin, a hit dealt
        /// or taken, a job (GatherProgress &gt; 0 or channelling a vein), WalkBackCap seconds, or death.</summary>
        public double[] DownSec = new double[Classes], WalkBackSec = new double[Classes];
        public double[] Damage = new double[Classes], Kills = new double[Classes], Deaths = new double[Classes], Healing = new double[Classes];
        public double[] Objective = new double[Classes], Delivered = new double[Classes], KaijuDamage = new double[Classes];
        public double Top10DamageShare, Top10KillShare;
        public int TotalKills;

        public MatchRecord()
        {
            for (int t = 0; t < 2; t++)
                for (int l = 0; l < 5; l++) HutAt[t, l] = double.NaN;
        }

        public bool Blowout
        {
            get
            {
                double hi = Math.Max(HillAzure, HillCrimson), lo = Math.Min(HillAzure, HillCrimson);
                return Winner != MatchResult.Draw && Winner != MatchResult.None && hi >= 2 * lo;
            }
        }
    }

    /// <summary>
    /// b15 tools: watches one match tick by tick from the outside (World.Events since the last tick, then state). Definitions:
    /// <para>dead = not alive or getting up; fighting = alive and within FightWindow of a hit dealt or taken or a swing started;
    /// walking = alive, not fighting, moving faster than WalkSpeed, carrying nothing and off the hill; the rest is working.</para>
    /// <para>lead changes = the sign of the hill-seconds difference (dead band 0.5 s) flips, sampled at 1 Hz (the b13 soaks' rule);
    /// stuck = the DG-02 window: move input held for 2 s with no enemy in contact and under 0.05 u of displacement.</para>
    /// </summary>
    public sealed class BalanceProbe
    {
        public const double FightWindow = 1.0;
        public const double WalkSpeed = 0.5;
        public const int StuckWindow = 60;
        public const double StuckDistance = 0.05;
        public const double LeadDeadBand = 0.5;
        public const double OwnedFrom = 30;
        /// <summary>The walk back after a respawn ends this far outside the hill radius, or after this many seconds (PR #219).</summary>
        public const double WalkBackHillMargin = 6, WalkBackCap = 20;

        int _cursor, _hz, _n, _fightTicks;
        double _dt;
        int[] _lastFight, _held, _walkFrom;
        bool[] _walkBack;
        double[] _px, _pz, _ax, _az, _damage, _kills;
        int _lastSign;
        bool _halfDone;
        HillState _holdState;
        double _holdStart;
        int _ownedSamples, _samples;
        readonly List<double> _tickUs = new List<double>();
        MatchRecord _r;

        public MatchRecord Record => _r;

        public void Begin(World w, string source, int seed)
        {
            _r = new MatchRecord { Source = source, Seed = seed, TeamSize = w.ActorCount / 2, MatchSeconds = w.Config.MatchSeconds, MapWidth = w.Map.Width };
            _n = w.ActorCount;
            _r.Bodies = _n;
            _hz = (int)Math.Round(w.Config.SimHz);
            _dt = 1.0 / w.Config.SimHz;
            _fightTicks = (int)Math.Round(FightWindow * w.Config.SimHz);
            _cursor = w.EventHead;
            _lastFight = new int[_n];
            _held = new int[_n];
            _walkFrom = new int[_n];
            _walkBack = new bool[_n];
            _px = new double[_n];
            _pz = new double[_n];
            _ax = new double[_n];
            _az = new double[_n];
            _damage = new double[_n];
            _kills = new double[_n];
            for (int i = 0; i < _n; i++)
            {
                _lastFight[i] = int.MinValue / 2;
                _px[i] = w.X[i];
                _pz[i] = w.Z[i];
                _r.Body[(int)w.Actors[i].Cls]++;
            }
            _lastSign = 0;
            _halfDone = false;
            _holdState = HillState.Neutral;
            _holdStart = 0;
            _ownedSamples = 0;
            _samples = 0;
            _tickUs.Clear();
        }

        int ClsOf(World w, int id) => id >= 0 && id < _n ? (int)w.Actors[id].Cls : -1;

        /// <summary>Call after every sim tick; tickUs = the wall time that tick took (microseconds, 0 = not measured).</summary>
        public void AfterTick(World w, double tickUs)
        {
            if (tickUs > 0) _tickUs.Add(tickUs);
            int tick = w.Tick;
            if (w.EventHead - _cursor > World.EventCapacity)
            {
                _r.EventsLost += w.EventHead - _cursor - World.EventCapacity;
                _cursor = w.EventHead - World.EventCapacity;
            }
            for (; _cursor < w.EventHead; _cursor++) OnEvent(w, w.Events[_cursor % World.EventCapacity], tick);

            MatchState m = w.Match;
            double hillR = w.Map.Hill.R;
            for (int i = 0; i < _n; i++)
            {
                Actor a = w.Actors[i];
                int c = (int)a.Cls;
                _r.ActorSec[c] += _dt;
                double dx = w.X[i] - _px[i], dz = w.Z[i] - _pz[i];
                _px[i] = w.X[i];
                _pz[i] = w.Z[i];
                if (!a.Alive || a.StandUp > 0)
                {
                    _r.DeadSec[c] += _dt;
                }
                else if (tick - _lastFight[i] <= _fightTicks)
                {
                    _r.FightSec[c] += _dt;
                }
                else
                {
                    double hx = w.X[i] - w.Map.Hill.X, hz = w.Z[i] - w.Map.Hill.Z;
                    bool onHill = hx * hx + hz * hz <= hillR * hillR;
                    bool moving = dx * dx + dz * dz > WalkSpeed * _dt * WalkSpeed * _dt;
                    if (moving && a.CarryAmount <= 0 && !onHill) _r.WalkSec[c] += _dt;
                }
                // b15 wave two: dead (not alive) + the walk back after a respawn (PR #219's definition)
                if (!a.Alive)
                {
                    _r.DownSec[c] += _dt;
                    _walkBack[i] = false;
                }
                else if (_walkBack[i])
                {
                    double bx = w.X[i] - w.Map.Hill.X, bz = w.Z[i] - w.Map.Hill.Z, br = hillR + WalkBackHillMargin;
                    if (bx * bx + bz * bz < br * br || a.GatherProgress > 0 || Blacksmith.ChannelVein(w, i) >= 0 || tick - _walkFrom[i] > WalkBackCap * _hz)
                        _walkBack[i] = false;
                    else
                    {
                        _r.DownSec[c] += _dt;
                        _r.WalkBackSec[c] += _dt;
                    }
                }
                StuckStep(w, i, a);
            }

            // hill: longest hold (every tick), owned share and the lead (1 Hz)
            HillState hs = m.HillState;
            if (hs != _holdState)
            {
                CloseHold(m.Time);
                _holdState = hs;
                _holdStart = m.Time;
            }
            if (tick % _hz == 0)
            {
                double t = m.Time;
                if (t > OwnedFrom + 1e-6)
                {
                    _samples++;
                    if (hs == HillState.Azure || hs == HillState.Crimson) _ownedSamples++;
                }
                double lead = m.HillSeconds.Azure - m.HillSeconds.Crimson;
                int sign = lead > LeadDeadBand ? 1 : lead < -LeadDeadBand ? -1 : 0;
                if (sign != 0 && _lastSign != 0 && sign != _lastSign)
                {
                    _r.LeadChanges++;
                    _r.DecidedAt = t;
                }
                if (sign != 0 && _lastSign == 0 && double.IsNaN(_r.DecidedAt)) _r.DecidedAt = t;
                if (sign != 0) _lastSign = sign;
                if (!_halfDone && t + 1e-6 >= w.Config.MatchSeconds / 2)
                {
                    _halfDone = true;
                    _r.LeaderAtHalf = sign;
                }
            }
        }

        void CloseHold(double now)
        {
            if (_holdState == HillState.Azure || _holdState == HillState.Crimson)
                _r.LongestHold = Math.Max(_r.LongestHold, now - _holdStart);
        }

        void StuckStep(World w, int i, Actor a)
        {
            InputFrame inp = w.Inputs[i];
            bool held = a.Alive && a.StandUp <= 0 && inp.MoveX * inp.MoveX + inp.MoveZ * inp.MoveZ > 0.25 && !EnemyTouching(w, i);
            if (!held)
            {
                _held[i] = 0;
                return;
            }
            if (_held[i] == 0)
            {
                _ax[i] = w.X[i];
                _az[i] = w.Z[i];
            }
            if (++_held[i] < StuckWindow) return;
            _held[i] = 0;
            double ddx = w.X[i] - _ax[i], ddz = w.Z[i] - _az[i];
            if (ddx * ddx + ddz * ddz >= StuckDistance * StuckDistance) return;
            _r.Stuck++;
            _r.StuckCells.Add((int)Math.Floor(w.Z[i]) * w.Map.Width + (int)Math.Floor(w.X[i]));
        }

        static bool EnemyTouching(World w, int i)
        {
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == i || !w.Actors[j].Alive || w.Actors[j].Team == w.Actors[i].Team) continue;
                double rr = Collide.ActorRadius(w, i) + Collide.ActorRadius(w, j) + 0.2;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i];
                if (dx * dx + dz * dz < rr * rr) return true;
            }
            return false;
        }

        void OnEvent(World w, SimEvent e, int tick)
        {
            switch (e.Type)
            {
                case SimEventType.Hit:
                    if (e.A >= 0 && e.A < _n) { _lastFight[e.A] = tick; _walkBack[e.A] = false; }
                    if (e.B >= 0 && e.B < _n)
                    {
                        _lastFight[e.B] = tick;
                        _walkBack[e.B] = false;
                        _damage[e.B] += e.Value;
                        _r.Damage[ClsOf(w, e.B)] += e.Value;
                    }
                    break;
                case SimEventType.Respawn:
                    if (e.A >= 0 && e.A < _n) { _walkBack[e.A] = true; _walkFrom[e.A] = tick; }
                    break;
                case SimEventType.Death:
                    if (e.A >= 0 && e.A < _n) _r.Deaths[ClsOf(w, e.A)]++;
                    _r.TotalKills++;
                    if (e.B >= 0 && e.B < _n)
                    {
                        _kills[e.B]++;
                        _r.Kills[ClsOf(w, e.B)]++;
                    }
                    break;
                case SimEventType.AttackStart:
                    if (e.A >= 0 && e.A < _n) _lastFight[e.A] = tick;
                    break;
                case SimEventType.HealLanded:
                    if (e.B >= 0 && e.B < _n) _r.Healing[ClsOf(w, e.B)] += e.Value;
                    break;
                case SimEventType.KaijuDamaged:
                    if (e.A >= 0 && e.A < _n) _r.KaijuDamage[ClsOf(w, e.A)] += e.Value;
                    break;
                case SimEventType.DeliverBiomass:
                    if (e.A >= 0 && e.A < _n) _r.Delivered[ClsOf(w, e.A)] += e.Value;
                    break;
                case SimEventType.HutLevel:
                    {
                        int team = e.A, lvl = (int)Math.Round(e.Value);
                        if (team >= 0 && team < 2 && lvl >= 1 && lvl <= 5 && double.IsNaN(_r.HutAt[team, lvl - 1])) _r.HutAt[team, lvl - 1] = w.Match.Time;
                    }
                    break;
                case SimEventType.WallBuilt:
                    if (e.B >= 0) _r.WallsBuilt++;
                    break;
                case SimEventType.WallBroken: _r.WallsBroken++; break;
                case SimEventType.PiecePlaced: _r.PiecesPlaced++; break;
                case SimEventType.PieceDestroyed: _r.PiecesDestroyed++; break;
                case SimEventType.BarricadePlaced: _r.BarricadesPlaced++; break;
                case SimEventType.BarricadeBroken: _r.BarricadesBroken++; break;
                case SimEventType.Topple:
                    if (e.Value >= 0) _r.Topples++;
                    break;
                case SimEventType.Hatch:
                    _r.Hatched = true;
                    break;
            }
        }

        /// <summary>Closes the match: result, Kaiju, objective credit (Actor.HillSeconds), top-10 shares and tick cost.</summary>
        public MatchRecord Finish(World w)
        {
            MatchState m = w.Match;
            CloseHold(m.Time);
            _r.Ticks = w.Tick;
            _r.Length = m.Time;
            _r.Winner = m.Winner;
            _r.HillAzure = m.HillSeconds.Azure;
            _r.HillCrimson = m.HillSeconds.Crimson;
            _r.OwnedShare = _samples > 0 ? (double)_ownedSamples / _samples : 0;
            if (m.Hatched) _r.Hatched = true;
            if (_r.Hatched)
            {
                _r.KaijuType = HatchType ?? (w.KaijuKind != null ? w.KaijuKind.Type.ToString() : KaijuTypeName.Elder);
                _r.KaijuOwner = HatchOwner ?? w.Kaiju.State;
            }
            for (int i = 0; i < _n; i++) _r.Objective[(int)w.Actors[i].Cls] += w.Actors[i].HillSeconds;
            _r.Top10DamageShare = TopShare(_damage, 10);
            _r.Top10KillShare = TopShare(_kills, 10);
            if (_tickUs.Count > 0)
            {
                double sum = 0;
                foreach (double v in _tickUs) sum += v;
                _r.TickMeanUs = sum / _tickUs.Count;
                var s = new List<double>(_tickUs);
                s.Sort();
                _r.TickP99Us = s[Math.Min(s.Count - 1, (int)Math.Ceiling(0.99 * s.Count) - 1)];
            }
            return _r;
        }

        /// <summary>The Kaiju's side the tick it hatched (set by the runner the first tick Match.Hatched reads true).</summary>
        public KaijuState? HatchOwner;
        public string HatchType;

        public void WatchHatch(World w)
        {
            if (HatchOwner == null && w.Match.Hatched)
            {
                HatchOwner = w.Kaiju.State;
                HatchType = w.KaijuKind != null ? w.KaijuKind.Type.ToString() : KaijuTypeName.Elder;
                _r.Hatched = true;
                _r.KaijuType = HatchType;
            }
        }

        public static double TopShare(double[] v, int k)
        {
            double total = 0;
            foreach (double x in v) total += x;
            if (!(total > 0)) return double.NaN;
            var s = new List<double>(v);
            s.Sort();
            s.Reverse();
            double top = 0;
            for (int i = 0; i < Math.Min(k, s.Count); i++) top += s[i];
            return top / total;
        }

        static class KaijuTypeName
        {
            public const string Elder = "Elder";
        }
    }

    /// <summary>
    /// b15 tools: "Path=Value" overrides on a GameConfig by reflection (the switch sets of the balance report), for example
    /// Depth.Hut.On=false, Classes[0].AttackDamage=24, Classes[Warrior].MaxHp=170, Depth.Hut.LevelCost=6,10,14,18,20 or
    /// Depth.Hut.LevelCost[4]=20. Unknown paths and unparsable values throw ArgumentException naming the path.
    /// </summary>
    public static class ConfigOverrides
    {
        public static void Apply(GameConfig cfg, IEnumerable<string> sets)
        {
            if (sets == null) return;
            foreach (string s in sets)
            {
                if (string.IsNullOrWhiteSpace(s)) continue;
                int eq = s.IndexOf('=');
                if (eq <= 0) throw new ArgumentException("override '" + s + "': expected Path=Value");
                Set(cfg, s.Substring(0, eq).Trim(), s.Substring(eq + 1).Trim());
            }
        }

        public static void Set(object root, string path, string value)
        {
            string[] parts = path.Split('.');
            object obj = root;
            for (int p = 0; p < parts.Length; p++)
            {
                string part = parts[p];
                string name = part;
                string index = null;
                int lb = part.IndexOf('[');
                if (lb >= 0)
                {
                    if (!part.EndsWith("]", StringComparison.Ordinal)) throw new ArgumentException("override '" + path + "': bad index in '" + part + "'");
                    name = part.Substring(0, lb);
                    index = part.Substring(lb + 1, part.Length - lb - 2);
                }
                FieldInfo f = obj.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (f == null) throw new ArgumentException("override '" + path + "': no field '" + name + "' on " + obj.GetType().Name);
                bool last = p == parts.Length - 1;
                if (index == null)
                {
                    if (last)
                    {
                        f.SetValue(obj, Parse(f.FieldType, value, path));
                        return;
                    }
                    object next = f.GetValue(obj);
                    if (next == null) throw new ArgumentException("override '" + path + "': '" + name + "' is null");
                    obj = next;
                    continue;
                }
                if (!(f.GetValue(obj) is Array arr)) throw new ArgumentException("override '" + path + "': '" + name + "' is not an array");
                int k = ParseIndex(index, path);
                if (k < 0 || k >= arr.Length) throw new ArgumentException("override '" + path + "': index " + k + " outside 0.." + (arr.Length - 1));
                if (last)
                {
                    arr.SetValue(Parse(f.FieldType.GetElementType(), value, path), k);
                    return;
                }
                obj = arr.GetValue(k);
                if (obj == null) throw new ArgumentException("override '" + path + "': element " + k + " is null");
            }
        }

        static int ParseIndex(string s, string path)
        {
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k)) return k;
            if (Enum.TryParse(s, true, out ClassId c)) return (int)c;
            if (Enum.TryParse(s, true, out TeamId t)) return (int)t;
            throw new ArgumentException("override '" + path + "': index '" + s + "' is not a number, class or team");
        }

        static object Parse(Type t, string v, string path)
        {
            try
            {
                if (t == typeof(double)) return double.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (t == typeof(float)) return float.Parse(v, NumberStyles.Float, CultureInfo.InvariantCulture);
                if (t == typeof(int)) return int.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (t == typeof(long)) return long.Parse(v, NumberStyles.Integer, CultureInfo.InvariantCulture);
                if (t == typeof(bool)) return bool.Parse(v);
                if (t == typeof(string)) return v;
                if (t.IsEnum) return Enum.Parse(t, v, true);
                if (t.IsArray)
                {
                    string[] items = v.Trim('[', ']', ' ').Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                    Type et = t.GetElementType();
                    Array a = Array.CreateInstance(et, items.Length);
                    for (int i = 0; i < items.Length; i++) a.SetValue(Parse(et, items[i].Trim(), path), i);
                    return a;
                }
            }
            catch (FormatException)
            {
                throw new ArgumentException("override '" + path + "': cannot read '" + v + "' as " + t.Name);
            }
            throw new ArgumentException("override '" + path + "': type " + t.Name + " cannot be set from text");
        }
    }

    /// <summary>b15 tools: plays the matches the balance report measures (read-only use of the sim).</summary>
    public static class BalanceRunner
    {
        /// <summary>The balance tests' config (GameConfig.CreateBrowser5v5().ApplyRules(profile)) with the overrides on top.</summary>
        public static GameConfig Config(RulesProfile profile, IEnumerable<string> sets)
        {
            GameConfig cfg = GameConfig.CreateBrowser5v5().ApplyRules(profile);
            ConfigOverrides.Apply(cfg, sets);
            return cfg;
        }

        public static WorldOptions Options(GameConfig cfg, int seed, int teamSize) => new WorldOptions
        {
            Seed = seed,
            TeamSize = teamSize,
            HumanTeam = TeamId.Azure,
            HumanClass = ClassId.Warrior,
            Config = cfg,
        };

        /// <summary>One full bot match (actor 0 on autopilot, the soak harness order); maxSeconds 0 = the whole match.</summary>
        public static MatchRecord BotMatch(GameConfig cfg, int seed, int teamSize, double maxSeconds = 0, Action<World> onStart = null, Action<World> onEnd = null)
        {
            World w = World.CreateWorld(Options(cfg, seed, teamSize));
            var probe = new BalanceProbe();
            probe.Begin(w, "bot seed " + seed.ToString(CultureInfo.InvariantCulture), seed);
            InputFrame scratch = SimInput.EmptyInput();
            int max = (int)Math.Ceiling((maxSeconds > 0 ? Math.Min(maxSeconds, cfg.MatchSeconds) : cfg.MatchSeconds) * cfg.SimHz) + (maxSeconds > 0 ? 0 : (int)cfg.SimHz);
            double usPerTick = 1e6 / Stopwatch.Frequency;
            onStart?.Invoke(w);
            try
            {
                for (int t = 0; t < max && w.Match.Phase != MatchPhase.Ended; t++)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    MatchTick.Advance(w, null, scratch);
                    double us = (Stopwatch.GetTimestamp() - t0) * usPerTick;
                    probe.WatchHatch(w);
                    probe.AfterTick(w, us);
                }
            }
            finally
            {
                onEnd?.Invoke(w);
            }
            return probe.Finish(w);
        }

        /// <summary>Re-simulates a saved replay with the same probe; Desynced is set when playback stopped at a fingerprint.</summary>
        public static MatchRecord Replay(ReplayData data, string source)
        {
            var player = new ReplayPlayer(data);
            World w = player.World;
            var probe = new BalanceProbe();
            probe.Begin(w, source, data.Header.Start != null ? data.Header.Start.Seed : 0);
            double usPerTick = 1e6 / Stopwatch.Frequency;
            while (true)
            {
                long t0 = Stopwatch.GetTimestamp();
                if (!player.Step()) break;
                double us = (Stopwatch.GetTimestamp() - t0) * usPerTick;
                probe.WatchHatch(w);
                probe.AfterTick(w, us);
            }
            MatchRecord r = probe.Finish(w);
            r.Desynced = player.Desynced;
            return r;
        }
    }

    /// <summary>
    /// b15 tools: the 6x6 duel tables, played with no bots on an empty Skybreak Basin (every other body removed, as DepthKit.Field
    /// does). TTK: row class attacks a passive full-HP column class (primary attack held, aimed at the target, walking in to 0.8 x
    /// its range); time from the first press to the death. Duel: both do that to each other; the survivor wins, a timeout is a
    /// draw. Each cell plays Reps start angles around the hill centre. No abilities, no dodge, no hut levels.
    /// </summary>
    public static class DuelRunner
    {
        public const double MaxSeconds = 30;
        public const double StartGap = 3.0;

        public sealed class Cell
        {
            public readonly List<double> Ttk = new List<double>();
            public int Kills, Reps;
            public int Wins, Losses, Draws;
        }

        public static Cell[,] Ttk(RulesProfile profile, IEnumerable<string> sets, int reps) => Play(profile, sets, reps, false);
        public static Cell[,] Duels(RulesProfile profile, IEnumerable<string> sets, int reps) => Play(profile, sets, reps, true);

        static Cell[,] Play(RulesProfile profile, IEnumerable<string> sets, int reps, bool both)
        {
            var cells = new Cell[6, 6];
            for (int a = 0; a < 6; a++)
                for (int b = 0; b < 6; b++)
                {
                    var cell = new Cell();
                    for (int k = 0; k < reps; k++) One(profile, sets, (ClassId)a, (ClassId)b, 2 * Math.PI * k / reps, both, cell);
                    cells[a, b] = cell;
                }
            return cells;
        }

        static void One(RulesProfile profile, IEnumerable<string> sets, ClassId ca, ClassId cb, double angle, bool both, Cell cell)
        {
            GameConfig cfg = BalanceRunner.Config(profile, sets);
            World w = World.CreateWorld(BalanceRunner.Options(cfg, 4242, 5));
            int ia = 0, ib = w.ActorCount / 2;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == ia || j == ib) continue;
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1;
                w.Depth.WasAlive[j] = 0;
            }
            double cx = w.Map.Hill.X, cz = w.Map.Hill.Z;
            Place(w, ia, ca, cx - Math.Cos(angle) * StartGap / 2, cz - Math.Sin(angle) * StartGap / 2);
            Place(w, ib, cb, cx + Math.Cos(angle) * StartGap / 2, cz + Math.Sin(angle) * StartGap / 2);
            InputFrame fa = SimInput.EmptyInput(), fb = SimInput.EmptyInput(), idle = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(MaxSeconds * cfg.SimHz);
            int start = w.Tick;
            cell.Reps++;
            for (int t = 0; t < max; t++)
            {
                Drive(w, ia, ib, fa);
                SimInput.ApplyInput(w, ia, fa);
                if (both) Drive(w, ib, ia, fb);
                SimInput.ApplyInput(w, ib, both ? fb : idle);
                for (int j = 0; j < w.ActorCount; j++)
                    if (j != ia && j != ib) SimInput.ApplyInput(w, j, idle);
                SimStep.Step(w);
                bool aDead = !w.Actors[ia].Alive, bDead = !w.Actors[ib].Alive;
                if (!aDead && !bDead) continue;
                double secs = (w.Tick - start) / cfg.SimHz;
                if (bDead && !aDead)
                {
                    cell.Kills++;
                    cell.Ttk.Add(secs);
                    cell.Wins++;
                }
                else if (aDead && !bDead) cell.Losses++;
                else cell.Draws++;
                return;
            }
            if (both) cell.Draws++;
        }

        static void Place(World w, int j, ClassId cls, double x, double z)
        {
            Actor a = w.Actors[j];
            a.Cls = cls;
            a.MaxHp = w.Config.Class(cls).MaxHp;
            a.Hp = a.MaxHp;
            a.Alive = true;
            a.SpawnProtect = 0;
            a.StandUp = 0;
            a.AirLen = 0;
            w.X[j] = x;
            w.Z[j] = z;
            w.PrevX[j] = x;
            w.PrevZ[j] = z;
            w.Vx[j] = 0;
            w.Vz[j] = 0;
        }

        // ---------------------------------------------------------------------------------------------- b16 duel order

        /// <summary>
        /// b16 (design advisor 2026-11-21): row a beats column b when it wins more than half of all its tries against b, both
        /// seatings counted (Duels[a, b] wins plus Duels[b, a] losses).
        /// </summary>
        public static bool Beats(Cell[,] duels, int a, int b)
        {
            Cell ab = duels[a, b], ba = duels[b, a];
            int wins = ab.Wins + ba.Losses, tries = ab.Reps + ba.Reps;
            return tries > 0 && wins * 2 > tries;
        }

        /// <summary>The middle classes of the duel order: neither the fighter nor the Healer.</summary>
        public static readonly int[] Middle = { (int)ClassId.Farmer, (int)ClassId.Miner, (int)ClassId.Demolitions, (int)ClassId.Builder };

        /// <summary>
        /// b16 duel order, the advisor's rule: the Warrior beats every class; every class beats the Healer; among the Miner,
        /// Builder, Farmer and Demolitions none is unbeaten (no middle class beats it) and none is winless (it beats no middle
        /// class). Returns the number of broken conditions (0 = the order holds) and names each one in why.
        /// </summary>
        public static int OrderViolations(Cell[,] duels, out string why)
        {
            var parts = new List<string>();
            int w = (int)ClassId.Warrior, h = (int)ClassId.Healer;
            for (int b = 0; b < 6; b++)
                if (b != w && !Beats(duels, w, b)) parts.Add("Warrior does not beat " + (ClassId)b);
            for (int a = 0; a < 6; a++)
                if (a != h && !Beats(duels, a, h)) parts.Add((ClassId)a + " does not beat Healer");
            foreach (int m in Middle)
            {
                int wins = 0, losses = 0;
                foreach (int o in Middle)
                {
                    if (o == m) continue;
                    if (Beats(duels, m, o)) wins++;
                    if (Beats(duels, o, m)) losses++;
                }
                if (losses == 0) parts.Add((ClassId)m + " unbeaten among the middle four");
                if (wins == 0) parts.Add((ClassId)m + " winless among the middle four");
            }
            why = string.Join("; ", parts);
            return parts.Count;
        }

        // ---------------------------------------------------------------------------------------------- b16 escape

        public const double EscapeGap = 4.0, EscapeGoal = 20.0, EscapeMaxSeconds = 12;

        /// <summary>
        /// b16 scripted chase (design advisor 2026-11-21: "their speed should be a touch slower so the non fighter has a chance of
        /// escape"): on the empty map a Warrior stands at the hill centre and a full-HP class 4 u from him along one of reps
        /// angles; the runner flees straight away from the Warrior, the Warrior chases (aims at the runner, swings only when
        /// it is in reach, dodges at it once the dash would bring it into reach). runnerDodges: the runner also dodges away when
        /// the Warrior is within his reach + 1 u. Escape = the runner is alive and 20 u from the Warrior's start within 12 s.
        /// Cells indexed by ClassId (the Warrior's own cell stays empty): Wins = escapes, Losses = caught (killed, or not out
        /// in time), Kills = killed.
        /// </summary>
        public static Cell[] Escape(RulesProfile profile, IEnumerable<string> sets, int reps, bool runnerDodges)
        {
            var cells = new Cell[6];
            for (int c = 0; c < 6; c++)
            {
                cells[c] = new Cell();
                if (c == (int)ClassId.Warrior) continue;
                for (int k = 0; k < reps; k++) Chase(profile, sets, (ClassId)c, 2 * Math.PI * (k + 0.5) / reps, runnerDodges, cells[c]);
            }
            return cells;
        }

        static void Chase(RulesProfile profile, IEnumerable<string> sets, ClassId runner, double angle, bool runnerDodges, Cell cell)
        {
            GameConfig cfg = BalanceRunner.Config(profile, sets);
            World w = World.CreateWorld(BalanceRunner.Options(cfg, 4242, 5));
            int iw = 0, ir = w.ActorCount / 2;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == iw || j == ir) continue;
                w.Actors[j].Alive = false;
                w.Actors[j].RespawnIn = 1e9;
                w.MeatDropped[j] = 1;
                w.Depth.WasAlive[j] = 0;
            }
            double cx = w.Map.Hill.X, cz = w.Map.Hill.Z;
            double ux = Math.Cos(angle), uz = Math.Sin(angle);
            Place(w, iw, ClassId.Warrior, cx, cz);
            Place(w, ir, runner, cx + ux * EscapeGap, cz + uz * EscapeGap);
            InputFrame fw = SimInput.EmptyInput(), fr = SimInput.EmptyInput(), idle = SimInput.EmptyInput();
            int max = (int)Math.Ceiling(EscapeMaxSeconds * cfg.SimHz);
            double reachW = cfg.Class(ClassId.Warrior).AttackRange + cfg.Class(runner).Radius;
            double dodgeD = cfg.Depth.Dodge != null ? cfg.Depth.Dodge.Distance : 0;
            cell.Reps++;
            for (int t = 0; t < max; t++)
            {
                double dx = w.X[ir] - w.X[iw], dz = w.Z[ir] - w.Z[iw];
                double d = Math.Sqrt(dx * dx + dz * dz);
                double nx = d > 1e-9 ? dx / d : ux, nz = d > 1e-9 ? dz / d : uz;
                fw.MoveX = nx; fw.MoveZ = nz; fw.AimX = w.X[ir]; fw.AimZ = w.Z[ir];
                fw.Attack = d <= reachW;
                fw.Dodge = d > reachW && d <= reachW + dodgeD;
                fr.MoveX = nx; fr.MoveZ = nz; fr.AimX = w.X[ir] + nx; fr.AimZ = w.Z[ir] + nz;
                fr.Attack = false;
                fr.Dodge = runnerDodges && d <= reachW + 1.0;
                SimInput.ApplyInput(w, iw, fw);
                SimInput.ApplyInput(w, ir, fr);
                for (int j = 0; j < w.ActorCount; j++)
                    if (j != iw && j != ir) SimInput.ApplyInput(w, j, idle);
                SimStep.Step(w);
                if (!w.Actors[ir].Alive)
                {
                    cell.Kills++;
                    cell.Losses++;
                    return;
                }
                double ox = w.X[ir] - cx, oz = w.Z[ir] - cz;
                if (ox * ox + oz * oz >= EscapeGoal * EscapeGoal)
                {
                    cell.Wins++;
                    return;
                }
            }
            cell.Losses++;
        }

        static void Drive(World w, int self, int target, InputFrame f)
        {
            double dx = w.X[target] - w.X[self], dz = w.Z[target] - w.Z[self];
            double d = Math.Sqrt(dx * dx + dz * dz);
            double reach = 0.8 * w.Config.Class(w.Actors[self].Cls).AttackRange;
            f.MoveX = d > reach && d > 1e-9 ? dx / d : 0;
            f.MoveZ = d > reach && d > 1e-9 ? dz / d : 0;
            f.AimX = w.X[target];
            f.AimZ = w.Z[target];
            f.Attack = true;
        }
    }
}
