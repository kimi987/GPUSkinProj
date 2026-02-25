#ifndef CUSTOM_BRDF_INCLUDE
#define CUSTOM_BRDF_INCLUDE

// Specifies minimal reflectance for dielectrics (when metalness is zero)
// Nothing has lower reflectance than 2%, but we use 4% to have consistent results with UE4, Frostbite, et al.
#define MIN_DIELECTRICS_F0 0.04f
#ifndef PI
#define PI 3.141592653589f
#endif

#ifndef ONE_OVER_PI
#define ONE_OVER_PI (1.0f / PI)
#endif

#define kDielectricSpec half4(0.04, 0.04, 0.04, 1.0 - 0.04) // standard dielectric reflectivity coef at incident angle (= 4%)

float _GIShadowStrength;
real4 _ShadowColor;

struct MaterialProperties
{
    half3 albedo;
    half metallic; //
    half3 emission;
    float roughness;
    half3 specular;
    half3 bakedGI;
    float transmissivness;
    float opacity;
    float occlusion;
};

struct brdfData
{
    half3 albedo;
    half3 diffuse;
    half3 specular;
    half reflectivity;

    float perceptualRoughness;
    float roughness;
    float roughness2;
    //float roughness4;

    half grazingTerm;

    half normalizationTerm;     // roughness * 4.0 + 2.0
    half roughness2MinusOne;    // roughness^2 - 1.0
    

    float3 F;   //fresnel

    float3 V;  //Direction to Viewer
    float3 N; // Normal
    float3 H; // Half Vector
    float3 L; // Direction to Light

    float NdotL;
    float NdotV;
    float LdotH;
    float NdotH;
    float VdotH;
};
#include "GlobalIllData.hlsl"

real Pow5(real x)
{
    return (x * x) * (x * x) * x;
}

float3 baseColorToSpecularF0(float3 baseColor, float metalness)
{
    return lerp(float3(MIN_DIELECTRICS_F0, MIN_DIELECTRICS_F0, MIN_DIELECTRICS_F0), baseColor, metalness);
}

float3 baseColorToDiffuseReflectance(float3 baseColor, float metalness)
{
    return baseColor * (1.0f - metalness);
}

// Schlick's approximation to Fresnel term
// f90 should be 1.0, except for the trick used by Schuler (see 'shadowedF90' function)
float3 evalFresnelSchlick(float3 f0, float f90, float NdotS)
{
    return f0 + (f90 - f0) * Pow5(1.0f - NdotS);
}

float3 evalFresnel(float3 f0, float f90, float NdotS)
{
    // Default is Schlick's approximation
    return evalFresnelSchlick(f0, f90, NdotS);
}

float luminance(float3 rgb)
{
    return dot(rgb, float3(0.2126f, 0.7152f, 0.0722f));
}

// Attenuates F90 for very low F0 values
// Source: "An efficient and Physically Plausible Real-Time Shading Model" in ShaderX7 by Schuler
// Also see section "Overbright highlights" in Hoffman's 2010 "Crafting Physically Motivated Shading Models for Game Development" for discussion
// IMPORTANT: Note that when F0 is calculated using metalness, it's value is never less than MIN_DIELECTRICS_F0, and therefore,
// this adjustment has no effect. To be effective, F0 must be authored separately, or calculated in different way. See main text for discussion.
float shadowedF90(float3 F0) {
    // This scaler value is somewhat arbitrary, Schuler used 60 in his article. In here, we derive it from MIN_DIELECTRICS_F0 so
    // that it takes effect for any reflectance lower than least reflective dielectrics
    //const float t = 60.0f;
    const float t = (1.0f / MIN_DIELECTRICS_F0);
    return min(1.0f, t * luminance(F0));
}

// Smith G2 term (masking-shadowing function) for GGX distribution
// Separable version assuming independent (uncorrelated) masking and shadowing - optimized by substituing G_Lambda for G_Lambda_GGX and 
// dividing by (4 * NdotL * NdotV) to cancel out these terms in specular BRDF denominator
// Source: "Moving Frostbite to Physically Based Rendering" by Lagarde & de Rousiers
// Note that returned value is G2 / (4 * NdotL * NdotV) and therefore includes division by specular BRDF denominator
float Smith_G2_Separable_GGX_Lagarde(float alphaSquared, float NdotL, float NdotV) {
    float a = NdotV + sqrt(alphaSquared + NdotV * (NdotV - alphaSquared * NdotV));
    float b = NdotL + sqrt(alphaSquared + NdotL * (NdotL - alphaSquared * NdotL));
    return 1.0f / (a * b);
}

float Smith_G2(float alpha, float alphaSquared, float NdotL, float NdotV)
{
    return Smith_G2_Separable_GGX_Lagarde(alpha, NdotL, NdotV);
}

brdfData prepareBrdfData(float3 N, float3 V, float3 L, MaterialProperties properties)
{
    brdfData data = (brdfData)0;
    data.V = V;
    data.N = N;
    data.L = L;
    data.H = SafeNormalize(L + V);

    half oneMinusReflectivity = OneMinusReflectivityMetallic(properties.metallic);
    half reflectivity = half(1.0) - oneMinusReflectivity;
    half3 brdfDiffuse = properties.albedo * oneMinusReflectivity;
    half3 brdfSpecular = lerp(kDieletricSpec.rgb, properties.albedo, properties.metallic);

    data.albedo = properties.albedo;
    data.diffuse = brdfDiffuse;
    data.specular = brdfSpecular;
    data.reflectivity = reflectivity;


    float NdotL = dot(N, L);
    float NdotV = dot(N, V);
    
    data.NdotL = saturate(NdotL);
    data.NdotV = saturate(NdotV);
    data.NdotH = saturate(dot(N, data.H));
    data.LdotH = saturate(dot(L, data.H));

    data.perceptualRoughness = properties.roughness;
    data.roughness = data.perceptualRoughness;
    //data.roughness =  max(PerceptualRoughnessToRoughness(properties.roughness), HALF_MIN_SQRT);
    data.roughness2 = max(data.roughness * data.roughness, HALF_MIN); 
    //data.roughness4 = max(data.roughness2 * data.roughness2,HALF_MIN);
    
    data.grazingTerm         = saturate(1.0h - properties.roughness + reflectivity);
    data.normalizationTerm   = data.roughness * half(4.0) + half(2.0);
    data.roughness2MinusOne  = data.roughness2 - half(1.0);
    
    return data;
}
void CreateBRDFClearCoatData(half clearCoatMask, half clearCoatRoughness, inout brdfData baseData, out brdfData clearCoatData)
{
    clearCoatData = (brdfData)0;
    clearCoatData.albedo = half(1.0);

    // 计算粗糙度
    clearCoatData.diffuse = kDielectricSpec.aaa;
    clearCoatData.specular = kDielectricSpec.rgb;

    clearCoatData.reflectivity = kDielectricSpec.r;

    clearCoatData.perceptualRoughness = clearCoatData.roughness;
    clearCoatData.roughness =  max(PerceptualRoughnessToRoughness(clearCoatData.roughness), HALF_MIN_SQRT);
    clearCoatData.roughness2 = max(clearCoatData.roughness * clearCoatData.roughness, HALF_MIN);

    clearCoatData.normalizationTerm = clearCoatData.roughness * half(4.0) + half(2.0);
    clearCoatData.roughness2MinusOne = clearCoatData.roughness2 - half(1.0);
    clearCoatData.grazingTerm = saturate(1.0h - clearCoatRoughness + kDielectricSpec.x);

    // Darken/saturate base layer using coat to surface reflectance (vs. air to surface)
    baseData.specular = lerp(baseData.specular, ConvertF0ForClearCoat15(baseData.specular), clearCoatMask);
}

brdfData CreateClearCoatData(half clearCoatMask, half clearCoatRoughness, inout brdfData baseData)
{
    brdfData brdfClearCoatData = (brdfData)0;
    #if defined(_USECLEARCOAT_ON)
    CreateBRDFClearCoatData(clearCoatMask, clearCoatRoughness, baseData, brdfClearCoatData);
    #endif

    return brdfClearCoatData;
}

float GGX_D(float alpha, float NdotH) {
    // float b = ((alphaSquared - 1.0f) * NdotH * NdotH + 1.0f);
    // return alphaSquared / (PI * b * b);

    float b = NdotH * NdotH * (alpha - 1.0f)  + 1.0f;
    return alpha / (PI * b * b);
}

// Frostbite's version of Disney diffuse with energy normalization.
// Source: "Moving Frostbite to Physically Based Rendering" by Lagarde & de Rousiers
float frostbiteDisneyDiffuse(in brdfData data) {
    float energyBias = 0.5f * data.roughness;
    float energyFactor = lerp(1.0f, 1.0f / 1.51f, data.roughness);

    float FD90MinusOne = energyBias + 2.0 * data.LdotH * data.LdotH * data.roughness - 1.0f;
    
    float FDL = 1.0f + (FD90MinusOne * Pow5(1.0f - data.NdotL));
    float FDV = 1.0f + (FD90MinusOne * Pow5(1.0f - data.NdotV));

    return FDL * FDV * energyFactor;
}

float3 evalFrostbiteDisneyDiffuse(in brdfData data) {
    return data.reflectivity * (frostbiteDisneyDiffuse(data) * ONE_OVER_PI * data.NdotL);
}

float3 evalMicrofacet(in brdfData data)
{
    float D = GGX_D(max(0.00001f, data.roughness2), data.NdotH);
    float G2 = Smith_G2(data.roughness2, data.roughness2, data.NdotL, data.NdotV);
    return data.F * (G2 * D);
}

float DisneySpecularD(float a2, float NdotH)
{
    float cos2th = NdotH * NdotH;
    float den = (1.00001f + (a2 - 1.0) * cos2th);
	
    return a2 / (PI * den * den);
}

// Computes the scalar specular term for Minimalist CookTorrance BRDF
// NOTE: needs to be multiplied with reflectance f0, i.e. specular color to complete
half UnityBRDFSpecular(brdfData brdfData)
{
    // GGX Distribution multiplied by combined approximation of Visibility and Fresnel
    // BRDFspec = (D * V * F) / 4.0
    // D = roughness^2 / ( NoH^2 * (roughness^2 - 1) + 1 )^2
    // V * F = 1.0 / ( LoH^2 * (roughness + 0.5) )
    // See "Optimizing PBR for Mobile" from Siggraph 2015 moving mobile graphics course
    // https://community.arm.com/events/1155

    // Final BRDFspec = roughness^2 / ( NoH^2 * (roughness^2 - 1) + 1 )^2 * (LoH^2 * (roughness + 0.5) * 4.0)
    // We further optimize a few light invariant terms
    // brdfData.normalizationTerm = (roughness + 0.5) * 4.0 rewritten as roughness * 4.0 + 2.0 to a fit a MAD.
    float d = brdfData.NdotH * brdfData.NdotH * brdfData.roughness2MinusOne + 1.00001f;

    half LoH2 = brdfData.LdotH * brdfData.LdotH;
    half specularTerm = brdfData.roughness2 / (d * d * max(0.1h, LoH2) * brdfData.normalizationTerm);
    
    // On platforms where half actually means something, the denominator has a risk of overflow
    // clamp below was added specifically to "fix" that, but dx compiler (we convert bytecode to metal/gles)
    // sees that specularTerm have only non-negative terms, so it skips max(0,..) in clamp (leaving only min(100,...))
    #if defined (SHADER_API_MOBILE) || defined (SHADER_API_SWITCH)
    specularTerm = specularTerm - HALF_MIN;
    specularTerm = clamp(specularTerm, 0.0, 100.0); // Prevent FP16 overflow on mobiles
    #endif

    return specularTerm;
}

half3 UnityBRDFClearCoatSpecular(brdfData baseData, brdfData clearCoatData)
{
    float d = baseData.NdotH * baseData.NdotH * clearCoatData.roughness2MinusOne + 1.00001f;
    half d2 = half(d * d);

    half LoH2 = baseData.LdotH * baseData.LdotH;
    half specularTerm = clearCoatData.roughness2 / (d2 * max(half(0.1), LoH2) * clearCoatData.normalizationTerm);

    // On platforms where half actually means something, the denominator has a risk of overflow
    // clamp below was added specifically to "fix" that, but dx compiler (we convert bytecode to metal/gles)
    // sees that specularTerm have only non-negative terms, so it skips max(0,..) in clamp (leaving only min(100,...))
    #if defined (SHADER_API_MOBILE) || defined (SHADER_API_SWITCH)
    specularTerm = specularTerm - HALF_MIN;
    specularTerm = clamp(specularTerm, 0.0, 100.0); // Prevent FP16 overflow on mobiles
    #endif

    return specularTerm;
}

half3 MixCleatCoat(half3 brdfColor, brdfData baseData, brdfData clearCoatData, half clearCoatMask, half3 normalWS, half3 viewDirectionWS, half clearCoatFresnelStrength)
{
    half3 brdfCoat = kDielectricSpec.r * UnityBRDFClearCoatSpecular(baseData, clearCoatData);

    half NoV = saturate(dot(normalWS, viewDirectionWS));


    half coatFresnel = kDielectricSpec.x + kDielectricSpec.a * Pow4(1.0 - NoV);


    brdfColor = brdfColor * (1.0 - clearCoatMask * coatFresnel) + brdfCoat * coatFresnel * clearCoatFresnelStrength * clearCoatMask;

    return brdfColor;
}

float3 evalCombineBRDFNoSpec(float3 N, float3 L, float3 V, half3 radiance, float shadowAtten, MaterialProperties properties)
{
    brdfData data = prepareBrdfData(N, V, L, properties);
    half3 giColor = CGlobalIllumination(data, properties.bakedGI, N, V);
    giColor = lerp(giColor * (1.0 - _GIShadowStrength), giColor, shadowAtten);
    return data.diffuse * radiance + giColor + properties.emission;
}

float3 evalCombineBRDF(float3 N, float3 L, float3 V, half3 radiance, float shadowAtten, MaterialProperties properties)
{
    brdfData data = prepareBrdfData(N, V, L, properties);
    half3 specular = data.specular * UnityBRDFSpecular(data);
    half3 giColor = CGlobalIllumination(data, properties.bakedGI, N, V);
    giColor = lerp(giColor * (1.0 - _GIShadowStrength), giColor, shadowAtten);
    return (data.diffuse + specular) * radiance + giColor + properties.emission;
}

float3 evalCombineBRDFWithSpecular(float3 N, float3 L, float3 V, half3 specular, half noise, half3 radiance, float shadowAtten, MaterialProperties properties)
{
    brdfData data = prepareBrdfData(N, V, L, properties);
    half3 giColor = CGlobalIllumination(data, properties.bakedGI, N, V);
    giColor = lerp(giColor * (1.0 - _GIShadowStrength), giColor, shadowAtten);
    return (data.diffuse + specular) * radiance + giColor * noise + properties.emission;
}

void evalCombineBRDFDebug(float3 N, float3 L, float3 V, half3 radiance, float shadowAtten, MaterialProperties properties, out float3 diffuse, out float3 specular, out float3 giDiffuse, out float3 giSpecular, out float3 emission)
{
    brdfData data = prepareBrdfData(N, V, L, properties);
    specular = data.specular * UnityBRDFSpecular(data);
    CGlobalIlluminationDebug(data, properties.bakedGI, N, V,  giDiffuse, giSpecular);
    diffuse = data.diffuse * radiance;
    specular = specular * radiance;
    emission = properties.emission;
    giDiffuse = lerp(giDiffuse * (1.0 - _GIShadowStrength), giDiffuse, shadowAtten);
    giSpecular = lerp(giSpecular * (1.0 - _GIShadowStrength), giSpecular, shadowAtten);
    giDiffuse *= properties.occlusion;
    giSpecular *= properties.occlusion;
}

void evalCombineBRDFNoSpecDebug(float3 N, float3 L, float3 V, half3 radiance, float shadowAtten, MaterialProperties properties, out float3 diffuse, out float3 specular, out float3 giDiffuse, out float3 giSpecular, out float3 emission)
{
    brdfData data = prepareBrdfData(N, V, L, properties);
    CGlobalIlluminationDebug(data, properties.bakedGI, N, V,  giDiffuse, giSpecular);
    diffuse = data.diffuse;
    specular = float3(0, 0, 0);
    emission = properties.emission;
    giDiffuse = lerp(giDiffuse * (1.0 - _GIShadowStrength), giDiffuse, shadowAtten);
    giSpecular = lerp(giSpecular * (1.0 - _GIShadowStrength), giSpecular, shadowAtten);
    giDiffuse *= properties.occlusion;
    giSpecular *= properties.occlusion;
}

float3 debugDiffuse(float3 N, float3 L, float3 V, half3 radiance, MaterialProperties properties)
{
    brdfData data = prepareBrdfData(N, V, L, properties);
    //half3 specular = data.specular * UnityBRDFSpecular(data);
    //half3 giColor = CGlobalIllumination(data, properties.bakedGI, N, V);
    return data.diffuse;
}

float3 debugSpecular(float3 N, float3 L, float3 V, half3 radiance, MaterialProperties properties)
{
    brdfData data = prepareBrdfData(N, V, L, properties);
    half3 specular = data.specular * UnityBRDFSpecular(data);
    //half3 giColor = CGlobalIllumination(data, properties.bakedGI, N, V);
    return specular;
}

#endif