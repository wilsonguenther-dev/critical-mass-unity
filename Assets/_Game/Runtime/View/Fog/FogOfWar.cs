using System;
using CriticalMass.Runtime.Game;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using CriticalMass.Sim.Grid;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace CriticalMass.Runtime.View.Fog
{
    /// <summary>
    /// Batch 12 item P: fog of war on this machine, VIEW ONLY (numbers in FogConfig, sight in FogSight).
    /// <para>10 times a second (FogClock) it recomputes what the local player's team sees from the shared sim state into
    /// a preallocated FogGrid and uploads it once into one 128 x 128 single-channel texture (R8, bilinear, so the edge is
    /// soft). Every frame it fades each enemy's own visibility over FogConfig.FadeSeconds; readers ask Hides(actor):
    /// ActorViews (models), GameHud (minimap dots, overhead hearts), FullMapView (markers). Nothing reaches the sim, so
    /// lockstep and checksums are untouched and bots see everything as before.</para>
    /// <para>Drawing: top-down, one quad at the haze floor (just above the highest walkable ground) blending toward the
    /// haze tone where unseen (darker, less saturated); third and first person, a three-layer height-fog band from the
    /// same texture (one mesh, one draw), so a low camera sees the same edge. One draw call either way, 2 or 6 triangles.</para>
    /// <para>Off: Classic profile, the setting off, spectating, results (match ended), behind the title, and batch-mode
    /// runs unless -fogShots or "-fog on" (FogConfig.Pin).</para>
    /// </summary>
    [DefaultExecutionOrder(-60)]   // before ActorViews and the HUD read it in their LateUpdate
    [global::CriticalMass.Runtime.Flow.GameOverlay(Order = 57, Scope = global::CriticalMass.Runtime.Flow.OverlayScope.MatchOnly)]
    public sealed class FogOfWar : MonoBehaviour
    {
        public const string HazeShaderResource = "CMFogHaze";
        static readonly int FogTexId = Shader.PropertyToID("_FogTex"), HazeId = Shader.PropertyToID("_Haze");
        static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("CM.Fog.Update");

        static FogOfWar _current;

        /// <summary>True while the fog is drawn on this machine this frame.</summary>
        public static bool Active => _current != null && _current._active;
        /// <summary>The visibility texture (null until the fog first turns on).</summary>
        public static Texture2D Texture => _current != null ? _current._tex : null;
        public static FogOfWar Current => _current;

        /// <summary>True when actor i is an enemy the local team does not see now (not drawn, no dot, no overhead UI).</summary>
        public static bool Hides(int actor)
        {
            FogOfWar f = _current;
            return f != null && f._active && actor >= 0 && actor < f._vis.Length && f._enemy[actor] && f._vis[actor] < FogConfig.EnemyDrawnAt;
        }

        /// <summary>The faded visibility (0..1) of the sim point (x, z) as the map shows it; 1 when the fog is off.</summary>
        public static float VisibleAt(double x, double z)
        {
            FogOfWar f = _current;
            if (f == null || !f._active || f._map == null) return 1f;
            int res = f._grid.Res;
            int cx = (int)Math.Floor(x / f._map.Width * res), cz = (int)Math.Floor(z / f._map.Height * res);
            if (cx < 0 || cz < 0 || cx >= res || cz >= res) return 1f;
            return f._grid.Shown[cz * res + cx] / 255f;
        }

        SimDriverBehaviour _driver;
        readonly FogGrid _grid = new FogGrid();
        FogClock _clock;
        Texture2D _tex;
        float[] _vis = new float[40];
        bool[] _enemy = new bool[40];
        bool _active, _settingOn = true, _wantNow, _wantKnown, _hazeOk, _hazeTried;
        MapDef _map;
        GameObject _topGo, _bandGo;
        Mesh _topMesh, _bandMesh;
        Material _hazeMat;

        public FogGrid Grid => _grid;
        /// <summary>Visibility map updates since load, and the last update's cost in milliseconds (capture log).</summary>
        public int Updates { get; private set; }
        public double LastUpdateMs { get; private set; }

        void OnEnable()
        {
            _current = this;
            _settingOn = global::CriticalMass.Runtime.Menu.SettingsExtras.FogOfWar;
            global::CriticalMass.Runtime.Menu.SettingsExtras.Changed += OnSettings;
        }

        void OnDisable()
        {
            global::CriticalMass.Runtime.Menu.SettingsExtras.Changed -= OnSettings;
            Deactivate();
            if (_current == this) _current = null;
        }

        void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
            if (_topMesh != null) Destroy(_topMesh);
            if (_bandMesh != null) Destroy(_bandMesh);
            if (_hazeMat != null) Destroy(_hazeMat);
        }

        void OnSettings() => _settingOn = global::CriticalMass.Runtime.Menu.SettingsExtras.FogOfWar;

        void Start()
        {
            if (FogShots.Requested && GetComponent<FogShots>() == null) gameObject.AddComponent<FogShots>();
        }

        /// <summary>Whether the fog should draw now (every switch in FogConfig.WantFog).</summary>
        bool Want(World w)
        {
            bool setting = FogConfig.CaptureOverride ?? FogConfig.Pin ?? _settingOn;
            bool title = global::CriticalMass.Runtime.Hud.TitleBackdrop.Frozen || GetComponent<global::CriticalMass.Runtime.Hud.TitleScreen>() != null;
            return FogConfig.WantFog(setting, !FogConfig.ProfileAllows(_driver.Config), global::CriticalMass.Runtime.Spectator.SpectatorMode.IsSpectating,
                w.Match.Phase == MatchPhase.Ended, title);
        }

        void LateUpdate()
        {
            if (_driver == null) _driver = GetComponent<SimDriverBehaviour>();
            World w = _driver != null ? _driver.World : null;
            if (w == null || w.ActorCount == 0 || LocalSeat.Actor >= w.ActorCount) { Deactivate(); return; }
            bool tick = _clock.Step(Time.unscaledDeltaTime);
            if (tick || !_wantKnown) { _wantNow = Want(w); _wantKnown = true; }
            if (!_wantNow) { Deactivate(); return; }

            bool first = !_active;
            if (first || tick || w.Map != _map)
            {
                EnsureBuilt(w);
                UpdateNow(w, first);
            }
            _active = true;

            // per frame: each enemy's own fade (0.3 s), no allocation
            float step = (float)(Time.unscaledDeltaTime / FogConfig.FadeSeconds);
            for (int i = 0; i < _vis.Length; i++)
            {
                float target = _grid.Seen(i) ? 1f : 0f;
                _vis[i] = first ? target : Mathf.MoveTowards(_vis[i], target, step);
            }

            bool low = global::CriticalMass.Runtime.Camera.GroundView.IsActive;
            bool top = _hazeOk && !low, band = _hazeOk && low;
            if (_topGo.activeSelf != top) _topGo.SetActive(top);
            if (_bandGo.activeSelf != band) _bandGo.SetActive(band);
        }

        /// <summary>One visibility update and its single texture upload (also the capture's forced refresh).</summary>
        public void UpdateNow(World w, bool snap)
        {
            using (UpdateMarker.Auto())
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                TeamId team = w.Actors[LocalSeat.Actor].Team;
                _grid.Update(w, team, snap);
                if (_vis.Length < w.ActorCount) { Array.Resize(ref _vis, w.ActorCount); Array.Resize(ref _enemy, w.ActorCount); }
                for (int i = 0; i < _enemy.Length; i++) _enemy[i] = i < w.ActorCount && w.Actors[i] != null && w.Actors[i].Team != team;
                _tex.SetPixelData(_grid.Shown, 0);
                _tex.Apply(false, false);
                Updates++;
                LastUpdateMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            }
        }

        void Deactivate()
        {
            _active = false;
            if (_topGo != null && _topGo.activeSelf) _topGo.SetActive(false);
            if (_bandGo != null && _bandGo.activeSelf) _bandGo.SetActive(false);
        }

        void EnsureBuilt(World w)
        {
            if (_tex == null)
            {
                int res = FogConfig.Resolution;
                _tex = new Texture2D(res, res, TextureFormat.R8, false, true)
                {
                    name = "Fog Of War Visibility",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    anisoLevel = 0,
                };
            }
            if (_hazeMat == null && !_hazeTried)
            {
                _hazeTried = true;
                Shader sh = Resources.Load<Shader>(HazeShaderResource);
                _hazeOk = sh != null && sh.isSupported;
                if (!_hazeOk) Debug.LogWarning("[CM.Fog] haze shader " + HazeShaderResource + " missing or unsupported; the haze is not drawn (maps and fighters still follow the fog)");
                _hazeMat = _hazeOk ? new Material(sh) { name = "Fog Haze", renderQueue = (int)RenderQueue.Transparent - 50 } : null;
                if (_hazeMat != null)
                {
                    _hazeMat.SetTexture(FogTexId, _tex);
                    _hazeMat.SetColor(HazeId, FogConfig.Haze);
                }
            }
            if (_map == w.Map && _topGo != null) return;
            _map = w.Map;
            float floor = FloorTop(_map) + FogConfig.HazeLift;
            float[] topH = { 0f };
            float[] topA = { FogConfig.TopDownAlpha };
            if (_topMesh != null) Destroy(_topMesh);
            if (_bandMesh != null) Destroy(_bandMesh);
            _topMesh = Layers(_map, floor, topH, topA, "Fog Haze Top-Down");
            _bandMesh = Layers(_map, floor, FogConfig.BandHeights, FogConfig.BandAlphas, "Fog Haze Band");
            if (_topGo == null) _topGo = Holder("Fog Haze (top-down)");
            if (_bandGo == null) _bandGo = Holder("Fog Haze (low views)");
            _topGo.GetComponent<MeshFilter>().sharedMesh = _topMesh;
            _bandGo.GetComponent<MeshFilter>().sharedMesh = _bandMesh;
        }

        GameObject Holder(string objectName)
        {
            var go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _hazeMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            r.allowOcclusionWhenDynamic = false;
            go.SetActive(false);
            return go;
        }

        /// <summary>The highest walkable ground of the map (view ground heights), so the haze floor lies over every island.</summary>
        public static float FloorTop(MapDef map)
        {
            double top = 0;
            bool any = false;
            for (int cz = 0; cz < map.Height; cz++)
                for (int cx = 0; cx < map.Width; cx++)
                {
                    if (map.Cells[cz * map.Width + cx] == Cell.Void) continue;
                    double h = global::CriticalMass.Runtime.GroundHeight.At(map, cx + 0.5, cz + 0.5);
                    if (!any || h > top) { top = h; any = true; }
                }
            return (float)top;
        }

        /// <summary>One quad per layer over the map's sim bounds; uv = (x / Width, z / Height); vertex alpha = the layer's opacity.</summary>
        public static Mesh Layers(MapDef map, float floor, float[] heights, float[] alphas, string meshName)
        {
            int n = heights.Length;
            var v = new Vector3[n * 4];
            var uv = new Vector2[n * 4];
            var col = new Color[n * 4];
            var tri = new int[n * 6];
            for (int l = 0; l < n; l++)
            {
                float y = floor + heights[l];
                for (int k = 0; k < 4; k++)
                {
                    double x = (k == 1 || k == 2) ? map.Width : 0, z = k >= 2 ? map.Height : 0;
                    v[l * 4 + k] = SimSpace.ToWorld(x, y, z);
                    uv[l * 4 + k] = new Vector2((float)(x / map.Width), (float)(z / map.Height));
                    col[l * 4 + k] = new Color(1f, 1f, 1f, alphas[l]);
                }
                int b = l * 4, t = l * 6;
                tri[t] = b; tri[t + 1] = b + 1; tri[t + 2] = b + 2;
                tri[t + 3] = b; tri[t + 4] = b + 2; tri[t + 5] = b + 3;
            }
            var m = new Mesh { name = meshName };
            m.vertices = v;
            m.uv = uv;
            m.colors = col;
            m.triangles = tri;
            m.RecalculateBounds();
            return m;
        }
    }
}
