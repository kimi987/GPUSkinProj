#ifndef CUSTOM_SHADOW_CAST_FUNC_INCLUDED
#define CUSTOM_SHADOW_CAST_FUNC_INCLUDED

real LerpWhiteTo(real b, real t)
{
    real oneMinusT = 1.0 - t;
    return oneMinusT + b * t;
}

half Alpha(half albedoAlpha, half4 color, half cutoff)
{
#if !defined(_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A) && !defined(_GLOSSINESS_FROM_BASE_ALPHA)
    half alpha = albedoAlpha * color.a;
#else
    half alpha = color.a;
#endif

#if defined(_ALPHATEST_ON)
    clip(alpha - cutoff);
#endif
    return alpha;
}

half4 SampleAlbedoAlpha(float2 uv, TEXTURE2D_PARAM(albedoAlphaMap, sampler_albedoAlphaMap))
{
    return half4(SAMPLE_TEXTURE2D(albedoAlphaMap, sampler_albedoAlphaMap, uv));
}
#include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"

// Varyings CustomShadowPassVertex(Attributes input)
// {
//     Varyings output;
//     UNITY_SETUP_INSTANCE_ID(input);
//
//     output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
//     output.positionCS = GetShadowPositionHClip(input);
//     return output;
// }
//
//
half4 CustomShadowPassFragment(Varyings input) : SV_TARGET
{
    Alpha(SampleAlbedoAlpha(0, TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap)).a, _BaseColor, _Cutoff);
    return 0;
}

float4 GetTreeShadowPositionHClip(Attributes input, float3 positionWS)
{
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

    #if _CASTING_PUNCTUAL_LIGHT_SHADOW
    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
    #else
    float3 lightDirectionWS = _LightDirection;
    #endif

    float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));

    #if UNITY_REVERSED_Z
    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #else
    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
    #endif

    return positionCS;
}

#endif