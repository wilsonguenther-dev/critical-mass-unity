using System.IO;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>
    /// -smokeShots (automation only, fix/smoke-look-1 evidence): one view-only explosion beside the local fighter (for
    /// scale), shot at Ages seconds old in TOP-DOWN and THIRD person, for a TNT blast, a Kaiju slam and a wall break, as
    /// shot-smoke-{view}-{kind}-{age}s.png in the data folder, with a log line each. From StartSeconds of match time the
    /// clock is held (ticks pause; the sim computes nothing different) and each still restarts the explosion at its age
    /// (FxScene.PreviewExplosion clears the other effects first). Logs "[CM.Smoke] done" at the end.
    /// </summary>
    public sealed class SmokeShots : MonoBehaviour
    {
        public const string Flag = "-smokeshots";
        public const double StartSeconds = 30;
        public static readonly double[] Ages = { 0.1, 0.4, 0.8, 1.4 };
        static readonly CameraView[] Views = { CameraView.TopDown, CameraView.ThirdPerson };
        static readonly ExplosionKind[] Kinds = { ExplosionKind.Tnt, ExplosionKind.KaijuSlam, ExplosionKind.WallBreak };
        static readonly float[] Radii = { 3f, 6f, 1.1f };
        const int ViewSettleFrames = 30, ShotSettleFrames = 4;

        public GroundView View;
        int _view, _kind, _age, _frame = -1;
        bool _started, _done, _held;
        double _savedScale;
        CombatFx _fx;

        string Dir => GamePaths.DataDir;

        void Start()
        {
            GroundView.ForcePlay = true;
            GroundView.ViewOverride = Views[0];
        }

        void OnDestroy()
        {
            Hold(false);
            GroundView.ForcePlay = false;
            GroundView.ViewOverride = null;
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

        void Update()
        {
            if (_done || View == null || View.Driver == null || View.Driver.World == null) return;
            World w = View.Driver.World;
            if (!_started)
            {
                if (w.Match.Time < StartSeconds || !w.Actors[0].Alive) return;
                _fx = FindAnyObjectByType<CombatFx>();
                if (_fx == null) return;
                _started = true;
                Hold(true);
                GroundView.ViewOverride = Views[_view];
                _frame = -ViewSettleFrames;
                return;
            }
            if (_frame == -ViewSettleFrames + 1) Debug.Log($"[CM.Smoke] view {Views[_view]}");
            _frame++;
            if (_frame == 0)
            {
                // Beside the fighter: 2.5 u to the view's right and 1.5 u ahead, so the fighter gives the scale.
                GroundCamera.Basis(GroundView.PlayYaw, out double ux, out double uz, out double rx, out double rz);
                double x = w.X[0] + rx * 2.5 + ux * 1.5, z = w.Z[0] + rz * 2.5 + uz * 1.5;
                _fx.PreviewExplosion(Ages[_age], x, z, Radii[_kind], Kinds[_kind]);
                return;
            }
            if (_frame < ShotSettleFrames) return;
            string name = $"shot-smoke-{(Views[_view] == CameraView.TopDown ? "top" : "third")}-{Kinds[_kind].ToString().ToLowerInvariant()}-{Ages[_age]:0.0}s.png";
            string path = Path.Combine(Dir, name);
            ScreenCapture.CaptureScreenshot(path);
            Debug.Log($"[CM.Smoke] {path} R={Radii[_kind]} age={Ages[_age]:0.0}s smokePuffRadiusNow={FxScene.SmokeRadius(Radii[_kind], (float)(Ages[_age] / FxScene.SmokeLife)):F2} fxShrunk={ViewCull.LastFxShrunk} fxSoftened={ViewCull.LastFxSoftened}");
            _frame = 0 - 1;   // next still starts on the next frame
            if (++_age < Ages.Length) return;
            _age = 0;
            if (++_kind < Kinds.Length) return;
            _kind = 0;
            if (++_view < Views.Length)
            {
                GroundView.ViewOverride = Views[_view];
                _frame = -ViewSettleFrames;
                return;
            }
            _done = true;
            Hold(false);
            Debug.Log("[CM.Smoke] done");
        }
    }
}
