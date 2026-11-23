// Combat effects (Runtime/View/Effects): flat vertex-colour toon shapes, no lighting. Tone steps and the Void Navy
// outline are baked into the vertex colours by FxMeshBuilder; alpha fades come from vertex alpha. Render state per
// material: _Cull (0 off, 2 back), _ZWrite (0/1), _OffsetUnits (ground decals pull forward to sit on the ground).
// Vertex colours are authored in sRGB and converted to linear here when the project is in linear colour space.
// v2 (effects v2): premultiplied blending (One, OneMinusSrcAlpha). TEXCOORD0.x is a per-vertex additive amount: 0 is a
// normal alpha-blended toon shape, 1 adds the colour (the explosion light pulse), so glow needs no second material.
Shader "CM/FxUnlit"
{
    Properties
    {
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 0
        _OffsetUnits ("Depth Offset", Float) = 0
        _CMVolFade ("Camera Fade Volumes", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "FxUnlit"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull [_Cull]
            Offset [_OffsetUnits], [_OffsetUnits]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Assets/_Game/Art/Shaders/CMFadeVolumes.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Cull;
                float _ZWrite;
                float _OffsetUnits;
                float _CMVolFade;   // fix/px-camera-in-editor: 1 on the Depth Solid layer (groves): the camera's fade volumes apply
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; half4 ext : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; half4 color : COLOR; half add : TEXCOORD0; float3 positionWS : TEXCOORD1; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);   // unchanged maths (top-down pixel-identical)
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.color = i.color;
                o.add = i.ext.x;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                o.color.rgb = SRGBToLinear(i.color.rgb);
                #endif
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_CMVolFade > 0.5) CMFadeVolumesClip(i.positionWS, i.positionCS, 0.0);
                half a = i.color.a;
                // Premultiplied: alpha-blended shapes keep alpha a; additive ones write alpha 0 (dest kept, colour added).
                return half4(i.color.rgb * a, a * (1.0h - i.add));
            }
            ENDHLSL
        }
    }
}
