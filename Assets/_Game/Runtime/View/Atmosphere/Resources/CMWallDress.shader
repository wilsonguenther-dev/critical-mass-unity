// CM/WallDress (world horizon 2, 2026-11-14): the wall architecture overlay (WallArchitecture.cs). View only: one mesh,
// one material, no texture, no shadow caster, no collider. Toon-banded like CM/Toon (lit x1, mid x0.84, shadow x0.66
// mixed 12% toward Void Navy), receives the sun's shadows, takes the time-of-day grade, and keeps the follow camera's
// see-through line (the same dither as CMToonFadeClip).
//
// The low-view rise: UV0.x is how far (u) a vertex rises in a full eye-level view. The weight is per camera:
//   low = saturate((forward.y + 0.7) / 0.3)   (0 when the view looks down more than about 44 degrees, 1 above 24),
//   0 on orthographic cameras (minimap, top-down shots), and faded to 0 within 3..8 u of the camera so nothing between
//   the follow camera and its fighter grows. The top-down gameplay camera (about 55 degrees down) sees low = 0 exactly:
//   the gameplay view, its readability and what it can see are unchanged.
// UV0.y flags: 1 cloth (sways), 2 flame (self-lit, flickers, brighter at dusk and night), 3 low-only (the vertex folds by
// UV1.xyz times (1 - low): gate arches collapse into their post and the hill beacon to a point from the top-down camera,
// so they cover no walkable cell there). UV2 = (sway weight, phase).
Shader "CM/WallDress"
{
    Properties
    {
        _ShadowTint ("Shadow Tint (Void Navy)", Color) = (0.094118, 0.137255, 0.231373, 1)
        _MidMultiplier ("Mid", Float) = 0.84
        _ShadowMultiplier ("Shadow", Float) = 0.66
        _ShadowTintAmount ("Shadow tint amount", Float) = 0.12
        _LitThreshold ("Lit threshold", Float) = 0.35
        _ShadowThreshold ("Shadow threshold", Float) = -0.05
        _BandSoftness ("Band softness", Float) = 0.02
        _TopBandLift ("Top faces band lift", Float) = 1
        _MaxRise ("Max rise (u)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ShadowTint;
            half _MidMultiplier;
            half _ShadowMultiplier;
            half _ShadowTintAmount;
            half _LitThreshold;
            half _ShadowThreshold;
            half _BandSoftness;
            half _TopBandLift;
            float _MaxRise;
        CBUFFER_END

        struct A
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv0 : TEXCOORD0;   // rise, flag
            float4 uv1 : TEXCOORD1;   // fold offset (low-only vertices)
            float2 uv2 : TEXCOORD2;   // sway weight, phase
        };

        // How much of the eye-level dressing this camera sees: 0 top-down and orthographic, 1 at eye level.
        float WallLowView()
        {
            if (unity_OrthoParams.w > 0.5) return 0.0;
            float3 fwd = -UNITY_MATRIX_V[2].xyz;
            return saturate((fwd.y + 0.7) / 0.3);
        }

        float3 WallDressPos(A i, out float flag)
        {
            float3 p = TransformObjectToWorld(i.positionOS.xyz);
            flag = i.uv0.y;
            float low = WallLowView();
            float nearFade = smoothstep(3.0, 8.0, distance(p, _WorldSpaceCameraPos));
            p.y += min(i.uv0.x, _MaxRise) * low * nearFade;
            if (flag > 2.5) p += i.uv1.xyz * (1.0 - low);
            float t = _Time.y;
            if (i.uv2.x > 0.0)
            {
                float s = sin(t * 1.7 + i.uv2.y + p.y * 1.3) + 0.4 * sin(t * 3.1 + i.uv2.y * 1.7);
                if (flag > 1.5 && flag < 2.5) p.xz += float2(s, cos(t * 2.3 + i.uv2.y)) * i.uv2.x;   // flame tip
                else p.xz += float2(0.8, 0.6) * s * i.uv2.x;                                        // cloth in the wind
            }
            return p;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Assets/_Game/Art/Shaders/CMTimeOfDay.hlsl"
            #include "Assets/_Game/Art/Shaders/CMFadeVolumes.hlsl"   // fix/px-camera-in-editor: lens sphere (all zero = off)

            float4 _CMAmbient;     // w: 0 Day, 0.5 Dusk, 1 Night (TimeOfDayDirector)
            float4 _CMFadeCam;     // follow camera's see-through line (GroundView); w = 1 on
            float4 _CMFadeTarget;

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                half3 color : TEXCOORD2;
                float2 fx : TEXCOORD3;   // flag, phase
            };

            V Vert(A i)
            {
                V o;
                float flag;
                o.positionWS = WallDressPos(i, flag);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.color = SRGBToLinear(i.color.rgb);
                o.fx = float2(flag, i.uv2.y);
                return o;
            }

            void FadeClip(float3 positionWS, float4 positionCS)
            {
                CMFadeVolumesClip(positionWS, positionCS, 0.0);
                if (_CMFadeCam.w < 0.5) return;
                float3 a = _CMFadeCam.xyz;
                float3 s = _CMFadeTarget.xyz - a;
                float t = dot(positionWS - a, s) / max(dot(s, s), 1e-4);
                if (t <= 0.02 || t >= 0.85) return;
                float d = length(a + s * t - positionWS);
                float r = _CMFadeTarget.w;
                float amount = saturate((r - d) / (r * 0.5)) * 0.8;
                float n = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
                clip(n - amount);
            }

            half4 Frag(V i) : SV_Target
            {
                FadeClip(i.positionWS, i.positionCS);
                half3 albedo = i.color;
                if (i.fx.x > 1.5 && i.fx.x < 2.5)
                {
                    // Flames and lit windows: self-lit, a slow flicker, brighter at dusk and night.
                    float t = _Time.y * 9.0 + i.fx.y * 7.0;
                    half flick = 0.85h + 0.15h * (half)(sin(t) * sin(t * 0.37 + 1.3));
                    half glow = 1.0h + 0.6h * (half)saturate(_CMAmbient.w * 1.4);
                    return half4(albedo * flick * glow, 1.0);
                }
                // Cull Off: every face lights from the side the camera sees (cards are single sheets, solids any winding).
                float3 n = normalize(i.normalWS);
                float3 vd = GetWorldSpaceNormalizeViewDir(i.positionWS);
                n = dot(n, vd) < 0.0 ? -n : n;
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                Light l = GetMainLight(shadowCoord);
                float ndl = dot(n, l.direction) + _TopBandLift * saturate(n.y - 0.7);
                half w = _BandSoftness * 0.5h;
                half band = smoothstep(_ShadowThreshold - w, _ShadowThreshold + w, ndl) + smoothstep(_LitThreshold - w, _LitThreshold + w, ndl);
                band *= step(0.5, l.shadowAttenuation) > 0.5 ? 1.0h : 0.0h;
                half3 mid = albedo * _MidMultiplier;
                half3 shadow = lerp(albedo * _ShadowMultiplier, _ShadowTint.rgb, _ShadowTintAmount);
                half3 col = band <= 1.0h ? lerp(shadow, mid, band) : lerp(mid, albedo, band - 1.0h);
                return half4(CMTodGrade(col, band * 0.5h, 0.0h), 1.0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            Cull Off
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DVert
            #pragma fragment DFrag
            float4 DVert(A i) : SV_POSITION { float f; return TransformWorldToHClip(WallDressPos(i, f)); }
            half DFrag(float4 p : SV_POSITION) : SV_Target { return p.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma vertex NVert
            #pragma fragment NFrag
            struct NV { float4 p : SV_POSITION; float3 n : TEXCOORD0; };
            NV NVert(A i) { NV o; float f; o.p = TransformWorldToHClip(WallDressPos(i, f)); o.n = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 NFrag(NV i) : SV_Target { return half4(normalize(i.n), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
