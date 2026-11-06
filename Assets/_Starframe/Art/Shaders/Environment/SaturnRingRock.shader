Shader "Starframe/Environment/Saturn Ring Rock"
{
    Properties
    {
        _BaseColor ("Surface colour", Color) = (0.62, 0.61, 0.59, 1)
        _Smoothness ("Surface smoothness", Range(0, 1)) = 0.13
        _MacroVariation ("Broad patch variation", Range(0, 0.3)) = 0.05
        _MacroScale ("Patch frequency per local metre", Float) = 0.015
        _ContrastReduction ("Face contrast reduction", Range(0, 1)) = 0.18
        _HazeStart ("Distance colour start", Float) = 4000
        _HazeEnd ("Distance colour end", Float) = 24000
        _HazeStrength ("Distance colour strength", Range(0, 0.35)) = 0.35
        _HazeColor ("Distant navy grey", Color) = (0.08, 0.105, 0.14, 1)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Back ZWrite On ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor, _HazeColor;
                half _Smoothness, _MacroVariation, _ContrastReduction, _HazeStrength;
                float _MacroScale, _HazeStart, _HazeEnd;
            CBUFFER_END
            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half3 vertexLighting : TEXCOORD2;
                float3 localPosition : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.vertexLighting = VertexLighting(output.positionWS, output.normalWS);
                output.localPosition = input.positionOS;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = NormalizeNormalPerPixel(input.normalWS);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1), 0);
                lighting.vertexLighting = input.vertexLighting;
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                // Body-local, low-frequency variation follows every static instance at all LODs.
                float3 p = input.localPosition * _MacroScale;
                half patch = sin(p.x + sin(p.z * 0.7)) * sin(p.y * 0.8 + p.z * 0.6);
                surface.albedo = _BaseColor.rgb * (1 + patch * _MacroVariation);
                surface.specular = half3(0.2, 0.2, 0.2);
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;
                half4 color = UniversalFragmentPBR(lighting, surface);
                half3 calm = surface.albedo * (SampleSH(half3(0, 1, 0)) + half3(0.16, 0.16, 0.16));
                color.rgb = lerp(color.rgb, calm, saturate(_ContrastReduction));
                float distanceBlend = smoothstep(_HazeStart, max(_HazeStart + 1, _HazeEnd),
                    distance(input.positionWS, _WorldSpaceCameraPos)) * clamp(_HazeStrength, 0, 0.35);
                color.rgb = lerp(color.rgb, _HazeColor.rgb, distanceBlend);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return half4(color.rgb, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
