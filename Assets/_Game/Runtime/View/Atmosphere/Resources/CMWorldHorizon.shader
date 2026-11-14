// CM/WorldHorizon (world horizon 1, 2026-11-14): the sky and the far world for eye-level cameras, in ONE mesh, ONE draw,
// no texture (WorldHorizon.cs). The mesh holds offsets from the camera; the vertex shader adds the camera position (so
// it is a skybox: it never needs a far clip and never moves on the CPU), slides the near bands a little against the
// camera's travel (parallax), drifts the cloud cards, and pins depth to the far plane, so anything the arena or the
// world around it drew stays in front. No depth write: the index order paints dome, far, mid, near band.
// It hides itself (every vertex moved off-screen, so no pixel is shaded) where nobody sees a horizon: when the top of
// the view looks more than 12 degrees down (the top-down gameplay camera tops out about 37 degrees down), on
// orthographic cameras (minimap) and on short-range cameras (far clip under 150 u: menus, close-ups).
// Colours come from the time of day: the horizon is exactly CM/WorldBeyond's fog colour (so the islands melt into it),
// the zenith is _CMTodZenith (TimeOfDayDirector), the sun and moon sit on the key light's bearing at an elevation a
// standing player can see; stars by night; toon cloud banks on the horizon and a three-tone cloud sea below it, all
// animated here (no script). Visual only.
// Follow camera (PR #103): far clip 600 u, FOV 50 to 90, eye height in first person. This layer ignores the far clip
// (depth pinned to the far plane), its far land reads as 260 to 400 u away and beyond the 90 u fighter range, and
// anything the far clip cuts simply reveals this sky, whose horizon colour is the world haze colour, so no edge shows.
Shader "CM/WorldHorizon"
{
    Properties
    {
        _FogColor ("Horizon Colour (Day; same as CM/WorldBeyond)", Color) = (0.705882, 0.854902, 0.933333, 1)
        _Centre ("Arena Centre (world)", Vector) = (56, 0, -36, 0)
        _SeaDepth ("Cloud Sea Depth Below The Eye (u)", Float) = 38
        _CloudScale ("Cloud Sea Scale", Float) = 0.035
        _Wind ("Cloud Wind (u/s, xz)", Vector) = (1.4, 0, 0.6, 0)
        _HideBelowDeg ("Hidden When The View Top Looks Below (deg)", Float) = -12
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Background" "Queue" = "Geometry+450" "PreviewType" = "Skybox" }

        Pass
        {
            Name "Horizon"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Assets/_Game/Art/Shaders/CMTimeOfDay.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float4 _Centre;
                float _SeaDepth;
                float _CloudScale;
                float4 _Wind;
                float _HideBelowDeg;
            CBUFFER_END

            // xyz zenith colour (linear), w 1 when set (TimeOfDayDirector).
            float4 _CMTodZenith;
            // x motion scale, y fireflies, z light pools, w creature glow (0 Day, 0.5 Dusk, 1 Night).
            float4 _CMAmbient;
            float _CMAmbientTime;

            struct A
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv0        : TEXCOORD0;
            };

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 dir        : TEXCOORD0;
                half4  color      : COLOR;
                nointerpolation float kind : TEXCOORD1;
            };

            V Vert(A i)
            {
                V o;
                float3 cam = _WorldSpaceCameraPos;
                float3 rel = i.positionOS.xyz;
                float follow = i.uv0.x, kind = i.uv0.y;
                // Parallax: a band that follows 0.9 of the camera slides a tenth of its travel from the arena centre.
                rel.xz -= (cam.xz - _Centre.xz) * (1.0 - follow);
                if (kind > 1.5 && kind < 2.5)
                {
                    // Cloud banks drift round the horizon, one turn in about half an hour.
                    float a = _CMAmbientTime * 0.0035;
                    float sa = sin(a), ca = cos(a);
                    rel.xz = float2(ca * rel.x - sa * rel.z, sa * rel.x + ca * rel.z);
                }
                float4 cs = TransformWorldToHClip(cam + rel);
                #if UNITY_REVERSED_Z
                    cs.z = cs.w * 1e-5;
                #else
                    cs.z = cs.w * (1.0 - 1e-5);
                #endif
                // Who sees a horizon: the top edge of the view must look above _HideBelowDeg.
                float3 fwd = -UNITY_MATRIX_V[2].xyz;
                float halfV = atan(1.0 / max(1e-3, abs(UNITY_MATRIX_P[1][1])));
                float topElev = asin(clamp(fwd.y, -1.0, 1.0)) + halfV;
                bool hide = unity_OrthoParams.w > 0.5 || _ProjectionParams.z < 150.0 || topElev < radians(_HideBelowDeg);
                o.positionCS = hide ? float4(0.0, 0.0, -2.0, 1.0) : cs;
                o.dir = rel;
                o.color = i.color;
                o.kind = kind;
                return o;
            }

            float HNoise(float2 p)
            {
                float2 c = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = CMTodHash12(c), b = CMTodHash12(c + float2(1, 0)), d = CMTodHash12(c + float2(0, 1)), e = CMTodHash12(c + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(d, e, f.x), f.y);
            }

            half3 HorizonColour() { return lerp(_FogColor.rgb, (half3)_CMTodFog.rgb, (half)_CMTodFog.a); }
            half3 ZenithColour() { return _CMTodZenith.w > 0.5 ? (half3)_CMTodZenith.rgb : SRGBToLinear(half3(0.435, 0.796, 0.918)); }

            // The sky gradient alone (the air the silhouettes sit in): horizon to zenith in soft toon steps.
            half3 SkyAt(float e, half3 H, half3 Z)
            {
                half t = (half)smoothstep(0.0, 0.62, e);
                half q = t * 5.0h;
                t = (floor(q) + smoothstep(0.35h, 0.65h, frac(q))) / 5.0h;
                return lerp(H, Z, t);
            }

            void CloudTones(half3 H, half3 Z, half night, out half3 lit, out half3 mid, out half3 shade)
            {
                lit = lerp(H, half3(1.0h, 1.0h, 1.0h), 0.55h * (1.0h - night)) + H * (0.6h * night);
                mid = lerp(lit, lerp(H, Z, 0.25h), 0.55h);
                shade = lerp(H, Z, 0.55h) * 0.92h;
            }

            half4 Frag(V i) : SV_Target
            {
                float3 vd = normalize(i.dir);
                float e = vd.y;
                half3 H = HorizonColour(), Z = ZenithColour();
                half night = (half)saturate((_CMAmbient.w - 0.6) / 0.3);
                half dusk = (half)saturate(_CMAmbient.w * 2.0) * (1.0h - night);
                half3 lit, mid, shade;
                CloudTones(H, Z, night, lit, mid, shade);

                // Sun and moon on the key light's bearing. The real key is 50 to 60 degrees up (the shadows need it);
                // the disc is drawn no higher than 30 degrees so a standing player can find it. By night the key is the
                // moon; at dusk the moon rises opposite the low sun.
                Light L = GetMainLight();
                float3 l = L.direction;
                float2 hz = normalize(l.xz + float2(1e-4, 0.0));
                float sunEl = min(asin(saturate(l.y)), radians(30.0));
                float3 sunDir = float3(hz.x * cos(sunEl), sin(sunEl), hz.y * cos(sunEl));
                float2 mh = night > 0.5h ? hz : -hz;
                float mEl = radians(night > 0.5h ? 24.0 : 30.0);
                float3 moonDir = float3(mh.x * cos(mEl), sin(mEl), mh.y * cos(mEl));

                half3 col;
                if (i.kind < 0.5)
                {
                    if (e >= 0.0)
                    {
                        col = SkyAt(e, H, Z);
                        // Cloud banks on the horizon: a lumpy band of toon cumulus (seamless round the circle), drifting.
                        float az = atan2(vd.z, vd.x);
                        float2 ring = float2(cos(az), sin(az));
                        float drift = _CMAmbientTime * 0.004;
                        float h = 0.012 + 0.075 * HNoise(ring * 4.0 + drift) * HNoise(ring * 9.0 + 3.1 - drift * 1.7);
                        if (e < h)
                        {
                            half3 c = e > h - 0.008 ? lit : (e > h * 0.45 ? mid : shade);
                            col = lerp(c, H, 0.35h);
                        }
                        else
                        {
                            // Stars by night, fading out toward the horizon haze.
                            if (_CMTodSky.x > 0.0 && e > 0.03)
                            {
                                float2 p = vd.xz / (vd.y + 0.35) * 60.0;
                                float2 cell = floor(p);
                                float hs = CMTodHash12(cell);
                                if (hs > 0.93)
                                {
                                    float2 cc = float2(CMTodHash12(cell + 17.3), CMTodHash12(cell + 41.9)) * 0.7 + 0.15;
                                    float tw = 0.65 + 0.35 * sin(_CMTodSky.w * (1.3 + 2.0 * frac(hs * 91.0)) + hs * 60.0);
                                    float st = saturate(1.0 - length(frac(p) - cc) / 0.09) * tw * smoothstep(0.03, 0.25, e);
                                    col += half3(0.86h, 0.90h, 1.0h) * (half)(st * _CMTodSky.x);
                                }
                            }
                            // The moon: a toon disc with one shade band and two craters, and a soft halo.
                            if (_CMTodSky.y > 0.0)
                            {
                                float d = dot(vd, moonDir);
                                float cosR = cos(radians(3.0)), sinR = sin(radians(3.0));
                                float disc = smoothstep(cosR - 0.00004, cosR + 0.00004, d);
                                float cosH = 1.0 - (1.0 - cosR) * 16.0;
                                float halo = saturate((d - cosH) / (1.0 - cosH));
                                halo = halo * halo * halo * 0.22;
                                float3 tt = normalize(cross(moonDir, float3(0, 1, 0)));
                                float3 bb = cross(tt, moonDir);
                                float2 uv = float2(dot(vd, tt), dot(vd, bb)) / sinR;
                                half sh = step(1.0, length(uv - float2(-0.42, 0.30)) / 1.05);
                                half cr = max(step(length(uv - float2(0.25, -0.18)), 0.22), step(length(uv - float2(-0.30, -0.40)), 0.14));
                                half3 face = lerp(half3(0.96h, 0.95h, 0.88h), half3(0.74h, 0.77h, 0.88h), saturate(sh + cr * 0.6h));
                                col = lerp(col, face, (half)(disc * _CMTodSky.y)) + half3(0.55h, 0.62h, 0.85h) * (half)(halo * (1.0 - disc) * _CMTodSky.y);
                            }
                        }
                    }
                    else
                    {
                        // The cloud sea: a plane _SeaDepth under the eye, two octaves of value noise cut into three tones,
                        // blowing with the wind; its far reach melts into the horizon colour (and the noise calms there,
                        // so the distance never shimmers).
                        float d = -e;
                        float2 q = vd.xz / max(0.015, d) * _SeaDepth + _Wind.xz * _CMAmbientTime;
                        float n = HNoise(q * _CloudScale) * 0.65 + HNoise(q * _CloudScale * 2.7 + 7.3) * 0.35;
                        n = lerp(0.5, n, smoothstep(0.02, 0.12, d));
                        half3 c = n > 0.58 ? lit : (n > 0.44 ? mid : shade);
                        half haze = 1.0h - (half)smoothstep(0.0, 0.18, d);
                        col = lerp(c, H, haze * 0.85h);
                    }
                    // The sun: a warm glow round it, a band of warm light along the horizon on its side at dusk, the disc.
                    half sunVis = 1.0h - night;
                    float ds = dot(vd, sunDir);
                    half3 warm = lerp(half3(1.0h, 0.93h, 0.78h), half3(1.0h, 0.62h, 0.38h), dusk);
                    col += warm * (half)(pow(saturate(ds), 6.0) * 0.30) * sunVis * (0.35h + 0.65h * dusk);
                    float side = saturate(dot(normalize(vd.xz + float2(1e-4, 0.0)), hz) * 0.5 + 0.5);
                    col = lerp(col, warm * H * 1.6h, (half)((1.0 - smoothstep(0.0, 0.14, abs(e))) * side * side * side) * 0.45h * dusk);
                    float sunDisc = step(cos(radians(2.4)), ds);
                    col = lerp(col, lerp(half3(1.0h, 0.97h, 0.86h), half3(1.0h, 0.78h, 0.50h), dusk), (half)sunDisc * sunVis);
                }
                else
                {
                    // Far silhouettes and cloud cards: the tone takes the time-of-day grade, then the air in front of it
                    // (the sky colour at that height) by the band's haze; glows (embers, sky-glass) light up toward night.
                    half3 air = SkyAt(max(0.0, e), H, Z);
                    half haze = i.color.a;
                    if (i.kind > 1.5 && i.kind < 2.5)
                    {
                        col = lerp(shade, lit, i.color.r);
                    }
                    else
                    {
                        half3 tone = SRGBToLinear(i.color.rgb);
                        col = CMTodGrade(tone, 0.5h, 0.0h);
                        if (i.kind > 2.5) col = lerp(col, tone, 0.35h + 0.6h * (half)saturate(_CMAmbient.w));
                    }
                    col = lerp(col, air, haze);
                }
                return half4(col, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
