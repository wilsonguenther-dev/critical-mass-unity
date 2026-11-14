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
    /// -followShots (automation only, feat/follow-camera-1 evidence): the third-person and first-person play views in a live
    /// autoplay match, written as shot-follow-*.png into the data folder (tools/remote-playtest.sh --mode captures copies
    /// shot-*.png back). The autopilot drives actor 0; GroundView.ForcePlay lets the play view follow it anyway.
    /// From StartSeconds of match time:
    ///   1. follow-stats.txt: fighters drawn, culled and their rough triangles in TOP-DOWN, THIRD and FIRST at the same held tick;
    ///   2. per view (third, then first): standing, running, turn (the middle frame of a 180-degree turn), hill fight,
    ///      bridge, facing the Kaiju; each waits up to MomentTimeout s of match time for its moment, then shoots anyway
    ///      (the log line says "fallback");
    ///   3. third person only: a 5-frame turn strip (shot-follow-third-turnstrip-1..5) at 0.4 s steps from the turn's start.
    /// Picks only read the world; the clock hold changes when ticks run, never what they compute.
    /// </summary>
    public sealed class FollowCameraShots : MonoBehaviour
    {
        public const string Flag = "-followshots";
        public GroundView View;
        public const double StartSeconds = 60, MomentTimeout = 45;
        const int SettleFrames = 6, StatsFrames = 2 * ViewCull.SampleEvery + 5;

        enum Phase { Wait, Stats, Moments, Done }
        Phase _phase = Phase.Wait;
        int _statsView, _frame = -1, _moment, _pass, _strip = -1;
        double _momentStart, _stripAt, _savedScale;
        bool _held;
        StreamWriter _stats;
        static readonly string[] Moments = { "standing", "running", "turn", "hill-fight", "bridge", "kaiju" };
        static readonly CameraView[] Passes = { CameraView.ThirdPerson, CameraView.FirstPerson };

        string Dir => GamePaths.DataDir;

        void Start()
        {
            GroundView.ForcePlay = true;
            GroundView.ViewOverride = CameraView.ThirdPerson;
        }

        void OnDestroy()
        {
            if (_held) AutoplayArgs.ClockHeld = false;
            GroundView.ForcePlay = false;
            GroundView.ViewOverride = null;
            if (_stats != null) { _stats.Dispose(); _stats = null; }
        }

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

        void Shot(string name, string note)
        {
            string path = Path.Combine(Dir, "shot-follow-" + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[CM.Follow] {path} {note} view={GroundView.ActiveView} yaw={View.Follow.Yaw:F0} pitch={View.Follow.Pitch:F0} dist={View.Follow.Dist:F2} drawn={ViewCull.LastDrawn} culled={ViewCull.LastCulled}");
        }

        void Update()
        {
            if (View == null || View.Driver == null || View.Driver.World == null) return;
            World w = View.Driver.World;
            if (w.Match.Phase == MatchPhase.Ended || !w.Actors[0].Alive) { if (_held) Hold(false); return; }
            switch (_phase)
            {
                case Phase.Wait:
                    if (w.Match.Time >= StartSeconds && View.Mode == GroundViewMode.Play) { _phase = Phase.Stats; _statsView = 0; _frame = -1; }
                    break;
                case Phase.Stats:
                    StepStats(w);
                    break;
                case Phase.Moments:
                    StepMoment(w);
                    break;
            }
        }

        // ---------------------------------------------------------------- 1. what each view submits

        void StepStats(World w)
        {
            if (_frame < 0)
            {
                Hold(true);
                GroundView.ViewOverride = (CameraView)_statsView;
                _frame = 0;
                if (_stats == null)
                {
                    _stats = new StreamWriter(Path.Combine(Dir, "follow-stats.txt"), false);
                    _stats.WriteLine($"tick {w.Tick}  fighter draw distance in the low views {FollowCamera.FighterDrawDistance} u");
                }
                return;
            }
            if (++_frame < StatsFrames) return;
            _stats.WriteLine($"{(CameraView)_statsView}: fighters drawn {ViewCull.LastDrawn}, culled {ViewCull.LastCulled}, rough fighter triangles {ViewCull.SampledTriangles}");
            _frame = -1;
            if (++_statsView <= 2) return;
            _stats.Flush();
            _stats.Dispose();
            _stats = null;
            Hold(false);
            _pass = 0;
            _moment = 0;
            GroundView.ViewOverride = Passes[0];
            _momentStart = w.Match.Time;
            _phase = Phase.Moments;
        }

        // ---------------------------------------------------------------- 2. the moments

        void StepMoment(World w)
        {
            string view = Passes[_pass] == CameraView.ThirdPerson ? "third" : "first";
            if (_strip >= 0) { StepStrip(w, view); return; }
            if (_frame < 0)
            {
                bool timedOut = w.Match.Time - _momentStart > MomentTimeout;
                if (!MomentNow(w, _moment) && !timedOut) return;
                if (Moments[_moment] == "turn" && !timedOut)
                {
                    // The turn: the strip first (clock running), the still is its middle frame.
                    _strip = 0;
                    _stripAt = Time.unscaledTimeAsDouble;
                    return;
                }
                _fallback = timedOut;
                Hold(true);
                _frame = 0;
                return;
            }
            if (++_frame == SettleFrames) Shot(view + "-" + Moments[_moment], _fallback ? "fallback (moment not found in time)" : "");
            if (_frame >= SettleFrames + 2) NextMoment(w);
        }

        bool _fallback;

        void StepStrip(World w, string view)
        {
            if (Time.unscaledTimeAsDouble < _stripAt) return;
            _strip++;
            bool third = Passes[_pass] == CameraView.ThirdPerson;
            if (third) Shot(view + "-turnstrip-" + _strip, "turn strip");
            if (_strip == 3) Shot(view + "-turn", "middle of the turn");
            _stripAt = Time.unscaledTimeAsDouble + 0.4;
            if (_strip >= 5) { _strip = -1; NextMoment(w); }
        }

        void NextMoment(World w)
        {
            Hold(false);
            _frame = -1;
            _moment++;
            _momentStart = w.Match.Time;
            if (_moment < Moments.Length) return;
            _moment = 0;
            _pass++;
            if (_pass < Passes.Length) { GroundView.ViewOverride = Passes[_pass]; return; }
            _phase = Phase.Done;
            Debug.Log("[CM.Follow] done");
        }

        bool MomentNow(World w, int k)
        {
            double hz = w.Config.SimHz > 0 ? w.Config.SimHz : 30;
            double vx = (w.X[0] - w.PrevX[0]) * hz, vz = (w.Z[0] - w.PrevZ[0]) * hz;
            double speed = Math.Sqrt(vx * vx + vz * vz);
            switch (Moments[k])
            {
                case "standing": return speed < 0.3;
                case "running": return speed > 2.5 && Math.Abs(GroundCamera.DeltaAngle(View.Follow.Yaw, GroundCamera.YawToward(0, 0, vx, vz, View.Follow.Yaw))) < 25;
                case "turn": return speed > 1.5 && Math.Abs(GroundCamera.DeltaAngle(View.Follow.Yaw, GroundCamera.YawToward(0, 0, vx, vz, View.Follow.Yaw))) > 140;
                case "hill-fight":
                {
                    HillDef h = w.Map.Hill;
                    double dx = w.X[0] - h.X, dz = w.Z[0] - h.Z;
                    if (dx * dx + dz * dz > (h.R + 2) * (h.R + 2)) return false;
                    return EnemyWithin(w, 6);
                }
                case "bridge":
                    foreach (BridgeSpan sp in BridgeSpans.All)
                    {
                        double along = BridgeSpans.Along(sp, w.X[0], w.Z[0]);
                        if (along > sp.Len * 0.2 && along < sp.Len * 0.8 && Math.Abs(BridgeSpans.Lateral(sp, w.X[0], w.Z[0])) <= SkybreakBasinMap.BridgeHalfWidth) return true;
                    }
                    return false;
                default:   // facing the Kaiju: hatched, within 45 u, and the view centre within 30 degrees of it
                {
                    if (!GroundDirector.Valid(w, GroundDirector.KaijuTarget)) return false;
                    Kaiju kj = w.Kaiju;
                    double dx = kj.X - w.X[0], dz = kj.Z - w.Z[0];
                    if (dx * dx + dz * dz > 45 * 45) return false;
                    return Math.Abs(GroundCamera.DeltaAngle(View.Follow.Yaw, GroundCamera.YawToward(w.X[0], w.Z[0], kj.X, kj.Z, 0))) < 30;
                }
            }
        }

        static bool EnemyWithin(World w, double r)
        {
            for (int j = 1; j < w.ActorCount; j++)
            {
                if (!w.Actors[j].Alive || w.Actors[j].Team == w.Actors[0].Team) continue;
                double dx = w.X[j] - w.X[0], dz = w.Z[j] - w.Z[0];
                if (dx * dx + dz * dz <= r * r) return true;
            }
            return false;
        }
    }
}
