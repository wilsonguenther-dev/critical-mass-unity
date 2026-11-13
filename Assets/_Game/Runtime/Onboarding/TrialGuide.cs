using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Sim.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Onboarding
{
    /// <summary>
    /// The guided match on screen: a TrialRunner checked once per sim tick, the mascot saying each step's line (MascotGuide),
    /// a world arrow on the step's target (a bobbing chevron above it, or an edge pointer with the distance when it is off
    /// screen), and the skip key (tap: skip this step; hold one second: leave the guided match). View only: it reads the
    /// World and never writes it; the match itself was set up by TrialMatch before Begin.
    /// </summary>
    public sealed class TrialGuide : MonoBehaviour
    {
        public SimDriverBehaviour Driver;
        public TrialRunner Runner { get; private set; }
        public Canvas Canvas { get; private set; }
        /// <summary>The arrow is drawn this frame (on screen or as an edge pointer).</summary>
        public bool ArrowVisible { get; private set; }
        public bool ArrowOnScreen { get; private set; }

        /// <summary>Seconds the skip key must be held to leave the guided match.</summary>
        public const float HoldToLeaveSeconds = 1f;
        /// <summary>Seconds a cheer line shows before the next step's line.</summary>
        public const float CheerSeconds = 1.3f;

        RectTransform _root, _chevron, _edge, _edgeArrow;
        VectorShape _chevronShape, _ring;
        Text _edgeDist;
        MascotGuide _mascot;
        float _cheerUntil = -1f;
        int _cheers;
        bool _wasAlive = true;
        bool _finishedShown;
        float _skipDownAt = -1f;
        bool _skipHoldFired;
        float _bob;
        int _lastDevice = -1;

        static readonly string[] Metres = BuildMetres();

        static string[] BuildMetres()
        {
            var a = new string[1000];
            for (int i = 0; i < a.Length; i++) a[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m";
            return a;
        }

        /// <summary>Starts the steps on the driver's world (the match must already be set up).</summary>
        public void Begin(TrialStep[] steps)
        {
            if (Driver == null) Driver = GetComponent<SimDriverBehaviour>();
            if (Driver == null || Driver.World == null) return;
            BuildArrow();
            TrialText.RegisterFallbacks(steps);
            _mascot = MascotGuide.Ensure(gameObject);
            if (_mascot != null) _mascot.AcceptsToggle = true;
            Runner = new TrialRunner(steps);
            Runner.StepStarted += OnStepStarted;
            Runner.StepEnded += OnStepEnded;
            Runner.Completed += OnCompleted;
            _finishedShown = false;
            _cheerUntil = -1f;
            _wasAlive = true;
            ControlBindings.ActiveDeviceChanged += OnDevice;
            Runner.Begin(Driver.World);
        }

        void OnDestroy()
        {
            ControlBindings.ActiveDeviceChanged -= OnDevice;
            if (Runner != null)
            {
                Runner.StepStarted -= OnStepStarted;
                Runner.StepEnded -= OnStepEnded;
                Runner.Completed -= OnCompleted;
            }
            if (Canvas != null) Destroy(Canvas.gameObject);
        }

        /// <summary>Ends the guided match now (skip-all). completed = every step ran to its end.</summary>
        public void Leave()
        {
            if (Runner == null || Runner.Finished) return;
            Runner.SkipAll(Driver.World);
        }

        // ---------------------------------------------------------------- runner events

        void OnStepStarted(int i)
        {
            if (Time.unscaledTime < _cheerUntil) return;   // the cheer finishes first; Update says the line after it
            SayStep();
        }

        void OnStepEnded(int i, TrialStepState how)
        {
            Trial.RaiseStepEnded(Runner.Steps[i], how);
            if (how != TrialStepState.Done || _mascot == null) return;
            _mascot.React(MascotReaction.Cheer);
            _mascot.Say(MascotLines.CheerId(_cheers++));
            _cheerUntil = Time.unscaledTime + CheerSeconds;
        }

        void OnCompleted()
        {
            bool all = Runner.Count(TrialStepState.Done) > 0 && Runner.Count(TrialStepState.Skipped) == 0;
            TrialProgress.Done = true;
            Trial.RaiseFinished(all);
        }

        void OnDevice(ControlDevice d) => _lastDevice = -1;

        void SayStep()
        {
            if (_mascot == null || Runner == null) return;
            TrialStep s = Runner.ActiveStep;
            if (s == null) return;
            ControlDevice dev = ControlBindings.ActiveDevice;
            _lastDevice = (int)dev;
            _mascot.SayText(TrialText.StepLine(s, dev));
            _mascot.SetHint(TrialText.Fill("{Skip}  SKIP STEP (HOLD: LEAVE)    {Toggle}  HIDE ME", dev));
            _mascot.React(MascotReaction.Point);
        }

        void SayDone()
        {
            if (_mascot == null) return;
            ControlDevice dev = ControlBindings.ActiveDevice;
            _mascot.React(MascotReaction.Cheer);
            bool left = Runner.Count(TrialStepState.Done) == 0;
            _mascot.Say(left ? TrialText.Left : TrialText.Done);
            _mascot.SetHint(Trial.AfterTrial != null ? string.Empty : TrialText.Fill(MascotLines.Resolve(TrialText.DoneHint), dev));
        }

        // ---------------------------------------------------------------- frame

        void Update()
        {
            if (Runner == null || Driver == null || Driver.World == null) return;
            World w = Driver.World;
            HumanInputSource input = Driver.GetComponent<HumanInputSource>();
            bool paused = input != null && input.State.Paused;

            if (!Runner.Finished)
            {
                Runner.Observe(w);
                if (!paused) ReadSkip(w);
            }

            if (Runner.Finished)
            {
                if (!_finishedShown)
                {
                    _finishedShown = true;
                    if (Trial.AfterTrial != null) Trial.AfterTrial();
                    SayDone();
                }
                HideArrow();
                if (!paused && Trial.AfterTrial == null && ConfirmPressed()) Trial.StartRealMatch();
                return;
            }

            // Mascot: cheer, then the step line; wince on a knock-out; point at the arrow.
            Actor me = w.Actors[TrialChecks.Player];
            if (_mascot != null)
            {
                if (_wasAlive && !me.Alive)
                {
                    _mascot.React(MascotReaction.Wince);
                    _mascot.Say("mascot.wince");
                }
                else if (!_wasAlive && me.Alive) SayStep();
                else if (_cheerUntil > 0f && Time.unscaledTime >= _cheerUntil)
                {
                    _cheerUntil = -1f;
                    SayStep();
                }
                else if (_lastDevice != (int)ControlBindings.ActiveDevice && _cheerUntil < 0f && me.Alive) SayStep();
            }
            _wasAlive = me.Alive;

            UpdateArrow(w, me.Alive);
        }

        void ReadSkip(World w)
        {
            bool held = SkipHeld();
            float now = Time.unscaledTime;
            if (held && _skipDownAt < 0f)
            {
                _skipDownAt = now;
                _skipHoldFired = false;
            }
            if (held && !_skipHoldFired && now - _skipDownAt >= HoldToLeaveSeconds)
            {
                _skipHoldFired = true;
                Leave();
            }
            if (!held && _skipDownAt >= 0f)
            {
                if (!_skipHoldFired) Runner.Skip(w);
                _skipDownAt = -1f;
            }
        }

        static bool SkipHeld()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.backspaceKey.isPressed) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && pad.dpad.down.isPressed;
        }

        static bool ConfirmPressed()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) return true;
            Gamepad pad = Gamepad.current;
            return pad != null && pad.dpad.right.wasPressedThisFrame;
        }

        // ---------------------------------------------------------------- world arrow

        void BuildArrow()
        {
            if (Canvas != null) return;
            var go = new GameObject("Trial Arrow Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            Canvas = go.AddComponent<Canvas>();
            Canvas.renderMode = RenderMode.ScreenSpaceCamera;
            Canvas.sortingOrder = 12;   // over the HUD world layer, under the mascot
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            Canvas.worldCamera = cam;
            Canvas.planeDistance = cam != null ? Mathf.Max(0.48f, cam.nearClipPlane * 1.45f) : 0.48f;
            _root = (RectTransform)go.transform;

            // On-screen: a Biomass-green chevron bobbing over the target, with a ground ring under it.
            _ring = MakeShape(_root, "Ring", ShapeKind.Circle, 96f, 48f, CMTokens.WithAlpha(CMTokens.Biomass.Color, 0.22f));
            _ring.OutlinePx = 3f;
            _ring.RimPx = 2f;
            _ring.RimColor = CMTokens.Biomass.Color;
            _ring.rectTransform.anchorMin = _ring.rectTransform.anchorMax = Vector2.zero;   // screen pixels / scale
            _chevron = Rt(_root, "Chevron");
            Size(_chevron, 56f, 52f);
            _chevronShape = MakeShape(_chevron, "Shape", ShapeKind.TriangleDown, 56f, 52f, CMTokens.Biomass.Color);
            _chevronShape.OutlinePx = 4f;
            _chevronShape.RimPx = 2f;

            // Off-screen: an edge pointer with the distance.
            _edge = Rt(_root, "Edge Pointer");
            Size(_edge, 72f, 80f);
            VectorShape disc = MakeShape(_edge, "Disc", ShapeKind.Circle, 48f, 48f, CMTokens.VoidNavy.Color);
            disc.RimColor = CMTokens.Biomass.Color;
            _edgeArrow = Rt(_edge, "Arrow Pivot");
            _edgeArrow.anchorMin = _edgeArrow.anchorMax = new Vector2(0.5f, 0.5f);
            _edgeArrow.sizeDelta = Vector2.zero;
            VectorShape arrow = MakeShape(_edgeArrow, "Arrow", ShapeKind.TriangleRight, 22f, 26f, CMTokens.Biomass.Color);
            arrow.RimPx = 0f;
            arrow.rectTransform.anchoredPosition = new Vector2(38f, 0f);
            _edgeDist = HudText.Make(_edge, "Distance", CMTokens.Mono, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter, 2.5f);
            RectTransform dr = _edgeDist.rectTransform;
            dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 0.5f);
            dr.pivot = new Vector2(0.5f, 0.5f);
            dr.sizeDelta = new Vector2(90f, 26f);
            dr.anchoredPosition = new Vector2(0f, -40f);
            HideArrow();
        }

        static RectTransform Rt(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        /// <summary>Bottom-left anchored (screen pixels / scale), centred pivot.</summary>
        static void Size(RectTransform rt, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
        }

        static VectorShape MakeShape(Transform parent, string name, ShapeKind kind, float w, float h, Color fill)
        {
            RectTransform rt = Rt(parent, name);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            VectorShape s = rt.gameObject.AddComponent<VectorShape>();
            s.Kind = kind;
            s.FillColor = fill;
            s.Radius = 0f;
            s.raycastTarget = false;
            return s;
        }

        void HideArrow()
        {
            ArrowVisible = false;
            ArrowOnScreen = false;
            if (_chevron != null && _chevron.gameObject.activeSelf) _chevron.gameObject.SetActive(false);
            if (_ring != null && _ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);
            if (_edge != null && _edge.gameObject.activeSelf) _edge.gameObject.SetActive(false);
        }

        void UpdateArrow(World w, bool alive)
        {
            UnityEngine.Camera cam = Canvas != null ? Canvas.worldCamera : null;
            if (cam == null && Canvas != null)
            {
                cam = UnityEngine.Camera.main;
                Canvas.worldCamera = cam;
            }
            if (cam == null || !alive || !Runner.Target(w, out double tx, out double tz))
            {
                HideArrow();
                if (_mascot != null && !alive) _mascot.StopPointing();
                return;
            }
            float sw = cam.pixelWidth, sh = cam.pixelHeight;
            float scale = HudLayout.Scale(sw, sh);
            if (Mathf.Abs(Canvas.scaleFactor - scale) > 1e-4f) Canvas.scaleFactor = scale;
            ArrowVisible = true;

            double ground = GroundHeight.At(w.Map, tx, tz);
            Vector3 groundPt = SimSpace.ToWorld(tx, ground + 0.05, tz);
            _bob += Time.unscaledDeltaTime;
            float lift = 2.6f + 0.25f * Mathf.Sin(_bob * Mathf.PI * 1.2f);
            Vector3 top = SimSpace.ToWorld(tx, ground + lift, tz);
            Vector3 sp = cam.WorldToScreenPoint(top);
            bool on = sp.z > 0 && sp.x >= 0 && sp.y >= 0 && sp.x <= sw && sp.y <= sh;
            ArrowOnScreen = on;

            Vector3 me = SimSpace.ToWorld(w.X[TrialChecks.Player], GroundHeight.At(w.Map, w.X[TrialChecks.Player], w.Z[TrialChecks.Player]) + 1.0,
                w.Z[TrialChecks.Player]);
            Vector3 meSp = cam.WorldToScreenPoint(me);
            if (_mascot != null)
            {
                if (meSp.z > 0) _mascot.AvoidScreenPoint(new Vector2(meSp.x, meSp.y));
                _mascot.PointToward(new Vector2(sp.x - meSp.x, sp.y - meSp.y) * (sp.z < 0 ? -1f : 1f));
            }

            bool selfTarget = Runner.ActiveStep != null && Runner.ActiveStep.Target == TrialTarget.Self;
            if (on)
            {
                if (_edge.gameObject.activeSelf) _edge.gameObject.SetActive(false);
                // The Self target (move, dig) shows only the ring at the player's feet: the chevron would sit on his head.
                bool chev = !selfTarget;
                if (_chevron.gameObject.activeSelf != chev) _chevron.gameObject.SetActive(chev);
                if (chev) _chevron.anchoredPosition = new Vector2(sp.x, sp.y) / scale;
                Vector3 gp = cam.WorldToScreenPoint(groundPt);
                bool ringOn = gp.z > 0;
                if (_ring.gameObject.activeSelf != ringOn) _ring.gameObject.SetActive(ringOn);
                if (ringOn) _ring.rectTransform.anchoredPosition = new Vector2(gp.x, gp.y) / scale;
                return;
            }

            if (_chevron.gameObject.activeSelf) _chevron.gameObject.SetActive(false);
            if (_ring.gameObject.activeSelf) _ring.gameObject.SetActive(false);
            if (!_edge.gameObject.activeSelf) _edge.gameObject.SetActive(true);
            Vector2 c = new Vector2(sw * 0.5f, sh * 0.5f);
            Vector2 d = new Vector2(sp.x, sp.y) - c;
            if (sp.z < 0) d = -d;
            if (d.sqrMagnitude < 1e-4f) d = Vector2.up;
            float pad = 80f * scale;
            float kx = Mathf.Abs(d.x) > 1e-4f ? (c.x - pad) / Mathf.Abs(d.x) : float.MaxValue;
            float ky = Mathf.Abs(d.y) > 1e-4f ? (c.y - pad) / Mathf.Abs(d.y) : float.MaxValue;
            Vector2 pos = c + d * Mathf.Min(kx, ky);
            _edge.anchoredPosition = pos / scale;
            _edgeArrow.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            double dx = tx - w.X[TrialChecks.Player], dz = tz - w.Z[TrialChecks.Player];
            HudText.Set(_edgeDist, Metres[Math.Min(999, (int)Math.Round(Math.Sqrt(dx * dx + dz * dz)))]);
        }
    }
}
