// CM/Toon shared declarations (V-01, U-008: the one shared flat shader; art direction 2026-11-12 sections 2.2 and 2.3).
// Every pass of CMToon.shader includes this file so the UnityPerMaterial block is identical in all of them, which keeps
// the shader SRP Batcher compatible. Starting values live in the shader's Properties block; tuning goes through a U-###.
#ifndef CM_TOON_COMMON_INCLUDED
#define CM_TOON_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
// SRGBToLinear / LinearToSRGB for the vertex-colour mode.
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
// Time of day (arena night life 1): the global grade, the void colour, stars and moon; all globals 0 = Day.
#include "CMTimeOfDay.hlsl"

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
    // Art refresh 2 (2026-11-12): painted face decal over vertex colour (UV0, mixed by its alpha before the bands), the
    // Kaiju berserk darkening of fixed colours, and a per-vertex outline weight in UV4.x (0 = no hull: faces).
    float _DecalOn;
    float _Berserk;
    half4 _BerserkMul;
    half4 _BerserkTint;
    half _BerserkTintAmount;
    float _OutlineWeightUV4;
    // World reactions (2026-11-12, physics audit seat "world reactions"): _SwayOn reads UV5 (pivot xz, weight, kind) and
    // displaces the vertex in every pass; _WaterFlow scrolls the water stripes and draws ripple rings; _WobbleUV4Y reads
    // a hair / scarf / tabard wobble weight from UV4.y (art supplies it; 0 everywhere = no motion). All default 0.
    float _SwayOn;
    float _WaterFlow;
    float _WobbleUV4Y;
    // Sharpness switch "normals" (batch 12 AB): 0 the imported normals, 1 the by-position smoothed ones in UV3
    // (Editor/Art/SharpNormalsPostprocessor; characters only, ArtMaterials sets it). Default 0: no other mesh reads UV3 here.
    float _SmoothNormals;
    // b14 polish (Kaiju readability at night): a type hue as a soft fresnel emissive plus a per-type pattern on the body
    // (KaijuAnimator sets both through its one property block; 0 on every other material, so nothing else changes).
    half4 _KaijuGlow;
    float _KaijuPattern;
    // b15 view polish r2 (Kaiju identity): the Kaiju's tone (rgb a linear tint, a a saturation multiplier about the luma).
    // a above 0 marks a Kaiju (KaijuAnimator sets it hatched or not; 0 on every other material, so nothing else changes):
    // its body skips the character band fill and gain and takes half the time-of-day grade (CM_KAIJU_TOD_SHARE).
    half4 _KaijuTone;
    // b14 polish (team read from above): share of the team cloth laid over top-facing fixed (code 0) colours.
    half _TeamTopTint;
CBUFFER_END

// Outside the per-material block (textures are not CBUFFER members; SRP Batcher compatible).
TEXTURE2D(_FaceTex);
SAMPLER(sampler_FaceTex);

// World reactions globals (contract C1 of the physics build list; set by Runtime/View/World/ForceField.cs). Every one is
// 0 until it runs, which means no motion at all. PR #30's _CMFxImpulses / _CMFxImpulseCount are retired.
// _CMForceTex: RGBA half over a square window following the camera target: RG push (world x, z), B pressure (walkers,
// bridge sag, water rings). _CMForceTexWindow: world origin x, world origin z, window size (u), unused.
// _CMForces[16 x 2]: the 16 live force slots, (pos x, pos z (world), radius, strength x remaining life) and
// (dir x, dir z, age s, shape); _CMForceCount slots used. _CMWind: world dir x, dir z, strength 0..1, gust frequency
// (Hz). _CMSwayParams: x amplitude (0 off, 1 full, 0.25 reduced motion), y bridge sag (u at full pressure), z 1 when
// _CMForceTex is bound (Medium, High; Low reads the 16 slots instead), w view time (s).
TEXTURE2D(_CMForceTex);
SAMPLER(sampler_CMForceTex);
float4 _CMForceTexWindow;
float4 _CMForces[32];
float _CMForceCount;
float4 _CMWind;
float4 _CMSwayParams;

// follow-camera-2: the low views' see-through line. Globals (set by GroundView once a frame, outside the per-material
// buffer so the SRP batcher is untouched): _CMFadeCam.xyz = camera position, w = 1 on / 0 off; _CMFadeTarget.xyz = the
// followed fighter's chest, w = tube radius. Surfaces near the sight line lose up to 80 percent of their pixels in a
// screen-space dither (clip, no blending, no extra pass); ViewCull.FadeAmount in C# is the same maths (tests).
float4 _CMFadeCam;
float4 _CMFadeTarget;
// b15 combat feel (audit item 8): _CMFadeKeep.xyz = the chest of the fighter the local player just hit, w = keep radius
// (0 = none). Surfaces inside that sphere are never dithered, so the victim of your own hit stays solid for ~0.35 s.
float4 _CMFadeKeep;

void CMToonFadeClip(float3 positionWS, float4 positionCS)
{
    if (_CMFadeCam.w < 0.5) return;
    if (_CMFadeKeep.w > 0.0 && distance(positionWS, _CMFadeKeep.xyz) < _CMFadeKeep.w) return;
    float3 a = _CMFadeCam.xyz;
    float3 s = _CMFadeTarget.xyz - a;
    float t = dot(positionWS - a, s) / max(dot(s, s), 1e-4);
    if (t <= 0.02 || t >= 0.85) return;
    float d = length(a + s * t - positionWS);
    float r = _CMFadeTarget.w;
    float amount = saturate((r - d) / (r * 0.5)) * 0.8;
    // Interleaved gradient noise on the pixel position: a stable, even dither with no texture.
    float n = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
    clip(n - amount);
}

// Shared vertex input. uv0 is read only when _DecalOn is set and outlineW (UV4.x, baked by ArtImportPostprocessor) only
// when _OutlineWeightUV4 is set, for the same reason as UV3 below: meshes without the channel are never asked for it.
// Shared vertex input. color.r is the team mask (vertex colour layer "TeamMask", U-043):
// 1 paints _TeamColor, 0 keeps _BaseColor, values between lerp.
// texcoord3.xyz is a smoothed outline direction (object space), read only when _OutlineSource is 2: a mesh without UV3
// does not get zeros on every graphics API (Metal fed the capsule primitive another channel and broke its line).
// Named CMToonAttributes because URP's ShadowCasterPass.hlsl declares its own "Attributes".
struct CMToonAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    half4  color      : COLOR;
    float2 uv0        : TEXCOORD0;
    float3 extrudeOS  : TEXCOORD2;
    float3 outlineOS  : TEXCOORD3;
    float2 outlineW   : TEXCOORD4;
    float4 sway       : TEXCOORD5;
};

// Sharpness switch "normals" (batch 12 AB): the lighting normal of a vertex. With _SmoothNormals 0 exactly the imported
// normal. Else UV3 (the outlineOS slot; characters draw their hull from TANGENT, _OutlineSource 3, so UV3 is free on them)
// holds the 60 degree by-position smoothed normal in the vertex's own frame: x along the tangent made orthogonal to N,
// y along N x T, z along N. UV3 is not skinned but N and T are, so the frame rebuilt here turns with the bones
// (SharpNormalsPostprocessor.DecodeInFrame is the C# twin). T nearly parallel to N: the imported normal.
float3 CMToonSmoothNormalOS(CMToonAttributes input)
{
    if (_SmoothNormals <= 0.0) return input.normalOS;
    float3 n = normalize(input.normalOS);
    float3 t = input.tangentOS.xyz - n * dot(n, input.tangentOS.xyz);
    float tl = length(t);
    if (tl < 0.05) return input.normalOS;
    t /= tl;
    float3 e = input.outlineOS;
    float3 s = e.x * t + e.y * cross(n, t) + e.z * n;
    return normalize(lerp(n, s, _SmoothNormals));
}

// Vertex-colour mode (_ColorMode 1; Blender art import 2026-11-12). The art's colour set "Col" carries the lit colour
// in RGB (sRGB hex from the bible) and a mask code in A, read here in eighths:
//   0 fixed (banded)        1 fixed glow, lined (self-lit)   2 fixed glow (self-lit, no hull; alpha 0.25: the Farmer's
//                                                            biomass glow, never team-swapped)
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
    // Kaiju berserk (kaiju v3 report, kaiju_palette.py): fixed colours only, in sRGB, before the bands:
    // fixed' = lerp(fixed, lerp(fixed * (0.82, 0.56, 0.78), Corruption #8E3FD1, 0.15), _Berserk).
    if (code == 0 && _Berserk > 0.0)
        rgb = lerp(rgb, lerp(rgb * _BerserkMul.rgb, LinearToSRGB(_BerserkTint.rgb), _BerserkTintAmount), _Berserk);
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

// The painted face decal (Kaiju_Face.png, Warrior_Face.png, MinerFace.png) over the linear albedo, by its alpha; faces
// that show no face map to a transparent texel. Banded afterwards like the vertex colour it covers.
half3 CMToonDecal(half3 albedo, float2 uv)
{
    if (_DecalOn < 0.5) return albedo;
    half4 d = SAMPLE_TEXTURE2D(_FaceTex, sampler_FaceTex, uv);
    return lerp(albedo, d.rgb, d.a);
}

// Textured Warrior pilot (batch 12 AB, part B): keyword _CMTOON_BASEMAP, enabled only on the materials of a model with a
// base map (ArtMaterials), so every untextured model compiles and draws exactly as before. _BaseMap: sRGB rgb = the lit
// colour (baked from the generator's original 4K texture), alpha = the team mask: 0 fixed, 0.5 team cloth (banded),
// 1 team glow (self-lit); in between (bilinear, mips, compression) lerps. Team channels swap as the vertex-colour codes
// do (CMToonSwap, in sRGB). Replaces the vertex-colour albedo; the bands, rim, time of day and fog that follow are shared.
TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

void CMToonBaseMapColour(float2 uv, out half3 albedo, out half selfLit)
{
    half4 t = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
#if defined(UNITY_COLORSPACE_GAMMA)
    half3 srgb = t.rgb;
#else
    half3 srgb = LinearToSRGB(t.rgb);
#endif
    half cloth = saturate(t.a * 2.0h);
    half glow = saturate(t.a * 2.0h - 1.0h);
    half3 c = lerp(srgb, CMToonSwap(srgb, _TeamCloth.rgb, _DefaultCloth.rgb), cloth);
    c = lerp(c, CMToonSwap(srgb, _TeamGlow.rgb, _DefaultGlow.rgb), glow);
    selfLit = glow > 0.5h ? 1.0h : 0.0h;
#if defined(UNITY_COLORSPACE_GAMMA)
    albedo = c;
#else
    albedo = SRGBToLinear(c);
#endif
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

// ------------------------------------------------------------------------------------------- world reactions
// UV of a world xz in the force window. The window texture is written with raw clip positions (ForceField's stamp mesh),
// so on platforms whose texture rows start at the top the logical v flips, exactly as the core Blit full-screen
// triangle does (GetFullScreenTriangleTexCoord).
float2 CMForceUV(float2 xz)
{
    float2 uv = (xz - _CMForceTexWindow.xy) / max(_CMForceTexWindow.z, 1e-3);
#if UNITY_UV_STARTS_AT_TOP
    uv.y = 1.0 - uv.y;
#endif
    return uv;
}

// Push (xy, world x / z) and pressure (z) at a world xz: the force texture at Medium and High, else the 16 force
// slots (Low). Fades to 0 over the outer 10% of the window so nothing pops as it scrolls.
float3 CMForceAt(float2 xz)
{
    float3 f = float3(0.0, 0.0, 0.0);
    if (_CMSwayParams.z > 0.5)
    {
        float2 uv = CMForceUV(xz);
        float edge = saturate(min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y)) * 10.0);
        if (edge > 0.0)
            f = SAMPLE_TEXTURE2D_LOD(_CMForceTex, sampler_CMForceTex, uv, 0).xyz * edge;
    }
    else
    {
        // Low tier: the 16 force slots directly (an expanding push front for radial shapes would need the texture).
        [unroll] for (int i = 0; i < 16; i++)
        {
            float4 a = _CMForces[i * 2];
            float2 d = xz - a.xy;
            float dist = length(d);
            float k = saturate(1.0 - dist / max(a.z, 1e-3)) * a.w * step((float)i, _CMForceCount - 0.5);
            f.xy += d / max(dist, 1e-3) * k;
            f.z += k;
        }
    }
    return f;
}

float CMToonHash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// The ONE sway function (world space offset). sway = UV5 baked by WorldView.MakeGroup: pivot x, pivot z (world), weight
// (0 at the root, 1 at the free tip: height squared for foliage, sin along the span for bridge decks), kind:
// 1 grass / flowers, 2 bush, 3 tree, 4 banner, 5 bridge deck, 6 lantern (pendulum), 7 water (no vertex motion).
// Wind (2-octave sine, per-piece phase from the pivot hash) plus the force field read at the pivot (a whole tuft bends
// together; bridge decks read it at the vertex so the deck dips under the walker). Clamped to 0.6 u (bounds grow 0.6).
float3 CMToonSwayOffset(float3 positionWS, float4 sway)
{
    float w = sway.z;
    float amp = _CMSwayParams.x;
    if (w <= 0.0 || amp <= 0.0) return float3(0.0, 0.0, 0.0);
    int kind = (int)round(sway.w);
    if (kind <= 0 || kind >= 7) return float3(0.0, 0.0, 0.0);
    float2 pivot = sway.xy;
    float t = _CMSwayParams.w;
    float phase = CMToonHash12(pivot);
    float windAmp = kind == 1 ? 0.10 : (kind == 2 ? 0.05 : (kind == 3 ? 0.06 : (kind == 4 ? 0.16 : (kind == 6 ? 0.05 : 0.0))));
    float pushAmp = kind == 1 ? 0.45 : (kind == 2 ? 0.22 : (kind == 3 ? 0.10 : (kind == 4 ? 0.30 : (kind == 6 ? 0.25 : 0.0))));
    float slow = kind == 4 ? 5.3 : 1.7;
    float fast = kind == 4 ? 9.1 : 3.9;
    float gust = 0.55 + 0.45 * sin(t * 6.2831853 * _CMWind.w + dot(pivot, float2(0.11, 0.07)));
    float wave = 0.65 * sin(t * slow + phase * 6.2831853) + 0.35 * sin(t * fast + phase * 11.0);
    float2 off = _CMWind.xy * (_CMWind.z * gust * (0.6 + 0.4 * wave)) * windAmp;
    float3 f = CMForceAt(kind == 5 ? positionWS.xz : pivot);
    off += f.xy * pushAmp;
    if (kind == 3 || kind == 6)
    {
        // Tree shake / lantern swing: an oscillation whose size follows the local push.
        float m = saturate(length(f.xy));
        float fl = length(f.xy);
        float2 axis = fl > 1e-3 ? f.xy / max(fl, 1e-3) : float2(0.7071, 0.7071);
        off += axis * (m * sin(t * (kind == 3 ? 21.0 : 7.0) + phase * 6.2831853) * (kind == 3 ? 0.06 : 0.12));
    }
    off *= w * amp;
    float len = length(off);
    if (len > 0.6) { off *= 0.6 / len; len = 0.6; }
    float3 o = float3(off.x, -0.5 * len * len, off.y);
    if (kind == 1) o.y -= saturate(f.z) * 0.12 * w * amp;                         // flattened under a blast or a walker
    if (kind == 5)
    {
        float p = saturate(f.z);
        o.y -= (_CMSwayParams.y * p + 0.02 * p * sin(t * 25.0 + phase * 6.2831853)) * w * amp;   // sag + plank bounce
    }
    return o;
}

// Hair / scarf / tabard wobble from UV4.y (no joints for them on the KayKit rig). Art supplies the weight; 0 = still.
float3 CMToonWobble(float3 positionWS, float weight)
{
    float amp = _CMSwayParams.x;
    if (weight <= 0.0 || amp <= 0.0) return float3(0.0, 0.0, 0.0);
    float t = _CMSwayParams.w;
    float3 o = float3(sin(t * 7.0 + positionWS.y * 3.0), 0.0, cos(t * 6.1 + positionWS.x * 2.7)) * 0.025;
    o.xz += _CMWind.xy * (0.03 * _CMWind.z);
    return o * saturate(weight) * amp;
}

// World position of a vertex in every pass: the plain transform plus the sway (world meshes with UV5) and the wobble.
float3 CMToonWorldPos(CMToonAttributes input)
{
    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    if (_SwayOn > 0.5) positionWS += CMToonSwayOffset(positionWS, input.sway);
    if (_WobbleUV4Y > 0.5) positionWS += CMToonWobble(positionWS, input.outlineW.y);
    return positionWS;
}

// Water (_WaterFlow): stripes that fall down steep faces (the waterfall) and drift on flat ones, plus ripple rings where
// the force field has pressure (fighters wading, splashes). Lit-pass only.
half3 CMToonWater(half3 color, float3 positionWS, float3 normalWS)
{
    if (_WaterFlow <= 0.0) return color;
    float t = _CMSwayParams.w;
    float steep = 1.0 - saturate(abs(normalWS.y));
    float coord = steep > 0.5 ? positionWS.y * 1.6 + t * 1.8 * _WaterFlow
                              : dot(positionWS.xz, float2(0.7071, 0.7071)) * 1.2 - t * 0.35 * _WaterFlow;
    float stripe = step(0.82, frac(coord + 0.15 * sin(positionWS.x * 1.3 + positionWS.z * 0.9)));
    half3 light = saturate(color * 1.18h + 0.04h);
    color = lerp(color, light, stripe * 0.85h);
    float p = saturate(CMForceAt(positionWS.xz).z);
    float ring = step(0.7, frac(p * 3.0 - t * 2.0)) * step(0.05, p);
    return lerp(color, light, ring * 0.6 * (1.0 - steep));
}

// Clip position of the lit, depth and normals passes: the displaced world position, plus the foam extrusion when the
// material sets _ExtrudePx (only the water look; other meshes may carry no UV2, so it is never read for them).
float4 CMToonPositionCS(CMToonAttributes input)
{
    float4 positionCS = TransformWorldToHClip(CMToonWorldPos(input));
    if (_ExtrudePx > 0.0 && dot(input.extrudeOS, input.extrudeOS) > 0.25)
        positionCS = CMToonPushPx(positionCS, input.extrudeOS, _ExtrudePx);
    return positionCS;
}

// Height fog below the islands (environment kit v3: starts at y -3.5): 0 above _FogTop, _FogAmount at _FogBottom.
// Time of day: the fog colour comes from CMTodFogColor (the preset's void colour and gradient, stars and moon where
// the fog is full); with every time-of-day global at 0 it is exactly _FogColor, as before.
half3 CMToonFog(half3 color, float3 positionWS)
{
    float f = _FogAmount * saturate((_FogTop - positionWS.y) / max(1e-3, _FogTop - _FogBottom));
    if (f <= 0.0) return color;
    return lerp(color, CMTodFogColor(_FogColor.rgb, positionWS, (half)f), f);
}

// b14 polish (team read from above): a top-facing fixed-colour pixel (vertex-colour mode, code 0) mixed _TeamTopTint
// toward the team cloth, ramping in from N.y 0.70 to 0.86 (head and helmet tops, shoulders, pack lids; never faces or
// sides). Team channels, glows and the textured pilots are untouched.
half3 CMToonTeamTop(half3 albedo, half4 col, float3 normalWS)
{
    if (_TeamTopTint <= 0.0h || _ColorMode < 0.5) return albedo;
    if (CMToonMaskCode(col.a) != 0) return albedo;
    half t = _TeamTopTint * (half)smoothstep(0.70, 0.86, normalWS.y);
    return lerp(albedo, _TeamCloth.rgb, t);
}

// b14 polish: the Kaiju type glow. A soft fresnel band (wider and softer than the hard team rim, inside it) and a pattern
// that differs in SHAPE per type, so the type reads without hue (colour-blind safe): 1 GROVE leafy crest on the top
// surfaces, 2 BRUTE separate back plates, 3 TIMBER horizontal bark bands, 4 MIRE mist spots, 5 ELDER a spine ridge.
// Patterns run in object space measured in world units (they ride with the body, no crawl when it walks).
float CMToonKaijuHash(float3 c)
{
    return frac(sin(dot(c, float3(12.9898, 78.233, 37.719))) * 43758.5453);
}

// b15 view polish: the parts models' fresnel weight (KaijuTypeLook.PartsFresnel mirrors it; a test reads both).
#define CM_KAIJU_PARTS_FRESNEL 0.50
// b15 view polish r2 (Editor review round 2: the round-1 lift washed the Kaiju pale, 8..47% of body pixels clipped, and
// at Night they stayed as bright as Day). The Kaiju takes this share of the time-of-day grade (KaijuTypeLook.TodShare)
// and its toned albedo never passes CM_KAIJU_TONE_MAX in any channel (KaijuTypeLook.ToneMax), so no band can clip.
#define CM_KAIJU_TOD_SHARE 0.5
#define CM_KAIJU_TONE_MAX 0.92

// 1 on a Kaiju (_KaijuTone.a above 0), else 0.
half CMToonKaijuW() { return step(0.001h, _KaijuTone.a); }

// The Kaiju's identity tone (KaijuTypeLook.Tone mirrors it): saturation about the linear luma, a tint, then a hue-kept
// cap at CM_KAIJU_TONE_MAX. Fixed colours only (vertex code 0): the team plates, crystals and glows keep their colour,
// so the side still reads. Identity off a Kaiju.
half3 CMToonKaijuTone(half3 albedo, half4 col)
{
    if (_KaijuTone.a <= 0.0h) return albedo;
    if (_ColorMode > 0.5 && CMToonMaskCode(col.a) != 0) return albedo;
    half l = dot(albedo, half3(0.2126h, 0.7152h, 0.0722h));
    half3 c = max(0.0h, l + (albedo - l) * _KaijuTone.a) * _KaijuTone.rgb;
    half m = max(c.r, max(c.g, c.b));
    return m > (half)CM_KAIJU_TONE_MAX ? c * ((half)CM_KAIJU_TONE_MAX / m) : c;
}

half3 CMToonKaijuGlow(half3 color, float3 normalWS, float3 viewDirWS, float3 positionWS)
{
    if (_KaijuGlow.a <= 0.0h) return color;
    float fres = 1.0 - saturate(dot(normalWS, normalize(viewDirWS)));
    float rimGlow = smoothstep(0.30, 0.78, fres);
    float sc = max(1e-4, length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)));
    float3 p = TransformWorldToObject(positionWS) * sc;
    float up = normalWS.y;
    float pat = 0.0;
    int kind = (int)round(_KaijuPattern);
    if (kind == 1)
    {
        // leafy crest: scalloped leaf patches over every upward face
        float leaf = sin(p.x * 3.1) * sin(p.z * 3.1 + p.y * 1.7);
        pat = smoothstep(0.30, 0.55, up) * step(0.30, leaf);   // b14 final look: leaf patches, not the whole top (was -0.15: lime body)
    }
    else if (kind == 2)
    {
        // back plates: a grid of separate plates (gaps between them) on the upward faces
        float2 f = frac(p.xz * 1.35);
        float edge = min(min(f.x, 1.0 - f.x), min(f.y, 1.0 - f.y));
        float2 id = floor(p.xz * 1.35);
        pat = step(0.40, up) * step(0.14, edge) * step(0.5, frac((id.x + id.y) * 0.5));   // b14 final look: every other plate (was all: orange body)
    }
    else if (kind == 3)
    {
        // bark bands: pale horizontal bands round the whole body
        pat = step(frac(p.y * 1.4), 0.30) * step(-0.35, up);
    }
    else if (kind == 4)
    {
        // mist spots: scattered round spots on the upper body
        float3 cell = floor(p * 2.2);
        float3 fr = frac(p * 2.2) - 0.5;
        pat = step(0.45, CMToonKaijuHash(cell)) * step(length(fr), 0.34) * step(-0.10, up);
    }
    else if (kind == 5)
    {
        // spine ridge: a band along the back (object x near 0) on the upward faces
        pat = smoothstep(0.35, 0.60, up) * step(abs(p.x), 0.42);
    }
    half k = (half)saturate(_KaijuGlow.a);
    half3 glow = _KaijuGlow.rgb;
    // b14 final look (seen in engine at night): pattern 0.80 -> 0.58 and fresnel 0.55 -> 0.32; at the old values the glow
    // swallowed the body (GROVE read as a lime blob, BRUTE as solid orange).
    color = lerp(color, glow, (half)(pat * 0.58) * k);
    // b15 view polish (Editor review item 1): a parts model (pattern 0: its plates and vents are modelled) has no painted
    // pattern to swallow the body, so its fresnel is stronger and starts a little further in (CMToonKaijuFresnel), which
    // separates the silhouette from the cream hill paving; the painted-pattern models keep the b14 0.32.
    half fw = kind == 0 ? (half)CM_KAIJU_PARTS_FRESNEL : 0.32h;
    rimGlow = kind == 0 ? smoothstep(0.22, 0.70, fres) : rimGlow;
    // b15 view polish r2: the edge leans TOWARD the type hue (was added on top: the added glow clipped the lit band)
    return lerp(color, glow, (half)(rimGlow * fw) * k);
}

// Time of day: how much of the grade a CM/Toon material skips. Characters and the Kaiju (rim strength above 0: world
// materials set 0, art direction 2.2) and crystals (lit multiplier above 1) are the bright layer and keep most of
// their Day value; the rest of the world takes the full grade.
half CMToonTodLift()
{
    return saturate(max(step(0.001h, _RimStrength), (_LitMultiplier - 1.0h) * 1.25h));
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
    // 3 (art refresh 2): the smoothed direction in TANGENT.xyz, which skinning deforms (characters and Kaiju).
    float3 dirOS = _OutlineSource > 2.5 ? input.tangentOS.xyz
                 : _OutlineSource > 1.5 ? input.outlineOS : (_OutlineSource > 0.5 ? input.positionOS.xyz : input.normalOS);
    float4 positionCS = TransformWorldToHClip(CMToonWorldPos(input));   // same displaced position as the fill
    float3 dirWS = TransformObjectToWorldDir(dirOS, true);
    float2 dirCS = mul((float3x3)GetWorldToHClipMatrix(), dirWS).xy;
    float len = length(dirCS);
    if (len > 1e-6)
    {
        half w = _OutlineWeightUV4 > 0.5 ? saturate(input.outlineW.x) : 1.0h;
        float2 offset = dirCS / len * (_OutlinePx * CMToonOutlineMask(input.color) * w * 2.0 / 1080.0);
        offset.x *= _ScreenParams.y / _ScreenParams.x;
        positionCS.xy += offset * positionCS.w;
    }
    return positionCS;
}

#endif // CM_TOON_COMMON_INCLUDED
