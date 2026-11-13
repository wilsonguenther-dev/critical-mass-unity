using System;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.Menu;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Interact
{
    /// <summary>
    /// The 4-way ping wheel: hold the ping key, push the mouse or the right stick toward a call, release (or click) to
    /// place it; a quick tap places "Attack here"; releasing in the centre after a hold cancels. Up: Attack here, right: On
    /// my way, down: Defend here, left: Need help. The point is fixed when the key goes down (world aim point, or the
    /// full-map point under the cursor), so steering the wheel never moves the ping.
    /// </summary>
    public sealed class PingWheel
    {
        public const float Radius = 116f, DeadZone = 28f, TapSeconds = 0.22f;
        public const float ChipW = 196f, ChipH = 50f;

        RectTransform _rt;
        readonly VectorShape[] _chips = new VectorShape[4];
        readonly VectorShape[] _icons = new VectorShape[4];
        readonly Text[] _labels = new Text[4];
        VectorShape _hub, _needle;
        RectTransform _needlePivot;
        Text _hubText;

        public bool IsOpen { get; private set; }
        public int Selected { get; private set; } = -1;
        public double X { get; private set; }
        public double Z { get; private set; }
        /// <summary>True when the wheel was opened on the full map (the point came from the map).</summary>
        public bool FromMap { get; private set; }
        float _openedAt;

        public static readonly Vector2[] Offsets = { new Vector2(0f, Radius), new Vector2(Radius, 0f), new Vector2(0f, -Radius), new Vector2(-Radius, 0f) };

        public void Build(RectTransform root)
        {
            _rt = InteractUi.Centred(root, "Ping Wheel", 2f * Radius + ChipW, 2f * Radius + ChipH);
            _hub = MenuUi.Shape(_rt, "Hub", ShapeKind.Circle, 0f, 0f, 64f, 64f, InteractUi.Navy, 8f);
            CentreChild(_hub.rectTransform, Vector2.zero, 64f, 64f);
            _hub.RimColor = InteractUi.Cream;
            _hubText = MenuUi.Label(_hub.transform, "Word", CMTokens.Ui, 13, InteractUi.Cream, TextAnchor.MiddleCenter, 0f, 0f, 64f, 64f, "PING", 2f);
            _needlePivot = InteractUi.Centred(_rt, "Needle Pivot", 2f, 2f);
            _needle = MenuUi.Shape(_needlePivot, "Needle", ShapeKind.TriangleRight, 0f, 0f, 18f, 18f, InteractUi.Brass, 3f);
            CentreChild(_needle.rectTransform, new Vector2(44f, 0f), 18f, 18f);
            _needle.RimPx = 0f;
            for (int k = 0; k < 4; k++)
            {
                VectorShape chip = MenuUi.Shape(_rt, "Chip " + k, ShapeKind.RoundRect, 0f, 0f, ChipW, ChipH, InteractUi.Panel, 14f);
                CentreChild(chip.rectTransform, Offsets[k], ChipW, ChipH);
                chip.OutlinePx = 4f;
                chip.RimPx = 2f;
                _chips[k] = chip;
                VectorShape icon = MenuUi.Shape(chip.transform, "Icon", PingWords.Shape[k], 12f, 11f, 28f, 28f, PingWords.Colour((PingKind)k), 4f);
                icon.RimPx = 0f;
                _icons[k] = icon;
                _labels[k] = MenuUi.Label(chip.transform, "Label", CMTokens.Ui, 20, InteractUi.Cream, TextAnchor.MiddleLeft, 48f, 0f, ChipW - 56f, ChipH,
                    PingWords.Upper[k], 2f);
            }
            _rt.gameObject.SetActive(false);
        }

        static void CentreChild(RectTransform rt, Vector2 at, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = at;
        }

        /// <summary>The call a direction picks: 0 up, 1 right, 2 down, 3 left; -1 inside the dead zone.</summary>
        public static int Sector(Vector2 d, float deadZone)
        {
            if (d.sqrMagnitude < deadZone * deadZone) return -1;
            if (Mathf.Abs(d.y) >= Mathf.Abs(d.x)) return d.y >= 0 ? (int)PingKind.Attack : (int)PingKind.Defend;
            return d.x >= 0 ? (int)PingKind.OnMyWay : (int)PingKind.Help;
        }

        /// <summary>Opens at a canvas-root point (centre origin) for the sim point (x, z).</summary>
        public void Open(double x, double z, Vector2 rootLocal, Rect rootBounds, float now, bool fromMap)
        {
            if (_rt == null) return;
            X = x;
            Z = z;
            FromMap = fromMap;
            _openedAt = now;
            IsOpen = true;
            Selected = -1;
            Vector2 half = _rt.sizeDelta * 0.5f;
            rootLocal.x = Mathf.Clamp(rootLocal.x, rootBounds.xMin + half.x, Mathf.Max(rootBounds.xMin + half.x, rootBounds.xMax - half.x));
            rootLocal.y = Mathf.Clamp(rootLocal.y, rootBounds.yMin + half.y, Mathf.Max(rootBounds.yMin + half.y, rootBounds.yMax - half.y));
            _rt.anchoredPosition = rootLocal;
            Paint();
            _rt.gameObject.SetActive(true);
        }

        public Vector2 Centre => _rt != null ? _rt.anchoredPosition : Vector2.zero;

        /// <summary>Steers with a direction in root units from the wheel centre (mouse) or a stick scaled by Radius.</summary>
        public void Steer(Vector2 d)
        {
            if (!IsOpen) return;
            int s = Sector(d, DeadZone);
            if (s == Selected) return;
            Selected = s;
            Paint();
            if (s >= 0) _needlePivot.localEulerAngles = new Vector3(0f, 0f, s == 0 ? 90f : s == 1 ? 0f : s == 2 ? -90f : 180f);
        }

        void Paint()
        {
            _needle.gameObject.SetActive(Selected >= 0);
            for (int k = 0; k < 4; k++)
            {
                bool on = k == Selected;
                _chips[k].SetFill(on ? InteractUi.Brass : InteractUi.Panel);
                _labels[k].color = on ? InteractUi.Navy : InteractUi.Cream;
                float s = on ? 1.08f : 1f;
                _chips[k].rectTransform.localScale = new Vector3(s, s, 1f);
            }
            HudText.Set(_hubText, Selected >= 0 ? "RELEASE" : "PING");
        }

        /// <summary>
        /// Closes the wheel. Returns true with the call to place: the selected one, or Attack on a quick tap. A hold released
        /// in the centre returns false (cancelled).
        /// </summary>
        public bool Release(float now, out PingKind kind)
        {
            kind = PingKind.Attack;
            if (!IsOpen) return false;
            bool tap = now - _openedAt <= TapSeconds;
            int sel = Selected;
            Cancel();
            if (sel >= 0) { kind = (PingKind)sel; return true; }
            return tap;
        }

        public void Cancel()
        {
            IsOpen = false;
            Selected = -1;
            if (_rt != null && _rt.gameObject.activeSelf) _rt.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Pings drawn in the world (projected every frame, clamped to the screen edge when off screen, with the distance) and
    /// on the HUD minimap (children of GameHud.MapArea, through the minimap's projection). Fixed pools; fade with
    /// PingBook.Alpha; a short pop when placed (view-only, real time).
    /// </summary>
    public sealed class PingMarkers
    {
        const int N = PingBook.Capacity;
        const float PopSeconds = 0.22f, EdgePad = 64f;
        readonly RectTransform[] _world = new RectTransform[N];
        readonly CanvasGroup[] _worldGroup = new CanvasGroup[N];
        readonly VectorShape[] _worldShape = new VectorShape[N];
        readonly Text[] _worldWord = new Text[N], _worldDist = new Text[N];
        readonly RectTransform[] _mini = new RectTransform[N];
        readonly CanvasGroup[] _miniGroup = new CanvasGroup[N];
        readonly VectorShape[] _miniShape = new VectorShape[N];
        readonly int[] _serial = new int[N];
        readonly float[] _popAt = new float[N];
        RectTransform _root;
        RectTransform _mapArea;
        static readonly string[] Metres = BuildMetres();

        static string[] BuildMetres()
        {
            var a = new string[1000];
            for (int i = 0; i < a.Length; i++) a[i] = i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "m";
            return a;
        }

        public void Build(RectTransform root)
        {
            _root = root;
            for (int i = 0; i < N; i++)
            {
                RectTransform rt = InteractUi.Centred(root, "World Ping " + i, 60f, 60f);
                _worldGroup[i] = rt.gameObject.AddComponent<CanvasGroup>();
                _worldGroup[i].blocksRaycasts = false;
                VectorShape ring = MenuUi.Shape(rt, "Ring", ShapeKind.Circle, 4f, 4f, 52f, 52f, new Color(0, 0, 0, 0), 8f);
                ring.OutlinePx = 3f;
                ring.RimPx = 2f;
                _worldShape[i] = MenuUi.Shape(rt, "Shape", ShapeKind.TriangleDown, 14f, 14f, 32f, 32f, CMTokens.MeterOrange.Color, 5f);
                _worldWord[i] = MenuUi.Label(rt, "Word", CMTokens.Ui, 16, InteractUi.Cream, TextAnchor.MiddleCenter, -50f, -24f, 160f, 22f, "", 2.5f);
                _worldDist[i] = MenuUi.Label(rt, "Distance", CMTokens.Mono, 16, InteractUi.Cream, TextAnchor.MiddleCenter, -10f, 60f, 80f, 20f, "", 2.5f);
                rt.gameObject.SetActive(false);
                _world[i] = rt;
            }
        }

        /// <summary>Adds the minimap layer under the HUD's map area (once it exists).</summary>
        public void BindMinimap(RectTransform mapArea)
        {
            if (mapArea == null || _mapArea == mapArea) return;
            _mapArea = mapArea;
            for (int i = 0; i < N; i++)
            {
                var go = new GameObject("Ping " + i, typeof(RectTransform));
                go.layer = 5;
                go.transform.SetParent(mapArea, false);
                RectTransform rt = (RectTransform)go.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(14f, 14f);
                _miniGroup[i] = go.AddComponent<CanvasGroup>();
                _miniGroup[i].blocksRaycasts = false;
                _miniShape[i] = MenuUi.Shape(rt, "Shape", ShapeKind.TriangleDown, 0f, 0f, 14f, 14f, CMTokens.MeterOrange.Color, 3f);
                _miniShape[i].OutlinePx = 1.5f;
                _miniShape[i].RimPx = 0f;
                go.SetActive(false);
                _mini[i] = rt;
            }
        }

        /// <summary>Per frame: places and fades every live ping. Allocation-free.</summary>
        public void Tick(World w, UnityEngine.Camera cam, float realNow, int me)
        {
            if (_root == null) return;
            double now = w != null ? w.Match.Time : 0;
            MinimapProjection proj = MinimapGeometry.Projection;
            Rect r = _root.rect;
            for (int i = 0; i < N; i++)
            {
                Ping p = Pings.Book.Get(i);
                float a = w != null ? PingBook.Alpha(p, now) : 0f;
                bool on = a > 0f && cam != null;
                if (_world[i].gameObject.activeSelf != on) _world[i].gameObject.SetActive(on);
                bool miniOn = a > 0f && _mini[i] != null;
                if (_mini[i] != null && _mini[i].gameObject.activeSelf != miniOn) _mini[i].gameObject.SetActive(miniOn);
                if (a <= 0f) continue;
                if (_serial[i] != p.Serial)
                {
                    _serial[i] = p.Serial;
                    _popAt[i] = realNow;
                    Color c = PingWords.Colour(p.Kind);
                    _worldShape[i].Kind = PingWords.Shape[(int)p.Kind];
                    _worldShape[i].SetFill(c);
                    _worldShape[i].SetVerticesDirty();
                    HudText.Set(_worldWord[i], PingWords.Upper[(int)p.Kind]);
                    if (_miniShape[i] != null)
                    {
                        _miniShape[i].Kind = PingWords.Shape[(int)p.Kind];
                        _miniShape[i].SetFill(c);
                        _miniShape[i].SetVerticesDirty();
                    }
                }
                float pop = Mathf.Clamp01((realNow - _popAt[i]) / PopSeconds);
                float scale = 1f + 0.35f * (1f - pop) * (1f - pop);
                if (miniOn)
                {
                    Vector2 px = proj.ToPixel(p.X, p.Z);
                    _mini[i].anchoredPosition = new Vector2(px.x, -px.y);
                    _miniGroup[i].alpha = a;
                }
                if (!on) continue;
                Vector3 world = SimSpace.ToWorld(p.X, GroundHeight.At(w.Map, p.X, p.Z) + 1.2, p.Z);
                Vector3 sp = cam.WorldToScreenPoint(world);
                bool behind = sp.z < 0f;
                Vector2 screen = new Vector2(sp.x, sp.y);
                if (behind) screen = new Vector2(cam.pixelWidth, cam.pixelHeight) - screen;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, screen, cam, out Vector2 local);
                bool inside = !behind && local.x > r.xMin + EdgePad && local.x < r.xMax - EdgePad && local.y > r.yMin + EdgePad && local.y < r.yMax - EdgePad;
                if (!inside)
                {
                    local.x = Mathf.Clamp(local.x, r.xMin + EdgePad, r.xMax - EdgePad);
                    local.y = Mathf.Clamp(local.y, r.yMin + EdgePad, r.yMax - EdgePad);
                    scale *= 0.8f;
                }
                _world[i].anchoredPosition = local;
                _world[i].localScale = new Vector3(scale, scale, 1f);
                _worldGroup[i].alpha = a;
                if (me >= 0 && me < w.ActorCount)
                {
                    double dx = p.X - w.X[me], dz = p.Z - w.Z[me];
                    HudText.Set(_worldDist[i], Metres[Math.Min(999, (int)Math.Round(Math.Sqrt(dx * dx + dz * dz)))]);
                }
            }
        }

        public bool WorldShown(int slot) => _world[slot] != null && _world[slot].gameObject.activeSelf;
        public bool MinimapShown(int slot) => _mini[slot] != null && _mini[slot].gameObject.activeSelf;
        public Vector2 MinimapPos(int slot) => _mini[slot] != null ? _mini[slot].anchoredPosition : Vector2.zero;
    }

    /// <summary>
    /// The ping feed: up to four short lines above the player card ("You: Attack here, the hill"), newest at the bottom,
    /// each fading after PingBook.Life seconds. Lines come from PingWords.FeedLine (built once), so a ping allocates nothing.
    /// </summary>
    public sealed class PingFeed
    {
        public const int Lines = 4;
        const float LineH = 30f, Bottom = 196f, Left = 28f, Life = (float)PingBook.Life, Fade = 0.4f;
        readonly RectTransform[] _rt = new RectTransform[Lines];
        readonly Text[] _text = new Text[Lines];
        readonly VectorShape[] _icon = new VectorShape[Lines];
        readonly CanvasGroup[] _group = new CanvasGroup[Lines];
        readonly float[] _at = new float[Lines];
        int _count;
        static readonly string[] WaitLines = BuildWait();

        static string[] BuildWait()
        {
            var a = new string[7];
            for (int i = 0; i < a.Length; i++) a[i] = "Too many pings: wait " + i + " s";
            return a;
        }

        public void Build(RectTransform root)
        {
            for (int i = 0; i < Lines; i++)
            {
                RectTransform rt = MenuUi.Rt(root, "Ping Line " + i);
                rt.anchorMin = rt.anchorMax = new Vector2(0f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(520f, LineH);
                _group[i] = rt.gameObject.AddComponent<CanvasGroup>();
                _group[i].blocksRaycasts = false;
                _icon[i] = MenuUi.Shape(rt, "Icon", ShapeKind.TriangleDown, 0f, 4f, 22f, 22f, CMTokens.MeterOrange.Color, 4f);
                _icon[i].RimPx = 0f;
                _text[i] = MenuUi.Label(rt, "Text", CMTokens.Ui, 20, InteractUi.Cream, TextAnchor.MiddleLeft, 32f, 0f, 488f, LineH, "", 2.5f);
                rt.gameObject.SetActive(false);
                _rt[i] = rt;
            }
        }

        public string LastLine => _count > 0 ? _text[(_count - 1) % Lines].text : null;

        public void Add(Ping p, float realNow) => Push(PingWords.FeedLine[(int)p.Kind, Mathf.Clamp(p.Place, 0, PingWords.Places.Length - 1)],
            PingWords.Shape[(int)p.Kind], PingWords.Colour(p.Kind), realNow);

        public void AddRefused(double waitSeconds, float realNow) =>
            Push(WaitLines[Mathf.Clamp(Mathf.CeilToInt((float)waitSeconds), 0, WaitLines.Length - 1)], ShapeKind.Diamond, CMTokens.Stone.Color, realNow);

        void Push(string line, ShapeKind shape, Color c, float realNow)
        {
            if (_rt[0] == null) return;
            int slot = _count % Lines;
            _count++;
            HudText.Set(_text[slot], line);
            _icon[slot].Kind = shape;
            _icon[slot].SetFill(c);
            _icon[slot].SetVerticesDirty();
            _at[slot] = realNow;
            _rt[slot].gameObject.SetActive(true);
        }

        /// <summary>Per frame: stacks the live lines (newest lowest) and fades the old ones.</summary>
        public void Tick(float realNow)
        {
            if (_rt[0] == null) return;
            int row = 0;
            for (int k = 0; k < Lines; k++)
            {
                int slot = ((_count - 1 - k) % Lines + Lines) % Lines;
                if (k >= _count || !_rt[slot].gameObject.activeSelf) continue;
                float age = realNow - _at[slot];
                if (age >= Life) { _rt[slot].gameObject.SetActive(false); continue; }
                _group[slot].alpha = age > Life - Fade ? (Life - age) / Fade : 1f;
                _rt[slot].anchoredPosition = new Vector2(Left, Bottom + row * (LineH + 4f));
                row++;
            }
        }
    }
}
