// ground map keeps its layout (roads, dirt, water edges), but sand texels are graded to a clean golden orange, fine noise is
// smoothed away (blurred mip), and world-space wind ripples are added: big soft domain-warped swirls plus small ripples,
// light crests and soft troughs. Standard URP lighting (main + additional/FX lights in Forward+, SSAO, fog) with warm,
// soft shadows: shadowed sand is lifted with a warm orange-brown instead of going grey.
// signed-distance texture (the paint is sampled just outside its own soft edge on each side and blended by the pixel's
// analytic coverage: the paint keeps its colours, only the edges get sharp) + world-space detail (asphalt / concrete, dirt,
// sand: albedo, cracks, normals; two scales against tiling; faded with distance). Off by default (_SdfMode 0, _DetailOn 0),
// so the L17 ground can share this shader with every desert feature zeroed (OpenZ/Textures v2/2 Materials).
Shader "OpenZ/Sand"
{
    Properties
    {
        _BaseMap ("Ground paint", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.12
        _SandColor ("Sand colour (sRGB)", Color) = (0.93, 0.66, 0.27, 1)
        _SandRef ("Sand reference value (linear V)", Range(0.1,1)) = 0.62
        _Grade ("Grade sand to the sand colour", Range(0,1)) = 0.85
        _Blur ("Smooth fine noise", Range(0,1)) = 0.75
        _BlurLod ("Smoothing mip", Range(0,8)) = 3.5
        _RippleLarge ("Wind swirls strength", Range(0,0.4)) = 0.1
        _RippleSmall ("Small ripples strength", Range(0,0.2)) = 0.035
        _SwirlScale ("Wind swirl period (m)", Float) = 16
        _RippleScale ("Small ripple period (m)", Float) = 1.7
        _WindAngle ("Wind angle (deg)", Float) = 28
        _ShadowTint ("Warm shadow colour", Color) = (0.55, 0.28, 0.1, 1)
        _ShadowLift ("Warm shadow lift", Range(0,1.5)) = 0.55
        [NoScaleOffset] _GroundSdf ("v2 edge SDF (R road, G dash (L02) or plate weight (L17), B road markings (L17 mode 3))", 2D) = "gray" {}
        _SdfMode ("v2 crisp edges: 0 off, 1 roads + dashes, 2 roads + plates, 3 roads + plates + road markings", Float) = 0
        _SdfRange ("v2 SDF range (m)", Float) = 1
        _GroundSize ("v2 paint size x, z (m)", Vector) = (192, 228, 0, 0)
        _EdgeIO ("v2 road in, road out, dash in, dash out (m)", Vector) = (0.16, 0.12, 0.07, 0.1)
        _CrispPx ("v2 crisp full / off at pixel size (m)", Vector) = (0.05, 0.1, 0, 0)
        _MarkIO ("lights_roads_v1 road markings: paint read in, out (m)", Vector) = (0.08, 0.1, 0, 0)
        _PlateBevel ("v2 L17 plate light, dark, rivet, inset", Vector) = (1.08, 0.74, 0.12, 0.08)
        [NoScaleOffset] _DetailTex ("v2 detail (R asphalt, G dirt, B sand, A cracks)", 2D) = "gray" {}
        [NoScaleOffset] _DetailNrm ("v2 detail normals (RG asphalt, BA dirt)", 2D) = "gray" {}
        _DetailOn ("v2 detail amount", Range(0,1)) = 0
        _DetailTiles ("v2 detail tile, 2nd tile (m)", Vector) = (4, 9.7, 0, 0)
        _DetailStr ("v2 asphalt, dirt, sand, cracks", Vector) = (0.55, 0.5, 0.4, 0.4)
        _DetailNormalStr ("v2 detail normal strength", Range(0,2)) = 1
        _DetailFade ("v2 detail full to (m), min at (m), min amount", Vector) = (110, 260, 0.35, 0)
        [HideInInspector] _OzCraterRole ("Deformable terrain: 0 none, 1 ground cut under craters (MPB on TER_Ground), 2 crater patch", Float) = 0
        [HideInInspector][NoScaleOffset] _OzCraterDecal ("Deformable terrain: crater decal (UV1)", 2D) = "gray" {}
        [HideInInspector] _OzCraterDecalMul ("Deformable terrain: decal is 2x multiply (linear) instead of straight alpha", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half _Smoothness;
            half4 _SandColor; half _SandRef; half _Grade; half _Blur; half _BlurLod;
            half _RippleLarge; half _RippleSmall; float _SwirlScale; float _RippleScale; float _WindAngle;
            half4 _ShadowTint; half _ShadowLift;
            float4 _GroundSdf_TexelSize; half _SdfMode; float _SdfRange; float4 _GroundSize; float4 _EdgeIO; float4 _CrispPx; float4 _MarkIO;
            half4 _PlateBevel; half _DetailOn; float4 _DetailTiles; half4 _DetailStr; half _DetailNormalStr; float4 _DetailFade;
            half _OzCraterRole; half _OzCraterDecalMul;
        CBUFFER_END
        float4 _OzCraters[12]; float _OzCraterCount;
        void OZCraterClip(float3 positionWS)
        {   // role 1 (the rendered ground): no pixel inside a crater; its patch draws the bowl there. Every pass, so depth / SSAO agree.
            if (_OzCraterRole < 0.5 || _OzCraterRole > 1.5 || _OzCraterCount < 0.5) return;
            int n = (int)min(_OzCraterCount, 12.0);
            [loop] for (int k = 0; k < n; k++)
            {
                float2 d = positionWS.xz - _OzCraters[k].xy;
                if (dot(d, d) < _OzCraters[k].z * _OzCraters[k].z) clip(-1);
            }
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma shader_feature_local_fragment _ _OZ_NO_REFL_SPEC   // textures_v2: the L17 ground keeps URP Lit's old switches
            #if defined(_OZ_NO_REFL_SPEC)
                #define _ENVIRONMENTREFLECTIONS_OFF
                #define _SPECULARHIGHLIGHTS_OFF
            #endif
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OpenZNightV2.hlsl"                                            // night_v2: day-sky reflection fix
            #include "OpenZWet.hlsl"                                                // weather_l17_v1: rain wetness (globals; 0 = dry = unchanged)
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_GroundSdf); SAMPLER(sampler_GroundSdf);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
            TEXTURE2D(_DetailNrm); SAMPLER(sampler_DetailNrm);
            TEXTURE2D(_OzCraterDecal); SAMPLER(sampler_OzCraterDecal);

            // raw SDF texel (0.5 = edge): x = road, y = dash (mode 1) or plate weight 0..1 (mode 2), z = road markings (mode 3)
            float3 OZSdf(float2 uv) { return SAMPLE_TEXTURE2D_LOD(_GroundSdf, sampler_GroundSdf, uv, 0).rgb; }

            // the paint just outside its soft edge on each side of a feature (d = signed distance, m, + inside; dirM = unit
            // direction of growing d in metre space along (u, v)), blended by the pixel's analytic coverage
            half3 OZEdge(float2 uv, float2 duvx, float2 duvy, float d, float2 dirM, float dIn, float dOut, float aa)
            {
                float2 uvm = dirM / _GroundSize.xy;
                float tin = max(d, dIn) - d, tout = max(-d, dOut) + d;
                half3 cIn = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv + uvm * tin, duvx, duvy).rgb;
                half3 cOut = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv - uvm * tout, duvx, duvy).rgb;
                return lerp(cOut, cIn, (half)saturate(d / aa + 0.5));
            }

            // seam line and 7 cm dark after it, 4 rivets per plate (lit dome + offset shadow). The paint is read away from the
            // seams (inset) and bevels / rivets are drawn analytically on top, anti-aliased by the pixel footprint.
            half3 OZPlates(float2 uv, float2 duvx, float2 duvy, float3 positionWS, float3 dpx, float3 dpy, float pxm)
            {
                float2 f = frac(float2(positionWS.x, -positionWS.z) * 0.5);
                float2 dm = (clamp(f, _PlateBevel.w, 1.0 - _PlateBevel.w) - f) * 2.0;   // metres along (+x, -z)
                // uv per world metre from the screen derivatives (planar UV of any orientation)
                float2x2 muv = float2x2(duvx.x, duvy.x, duvx.y, duvy.y);
                float det = dpx.x * dpy.z - dpy.x * dpx.z;
                float2 duv = 0;
                if (abs(det) > 1e-12)
                    duv = mul(mul(muv, float2x2(dpy.z, -dpy.x, -dpx.z, dpx.x) / det), float2(dm.x, -dm.y));
                half3 base = SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, uv + duv, duvx, duvy).rgb;
                float pf = max(pxm * 0.5, 1e-4);                                        // pixel footprint in plate units
                float2 s = min(f, 1.0 - f);
                float2 cov = saturate((0.035 - s) / pf + 0.5);
                float2 low = step(f, 0.5);
                half light = (half)max(cov.x * low.x, cov.y * low.y);
                half dark = (half)max(cov.x * (1 - low.x), cov.y * (1 - low.y));
                float2 r = min(abs(f - 0.14), abs(f - 0.86)), r2 = min(abs(f - 0.155), abs(f - 0.875));
                half riv = (half)saturate(1 - length(r) / 0.028), sh = (half)saturate(1 - length(r2) / 0.03);
                half3 cs = LinearToSRGB(base);                                           // the paint's recipe is in sRGB
                cs *= lerp((half)1, (half)_PlateBevel.x, light) * lerp((half)1, (half)_PlateBevel.y, dark) * (1 - (half)0.25 * sh);
                cs += (half)_PlateBevel.z * riv;
                return SRGBToLinear(cs);
            }

            // crisp pass: sharp road / dash / plate edges while a pixel covers <= _CrispPx.x m (fading out by _CrispPx.y m)
            half3 OZCrisp(float2 uv, float2 duvx, float2 duvy, float3 positionWS, float3 dpx, float3 dpy, float pxm, half3 paint)
            {
                half k = (half)saturate((_CrispPx.y - pxm) / max(_CrispPx.y - _CrispPx.x, 1e-4));
                if (_SdfMode < 0.5 || k <= 0) return paint;
                float aa = max(pxm, 1e-4) * 0.75;
                float2 t = _GroundSdf_TexelSize.xy;
                float3 raw3 = OZSdf(uv);
                float3 ru3 = OZSdf(uv + float2(t.x, 0)) - OZSdf(uv - float2(t.x, 0));
                float3 rv3 = OZSdf(uv + float2(0, t.y)) - OZSdf(uv - float2(0, t.y));
                float2 raw = raw3.xy, ru = ru3.xy, rv = rv3.xy;
                float2 d = (raw - 0.5) * (2.0 * _SdfRange);                             // metres, + inside
                float2 gR = float2(ru.x / (t.x * _GroundSize.x), rv.x / (t.y * _GroundSize.y));   // metre-space gradients
                float2 gD = float2(ru.y / (t.x * _GroundSize.x), rv.y / (t.y * _GroundSize.y));
                gR *= rsqrt(max(dot(gR, gR), 1e-12)); gD *= rsqrt(max(dot(gD, gD), 1e-12));
                half3 c = paint;
                bool dashes = _SdfMode < 1.5;
                if (dashes && abs(d.y) < abs(d.x) && abs(d.y) < max(_EdgeIO.z, _EdgeIO.w) + aa)
                    c = OZEdge(uv, duvx, duvy, d.y, gD, _EdgeIO.z, _EdgeIO.w, aa);
                else if (abs(d.x) < max(_EdgeIO.x, _EdgeIO.y) + aa)
                    c = OZEdge(uv, duvx, duvy, d.x, gR, _EdgeIO.x, _EdgeIO.y, aa);
                if (!dashes && raw.y > 0.02)
                    c = lerp(c, OZPlates(uv, duvx, duvy, positionWS, dpx, dpy, pxm), (half)(saturate(raw.y * 1.25) * saturate(-d.x / aa + 0.5)));
                if (_SdfMode > 2.5)
                {   // lights_roads_v1: L17 road markings (B = their signed distance): the painted lines keep their colour and wear,
                    // only their edges get sharp (same recipe as the road edges; last, so a line over an abutment plate wins)
                    float dm = (raw3.z - 0.5) * (2.0 * _SdfRange);
                    if (abs(dm) < max(_MarkIO.x, _MarkIO.y) + aa)
                    {
                        float2 gM = float2(ru3.z / (t.x * _GroundSize.x), rv3.z / (t.y * _GroundSize.y));
                        gM *= rsqrt(max(dot(gM, gM), 1e-12));
                        c = OZEdge(uv, duvx, duvy, dm, gM, _MarkIO.x, _MarkIO.y, aa);
                    }
                }
                return lerp(paint, c, k);
            }

            // detail: world-space asphalt / dirt / sand (a 4 m tile + a 9.7 m tile turned 37 deg against visible repeats);
            // classes from the paint colour (grey = asphalt / concrete, light warm = dune sand, else dirt); fades toward
            // _DetailFade.z beyond _DetailFade.x m. Multiplies albedo, returns the world-space normal offset (x, z).
            half2 OZDetail(float3 positionWS, half3 classCol, inout half3 albedo)
            {
                float dist = distance(positionWS, _WorldSpaceCameraPos);
                half fade = (half)(lerp(1.0, _DetailFade.z, smoothstep(_DetailFade.x, _DetailFade.y, dist)) * _DetailOn);
                const float ca = 0.79864, sa = 0.60181;
                float2 w = positionWS.xz;
                float2 uv1 = w / _DetailTiles.x;
                float2 uv2 = float2(ca * w.x - sa * w.y, sa * w.x + ca * w.y) / _DetailTiles.y;
                half4 d1 = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, uv1);
                half4 d2 = SAMPLE_TEXTURE2D(_DetailTex, sampler_DetailTex, uv2);
                half4 n1 = SAMPLE_TEXTURE2D(_DetailNrm, sampler_DetailNrm, uv1) * 2 - 1;
                half4 n2 = SAMPLE_TEXTURE2D(_DetailNrm, sampler_DetailNrm, uv2) * 2 - 1;
                half mx = max(classCol.r, max(classCol.g, classCol.b)), mn = min(classCol.r, min(classCol.g, classCol.b));
                half sat = (mx - mn) / max(mx, (half)1e-3);
                half roadW = 1 - smoothstep((half)0.12, (half)0.28, sat);
                half sandW = smoothstep((half)0.58, (half)0.68, mx) * (1 - roadW);
                half dirtW = 1 - roadW - sandW;
                half4 m = (d1 - 0.5) * 1.5 + (d2 - 0.5) * 0.9;
                half mod = 1 + fade * (roadW * _DetailStr.x * m.r + dirtW * _DetailStr.y * m.g + sandW * _DetailStr.z * m.b);
                half crack = max(d1.a, (half)0.6 * d2.a) * roadW * _DetailStr.w * fade;
                albedo *= max(mod, (half)0) * (1 - crack);
                half2 a = n1.rg + (half)0.6 * half2(ca * n2.r + sa * n2.g, -sa * n2.r + ca * n2.g);
                half2 b = n1.ba + (half)0.6 * half2(ca * n2.b + sa * n2.a, -sa * n2.b + ca * n2.a);
                return lerp(b * (half)0.6, a, roadW) * (half)(_DetailNormalStr * fade);
            }

            float3 OZDetailNormal(float3 n, half2 o)
            {   // world-space offset (x, z) on the ground normal; the tangents follow the terrain slope
                float3 t = normalize(float3(1, 0, 0) - n * n.x), b = normalize(float3(0, 0, 1) - n * n.z);
                return normalize(n + t * o.x + b * o.y);
            }

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; half fog : TEXCOORD3; float2 uv1 : TEXCOORD4; };

            V vert(A i)
            {
                V o; VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.fog = ComputeFogFactor(p.positionCS.z);
                o.uv1 = i.uv1;
                return o;
            }

            float hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash21(i), hash21(i + float2(1, 0)), f.x), lerp(hash21(i + float2(0, 1)), hash21(i + float2(1, 1)), f.x), f.y);
            }
            half3 RgbToHsv(half3 c)
            {
                half4 K = half4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                half4 p = lerp(half4(c.bg, K.wz), half4(c.gb, K.xy), step(c.b, c.g));
                half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
                half d = q.x - min(q.w, q.y); half e = 1.0e-4;
                return half3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }

            half4 frag(V i) : SV_Target
            {
                OZCraterClip(i.positionWS);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                float2 ozDuvx = ddx(i.uv), ozDuvy = ddy(i.uv);                                 // textures_v2
                float3 ozDpx = ddx(i.positionWS), ozDpy = ddy(i.positionWS);
                float ozPxm = max(length(ozDpx.xz), length(ozDpy.xz));                       // ground metres per pixel
                tex.rgb = OZCrisp(i.uv, ozDuvx, ozDuvy, i.positionWS, ozDpx, ozDpy, ozPxm, tex.rgb);
                half4 soft = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, i.uv, _BlurLod);
                half3 hsv = RgbToHsv(soft.rgb);
                // sand = warm hue, some saturation, not dark (roads are greyer, water edges darker, dirt browner)
                half sand = smoothstep(0.03, 0.07, hsv.x) * (1 - smoothstep(0.15, 0.2, hsv.x)) * smoothstep(0.18, 0.32, hsv.y) * smoothstep(0.25, 0.45, hsv.z);
                half3 albedo = lerp(tex.rgb, soft.rgb, _Blur * sand) * _BaseColor.rgb;
                half v = RgbToHsv(albedo).z;
                half3 graded = _SandColor.rgb * clamp(v / _SandRef, 0.82, 1.12);
                albedo = lerp(albedo, graded, sand * _Grade);

                // wind ripples: domain-warped bands, light crests + soft troughs (Tanks! / low-poly desert look)
                float a = radians(_WindAngle); float2 dir = float2(cos(a), sin(a)), dir2 = float2(cos(a + 0.35), sin(a + 0.35));
                float2 w = i.positionWS.xz;
                float warp = vnoise(w / (_SwirlScale * 2.2)) * 7.0 + vnoise(w / (_SwirlScale * 0.8)) * 2.0;
                float big = sin(dot(w, dir) * 6.2832 / _SwirlScale + warp);
                float crest = smoothstep(0.35, 0.95, big) - 0.7 * smoothstep(0.2, 0.9, -big) * smoothstep(0.1, 0.6, vnoise(w / 9.0));
                float small = sin(dot(w, dir2) * 6.2832 / _RippleScale + warp * 3.0 + vnoise(w * 0.7) * 2.0);
                albedo *= 1 + sand * (crest * _RippleLarge + small * _RippleSmall);
                half2 ozNrm = OZDetail(i.positionWS, tex.rgb, albedo);                       // textures_v2 detail
                if (_OzCraterRole > 1.5)
                {
                    half4 dc = SAMPLE_TEXTURE2D(_OzCraterDecal, sampler_OzCraterDecal, i.uv1);
                    albedo = _OzCraterDecalMul > 0.5 ? albedo * lerp((half3)1, dc.rgb * 2, dc.a) : lerp(albedo, dc.rgb, dc.a);
                }
                half ozWetP = 0;                                                             // weather_l17_v1: damp ground + puddles (OpenZWet.hlsl)
                if (_OzWetness > 0.001) albedo = OZWetAlbedo(i.positionWS.xz, albedo, (half)normalize(i.normalWS).y, ozWetP);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.alpha = 1; s.metallic = 0; s.smoothness = _Smoothness; s.occlusion = 1; s.normalTS = half3(0, 0, 1);
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS; inputData.positionCS = i.positionCS;
                inputData.normalWS = NormalizeNormalPerPixel(i.normalWS);
                inputData.normalWS = OZDetailNormal(inputData.normalWS, ozNrm);                 // textures_v2 detail normal
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                inputData.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                inputData.fogCoord = i.fog; inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);
                half4 c = UniversalFragmentPBR(inputData, s);
                c.rgb = OZNightEnvFix(c.rgb, inputData, s, 0);                          // night_v2: ground = environment class
                // soft warm shadows: shadowed sand keeps a warm bounce instead of going grey
                Light ml = GetMainLight(inputData.shadowCoord);
                half ndl = saturate(dot(inputData.normalWS, ml.direction));
                c.rgb += albedo * _ShadowTint.rgb * ml.color * ndl * (1 - ml.shadowAttenuation) * _ShadowLift;
                // fog factor per pixel (the interpolated vertex factor darkened the whole floor on this 40k-triangle ground mesh)
                if (_OzWetness > 0.001)                                                      // weather_l17_v1: wet sheen, puddle reflections, drop ripples
                    c.rgb += OZWetSheen(i.positionWS, (half3)inputData.normalWS, (half3)inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV, inputData.shadowCoord, ozWetP, ozPxm);
                c.rgb = MixFog(c.rgb, ComputeFogFactor(TransformWorldToHClip(i.positionWS).z));
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }
        // (no shadow caster: the ground receives shadows only; a borrowed caster self-shadowed the whole floor)
        // so the SSAO prepass sees the bowl, not the cut-away flat ground
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            half frag(Varyings i) : SV_Target { OZCraterClip(i.positionWS); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { OZCraterClip(i.positionWS); return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
