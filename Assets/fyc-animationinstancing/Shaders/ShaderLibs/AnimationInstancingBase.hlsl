#ifndef ANIMATION_INSTANCING_BASE_INCLUDED
#define ANIMATION_INSTANCING_BASE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Matrix.hlsl"
#include "UnPacked.hlsl"

TEXTURE2D(_BoneTexture); SAMPLER(sampler_BoneTexture);
float _BoneTextureBlockWidth;
float _BoneTextureBlockHeight;
float _BoneTextureWidth;
float _BoneTextureHeight;

#if (SHADER_TARGET < 30 || SHADER_API_GLES)
uniform float _FrameIndex;
uniform float _PreFrameIndex;
uniform float _TransitionProgress;
#else
UNITY_INSTANCING_BUFFER_START(Props)
    UNITY_DEFINE_INSTANCED_PROP(float, _PreFrameIndex)
#define PreFrameIndex_arr Props
    UNITY_DEFINE_INSTANCED_PROP(float, _FrameIndex)
#define FrameIndex_arr Props
    UNITY_DEFINE_INSTANCED_PROP(float, _TransitionProgress)
#define TransitionProgress_arr Props
UNITY_INSTANCING_BUFFER_END(Props)
#endif


float4x4 LoadMatFromTexture(uint frameIndex, uint boneIndex)
{
    uint iBlockWidth  = (uint)_BoneTextureBlockWidth;
    uint iBlockHeight = (uint)_BoneTextureBlockHeight;
    uint iTexWidth    = (uint)_BoneTextureWidth;

    uint blockCount = iTexWidth / max(iBlockWidth, 1u);

    int2 uv;
    uint blockRow = frameIndex / max(blockCount, 1u);
    uv.y = blockRow * iBlockHeight + boneIndex;
    uv.x = iBlockWidth * (frameIndex - blockCount * blockRow);

    // RGBAHalf: 2 pixels per bone
    // pixel1: pos.xyz, scale
    // pixel2: rot.xyzw
    float4 c1 = LOAD_TEXTURE2D_LOD(_BoneTexture, uv, 0);
    float4 c2 = LOAD_TEXTURE2D_LOD(_BoneTexture, int2(uv.x + 1, uv.y), 0);

    float4x4 m = ReconstructMatrixScale(c1.rgb, c2, c1.a);
    return m;
}

real4 skinning(inout AttributesAnimationInstancing IN)
{
    real4 w = IN.color;
    uint4 bone = IN.uv1;
    #if (SHADER_TARGET < 30 || SHADER_API_GLES)
    float curFrame = _FrameIndex;
    float preAniFrame = _PreFrameIndex;
    float progress = _TransitionProgress;
    #else
    float curFrame = UNITY_ACCESS_INSTANCED_PROP(FrameIndex_arr, _FrameIndex);
    float preAniFrame = UNITY_ACCESS_INSTANCED_PROP(PreFrameIndex_arr, _PreFrameIndex);
    float progress = UNITY_ACCESS_INSTANCED_PROP(TransitionProgress_arr, _TransitionProgress);
    #endif

    int preFrame = curFrame;
    int nextFrame = curFrame + 1;
    // real4x4 rootLocalMatrixPre = LoadMatFromTexture(preFrame, bone.x);
    real4x4 rootLocalMatrixPre = LoadMatFromTexture(preFrame, bone.x) * w.x;
    rootLocalMatrixPre += LoadMatFromTexture(preFrame, bone.y) * max(0, w.y);
    // rootLocalMatrixPre += LoadMatFromTexture(preFrame, bone.z) * max(0, w.z);
    // rootLocalMatrixPre += LoadMatFromTexture(preFrame, bone.w) * max(0, w.w);
    // real4x4 rootLocalMatrixNext = LoadMatFromTexture(nextFrame, bone.x);
    real4x4 rootLocalMatrixNext = LoadMatFromTexture(nextFrame, bone.x) * w.x;
    rootLocalMatrixNext += LoadMatFromTexture(nextFrame, bone.y) * max(0, w.y);
    // rootLocalMatrixNext += LoadMatFromTexture(nextFrame, bone.z) * max(0, w.z);
    // rootLocalMatrixNext += LoadMatFromTexture(nextFrame, bone.w) * max(0, w.w);

    real4 rootLocalPosPre = mul(IN.vertex, rootLocalMatrixPre);
    real4 rootLocalPosNext = mul(IN.vertex, rootLocalMatrixNext);
    real4 rootLocalPos = lerp(rootLocalPosPre, rootLocalPosNext, curFrame - preFrame);

    real3 rootLocalNormPre = mul(IN.normal.xyz,  (float3x3) rootLocalMatrixPre);
    real3 rootLocalNormNext = mul(IN.normal.xyz, (float3x3) rootLocalMatrixNext);
    IN.normal = normalize(lerp(rootLocalNormPre, rootLocalNormNext, curFrame - preFrame));
    real3 rootLocalTanPre = mul(IN.tangentOS.xyz, (float3x3) rootLocalMatrixPre);
    real3 rootLocalTanNext = mul(IN.tangentOS.xyz, (float3x3) rootLocalMatrixNext);
    IN.tangentOS.xyz = normalize(lerp(rootLocalTanPre, rootLocalTanNext, curFrame - preFrame));

    real4x4 rootLocalMatrixPreAni = LoadMatFromTexture(preAniFrame, bone.x);
    real4 rootLocalPreAni = mul(IN.vertex, rootLocalMatrixPreAni);
    rootLocalPos = lerp(rootLocalPos, rootLocalPreAni, (1.0f - progress) * (preAniFrame > 0.0f));

    return rootLocalPos;
}

real4 skinningShadow(uint4 bone, float4 positionOS)
{
#if (SHADER_TARGET < 30 || SHADER_API_GLES)
    float curFrame = _FrameIndex;
    float preAniFrame = _PreFrameIndex;
    float progress = _TransitionProgress;
#else
    float curFrame = UNITY_ACCESS_INSTANCED_PROP(FrameIndex_arr, _FrameIndex);
    float preAniFrame = UNITY_ACCESS_INSTANCED_PROP(PreFrameIndex_arr, _PreFrameIndex);
    float progress = UNITY_ACCESS_INSTANCED_PROP(TransitionProgress_arr, _TransitionProgress);
#endif

    int preFrame = curFrame;
    int nextFrame = curFrame + 1;
    real4x4 rootLocalMatrixPre = LoadMatFromTexture(preFrame, bone.x);
    real4x4 rootLocalMatrixNext = LoadMatFromTexture(nextFrame, bone.x);
    real4 rootLocalPosPre = mul(positionOS, rootLocalMatrixPre);
    real4 rootLocalPosNext = mul(positionOS, rootLocalMatrixNext);
    real4 rootLocalPos = lerp(rootLocalPosPre, rootLocalPosNext, curFrame - preFrame);

    real4x4 rootLocalMatrixPreAni = LoadMatFromTexture(preAniFrame, bone.x);
    real4 rootLocalPreAni = mul(positionOS, rootLocalMatrixPreAni);
    rootLocalPos = lerp(rootLocalPos, rootLocalPreAni, (1.0f - progress) * (preAniFrame > 0.0f));

    return rootLocalPos;
}

#endif