// CM/ReactionStamp (world reactions, 2026-11-12): the four off-screen passes ForceField and GroundPaint draw into their
// render textures with Graphics.ExecuteCommandBuffer, outside any camera. Vertices are written as raw clip positions
// (no camera matrices), and every reader converts a logical uv to the texture row order the same way the core Blit
// full-screen triangle does (UNITY_UV_STARTS_AT_TOP flips v), so writers and readers agree on every graphics API.
//   0 ForceDecay   full-screen quad: the previous force window, shifted when the window scrolled, times _Decay
//   1 ForceStamp   additive: walker brushes (discs pushing outward), expanding rings (radial forces), cones (hits)
//   2 PaintStamp   world-space effect geometry (FxMeshBuilder layout: POSITION + COLOR) into the map-wide paint texture;
//                  colour alpha-blended, A (remaining life) takes the max
//   3 PaintFade    full-screen quad, alpha only: life -= _Step (linear fade; no 8-bit stall)
Shader "CM/ReactionStamp"
{
    Properties
    {
        _Decay ("Decay per frame", Float) = 0.88
        _Shift ("Window shift (logical uv)", Vector) = (0, 0, 0, 0)
        _PaintRect ("Paint rect (sim x0, z0, 1/w, 1/h)", Vector) = (0, 0, 0.01, 0.01)
        _Step ("Life step", Float) = 0.01
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZTest Always
        ZWrite Off
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float _Decay;
            float4 _Shift;
            float4 _PaintRect;
            float _Step;
        CBUFFER_END

        TEXTURE2D(_CMForcePrev);
        SAMPLER(sampler_CMForcePrev);

        float2 CMStampNativeUV(float2 logical)
        {
        #if UNITY_UV_STARTS_AT_TOP
            logical.y = 1.0 - logical.y;
        #endif
            return logical;
        }

        struct QuadA { float4 positionOS : POSITION; };
        struct QuadV { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

        QuadV QuadVert(QuadA i)
        {
            QuadV o;
            o.positionCS = float4(i.positionOS.xy, 0.5, 1.0);
            o.uv = i.positionOS.xy * 0.5 + 0.5;
            return o;
        }
        ENDHLSL

        Pass
        {
            Name "ForceDecay"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex QuadVert
            #pragma fragment Frag
            float4 Frag(QuadV i) : SV_Target
            {
                float2 uv = i.uv + _Shift.xy;
                if (uv.x <= 0.0 || uv.x >= 1.0 || uv.y <= 0.0 || uv.y >= 1.0) return float4(0, 0, 0, 0);
                return SAMPLE_TEXTURE2D_LOD(_CMForcePrev, sampler_CMForcePrev, CMStampNativeUV(uv), 0) * _Decay;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ForceStamp"
            Blend One One
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            // uv0: local position in the stamp (-1..1 on world x / z), ring radius (local units, < 0 = disc), shape
            // (0 disc pushing outward, 1 ring, 2 cone along dir). uv1: dir x, dir z (world), push strength, pressure.
            struct A { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            V Vert(A i)
            {
                V o;
                o.positionCS = float4(i.positionOS.xy, 0.5, 1.0);
                o.uv0 = i.uv0;
                o.uv1 = i.uv1;
                return o;
            }
            float4 Frag(V i) : SV_Target
            {
                float2 l = i.uv0.xy;
                float r = length(l);
                if (r >= 1.0) return float4(0, 0, 0, 0);
                float2 outward = l / max(r, 1e-3);
                float k;
                float2 dir;
                if (i.uv0.w > 1.5)
                {
                    dir = i.uv1.xy;
                    k = (1.0 - r) * saturate(dot(outward, dir) * 0.5 + 0.5);
                }
                else if (i.uv0.w > 0.5)
                {
                    dir = outward;
                    k = saturate(1.0 - abs(r - i.uv0.z) / 0.22);
                }
                else
                {
                    dir = outward;
                    k = 1.0 - r;
                }
                return float4(dir * (k * i.uv1.z), k * i.uv1.w, 0.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PaintStamp"
            Blend SrcAlpha OneMinusSrcAlpha, One One
            BlendOp Add, Max
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            struct A { float4 positionOS : POSITION; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; };
            V Vert(A i)
            {
                V o;
                // FxMeshBuilder writes Unity world positions; the paint covers the map in sim coordinates (z = -world z).
                float2 sim = float2(i.positionOS.x, -i.positionOS.z);
                float2 logical = (sim - _PaintRect.xy) * _PaintRect.zw;
                o.positionCS = float4(logical * 2.0 - 1.0, 0.5, 1.0);
                o.color = i.color;
            #if !defined(UNITY_COLORSPACE_GAMMA)
                o.color.rgb = SRGBToLinear(i.color.rgb);
            #endif
                return o;
            }
            half4 Frag(V i) : SV_Target
            {
                return i.color;
            }
            ENDHLSL
        }

        Pass
        {
            Name "PaintFade"
            ColorMask A
            BlendOp RevSub
            Blend One One
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex QuadVert
            #pragma fragment Frag
            half4 Frag(QuadV i) : SV_Target
            {
                return half4(0, 0, 0, _Step);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
