using System;
using System.Collections.Generic;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Spectator;
using CriticalMass.Runtime.View.Dressing;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>What the ground view is doing this frame.</summary>
    public enum GroundViewMode { Off, Watch, Play }

    /// <summary>
    /// Ground view (feat/ground-camera, 2026-11-13). A low third-person camera, 3 u behind and about 1.65 u up, over the
    /// followed body's shoulder, with the horizon on screen.
    /// <list type="bullet">
    /// <item>Watch (spectator ground view): while spectating, while knocked out and waiting to respawn, and in automation
    /// runs (-groundView, -groundShots). Follows SpectatorMode's followed fighter (its Q / E and bumpers cycle him, its
    /// objectives pick the Kaiju), else the killer, else Q / E and the bumpers cycle here. Right mouse drag or the right
    /// stick orbits; the yaw swings back behind the fighter 2.5 s later. F6 toggles the auto-director (GroundDirector:
    /// duels, crowds, the hill, carriers, the Kaiju; at least 4 s a shot), on by default when nobody picks.</item>
    /// <item>Play (experimental, Settings, Game, CAMERA: GROUND): the local player plays from the same camera. The mouse
    /// (cursor locked) or the right stick turns it; the move keys and the left stick are camera-relative and become the
    /// same world-space move the sim already takes (HumanInputState.SetCameraBasis); the aim is the point ahead of the
    /// fighter along the camera's facing at the reticle's ground distance (GroundCamera.AimPoint). The sim receives
    /// exactly the InputFrame fields it always did: no Sim change, determinism untouched.</item>
    /// </list>
    /// The rebindable "Camera view" action (F5, double-tap View on a controller) toggles: watching, between the high
    /// spectator view and ground view; playing, the CAMERA setting itself. While on, CinemachineBrain is disabled and this
    /// writes Camera.main directly (a hard cut both ways, nothing blends through walls); the brain takes the gameplay rig
    /// back on exit with its lens. GroundCollision keeps the camera out of walls, towers, trees and cliffs. The backdrop
    /// (GroundBackdrop) gives the low camera a sky and far islands; fighter LODs are picked around the followed body
    /// (LodActive / LodAnchor); the minimap and Kaiju panels hide while watching (minimal spectator layout); camera shake
    /// is limited to 30% (CameraShake.Limit). Never writes the sim.
    /// </summary>
    [DefaultExecutionOrder(-200)]   // after every Update (the driver's Alpha, SpectatorMode's rig), before CameraShake's LateUpdate
    public sealed class GroundView : MonoBehaviour
    {
        public const string ActionName = "CameraView";
        /// <summary>Camera shake share kept in ground view (a 0.32 u shake at 3 u from the pivot is a lot).</summary>
        public const float ShakeLimit = 0.3f;
        // fix/px-combat-camera: the third-person COMBAT FRAMING (CombatFrame / CombatFramer) replaces lane C's side-on duel
        // swing (FightFrame) and its 12% pull-back (HitFeelTuning.FightDistanceShare / FightFovAdd), which made both
        // fighters SMALLER. State: the framer, the target it frames (-1 none) and his chest (the see-through line).
        readonly CombatFramer _combat = new CombatFramer();
        int _duelTarget = -1, _lastFramed = -1;
        double _lastFramedX, _lastFramedZ;
        Vector3 _duelChest;
        /// <summary>The player's own camera yaw and pitch (auto follow or his look), before the combat framing adds its offsets.
        /// The aim (TryPlayAim) and the camera-relative move keys use the own yaw: the framing never steers a swing.</summary>
        double _ownYaw = 315, _ownPitch = FollowCamera.ThirdPitch;
        // fix/px-combat-camera-2: the own yaw one frame ago (his turn rate: a fast turn counts as looking around)
        double _prevOwnYaw = double.NaN;
        bool _combatOn = true;
        float _combatStrength = 1f;
        /// <summary>The combat framing's applied share 0..1 (weight x strength), the target it frames (-1 none) and the yaw it
        /// adds to the player's own this frame (tests, captures). DuelFraming / FightFraming keep their old names for captures.</summary>
        public static double CombatFraming => Current != null ? Current._combat.K : 0;
        public static double DuelFraming => CombatFraming;
        public static double FightFraming => CombatFraming;
        public static int DuelTarget => Current != null ? Current._duelTarget : -1;
        public static double DuelYawOffset => Current != null ? Current._combat.YawOffset : 0;
        /// <summary>The framer itself (tests read its side, wall keep and steering weight).</summary>
        public static CombatFramer Framer => Current != null ? Current._combat : null;
        /// <summary>The yaw the swing and the move keys use while playing (the player's own; the drawn camera may be turned
        /// by the combat framing).</summary>
        /// <summary>fix/px-combat-camera-3: the third-person lens distance from the pivot this frame (0 when not playing low).</summary>
        public static double LensDistance => Current != null && PlayActive ? Current._follow.Dist : 0;
        public static double AimYaw => Current != null ? Current._ownYaw : 315;
        /// <summary>Lane C's old pivot lift constant (kept for its tests; no longer applied).</summary>
        public const double FightLift = 0.15;
        /// <summary>Mouse look degrees per pixel and right stick degrees per second (yaw, pitch).</summary>
        public const float MouseDegPerPx = 0.14f, StickYawDegPerSec = 160f, StickPitchDegPerSec = 90f;
        /// <summary>Off-screen threat ring: enemies within this many u outside the view get a pointer.</summary>
        public const double ThreatRange = 14;
        const int MaxCylinders = 64, MaxStatic = 1024;
        /// <summary>fix/px-combat-camera-2: every solid dressed piece (trees with their crowns, rocks, crystals, props,
        /// buildings, landmarks) as cylinders from the kit meshes' bounds, and the set near the framed fight.</summary>
        const int MaxScenery = 8192, MaxNearScenery = 256;
        /// <summary>A standing wood grove as a camera obstacle (DepthWorldView.Grove: trees 0.75 u out, crowns 0.42 u half-width, ~2.1 u tall).</summary>
        const double GroveRadiusU = 1.3, GroveHeightU = 2.2;

        public static GroundView Current { get; private set; }
        /// <summary>True while the local player plays from the ground camera (HumanInputSource reads it).</summary>
        public static bool PlayActive => Current != null && Current.Mode == GroundViewMode.Play;
        /// <summary>True while ground view drives the camera.</summary>
        public static bool IsActive => Current != null && Current.Mode != GroundViewMode.Off;
        /// <summary>The yaw the keys and the left stick move relative to (the follow views hold it while a move is held;
        /// InputLatch), and the camera's own yaw otherwise.</summary>
        public static double PlayYaw => Current != null ? Current._inputYaw : 315;
        /// <summary>LOD contract C4 from this camera: fighters pick LOD0 by distance to the followed body, which counts as "self".</summary>
        public static bool LodActive => IsActive && Current._lodValid;
        public static Vector3 LodAnchor => Current != null ? Current._lodAnchor : Vector3.zero;
        public static int LodSelf => Current != null ? Current._lodSelf : 0;
        /// <summary>feat/follow-camera-1: the view the local player plays in now (TopDown when the high camera drives).</summary>
        public static CameraView ActiveView => PlayActive ? Current._view : CameraView.TopDown;
        /// <summary>First person: the local player's own body is not drawn (ActorViews reads it).</summary>
        public static bool HideSelf => PlayActive && Current._view == CameraView.FirstPerson;
        /// <summary>Capture runs only (FollowCameraShots): the view to use instead of the setting, and play from it even
        /// while the autopilot drives actor 0.</summary>
        public static CameraView? ViewOverride;
        public static bool ForcePlay;
        /// <summary>follow-camera-3: a sim point (x, z) the auto-follow should face in the low play views (captures frame
        /// the fight and the Kaiju with it). Null = behind the fighter as usual.</summary>
        public static Vector2? AimAt;
        /// <summary>fix/px-camera-in-editor: true while the robust third-person play camera runs (trees fade, SeeThrough
        /// leaves them out).</summary>
        public static bool RobustThirdPerson { get; private set; }
        double _extraSm, _extraInFor;
        /// <summary>fix/px-rough-spots: the framing distance: out on ExtraOutTau (s); in only after ExtraHold s of wanting it by more
        /// than ExtraDeadband u, on ExtraInTau. The two-shot fit looks at an unframed enemy within FitRange u too.</summary>
        const double ExtraInTau = 1.4, ExtraOutTau = 0.3, ExtraHold = 2.0, ExtraDeadband = 0.5, FitRange = 3.0, FitOvershoot = 0.35;
        readonly double[] _fitX = new double[2], _fitY = new double[2], _fitZ = new double[2];
        /// <summary>fix/px-camera-in-editor: Editor diagnostics only (A/B against the old follow, SessionState key
        /// cm.camera.robustOff, gone when the Editor quits); always false in a player.</summary>
        public static bool RobustOffForDiagnostics
#if UNITY_EDITOR
            => UnityEditor.SessionState.GetBool("cm.camera.robustOff", false);
#else
            => false;
#endif

        public SimDriverBehaviour Driver;
        public GroundViewMode Mode { get; private set; }
        /// <summary>The followed actor id, or GroundDirector.KaijuTarget.</summary>
        public int Target { get; private set; } = -1;
        public bool DirectorOn { get; private set; } = true;
        /// <summary>The player asked for ground view while watching (F5); automation sets it too.</summary>
        public bool WatchGround { get; set; }
        public GroundFollow Follow => _follow;
        public GroundDirector Director => _director;
        /// <summary>Frames ground view has been on since it last turned on (tests).</summary>
        public int ActiveFrames { get; private set; }

        readonly GroundFollow _follow = new GroundFollow();
        readonly GroundDirector _director = new GroundDirector();
        readonly SeeThroughCylinder[] _cyl = new SeeThroughCylinder[MaxCylinders];
        readonly SeeThroughCylinder[] _static = new SeeThroughCylinder[MaxStatic];
        int _staticCount, _staticVersion = -1;
        int _cylCount;
        readonly SeeThroughCylinder[] _scenery = new SeeThroughCylinder[MaxScenery];
        readonly SeeThroughCylinder[] _scn = new SeeThroughCylinder[MaxNearScenery];
        int _sceneryCount, _scnCount;
        // fix/px-combat-camera-3: the plain follow's own real-size scenery set (crowns, groves, rocks, buildings, Kaiju)
        readonly SeeThroughCylinder[] _fol = new SeeThroughCylinder[MaxNearScenery];
        int _folCount;
        // fix/px-camera-in-editor: the boom's hard set, and the fade volumes (crowns, groves, props in the way)
        readonly SeeThroughCylinder[] _hard = new SeeThroughCylinder[MaxNearScenery];
        int _hardCount;
        const int MaxFades = 16, ShaderFades = 8;
        readonly SeeThroughCylinder[] _fadeCyl = new SeeThroughCylinder[MaxFades];
        readonly float[] _fadeAmt = new float[MaxFades], _fadeWant = new float[MaxFades];
        int _fadeCount;
        bool _volSet;
        readonly Vector4[] _volA = new Vector4[ShaderFades], _volB = new Vector4[ShaderFades];
        static readonly int VolId = Shader.PropertyToID("_CMFadeVol"), VolYId = Shader.PropertyToID("_CMFadeVolY"),
            VolCountId = Shader.PropertyToID("_CMFadeVolCount"), LensId = Shader.PropertyToID("_CMFadeLens"), SelfId = Shader.PropertyToID("_CMFadeSelf");
        /// <summary>fix/px-camera-in-editor: how much a crown or prop in the way is faded (0.75 = a quarter of it drawn), the
        /// lens sphere radius (nothing closer is drawn) and the local fighter's keep radius.</summary>
        public const float FadeInWay = 0.75f, LensClearU = 0.8f, SelfKeepU = 0.75f, FadeRate = 5f;
        // fix/px-combat-camera-3: the let-go assist (shoulder to the enemy's side, a little more pitch) and its side
        double _assist;
        int _assistSide = 1;
        bool _sightWide;
        double _sightR;
        static readonly Dictionary<string, Vector3[]> PieceBoxes = new Dictionary<string, Vector3[]>();
        GroundViewMode _lastMode;
        int _lastTarget = int.MinValue;
        int _lastSpectated = -1;
        CinemachineBrain _brain;
        UnityEngine.Camera _cam;
        GroundBackdrop _backdrop;
        InputAction _toggle;
        bool _toggleLooked;
        HumanInputSource _input;
        WorldView _world;
        bool _lodValid;
        Vector3 _lodAnchor;
        int _lodSelf;
        double _aimDistance = GroundCamera.AimDistance;
        readonly YawFollow _yawFollow = new YawFollow();
        readonly InputLatch _latch = new InputLatch();
        readonly StrafeGate _strafe = new StrafeGate();
        Vector3 _chest;
        bool _fadeSet;
        static readonly int FadeCamId = Shader.PropertyToID("_CMFadeCam"), FadeTargetId = Shader.PropertyToID("_CMFadeTarget");
        // b15 combat feel (audit item 8): the fighter the local player just hit is exempt from the see-through dither.
        static readonly int FadeKeepId = Shader.PropertyToID("_CMFadeKeep");
        /// <summary>How long (real seconds) a fighter you hit stays solid inside the see-through line, and the kept sphere.</summary>
        public const float HitKeepSeconds = 0.35f, HitKeepRadius = 1.15f;
        bool _keepSet;
        double _inputYaw = 315;
        float _sens = 1f;
        bool _invertY;
        CameraView _view = CameraView.ThirdPerson, _lineView = (CameraView)(-1);
        bool _lineManual;
        string _playLine = "";
        double _sinceLook = 999;
        float _fov = FollowCamera.FovDefault;
        bool _manual;
        static readonly int[] HudPanels = { (int)HudCluster.Minimap, (int)HudCluster.Kaiju };
        readonly Vector3[] _corners = new Vector3[4];
        readonly bool[] _hidClusters = new bool[4];
        bool _cursorLocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Current = null; ViewOverride = null; ForcePlay = false; RobustThirdPerson = false; }

        void Awake() => Current = this;

        void Start()
        {
            // Automation runs: watch from the ground with the director (-groundView), or the scripted capture tour.
            if (AutoplayArgs.HasAutomationFlag("-groundview") || AutoplayArgs.HasAutomationFlag("-groundshots")) WatchGround = true;
            if (AutoplayArgs.HasAutomationFlag("-groundshots") && GetComponent<GroundViewShots>() == null)
                gameObject.AddComponent<GroundViewShots>().View = this;
            if (AutoplayArgs.HasAutomationFlag(FollowCameraShots.Flag) && GetComponent<FollowCameraShots>() == null)
                gameObject.AddComponent<FollowCameraShots>().View = this;
            if (AutoplayArgs.HasAutomationFlag(SmokeShots.Flag) && GetComponent<SmokeShots>() == null)
                gameObject.AddComponent<SmokeShots>().View = this;
            if (GetComponent<CriticalMass.Runtime.View.LowViewTags>() == null) gameObject.AddComponent<CriticalMass.Runtime.View.LowViewTags>();
        }

        void OnDestroy()
        {
            if (Mode != GroundViewMode.Off) Leave();
            if (Current == this) Current = null;
        }

        // ---------------------------------------------------------------- public API (keys, tests, the capture tour)

        /// <summary>The toggle: watching flips ground / high; playing flips the CAMERA setting.</summary>
        public void Toggle()
        {
            if (WatchAllowed() && !PlayAllowedNow())
            {
                WatchGround = !WatchGround;
                Toast(WatchGround ? "GROUND VIEW" : "HIGH VIEW");
            }
            else
            {
                // feat/follow-camera-1: playing, F5 cycles TOP-DOWN, THIRD PERSON, FIRST PERSON (Settings, Game, CAMERA too).
                CameraView next = FollowCamera.Next(ChosenView());
                if (ViewOverride.HasValue) ViewOverride = next; else GameSettings.CameraView = next;
                Toast(next == CameraView.ThirdPerson ? "VIEW: THIRD PERSON" : next == CameraView.FirstPerson ? "VIEW: FIRST PERSON" : "VIEW: TOP-DOWN");
            }
        }

        /// <summary>Follow the next (dir +1) or previous living fighter; the director stands down.</summary>
        public void Cycle(int dir)
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null || w.ActorCount <= 0) return;
            DirectorOn = false;
            int c = GroundCamera.NextAlive(w, Target, dir);
            if (c >= 0) SetTarget(c);
        }

        /// <summary>Follow this actor (or GroundDirector.KaijuTarget); the director stands down.</summary>
        public void Pick(int id)
        {
            DirectorOn = false;
            SetTarget(id);
        }

        public void SetDirector(bool on)
        {
            DirectorOn = on;
            if (on) _director.Reset();
        }

        void SetTarget(int id)
        {
            Target = id;
            _director.Force(id);
            SpectatorMode sm = SpectatorMode.Current;
            World w = Driver != null ? Driver.World : null;
            if (sm != null && sm.Active && id >= 0 && w != null) sm.Rig.Follow(w, id);
            if (sm != null && sm.Active) _lastSpectated = sm.Rig.Followed;
        }

        /// <summary>Points the camera this way now (capture tour; the auto yaw then holds it for OrbitHoldSeconds).</summary>
        public void Aim(double unityYaw, double pitch)
        {
            _follow.SetYaw(unityYaw);
            _follow.SetPitch(pitch);
            _follow.HoldAuto();
        }

        // ---------------------------------------------------------------- rules

        bool InMatch()
        {
            if (Driver == null || Driver.World == null || Driver.World.ActorCount <= 0) return false;
            if (Driver.Request != null && Driver.Request.Backdrop) return false;
            var st = CriticalMass.Runtime.Flow.GameFlow.State;
            return st == CriticalMass.Runtime.Flow.FlowState.Match || st == CriticalMass.Runtime.Flow.FlowState.Off;
        }

        /// <summary>Watching is possible: spectating, knocked out (waiting to respawn), or an automation run.</summary>
        public bool WatchAllowed()
        {
            if (!InMatch()) return false;
            World w = Driver.World;
            if (w.Match.Phase == MatchPhase.Ended) return false;
            return SpectatorMode.IsSpectating || !w.Actors[LocalSeat.Actor].Alive || Driver.HumanAutopilot;
        }

        static CameraView ChosenView() => ViewOverride ?? GameSettings.CameraView;

        bool PlayAllowedNow()
        {
            if (!InMatch() || ChosenView() == CameraView.TopDown) return false;
            World w = Driver.World;
            return w.Match.Phase != MatchPhase.Ended && !SpectatorMode.IsSpectating && (!Driver.HumanAutopilot || ForcePlay) && w.Actors[LocalSeat.Actor].Alive;
        }

        GroundViewMode WantMode()
        {
            if (PlayAllowedNow()) return GroundViewMode.Play;
            // Knocked out with the ground camera chosen: the death cam is ground view too.
            if (WatchAllowed() && (WatchGround || (GameSettings.GroundCamera && !SpectatorMode.IsSpectating))) return GroundViewMode.Watch;
            return GroundViewMode.Off;
        }

        // ---------------------------------------------------------------- frame

        void LateUpdate()
        {
            var cost = FrameCost.Enter();
            try { Frame(); }
            finally { FrameCost.Exit(FrameCost.Slot.Camera, cost); }
        }

        void Frame()
        {
            ReadToggle();
            GroundViewMode want = WantMode();
            if (want != Mode)
            {
                if (Mode == GroundViewMode.Off) Enter();
                Mode = want;
                if (want == GroundViewMode.Off) Leave();
            }
            if (Mode == GroundViewMode.Off)
            {
                _lodValid = false;
                return;
            }
            ActiveFrames++;
            World w = Driver.World;
            MapDef map = w.Map;
            double dt = Driver.FrameDt;
            bool paused = _input != null && _input.State.Paused;

            // Who to follow.
            bool snap = _lastMode != Mode;
            int target = Mode == GroundViewMode.Play ? 0 : WatchTarget(w, paused ? 0 : dt);
            if (target != _lastTarget) snap = true;
            _lastTarget = target;
            _lastMode = Mode;
            Target = target;
            if (!GroundDirector.Valid(w, target)) target = 0;

            double a = Driver.Alpha;
            double tx, tz, facing, lift = 0, extra = 0;
            if (target == GroundDirector.KaijuTarget)
            {
                Kaiju k = w.Kaiju;
                tx = k.PrevX + (k.X - k.PrevX) * a;
                tz = k.PrevZ + (k.Z - k.PrevZ) * a;
                facing = k.Facing;
                lift = 2.2;
                extra = 4.5;
            }
            else
            {
                tx = w.PrevX[target] + (w.X[target] - w.PrevX[target]) * a;
                tz = w.PrevZ[target] + (w.Z[target] - w.PrevZ[target]) * a;
                facing = w.Actors[target].Facing;
            }
            double gy = GroundHeight.At(map, tx, tz);
            double tx0 = tx, tz0 = tz, gy0 = gy;   // the followed body itself (the sight line's end)

            // Orbit / look input.
            ReadLook(paused, dt, out double orbitYaw, out double orbitPitch);
            GroundRig rig = default;
            _chest = SimSpace.ToWorld(tx, gy + 1.2 + lift, tz);
            if (Mode == GroundViewMode.Play)
            {
                CameraView v = ChosenView();
                if (v != _view) { _view = v; snap = true; }
                rig = _view == CameraView.FirstPerson ? FollowCamera.FirstRig : FollowCamera.ThirdRig;
                double fdt = paused ? 0 : dt;
                double leadX = tx, leadZ = tz;
                PlayFollow(w, target, facing, fdt, snap, orbitYaw, orbitPitch, rig, ref tx, ref tz);
                leadX = tx - leadX;
                leadZ = tz - leadZ;
                orbitYaw = orbitPitch = 0;   // PlayFollow owns the yaw and pitch
                // fix/px-combat-camera: the over-the-shoulder combat framing (third person only; never first person, never
                // under reduce motion, off with Settings, Video, COMBAT CAMERA). Adds bounded offsets to the player's own pose.
                StepCombat(w, target, rig, fdt, snap, leadX, leadZ, ref tx, ref tz, ref gy, ref lift, ref extra, ref rig.Shoulder);
            }
            else if (_combat.Weight > 0 || _duelTarget >= 0)
            {
                _combat.Reset();
                _duelTarget = _lastFramed = -1;
            }

            GatherCylinders(w, tx, tz, target);
            // fix/px-combat-camera-3 (pass 3: the plain follow parked inside tree crowns): it collides with the same
            // real-size scenery as the framing (this frame's set around the look point), playing or watching.
            _folCount = GatherScenery(w, tx, tz, 0, _fol, target);
            // fix/px-camera-in-editor: the third-person play camera collides only with hard scenery (rocks, props,
            // buildings) and the grid; trees and groves never move it, they fade (UpdateFades)
            bool robust = Mode == GroundViewMode.Play && _view != CameraView.FirstPerson && !RobustOffForDiagnostics;
            RobustThirdPerson = robust;
            _hardCount = 0;
            for (int i = 0; i < _folCount; i++) if (!robust || !_fol[i].Soft) _hard[_hardCount++] = _fol[i];
            // fix/px-rough-spots (item 1): the two-shot fit. With an enemy framed (or, unframed, one within FitRange) the lens
            // moves back until both whole bodies sit inside the safe frame, at this frame's drawn yaw and pitch.
            if (robust && !paused)
            {
                int foe = _duelTarget >= 0 && _combat.K > 0.01 ? _duelTarget : NearestEnemy(w, target, FitRange);
                if (foe >= 0 && foe < w.ActorCount && w.Actors[foe].Alive)
                {
                    double ex = w.PrevX[foe] + (w.X[foe] - w.PrevX[foe]) * a, ez = w.PrevZ[foe] + (w.Z[foe] - w.PrevZ[foe]) * a;
                    _fitX[0] = tx0; _fitY[0] = gy0; _fitZ[0] = tz0;
                    _fitX[1] = ex; _fitY[1] = GroundHeight.At(map, ex, ez); _fitZ[1] = ez;
                    GroundCamera.Basis(_follow.Yaw, out _, out _, out double frx, out double frz);
                    double piv = gy + rig.PivotHeight + lift;
                    double fitD = CombatFrame.FitDistance(tx + frx * rig.Shoulder, piv, tz + frz * rig.Shoulder, _follow.Yaw, _follow.Pitch,
                        _cam != null ? _cam.fieldOfView : FollowCamera.FovDefault, _cam != null ? _cam.aspect : 16.0 / 9.0, rig.Distance + extra, _fitX, _fitY, _fitZ, 2);
                    // going out, overshoot a little so the next small step of the fight needs no new move (item 3: reversals)
                    double fx2 = fitD - rig.Distance;
                    if (fx2 > _extraSm + 0.05) fx2 += FitOvershoot;
                    extra = Math.Max(extra, fx2);
                }
            }
            // fix/px-camera-in-editor (no pumping) + fix/px-rough-spots (item 3, the hill's 17 reversals a minute): the framing's
            // wanted distance goes OUT promptly (ExtraOutTau: both fighters must fit) and comes back IN only after it has wanted
            // to for ExtraHold seconds by more than ExtraDeadband, then slowly (ExtraInTau): a fight that flickers never pumps.
            if (robust && !snap && !paused && dt > 0)
            {
                if (extra > _extraSm) { _extraSm += (extra - _extraSm) * (1 - Math.Exp(-dt / ExtraOutTau)); _extraInFor = 0; }
                else if (_extraSm - extra > (_extraInFor >= ExtraHold ? 0.02 : ExtraDeadband))
                {
                    _extraInFor += dt;
                    if (_extraInFor >= ExtraHold) _extraSm += (extra - _extraSm) * (1 - Math.Exp(-dt / ExtraInTau));
                }
                else _extraInFor = 0;
            }
            else { _extraSm = extra; _extraInFor = 0; }
            var f = new GroundFollowInput
            {
                Dt = paused ? 0 : dt,
                TargetX = tx,
                TargetZ = tz,
                GroundY = gy,
                PivotLift = lift,
                ExtraDistance = robust ? _extraSm : extra,
                WantYaw = GroundCamera.YawOfFacing(facing),
                AutoYaw = Mode == GroundViewMode.Watch,
                Rig = rig,
                OrbitYaw = orbitYaw,
                OrbitPitch = orbitPitch,
                Snap = snap,
                Map = map,
                // fix/px-combat-camera-3: scenery pulls the lens in exactly like a wall, framed or not
                Cylinders = _hard,
                CylinderCount = _hardCount,
                SlowOut = Mode == GroundViewMode.Play,
                SightOn = !robust && Mode == GroundViewMode.Play && target >= 0,
                Robust = robust,
                BodyRule = 1,   // pass 5: the whole fighter stays in frame in a framed fight too (the chest-only rule cut his legs)
                SightX = tx0,
                SightY = gy0 + 1.2,
                SightZ = tz0,
                VFovDeg = _cam != null ? _cam.fieldOfView : FollowCamera.FovDefault,
                Aspect = _cam != null ? _cam.aspect : 16.0 / 9.0,
            };
            _follow.Step(f);
            _sightWide = _follow.SightBlocked;
            _sightR = _follow.SightR;
            if (Mode != GroundViewMode.Play) _inputYaw = _follow.Yaw;
            UpdateFades(w, robust ? target : -1, tx0, gy0, tz0, paused ? 0 : dt);
            ApplyCamera();

            _lodValid = true;
            _lodAnchor = SimSpace.ToWorld(tx, 0, tz);
            _lodSelf = target >= 0 ? target : -1;
            if (Mode == GroundViewMode.Play) UpdateAimDistance(w);
            UpdateCursor(paused);
        }

        /// <summary>
        /// feat/follow-camera-1: the third / first person yaw and pitch. Auto (default): the yaw follows the character's
        /// travel direction (its facing when standing) on YawFollow's critically damped spring after a short delay, and the
        /// pitch eases to the view's own. Manual (Settings, Video, CAMERA FOLLOW): the mouse or right stick orbits; after
        /// RecenterSeconds without look input, or a middle click, it re-centres behind. Third person also looks a little
        /// ahead along the travel (none under reduce motion).
        /// </summary>
        /// <summary>
        /// fix/px-combat-camera: one frame of the combat framing (CombatFramer) on top of the player's own pose (_ownYaw,
        /// _ownPitch, set by PlayFollow): the look point moves toward the enemy, the camera comes closer, lower and orbits to
        /// the side that already shows more of him (walls: the other side, then no orbit), and the drawn yaw / pitch are the
        /// own ones plus the framer's bounded offsets. The target: FightState.Target within 1.5 x my reach, or within
        /// BusyRange while I swing, guard, am staggered or traded a hit in the last RecentSeconds.
        /// </summary>
        void StepCombat(World w, int me, GroundRig rig, double dt, bool snap, double leadX, double leadZ,
                        ref double tx, ref double tz, ref double gy, ref double lift, ref double extra, ref double shoulder)
        {
            // fix/px-combat-camera-3 (pass 3: switching it off took ~0.6 s to start): the setting is read every frame
            bool enabled = !rig.FirstPerson && GameSettings.CombatCamera && !CriticalMass.Runtime.View.Effects.FeelGate.ReduceMotion;
            double strength = GameSettings.CombatCameraStrength;
            double a = Driver.Alpha;
            double mx = w.PrevX[me] + (w.X[me] - w.PrevX[me]) * a, mz = w.PrevZ[me] + (w.Z[me] - w.PrevZ[me]) * a;
            int t = enabled ? CombatTarget(w, me, _lastFramed, _combat.Active || _combat.Weight > 0) : -1;
            bool alive = false;
            if (t >= 0) _lastFramed = t;
            if (_lastFramed >= 0 && _lastFramed < w.ActorCount && w.Actors[_lastFramed].Alive)
            {
                // the target (or, in the hold, the last one while he stands); a downed body is never followed again
                _lastFramedX = w.PrevX[_lastFramed] + (w.X[_lastFramed] - w.PrevX[_lastFramed]) * a;
                _lastFramedZ = w.PrevZ[_lastFramed] + (w.Z[_lastFramed] - w.PrevZ[_lastFramed]) * a;
                alive = true;
            }
            bool haveLine = _lastFramed >= 0;
            double ex = _lastFramedX, ez = _lastFramedZ;
            double lineYaw = haveLine ? GroundCamera.YawToward(mx, mz, ex, ez, _ownYaw) : _ownYaw;
            double clearPlus = 1, clearMinus = 1, clearNone = 1, plusHalf = 0, minusHalf = 0, plusQuarter = 0, minusQuarter = 0;
            if (enabled && haveLine)
            {
                // fix/px-combat-camera-2: this frame's scenery around BOTH fighters (it was last frame's trunk-only set
                // around the look point), then each framing must fit AND see both chests (CombatFrame.SideClear).
                double k1 = Math.Max(0, Math.Min(1, strength));
                double fx = mx + CombatFrame.FocusShare * k1 * (ex - mx), fz = mz + CombatFrame.FocusShare * k1 * (ez - mz);
                double gyM = GroundHeight.At(w.Map, mx, mz), gyE = GroundHeight.At(w.Map, ex, ez);
                double fy = gyM + (gyE - gyM) * CombatFrame.FocusShare * k1 + FollowCamera.ThirdPivotHeight + (CombatFrame.FocusHeight - FollowCamera.ThirdPivotHeight) * k1;
                _scnCount = GatherScenery(w, fx, fz, Math.Sqrt((ex - mx) * (ex - mx) + (ez - mz) * (ez - mz)), _scn, -1, true);
                double fit = CombatFrame.FovFit(_fov), myC = gyM + CombatFrame.ChestHeight, eyC = gyE + CombatFrame.ChestHeight;
                // fix/px-combat-camera-3: the ladder: each side at 48, 24 and 12 deg (the smaller swings only when needed),
                // then the closer-and-higher fallback
                double Clear(int turn, double swing) => CombatFrame.SideClear(w.Map, fx, fy, fz, lineYaw, turn, swing, k1, fit, mx, myC, mz, ex, eyC, ez, _scn, _scnCount, out _, out _, out _);
                clearPlus = Clear(1, 1);
                clearMinus = Clear(-1, 1);
                if (clearPlus < CombatFrame.WallShare) { plusHalf = Clear(1, 0.5); if (plusHalf < CombatFrame.WallShare) plusQuarter = Clear(1, 0.25); }
                if (clearMinus < CombatFrame.WallShare) { minusHalf = Clear(-1, 0.5); if (minusHalf < CombatFrame.WallShare) minusQuarter = Clear(-1, 0.25); }
                bool anySwing = Math.Max(clearPlus, Math.Max(clearMinus, Math.Max(plusHalf, Math.Max(minusHalf, Math.Max(plusQuarter, minusQuarter))))) >= CombatFrame.WallShare;
                clearNone = anySwing ? 1 : Clear(0, 0);
            }
            else if (_combat.K <= 0) _scnCount = 0;
            // fix/px-combat-camera-2: looking around = his look input lately (mouse look) or his own yaw turning fast (auto)
            double turnRate = dt > 0 && !double.IsNaN(_prevOwnYaw) ? Math.Abs(GroundCamera.DeltaAngle(_prevOwnYaw, _ownYaw)) / dt : 0;
            _prevOwnYaw = _ownYaw;
            bool looking = (_manual && _sinceLook < CombatFrame.LookHoldSeconds) || turnRate > CombatFrame.LookRateDeg;
            var input = new CombatFrameInput
            {
                Dt = dt,
                Snap = snap,
                Enabled = enabled,
                Strength = strength,
                Engaged = t >= 0,
                TargetAlive = alive,
                OwnYaw = _ownYaw,
                LineYaw = lineYaw,
                Looking = looking,
                ClearPlus = clearPlus,
                ClearMinus = clearMinus,
                ClearPlusHalf = plusHalf,
                ClearMinusHalf = minusHalf,
                ClearPlusQuarter = plusQuarter,
                ClearMinusQuarter = minusQuarter,
                Manual = _manual,
                NoClearFrame = clearNone < CombatFrame.WallShare,
            };
            _combat.Step(input);
            // fix/px-combat-camera-3 (pass 3: after a drag the view swung back 45 deg): the yaw the framing gives up becomes
            // his own, so the drawn view never unwinds on its own and the aim stays on screen centre
            double fold = _combat.TakeFold();
            if (fold != 0)
            {
                _yawFollow.Reset(_yawFollow.Yaw + fold);
                _ownYaw = _yawFollow.Yaw;
                _prevOwnYaw = _ownYaw;
            }
            _duelTarget = _combat.Active && haveLine ? _lastFramed : -1;
            if (!_combat.Active && _combat.Weight <= 0) _lastFramed = -1;
            double k = _combat.K;
            if (k > 0 && haveLine)
            {
                double share = _combat.Focus;   // of the way from me to him
                tx += share * (ex - mx) - leadX * k;
                tz += share * (ez - mz) - leadZ * k;
                double gyE = GroundHeight.At(w.Map, ex, ez);
                gy += (gyE - gy) * share;       // stairs, bridges: the look point sits between the two grounds
                lift += _combat.Lift;
                extra += (CombatFrame.Distance * CombatFrame.FovFit(_fov) * _combat.Reach - rig.Distance) * k;
                shoulder += _combat.Shoulder;
                _duelChest = SimSpace.ToWorld(ex, gyE + 1.1, ez);
            }
            // fix/px-combat-camera-3 (pass 3: when the framing let go, the plain camera hid the enemy behind me): with an
            // enemy near and no framing, a small lens shift to his side and a little more pitch (collision-checked by
            // GroundFollow's shoulder test)
            int near = enabled && !_combat.Active && _combat.Weight <= 0.01 ? NearestEnemy(w, me, CombatFrame.KeepRange) : -1;
            double ag = near >= 0 ? 1 : 0;
            _assist = snap || dt <= 0 ? (snap ? ag : _assist) : ag > _assist ? Math.Min(ag, _assist + dt / CombatFrame.AssistSeconds) : Math.Max(ag, _assist - dt / CombatFrame.AssistSeconds);
            double pitchAdd = 0;
            if (_assist > 0)
            {
                if (near >= 0)
                {
                    GroundCamera.Basis(_ownYaw, out _, out _, out double rx, out double rz);
                    double lat = (w.X[near] - mx) * rx + (w.Z[near] - mz) * rz;
                    if (Math.Abs(lat) > 0.3) _assistSide = lat > 0 ? 1 : -1;
                }
                double ak = _assist * _assist * (3 - 2 * _assist);
                shoulder += _assistSide * CombatFrame.AssistShoulderU * ak;
                pitchAdd = CombatFrame.AssistPitchDeg * ak;
            }
            _follow.SetYaw(_ownYaw + _combat.YawOffset);
            _follow.SetPitch(_ownPitch + _combat.PitchOffset + pitchAdd, rig.MinPitch, rig.MaxPitch);
        }

        /// <summary>fix/px-combat-camera-3: the nearest standing enemy within range (no facing cone), -1 none.</summary>
        static int NearestEnemy(World w, int me, double range)
        {
            if (w == null || me < 0 || me >= w.ActorCount || !w.Actors[me].Alive) return -1;
            TeamId team = w.Actors[me].Team;
            int best = -1;
            double bd = range * range;
            for (int i = 0; i < w.ActorCount; i++)
            {
                if (i == me || !w.Actors[i].Alive || w.Actors[i].Team == team) continue;
                double dx = w.X[i] - w.X[me], dz = w.Z[i] - w.Z[me], d2 = dx * dx + dz * dz;
                if (d2 < bd) { bd = d2; best = i; }
            }
            return best;
        }

        /// <summary>The enemy the combat framing shows (-1 none): see StepCombat. Sticky: the body already framed is kept while
        /// he stays within 1.25 x the range (a crowd's nearest-enemy flicker never swings the camera), unless the player
        /// locks someone else.</summary>
        static int CombatTarget(World w, int me, int last, bool framed = false)
        {
            if (w == null || me < 0 || me >= w.ActorCount || !w.Actors[me].Alive) return -1;
            Actor a = w.Actors[me];
            // fix/px-combat-camera-3: once framed it is sticky: the framed body while he stands within KeepRange (unless he
            // locks someone else), else the nearest enemy within KeepRange, no facing cone
            if (framed)
            {
                int lk = AimAssist.LockTarget;
                if (lk >= 0 && lk < w.ActorCount && lk != last && w.Actors[lk].Alive && w.Actors[lk].Team != a.Team) { }
                else if (last >= 0 && last < w.ActorCount && w.Actors[last].Alive && w.Actors[last].Team != a.Team)
                {
                    double dx = w.X[last] - w.X[me], dz = w.Z[last] - w.Z[me];
                    if (dx * dx + dz * dz <= CombatFrame.KeepRange * CombatFrame.KeepRange) return last;
                }
                int n = NearestEnemy(w, me, CombatFrame.KeepRange);
                if (n >= 0 && !(lk >= 0 && lk < w.ActorCount && w.Actors[lk].Alive && w.Actors[lk].Team != a.Team)) return n;
            }
            double reach = w.Config != null ? w.Config.Class(a.Cls).AttackRange : 1.5;
            long sinceSwing = (long)w.Tick - a.SwingTick;
            bool busy = a.SwingPending || (sinceSwing >= 0 && sinceSwing <= a.SwingWindup + a.SwingRecovery + 1) || a.HitStunTicks > 0
                        || CriticalMass.Runtime.View.B17.MovesRead.GuardHeld(w, me)
                        || FightState.SinceCombat(Time.unscaledTimeAsDouble) < CombatFrame.RecentSeconds;
            double near = reach * CombatFrame.ReachShare, range = busy ? Math.Max(near, CombatFrame.BusyRange) : near;
            int t = FightState.Target(w, me, range);
            if (t >= 0 && t != last && last >= 0 && last < w.ActorCount && t != AimAssist.LockTarget && w.Actors[last].Alive && w.Actors[last].Team != a.Team)
            {
                double dx = w.X[last] - w.X[me], dz = w.Z[last] - w.Z[me], keep = range * 1.25;
                if (dx * dx + dz * dz <= keep * keep) t = last;
            }
            return t;
        }

        void PlayFollow(World w, int id, double facing, double dt, bool snap, double orbitYaw, double orbitPitch, GroundRig rig, ref double tx, ref double tz)
        {
            double hz = w.Config.SimHz > 0 ? w.Config.SimHz : 30;
            double vx = (w.X[id] - w.PrevX[id]) * hz, vz = (w.Z[id] - w.PrevZ[id]) * hz;
            bool moving = vx * vx + vz * vz > FollowCamera.MovingSpeed * FollowCamera.MovingSpeed;
            double behind = moving ? GroundCamera.YawToward(0, 0, vx, vz, GroundCamera.YawOfFacing(facing)) : GroundCamera.YawOfFacing(facing);
            bool reduce = CriticalMass.Runtime.View.Effects.FeelGate.ReduceMotion;
            double defPitch = rig.FirstPerson ? FollowCamera.FirstPitch : FollowCamera.ThirdPitch;
            // follow-camera-3: a capture (or a later feature) may ask the auto-follow to frame a point of action.
            if (AimAt.HasValue) behind = GroundCamera.YawToward(w.X[id], w.Z[id], AimAt.Value.x, AimAt.Value.y, behind);
            // follow-camera-3: first person turns the view off a wall right in front of the eyes (view only; the controls
            // stay latched while a move is held).
            if (rig.FirstPerson && !_manual)
                behind = FollowCamera.WallYaw(w.Map, w.X[id], GroundHeight.At(w.Map, w.X[id], w.Z[id]) + FollowCamera.EyeHeight, w.Z[id], behind, _cyl, _cylCount);
            if (snap)
            {
                _yawFollow.Reset(GroundCamera.YawOfFacing(facing));
                _ownPitch = Math.Max(rig.MinPitch, Math.Min(rig.MaxPitch, defPitch));
                _sinceLook = 999;
            }
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.middleButton.wasPressedThisFrame) { _yawFollow.Recenter(behind); _sinceLook = 999; }
            bool looked = _manual && (Math.Abs(orbitYaw) > 1e-6 || Math.Abs(orbitPitch) > 1e-6);
            if (looked)
            {
                _sinceLook = 0;
                // fix/px-combat-camera: the look turns the player's OWN yaw and pitch (not the drawn camera, which may carry
                // the combat framing's offset): his mouse or stick moves the view 1:1.
                _yawFollow.Reset(_yawFollow.Yaw + orbitYaw);
                _ownPitch = Math.Max(rig.MinPitch, Math.Min(rig.MaxPitch, _ownPitch + orbitPitch));
            }
            else
            {
                _sinceLook += dt;
                if (!_manual || _sinceLook >= FollowCamera.RecenterSeconds)
                {
                    // follow-camera-2: a sideways run with no forward input holds the yaw; forward input coming back
                    // re-centres behind at once.
                    bool hold = false, recenter = false;
                    if (_input != null && !_manual) _strafe.Step(_input.State.MoveRight, _input.State.MoveUp, out hold, out recenter);
                    if (recenter) _yawFollow.Recenter(behind);
                    _yawFollow.Step(dt, hold ? _yawFollow.Target : behind, reduce);
                    if (dt > 0) _ownPitch = Math.Max(rig.MinPitch, Math.Min(rig.MaxPitch, _ownPitch + (defPitch - _ownPitch) * (1 - Math.Exp(-dt / 0.4))));
                }
            }
            _ownYaw = _yawFollow.Yaw;
            _follow.SetYaw(_ownYaw);
            _follow.SetPitch(_ownPitch, rig.MinPitch, rig.MaxPitch);
            bool held = _input != null && _input.State.MoveHeld;
            // the move keys stay relative to the player's own yaw (a framed fight never turns his W key)
            _inputYaw = snap ? _ownYaw : _latch.Step(held, looked, _ownYaw);
            if (!rig.FirstPerson)
            {
                FollowCamera.Lead(vx, vz, reduce, out double lx, out double lz);
                tx += lx;
                tz += lz;
            }
        }

        int WatchTarget(World w, double dt)
        {
            SpectatorMode sm = SpectatorMode.Current;
            bool spectating = sm != null && sm.Active;
            if (spectating)
            {
                // The spectator's own picks (Q / E, bumpers, click, objectives) win and stop the director.
                if (sm.Rig.Focus == SpectatorFocus.Objective && sm.Rig.Objective == SpectatorObjective.Kaiju && GroundDirector.Valid(w, GroundDirector.KaijuTarget))
                {
                    if (Target != GroundDirector.KaijuTarget) { DirectorOn = false; _director.Force(GroundDirector.KaijuTarget); }
                    return GroundDirector.KaijuTarget;
                }
                if (sm.Rig.Focus == SpectatorFocus.Player && sm.Rig.Followed != _lastSpectated)
                {
                    if (_lastSpectated >= 0) DirectorOn = false;
                    _lastSpectated = sm.Rig.Followed;
                    if (!DirectorOn) _director.Force(sm.Rig.Followed);
                }
            }
            else
            {
                Keyboard kb = Keyboard.current;
                Gamepad pad = Gamepad.current;
                if (kb != null && kb.qKey.wasPressedThisFrame) Cycle(-1);
                if (kb != null && kb.eKey.wasPressedThisFrame) Cycle(1);
                if (pad != null && pad.leftShoulder.wasPressedThisFrame) Cycle(-1);
                if (pad != null && pad.rightShoulder.wasPressedThisFrame) Cycle(1);
            }
            Keyboard k2 = Keyboard.current;
            if (k2 != null && k2.f6Key.wasPressedThisFrame)
            {
                SetDirector(!DirectorOn);
                Toast(DirectorOn ? "AUTO-DIRECTOR ON" : "AUTO-DIRECTOR OFF");
            }
            if (DirectorOn)
            {
                if (_director.Step(w, dt) || (spectating && _director.Current >= 0 && sm.Rig.Followed != _director.Current))
                {
                    if (spectating && _director.Current >= 0)
                    {
                        sm.Rig.Follow(w, _director.Current);
                        _lastSpectated = _director.Current;
                    }
                }
                if (GroundDirector.Valid(w, _director.Current)) return _director.Current;
            }
            if (spectating && sm.Rig.Focus == SpectatorFocus.Player && GroundDirector.Valid(w, sm.Rig.Followed)) return sm.Rig.Followed;
            if (GroundDirector.Valid(w, Target)) return Target;
            // Nobody picked yet, or the followed body went down: the killer, then a team-mate, then anyone standing.
            int pick = -1;
            int killer = w.Actors[LocalSeat.Actor].LastAttacker;
            if (!w.Actors[LocalSeat.Actor].Alive && killer >= 0 && killer < w.ActorCount && w.Actors[killer].Alive) pick = killer;
            for (int i = 0; i < w.ActorCount && pick < 0; i++)
                if (w.Actors[i].Alive && w.Actors[i].Team == w.Actors[LocalSeat.Actor].Team) pick = i;
            for (int i = 0; i < w.ActorCount && pick < 0; i++)
                if (w.Actors[i].Alive) pick = i;
            if (pick >= 0) _director.Force(pick);
            return pick >= 0 ? pick : 0;
        }

        void ReadToggle()
        {
            if (!_toggleLooked || (_toggle == null && Time.frameCount % 120 == 0))
            {
                _toggleLooked = true;
                InputActionMap map = ControlBindings.Map(InputSystem.actions);
                _toggle = map != null ? map.FindAction(ActionName, false) : null;
            }
            if (!InMatch()) return;
            bool pressed = _toggle != null ? _toggle.WasPerformedThisFrame() : Keyboard.current != null && Keyboard.current.f5Key.wasPressedThisFrame;
            if (pressed) Toggle();
        }

        void ReadLook(bool paused, double dt, out double yaw, out double pitch)
        {
            yaw = pitch = 0;
            if (paused || SimDriverBehaviour.CaptureMode) return;
            Mouse mouse = Mouse.current;
            Gamepad pad = Gamepad.current;
            bool play = Mode == GroundViewMode.Play;
            // feat/follow-camera-1: playing, the look input only orbits in manual mode (auto follow owns the yaw otherwise).
            if (Time.frameCount % 30 == 0 || ActiveFrames <= 1)
            {
                _manual = GameSettings.CameraManual;
                _fov = GameSettings.CameraFov;
                _sens = GameSettings.CameraSensitivity;
                _invertY = GameSettings.CameraInvertY;
                _combatOn = GameSettings.CombatCamera;
                _combatStrength = GameSettings.CombatCameraStrength;
            }
            if (play && !_manual) return;
            float sens = play ? _sens : 1f;
            if (mouse != null && (play ? _cursorLocked : mouse.rightButton.isPressed))
            {
                Vector2 d = mouse.delta.ReadValue();
                yaw += d.x * MouseDegPerPx * sens;
                pitch -= d.y * MouseDegPerPx * sens * ((play ? _invertY : ControllerSettings.InvertAimY) ? -1f : 1f);
            }
            if (pad != null && play)
            {
                Vector2 s0 = pad.rightStick.ReadValue();
                float m0 = s0.magnitude;
                if (m0 > 0.2f)
                {
                    float k0 = Mathf.Clamp01((m0 - 0.2f) / 0.8f) / m0 * sens;
                    yaw += s0.x * k0 * StickYawDegPerSec * dt;
                    pitch -= s0.y * k0 * StickPitchDegPerSec * dt * (_invertY ? -1f : 1f);
                }
                return;
            }
            if (pad != null)
            {
                Vector2 s = pad.rightStick.ReadValue();
                float m = s.magnitude;
                if (m > 0.2f)
                {
                    float k = Mathf.Clamp01((m - 0.2f) / 0.8f) / m;
                    yaw += s.x * k * StickYawDegPerSec * dt;
                    pitch -= s.y * k * StickPitchDegPerSec * dt * (ControllerSettings.InvertAimY ? -1f : 1f);
                }
            }
        }

        /// <summary>The reticle (screen centre) to the ground: its distance from the fighter is the aim distance.</summary>
        void UpdateAimDistance(World w)
        {
            if (_cam == null) return;
            // fix/px-combat-camera: while the combat framing turns the drawn camera, the screen centre is no longer the
            // player's reticle line: keep the aim distance he had going in (a throw's reach never jumps mid-fight).
            if (_combat.K > 0.01) return;
            Ray ray = _cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            double hx = w.X[LocalSeat.Actor], hz = w.Z[LocalSeat.Actor];
            if (GroundHeight.TryAimPoint(ray, w.Map, hx, hz, out double sx, out double sz))
            {
                double dx = sx - hx, dz = sz - hz;
                GroundCamera.Basis(_ownYaw, out double ux, out double uz, out _, out _);
                double along = dx * ux + dz * uz;   // ahead of the fighter along the player's own facing
                _aimDistance = along > GroundCamera.MinAim && along < GroundCamera.MaxAim * 1.5 ? along : GroundCamera.AimDistance;
            }
            else _aimDistance = GroundCamera.AimDistance;
        }

        /// <summary>The aim point the sim gets this frame while playing (false when not playing).</summary>
        public static bool TryPlayAim(World w, out double ax, out double az)
        {
            ax = az = 0;
            GroundView g = Current;
            if (g == null || g.Mode != GroundViewMode.Play || w == null || w.ActorCount <= 0) return false;
            // fix/px-combat-camera: the player's own yaw, never the combat framing's turned camera
            GroundCamera.AimPoint(w.X[LocalSeat.Actor], w.Z[LocalSeat.Actor], g._ownYaw, g._aimDistance, out ax, out az);
            return true;
        }

        void UpdateCursor(bool paused)
        {
            bool want = Mode == GroundViewMode.Play && !paused && Application.isFocused && !SimDriverBehaviour.CaptureMode;
            if (want == _cursorLocked) return;
            _cursorLocked = want;
            Cursor.lockState = want ? CursorLockMode.Locked : CursorLockMode.None;
            if (want) Cursor.visible = false;
        }

        // ---------------------------------------------------------------- camera

        void Enter()
        {
            if (_cam == null || !_cam.isActiveAndEnabled) _cam = UnityEngine.Camera.main;
            if (_cam != null) _brain = _cam.GetComponent<CinemachineBrain>();
            if (_brain != null) _brain.enabled = false;
            if (_input == null && Driver != null) _input = Driver.GetComponent<HumanInputSource>();
            if (_backdrop == null)
            {
                var go = new GameObject("Ground Backdrop");
                _backdrop = go.AddComponent<GroundBackdrop>();
                _backdrop.Build();
            }
            _backdrop.gameObject.SetActive(true);
            CameraShake.Limit = ShakeLimit;
            ActiveFrames = 0;
            _lastMode = GroundViewMode.Off;
            _lastTarget = int.MinValue;
            SpectatorMode sm = SpectatorMode.Current;
            _lastSpectated = sm != null && sm.Active ? sm.Rig.Followed : -1;
            SetMinimalHud(true);
        }

        /// <summary>follow-camera-2: the toon shader's see-through line (CMToonFadeClip) from the lens to the followed body's
        /// chest; off in first person and when the low views are off. Two global vectors a frame, no allocation.</summary>
        void SetFade(Vector3 camPos)
        {
            bool on = Mode != GroundViewMode.Off && !(Mode == GroundViewMode.Play && _view == CameraView.FirstPerson);
            if (on)
            {
                Shader.SetGlobalVector(FadeCamId, new Vector4(camPos.x, camPos.y, camPos.z, 1f));
                // follow-camera-3: aim the see-through at head and shoulders (chest + 0.35), not the belly.
                // lane C fix round: in a framed fight the see-through line runs to the TARGET's chest, so whatever still
                // covers him (the own fighter's ladder or shoulder, a wall in front) dithers instead of hiding the hit.
                // fix/px-combat-camera-3: scenery the pull-in could not clear (lens right behind foliage) gets a wider
                // dither tube, so it fades instead of filling the screen
                float wide = _sightWide ? Mathf.Max(ViewCull.FadeRadius, (float)_sightR + 0.6f) : 0f;
                if (_combat.K > 0.5 && _duelTarget >= 0)
                    Shader.SetGlobalVector(FadeTargetId, new Vector4(_duelChest.x, _duelChest.y, _duelChest.z, Mathf.Max(FightFrame.FadeRadius, wide)));
                else
                    Shader.SetGlobalVector(FadeTargetId, new Vector4(_chest.x, _chest.y + 0.35f, _chest.z, Mathf.Max(ViewCull.FadeRadius, wide)));
                _fadeSet = true;
                SetHitKeep();
            }
            else if (_fadeSet)
            {
                Shader.SetGlobalVector(FadeCamId, Vector4.zero);
                _fadeSet = false;
            }
            ViewCull.SetFollowLine(_chest + new Vector3(0f, 0.35f, 0f), on);
        }

        /// <summary>b15: the fighter the local seat struck last (CombatFeelState.StruckBy / StruckTick, sim state the view reads;
        /// no World.Events reader) stays solid HitKeepSeconds after the strike.</summary>
        void SetHitKeep()
        {
            World w = Driver != null ? Driver.World : null;
            CombatFeelState fs = w != null && w.Depth != null ? w.Depth.Feel : null;
            bool keep = false;
            if (fs != null && CombatFeel.On(w))
            {
                int me = LocalSeat.Actor, best = -1, bestTick = CombatFeel.NoTick;
                int window = Math.Max(1, CombatFeel.Ticks(w, HitKeepSeconds));
                for (int i = 0; i < w.ActorCount && i < fs.StruckBy.Length; i++)
                    if (i != me && fs.StruckBy[i] == me && fs.StruckTick[i] > bestTick && w.Tick - fs.StruckTick[i] <= window && w.Actors[i].Alive)
                    {
                        best = i;
                        bestTick = fs.StruckTick[i];
                    }
                if (best >= 0)
                {
                    double a = Driver.Alpha;
                    double x = w.PrevX[best] + (w.X[best] - w.PrevX[best]) * a, z = w.PrevZ[best] + (w.Z[best] - w.PrevZ[best]) * a;
                    Vector3 c = SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + 0.9, z);
                    Shader.SetGlobalVector(FadeKeepId, new Vector4(c.x, c.y, c.z, HitKeepRadius));
                    _keepSet = true;
                    keep = true;
                }
            }
            if (!keep && _keepSet)
            {
                Shader.SetGlobalVector(FadeKeepId, Vector4.zero);
                _keepSet = false;
            }
        }

        void Leave()
        {
            RobustThirdPerson = false;
            if (_fadeSet) { Shader.SetGlobalVector(FadeCamId, Vector4.zero); _fadeSet = false; }
            if (_keepSet) { Shader.SetGlobalVector(FadeKeepId, Vector4.zero); _keepSet = false; }
            ClearVolumes();
            Mode = GroundViewMode.Off;
            _lodValid = false;
            if (_brain != null) _brain.enabled = true;
            if (_cam != null)
            {
                // The brain writes the rig's lens again on its next update; restore now so this frame is right too.
                _cam.fieldOfView = (float)GameplayCamera.FovDeg;
                _cam.nearClipPlane = GameplayCamera.NearClip;
                _cam.farClipPlane = GameplayCamera.FarClip;
            }
            if (_backdrop != null) _backdrop.gameObject.SetActive(false);
            CameraShake.Limit = 1f;
            SetMinimalHud(false);
            if (_cursorLocked)
            {
                _cursorLocked = false;
                Cursor.lockState = CursorLockMode.None;
            }
        }

        void ApplyCamera()
        {
            if (_cam == null || !_cam.isActiveAndEnabled)
            {
                _cam = UnityEngine.Camera.main;
                if (_cam == null) return;
                _brain = _cam.GetComponent<CinemachineBrain>();
                if (_brain != null) _brain.enabled = false;
            }
            Vector3 pos = SimSpace.ToWorld(_follow.CamX, _follow.CamY, _follow.CamZ);
            _cam.transform.SetPositionAndRotation(pos, Quaternion.Euler((float)_follow.Pitch, (float)_follow.Yaw, 0f));
            // fix/px-combat-camera: the old +3 degree fight widening is retired (the combat framing comes closer instead)
            float fov = Mode == GroundViewMode.Play ? _fov : GroundCamera.FovDeg;
            if (_cam.fieldOfView != fov) _cam.fieldOfView = fov;
            if (_cam.nearClipPlane != GroundCamera.NearClip) _cam.nearClipPlane = GroundCamera.NearClip;
            if (_cam.farClipPlane != GroundCamera.FarClip) _cam.farClipPlane = GroundCamera.FarClip;
            if (_backdrop != null) _backdrop.Place(pos);
            SetFade(pos);
        }

        /// <summary>The HUD's minimap and Kaiju panels hide while watching from the ground (the clock and score stay).</summary>
        void SetMinimalHud(bool minimal)
        {
            GameHud hud = Driver != null ? Driver.GetComponent<GameHud>() : null;
            if (hud == null || hud.Clusters == null) return;
            foreach (int i in HudPanels)
            {
                RectTransform c = hud.Clusters[i];
                if (c == null) continue;
                if (minimal)
                {
                    // Only while watching: playing keeps the full HUD (the player needs his minimap).
                    _hidClusters[i] = false;
                    continue;
                }
                if (_hidClusters[i]) c.gameObject.SetActive(true);
                _hidClusters[i] = false;
            }
        }

        void Update()
        {
            // The minimal layout follows the mode (Watch hides two panels, Play shows them), checked once a frame.
            GameHud hud = Driver != null ? Driver.GetComponent<GameHud>() : null;
            if (hud == null || hud.Clusters == null) return;
            bool hide = Mode == GroundViewMode.Watch;
            foreach (int i in HudPanels)
            {
                RectTransform c = hud.Clusters[i];
                if (c == null) continue;
                if (hide && c.gameObject.activeSelf)
                {
                    c.gameObject.SetActive(false);
                    _hidClusters[i] = true;
                }
                else if (!hide && _hidClusters[i])
                {
                    c.gameObject.SetActive(true);
                    _hidClusters[i] = false;
                }
            }
        }

        // ---------------------------------------------------------------- fix/px-camera-in-editor: fades

        /// <summary>Fade volume i this frame (captures, tests): the cylinder and how faded it is (0..1).</summary>
        public int FadeVolumes => _fadeCount;
        public SeeThroughCylinder FadeVolume(int i, out float amount) { amount = _fadeAmt[i]; return _fadeCyl[i]; }

        /// <summary>
        /// The third-person camera never dodges scenery by moving in: what stands in the way is faded instead. A tree, a
        /// grove or a prop is faded (FadeInWay) while the line from the lens to my head or chest (or to the framed enemy's
        /// chest) crosses it or I stand inside its crown; gone (1) while the lens is inside it. Each fade eases at
        /// FadeRate per second. Sent to the toon shaders as up to eight cylinders (CMFadeVolumes.hlsl), with the lens
        /// sphere and my keep capsule. target &lt; 0: everything off (first person, watching).
        /// </summary>
        void UpdateFades(World w, int target, double px, double py, double pz, double dt)
        {
            for (int k = 0; k < _fadeCount; k++) _fadeWant[k] = 0f;
            if (target >= 0 && w != null)
            {
                double lx = _follow.CamX, ly = _follow.CamY, lz = _follow.CamZ;
                bool duel = _combat.K > 0.3 && _duelTarget >= 0 && _duelTarget < w.ActorCount;
                double ex = 0, ey = 0, ez = 0;
                if (duel)
                {
                    double a = Driver.Alpha;
                    ex = w.PrevX[_duelTarget] + (w.X[_duelTarget] - w.PrevX[_duelTarget]) * a;
                    ez = w.PrevZ[_duelTarget] + (w.Z[_duelTarget] - w.PrevZ[_duelTarget]) * a;
                    ey = GroundHeight.At(w.Map, ex, ez) + 1.1;
                }
                for (int i = 0; i < _folCount; i++)
                {
                    SeeThroughCylinder c = _fol[i];
                    if (c.Id == 0) continue;   // the Kaiju is never faded
                    double dlx = lx - c.X, dlz = lz - c.Z, dpx = px - c.X, dpz = pz - c.Z;
                    bool lensIn = dlx * dlx + dlz * dlz < (c.R + 0.35) * (c.R + 0.35) && ly > c.Y0 - 0.35 && ly < c.Y1 + 0.35;
                    bool meIn = dpx * dpx + dpz * dpz < (c.R + 0.2) * (c.R + 0.2) && c.Y1 > py + 1.0 && c.Y0 < py + 2.6;
                    bool way = meIn
                               || GroundCollision.Crosses(lx, ly, lz, px, py + 1.0, pz, 0.0, c)
                               || GroundCollision.Crosses(lx, ly, lz, px, py + 1.8, pz, 0.0, c)
                               || (duel && GroundCollision.Crosses(lx, ly, lz, ex, ey, ez, 0.3, c));
                    float want = lensIn ? 1f : way ? FadeInWay : 0f;
                    if (want <= 0f) continue;
                    int slot = -1;
                    for (int k = 0; k < _fadeCount; k++) if (_fadeCyl[k].Id == c.Id) { slot = k; break; }
                    if (slot < 0)
                    {
                        if (_fadeCount >= MaxFades) continue;
                        slot = _fadeCount++;
                        _fadeAmt[slot] = 0f;
                    }
                    _fadeCyl[slot] = c;
                    _fadeWant[slot] = Mathf.Max(_fadeWant[slot], want);
                }
            }
            float step = dt > 0 ? (float)dt * FadeRate : 0f;
            int m = 0;
            for (int k = 0; k < _fadeCount; k++)
            {
                float amt = step > 0f ? Mathf.MoveTowards(_fadeAmt[k], _fadeWant[k], step) : (target >= 0 ? _fadeAmt[k] : 0f);
                if (amt <= 0f && _fadeWant[k] <= 0f) continue;
                _fadeCyl[m] = _fadeCyl[k];
                _fadeAmt[m] = amt;
                _fadeWant[m] = _fadeWant[k];
                m++;
            }
            _fadeCount = m;
            if (target < 0) { ClearVolumes(); return; }
            // the strongest eight go to the shaders
            int sent = 0;
            for (int pass = 0; pass < 2 && sent < ShaderFades; pass++)
                for (int k = 0; k < _fadeCount && sent < ShaderFades; k++)
                {
                    bool strong = _fadeAmt[k] >= 0.5f;
                    if ((pass == 0) != strong || _fadeAmt[k] <= 0f) continue;
                    SeeThroughCylinder c = _fadeCyl[k];
                    _volA[sent] = new Vector4((float)c.X, (float)-c.Z, (float)c.R, _fadeAmt[k]);
                    _volB[sent] = new Vector4((float)c.Y0, (float)c.Y1, 0f, 0f);
                    sent++;
                }
            for (int k = sent; k < ShaderFades; k++) { _volA[k] = Vector4.zero; _volB[k] = Vector4.zero; }
            Shader.SetGlobalVectorArray(VolId, _volA);
            Shader.SetGlobalVectorArray(VolYId, _volB);
            Shader.SetGlobalFloat(VolCountId, sent);
            Vector3 lens = SimSpace.ToWorld(_follow.CamX, _follow.CamY, _follow.CamZ);
            Shader.SetGlobalVector(LensId, new Vector4(lens.x, lens.y, lens.z, LensClearU));
            Vector3 feet = SimSpace.ToWorld(px, py, pz);
            Shader.SetGlobalVector(SelfId, new Vector4(feet.x, feet.y, feet.z, SelfKeepU));
            _volSet = true;
        }

        void ClearVolumes()
        {
            if (!_volSet) return;
            for (int k = 0; k < ShaderFades; k++) { _volA[k] = Vector4.zero; _volB[k] = Vector4.zero; }
            Shader.SetGlobalVectorArray(VolId, _volA);
            Shader.SetGlobalVectorArray(VolYId, _volB);
            Shader.SetGlobalFloat(VolCountId, 0f);
            Shader.SetGlobalVector(LensId, Vector4.zero);
            Shader.SetGlobalVector(SelfId, Vector4.zero);
            _volSet = false;
            _fadeCount = 0;
        }

        // ---------------------------------------------------------------- collision volumes

        void GatherCylinders(World w, double tx, double tz, int target)
        {
            RefreshStatic();
            int n = 0;
            for (int i = 0; i < _staticCount && n < MaxCylinders - 1; i++)
            {
                double dx = _static[i].X - tx, dz = _static[i].Z - tz;
                double reach = GroundCamera.Distance + 6 + _static[i].R;
                if (dx * dx + dz * dz > reach * reach) continue;
                _cyl[n++] = _static[i];
            }
            Kaiju k = w.Kaiju;
            if (target != GroundDirector.KaijuTarget && w.Match.Hatched && k.State != KaijuState.Egg)
            {
                double a = Driver.Alpha;
                double kx = k.PrevX + (k.X - k.PrevX) * a, kz = k.PrevZ + (k.Z - k.PrevZ) * a;
                double ky = GroundHeight.At(w.Map, kx, kz);
                _cyl[n++] = new SeeThroughCylinder { X = kx, Z = kz, R = w.Config.Kaiju.Radius, Y0 = ky, Y1 = ky + w.Config.Render.KaijuHeight };
            }
            _cylCount = n;
        }

        /// <summary>fix/px-combat-camera-2: the scenery cylinders within reach of the framed look point (fx, fz): the lens and
        /// both sight lines stay inside ThirdDistance + shoulder + half the fighters' gap of it. Plus the hatched Kaiju.</summary>
        int GatherScenery(World w, double fx, double fz, double gap, SeeThroughCylinder[] into, int target = -1, bool hardOnly = false)
        {
            RefreshStatic();
            SeeThroughCylinder[] _scn = into;
            int n = 0;
            double near = FollowCamera.ThirdDistance + CombatFrame.ShoulderU + gap + 1.0;
            for (int i = 0; i < _sceneryCount && n < MaxNearScenery - 1; i++)
            {
                if (hardOnly && _scenery[i].Soft) continue;   // fix/px-camera-in-editor: a tree fades, it never blocks a framing
                double dx = _scenery[i].X - fx, dz = _scenery[i].Z - fz, reach = near + _scenery[i].R;
                if (dx * dx + dz * dz > reach * reach) continue;
                _scn[n++] = _scenery[i];
            }
            // Editor review pass 3: the wood groves (DepthWorldView.Grove: up to four trees, crowns to ~2 u, within ~1.2 u of
            // the source) are sim sources drawn in the Depth Solid mesh, not dressing, so they were missing from the set
            // above and the framing parked the lens inside one beside the 28.5:58.5 grove. A standing grove counts like a tree.
            int sources = hardOnly ? 0 : CriticalMass.Runtime.View.Mechanics.DepthRead.SourceCount(w);
            for (int i = 0; i < sources && n < MaxNearScenery - 1; i++)
            {
                if (!CriticalMass.Runtime.View.Mechanics.DepthRead.Source(w, i, out bool water, out double sx, out double sz, out int ch, out _, out _)) continue;
                if (water || ch <= 0) continue;
                double dx = sx - fx, dz = sz - fz, reach = near + GroveRadiusU;
                if (dx * dx + dz * dz > reach * reach) continue;
                double gy = GroundHeight.At(w.Map, sx, sz);
                _scn[n++] = new SeeThroughCylinder { X = sx, Z = sz, R = GroveRadiusU, Y0 = gy, Y1 = gy + GroveHeightU, Soft = true, Id = -(i + 1) };
            }
            Kaiju k = w.Kaiju;
            if (target != GroundDirector.KaijuTarget && w.Match.Hatched && k.State != KaijuState.Egg)
            {
                double a = Driver.Alpha;
                double kx = k.PrevX + (k.X - k.PrevX) * a, kz = k.PrevZ + (k.Z - k.PrevZ) * a;
                double ky = GroundHeight.At(w.Map, kx, kz);
                _scn[n++] = new SeeThroughCylinder { X = kx, Z = kz, R = w.Config.Kaiju.Radius, Y0 = ky, Y1 = ky + w.Config.Render.KaijuHeight };
            }
            return n;
        }

        /// <summary>fix/px-combat-camera-2: the dressing categories that stand solid in the camera's way (the walkable
        /// surface, decals, edges, lights and the sky are not).</summary>
        static bool SolidScenery(in WorldPlacement p) =>
            !p.Surface && p.Cat != null && (p.Cat == "tree" || p.Cat == "rock_big" || p.Cat == "crystal" || p.Cat == "landmark" || p.Cat == "decor"
                                            || p.Cat == "dress_tall" || p.Cat == "small_tall" || p.Cat == "small" || p.Cat == "base" || p.Cat == "struct" || p.Cat == "deco");

        /// <summary>fix/px-camera-in-editor: only buildings, landmarks and big rocks stop the third-person boom; trees,
        /// crystals and props fade instead (fewer collisions: no pumping, no steep views over a crystal).</summary>
        /// <summary>pass 5: a prop narrower than this (u from its centre) never moves the third-person boom; it fades.</summary>
        const double HardPropExtent = 1.0;
        /// <summary>fix/px-rough-spots: a mesh box longer than this x its width is split into a row of cylinders (AddScenery).</summary>
        const double SplitAspect = 1.4;
        static bool SoftScenery(string cat) => !(cat == "base" || cat == "struct" || cat == "landmark" || cat == "rock_big");
        /// <summary>fix/px-rough-spots (items 2 and 4: the hill rim and the stuck spot by the hill brazier, 53.4:49.6): the hill's
        /// open arches and the horns that stand over them fade like props; they never push the lens in.</summary>
        static bool SoftPiece(string piece) => piece != null && (piece.IndexOf("hill_arc", StringComparison.Ordinal) >= 0 || piece.IndexOf("hill_horn", StringComparison.Ordinal) >= 0);

        /// <summary>A kit piece's mesh boxes (8 corners per mesh, its own frame), from Mesh.bounds (readable or not).</summary>
        static Vector3[] PieceCorners(WorldKitCatalog cat, string piece)
        {
            if (piece == null) return Array.Empty<Vector3>();
            if (PieceBoxes.TryGetValue(piece, out Vector3[] c)) return c;
            var list = new List<Vector3>();
            GameObject model = cat != null ? cat.Find(piece) : null;
            if (model != null)
                foreach (MeshFilter mf in model.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Bounds b = mf.sharedMesh.bounds;
                    Matrix4x4 m = model.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    for (int k = 0; k < 8; k++)
                        list.Add(m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((k & 1) != 0 ? 1f : -1f, (k & 2) != 0 ? 1f : -1f, (k & 4) != 0 ? 1f : -1f))));
                }
            return PieceBoxes[piece] = list.ToArray();
        }

        /// <summary>fix/px-combat-camera-2: one placement's solid volume as cylinders (one per kit mesh: a crown and a trunk
        /// that are separate meshes stay separate), sim space. Without the kit model: the old tree numbers with a crown
        /// radius, rocks and crystals as small cylinders, anything else skipped. Volumes lower than 0.5 u are skipped.</summary>
        void AddScenery(in WorldPlacement p, Vector3[] corners, bool soft)
        {
            for (int b = 0; b + 8 <= corners.Length && _sceneryCount < MaxScenery; b += 8)
            {
                double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue, y0 = double.MaxValue, y1 = double.MinValue;
                var pts = new Vector3[8];
                for (int k = 0; k < 8; k++)
                {
                    Vector3 v = p.Matrix.MultiplyPoint3x4(corners[b + k]);
                    pts[k] = v;
                    x0 = Math.Min(x0, v.x); x1 = Math.Max(x1, v.x); z0 = Math.Min(z0, -v.z); z1 = Math.Max(z1, -v.z); y0 = Math.Min(y0, v.y); y1 = Math.Max(y1, v.y);
                }
                if (y1 - p.Y < 0.5) continue;
                double cx = (x0 + x1) * 0.5, cz = (z0 + z1) * 0.5, r = 0;
                for (int k = 0; k < 8; k++) r = Math.Max(r, Math.Sqrt((pts[k].x - cx) * (pts[k].x - cx) + (-pts[k].z - cz) * (-pts[k].z - cz)));
                if (r > 8) continue;   // a slab or an island piece, not a prop
                // fix/px-rough-spots (item 2: the lens pressed 1.6 u behind him by the hill rim): one cylinder round a long box (an
                // arc, a horn, a wall run) covered open ground up to 2.6 u from the real mesh. A box longer than SplitAspect x its
                // width becomes a row of cylinders along its own long axis, each just wide enough for its slice.
                Vector3 ea = pts[1] - pts[0], eb = pts[4] - pts[0];
                double la = Math.Sqrt(ea.x * ea.x + ea.z * ea.z), lb = Math.Sqrt(eb.x * eb.x + eb.z * eb.z);
                double lLong = Math.Max(la, lb), lShort = Math.Min(la, lb);
                // fix/px-rough-spots: a small mesh inside a big layout piece (a brazier, a barrel stack, a post) fades like a prop
                if (!soft && r < HardPropExtent + 0.2) soft = true;
                if (!soft && lShort > 0.05 && lLong > SplitAspect * lShort && _sceneryCount + 8 < MaxScenery)
                {
                    Vector3 ax = la >= lb ? ea : eb;
                    double axx = ax.x / lLong, axz = -ax.z / lLong;   // sim space (z flipped)
                    int nseg = Math.Min(6, (int)Math.Ceiling(lLong / lShort));
                    double seg = lLong / nseg, rs = Math.Sqrt(lShort * lShort * 0.25 + seg * seg * 0.25);
                    for (int q = 0; q < nseg; q++)
                    {
                        double o = -lLong * 0.5 + seg * (q + 0.5);
                        _scenery[_sceneryCount] = new SeeThroughCylinder { X = cx + axx * o, Z = cz + axz * o, R = rs, Y0 = y0, Y1 = y1, Soft = soft, Id = _sceneryCount + 1 };
                        _sceneryCount++;
                    }
                    continue;
                }
                _scenery[_sceneryCount] = new SeeThroughCylinder { X = cx, Z = cz, R = r, Y0 = y0, Y1 = y1, Soft = soft, Id = _sceneryCount + 1 };
                _sceneryCount++;
            }
        }

        /// <summary>Trees and landmarks (towers) from the dressed world as cylinders (rebuilt when the dressing changes).</summary>
        void RefreshStatic()
        {
            if (_world == null && Driver != null) _world = Driver.GetComponent<WorldView>();
            if (_world == null || _world.Plan == null) { _staticCount = 0; return; }
            if (_staticVersion == WorldView.DressVersion) return;
            _staticVersion = WorldView.DressVersion;
            _staticCount = 0;
            _sceneryCount = 0;
            List<WorldPlacement> ps = _world.Plan.Placements;
            // fix/px-combat-camera-2: WHY the framing parked in trees: the camera never used physics or renderers; its only
            // scenery was the list below, trees as 0.6 u TRUNK cylinders (the crowns are ~2 u wide) and no rocks, crystals,
            // props or buildings at all. The framing now tests the full set (the regular follow keeps the list below).
            WorldKitCatalog cat = WorldKitCatalog.Load();
            for (int i = 0; i < ps.Count && _sceneryCount < MaxScenery; i++)
            {
                WorldPlacement p = ps[i];
                if (!SolidScenery(p)) continue;
                // breakable states come and go with the match; the grid (SolidTop) already stands them up like walls
                if (p.Piece != null && (p.Piece.IndexOf("intact", StringComparison.Ordinal) >= 0 || p.Piece.IndexOf("cracked", StringComparison.Ordinal) >= 0
                                        || p.Piece.IndexOf("destroyed", StringComparison.Ordinal) >= 0)) continue;
                Vector3[] corners = PieceCorners(cat, p.Piece);
                if (corners.Length > 0)
                {
                    // pass 5: small props (barrels, banners, lamps, stakes) fade like trees; only big ones move the boom
                    double ext = 0;
                    for (int c = 0; c < corners.Length; c++) ext = Math.Max(ext, Math.Sqrt(corners[c].x * corners[c].x + corners[c].z * corners[c].z));
                    AddScenery(p, corners, SoftScenery(p.Cat) || ext < HardPropExtent || SoftPiece(p.Piece));
                    continue;
                }
                double r, h;
                if (p.Cat == "tree") { r = SeeThrough.TreeRadius * 1.5; h = p.Piece != null && p.Piece.IndexOf("tall", StringComparison.Ordinal) >= 0 ? SeeThrough.TreeHeightTall : SeeThrough.TreeHeightRound; }
                else if (p.Cat == "rock_big") { r = 0.8; h = 1.2; }
                else if (p.Cat == "crystal") { r = 0.6; h = 1.6; }
                else if (p.Cat == "landmark") { r = 1.6; h = 9.0; }
                else continue;
                _scenery[_sceneryCount] = new SeeThroughCylinder { X = p.SimX, Z = p.SimZ, R = r, Y0 = p.Y - 1, Y1 = p.Y + h, Soft = SoftScenery(p.Cat), Id = _sceneryCount + 1 };
                _sceneryCount++;
            }
            for (int i = 0; i < ps.Count && _staticCount < MaxStatic; i++)
            {
                WorldPlacement p = ps[i];
                double r, h;
                if (p.Cat == "tree")
                {
                    r = 0.6;
                    h = p.Piece != null && p.Piece.IndexOf("tall", StringComparison.Ordinal) >= 0 ? SeeThrough.TreeHeightTall : SeeThrough.TreeHeightRound;
                }
                else if (p.Cat == "landmark") { r = 1.6; h = 9.0; }
                else continue;
                _static[_staticCount++] = new SeeThroughCylinder { X = p.SimX, Z = p.SimZ, R = r, Y0 = p.Y - 1, Y1 = p.Y + h };
            }
        }

        /// <summary>fix/px-combat-camera-2: the scenery volumes near the framed fight this frame (tests).</summary>
        public int Scenery(out SeeThroughCylinder[] cyl)
        {
            cyl = _scn;
            return _scnCount;
        }

        /// <summary>fix/px-combat-camera-2: the reticle (GUI pixels, y down). Unframed: the screen centre (its ground ray IS
        /// the aim). Framed, the view is turned off his own yaw, so the reticle moves (by K) onto where the swing really
        /// goes, his aim point (TryPlayAim): what the screen shows and where a click swings never disagree.</summary>
        public Vector2 ReticleGui(World w)
        {
            var c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            float k = (float)_combat.K;
            if (!(k > 0) || _cam == null || w == null || !TryPlayAim(w, out double ax, out double az)) return c;
            Vector3 sp = _cam.WorldToScreenPoint(SimSpace.ToWorld(ax, GroundHeight.At(w.Map, ax, az), az));
            if (sp.z <= 0f) return c;
            return Vector2.Lerp(c, new Vector2(sp.x, Screen.height - sp.y), k);
        }

        /// <summary>
        /// fix/px-combat-camera-2: where a head tag (name, bar, row words, the YOU marker) should project from. The HUD puts
        /// tags at a fixed lift over the ground (2.65 u); the framed camera is closer and lower, so that lift drew the enemy's
        /// name and bar far above his head, up under the resource banner. While framed (by K) the anchor moves, at the same
        /// depth, to just above the drawn head top (CombatFrame.TagScreenY) and below the top HUD rows. Unframed: unchanged.
        /// </summary>
        public static Vector3 TagPoint(World w, int id, Vector3 head, float lift)
        {
            GroundView g = Current;
            if (g == null || g._cam == null || g.Mode != GroundViewMode.Play || w == null || id < 0 || id >= w.ActorCount) return head;
            if (g._view == CameraView.FirstPerson) return head;
            // fix/px-combat-camera-3 (pass 3: the YOU tag jumped between the top-band clamp and the head): my own tag always
            // takes the just-above-the-head rule at full weight in third person (one stable spot); the others by K.
            bool mine = id == LocalSeat.Actor;
            float k = mine ? 1f : (float)g._combat.K;
            Vector3 sh = g._cam.WorldToScreenPoint(head);
            if (sh.z <= 0f) return head;
            float y = sh.y;
            if (k > 0f && FitY(g._cam, w, id, head, lift, out float fy)) y = Mathf.Lerp(sh.y, fy, k);
            // fix/px-combat-camera-3 (pass 3: YOU sat on the enemy's tag): my tag never overlaps another fighter's tag; it
            // is pushed above it (below when the top HUD rows leave no room)
            if (mine) y = Separate(g, w, id, sh.x, y);
            if (Mathf.Abs(y - sh.y) < 0.01f) return head;
            return g._cam.ScreenToWorldPoint(new Vector3(sh.x, y, sh.z));
        }

        /// <summary>The framed rule's screen y for fighter id's tag (CombatFrame.TagScreenY).</summary>
        static bool FitY(UnityEngine.Camera cam, World w, int id, Vector3 head, float lift, out float y)
        {
            y = 0f;
            FightFrame.Silhouette(w.Actors[id].Cls, 0, out _, out double h);
            Vector3 top = new Vector3(head.x, head.y - lift + (float)h, head.z);
            Vector3 sh = cam.WorldToScreenPoint(head), st = cam.WorldToScreenPoint(top);
            if (sh.z <= 0f || st.z <= 0f) return false;
            y = (float)CombatFrame.TagScreenY(sh.y, st.y, cam.pixelHeight);
            return true;
        }

        /// <summary>fix/px-combat-camera-3: tag box (share of the screen) two tags may not share.</summary>
        public const float TagSepW = 0.06f, TagSepH = 0.05f;

        /// <summary>My tag's screen y pushed clear of every other standing fighter's tag near me (theirs at the HUD's lift,
        /// K-weighted like TagPoint).</summary>
        static float Separate(GroundView g, World w, int me, float x, float y)
        {
            UnityEngine.Camera cam = g._cam;
            float W = cam.pixelWidth, H = cam.pixelHeight, sw = TagSepW * W, shh = TagSepH * H;
            float ceiling = (float)((1 - CombatFrame.TagTopShare) * H);
            double a = g.Driver != null ? g.Driver.Alpha : 1;
            float k = (float)g._combat.K;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < w.ActorCount; i++)
                {
                    if (i == me || !w.Actors[i].Alive) continue;
                    double ox = w.PrevX[i] + (w.X[i] - w.PrevX[i]) * a, oz = w.PrevZ[i] + (w.Z[i] - w.PrevZ[i]) * a;
                    double dx = ox - w.X[me], dz = oz - w.Z[me];
                    if (dx * dx + dz * dz > 64) continue;
                    Vector3 oh = SimSpace.ToWorld(ox, GroundHeight.At(w.Map, ox, oz) + 2.65, oz);
                    Vector3 os = cam.WorldToScreenPoint(oh);
                    if (os.z <= 0f) continue;
                    float oy = os.y;
                    if (k > 0f && FitY(cam, w, i, oh, 2.65f, out float fy)) oy = Mathf.Lerp(os.y, fy, k);
                    y = SeparateY(x, y, os.x, oy, sw, shh, ceiling);
                }
            return y;
        }

        /// <summary>Pure (tests): my tag y moved clear of one other tag at (ox, oy): above it, or below when that would
        /// cross the ceiling. Unchanged when the two boxes do not overlap.</summary>
        public static float SeparateY(float x, float y, float ox, float oy, float sepW, float sepH, float ceiling)
        {
            if (Mathf.Abs(x - ox) >= sepW || Mathf.Abs(y - oy) >= sepH) return y;
            float up = oy + sepH;
            return up <= ceiling ? up : oy - sepH;
        }

        /// <summary>The camera volumes near the target this frame (tests run the same Inside check).</summary>
        public int Cylinders(out SeeThroughCylinder[] cyl)
        {
            cyl = _cyl;
            return _cylCount;
        }

        // ---------------------------------------------------------------- overlay (reticle, threat ring, mode line)

        void Toast(string s)
        {
            // hud-containment-2: the notice goes to the toast lane (a cap and a queue; it waits behind an achievement toast).
            ToastLane.Post(s, Time.unscaledTime);
        }

        static Texture2D _tri, _dot;
        static GUIStyle _style;

        static Texture2D Tri()
        {
            if (_tri != null) return _tri;
            const int n = 32;
            _tri = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // A triangle pointing up (+y), 1 px soft edge.
                    float half = (y + 0.5f) / n * 0.5f * n;
                    float d = half - Mathf.Abs(x + 0.5f - n * 0.5f);
                    byte a = (byte)(Mathf.Clamp01(d) * 255);
                    px[y * n + x] = new Color32(255, 255, 255, a);
                }
            _tri.SetPixels32(px);
            _tri.Apply();
            return _tri;
        }

        static Texture2D Dot()
        {
            if (_dot != null) return _dot;
            const int n = 16;
            _dot = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f));
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(n * 0.5f - d) * 255));
                }
            _dot.SetPixels32(px);
            _dot.Apply();
            return _dot;
        }

        static readonly string WatchLine = "GROUND VIEW   F5 HIGH VIEW   Q / E NEXT   RIGHT DRAG ORBIT   F6 DIRECTOR";

        /// <summary>The play hint, rebuilt only when the view or the follow mode changes (no per-frame strings).</summary>
        string PlayHint()
        {
            if (_lineView == _view && _lineManual == _manual) return _playLine;
            _lineView = _view;
            _lineManual = _manual;
            string next = FollowCamera.Label(FollowCamera.Next(_view));
            _playLine = FollowCamera.Label(_view) + (_manual ? "   MOUSE LOOK   MIDDLE CLICK RE-CENTRE" : "   AUTO FOLLOW")
                + "   F5 " + next;
            return _playLine;
        }

        void OnGUI()
        {
            if (Mode == GroundViewMode.Off || Event.current.type != EventType.Repaint) return;
            float s = Screen.height / 1080f;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            }
            _style.fontSize = Mathf.Max(10, Mathf.RoundToInt(15 * s));
            Color old = GUI.color;
            // Mode line at the bottom centre, quiet (it never covers the fight).
            GUI.color = new Color(1f, 0.97f, 0.88f, 0.75f);
            string line = Mode == GroundViewMode.Play ? PlayHint() : WatchLine;
            // readability-4: the view-hint row only while ViewHintGate allows it (not over menus or the banner line, and it
            // retires once he has learned it)
            if (CriticalMass.Runtime.Hud.ViewHintGate.Show && GetComponent<GroundViewShots>() == null && GetComponent<FollowCameraShots>() == null) GUI.Label(new Rect(0, Screen.height - 40 * s, Screen.width, 28 * s), line, _style);
            string toast = ToastLane.Current(Time.unscaledTime);
            if (toast != null)
            {
                GUI.color = new Color(1f, 0.85f, 0.45f, ToastLane.Alpha(Time.unscaledTime));   // fix/hud-containment-5: fades in and out
                _style.fontSize = Mathf.RoundToInt(26 * s);
                GUI.Label(ToastLane.PixelRect(Screen.width, Screen.height, 40 * s), toast, _style);
            }
            if (Mode == GroundViewMode.Play) { DrawAimTrail(s); DrawPlayAids(s); DrawViewCone(s); }
            GUI.color = old;
        }

        /// <summary>
        /// feat/follow-camera-1: the minimap stays fixed (the baked island picture, the markers and the click-to-ping maths
        /// all assume its heading), and a soft view cone at the player's marker shows where the low camera looks. Drawn
        /// with the reticle's dot texture (no new texture, no allocation).
        /// </summary>
        void DrawViewCone(float s)
        {
            World w = Driver != null ? Driver.World : null;
            GameHud hud = Driver != null ? Driver.GetComponent<GameHud>() : null;
            if (w == null || hud == null || hud.Clusters == null || hud.Canvas == null) return;
            RectTransform c = hud.Clusters[(int)HudCluster.Minimap];
            if (c == null || !c.gameObject.activeInHierarchy) return;
            CriticalMass.Runtime.Hud.Ui.MinimapProjection proj = CriticalMass.Runtime.Hud.Ui.MinimapGeometry.Projection;
            if (proj == null || proj.Width <= 0 || proj.Height <= 0) return;
            c.GetWorldCorners(_corners);
            UnityEngine.Camera uiCam = hud.Canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : hud.Canvas.worldCamera;
            Vector2 bl = RectTransformUtility.WorldToScreenPoint(uiCam, _corners[0]);
            Vector2 tr = RectTransformUtility.WorldToScreenPoint(uiCam, _corners[2]);
            float fw = (tr.x - bl.x), fh = (tr.y - bl.y);
            if (fw < 8f || fh < 8f) return;
            // The map image sits inside the frame (HudLayout.MinimapFrame on each side of the reference size).
            float kx = fw / HudLayout.MinimapSize.x, ky = fh / HudLayout.MinimapSize.y;
            float left = bl.x + HudLayout.MinimapFrame * kx, top = Screen.height - tr.y + HudLayout.MinimapFrame * ky;
            float iw = fw - 2f * HudLayout.MinimapFrame * kx, ih = fh - 2f * HudLayout.MinimapFrame * ky;
            Vector2 px = proj.ToPixel(w.X[LocalSeat.Actor], w.Z[LocalSeat.Actor]);
            Vector2 at = new Vector2(left + px.x / proj.Width * iw, top + px.y / proj.Height * ih);
            GroundCamera.Basis(_follow.Yaw, out double ux, out double uz, out _, out _);
            Vector2 dir = new Vector2((float)proj.U(ux, uz), (float)proj.V(ux, uz));
            if (dir.sqrMagnitude < 1e-6f) return;
            dir.Normalize();
            Vector2 side = new Vector2(-dir.y, dir.x);
            float half = Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
            GUI.color = new Color(1f, 0.96f, 0.86f, 0.22f);
            for (int i = 1; i <= 7; i++)
            {
                float d = i * 4.5f * s, r = Mathf.Max(1.5f * s, d * half * 0.55f);
                Vector2 p = at + dir * d;
                for (int j = -1; j <= 1; j++)
                {
                    Vector2 q = p + side * (j * d * half * 0.6f);
                    GUI.DrawTexture(new Rect(q.x - r * 0.5f, q.y - r * 0.5f, r, r), Dot());
                }
            }
        }

        /// <summary>
        /// feat/follow-camera-1: where skills and shots go, on the ground in front of the character: a dotted trail from his
        /// feet to the aim point the sim gets (TryPlayAim), the dots smaller with distance, a larger dot at the end. Points
        /// behind the lens are skipped (first person starts the trail below the screen). No texture or mesh added.
        /// </summary>
        void DrawAimTrail(float s)
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null || _cam == null || !TryPlayAim(w, out double ax, out double az)) return;
            double a = Driver.Alpha;
            double px = w.PrevX[LocalSeat.Actor] + (w.X[LocalSeat.Actor] - w.PrevX[LocalSeat.Actor]) * a, pz = w.PrevZ[LocalSeat.Actor] + (w.Z[LocalSeat.Actor] - w.PrevZ[LocalSeat.Actor]) * a;
            const int n = 9;
            for (int k = 2; k <= n; k++)
            {
                double t = k / (double)n;
                double x = px + (ax - px) * t, z = pz + (az - pz) * t;
                Vector3 g = SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + 0.06, z);
                Vector3 sp = _cam.WorldToScreenPoint(g);
                if (sp.z <= 0.2f) continue;
                float size = Mathf.Clamp(60f / sp.z, 3f, 14f) * s * (k == n ? 1.8f : 1f);
                float gy = Screen.height - sp.y;
                GUI.color = new Color(0.06f, 0.08f, 0.16f, 0.45f);
                GUI.DrawTexture(new Rect(sp.x - size * 0.65f, gy - size * 0.65f, size * 1.3f, size * 1.3f), Dot());
                GUI.color = new Color(1f, 0.96f, 0.86f, k == n ? 0.9f : 0.6f);
                GUI.DrawTexture(new Rect(sp.x - size * 0.5f, gy - size * 0.5f, size, size), Dot());
            }
        }

        void DrawPlayAids(float s)
        {
            World w = Driver != null ? Driver.World : null;
            if (w == null || _cam == null) return;
            // Reticle: a small cream dot with a dark ring, at the screen centre (the aim point is along this ray).
            Vector2 c = ReticleGui(w);
            GUI.color = new Color(0.06f, 0.08f, 0.16f, 0.8f);
            GUI.DrawTexture(new Rect(c.x - 7 * s, c.y - 7 * s, 14 * s, 14 * s), Dot());
            GUI.color = new Color(1f, 0.96f, 0.86f, 1f);
            GUI.DrawTexture(new Rect(c.x - 4 * s, c.y - 4 * s, 8 * s, 8 * s), Dot());
            // Off-screen threat ring: enemies within ThreatRange that the camera cannot see, as team-coloured pointers on a
            // ring around the reticle (behind the player = bottom of the ring).
            TeamId me = w.Actors[LocalSeat.Actor].Team;
            float ring = Screen.height * 0.30f;
            GroundCamera.Basis(_follow.Yaw, out double ux, out double uz, out double rx, out double rz);
            for (int i = 1; i < w.ActorCount; i++)
            {
                Actor a = w.Actors[i];
                if (!a.Alive || a.Team == me) continue;
                double dx = w.X[i] - w.X[LocalSeat.Actor], dz = w.Z[i] - w.Z[LocalSeat.Actor];
                double d2 = dx * dx + dz * dz;
                if (d2 > ThreatRange * ThreatRange) continue;
                Vector3 vp = _cam.WorldToViewportPoint(SimSpace.ToWorld(w.X[i], GroundHeight.At(w.Map, w.X[i], w.Z[i]) + 1.0, w.Z[i]));
                if (vp.z > 0 && vp.x > 0.02f && vp.x < 0.98f && vp.y > 0.02f && vp.y < 0.98f) continue;
                double fwd = dx * ux + dz * uz, side = dx * rx + dz * rz;
                float ang = Mathf.Atan2((float)side, (float)fwd);   // 0 = ahead (top of the ring), +90 = right
                float near = 1f - Mathf.Sqrt((float)d2) / (float)ThreatRange;
                Vector2 p = c + new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * ring;
                float size = (18f + 14f * near) * s;
                Color tc = CMTokens.Team(a.Team);
                tc.a = 0.55f + 0.45f * near;
                GUI.color = tc;
                Matrix4x4 m = GUI.matrix;
                GUIUtility.RotateAroundPivot(ang * Mathf.Rad2Deg + 180f, p);   // the texture apex is its bottom row: point outward, at the threat
                GUI.DrawTexture(new Rect(p.x - size * 0.5f, p.y - size * 0.5f, size, size), Tri());
                GUI.matrix = m;
            }
        }
    }
}
