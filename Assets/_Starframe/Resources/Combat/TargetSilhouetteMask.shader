Shader "Hidden/Starframe/TargetSilhouetteMask"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off ZWrite Off ZTest Always Blend Off
        // Explicit SV_InstanceID and a persistent matrix buffer avoid stripped runtime-only instancing variants.
        Pass
        {
            Name "SelectedInstances"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            StructuredBuffer<float4x4> _StarframeSelectedMatrices;
            float4x4 _StarframeTargetRoot, _StarframeTargetViewProjection;
            struct Attributes { float4 vertex : POSITION; uint instanceID : SV_InstanceID; };
            float4 Vert(Attributes input) : SV_POSITION
            {
                float4 local = mul(_StarframeSelectedMatrices[input.instanceID], input.vertex);
                return mul(_StarframeTargetViewProjection, mul(_StarframeTargetRoot, local));
            }
            half4 Frag() : SV_Target { return half4(1,1,1,1); }
            ENDHLSL
        }
        Pass
        {
            Name "SelectedFallback"
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #include "UnityCG.cginc"
            float4x4 _StarframeTargetRoot, _StarframeTargetViewProjection;
            float4 Vert(float4 vertex : POSITION) : SV_POSITION
            {
                return mul(_StarframeTargetViewProjection, mul(_StarframeTargetRoot, mul(unity_ObjectToWorld, vertex)));
            }
            half4 Frag() : SV_Target { return half4(1,1,1,1); }
            ENDHLSL
        }
    }
}
