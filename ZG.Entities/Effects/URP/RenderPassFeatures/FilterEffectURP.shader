// This shader fills the mesh shape with a color predefined in the code.
Shader "ZG/FilterEffectURP"
{
    // The properties block of the Unity shader. In this example this block is empty
    // because the output color is predefined in the fragment shader code.
    Properties
    { }

    // The SubShader block containing the Shader code. 
    SubShader
    {
        // SubShader Tags define when and under which conditions a SubShader block or
        // a pass is executed.
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            // The HLSL code block. Unity SRP uses the HLSL language.
            HLSLPROGRAM
            // This line defines the name of the vertex shader. 
            #pragma vertex vert
            // This line defines the name of the fragment shader. 
            #pragma fragment frag

            // The Core.hlsl file contains definitions of frequently used HLSL
            // macros and functions, and also contains #include references to other
            // HLSL files (for example, Common.hlsl, SpaceTransforms.hlsl, etc.).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"            

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x gles
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            // The structure definition defines which variables it contains.
            // This example uses the Attributes structure as an input structure in
            // the vertex shader.
            struct Attributes
            {
                float3 normalOS : NORMAL;

                // The positionOS variable contains the vertex positions in object
                // space.
                float4 positionOS   : POSITION; 

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                half3 color : TEXCOORD0;
                // The positions in this struct must have the SV_POSITION semantic.
                float4 positionHCS  : SV_POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            half4 _FilterColor;

            // The vertex shader definition with properties defined in the Varyings 
            // structure. The type of the vert function must match the type (struct)
            // that it returns.
            Varyings vert(Attributes input)
            {
                // Declaring the output object (OUT) with the Varyings struct.
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float value = 1.5f - max(dot(input.normalOS, normalize(TransformWorldToObject(_WorldSpaceCameraPos) - input.positionOS.xyz)), 0.0f);
                output.color = _FilterColor.rgb * value;
                // The TransformObjectToHClip function transforms vertex positions
                // from object space to homogenous clip space.
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                // Returning the output.
                return output;
            }

            // The fragment shader definition.            
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                return half4(input.color, _FilterColor.a);
            }
            ENDHLSL
        }

        Pass
        {
            // The HLSL code block. Unity SRP uses the HLSL language.
            HLSLPROGRAM
            // This line defines the name of the vertex shader. 
            #pragma vertex vert
            // This line defines the name of the fragment shader. 
            #pragma fragment frag

            // The Core.hlsl file contains definitions of frequently used HLSL
            // macros and functions, and also contains #include references to other
            // HLSL files (for example, Common.hlsl, SpaceTransforms.hlsl, etc.).
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl" 

            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x gles
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON

            // The structure definition defines which variables it contains.
            // This example uses the Attributes structure as an input structure in
            // the vertex shader.
            struct Attributes
            {
                // The positionOS variable contains the vertex positions in object
                // space.
                float4 positionOS   : POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                half4 color : TEXCOORD0;
                // The positions in this struct must have the SV_POSITION semantic.
                float4 positionHCS  : SV_POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            uniform half4 g_FilterSourceColor;
            uniform half4 g_FilterDestinationColor;

            uniform int g_FilterParamsLength = 0;
            uniform half4 g_FilterParams[32];

            // The vertex shader definition with properties defined in the Varyings 
            // structure. The type of the vert function must match the type (struct)
            // that it returns.
            Varyings vert(Attributes input)
            {
                // Declaring the output object (OUT) with the Varyings struct.
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float4 param;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float alpha = 1.0f;
                for (int i = 0; i < g_FilterParamsLength; ++i)
                {
                    param = g_FilterParams[i];
                    alpha = min(alpha, distance(param.xyz, positionWS) / param.w);
                }

                output.color = lerp(g_FilterSourceColor, g_FilterDestinationColor, alpha);
                // The TransformObjectToHClip function transforms vertex positions
                // from object space to homogenous clip space.
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);

                // Returning the output.
                return output;
            }

            // The fragment shader definition.            
            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                return input.color;
            }
            ENDHLSL
        }
    }
}