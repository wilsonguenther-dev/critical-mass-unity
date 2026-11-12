// Death state world grade (art direction 6.3, HUD v2). A uGUI graphic drawn first in the HUD canvas (Screen Space -
// Camera, so inside the gameplay camera's transparent pass, after URP copies the opaque texture): it writes the world
// back at 70% brightness and 60% saturation; every HUD element drawn after it keeps full brightness and colour.
// View only; GameHud asks the camera for the opaque texture only while the local player is dead.
Shader "CM/UI/DeathGrade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Unused", 2D) = "white" {}
        _Brightness ("Brightness (sRGB multiplier)", Float) = 0.7
        _Saturation ("Saturation", Float) = 0.6
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend One Zero

        Pass
        {
            Name "DeathGrade"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Brightness;
                float _Saturation;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                half3 c = SampleSceneColor(uv);
                // grade in display (sRGB) space so "70% brightness, 60% saturation" means what the brief measures
                half3 g = LinearToSRGB(max(c, 0));
                float3 gf = (float3)g;
                float l = dot(gf, float3(0.2126, 0.7152, 0.0722));
                float sat = _Saturation > 0.001 ? _Saturation : 0.6;      // material value, brief default if unset
                float bright = _Brightness > 0.001 ? _Brightness : 0.7;
                g = (half3)((l.xxx + (gf - l.xxx) * sat) * bright);
                return half4(SRGBToLinear(saturate(g)), 1);
            }
            ENDHLSL
        }
    }
}
