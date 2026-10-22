Shader "Starframe/Ships/EnergyShield"
{
 Properties
 {
  [HDR] _Color("Shield tint / rim opacity",Color)=(.2,.5,.85,.055)
  _SurfaceOpacity("Surface opacity",Range(0,.05))=.0015
  _HitOpacity("Hit flash opacity",Range(0,1))=.12
  [HideInInspector] _LayerOpacityScale("Overlap attenuation",Float)=1
  _ChargeRatio("Charge",Range(0,1))=1
  _HitPulse("Hit flash",Range(0,1))=0
  _HitPosition("Hit position",Vector)=(0,0,0,0)
 }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent-20"}
  Pass
  {
   Tags {"LightMode"="SRPDefaultUnlit"}
   Blend SrcAlpha One
   ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float4 _Color,_HitPosition;float _ChargeRatio,_HitPulse,_SurfaceOpacity,_HitOpacity,_LayerOpacityScale;
   CBUFFER_END
   struct A {float4 positionOS:POSITION;float3 normalOS:NORMAL;};
   struct V {float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;float3 normalWS:TEXCOORD1;float3 local:TEXCOORD2;};
   V Vert(A i){V o;o.positionWS=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.positionWS);o.normalWS=TransformObjectToWorldNormal(i.normalOS);o.local=i.positionOS.xyz;return o;}
   half4 Frag(V i):SV_Target
   {
    float fresnel=pow(1-abs(dot(normalize(i.normalWS),GetWorldSpaceNormalizeViewDir(i.positionWS))),4);
    float wave=.5+.5*sin(i.local.y*32-_Time.y*1.8);
    float impact=exp(-distance(i.positionWS,_HitPosition.xyz)*1.3)*_HitPulse;
    float alpha=(_SurfaceOpacity*(.75+wave*.25)+fresnel*_Color.a)*lerp(.35,1,_ChargeRatio)+impact*_HitOpacity;
    alpha*=saturate(_LayerOpacityScale);
    return half4(_Color.rgb*(.55+fresnel*.4)+impact*float3(.2,.45,.7),saturate(alpha));
   }
   ENDHLSL
  }
 }
}
