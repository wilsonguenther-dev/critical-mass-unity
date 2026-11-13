// DG-20 optional occluder dither hook (not wired yet: CMToon.shader belongs to the shader owner).
//
// SeeThrough.cs publishes, every frame:
//   _CM_SeeThroughPos    xyz = the player's chest (world), w = 0..1 strength (0 while he is not covered)
//   _CM_SeeThroughRadius world units around him, measured at his depth, that the dither opens
//
// To use it, the CMToon owner adds two lines to the ForwardLit pass of CMToon.shader (and, if wanted, the Outline pass):
//   #include "Assets/_Game/Runtime/Camera/Shaders/CMSeeThroughHook.hlsl"    (after CMToonCommon.hlsl)
//   CMSeeThroughClip(input.positionWS, input.positionCS);                   (first line of frag)
// Surfaces nearer the camera than the player by 1 u or more, inside the circle, are then clipped in an ordered 4x4
// dither that thins toward the centre. The player's own body is never clipped (it is not nearer than himself).
#ifndef CM_SEE_THROUGH_HOOK_INCLUDED
#define CM_SEE_THROUGH_HOOK_INCLUDED

float4 _CM_SeeThroughPos;
float _CM_SeeThroughRadius;

static const float CMSeeThroughBayer[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

float CMSeeThroughBayer4(float2 px)
{
    uint2 p = (uint2)px & 3u;
    return (CMSeeThroughBayer[p.y * 4u + p.x] + 0.5) / 16.0;
}

void CMSeeThroughClip(float3 positionWS, float4 positionCS)
{
    float strength = _CM_SeeThroughPos.w;
    if (strength <= 0.0) return;
    float3 fragVS = TransformWorldToView(positionWS);
    float3 playerVS = TransformWorldToView(_CM_SeeThroughPos.xyz);
    float fragDepth = -fragVS.z;
    float playerDepth = -playerVS.z;
    if (fragDepth <= 0.0 || playerDepth <= 0.0 || fragDepth > playerDepth - 1.0) return;
    // Compare directions (x/z, y/z) so the circle is round on screen, sized in world units at the player's depth.
    float2 d = fragVS.xy / fragDepth - playerVS.xy / playerDepth;
    float r = _CM_SeeThroughRadius / playerDepth;
    float k = 1.0 - saturate(length(d) / max(r, 1e-4));
    clip(CMSeeThroughBayer4(positionCS.xy) - k * strength * 0.85);
}

#endif
