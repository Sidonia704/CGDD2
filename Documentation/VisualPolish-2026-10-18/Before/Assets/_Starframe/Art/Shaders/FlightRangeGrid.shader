Shader "Starframe/Flight/RangeGrid"
{
 Properties { _PlaneAxis("Axis",Float)=2 _Boundary("Boundary",Float)=0 _CellSize("Cell",Float)=4
  _FadeNear("Near",Float)=30 _FadeFar("Far",Float)=130 _BoundaryFadeDistance("Boundary Fade",Float)=220 _PlayerPosition("Player",Vector)=(0,0,0,0) }
 SubShader
 {
  Tags{"RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent"}
  Cull Off ZWrite Off Blend SrcAlpha OneMinusSrcAlpha
  Pass
  {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float _PlaneAxis,_Boundary,_CellSize,_FadeNear,_FadeFar,_BoundaryFadeDistance;float4 _PlayerPosition;
   CBUFFER_END
   struct A{float4 p:POSITION;};struct V{float4 p:SV_POSITION;float3 world:TEXCOORD0;};
   V Vert(A v){V o;o.world=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.world);return o;}
   float Line(float2 p,float cell,float width)
   {
    float2 distance=abs(frac(p/cell+.5)-.5)*cell;
    float2 aa=max(fwidth(p),.001);
    return max(1-smoothstep(width,width+aa.x,distance.x),1-smoothstep(width,width+aa.y,distance.y));
   }
   half4 Frag(V v):SV_Target
   {
    float2 plane=_PlaneAxis<.5?v.world.yz:_PlaneAxis<1.5?v.world.xz:v.world.xy;
    float grid=max(Line(plane,max(.01,_CellSize),.015)*.18,Line(plane,max(.01,_CellSize)*10,.035)*.34);
    float alpha;
    if(_Boundary<.5)alpha=grid*(1-smoothstep(_FadeNear,_FadeFar,distance(v.world,_PlayerPosition.xyz)));
    else{
     float planePosition=_PlaneAxis<.5?v.world.x:_PlaneAxis<1.5?v.world.y:v.world.z;
     float playerPosition=_PlaneAxis<.5?_PlayerPosition.x:_PlaneAxis<1.5?_PlayerPosition.y:_PlayerPosition.z;
     alpha=(.025+grid)*(1-smoothstep(0,_BoundaryFadeDistance,abs(planePosition-playerPosition)));
    }
    clip(alpha-.001);
    return half4(.25,1,3.5,saturate(alpha));
   }
   ENDHLSL
  }
 }
}