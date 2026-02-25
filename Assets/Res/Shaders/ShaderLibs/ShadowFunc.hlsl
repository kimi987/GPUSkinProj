
#ifndef CUSTOM_SHADOW_FUNC_INPUT_INCLUDED
#define CUSTOM_SHADOW_FUNC_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

TEXTURE2D(_MainLightShadowmapTextureEx);
SAMPLER(sampler_MainLightShadowmapTextureEx);

float _ESMConst;
float _VSMMin;

float _EVSMConstX;
float _EVSMConstY;
float _EVSMMin;

float _SSBlurSize;

// 5x5 Tent filter (45 degree sloped triangles in U and V)
void SampleShadow_ComputeSamples_Tent_5x5WithScale(real4 shadowMapTexture_TexelSize, real2 coord, real scale, out real fetchesWeights[9], out real2 fetchesUV[9])
{
    // tent base is 5x5 base thus covering from 25 to 36 texels, thus we need 9 bilinear PCF fetches
    real2 tentCenterInTexelSpace = coord.xy * shadowMapTexture_TexelSize.zw;
    real2 centerOfFetchesInTexelSpace = floor(tentCenterInTexelSpace + 0.5);
    real2 offsetFromTentCenterToCenterOfFetches = tentCenterInTexelSpace - centerOfFetchesInTexelSpace;

    // find the weight of each texel based on the area of a 45 degree slop tent above each of them.
    real3 texelsWeightsU_A, texelsWeightsU_B;
    real3 texelsWeightsV_A, texelsWeightsV_B;
    SampleShadow_GetTexelWeights_Tent_5x5(offsetFromTentCenterToCenterOfFetches.x, texelsWeightsU_A, texelsWeightsU_B);
    SampleShadow_GetTexelWeights_Tent_5x5(offsetFromTentCenterToCenterOfFetches.y, texelsWeightsV_A, texelsWeightsV_B);

    // each fetch will cover a group of 2x2 texels, the weight of each group is the sum of the weights of the texels
    real3 fetchesWeightsU = real3(texelsWeightsU_A.xz, texelsWeightsU_B.y) + real3(texelsWeightsU_A.y, texelsWeightsU_B.xz);
    real3 fetchesWeightsV = real3(texelsWeightsV_A.xz, texelsWeightsV_B.y) + real3(texelsWeightsV_A.y, texelsWeightsV_B.xz);

    // move the PCF bilinear fetches to respect texels weights
    real3 fetchesOffsetsU = real3(texelsWeightsU_A.y, texelsWeightsU_B.xz) / fetchesWeightsU.xyz + real3(-2.5,-0.5,1.5);
    real3 fetchesOffsetsV = real3(texelsWeightsV_A.y, texelsWeightsV_B.xz) / fetchesWeightsV.xyz + real3(-2.5,-0.5,1.5);
    fetchesOffsetsU *= shadowMapTexture_TexelSize.xxx;
    fetchesOffsetsV *= shadowMapTexture_TexelSize.yyy;

    real2 bilinearFetchOrigin = centerOfFetchesInTexelSpace * shadowMapTexture_TexelSize.xy;
    fetchesUV[0] = bilinearFetchOrigin + real2(fetchesOffsetsU.x, fetchesOffsetsV.x) * scale;
    fetchesUV[1] = bilinearFetchOrigin + real2(fetchesOffsetsU.y, fetchesOffsetsV.x) * scale;
    fetchesUV[2] = bilinearFetchOrigin + real2(fetchesOffsetsU.z, fetchesOffsetsV.x) * scale;
    fetchesUV[3] = bilinearFetchOrigin + real2(fetchesOffsetsU.x, fetchesOffsetsV.y) * scale;
    fetchesUV[4] = bilinearFetchOrigin + real2(fetchesOffsetsU.y, fetchesOffsetsV.y) * scale;
    fetchesUV[5] = bilinearFetchOrigin + real2(fetchesOffsetsU.z, fetchesOffsetsV.y) * scale;
    fetchesUV[6] = bilinearFetchOrigin + real2(fetchesOffsetsU.x, fetchesOffsetsV.z) * scale;
    fetchesUV[7] = bilinearFetchOrigin + real2(fetchesOffsetsU.y, fetchesOffsetsV.z) * scale;
    fetchesUV[8] = bilinearFetchOrigin + real2(fetchesOffsetsU.z, fetchesOffsetsV.z) * scale;

    fetchesWeights[0] = fetchesWeightsU.x * fetchesWeightsV.x;
    fetchesWeights[1] = fetchesWeightsU.y * fetchesWeightsV.x;
    fetchesWeights[2] = fetchesWeightsU.z * fetchesWeightsV.x;
    fetchesWeights[3] = fetchesWeightsU.x * fetchesWeightsV.y;
    fetchesWeights[4] = fetchesWeightsU.y * fetchesWeightsV.y;
    fetchesWeights[5] = fetchesWeightsU.z * fetchesWeightsV.y;
    fetchesWeights[6] = fetchesWeightsU.x * fetchesWeightsV.z;
    fetchesWeights[7] = fetchesWeightsU.y * fetchesWeightsV.z;
    fetchesWeights[8] = fetchesWeightsU.z * fetchesWeightsV.z;
}
//PCF
half CustomSampleShadowmapFiltered(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, ShadowSamplingData samplingData)
{
    half attenuation;

    #if defined(SHADER_API_MOBILE) || defined(SHADER_API_SWITCH)
    //移动端版本 2x2
    half4 attenuation4;
    attenuation4.x = half(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz + samplingData.shadowOffset0.xyz * _SSBlurSize));
    attenuation4.y = half(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz + samplingData.shadowOffset1.xyz * _SSBlurSize));
    // attenuation4.z = half(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz + samplingData.shadowOffset2.xyz * _SSBlurSize));
    // attenuation4.w = half(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz + samplingData.shadowOffset3.xyz * _SSBlurSize));
    attenuation = dot(attenuation4, 0.25);
    #else
    // 5x5
    float fetchesWeights[9];
    float2 fetchesUV[9];
    
    SampleShadow_ComputeSamples_Tent_5x5WithScale(samplingData.shadowmapSize, shadowCoord.xy, _SSBlurSize, fetchesWeights, fetchesUV);
    
    attenuation = fetchesWeights[0] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[0].xy, shadowCoord.z));
    attenuation += fetchesWeights[1] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[1].xy, shadowCoord.z));
    attenuation += fetchesWeights[2] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[2].xy, shadowCoord.z));
    attenuation += fetchesWeights[3] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[3].xy, shadowCoord.z));
    attenuation += fetchesWeights[4] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[4].xy, shadowCoord.z));
    attenuation += fetchesWeights[5] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[5].xy, shadowCoord.z));
    attenuation += fetchesWeights[6] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[6].xy, shadowCoord.z));
    attenuation += fetchesWeights[7] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[7].xy, shadowCoord.z));
    attenuation += fetchesWeights[8] * SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, float3(fetchesUV[8].xy, shadowCoord.z));
    #endif

    return attenuation;
}

half CustomSampleShadowmap(TEXTURE2D_SHADOW_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, ShadowSamplingData samplingData, half4 shadowParams, bool isPerspectiveProjection = true)
{
    // Compiler will optimize this branch away as long as isPerspectiveProjection is known at compile time
    if (isPerspectiveProjection)
        shadowCoord.xyz /= shadowCoord.w;

    half attenuation;
    half shadowStrength = shadowParams.x;
    #ifdef _SOFT_SHADOW
   
    if (shadowParams.y != 0)
    {
        attenuation = CustomSampleShadowmapFiltered(TEXTURE2D_SHADOW_ARGS(ShadowMap, sampler_ShadowMap), shadowCoord, samplingData);
    }
    else
    #endif
    {
        
        // 1-tap hardware comparison
        attenuation = half(SAMPLE_TEXTURE2D_SHADOW(ShadowMap, sampler_ShadowMap, shadowCoord.xyz));
    }
    attenuation = 1.0 - shadowStrength + shadowStrength * attenuation;
    // Shadow coords that fall out of the light frustum volume must always return attenuation = 1.0
    // TODO: We could use branch here to save some perf on some platforms

    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
}

half CustomSampleDepth(TEXTURE2D_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord)
{
    return half(SAMPLE_DEPTH_TEXTURE_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, 9));
}


half CustomMainLightRealtimeDepth(float4 shadowCoord)
{
    return CustomSampleDepth(TEXTURE2D_ARGS(_MainLightShadowmapTextureEx, sampler_MainLightShadowmapTextureEx), shadowCoord);
}

int GetMipMapValueFromDepth(float deltaDepth)
{
    return floor(deltaDepth * 100);
}


// ESM Shadow
half SampleEsmPrefilteredShadowmap(TEXTURE2D_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord)
{
    half attenuation = 0;
    #ifdef _ESM_MIP
    half2 originMoments = SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, 0).rg;
    half deltaDepth = originMoments.x - shadowCoord.z;
    int mipMin = GetMipMapValueFromDepth(deltaDepth);
    int mipMax = mipMin + 1;
    attenuation = lerp(SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, mipMin).r, SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, mipMax ).r, deltaDepth * 100 - mipMin);
    #else
    attenuation = SAMPLE_TEXTURE2D(ShadowMap, sampler_ShadowMap, shadowCoord.xy).r;
    #endif

    #if UNITY_REVERSED_Z
    // e^(cz) * e^(-cd) 
    attenuation = saturate(exp(_ESMConst * shadowCoord.z) * exp(-_ESMConst * attenuation));

    #else
    // e^(-cz) * e^(cd)
    attenuation = saturate(exp(-_ESMConst * shadowCoord.z) * attenuation);
    #endif

    return 1.0 - attenuation;
}

// VSM Shadow

inline half remapping(half shadowFactor, half minVal)
{
    return saturate((shadowFactor - minVal)/ (1.0 - minVal));
}

inline half Chebyshev(half2 moments, half mean, half minVariance, half reduceLightBleeding)
{
    half variance = moments.y - (moments.x * moments.x);
    variance = max(variance, minVariance);

    half d = mean - moments.x;

    half pMax = remapping(variance / (variance + (d * d)), reduceLightBleeding);

    #if UNITY_REVERSED_Z
    half p = step(moments.x, mean);
    return max(p, pMax);
    #else
    half p = step(mean, moments.x);
    return max(p, pMax);
    #endif
}

// VSM F = V^2 / (V^2 + (z - d)^2)
half SampleVSMPreFilteredShadowmap(TEXTURE2D_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord)
{
    float depth = shadowCoord.z;
    float2 moments;
    #ifdef _VSM_MIP
    half2 originMoments = SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, 0).rg;
    half deltaDepth = originMoments.x - depth;
    int mipMin = GetMipMapValueFromDepth(deltaDepth);
    int mipMax = mipMin + 1;
    moments = lerp(SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, mipMin).rg, SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, mipMax ).rg, deltaDepth * 100 - mipMin);
    #else
    moments = SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, 4).rg;
    #endif
    half p = Chebyshev(moments, depth, 0.0001f, _VSMMin);
    return p;
}

// EVSM

inline half2 warpDepth(half depth)
{
    half pos = exp(_EVSMConstX * depth);
    half neg = -exp(-_EVSMConstY * depth);
    return half2(pos, neg);
}

half SampleEVSMPreFilteredShadowmap(TEXTURE2D_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord)
{
    half4 moments;
    #ifdef _EVSM_MIP
    half originMoment = SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, 0).r;
    half deltaDepth = originMoment - shadowCoord.z;
    int mipMin = GetMipMapValueFromDepth(deltaDepth);
    int mipMax = mipMin + 1;
    moments = lerp(SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, mipMin), SAMPLE_TEXTURE2D_LOD(ShadowMap, sampler_ShadowMap, shadowCoord.xy, mipMax ), deltaDepth * 100 - mipMin);
    #else
    moments = SAMPLE_TEXTURE2D(ShadowMap, sampler_ShadowMap, shadowCoord.xy);
    #endif
    // half4 moments = SAMPLE_TEXTURE2D(ShadowMap, sampler_ShadowMap, shadowCoord.xy);
    half2 positiveMoments = moments.xy;
    half2 negativeMoments = moments.zw;

    half2 depth = warpDepth(shadowCoord.z);
    half2 depthScale = half2(_EVSMConstX, _EVSMConstY) * depth;
    half2 minVariance = 0.0001f * depthScale * depthScale;
    half positiveResult = Chebyshev(positiveMoments, depth.x, minVariance.x, _EVSMMin);
    half negativeResult = Chebyshev(negativeMoments, depth.y, minVariance.y, _EVSMMin);

    half p = min(positiveResult, negativeResult);

    return p;
}


float ComputeMSMShadowIntensity(float4 b,float FragmentDepth){
    float L32D22=mad(-b[0],b[1],b[2]);
    float D22=mad(-b[0],b[0], b[1]);
    float SquaredDepthVariance=mad(-b[1],b[1], b[3]);
    float D33D22=dot(float2(SquaredDepthVariance,-L32D22),
    float2(D22, L32D22));
    float InvD22=1.0f/D22;
    float L32=L32D22*InvD22;
    float3 z;
    z[0]=FragmentDepth;
    float3 c=float3(1.0f,z[0],z[0]*z[0]);
    c[1]-=b.x;
    c[2]-=b.y+L32*c[1];
    c[1]*=InvD22;
    c[2]*=D22/D33D22;
    c[1]-=L32*c[2];
    c[0]-=dot(c.yz,b.xy);
    float InvC2=1.0f/c[2];
    float p=c[1]*InvC2;
    float q=c[0]*InvC2;
    float r=sqrt((p*p*0.25f)-q);
    z[1]=-p*0.5f-r;
    z[2]=-p*0.5f+r;
    float4 Switch=
    (z[2]<z[0])?float4(z[1],z[0],1.0f,1.0f):(
    (z[1]<z[0])?float4(z[0],z[1],0.0f,1.0f):
    float4(0.0f,0.0f,0.0f,0.0f));
    float Quotient=(Switch[0]*z[2]-b[0]*(Switch[0]+z[2])+b[1])
    /((z[2]-Switch[1])*(z[0]-z[1]));
    return saturate(Switch[2]+Switch[3]*Quotient);
}

const float4x4 msmRevertMatrix = {
    -0.6667, 0, 1.732, 0,
    0, 0.125, 0, 1,
    -0.75, 0, 1.229, 0,
    0, -0.125, 0, 1
};

float linstep(float min, float max, float v)  
{
    return clamp ((v - min) / (max - min), 0.0, 1.0);
}

float reduceLightBleeding(float p_max, float amount)  
{  
    return linstep(amount, 1, p_max);  
}  


// MSM
half SampleMSMPreFilteredShadowmap(TEXTURE2D_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord)
{
    if(shadowCoord.z > 1.0)
        return 1.0;
    // float4 moments = SAMPLE_TEXTURE2D(ShadowMap, sampler_ShadowMap, shadowCoord.xy) - float4(0.5, 0, 0.5, 0);

    // float4 b = float4(-0.667 * moments.x + 1.732 * moments.z, 0.125 * moments.y + moments.w, -0.75 * moments.x + 1.229 * moments.z, -0.125 * moments.y + moments.w);
    // const float a = 0.15;
    // float4 b1 = (1 - a) * b + (a * pow(float4(0, 0.63, 0, 0.63), 1));

    float4 moments = SAMPLE_TEXTURE2D(ShadowMap, sampler_ShadowMap, shadowCoord.xy);
    moments = lerp(moments, 0.5, 0.0001);
    
    float strength =  ComputeMSMShadowIntensity(moments, shadowCoord.z);

    return reduceLightBleeding(strength, 0.5);
}

half SampleShadowmapEX(TEXTURE2D_PARAM(ShadowMap, sampler_ShadowMap), float4 shadowCoord, half4 shadowParams, bool isPerspectiveProjection = true)
{
    if (isPerspectiveProjection)
        shadowCoord.xyz /= shadowCoord.w;

    half attenuation = 0;
    half shadowStrength = shadowParams.x;

    #if defined(_ESM) || defined(_ESM_MIP)
    attenuation = SampleEsmPrefilteredShadowmap(ShadowMap, sampler_ShadowMap, shadowCoord);
    #elif defined(_VSM) || defined(_VSM_MIP)
    attenuation = SampleVSMPreFilteredShadowmap(ShadowMap, sampler_ShadowMap, shadowCoord);
    #elif defined(_EVSM) || defined(_EVSM_MIP)
    attenuation = SampleEVSMPreFilteredShadowmap(ShadowMap, sampler_ShadowMap, shadowCoord);
    #elif defined(_MSM)
    attenuation = SampleMSMPreFilteredShadowmap(ShadowMap, sampler_ShadowMap, shadowCoord);
    #endif

    attenuation = 1.0 - shadowStrength + shadowStrength * attenuation;

    return BEYOND_SHADOW_FAR(shadowCoord) ? 1.0 : attenuation;
}


half CustomMainLightRealtimeShadow(float4 shadowCoord)
{
    #if !defined(MAIN_LIGHT_CALCULATE_SHADOWS) && !defined(_SOFT_SHADOW)
        return half(1.0);
    #endif
    
    #if defined(_ESM) || defined(_VSM) || defined(_EVSM) || defined(_ESM_MIP) || defined(_VSM_MIP) || defined(_EVSM_MIP) || defined(_MSM)
        return SampleShadowmapEX(TEXTURE2D_ARGS(_MainLightShadowmapTextureEx, sampler_MainLightShadowmapTextureEx), shadowCoord, GetMainLightShadowParams(), false);
    #elif defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
        return SampleScreenSpaceShadowmap(shadowCoord);
    #else
    ShadowSamplingData shadowSamplingData = GetMainLightShadowSamplingData();
    half4 shadowParams = GetMainLightShadowParams();
    return CustomSampleShadowmap(TEXTURE2D_ARGS(_MainLightShadowmapTexture, sampler_MainLightShadowmapTexture), shadowCoord, shadowSamplingData, shadowParams, false);  
    #endif
}

Light GetCustomMainLight(float4 shadowCoord)
{
    Light light = GetMainLight();
#if defined(_MAIN_LIGHT_SHADOWS) || defined(_SOFT_SHADOW)
    light.shadowAttenuation = max(shadowCoord.x, shadowCoord.y) >= 1 || min(shadowCoord.x, shadowCoord.y) <= 0 ? 1 : CustomMainLightRealtimeShadow(shadowCoord);
#endif
    return light;
}

#endif