Shader "Unlit/Water"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)  // Note: float4 syntax, not "white"
        _WaterHeightMulti ("Water height", Range(0.0, 5.0)) = 1.2
        _WaterSize ("Water size", Float) = 2000
        _WaterOrigin ("Water origin", Vector) = (0,0,0,0)
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        LOD 100

        Pass
        {
            HLSLPROGRAM  // Changed from CGPROGRAM
            
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            // URP includes
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // Texture declarations
            TEXTURE2D(_WaterSimulation);
            SAMPLER(sampler_WaterSimulation);
            
            // Material properties
            half4 _Color;
            float _WaterHeightMulti;
            float _WaterSize;
            float4 _PatchInfo;
            float4 _WaterOrigin;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings  
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 waterData : TEXCOORD1; 
            };
            //Water Data Format - R-> Velocity | G-> Height | B -> Foam Level | A -> Blank Space
            //Patch Data format - x -> Patch Center X | y -> Patch Center Y | z -> Patch Size
            Varyings vert(Attributes input)
            {
                // Compute world position of the vertex
                float3 worldPos = TransformObjectToWorld(input.positionOS.xyz);

                // Map world XZ to simulation UVs across the entire combined mesh.
                // _WaterOrigin.xy is the world-space bottom-left origin of the combined mesh.
                float x = _PatchInfo.x + (input.uv.x)*_PatchInfo.z - _PatchInfo.z/2;
                float y = _PatchInfo.y + (input.uv.y)*_PatchInfo.z - _PatchInfo.z/2;
                float ux = (_WaterOrigin.x - x)/(_WaterSize/2)- 1;
                float uy = (_WaterOrigin.y - y)/(_WaterSize/2) - 1;
                float2 simUV = (float2(x, y) - _WaterOrigin.xy) / _WaterSize + 0.5;
                float4 waterData = SAMPLE_TEXTURE2D_LOD(_WaterSimulation, sampler_WaterSimulation, simUV, 0);
                float height = waterData.g * _WaterHeightMulti;
                float4 pos = float4(worldPos.x, height, worldPos.z, 1);
    
                Varyings output;
                output.positionCS = TransformWorldToHClip(pos);
                output.uv = simUV;
                output.waterData = waterData;
    
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float4 st = SAMPLE_TEXTURE2D_LOD(_WaterSimulation, sampler_WaterSimulation, input.uv, 0);
                return st * (1-st.b) + st.b * float4(1, 1, 1, 1);

            }
            ENDHLSL
        }
    }
}