using System;
using CriticalMass.Runtime.Flow;
using CriticalMass.Runtime.Game;
using UnityEngine;

namespace CriticalMass.Runtime.View.Atmosphere
{
    /// <summary>The map's time of day (arena night life 1, 2026-11-13). Visual only: the Sim never reads it.</summary>
    public enum TimeOfDay
    {
        Day = 0,
        Dusk = 1,
        Night = 2,
    }

    /// <summary>The player's choice (Settings, Game tab, and the READY screen): a time, or Random per match.</summary>
    public enum TimeOfDayChoice
    {
        Day = 0,
        Dusk = 1,
        Night = 2,
        Random = 3,
    }

    /// <summary>
    /// One lighting preset. Colours named "multiplier" are LINEAR factors applied to the toon bands in the shaders
    /// (CMTimeOfDay.hlsl: lit band x Key, shadow band x Shade, plus Ambient), so (1, 1, 1) / (0, 0, 0) is today's Day
    /// look exactly. Key folds in the post exposure (the project has no post-processing volume). Fog, Void, Rim and
    /// Background are sRGB colours as authored.
    /// </summary>
    [Serializable]
    public struct TimeOfDayPreset
    {
        public TimeOfDay Id;
        public string Name;
        /// <summary>Key light (sun or moon) elevation and travel direction clockwise from screen-up, degrees.</summary>
        public float SunElevationDeg, SunAzimuthFromScreenUpDeg;
        /// <summary>Lit-band multiplier (linear): the key light colour x exposure.</summary>
        public Color Key;
        /// <summary>Shadow-band multiplier (linear): the shadow tint.</summary>
        public Color Shade;
        /// <summary>Sky fill added (linear), scaled by 1 - lift.</summary>
        public Color Ambient;
        /// <summary>How much of the grade characters, the Kaiju and crystals skip (0..1): units stay the brightest layer.</summary>
        public float CharacterLift;
        /// <summary>Rim on world solids that face the key light (sRGB) and its strength (0 = none).</summary>
        public Color Rim;
        public float RimStrength;
        /// <summary>The height-fog (void) colour under the islands (sRGB) and how much it replaces the materials' fog.</summary>
        public Color Fog;
        public float FogMix;
        /// <summary>The deep void colour at the bottom of the view (sRGB) and the gradient amount.</summary>
        public Color Void;
        public float VoidAmount;
        /// <summary>Stars and moon in the open void (0..1); glow decal and light pool boost (multiplier minus 1).</summary>
        public float Stars, Moon, GlowBoost;
        /// <summary>Camera clear colour (sRGB), seen only where nothing at all is drawn.</summary>
        public Color Background;
        /// <summary>Night life intensities 0..1: fireflies, lantern halos and light pools, creature glow spots.</summary>
        public float Fireflies, LightPools, CreatureGlow;

        /// <summary>Every number and colour channel is finite and in its documented range (EditMode test).</summary>
        public bool IsComplete(out string problem)
        {
            problem = null;
            if (string.IsNullOrEmpty(Name)) { problem = "no name"; return false; }
            if (!(SunElevationDeg >= 10f && SunElevationDeg <= 85f)) { problem = "key elevation " + SunElevationDeg; return false; }
            if (!(SunAzimuthFromScreenUpDeg >= 0f && SunAzimuthFromScreenUpDeg < 360f)) { problem = "key azimuth"; return false; }
            foreach (Color c in new[] { Key, Shade, Ambient, Rim, Fog, Void, Background })
                if (!Finite(c.r) || !Finite(c.g) || !Finite(c.b) || c.r < 0f || c.g < 0f || c.b < 0f || c.r > 2f || c.g > 2f || c.b > 2f)
                { problem = "colour out of range " + c; return false; }
            foreach (float f in new[] { CharacterLift, RimStrength, FogMix, VoidAmount, Stars, Moon, Fireflies, LightPools, CreatureGlow })
                if (!Finite(f) || f < 0f || f > 1f) { problem = "amount out of 0..1: " + f; return false; }
            if (!Finite(GlowBoost) || GlowBoost < 0f || GlowBoost > 3f) { problem = "glow boost " + GlowBoost; return false; }
            return true;
        }

        static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);
    }

    /// <summary>The three presets and their blend (pure; EditMode tests read them).</summary>
    public static class TimeOfDayPresets
    {
        static Color Hex(string h)
        {
            ColorUtility.TryParseHtmlString(h, out Color c);
            return c;
        }

        static Color Lin(float r, float g, float b) => new Color(r, g, b, 1f);

        /// <summary>Today's look exactly on the play surface (identity grade, the sun of art direction 2.2); the void gets
        /// a gentle top-to-bottom gradient (map quality pass: the flat one-colour void read unfinished).</summary>
        public static readonly TimeOfDayPreset Day = new TimeOfDayPreset
        {
            Id = TimeOfDay.Day, Name = "DAY",
            SunElevationDeg = GameBootstrap.SunElevationDeg, SunAzimuthFromScreenUpDeg = GameBootstrap.SunAzimuthFromScreenUpDeg,
            Key = Lin(1f, 1f, 1f), Shade = Lin(1f, 1f, 1f), Ambient = Lin(0f, 0f, 0f), CharacterLift = 0f,
            Rim = Hex("#FFFFFF"), RimStrength = 0f,
            Fog = Hex("#B4DAEE"), FogMix = 0f,
            Void = Hex("#86BEE0"), VoidAmount = 0.45f,
            Stars = 0f, Moon = 0f, GlowBoost = 0f,
            Background = Hex(CriticalMass.Sim.Config.Palette.SkyTop),
            Fireflies = 0f, LightPools = 0f, CreatureGlow = 0f,
        };

        /// <summary>Golden hour: a low sun from the screen's left (long shadows to the lower right), the lit band only a
        /// little warmer and no darker (a darker dusk walked the paving into the pickups' and markers' lightness and
        /// failed the readability test), violet shadows, a warm rim, a peach cloud sea darkening to violet, the first
        /// stars and a pale moon, lanterns lit at 60%.</summary>
        public static readonly TimeOfDayPreset Dusk = new TimeOfDayPreset
        {
            Id = TimeOfDay.Dusk, Name = "DUSK",
            SunElevationDeg = 26f, SunAzimuthFromScreenUpDeg = 105f,
            Key = Lin(1.06f, 0.94f, 0.86f), Shade = Lin(0.62f, 0.52f, 0.78f), Ambient = Lin(0.012f, 0.006f, 0.020f), CharacterLift = 0.6f,
            Rim = Hex("#FFC08A"), RimStrength = 0.28f,
            Fog = Hex("#E8A58E"), FogMix = 0.9f,
            Void = Hex("#5C4A8A"), VoidAmount = 0.85f,
            Stars = 0.3f, Moon = 0.55f, GlowBoost = 0.6f,
            Background = Hex("#C48A8E"),
            Fireflies = 0.45f, LightPools = 0.6f, CreatureGlow = 0.5f,
        };

        /// <summary>Cool moonlight from the screen's upper right, a dark world with the fighters, the Kaiju, crystals,
        /// team markers and glows kept near their Day value; a starry indigo void with the moon; every lantern lit,
        /// fireflies around the edge trees, glowing spots on the sky creatures.</summary>
        public static readonly TimeOfDayPreset Night = new TimeOfDayPreset
        {
            Id = TimeOfDay.Night, Name = "NIGHT",
            SunElevationDeg = 58f, SunAzimuthFromScreenUpDeg = 215f,
            Key = Lin(0.13f, 0.16f, 0.28f), Shade = Lin(0.078f, 0.096f, 0.17f), Ambient = Lin(0.006f, 0.008f, 0.024f), CharacterLift = 0.95f,
            Rim = Hex("#9DBBFF"), RimStrength = 0.3f,
            Fog = Hex("#1B2552"), FogMix = 1f,
            Void = Hex("#070B22"), VoidAmount = 0.9f,
            Stars = 1f, Moon = 1f, GlowBoost = 1.2f,
            Background = Hex("#0E1430"),
            Fireflies = 1f, LightPools = 1f, CreatureGlow = 1f,
        };

        public static readonly TimeOfDayPreset[] All = { Day, Dusk, Night };

        public static TimeOfDayPreset For(TimeOfDay t) => t == TimeOfDay.Night ? Night : t == TimeOfDay.Dusk ? Dusk : Day;

        static float Azimuth(float a, float b, float t)
        {
            float r = Mathf.LerpAngle(a, b, t) % 360f;
            return r < 0f ? r + 360f : r;
        }

        /// <summary>A blend of two presets (t 0..1); the id and name are b's from t 0.5.</summary>
        public static TimeOfDayPreset Lerp(in TimeOfDayPreset a, in TimeOfDayPreset b, float t)
        {
            t = Mathf.Clamp01(t);
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            return new TimeOfDayPreset
            {
                Id = t < 0.5f ? a.Id : b.Id, Name = t < 0.5f ? a.Name : b.Name,
                SunElevationDeg = Mathf.Lerp(a.SunElevationDeg, b.SunElevationDeg, t),
                SunAzimuthFromScreenUpDeg = Azimuth(a.SunAzimuthFromScreenUpDeg, b.SunAzimuthFromScreenUpDeg, t),
                Key = Color.Lerp(a.Key, b.Key, t), Shade = Color.Lerp(a.Shade, b.Shade, t), Ambient = Color.Lerp(a.Ambient, b.Ambient, t),
                CharacterLift = Mathf.Lerp(a.CharacterLift, b.CharacterLift, t),
                Rim = Color.Lerp(a.Rim, b.Rim, t), RimStrength = Mathf.Lerp(a.RimStrength, b.RimStrength, t),
                Fog = Color.Lerp(a.Fog, b.Fog, t), FogMix = Mathf.Lerp(a.FogMix, b.FogMix, t),
                Void = Color.Lerp(a.Void, b.Void, t), VoidAmount = Mathf.Lerp(a.VoidAmount, b.VoidAmount, t),
                Stars = Mathf.Lerp(a.Stars, b.Stars, t), Moon = Mathf.Lerp(a.Moon, b.Moon, t), GlowBoost = Mathf.Lerp(a.GlowBoost, b.GlowBoost, t),
                Background = Color.Lerp(a.Background, b.Background, t),
                Fireflies = Mathf.Lerp(a.Fireflies, b.Fireflies, t), LightPools = Mathf.Lerp(a.LightPools, b.LightPools, t),
                CreatureGlow = Mathf.Lerp(a.CreatureGlow, b.CreatureGlow, t),
            };
        }
    }

    /// <summary>
    /// The CPU mirror of CMTodGrade (CMTimeOfDay.hlsl), so the readability thresholds are checked by an EditMode test
    /// with the same numbers the GPU uses: linear colour in, linear colour out.
    /// </summary>
    public static class TimeOfDayGrade
    {
        /// <summary>band01: 0 shadow band .. 1 lit band; lift 0 world .. 1 skips the grade (times the preset's lift).</summary>
        public static Color Apply(Color linear, float band01, float lift, in TimeOfDayPreset p)
        {
            float l = Mathf.Clamp01(lift * p.CharacterLift);
            float b = Mathf.Clamp01(band01);
            float Ch(float c, float shade, float key, float amb) => c * Mathf.Lerp(Mathf.Lerp(shade, key, b), 1f, l) + amb * (1f - l);
            return new Color(Ch(linear.r, p.Shade.r, p.Key.r, p.Ambient.r), Ch(linear.g, p.Shade.g, p.Key.g, p.Ambient.g),
                Ch(linear.b, p.Shade.b, p.Key.b, p.Ambient.b), 1f);
        }

        /// <summary>OKLab of a linear colour.</summary>
        public static Vector3 OkLab(Color c)
        {
            float l = 0.4122214708f * c.r + 0.5363325363f * c.g + 0.0514459929f * c.b;
            float m = 0.2119034982f * c.r + 0.6806995451f * c.g + 0.1073969566f * c.b;
            float s = 0.0883024619f * c.r + 0.2817188376f * c.g + 0.6299787005f * c.b;
            l = Mathf.Pow(Mathf.Max(0f, l), 1f / 3f); m = Mathf.Pow(Mathf.Max(0f, m), 1f / 3f); s = Mathf.Pow(Mathf.Max(0f, s), 1f / 3f);
            return new Vector3(0.2104542553f * l + 0.7936177850f * m - 0.0040720468f * s,
                1.9779984951f * l - 2.4285922050f * m + 0.4505937099f * s,
                0.0259040371f * l + 0.7827717662f * m - 0.8086757660f * s);
        }

        /// <summary>Perceptual colour difference (OKLab Euclidean distance; about 0.02 is a just-noticeable step).</summary>
        public static float DeltaE(Color a, Color b) => Vector3.Distance(OkLab(a), OkLab(b));

        /// <summary>WCAG relative-luminance contrast ratio of two linear colours (1 .. 21).</summary>
        public static float Contrast(Color a, Color b)
        {
            float la = 0.2126f * a.r + 0.7152f * a.g + 0.0722f * a.b, lb = 0.2126f * b.r + 0.7152f * b.g + 0.0722f * b.b;
            return (Mathf.Max(la, lb) + 0.05f) / (Mathf.Min(la, lb) + 0.05f);
        }
    }

    /// <summary>The time-of-day choice, stored in PlayerPrefs (default Day), read by the director every frame.</summary>
    public static class TimeOfDaySettings
    {
        public const string Key = "cm.map.timeOfDay";
        public static event Action Changed;

        public static TimeOfDayChoice Choice
        {
            get
            {
                int v = PlayerPrefs.GetInt(Key, 0);
                return v >= 0 && v <= 3 ? (TimeOfDayChoice)v : TimeOfDayChoice.Day;
            }
            set
            {
                PlayerPrefs.SetInt(Key, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        public static void Step(int d)
        {
            int n = ((int)Choice + (d >= 0 ? 1 : -1) + 4) % 4;
            Choice = (TimeOfDayChoice)n;
        }

        public static void Reset()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public static string Label(TimeOfDayChoice c) =>
            c == TimeOfDayChoice.Dusk ? "DUSK" : c == TimeOfDayChoice.Night ? "NIGHT" : c == TimeOfDayChoice.Random ? "RANDOM" : "DAY";

        /// <summary>The time a match shows: the choice, or for Random one picked from the match seed by our own hash (the
        /// Sim's random stream is never touched; the same seed always shows the same time, so a rematch matches).</summary>
        public static TimeOfDay Resolve(TimeOfDayChoice c, int seed)
        {
            if (c != TimeOfDayChoice.Random) return (TimeOfDay)(int)c;
            unchecked
            {
                uint h = (uint)seed * 2654435761u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                return (TimeOfDay)(int)(h % 3u);
            }
        }
    }

    /// <summary>
    /// Applies the time of day: the shader globals of CMTimeOfDay.hlsl, the key light's angle, the camera clear colour,
    /// the moon's place on screen and the night-life intensities (AmbientLife). Snaps at a world build, blends over
    /// BlendSeconds when changed mid-match (Settings or the READY screen). Visual only, nothing reaches the Sim.
    /// Cost: eight SetGlobalVector calls and one rotation per frame (well under 0.01 ms).
    /// </summary>
    [DefaultExecutionOrder(900)]
    [GameOverlay(Order = 75, Scope = OverlayScope.Always)]
    public sealed class TimeOfDayDirector : MonoBehaviour
    {
        public const float BlendSeconds = 0.8f;
        /// <summary>Where the moon sits on screen (viewport) and its angular radius (degrees).</summary>
        public static readonly Vector2 MoonViewport = new Vector2(0.22f, 0.70f);
        public const float MoonRadiusDeg = 3.0f;

        /// <summary>Captures and tests force a time here (null = the player's choice).</summary>
        public static TimeOfDay? Override;
        public static TimeOfDayDirector Current { get; private set; }

        public static readonly int KeyId = Shader.PropertyToID("_CMTodKey"), ShadeId = Shader.PropertyToID("_CMTodShade"),
            AmbientId = Shader.PropertyToID("_CMTodAmbient"), RimId = Shader.PropertyToID("_CMTodRim"), FogId = Shader.PropertyToID("_CMTodFog"),
            VoidId = Shader.PropertyToID("_CMTodVoid"), SkyId = Shader.PropertyToID("_CMTodSky"), MoonId = Shader.PropertyToID("_CMTodMoon"),
            AmbientLifeId = Shader.PropertyToID("_CMAmbient");

        public TimeOfDay Target { get; private set; }
        public TimeOfDayPreset Shown { get; private set; }
        /// <summary>0..1 while a change blends in; 1 when settled.</summary>
        public float Blend => _blend;
        public int Applied { get; private set; }

        SimDriverBehaviour _driver;
        /// <summary>World horizon 1: the sky's zenith colour for CM/WorldHorizon (xyz linear, w 1 when set).</summary>
        static readonly int ZenithId = Shader.PropertyToID("_CMTodZenith");
        Light _sun;
        TimeOfDayPreset _from;
        float _blend = 1f;
        bool _started;
        CriticalMass.Sim.Core.World _world;

        void OnEnable() => Current = this;

        void OnDisable()
        {
            if (Current == this) Current = null;
            ClearGlobals();
        }

        /// <summary>Every global back to 0, which the shaders read as Day.</summary>
        public static void ClearGlobals()
        {
            foreach (int id in new[] { KeyId, ShadeId, AmbientId, RimId, FogId, VoidId, SkyId, MoonId, ZenithId }) Shader.SetGlobalVector(id, Vector4.zero);
            Shader.SetGlobalVector(AmbientLifeId, new Vector4(1f, 0f, 0f, 0f));
        }

        /// <summary>The time the current match (or menu backdrop) should show.</summary>
        public TimeOfDay Wanted()
        {
            if (Override.HasValue) return Override.Value;
            int seed = _driver != null && _driver.World != null ? _driver.World.Opts.Seed : 0;
            return TimeOfDaySettings.Resolve(TimeOfDaySettings.Choice, seed);
        }

        /// <summary>Ends any blend now (captures and tests): the next frame shows the target preset fully.</summary>
        public void SnapToTarget()
        {
            Target = Wanted();
            _from = TimeOfDayPresets.For(Target);
            _blend = 1f;
            _started = true;
            Shown = _from;
            Apply(Shown);
        }

        void LateUpdate()
        {
            if (_driver == null)
            {
                _driver = GetComponent<SimDriverBehaviour>();
                if (_driver == null) _driver = FindAnyObjectByType<SimDriverBehaviour>();
            }
            TimeOfDay want = Wanted();
            bool newWorld = _driver != null && _driver.World != _world;
            if (newWorld) _world = _driver.World;
            if (!_started || want != Target)
            {
                _from = _started ? Shown : TimeOfDayPresets.For(want);
                Target = want;
                // A new world (match start, menus) snaps; a change while it runs blends.
                _blend = !_started || newWorld ? 1f : 0f;
                _started = true;
            }
            if (_blend < 1f) _blend = Mathf.Min(1f, _blend + Time.unscaledDeltaTime / BlendSeconds);
            float s = _blend * _blend * (3f - 2f * _blend);
            Shown = TimeOfDayPresets.Lerp(_from, TimeOfDayPresets.For(Target), s);
            Apply(Shown);
        }

        void Apply(in TimeOfDayPreset p)
        {
            Applied++;
            Shader.SetGlobalVector(KeyId, new Vector4(p.Key.r - 1f, p.Key.g - 1f, p.Key.b - 1f, p.CharacterLift));
            Shader.SetGlobalVector(ShadeId, new Vector4(p.Shade.r - 1f, p.Shade.g - 1f, p.Shade.b - 1f, 0f));
            Shader.SetGlobalVector(AmbientId, new Vector4(p.Ambient.r, p.Ambient.g, p.Ambient.b, 0f));
            Color rim = p.Rim.linear, fog = p.Fog.linear, v = p.Void.linear;
            Shader.SetGlobalVector(RimId, new Vector4(rim.r, rim.g, rim.b, p.RimStrength));
            Shader.SetGlobalVector(FogId, new Vector4(fog.r, fog.g, fog.b, p.FogMix));
            Shader.SetGlobalVector(VoidId, new Vector4(v.r, v.g, v.b, p.VoidAmount));
            Shader.SetGlobalVector(SkyId, new Vector4(p.Stars, p.Moon, p.GlowBoost, Time.time));
            // The eye-level sky's zenith: the preset's background, deepened toward the void colour at dusk and night.
            Color zen = Color.Lerp(p.Background, p.Void, p.VoidAmount * 0.45f * Mathf.Clamp01(p.FogMix)).linear;
            Shader.SetGlobalVector(ZenithId, new Vector4(zen.r, zen.g, zen.b, 1f));
            float motion = CriticalMass.Runtime.Menu.GameSettings.ReducedMotion ? 0.35f : 1f;
            Shader.SetGlobalVector(AmbientLifeId, new Vector4(motion, p.Fireflies, p.LightPools, p.CreatureGlow));

            UnityEngine.Camera cam = UnityEngine.Camera.main;
            if (cam != null)
            {
                if (cam.clearFlags == CameraClearFlags.SolidColor) cam.backgroundColor = p.Background;
                Vector3 md = cam.ViewportPointToRay(new Vector3(MoonViewport.x, MoonViewport.y, 0f)).direction.normalized;
                Shader.SetGlobalVector(MoonId, new Vector4(md.x, md.y, md.z, Mathf.Cos(MoonRadiusDeg * Mathf.Deg2Rad)));
            }

            if (_sun == null) _sun = FindSun();
            if (_sun != null)
            {
                double yaw = _driver != null && _driver.Config != null ? _driver.Config.Render.CameraYawDeg : 45.0;
                _sun.transform.rotation = Quaternion.Euler(p.SunElevationDeg, (float)(360.0 - yaw) + p.SunAzimuthFromScreenUpDeg, 0f);
                // The toon shaders read only the direction and the shadow map; the colour is kept for any other reader.
                _sun.color = new Color(Mathf.Clamp01(p.Key.r), Mathf.Clamp01(p.Key.g), Mathf.Clamp01(p.Key.b), 1f).gamma;
            }
        }

        static Light FindSun()
        {
            if (RenderSettings.sun != null) return RenderSettings.sun;
            foreach (Light l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                if (l.type == LightType.Directional) return l;
            return null;
        }
    }
}
