Shader "FYC/Character/AnimationInstancingPBRNew"
{
   Properties
   {
        [Title(PBRShader)]
        [Main(Base,_,on,off)]_group1("基础部分", float)=0
        [Sub(Base)]_BaseMap ("反射率贴图(基础颜色)", 2D) = "white" {}
        [Sub(Base)]_BaseColor("基础颜色", color) = (1,1,1,1)
        [Sub(Base)]_Cutoff("Cutoff", Range(0, 1)) = 1
        [SubToggle(Base, _RECEIVE_SHADOWS)] _ReceiveShadows("接受阴影", Float) = 1.0
        
        [Main(Normal,_,on,off)] _group2("法线部分", float)=0
        [Sub(Normal)]_NormalMap("法线贴图", 2D) = "bump" {}
        [Sub(Normal)]_NormalScale("法线缩放", float) = 1
       
        [Main(Mask,_,on,off)] _group3("遮罩部分", float)=0
        [Sub(Mask)]_MetallicRoughnessEmissionAOMap("遮罩贴图(R:金属 G:粗糙 B:自发光 A:AO)", 2D) = "white" {}
        [Sub(Mask)]_Roughness("粗糙度控制", Range(0.01, 2)) = 1
        [Sub(Mask)]_Metallic("金属度控制", Range(0, 1)) = 0
        [Sub(Mask)]_Emission("自发光控制", Range(0, 10)) = 0
        [Sub(Mask)][HDR]_EmissionColor("自发光颜色", color) = (1,1,1,1)
        [Sub(Mask)]_AO("AO控制", Range(0, 2)) = 1
       
        [Title(Preset Samples)]
        [Present(_)] _Present("预设", float) = 2
        [Enum(UnityEngine.Rendering.CullMode)]_Cull("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)]_SrcBlend("SrcBlend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)]_DstBlend("DstBlend", Float) = 0
        [Toggle(_)]_ZWrite("ZWrite ", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)]_ZTest("ZTest", Float) = 4 // 4 is LEqual
       
        [Header(_____Stencil_____)]
        [StencilPresent(_)] _Stencil("Stencil预设", float) = 0
        [IntRange] _StencilID ("StencilID", Range (0, 256)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp  ("Stencil Comparison", Int) = 0
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilPass ("Stencil Pass Operation", Int) = 0
        [Enum(UnityEngine.Rendering.StencilOp)] _StencilFail ("Stencil Fail Operation", Int) = 0
   }
   
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
            "IgnoreProjector" = "True"
        }
        Stencil
        {
             Ref [_StencilID]
             Comp [_StencilComp]
             Pass [_StencilPass]
             Fail [_StencilFail]
        }   
        Blend [_SrcBlend] [_DstBlend]
        Cull [_Cull]
        Lighting Off
        ZWrite [_ZWrite]
        ZTest [_ZTest]
        Pass
        {
            Name "UniversalForward"
            Tags {
                "LightMode"="UniversalForward"
            }
            
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma shader_feature_local _RECEIVE_SHADOWS
            #pragma multi_compile _INSTANCING _INDIRECT
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ShaderLibs/CharacterPBRInput.hlsl"
            #include "../ShaderLibs/ShadowFunc.hlsl"
            #include "Assets/fyc-animationinstancing/Shaders/ShaderLibs/AnimationInstancingInput.hlsl"
            #ifdef _INSTANCING
            #include "Assets/fyc-animationinstancing/Shaders/ShaderLibs/AnimationInstancingBase.hlsl"
            #else
            #include "Assets/fyc-animationinstancing/Shaders/ShaderLibs/AnimationBufferBase.hlsl"
            #endif
            
            #include "../ShaderLibs/Brdf.hlsl"
            #include "ShaderLibs/CharacterFunc.hlsl"
            #ifdef _INSTANCING
            Varyings vert (Attributes IN)
            #else
            Varyings vert (Attributes IN, uint instanceID : SV_InstanceID)
            #endif
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                #ifdef _INSTANCING
                IN.vertex = skinning(IN);
                OUT.vertex = TransformObjectToHClip(IN.vertex.xyz);
                OUT.posW = TransformObjectToWorld(IN.vertex.xyz);
                OUT.normalW =TransformObjectToWorldNormal(IN.normal);
                real sign = real(IN.tangentOS.w) * GetOddNegativeScale();
                OUT.tangentW =  real4(real3(TransformObjectToWorldDir(IN.tangentOS.xyz)), sign); ;
                OUT.bitangentWS = real3(cross(OUT.normalW, real3(OUT.tangentW.xyz))) * sign;
                #else
                float4x4 worldMatrixl = _OutDrawFrameData[instanceID].WorldMatrix;
                IN.vertex = skinning(IN, instanceID);
                OUT.posW = mul(IN.vertex, worldMatrixl);
                OUT.vertex = TransformWorldToHClip(OUT.posW);
                OUT.normalW = mul(IN.normal, (float3x3)worldMatrixl);
                real sign = real(IN.tangentOS.w) * GetOddNegativeScale();
                OUT.tangentW =  real4(mul(IN.tangentOS.xyz, (float3x3)worldMatrixl), sign);
                OUT.bitangentWS = real3(cross(OUT.normalW, real3(OUT.tangentW.xyz))) * sign;
                #endif
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                
                OUT.shadowCoord = TransformWorldToShadowCoord(OUT.posW);
                OUTPUT_SH(OUT.normalW.xyz, OUT.vertexSH);
                return OUT;
            }

            real4 frag (Varyings input) : SV_Target
            {
                //normal
                real3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv), _NormalScale) ;
                real sgn = input.tangentW.w;
                real3 bitangent = sgn * cross(input.normalW.xyz, input.tangentW.xyz);
                real3x3 tangentToWorld = real3x3(input.tangentW.xyz, bitangent.xyz, input.normalW.xyz);
                input.normalW = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                real4 metaillicRoughnessEmissionAO = SAMPLE_TEXTURE2D(_MetallicRoughnessEmissionAOMap, sampler_MetallicRoughnessEmissionAOMap, input.uv);

                real metallic = metaillicRoughnessEmissionAO.r * _Metallic;
                real roughness = metaillicRoughnessEmissionAO.g * _Roughness;
                real emssive = metaillicRoughnessEmissionAO.b * _Emission;
                real ao = saturate(pow(metaillicRoughnessEmissionAO.a, _AO));
                MaterialProperties properties = (MaterialProperties)0;
                InitMaterialProperties(input.uv, metallic, roughness, input.vertexSH.xyz, input.normalW,  emssive, ao, properties);
                properties.emission *= _EmissionColor.rgb;
                
                #if defined(_MAIN_LIGHT_SHADOWS) && defined(_RECEIVE_SHADOWS)
                Light light = GetCustomMainLight(input.shadowCoord);
                #else
                Light light = GetMainLight();
                #endif
                
                real3 V = normalize(GetCameraPositionWS() - input.posW);
                real3 L = normalize(light.direction);

                real3 col = 0;
                
                real shadowAttenuation = 1;
#ifndef _RECEIVE_SHADOWS
                shadowAttenuation = 1;
#else
                shadowAttenuation = max(light.shadowAttenuation, 0.5);
#endif

                float shadowAtten = saturate(dot(input.normalW, L)) * light.distanceAttenuation * shadowAttenuation;
                real3 radiance = light.color * shadowAtten;
                col = evalCombineBRDF(input.normalW, L, V, radiance, shadowAtten, properties) * ao;

                return real4(col.rgb, properties.opacity);
            }
            ENDHLSL
        }

         
//        Pass
//        {
//            Name "ShadowCaster"
//            Tags{"LightMode" = "ShadowCaster"}
//
//            ZWrite On
//            ZTest LEqual
//            Cull Back
//
//            HLSLPROGRAM
//            // Required to compile gles 2.0 with standard srp library
//            #pragma prefer_hlslcc gles
//            #pragma exclude_renderers d3d11_9x
//            #pragma target 2.0
//
//            // -------------------------------------
//            // Material Keywords
//            #pragma shader_feature _ALPHATEST_ON
//
//            //--------------------------------------
//            // GPU Instancing
//            #pragma multi_compile_instancing
//            #pragma shader_feature _SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A
//
//            #pragma vertex CustomShadowPassVertexWithSkinning
//            #pragma fragment CustomShadowPassFragment
//            
//            #include "ShaderLibs/CharacterPBRInput.hlsl"
//            #include "../ShaderLibs/ShadowCastFunc.hlsl"
//            #include "Assets/common-package-animationinstancing/Shader/ShaderLibs/AnimationInstancingShadow.hlsl"
//
//            struct AttributesShadow
//            {
//                float4 vertex   : POSITION;
//                float3 normal     : NORMAL;
//                float2 uv     : TEXCOORD0;
//                real4 uv1     : TEXCOORD1;
//                UNITY_VERTEX_INPUT_INSTANCE_ID
//            };
//
//            float4 GetShadowPositionSkinningHClip(AttributesShadow input)
//            {
//                float3 positionWS = TransformObjectToWorld(input.vertex.xyz);
//                float3 normalWS = TransformObjectToWorldNormal(input.normal);
//
//                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
//                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
//                #else
//                float3 lightDirectionWS = _LightDirection;
//                #endif
//
//                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
//
//                #if UNITY_REVERSED_Z
//                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
//                #else
//                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
//                #endif
//
//                return positionCS;
//            }
//
//            Varyings CustomShadowPassVertexWithSkinning(AttributesShadow IN)
//            {
//                Varyings output;
//                UNITY_SETUP_INSTANCE_ID(IN);
//                IN.vertex = skinningShadow(IN.uv1, IN.vertex);
//                // output.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
//                output.positionCS = GetShadowPositionSkinningHClip(IN);
//                return output;
//            }
//            
//            ENDHLSL
//        }   
        
        Pass
        {
            Name "Debug Draw"
            Tags {
                "LightMode"="DebugDraw"
            }
            
            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma shader_feature_local _RECEIVE_SHADOWS
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "ShaderLibs/CharacterPBRInput.hlsl"
            #include "../ShaderLibs/ShadowFunc.hlsl"
            #include "Assets/common-package-animationinstancing/Shader/ShaderLibs/AnimationInstancingInput.hlsl"
            #include "Assets/common-package-animationinstancing/Shader/ShaderLibs/AnimationInstancingBase.hlsl"
            
            #include "../ShaderLibs/Brdf.hlsl"
            #include "ShaderLibs/CharacterFunc.hlsl"
            #include "../ShaderLibs/DebugDraw.hlsl"
            Varyings vert (Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                IN.vertex = skinning(IN);
                OUT.vertex = TransformObjectToHClip(IN.vertex.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);   
                OUT.posW = TransformObjectToWorld(IN.vertex.xyz);
                OUT.normalW =TransformObjectToWorldNormal(IN.normal);
                real sign = real(IN.tangentOS.w) * GetOddNegativeScale();
                OUT.tangentW =  real4(real3(TransformObjectToWorldDir(IN.tangentOS.xyz)), sign); ;
                OUT.bitangentWS = real3(cross(OUT.normalW, real3(OUT.tangentW.xyz))) * sign;
                OUT.shadowCoord = TransformWorldToShadowCoord(OUT.posW);
                OUTPUT_SH(OUT.normalW.xyz, OUT.vertexSH);
                return OUT;
            }

            real4 frag (Varyings input) : SV_Target
            {
                //normal
                real3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv), _NormalScale) ;
                real sgn = input.tangentW.w;
                real3 bitangent = sgn * cross(input.normalW.xyz, input.tangentW.xyz);
                real3x3 tangentToWorld = real3x3(input.tangentW.xyz, bitangent.xyz, input.normalW.xyz);
                input.normalW = normalize(TransformTangentToWorld(normalTS, tangentToWorld));

                real4 metaillicRoughnessEmissionAO = SAMPLE_TEXTURE2D(_MetallicRoughnessEmissionAOMap, sampler_MetallicRoughnessEmissionAOMap, input.uv);

                real metallic = metaillicRoughnessEmissionAO.r * _Metallic;
                real roughness = metaillicRoughnessEmissionAO.g * _Roughness;
                real emssive = metaillicRoughnessEmissionAO.b * _Emission;
                real ao = saturate(pow(metaillicRoughnessEmissionAO.a, _AO));
                MaterialProperties properties = (MaterialProperties)0;
                InitMaterialProperties(input.uv, metallic, roughness, input.vertexSH.xyz, input.normalW,  emssive, ao, properties);
                properties.emission *= _EmissionColor.rgb;
                
                #if defined(_MAIN_LIGHT_SHADOWS) && defined(_RECEIVE_SHADOWS)
                Light light = GetCustomMainLight(input.shadowCoord);
                #else
                Light light = GetMainLight();
                #endif
                
                real3 V = normalize(GetCameraPositionWS() - input.posW);
                real3 L = normalize(light.direction);

                real3 col = 0;
                
                real shadowAttenuation = 1;
#ifndef _RECEIVE_SHADOWS
                shadowAttenuation = 1;
#else
                shadowAttenuation = max(light.shadowAttenuation, 0.5);
#endif
                float shadowAtten = saturate(dot(input.normalW, L)) * light.distanceAttenuation * shadowAttenuation;
                real3 radiance = light.color * shadowAtten;
                float3 diffuse = 0;
                float3 specular = 0;
                float3 giDiffuse = 0;
                float3 giSpecular = 0;
                float3 emission = 0;
                evalCombineBRDFDebug(input.normalW, L, V, radiance, shadowAtten, properties, diffuse, specular, giDiffuse, giSpecular, emission);

                switch (_DebugDrawType)
                {
                    case DEBUG_DRAW_MODE_ALBEDO:
                        return float4(properties.albedo.rgb, 1);
                    case DEBUG_DRAW_MODE_NORMAL_WORLD:
                        return float4(input.normalW, 1);
                    case DEBUG_DRAW_MODE_NORMAL_LOCAL:
                        return float4(normalTS, 1);
                    case DEBUG_DRAW_MODE_METALLIC:
                        return properties.metallic;
                    case DEBUG_DRAW_MODE_ROUGHNESS:
                        return properties.roughness;
                    case DEBUG_DRAW_MODE_EMISSION:
                        return float4(emission, 1);
                    case DEBUG_DRAW_MODE_LIGHTDIFFUSE:
                        return float4(diffuse, 1);
                    case DEBUG_DRAW_MODE_LIGHTSPECULAR:
                        return float4(specular, 1);
                    case DEBUG_DRAW_MODE_GIDIFFUSE:
                        return float4(giDiffuse, 1);
                    case DEBUG_DRAW_MODE_GISPECULAR:
                        return float4(giSpecular, 1);
                    case DEBUG_DRAW_MODE_AO:
                        return properties.occlusion;
                    case DEBUG_DRAW_MODE_SHADOW:
                        return light.shadowAttenuation;
                }
                return real4(col.rgb, 1);
            }
            ENDHLSL
        }
    }
    CustomEditor "FYC.Editor.CustomShaderGUI"
}