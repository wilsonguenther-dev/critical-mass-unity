// fix/px-camera-in-editor: the third-person camera's fade volumes. Globals set by GroundView once a frame (outside any
// per-material buffer, so the SRP batcher is untouched); all zero (top-down, first person, menus) = no effect at all, the
// pixel path is unchanged.
//   _CMFadeVol[i]  : xy = world x, world z of a vertical cylinder's axis, z = radius, w = fade 0..1 (1 = gone)
//   _CMFadeVolY[i] : x = bottom, y = top (world y)
//   _CMFadeVolCount: how many are set (<= 8)
//   _CMFadeLens    : xyz = the lens, w = radius: any surface this close to the lens is not drawn (nothing ever sits in
//                    front of the HUD planes or fills the screen)
//   _CMFadeSelf    : xyz = the local fighter's feet, w = radius of his keep capsule (2.6 u tall): the follow camera's
//                    see-through line and the lens sphere never touch him
// Rigged characters (isChar) are never faded by the volumes, so a fighter standing in a faded crown stays solid.
#ifndef CM_FADE_VOLUMES_INCLUDED
#define CM_FADE_VOLUMES_INCLUDED

float4 _CMFadeVol[8];
float4 _CMFadeVolY[8];
float _CMFadeVolCount;
float4 _CMFadeLens;
float4 _CMFadeSelf;

bool CMFadeInSelf(float3 p)
{
    if (_CMFadeSelf.w <= 0.0) return false;
    float2 d = p.xz - _CMFadeSelf.xz;
    return dot(d, d) < _CMFadeSelf.w * _CMFadeSelf.w && p.y > _CMFadeSelf.y - 0.3 && p.y < _CMFadeSelf.y + 2.6;
}

void CMFadeVolumesClip(float3 p, float4 positionCS, float isChar)
{
    if (_CMFadeVolCount < 0.5 && _CMFadeLens.w <= 0.0) return;
    float amount = 0.0;
    if (_CMFadeLens.w > 0.0 && !CMFadeInSelf(p))
    {
        float3 dl = p - _CMFadeLens.xyz;
        if (dot(dl, dl) < _CMFadeLens.w * _CMFadeLens.w) amount = 1.0;
    }
    if (isChar < 0.5)
    {
        int n = (int)min(_CMFadeVolCount, 8.0);
        for (int i = 0; i < n; i++)
        {
            float4 v = _CMFadeVol[i];
            float2 d = p.xz - v.xy;
            if (dot(d, d) < v.z * v.z && p.y >= _CMFadeVolY[i].x && p.y <= _CMFadeVolY[i].y) amount = max(amount, v.w);
        }
    }
    if (amount <= 0.0) return;
    // the same interleaved gradient noise as CMToonFadeClip (stable, even, no texture); amount 1 removes every pixel
    float noise = frac(52.9829189 * frac(dot(positionCS.xy, float2(0.06711056, 0.00583715))));
    clip(noise - amount);
}

#endif
