Shader "Starframe/Combat/GlowBurst"
{
    Properties
    {
        [HDR]_Color("Burst color", Color) = (.3, .75, 1.5, 1)
        _Intensity("Emission intensity", Float) = 6
        _Opacity("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Back ZWrite Off Blend SrcAlpha One
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity;
                float _Opacity;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS:NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS:TEXCOORD0;float3 world:TEXCOORD1; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);output.world=TransformObjectToWorld(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float center=pow(saturate(dot(normalize(input.normalWS),normalize(GetCameraPositionWS()-input.world))),1.3);
                return half4(_Color.rgb * _Intensity, _Color.a * _Opacity*center);
            }
            ENDHLSL
        }
    }
}
