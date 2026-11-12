// CM/Toon shared declarations (V-01, U-008: the one shared flat shader; art direction 2026-11-12 sections 2.2 and 2.3).
// Every pass of CMToon.shader includes this file so the UnityPerMaterial block is identical in all of them, which keeps
// the shader SRP Batcher compatible. Starting values live in the shader's Properties block; tuning goes through a U-###.
#ifndef CM_TOON_COMMON_INCLUDED
#define CM_TOON_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    half4 _TeamColor;
    half _MidMultiplier;
    half _ShadowMultiplier;
    half4 _ShadowTint;
    half _ShadowTintAmount;
    half _LitThreshold;
    half _ShadowThreshold;
    half _BandSoftness;
    half _TopBandLift;
    half4 _OutlineColor;
    float _OutlinePx;
    float _OutlineSource;
    half _RimWidth;
    half _RimStrength;
CBUFFER_END

// Shared vertex input. color.r is the team mask (vertex colour layer "TeamMask", U-043):
// 1 paints _TeamColor, 0 keeps _BaseColor, values between lerp.
// texcoord3.xyz is a smoothed outline direction (object space), read only when _OutlineSource is 2: a mesh without UV3
// does not get zeros on every graphics API (Metal fed the capsule primitive another channel and broke its line).
// Named CMToonAttributes because URP's ShadowCasterPass.hlsl declares its own "Attributes".
struct CMToonAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    half4  color      : COLOR;
    float3 outlineOS  : TEXCOORD3;
};

// Three tone bands (2.2): lit = base x 1.00, mid = base x _MidMultiplier, shadow = base x _ShadowMultiplier mixed
// _ShadowTintAmount toward _ShadowTint (Void Navy). band is 0 (shadow) .. 2 (lit); between whole numbers it is the
// hard smoothstep edge only.
half3 CMToonBandColor(half3 albedo, half band)
{
    half3 lit = albedo;
    half3 mid = albedo * _MidMultiplier;
    half3 shadow = lerp(albedo * _ShadowMultiplier, _ShadowTint.rgb, _ShadowTintAmount);
    return band <= 1.0h ? lerp(shadow, mid, band) : lerp(mid, lit, band - 1.0h);
}

// N.L to a band with hard edges: > _LitThreshold lit, _ShadowThreshold.._LitThreshold mid, below shadow.
half CMToonBand(float ndl)
{
    half w = _BandSoftness * 0.5h;
    return smoothstep(_ShadowThreshold - w, _ShadowThreshold + w, ndl) + smoothstep(_LitThreshold - w, _LitThreshold + w, ndl);
}

// Clip-space inverted hull of constant screen width (2.3): _OutlinePx pixels at 1080p, scaled with the vertical
// resolution, so the offset in NDC is _OutlinePx * 2 / 1080 vertically whatever the target size, and the horizontal
// offset is corrected by the aspect. Width does not change with distance or perspective.
float4 CMToonOutlinePositionCS(CMToonAttributes input)
{
    // _OutlineSource: 0 the vertex normal (smooth meshes), 1 away from the pivot (convex primitives with split normals,
    // so the hull has no gaps at the corners), 2 the UV3 direction (chunk meshes write each box corner's direction).
    float3 dirOS = _OutlineSource > 1.5 ? input.outlineOS : (_OutlineSource > 0.5 ? input.positionOS.xyz : input.normalOS);
    float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
    float3 dirWS = TransformObjectToWorldDir(dirOS, true);
    float2 dirCS = mul((float3x3)GetWorldToHClipMatrix(), dirWS).xy;
    float len = length(dirCS);
    if (len > 1e-6)
    {
        float2 offset = dirCS / len * (_OutlinePx * 2.0 / 1080.0);
        offset.x *= _ScreenParams.y / _ScreenParams.x;
        positionCS.xy += offset * positionCS.w;
    }
    return positionCS;
}

#endif // CM_TOON_COMMON_INCLUDED
