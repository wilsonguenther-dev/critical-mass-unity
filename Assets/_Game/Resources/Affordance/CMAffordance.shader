// CM/Affordance (feat/terrain-affordances): the terrain markers, the live-target outline, the placement ghost, the damage
// crack decals and the hit chips, all drawn by TerrainAffordanceView as ONE dynamic mesh with ONE material (one draw
// call). uv0.xy = atlas uv (a procedural 4 x 3 atlas the view bakes once: tiles, outlines, icons, cracks, disc, ring);
// the vertex colour carries the per-action colour and the opacity (quiet at rest, brighter in reach). Unlit,
// alpha-blended, no depth write, depth-tested so fighters, walls and the Kaiju hide what lies under them (markers sit
// under fighters, never over them), offset toward the camera so ground quads never fight the floor. Lives under
// Resources so a player build includes it.
Shader "CM/Affordance"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-45" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Affordance"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -2, -2
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half _Opacity;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half4 color : COLOR; };

            V Vert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.uv = i.uv;
                o.color = i.color;
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                // The atlas stores coverage in alpha and a two-tone mask in red (1 = colour, 0 = the Void Navy edge line).
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half3 navy = half3(0.094, 0.137, 0.231);
                half3 rgb = lerp(navy, i.color.rgb, t.r);
                return half4(rgb, t.a * i.color.a * _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
