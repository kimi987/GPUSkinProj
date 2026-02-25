#ifndef ANIMATION_INSTANCING_BASE_INCLUDED
#define ANIMATION_INSTANCING_BASE_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Matrix.hlsl"
#include "UnPacked.hlsl"

TEXTURE2D(_BoneTexture); SAMPLER(sampler_BoneTexture);
real _BoneTextureBlockWidth;
real _BoneTextureBlockHeight;
real _BoneTextureWidth;
real _BoneTextureHeight;

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


real4x4 LoadMatFromTexture(uint frameIndex, uint boneIndex)
{
    float rcpBoneTextureBlockWidth = rcp(_BoneTextureBlockWidth);
    
    uint blockCount = _BoneTextureWidth * rcpBoneTextureBlockWidth;

    int2 uv;
    uv.y = frameIndex / blockCount * _BoneTextureBlockHeight;
    uv.x = _BoneTextureBlockWidth * (frameIndex - _BoneTextureWidth * rcpBoneTextureBlockWidth * uv.y);

    int matCount_x = _BoneTextureBlockWidth;//* 0.25;
    int matCount_y = rcpBoneTextureBlockWidth;
    uv.x = uv.x + (boneIndex % matCount_x);
    uv.y = uv.y + boneIndex / matCount_y;

    float offset = rcp((float)_BoneTextureWidth);
    float2 uvFrame;
    uvFrame.x = uv.x * offset;
    uvFrame.y = uv.y * rcp((float)_BoneTextureHeight);
    real4 uvf = real4(uvFrame, 0, 0);

    float4 c = SAMPLE_TEXTURE2D_LOD(_BoneTexture, sampler_BoneTexture, uvf.xy, 0);
    // float3 r1 = unpack111110(c.r);
    // float3 r2 = unpack111110(c.g);
    // float3 r3 = unpack111110(c.b);
    // float3 r4 = unpack111110(c.a);
    // uvf.x = uvf.x + offset;
    // real4 c2 = SAMPLE_TEXTURE2D_LOD(_BoneTexture, sampler_BoneTexture, uvf.xy, 0);
    // uvf.x = uvf.x + offset;
    // real4 c3 = SAMPLE_TEXTURE2D_LOD(_BoneTexture, sampler_BoneTexture, uvf.xy, 0);

    float2 r1 = simpleUnpack16(c.r);  //pos.x, rot.x
    float2 r2 = simpleUnpack16(c.g);  //pos.y, rot.y
    float2 r3 = simpleUnpack16(c.b);  //pos.z, rot.z
    float2 r4 = simpleUnpack16(c.a);  //rot, scale

    float4x4 m = ReconstructMatrixScale(float3(r1.x, r2.x, r3.x), float4(r1.y, r2.y, r3.y, r4.x), r4.y);
    // real4 c4 = real4(0, 0, 0, 1);
    //
    // real4x4 m;
    // m._11_21_31_41 = float4(r1.x, r2.x, r3.x, r4.x);
    // m._12_22_32_42 = float4(r1.y, r2.y, r3.y, r4.y);
    // m._13_23_33_43 = float4(r1.z, r2.z, r3.z, r4.z);
    // m._14_24_34_44 = c4;
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