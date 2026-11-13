// CM/WorldGround (environment kit v3 "CM_Ground", 2026-11-12): the island ground of the dressed world, to the kit README
// spec. World-space xz (sim frame: x east, z = -Unity z), domain-warped by 2D value noise (scale 0.55, +-0.14 u).
// Grass: Voronoi F1 at scale 0.4 (mean cell 2.5 u), cell hash -> three greens, a 1 px seam on the cell edges (fwidth,
// constant on screen), value x (1 + 0.04 sin(2 pi G / 8 + 6 noise(0.07))). Dirt mixed by the field's B, the ledge shadow
// 55% of A toward #6F9A3E. Paving: path half-width w = 1.6 + 0.6 noise(0.18); a flagstone cell (Voronoi scale 1.0) is
// paved when the field's R at the CELL's feature point is below w - 0.5 and a 0.33-scale noise is under 0.6 (grass breaks
// through), three tones, 5% of cells dirt, grout lines; kerbstones (Voronoi scale 2.0) where w - 0.5 < R(feature) < w.
// Lighting: flat, cast shadows only (shadow = colour x 0.66 mixed 12% toward Void Navy). No outline, no fog (y >= 0).
// The field texture (_FieldTex, 4 texels per unit, RGBA half: R path distance, G feature distance, B dirt, A ledge
// shadow) is baked from the Sim grid by WorldField.Bake; _FieldRect = (x0, z0, width, height) in sim units.
// World reactions (2026-11-12): the ground paint texture (_CMPaintTex, global) is mixed over the colour.
Shader "CM/WorldGround"
{
    Properties
    {
        _FieldTex ("Field (R path, G feature, B dirt, A ledge shadow)", 2D) = "black" {}
        _FieldRect ("Field rect (sim x0, z0, w, h)", Vector) = (0, 0, 112, 72)
        _ShadowTint ("Shadow Tint (Void Navy)", Color) = (0.094118, 0.137255, 0.231373, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" "Queue" = "Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _FieldRect;
            float4 _FieldTex_ST;
            half4 _ShadowTint;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            // Arena night life 1: the time-of-day grade (all globals 0 = Day).
            #include "CMTimeOfDay.hlsl"
            TEXTURE2D(_FieldTex);
            SAMPLER(sampler_FieldTex);
            // Ground paint (world reactions, GroundPaint.cs): blood, scorch, cracks and footprints stamped once into a
            // linear RGBA8 texture over the map. RGB colour, A remaining life (1 at the stamp, falling linearly to 0);
            // the mark is fully shown until 30% of its life is left, then fades. _CMPaintRect = (sim x0, z0, 1/w, 1/h),
            // all 0 (never set) = no paint.
            TEXTURE2D(_CMPaintTex);
            SAMPLER(sampler_CMPaintTex);
            float4 _CMPaintRect;

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };

            V Vert(A i)
            {
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float2 Hash22(float2 p) { float n = Hash21(p); return float2(n, Hash21(p + n + 17.17)); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), u.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), u.x), u.y);
            }
            // Voronoi F1 with the distance to the cell border (two-pass); cell id, feature point (same space as x).
            void Voronoi(float2 x, out float2 cellId, out float2 feature, out float edge)
            {
                float2 n = floor(x), f = frac(x), mg = 0, mr = 0;
                float md = 8.0;
                [unroll] for (int j = -1; j <= 1; j++)
                [unroll] for (int i = -1; i <= 1; i++)
                {
                    float2 g = float2(i, j);
                    float2 r = g + Hash22(n + g) - f;
                    float d = dot(r, r);
                    if (d < md) { md = d; mr = r; mg = g; }
                }
                md = 8.0;
                [unroll] for (int j2 = -2; j2 <= 2; j2++)
                [unroll] for (int i2 = -2; i2 <= 2; i2++)
                {
                    float2 g = mg + float2(i2, j2);
                    float2 r = g + Hash22(n + g) - f;
                    float2 dr = r - mr;
                    if (dot(dr, dr) > 1e-5) md = min(md, dot(0.5 * (mr + r), normalize(dr)));
                }
                cellId = n + mg;
                feature = x + mr;
                edge = md;
            }
            float4 Field(float2 sim)
            {
                float2 uv = (sim - _FieldRect.xy) / _FieldRect.zw;
                return SAMPLE_TEXTURE2D_LOD(_FieldTex, sampler_FieldTex, uv, 0);
            }
            half3 Hex(float r, float g, float b) { return SRGBToLinear(half3(r, g, b) / 255.0); }
            half3 Pick3(float h, half3 a, half3 b, half3 c) { return h < 0.3333 ? a : (h < 0.6667 ? b : c); }

            half4 Frag(V i) : SV_Target
            {
                float2 sim = float2(i.positionWS.x, -i.positionWS.z);
                float2 q = sim + (float2(VNoise(sim * 0.55), VNoise(sim * 0.55 + 31.7)) - 0.5) * 0.28;
                float4 F = Field(q);

                // Grass cells (scale 0.4) with a constant 1 px seam.
                float2 gid, gfeat; float gedge;
                Voronoi(q * 0.4, gid, gfeat, gedge);
                float gedgeU = gedge / 0.4;
                half3 col = Pick3(Hash21(gid), Hex(0xAC, 0xCB, 0x56), Hex(0xA3, 0xC1, 0x50), Hex(0x96, 0xB2, 0x49));
                float seamW = max(0.008, fwidth(gedgeU));
                col = gedgeU < seamW ? Hex(0x7E, 0x9A, 0x3F) : col;
                col *= 1.0 + 0.04 * sin(6.2831853 * F.g / 8.0 + 6.0 * VNoise(q * 0.07));
                col = lerp(col, Hex(0xD3, 0x9A, 0x5C), saturate(F.b));
                col = lerp(col, Hex(0x6F, 0x9A, 0x3E), 0.55 * saturate(F.a));

                // Flagstones (scale 1.0): paved by the path distance at the cell's feature point.
                float2 fid, ffeat; float fedge;
                Voronoi(q, fid, ffeat, fedge);
                float wf = 1.6 + 0.6 * VNoise(ffeat * 0.18);
                float rf = Field(ffeat).r;
                if (rf < wf - 0.5 && VNoise(ffeat * 0.33) < 0.6)
                {
                    float h = Hash21(fid + 7.0);
                    half3 stone = Pick3(frac(h * 7.31), Hex(0xE6, 0xC5, 0x97), Hex(0xD8, 0xB7, 0x8C), Hex(0xEB, 0xD2, 0xA6));
                    stone = h < 0.05 ? Hex(0xD3, 0x9A, 0x5C) : stone;
                    col = fedge < max(0.045, fwidth(fedge)) ? Hex(0xA0, 0x8A, 0x68) : stone;
                }
                else
                {
                    // Kerbstones (scale 2.0) along the path edge band.
                    float2 kid, kfeat; float kedge;
                    Voronoi(q * 2.0, kid, kfeat, kedge);
                    float2 kf = kfeat / 2.0;
                    float wk = 1.6 + 0.6 * VNoise(kf * 0.18);
                    float rk = Field(kf).r;
                    if (rk > wk - 0.5 && rk < wk)
                    {
                        half3 kerb = Pick3(Hash21(kid + 3.0), Hex(0xC9, 0xAD, 0x84), Hex(0xBD, 0xA0, 0x78), Hex(0xD6, 0xBA, 0x90));
                        col = kedge / 2.0 < max(0.03, fwidth(kedge / 2.0)) ? Hex(0x8C, 0x75, 0x56) : kerb;
                    }
                }

                // Ground paint over the ground colour (before the shadow, so marks in shade darken with the ground).
                if (_CMPaintRect.z > 0.0)
                {
                    float2 puv = (sim - _CMPaintRect.xy) * _CMPaintRect.zw;
                #if UNITY_UV_STARTS_AT_TOP
                    puv.y = 1.0 - puv.y;
                #endif
                    half4 paint = SAMPLE_TEXTURE2D_LOD(_CMPaintTex, sampler_CMPaintTex, puv, 0);
                    col = lerp(col, paint.rgb, saturate(paint.a / 0.3h));
                }

                // Flat; cast shadows only.
                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                half atten = MainLightRealtimeShadow(shadowCoord);
                if (atten < 0.5) col = lerp(col * 0.66, _ShadowTint.rgb, 0.12);
                return half4(CMTodGrade(col, atten < 0.5 ? 0.0h : 1.0h, 0.0h), 1.0);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex DVert
            #pragma fragment DFrag
            float4 DVert(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half DFrag(float4 p : SV_POSITION) : SV_Target { return p.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex NVert
            #pragma fragment NFrag
            struct NA { float4 p : POSITION; };
            struct NV { float4 p : SV_POSITION; };
            NV NVert(NA i) { NV o; o.p = TransformObjectToHClip(i.p.xyz); return o; }
            half4 NFrag(NV i) : SV_Target { return half4(0, 1, 0, 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
