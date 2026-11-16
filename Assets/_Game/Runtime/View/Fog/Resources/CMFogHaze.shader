// Batch 12 item P: fog of war haze (view only). One quad (top-down) or a three-layer band (third / first person) over
// the map; uv = (sim x / map width, sim z / map height) into the 128 x 128 R8 visibility map (FogOfWar). Where a cell is
// unseen the layer blends toward _Haze by the vertex alpha (the layer's opacity), darkening and desaturating what is
// behind; seen cells are fully clear. Transparent, no depth write, depth tested so fighters standing above it stay sharp.
Shader "CM/FogHaze"
{
    Properties
    {
        _FogTex ("Visibility (R8)", 2D) = "white" {}
        _Haze ("Haze tone", Color) = (0.075, 0.09, 0.15, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-50" "IgnoreProjector" = "True" }
        Pass
        {
            Name "FogHaze"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FogTex);
            SAMPLER(sampler_FogTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _FogTex_ST;
                half4 _Haze;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half alpha : TEXCOORD1; };

            V Vert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.alpha = i.color.a;
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                half seen = SAMPLE_TEXTURE2D(_FogTex, sampler_FogTex, i.uv).r;
                half a = (1.0h - seen) * i.alpha;
                return half4(_Haze.rgb, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
