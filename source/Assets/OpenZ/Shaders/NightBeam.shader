// OpenZ/NightBeam: soft additive "volumetric-looking" light cone for searchlight beams and street-lamp fog glow cones
// (URP 17 forward, one pass, no light shafts, no raymarch). The mesh is NightMeshes.Cone: apex at the origin, axis +Z,
// length 1, end radius 1, uv.y = 0 apex -> 1 far end; NightLights scales it to the light's length and angle.
// Look: bright core where the cone surface faces the camera (both faces drawn, so the middle of the beam is denser),
// soft silhouettes, fade along the length and at the apex, drifting dust motes in world space, soft intersection with
// the ground / units / walls (camera depth texture), faded near the camera. HDR colour feeds the night bloom.
// Per renderer (MaterialPropertyBlock): _Color (HDR, already x intensity x switch level) and _Fade.
// Global: _OZBeamHazeBoost (NightGrade, level haze - 1: the city's mystery fog shows more beam than the desert).
Shader "OpenZ/NightBeam"
{
    Properties
    {
        [HDR] _Color ("Colour (HDR)", Color) = (0.6, 0.66, 0.75, 1)
        _Fade ("Fade", Range(0, 1)) = 1
        _EdgePower ("Edge softness (N.V power)", Range(0.3, 6)) = 1.6
        _LengthPower ("Length falloff power", Range(0.2, 6)) = 1.4
        _ApexFade ("Apex fade (fraction of length)", Range(0, 0.5)) = 0.06
        _NoiseScale ("Dust noise scale (1/m)", Range(0.02, 4)) = 0.55
        _NoiseSpeed ("Dust drift speed (m/s)", Range(0, 4)) = 0.6
        _NoiseAmount ("Dust amount", Range(0, 1)) = 0.45
        _DepthFade ("Soft intersection distance (m)", Range(0.05, 6)) = 1.2
        _CameraFade ("Near camera fade distance (m)", Range(0.1, 40)) = 6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "NightBeam"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BeamVert
            #pragma fragment BeamFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fade;
                half _EdgePower;
                half _LengthPower;
                half _ApexFade;
                float _NoiseScale;
                float _NoiseSpeed;
                half _NoiseAmount;
                float _DepthFade;
                float _CameraFade;
            CBUFFER_END
            float _OZBeamHazeBoost;     // global, 0 = neutral

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float along : TEXCOORD2;
            };

            float BeamHash(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1); p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }
            float BeamNoise(float3 x)
            {
                float3 i = floor(x); float3 f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(BeamHash(i), BeamHash(i + float3(1, 0, 0)), f.x), lerp(BeamHash(i + float3(0, 1, 0)), BeamHash(i + float3(1, 1, 0)), f.x), f.y),
                            lerp(lerp(BeamHash(i + float3(0, 0, 1)), BeamHash(i + float3(1, 0, 1)), f.x), lerp(BeamHash(i + float3(0, 1, 1)), BeamHash(i + float3(1, 1, 1)), f.x), f.y), f.z);
            }

            Varyings BeamVert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.along = v.uv.y;
                return o;
            }

            half4 BeamFrag(Varyings i) : SV_Target
            {
                float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                half ndv = abs(dot(normalize(i.normalWS), V));
                half core = pow(ndv, _EdgePower);                                   // soft silhouettes, dense middle
                half len = pow(saturate(1.0 - i.along), _LengthPower);              // fades towards the far end
                half apex = smoothstep(0.0, max(_ApexFade, 1e-3), i.along);         // the lens glow covers the tip
                // soft intersection with whatever the beam hits (ground, units, walls)
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float fragEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                half soft = saturate((sceneEye - fragEye) / max(_DepthFade, 1e-3));
                half camFade = saturate((fragEye - _ProjectionParams.y) / max(_CameraFade, 1e-3));
                // dust motes drifting through the light
                float t = _Time.y * _NoiseSpeed;
                float n = BeamNoise(i.positionWS * _NoiseScale + float3(t * 0.35, t, t * 0.6)) * 0.65
                        + BeamNoise(i.positionWS * (_NoiseScale * 2.7) - float3(t * 0.8, t * 0.4, 0)) * 0.35;
                half dust = lerp(1.0, saturate(n * 1.9 - 0.15) * 1.5, _NoiseAmount);
                half haze = max(0.0, 1.0 + _OZBeamHazeBoost);
                half3 c = _Color.rgb * (core * len * apex * soft * camFade * dust * haze * _Fade);
                return half4(c, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
