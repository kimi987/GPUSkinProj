#ifndef ANIMATION_INSTANCING_INPUT_INCLUDED
#define ANIMATION_INSTANCING_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

struct Attributes
{
    float4 vertex : POSITION;
    float3 normal : NORMAL;
    float4 tangentOS  : TANGENT;
    real4 color : COLOR; 
    float2 uv : TEXCOORD0;
    real4 uv1 : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv : TEXCOORD0;
    float4 vertex : SV_POSITION;
    float3 posW : TEXTCOORD1;
    real3 normalW : TEXTCOORD2;
    real4 tangentW : TEXTCOORD3;
    real3 bitangentWS : TEXTCOORD4;
    real4 shadowCoord : TEXTCOORD5;
    real3 vertexSH : TEXTCOORD6;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};


#endif