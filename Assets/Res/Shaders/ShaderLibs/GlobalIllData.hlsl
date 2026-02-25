#ifndef CUSTOM_GLOBAL_ILL_INCLUDED
#define CUSTOM_GLOBAL_ILL_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

half3 CGlossyEnvironmentReflection(half3 reflectVector, half perceptualRoughness)
{
    #if !defined(_ENVIRONMENTREFLECTIONS_OFF)
    half3 irradiance;
        
    half mip = PerceptualRoughnessToMipmapLevel(perceptualRoughness);
    half4 encodedIrradiance = half4(SAMPLE_TEXTURECUBE_LOD(unity_SpecCube0, samplerunity_SpecCube0, reflectVector, mip));

    irradiance = DecodeHDREnvironment(encodedIrradiance, unity_SpecCube0_HDR);

    return irradiance;
    #else
    return _GlossyEnvironmentColor.rgb;
    #endif // _ENVIRONMENTREFLECTIONS_OFF
}

// Computes the specular term for EnvironmentBRDF
half3 CEnvironmentBRDFSpecular(brdfData brdfData, half fresnelTerm)
{
    float surfaceReduction = 1.0 / (brdfData.roughness2 + 1.0);
    return half3(surfaceReduction * lerp(brdfData.specular, brdfData.grazingTerm, fresnelTerm));
}

half3 CEnvironmentBRDF(brdfData brdfData, half3 indirectDiffuse, half3 indirectSpecular, half fresnelTerm)
{
    half3 c = indirectDiffuse * brdfData.diffuse;
    c += indirectSpecular * CEnvironmentBRDFSpecular(brdfData, fresnelTerm);
    return c;
}

half3 CGlobalIllumination(brdfData brdfData, half3 bakedGI, half3 normalWS, half3 viewDirectionWS)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half fresnelTerm = Pow4(1.0 - brdfData.NdotV);

    half3 indirectDiffuse = bakedGI;
    half3 indirectSpecular = CGlossyEnvironmentReflection(reflectVector, brdfData.perceptualRoughness);
    half3 color = CEnvironmentBRDF(brdfData, indirectDiffuse, indirectSpecular, fresnelTerm);

    return color;
}

void CGlobalIlluminationDebug(brdfData brdfData, half3 bakedGI, half3 normalWS, half3 viewDirectionWS, out float3 giDiffuse, out float3 giSpecular)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half fresnelTerm = Pow4(1.0 - brdfData.NdotV);

    giDiffuse = bakedGI;
    giSpecular = CGlossyEnvironmentReflection(reflectVector, brdfData.perceptualRoughness);
}


half3 CEnvironmentBRDFClearCoat(brdfData data, half clearCoatMask, half3 indirectSpecular, half fresnelTerm)
{
    return indirectSpecular * CEnvironmentBRDFSpecular(data, fresnelTerm) * clearCoatMask;
}

half3 CGlobalIlluminationWithClearCoat(brdfData baseData, brdfData clearCoatData, half clearCoatMask, half3 bakedGI, half3 normalWS,
    half3 viewDirectionWS, half3 clearCoatGIColor)
{
    half3 reflectVector = reflect(-viewDirectionWS, normalWS);
    half fresnelTerm = Pow4(1.0 - baseData.NdotV);

    half3 indirectDiffuse = bakedGI;
    half3 indirectSpecular = CGlossyEnvironmentReflection(reflectVector, clearCoatData.perceptualRoughness);

    half3 color = CEnvironmentBRDF(baseData, indirectDiffuse, indirectSpecular, fresnelTerm);

    #if defined(_USECLEARCOAT_ON)
    // Clear Coat
    half3 coatIndirectSpecular = CGlossyEnvironmentReflection(reflectVector, clearCoatData.roughness);

    half3 coatColor = CEnvironmentBRDFClearCoat(clearCoatData, clearCoatMask, coatIndirectSpecular, fresnelTerm) * clearCoatGIColor;

    half coatFresnel = kDielectricSpec.x + kDielectricSpec.a * fresnelTerm;

    return color * saturate(1.0h - coatFresnel * clearCoatMask) + coatColor;
    #endif
    
    return color;
}


#endif