// CM time of day (arena night life 1, 2026-11-13): one global grade shared by every lit world shader (CM/Toon,
// CM/WorldGround, CM/ToonGround, CM/WorldDecal) and the ambient life shaders. Set by Runtime/View/World/TimeOfDay.cs
// (TimeOfDayDirector) from a preset (Day, Dusk, Night). EVERY GLOBAL AT ZERO IS DAY (the identity): a material drawn
// before the director runs, an editor tool or a test sees exactly the look the game had before this file existed.
// Visual only: nothing here is read by the Sim.
//
// _CMTodKey      rgb: lit-band multiplier minus 1 (the key light colour x exposure); a: character lift 0..1 (how much
//                of the grade a character, a crystal or another "brightest layer" solid skips, art direction: units are
//                the brightest, most saturated layer at every time of day).
// _CMTodShade    rgb: shadow-band multiplier minus 1 (shadow tint); between bands the multiplier is interpolated.
// _CMTodAmbient  rgb: sky fill added in linear (scaled by 1 - lift); a: unused.
// _CMTodRim      rgb: rim (moon / low sun) colour, linear; a: strength on world solids (characters keep their team rim).
// _CMTodFog      rgb: height-fog (void) colour, linear; a: how much of it replaces the material's own fog colour.
// _CMTodVoid     rgb: the deep void colour at the bottom of the view; a: gradient amount (screen top -> bottom).
// _CMTodSky      x: stars 0..1, y: moon 0..1, z: glow decal and light pool boost (multiplier minus 1), w: view time (s).
// _CMTodMoon     xyz: moon direction (world, unit, from the camera), w: cos of the moon's angular radius.
#ifndef CM_TIME_OF_DAY_INCLUDED
#define CM_TIME_OF_DAY_INCLUDED

float4 _CMTodKey;
float4 _CMTodShade;
float4 _CMTodAmbient;
float4 _CMTodRim;
float4 _CMTodFog;
float4 _CMTodVoid;
float4 _CMTodSky;
float4 _CMTodMoon;

// The grade of a banded colour: band01 is 0 (shadow band) .. 1 (lit band); lift 0 (world) .. 1 (skips the grade).
half3 CMTodGrade(half3 color, half band01, half lift)
{
    half3 key = 1.0h + (half3)_CMTodKey.rgb;
    half3 shade = 1.0h + (half3)_CMTodShade.rgb;
    half3 m = lerp(shade, key, saturate(band01));
    half l = saturate(lift * (half)_CMTodKey.a);
    m = lerp(m, 1.0h, l);
    return color * m + (half3)_CMTodAmbient.rgb * (1.0h - l);
}

// Rim on world solids: a hard edge on silhouettes that face the key light (moonlit edges keep shapes readable at
// night). Off (strength 0) in Day.
half3 CMTodRim(half3 color, float3 normalWS, float3 viewDirWS, float3 lightDirWS, half lift)
{
    if (_CMTodRim.a <= 0.0) return color;
    half rim = 1.0h - saturate(dot(normalWS, normalize(viewDirWS)));
    half k = step(0.74h, rim) * step(0.05h, dot(normalWS, lightDirWS)) * (half)_CMTodRim.a * (1.0h - saturate(lift));
    return lerp(color, (half3)_CMTodRim.rgb, k);
}

float CMTodHash12(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

// The angle one screen pixel covers (radians), from the projection: no derivatives, so this is safe inside the
// per-pixel branches below (derivatives are undefined in divergent flow on some graphics APIs).
float CMTodPixelAngle()
{
    return 2.0 / max(1e-3, abs(GetViewToHClipMatrix()[1][1]) * _ScreenParams.y);
}

// Stars and the moon of the open void, by view direction (infinitely far: they do not slide when the camera pans).
// Stars are about 1.5 px at any resolution.
half3 CMTodSkyGlints(float3 vd)
{
    half3 add = 0;
    float pa = CMTodPixelAngle();
    if (_CMTodSky.x > 0.0)
    {
        float2 p = vd.xz / max(0.2, -vd.y) * 40.0;
        float2 cell = floor(p);
        float h = CMTodHash12(cell);
        if (h > 0.92)
        {
            float2 c = float2(CMTodHash12(cell + 17.3), CMTodHash12(cell + 41.9)) * 0.7 + 0.15;
            float px = max(1e-4, 40.0 * pa / max(0.04, vd.y * vd.y)) * 0.9;
            float d = length(frac(p) - c) / px;
            float big = step(0.988, h);
            float tw = 0.65 + 0.35 * sin(_CMTodSky.w * (1.3 + 2.0 * frac(h * 91.0)) + h * 60.0);
            float s = saturate((1.0 + big * 0.9) - d) * tw * (0.55 + 0.45 * big);
            add += half3(0.86, 0.90, 1.0) * (half)(s * _CMTodSky.x);
        }
    }
    if (_CMTodSky.y > 0.0)
    {
        float3 m = _CMTodMoon.xyz;
        float d = dot(vd, m);
        float cosR = _CMTodMoon.w;
        float sinR = sqrt(saturate(1.0 - cosR * cosR));
        float e = max(1e-6, pa * sinR);
        float disc = smoothstep(cosR - e, cosR + e, d);
        // Soft halo, about four radii.
        float cosH = 1.0 - (1.0 - cosR) * 16.0;
        float halo = saturate((d - cosH) / max(1e-4, 1.0 - cosH));
        halo = halo * halo * halo * 0.22;
        // Toon moon: a lit face, one shade band toward the lower right, two craters.
        float3 t = normalize(cross(m, float3(0, 1, 0)));
        float3 b = cross(t, m);
        float2 uv = float2(dot(vd, t), dot(vd, b)) / max(1e-4, sinR);
        half shade = step(1.0, length(uv - float2(-0.42, 0.30)) / 1.05);
        half crater = max(step(length(uv - float2(0.25, -0.18)), 0.22), step(length(uv - float2(-0.30, -0.40)), 0.14));
        half3 face = lerp(half3(0.96, 0.95, 0.88), half3(0.74, 0.77, 0.88), saturate(shade + crater * 0.6h));
        add = lerp(add, face, disc * (half)_CMTodSky.y) + half3(0.55, 0.62, 0.85) * (half)(halo * (1.0 - disc) * _CMTodSky.y);
    }
    return add;
}

// The fog (void) colour at a pixel: the preset's colour over the material's, a top-to-bottom gradient of the view,
// and, where the fog is (almost) full (the open void below the islands), the stars and the moon.
half3 CMTodFogColor(half3 materialFog, float3 positionWS, half f)
{
    half3 c = lerp(materialFog, (half3)_CMTodFog.rgb, (half)_CMTodFog.a);
    if (_CMTodVoid.a > 0.0 || _CMTodSky.x > 0.0 || _CMTodSky.y > 0.0)
    {
        float3 vd = normalize(positionWS - _WorldSpaceCameraPos);
        // The gameplay camera (pitch 55, FOV 35) looks 37.5 to 72.5 degrees down: -vd.y from about 0.61 to 0.95.
        half g = saturate((-vd.y - 0.62) / 0.30);
        c = lerp(c, (half3)_CMTodVoid.rgb, g * (half)_CMTodVoid.a);
        half open = saturate((f - 0.92h) * 12.5h);
        if (open > 0.0h) c += CMTodSkyGlints(vd) * open;
    }
    return c;
}

#endif // CM_TIME_OF_DAY_INCLUDED
