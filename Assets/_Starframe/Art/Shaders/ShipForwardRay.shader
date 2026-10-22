Shader "Starframe/Construction/ForwardRay"
{
 SubShader
 {
  Tags {"Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off ZTest Always Blend SrcAlpha OneMinusSrcAlpha
  Pass
  {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   struct A{float4 p:POSITION;float4 c:COLOR;};struct V{float4 p:SV_POSITION;float4 c:COLOR;};
   V Vert(A i){V o;o.p=TransformObjectToHClip(i.p.xyz);o.c=i.c;return o;}
   half4 Frag(V i):SV_Target{return half4(1.8,1.8,1.8,.95)*i.c;}
   ENDHLSL
  }
 }
}