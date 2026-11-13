// DG-20 see-through silhouette (Runtime/Camera/SeeThrough.cs draws the player's renderers with it while he is covered).
// ZTest Greater: only the pixels of the player that something nearer hides are drawn, as a diagonal two-tone stripe in
// his team colour (_Color) with a Warm Cream rim (_RimColor) on the grazing edges, faded by _Fade. ZWrite Off, so the
// world's depth is untouched. View only; CMToon.shader is not edited.
Shader "CM/CameraSeeThrough"
{
    Properties
    {
        _Color ("Team Colour", Color) = (0.086, 0.78, 0.851, 1)
        _RimColor ("Rim Colour", Color) = (0.969, 0.941, 0.898, 1)
        _Fade ("Fade", Range(0, 1)) = 1
        _StripePx ("Stripe Period (px)", Float) = 8
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent" }
        Pass
        {
            Name "SeeThrough"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            ZTest Greater
            ZWrite Off
            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _RimColor;
                half _Fade;
                float _StripePx;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 viewWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewWS = GetWorldSpaceViewDir(positionWS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 px = input.positionCS.xy;
                half stripe = frac((px.x + px.y) / max(_StripePx, 2.0)) < 0.5 ? 1.0h : 0.72h;
                half ndv = saturate(dot(normalize(input.normalWS), normalize(input.viewWS)));
                half rim = smoothstep(0.55h, 0.85h, 1.0h - ndv);
                half3 c = lerp(_Color.rgb * stripe, _RimColor.rgb, rim);
                half a = (0.6h + 0.35h * rim) * _Fade;
                return half4(c, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
