// CM/Toon shared declarations (V-01, U-008: the one shared flat shader).
// Every pass of CMToon.shader includes this file so the UnityPerMaterial block is identical in all of them, which keeps
// the shader SRP Batcher compatible. Starting values live in the shader's Properties block; tuning goes through a U-###.
#ifndef CM_TOON_COMMON_INCLUDED
#define CM_TOON_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

CBUFFER_START(UnityPerMaterial)
    half4 _BaseColor;
    half _ShadeMultiplier;
    half _RampThreshold;
    half4 _TeamColor;
    half4 _OutlineColor;
    float _OutlineWidth;
    half _RimWidth;
    half _RimStrength;
CBUFFER_END

// Shared vertex input. color.r is the team mask (vertex colour layer "TeamMask", U-043):
// 1 paints _TeamColor, 0 keeps _BaseColor, values between lerp.
// Named CMToonAttributes because URP's ShadowCasterPass.hlsl declares its own "Attributes".
struct CMToonAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    half4  color      : COLOR;
};

#endif // CM_TOON_COMMON_INCLUDED
