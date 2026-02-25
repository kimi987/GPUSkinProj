#ifndef CUSTOM_CHARACTER_PBR_INPUT_INCLUDED
#define CUSTOM_CHARACTER_PBR_INPUT_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
TEXTURE2D(_MetallicRoughnessEmissionAOMap); SAMPLER(sampler_MetallicRoughnessEmissionAOMap);

CBUFFER_START(UnityPerMaterial)
    real4 _BaseColor;
    real4 _BaseMap_ST;
	real _Cutoff;
    real4 _NormalMap_ST;
    real _NormalScale;
    real _Scale;
    real _Roughness;
    real _Metallic;
	real _Emission;
	real4 _EmissionColor;
    real _AO;
CBUFFER_END


#endif