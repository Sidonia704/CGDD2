Shader "Starframe/Combat/LaserProjectile"
{
    Properties
    {
        [HDR]_GlowColor("Outer glow", Color) = (.025, .2, 1.6, 1)
        [HDR]_CoreColor("Bright core", Color) = (.65, 1.2, 1.8, 1)
        _Intensity("Emission intensity", Float) = 5.5
        _TailFraction("Tapered tail fraction", Range(0, .9)) = .35
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Cull Off ZWrite Off Blend SrcAlpha One
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _GlowColor;
                float4 _CoreColor;
                float _Intensity;
                float _TailFraction;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float tailWidth = _TailFraction > .0001 ? lerp(.015, 1.0, saturate(input.uv.y / _TailFraction)) : 1.0;
                float radial = abs(input.uv.x * 2.0 - 1.0) / tailWidth;
                float outer = pow(saturate(1.0 - radial * radial), 1.7);
                float core = 1.0 - smoothstep(.12, .52, radial);
                float tail = _TailFraction > .0001
                    ? smoothstep(0.0, _TailFraction, input.uv.y)
                    : 1.0;
                float endFade = smoothstep(0.0, .035, input.uv.y);
                float alpha = outer * tail * endFade;
                clip(alpha - .003);
                float3 color = lerp(_GlowColor.rgb, _CoreColor.rgb, core);
                float emission = _Intensity * (lerp(.75, 1.7, core));
                return half4(color * emission, alpha);
            }
            ENDHLSL
        }
    }
}
