Shader "Starframe/Environment/Range Asteroid Backdrop"
{
    Properties
    {
        _BaseColor ("Plastic white rock", Color) = (0.93, 0.93, 0.93, 1)
        _Smoothness ("Plastic smoothness", Range(0, 1)) = 0.6
        _NearFadeStart ("Near fade start", Float) = 80
        _NearFadeEnd ("Near fade end", Float) = 350
        _FarFadeStart ("Far fade start", Float) = 17000
        _FarFadeEnd ("Far fade end", Float) = 25000
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
                half4 _BaseColor;
                half _Smoothness;
                float _NearFadeStart;
                float _NearFadeEnd;
                float _FarFadeStart;
                float _FarFadeEnd;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fade : TEXCOORD2;
                half3 tint : TEXCOORD3;
                half3 vertexLighting : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float3 world = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(world);
                output.positionWS = world;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tint = input.color.rrr;
                output.vertexLighting = VertexLighting(world, output.normalWS);
                float distanceToCamera = distance(world, _WorldSpaceCameraPos);
                float nearFade = saturate((distanceToCamera - _NearFadeStart) / max(1.0, _NearFadeEnd - _NearFadeStart));
                float farFade = saturate((_FarFadeEnd - distanceToCamera) / max(1.0, _FarFadeEnd - _FarFadeStart));
                output.fade = nearFade * farFade;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                // Screen-door opacity fades whole voxel shells without transparency sorting or blur.
                float2 pixel = floor(input.positionCS.xy);
                float threshold = frac(52.9829189 * frac(0.06711056 * pixel.x + 0.00583715 * pixel.y));
                clip(input.fade - threshold - 0.001);
                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.positionCS = input.positionCS;
                lighting.normalWS = NormalizeNormalPerPixel(input.normalWS);
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                lighting.shadowCoord = float4(0, 0, 0, 0);
                lighting.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1), 0);
                lighting.vertexLighting = input.vertexLighting;
                lighting.bakedGI = SampleSH(lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = _BaseColor.rgb * input.tint;
                surface.specular = half3(0.2, 0.2, 0.2);
                surface.metallic = 0;
                surface.smoothness = _Smoothness;
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;
                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return color;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
