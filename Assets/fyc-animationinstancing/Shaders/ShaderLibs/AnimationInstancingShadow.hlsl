#ifndef ANIMATION_INSTANCING_SHADOW_INCLUDED
#define ANIMATION_INSTANCING_SHADOW_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_boneTexture); SAMPLER(sampler_boneTexture);
real _boneTextureBlockWidth;
real _boneTextureBlockHeight;
real _boneTextureWidth;
real _boneTextureHeight;

#if (SHADER_TARGET < 30 || SHADER_API_GLES)
uniform float frameIndex;
uniform float preFrameIndex;
uniform float transitionProgress;
#else
UNITY_INSTANCING_BUFFER_START(Props)
    UNITY_DEFINE_INSTANCED_PROP(float4, _PackedFrameData)
#define PackedFrameData_arr Props
UNITY_INSTANCING_BUFFER_END(Props)
#endif

real4x4 LoadMatFromTexture(uint frameIndex, uint boneIndex)
{
    float rcpBoneTextureBlockWidth = rcp(_boneTextureBlockWidth);
    
    uint blockCount = _boneTextureWidth * rcpBoneTextureBlockWidth;

    int2 uv;
    uv.y = frameIndex / blockCount * _boneTextureBlockHeight;
    uv.x = _boneTextureBlockWidth * (frameIndex - _boneTextureWidth * rcpBoneTextureBlockWidth * uv.y);

    int matCount_x = _boneTextureBlockWidth * 0.25;
    int matCount_y = rcpBoneTextureBlockWidth * 4;
    uv.x = uv.x + (boneIndex % matCount_x) * 4;
    uv.y = uv.y + boneIndex / matCount_y;

    float offset = rcp((float)_boneTextureWidth);
    float2 uvFrame;
    uvFrame.x = uv.x * offset;
    uvFrame.y = uv.y * rcp((float)_boneTextureHeight);
    real4 uvf = real4(uvFrame, 0, 0);

    real4 c1 = SAMPLE_TEXTURE2D_LOD(_boneTexture, sampler_boneTexture, uvf.xy, 0);
    uvf.x = uvf.x + offset;
    real4 c2 = SAMPLE_TEXTURE2D_LOD(_boneTexture, sampler_boneTexture, uvf.xy, 0);
    uvf.x = uvf.x + offset;
    real4 c3 = SAMPLE_TEXTURE2D_LOD(_boneTexture, sampler_boneTexture, uvf.xy, 0);
    real4 c4 = real4(0, 0, 0, 1);

    real4x4 m;
    m._11_21_31_41 = c1;
    m._12_22_32_42 = c2;
    m._13_23_33_43 = c3;
    m._14_24_34_44 = c4;
    return m;
}

real4 skinningShadow(uint4 bone, float4 positionOS)
{
#if (SHADER_TARGET < 30 || SHADER_API_GLES)
    float curFrame = frameIndex;
    float preAniFrame = preFrameIndex;
    float progress = transitionProgress;
#else
    float4 packedFrameData = UNITY_ACCESS_INSTANCED_PROP(PackedFrameData_arr, _PackedFrameData);
    float curFrame = packedFrameData.x;
    float preAniFrame = packedFrameData.y;
    float progress = packedFrameData.z;
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
