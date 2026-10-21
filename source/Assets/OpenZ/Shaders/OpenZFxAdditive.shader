// (every material property in UnityPerMaterial), no textures: every shape is procedural, so no authored sprite sheet is
// needed and no shader_feature variant can be stripped from a build (shapes are a uniform branch on _Shape).
//
// Why a custom shader (checked against the URP source, see README_IMPORT.md):
//  - URP Particles/Unlit takes its shape only from _BaseMap (ParticlesUnlitForwardPass.hlsl:57): without authored textures
//    every particle is a hard square.
//  - Soft particles in URP are a shader_feature (_SOFTPARTICLES_ON, ParticlesUnlit.shader:103): set from C# at run time
//    only, the variant is stripped from player builds.
//  - the ring is a 32-sided annulus painted on the terrain, its ground light a Gaussian, its scorch a multiplicative
//    darkening: none exist in URP's particle shaders.
//
// One blend state for everything: premultiplied alpha, Blend One OneMinusSrcAlpha.
//   rgb = colour * coverage, alpha = coverage * _Opaqueness
//   _Opaqueness 1 -> normal "over" (smoke, dust, fireball bodies, the M12 ring, water ripples)
//   rgb 0 + alpha a -> multiplicative darkening dst * (1 - a) (scorch: diffuse *= 1 - burn * .58)
//
// _Shape: 0 Glow (LEG point sprite core + halo)   1 Streak (M12 spark "squares", droplets, tracers)
//         2 Puff (lit smoke / dust sphere impostor) 3 Fireball (M12 fireball / core sphere, hot heart, cooler rim)
//         4 Band (ground-conforming shock ring, world-space centre)  5 BandUV (flat ring on a particle quad: water ripple)
//         6 Gauss (M12 blast ground light)          7 Scorch (vertex-alpha darkening: scorch marks, tread marks)
//         8 FlameMesh (rocket exhaust cone, N.V hot core)            9 Tracer (shell rear tracer along an object axis)
//
// streams UV, AgePercent, StableRandomX). Shape 2 with _NoiseAmt > 0: billowy noise-eroded silhouette, bumped impostor normal,
// self-shadowed sun + sky + point lights, heat glow at birth, darkening over life. Shape 3 with _FireRamp 1: heat ramp
// (soot < rim < body < hot < core, linear values tuned through URP ACES + .2 EV) so only the small hot centre blooms.
// Shape 4 with _BandProfile 1: thin shock front, bright leading edge. Defaults keep the old look (NukeFx etc. unchanged).
//
// with _FireOpacity.x >= 0 makes the over / additive mix follow the heat: opacity = lerp(x, y, heat), so the cool sooty shell
// occludes (saturated orange-red over the sand) while the incandescent heart is ADDITIVE (x .92 -> y .10 for the fireballs,
// .35 -> 0 for the core flash). The ramp values (C#) sit 2-5x above URP's real linear bloom threshold. x < 0 = old behaviour.
// _AtlasOn 1 draws every puff from a 4096 lit smoke flipbook (8 variants x 8 time steps of 512 px: normal xy, front height,
// frame over life, eroded at the edges by a tileable 1024 billow detail texture (2 scales, about 4-5k detail texels across a
// puff) so big puffs keep crisp, detailed edges; erosion rises at end of life (puffs thin into wisps, no pop); at night the
// unlit share goes to zero (_OZNight) so smoke is lit by the moon / lamps / fires and never glows in the dark.
// Spark streaks (MOTION BLUR option): a spark channel is drawn as a stretched billboard along the particle's velocity with
// _StreakBlur > 0 (shapes 0 and 1): the front half of the sprite stays as it is (a round head), its rear half is drawn out
// over the tail and fades to nothing at the tail end (FxStreakAlong). _StreakBlur 0 = the plain sprite, as before.
// Puffs blurred along their path (MOTION BLUR option, shape 2 with _SmearPath > 0): the quad is widened along the particle's
// velocity so it covers the way the puff travels during the exposure (at most 0.3 of its size), and the puff's coverage is the mean of 6 reads of the
// puff along that way (FxPuffTap), so its leading and trailing edges go soft while nothing behind it is touched. A puff that
// hardly moves is drawn exactly as without it. _SmearPath 0 = the stretched quad, as before.
Shader "OpenZ/FxAdditive"
{
    Properties
    {
        [HDR] _Color ("Tint (linear, gain applied from C#)", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity (HDR gain)", Float) = 1
        [HDR] _CoreColor ("White-hot core colour", Color) = (1, 0.94, 0.72, 1)
        _CoreMix ("Core mix (LEG .8)", Range(0, 1)) = 0.8
        _Shape ("Shape (0 glow, 1 streak, 2 puff, 3 fireball, 4 band, 5 band uv, 6 gauss, 7 scorch, 8 flame mesh, 9 tracer, 10 water swell, 11 water foam)", Float) = 0
        _Opaqueness ("Alpha-over amount (0 additive, 1 premultiplied over)", Range(0, 1)) = 0
        _UseVertexColor ("Use vertex colour (particles 1, glb meshes 0)", Range(0, 1)) = 1
        _SoftFade ("Soft particle fade distance (m, 0 = off)", Float) = 0
        _Lit ("Lit amount (sun + ambient + point lights)", Range(0, 1)) = 0
        _Wrap ("Light wrap (smoke glows from inside)", Range(0, 1)) = 0.5
        [HDR] _EmissionColor ("Emission (lit shapes, linear)", Color) = (0, 0, 0, 1)
        _RimGain ("Fireball rim gain (fraction of the heart)", Range(0, 1)) = 0.35
        _RimPower ("Fireball rim power", Float) = 1.2
        _Squareness ("Streak squareness (0 ellipse, 1 box)", Range(0, 1)) = 0.5
        _StreakTaper ("Streak comet taper: 0 = symmetric box, >0 = round HDR head at the front + tapered tail (head gain 1 + x)", Range(0, 2)) = 0
        _StreakBlur ("Motion smoothing, spark on a stretched billboard: 0 = off, > 0 = the rear half is drawn out as a tail that fades to nothing (the power of the fade)", Float) = 0
        _EdgeSoft ("Puff silhouette softness", Range(0.001, 1)) = 0.25
        _BandInner ("Band inner radius fraction", Range(0, 1)) = 0.88
        _BandSides ("Band polygon sides (0 = round)", Float) = 32
        _RingCenter ("Ring / flash centre (world xyz)", Vector) = (0, 0, 0, 0)
        _RingRadius ("Ring outer / flash radius (m)", Float) = 1
        _RingAlpha ("Ring / flash / decal opacity", Float) = 1
        _AxisOS ("Tracer axis (object space, unit)", Vector) = (0, 0, 1, 0)
        _AxisRange ("Tracer: full at x, zero at y (along the axis)", Vector) = (0, 1, 0, 0)
        // fx_look (C# sets every value with SetVector, i.e. linear, so the defaults below only matter for other materials)
        _FireRamp ("Fireball: 0 tint x intensity, 1 heat ramp", Range(0, 1)) = 0
        _FireCore ("Fire ramp: core (linear HDR)", Vector) = (3.2, 1.6, 0.32, 1)
        _FireHot ("Fire ramp: hot (linear HDR)", Vector) = (2.2, 0.86, 0.1, 1)
        _FireMid ("Fire ramp: body (linear HDR)", Vector) = (1.45, 0.42, 0.03, 1)
        _FireRim ("Fire ramp: rim (linear)", Vector) = (0.55, 0.075, 0.012, 1)
        _FireSoot ("Fire ramp: soot (linear)", Vector) = (0.09, 0.075, 0.06, 1)
        _Cooling ("Fire: heat lost per life", Float) = 1
        _HeatBias ("Fire: heat added (fades over life)", Float) = 0
        _NoiseAmt ("Billow erosion (0 = smooth sphere, old look)", Range(0, 1)) = 0
        _NoiseScale ("Billow noise cells per radius", Float) = 2.2
        _Boil ("Billow boil (noise units per life)", Float) = 0.8
        _Spin ("Billow spin (rad per life)", Float) = 0.4
        _Bump ("Billow normal bump", Float) = 0
        _SelfShadow ("Smoke self shadow (0 = old wrap lighting)", Range(0, 1)) = 0
        _AgeColor ("Smoke albedo multiplier at end of life (linear)", Vector) = (1, 1, 1, 1)
        _HeatColor ("Smoke heat glow at birth (linear HDR)", Vector) = (0, 0, 0, 1)
        _HeatTime ("Smoke heat glow duration (fraction of life)", Float) = 0.25
        _BandProfile ("Band: 0 flat, 1 shock front (bright outer edge)", Range(0, 1)) = 0
        _FireOpacity ("fx_v2 fire: opacity at heat 0 (x) / heat 1 (y); x < 0 = use _Opaqueness", Vector) = (-1, 0, 0, 0)
        _SmokeAtlas ("Smoke flipbook 8x8 (normal xy, height, density; linear)", 2D) = "black" {}
        _SmokeDetail ("Smoke detail erosion (tileable billow noise; linear)", 2D) = "grey" {}
        _AtlasOn ("Puff: 0 procedural (old), 1 flipbook + detail", Range(0, 1)) = 0
        _DetailAmt ("Puff detail erosion", Range(0, 1)) = 0.35
        _DetailScale ("Puff detail repeats across a puff", Float) = 3
        _Dissolve ("Puff extra erosion at end of life", Range(0, 1)) = 0.35
        _FlipRate ("Puff flipbook frames per life (1 = all 8)", Float) = 1
        _Smear ("Motion smoothing: stretch along the particle velocity (x the global shutter, 0 = none; particle channels only)", Float) = 0
        _SmearPath ("Motion smoothing, puffs: 0 = the quad is stretched along the velocity, 1 = the puff is blurred along its own path, 2 = the same without the fade-in at birth (puffs placed by code)", Float) = 0
        _GroundBand ("Ground decal: scene surfaces up to this far in front still receive it (m of eye depth, 0 = depth buffer test)", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test (Always with the ground band)", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }

        Pass
        {
            Name "FxForward"
            Tags { "LightMode"="UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]         // LEqual; Always for ground decals with _GroundBand > 0 (manual scene depth test below)
            Cull Off
            Offset -1, -1          // ground decals sit 4-7 cm over the terrain; this keeps them clear of it at RTS distance

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FxVert
            #pragma fragment FxFrag

            // point / spot lights (explosion and muzzle lights light the smoke from inside), Forward and Forward+
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Fog.hlsl"

            #define _SURFACE_TYPE_TRANSPARENT 1

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Intensity;
                half4 _CoreColor;
                half _CoreMix;
                float _Shape;
                half _Opaqueness;
                half _UseVertexColor;
                float _SoftFade;
                half _Lit;
                half _Wrap;
                half4 _EmissionColor;
                half _RimGain;
                half _RimPower;
                half _Squareness;
                half _StreakTaper;
                float _StreakBlur;
                half _EdgeSoft;
                half _BandInner;
                float _BandSides;
                float4 _RingCenter;
                float _RingRadius;
                float _RingAlpha;
                float4 _AxisOS;
                float4 _AxisRange;
                half _FireRamp;
                half4 _FireCore;
                half4 _FireHot;
                half4 _FireMid;
                half4 _FireRim;
                half4 _FireSoot;
                float _Cooling;
                float _HeatBias;
                float _NoiseAmt;
                float _NoiseScale;
                float _Boil;
                float _Spin;
                float _Bump;
                half _SelfShadow;
                half4 _AgeColor;
                half4 _HeatColor;
                float _HeatTime;
                half _BandProfile;
                float4 _FireOpacity;
                half _AtlasOn;
                half _DetailAmt;
                float _DetailScale;
                half _Dissolve;
                float _FlipRate;
                float _Smear;
                float _SmearPath;
                float _GroundBand;
                float _ZTest;
            CBUFFER_END
            TEXTURE2D(_SmokeAtlas); SAMPLER(sampler_SmokeAtlas);
            TEXTURE2D(_SmokeDetail); SAMPLER(sampler_SmokeDetail);
            float4 _OZFxSmearFast;                            // fast particles: x speed (m/s) where a longer stretch starts, y speed of the full one, z the longest stretch then (share of the size)
            float4 _OZFxSmear;                                // motion smoothing global (MOTION BLUR option): x shutter s (0 = off), y longest stretch (share of the size), z smoke fade-in (share of life)
            float _OZNight;                                   // NightGrade global: 0 by day .. 1 at night

            struct FxAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4  color      : COLOR;
                float4 uv         : TEXCOORD0;      // xy uv, z particle age 0-1, w stable random (fx_look vertex streams)
                float4 pvel       : TEXCOORD1;      // particle channels: xyz velocity (m/s, world), w size (m); zero on plain meshes
                float4 pctr       : TEXCOORD2;      // particle channels: xyz particle centre (world)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct FxVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4  color      : COLOR;
                float4 uv         : TEXCOORD2;
                float  axisT      : TEXCOORD3;
                float4 smearA     : TEXCOORD4;      // puff blurred along its path: xyz = this corner where the puff stands (world), w = 1 while on
                float3 smearV     : TEXCOORD5;      // the way the puff travels during the exposure (world, m)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            FxVaryings FxVert(FxAttributes v)
            {
                FxVaryings o = (FxVaryings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.color = lerp(half4(1.0, 1.0, 1.0, 1.0), v.color, _UseVertexColor);
                if (_Smear > 0.0 && _OZFxSmear.x > 0.0)
                {   // MOTION BLUR option (particle channels with the velocity / size / centre streams): the view-facing quad is drawn out
                    // along the particle's own velocity (shutter x speed, at most _OZFxSmear.y of its size, never toward / away from the
                    // view) and thinned by the same share; smoke (shape 2) fades in at birth instead of popping
                    float size = max(v.pvel.w, 1e-4);
                    float3 vf = -UNITY_MATRIX_V[2].xyz;
                    float3 dv = v.pvel.xyz * (_OZFxSmear.x * _Smear);
                    dv -= vf * dot(dv, vf);
                    float dl = length(dv);
                    if (dl > 1e-4)
                    {
                        // the fast first moments of a big blast may be drawn out further; slow smoke keeps the plain cap
                        float fast = saturate((length(v.pvel.xyz) - _OZFxSmearFast.x) / max(_OZFxSmearFast.y - _OZFxSmearFast.x, 1e-3));
                        bool onPath = _SmearPath > 0.5 && _Shape > 1.5 && _Shape < 2.5;     // a puff blurred along its path (FxFrag): the quad only has to cover that path
                        // such a puff's path is at most 0.3 of its size: up to there the 6 reads of FxFrag melt into one smooth ramp, a longer path shows them as steps
                        float k = min(1.0, (onPath ? min(_OZFxSmear.y, 0.3) : lerp(_OZFxSmear.y, max(_OZFxSmear.y, _OZFxSmearFast.z), fast)) * size / dl);
                        dv *= k; dl *= k;
                        if (onPath) { o.smearA = float4(o.positionWS, 1.0); o.smearV = dv; }
                        float wl = onPath ? 1.5 : 1.0;
                        float w = clamp(dot(o.positionWS - v.pctr.xyz, dv) / (dl * size * 0.5), -wl, wl);
                        o.positionWS += dv * (0.5 * w);
                        o.positionCS = TransformWorldToHClip(o.positionWS);
                        if (!onPath) o.color.a *= (half)(size / (size + dl));
                    }
                    if (_Shape > 1.5 && _Shape < 2.5 && _SmearPath < 1.5) o.color.a *= (half)saturate(v.uv.z / max(_OZFxSmear.z, 1e-3));
                }
                o.uv = v.uv;
                o.axisT = dot(v.positionOS.xyz, _AxisOS.xyz);
                return o;
            }

            // sun (with wrap) + SH ambient + point / spot lights. wrap > 0 lets an explosion light glow through the smoke.
            // fx_look selfShadow: the sun term gets a soft terminator (the far side of a puff is in its own shadow) and the sky
            // term an occlusion that darkens down-facing normals, so a lit puff reads as a volume, not a flat disc.
            half3 FxLightingSS(float3 positionWS, half3 n, float2 screenUV, half wrap, half selfShadow)
            {
                Light mainLight = GetMainLight();
                half ndlRaw = dot(n, mainLight.direction);
                half ndl = saturate((ndlRaw + wrap) / (1.0 + wrap));
                ndl *= lerp((half)1.0, (half)smoothstep(-0.45, 0.7, ndlRaw), selfShadow);
                half ao = lerp((half)1.0, (half)(0.5 + 0.5 * saturate(n.y * 0.5 + 0.5)), selfShadow);
                half3 c = mainLight.color * (ndl * mainLight.distanceAttenuation) + SampleSH(n) * ao;
            #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;          // LIGHT_LOOP_BEGIN reads inputData (Forward+ cluster loop)
                    inputData.positionWS = positionWS;
                    inputData.normalizedScreenSpaceUV = screenUV;
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, positionWS);
                        half ndlA = saturate((dot(n, light.direction) + wrap) / (1.0 + wrap));
                        c += light.color * (light.distanceAttenuation * ndlA);
                    LIGHT_LOOP_END
                }
            #endif
                return c;
            }

            half3 FxLighting(float3 positionWS, half3 n, float2 screenUV, half wrap)
            {
                return FxLightingSS(positionWS, n, screenUV, wrap, (half)0.0);
            }

            // sphere-impostor normal on a camera-facing particle quad. The quad's world tangent frame comes from screen
            // derivatives (cotangent frame), so it is right for any particle roll or UV orientation.
            half3 FxImpostorNormal(float3 positionWS, float2 uv, float2 q, float z)
            {
                float3 dp1 = ddx(positionWS);
                float3 dp2 = ddy(positionWS);
                float2 duv1 = ddx(uv);
                float2 duv2 = ddy(uv);
                float3 N = normalize(cross(dp1, dp2) + 1e-7);
                float3 toCam = GetWorldSpaceNormalizeViewDir(positionWS);
                N = dot(N, toCam) < 0.0 ? -N : N;
                float3 dp2perp = cross(dp2, N);
                float3 dp1perp = cross(N, dp1);
                float3 T = dp2perp * duv1.x + dp1perp * duv2.x;
                float3 B = dp2perp * duv1.y + dp1perp * duv2.y;
                float invmax = rsqrt(max(max(dot(T, T), dot(B, B)), 1e-12));
                T *= invmax;
                B *= invmax;
                // q = uv * 2 - 1 so d(q)/d(uv) = 2: the quad half-extent maps to 1
                return (half3)normalize(T * q.x + B * q.y + N * z);
            }

            half FxSoftParticle(float3 positionWS, float2 screenUV)
            {
                if (_SoftFade <= 0.0) return 1.0;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float fragEye = -TransformWorldToView(positionWS).z;
                return (half)saturate((sceneEye - fragEye) / _SoftFade);
            }

            // Ground decals (ground light, shock rings) lie on a coarse height grid, so raised ground, crater rims and low props
            // cut them with a hard edge under the depth buffer test. With _GroundBand > 0 the material draws with ZTest Always
            // and tests the camera depth texture here instead: a scene surface up to 0.6 x band (eye depth) in front of the decal
            // still receives it in full, it fades out up to the band, and anything further in front (units, walls) hides it.
            half FxGroundBand(float3 positionWS, float2 screenUV)
            {
                if (_GroundBand <= 0.0) return 1.0;
                float rawDepth = SampleSceneDepth(screenUV);
                float sceneEye;
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        rawDepth = 1.0 - rawDepth;
                    #endif
                    sceneEye = lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                }
                else sceneEye = LinearEyeDepth(rawDepth, _ZBufferParams);
                float fragEye = -TransformWorldToView(positionWS).z;
                return (half)(1.0 - smoothstep(_GroundBand * 0.6, _GroundBand, fragEye - sceneEye));
            }

            // M12 terrain ring coverage: 32-sided annulus, antialiased with fwidth of the squared radius
            // value noise for the foam patch (shape 11)
            float OzFxHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float OzFxNoise(float2 p)
            {
                float2 c = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(OzFxHash(c), OzFxHash(c + float2(1.0, 0.0)), f.x), lerp(OzFxHash(c + float2(0.0, 1.0)), OzFxHash(c + float2(1.0, 1.0)), f.x), f.y);
            }

            half FxBandCoverage(float2 offset, float outerR, half innerFrac, float sides)
            {
                float r2 = dot(offset, offset);
                if (sides >= 3.0)
                {
                    float sector = TWO_PI / sides;
                    float ang = atan2(offset.y, offset.x) + TWO_PI;
                    float sideAngle = fmod(ang, sector) - sector * 0.5;
                    float side = cos(sideAngle) / cos(sector * 0.5);
                    r2 *= side * side;
                }
                float outer2 = outerR * outerR;
                float inner2 = outer2 * innerFrac * innerFrac;
                float edge = max(fwidth(r2), 1e-4);
                return (half)(smoothstep(inner2 - edge, inner2 + edge, r2) * (1.0 - smoothstep(outer2 - edge, outer2 + edge, r2)));
            }

            // fx_look: 2D value noise with analytic derivatives (value, d/dx, d/dy); 3-octave fbm normalised to ~[0, 1]
            float FxHash21(float2 p)
            {
                p = frac(p * float2(233.34, 851.73));
                p += dot(p, p + 23.45);
                return frac(p.x * p.y);
            }

            float3 FxValueNoiseD(float2 p)
            {
                float2 i0 = floor(p);
                float2 f = p - i0;
                float2 u = f * f * (3.0 - 2.0 * f);
                float2 du = 6.0 * f * (1.0 - f);
                float a = FxHash21(i0);
                float b = FxHash21(i0 + float2(1.0, 0.0));
                float c = FxHash21(i0 + float2(0.0, 1.0));
                float d = FxHash21(i0 + float2(1.0, 1.0));
                float k = a - b - c + d;
                return float3(a + (b - a) * u.x + (c - a) * u.y + k * u.x * u.y, du * (float2(b - a, c - a) + k * u.yx));
            }

            float3 FxFbmD(float2 p)
            {
                float3 n0 = FxValueNoiseD(p);
                float3 n1 = FxValueNoiseD(p * 2.03 + float2(17.1, 9.2));
                float3 n2 = FxValueNoiseD(p * 4.01 + float2(-5.3, 31.7));
                float v = n0.x * 0.5714 + n1.x * 0.2857 + n2.x * 0.1429;
                float2 g = n0.yz * 0.5714 + n1.yz * (0.2857 * 2.03) + n2.yz * (0.1429 * 4.01);
                return float3(saturate((v - 0.5) * 1.7 + 0.5), g * 1.7);
            }

            // fx_look fireball heat ramp: soot < rim < body < hot < core (linear; only hot / core exceed the 1.25 bloom threshold)
            half3 FxFireRamp(half h)
            {
                half3 c = lerp(_FireSoot.rgb, _FireRim.rgb, smoothstep((half)0.0, (half)0.24, h));
                c = lerp(c, _FireMid.rgb, smoothstep((half)0.24, (half)0.56, h));
                c = lerp(c, _FireHot.rgb, smoothstep((half)0.56, (half)0.8, h));
                return lerp(c, _FireCore.rgb, smoothstep((half)0.8, (half)0.97, h));
            }

            // MOTION BLUR option, spark channels (_StreakBlur > 0) on a stretched billboard: uv.x runs from the head (0, the front
            // along the velocity) to the tail end (1). Returns the sprite's own coordinate along the streak: the front half of the
            // sprite stays as it is (a round head), its rear half is drawn out over the tail. fade = 1 at the head .. 0 at the tail
            // end (power _StreakBlur); a streak ends at 24 widths. A spark at rest (as long as it is wide) is the plain sprite.
            float FxStreakAlong(float2 uv, out half fade)
            {
                float2 gu = float2(ddx(uv.x), ddy(uv.x));
                float2 gv = float2(ddx(uv.y), ddy(uv.y));
                float aspect = clamp(length(gv) / max(length(gu), 1e-6), 1.0, 64.0);       // length / width on screen
                float s = uv.x * aspect;                                                    // distance from the front edge, in widths
                float t = saturate((s - 0.5) / max(min(aspect, 24.0) - 0.5, 1e-3));         // 0 at the head centre .. 1 at the tail end
                float k = saturate(aspect - 1.0);                                           // 0 at rest .. 1 from twice as long as wide
                fade = (half)lerp(1.0, pow(saturate(1.0 - t), max(_StreakBlur, 0.1)), k);
                return s < 0.5 ? s * 2.0 - 1.0 : t * lerp(1.0, 0.5, k);
            }

            // MOTION BLUR option, puffs blurred along their path (_SmearPath): one read of a puff at q (its own coordinates, -1 .. 1):
            // x = its coverage as shape 2 draws it, without the fine erosion (one read of the smoke flipbook at the mip that matches
            // the spacing of the reads, or the plain round puff when the flipbook is off), y = coverage x the birth heat share.
            // Nothing outside the puff's own quad.
            half2 FxPuffTap(float2 q, float age, float rnd, float lod, half erode, half soft)
            {
                float r2 = dot(q, q);
                if (max(abs(q.x), abs(q.y)) > 1.0) return half2(0.0, 0.0);
                if (_AtlasOn > 0.5)
                {
                    float ang = rnd * 6.2831853 + age * _Spin;
                    float2 cs = float2(cos(ang), sin(ang));
                    float2 qr = float2(cs.x * q.x - cs.y * q.y, cs.y * q.x + cs.x * q.y);
                    qr.x *= frac(rnd * 7.31) < 0.5 ? -1.0 : 1.0;
                    float row = floor(frac(rnd * 13.17) * 7.999);
                    float cell = min(floor(saturate(age * _FlipRate) * 7.0 + 0.5), 7.0);
                    float2 fuv = clamp(qr * 0.5 + 0.5, 0.03, 0.97);
                    half4 sm = SAMPLE_TEXTURE2D_LOD(_SmokeAtlas, sampler_SmokeAtlas, (float2(cell, row) + fuv) * 0.125, lod);
                    half body = (half)smoothstep(0.1, 0.9, sm.a);
                    half dens = sm.a - erode * 0.5 * (1.0 - sm.a * 0.6);
                    half c = smoothstep((half)0.0, (half)max(0.08, _EdgeSoft * 0.75), dens) * lerp((half)0.6, (half)1.0, body) * (half)0.89;
                    half rs = (half)saturate(1.0 - r2 / 0.72);
                    c *= lerp((half)1.0, rs * rs * (3.0 - 2.0 * rs), soft);
                    half hk = sm.b * body * (half)0.775;
                    return half2(c, c * hk * hk);
                }
                half c1 = (half)smoothstep(0.0, max(_EdgeSoft, 1e-3), saturate(1.0 - r2) - _NoiseAmt * 0.5);
                return half2(c1, c1 * (half)saturate(1.0 - r2));
            }

            half4 FxFrag(FxVaryings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float3 positionWS = i.positionWS;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                half4 vc = i.color;                                   // particle colour (linear via applyActiveColorSpace) + alpha
                half3 tint = _Color.rgb * vc.rgb;
                float2 q = i.uv.xy * 2.0 - 1.0;
                float r = length(q);
                int shape = (int)round(_Shape);

                half3 rgb = half3(0.0, 0.0, 0.0);
                half cover = 0.0;
                half opq = _Opaqueness;                               // fx_v2: shape 3 may make it follow the heat

                if (shape == 0)
                {
                    float rr = r;
                    half sfade = 1.0;
                    if (_StreakBlur > 0.0) rr = length(float2(FxStreakAlong(i.uv.xy, sfade), q.y));   // MOTION BLUR option: a spark, its rear half drawn out as a fading tail
                    half core = 1.0 - smoothstep(0.0, 0.18, rr);
                    half halo = pow(saturate(1.0 - rr), 2.35);
                    half3 hot = lerp(tint, _CoreColor.rgb, core * _CoreMix);
                    cover = vc.a * (core * 0.9 + halo * 0.42) * step(rr, 1.0) * sfade;
                    rgb = hot * _Intensity;
                }
                else if (shape == 1 && _StreakTaper > 0.0)
                {
                    // uv.x = 0 at the FRONT (the particle position, along the velocity) and 1 at the tail; the quad's aspect comes from the
                    // screen derivatives, so the head is a true round disc one width wide whatever the streak length; the body tapers to
                    // a point and dims toward the tail; the head is (1 + _StreakTaper) x brighter and white-hot (blooms)
                    float2 gu = float2(ddx(i.uv.x), ddy(i.uv.x));
                    float2 gv = float2(ddx(i.uv.y), ddy(i.uv.y));
                    float aspect = clamp(length(gv) / max(length(gu), 1e-6), 1.0, 64.0);
                    float s = i.uv.x * aspect;                                  // distance from the front edge, in widths
                    float yw = q.y * 0.5;                                       // -0.5 .. 0.5 widths across
                    float dh = length(float2(s - 0.5, yw));                     // to the head centre
                    half headDisc = (half)(1.0 - smoothstep(0.30, 0.5, dh));
                    half headCore = (half)(1.0 - smoothstep(0.0, 0.3, dh));
                    float tt = saturate((s - 0.5) / max(aspect - 0.5, 1e-3));   // 0 at the head .. 1 at the tail tip
                    float hw = lerp(0.40, 0.04, pow(tt, 0.8));                  // body half-width (widths)
                    half bodyC = (half)((1.0 - smoothstep(hw * 0.35, hw, abs(yw))) * step(0.5, s) * (1.0 - smoothstep(0.7, 1.0, tt)));
                    half fall = (half)lerp(1.0, 0.2, pow(tt, 0.7));
                    half gain = lerp(fall, (half)(1.0 + _StreakTaper), headDisc);
                    half3 hot = lerp(tint, _CoreColor.rgb, headCore * _CoreMix);
                    // the body cools along the tail: yellow-white head -> orange body -> red tail at every age (reference E2 / E3)
                    half3 bodyCol = tint * lerp(half3(1.0, 1.0, 1.0), half3(1.0, 0.3, 0.15), (half)smoothstep(0.0, 0.7, tt));
                    rgb = lerp(bodyCol, hot, headDisc) * _Intensity * gain;
                    cover = vc.a * max(headDisc, bodyC);
                }
                else if (shape == 1)
                {   // M12 spark / droplet / tracer streak: a rounded box ("square") with a white-hot core, axis-agnostic
                    float p = lerp(2.0, 8.0, _Squareness);
                    float2 qs = q;
                    half sfade = 1.0;
                    if (_StreakBlur > 0.0) qs.x = FxStreakAlong(i.uv.xy, sfade);   // MOTION BLUR option: a spark, its rear half drawn out as a fading tail
                    float2 aq = max(abs(qs), 1e-4);
                    float d = pow(pow(aq.x, p) + pow(aq.y, p), 1.0 / p);
                    half core = 1.0 - smoothstep(0.0, 0.45, d);
                    half body = 1.0 - smoothstep(0.6, 1.0, d);
                    half3 hot = lerp(tint, _CoreColor.rgb, core * _CoreMix);
                    half3 lit = _Lit > 0.0 ? tint * FxLighting(positionWS, (half3)normalize(i.normalWS), screenUV, _Wrap) : half3(0.0, 0.0, 0.0);
                    rgb = lerp(hot * _Intensity, lit + _EmissionColor.rgb, _Lit);
                    cover = vc.a * body * sfade;
                }
                else if (shape == 2)
                {   // M12 smoke / dust sphere, lit like MeshStandardMaterial; dust soft edge smoothstep(.02,.72,|n.v|).
                    // fx_look (_NoiseAmt > 0): fbm on the rotated, boiling quad erodes the silhouette into billows and bumps the
                    // sphere normal; sun + sky + blast point lights with a self-shadowed terminator; heat glow at birth; the
                    // albedo darkens to _AgeColor over life (age = uv.z, stable random = uv.w).
                    // MOTION BLUR option, puffs blurred along their path (_SmearPath): FxVert widened the quad along the path; here every
                    // pixel finds its place on the puff as it stands (q) and the path in the puff's own coordinates (mq). Both come from
                    // the screen derivatives of the corner positions before the widening, so any roll or mirror of the quad is right.
                    // pathMat is one value for the whole material, so no derivative sits inside a per-pixel test: with the option off, and
                    // on every material that is not blurred along its path, nothing of this runs.
                    float2 mq = float2(0.0, 0.0);
                    half pathIn = 1.0;
                    bool pathMat = _SmearPath > 0.5 && _Smear > 0.0 && _OZFxSmear.x > 0.0;
                    if (pathMat)
                    {
                        float3 da1 = ddx(i.smearA.xyz);
                        float3 da2 = ddy(i.smearA.xyz);
                        float2 dq1 = ddx(q);
                        float2 dq2 = ddy(q);
                        float3 nq = cross(da1, da2);
                        float nl = dot(nq, nq);
                        if (i.smearA.w > 0.5 && nl > 1e-20)
                        {
                            float invD = rsqrt(nl);
                            nq *= invD;
                            float3 a2p = cross(da2, nq);
                            float3 a1p = cross(nq, da1);
                            float3 gx = (a2p * dq1.x + a1p * dq2.x) * invD;      // world gradient of q.x on the puff as it stands
                            float3 gy = (a2p * dq1.y + a1p * dq2.y) * invD;
                            float3 dpos = positionWS - i.smearA.xyz;
                            q += float2(dot(gx, dpos), dot(gy, dpos));
                            mq = float2(dot(gx, i.smearV), dot(gy, i.smearV));
                            pathIn = (half)step(max(abs(q.x), abs(q.y)), 1.0);
                        }
                    }
                    float r2 = dot(q, q);
                    float z = sqrt(saturate(1.0 - r2));
                    float age = i.uv.z;
                    float2 qn = q;
                    half hz = (half)z;                                // depth for the birth heat glow
                    half detShade = 1.0;
                    half wrapE = _Wrap, ssE = _SelfShadow;
                    if (_AtlasOn > 0.5)
                    {
                        float rnd = i.uv.w;
                        float ang = rnd * 6.2831853 + age * _Spin;
                        float2 cs = float2(cos(ang), sin(ang));
                        float2 qr = float2(cs.x * q.x - cs.y * q.y, cs.y * q.x + cs.x * q.y);
                        float flip = frac(rnd * 7.31) < 0.5 ? -1.0 : 1.0;
                        qr.x *= flip;
                        float row = floor(frac(rnd * 13.17) * 7.999);
                        float ft = saturate(age * _FlipRate) * 7.0;
                        float c0 = min(floor(ft), 6.0);
                        half fb = (half)saturate(ft - c0);
                        float2 fuv = clamp(qr * 0.5 + 0.5, 0.004, 0.996);
                        float2 uv0 = (float2(c0, row) + fuv) * 0.125;
                        half4 s0 = SAMPLE_TEXTURE2D(_SmokeAtlas, sampler_SmokeAtlas, uv0);
                        half4 s1 = SAMPLE_TEXTURE2D(_SmokeAtlas, sampler_SmokeAtlas, uv0 + float2(0.125, 0.0));
                        half4 sm = lerp(s0, s1, fb);
                        float2 duv = qr * (0.5 * _DetailScale) + float2(rnd * 17.3, rnd * 5.1) + float2(0.0, age * _Boil * 0.12);
                        half det = SAMPLE_TEXTURE2D(_SmokeDetail, sampler_SmokeDetail, duv).r * 0.62
                                 + SAMPLE_TEXTURE2D(_SmokeDetail, sampler_SmokeDetail, duv * 2.37 + 0.31).g * 0.38;
                        half erode = _DetailAmt * (1.0 + _Dissolve * 2.0 * (half)smoothstep(0.45, 1.0, age));
                        half dens = sm.a - erode * (1.0 - det) * (1.0 - sm.a * 0.6);
                        half body = (half)smoothstep(0.1, 0.9, sm.a);    // thin outer smoke stays translucent (no stacked-disc look)
                        cover = vc.a * smoothstep((half)0.0, (half)max(0.08, _EdgeSoft * 0.75), dens) * lerp((half)0.6, (half)1.0, body) * (0.78 + 0.22 * det);
                        half2 nxy = sm.rg * 2.0 - 1.0;
                        nxy.x *= (half)flip;
                        qn = float2(cs.x * nxy.x + cs.y * nxy.y, -cs.y * nxy.x + cs.x * nxy.y) * (1.0 + _Bump * 0.5) * lerp(0.35, 1.0, body);   // flat at thin edges: no dark rim rings
                        z = sqrt(saturate(1.0 - dot(qn, qn)));
                        hz = sm.b * body * (0.55 + 0.45 * det);         // birth heat glow follows the billows (no flat orange discs)
                        detShade = 0.84 + 0.16 * det;                 // fine crevices inside big puffs
                        // round normal and read as separate hard lit balls. Below ~260 px radius the puff fades out radially like a
                        // soft blob, its normal flattens and the sun wraps further, so neighbouring puffs fuse into one translucent cloud.
                        float rpx = 1.0 / max(length(ddx(q)), 1e-5);                     // puff radius in screen px
                        half small = (half)(1.0 - smoothstep(60.0, 260.0, rpx));
                        half soft = saturate(small + (half)0.2) * saturate((half)0.55 + _EdgeSoft);
                        half rs = (half)saturate(1.0 - dot(qr, qr) / 0.72);
                        cover *= lerp((half)1.0, rs * rs * (3.0 - 2.0 * rs), soft);
                        qn *= lerp(1.0, 0.45, small);
                        z = sqrt(saturate(1.0 - dot(qn, qn)));
                        wrapE = lerp(_Wrap, max(_Wrap, (half)0.65), small);
                        ssE = _SelfShadow * lerp((half)1.0, (half)0.3, small);
                    }
                    else if (_NoiseAmt > 0.0)
                    {
                        float rnd = i.uv.w;
                        float ang = rnd * 6.2831853 + age * _Spin;
                        float2 cs = float2(cos(ang), sin(ang));
                        float2 qr = float2(cs.x * q.x - cs.y * q.y, cs.y * q.x + cs.x * q.y);
                        float3 nz = FxFbmD(qr * _NoiseScale + rnd * 41.7 + float2(0.0, age * _Boil));
                        float2 g = nz.yz * _NoiseScale;
                        g = float2(cs.x * g.x + cs.y * g.y, cs.x * g.y - cs.y * g.x);          // gradient back to quad space
                        qn = q - g * _Bump;
                        float dens = saturate(1.0 - r2) - _NoiseAmt * (1.0 - nz.x);
                        cover = vc.a * smoothstep(0.0, _EdgeSoft, dens) * (0.8 + 0.2 * nz.x);
                    }
                    else
                    {
                        cover = vc.a * smoothstep(0.02, 0.02 + _EdgeSoft, z) * step(r2, 1.0);
                    }
                    if (pathMat)
                    {   // the puff's coverage = the mean of 6 reads of the puff along the way it travels during the exposure (this pixel is
                        // the middle of that way). A path shorter than 1 % of the puff keeps the puff's own coverage, so slow smoke is
                        // drawn exactly as without it. Colour and light stay those of this pixel; the birth heat follows the reads.
                        // The derivatives stand outside the per-pixel test, so the 6 reads are a real branch: a puff at rest pays nothing.
                        float2 dqx = ddx(q);
                        float2 dqy = ddy(q);
                        float ml = length(mq);
                        cover *= pathIn;
                        UNITY_BRANCH
                        if (ml > 0.02)
                        {
                            half softT = _AtlasOn > 0.5 ? saturate((half)(1.0 - smoothstep(60.0, 260.0, 1.0 / max(length(dqx), 1e-5))) + (half)0.2) * saturate((half)0.55 + _EdgeSoft) : (half)0.0;
                            float lodT = clamp(log2(max(max(256.0 * max(length(dqx), length(dqy)), ml * 42.7), 1.0)), 0.0, 4.5);
                            half erodeT = _DetailAmt * (1.0 + _Dissolve * 2.0 * (half)smoothstep(0.45, 1.0, age));
                            half2 acc = half2(0.0, 0.0);
                            for (int tk = 0; tk < 6; tk++)
                                acc += FxPuffTap(q - mq * ((tk + 0.5) / 6.0 - 0.5), age, i.uv.w, lodT, erodeT, softT);
                            half kb = (half)saturate((ml - 0.02) / 0.1);
                            cover = lerp(cover, vc.a * acc.x / 6.0, kb);
                            hz = lerp(hz, (half)sqrt(acc.y / max(acc.x, 1e-4)), kb);
                        }
                    }
                    float3 frameWS = i.smearA.w > 0.5 ? i.smearA.xyz : positionWS;      // the frame of the puff as it stands, not of the widened quad (its light stays its own)
                    half3 n = FxImpostorNormal(frameWS, i.uv.xy, qn, z);
                    half3 light = FxLightingSS(positionWS, n, screenUV, wrapE, ssE);
                    half3 albedo = tint * lerp(half3(1.0, 1.0, 1.0), _AgeColor.rgb, (half)smoothstep(0.08, 1.0, age)) * detShade;
                    half heat = (half)((1.0 - smoothstep(0.0, max(_HeatTime, 1e-3), age)) * hz * hz);
                    half litAmt = _AtlasOn > 0.5 ? lerp(_Lit, (half)1.0, (half)saturate(_OZNight)) : _Lit;   // never glows at night
                    rgb = albedo * lerp(half3(1.0, 1.0, 1.0), light, litAmt) + _EmissionColor.rgb + _HeatColor.rgb * heat;
                }
                else if (shape == 3)
                {   // M12 fireball / core: emissive heart (x7 fire, x12 core), cooler rim so only the heart blooms
                    float r2 = dot(q, q);
                    float z = sqrt(saturate(1.0 - r2));
                    if (_FireRamp > 0.5)
                    {   // fx_look heat ramp: heat = sphere depth x billow noise, cooling over life (+ bias for the core flash);
                        // the silhouette is noise-eroded like the smoke, so no disc edge is ever visible
                        float age = i.uv.z, rnd = i.uv.w;
                        float ang = rnd * 6.2831853 + age * _Spin;
                        float2 cs = float2(cos(ang), sin(ang));
                        float2 qr = float2(cs.x * q.x - cs.y * q.y, cs.y * q.x + cs.x * q.y);
                        float3 nz = FxFbmD(qr * _NoiseScale + rnd * 23.3 + float2(0.0, age * _Boil));
                        float dens = saturate(1.0 - r2) - _NoiseAmt * (1.0 - nz.x);
                        float heat = pow(z, 1.35) * (0.72 + 0.56 * nz.x);
                        heat = saturate(heat * saturate(1.0 - _Cooling * age) + _HeatBias * (1.0 - age));
                        rgb = FxFireRamp((half)heat) * _Intensity;
                        if (_FireOpacity.x >= 0.0) opq = (half)lerp(_FireOpacity.x, _FireOpacity.y, heat);   // fx_v2: hot = additive
                        cover = vc.a * smoothstep(0.0, _EdgeSoft, dens);
                    }
                    else
                    {
                        half heart = pow(saturate(z), _RimPower);
                        half3 hot = lerp(tint * _RimGain, lerp(tint, _CoreColor.rgb, _CoreMix * heart * heart), heart);
                        half3 lit = _Lit > 0.0 ? _EmissionColor.rgb * FxLighting(positionWS, FxImpostorNormal(positionWS, i.uv.xy, q, z), screenUV, 0.0) : half3(0.0, 0.0, 0.0);
                        rgb = hot * _Intensity + lit;
                        cover = vc.a * smoothstep(0.0, _EdgeSoft, z) * step(r2, 1.0);
                    }
                }
                else if (shape == 4)
                {   // M12 shock ring on a ground-conforming mesh: diffuse -> tone/6 at opacity, emissive += tone * opacity
                    float2 ringOff = positionWS.xz - _RingCenter.xz;
                    half c = FxBandCoverage(ringOff, _RingRadius, _BandInner, _BandSides);
                    if (_BandProfile > 0.0)
                    {   // fx_look: thin shock front, brightest at the leading (outer) edge, soft trailing edge
                        half f = (half)saturate((length(ringOff) / max(_RingRadius, 1e-3) - _BandInner) / max(1.0 - _BandInner, 1e-3));
                        c *= lerp((half)1.0, (half)(0.3 + 0.7 * f * f), _BandProfile);
                    }
                    cover = c * _RingAlpha * vc.a;
                    half3 albedoLit = (tint / 6.0) * FxLighting(positionWS, half3(0.0, 1.0, 0.0), screenUV, 0.0);
                    rgb = tint * _Intensity + albedoLit * _Opaqueness;
                }
                else if (shape == 5)
                {   // flat ring on a world-aligned particle quad (M12 ripple: FX_ShockRing .88-1.0, lit water colour + emission)
                    half c = FxBandCoverage(q, 1.0, _BandInner, _BandSides);
                    cover = c * vc.a;
                    half3 lit = _Lit > 0.0 ? tint * FxLighting(positionWS, half3(0.0, 1.0, 0.0), screenUV, 0.0) : tint;
                    rgb = lit * _Intensity + _EmissionColor.rgb;
                }
                else if (shape == 10)
                {   // soft swells on water, on a world-aligned quad: a few broad concentric waves (_BandSides of them across the
                    // radius, _BandInner their phase); a crest is lit a little lighter than the water, a trough a little darker
                    // (_CoreMix of the lit tone); they thin out toward the middle and end softly at the rim
                    float waves = _BandSides > 0.5 ? _BandSides : 2.5;
                    float w = sin((r * waves - _BandInner) * TWO_PI);
                    half env = (half)(smoothstep(0.10, 0.55, r) * (1.0 - smoothstep(0.90, 1.0, r)));
                    half crest = (half)saturate(w) * 0.6;                 // a broad, half see-through light band: a gentle edge, never a drawn line
                    half trough = (half)saturate(-w) * 0.75;
                    half3 lit = _Lit > 0.0 ? tint * FxLighting(positionWS, half3(0.0, 1.0, 0.0), screenUV, 0.0) : tint;
                    half sum = crest + trough;
                    cover = sum * env * vc.a;
                    rgb = ((lit * _Intensity + _EmissionColor.rgb) * crest + lit * _CoreMix * trough) / max(sum, 1e-3);
                }
                else if (shape == 11)
                {   // a foam patch on water, on a world-aligned quad: a soft disc broken into flecks toward its rim (the flecks stay
                    // where they are on the water while the patch spreads)
                    float n = OzFxNoise(positionWS.xz * 2.6) * 0.62 + OzFxNoise(positionWS.xz * 6.3 + 11.3) * 0.38;
                    half body = (half)(1.0 - smoothstep(0.30, 1.0, r + (n - 0.5) * 0.6));
                    half flecks = (half)smoothstep(0.30, 0.60, n + (1.0 - r) * 0.42);
                    cover = body * flecks * vc.a;
                    half3 lit = _Lit > 0.0 ? tint * FxLighting(positionWS, half3(0.0, 1.0, 0.0), screenUV, 0.0) : tint;
                    rgb = lit * _Intensity + _EmissionColor.rgb;
                }
                else if (shape == 6)
                {   // M12 blast ground light: colour * exp(-5 (d/r)^2) * w, added as emission
                    float2 d = (positionWS.xz - _RingCenter.xz) / max(_RingRadius, 1e-3);
                    cover = exp(-dot(d, d) * 5.0) * _RingAlpha * vc.a;
                    rgb = tint * _Intensity;
                }
                else if (shape == 7)
                {   // M12 scorch / tread mark: burn baked in vertex alpha; dst * (1 - .58 burn)
                    cover = vc.a * _RingAlpha;
                    rgb = half3(0.0, 0.0, 0.0);
                }
                else if (shape == 8)
                {   // rocket exhaust cone (FX_Flame, M12 #ffb13b x15): hot where it faces the camera, soft at the silhouette
                    half3 n = (half3)normalize(i.normalWS);
                    half ndv = abs(dot(n, GetWorldSpaceNormalizeViewDir(positionWS)));
                    half heart = pow(ndv, 1.5);
                    rgb = lerp(tint * _RimGain, lerp(tint, _CoreColor.rgb, _CoreMix * heart), heart) * _Intensity;
                    cover = vc.a * smoothstep(0.0, 0.35, ndv) * _RingAlpha;
                }
                else
                {   // shell rear tracer: emissive (5,.95,.12) on the rear 22% -> 5% of the length
                    half tail = 1.0 - smoothstep(_AxisRange.x, _AxisRange.y, i.axisT);
                    cover = tail * _RingAlpha;
                    rgb = tint * _Intensity;
                }

                cover *= FxSoftParticle(positionWS, screenUV);
                cover *= FxGroundBand(positionWS, screenUV);
                cover = saturate(cover);

                // fog: MixFog(1) - MixFog(0) = how much of this surface survives the fog (1 when fog is off)
                half fogFactor = InitializeInputDataFog(float4(positionWS, 1.0), 0.0);
                half fogVis = MixFog(half3(1.0, 1.0, 1.0), fogFactor).r - MixFog(half3(0.0, 0.0, 0.0), fogFactor).r;
                half alpha = cover * opq;
                half3 outRgb = rgb * cover * fogVis + unity_FogColor.rgb * ((1.0 - fogVis) * alpha);
                return half4(outRgb, alpha);
            }
            ENDHLSL
        }
    }
}
