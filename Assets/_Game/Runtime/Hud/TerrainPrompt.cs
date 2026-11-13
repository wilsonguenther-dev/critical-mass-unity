using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Input;
using CriticalMass.Runtime.Menu;
using CriticalMass.Runtime.Onboarding;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// Creator acceptance (2026-11-13): the terrain prompt chip, bottom centre above the ability row, on its own small canvas
    /// (sorting 11, the ProgressionHud pattern) so the HUD root is not edited. Two lines the creator's pitch needs:
    /// <list type="bullet">
    /// <item>a Miner always sees what the dig / place key does now and what he carries: "F  DIG GROUND   ·   BLOCKS 0/1",
    /// "F  PLACE BLOCK   ·   BLOCKS 1/1" (the key glyph of the device used last; the wheel or a bumper switches);</item>
    /// <item>anyone standing within PillarRange of a standing knock-down pillar sees which way his next hits knock it over
    /// from where he stands: "ATTACK  KNOCK IT DOWN   ·   FROM HERE: RAMP" (or WALL), the sim's own Topple.ModeFor.</item>
    /// </list>
    /// Read-only on the sim. The text is rebuilt only when what it says changes (no per-frame allocation).
    /// </summary>
    [CriticalMass.Runtime.Flow.GameOverlay(Order = 54, Scope = CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]
    public sealed class TerrainPrompt : MonoBehaviour
    {
        /// <summary>World units from a pillar's base cell centre inside which the knock-down line shows.</summary>
        public const double PillarRange = 4.5;
        const float ChipW = 620f, ChipH = 46f, LiftY = 214f, ShiftX = 190f;

        public static TerrainPrompt Instance { get; private set; }
        /// <summary>What the chip says now ("" = hidden). Tests and captures read it.</summary>
        public static string Line => Instance != null && Instance._shown ? Instance._text : string.Empty;

        SimDriverBehaviour _driver;
        GameHud _hud;
        Canvas _canvas;
        RectTransform _chip;
        Text _label;
        string _text = string.Empty;
        bool _shown;
        int _key = int.MinValue;

        void OnEnable() => Instance = this;

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            _hud = GetComponent<GameHud>();
            if (_hud == null) _hud = FindAnyObjectByType<GameHud>();
            Build();
        }

        void Build()
        {
            var go = new GameObject("Terrain Prompt Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            UnityEngine.Camera cam = UnityEngine.Camera.main;
            _canvas.renderMode = cam != null ? RenderMode.ScreenSpaceCamera : RenderMode.ScreenSpaceOverlay;
            _canvas.worldCamera = cam;
            _canvas.planeDistance = cam != null ? Mathf.Max(0.5f, cam.nearClipPlane * 1.5f) : 0.5f;
            _canvas.sortingOrder = 11;
            var root = (RectTransform)go.transform;

            _chip = MenuUi.Rt(root, "Terrain Chip");
            _chip.anchorMin = _chip.anchorMax = new Vector2(0.5f, 0f);
            _chip.pivot = new Vector2(0.5f, 0f);
            _chip.sizeDelta = new Vector2(ChipW, ChipH);
            _chip.anchoredPosition = new Vector2(ShiftX, LiftY);   // right of the mascot box and the intro card
            VectorShape body = MenuUi.Shape(_chip, "Body", ShapeKind.RoundRect, 0f, 0f, ChipW, ChipH, CMTokens.PanelNavy.Color, -1f);
            body.OutlinePx = 3f;
            body.RimPx = 2f;
            _label = MenuUi.Fit(MenuUi.Label(_chip, "Line", CMTokens.Display, UiScale.TypeBody, CMTokens.WarmCream.Color, TextAnchor.MiddleCenter,
                12f, 0f, ChipW - 24f, ChipH, "", 3f), 16);
            _chip.gameObject.SetActive(false);
            UpdateScale();
        }

        void UpdateScale()
        {
            UnityEngine.Camera cam = _canvas.worldCamera;
            if (_canvas.renderMode == RenderMode.ScreenSpaceCamera && cam == null)
            {
                cam = UnityEngine.Camera.main;
                _canvas.worldCamera = cam;
            }
            float w = cam != null ? cam.pixelWidth : Screen.width, h = cam != null ? cam.pixelHeight : Screen.height;
            if (w < 1 || h < 1) return;
            float s = HudLayout.Scale(w, h);
            if (Mathf.Abs(_canvas.scaleFactor - s) > 1e-4f) _canvas.scaleFactor = s;
        }

        void LateUpdate()
        {
            if (_canvas == null) return;
            bool hudOn = _hud != null && _hud.Canvas != null && _hud.Canvas.enabled;
            if (_canvas.enabled != hudOn) _canvas.enabled = hudOn;
            if (!hudOn) return;
            UpdateScale();
            World w = _driver != null ? _driver.World : null;
            int key = w != null ? StateKey(w) : -1;
            if (key == _key) return;
            _key = key;
            _text = key < 0 ? string.Empty : Compose(w, key);
            _shown = _text.Length > 0;
            if (_chip.gameObject.activeSelf != _shown) _chip.gameObject.SetActive(_shown);
            if (_shown) HudText.Set(_label, _text);
        }

        /// <summary>
        /// Everything the chip's text depends on, packed in one int (-1 = hidden): pillar line (0 none, 1 ramp, 2 wall),
        /// miner mode, blocks carried, carry max and the device used last.
        /// </summary>
        public static int StateKey(World w)
        {
            const int me = 0;
            if (w.ActorCount <= me || w.Match.Phase != MatchPhase.Playing) return -1;
            Actor a = w.Actors[me];
            if (!a.Alive) return -1;
            int pillar = PillarModeHere(w, me);
            bool miner = a.Cls == ClassId.Miner;
            // feat/terrain-affordances (e): the other terrain classes keep their resource in the same strip.
            if (pillar == 0 && !miner)
            {
                int dev = (int)ControlBindings.ActiveDevice;
                switch (a.Cls)
                {
                    case ClassId.Builder:
                        return (1 << 16) | (Math.Min(4095, (int)Math.Floor(Math.Max(0, BuiltPieces.Bank(w, a.Team)))) << 19) | (dev << 15);
                    case ClassId.Farmer:
                        return (1 << 17) | (Math.Min(63, a.CarryAmount + a.CarryMeat) << 19) | (Math.Min(63, (int)w.Config.Class(a.Cls).CarryCapacity) << 25) | (dev << 15);
                    case ClassId.Demolitions:
                        return (1 << 18) | (Math.Min(4095, (int)Math.Ceiling(Demolitions.TntWaitTicks(w, me) / w.Config.SimHz)) << 19) | (dev << 15);
                    default:
                        return -1;
                }
            }
            int mode = miner ? (int)Terraform.ModeOf(w, me) : 0;
            int carried = miner ? w.Terraform.Carried[me] : 0;
            int max = miner ? Terraform.CarryMax(w.Config) : 0;
            int device = (int)ControlBindings.ActiveDevice;
            return pillar | (miner ? 4 : 0) | (mode << 3) | (carried << 5) | (max << 10) | (device << 15);
        }

        /// <summary>Topple.Ramp / Topple.Block for the nearest standing pillar within PillarRange of actor id, else 0.</summary>
        public static int PillarModeHere(World w, int id)
        {
            DecorationDef[] ds = w.Map.Decorations;
            if (ds == null || w.Topple == null) return 0;
            double px = w.X[id], pz = w.Z[id];
            int best = -1;
            double bd = PillarRange * PillarRange;
            for (int k = 0; k < ds.Length; k++)
            {
                if (k < w.Topple.Fallen.Length && w.Topple.Fallen[k] != Topple.Standing) continue;
                double dx = ds[k].Cx + 0.5 - px, dz = ds[k].Cz + 0.5 - pz;
                double d = dx * dx + dz * dz;
                if (d > bd) continue;
                bd = d;
                best = k;
            }
            return best < 0 ? 0 : Topple.ModeFor(ds[best], px, pz);
        }

        static string Compose(World w, int key)
        {
            ControlDevice device = ControlBindings.ActiveDevice;
            if ((key & (7 << 16)) != 0)
            {
                int v = (key >> 19) & 63, v12 = (key >> 19) & 4095;
                if ((key & (1 << 16)) != 0) return "TEAM WOOD " + v12 + "   ·   " + TrialText.Fill("{Ability}", device) + ": BUILD";
                if ((key & (1 << 17)) != 0) return "CARRYING " + v + "/" + ((key >> 25) & 63) + "   ·   HOLD " + TrialText.Fill("{Interact}", device) + ": GATHER";
                return (v12 == 0 ? "TNT READY" : "TNT IN " + v12 + " S") + "   ·   " + TrialText.Fill("{DigPlace}", device) + ": THROW";
            }
            int pillar = key & 3;
            if (pillar != 0)
                return TrialText.Fill("{Attack}", device) + "  KNOCK IT DOWN   ·   FROM HERE: " + (pillar == Topple.Ramp ? "RAMP" : "WALL");
            int carried = (key >> 5) & 31, max = (key >> 10) & 31;
            bool place = ((key >> 3) & 3) == (int)TerraformMode.Place;
            string line = TrialText.Fill("{DigPlace}", device) + (place ? "  PLACE BLOCK" : "  DIG GROUND") + "   ·   BLOCKS " + carried + "/" + max;
            // The other mode is one switch away (the sim flips by itself only on a full or an empty carry).
            if (!place && carried > 0) line += "   ·   " + TrialText.Fill("{Switch}", device) + ": PLACE";
            else if (place && carried < max) line += "   ·   " + TrialText.Fill("{Switch}", device) + ": DIG";
            return line;
        }
    }
}
