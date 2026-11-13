// CM/AmbientCreature (arena night life 1, 2026-11-13): the background sky creatures (skyrays, cloud-loafers, bird
// flocks), all of one kind in ONE combined mesh and animated entirely here, so they cost one draw for the fill and one
// for the ink line and no CPU per frame. Every vertex carries its creature's path: UV1 (seed, kind, unused, unused),
// UV2 (path centre x, z, radius x, radius z, world units), UV3 (height y, angular speed rad/s, phase rad, scale). The
// mesh is authored in the creature's own frame (+z forward, +x right). UV1.zw: a formation offset (x right, z forward,
// world units) added after the flap, so a bird's place in its flock never scales its wing beat. Kinds: 0 skyray (wing beat), 1 cloud-loafer
// (slow body wave), 2 bird (fast flap). Vertex alpha 1 marks a glow spot (self-lit, scaled by _CMAmbient.w: 0 in Day).
// Toon: two hard bands on the main light, the time-of-day grade and the height fog of the world (CMTimeOfDay.hlsl), so
// they sink into the void like the sky rocks. Motion time: _CMAmbientTime (AmbientLife.Clock: seconds x 0.35 under reduce motion). Visual only.
// No shadow caster (they never shadow the islands) and no depth passes (nothing reads them).
Shader "CM/AmbientCreature"
{
    Properties
    {
        _FogColor ("Height Fog Colour", Color) = (0.705882, 0.854902, 0.933333, 1)
        _FogTop ("Height Fog Top (y)", Float) = -3.5
        _FogBottom ("Height Fog Full (y)", Float) = -12
        _FogAmount ("Height Fog Amount", Range(0, 1)) = 0.8
        _OutlinePx ("Outline Width (px at 1080p)", Range(0, 4)) = 1.5
        _OutlineColor ("Outline Colour (Void Navy)", Color) = (0.094118, 0.137255, 0.231373, 1)
        _GlowColor ("Glow Spot Colour", Color) = (1, 0.905882, 0.639216, 1)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry+10" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Assets/_Game/Art/Shaders/CMTimeOfDay.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _FogColor;
            float _FogTop;
            float _FogBottom;
            half _FogAmount;
            float _OutlinePx;
            half4 _OutlineColor;
            half4 _GlowColor;
        CBUFFER_END

        // x motion scale (1, reduce motion 0.35), y fireflies, z light pools, w creature glow (TimeOfDayDirector).
        float4 _CMAmbient;
        float _CMAmbientTime;

        struct CreatureAttributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            half4  color      : COLOR;
            float4 uv1        : TEXCOORD1;
            float4 uv2        : TEXCOORD2;
            float4 uv3        : TEXCOORD3;
        };

        float CreatureTime() { return _CMAmbientTime; }

        // World position and normal of a creature vertex.
        void CreaturePose(CreatureAttributes i, out float3 positionWS, out float3 normalWS)
        {
            float t = CreatureTime();
            float kind = i.uv1.y;
            float a = i.uv3.z + t * i.uv3.y;
            float s = i.uv3.w;
            float2 c = i.uv2.xy, r = i.uv2.zw;
            float bob = sin(t * 0.45 + i.uv3.z * 3.0) * (kind > 1.5 ? 0.25 : 0.5);
            float3 P = float3(c.x + cos(a) * r.x, i.uv3.x + bob, c.y + sin(a) * r.y);
            float3 F = normalize(float3(-sin(a) * r.x, 0.0, cos(a) * r.y) * (i.uv3.y >= 0.0 ? 1.0 : -1.0));
            float3 U = float3(0, 1, 0);
            float3 R = normalize(cross(U, F));
            // A gentle bank into the turn.
            U = normalize(U - R * 0.18 * sign(i.uv3.y));
            R = normalize(cross(U, F));

            float3 lp = i.positionOS.xyz;
            if (kind < 0.5)        lp.y += sin(t * 1.3 + i.uv3.z * 5.0) * 0.30 * pow(abs(lp.x), 1.3);               // skyray wing beat
            else if (kind < 1.5)   lp.x += sin(lp.z * 0.55 - t * 0.9 + i.uv3.z) * 0.10 * saturate(abs(lp.z) * 0.35); // loafer body wave
            else                   lp.y += sin(t * 8.5 + i.uv1.x * 40.0) * 0.55 * abs(lp.x);                         // bird flap
            lp *= s;
            lp += float3(i.uv1.z, 0.0, i.uv1.w);
            positionWS = P + R * lp.x + U * lp.y + F * lp.z;
            float3 n = i.normalOS;
            normalWS = normalize(R * n.x + U * n.y + F * n.z);
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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : COLOR;
            };

            V Vert(CreatureAttributes i)
            {
                V o;
                CreaturePose(i, o.positionWS, o.normalWS);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.color = i.color;
                return o;
            }

            half4 Frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 n = normalize(i.normalWS) * (front ? 1.0 : -1.0);
                Light L = GetMainLight();
                half ndl = dot(n, L.direction);
                half band = step(0.0h, ndl) + step(0.45h, ndl);       // 0 shadow, 1 mid, 2 lit
                half3 albedo = SRGBToLinear(i.color.rgb);
                half3 col = albedo * (band >= 2.0h ? 1.0h : band >= 1.0h ? 0.84h : 0.66h);
                col = CMTodGrade(col, band * 0.5h, 0.0h);
                // Glow spots: self-lit, only as bright as the night asks (0 in Day: they read as pale markings).
                half glow = step(0.5h, i.color.a);
                col = lerp(col, lerp(col, _GlowColor.rgb, saturate((half)_CMAmbient.w)), glow);
                float f = _FogAmount * saturate((_FogTop - i.positionWS.y) / max(1e-3, _FogTop - _FogBottom)) * (1.0 - glow * saturate(_CMAmbient.w) * 0.7);
                if (f > 0.0) col = lerp(col, CMTodFogColor(_FogColor.rgb, i.positionWS, (half)min(f, 0.9)), f);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag

            struct V
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            V Vert(CreatureAttributes i)
            {
                V o;
                float3 n;
                CreaturePose(i, o.positionWS, n);
                float4 cs = TransformWorldToHClip(o.positionWS);
                float2 d = mul((float3x3)GetWorldToHClipMatrix(), n).xy;
                float len = length(d);
                if (len > 1e-6)
                {
                    float2 off = d / len * (_OutlinePx * 2.0 / 1080.0);
                    off.x *= _ScreenParams.y / _ScreenParams.x;
                    cs.xy += off * cs.w;
                }
                o.positionCS = cs;
                return o;
            }

            half4 Frag(V i) : SV_Target
            {
                // Far background line (environment brief): mixed toward the fog with depth like the sky rocks.
                float f = _FogAmount * saturate((_FogTop - i.positionWS.y) / max(1e-3, _FogTop - _FogBottom));
                half3 fog = CMTodFogColor(_FogColor.rgb, i.positionWS, 0.0h);
                return half4(lerp(_OutlineColor.rgb, fog, saturate(0.35 + f * 0.6)), 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
