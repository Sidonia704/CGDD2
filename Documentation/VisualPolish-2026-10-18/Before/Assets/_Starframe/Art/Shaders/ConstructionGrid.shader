Shader "Starframe/Construction/Grid"
{
 SubShader
 {
  Tags {"Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  Pass
  {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A{float4 positionOS:POSITION;};
   struct V{float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;};
   V Vert(A v){V o;o.world=TransformObjectToWorld(v.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);return o;}
   half4 Frag(V i):SV_Target
   {
    float2 p=i.world.xz;float2 fw=max(fwidth(p),.003);
    float2 distance=abs(frac(p+.5)-.5);
    float2 fine=1-smoothstep(fw*.35,fw*.95,distance);
    float2 majorDistance=abs(frac(p/5+.5)-.5)*5;
    float major=max(1-smoothstep(fw.x*.5,fw.x*1.5,majorDistance.x),1-smoothstep(fw.y*.5,fw.y*1.5,majorDistance.y));
    float dots=max(fine.x,fine.y)*step(.78,frac(p.x*4))*step(.78,frac(p.y*4));
    float fade=exp(-length(p)*.045);
    float2 axis=1-smoothstep(fw*.22,fw*.72,abs(p));
    float axes=max(axis.x,axis.y*step(.43,frac(p.x*1.7)));
    float frame=1-smoothstep(.009,.009+max(fw.x,fw.y),abs(max(abs(p.x),abs(p.y))-6));
    float ring=1-smoothstep(.01,.01+max(fw.x,fw.y),abs(length(p)-1.85));
    float alpha=saturate((max(fine.x,fine.y)*.025+major*.06+dots*.18+frame*.45+ring*.3)*fade+axes*.8);
    return half4(.46,.66,.84,alpha);
   }
   ENDHLSL
  }
 }
}
