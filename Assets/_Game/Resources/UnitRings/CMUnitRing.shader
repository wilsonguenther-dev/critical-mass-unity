// CM/UnitRing (DG-03, art direction 6.4): the ground ring under every unit, drawn by UnitRings as one dynamic mesh.
// The ring is a signed distance field in world units (uv0 = offset from the ring centre on the ground plane), filled
// flat and outlined in Void Navy like the toon line, anti-aliased over one screen pixel with fwidth, so it stays crisp
// at any zoom. uv1 = (style 0 self / 1 ally / 2 enemy, team 0 Azure / 1 Crimson, tick angle in radians, unused).
// Self: Tool Brass, 0.06 u, four ticks on the screen axes. Ally: solid team colour, 0.04 u. Enemy: 8 dashes, 0.04 u.
// The stroke never drops below a pixel floor (_MinPx, _SelfMinPx at 1080p, scaled by the target height), so far rings
// stay readable. Unlit, alpha-blended, no depth write, depth-tested (bodies, walls and the Kaiju hide it), offset
// toward the camera so it never fights the ground. Lives under Resources so a player build includes it.
Shader "CM/UnitRing"
{
    Properties
    {
        _SelfColor ("Self (Tool Brass)", Color) = (0.780, 0.573, 0.271, 1)
        _AzureColor ("Azure", Color) = (0.086, 0.780, 0.851, 1)
        _CrimsonColor ("Crimson", Color) = (1, 0.353, 0.373, 1)
        _OutlineColor ("Outline (Void Navy)", Color) = (0.094, 0.137, 0.231, 1)
        _Radius ("Radius (u)", Float) = 0.55
        _SelfWidth ("Self width (u)", Float) = 0.06
        _TeamWidth ("Ally / enemy width (u)", Float) = 0.04
        _TickLength ("Tick length (u)", Float) = 0.13
        _TickWidth ("Tick width (u)", Float) = 0.06
        _Dashes ("Enemy dashes", Float) = 8
        _DashFill ("Dash share of a period", Range(0.2, 0.9)) = 0.62
        _MinPx ("Ally / enemy minimum stroke (px at 1080p)", Float) = 2.6
        _SelfMinPx ("Self minimum stroke (px at 1080p)", Float) = 4.0
        _OutlinePx ("Outline (px at 1080p)", Float) = 1.25
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Transparent" "Queue" = "Transparent-40" "IgnoreProjector" = "True" }
        Pass
        {
            Name "UnitRing"
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
            CBUFFER_START(UnityPerMaterial)
                half4 _SelfColor;
                half4 _AzureColor;
                half4 _CrimsonColor;
                half4 _OutlineColor;
                float _Radius;
                float _SelfWidth;
                float _TeamWidth;
                float _TickLength;
                float _TickWidth;
                float _Dashes;
                float _DashFill;
                float _MinPx;
                float _SelfMinPx;
                float _OutlinePx;
                half _Opacity;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float4 uv0 : TEXCOORD0; float4 uv1 : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float2 p : TEXCOORD0; float4 info : TEXCOORD1; };

            V Vert(A i)
            {
                V o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.p = i.uv0.xy;
                o.info = i.uv1;
                return o;
            }

            // Signed distance (u) to a radial tick at the nearest of four angles off a, spanning r0..r1, half width hw.
            float TickSdf(float2 p, float a, float r0, float r1, float hw)
            {
                float ang = atan2(p.y, p.x) - a;
                float q = HALF_PI;
                float da = ang - q * round(ang / q);
                float r = length(p);
                float along = r * cos(da);
                float across = abs(r * sin(da));
                float2 d = float2(across - hw, abs(along - 0.5 * (r0 + r1)) - 0.5 * (r1 - r0));
                return length(max(d, 0.0)) + min(max(d.x, d.y), 0.0);
            }

            half4 Frag(V i) : SV_Target
            {
                float style = i.info.x;
                float r = length(i.p);
                // World units per screen pixel here, and the 1080p pixel scale for the floors.
                float upp = max(fwidth(r), 1e-5);
                float pxScale = _ScreenParams.y / 1080.0;
                bool self = style < 0.5;
                float width = self ? _SelfWidth : _TeamWidth;
                width = max(width, (self ? _SelfMinPx : _MinPx) * pxScale * upp);

                // Ring band.
                float d = abs(r - _Radius) - 0.5 * width;
                if (style > 1.5)
                {
                    // Dashes: intersect the band with angular segments (arc length in u, so the AA is per pixel too).
                    float t = frac(atan2(i.p.y, i.p.x) / TWO_PI * _Dashes);
                    float arc = TWO_PI * _Radius / _Dashes;
                    float dd = (abs(t - 0.5 * _DashFill) - 0.5 * _DashFill) * arc;
                    d = max(d, dd);
                }
                if (self)
                {
                    float tw = max(_TickWidth, _SelfMinPx * pxScale * upp);
                    float tick = TickSdf(i.p, i.info.z, _Radius, _Radius + 0.5 * width + _TickLength, 0.5 * tw);
                    d = min(d, tick);
                }

                float outline = _OutlinePx * pxScale * upp;
                half fill = saturate(0.5 - d / upp);
                half cover = saturate(0.5 - (d - outline) / upp);
                half3 team = i.info.y < 0.5 ? _AzureColor.rgb : _CrimsonColor.rgb;
                half3 body = self ? _SelfColor.rgb : team;
                half3 rgb = lerp(_OutlineColor.rgb, body, fill / max(cover, 1e-3));
                return half4(rgb, cover * _Opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
