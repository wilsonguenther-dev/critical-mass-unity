using System;
using CriticalMass.Runtime.Hud.Style;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using CriticalMass.Sim.Maps;
using UnityEngine;
using UnityEngine.UI;

namespace CriticalMass.Runtime.Hud.Ui
{
    /// <summary>
    /// The map the minimap was baked from (Editor/MinimapBaker): Skybreak Basin with the default config, at the gameplay
    /// camera's yaw. The HUD lays the minimap out from this projection, so the frame, the image and every marker share
    /// one mapping.
    /// </summary>
    public static class MinimapGeometry
    {
        public static readonly MapDef BakedMap = SkybreakBasinMap.BuildSkybreakBasin(GameConfig.CreateDefault());
        /// <summary>The heading the minimap images in Resources were baked at (Editor/MinimapBaker, default config).</summary>
        public static readonly double BakedYaw = GameConfig.CreateDefault().Render.CameraYawDeg;
        static MinimapProjection _proj;
        static double _projYaw = double.NaN;
        static readonly System.Collections.Generic.Dictionary<int, UnityEngine.Texture2D> _runtime = new System.Collections.Generic.Dictionary<int, UnityEngine.Texture2D>();
        static double _texYaw = double.NaN;

        /// <summary>The minimap's mapping at the camera heading on screen (CameraHeading.Active), so the minimap matches it.</summary>
        public static MinimapProjection Projection
        {
            get
            {
                double y = CriticalMass.Runtime.Camera.CameraHeading.Active;
                if (_proj == null || y != _projYaw) { _proj = MinimapProjection.For(BakedMap, y); _projYaw = y; }
                return _proj;
            }
        }

        /// <summary>
        /// The minimap image at a tier for the heading on screen: the baked Resources image at the baked heading, otherwise
        /// baked here once per tier with the same MinimapBake the editor uses (cached).
        /// polish-3 (boot timing: "hud minimap" took 959 of the 973 ms HUD build, boot to title 8.8 s): the creator heading
        /// (135) is not the baked heading (45), so every boot ran the full 3x3-supersampled bake on the main thread. The bake
        /// is pure C# (no Unity calls; MapDef, Color maths), so it now runs on a worker thread, started at HUD build and
        /// finished long before the first match; this returns null until it is ready (callers keep the image hidden and
        /// ask again next frame), then turns the pixels into a texture once on the main thread (SetPixels32 + one Apply).
        /// Same pixels as before, so the minimap looks the same in the match. <see cref="TextureNow"/> waits for it.
        /// </summary>
        public static UnityEngine.Texture2D Texture(int tier) => Get(tier, false);

        /// <summary>The minimap image at a tier, baking on this thread if needed (tools and tests).</summary>
        public static UnityEngine.Texture2D TextureNow(int tier) => Get(tier, true);

        /// <summary>A worker bake in flight: the heading and tier it is for, and its pixels when done.</summary>
        sealed class Pending
        {
            public double Yaw;
            public int Tier, W, H;
            public Color32[] Px;
            public Exception Error;
            public bool Queued;
            public volatile bool Done;
        }
        static readonly System.Collections.Generic.Dictionary<int, Pending> _pending = new System.Collections.Generic.Dictionary<int, Pending>();

        static UnityEngine.Texture2D Get(int tier, bool wait)
        {
            double y = CriticalMass.Runtime.Camera.CameraHeading.Active;
            if (Math.Abs(y - BakedYaw) < 1e-6) return UnityEngine.Resources.Load<UnityEngine.Texture2D>("UI/Minimap/minimap@" + tier + "x");
            if (y != _texYaw) { _runtime.Clear(); _pending.Clear(); _texYaw = y; }
            if (_runtime.TryGetValue(tier, out UnityEngine.Texture2D t) && t != null) return t;
            if (!_pending.TryGetValue(tier, out Pending job))
            {
                job = new Pending { Yaw = y, Tier = tier };
                _pending[tier] = job;
                if (!wait)
                {
                    MapDef map = BakedMap;   // built here, on the main thread, before the worker reads it
                    Pending j = job;
                    try
                    {
                        System.Threading.ThreadPool.QueueUserWorkItem(_ => Bake(j, map));
                        job.Queued = true;
                    }
                    catch (Exception e) { job.Error = e; job.Done = true; }
                }
            }
            if (!job.Done)
            {
                if (!wait) return null;
                if (!job.Queued) Bake(job, BakedMap);   // not started on a worker: bake here
                else while (!job.Done) System.Threading.Thread.Sleep(1);
            }
            _pending.Remove(tier);
            if (job.Px == null)
            {
                // the worker failed (should not happen): bake here so the minimap is never missing
                if (job.Error != null) UnityEngine.Debug.LogWarning("[Minimap] worker bake failed, baking on the main thread: " + job.Error.Message);
                var again = new Pending { Yaw = y, Tier = tier };
                Bake(again, BakedMap);
                job = again;
                if (job.Px == null) return null;
            }
            t = MinimapBake.ToTexture(job.Px, job.W, job.H);
            t.name = "minimap@" + tier + "x (" + y.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) + ")";
            _runtime[tier] = t;
            return t;
        }

        static void Bake(Pending job, MapDef map)
        {
            try
            {
                var bake = new MinimapBake(map, job.Yaw);
                job.Px = bake.Render(job.Tier, out job.W, out job.H);
            }
            catch (Exception e) { job.Error = e; }
            finally { job.Done = true; }
        }

        /// <summary>Texture tier for a canvas scale: 1x up to 1080p (drawn 1:1), 2x up to 4K, else 4x.</summary>
        public static int TierFor(float canvasScale) => canvasScale <= 1.01f ? 1 : canvasScale <= 2.01f ? 2 : 4;
    }

    /// <summary>
    /// Ground changes since the bake, drawn over the baked image as cell diamonds: dug or broken ground (void now) in the
    /// sky tone, new ground (built or bridged) in grass, broken breakables as rubble, barricades in brass. Rebuilt only
    /// when World.GridVersion changes, never per frame.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MinimapGround : MaskableGraphic
    {
        static readonly Color Hole = CMTokens.ParseHex("#1E2D4A"), NewGround = CMTokens.ParseHex("#77B247"),
            Rubble = CMTokens.ParseHex("#8A6A4C"), Cracked = CMTokens.ParseHex("#A88762"), Brass = CMTokens.ToolBrass.Color,
            Floor = CMTokens.ParseHex("#77B247"), Bridge = CMTokens.ParseHex("#C99258");

        MinimapProjection _proj;
        byte[] _base;
        MapDef _map;
        int _version = int.MinValue;
        public int Changed { get; private set; }

        public void Bind(MinimapProjection proj, MapDef baked)
        {
            _proj = proj;
            _base = baked.Cells;
            _version = int.MinValue;
        }

        /// <summary>Call every frame; does work only when the grid version moved.</summary>
        public void Watch(MapDef map, int gridVersion)
        {
            if (gridVersion == _version && map == _map) return;
            _version = gridVersion;
            _map = map;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Changed = 0;
            if (_proj == null || _map == null || _base == null || _map.Cells == null || _map.Cells.Length != _base.Length) return;
            Rect r = rectTransform.rect;
            Vector2 tl = new Vector2(r.xMin, r.yMax);
            int w = _map.Width;
            for (int i = 0; i < _base.Length; i++)
            {
                byte was = _base[i], now = _map.Cells[i];
                if (was == now) continue;
                Color c;
                if (now == Cell.Void) c = Hole;
                else if (now == Cell.Barricade) c = Brass;
                else if (now == Cell.BreakableDestroyed) c = Rubble;
                else if (now == Cell.BreakableCracked) c = Cracked;
                else if (now == Cell.Bridge) c = Bridge;
                else if (was == Cell.Void) c = NewGround;
                else c = Floor;
                Diamond(vh, tl, i % w, i / w, c);
                Changed++;
            }
        }

        void Diamond(VertexHelper vh, Vector2 tl, int cx, int cz, Color c)
        {
            int s = vh.currentVertCount;
            for (int k = 0; k < 4; k++)
            {
                double x = cx + (k == 1 || k == 2 ? 1 : 0), z = cz + (k >= 2 ? 1 : 0);
                Vector2 p = _proj.ToPixel(x, z);
                vh.AddVert(new Vector3(tl.x + p.x, tl.y - p.y), c, Vector4.zero);
            }
            vh.AddTriangle(s, s + 1, s + 2);
            vh.AddTriangle(s, s + 2, s + 3);
        }
    }
}
