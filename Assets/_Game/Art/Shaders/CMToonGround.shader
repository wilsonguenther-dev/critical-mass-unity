// CM/ToonGround: the ground variant of CM/Toon (art direction 2026-11-12, sections 2.2, 2.3 and 2.5).
// One flat lighting band plus a world-space pattern, continuous across chunk meshes so the 1 m Sim grid never shows:
// Voronoi cells (jittered grid of _CellSize, mean about 2.5 u across, 1.5 to 3.5 u range) each painted one of three
// grass tones by hash, 1 px seams in _SeamColor (pixel width from fwidth), and a soft 8 u macro band of +-4 % value.
// Faces that are not top-facing (ledge and island cliff faces) are the flat _CliffColor band. Ground takes cast shadows
// only: a shadowed pixel goes to its shadow band (x _ShadowMultiplier mixed toward Void Navy), never black. No outline
// pass (no hull on ground cells). _PatternStrength 0 turns the pattern off (water and foam use this shader flat).
// Not here yet (named hooks for the environment kit v2): the sawtooth edge fringe and its 4 px ledge ink line, paving
// and dirt patterns, and macro bands aligned to terrain features (these bands are plain world-space noise).
Shader "CM/ToonGround"
{
    Properties
    {
        _GrassLight ("Grass Light", Color) = (0.674510, 0.796078, 0.337255, 1)
        _GrassMid ("Grass Mid", Color) = (0.639216, 0.756863, 0.313725, 1)
        _GrassDark ("Grass Dark", Color) = (0.588235, 0.698039, 0.286275, 1)
        _SeamColor ("Seam", Color) = (0.494118, 0.603922, 0.247059, 1)
        _CliffColor ("Cliff Face (Rock Dark)", Color) = (0.356863, 0.368627, 0.345098, 1)
        _CellSize ("Cell Size (u)", Range(0.5, 8)) = 2.5
        _SeamPx ("Seam Width (px)", Range(0, 4)) = 1
        _MacroPeriod ("Macro Band Period (u)", Range(1, 32)) = 8
        _MacroAmount ("Macro Band Value", Range(0, 0.2)) = 0.04
        _PatternStrength ("Pattern Strength", Range(0, 1)) = 1
        _ShadowMultiplier ("Shadow Band Multiplier", Range(0, 1)) = 0.66
        _ShadowTint ("Shadow Tint (Void Navy)", Color) = (0.094118, 0.137255, 0.231373, 1)
        _ShadowTintAmount ("Shadow Tint Amount", Range(0, 1)) = 0.12
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _GrassLight;
            half4 _GrassMid;
            half4 _GrassDark;
            half4 _SeamColor;
            half4 _CliffColor;
            float _CellSize;
            float _SeamPx;
            float _MacroPeriod;
            half _MacroAmount;
            half _PatternStrength;
            half _ShadowMultiplier;
            half4 _ShadowTint;
            half _ShadowTintAmount;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex GroundVertex
            #pragma fragment GroundFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            // Arena night life 1: the time-of-day grade (all globals 0 = Day).
            #include "CMTimeOfDay.hlsl"

            struct GroundAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct GroundVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            GroundVaryings GroundVertex(GroundAttributes input)
            {
                GroundVaryings o;
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return o;
            }

            float2 GroundHash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453);
            }

            float GroundHash1(float2 p)
            {
                return frac(sin(dot(p, float2(41.3, 289.1))) * 15731.743);
            }

            // Voronoi on the ground plane: F1, F2 distance (F2 - F1 is the distance to the nearest border, x2) and the
            // nearest cell id. Jitter 0.8 of the spacing keeps cells between about 0.6x and 1.4x the spacing.
            void GroundVoronoi(float2 p, out float edge, out float2 cellId)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                cellId = i;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y);
                    float2 o = 0.1 + 0.8 * GroundHash2(i + g);
                    float2 r = g + o - f;
                    float d = dot(r, r);
                    if (d < d1) { d2 = d1; d1 = d; cellId = i + g; }
                    else if (d < d2) { d2 = d; }
                }
                edge = sqrt(d2) - sqrt(d1);
            }

            float GroundValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = GroundHash1(i), b = GroundHash1(i + float2(1, 0));
                float c = GroundHash1(i + float2(0, 1)), d = GroundHash1(i + float2(1, 1));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            half4 GroundFragment(GroundVaryings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                float2 uv = input.positionWS.xz / _CellSize;

                float edge;
                float2 cellId;
                GroundVoronoi(uv, edge, cellId);
                float pick = GroundHash1(cellId + 17.0);
                half3 tone = pick < 0.3333 ? _GrassLight.rgb : (pick < 0.6667 ? _GrassMid.rgb : _GrassDark.rgb);
                // Seam: edge / fwidth(edge) is about the distance to the cell border in pixels, so a seam _SeamPx wide in
                // total is where that distance is under half of it. Hard edge.
                float px = max(fwidth(edge), 1e-5);
                half seam = 1.0h - step(_SeamPx * 0.5, edge / px);
                tone = lerp(tone, _SeamColor.rgb, seam);
                half macro = (GroundValueNoise(input.positionWS.xz / _MacroPeriod) * 2.0h - 1.0h) * _MacroAmount;
                tone *= 1.0h + macro;
                tone = lerp(_GrassMid.rgb, tone, _PatternStrength);

                half top = step(0.5, normalWS.y);
                half3 albedo = lerp(_CliffColor.rgb, tone, top);

                // One band; cast shadows only.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half shadowed = 1.0h - step(0.5, mainLight.shadowAttenuation);
                half3 shadow = lerp(albedo * _ShadowMultiplier, _ShadowTint.rgb, _ShadowTintAmount);
                return half4(CMTodGrade(lerp(albedo, shadow, shadowed), 1.0h - shadowed, 0.0h), 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
