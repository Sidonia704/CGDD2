Shader "Starframe/Combat/VoxelGlow"
{
    Properties
    {
        [HDR] _Color ("Voxel color", Color) = (0.12,0.7,1,1)
        _Intensity ("Emission", Float) = 3
        _Opacity ("Opacity", Range(0,1)) = 1
        _AxialShrink ("Cube axial scale", Float) = 1
        _TailFraction ("Dim laser tail", Range(0,1)) = 0
        _FlowEnabled ("Flowing exhaust", Float) = 0
        _FlowPhase ("Continuous exhaust phase", Float) = 0
        _EffectProfile ("0 debris, 1 laser, 2 exhaust", Float) = 0
        [HDR] _HeadColor ("Exhaust white core",Color) = (.9,.98,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options forcemaxcount:128
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _Color;
            float _Intensity, _Opacity, _AxialShrink, _TailFraction;
            float _FlowEnabled, _FlowPhase;
            float _EffectProfile;float4 _HeadColor;
            CBUFFER_END
            #if defined(UNITY_INSTANCING_ENABLED)
            UNITY_INSTANCING_BUFFER_START(VoxelEffects)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                UNITY_DEFINE_INSTANCED_PROP(float4, _HeadColor)
                UNITY_DEFINE_INSTANCED_PROP(float, _Intensity)
                UNITY_DEFINE_INSTANCED_PROP(float, _Opacity)
                UNITY_DEFINE_INSTANCED_PROP(float, _AxialShrink)
                UNITY_DEFINE_INSTANCED_PROP(float, _TailFraction)
                UNITY_DEFINE_INSTANCED_PROP(float, _FlowEnabled)
                UNITY_DEFINE_INSTANCED_PROP(float, _FlowPhase)
                UNITY_DEFINE_INSTANCED_PROP(float, _EffectProfile)
            UNITY_INSTANCING_BUFFER_END(VoxelEffects)
            #define FX_PROP(name) UNITY_ACCESS_INSTANCED_PROP(VoxelEffects, name)
            #else
            #define FX_PROP(name) name
            #endif
            struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float4 color:COLOR; float2 centre:TEXCOORD1; float2 flow:TEXCOORD2; float2 radialCentre:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float shade:TEXCOORD0; float glow:TEXCOORD1; float fade:TEXCOORD2; float4 tint:TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                float4 color=FX_PROP(_Color),headColor=FX_PROP(_HeadColor);
                float axialShrink=FX_PROP(_AxialShrink),tailFraction=FX_PROP(_TailFraction);
                float profile=FX_PROP(_EffectProfile),flowEnabled=FX_PROP(_FlowEnabled),flowPhase=FX_PROP(_FlowPhase);
                float3 p = input.positionOS.xyz;
                p.y = lerp(p.y, input.centre.x + (p.y - input.centre.x) * axialShrink, input.centre.y);
                output.fade = 1;
                output.tint=float4(color.rgb,1);
                if(profile>.5&&profile<1.5){
                    // Clip the actual expanded cube vertices, including very short newly launched bolts.
                    float halfHead=min(.5,axialShrink*.03);
                    float centre=lerp(halfHead,1-halfHead,saturate((input.centre.x-.025)/.935));
                    p.y=saturate(centre+p.y-input.centre.x);output.tint=float4(color.rgb*input.color.rgb*float3(.8,.95,1.6),input.color.a);
                }
                if(profile>1.5)output.tint=float4(headColor.rgb,1);
                if (flowEnabled > .5 && input.flow.y > .5)
                {
                    float life = frac(input.flow.x + flowPhase);
                    float3 originalCentre = float3(input.radialCentre.x, input.centre.x, input.radialCentre.y);
                    float3 offset = p - originalCentre;
                    float shrink = lerp(1,.22,pow(saturate((life-.35)/.65),1.3));
                    float3 flowingCentre = float3(input.radialCentre.x,.055+life*.91,input.radialCentre.y);
                    flowingCentre.xz *= lerp(.3,1.1,life);
                    p = flowingCentre + offset * shrink;
                    p.y=max(0,p.y);
                    output.fade = smoothstep(0,.018,life)*(1-smoothstep(.78,1,life));
                    float3 cyan=float3(.015,.72,1);
                    output.tint.rgb=lerp(headColor.rgb,cyan,smoothstep(.02,.28,life));
                    output.tint.rgb=lerp(output.tint.rgb,color.rgb,smoothstep(.42,.92,life));
                    output.tint.rgb*=lerp(1,.17,pow(life,1.5));
                    if(input.flow.y>1.5)output.fade*=.35*smoothstep(.12,.4,life);
                }
                output.positionCS = TransformObjectToHClip(p);
                // Stable face values keep the little blocks legible through bloom and at oblique views.
                output.shade = dot(abs(input.normalOS), float3(.63,1,.82));
                output.glow = profile<.5&&tailFraction>.001?lerp(.2,1,saturate(input.centre.x/tailFraction)):1;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return half4(input.tint.rgb*max(0,FX_PROP(_Intensity))*input.shade*input.glow,saturate(FX_PROP(_Opacity))*FX_PROP(_Color).a*input.fade*input.tint.a);
            }
            ENDHLSL
        }
    }
}
