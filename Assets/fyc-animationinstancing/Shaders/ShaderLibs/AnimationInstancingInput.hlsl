#ifndef ANIMATION_INSTANCING_INPUT_INCLUDED
#define ANIMATION_INSTANCING_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct AttributesAnimationInstancing
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float4 tangentOS  : TANGENT;
    real4 color : COLOR; 
    float2 uv : TEXCOORD0;
    real4 uv1 : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct VaryingsAnimationInstancing
{
    float2 uv : TEXCOORD0;
    float4 vertex : SV_POSITION;
    float3 posW : TEXCOORD1;
    real3 normalW : TEXCOORD2;
    real4 tangentW : TEXCOORD3;
    real3 bitangentWS : TEXCOORD4;
    // real4 shadowCoord : TEXCOORD5;
    real3 vertexSH : TEXCOORD5;
    real4 screenPos : TEXCOORD6;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};


#endif