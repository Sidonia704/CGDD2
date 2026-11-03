Shader "Starframe/Flight/AtmosphereParticle"
{
    Properties { _MainTex("Soft density",2D)="white"{} _Softness("Depth softening",Float)=2 _Gain("Light gain",Float)=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE2D(_MainTex);SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float _Softness,_Gain;
            CBUFFER_END
            float4 _StarframeRangeSkyState,_StarframeRangeSkyLight,_StarframeRangeSkyWarm,_StarframeRangeSkyCool;
            struct A {float4 positionOS:POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;};
            struct V {float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0;half4 color:COLOR;float eyeDepth:TEXCOORD1;};
            V Vert(A v)
            {
                V o;float3 world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(world);o.uv=v.uv;o.color=v.color;o.eyeDepth=-TransformWorldToView(world).z;
                float facing=pow(saturate(dot(normalize(world-_WorldSpaceCameraPos),_StarframeRangeSkyLight.xyz)),8.0);
                float3 lit=0.45+_StarframeRangeSkyCool.rgb*0.7+_StarframeRangeSkyWarm.rgb*(0.65+facing*2.2);
                o.color.rgb*=lerp(float3(1,1,1),lit,saturate(_StarframeRangeSkyState.x));return o;
            }
            half4 Frag(V v):SV_Target
            {
                half alpha=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,v.uv).r*v.color.a;
                clip(alpha-.0005);
                float sceneEye=LinearEyeDepth(SampleSceneDepth(GetNormalizedScreenSpaceUV(v.positionCS)),_ZBufferParams);
                alpha*=saturate((sceneEye-v.eyeDepth)/max(.01,_Softness));
                alpha*=smoothstep(2,8,v.eyeDepth);
                return half4(v.color.rgb*_Gain,alpha);
            }
            ENDHLSL
        }
    }
}
