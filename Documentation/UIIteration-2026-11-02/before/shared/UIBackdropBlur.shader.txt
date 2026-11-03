Shader "Starframe/UI/BackdropBlur"
{
 Properties
 {
  [PerRendererData] _MainTex("Background",2D)="white"{}
  _Tint("Tint",Color)=(.048,.058,.065,.48)
  _BlurRadius("Blur Radius",Float)=32
  _PanelUVRect("Panel Rect",Vector)=(0,0,1,1)
  _GrainStrength("Grain",Float)=.0008
  [HideInInspector] _SheenStrength("Legacy reflection (unused)",Float)=0
  _FinishStrength("Frosted optical finish",Range(0,1))=1
  _BackdropSaturation("Transmitted saturation",Range(0,1))=.25
  _BackdropTransmission("Transmitted brightness",Range(.3,1.2))=.86
  _RefractionPixels("Soft refraction",Range(0,4))=1.6
 }
 SubShader
 {
  Tags {"Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"}
  Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
  Blend SrcAlpha OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma target 3.0
   #pragma vertex Vert
   #pragma fragment Frag
   #include "UnityCG.cginc"
   struct A{float4 vertex:POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
   struct V{float4 vertex:SV_POSITION;float2 uv:TEXCOORD0;float4 color:COLOR;};
   sampler2D _MainTex;float4 _MainTex_TexelSize,_Tint,_PanelUVRect;float _BlurRadius,_GrainStrength,_SheenStrength;
   float _FinishStrength,_BackdropSaturation,_BackdropTransmission,_RefractionPixels;
   V Vert(A v){V o;o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color;return o;}
   half4 Frag(V i):SV_Target
   {
    float weights[5]={1,4,6,4,1};half3 color=0;
    float2 local=saturate((i.uv-_PanelUVRect.xy)/max(_PanelUVRect.zw,.00001));
    // A broad, static lens field gives the pane depth without a moving distortion or a drawn border.
    float2 lens=(local-.5)*(1-dot(local-.5,local-.5));
    float2 sampleUV=i.uv+lens*_MainTex_TexelSize.xy*_RefractionPixels*_FinishStrength;
    // Prefilter tiny stars before sampling a broad blur, avoiding a visible grid of duplicated dots.
    float lod=max(0,log2(max(1,_BlurRadius))-1.2);
    float2 stepUV=_MainTex_TexelSize.xy*max(1,_BlurRadius*.3);
    [unroll]for(int y=-2;y<=2;y++)[unroll]for(int x=-2;x<=2;x++)
     color+=tex2Dlod(_MainTex,float4(sampleUV+float2(x,y)*stepUV,0,lod)).rgb*(weights[x+2]*weights[y+2]/256.0);
    float grain=frac(sin(dot(floor(i.uv*_MainTex_TexelSize.zw),float2(12.9898,78.233)))*43758.5453)-.5;
    float luminance=dot(color,float3(.2126,.7152,.0722));
    color=lerp(color,lerp(luminance.xxx,color,_BackdropSaturation)*_BackdropTransmission,_FinishStrength);
    color=lerp(color,_Tint.rgb,_Tint.a);
    // Frosted panes have uniform transmission. All visible variation comes from the blurred scene.
    color+=grain*_GrainStrength;
    return half4(color*i.color.rgb,i.color.a);
   }
   ENDCG
  }
 }
}
