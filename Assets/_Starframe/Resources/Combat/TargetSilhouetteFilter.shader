Shader "Hidden/Starframe/TargetSilhouetteFilter"
{
    Properties { _MainTex("Union mask",2D)="black"{} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always Blend Off
        CGINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _MainTex_TexelSize;
        half4 Expand(v2f_img input) : SV_Target
        {
            half value = 0;
            [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                value = max(value, tex2D(_MainTex, input.uv+float2(x,y)*abs(_MainTex_TexelSize.xy)).r);
            return value.xxxx;
        }
        half4 Contract(v2f_img input) : SV_Target
        {
            half value = 1;
            [unroll] for(int y=-1;y<=1;y++) [unroll] for(int x=-1;x<=1;x++)
                value = min(value, tex2D(_MainTex, input.uv+float2(x,y)*abs(_MainTex_TexelSize.xy)).r);
            return value.xxxx;
        }
        ENDCG
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Expand
            ENDCG
        }
        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert_img
            #pragma fragment Contract
            ENDCG
        }
    }
}
