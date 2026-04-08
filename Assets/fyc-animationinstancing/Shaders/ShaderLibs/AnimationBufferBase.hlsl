#ifndef ANIMATION_BUFFER_BASE_INCLUDED
#define ANIMATION_BUFFER_BASE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Matrix.hlsl"
#include "UnPacked.hlsl"

TEXTURE2D(_BoneTexture); SAMPLER(sampler_BoneTexture);
float _BoneTextureBlockWidth;
float _BoneTextureBlockHeight;
float _BoneTextureWidth;
float _BoneTextureHeight;

struct DrawFrameData
{
    float4x4 WorldMatrix;
    float FrameIndex;
    float PreFrameIndex;
    float TransitionProgress;
    float Padding;
};

StructuredBuffer<DrawFrameData> _OutDrawFrameData;


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

float4 skinning(inout AttributesAnimationInstancing IN, uint instanceID)
{
    float4 w = IN.color;
    uint4 bone = IN.uv1;

    float curFrame = _OutDrawFrameData[instanceID].FrameIndex;
    float preAniFrame = _OutDrawFrameData[instanceID].PreFrameIndex;
    float progress = _OutDrawFrameData[instanceID].TransitionProgress;

    int preFrame = curFrame;
    int nextFrame = curFrame + 1;

    //TODO Bone Num Controller
    // float4x4 rootLocalMatrixPre = LoadMatFromTexture(preFrame, bone.x);
    float4x4 rootLocalMatrixPre = LoadMatFromTexture(preFrame, bone.x) * w.x;
    rootLocalMatrixPre += LoadMatFromTexture(preFrame, bone.y) * max(0, w.y);
    // rootLocalMatrixPre += LoadMatFromTexture(preFrame, bone.z) * max(0, w.z);
    // rootLocalMatrixPre += LoadMatFromTexture(preFrame, bone.w) * max(0, w.w);
    // float4x4 rootLocalMatrixNext = LoadMatFromTexture(nextFrame, bone.x);
    float4x4 rootLocalMatrixNext = LoadMatFromTexture(nextFrame, bone.x) * w.x;
    rootLocalMatrixNext += LoadMatFromTexture(nextFrame, bone.y) * max(0, w.y);
    // rootLocalMatrixNext += LoadMatFromTexture(nextFrame, bone.z) * max(0, w.z);
    // rootLocalMatrixNext += LoadMatFromTexture(nextFrame, bone.w) * max(0, w.w);

    float4 rootLocalPosPre = mul(IN.vertex, rootLocalMatrixPre);
    float4 rootLocalPosNext = mul(IN.vertex, rootLocalMatrixNext);
    float4 rootLocalPos = lerp(rootLocalPosPre, rootLocalPosNext, curFrame - preFrame);

    float3 rootLocalNormPre = mul(IN.normal.xyz,  (float3x3) rootLocalMatrixPre);
    float3 rootLocalNormNext = mul(IN.normal.xyz, (float3x3) rootLocalMatrixNext);
    IN.normal = normalize(lerp(rootLocalNormPre, rootLocalNormNext, curFrame - preFrame));
    float3 rootLocalTanPre = mul(IN.tangentOS.xyz, (float3x3) rootLocalMatrixPre);
    float3 rootLocalTanNext = mul(IN.tangentOS.xyz, (float3x3) rootLocalMatrixNext);
    IN.tangentOS.xyz = normalize(lerp(rootLocalTanPre, rootLocalTanNext, curFrame - preFrame));

    float4x4 rootLocalMatrixPreAni = LoadMatFromTexture(preAniFrame, bone.x);
    float4 rootLocalPreAni = mul(IN.vertex, rootLocalMatrixPreAni);
    rootLocalPos = lerp(rootLocalPos, rootLocalPreAni, (1.0f - progress) * (preAniFrame > 0.0f));

    return rootLocalPos;
}

float4 skinningShadow(uint4 bone, float4 positionOS, uint instanceID)
{
    float curFrame = _OutDrawFrameData[instanceID].FrameIndex;
    float preAniFrame = _OutDrawFrameData[instanceID].PreFrameIndex;
    float progress = _OutDrawFrameData[instanceID].TransitionProgress;

    int preFrame = curFrame;
    int nextFrame = curFrame + 1;
    float4x4 rootLocalMatrixPre = LoadMatFromTexture(preFrame, bone.x);
    float4x4 rootLocalMatrixNext = LoadMatFromTexture(nextFrame, bone.x);
    float4 rootLocalPosPre = mul(positionOS, rootLocalMatrixPre);
    float4 rootLocalPosNext = mul(positionOS, rootLocalMatrixNext);
    float4 rootLocalPos = lerp(rootLocalPosPre, rootLocalPosNext, curFrame - preFrame);

    float4x4 rootLocalMatrixPreAni = LoadMatFromTexture(preAniFrame, bone.x);
    float4 rootLocalPreAni = mul(positionOS, rootLocalMatrixPreAni);
    rootLocalPos = lerp(rootLocalPos, rootLocalPreAni, (1.0f - progress) * (preAniFrame > 0.0f));

    return rootLocalPos;
}

#endif