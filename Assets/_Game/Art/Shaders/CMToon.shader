// CM/Toon (V-01): the one shared flat shader for characters, Kaiju, props and structures (U-008), to the art
// direction of 2026-11-12 (sections 2.2 and 2.3): three tone bands with hard edges on the main light (lit x 1.00, mid
// x 0.84, shadow x 0.66 mixed 12% toward Void Navy, so never black), cast shadows put a pixel on its shadow band,
// top-facing surfaces sit one band lighter, vertex-colour team mask (red channel), hard rim in the team colour
// (characters only: props and structures set _RimStrength 0), and a Void Navy inverted-hull outline of CONSTANT SCREEN
// WIDTH set per material (_OutlinePx at 1080p: 3 characters, 2.5 props, 2 structures). Main light only: no additional
// lights, no ambient probe, no highlights, no probes, no fog. Defaults are the bible core palette (p05); they are
// starting values, tuned only through a U-### entry. Art import (2026-11-12): _ColorMode 1 reads the Blender art's
// vertex colour with its alpha mask codes and team-glow contract (CMToonCommon.hlsl), and the DepthOnly and DepthNormals
// passes put CM/Toon objects into the depth and normals textures. Art refresh 2: face decal (_DecalOn, _FaceTex on
// UV0), Kaiju _Berserk darkening of fixed colours, per-vertex outline weight (UV4.x) so faces carry no hull.
// Arena night life 1 (2026-11-13): the lit pass takes the global time-of-day grade (CMTimeOfDay.hlsl; all 0 = Day);
// self-lit glows (mask codes 1, 2, 3, 5, 6, 7) never do, so team markers and glows read the same at every time of day.
// World reactions (2026-11-12): every pass (lit, outline, shadow caster, depth, depth normals) places the vertex with the
// same CMToonWorldPos (CMToonCommon.hlsl: wind + force-field sway from UV5 when _SwayOn, UV4.y wobble when _WobbleUV4Y),
// so the bent silhouette, its outline hull and its shadow agree; the shadow caster has its own vertex for that reason.
// Shader model 3.0 for the vertex texture fetch of the force field.
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
        _OutlineSource ("Outline Direction (0 normal, 1 from pivot, 2 UV3, 3 tangent)", Range(0, 3)) = 0
        _RimWidth ("Rim Width", Range(0, 1)) = 0.25
        _RimStrength ("Rim Strength", Range(0, 1)) = 0.6
        _ColorMode ("Colour Source (0 team mask in vertex red, 1 vertex colour + alpha mask codes)", Range(0, 1)) = 0
        _TeamCloth ("Team Cloth (target, code 8)", Color) = (0.164706, 0.584314, 0.682353, 1)
        _TeamGlow ("Team Glow (target, codes 5 6)", Color) = (0.309804, 0.890196, 0.941176, 1)
        _TeamDark ("Team Dark (target, code 4)", Color) = (0.164706, 0.215686, 0.301961, 1)
        _DefaultCloth ("Authored Cloth (code 8)", Color) = (0.164706, 0.584314, 0.682353, 1)
        _DefaultGlow ("Authored Glow (codes 5 6)", Color) = (0.309804, 0.890196, 0.941176, 1)
        _DefaultDark ("Authored Dark (code 4)", Color) = (0.164706, 0.215686, 0.301961, 1)
        _TeamMarker ("Team Marker Light (target, code 3)", Color) = (0.152941, 0.972549, 0.972549, 1)
        _TeamMarkerDark ("Team Marker Dark (target, code 7)", Color) = (0.262745, 0.760784, 0.772549, 1)
        _DefaultMarker ("Authored Marker Light (code 3)", Color) = (0.152941, 0.972549, 0.972549, 1)
        _DefaultMarkerDark ("Authored Marker Dark (code 7)", Color) = (0.262745, 0.760784, 0.772549, 1)
        _LitMultiplier ("Lit Band Multiplier", Range(0, 3)) = 1
        _MidRatio ("Mid Band Ratio (per channel)", Color) = (1, 1, 1, 1)
        _ShadowRatio ("Shadow Band Ratio (per channel)", Color) = (1, 1, 1, 1)
        _ReceiveShadows ("Cast Shadows Drop The Band", Range(0, 1)) = 1
        _FogColor ("Height Fog Colour", Color) = (0.705882, 0.854902, 0.933333, 1)
        _FogTop ("Height Fog Top (y)", Float) = -3.5
        _FogBottom ("Height Fog Full (y)", Float) = -10
        _FogAmount ("Height Fog Amount", Range(0, 1)) = 0
        _ExtrudePx ("UV2 Extrusion (px at 1080p, foam)", Range(0, 32)) = 0
        [Toggle] _DecalOn ("Face Decal (UV0, alpha mix)", Float) = 0
        [NoScaleOffset] _FaceTex ("Face Decal", 2D) = "black" {}
        _Berserk ("Berserk (fixed colours darken)", Range(0, 1)) = 0
        _BerserkMul ("Berserk Multiplier (sRGB)", Vector) = (0.82, 0.56, 0.78, 1)
        _BerserkTint ("Berserk Tint (Corruption)", Color) = (0.556863, 0.247059, 0.819608, 1)
        _BerserkTintAmount ("Berserk Tint Amount", Range(0, 1)) = 0.15
        [Toggle] _OutlineWeightUV4 ("Outline Weight From UV4.x", Float) = 0
        [Toggle] _SwayOn ("World Sway From UV5 (pivot xz, weight, kind)", Float) = 0
        _WaterFlow ("Water Flow (stripes and ripple rings; 0 off)", Range(0, 2)) = 0
        [Toggle] _WobbleUV4Y ("Wobble Weight From UV4.y (hair, scarf, tabard)", Float) = 0
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
            #pragma target 3.0
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
                float2 uv0        : TEXCOORD2;
                half4  color      : COLOR;
            };

            CMToonForwardVaryings CMToonForwardVertex(CMToonAttributes input)
            {
                CMToonForwardVaryings output;
                output.positionCS = CMToonPositionCS(input);
                output.positionWS = CMToonWorldPos(input);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.uv0 = _DecalOn > 0.5 ? input.uv0 : float2(0, 0);
                return output;
            }

            half4 CMToonForwardFragment(CMToonForwardVaryings input) : SV_Target
            {
                CMToonFadeClip(input.positionWS, input.positionCS);   // follow-camera-2: low-view see-through dither
                // Handles cascades and screen-space shadows itself.
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = GetCameraPositionWS() - input.positionWS;

                half3 albedo;
                half selfLit = 0.0h;
                if (_ColorMode > 0.5) CMToonVertexColour(input.color, albedo, selfLit);
                else albedo = lerp(_BaseColor.rgb, _TeamColor.rgb, input.color.r);
                albedo = CMToonDecal(albedo, input.uv0);

                // Three bands from N.L; a cast shadow drops the pixel to the shadow band; a top-facing surface is one
                // band lighter (2.2 bevel rule: tops exactly one band lighter than their sides).
                float ndl = dot(normalWS, mainLight.direction);
                half band = CMToonBand(ndl) * lerp(1.0h, step(0.5, mainLight.shadowAttenuation), _ReceiveShadows);
                band = min(2.0h, band + _TopBandLift * smoothstep(0.65, 0.75, normalWS.y));
                half3 lit = CMToonBandColor(albedo, band);
                // Time of day (CMTimeOfDay.hlsl): key and shadow tint by band, sky fill, moon rim on world solids.
                half todLift = CMToonTodLift();
                lit = CMTodGrade(lit, band * 0.5h, todLift);
                lit = CMTodRim(lit, normalWS, viewDirWS, mainLight.direction, todLift);

                // Hard rim in the team colour.
                float rim = 1.0 - saturate(dot(normalWS, normalize(viewDirWS)));
                half rimMask = rim > (1.0 - _RimWidth) ? 1.0 : 0.0;
                half3 color = lerp(lit, _TeamColor.rgb, rimMask * _RimStrength);

                // Glows are self-lit: unshaded, no rim.
                color = selfLit > 0.5h ? albedo : color;
                color = CMToonWater(color, input.positionWS, normalWS);
                return half4(CMToonFog(color, input.positionWS), 1.0);
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
            #pragma target 3.0
            #pragma vertex CMToonOutlineVertex
            #pragma fragment CMToonOutlineFragment

            #include "CMToonCommon.hlsl"

            struct CMToonOutlineVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CMToonOutlineVaryings CMToonOutlineVertex(CMToonAttributes input)
            {
                CMToonOutlineVaryings output;
                output.positionCS = CMToonOutlinePositionCS(input);
                output.positionWS = CMToonWorldPos(input);
                return output;
            }

            half4 CMToonOutlineFragment(CMToonOutlineVaryings input) : SV_Target
            {
                CMToonFadeClip(input.positionWS, input.positionCS);
                return _OutlineColor;
            }
            ENDHLSL
        }

        // Pass 2: shadow casting. URP's ShadowCasterPass.hlsl supplies _LightDirection / _LightPosition and the bias
        // helpers; the vertex is our own so the shadow is cast from the same swayed position as the lit pass (URP's
        // ShadowPassVertex reads the undisplaced POSITION and the shadow would stay still under a bending bush).
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            Cull Back
            ZWrite On
            ZTest LEqual
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex CMToonShadowVertex
            #pragma fragment CMToonShadowFragment

            // Point and spot light shadows use _LightPosition instead of _LightDirection.
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "CMToonCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

            struct CMToonShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            CMToonShadowVaryings CMToonShadowVertex(CMToonAttributes input)
            {
                CMToonShadowVaryings output;
                float3 positionWS = CMToonWorldPos(input);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                output.positionCS = ApplyShadowClamping(positionCS);
                return output;
            }

            half4 CMToonShadowFragment(CMToonShadowVaryings input) : SV_Target
            {
                return 0;
            }
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
            #pragma target 3.0
            #pragma vertex CMToonDepthVertex
            #pragma fragment CMToonDepthFragment

            #include "CMToonCommon.hlsl"

            struct CMToonDepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            CMToonDepthVaryings CMToonDepthVertex(CMToonAttributes input)
            {
                CMToonDepthVaryings output;
                output.positionCS = CMToonPositionCS(input);
                output.positionWS = CMToonWorldPos(input);
                return output;
            }

            half CMToonDepthFragment(CMToonDepthVaryings input) : SV_Target
            {
                CMToonFadeClip(input.positionWS, input.positionCS);   // the depth prepass must leave the same holes
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
            #pragma target 3.0
            #pragma vertex CMToonDepthNormalsVertex
            #pragma fragment CMToonDepthNormalsFragment

            #include "CMToonCommon.hlsl"

            struct CMToonDepthNormalsVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
            };

            CMToonDepthNormalsVaryings CMToonDepthNormalsVertex(CMToonAttributes input)
            {
                CMToonDepthNormalsVaryings output;
                output.positionCS = CMToonPositionCS(input);
                output.positionWS = CMToonWorldPos(input);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 CMToonDepthNormalsFragment(CMToonDepthNormalsVaryings input) : SV_Target
            {
                CMToonFadeClip(input.positionWS, input.positionCS);
                return half4(normalize(input.normalWS), 0.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
