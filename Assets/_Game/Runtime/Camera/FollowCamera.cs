using System;
using CriticalMass.Sim.Grid;
using UnityEngine;

namespace CriticalMass.Runtime.Camera
{
    /// <summary>The three play views; F5 (the "Camera view" action) cycles them in a match.</summary>
    public enum CameraView { TopDown = 0, ThirdPerson = 1, FirstPerson = 2 }

    /// <summary>
    /// feat/follow-camera-1 (owner 2026-11-14: "make it more like GTA where it's like above their head ... the camera does not
    /// follow and point where they're at. Can we make that happen automatically please ... and manually too in the
    /// settings"). The numbers and the pure maths of the third- and first-person follow views. View only: nothing here
    /// reaches the sim. Unity yaw convention (GroundCamera's).
    /// </summary>
    public static class FollowCamera
    {
        // Third person: behind and above the head and shoulders, the character in the lower-middle of the screen.
        public const double ThirdDistance = 4.4, ThirdPivotHeight = 2.2, ThirdPitch = 14, ThirdMinPitch = -10, ThirdMaxPitch = 50;
        // First person: the eyes (CharacterHeight is 2.0), a little ahead of the body centre.
        // follow-camera-3: eyes at 1.9 (the heads sit near 2.1; tags float at 2.65) so the wall tops stop filling the view.
        public const double EyeHeight = 1.9, EyeForward = 0.18, FirstPitch = 3, FirstMinPitch = -40, FirstMaxPitch = 45;
        /// <summary>Look-ahead: the pivot leads the travel by this many seconds of velocity, at most LeadMax u.</summary>
        public const double LeadSeconds = 0.3, LeadMax = 1.2;
        /// <summary>Manual mode: seconds without look input before the camera re-centres behind the character.</summary>
        public const double RecenterSeconds = 3.0;
        /// <summary>Speed (u/s) above which the character counts as travelling (its travel direction leads the yaw).</summary>
        public const double MovingSpeed = 0.6;
        /// <summary>Fighter draw distance in the low views (u): beyond it, or outside the view, fighters are not posed or drawn.</summary>
        public const float FighterDrawDistance = 90f;
        /// <summary>follow-camera-3: fighters' level of detail in the low views, by distance from the CAMERA (not the
        /// followed body): LOD0 within LowLod0Metres and inside the crowd budget, LOD1 beyond (the models have two levels).
        /// Top-down keeps Lod0Metres 25 u to the target.</summary>
        public const float LowLod0Metres = 9f;
        /// <summary>At most this many other fighters draw LOD0 in a low view (the nearest to the followed body).</summary>
        public const int LowLod0Budget = 6;

        // ---- follow-camera-3: first person never stares into a wall

        /// <summary>A wall nearer than WallNear u straight ahead of the eyes turns the auto-follow view (not the controls)
        /// by the smallest of WallTurns degrees that looks at least WallNear clear; probes reach WallLook u.</summary>
        public const double WallNear = 1.6, WallLook = 3.0;
        static readonly double[] WallTurns = { 25, -25, 50, -50, 75, -75 };

        /// <summary>Clear distance (u) from the eyes (sim x, y, z) straight ahead along a yaw, up to WallLook (pure).</summary>
        public static double Ahead(MapDef map, double x, double y, double z, double yaw, SeeThroughCylinder[] cyl, int n)
        {
            GroundCamera.Basis(yaw, out double ux, out double uz, out _, out _);
            return GroundCollision.Resolve(map, x, y, z, ux, 0, uz, WallLook, cyl, n) + GroundCollision.Clearance;
        }

        /// <summary>The yaw to look along in first person (pure; tests): the wanted yaw when it is clear for WallNear u,
        /// else the nearest turn that is, else the most open turn. No map: unchanged.</summary>
        public static double WallYaw(MapDef map, double x, double y, double z, double yaw, SeeThroughCylinder[] cyl, int n)
        {
            if (map == null) return yaw;
            double free = Ahead(map, x, y, z, yaw, cyl, n);
            if (free >= WallNear) return yaw;
            double best = yaw, bestFree = free;
            for (int k = 0; k < WallTurns.Length; k++)
            {
                double y2 = GroundCamera.Normalize(yaw + WallTurns[k]);
                double f2 = Ahead(map, x, y, z, y2, cyl, n);
                if (f2 >= WallNear) return y2;
                if (f2 > bestFree + 0.2) { best = y2; bestFree = f2; }
            }
            return best;
        }

        /// <summary>How far ahead of the pivot the eyes sit (pure; tests): EyeForward when that point is clear of walls,
        /// 0 when only the pivot is clear, and EyeBack behind it when even the pivot touches one (never inside a wall).</summary>
        public const double EyeBack = -0.3, EyeRadius = 0.2;
        public static double EyeOffset(MapDef map, double px, double py, double pz, double ux, double uz, SeeThroughCylinder[] cyl, int n)
        {
            if (map == null) return EyeForward;
            if (!GroundCollision.Inside(map, px + ux * EyeForward, py, pz + uz * EyeForward, EyeRadius, cyl, n)) return EyeForward;
            if (!GroundCollision.Inside(map, px, py, pz, EyeRadius, cyl, n)) return 0;
            return EyeBack;
        }

        /// <summary>The LOD a fighter draws in a low view (pure; tests): self always 0; LOD0 only within LowLod0Metres of
        /// the camera and inside the crowd budget cut (distance to the followed body).</summary>
        public static int LowViewLod(bool self, float distToCamera, float distToTarget, float budgetCut)
        {
            if (self) return 0;
            if (distToCamera > LowLod0Metres) return 1;
            return distToTarget > budgetCut ? 1 : 0;
        }
        public const int FovMin = 50, FovMax = 90, FovDefault = 65, FovStep = 5;

        public static GroundRig ThirdRig => new GroundRig
        {
            Set = true, Distance = ThirdDistance, PivotHeight = ThirdPivotHeight, Shoulder = 0,
            MinPitch = ThirdMinPitch, MaxPitch = ThirdMaxPitch,
        };

        public static GroundRig FirstRig => new GroundRig
        {
            Set = true, FirstPerson = true, Distance = 0, PivotHeight = EyeHeight, Shoulder = 0,
            MinPitch = FirstMinPitch, MaxPitch = FirstMaxPitch,
        };

        public static CameraView Next(CameraView v) => (CameraView)(((int)v + 1) % 3);

        public static string Label(CameraView v) =>
            v == CameraView.ThirdPerson ? "THIRD PERSON" : v == CameraView.FirstPerson ? "FIRST PERSON" : "TOP-DOWN";

        /// <summary>The look-ahead offset (sim x, z) for a velocity (u/s); zero under reduce motion.</summary>
        public static void Lead(double vx, double vz, bool reduceMotion, out double lx, out double lz)
        {
            lx = lz = 0;
            if (reduceMotion) return;
            lx = vx * LeadSeconds;
            lz = vz * LeadSeconds;
            double l = Math.Sqrt(lx * lx + lz * lz);
            if (l > LeadMax) { lx *= LeadMax / l; lz *= LeadMax / l; }
        }
    }

    /// <summary>
    /// The auto-follow yaw (pure, testable): a critically damped angular spring (no overshoot, no snapping) toward the
    /// character's travel direction, which only becomes the new target after it has held for a short delay, so quick taps
    /// do not swing the view: 0.15 s within 60 degrees of the view, 0.6 s for a bigger turn (a sideways step does not spin
    /// the camera; a real turn-around swings it behind). Re-centring (a key, or the idle timer in manual mode) uses the
    /// same spring with no delay.
    /// </summary>
    public sealed class YawFollow
    {
        /// <summary>Spring smooth time (s): settles behind a 90-degree turn in about 1.2 s after the delay.</summary>
        public const double SmoothTime = 0.32;
        public const double Delay = 0.15, BigTurnDelay = 0.6, BigTurnDeg = 60, SameDirDeg = 12;
        /// <summary>Reduce motion: a quicker spring (less time spent sliding) and no look-ahead.</summary>
        public const double ReducedSmoothTime = 0.18;

        public double Yaw { get; private set; } = 315;
        /// <summary>Angular velocity (degrees per second).</summary>
        public double Rate { get; private set; }
        public double Target { get; private set; } = 315;

        double _pending = double.NaN, _held;

        public void Reset(double yaw)
        {
            Yaw = Target = GroundCamera.Normalize(yaw);
            Rate = 0;
            _pending = double.NaN;
            _held = 0;
        }

        /// <summary>Make this the target now (re-centre): no delay.</summary>
        public void Recenter(double want)
        {
            Target = GroundCamera.Normalize(want);
            _pending = double.NaN;
            _held = 0;
        }

        /// <summary>One frame: want = the yaw behind the character (its travel direction, or its facing when standing).</summary>
        public void Step(double dt, double want, bool reduceMotion)
        {
            if (!(dt > 0)) return;
            want = GroundCamera.Normalize(want);
            if (Math.Abs(GroundCamera.DeltaAngle(Target, want)) <= SameDirDeg)
            {
                Target = want;   // small corrections are followed at once (no delay)
                _pending = double.NaN;
                _held = 0;
            }
            else
            {
                if (double.IsNaN(_pending) || Math.Abs(GroundCamera.DeltaAngle(_pending, want)) > SameDirDeg) { _pending = want; _held = 0; }
                else { _pending = want; _held += dt; }
                double need = Math.Abs(GroundCamera.DeltaAngle(Yaw, want)) > BigTurnDeg ? BigTurnDelay : Delay;
                if (_held >= need) { Target = want; _pending = double.NaN; _held = 0; }
            }
            // Critically damped spring (the SmoothDamp form): change = yaw - target as a signed angle.
            double smooth = reduceMotion ? ReducedSmoothTime : SmoothTime;
            double omega = 2.0 / smooth;
            double x = omega * dt;
            double exp = 1.0 / (1.0 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = GroundCamera.DeltaAngle(Target, Yaw);
            double temp = (Rate + omega * change) * dt;
            Rate = (Rate - omega * temp) * exp;
            double next = (change + temp) * exp;
            // Never cross the target (the approximation can by a hair when the target jumps): clamp and stop.
            if (change > 0 ? next < 0 : change < 0 && next > 0) { next = 0; Rate = 0; }
            Yaw = GroundCamera.Normalize(Target + next);
        }
    }

    /// <summary>
    /// follow-camera-2: name tags and overhead health in the third- and first-person views. They are screen-space marks
    /// placed at the head, so they always face the camera; this sets their size by distance from the camera (larger up
    /// close, smaller far, within limits) and hides them too close to the lens or beyond a distance.
    /// </summary>
    public static class TagLens
    {
        public const float TagMaxDistance = 28f, BarMaxDistance = 32f, MinDistance = 0.9f;
        /// <summary>Distance (u) at which a tag is its authored size.</summary>
        public const float UnitDistance = 9f;
        public const float MinScale = 0.6f, MaxScale = 1.2f;

        public static float Scale(float d) => d <= 1e-3f ? MaxScale : Mathf.Clamp(UnitDistance / d, MinScale, MaxScale);
        public static bool Show(float d, float max) => d >= MinDistance && d <= max;
    }

    /// <summary>
    /// follow-camera-2: a sideways run does not turn the view. While the move is sideways with no forward or back input
    /// (|up| under 0.2, |right| over 0.3), auto-follow holds its yaw; when forward input returns after a hold, it re-centres
    /// behind the character at once. Back-only input keeps the turn-around swing (after YawFollow.BigTurnDelay).
    /// </summary>
    public sealed class StrafeGate
    {
        public const double UpDead = 0.2, SideMin = 0.3;
        bool _holding;
        public bool Holding => _holding;

        /// <summary>One frame of input (screen right, up); recenter is true on the frame forward input returns after a hold.</summary>
        public void Step(double right, double up, out bool hold, out bool recenter)
        {
            bool strafe = Math.Abs(up) < UpDead && Math.Abs(right) > SideMin;
            recenter = _holding && !strafe && up >= UpDead;
            _holding = strafe;
            hold = strafe;
        }
    }

    /// <summary>
    /// Camera-relative movement that does not chase its own tail (pure, testable): the yaw that maps the keys / stick to a
    /// world direction is the camera's yaw when the move began, held while any move input stays held, so a run toward the
    /// camera keeps going the same way while auto-follow swings the camera behind. Released, or the player looks
    /// (manual orbit), it takes the camera's yaw again at once.
    /// </summary>
    public sealed class InputLatch
    {
        public double Yaw { get; private set; } = 315;
        bool _held;

        public double Step(bool moveHeld, bool looked, double cameraYaw)
        {
            if (!moveHeld || !_held || looked) Yaw = GroundCamera.Normalize(cameraYaw);
            _held = moveHeld;
            return Yaw;
        }
    }

    /// <summary>
    /// Frustum and distance culling for the low views (no allocation: the six planes live in one array, refreshed once
    /// a frame), plus what the fighters submitted this frame, so the third-person cost can be compared with top-down.
    /// </summary>
    public static class ViewCull
    {
        static readonly Plane[] Planes = new Plane[6];
        static int _frame = -1;
        static Vector3 _cam;
        static bool _on, _lastWasSample;

        /// <summary>follow-camera-2: cosmetic effects (FxMeshBuilder primitives from combat, gore that GoreGate let through
        /// and skills; rubble pieces) beyond this distance from the low camera, or outside its view, are not drawn (u).</summary>
        public const float FxDrawDistance = 70f;
        /// <summary>Extra room past the frustum edge before an effect is dropped (u): no popping at the screen edge.</summary>
        public const float FxMargin = 1.5f;
        /// <summary>Effect primitives and rubble pieces drawn and culled this frame (all builders and the rubble pool), and
        /// the same numbers last frame.</summary>
        public static int FxDrawn, FxCulled, RubbleCulled, LastFxDrawn, LastFxCulled, LastRubbleCulled;

        /// <summary>Recompute the planes from a camera now and switch the culling on or off (tests; Begin calls it once a frame).</summary>
        public static void SetView(UnityEngine.Camera cam, bool on)
        {
            _on = on && cam != null;
            if (!_on) return;
            _cam = cam.transform.position;
            GeometryUtility.CalculateFrustumPlanes(cam, Planes);
        }

        public static bool On => _on;
        /// <summary>follow-camera-3: the low-view camera's position this frame (fighter LOD by camera distance).</summary>
        public static Vector3 CamPos => _cam;

        // ---- follow-camera-3: effects near the lens, on the sight line, or too big on screen

        /// <summary>No effect sprite whose nearest edge is within NearCull u of the lens; shrinking from NearFade in.</summary>
        public const float NearCull = 1.2f, NearFade = 3.5f;
        /// <summary>Clearance kept around the line from the camera to the followed fighter's chest (u).</summary>
        public const float LineClear = 0.5f;
        /// <summary>Screen size cap: an effect's radius never exceeds this tangent of its distance (about 10 degrees, a
        /// third of the 65-degree lens's height across).</summary>
        public const float MaxScreenTan = 0.18f;
        public static int FxNearCulled, FxShrunk, LastFxNearCulled, LastFxShrunk;
        static Vector3 _lineTo;
        static bool _lineOn;

        /// <summary>GroundView, once a frame: the sight line to keep clear (third person and watching), off in first person.</summary>
        public static void SetFollowLine(Vector3 chest, bool on)
        {
            _lineTo = chest;
            _lineOn = on;
        }

        /// <summary>
        /// The scale an effect sprite (centre c, radius r) draws at in the low views: 0 = not drawn (out of view, too far,
        /// at the lens, or on the sight line to the player), below 1 = shrunk (near the lens, or capped on screen).
        /// nearRules false (ground marks, telegraphs): only the view and distance test. Always 1 with no low view.
        /// </summary>
        public static float FxScale(Vector3 c, float r, bool nearRules)
        {
            if (!_on) return 1f;
            if (!FxVisible(c, r)) return 0f;
            if (!nearRules) return 1f;
            float d = (c - _cam).magnitude;
            float edge = d - r;
            if (edge < NearCull) { FxNearCulled++; return 0f; }
            if (_lineOn)
            {
                Vector3 s = _lineTo - _cam;
                float l2 = s.sqrMagnitude;
                if (l2 > 0.25f)
                {
                    float t = Vector3.Dot(c - _cam, s) / l2;
                    if (t > 0.02f && t < 0.95f && (_cam + s * t - c).magnitude < r * 0.8f + LineClear) { FxNearCulled++; return 0f; }
                }
            }
            float k = edge < NearFade ? (edge - NearCull) / (NearFade - NearCull) : 1f;
            float cap = MaxScreenTan * d;
            if (r * k > cap) k = cap / r;
            if (k < 1f) FxShrunk++;
            return k;
        }

        /// <summary>False when an effect centred at c with extent r is beyond FxDrawDistance or outside the view by more
        /// than FxMargin. Always true when no low view is on. Counts what it decides.</summary>
        public static bool FxVisible(Vector3 c, float r)
        {
            if (!_on) return true;
            bool vis = (c - _cam).sqrMagnitude <= (FxDrawDistance + r) * (FxDrawDistance + r);
            if (vis)
                for (int i = 0; i < 6; i++)
                    if (Planes[i].GetDistanceToPoint(c) < -(r + FxMargin)) { vis = false; break; }
            if (vis) FxDrawn++; else FxCulled++;
            return vis;
        }

        /// <summary>
        /// The dithered fade the toon shader applies (CMToonFadeClip in CMToonCommon.hlsl, same maths): the share of a
        /// surface's pixels removed at world point p, 0..MaxFade, by its distance to the line from the camera to the followed
        /// fighter's chest. Nothing within the last 15 percent of the line (the fighter himself) or at the lens end.
        /// </summary>
        public static float FadeAmount(Vector3 cam, Vector3 target, Vector3 p, float radius)
        {
            Vector3 s = target - cam;
            float l2 = Mathf.Max(s.sqrMagnitude, 1e-4f);
            float t = Vector3.Dot(p - cam, s) / l2;
            if (t <= 0.02f || t >= 0.85f) return 0f;
            float d = (cam + s * t - p).magnitude;
            return Mathf.Clamp01((radius - d) / (radius * 0.5f)) * MaxFade;
        }

        /// <summary>At most this share of pixels is removed (the body still reads as a ghost).</summary>
        public const float MaxFade = 0.8f;
        /// <summary>Radius of the faded tube around the sight line (u).</summary>
        public const float FadeRadius = 1.0f;

        /// <summary>Fighters posed and drawn this frame, culled (outside the view or beyond the draw distance), and their
        /// rough triangle count (the active renderers' meshes; ActorViews fills it in every view).</summary>
        public static int FightersDrawn, FightersCulled, FighterTriangles;
        /// <summary>The same three numbers last frame (stable to read from anywhere).</summary>
        public static int LastDrawn, LastCulled, LastTriangles;

        /// <summary>Called once per frame by ActorViews before the fighters: on = a low view is active.</summary>
        public static void Begin(UnityEngine.Camera cam, bool on)
        {
            LastDrawn = FightersDrawn; LastCulled = FightersCulled; LastTriangles = FighterTriangles;
            LastFxDrawn = FxDrawn; LastFxCulled = FxCulled; LastRubbleCulled = RubbleCulled;
            LastFxNearCulled = FxNearCulled; LastFxShrunk = FxShrunk;
            FxDrawn = FxCulled = RubbleCulled = FxNearCulled = FxShrunk = 0;
            if (_lastWasSample) SampledTriangles = FighterTriangles;
            _lastWasSample = SampleFrame;
            FightersDrawn = FightersCulled = FighterTriangles = 0;
            _on = on && cam != null;
            if (!_on || _frame == Time.frameCount) return;
            _frame = Time.frameCount;
            SetView(cam, true);
        }

        /// <summary>Rough triangles a model submitted last frame: its renderers the camera saw (isVisible already drops
        /// LOD levels and culled parts), index count / 3. Sampled every SampleEvery frames only; no allocation.</summary>
        public static int Triangles(Renderer[] rs)
        {
            if (rs == null) return 0;
            int t = 0;
            for (int i = 0; i < rs.Length; i++)
            {
                Renderer r = rs[i];
                if (r == null || !r.enabled || !r.isVisible) continue;
                Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : null;
                if (mesh == null && r is MeshRenderer)
                {
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    mesh = mf != null ? mf.sharedMesh : null;
                }
                if (mesh == null) continue;
                for (int sm = 0; sm < mesh.subMeshCount; sm++) t += (int)(mesh.GetIndexCount(sm) / 3);
            }
            return t;
        }

        public const int SampleEvery = 30;
        public static bool SampleFrame => Time.frameCount % SampleEvery == 0;

        /// <summary>Fighters' rough triangle total from the last sample frame (held between samples).</summary>
        public static int SampledTriangles;

        /// <summary>
        /// The corridor fade: true when a body at (x, z) stands between the camera and the followed fighter (within
        /// radius of the segment on the ground, not at either end). Horizontal maths only: the low cameras are 1.6 to 3.5 u
        /// up, below a fighter's head height plus the pitch, so a body on that line covers the character.
        /// </summary>
        public static bool InCorridor(Vector3 cam, Vector3 target, Vector3 p, float radius)
        {
            float sx = target.x - cam.x, sz = target.z - cam.z;
            float l2 = sx * sx + sz * sz;
            if (l2 < 0.25f) return false;
            float t = ((p.x - cam.x) * sx + (p.z - cam.z) * sz) / l2;
            if (t <= 0.02f || t >= 0.85f) return false;
            float cx = cam.x + sx * t - p.x, cz = cam.z + sz * t - p.z;
            return cx * cx + cz * cz < radius * radius;
        }

        /// <summary>False when a body at p (feet) of radius r and height h is beyond the draw distance or outside the view.</summary>
        public static bool Visible(Vector3 p, float r, float h)
        {
            if (!_on) return true;
            Vector3 d = p - _cam;
            if (d.sqrMagnitude > FollowCamera.FighterDrawDistance * FollowCamera.FighterDrawDistance) return false;
            var b = new Bounds(p + new Vector3(0f, h * 0.5f, 0f), new Vector3(r * 2f, h, r * 2f));
            return GeometryUtility.TestPlanesAABB(Planes, b);
        }
    }
}
