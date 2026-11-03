Shader "Starframe/Tools/VoxelPreview"
{
    Properties { _BaseColor ("Color", Color) = (.55,.57,.6,1) }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            CBUFFER_END
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output; output.positionCS=TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS); return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                float3 n=normalize(input.normalWS);
                float light=.30+.64*saturate(dot(n,normalize(float3(-.45,.8,-.5))))+.13*saturate(dot(n,normalize(float3(.7,.2,.5))));
                return half4(_BaseColor.rgb*light,1);
            }
            ENDHLSL
        }
    }
}
