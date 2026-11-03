Shader "Starframe/UI/TargetSilhouetteEdge"
{
    Properties
    {
        [PerRendererData] _MainTex("Closed silhouette",2D)="black"{}
        _OutlineRadius("Outline radius in mask pixels",Float)=1
        _StencilComp("Stencil Comparison",Float)=8
        _Stencil("Stencil ID",Float)=0
        _StencilOp("Stencil Operation",Float)=0
        _StencilWriteMask("Stencil Write Mask",Float)=255
        _StencilReadMask("Stencil Read Mask",Float)=255
        _ColorMask("Color Mask",Float)=15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off Lighting Off ZWrite Off ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            struct Attributes { float4 vertex:POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; };
            struct Varyings { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; float4 color:COLOR; float4 local:TEXCOORD1; };
            sampler2D _MainTex;
            float4 _MainTex_TexelSize, _ClipRect;
            float _OutlineRadius;
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.local=input.vertex;
                output.vertex=UnityObjectToClipPos(input.vertex);
                output.uv=input.uv; output.color=input.color;
                return output;
            }
            half4 Frag(Varyings input):SV_Target
            {
                half center=tex2D(_MainTex,input.uv).r, expanded=center;
                float2 stepUV=abs(_MainTex_TexelSize.xy)*_OutlineRadius;
                [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                    expanded=max(expanded,tex2D(_MainTex,input.uv+float2(x,y)*stepUV).r);
                half4 result=input.color;
                result.a*=saturate(expanded-center);
                #ifdef UNITY_UI_CLIP_RECT
                result.a*=UnityGet2DClipping(input.local.xy,_ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(result.a-.001);
                #endif
                return result;
            }
            ENDCG
        }
    }
}
