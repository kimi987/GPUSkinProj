#ifndef CUSTOM_CHARACTER_FUNC_INPUT_INCLUDED
#define CUSTOM_CHARACTER_FUNC_INPUT_INCLUDED

void InitMaterialProperties(float2 uv, half metallic, half roughness, half3 vertexSH, float3 normalW, float emission, float occlusion,
            out MaterialProperties properties)
{
    half4 albedoAlpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
    properties.albedo = albedoAlpha.rgb * _BaseColor.rgb;
    properties.metallic = metallic;
    properties.roughness = roughness;
    properties.specular = half3(0.0, 0.0, 0.0);
    properties.emission = emission * properties.albedo;
    properties.bakedGI = SampleSHPixel(vertexSH, normalW);
    properties.transmissivness = 0;
    properties.opacity = albedoAlpha.a * _BaseColor.a;
    properties.occlusion = occlusion;
}

#endif