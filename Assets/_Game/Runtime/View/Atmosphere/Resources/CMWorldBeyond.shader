// CM/WorldBeyond (world around arena 1, 2026-11-13): the islands, landmarks, roots and waterfalls around and below the
// play island, all in ONE static mesh (WorldBeyond.cs). Vertex colour is the albedo (sRGB; alpha 1 = self-lit glow:
// sky-glass crystals, homeland windows, embers). UV0: water ribbon (u across, v metres down). UV1: x depth band 0..1
// (atmospheric fade toward the void colour), y team (0 none, 1 Azure, 2 Crimson: a soft team-tinted light on the
// homelands), z flag (0 solid, 1 water, 2 root), w sway weight (roots swing a few centimetres at the tip). UV2: the
// smoothed outline normal (the ink hull follows it, so flat-faced boxes do not tear).
// Toon: three hard bands on the main light (bible), the time-of-day grade, the moon / low-sun rim, the world's height
// fog plus the band fade, so the far rings sink into the cloud sea and the play surface stays the highest-contrast
// layer. Glows stay self-lit and pierce the fog at night. Ink: constant 1 px at 1080p, mixed toward the fog with depth
// (environment brief: far background 1.0 px, 50% toward the horizon colour); never on water. The hull is drawn with
// Cull Off and pushed a hair back in depth, so it can only ever show around silhouettes, whatever the winding.
// No shadow caster and no depth passes (nothing reads them; the void must never shadow the play island). Visual only.
Shader "CM/WorldBeyond"
{
    Properties
    {
        _FogColor ("Height Fog Colour", Color) = (0.705882, 0.854902, 0.933333, 1)
        _FogTop ("Height Fog Top (y)", Float) = -2.5
        _FogBottom ("Height Fog Full (y)", Float) = -12
        _FogAmount ("Height Fog Amount", Range(0, 1)) = 0.78
        _BandFade ("Band Fade (at band 1)", Range(0, 1)) = 0.62
        _NearFade ("Haze on the near band", Range(0, 1)) = 0.14
        _DistStart ("Distance Haze Start (u)", Float) = 45
        _DistRange ("Distance Haze Range (u)", Float) = 170
        _DistFade ("Distance Haze Amount", Range(0, 1)) = 0.72
        _TeamTint ("Team Light Strength", Range(0, 1)) = 0.22
        _OutlinePx ("Outline Width (px at 1080p)", Range(0, 4)) = 1.0
        _OutlineColor ("Outline Colour (Void Navy)", Color) = (0.094118, 0.137255, 0.231373, 1)
        _AzureColor ("Azure", Color) = (0.086275, 0.780392, 0.85098, 1)
        _CrimsonColor ("Crimson", Color) = (1, 0.352941, 0.372549, 1)
        _FoamColor ("Foam", Color) = (0.968627, 0.976471, 0.964706, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+8" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Assets/_Game/Art/Shaders/CMTimeOfDay.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _FogColor;
            float _FogTop;
            float _FogBottom;
            half _FogAmount;
            half _BandFade;
            half _NearFade;
            float _DistStart;
            float _DistRange;
            half _DistFade;
            half _TeamTint;
            float _OutlinePx;
            half4 _OutlineColor;
            half4 _AzureColor;
            half4 _CrimsonColor;
            half4 _FoamColor;
        CBUFFER_END

        // x motion scale, y fireflies, z light pools, w creature glow (0 Day, 0.5 Dusk, 1 Night; TimeOfDayDirector).
        float4 _CMAmbient;
        float _CMAmbientTime;
        // Captures and tuning: x band fade scale (0 = the material value), y team tint scale (0 = material value).
        float4 _CMBeyondTune;

        struct BeyondAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
            float2 uv0        : TEXCOORD0;
            float4 uv1        : TEXCOORD1;
            float3 uv2        : TEXCOORD2;
        };

        float3 BeyondPosition(BeyondAttributes i)
        {
            float3 p = TransformObjectToWorld(i.positionOS.xyz);
            float sway = i.uv1.w;
            if (sway > 0.0)
            {
                float t = _CMAmbientTime;
                float ph = p.x * 0.7 + p.z * 0.45;
                p.x += sin(t * 0.8 + ph) * 0.10 * sway;
                p.z += cos(t * 0.65 + ph * 1.3) * 0.08 * sway;
            }
            return p;
        }

        // How much of the void colour replaces the surface: height fog plus the depth band.
        half BeyondFog(float3 positionWS, half band, half glow)
        {
            float y = positionWS.y;
            half bandFade = _BandFade * (_CMBeyondTune.x > 0.0 ? (half)_CMBeyondTune.x : 1.0h);
            half f = _FogAmount * saturate((_FogTop - y) / max(1e-3, _FogTop - _FogBottom));
            f = 1.0h - (1.0h - f) * (1.0h - saturate(band * bandFade + _NearFade));   // pass 2: even the near band sits in a little air
            // Glows pierce the haze at night (they are lights).
            // Distance haze for the low ground camera (it looks along the horizon, where height fog alone is thin). Zero at
            // gameplay-camera distances (under 45 u), so the top-down look is unchanged; the far rings melt into the sky.
            half dist = (half)saturate((distance(positionWS, _WorldSpaceCameraPos) - _DistStart) / max(1.0, _DistRange));
            f = 1.0h - (1.0h - f) * (1.0h - dist * _DistFade);
            f *= 1.0h - glow * saturate((half)_CMAmbient.w) * 0.65h;
            return min(f, 0.94h);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                nointerpolation float4 data : TEXCOORD3;   // band, team, flag, unused
                half4  color      : COLOR;
            };

            V Vert(BeyondAttributes i)
            {
                V o;
                o.positionWS = BeyondPosition(i);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv0;
                o.data = float4(i.uv1.xyz, 0.0);
                o.color = i.color;
                return o;
            }

            half4 Frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                half band = (half)i.data.x;
                float team = i.data.y, flag = i.data.z;
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                Light L = GetMainLight();
                half3 albedo = SRGBToLinear(i.color.rgb);
                half glow = step(0.5h, i.color.a);
                half3 col;
                if (flag > 0.5 && flag < 1.5)
                {
                    // Water: three scrolling streak tones and a foam fleck, 2 u/s down the ribbon (bible waterfalls).
                    float s = frac(i.uv.y * 0.55 - _CMAmbientTime * 0.9 + sin(i.uv.x * 6.0) * 0.08);
                    half3 w = albedo * (s < 0.33 ? 1.0h : s < 0.66 ? 0.88h : 1.08h);
                    half foam = step(0.9, s) * step(0.2, frac(i.uv.x * 3.0 + i.uv.y * 0.1));
                    w = lerp(w, SRGBToLinear(_FoamColor.rgb), foam * 0.8h);
                    col = CMTodGrade(w, 1.0h, 0.0h);
                }
                else
                {
                    half ndl = dot(n, L.direction);
                    half bandL = step(-0.05h, ndl) + step(0.35h, ndl);   // 0 shadow, 1 mid, 2 lit (bible thresholds)
                    half3 shadowTone = lerp(albedo * 0.66h, SRGBToLinear(half3(0.094, 0.137, 0.231)), 0.12h);
                    col = bandL >= 2.0h ? albedo : bandL >= 1.0h ? albedo * 0.84h : shadowTone;
                    // Top faces one band lighter at the 55 degree camera.
                    col *= 1.0h + 0.06h * step(0.8h, (half)n.y);
                    col = CMTodGrade(col, bandL * 0.5h, 0.0h);
                    float3 viewDir = _WorldSpaceCameraPos - i.positionWS;
                    col = CMTodRim(col, n, viewDir, L.direction, 0.0h);
                    if (team > 0.5)
                    {
                        // The homelands stand in their banner's light: a soft wash on the lit side, stronger at dusk and night.
                        half3 tc = SRGBToLinear(team < 1.5 ? _AzureColor.rgb : _CrimsonColor.rgb);
                        half k = _TeamTint * (_CMBeyondTune.y > 0.0 ? (half)_CMBeyondTune.y : 1.0h) * (0.6h + 0.4h * saturate((half)_CMAmbient.w)) * (0.5h + 0.25h * bandL);
                        col = lerp(col, col * 0.4h + tc * max(0.08h, Luminance(col)) * 1.6h, saturate(k));
                    }
                    // Glows: self-lit, unshaded (bible), at full strength from dusk on.
                    col = lerp(col, albedo * (1.0h + 0.25h * saturate((half)_CMAmbient.w)), glow);
                }
                half f = BeyondFog(i.positionWS, band, glow);
                if (f > 0.0h) col = lerp(col, CMTodFogColor(_FogColor.rgb, i.positionWS, min(f, 0.9h)), f);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                nointerpolation float band : TEXCOORD1;
            };

            V Vert(BeyondAttributes i)
            {
                V o;
                o.positionWS = BeyondPosition(i);
                float4 cs = TransformWorldToHClip(o.positionWS);
                float3 n = TransformObjectToWorldNormal(i.uv2);
                float2 d = mul((float3x3)GetWorldToHClipMatrix(), n).xy;
                float len = length(d);
                float px = (i.uv1.z > 0.5 && i.uv1.z < 1.5) ? 0.0 : _OutlinePx;   // no ink on water
                if (len > 1e-6)
                {
                    float2 off = d / len * (px * 2.0 / 1080.0);
                    off.x *= _ScreenParams.y / _ScreenParams.x;
                    cs.xy += off * cs.w;
                }
                #if UNITY_REVERSED_Z
                    cs.z -= 0.0008 * cs.w;
                #else
                    cs.z += 0.0008 * cs.w;
                #endif
                o.positionCS = cs;
                o.band = i.uv1.x;
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                half f = BeyondFog(i.positionWS, (half)i.band, 0.0h);
                half3 fog = CMTodFogColor(_FogColor.rgb, i.positionWS, 0.0h);
                return half4(lerp(_OutlineColor.rgb, fog, saturate(0.6h + f * 0.4h)), 1.0);   // pass 2: softer ink, the arena keeps the strongest line
            }
            ENDHLSL
        }
    }

    FallBack Off
}
