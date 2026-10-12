// OpenZ/WaterV4: clear desert-oasis water for the L02 desert river, URP 17.3 (Unity 6.3, Forward+). The L17 harbour keeps its approved muddy olive on OpenZ/WaterStylized (v3), untouched.
//
// The desert water follows a reference (refs/style_refs/
// water_ref_desert_oasis_topdown.png = the primary target, water_ref_desert_oasis.png = the transparency of the shallows):
// the reference's water palette was sampled (k-means in CIELAB) and every colour here was fitted on the offline preview
// to it at the game's exposure, dE2000 <= 1.2 per zone (README_IMPORT.md section 2). Nothing here is emissive: the bed
// tint can only transmit (every channel <= 1), the body is a lit albedo, the foam is white paint lit by the scene (its
// gain capped at 1.5), caustics and sheen are subtle. At the shore the water is almost fully transparent: the warm sand
// bed with its ripple terraces (stepped ledges parallel to the shore, from the bed map's shore distance) shows through a
// pale aqua-green tint; depth absorption (red first) and the body turn it turquoise, teal, then deep blue in the channel,
// with darker blue patches over the deep bed. The shore foam is soft broken white wave lines lapping in and out on some
// stretches of the shoreline (never a closed rim), like the top-down reference.
//
// Why v4: v3 read as a flat pastel cut-out with soft edges and nothing inside (refs/style_refs/issue_water_v3_flat.png):
// the real bed is a flat 0.75 m everywhere past 1 m from the bank (one colour), the bed was plain tan paint, and every
//
// What the proven open-source stylised waters do, and what this shader takes from them (credits: CREDITS_water_v4.md):
//  - danielshervheim/unity-stylized-water (BSD-3-Clause): the bed seen through the water is MULTIPLIED by an HDR shallow
//    colour and fades into the deep colour by transmittance. Here: bed x aqua tint x per-channel absorption exp(-k d),
//    so the bed keeps its detail (multiplying keeps relative contrast) while red dies first: pale aqua -> turquoise -> teal.
//  - IronWarrior/ToonWaterShader (Roystan tutorial, Unlicense): crisp toon foam = a noise-shaped cutoff, anti-aliased with
//    a smoothstep of about one pixel; wider foam around objects than along gentle banks.
//  - MatrixRex/Uber-Stylized-Water (MIT): shoreline bands that travel toward the shore and dissolve further out;
//    caustics projected on the underwater geometry; intersection foam.
//  - the ameye.dev notes (stylised water, realtime caustics): vertical depth from the depth buffer, depth-checked
//    refraction from a full-resolution opaque texture.
// No code from them is copied (the BSD shader is Built-in pipeline CG, the Uber one an 8 MB Shader Graph).
//
// What an RTS camera at 55-70 m needs on top (all measured on offline previews composited over real game frames):
//  - a VIRTUAL channel depth from a baked L02 bed map (T_WaterBedMap_L02: extra depth along the centre of every branch,
//    signed distance to the waterline, terrain height, across-channel position): bright aqua shallows -> clear turquoise
//    -> a continuous deeper teal channel, without touching the terrain mesh (wading, tracks and the walk collider unchanged)
//  - a river bed worth looking at: the terrain under the water is REDRAWN here (only where the scene under the pixel IS the
//    terrain; legs, piers and rubble keep their own colour): pale lagoon sand with current ripple marks and a few clean
//    pebbles, a darker silty channel, hash-scattered rounded stones and swaying weed tufts, lit by sun / moon, its own
//    shadow and the Forward+ lamps
//  - a procedural, animated caustic web (Voronoi F2-F1 whose cells morph over time), warped by the surface ripples,
//    projected on everything under the water, plus a small untinted share on the surface (light dancing on the water)
//  - two scrolling ripple normal layers (flow-map advected along every branch, two-phase); the broad layer is long-crested
//    (wind ripples), and its faces turned toward the sun light up as soft moving streaks: a sun sheen, never litter specks
//  - sky reflection by Fresnel and a slow surface tone variation
//  - crisp foam: a scalloped, lapping, sometimes broken waterline (width in METRES from the shore, readable at 70 m) with
//    a few bubbles; thin shoreline bands travelling in to the shore; a soft ring around piers and wading legs
//  - the body is lit like an albedo by a hue-neutralised light (the game's sun is very warm), never emissive; low sun
//    darkens it (no blue-hour glow), the moon keeps its cool colour, night desaturates and darkens it
//  - every animated pattern uses bounded motion (two-phase flow, phases, constant slow drifts): nothing shears or smears
//    however long the game runs
// The wet dark-sand band above the waterline is a separate shader (OpenZ/WaterV4Wet) on its own proxy mesh, because the
// river mesh ends only ~0.8 m (median) past the waterline and would cut the band with a hard edge.
// Needs: URP Depth Texture ON, Opaque Texture ON, Opaque Downsampling NONE (OpenZ > Water v4 > 1 Apply sets them).
Shader "OpenZ/WaterV4"
{
    Properties
    {
        [Header(Water colour by virtual depth)]
        _ShallowColor ("Body colour: shallow (sRGB)", Color) = (0.1876, 0.7056, 0.7388, 1)
        _MidColor ("Body colour: mid (clear turquoise)", Color) = (0.3476, 0.5579, 0.6092, 1)
        _DeepColor ("Body colour: deep channel (blue)", Color) = (0.2556, 0.434, 0.5405, 1)
        _DeepPatch ("Deep patches: darker blue over deeper bed (0 = off)", Range(0, 1)) = 0.5
        _DeepPatchColor ("Deep patch colour", Color) = (0.184, 0.3125, 0.3892, 1)
        _DeepPatchScale ("Deep patch size (m)", Range(0.5, 20)) = 2.2
        _ShelfWidth ("Lagoon shelf: shallow look within (m of the waterline)", Range(0.01, 4)) = 1.2
        _ShelfScale ("Lagoon shelf: depth scale at the waterline", Range(0.05, 1)) = 0.3
        _MidDepth ("Mid colour at depth (m)", Range(0.05, 3)) = 0.45
        _DeepDepth ("Deep colour at depth (m)", Range(0.1, 6)) = 1.2
        _Clarity ("Clarity (m to 63 % of the max opacity)", Range(0.05, 6)) = 0.343
        _OpacityMax ("Body opacity of the deepest water", Range(0, 1)) = 0.97
        _BedTintShallow ("Bed tint at the waterline (pale aqua)", Color) = (0.7139, 0.9732, 0.9321, 1)
        _BedTint ("Bed tint under the water (aqua)", Color) = (0.485, 0.948, 0.7516, 1)
        _BedTintGain ("Bed tint gain (x the tint colours)", Range(0.5, 3)) = 1
        _BedTintMaxLum ("Bed tint luminance cap", Range(0.5, 1.5)) = 1
        _BedTintDepth ("Bed tint: shallow -> full over (m)", Range(0.01, 3)) = 0.5
        _Absorption ("Absorption per m (R, G, B): red dies first", Vector) = (2.276, 0.517, 0.362, 0)
        _LightNeutral ("Water light hue removed (0 = the sun's own colour)", Range(0, 1)) = 0.85
        _Refraction ("Refraction (m of bed shift at full ripple slope)", Range(0, 0.5)) = 0.12
        _EdgeFade ("Waterline softening (m of depth)", Range(0.001, 0.2)) = 0.012

        [Header(L02 bed map)]
        [Toggle(_WATER_BEDMAP)] _UseBedMap ("Use the bed map", Float) = 1
        [NoScaleOffset] _BedMap ("Bed map (R extra depth, G shore distance, B terrain height, A across)", 2D) = "black" {}
        _BedRect ("Bed map rect (x0, z0, size x, size z in m)", Vector) = (-24, -204, 192, 228)
        _BedEncode ("Bed map decode (extra max, sd min, sd range, y min)", Vector) = (1.2, -2, 6, -1.6)
        _BedEncodeY ("Bed map decode (y range, -, -, -)", Vector) = (2.2, 0, 0, 0)
        _ChannelDepth ("Channel depth multiplier", Range(0, 2)) = 0.85

        [Header(River bed redraw)]
        [Toggle(_WATER_BED_DETAIL)] _BedDetailOn ("Redraw the river bed", Float) = 1
        _BedDetail ("Bed redraw amount", Range(0, 1)) = 1
        [NoScaleOffset] _BedAlbedo ("Bed albedo (sand ripples + pebbles, A height)", 2D) = "grey" {}
        [NoScaleOffset] _BedNormal ("Bed normal (RG x/z, B cavity)", 2D) = "grey" {}
        _BedTile ("Bed texture tile (m)", Range(1, 20)) = 6
        _BedSandColor ("Shelf sand tint", Color) = (1, 1, 1, 1)
        _BedSiltColor ("Channel silt tint", Color) = (0.62, 0.72, 0.72, 1)
        _BedRelief ("Bed relief (normal strength)", Range(0, 2)) = 1
        [NoScaleOffset] _Decals ("Decal atlas 4x4 (RGB albedo, A coverage)", 2D) = "black" {}
        [NoScaleOffset] _DecalsN ("Decal atlas normals (RG x/z, B sway, A height)", 2D) = "grey" {}
        _DecalCell ("Decal grid cell (m)", Range(0.8, 8)) = 2.4
        _DecalSize ("Decal size (m per atlas cell)", Range(0.3, 3)) = 2.2
        _RockDensity ("Rounded stones", Range(0, 1)) = 0.16
        _WeedDensity ("Weed tufts", Range(0, 1)) = 0.08
        _PebbleDensity ("Pebble clusters", Range(0, 1)) = 0.04
        _WeedSway ("Weed sway", Range(0, 1)) = 0.5
        _DecalShadow ("Decal contact shadow", Range(0, 1)) = 0.5
        _BedBlendDepth ("Bed redraw fades in over (m of depth at the waterline)", Range(0.02, 0.5)) = 0.3

        [Header(Sand terraces under the shallows (stepped ledges parallel to the shore))]
        _TerraceSpacing ("Terrace spacing (m)", Range(0.2, 3)) = 0.55
        _TerraceReach ("Terraces reach (m from the waterline)", Range(0, 6)) = 2.4
        _TerraceRelief ("Terrace step relief (normal)", Range(0, 3)) = 0.6
        _TerraceLip ("Terrace lip lighter, riser foot darker (albedo)", Range(0, 0.5)) = 0.06
        _TerraceWobble ("Terrace wobble (x spacing)", Range(0, 1.5)) = 0.6
        _TerraceWobbleScale ("Terrace wobble size (m)", Range(0.5, 20)) = 3.5

        [Header(Caustics (procedural animated web))]
        _CausticsStrength ("Caustics strength", Range(0, 6)) = 0.3
        _CausticsScale ("Caustic cell size (m)", Range(0.3, 6)) = 1.7
        _CausticsWidth ("Caustic line half-width (m)", Range(0.005, 0.2)) = 0.04
        _CausticsGlow ("Caustic line glow", Range(0, 2)) = 0.25
        _CausticsMorph ("Caustic cell morph speed", Range(0, 3)) = 0.6
        _CausticsSpeed ("Caustic drift (m per s)", Range(0, 1)) = 0.12
        _CausticsWarp ("Caustic warp by the ripples (m)", Range(0, 1)) = 0.12
        _CausticsCurl ("Caustic line curl (bends the straight cell edges)", Range(0, 2)) = 0.9
        _CausticsPatch ("Caustic brightness patches (m)", Range(1, 40)) = 8
        _CausticsSurface ("Caustic share on the surface (untinted)", Range(0, 1)) = 0
        _CausticsFadeIn ("Caustics fade-in depth (m)", Range(0.001, 1)) = 0.06
        _CausticsFadeOut ("Caustics gone at virtual depth (m)", Range(0.2, 6)) = 1

        [Header(Ripples and sun sheen)]
        [NoScaleOffset][Normal] _RippleNormal ("Ripple normal map (tileable, long-crested)", 2D) = "bump" {}
        _RippleScaleA ("Layer A size (m per tile)", Range(0.5, 40)) = 4.5
        _RippleScaleB ("Layer B size (m per tile)", Range(0.5, 40)) = 1.9
        _RippleStrengthA ("Layer A strength", Range(0, 2)) = 0.55
        _RippleStrengthB ("Layer B strength", Range(0, 2)) = 0.5
        _RippleSpeed ("Drift speed (m per s)", Range(0, 2)) = 0.22
        _FlowDirection ("Flow direction without a flow map (deg)", Range(0, 360)) = 90
        [Toggle(_WATER_FLOWMAP)] _UseFlowMap ("Use the flow map", Float) = 1
        [NoScaleOffset] _FlowMap ("Flow map (RG = direction x speed, 0.5 = still)", 2D) = "gray" {}
        _FlowRect ("Flow map rect (x0, z0, size x, size z in m; a negative size mirrors)", Vector) = (-24, -204, 192, 228)
        _FlowCycle ("Flow cycle (s)", Range(1, 30)) = 6
        _SheenIntensity ("Sun sheen intensity", Range(0, 2)) = 0.012
        _SheenThreshold ("Sun sheen: ripple tilt toward the sun that lights up", Range(0, 0.5)) = 0.075
        _SheenSoftness ("Sun sheen softness", Range(0.001, 0.2)) = 0.06
        _SheenScale ("Sun sheen ripple size (m per tile)", Range(0.5, 40)) = 5
        _SheenSlope ("Sun sheen ripple strength", Range(0, 3)) = 1
        _SheenBlur ("Sun sheen broadness (mip bias)", Range(0, 4)) = 0.8
        _SheenWindAngle ("Sun sheen wind direction (deg)", Range(0, 360)) = 35
        _SheenPatchScale ("Sun sheen patch size (m)", Range(1, 60)) = 10
        _SheenPatchCover ("Sun sheen patch cover", Range(0, 1)) = 0.55
        _SheenFadeHeight ("Sun sheen calmer above camera height (m)", Range(20, 400)) = 160

        [Header(Sky reflection and surface)]
        _SkyColor ("Sky reflection tint", Color) = (0.88, 0.95, 1, 1)
        _ReflectionStrength ("Sky reflection strength", Range(0, 1)) = 0.45
        _ReflectionF0 ("Reflection facing the camera", Range(0, 0.3)) = 0.05
        _FresnelPower ("Fresnel power", Range(1, 10)) = 4
        _SurfaceNoise ("Surface tone variation", Range(0, 0.3)) = 0.06
        _SurfaceNoiseScale ("Surface tone variation size (m)", Range(1, 40)) = 7

        [Header(Shore foam)]
        [NoScaleOffset] _NoiseTex ("Noise (R smooth, G cells, BA distortion)", 2D) = "grey" {}
        _FoamColor ("Foam colour = white paint albedo (A = opacity)", Color) = (0.84, 0.87, 0.84, 0.92)
        _FoamBright ("Foam albedo gain by day (1 = plain paint; capped at 1.5: no glow)", Range(0.5, 1.5)) = 1
        _FoamScale ("Foam detail size (m)", Range(0.2, 10)) = 1.6
        _FoamGapScale ("Foam gap and wobble size (m)", Range(1, 30)) = 8
        _FoamLineM ("Swash line width at full lap (m from the waterline)", Range(0, 1)) = 0.13
        _FoamLineGaps ("Shoreline without foam (share)", Range(0, 0.9)) = 0.4
        _FoamScallop ("Swash line scallops", Range(0, 1)) = 0.5
        _FoamLace ("Foam lace: lines break into filaments (0 = solid)", Range(0, 1)) = 0.55
        _LapSpeed ("Waterline lapping speed", Range(0, 5)) = 1.3
        _ObjectFoam ("Foam around piers and wading legs (m of depth)", Range(0, 0.6)) = 0.16
        _ObjectRing ("Foam ring around anything crossing the surface (m, 0 = off)", Range(0, 1)) = 0.22
        _BandSpacing ("Shore band spacing (m)", Range(0.1, 3)) = 0.45
        _BandWidthM ("Wave line crest width (m)", Range(0.01, 0.5)) = 0.11
        _BandTailM ("Wave line soft tail behind the crest (m)", Range(0.01, 1)) = 0.08
        _BandReach ("Shore bands reach (m from the waterline)", Range(0, 4)) = 1.1
        _BandSpeed ("Shore bands speed (bands per s)", Range(0, 2)) = 0.22
        _BandBreakup ("Shore band break-up", Range(0, 1)) = 0.5
        _BandWobble ("Shore band wobble (m)", Range(0, 1)) = 0.35
        _BandOpacity ("Shore band opacity", Range(0, 1)) = 0.95

        [Header(Light)]
        _AmbientStrength ("Ambient strength", Range(0, 2)) = 0.6
        _ShadowStrength ("Sun shadow strength on the water", Range(0, 1)) = 0.7
        [ToggleOff(_WATER_NO_ADDITIONAL_LIGHTS)] _AdditionalLights ("Lamps, headlights and fires light the water", Float) = 1
        _AddLightStrength ("Lamp light strength", Range(0, 4)) = 1

        [Header(Night)]
        _NightDarken ("Night body darkening", Range(0, 1)) = 0.5
        _NightDesaturate ("Night body desaturation", Range(0, 1)) = 0.7
        _DuskDarken ("Dusk and dawn darkening (low sun)", Range(0, 1)) = 0.75

        [Header(Fallbacks)]
        [Toggle(_WATER_NO_OPAQUE)] _NoOpaque ("No opaque texture (plain blending, no refraction)", Float) = 0
        [Toggle(_WATER_NO_DEPTH)] _NoDepth ("No depth texture (fixed depth)", Float) = 0
        _FallbackDepth ("Fixed depth (m)", Range(0, 5)) = 0.7
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _ShallowColor; half4 _MidColor; half4 _DeepColor; half _DeepPatch; half4 _DeepPatchColor; float _DeepPatchScale;
            float _ShelfWidth; float _ShelfScale; float _MidDepth; float _DeepDepth; float _Clarity; half _OpacityMax;
            half4 _BedTintShallow; half4 _BedTint; half _BedTintGain; half _BedTintMaxLum; float _BedTintDepth; float4 _Absorption;
            half _LightNeutral; float _Refraction; float _EdgeFade;
            half _UseBedMap; float4 _BedRect; float4 _BedEncode; float4 _BedEncodeY; half _ChannelDepth;
            half _BedDetailOn; half _BedDetail; float _BedTile; half4 _BedSandColor; half4 _BedSiltColor; half _BedRelief;
            float _DecalCell; float _DecalSize; half _RockDensity; half _WeedDensity; half _PebbleDensity; half _WeedSway; half _DecalShadow;
            float _BedBlendDepth; float _TerraceSpacing; float _TerraceReach; half _TerraceRelief; half _TerraceLip; float _TerraceWobble; float _TerraceWobbleScale;
            half _CausticsStrength; float _CausticsScale; float _CausticsWidth; half _CausticsGlow; float _CausticsMorph; float _CausticsSpeed;
            float _CausticsWarp; float _CausticsCurl; float _CausticsPatch; half _CausticsSurface; float _CausticsFadeIn; float _CausticsFadeOut;
            float _RippleScaleA; float _RippleScaleB; half _RippleStrengthA; half _RippleStrengthB; float _RippleSpeed;
            float _FlowDirection; half _UseFlowMap; float4 _FlowRect; float _FlowCycle;
            half _SheenIntensity; half _SheenThreshold; half _SheenSoftness; float _SheenScale; half _SheenSlope; half _SheenBlur; float _SheenWindAngle;
            float _SheenPatchScale; half _SheenPatchCover; float _SheenFadeHeight;
            half4 _SkyColor; half _ReflectionStrength; half _ReflectionF0; half _FresnelPower; half _SurfaceNoise; float _SurfaceNoiseScale;
            half4 _FoamColor; half _FoamBright; float _FoamScale; float _FoamGapScale; float _FoamLineM; half _FoamLineGaps; half _FoamScallop;
            half _FoamLace; float _LapSpeed; float _ObjectFoam; float _ObjectRing;
            float _BandSpacing; float _BandWidthM; float _BandTailM; float _BandReach; float _BandSpeed; half _BandBreakup; float _BandWobble; half _BandOpacity;
            half _AmbientStrength; half _ShadowStrength; half _AdditionalLights; half _AddLightStrength;
            half _NightDarken; half _NightDesaturate; half _DuskDarken;
            half _NoOpaque; half _NoDepth; float _FallbackDepth;
        CBUFFER_END

        TEXTURE2D(_BedMap);       SAMPLER(sampler_BedMap);
        float _OZNight;           // night package (NightGrade): 0 by day .. 1 at night, global

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

        // L02 bed map: x = extra channel depth (m), y = signed distance to the waterline (m, + in the water),
        // z = terrain height (m), w = across-channel position (0 bank .. 1 channel line)
        float4 WaterBedMap(float2 xz)
        {
            float2 uv = (xz - _BedRect.xy) / max(_BedRect.zw, 1e-3);
            float4 m = SAMPLE_TEXTURE2D_LOD(_BedMap, sampler_BedMap, uv, 0);
            float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
            return float4(m.r * _BedEncode.x * inside,
                          lerp(-_BedEncode.z, _BedEncode.y + m.g * _BedEncode.z, inside),
                          _BedEncode.w + m.b * _BedEncodeY.x,
                          m.a * inside);
        }
        ENDHLSL

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
            #pragma shader_feature_local_fragment _WATER_BEDMAP
            #pragma shader_feature_local_fragment _WATER_BED_DETAIL
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_RippleNormal); SAMPLER(sampler_RippleNormal);
            TEXTURE2D(_FlowMap);      SAMPLER(sampler_FlowMap);
            TEXTURE2D(_BedAlbedo);    SAMPLER(sampler_BedAlbedo);
            TEXTURE2D(_BedNormal);    SAMPLER(sampler_BedNormal);
            TEXTURE2D(_Decals);       SAMPLER(sampler_Decals);
            TEXTURE2D(_DecalsN);      SAMPLER(sampler_DecalsN);
            TEXTURE2D(_NoiseTex);     SAMPLER(sampler_NoiseTex);

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

            static const float2x2 kRotB = float2x2(0.6427876, -0.7660444, 0.7660444, 0.6427876);   // layer B: 50 deg
            static const float2x2 kRotP = float2x2(0.8571673, -0.5150381, 0.5150381, 0.8571673);   // deep patches: 31 deg
            static const half3 kLuma = half3(0.2126, 0.7152, 0.0722);

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

            // the light's hue partly removed (luminance kept): the game's warm sun would otherwise turn turquoise olive
            // (by day only: the moon's cool light keeps its colour, night water reads dark blue-grey)
            half3 WaterNeutral(half3 light)
            {
                return lerp(light, dot(light, kLuma).xxx, _LightNeutral * (1.0 - saturate((half)_OZNight)));
            }

            // a tint colour x gain with its luminance capped and every channel <= 1: water only transmits, the bed may never
            // come out brighter than it is (rev2's gain 1.5 made the shallows glow)
            half3 WaterTint(half3 c)
            {
                half3 t = c * _BedTintGain;
                t = t / max(dot(t, kLuma) / max(_BedTintMaxLum, (half)0.01), (half)1.0);
                return min(t, (half3)1.0);
            }

            float2 WaterFlowVelocity(float2 xz)
            {
            #if defined(_WATER_FLOWMAP)
                float2 sgn = step(0.0, _FlowRect.zw) * 2.0 - 1.0;
                float2 uv = (xz - _FlowRect.xy) / (sgn * max(abs(_FlowRect.zw), 1e-3));
                float2 f = (SAMPLE_TEXTURE2D_LOD(_FlowMap, sampler_FlowMap, uv, 0).rg * 2.0 - 1.0) * sgn;
                return f * _RippleSpeed;
            #else
                float a = radians(_FlowDirection);
                return float2(cos(a), sin(a)) * _RippleSpeed;
            #endif
            }

            half2 WaterRippleSlope(float2 xz, float2 disp)
            {
                float2 pA = (xz - disp) / _RippleScaleA;
                half3 nA = UnpackNormalScale(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, pA), _RippleStrengthA);
                float2 pB = mul(kRotB, xz - disp * 1.35) / _RippleScaleB + float2(0.37, 0.71);
                half3 nB = UnpackNormalScale(SAMPLE_TEXTURE2D(_RippleNormal, sampler_RippleNormal, pB), _RippleStrengthB);
                return nA.xy + mul(nB.xy, kRotB);
            }

            // the broad, long-crested layer in the wind frame, mip-biased (soft streaks), turned back to world
            half2 WaterSheenSlopeAt(float2 xz, float2 disp, float2 cs)
            {
                float2 p = xz - disp;
                float2 r = float2(cs.x * p.x + cs.y * p.y, -cs.y * p.x + cs.x * p.y) / _SheenScale;
                half2 a = UnpackNormalScale(SAMPLE_TEXTURE2D_BIAS(_RippleNormal, sampler_RippleNormal, r, _SheenBlur), _SheenSlope).xy;
                return half2(cs.x * a.x - cs.y * a.y, cs.y * a.x + cs.x * a.y);
            }

            // two-phase flow (Valve), phase offset by a slow noise so resets never pulse together: bounded displacement
            void WaterFlowPhases(float2 xz, float t, out float p0, out float p1, out half w1)
            {
                float cyc = max(_FlowCycle, 0.5);
                float ph = t / cyc + WaterValueNoise(xz * 0.21) * 0.5;
                p0 = frac(ph); p1 = frac(ph + 0.5);
                w1 = (half)abs(1.0 - 2.0 * p0);
            }

            // caustic web: F2 - F1 of a Voronoi whose feature points circle slowly (cells morph); 0 on the web lines
            float WaterWeb(float2 p, float t)
            {
                float2 i = floor(p), f = frac(p);
                float F1 = 9.0, F2 = 9.0;
                [unroll] for (int oz = -1; oz <= 1; oz++)
                {
                    [unroll] for (int ox = -1; ox <= 1; ox++)
                    {
                        float2 o = float2(ox, oz);
                        float2 c = i + o;
                        float h1 = WaterHash21(c), h2 = WaterHash21(c + float2(17.3, 41.9));
                        float2 pt = o + 0.5 + 0.38 * float2(sin(t * _CausticsMorph + 6.2831853 * h1), sin(t * _CausticsMorph * 0.87 + 6.2831853 * h2));
                        float2 dd = pt - f;
                        float d2 = dot(dd, dd);
                        F2 = (d2 < F1) ? F1 : min(F2, d2);
                        F1 = min(F1, d2);
                    }
                }
                return sqrt(F2) - sqrt(F1);
            }

            half WaterCaustics(float2 p, float t, half2 slope, float mPerPx)
            {
                float s = max(_CausticsScale, 0.05);
                float2 w = p + (float2)slope * _CausticsWarp + float2(_CausticsSpeed, _CausticsSpeed * 0.4) * t;
                float2 q = w / s;
                // bend the straight Voronoi edges (organic caustic lines, not cracked glass)
                q += (float2(WaterValueNoise(q * 0.6), WaterValueNoise(q * 0.6 + float2(5.2, 1.3))) - 0.5) * _CausticsCurl;
                float e = WaterWeb(q, t) * s;                                            // metres (x2 to the line)
                float lw = _CausticsWidth, aa = max(mPerPx, 1e-4);
                half webLine = (half)(1.0 - smoothstep(lw - aa, lw + aa, e));
                half glow = (half)exp(-e / max(lw * 3.0, 1e-3)) * _CausticsGlow;
                half patch = (half)smoothstep(0.15, 0.85, WaterValueNoise(p / _CausticsPatch + float2(t * 0.03, -t * 0.02))) * 0.8 + 0.2;
                return (webLine + glow) * patch;
            }

            // ---- the river bed: albedo, normal (world), cavity from the tiled textures plus hash-scattered decals
            // sdB = metres from the waterline at the bed point (+ in the water), gsd = its gradient (points to the channel)
            void WaterBedSurface(float2 p, float2 dpx, float2 dpy, float t, half across, float sdB, float2 gsd, half3 L, out half3 alb, out half3 nrm, out half ao)
            {
                // the tile, turned a little per 13 m block of noise so the repeat never lines up
                float2 uv = p / _BedTile;
                uv += (float2(WaterValueNoise(p * 0.071), WaterValueNoise(p * 0.071 + 7.3)) - 0.5) * 0.35;
                // explicit gradients everywhere in here: the decal branches differ per pixel, implicit mips would break
                float2 gx = dpx / _BedTile, gy = dpy / _BedTile;
                half4 a4 = SAMPLE_TEXTURE2D_GRAD(_BedAlbedo, sampler_BedAlbedo, uv, gx, gy);
                half4 n4 = SAMPLE_TEXTURE2D_GRAD(_BedNormal, sampler_BedNormal, uv, gx, gy);
                half zone = (half)WaterValueNoise(p * 0.19 + 3.1);
                half silt = (half)saturate(smoothstep(0.45, 0.95, across) * (0.75 + 0.5 * zone));
                alb = a4.rgb * lerp(_BedSandColor.rgb, _BedSiltColor.rgb, silt);
                half2 nxz = (n4.rg * 2.0 - 1.0) * _BedRelief * (1.0 - 0.6 * silt);
                ao = n4.b;

                // rev3: sand terraces like the reference oasis: contour lines of the shore distance, wobbled, each a flat
                // tread and a short riser dropping toward the channel, a lighter lip on the tread edge and a darker foot.
                // Faded in past the waterline, out with distance from the shore, in the channel and when a terrace would be
                // under ~6 px wide (the overview never shimmers).
                {
                    float sp = max(_TerraceSpacing, 0.05);
                    float pxM = max(length(dpx), length(dpy));
                    half fadeT = (half)((1.0 - smoothstep(_TerraceReach * 0.6, max(_TerraceReach, 1e-3), sdB)) * smoothstep(0.03, 0.25, sdB)
                               * (1.0 - smoothstep(0.6, 0.9, (float)across)) * (1.0 - smoothstep(sp / 8.0, sp / 4.0, pxM)));
                    if (fadeT > 0.001)
                    {
                        float q = sdB / sp + (WaterValueNoise(p / max(_TerraceWobbleScale, 0.1) + 11.3) - 0.5) * 2.0 * _TerraceWobble;
                        float fq = frac(q);
                        half riser = (half)sin(3.14159265 * saturate((fq - 0.7) / 0.3));        // slope bell on the riser
                        float lq = (fq - 0.66) / 0.05;
                        half lip = (half)exp(-lq * lq);
                        // rev3 review: a soft foot centred on the riser bottom (the one-sided foot cut a hard dark contour
                        // line along the whole shore: an outline, not a terrace)
                        float fw = (fq > 0.5 ? fq - 1.0 : fq) / 0.09;
                        half foot = (half)exp(-fw * fw);
                        float gl = length(gsd);
                        half2 dir = (half2)(gsd / max(gl, 1e-4) * saturate(gl * 2.0));
                        nxz += dir * (riser * _TerraceRelief * fadeT);
                        alb *= 1.0 + _TerraceLip * fadeT * (lip - 0.6 * foot);
                    }
                }

                // decals: one per grid cell at most, kept inside the cell (no neighbour lookups)
                float cell = max(_DecalCell, 0.2);
                float2 c = floor(p / cell);
                float2 f = p / cell - c;
                float h1 = WaterHash21(c + 11.7), h2 = WaterHash21(c + 47.3), h3 = WaterHash21(c + 83.1), h4 = WaterHash21(c + 19.9);
                half rockP = _RockDensity * smoothstep(0.2, 0.5, across);
                half weedP = _WeedDensity * smoothstep(0.08, 0.25, across) * (1.0 - smoothstep(0.75, 0.95, across)) * (0.4 + 1.2 * zone);
                half pebP = _PebbleDensity * (1.0 - 0.5 * silt);
                float variant = -1.0;
                float sizeM = _DecalSize * lerp(0.45, 0.85, h2);
                if (h1 < rockP) { variant = floor(h3 * 5.99); sizeM *= 0.9; }                           // 0-3 stones, 4-5 slabs
                else if (h1 < rockP + weedP) { variant = 8.0 + floor(h3 * 6.99); sizeM *= 1.1; }          // 8-14 weeds
                else if (h1 < rockP + weedP + pebP) { variant = h3 < 0.85 ? 6.0 + floor(h4 * 1.99) : 15.0; sizeM *= 0.8; }
                if (variant >= 0.0)
                {
                    half s = (half)min(sizeM / cell, 0.95);
                    float2 ctr = 0.5 + (float2(h3, h4) - 0.5) * (1.0 - s);
                    float ang = h2 * 6.2831853 + h4 * 3.0;
                    float2 cs = float2(cos(ang), sin(ang));
                    float2 q = (f - ctr) / s;                                                                  // -0.5..0.5 in the decal
                    float2 lq = float2(q.x * cs.x + q.y * cs.y, -q.x * cs.y + q.y * cs.x);
                    bool weed = variant >= 8.0 && variant < 15.0;
                    if (weed)
                    {   // tips sway more than the roots (radial weight), each tuft on its own phase, slow current push
                        float wgt = saturate(length(lq) * 2.2);
                        float sw = sin(t * 1.35 + h1 * 40.0 + p.x * 0.35) + 0.45 * sin(t * 2.3 + h2 * 17.0);
                        lq += float2(0.05, 0.025) * sw * wgt * wgt * _WeedSway;
                    }
                    float2 cellUV = float2(fmod(variant, 4.0), 3.0 - floor(variant / 4.0));             // atlas row 0 = top
                    float2 duv = (lq + 0.5);
                    if (all(duv > 0.0) && all(duv < 1.0))
                    {
                        float2 auv = (cellUV + duv) * 0.25;
                        // atlas gradient: world metres -> cell units -> decal (rotated, scaled) -> atlas quarter
                        float2 ax = dpx / (cell * s) * 0.25, ay = dpy / (cell * s) * 0.25;
                        ax = float2(ax.x * cs.x + ax.y * cs.y, -ax.x * cs.y + ax.y * cs.x);
                        ay = float2(ay.x * cs.x + ay.y * cs.y, -ay.x * cs.y + ay.y * cs.x);
                        half4 dc = SAMPLE_TEXTURE2D_GRAD(_Decals, sampler_Decals, auv, ax, ay);
                        half4 dn = SAMPLE_TEXTURE2D_GRAD(_DecalsN, sampler_DecalsN, auv, ax, ay);
                        // soft contact shadow: the coverage one step toward the sun, blurred (mip), under the decal
                        float2 sunL = float2(L.x * cs.x + L.z * cs.y, -L.x * cs.y + L.z * cs.x);
                        float2 suv = (cellUV + saturate(duv - sunL * 0.06)) * 0.25;
                        half sh = SAMPLE_TEXTURE2D_LOD(_Decals, sampler_Decals, suv, 3.0).a;
                        alb *= 1.0 - _DecalShadow * sh * (1.0 - dc.a);
                        ao *= 1.0 - 0.35 * _DecalShadow * sh * (1.0 - dc.a);
                        alb = lerp(alb, dc.rgb * lerp((half3)1.0, _BedSiltColor.rgb, silt * 0.35), dc.a);   // straight alpha
                        half2 dnx = dn.rg * 2.0 - 1.0;
                        half2 dw = half2(dnx.x * cs.x - dnx.y * cs.y, dnx.x * cs.y + dnx.y * cs.x);          // back to world
                        nxz = lerp(nxz, dw * 1.4, dc.a);
                        ao = lerp(ao, (half)(0.7 + 0.3 * dn.a), dc.a);
                    }
                }
                nrm = normalize(half3(nxz.x, 1.0, nxz.y));
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
                float mPerPx = max(length(fwidth(xz)), 1e-5);                          // metres per pixel on the surface
                half calm = (half)(1.0 - smoothstep(0.07, 0.2, mPerPx));               // 1 near, 0 at the overview

                // ---- the scene under this pixel: real vertical depth
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

                // ---- bed map: channel depth, distance to the shore, terrain height, across position
            #if defined(_WATER_BEDMAP)
                float4 bm = WaterBedMap(xz);
                float extra = bm.x * _ChannelDepth;
                float sd = bm.y;
                half across = (half)bm.w;
                // the scene point sits up to ~0.5 m sideways from the surface point (55 deg view): test it where it is
                half isTerrain = (half)(1.0 - smoothstep(0.07, 0.2, abs(sceneWS.y - WaterBedMap(sceneWS.xz).z)));
            #else
                float extra = 0.0;
                float sd = d * 1.4;                                                    // ~the L02 bank slope
                half across = (half)saturate(d / 0.75);
                half isTerrain = 1.0;
            #endif
                float dv = d + extra * smoothstep(0.02, 0.3, d);                      // virtual depth
                // lagoon shelf: the first _ShelfWidth m from the waterline read as a shallow sand shelf, so the bright aqua
                // rim is wide enough to see from 70 m (the real bank drops to 0.56 m within 0.5 m)
                half shelf = (half)smoothstep(0.0, max(_ShelfWidth, 1e-3), max(sd, 0.0));
                float dvC = dv * lerp(_ShelfScale, 1.0, shelf);                       // colour, opacity and bed tint depth

                // ---- ripples -> normal (calmer at the overview so nothing shimmers); two-phase flow along every branch
                float2 vel = WaterFlowVelocity(xz);
                float p0, p1; half w1;
                WaterFlowPhases(xz, t, p0, p1, w1);
                float cyc = max(_FlowCycle, 0.5);
            #if defined(_WATER_FLOWMAP)
                half2 slope = lerp(WaterRippleSlope(xz, vel * ((p0 - 0.5) * cyc)), WaterRippleSlope(xz + float2(2.3, 1.7), vel * ((p1 - 0.5) * cyc)), w1);
            #else
                half2 slope = WaterRippleSlope(xz, vel * t);
            #endif
                slope *= lerp((half)0.45, (half)1.0, calm);
                half3 n = normalize(half3(slope.x, 1.0, slope.y));
                half3 V = GetWorldSpaceNormalizeViewDir(positionWS);

                // ---- light
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                half3 sunColor = mainLight.color * mainLight.distanceAttenuation;
                half3 L = mainLight.direction;
                half sunShadow = lerp(1.0 - _ShadowStrength, 1.0, mainLight.shadowAttenuation);
                half3 ambient = i.ambientUp * _AmbientStrength;
                half3 diffuse = ambient + sunColor * (saturate(dot(normalize(lerp(up, n, (half)0.5)), L)) * sunShadow);
                half3 addDiffuse = half3(0.0, 0.0, 0.0);
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
                        addDiffuse += radiance * saturate(light.direction.y * 0.5 + 0.5);
                        half3 hAdd = SafeNormalize(float3(light.direction) + float3(V));
                        half3 nStreak = normalize(half3(n.x * 2.2, 1.0, n.z * 2.2));   // lamp reflections break into streaks
                        addSpec += radiance * (pow(saturate(dot(n, hAdd)), 220.0) + (0.25 + 0.5 * night) * pow(saturate(dot(nStreak, hAdd)), 48.0));
                    LIGHT_LOOP_END
                }
            #endif
                diffuse += addDiffuse;
                // low sun (blue hour, dusk, dawn): the ground falls into the tonemapper's toe faster than bright water,
                // so the water is darkened with the sun's height (v3's blue-hour "glow"); off at noon and at night
                half dusk = 1.0 - _DuskDarken * (1.0 - saturate(L.y * 3.0)) * (1.0 - night);
                half3 lightN = WaterNeutral(diffuse) * dusk;

                // ---- water body (in-scatter): 3 stops by virtual depth, opacity by clarity, lit like an albedo
                half tMid = (half)smoothstep(0.0, max(_MidDepth, 1e-3), dvC);
                half tDeep = (half)smoothstep(_MidDepth, max(_DeepDepth, _MidDepth + 1e-3), dvC);
                half3 bodyAlb = lerp(lerp(_ShallowColor.rgb, _MidColor.rgb, tMid), _DeepColor.rgb, tDeep);
                // rev3: darker blue patches over the deep channel (the reference's deeper bed): a static two-octave noise,
                // only where the water is already deep, soft edged. rev3 review: the noise is turned 31 deg and domain-warped
                // (value noise is grid-aligned: unturned, its blobs came out as boxes along the axis-aligned river)
                float2 pq = mul(kRotP, xz) / max(_DeepPatchScale, 0.1);
                pq += (float2(WaterValueNoise(pq * 0.5 + 7.1), WaterValueNoise(pq * 0.5 + 13.7)) - 0.5) * 1.6;
                float pn = WaterValueNoise(pq + 31.7) * 0.65 + WaterValueNoise(mul(kRotB, pq) * 2.3 + 5.1) * 0.35;
                half patchK = _DeepPatch * (half)smoothstep(0.46, 0.68, pn) * tDeep;
                bodyAlb = lerp(bodyAlb, _DeepPatchColor.rgb, patchK);
                half tone = (half)(WaterValueNoise(xz / _SurfaceNoiseScale + float2(0.05, 0.03) * t) * 2.0 - 1.0);
                bodyAlb *= 1.0 + _SurfaceNoise * tone;
                half bodyLum = dot(bodyAlb, kLuma);
                bodyAlb = lerp(bodyAlb, bodyLum.xxx, _NightDesaturate * night) * (1.0 - _NightDarken * night);
                half A = _OpacityMax * (half)(1.0 - exp(-dvC / max(_Clarity, 1e-3)));
                half3 body = bodyAlb * lightN;

                // ---- sky reflection x Fresnel
                half NdotV = saturate(dot(n, V));
                half fres = (_ReflectionF0 + (1.0 - _ReflectionF0) * pow(1.0 - NdotV, _FresnelPower)) * _ReflectionStrength;
                half3 sky = SampleSH(reflect(-V, n)) * _SkyColor.rgb * _AmbientStrength;

                // ---- sun sheen: the long-crested broad layer's faces turned toward the sun azimuth light up as soft
                // streaks that drift with the current (view independent: from the RTS camera the true mirror glint is
                // behind the camera most of the day). A slope threshold, so the cover is controlled and never speckles.
                half3 sunAz = SafeNormalize(float3(L.x, 0.0, L.z) + float3(0.0, 0.0, 1e-4));
                float wa = radians(_SheenWindAngle);
                float2 wcs = float2(cos(wa), sin(wa));
            #if defined(_WATER_FLOWMAP)
                half2 ss = lerp(WaterSheenSlopeAt(xz, vel * ((p0 - 0.5) * cyc), wcs), WaterSheenSlopeAt(xz + float2(2.3, 1.7), vel * ((p1 - 0.5) * cyc), wcs), w1);
            #else
                half2 ss = WaterSheenSlopeAt(xz, vel * t, wcs);
            #endif
                half tilt = (ss.x * sunAz.x + ss.y * sunAz.z) * rsqrt(1.0 + dot(ss, ss));
                half sheen = smoothstep(_SheenThreshold - _SheenSoftness, _SheenThreshold + _SheenSoftness, tilt);
                half patch = (half)WaterValueNoise(xz / _SheenPatchScale + float2(0.02, 0.013) * t);
                sheen *= smoothstep(1.0 - _SheenPatchCover - 0.2, 1.0 - _SheenPatchCover + 0.2, patch);
                float camH = _WorldSpaceCameraPos.y - positionWS.y;
                sheen *= (half)(1.0 - 0.6 * smoothstep(_SheenFadeHeight * 0.6, _SheenFadeHeight, camH));
                sheen *= saturate((L.y - 0.15) * 4.0) * mainLight.shadowAttenuation * (1.0 - 0.75 * night) * (half)smoothstep(0.02, 0.25, d);   // gone at blue hour
                half3 S = sunColor * (sheen * _SheenIntensity) + addSpec;

                // ---- foam (rev3, like the top-down reference): soft white wave lines that lap in and out along the shore,
                // broken into lace, only on some stretches of the shoreline (never a closed rim), plus rings around piers
                // and legs. White PAINT: an albedo lit by the scene's light (darker at dusk and night), never emissive.
                // Noise drifts at a constant slow speed (never with the per-pixel flow: that would shear over time).
                float aa = mPerPx * 0.75;
                float2 fuv = (xz - float2(0.03, 0.02) * t) / _FoamScale;
                half4 nz = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, fuv);
                float2 guv = (xz - float2(0.02, 0.012) * t) / _FoamGapScale;
                half big = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, guv + float2(0.37, 0.11)).r;
                half big2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, guv * 0.61 + float2(0.73, 0.29)).r;
                half lace = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, fuv * 2.3 + float2(0.5, 0.2)).g;   // cellular web
                half stretch = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, guv * 0.2 + float2(0.13, 0.57)).r;  // ~3 m features
                half segN = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, guv * 0.45 + float2(0.61, 0.07)).r;    // ~1 m features
                float sdp = max(sd, 0.0);
                // stretches of shore with foam and stretches without (about _FoamLineGaps of the shoreline has none)
                half shoreK = smoothstep(_FoamLineGaps - 0.1, _FoamLineGaps + 0.1, stretch);
                // (1) the swash line at the waterline: breathes in and out with the lapping (phase varies along the shore),
                // solid at the shore, lacy toward its outer edge
                half lap = (half)(0.5 + 0.5 * sin(t * _LapSpeed + big * 6.0));
                float lineW = _FoamLineM * ((0.3 + 0.7 * lap) * (0.7 + 0.6 * big) + (nz.g - 0.5) * _FoamScallop);
                half foamLine = (half)((1.0 - smoothstep(lineW - aa, lineW + aa, sdp)) * step(aa * 0.5, lineW)) * isTerrain * shoreK;
                half lineOut = (half)saturate(sdp / max(lineW, 1e-4));
                foamLine *= smoothstep(_FoamLace * lineOut - 0.12, _FoamLace * lineOut + 0.12, lace);
                // (2) wave lines travelling in to the shore: a crisp crest on the shore side, a soft lacy tail behind it,
                // born offshore (most broken far out), gone as they reach the swash line
                float bsp = max(_BandSpacing, 1e-3);
                float ph = (sdp + (big - 0.5) * _BandWobble) / bsp + t * _BandSpeed;
                float k = frac(ph);
                half reach = (half)saturate(sdp / max(_BandReach, 1e-3));
                // the crest swells and thins along the shore (~1 m); where it thins below a pixel the line breaks
                half thick = (half)saturate((segN * 0.7 + big * 0.3 - _BandBreakup * (0.3 + 0.5 * reach)) * 3.0);
                float bwf = _BandWidthM * thick / bsp, btf = max(_BandTailM * thick, 1e-3) / bsp, aap = aa / bsp;
                half crest = (half)(smoothstep(0.0, aap, k) * (1.0 - smoothstep(bwf, bwf + btf + aap, k))) * (half)smoothstep(aa * 0.3, aa, _BandWidthM * thick);
                half inTail = (half)saturate((k - bwf) / btf);
                half laceT = _FoamLace * (0.4 + 0.6 * inTail);
                half laceB = smoothstep(laceT - 0.12, laceT + 0.12, lace);
                half bands = crest * laceB * (1.0 - smoothstep((half)0.55, (half)1.0, reach)) * (half)smoothstep(_FoamLineM * 0.8, _FoamLineM * 1.8, sdp)
                           * isTerrain * shoreK * (1.0 - 0.5 * night);
                half foam = max(foamLine, bands * _BandOpacity);
                // (3) around anything that is not the terrain (piers, wading legs, rubble): a wider soft ring
                half objMask = (half)(1.0 - saturate(d / max(_ObjectFoam, 1e-4))) * (1.0 - isTerrain);
                half objCut = (half)(0.35 + 0.45 * nz.r);
                foam = max(foam, smoothstep(objCut - 0.08, objCut + 0.08, objMask));
                // (4) a soft ring on the water around anything that crosses the surface (piers, wading legs): eight taps
                // (four on a circle of _ObjectRing m, four at 0.6 of it); a tap that finds a non-terrain point within ~0.3 m
                // of the water line marks it (bridge decks above the water and units on the bank do not count)
            #if !defined(_WATER_NO_DEPTH) && defined(_WATER_BEDMAP)
                if (_ObjectRing > 0.0 && calm > 0.01)
                {
                    half ringHit = 0.0;
                    [unroll] for (int kr = 0; kr < 8; kr++)
                    {
                        float ang = kr * 0.7853982 + 0.3;
                        float3 pw = positionWS + float3(cos(ang), 0.0, sin(ang)) * (_ObjectRing * ((kr & 1) ? 0.6 : 1.0));
                        float4 pcs = TransformWorldToHClip(pw);
                        float2 suv = float2(pcs.x, pcs.y * _ProjectionParams.x) / max(pcs.w, 1e-4) * 0.5 + 0.5;
                        float3 sp = WaterScenePosition(pw, suv);
                        half notTer = (half)smoothstep(0.07, 0.2, abs(sp.y - WaterBedMap(sp.xz).z));
                        float dy = sp.y - positionWS.y;                                   // + above the water
                        half nearS = (half)((1.0 - smoothstep(0.06, 0.14, dy)) * (1.0 - smoothstep(0.2, 0.35, -dy)));
                        ringHit += notTer * nearS * 0.125;                                // soft: more taps hit closer in
                    }
                    foam = max(foam, smoothstep((half)0.04, (half)0.5, ringHit) * (half)(0.5 + 0.5 * nz.r) * isTerrain);
                }
            #endif
                foam *= _FoamColor.a * (1.0 - 0.55 * night);
                half3 foamLit = _FoamColor.rgb * lerp(min(_FoamBright, (half)1.5), (half)1.0, night) * lightN;   // lit paint

                // ---- surface = what the water itself sends (premultiplied); trans = how much of the bed shows
                half cover = 1.0 - (1.0 - A) * (1.0 - fres);
                half3 surf = body * (A * (1.0 - fres)) + sky * fres;
                surf = surf * (1.0 - foam) + foamLit * foam;
                cover = cover * (1.0 - foam) + foam;
                S *= (1.0 - foam);
                half trans = 1.0 - cover;

                // ---- fog on the surface part only: MixFog(1) - MixFog(0) = how much of the surface survives
                half fogFactor = InitializeInputDataFog(float4(positionWS, 1.0), 0.0);
                half fogVis = MixFog(half3(1.0, 1.0, 1.0), fogFactor).r - MixFog(half3(0.0, 0.0, 0.0), fogFactor).r;

            #if defined(_WATER_NO_OPAQUE)
                surf = surf * fogVis + unity_FogColor.rgb * ((1.0 - fogVis) * cover);
                S *= fogVis;
                return half4((surf + S) * edge, cover * edge);
            #else
                // ---- the bed: refracted (metres of shift -> screen UV at this depth), never anything above the water
                float surfaceEye = max(-TransformWorldToView(positionWS).z, 1e-3);
                float2 nView = TransformWorldToViewDir(float3(n.x, 0.0, n.z), false).xy;
                float refrM = _Refraction * saturate(d / 0.25);
                float2 uvR = screenUV + nView * refrM * float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11) * (0.5 / surfaceEye);
                float3 bedWS = sceneWS;
            #if !defined(_WATER_NO_DEPTH)
                float3 behind = WaterScenePosition(positionWS, uvR);
                if (behind.y > positionWS.y - 0.01) uvR = screenUV; else bedWS = behind;
            #endif
                half3 bed = SampleSceneColor(uvR);
                float2 bedDx = ddx(bedWS.xz), bedDy = ddy(bedWS.xz);
                float dBed = max(positionWS.y - bedWS.y, 0.0);
                half bedShadow = MainLightRealtimeShadow(TransformWorldToShadowCoord(bedWS));
                half3 bedAlbRef = half3(0.6, 0.55, 0.45);                              // for caustics on the scene bed

            #if defined(_WATER_BED_DETAIL)
                // redraw the terrain part of the bed (the painted tan ground) with the river bed, lit here
            #if defined(_WATER_BEDMAP)
                float4 bmB = WaterBedMap(bedWS.xz);
                half bedIsTerrain = (half)(1.0 - smoothstep(0.07, 0.2, abs(bedWS.y - bmB.z)));
                half bedAcross = (half)bmB.w;
                float sdB = bmB.y;
                float2 gsd = float2(WaterBedMap(bedWS.xz + float2(0.25, 0.0)).y - sdB, WaterBedMap(bedWS.xz + float2(0.0, 0.25)).y - sdB) / 0.25;
            #else
                half bedIsTerrain = 1.0, bedAcross = across;
                float sdB = sd; float2 gsd = float2(0.0, 0.0);
            #endif
                // rev3: fades in over _BedBlendDepth (15 cm) so the bank paint blends into the bed sand with no seam
                half detailK = _BedDetail * bedIsTerrain * (half)smoothstep(0.012, max(_BedBlendDepth, 0.02), dBed);
                {
                    half3 bAlb, bN; half bAo;
                    WaterBedSurface(bedWS.xz, bedDx, bedDy, t, bedAcross, sdB, gsd, L, bAlb, bN, bAo);
                    // the light that reaches the bed has passed through the water: its warm hue mostly gone (as the body)
                    half3 bLight = WaterNeutral(i.ambientUp * _AmbientStrength * bAo * (0.75 + 0.25 * bN.y)
                                 + sunColor * (saturate(dot(bN, L)) * bedShadow) + addDiffuse * bAo);
                    half3 bedLit = bAlb * bLight;
                    half bedFog = InitializeInputDataFog(float4(bedWS, 1.0), 0.0);
                    bedLit = MixFog(bedLit, bedFog);
                    bed = lerp(bed, bedLit, detailK);
                    bedAlbRef = lerp(bedAlbRef, bAlb, detailK);
                }
            #endif

                // caustics on everything under the water (bed, legs, piers): sun only, faded in at the shore, out with depth
                half dvBed = (half)(dBed + extra * smoothstep(0.02, 0.3, dBed));
                half caFade = (half)(saturate(dBed / max(_CausticsFadeIn, 1e-3)) * (1.0 - smoothstep(_CausticsFadeOut * 0.35, _CausticsFadeOut, dvBed)));
                caFade *= lerp((half)0.35, (half)1.0, calm) * (1.0 - night) * saturate(L.y * 3.0) * bedShadow;
                half caLight = 0.0;
                if (_CausticsStrength > 0.0)
                    caLight = WaterCaustics(bedWS.xz, t, slope, mPerPx) * caFade;
                half3 sunN = WaterNeutral(sunColor) * saturate(L.y);
                bed += bedAlbRef * sunN * (caLight * _CausticsStrength);
                // a small untinted share of the web on the surface (light dancing on the water), under the foam
                S += sunN * (caLight * _CausticsSurface * (1.0 - foam));

                // the bed seen through the water: a pale-aqua -> aqua tint (ramps in over _BedTintDepth, luminance capped:
                // the bed never glows) x per-channel absorption along the virtual depth (red dies first); at night the
                // tint desaturates and the bed darkens with the body
                half kt = (half)smoothstep(0.0, max(_BedTintDepth, 1e-3), dvC);
                half3 tint = lerp(WaterTint(_BedTintShallow.rgb), WaterTint(_BedTint.rgb), kt);
                tint = lerp(tint, dot(tint, kLuma).xxx, _NightDesaturate * night);
                half3 Tb = (half3)exp(-_Absorption.rgb * (1.0 - 0.7 * night) * dvC);
                // (rev3 review: 0.975 x _NightDarken 0.5 = rev3's 0.75 x 0.65: the bed at night unchanged, the body lifted)
                half3 bedSeen = bed * tint * Tb * ((1.0 - 0.975 * _NightDarken * night) * dusk);

                surf = surf * fogVis + unity_FogColor.rgb * ((1.0 - fogVis) * cover);
                S *= fogVis;
                half3 outC = surf + S + bedSeen * trans;
                return half4(outC * edge, edge);
            #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
