Shader "Starframe/Ships/FlowingEnergy"
{
 Properties
 {
  _BaseColor("Body tint",Color)=(.025,.15,.5,1)
  [HDR] _EmissionColor("Energy emission",Color)=(.04,.6,2.5,1)
  _FlowSpeed("Flow speed",Float)=.8
  _FlowStrength("Flow contrast",Range(0,1))=.4
  _Smoothness("Plastic highlight",Range(0,1))=.65
  _BaseMap("Base map",2D)="white"{}
  _Cutoff("Alpha cutoff",Float)=.5
  _Cull("Cull",Float)=2
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   ZWrite On Cull Back
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _BaseColor,_EmissionColor;
   float _FlowSpeed,_FlowStrength,_Smoothness;
   CBUFFER_END
   struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
   struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float3 positionOS:TEXCOORD2;};
   V Vert(A i){V o;o.positionWS=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.positionOS=i.positionOS.xyz;return o;}
   half4 Frag(V i):SV_Target
   {
    float3 n=normalize(i.normalWS),v=GetWorldSpaceNormalizeViewDir(i.positionWS);
    Light light=GetMainLight(TransformWorldToShadowCoord(i.positionWS));
    float3 h=normalize(v+light.direction);
    float highlight=pow(saturate(dot(n,h)),lerp(16,128,_Smoothness));
    float3 body=_BaseColor.rgb*(SampleSH(n)+light.color*saturate(dot(n,light.direction))*light.shadowAttenuation);
    body+=light.color*highlight*.6*light.shadowAttenuation;
    float t=_Time.y*_FlowSpeed;
    float flow=.5+.5*sin(dot(i.positionOS,float3(8,13,6))-t*3+sin(i.positionOS.z*10+t));
    float shimmer=.5+.5*sin(dot(i.positionOS,float3(19,-8,15))+t*2);
    float emission=lerp(1,.5+.65*flow+.2*shimmer,_FlowStrength);
    return half4(body+_EmissionColor.rgb*emission,1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
