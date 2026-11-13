using System;
using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// -groundShots (automation only, feat/ground-camera evidence): a capture tour of ground view in a live autoplay match,
    /// written as shot-ground-*.png into the data folder (tools/remote-playtest.sh --mode captures copies shot-*.png back).
    /// From 75 s of match time: four stills with the clock held (TimeScale 0; AutoplayArgs.ClockHeld keeps -autoplaySpeed
    /// off it): a duel nearest the hill, a bridge crossing, a base, the biggest crowd; then the auto-director runs with the
    /// clock going and the frame after each of its first five cuts is written (the strip); then the Kaiju once it hatches
    /// (or, by 420 s, whoever is nearest the shrine). Picks only read the world; the clock hold changes when ticks run,
    /// never what they compute.
    /// </summary>
    public sealed class GroundViewShots : MonoBehaviour
    {
        public GroundView View;
        public const double StartSeconds = 75, KaijuDeadline = 420;
        const int SettleFrames = 8;

        enum Phase { Wait, Stills, Strip, Kaiju, Done }
        Phase _phase = Phase.Wait;
        int _still;
        int _frame = -1;
        double _savedScale;
        bool _held;
        int _stripShots, _lastCuts, _cutFrame = -1;
        double _stripStart;
        static readonly string[] StillNames = { "1-hill-duel", "2-bridge", "3-base", "5-crowd" };

        string Dir => GamePaths.DataDir;

        void Hold(bool on)
        {
            SimDriverBehaviour d = View != null ? View.Driver : null;
            if (d == null || on == _held) return;
            _held = on;
            AutoplayArgs.ClockHeld = on;
            if (on)
            {
                _savedScale = d.TimeScale > 0 ? d.TimeScale : 2;
                d.TimeScale = 0;
            }
            else d.TimeScale = _savedScale;
        }

        void OnDestroy()
        {
            if (_held) AutoplayArgs.ClockHeld = false;
        }

        void Shot(string name)
        {
            string path = Path.Combine(Dir, "shot-ground-" + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[CM.Ground] {path} target={View.Target} yaw={View.Follow.Yaw:F0} pitch={View.Follow.Pitch:F0} dist={View.Follow.Dist:F2}");
        }

        void Update()
        {
            if (View == null || View.Driver == null || View.Driver.World == null) return;
            World w = View.Driver.World;
            if (w.Match.Phase == MatchPhase.Ended) { if (_held) Hold(false); return; }
            switch (_phase)
            {
                case Phase.Wait:
                    if (w.Match.Time >= StartSeconds && View.Mode == GroundViewMode.Watch) { _phase = Phase.Stills; _still = 0; _frame = -1; }
                    break;
                case Phase.Stills:
                    StepStill(w);
                    break;
                case Phase.Strip:
                    StepStrip(w);
                    break;
                case Phase.Kaiju:
                    StepKaiju(w);
                    break;
            }
        }

        void StepStill(World w)
        {
            if (_frame < 0)
            {
                Hold(true);
                if (!PickStill(w, _still, out int id, out double yaw))
                {
                    Debug.Log($"[CM.Ground] no subject for {StillNames[_still]}");
                    NextStill();
                    return;
                }
                View.Pick(id);
                _pendingYaw = yaw;
                _frame = 0;
                return;
            }
            _frame++;
            if (_frame == 2) View.Aim(_pendingYaw, GroundCamera.PitchDeg);   // after the cut's snap
            if (_frame == SettleFrames && CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying) { Hold(false); _frame--; return; }
            if (_frame == SettleFrames) { Hold(true); Shot(StillNames[_still]); }
            if (_frame >= SettleFrames + 2) NextStill();
        }

        double _pendingYaw;
        double _hatchSeen = double.NaN;

        void NextStill()
        {
            _still++;
            _frame = -1;
            if (_still < StillNames.Length) return;
            Hold(false);
            View.SetDirector(true);
            _lastCuts = View.Director.Cuts;
            _stripStart = Time.unscaledTimeAsDouble;
            _phase = Phase.Strip;
        }

        void StepStrip(World w)
        {
            if (View.Director.Cuts != _lastCuts)
            {
                _lastCuts = View.Director.Cuts;
                _cutFrame = 0;
            }
            if (_cutFrame >= 0 && ++_cutFrame >= 20 && !CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying)
            {
                _cutFrame = -1;
                _stripShots++;
                Shot("strip-" + _stripShots);
            }
            if (_stripShots >= 5 || Time.unscaledTimeAsDouble - _stripStart > 120)
            {
                _phase = Phase.Kaiju;
                _frame = -1;
            }
        }

        void StepKaiju(World w)
        {
            bool hatched = GroundDirector.Valid(w, GroundDirector.KaijuTarget);
            if (_frame < 0)
            {
                if (!hatched && w.Match.Time < KaijuDeadline) return;
                // Past the hatch's own takeover (band, flash, dim): the Kaiju itself, not the cut-in.
                if (hatched && double.IsNaN(_hatchSeen)) _hatchSeen = w.Match.Time;
                if (hatched && (w.Match.Time < _hatchSeen + 8 || CriticalMass.Runtime.View.Moments.BigMoments.IsPlaying)) return;
                Hold(true);
                if (hatched) View.Pick(GroundDirector.KaijuTarget);
                else
                {
                    int id = Nearest(w, w.Map.Shrine != null ? w.Map.Shrine.X : 0, w.Map.Shrine != null ? w.Map.Shrine.Z : 0);
                    if (id >= 0) View.Pick(id);
                }
                _frame = 0;
                return;
            }
            _frame++;
            if (_frame == 2 && !hatched && w.Map.Shrine != null && GroundDirector.Valid(w, View.Target))
                View.Aim(GroundCamera.YawToward(w.X[View.Target], w.Z[View.Target], w.Map.Shrine.X, w.Map.Shrine.Z, View.Follow.Yaw), GroundCamera.PitchDeg);
            if (_frame == SettleFrames) Shot(hatched ? "4-kaiju" : "4-kaiju-shrine");
            if (_frame >= SettleFrames + 2)
            {
                Hold(false);
                View.SetDirector(true);
                _phase = Phase.Done;
            }
        }

        static int Nearest(World w, double x, double z)
        {
            int best = -1;
            double bd = double.MaxValue;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (!w.Actors[i].Alive) continue;
                double dx = w.X[i] - x, dz = w.Z[i] - z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>The subject and camera yaw of still k (false when nobody fits).</summary>
        static bool PickStill(World w, int k, out int id, out double yaw)
        {
            id = -1;
            yaw = 0;
            HillDef hill = w.Map.Hill;
            switch (k)
            {
                case 0:   // a duel nearest the hill: look from behind one fighter at his opponent
                {
                    double best = double.MaxValue;
                    int foe = -1;
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        if (!w.Actors[i].Alive) continue;
                        int e = NearestEnemy(w, i, GroundDirector.DuelRange * 1.6);
                        if (e < 0) continue;
                        double dx = w.X[i] - hill.X, dz = w.Z[i] - hill.Z, d = dx * dx + dz * dz;
                        if (d < best) { best = d; id = i; foe = e; }
                    }
                    if (id < 0) return false;
                    yaw = GroundCamera.YawToward(w.X[id], w.Z[id], w.X[foe], w.Z[foe], 0);
                    return true;
                }
                case 1:   // on a bridge (else nearest one): look along the span the way he faces
                {
                    double best = double.MaxValue;
                    BridgeSpan bs = null;
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        if (!w.Actors[i].Alive) continue;
                        foreach (BridgeSpan sp in BridgeSpans.All)
                        {
                            double along = BridgeSpans.Along(sp, w.X[i], w.Z[i]);
                            double lat = Math.Abs(BridgeSpans.Lateral(sp, w.X[i], w.Z[i]));
                            double off = (along < 0 ? -along : along > sp.Len ? along - sp.Len : 0) + Math.Max(0, lat - SkybreakBasinMap.BridgeHalfWidth);
                            // Mid-span beats the ends: the deck and the drop on both sides are the point of the shot.
                            double score = off * 10 + Math.Abs(along - sp.Len * 0.5) * 0.2;
                            if (score < best) { best = score; id = i; bs = sp; }
                        }
                    }
                    if (id < 0 || bs == null) return false;
                    double fx = Math.Cos(w.Actors[id].Facing), fz = Math.Sin(w.Actors[id].Facing);
                    double s = fx * bs.Ux + fz * bs.Uz >= 0 ? 1 : -1;
                    yaw = GroundCamera.YawToward(0, 0, bs.Ux * s, bs.Uz * s, 0);
                    return true;
                }
                case 2:   // a base: nearest a pad, looking from the hill side into the base
                {
                    double best = double.MaxValue;
                    PadDef pad = null;
                    foreach (PadDef p in w.Map.Pads)
                    {
                        if (p == null) continue;
                        for (int i = 0; i < w.ActorCount; i++)
                        {
                            if (!w.Actors[i].Alive) continue;
                            double dx = w.X[i] - p.X, dz = w.Z[i] - p.Z, d = dx * dx + dz * dz;
                            if (d < best) { best = d; id = i; pad = p; }
                        }
                    }
                    if (id < 0 || pad == null) return false;
                    yaw = GroundCamera.YawToward(hill.X, hill.Z, pad.X, pad.Z, 0);
                    return true;
                }
                default:  // the biggest crowd: look at its middle
                {
                    int most = 0;
                    for (int i = 0; i < w.ActorCount; i++)
                    {
                        if (!w.Actors[i].Alive) continue;
                        int n = 0;
                        for (int j = 0; j < w.ActorCount; j++)
                        {
                            if (j == i || !w.Actors[j].Alive) continue;
                            double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i];
                            if (dx * dx + dz * dz <= 64) n++;
                        }
                        if (n > most) { most = n; id = i; }
                    }
                    if (id < 0) return false;
                    double cx = 0, cz = 0;
                    int c = 0;
                    for (int j = 0; j < w.ActorCount; j++)
                    {
                        if (j == id || !w.Actors[j].Alive) continue;
                        double dx = w.X[j] - w.X[id], dz = w.Z[j] - w.Z[id];
                        if (dx * dx + dz * dz > 64) continue;
                        cx += w.X[j];
                        cz += w.Z[j];
                        c++;
                    }
                    yaw = c > 0 ? GroundCamera.YawToward(w.X[id], w.Z[id], cx / c, cz / c, 0) : GroundCamera.YawOfFacing(w.Actors[id].Facing);
                    return true;
                }
            }
        }

        static int NearestEnemy(World w, int i, double range)
        {
            int best = -1;
            double bd = range * range;
            for (int j = 0; j < w.ActorCount; j++)
            {
                if (j == i || !w.Actors[j].Alive || w.Actors[j].Team == w.Actors[i].Team) continue;
                double dx = w.X[j] - w.X[i], dz = w.Z[j] - w.Z[i], d = dx * dx + dz * dz;
                if (d <= bd) { bd = d; best = j; }
            }
            return best;
        }
    }
}
