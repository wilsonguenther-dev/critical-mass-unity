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
        /// <summary>b12 r3: canvas units kept clear between the top of the prompt-pill lane and this chip when the pill is up
        /// (the two nearly touched, and overlapped once the pill grew its reason line).</summary>
        public const float PillGap = 10f;

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

        /// <summary>b14: the chip's slot in the one prompt stack (PromptStack: lowest priority, behind the station prompt and
        /// the terrain pill; hidden while two of those are up).</summary>
        void PlaceChip(int slot)
        {
            var p = new Vector2(PromptStack.CentreX[(int)PromptStack.Source.Strip], PromptStack.Bottom(slot, ChipH));
            if ((_chip.anchoredPosition - p).sqrMagnitude > 0.25f) _chip.anchoredPosition = p;
        }

        /// <summary>The chip is on screen now (it has words and a slot in the prompt stack).</summary>
        public static bool Visible => Instance != null && Instance._chip != null && Instance._chip.gameObject.activeSelf;

        void LateUpdate()
        {
            if (_canvas == null) return;
            bool hudOn = _hud != null && _hud.Canvas != null && _hud.Canvas.enabled && !CriticalMass.Runtime.Menus.MenuGate.AnyOpen;   // b14 review: never over a full-screen menu
            if (_canvas.enabled != hudOn) _canvas.enabled = hudOn;
            if (!hudOn)
            {
                PromptStack.Release(PromptStack.Source.Strip);
                return;
            }
            UpdateScale();
            World w = _driver != null ? _driver.World : null;
            int key = w != null ? StateKey(w) : -1;
            if (key != _key)
            {
                _key = key;
                _text = key < 0 ? string.Empty : Compose(w, key);
                _shown = _text.Length > 0;
                if (_shown) HudText.Set(_label, _text);
            }
            int slot = -1;
            if (_shown) { PromptStack.Want(PromptStack.Source.Strip); slot = PromptStack.SlotOf(PromptStack.Source.Strip); }
            else PromptStack.Release(PromptStack.Source.Strip);
            bool vis = slot >= 0;
            if (_chip.gameObject.activeSelf != vis) _chip.gameObject.SetActive(vis);
            if (vis) PlaceChip(slot);
        }

        /// <summary>
        /// Everything the chip's text depends on, packed in one int (-1 = hidden): pillar line (0 none, 1 ramp, 2 wall),
        /// miner mode, blocks carried, carry max and the device used last.
        /// </summary>
        public static int StateKey(World w)
        {
            int me = LocalSeat.Actor;
            if (w.ActorCount <= me || w.Match.Phase != MatchPhase.Playing) return -1;
            Actor a = w.Actors[me];
            if (!a.Alive) return -1;
            int pillar = PillarModeHere(w, me);
            bool miner = a.Cls == Terraform.OwnerClass(w) && a.Cls != ClassId.Builder;   // b13 r2: the dig / place strip follows the class that owns terrain; b14: the Builder always has his tool strip
            // feat/terrain-affordances (e): the other terrain classes keep their resource in the same strip.
            // b15 (owner walk 2026-11-17): the Builder's left click uses his selected tool, never knocks a pillar, so next to a
            // pillar his strip still names the tool (it said "LEFT CLICK KNOCK IT DOWN · FROM HERE: RAMP" with BRIDGE selected).
            if ((pillar == 0 || a.Cls == ClassId.Builder) && !miner)
            {
                int dev = (int)ControlBindings.ActiveDevice;
                switch (a.Cls)
                {
                    case ClassId.Builder:
                    {
                        // b14: the one tool wheel: the selected tool, its cost (or the blocks carried on dig / place) and the
                        // next-tool key. The aimed spot's verdict is the terrain pill's; the wood is on the kit's TEAM WOOD line.
                        BuilderTool tool = CriticalMass.Runtime.Hud.Mechanics.B13Kit.ToolOf(w, me);
                        int tier = Math.Min(2, Math.Max(0, BuilderWalls.Tier(w, a.Team)));
                        bool blocks = tool == BuilderTool.Dig || tool == BuilderTool.Place;
                        int n = Math.Min(15, blocks ? w.Terraform.Carried[me] : BuilderToolbelt.Cost(w, tool));
                        int bcap = blocks ? Math.Min(7, Terraform.CarryMax(w.Config)) : 0;
                        BuilderToolbelt.Rules(w, me, out bool walls, out bool terrain, out bool tall);
                        int next = (int)BuilderToolbelt.Next(tool, 1, walls, terrain, tall);
                        return (3 << 16) | next | ((int)tool << 19) | (tier << 22) | (n << 24) | (bcap << 28) | (dev << 15);
                    }
                    case ClassId.Farmer:
                        return (1 << 17) | (Math.Min(63, a.CarryAmount + a.CarryMeat) << 19) | (Math.Min(63, (int)w.Config.Class(a.Cls).CarryCapacity) << 25) | (dev << 15);
                    case ClassId.Demolitions:
                        // b12 r3: merged into the prompt pill while it is up (it says the same thing on one line there).
                        if (CriticalMass.Runtime.View.Affordance.TerrainAffordanceView.PromptPillShown) return -1;
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

        /// <summary>
        /// b12 r3: the Demolitionist's strip. Under the b12-jz rule her attack press throws TNT when ready and swings the pry
        /// bar while it reloads, so the line names the attack binding: "TNT READY · LEFT CLICK: THROW" or "TNT IN 3 S · LEFT
        /// CLICK: PRY BAR" (the pad's glyph on a controller). Without the rule: the Dig / place key throws, as before.
        /// </summary>
        public static string DemolitionsLine(bool attackThrows, int waitSeconds, ControlDevice device)
        {
            string tnt = CriticalMass.Runtime.View.Affordance.GrenadeTuning.Name;
            string state = waitSeconds <= 0 ? tnt + " READY" : tnt + " IN " + waitSeconds + " S";
            if (!attackThrows) return state + "   ·   " + TrialText.Fill("{DigPlace}", device) + ": THROW";
            return state + "   ·   " + TrialText.Fill("{Attack}", device) + (waitSeconds <= 0 ? ": THROW" : ": " + CriticalMass.Sim.Core.LightWeapons.DemolitionsName);
        }

        /// <summary>
        /// b12 r3: the chip's bottom (canvas units, from the screen bottom) on a w x h screen at HUD scale s: its old LiftY, or,
        /// while the prompt pill is up, PillGap above the top of the pill's reserved lane (the pill never leaves that lane).
        /// </summary>
        public static float ChipBottom(float w, float h, float s, bool pillShown)
        {
            if (!pillShown || w < 1 || h < 1 || s <= 0f) return LiftY;
            Rect lane = HudZones.PromptLanePixels(w, h);   // device px, top-left origin
            return Mathf.Max(LiftY, (h - lane.y) / s + PillGap);
        }

        /// <summary>b14: the Builder's tool strip for a packed key: "LADDER   ·   2 WOOD   ·   WHEEL: BRIDGE   ·   1-7: PICK",
        /// "DIG   ·   BLOCKS 1/3   ·   LB: PLACE" (the switch key names the tool it moves to).</summary>
        public static string BuilderLine(int key, ControlDevice device)
        {
            BuilderTool tool = (BuilderTool)((key >> 19) & 7);
            int tier = (key >> 22) & 3, n = (key >> 24) & 15, max = (key >> 28) & 7;
            string line = BuilderToolbelt.Word(tool, tier);
            if (tool == BuilderTool.Dig || tool == BuilderTool.Place) line += "   ·   BLOCKS " + n + "/" + max;
            else if (n > 0) line += "   ·   " + n + " WOOD";
            line += "   ·   " + TrialText.Fill("{Switch}", device) + ": " + BuilderToolbelt.Word((BuilderTool)(key & 7), tier);
            if (device != ControlDevice.Gamepad) line += "   ·   1-7: PICK";
            return line;
        }

        static string Compose(World w, int key)
        {
            ControlDevice device = ControlBindings.ActiveDevice;
            if ((key & (7 << 16)) == (3 << 16)) return BuilderLine(key, device);
            if ((key & (7 << 16)) != 0)
            {
                int v = (key >> 19) & 63, v12 = (key >> 19) & 4095;
                if ((key & (1 << 16)) != 0) return CriticalMass.Runtime.View.Mechanics.BuilderBank.WoodWord + " " + v12 + "   ·   " + TrialText.Fill("{Ability}", device) + ": BUILD";
                if ((key & (1 << 17)) != 0) return "CARRYING " + v + "/" + ((key >> 25) & 63) + "   ·   HOLD " + TrialText.Fill("{Interact}", device) + ": GATHER";
                return DemolitionsLine(CriticalMass.Runtime.View.Affordance.GrenadeTuning.AttackThrows(w.Config), v12, device);
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
