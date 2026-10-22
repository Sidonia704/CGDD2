Shader "Starframe/Flight/ThrusterExhaust"
{
    Properties
    {
        [HDR]_GlowColor("Glow color",Color)=(.025,.3,1,1)
        _Intensity("Intensity",Float)=3
        _Cap("Nozzle disc",Float)=0
    }
    SubShader
    {
        Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
        Cull Off ZWrite Off Blend SrcAlpha One
        Pass
        {
            Tags{"LightMode"="SRPDefaultUnlit"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _GlowColor;float _Intensity,_Cap;
            CBUFFER_END
            struct A{float4 p:POSITION;float2 uv:TEXCOORD0;};
            struct V{float4 p:SV_POSITION;float2 uv:TEXCOORD0;};
            V Vert(A v){V o;o.p=TransformObjectToHClip(v.p.xyz);o.uv=v.uv;return o;}
            half4 Frag(V v):SV_Target
            {
                float profile;
                if(_Cap>.5){
                    float2 p=v.uv*2-1;
                    profile=pow(saturate(1-dot(p,p)),2);
                }else{
                    float length=saturate(v.uv.y);
                    float radial=abs(v.uv.x*2-1)/max(.08,1-length*.9);
                    profile=pow(saturate(1-radial*radial),1.8)*saturate(length/.04)*pow(1-length,1.2);
                }
                clip(profile-.002);
                return half4(_GlowColor.rgb*_Intensity,profile);
            }
            ENDHLSL
        }
    }
}