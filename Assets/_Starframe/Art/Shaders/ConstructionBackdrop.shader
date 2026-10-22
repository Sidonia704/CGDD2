Shader "Starframe/Construction/Backdrop"
{
 Properties
 {
  _TextureStrength("Nebula texture",Range(0,3))=1
  _StarStrength("Star field",Range(0,3))=1
 }
 SubShader
 {
  Tags {"Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline"}
  Cull Off ZWrite Off
  Pass
  {
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   float _TextureStrength,_StarStrength;
   CBUFFER_END
   struct A{float4 positionOS:POSITION;};
   struct V{float4 positionCS:SV_POSITION;float3 direction:TEXCOORD0;};
   V Vert(A v){V o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);o.direction=v.positionOS.xyz;return o;}
   float Hash(float3 p){p=frac(p*.1031);p+=dot(p,p.yzx+33.33);return frac((p.x+p.y)*p.z);}
   float Noise(float3 p)
   {
    float3 a=floor(p),f=frac(p);f=f*f*(3-2*f);
    return lerp(lerp(lerp(Hash(a),Hash(a+float3(1,0,0)),f.x),lerp(Hash(a+float3(0,1,0)),Hash(a+float3(1,1,0)),f.x),f.y),
      lerp(lerp(Hash(a+float3(0,0,1)),Hash(a+float3(1,0,1)),f.x),lerp(Hash(a+float3(0,1,1)),Hash(a+float3(1,1,1)),f.x),f.y),f.z);
   }
   float Fbm(float3 p)
   {
    float n=0,weight=.57;
    [unroll]for(int k=0;k<4;k++){n+=Noise(p)*weight;p=p*2.07+float3(7.3,14.1,3.7);weight*=.48;}
    return n;
   }
   float Stars(float3 d,float density,float seed,float threshold)
   {
    float3 p=d*density,a=floor(p);
    float3 delta=frac(p)-float3(Hash(a+seed+4),Hash(a+seed+9),Hash(a+seed+18));
    return exp(-dot(delta,delta)/.025)*step(threshold,Hash(a+seed))*lerp(.25,1.5,Hash(a+seed+26));
   }
   half4 Frag(V v):SV_Target
   {
    float3 d=normalize(v.direction),p=d*5.5+float3(4.2,8,13);
    float cloud=Fbm(p),detail=Fbm(p*4.3+cloud*2.1);
    float band=.35+.65*exp(-pow(dot(d,normalize(float3(.2,.9,-.4)))*1.5,2));
    float mist=pow(saturate(cloud*1.65-.32),1.4)*band;
    float filaments=pow(saturate(detail*1.9-.72),2)*mist;
    float3 c=float3(.002,.005,.012);
    c+=(float3(.035,.075,.14)*mist+float3(.07,.11,.18)*filaments)*_TextureStrength;
    float stars=Stars(d,175,0,.982)+Stars(d,360,41,.99)*.55+Stars(d,65,77,.997)*1.4;
    c+=float3(.68,.81,1)*stars*_StarStrength;
    return half4(c,1);
   }
   ENDHLSL
  }
 }
}
