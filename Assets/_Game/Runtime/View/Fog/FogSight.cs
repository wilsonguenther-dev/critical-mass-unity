using System;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;

namespace CriticalMass.Runtime.View.Fog
{
    /// <summary>One sight disc on the sim ground (sim units).</summary>
    public struct FogSource
    {
        public double X, Z, R;
        public FogSource(double x, double z, double r) { X = x; Z = z; R = r; }
    }

    /// <summary>
    /// Batch 12 item P: the pure sight function (no Unity calls, no allocation). Reads the sim, never writes it.
    /// The visibility map is FogConfig.Resolution square, cell (cx, cz) at index cz * res + cx, covering sim x in
    /// [0, map.Width) and z in [0, map.Height) (texture row 0 = sim z 0, so texture uv = (x / Width, z / Height)).
    /// A cell's byte is 255 well inside a disc, 0 well outside, with a soft edge FogConfig.SoftCells wide centred on the
    /// exact radius (so byte >= FogConfig.SeenByte exactly when the cell centre is within the radius).
    /// </summary>
    public static class FogSight
    {
        /// <summary>The held-hill state of a team.</summary>
        public static HillState HeldBy(TeamId team) => team == TeamId.Azure ? HillState.Azure : HillState.Crimson;

        /// <summary>The sight radius of a team's base: its pad, station and bank zones, at least FogConfig.BaseSight.</summary>
        public static double BaseRadius(World w, TeamId team)
        {
            double r = FogConfig.BaseSight;
            PadDef pad = w.Map.Pads != null && (int)team < w.Map.Pads.Length ? w.Map.Pads[(int)team] : null;
            if (pad != null && pad.R > r) r = pad.R;
            if (w.Config != null && w.Config.Depth != null)
            {
                if (w.Config.Depth.Stations != null && w.Config.Depth.Stations.Range > r) r = w.Config.Depth.Stations.Range;
                if (w.Config.Depth.Sources != null && w.Config.Depth.Sources.BankRange > r) r = w.Config.Depth.Sources.BankRange;
            }
            return r;
        }

        /// <summary>
        /// Fills buf with team's sight discs: every living teammate (SightTeammate), the hill while held (SightHeldHill),
        /// the base. Returns the count (at most buf.Length).
        /// </summary>
        public static int Gather(World w, TeamId team, FogSource[] buf)
        {
            int n = 0;
            for (int i = 0; i < w.ActorCount && n < buf.Length; i++)
            {
                Actor a = w.Actors[i];
                if (a == null || !a.Alive || a.Team != team) continue;
                buf[n++] = new FogSource(w.X[i], w.Z[i], KaijuTypes.SightRadiusFor(w, i, FogConfig.SightTeammate));   // b13-k: SHROUD shrinks an affected teammate inside
            }
            if (n < buf.Length && w.Map.Hill != null && w.Match.HillState == HeldBy(team))
                buf[n++] = new FogSource(w.Map.Hill.X, w.Map.Hill.Z, FogConfig.SightHeldHill);
            PadDef pad = w.Map.Pads != null && (int)team < w.Map.Pads.Length ? w.Map.Pads[(int)team] : null;
            if (n < buf.Length && pad != null) buf[n++] = new FogSource(pad.X, pad.Z, BaseRadius(w, team));
            return n;
        }

        /// <summary>
        /// b13-k: true when MIRE's SHROUD hides actor target from team: the team is affected, the target stands inside the
        /// cloud, and no teammate standing inside the cloud is within its (shrunk) sight of it. Viewers outside never see in.
        /// </summary>
        public static bool CloudHides(World w, TeamId team, FogSource[] src, int n, int target)
        {
            if (!KaijuTypes.CloudActive(w, out double cx, out double cz, out double cr)) return false;
            if (!(target >= 0 && target < w.ActorCount) || !KaijuTypes.AffectsTeam(w, team)) return false;
            double tx = w.X[target] - cx, tz = w.Z[target] - cz;
            if (tx * tx + tz * tz > cr * cr) return false;
            for (int i = 0; i < n; i++)
            {
                double sx = src[i].X - cx, sz = src[i].Z - cz;
                if (sx * sx + sz * sz > cr * cr) continue;
                double dx = w.X[target] - src[i].X, dz = w.Z[target] - src[i].Z;
                if (dx * dx + dz * dz <= src[i].R * src[i].R) return false;
            }
            return true;
        }

        /// <summary>True when (x, z) is within any disc's exact radius (the enemy test; no grid rounding).</summary>
        public static bool SeenAt(FogSource[] src, int n, double x, double z)
        {
            for (int k = 0; k < n; k++)
            {
                double dx = x - src[k].X, dz = z - src[k].Z;
                if (dx * dx + dz * dz <= src[k].R * src[k].R) return true;
            }
            return false;
        }

        /// <summary>A cell byte counts as seen.</summary>
        public static bool Marked(byte v) => v >= FogConfig.SeenByte;

        /// <summary>Clears outBuf (res * res) and stamps every disc into it (max of the discs).</summary>
        public static void Compute(FogSource[] src, int n, double mapW, double mapH, int res, byte[] outBuf)
        {
            Array.Clear(outBuf, 0, res * res);
            double csx = mapW / res, csz = mapH / res;
            double soft = FogConfig.SoftCells * Math.Min(csx, csz);
            for (int k = 0; k < n; k++)
            {
                double x = src[k].X, z = src[k].Z, r = src[k].R, reach = r + soft;
                int x0 = Math.Max(0, (int)Math.Floor((x - reach) / csx)), x1 = Math.Min(res - 1, (int)Math.Floor((x + reach) / csx));
                int z0 = Math.Max(0, (int)Math.Floor((z - reach) / csz)), z1 = Math.Min(res - 1, (int)Math.Floor((z + reach) / csz));
                for (int cz = z0; cz <= z1; cz++)
                {
                    double dz = (cz + 0.5) * csz - z;
                    int row = cz * res;
                    for (int cx = x0; cx <= x1; cx++)
                    {
                        double dx = (cx + 0.5) * csx - x;
                        double v = (r - Math.Sqrt(dx * dx + dz * dz)) / soft + 0.5;
                        if (v <= 0) continue;
                        byte b = v >= 1 ? (byte)255 : (byte)(v * 255.0 + 0.5);
                        if (b > outBuf[row + cx]) outBuf[row + cx] = b;
                    }
                }
            }
        }
    }

    /// <summary>
    /// The 10 Hz clock: Step(dt) is true once per FogConfig.UpdateSeconds of accumulated time. A long stall fires once,
    /// not a burst. Pure.
    /// </summary>
    public struct FogClock
    {
        double _acc;

        public bool Step(double dt)
        {
            if (dt > 0) _acc += dt;
            if (_acc + 1e-6 < FogConfig.UpdateSeconds) return false;
            _acc -= FogConfig.UpdateSeconds;
            if (_acc > FogConfig.UpdateSeconds) _acc = 0;
            return true;
        }

        public void Reset() => _acc = 0;
    }

    /// <summary>
    /// The visibility state one machine keeps for its own team, preallocated: the sight map (Target), the faded map the
    /// textures show (Shown, void cells clear so the sky beyond the islands is not hazed), and which actors are seen.
    /// Update allocates nothing once warmed up (the source and actor buffers grow only if the roster does).
    /// </summary>
    public sealed class FogGrid
    {
        public readonly int Res;
        /// <summary>This update's sight (FogSight.Compute) with void cells lifted to 255.</summary>
        public readonly byte[] Target;
        /// <summary>The map drawn: moves toward Target by FogConfig.FadeStep per update.</summary>
        public readonly byte[] Shown;
        /// <summary>Raw sight of the last update (before the void lift), for tests and the capture log.</summary>
        public readonly byte[] Sight;
        readonly byte[] _void;
        FogSource[] _src = new FogSource[40];
        bool[] _seen = new bool[40];
        MapDef _voidMap;
        int _voidVersion = int.MinValue;

        public int SourceCount { get; private set; }
        public FogSource[] Sources => _src;
        public TeamId Team { get; private set; }

        public FogGrid(int res = FogConfig.Resolution)
        {
            Res = res;
            Target = new byte[res * res];
            Shown = new byte[res * res];
            Sight = new byte[res * res];
            _void = new byte[res * res];
        }

        /// <summary>True when actor i is a teammate, or stood inside sight at the last update.</summary>
        public bool Seen(int i) => i >= 0 && i < _seen.Length && _seen[i];

        /// <summary>One update for team. snap = no fade (the first update after the fog turns on).</summary>
        public void Update(World w, TeamId team, bool snap)
        {
            Team = team;
            if (_src.Length < w.ActorCount + 2) _src = new FogSource[w.ActorCount + 2];
            if (_seen.Length < w.ActorCount) _seen = new bool[w.ActorCount];
            MapDef map = w.Map;
            SourceCount = FogSight.Gather(w, team, _src);
            FogSight.Compute(_src, SourceCount, map.Width, map.Height, Res, Sight);
            if (map != _voidMap || w.GridVersion != _voidVersion) BuildVoid(map, w.GridVersion);
            int all = Res * Res;
            int step = FogConfig.FadeStep;
            for (int c = 0; c < all; c++)
            {
                byte t = Sight[c] > _void[c] ? Sight[c] : _void[c];
                Target[c] = t;
                if (snap) { Shown[c] = t; continue; }
                int s = Shown[c];
                Shown[c] = (byte)(s < t ? Math.Min(t, s + step) : Math.Max(t, s - step));
            }
            for (int i = 0; i < _seen.Length; i++)
            {
                Actor a = i < w.ActorCount ? w.Actors[i] : null;
                // a body (alive, or a corpse still drawn) is seen where it lies; teammates always
                _seen[i] = a != null && (a.Team == team || (FogSight.SeenAt(_src, SourceCount, w.X[i], w.Z[i]) && !FogSight.CloudHides(w, team, _src, SourceCount, i)));   // b13-k: SHROUD
            }
        }

        void BuildVoid(MapDef map, int version)
        {
            _voidMap = map;
            _voidVersion = version;
            double csx = (double)map.Width / Res, csz = (double)map.Height / Res;
            for (int cz = 0; cz < Res; cz++)
                for (int cx = 0; cx < Res; cx++)
                {
                    int mx = (int)Math.Floor((cx + 0.5) * csx), mz = (int)Math.Floor((cz + 0.5) * csz);
                    bool v = map.Cells == null || mx < 0 || mz < 0 || mx >= map.Width || mz >= map.Height || map.Cells[mz * map.Width + mx] == Cell.Void;
                    _void[cz * Res + cx] = v ? (byte)255 : (byte)0;
                }
        }
    }
}
