Shader "Starframe/SpaceSky"
{
    Properties
    {
        _Exposure ("Exposure", Range(0, 4)) = 1
        _NebulaStrength ("Nebula Strength", Range(0, 3)) = 1
        _StarStrength ("Star Strength", Range(0, 5)) = 1
        _QuietSky ("Restrained observatory sky", Range(0, 1)) = 1
        _CloudTint ("Distant dust tint", Color) = (.115,.14,.16,1)
        _BackgroundTint ("Deep space tint", Color) = (.0025,.004,.0065,1)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                float _Exposure;
                float _NebulaStrength;
                float _StarStrength;
                float _QuietSky;
                float4 _CloudTint;
                float4 _BackgroundTint;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }

            float Hash(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.yzx + 33.33);
                return frac((p.x + p.y) * p.z);
            }

            float Noise(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f*f*(3.0-2.0*f);
                float a = lerp(Hash(i), Hash(i+float3(1,0,0)), f.x);
                float b = lerp(Hash(i+float3(0,1,0)), Hash(i+float3(1,1,0)), f.x);
                float c = lerp(Hash(i+float3(0,0,1)), Hash(i+float3(1,0,1)), f.x);
                float d = lerp(Hash(i+float3(0,1,1)), Hash(i+float3(1,1,1)), f.x);
                return lerp(lerp(a,b,f.y), lerp(c,d,f.y), f.z);
            }

            float Fbm(float3 p)
            {
                float sum = 0;
                float amplitude = 0.5;
                [unroll] for (int i=0; i<5; i++)
                {
                    sum += amplitude * Noise(p);
                    p = p * 2.03 + float3(9.2, 17.4, 3.8);
                    amplitude *= 0.5;
                }
                return sum;
            }

            float3 Stars(float3 direction, float density, float seed)
            {
                float3 p = direction * density;
                float3 cell = floor(p);
                float rnd = Hash(cell + seed);
                float3 center = float3(Hash(cell+seed+3.1), Hash(cell+seed+7.8), Hash(cell+seed+13.4));
                float3 delta = frac(p) - center;
                float radius = lerp(0.075, 0.22, Hash(cell+seed+23.0))*lerp(1,.65,_QuietSky);
                float star = exp(-dot(delta,delta)/(radius*radius));
                star *= step(lerp(.987,.993,_QuietSky), rnd);
                float3 tint = lerp(float3(0.58,0.76,1.0), float3(1.0,0.86,0.68), Hash(cell+31.7));
                tint=lerp(tint,lerp(float3(.77,.85,.93),float3(1,.94,.83),Hash(cell+31.7)),_QuietSky);
                return tint * star * lerp(0.7,3.4,Hash(cell+40.2));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 d = normalize(input.direction);
                float3 p = d * 4.5 + float3(17.2, 6.4, 13.9);
                float noise = Fbm(p);
                float detail = Fbm(p*2.8 + noise*2.4);
                float plane = dot(d, normalize(float3(-0.26, 1, 0.38)));
                float band = exp(-pow((plane + (noise-0.5)*0.27)/0.21, 2));
                float clouds = pow(saturate(noise * 1.3 + detail * 0.35 - 0.28), 2);
                float3 tint = lerp(float3(0.22,0.035,0.39), float3(0.025,0.22,0.46), saturate(d.x*1.1+0.55));
                tint=lerp(tint,_CloudTint.rgb,_QuietSky);
                float3 color = lerp(float3(0.003,0.006,0.017),_BackgroundTint.rgb,_QuietSky);
                color += tint * band * clouds * 1.4 * _NebulaStrength;
                color += lerp(float3(.04,.05,.11),float3(.032,.038,.043),_QuietSky) * band * pow(detail,4) * lerp(1.8,.55,_QuietSky);
                float lane = pow(saturate(Fbm(p*1.4 + 34.0)-0.4),2);
                color *= 1.0 - saturate(lane * band * 7.0);
                color += (Stars(d,190.0,0.0) + Stars(d,410.0,54.0)*0.7) * _StarStrength;
                return half4(color * _Exposure, 1);
            }
            ENDHLSL
        }
    }
}
