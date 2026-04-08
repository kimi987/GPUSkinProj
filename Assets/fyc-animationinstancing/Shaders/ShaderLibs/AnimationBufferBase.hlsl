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
    float rcpBoneTextureBlockWidth = rcp(_BoneTextureBlockWidth);

    uint blockCount = _BoneTextureWidth * rcpBoneTextureBlockWidth;

    int2 uv;
    uv.y = frameIndex / blockCount * _BoneTextureBlockHeight;
    uv.x = _BoneTextureBlockWidth * (frameIndex - _BoneTextureWidth * rcpBoneTextureBlockWidth * uv.y);

    int matCount_x = _BoneTextureBlockWidth;// * 0.25;
    int matCount_y = rcpBoneTextureBlockWidth;// * 4;
    uv.x = uv.x + (boneIndex % matCount_x);// * 4;
    uv.y = uv.y + boneIndex / matCount_y;

    float offset = rcp((float)_BoneTextureWidth);
    float2 uvFrame;
    uvFrame.x = uv.x * offset;
    uvFrame.y = uv.y * rcp((float)_BoneTextureHeight);
    float4 uvf = float4(uvFrame, 0, 0);

    float4 c = SAMPLE_TEXTURE2D_LOD(_BoneTexture, sampler_BoneTexture, uvf.xy, 0);
    // uvf.x = uvf.x + offset;
    // real4 c2 = SAMPLE_TEXTURE2D_LOD(_BoneTexture, sampler_BoneTexture, uvf.xy, 0);
    // uvf.x = uvf.x + offset;
    // real4 c3 = SAMPLE_TEXTURE2D_LOD(_BoneTexture, sampler_BoneTexture, uvf.xy, 0);
    // real4 c4 = real4(0, 0, 0, 1);
    float2 r1 = simpleUnpack16(c.r);  //pos.x, rot.x
    float2 r2 = simpleUnpack16(c.g);  //pos.y, rot.y
    float2 r3 = simpleUnpack16(c.b);  //pos.z, rot.z
    float2 r4 = simpleUnpack16(c.a);  //rot, scale

    // real4x4 m;
    // m._11_21_31_41 = c1;
    // m._12_22_32_42 = c2;
    // m._13_23_33_43 = c3;
    // m._14_24_34_44 = c4;
    float4x4 m = ReconstructMatrixScale(float3(r1.x, r2.x, r3.x), float4(r1.y, r2.y, r3.y, r4.x), r4.y);
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