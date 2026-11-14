using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using CriticalMass.Runtime.Hud.Ui;
using CriticalMass.Runtime.View;
using CriticalMass.Runtime.View.Effects;
using CriticalMass.Sim.Config;
using CriticalMass.Sim.Core;
using UnityEngine;

namespace CriticalMass.Runtime.Hud
{
    /// <summary>
    /// first-minutes-1 (playthrough walk 1, defect #1 and fix 1): the first-time player could not tell which fighter was
    /// theirs, which team they were on, or what wins. Pure rules here (tests); YouMarker draws them.
    /// </summary>
    public static class FirstMinutes
    {
        /// <summary>Sim seconds the YOU ring and arrow stay at full size after the match starts or the player respawns.</summary>
        public const float LingerSeconds = 5f;
        /// <summary>Sim seconds the marker then takes to shrink away.</summary>
        public const float FadeSeconds = 1f;
        /// <summary>Ring radius on the ground (world units; a fighter's footprint is about 0.6) and its band width.</summary>
        public const float RingRadius = 1.15f, RingBand = 0.22f;
        /// <summary>Arrow tip height above the ground (above the name tag line) and the arrow's size.</summary>
        public const float ArrowHeight = 2.9f, ArrowSize = 0.55f;
        /// <summary>Ring segments: 2 triangles each.</summary>
        public const int RingSegments = 40;
        /// <summary>Seconds the team banner holds (banner clock).</summary>
        public const double BannerHoldSeconds = 4.5;

        /// <summary>Marker size 0..1 for the sim seconds since the match start or the last respawn.</summary>
        public static float MarkerScale(float sinceSeconds)
        {
            if (sinceSeconds < 0f) return 0f;
            if (sinceSeconds <= LingerSeconds) return 1f;
            float t = (sinceSeconds - LingerSeconds) / FadeSeconds;
            return t >= 1f ? 0f : 1f - t;
        }

        /// <summary>True on the frame the player's fighter comes back (dead last frame, alive now).</summary>
        public static bool Respawned(bool wasAlive, bool alive) => !wasAlive && alive;

        /// <summary>A new match on the same driver: the sim clock ran backwards.</summary>
        public static bool NewMatch(double lastTime, double time) => time + 0.25 < lastTime;

        public static string TeamWord(TeamId t) => t == TeamId.Azure ? "AZURE" : "CRIMSON";

        /// <summary>The one-line goal. True to the rule: Match.DecideWinner compares hill seconds.</summary>
        public static string GoalLine(TeamId t) => "Hold the hill longer than " + TeamWord(t == TeamId.Azure ? TeamId.Crimson : TeamId.Azure) + " to win";

        /// <summary>The match-start banner: your team in its colour, the goal under it.</summary>
        public static BannerSpec StartBanner(TeamId t) => new BannerSpec
        {
            Headline = "YOU ARE " + TeamWord(t),
            Reason = GoalLine(t),
            Icon = UiIcons.Banner[t == TeamId.Azure ? 0 : 1],
            Style = t == TeamId.Azure ? BannerStyle.Azure : BannerStyle.Crimson,
            HoldSeconds = BannerHoldSeconds,
            Priority = 2,
        };

        /// <summary>A flat ring on the ground (RingSegments x 2 triangles), built once and shared.</summary>
        public static Mesh RingMesh()
        {
            int n = RingSegments;
            var v = new Vector3[n * 2];
            var tri = new int[n * 6];
            float r0 = RingRadius - RingBand * 0.5f, r1 = RingRadius + RingBand * 0.5f;
            for (int i = 0; i < n; i++)
            {
                float a = i * Mathf.PI * 2f / n;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                v[i * 2] = new Vector3(c * r0, 0f, s * r0);
                v[i * 2 + 1] = new Vector3(c * r1, 0f, s * r1);
                int j = (i + 1) % n;
                int k = i * 6;
                tri[k] = i * 2; tri[k + 1] = j * 2; tri[k + 2] = i * 2 + 1;
                tri[k + 3] = i * 2 + 1; tri[k + 4] = j * 2; tri[k + 5] = j * 2 + 1;
            }
            var m = new Mesh { name = "You Ring" };
            m.vertices = v;
            m.triangles = tri;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        /// <summary>A downward arrow: an upside-down square pyramid, tip at the origin (6 triangles), built once and shared.</summary>
        public static Mesh ArrowMesh()
        {
            float h = ArrowSize * 0.5f, up = ArrowSize * 1.2f;
            var v = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(-h, up, -h), new Vector3(h, up, -h), new Vector3(h, up, h), new Vector3(-h, up, h),
            };
            var tri = new[] { 0, 2, 1, 0, 3, 2, 0, 4, 3, 0, 1, 4, 1, 2, 3, 1, 3, 4 };
            var m = new Mesh { name = "You Arrow" };
            m.vertices = v;
            m.triangles = tri;
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>
    /// first-minutes-1: a brass ring on the ground under the player's fighter and a brass arrow over its head, full size
    /// for FirstMinutes.LingerSeconds after the match starts and after every respawn, then shrinking away. Brass is the
    /// HUD's YOU colour (the full map's YOU arrow), so it never reads as a team. At match start it also shows the team
    /// banner ("YOU ARE AZURE" / "Hold the hill longer than CRIMSON to win"). View only: reads the world, writes nothing.
    /// Cost: two meshes shared for the life of the app (80 + 6 triangles), the cached brass prop material, two draw calls
    /// while shown and none after (the root is switched off). No per-frame allocation.
    /// </summary>
    [GameOverlay(Order = 72, Scope = OverlayScope.MatchOnly)]
    public sealed class YouMarker : MonoBehaviour
    {
        static Mesh s_ring, s_arrow;

        SimDriverBehaviour _driver;
        World _world;
        Transform _root, _arrow;
        double _lastTime = double.MaxValue, _since = -1;
        bool _wasAlive, _bannerShown;
        int _lastTick = -1;
        double _px, _pz, _cx, _cz;

        /// <summary>Marker size now (0 = hidden; tests and captures).</summary>
        public float Scale { get; private set; }

        void Start()
        {
            _driver = GetComponent<SimDriverBehaviour>();
            if (s_ring == null) s_ring = FirstMinutes.RingMesh();
            if (s_arrow == null) s_arrow = FirstMinutes.ArrowMesh();
            Material brass = ToonMaterials.Prop(Palette.Brass, false);
            _root = new GameObject("You Marker").transform;
            _root.SetParent(transform, false);
            Part("Ring", s_ring, brass, _root);
            _arrow = Part("Arrow", s_arrow, brass, _root);
            _root.gameObject.SetActive(false);
        }

        static Transform Part(string name, Mesh mesh, Material mat, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        void LateUpdate()
        {
            if (_root == null) return;
            World w = _driver != null ? _driver.World : null;
            int me = HudBinder.LocalActor;
            if (w == null || me < 0 || me >= w.ActorCount || w.Match.Phase != MatchPhase.Playing)
            {
                Hide();
                return;
            }
            Actor a = w.Actors[me];
            if (w != _world || FirstMinutes.NewMatch(_lastTime, w.Match.Time))
            {
                _world = w;
                _since = w.Match.Time;
                _bannerShown = false;
                _wasAlive = a.Alive;
                _lastTick = -1;
            }
            _lastTime = w.Match.Time;
            if (!_bannerShown)
            {
                _bannerShown = true;
                Banner.Show(FirstMinutes.StartBanner(a.Team));
            }
            if (FirstMinutes.Respawned(_wasAlive, a.Alive)) { _since = w.Match.Time; _lastTick = -1; }
            _wasAlive = a.Alive;

            float k = a.Alive ? FirstMinutes.MarkerScale((float)(w.Match.Time - _since)) : 0f;
            Scale = k;
            if (k <= 0f)
            {
                Hide();
                return;
            }
            // Smooth between ticks (the sim runs at 30 Hz): keep the last two sim positions and blend by the driver's alpha.
            if (w.Tick != _lastTick)
            {
                if (_lastTick < 0) { _px = w.X[me]; _pz = w.Z[me]; }
                else { _px = _cx; _pz = _cz; }
                _cx = w.X[me];
                _cz = w.Z[me];
                _lastTick = w.Tick;
            }
            double t = _driver.Alpha;
            double x = _px + (_cx - _px) * t, z = _pz + (_cz - _pz) * t;
            Vector3 p = SimSpace.ToWorld(x, GroundHeight.At(w.Map, x, z) + 0.06, z);
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            _root.position = p;
            _root.localScale = new Vector3(k, k, k);
            float bob = FeelGate.ReduceMotion ? 0f : 0.12f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 1.5f);
            _arrow.localPosition = new Vector3(0f, FirstMinutes.ArrowHeight + bob, 0f);
        }

        void Hide()
        {
            Scale = 0f;
            if (_root != null && _root.gameObject.activeSelf) _root.gameObject.SetActive(false);
        }
    }
}
