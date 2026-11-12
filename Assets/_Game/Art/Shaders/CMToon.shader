// CM/Toon (V-01): the one shared flat shader for characters, Kaiju, props and structures (U-008), to the art
// direction of 2026-11-12 (sections 2.2 and 2.3): three tone bands with hard edges on the main light (lit x 1.00, mid
// x 0.84, shadow x 0.66 mixed 12% toward Void Navy, so never black), cast shadows put a pixel on its shadow band,
// top-facing surfaces sit one band lighter, vertex-colour team mask (red channel), hard rim in the team colour
// (characters only: props and structures set _RimStrength 0), and a Void Navy inverted-hull outline of CONSTANT SCREEN
// WIDTH set per material (_OutlinePx at 1080p: 3 characters, 2.5 props, 2 structures). Main light only: no additional
// lights, no ambient probe, no highlights, no probes, no fog. Defaults are the bible core palette (p05); they are
// starting values, tuned only through a U-### entry. Art import (2026-11-12): _ColorMode 1 reads the Blender art's
// vertex colour with its alpha mask codes and team-glow contract (CMToonCommon.hlsl), and the DepthOnly and DepthNormals
// passes put CM/Toon objects into the depth and normals textures.
Shader "CM/Toon"
{
    Properties
    {
        _BaseColor ("Base Color (Warm Cream)", Color) = (0.968627, 0.941176, 0.898039, 1)
        _TeamColor ("Team Color (Azure Team)", Color) = (0.086275, 0.780392, 0.850980, 1)
        _MidMultiplier ("Mid Band Multiplier", Range(0, 1)) = 0.84
        _ShadowMultiplier ("Shadow Band Multiplier", Range(0, 1)) = 0.66
        _ShadowTint ("Shadow Tint (Void Navy)", Color) = (0.094118, 0.137255, 0.231373, 1)
        _ShadowTintAmount ("Shadow Tint Amount", Range(0, 1)) = 0.12
        _LitThreshold ("Lit Threshold (N.L)", Range(-1, 1)) = 0.35
        _ShadowThreshold ("Shadow Threshold (N.L)", Range(-1, 1)) = -0.05
        _BandSoftness ("Band Edge Width (N.L)", Range(0, 0.2)) = 0.02
        _TopBandLift ("Top Face Band Lift", Range(0, 1)) = 1
        _OutlineColor ("Outline Color (Void Navy)", Color) = (0.094118, 0.137255, 0.231373, 1)
        _OutlinePx ("Outline Width (px at 1080p)", Range(0, 8)) = 3
        _OutlineSource ("Outline Direction (0 normal, 1 from pivot, 2 UV3)", Range(0, 2)) = 0
        _RimWidth ("Rim Width", Range(0, 1)) = 0.25
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.6
        _ColorMode ("Colour Source (0 team mask in vertex red, 1 vertex colour + alpha mask codes)", Range(0, 1)) = 0
        _TeamCloth ("Team Cloth (target, code 8)", Color) = (0.164706, 0.584314, 0.682353, 1)
        _TeamGlow ("Team Glow (target, codes 5 6)", Color) = (0.309804, 0.890196, 0.941176, 1)
        _TeamDark ("Team Dark (target, code 4)", Color) = (0.164706, 0.215686, 0.301961, 1)
        _DefaultCloth ("Authored Cloth (code 8)", Color) = (0.164706, 0.584314, 0.682353, 1)
        _DefaultGlow ("Authored Glow (codes 5 6)", Color) = (0.309804, 0.890196, 0.941176, 1)
        _DefaultDark ("Authored Dark (code 4)", Color) = (0.164706, 0.215686, 0.301961, 1)
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

                half3 albedo;
                half selfLit = 0.0h;
                if (_ColorMode > 0.5) CMToonVertexColour(input.color, albedo, selfLit);
                else albedo = lerp(_BaseColor.rgb, _TeamColor.rgb, input.color.r);

                // Three bands from N.L; a cast shadow drops the pixel to the shadow band; a top-facing surface is one
                // band lighter (2.2 bevel rule: tops exactly one band lighter than their sides).
                float ndl = dot(normalWS, mainLight.direction);
                half band = CMToonBand(ndl) * step(0.5, mainLight.shadowAttenuation);
                band = min(2.0h, band + _TopBandLift * smoothstep(0.65, 0.75, normalWS.y));
                half3 lit = CMToonBandColor(albedo, band);

                // Hard rim in the team colour.
                float rim = 1.0 - saturate(dot(normalWS, normalize(viewDirWS)));
                half rimMask = rim > (1.0 - _RimWidth) ? 1.0 : 0.0;
                half3 color = lerp(lit, _TeamColor.rgb, rimMask * _RimStrength);

                // Glows are self-lit: unshaded, no rim.
                color = selfLit > 0.5h ? albedo : color;
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // Pass 1: the inverted hull outline, constant screen width (CMToonOutlinePositionCS).
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
                output.positionCS = CMToonOutlinePositionCS(input);
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

        // Pass 3: depth prepass / _CameraDepthTexture (2026-11-12): without it the object vanishes from depth-based
        // effects (SSAO, occluder dithering that reads depth, soft particles) whenever URP runs a prepass.
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Back
            ZWrite On
            ColorMask R

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex CMToonDepthVertex
            #pragma fragment CMToonDepthFragment

            #include "CMToonCommon.hlsl"

            struct CMToonDepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            CMToonDepthVaryings CMToonDepthVertex(CMToonAttributes input)
            {
                CMToonDepthVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half CMToonDepthFragment(CMToonDepthVaryings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        // Pass 4: depth + world normals for SSAO and other normals-texture effects (2026-11-12).
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex CMToonDepthNormalsVertex
            #pragma fragment CMToonDepthNormalsFragment

            #include "CMToonCommon.hlsl"

            struct CMToonDepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
            };

            CMToonDepthNormalsVaryings CMToonDepthNormalsVertex(CMToonAttributes input)
            {
                CMToonDepthNormalsVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 CMToonDepthNormalsFragment(CMToonDepthNormalsVaryings input) : SV_Target
            {
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
