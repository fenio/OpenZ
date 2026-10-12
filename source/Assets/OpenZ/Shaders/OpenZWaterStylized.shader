// OpenZ/WaterStylized v3: clean stylised water for URP 17.3 (Unity 6.3, Forward+), one pass, SRP Batcher compatible.
// Same shader name and GUID as v2, so M_Water / M_Water_Harbour keep their links.
//
//  - colour by VERTICAL water depth from the camera depth texture (camera-independent): clear shallows that show the bed,
//    a 3-stop shallow -> mid -> deep gradient, opacity rising with depth; the bed seen through the water keeps its
//    luminance (texture, caustics, unit shadows) and takes the water's hue with depth: clear turquoise, never murky
//    olive in the warm desert light, never black, never glowing
//  - refraction: the full-resolution URP opaque texture, shifted by the ripple slope in METRES of bed (constant at every
//    zoom), faded to zero at the waterline, never picking anything above the water
//  - ripples: one tileable normal map (T_WaterRipple_N) sampled as two layers (size, strength, 50 deg apart); optional world
//    flow map (T_WaterFlow_L02): the ripples drift ALONG each river branch (two-phase flow, no stretching); low amplitude
//  - caustics on the bed: analytic (five travelling waves, brightness = 1 / |det J| of the refracted-ray map), projected at
//    the bed position reconstructed from depth, lit by the sun and the bed's own shadow, faded in at the shore and out with
//    depth and zoom, off at night
//  - shore: a thin broken foam line where the water meets the bed, units and piers (vertical depth), an optional sandy-
//    shallows lift so the painted wet band under the water reads as wet sand, and a soft waterline
//  - light: the body is lit like an albedo (ambient SH + sun N.L x shadow + lamps), never emissive, so it can never be
//    brighter than sand in the same light; sky reflection = ambient probe along the reflected ray x Fresnel; a sparse toon
//    sun glint (fades out at high zoom); lamps / headlights / explosions light the water and streak on it (Forward+ loop)
//  - URP fog on the surface part only (the bed seen through the water is already fogged in the opaque texture)
//  - night (_OZNight, NightGrade): darker desaturated body, no caustics, dim foam, physical moon glint, lamp streaks
// Needs the URP asset's Depth Texture ON and Opaque Texture ON with Opaque Downsampling NONE (OpenZ > Water v3 > Apply).
// Fallback keywords: _WATER_NO_OPAQUE (plain alpha blending, no refraction / tint), _WATER_NO_DEPTH (fixed depth).
Shader "OpenZ/WaterStylized"
{
    Properties
    {
        [Header(Colour by vertical water depth)]
        _ShallowColor ("Shallow colour", Color) = (0.377, 1, 1, 1)
        _MidColor ("Mid colour", Color) = (0.318, 1, 0.974, 1)
        _DeepColor ("Deep colour", Color) = (0, 0.717, 0.7, 1)
        _MidDepth ("Mid colour at depth (m)", Range(0.02, 3)) = 0.3
        _DeepDepth ("Deep colour at depth (m)", Range(0.05, 6)) = 0.72
        _OpacityShallow ("Opacity at the waterline", Range(0, 1)) = 0.2
        _OpacityDeep ("Opacity of deep water", Range(0, 1)) = 0.577
        _OpacityDepth ("Opacity depth (m to 63 %)", Range(0.02, 6)) = 0.1
        _BedTint ("Bed colour under the water (its luminance is kept)", Color) = (0.561, 0.96, 1, 1)
        _BedTintDepth ("Bed recolour depth (m to full)", Range(0.02, 6)) = 0.32
        _SandLift ("Sandy shallows: lift a dark bed toward the sand colour", Range(0, 1)) = 0.3
        _SandColor ("Sandy shallows colour (albedo)", Color) = (0.9, 0.8, 0.6, 1)
        _SandDepth ("Sandy shallows depth (m)", Range(0.02, 1)) = 0.35
        _Refraction ("Refraction (m of bed shift at full ripple slope)", Range(0, 0.5)) = 0.1

        [Header(Ripples)]
        [NoScaleOffset][Normal] _RippleNormal ("Ripple normal map (tileable)", 2D) = "bump" {}
        _RippleScaleA ("Layer A size (m per tile)", Range(0.5, 40)) = 6
        _RippleScaleB ("Layer B size (m per tile)", Range(0.5, 40)) = 2.6
        _RippleStrengthA ("Layer A strength", Range(0, 2)) = 0.32
        _RippleStrengthB ("Layer B strength", Range(0, 2)) = 0.2
        _RippleSpeed ("Drift speed (m per s)", Range(0, 2)) = 0.12
        _RippleShade ("Ripple shading on the body", Range(0, 1)) = 0.8
        _RippleContrast ("Ripple contrast from any view (v2 mottling, L17)", Range(0, 1.5)) = 0
        _FlowDirection ("Flow direction without a flow map (deg, 0 = +X, 90 = +Z)", Range(0, 360)) = 90
        [Toggle(_WATER_FLOWMAP)] _UseFlowMap ("Use the flow map", Float) = 0
        [NoScaleOffset] _FlowMap ("Flow map (RG = direction x speed, 0.5 = still)", 2D) = "gray" {}
        _FlowRect ("Flow map rect (x0, z0, size x, size z in m; a negative size mirrors)", Vector) = (0, 0, 1, 1)
        _FlowCycle ("Flow cycle (s)", Range(1, 30)) = 8

        [Header(Light and sky)]
        _AmbientStrength ("Ambient strength", Range(0, 2)) = 1
        _ShadowStrength ("Sun shadow strength on the water", Range(0, 1)) = 0.75
        _SkyColor ("Sky reflection tint", Color) = (0.85, 0.94, 1, 1)
        _ReflectionStrength ("Sky reflection strength", Range(0, 1)) = 0.6
        _ReflectionF0 ("Reflection facing the camera", Range(0, 0.3)) = 0.06
        _FresnelPower ("Fresnel power", Range(1, 10)) = 5
        _GlintPower ("Sun glint size (Blinn power)", Range(8, 4096)) = 900
        _GlintIntensity ("Sun glint intensity", Range(0, 4)) = 1
        _GlintThreshold ("Sun glint toon threshold", Range(0, 1)) = 0.45
        _GlintSoftness ("Sun glint softness", Range(0.001, 0.5)) = 0.08
        _GlintViewBias ("Sun glint view bias (0 = physical)", Range(0, 1)) = 0.3
        _GlintSparsity ("Sun glint sparsity", Range(0, 1)) = 0.55
        _GlintFadeHeight ("Glints fade out above camera height (m)", Range(20, 400)) = 150
        [ToggleOff(_WATER_NO_ADDITIONAL_LIGHTS)] _AdditionalLights ("Lamps, headlights and fires light the water", Float) = 1
        _AddLightStrength ("Lamp light strength", Range(0, 4)) = 1

        [Header(Caustics on the bed)]
        _CausticsStrength ("Caustics strength", Range(0, 3)) = 0.8
        _CausticsScale ("Caustics size", Range(0.2, 5)) = 1
        _CausticsFocus ("Caustics focus (line sharpness)", Range(0.05, 0.5)) = 0.24
        _CausticsSpeed ("Caustics speed", Range(0, 3)) = 0.6
        _CausticsSplit ("Caustics colour split", Range(0, 0.3)) = 0.06
        _CausticsFadeIn ("Caustics fade-in depth (m)", Range(0.001, 1)) = 0.08
        _CausticsFadeOut ("Caustics fade-out depth (m)", Range(0.1, 10)) = 1.6

        [Header(Shore)]
        _FoamColor ("Foam colour (A = opacity)", Color) = (0.9, 0.95, 0.92, 0.45)
        _FoamWidth ("Foam width (m of depth)", Range(0, 1)) = 0.06
        _FoamScale ("Foam break-up size (m)", Range(0.1, 10)) = 0.9
        _FoamSpeed ("Foam drift (m per s)", Range(0, 1)) = 0.05
        _FoamBreakup ("Foam break-up", Range(0, 1)) = 0.75
        _FoamSoftness ("Foam softness", Range(0.001, 0.5)) = 0.08
        _EdgeFade ("Waterline softening (m of depth)", Range(0.001, 0.3)) = 0.02

        [Header(Night)]
        _NightDarken ("Night body darkening", Range(0, 1)) = 0.675
        _NightDesaturate ("Night body desaturation", Range(0, 1)) = 1

        [Header(Fallbacks)]
        [Toggle(_WATER_NO_OPAQUE)] _NoOpaque ("No opaque texture (plain blending, no refraction)", Float) = 0
        [Toggle(_WATER_NO_DEPTH)] _NoDepth ("No depth texture (fixed depth)", Float) = 0
        _FallbackDepth ("Fixed depth (m)", Range(0, 5)) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "WaterForward"
            Tags { "LightMode"="UniversalForward" }

            Blend One OneMinusSrcAlpha      // premultiplied: the bed is composited in the shader, alpha = waterline fade
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVert
            #pragma fragment WaterFrag

            #pragma shader_feature_local_fragment _WATER_FLOWMAP
            #pragma shader_feature_local_fragment _WATER_NO_OPAQUE
            #pragma shader_feature_local_fragment _WATER_NO_DEPTH
            #pragma shader_feature_local_fragment _WATER_NO_ADDITIONAL_LIGHTS

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            // transparent surface: main-light shadows come from the shadow map, never the opaque screen-space shadow texture
            #define _SURFACE_TYPE_TRANSPARENT 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "OpenZWet.hlsl"                                                // weather_l17_v1: rain drop ripples (_OzRain, 0 = none)

            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _MidColor;
                half4 _DeepColor;
                float _MidDepth;
                float _DeepDepth;
                half _OpacityShallow;
                half _OpacityDeep;
                float _OpacityDepth;
                half4 _BedTint;
                float _BedTintDepth;
                half _SandLift;
                half4 _SandColor;
                float _SandDepth;
                float _Refraction;
                float _RippleScaleA;
                float _RippleScaleB;
                half _RippleStrengthA;
                half _RippleStrengthB;
                float _RippleSpeed;
                half _RippleShade;
                half _RippleContrast;
                float _FlowDirection;
                half _UseFlowMap;
                float4 _FlowRect;
                float _FlowCycle;
                half _AmbientStrength;
                half _ShadowStrength;
                half4 _SkyColor;
                half _ReflectionStrength;
                half _ReflectionF0;
                half _FresnelPower;
                half _GlintPower;
                half _GlintIntensity;
                half _GlintThreshold;
                half _GlintSoftness;
                half _GlintViewBias;
                half _GlintSparsity;
                float _GlintFadeHeight;
                half _AdditionalLights;
                half _AddLightStrength;
                half _CausticsStrength;
                float _CausticsScale;
                half _CausticsFocus;
                float _CausticsSpeed;
                half _CausticsSplit;
                float _CausticsFadeIn;
                float _CausticsFadeOut;
                half4 _FoamColor;
                float _FoamWidth;
                float _FoamScale;
                float _FoamSpeed;
                half _FoamBreakup;
                half _FoamSoftness;
                float _EdgeFade;
                half _NightDarken;
                half _NightDesaturate;
                half _NoOpaque;
                half _NoDepth;
                float _FallbackDepth;
            CBUFFER_END

            TEXTURE2D(_RippleNormal); SAMPLER(sampler_RippleNormal);
            TEXTURE2D(_FlowMap);      SAMPLER(sampler_FlowMap);

            float _OZNight;   // night package (NightGrade): 0 by day .. 1 at night, global

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3  ambientUp  : TEXCOORD1;    // SH ambient for the flat, up-facing surface
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // layer B turns 50 deg against layer A (no visible repeat where the two tiles line up)
            static const float2x2 kRotB = float2x2(0.6427876, -0.7660444, 0.7660444, 0.6427876);
            static const float2x2 kRotC = float2x2(0.7986355, -0.6018150, 0.6018150, 0.7986355);   // NB46 anti-tiling copy of layer A (37 deg)
            // caustics: five travelling waves (wave vector in rad per m, amplitude = 1 / |k|, phase, angular speed)
            static const float2 kCausK[5] = { float2(2.8617, 0.8852), float2(-1.3055, 3.8360), float2(-4.6700, -2.3303),
                                              float2(1.8702, -6.3448), float2(-4.2261, 1.8930) };
            static const float kCausA[5] = { 0.3339, 0.2468, 0.1917, 0.1512, 0.2159 };
            static const float kCausP[5] = { 0.61, 2.93, 4.47, 1.19, 5.71 };
            static const float kCausW[5] = { 1.894, 2.203, 2.500, 2.816, 2.395 };

            float WaterHash21(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            float WaterValueNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(WaterHash21(i), WaterHash21(i + float2(1, 0)), u.x),
                            lerp(WaterHash21(i + float2(0, 1)), WaterHash21(i + float2(1, 1)), u.x), u.y);
            }

            // world position of the opaque scene behind a screen position (perspective and orthographic)
            float3 WaterScenePosition(float3 positionWS, float2 screenUV)
            {
                float rawDepth = SampleSceneDepth(screenUV);
                float surfaceEye = -TransformWorldToView(positionWS).z;
                if (IsPerspectiveProjection())
                {
                    float3 camPos = GetCameraPositionWS();
                    float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                    return camPos + (positionWS - camPos) * (sceneEye / max(surfaceEye, 1e-4));
                }
                float sceneEyeOrtho = LinearDepthToEyeDepth(rawDepth);
                return positionWS + GetViewForwardDir() * (sceneEyeOrtho - surfaceEye);
            }

            // flow velocity in m per s, world XZ
            float2 WaterFlowVelocity(float2 xz)
            {
            #if defined(_WATER_FLOWMAP)
                // a negative size mirrors the map on that axis (README step 4): keep the sign and turn the stored
                float2 sgn = step(0.0, _FlowRect.zw) * 2.0 - 1.0;
                float2 uv = (xz - _FlowRect.xy) / (sgn * max(abs(_FlowRect.zw), 1e-3));
                float2 f = (SAMPLE_TEXTURE2D_LOD(_FlowMap, sampler_FlowMap, uv, 0).rg * 2.0 - 1.0) * sgn;
                return f * _RippleSpeed;
            #else
                float a = radians(_FlowDirection);
                return float2(cos(a), sin(a)) * _RippleSpeed;
            #endif
            }

            // both ripple layers at a displacement -> world-space slope (x, z)
            half2 WaterRippleSlope(float2 xz, float2 disp)
            {
                float2 pA = (xz - disp) / _RippleScaleA;
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, pA), _RippleStrengthA);
                // turned 37 deg at 1.41x scale, blended in by a slow noise, and a slow strength variation, so no 8 m period reads
                float2 pA2 = mul(kRotC, xz - disp * 0.85) / (_RippleScaleA * 1.41) + float2(0.13, 0.59);
                half3 nA2 = UnpackNormalScale(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, pA2), _RippleStrengthA);
                half wA = (half)smoothstep(0.3, 0.7, WaterValueNoise(xz * 0.045 + 11.3));
                half gA = (half)(0.65 + 0.7 * WaterValueNoise(xz * 0.028 + 3.7));
                nA.xy = lerp(nA.xy, mul(nA2.xy, kRotC), wA) * gA;
                float2 pB = mul(kRotB, xz - disp * 0.7) / _RippleScaleB + float2(0.37, 0.71);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, pB), _RippleStrengthB);
                return nA.xy + mul(nB.xy, kRotB);      // layer B's tangent frame turned back to world
            }

            half2 WaterRipples(float2 xz, float t, float2 vel)
            {
            #if defined(_WATER_FLOWMAP)
                // two-phase flow (the ripples follow each branch without stretching): two copies half a cycle apart,
                // each cross-fading out while it resets; a slow noise offsets the phase so the reset never pulses in sync
                float cyc = max(_FlowCycle, 0.5);
                float ph = t / cyc + WaterValueNoise(xz * 0.21) * 0.5;
                float p0 = frac(ph), p1 = frac(ph + 0.5);
                half w1 = (half)abs(1.0 - 2.0 * p0);
                half2 s0 = WaterRippleSlope(xz, vel * ((p0 - 0.5) * cyc));
                half2 s1 = WaterRippleSlope(xz + float2(2.3, 1.7), vel * ((p1 - 0.5) * cyc));
                return lerp(s0, s1, w1);
            #else
                return WaterRippleSlope(xz, vel * t);
            #endif
            }

            // analytic caustics: bed brightness = 1 / |det(I - D * Hessian(h))| of the refracted-ray map, per colour channel
            half3 WaterCaustics(float2 p, float t, half focus, half split, half aa)
            {
                float hxx = 0.0, hzz = 0.0, hxz = 0.0;
                [unroll] for (int w = 0; w < 5; w++)
                {
                    float2 k = kCausK[w];
                    float s = sin(dot(k, p) - kCausW[w] * t + kCausP[w]) * kCausA[w];
                    hxx -= k.x * k.x * s;
                    hzz -= k.y * k.y * s;
                    hxz -= k.x * k.y * s;
                }
                float3 D = focus * float3(1.0 + split, 1.0, 1.0 - split);
                float3 J = (1.0 - D * hxx) * (1.0 - D * hzz) - D * D * (hxz * hxz);
                float3 I = 1.0 / max(abs(J), 0.12 + aa);
                return (half3)saturate((I - 1.0) * 0.22);
            }

            Varyings WaterVert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionWS = positionWS;
                o.positionCS = TransformWorldToHClip(positionWS);
                o.ambientUp = SampleSH(half3(0.0, 1.0, 0.0));
                return o;
            }

            half4 WaterFrag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 positionWS = i.positionWS;
                float2 xz = positionWS.xz;
                float t = _Time.y;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                const half3 up = half3(0.0, 1.0, 0.0);
                half night = saturate((half)_OZNight);
                float mPerPx = max(length(fwidth(xz)), 1e-5);                         // metres per pixel on the surface

                // ---- the scene under this pixel: vertical depth drives colour, opacity, foam and caustics
                float d;
                float3 sceneWS;
            #if defined(_WATER_NO_DEPTH)
                d = _FallbackDepth;
                sceneWS = positionWS - float3(0.0, _FallbackDepth, 0.0);
            #else
                sceneWS = WaterScenePosition(positionWS, screenUV);
                d = max(positionWS.y - sceneWS.y, 0.0);
            #endif
                half edge = (half)saturate(d / max(_EdgeFade, 1e-4));

                // ---- ripples -> normal
                float2 vel = WaterFlowVelocity(xz);
                half2 slope = WaterRipples(xz, t, vel);
                if (_OzRain > 0.001) slope += OZWRipples(xz, t, (half)_OzRain, mPerPx) * (half)1.6;   // weather_l17_v1: rain drop rings
                half3 n = normalize(half3(slope.x, 1.0, slope.y));
                half3 V = GetWorldSpaceNormalizeViewDir(positionWS);

                // ---- main light (the moon at night) on the surface
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half3 sunColor = mainLight.color * mainLight.distanceAttenuation;
                half3 L = mainLight.direction;
                half sunShadow = lerp(1.0 - _ShadowStrength, 1.0, mainLight.shadowAttenuation);
                half3 ambient = i.ambientUp * _AmbientStrength;
                half3 bodyN = normalize(lerp(up, n, _RippleShade));
                half3 diffuse = ambient + sunColor * (saturate(dot(bodyN, L)) * sunShadow);

                // ---- lamps, headlights, muzzle flashes, explosions (Forward+ sets only _CLUSTER_LIGHT_LOOP)
                half3 addSpec = half3(0.0, 0.0, 0.0);
            #if (defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)) && !defined(_WATER_NO_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;          // LIGHT_LOOP_BEGIN reads inputData in the cluster loop
                    inputData.positionWS = positionWS;
                    inputData.normalWS = n;
                    inputData.viewDirectionWS = V;
                    inputData.normalizedScreenSpaceUV = screenUV;
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, positionWS);
                        half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation * _AddLightStrength);
                        // crumpled bright paper in every lamp pool: the body takes the lamp on a nearly flat normal (water mostly reflects,
                        // so less of it) and the streak lobe is tighter and weaker: clean glints on the ripples instead of blotches
                        half3 lampN = normalize(lerp(up, n, (half)0.12));
                        diffuse += radiance * (saturate(dot(lampN, light.direction)) * (half)0.55);
                        half3 hAdd = SafeNormalize(float3(light.direction) + float3(V));
                        half3 nStreak = normalize(half3(n.x * 2.2, 1.0, n.z * 2.2));   // lamp reflections break into streaks
                        addSpec += radiance * (pow(saturate(dot(n, hAdd)), _GlintPower * 0.25)
                                   + (0.12 + 0.22 * night) * pow(saturate(dot(nStreak, hAdd)), 140.0)) * _GlintIntensity;
                    LIGHT_LOOP_END
                }
            #endif

                // ---- water body: 3-stop colour and opacity by depth, lit like an albedo (never emissive)
                half tMid = (half)smoothstep(0.0, max(_MidDepth, 1e-3), d);
                half tDeep = (half)smoothstep(_MidDepth, max(_DeepDepth, _MidDepth + 1e-3), d);
                half3 bodyAlb = lerp(lerp(_ShallowColor.rgb, _MidColor.rgb, tMid), _DeepColor.rgb, tDeep);
                half bodyLum = dot(bodyAlb, half3(0.2126, 0.7152, 0.0722));
                bodyAlb = lerp(bodyAlb, bodyLum.xxx, _NightDesaturate * night) * (1.0 - _NightDarken * night);
                half A = lerp(_OpacityShallow, _OpacityDeep, (half)(1.0 - exp(-d / max(_OpacityDepth, 1e-3))));
                // optional view-independent ripple contrast (the v2 mottling the approved L17 harbour has; 0 on L02)
                half2 shadeDir = SafeNormalize(float3(L.x + 0.3, 0.0, L.z + 0.5)).xz;
                half rippleK = max(1.0 + dot(n.xz, shadeDir) * (_RippleContrast * 2.0), 0.0);
                half3 body = bodyAlb * diffuse * rippleK;

                // ---- sky reflection (ambient probe along the reflected ray) x Fresnel
                half NdotV = saturate(dot(n, V));
                half fres = (_ReflectionF0 + (1.0 - _ReflectionF0) * pow(1.0 - NdotV, _FresnelPower)) * _ReflectionStrength;
                half3 sky = SampleSH(reflect(-V, n)) * _SkyColor.rgb * _AmbientStrength;

                // ---- shore foam: a thin broken line where the water meets the bed, units and piers
                float2 fp = (xz - vel * (t * (_FoamSpeed / max(_RippleSpeed, 1e-3)))) / max(_FoamScale, 0.05);
                half fn = (half)(WaterValueNoise(fp) * 0.65 + WaterValueNoise(fp * 2.3 + 5.1) * 0.35);
                half band = (half)(1.0 - saturate(d / max(_FoamWidth, 1e-4)));
                half cut = lerp((half)0.5, (half)(0.3 + 0.85 * fn), _FoamBreakup);
                half fAA = (half)fwidth(band - cut) + _FoamSoftness;
                half foam = smoothstep(cut - fAA, cut + fAA, band) * _FoamColor.a * (1.0 - 0.5 * night) * (half)(_FoamWidth > 0.0);
                half3 foamLit = _FoamColor.rgb * diffuse;

                // ---- sparse toon sun glint (view-biased by day so the fixed RTS camera sees some; physical at night)
                half3 mirrorDir = reflect(-V, up);
                half3 glintL = SafeNormalize(lerp(float3(L), float3(mirrorDir), _GlintViewBias * (1.0 - night)));
                half3 H = SafeNormalize(float3(glintL) + float3(V));
                half specRaw = pow(saturate(dot(n, H)), _GlintPower);
                half specAA = (half)fwidth(specRaw);
                half glintThr = lerp(_GlintThreshold, max(_GlintThreshold, (half)0.85), night);
                half glint = smoothstep(glintThr - _GlintSoftness - specAA, glintThr + _GlintSoftness + specAA, specRaw);
                half patches = (half)WaterValueNoise(xz * 0.13 + vel * (t * 0.35));
                glint *= smoothstep(_GlintSparsity, _GlintSparsity + 0.2, patches);
                float camH = _WorldSpaceCameraPos.y - positionWS.y;
                glint *= (half)(1.0 - smoothstep(_GlintFadeHeight * 0.6, _GlintFadeHeight, camH));
                glint *= saturate(L.y * 4.0) * mainLight.shadowAttenuation;
                half3 S = (sunColor * (glint * _GlintIntensity) + addSpec) * (1.0 - foam);

                // ---- surface = what the water itself sends to the camera (premultiplied), trans = how much bed shows
                half cover = 1.0 - (1.0 - A) * (1.0 - fres);
                half3 surf = body * (A * (1.0 - fres)) + sky * fres;
                surf = surf * (1.0 - foam) + foamLit * foam;
                cover = cover * (1.0 - foam) + foam;
                half trans = 1.0 - cover;

                // ---- fog on the surface part only: MixFog(1) - MixFog(0) = how much of the surface survives
                half fogFactor = InitializeInputDataFog(float4(positionWS, 1.0), 0.0);
                half fogVis = MixFog(half3(1.0, 1.0, 1.0), fogFactor).r - MixFog(half3(0.0, 0.0, 0.0), fogFactor).r;
                surf = surf * fogVis + unity_FogColor.rgb * ((1.0 - fogVis) * cover);
                S *= fogVis;

            #if defined(_WATER_NO_OPAQUE)
                return half4((surf + S) * edge, cover * edge);
            #else
                // ---- the bed: refracted (metres of shift -> screen UV at this depth), never picking anything above the water
                float surfaceEye = max(-TransformWorldToView(positionWS).z, 1e-3);
                float2 nView = TransformWorldToViewDir(float3(n.x, 0.0, n.z), false).xy;
                float refrM = _Refraction * saturate(d / 0.3);
                float2 uvR = screenUV + nView * refrM * float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11) * (0.5 / surfaceEye);
                float3 bedWS = sceneWS;
            #if !defined(_WATER_NO_DEPTH)
                float3 behind = WaterScenePosition(positionWS, uvR);
                if (behind.y > positionWS.y - 0.01) uvR = screenUV; else bedWS = behind;
            #endif
                half3 bed = SampleSceneColor(uvR);
                float dBed = max(positionWS.y - bedWS.y, 0.0);

                // the bed's own sun light (flat-bed approximation) and shadow: caustics and sandy shallows follow it
                half bedShadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(bedWS));
                half3 bedSun = sunColor * (saturate(L.y) * bedShadow);

                // sandy shallows: only lifts a bed darker than wet sand in the same light (the painted wet band), zero at
                // the waterline (no seam against the dry bank) and past _SandDepth
                half liftK = _SandLift * (half)(smoothstep(0.0, 0.06, dBed) * (1.0 - smoothstep(_SandDepth * 0.5, _SandDepth, dBed)));
                half3 sandLit = _SandColor.rgb * (ambient + bedSun);
                bed = lerp(bed, max(bed, sandLit), liftK);

                // caustics: sun only, faded in at the shore and out with depth, calmed at high zoom, off at night
                half caFade = (half)(saturate(dBed / max(_CausticsFadeIn, 1e-3)) * (1.0 - saturate(dBed / max(_CausticsFadeOut, 1e-3))));
                caFade *= (half)(1.0 - smoothstep(0.06, 0.16, mPerPx)) * (1.0 - night) * saturate(L.y * 3.0) * bedShadow;
                if (caFade > 0.001)
                {
                    float2 cp = (bedWS.xz - vel * (t * 0.5)) / max(_CausticsScale, 0.05);
                    half aa = (half)saturate(mPerPx / max(_CausticsScale, 0.05) * 3.0);
                    half3 ca = WaterCaustics(cp, t * _CausticsSpeed, _CausticsFocus, _CausticsSplit, aa);
                    half sunLum = max(dot(sunColor, half3(0.2126, 0.7152, 0.0722)), 1e-3);
                    bed += bed * ca * (_CausticsStrength * caFade) * saturate(sunColor / sunLum);
                }

                // the bed seen through the water takes the water's hue: its luminance (texture, caustics, unit shadows) is
                // kept and recoloured by _BedTint with depth. A plain multiply would turn the warm tan bed murky olive in
                // the game's warm light; this stays clear and turquoise, and can never go black or glow (luminance kept,
                // scaled by the tint's own brightness)
                half bedLum = dot(bed, half3(0.2126, 0.7152, 0.0722));
                half3 bedWater = bedLum * _BedTint.rgb;
                half3 bedSeen = lerp(bed, bedWater, (half)saturate(dBed / max(_BedTintDepth, 1e-3)));
                half3 outC = surf + S + bedSeen * trans;
                return half4(outC * edge, edge);
            #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
