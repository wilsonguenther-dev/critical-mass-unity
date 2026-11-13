// CM/AmbientSprite (arena night life 1, 2026-11-13): the soft ambient sprites, one combined mesh per material and
// animated entirely here (one draw each, no CPU per frame). Corner in POSITION.xy (-1..1), UV1 (seed, kind, a, b),
// UV2 (anchor world x, y, z, size), UV3 (kind parameters). Kinds:
//   0 cloud wisp: a flat toon puff lying in the void below the islands, drifting +x (UV3: wrap min x, wrap range,
//     speed u/s, unused); top and underside tones of the bible's cloud sea (#F7F9F6 / #BFD6E2), graded and fogged.
//   1 waterfall mist: a camera-facing puff rising and fading below a waterfall lip (cycle 4 s).
//   2 firefly: a 2 to 3 px warm dot with a faint halo wandering around an edge tree; blinks. x _CMAmbient.y.
//   3 lantern halo: a warm glow at a lantern or torch head; flickers. x _CMAmbient.z.
//   4 light pool: a warm ground pool under a lantern (flat, soft). x _CMAmbient.z.
// Alpha-blended material: kinds 0 and 1; additive material: kinds 2, 3 and 4 (_SrcBlend / _DstBlend). No outline
// (ink is for solids, never light). Motion time: _CMAmbientTime (AmbientLife.Clock). Visual only.
Shader "CM/AmbientSprite"
{
    Properties
    {
        _FogColor ("Height Fog Colour", Color) = (0.705882, 0.854902, 0.933333, 1)
        _FogTop ("Height Fog Top (y)", Float) = -3.5
        _FogBottom ("Height Fog Full (y)", Float) = -12
        _WarmColor ("Warm Light", Color) = (1, 0.784314, 0.470588, 1)
        _FireflyColor ("Firefly", Color) = (1, 0.92549, 0.658824, 1)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-40" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Sprite"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Assets/_Game/Art/Shaders/CMTimeOfDay.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _FogColor;
                float _FogTop;
                float _FogBottom;
                half4 _WarmColor;
                half4 _FireflyColor;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            float4 _CMAmbient;
            float _CMAmbientTime;

            struct A
            {
                float4 positionOS : POSITION;
                float4 uv1 : TEXCOORD1;
                float4 uv2 : TEXCOORD2;
                float4 uv3 : TEXCOORD3;
            };

            struct V
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                nointerpolation float4 data : TEXCOORD2;   // kind, intensity, seed, unused
            };

            V Vert(A i)
            {
                V o;
                float t = _CMAmbientTime;
                float kind = i.uv1.y, seed = i.uv1.x;
                float3 P = i.uv2.xyz;
                float size = i.uv2.w;
                float2 q = i.positionOS.xy;
                float intensity = 1.0;
                float3 right = UNITY_MATRIX_V[0].xyz, up = UNITY_MATRIX_V[1].xyz;
                float3 w;
                if (kind < 0.5)
                {
                    // Cloud wisp: flat, drifting +x, wrapping over the map with a fade at both ends.
                    float u = frac((P.x - i.uv3.x + t * i.uv3.z) / max(1.0, i.uv3.y));
                    P.x = i.uv3.x + u * i.uv3.y;
                    intensity = saturate(min(u, 1.0 - u) * i.uv3.y / 14.0);
                    w = P + float3(q.x * size, 0.0, q.y * size * 0.62);
                }
                else if (kind < 1.5)
                {
                    // Mist: rises 1.8 u and grows while it fades, a 4 s cycle per puff.
                    float c = frac(t * 0.25 + seed);
                    P += float3(sin(seed * 50.0) * 0.4, c * 1.8, cos(seed * 70.0) * 0.4);
                    float s = size * (0.6 + c * 0.8);
                    intensity = sin(3.14159 * c) * 0.55;
                    w = P + (right * q.x + up * q.y) * s;
                }
                else if (kind < 2.5)
                {
                    // Firefly: wanders around its anchor, blinks.
                    P += float3(sin(t * 0.37 + seed * 7.0) * 0.9, sin(t * 0.53 + seed * 3.0) * 0.35, cos(t * 0.29 + seed * 5.0) * 0.9);
                    float b = sin(t * (0.9 + frac(seed * 13.0)) + seed * 20.0) * 0.5 + 0.5;
                    intensity = smoothstep(0.25, 0.9, b) * _CMAmbient.y;
                    w = P + (right * q.x + up * q.y) * size;
                }
                else if (kind < 3.5)
                {
                    // Lantern halo: flickers a little.
                    intensity = (0.85 + 0.15 * sin(t * 7.0 + seed * 30.0)) * _CMAmbient.z;
                    w = P + (right * q.x + up * q.y) * size;
                }
                else
                {
                    // Light pool on the ground under a lantern.
                    intensity = (0.92 + 0.08 * sin(t * 5.0 + seed * 30.0)) * _CMAmbient.z;
                    w = P + float3(q.x * size, 0.02, q.y * size);
                }
                // Nothing to draw: collapse the quad (no fill cost in Day for the night-only kinds).
                if (intensity <= 0.001) w = P;
                o.positionWS = w;
                o.positionCS = TransformWorldToHClip(w);
                o.uv = q;
                o.data = float4(kind, intensity, seed, 0.0);
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                float kind = i.data.x, k = i.data.y;
                float2 q = i.uv;
                float r = length(q);
                if (kind < 0.5)
                {
                    // Toon puff: the union of four discs, a hard edge, the lower part in the underside tone.
                    float d = min(min(length(q - float2(-0.45, 0.05)) - 0.48, length(q - float2(0.08, 0.18)) - 0.58),
                                  min(length(q - float2(0.52, -0.02)) - 0.42, length(q - float2(0.0, -0.22)) - 0.50));
                    half inside = saturate(-d * 40.0);
                    half under = step(q.y, -0.30 + 0.12 * sin(q.x * 5.0));
                    half3 c = SRGBToLinear(lerp(half3(0.969, 0.976, 0.965), half3(0.749, 0.839, 0.886), under));
                    c = CMTodGrade(c, under > 0.5h ? 0.5h : 1.0h, 0.0h);
                    float f = saturate((_FogTop - i.positionWS.y) / max(1e-3, _FogTop - _FogBottom)) * 0.75;
                    c = lerp(c, CMTodFogColor(_FogColor.rgb, i.positionWS, 0.0h), f);
                    return half4(c, inside * 0.9 * k);
                }
                if (kind < 1.5)
                {
                    half a = saturate(1.0 - r);
                    half3 c = CMTodGrade(SRGBToLinear(half3(0.95, 0.97, 0.98)), 1.0h, 0.0h);
                    return half4(c, a * a * k);
                }
                if (kind < 2.5)
                {
                    // Core about a third of the quad, faint halo to the rim.
                    half core = saturate((0.32 - r) * 12.0);
                    half halo = saturate(1.0 - r);
                    return half4(_FireflyColor.rgb, saturate(core + halo * halo * 0.35) * k);
                }
                half fall = saturate(1.0 - r);
                fall = fall * fall;
                return half4(_WarmColor.rgb, fall * (kind < 3.5 ? 0.75 : 0.32) * k);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
