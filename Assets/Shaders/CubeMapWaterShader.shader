Shader "Unlit/CubemapWater"
{

//NOTE: This implementation only works in a room with infinite distance to any objects given the limitations of a cube map
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)  
        _WaterHeightMulti ("Water height", Range(0.0, 5.0)) = 1.2
        _WaterSize ("Water size", Float) = 2000
        _WaterOrigin ("Water origin", Vector) = (0,0,0,0)
        _Smoothness ("Smoothness", Range(1, 256)) = 64
        _FresnelPower ("Fresnel Power", Range(0.1, 10)) = 5
        _SampleOffset ("Normal Sample Offset", Float) = 1.0
        _RefractionDistortion ("Refraction Distortion", Range(0.0, 1.0)) = 0.1
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM  
            
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            // URP includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/GlobalIllumination.hlsl"

            // Texture declarations
            samplerCUBE _ReflectionCube;

            TEXTURE2D(_RefractionTexture);
            SAMPLER(sampler_RefractionTexture);


            TEXTURE2D(_WaterSimulation);
            SAMPLER(sampler_WaterSimulation);

            // Material properties
            half4 _Color;
            float _BaseWaterHeight;
            float _WaterHeightMulti;
            float _WaterSize;
            float4 _PatchInfo;
            float4 _WaterOrigin;
            float _Smoothness;
            float _FresnelPower;
            float _SampleOffset;
            float _RefractionDistortion;

            
            

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Varyings  
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : NORMAL0;
                float2 uv         : TEXCOORD0;
                half4 waterData   : TEXCOORD1; 
                float3 positionWS : TEXCOORD2;
                float  fogCoord   : TEXCOORD3;
            };
            //Water Data Format - R-> Velocity | G-> Height | B -> Foam Level | A -> Blank Space
            //Patch Data format - x -> Patch Center X | y -> Patch Center Y | z -> Patch Size

            
            float SampleWaterHeight(float2 worldXZ)
            {
                float2 simUV = (worldXZ - _WaterOrigin.xy) / _WaterSize + 0.5;
                float4 waterData = SAMPLE_TEXTURE2D_LOD(_WaterSimulation, sampler_WaterSimulation, simUV, 0);

                float height;
                if (waterData.g == 0)
                {
                    height = 0;
                }
                else
                {
                    height = waterData.g * _WaterHeightMulti;
                }
                return height;
            }
            
            Varyings vert(Attributes input)
            {
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);
                float x = _PatchInfo.x + (input.uv.x)*_PatchInfo.z - _PatchInfo.z/2;
                float y = _PatchInfo.y + (input.uv.y)*_PatchInfo.z - _PatchInfo.z/2;
                float ux = (_WaterOrigin.x - x)/(_WaterSize/2)- 1;
                float uy = (_WaterOrigin.y - y)/(_WaterSize/2) - 1;
                float2 simUV = (float2(x, y) - _WaterOrigin.xy) / _WaterSize + 0.5;
                float4 waterData = SAMPLE_TEXTURE2D_LOD(_WaterSimulation, sampler_WaterSimulation, simUV, 0);
                float height;
                if(waterData.g == 0)
                {
                    height = 0;
                } else
                {
                    height = waterData.g * _WaterHeightMulti;
                }
                float4 pos = float4(worldPos.x, _BaseWaterHeight + height, worldPos.z, 1);

                Varyings output;
                output.positionCS = TransformWorldToHClip(pos);
                output.uv = simUV;
                output.waterData = waterData;
                output.positionWS = pos.xyz;

                //Calculate Normals
                // Reconstruct the surface normal from the height field by
                // sampling neighboring heights and taking the cross product
                // of the resulting tangent vectors (finite-difference method).

                float offset = _SampleOffset;
                float hL = SampleWaterHeight(float2(worldPos.x - offset, worldPos.z));
                float hR = SampleWaterHeight(float2(worldPos.x + offset, worldPos.z));
                float hD = SampleWaterHeight(float2(worldPos.x, worldPos.z - offset));
                float hU = SampleWaterHeight(float2(worldPos.x, worldPos.z + offset));

                float3 tangentX = float3(2.0 * offset, hR - hL, 0.0);
                float3 tangentZ = float3(0.0, hU - hD, 2.0 * offset);
                output.normalWS = normalize(cross(tangentZ, tangentX));

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(GetCameraPositionWS() - input.positionWS);
                float foamMulti = input.waterData.b;

                //Light
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 lightDirWS = normalize(mainLight.direction);
                float3 radiance = mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                // Diffuse 
                float NdotL = saturate(dot(normalWS, lightDirWS));
                float3 diffuse = radiance * NdotL * _Color.rgb;

                // Specular 
                float3 halfDir = normalize(lightDirWS + viewDirWS);
                float NdotH = saturate(dot(normalWS, halfDir));
                float3 specular = radiance * pow(NdotH, _Smoothness);

                //Ambient
                float3 ambient = _Color.rgb;

                //Cube Map environmental reflections, calculating the fresnel effect
                float NdotV = saturate(dot(normalWS, viewDirWS));
                float fresnel = pow(1.0 - NdotV, _FresnelPower);

                // Sample reflection with vertical inversion
                float3 reflectionDir = reflect(-viewDirWS, normalWS);
                float3 reflectionColor = texCUBE(_ReflectionCube, reflectionDir);


                // Sample refraction with normal-based distortion
                
                float2 refractionUV = screenUV + (normalWS.xz * _RefractionDistortion);
                refractionUV = clamp(refractionUV, float2(0.0, 0.0), float2(1.0, 1.0));
                float3 refractionColor = SAMPLE_TEXTURE2D(_RefractionTexture, sampler_RefractionTexture, refractionUV).rgb;

                
                // Blend reflection and refraction based on fresnel
                float3 finalColor = lerp(refractionColor, reflectionColor, fresnel) + specular + ambient + float3(1, 1, 1) * round(foamMulti);

                // Apply fog
                finalColor = MixFog(finalColor, input.fogCoord);

                float4 result = float4(reflectionColor, _Color.a);

                return result;

                //Below is shading for debugging purposes. Do not touch.
                //float4 st = SAMPLE_TEXTURE2D_LOD(_WaterSimulation, sampler_WaterSimulation, input.uv, 0);
                //if(st.g == 0)
                //   return _Color; //
                
                //return st * (1-st.b) + st.b * float4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }
}