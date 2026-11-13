// CM/WorldDecal (environment kit v3, 2026-11-12): the kit's ground decals, unlit, no outline, no depth write.
// _Mode 0 = CM_GlowDecal (crystal ground glow: vertex colour, radial alpha from the disc's UV centre, additive), and a
// team decal (vertex alpha code 6) takes _TeamGlowDecal instead of its authored Azure when _SwapTeam is 1 (Crimson).
// _Mode 2 = bases light / egg pool: the vertex colour added with an alpha that falls from _Strength at the centre to 0 at
// the rim along the pool's authored vertex-alpha rings (art refresh 2; was a flat 35% disc).
// _Mode 1 = CM_ShadowDecal (ledge soft shadow on the lower ground: alpha = 1 - UV.v, alpha-blended _ShadowColor).
Shader "CM/WorldDecal"
{
    Properties
    {
        _Mode ("Mode (0 glow additive, 1 ledge shadow, 2 pool)", Range(0, 2)) = 0
        _Strength ("Strength", Range(0, 2)) = 0.6
        _SwapTeam ("Swap team glow (Crimson)", Range(0, 1)) = 0
        _TeamGlowDecal ("Team glow (Crimson)", Color) = (1, 0.454902, 0.380392, 1)
        _ShadowColor ("Ledge shadow colour", Color) = (0.27, 0.40, 0.20, 0.55)
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-50" }
        Pass
        {
            Name "Decal"
            Tags { "LightMode" = "UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Back
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            // Arena night life 1: glows and light pools grow by _CMTodSky.z (0 in Day); the ledge shadow takes the grade.
            #include "CMTimeOfDay.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half _Mode;
                half _Strength;
                half _SwapTeam;
                half4 _TeamGlowDecal;
                half4 _ShadowColor;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END
            struct A { float4 positionOS : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            V Vert(A i) { V o; o.positionCS = TransformObjectToHClip(i.positionOS.xyz); o.color = i.color; o.uv = i.uv; return o; }
            half4 Frag(V i) : SV_Target
            {
                if (_Mode > 1.5)
                {
                    half3 c = i.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                    c = SRGBToLinear(c);
                #endif
                    // Art refresh 2: the pool's vertex alpha carries its falloff rings (centre 3/8, mid 1/8, rim 0); smoothed
                    // and squared it falls to nothing at the rim, added (Blend SrcAlpha One) as soft warm light, not a disc.
                    half k = saturate(i.color.a / 0.375h);
                    k = k * k * (3.0h - 2.0h * k);
                    return half4(c, saturate(k * k * _Strength * (1.0h + (half)_CMTodSky.z)));
                }
                if (_Mode > 0.5) return half4(CMTodGrade(_ShadowColor.rgb, 0.0h, 0.0h), _ShadowColor.a * saturate(1.0 - i.uv.y) * _Strength);
                half3 rgb = i.color.rgb;
                if (_SwapTeam > 0.5 && (int)round(i.color.a * 8.0) == 6) rgb = LinearToSRGB(_TeamGlowDecal.rgb);
            #if !defined(UNITY_COLORSPACE_GAMMA)
                rgb = SRGBToLinear(rgb);
            #endif
                float r = saturate(length(i.uv - 0.5) * 2.0);
                half a = (1.0 - r) * (1.0 - r) * _Strength * (1.0h + (half)_CMTodSky.z);
                return half4(rgb * a, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
