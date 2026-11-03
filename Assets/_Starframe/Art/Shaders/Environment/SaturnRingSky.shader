Shader "Starframe/Environment/Saturn Ring Sky"
{
    Properties
    {
        _StarTex ("Starfield HDRI", 2D) = "black" {}
        _SaturnTex ("Saturn surface", 2D) = "gray" {}
        _StarExposure ("Star exposure", Range(0, 2)) = 0.45
        _PlanetDirection ("Planet center direction", Vector) = (-1, -0.05, 0.2, 0)
        _PlanetPoleDirection ("Axial tilt", Vector) = (0.18, 0.97, 0.24, 0)
        _PlanetRadius ("Angular radius (degrees)", Range(10, 85)) = 60
        _PlanetWorldRadius ("Planet world radius (m; 0 uses angular radius)", Float) = 0
        _PlanetCenterWS ("Planet world center (m)", Vector) = (-20300000, -400000, 800000, 0)
        _PlanetRotation ("Surface longitude rotation", Range(-180, 180)) = 0
        _PlanetBrightness ("Planet brightness", Range(0, 1.5)) = 0.38
        _PlanetTint ("Muted Saturn tint", Color) = (0.84, 0.87, 0.9, 1)
        _PlanetLightDirection ("Planet light direction", Vector) = (0.75, 0.35, -0.3, 0)
        _AtmosphereStrength ("Limb atmosphere", Range(0, 0.3)) = 0.09
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_StarTex);
            SAMPLER(sampler_StarTex);
            TEXTURE2D(_SaturnTex);
            SAMPLER(sampler_SaturnTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _StarTex_ST;
                float4 _SaturnTex_ST;
                float4 _PlanetDirection;
                float4 _PlanetPoleDirection;
                float4 _PlanetCenterWS;
                float4 _PlanetTint;
                float4 _PlanetLightDirection;
                float _StarExposure;
                float _PlanetRadius;
                float _PlanetWorldRadius;
                float _PlanetRotation;
                float _PlanetBrightness;
                float _AtmosphereStrength;
            CBUFFER_END
            float4 _StarframeRangeSkyState, _StarframeRangeSkyLight, _StarframeRangeSkyWarm, _StarframeRangeSkyCool, _StarframeRangeSkyFocus;

            static const float SATURN_PI = 3.14159265359;
            static const float SATURN_INV_PI = 0.31830988618;
            static const float SATURN_INV_TWO_PI = 0.15915494309;

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 directionWS : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionCS.z = output.positionCS.w * UNITY_RAW_FAR_CLIP_VALUE;
                // Skybox cube directions are already aligned with the camera's rotation-only view.
                output.directionWS = input.positionOS.xyz;
                return output;
            }

            float2 Equirectangular(float3 direction)
            {
                direction = normalize(direction);
                return float2(atan2(direction.x, direction.z) * SATURN_INV_TWO_PI + 0.5,
                              asin(clamp(direction.y, -1.0, 1.0)) * SATURN_INV_PI + 0.5);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 ray = normalize(input.directionWS);
                float2 starUv = Equirectangular(ray);
                starUv.x = frac(starUv.x + _StarTex_ST.z);
                starUv.y = saturate(starUv.y * _StarTex_ST.y + _StarTex_ST.w);
                float3 stars = SAMPLE_TEXTURE2D(_StarTex, sampler_StarTex, starUv).rgb * _StarExposure;
                float skyActive = saturate(_StarframeRangeSkyState.x);
                if (skyActive > 0.001)
                {
                float sunAlignment = dot(ray, _StarframeRangeSkyLight.xyz);
                float broadHalo = pow(saturate(sunAlignment * 0.5 + 0.5), 3.25);
                float nearHalo = pow(saturate(sunAlignment), 48.0);
                float coolScatter = pow(saturate(0.5 - sunAlignment * 0.5), 1.5);
                float beltScatter = pow(saturate(dot(ray, _StarframeRangeSkyFocus.xyz) * 0.5 + 0.5), 3.0) * _StarframeRangeSkyState.z;
                float3 backgroundGlow = (_StarframeRangeSkyWarm.rgb * (broadHalo + nearHalo * 0.85)
                    + _StarframeRangeSkyCool.rgb * (0.48 + coolScatter * 0.65 + beltScatter * 0.65)) * (_StarframeRangeSkyState.y * skyActive);
                stars += backgroundGlow;
                }

                // Solve in planet-radius units to avoid squaring million-metre world coordinates.
                // The world mode retains subtle parallax during flight without extra geometry or a camera.
                float3 center;
                if (_PlanetWorldRadius > 1.0)
                    center = (_PlanetCenterWS.xyz - _WorldSpaceCameraPos) / _PlanetWorldRadius;
                else
                {
                    float angularRadius = clamp(_PlanetRadius, 1.0, 85.0) * SATURN_PI / 180.0;
                    center = normalize(_PlanetDirection.xyz) / max(0.0174524, sin(angularRadius));
                }
                float3 axis = normalize(center);
                float b = -dot(center, ray);
                float c = dot(center, center) - 1.0;
                float discriminant = b * b - c;

                if (discriminant < 0.0)
                    return half4(stars, 1.0);

                float hitDistance = -b - sqrt(discriminant);
                if (hitDistance <= 0.0)
                    return half4(stars, 1.0);

                float3 surfaceNormal = normalize(ray * hitDistance - center);
                float3 pole = normalize(_PlanetPoleDirection.xyz);
                float3 north = axis - pole * dot(axis, pole);
                if (dot(north, north) < 0.0001)
                    north = cross(pole, float3(1, 0, 0));
                north = normalize(north);
                float3 east = normalize(cross(pole, north));

                float longitude = atan2(dot(surfaceNormal, east), dot(surfaceNormal, north)) * SATURN_INV_TWO_PI + 0.5;
                longitude = frac(longitude + _PlanetRotation / 360.0 + _SaturnTex_ST.z);
                float latitude = asin(clamp(dot(surfaceNormal, pole), -1.0, 1.0)) * SATURN_INV_PI + 0.5;
                latitude = saturate(latitude * _SaturnTex_ST.y + _SaturnTex_ST.w);
                float2 surfaceUv = float2(longitude, latitude);
                // Repair wrapped longitude derivatives so the texture seam does not select an unrelated coarse mip.
                float2 surfaceDx = ddx(surfaceUv), surfaceDy = ddy(surfaceUv);
                surfaceDx.x -= round(surfaceDx.x);
                surfaceDy.x -= round(surfaceDy.x);
                float3 sourceColor = SAMPLE_TEXTURE2D_GRAD(_SaturnTex, sampler_SaturnTex, surfaceUv, surfaceDx, surfaceDy).rgb;

                // Keep the real band detail while reducing the source map's stronger display colors.
                float luminance = dot(sourceColor, float3(0.24, 0.67, 0.09));
                float bandContrast = saturate((luminance - 0.48) * 1.35 + 0.5);
                float3 mutedSurface = lerp(sourceColor, bandContrast.xxx, 0.78) * _PlanetTint.rgb;
                float3 planetLight = normalize(lerp(_PlanetLightDirection.xyz, _StarframeRangeSkyLight.xyz, skyActive));
                float facing = saturate(dot(surfaceNormal, planetLight));
                float diffuse = 0.28 + 0.72 * facing;
                float3 planet = mutedSurface * (_PlanetBrightness * diffuse);

                // A narrow, restrained atmospheric edge keeps the silhouette legible without blooming.
                float viewFacing = saturate(-dot(surfaceNormal, ray));
                float limb = pow(1.0 - viewFacing, 12.0);
                float3 atmosphere = float3(0.68, 0.72, 0.75) * (limb * _AtmosphereStrength);
                return half4(planet + atmosphere, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
