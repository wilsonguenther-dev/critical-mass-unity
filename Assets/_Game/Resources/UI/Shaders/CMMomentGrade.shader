// Big moments impact grade (feat/big-moments). A full-screen uGUI RawImage: samples either the frozen frame (_MainTex,
// _Frozen = 1: the picture-hold captured with ScreenCapture) or the live world (_Frozen = 0: URP's opaque texture, drawn in
// a Screen Space - Camera canvas below the HUD the way CMDeathGrade does), then desaturates everything except two soft
// circles (attacker and victim stay in colour), dims it and can invert it for the one-frame flash. Vertex alpha fades it.
// View only; nothing here reaches the sim.
Shader "CM/UI/MomentGrade"
{
    Properties
    {
        [PerRendererData] _MainTex ("Frozen frame", 2D) = "black" {}
        _Frozen ("Frozen (1) or live (0)", Float) = 0
        _Saturation ("Saturation outside the holes", Float) = 0.15
        _Brightness ("Brightness outside the holes", Float) = 0.8
        _Invert ("Invert", Float) = 0
        _FlipY ("Flip the frozen frame vertically", Float) = 0
        _HoleA ("Hole A (uv x, uv y, radius in screen heights, on)", Vector) = (0.5, 0.5, 0.08, 0)
        _HoleB ("Hole B", Vector) = (0.5, 0.5, 0.08, 0)
        _Aspect ("Screen width / height", Float) = 1.7778
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "MomentGrade"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float _Frozen;
                float _Saturation;
                float _Brightness;
                float _Invert;
                float _FlipY;
                float4 _HoleA;
                float4 _HoleB;
                float _Aspect;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screenPos : TEXCOORD0; float4 color : COLOR; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.screenPos = ComputeScreenPos(o.positionCS);
                o.color = i.color;
                return o;
            }

            float Hole(float2 uv, float4 h)
            {
                if (h.w < 0.5) return 0;
                float2 d = float2((uv.x - h.x) * _Aspect, uv.y - h.y);
                float r = max(h.z, 1e-4);
                return 1 - smoothstep(r * 0.75, r, length(d));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screenPos.xy / i.screenPos.w;
                half3 c;
                if (_Frozen > 0.5)
                {
                    float2 fuv = float2(uv.x, _FlipY > 0.5 ? 1 - uv.y : uv.y);
                    c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, fuv).rgb;
                }
                else
                {
                    c = SampleSceneColor(uv);
                }
                float3 g = (float3)LinearToSRGB(max(c, 0));
                float keep = saturate(max(Hole(uv, _HoleA), Hole(uv, _HoleB)));
                float l = dot(g, float3(0.2126, 0.7152, 0.0722));
                float sat = lerp(_Saturation, 1, keep);
                float bright = lerp(_Brightness, 1, keep);
                g = (l.xxx + (g - l.xxx) * sat) * bright;
                g = lerp(g, 1 - g, saturate(_Invert));
                return half4(SRGBToLinear(saturate((half3)g)), i.color.a);
            }
            ENDHLSL
        }
    }
}
