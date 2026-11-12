// CM/Toon (V-01): the one shared flat shader for characters, Kaiju and props (U-008).
// Two-step light ramp on the main light, vertex-colour team mask (red channel), hard rim in the team colour,
// inverted-hull outline. Main light only: no additional lights, no ambient probe, no highlights, no probes, no fog.
// Defaults are the bible core palette (p05); they are starting values, tuned only through a U-### entry.
Shader "CM/Toon"
{
    Properties
    {
        _BaseColor ("Base Color (Warm Cream)", Color) = (0.968627, 0.941176, 0.898039, 1)
        _ShadeMultiplier ("Shade Multiplier", Range(0, 1)) = 0.62
        _RampThreshold ("Ramp Threshold", Range(-1, 1)) = 0.0
        _TeamColor ("Team Color (Azure Team)", Color) = (0.086275, 0.780392, 0.850980, 1)
        _OutlineColor ("Outline Color (Void Navy)", Color) = (0.094118, 0.137255, 0.231373, 1)
        _OutlineWidth ("Outline Width (object space)", Range(0, 0.1)) = 0.02
        _RimWidth ("Rim Width", Range(0, 1)) = 0.25
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }

        // Pass 0: the lit flat colour.
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex CMToonForwardVertex
            #pragma fragment CMToonForwardFragment

            // Main light shadow attenuation feeds the ramp test.
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "CMToonCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct CMToonForwardVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : COLOR;
            };

            CMToonForwardVaryings CMToonForwardVertex(CMToonAttributes input)
            {
                CMToonForwardVaryings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positionInputs.positionCS;
                output.positionWS = positionInputs.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                return output;
            }

            half4 CMToonForwardFragment(CMToonForwardVaryings input) : SV_Target
            {
                // Handles cascades and screen-space shadows itself.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetCameraPositionWS() - input.positionWS;

                half3 albedo = lerp(_BaseColor.rgb, _TeamColor.rgb, input.color.r);

                // Two flat bands; the main light's shadow attenuation multiplies ndl before the test.
                float ndl = dot(normalWS, mainLight.direction);
                ndl *= mainLight.shadowAttenuation;
                half3 lit = ndl > _RampThreshold ? albedo * mainLight.color : albedo * _ShadeMultiplier;

                // Hard rim in the team colour.
                float rim = 1.0 - saturate(dot(normalWS, normalize(viewDirWS)));
                half rimMask = rim > (1.0 - _RimWidth) ? 1.0 : 0.0;
                half3 color = lerp(lit, _TeamColor.rgb, rimMask * _RimStrength);

                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Pass 1: the inverted hull outline.
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex CMToonOutlineVertex
            #pragma fragment CMToonOutlineFragment

            #include "CMToonCommon.hlsl"

            struct CMToonOutlineVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            CMToonOutlineVaryings CMToonOutlineVertex(CMToonAttributes input)
            {
                CMToonOutlineVaryings output;
                float3 positionOS = input.positionOS.xyz + input.normalOS * _OutlineWidth;
                output.positionCS = TransformObjectToHClip(positionOS);
                return output;
            }

            half4 CMToonOutlineFragment(CMToonOutlineVaryings input) : SV_Target
            {
                return _OutlineColor;
            }
            ENDHLSL
        }

        // Pass 2: shadow casting through URP's own pass.
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

            // Point and spot light shadows use _LightPosition instead of _LightDirection.
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "CMToonCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }

    FallBack Off
}
