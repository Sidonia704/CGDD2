Shader "Starframe/Environment/Saturn Stylized Sky"
{
    Properties
    {
        _Background ("Deep space", Color) = (0.008, 0.020, 0.05, 1)
        _GridColor ("Calibration lines", Color) = (0.04, 0.09, 0.18, 1)
        _GridStrength ("Calibration line strength", Range(0, 1)) = 0
        _PlanetColor ("Warm stone daylight", Color) = (1, 0.42, 0.14, 1)
        _NightColor ("Planet night", Color) = (0.15, 0.063, 0.021, 1)
        _AtmosphereColor ("Limb colour", Color) = (0.27, 0.35, 0.45, 1)
        _AtmosphereStrength ("Limb strength", Range(0, 0.3)) = 0.065
        _PlanetAmbient ("Planet ambient light", Range(0, 0.5)) = 0.15
        _SunAngularRadius ("Sun radius in degrees", Range(0.05, 1)) = 0.22
        _SunIntensity ("Sun brightness", Range(0, 5)) = 2.5
        _SunHaloStrength ("Sun halo strength", Range(0, 0.3)) = 0.08
        _BandStrength ("Broad latitude band strength", Range(0, 0.2)) = 0.045
        _PlanetPoleDirection ("Planet axis", Vector) = (0.18, 0.97, 0.24, 0)
        _ShowPlanet ("Planet", Float) = 1
        _PlanetWorldRadius ("World radius", Float) = 20000000
        _PlanetCenterWS ("Center", Vector) = (-20300000, -400000, 800000, 0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "RenderPipeline"="UniversalPipeline" "PreviewType"="Skybox" }
        Pass
        {
            Cull Off ZWrite Off ZTest LEqual
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Background, _GridColor, _PlanetColor, _NightColor;
                float4 _AtmosphereColor, _PlanetPoleDirection, _PlanetCenterWS;
                float _GridStrength, _AtmosphereStrength, _PlanetAmbient, _BandStrength;
                float _ShowPlanet, _PlanetWorldRadius;
                float _SunAngularRadius, _SunIntensity, _SunHaloStrength;
            CBUFFER_END
            float4 _StarframeRangeSkyState, _StarframeRangeSkyLight;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 directionWS : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.z = output.positionCS.w * UNITY_RAW_FAR_CLIP_VALUE;
                output.directionWS = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 ray = normalize(input.directionWS);
                float3 fallbackLight = normalize(float3(0.62, 0.52, 0.58));
                float active = saturate(_StarframeRangeSkyState.x);
                float3 keyDirection = normalize(lerp(fallbackLight, _StarframeRangeSkyLight.xyz, active));
                // A broad, quiet sky gradient gives the black space a little depth.
                float skyGradient = 0.88 + 0.12 * saturate(ray.y * 0.5 + 0.5);
                float3 sky = _Background.rgb * skyGradient;
                float3 q = ray / max(0.18, abs(ray.y));
                float2 uv = q.xz * 3;
                float2 lineDistance = abs(frac(uv - 0.5) - 0.5) / max(fwidth(uv), 0.0001);
                float lines = (1 - saturate(min(lineDistance.x, lineDistance.y))) * smoothstep(0.08, 0.3, abs(ray.y));
                sky += _GridColor.rgb * lines * _GridStrength * 0.15;

                // The distant sun and the rock key light share one stable world direction.
                float sunAlignment = dot(ray, keyDirection);
                float sunRadius = radians(_SunAngularRadius);
                float sunDisk = smoothstep(cos(sunRadius * 1.12), cos(sunRadius * 0.88), sunAlignment);
                float sunHalo = pow(saturate(sunAlignment), 1800) * _SunHaloStrength;
                sky += float3(1, 0.88, 0.68) * (sunDisk * _SunIntensity + sunHalo) * active * _ShowPlanet;
                if (_ShowPlanet < 0.5) return half4(sky, 1);

                // Solve in planet-radius units to preserve precision at astronomical distances.
                float3 center = (_PlanetCenterWS.xyz - _WorldSpaceCameraPos) / max(1, _PlanetWorldRadius);
                float b = -dot(center, ray);
                float c = dot(center, center) - 1;
                float discriminant = b * b - c;
                if (discriminant < 0) return half4(sky, 1);
                float hitDistance = -b - sqrt(max(0, discriminant));
                if (hitDistance <= 0) return half4(sky, 1);
                float3 normal = normalize(ray * hitDistance - center);
                float3 pole = normalize(_PlanetPoleDirection.xyz + float3(0, 0.00001, 0));
                float latitude = dot(normal, pole);
                // Continuous wide bands stay restrained and have no texture or quantization seams.
                float band = sin(latitude * 13 + 0.4) * 0.65 + sin(latitude * 25 - 0.8) * 0.35;
                float bandVariation = 1 + band * _BandStrength;
                float3 stone = _PlanetColor.rgb * bandVariation;
                float nDotLight = dot(normal, keyDirection);
                float daylight = smoothstep(-0.18, 0.12, nDotLight);
                float diffuse = pow(saturate(nDotLight), 0.82);
                float3 day = stone * (_PlanetAmbient + (1 - _PlanetAmbient) * diffuse);
                float3 night = _NightColor.rgb * bandVariation;
                float3 planet = lerp(night, day, daylight);
                float viewFacing = saturate(-dot(normal, ray));
                float limb = pow(1 - viewFacing, 9);
                float sunlitLimb = smoothstep(-0.2, 0.35, nDotLight);
                planet += _AtmosphereColor.rgb * limb * _AtmosphereStrength * sunlitLimb;
                return half4(planet, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
