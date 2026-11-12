// CM/Toon shared declarations (V-01, U-008: the one shared flat shader; art direction 2026-11-12 sections 2.2 and 2.3).
// Every pass of CMToon.shader includes this file so the UnityPerMaterial block is identical in all of them, which keeps
// the shader SRP Batcher compatible. Starting values live in the shader's Properties block; tuning goes through a U-###.
#ifndef CM_TOON_COMMON_INCLUDED
#define CM_TOON_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
// SRGBToLinear / LinearToSRGB for the vertex-colour mode.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

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
    float _ColorMode;
    half4 _TeamCloth;
    half4 _TeamGlow;
    half4 _TeamDark;
    half4 _DefaultCloth;
    half4 _DefaultGlow;
    half4 _DefaultDark;
    // Props v2 marker codes (3 marker light, 7 marker dark): team, self-lit, unlined.
    half4 _TeamMarker;
    half4 _TeamMarkerDark;
    half4 _DefaultMarker;
    half4 _DefaultMarkerDark;
    // World art (2026-11-12): band multipliers per channel (foliage bands), lit multiplier (crystals), whether a cast
    // shadow drops the band, height fog below the islands, constant-pixel extrusion along UV2 (waterfall foam).
    half _LitMultiplier;
    half4 _MidRatio;
    half4 _ShadowRatio;
    half _ReceiveShadows;
    half4 _FogColor;
    float _FogTop;
    float _FogBottom;
    half _FogAmount;
    float _ExtrudePx;
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
    float3 extrudeOS  : TEXCOORD2;
    float3 outlineOS  : TEXCOORD3;
};

// Vertex-colour mode (_ColorMode 1; Blender art import 2026-11-12). The art's colour set "Col" carries the lit colour
// in RGB (sRGB hex from the bible) and a mask code in A, read here in eighths:
//   0 fixed (banded)        1 fixed glow, lined (self-lit)   2 fixed glow (self-lit, no hull)
//   4 team dark (banded)    5 team glow, lined (self-lit)    6 team glow (self-lit, no hull)    8 team cloth (banded)
//   3 team marker light (self-lit, no hull)    7 team marker dark (self-lit, no hull)   (props v2, 2026-11-12: Azure
//   authored #27F8F8 / #43C2C5, Crimson #E6A595 / #D6594F)
// Team channels are authored in one allegiance (the _Default* colours) and swapped by the team-glow contract of the
// Kaiju v1 art: out = target * max(rgb) / max(default), in sRGB as the art authored it, so the baked facet brightness
// survives the hue change and an authored-colour pixel becomes exactly the target colour.
int CMToonMaskCode(half a)
{
    return (int)round(a * 8.0h);
}

half3 CMToonSwap(half3 rgbSrgb, half3 targetLinear, half3 authoredLinear)
{
    half3 target = LinearToSRGB(targetLinear);
    half3 authored = LinearToSRGB(authoredLinear);
    half k = max(rgbSrgb.r, max(rgbSrgb.g, rgbSrgb.b)) / max(1e-4h, max(authored.r, max(authored.g, authored.b)));
    return saturate(target * k);
}

// Albedo (linear) and self-lit flag of one vertex colour.
void CMToonVertexColour(half4 col, out half3 albedo, out half selfLit)
{
    int code = CMToonMaskCode(col.a);
    half3 rgb = col.rgb;
    if (code == 4) rgb = CMToonSwap(rgb, _TeamDark.rgb, _DefaultDark.rgb);
    else if (code == 5 || code == 6) rgb = CMToonSwap(rgb, _TeamGlow.rgb, _DefaultGlow.rgb);
    else if (code == 8) rgb = CMToonSwap(rgb, _TeamCloth.rgb, _DefaultCloth.rgb);
    else if (code == 3) rgb = CMToonSwap(rgb, _TeamMarker.rgb, _DefaultMarker.rgb);
    else if (code == 7) rgb = CMToonSwap(rgb, _TeamMarkerDark.rgb, _DefaultMarkerDark.rgb);
    selfLit = (code == 1 || code == 2 || code == 3 || code == 5 || code == 6 || code == 7) ? 1.0h : 0.0h;
#if defined(UNITY_COLORSPACE_GAMMA)
    albedo = rgb;
#else
    albedo = SRGBToLinear(rgb);
#endif
}

// 0 where the vertex colour mode marks an unlined glow or marker (codes 2, 3, 6 and 7), else 1: the hull collapses
// there. Codes 1 and 5 are lined glows (the bible inks them) and keep the hull.
half CMToonOutlineMask(half4 col)
{
    if (_ColorMode < 0.5) return 1.0h;
    int code = CMToonMaskCode(col.a);
    return (code == 2 || code == 3 || code == 6 || code == 7) ? 0.0h : 1.0h;
}

// Pushes a clip-space position px pixels (at 1080p, scaled with the target height) along the screen projection of an
// object-space direction: the same constant-pixel rule as the hull, used for the waterfall foam band.
float4 CMToonPushPx(float4 positionCS, float3 dirOS, float px)
{
    float3 dirWS = TransformObjectToWorldDir(dirOS, true);
    float2 dirCS = mul((float3x3)GetWorldToHClipMatrix(), dirWS).xy;
    float len = length(dirCS);
    if (len > 1e-6)
    {
        float2 offset = dirCS / len * (px * 2.0 / 1080.0);
        offset.x *= _ScreenParams.y / _ScreenParams.x;
        positionCS.xy += offset * positionCS.w;
    }
    return positionCS;
}

// Clip position of the lit, depth and normals passes: the plain transform, plus the foam extrusion when the material
// sets _ExtrudePx (only the water look; other meshes may carry no UV2, so it is never read for them).
float4 CMToonPositionCS(CMToonAttributes input)
{
    float4 positionCS = TransformObjectToHClip(input.positionOS.xyz);
    if (_ExtrudePx > 0.0 && dot(input.extrudeOS, input.extrudeOS) > 0.25)
        positionCS = CMToonPushPx(positionCS, input.extrudeOS, _ExtrudePx);
    return positionCS;
}

// Height fog below the islands (environment kit v3: starts at y -3.5): 0 above _FogTop, _FogAmount at _FogBottom.
half3 CMToonFog(half3 color, float3 positionWS)
{
    float f = _FogAmount * saturate((_FogTop - positionWS.y) / max(1e-3, _FogTop - _FogBottom));
    return lerp(color, _FogColor.rgb, f);
}

// Three tone bands (2.2): lit = base x 1.00, mid = base x _MidMultiplier, shadow = base x _ShadowMultiplier mixed
// _ShadowTintAmount toward _ShadowTint (Void Navy). band is 0 (shadow) .. 2 (lit); between whole numbers it is the
// hard smoothstep edge only.
half3 CMToonBandColor(half3 albedo, half band)
{
    half3 lit = saturate(albedo * _LitMultiplier);
    half3 mid = albedo * _MidMultiplier * _MidRatio.rgb;
    half3 shadow = lerp(albedo * _ShadowMultiplier * _ShadowRatio.rgb, _ShadowTint.rgb, _ShadowTintAmount);
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
        float2 offset = dirCS / len * (_OutlinePx * CMToonOutlineMask(input.color) * 2.0 / 1080.0);
        offset.x *= _ScreenParams.y / _ScreenParams.x;
        positionCS.xy += offset * positionCS.w;
    }
    return positionCS;
}

#endif // CM_TOON_COMMON_INCLUDED
