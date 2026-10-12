// OpenZ/FlagCloth: every OpenZ flag (zone flags + both fort flags) and its pole, one material, SRP Batcher compatible.
//
// Cloth = a flat grid mesh built by FlagView (x = 0 at the pole .. W at the fly end, y = -H .. 0, z = 0); the wave runs on
// at the pole, weight grows to the fly end) plus a second ripple, a fast tip flutter and a calm-air droop.
//   TEXCOORD0 = (u 0 pole .. 1 fly end, v 0 bottom .. 1 top);  u < 0 marks rigid pole / finial vertices (no wave)
//   TEXCOORD1 = (cloth width, cloth height in metres, wave phase seed in radians, 0) (per mesh)
//   COLOR     = owner colour (LINEAR rgb, set by FlagView) + a = energy: 0..1 boosts the flutter; only the part above 0.3
//               adds the capture glow (a = 1 at the raise), so drops, fort flinches and dips flutter without glowing
//   _OZFlagWind (global, FlagView) = (strength 0.25-1.6, gust 0-1, wave phase in radians (wraps at 2 pi), time)
// The flag's own phase comes from its mesh (no per-renderer properties -> no MaterialPropertyBlock, the SRP
// Batcher batches every flag and pole). Two-sided: Cull Off, the normal is flipped toward the viewer, soft wrap light,
// sun shining through the cloth from behind (translucency), clean stylized shading like OpenZ/TeamLit.
// never shimmers at RTS distance), darker double hem with its fold line, dashed stitched border (free edges + hoist tape),
// crease shade / crest sheen from the wave normal, translucency toward the sun blocked by double layers, and the team
// emblem read from the owner vertex colour: red = five-point star, blue = double chevrons (light applique with a stitched
// edge), neutral = plain warm grey cloth (no emblem). _EmblemMode 1 / 2 forces star / chevrons for both, 3 = off.
Shader "OpenZ/FlagCloth"
{
    Properties
    {
        _BaseMap ("Cloth pattern (multiplies the owner colour, white = plain)", 2D) = "white" {}
        _Smoothness ("Cloth smoothness", Range(0,1)) = 0.16
        _PoleSmoothness ("Pole smoothness", Range(0,1)) = 0.45
        _BrassSmoothness ("Finial smoothness", Range(0,1)) = 0.7
        _PoleMetallic ("Pole / finial metallic", Range(0,1)) = 0.35
        _WaveScale ("Wave amplitude scale", Range(0,3)) = 1
        _WaveLength ("Wave length (cloth widths)", Range(0.3,3)) = 1.15
        _Droop ("Droop in calm air (radians at the fly end)", Range(0,1.2)) = 0.55
        _Translucency ("Sun through the cloth", Range(0,2)) = 0.55
        _FoldShade ("Fold shading", Range(0,1)) = 0.3
        _HemWidth ("Hem width (m)", Range(0,0.2)) = 0.05
        _HemShade ("Hem shade", Range(0,1)) = 0.78
        _HeaderWidth ("Hoist header tape width (m)", Range(0,0.4)) = 0.09
        _HeaderColor ("Hoist header tape", Color) = (0.86, 0.84, 0.78, 1)
        _CaptureGlow ("Capture glow gain", Range(0,4)) = 1.3
        _SoftFill ("Soft wrap fill", Range(0,1.5)) = 0.6
        _AmbientBoost ("Ambient boost", Range(1,3)) = 1.3
        _KeyWarm ("Warm key tint", Color) = (1.06, 1.0, 0.92, 1)
        [HDR] _RimColor ("Rim light", Color) = (0.45, 0.42, 0.36, 1)
        _RimPower ("Rim power", Range(1,8)) = 3.5
        // VX70 fabric
        _WeavePitch ("Weave: thread pitch (m)", Range(0.004,0.05)) = 0.014
        _WeaveAmt ("Weave: thread contrast", Range(0,0.5)) = 0.16
        _SlubAmt ("Weave: yarn slubs + mottling", Range(0,0.25)) = 0.06
        _HemLine ("Hem fold line darkness", Range(0,1)) = 0.35
        _StitchInset ("Stitches: inset from the edge (m)", Range(0,0.1)) = 0.028
        _StitchLength ("Stitches: period (m)", Range(0.005,0.08)) = 0.024
        _StitchWidth ("Stitches: thread width (m)", Range(0.002,0.02)) = 0.006
        _StitchColor ("Stitches: thread colour (team cloth blends toward it)", Color) = (0.92, 0.88, 0.78, 1)
        _StitchAmt ("Stitches: strength", Range(0,1)) = 0.6
        _CreaseShade ("Fold crease shade", Range(0,1)) = 0.25
        _FoldSheen ("Fold crest sheen", Range(0,0.5)) = 0.1
        _EmblemMode ("Emblem: 0 auto (red star, blue chevrons), 1 star, 2 chevrons, 3 off", Range(0,3)) = 0
        _EmblemSize ("Emblem radius (fraction of the cloth height)", Range(0.1,0.5)) = 0.3
        _EmblemColor ("Emblem applique cloth", Color) = (0.93, 0.89, 0.8, 1)
        _EmblemTint ("Emblem: team cloth -> applique colour", Range(0,1)) = 0.8
        _EmblemOffset ("Emblem centre: shift toward the pole (fraction of the field)", Range(-0.3,0.3)) = 0.04
        _TransView ("Translucency glow looking toward the sun", Range(0,2)) = 0.6
        _LayerBlock ("Double layers (hem, tape, emblem) block the light", Range(0,1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // SRP Batcher: every material property in one UnityPerMaterial buffer, identical in all passes
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _Smoothness; half _PoleSmoothness; half _BrassSmoothness; half _PoleMetallic;
            half _WaveScale; half _WaveLength; half _Droop; half _Translucency; half _FoldShade;
            half _HemWidth; half _HemShade; half _HeaderWidth; half4 _HeaderColor; half _CaptureGlow;
            half _SoftFill; half _AmbientBoost; half4 _KeyWarm; half4 _RimColor; half _RimPower;
            half _WeavePitch; half _WeaveAmt; half _SlubAmt; half _HemLine; half _StitchInset; half _StitchLength;
            half _StitchWidth; half4 _StitchColor; half _StitchAmt; half _CreaseShade; half _FoldSheen; half _EmblemMode;
            half _EmblemSize; half4 _EmblemColor; half _EmblemTint; half _EmblemOffset; half _TransView; half _LayerBlock;
        CBUFFER_END

        // global (Shader.SetGlobalVector in FlagView), outside the per-material buffer
        float4 _OZFlagWind;

        // ---- VX70 fabric helpers (cloth metres; only the ForwardLit pass calls them)
        float FabHash(float2 p) { p = frac(p * float2(0.1031, 0.1030)); p += dot(p, p.yx + 33.33); return frac((p.x + p.y) * p.x); }
        float FabNoise(float2 p)                                    // smooth value noise, 0..1
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
            return lerp(lerp(FabHash(i), FabHash(i + float2(1, 0)), f.x), lerp(FabHash(i + float2(0, 1)), FabHash(i + float2(1, 1)), f.x), f.y);
        }
        // five-point star, point up (Inigo Quilez sdStar5): r outer radius, rf inner / outer ratio; < 0 inside
        float FabStar(float2 p, float r, float rf)
        {
            const float2 k1 = float2(0.809016994375, -0.587785252292);
            const float2 k2 = float2(-k1.x, k1.y);
            p.x = abs(p.x);
            p -= 2.0 * max(dot(k1, p), 0.0) * k1;
            p -= 2.0 * max(dot(k2, p), 0.0) * k2;
            p.x = abs(p.x);
            p.y -= r;
            float2 ba = rf * float2(-k1.y, k1.x) - float2(0, 1);
            float h = clamp(dot(p, ba) / dot(ba, ba), 0.0, r);
            return length(p - ba * h) * sign(p.y * ba.x - p.x * ba.y);
        }
        // two chevrons pointing up (rank stripes), in units of the emblem radius; < 0 inside
        float FabChevrons(float2 p)
        {
            const float slope = 0.8, nrm = 0.780869;               // 1 / sqrt(1 + slope^2)
            float ax = abs(p.x);
            float q = p.y + ax * slope;                             // 0 on the chevron axis through the apex
            float d1 = abs(q - 0.52) * nrm - 0.17;
            float d2 = abs(q + 0.08) * nrm - 0.17;
            float d = min(d1, d2);
            return max(d, ax - 0.86);                               // arm length
        }

        // Moves one cloth vertex (object space) and returns its analytic normal (object space, +z side) and the fold value.
        // Pole / finial vertices (uv.x < 0) stay rigid and keep the mesh normal.
        bool FlagDeform(inout float3 p, inout float3 n, out float fold, float2 uv, float4 sizeSeed, float energy)
        {
            fold = 0;
            if (uv.x < 0) return false;
            float W = max(sizeSeed.x, 0.05), H = max(sizeSeed.y, 0.05);
            float s = _OZFlagWind.x, g = _OZFlagWind.y, t = _OZFlagWind.z;
            float seed = sizeSeed.z;                             // per-flag phase (FlagView, constant per mesh)
            float u = saturate(uv.x), v = saturate(uv.y);

            float w = u * (0.35 + 0.65 * u);                     // pinned at the pole, free at the fly end
            float dw = (0.35 + 1.3 * u) / W;                     // d w / d x
            float k = 6.2831853 / (W * _WaveLength);             // wave number (rad per metre)
            // integer multiples of the wrapped phase t keep the motion continuous when FlagView wraps it at 2 pi
            float ph1 = k * p.x - t + seed + v * 0.9;
            float ph2 = 2.3 * k * p.x - 2.0 * t + seed * 1.7 + v * 2.6;
            float ph3 = 5.1 * k * p.x - 3.0 * t + seed * 2.3 + v * 5.0;
            float amp = W * (0.035 + 0.045 * s + 0.03 * g) * (1.0 + 1.4 * energy) * _WaveScale;
            float u3 = u * u * u, u4 = u3 * u;
            float tipAmp = 0.16 * (0.4 + 0.6 * saturate(s + energy));

            float S = sin(ph1) + 0.32 * sin(ph2);
            float z = amp * (w * S + tipAmp * u4 * sin(ph3));
            float dzdx = amp * (dw * S + w * (k * cos(ph1) + 0.32 * 2.3 * k * cos(ph2))
                              + tipAmp * (4.0 * u3 / W * sin(ph3) + u4 * 5.1 * k * cos(ph3)));
            float dzdy = amp * (w * (0.9 * cos(ph1) + 0.32 * 2.6 * cos(ph2)) / H + tipAmp * u4 * 5.0 * cos(ph3) / H);

            p.z += z;
            p.y += amp * 0.12 * w * sin(ph1 + 0.7);             // the free edge lifts a little on each wave
            p.x -= 0.18 * abs(z) * u;                            // cloth does not stretch: the fly end pulls in on big waves
            n = normalize(float3(-dzdx, -dzdy, 1.0));

            // calm air: the cloth hangs from the pole (rotation about the top hoist corner, more toward the fly end)
            float droop = _Droop * saturate(1.0 - 0.75 * (s + 0.3 * g)) * (0.35 + 0.65 * u) * (1.0 - 0.5 * energy);
            float cs = cos(droop), sn = sin(droop);
            p.xy = float2(p.x * cs + p.y * sn, -p.x * sn + p.y * cs);
            n.xy = float2(n.x * cs + n.y * sn, -n.x * sn + n.y * cs);
            fold = w * S;
            return true;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Off
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OpenZNightV2.hlsl"                                            // night_v2: day-sky reflection fix

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 size : TEXCOORD1;
                float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float4 uvSize : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; float4 color : TEXCOORD3; float2 foldFog : TEXCOORD4;
                float slope : TEXCOORD5;                                // VX70: 1 - |n.z| of the waved cloth (creases)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 p = i.positionOS.xyz; float3 n = i.normalOS; float fold;
                FlagDeform(p, n, fold, i.uv, i.size, i.color.a);
                VertexPositionInputs vp = GetVertexPositionInputs(p);
                o.positionCS = vp.positionCS; o.positionWS = vp.positionWS;
                o.normalWS = TransformObjectToWorldNormal(n);
                o.uvSize = float4(i.uv, i.size.xy); o.color = i.color;
                o.foldFog = float2(fold, ComputeFogFactor(vp.positionCS.z));
                o.slope = 1.0 - abs(normalize(n).z);                   // the droop turns about z: n.z is the wave's own tilt
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                bool isCloth = i.uvSize.x >= 0;
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 N = normalize(i.normalWS);
                half facing = dot(N, V) < 0 ? -1.0h : 1.0h;          // two-sided: light the side we look at
                N *= facing;

                half3 albedo = i.color.rgb;                           // linear owner colour (cloth) / paint (pole)
                half occlusion = 1, smooth = _Smoothness, metal = 0;
                half layers = 0;                                          // VX70: double cloth layers (hem, tape, emblem)
                half3 transAlb = albedo;
                if (isCloth)
                {
                    float2 uv = saturate(i.uvSize.xy), size = max(i.uvSize.zw, 0.05);
                    half3 team = albedo;                                                          // owner colour (linear)
                    albedo *= SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv, _BaseMap)).rgb;
                    float xm = uv.x * size.x;                                                      // metres from the pole
                    float ym = uv.y * size.y;                                                      // metres from the bottom
                    float flyD = (1 - uv.x) * size.x;
                    float edge = min(min(flyD, ym), size.y - ym);

                    // woven cloth: plain weave (warp / weft alternate over-under), yarn slubs along the weft, soft mottling.
                    // Everything below the pixel size fades to its mean (no shimmer at the RTS camera distance).
                    float2 tq = float2(xm, ym) / _WeavePitch;
                    float2 fwt = fwidth(tq);
                    half aaW = saturate(1.6 - max(fwt.x, fwt.y) * 1.1);
                    float2 fq = frac(tq);
                    half over = fmod(floor(tq.x) + floor(tq.y), 2.0);
                    half prof = over < 0.5 ? sin(3.14159 * fq.x) : sin(3.14159 * fq.y);
                    half weave = (prof - 0.6366) * aaW;                                            // zero mean
                    half slub = (FabHash(float2(floor(tq.y), 7.3)) - 0.5) * aaW
                              + (FabNoise(float2(xm * 3.1, ym * 9.0)) - 0.5) * saturate(1.4 - max(fwt.x, fwt.y) * 0.25) * 0.7
                              + (FabNoise(float2(xm, ym) * 1.7 + 11.0) - 0.5) * 0.6;
                    albedo *= 1.0 + _WeaveAmt * weave + _SlubAmt * slub;

                    // team (from the owner colour FlagView writes): red / blue get the emblem, neutral grey stays plain
                    half isRed = step(0.12, team.r) * step(team.b * 2.5, team.r);
                    half isBlue = step(0.12, team.b) * step(team.r * 2.5, team.b);
                    half teamK = saturate(isRed + isBlue);
                    int mode = (int)round(_EmblemMode);
                    // (both shapes evaluated outside flow control: fwidth needs uniform control flow)
                    float fieldW = size.x - _HeaderWidth;
                    float2 ctr = float2(_HeaderWidth + fieldW * (0.5 - _EmblemOffset), size.y * 0.5);
                    float R = _EmblemSize * size.y;
                    float2 p = (float2(xm, ym) - ctr) / R;
                    bool star = mode == 1 || (mode == 0 && isRed > 0.5);
                    float d = (star ? FabStar(p + float2(0, 0.06), 0.98, 0.45) : FabChevrons(p)) * R;       // metres
                    float fwd = max(fwidth(d), 1e-4);
                    half show = (mode != 3 && teamK > 0.5) ? 1.0h : 0.0h;
                    half emb = saturate(0.5 - d / fwd) * show;
                    half embEdge = saturate(1 - abs(d + 0.006) / (fwd + 0.004)) * saturate(0.012 / fwd) * show;   // applique stitch edge
                    half3 embCol = lerp(team, _EmblemColor.rgb, _EmblemTint) * (1.0 + _WeaveAmt * 0.5 * weave);
                    albedo = lerp(albedo, embCol, emb);
                    albedo *= 1 - 0.28 * embEdge;

                    // darker double hem on the three free edges, with the fold line where the turned-in cloth ends
                    half hem = 1 - smoothstep(_HemWidth * 0.55, _HemWidth, edge);
                    albedo *= lerp(1.0h, _HemShade, hem);
                    float fwE = max(fwidth(edge), 1e-4);
                    half hemLine = saturate(1 - abs(edge - _HemWidth) / (fwE * 1.2 + 0.004)) * saturate(0.008 / fwE);
                    albedo *= 1 - _HemLine * hemLine;

                    // stitched border: dashed running stitch along the free edges and along the hoist tape
                    float sd = abs(edge - _StitchInset);
                    float along = flyD < min(ym, size.y - ym) ? ym : xm;
                    float sdH = abs(xm - (_HeaderWidth + _StitchInset * 0.5));
                    if (sdH < sd) { sd = sdH; along = ym; }
                    float fwS = max(fwidth(sd), 1e-4);
                    half sline = saturate(0.5 + (_StitchWidth * 0.5 - sd) / fwS) * saturate(_StitchWidth / fwS);
                    float a = along / _StitchLength; float fwa = max(fwidth(a), 1e-4);
                    half fa = frac(a);
                    half dash = smoothstep(0.0, fwa, fa) * (1 - smoothstep(0.62 - fwa, 0.62, fa));
                    dash = lerp(0.62, dash, saturate(1.5 - fwa * 1.5));                           // sub-pixel: average duty
                    half3 thread = teamK > 0.5 ? lerp(team, _StitchColor.rgb, 0.6) : team * 0.7;
                    albedo = lerp(albedo, thread, sline * dash * _StitchAmt);

                    half header = 1 - smoothstep(_HeaderWidth - 0.012, _HeaderWidth + 0.012, xm);   // white hoist tape
                    albedo = lerp(albedo, _HeaderColor.rgb * (1.0 + _WeaveAmt * 0.6 * weave), header);   // Color props arrive linear
                    layers = saturate(hem + header + emb);

                    // folds: the side of a wave that turns away from the viewer is a little darker, steep creases get a soft
                    // shade, crests facing the viewer a faint cloth sheen
                    occlusion = 1 - _FoldShade * saturate(0.5 - 0.5 * facing * i.foldFog.x / 1.3);
                    occlusion *= 1 - _CreaseShade * saturate(i.slope * 2.5);
                    albedo *= 1 + _FoldSheen * saturate(facing * i.foldFog.x / 1.3);
                    transAlb = lerp(albedo, albedo * albedo / max(max(albedo.r, max(albedo.g, albedo.b)), 1e-3), 0.35);   // light through dye: deeper
                }
                else
                {
                    bool brass = i.uvSize.y > 0.5;
                    smooth = brass ? _BrassSmoothness : _PoleSmoothness; metal = _PoleMetallic;
                }

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.alpha = 1; s.metallic = metal; s.smoothness = smooth;
                s.occlusion = occlusion; s.normalTS = half3(0, 0, 1);
                half glowK = smoothstep(0.3h, 1.0h, i.color.a);                                   // energy <= 0.3 = flutter only
                s.emission = isCloth ? albedo * glowK * _CaptureGlow : half3(0, 0, 0);            // capture flash, blooms

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS;
                d.normalWS = N; d.viewDirectionWS = V;
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.foldFog.y;
                d.bakedGI = SampleSH(N);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = OZNightEnvFix(c.rgb, d, s, 0);                                  // night_v2: brass pole / cloth stop reflecting the day sky

                Light ml = GetMainLight(d.shadowCoord);
                half ndl = dot(N, ml.direction);
                half direct = saturate(ndl) * ml.shadowAttenuation;
                half fill = saturate(saturate((ndl + 0.6) / 1.6) - saturate(ndl));                 // soft terminator
                c.rgb += albedo * ml.color * fill * _SoftFill * lerp(0.55, 1.0, ml.shadowAttenuation) * occlusion;
                c.rgb += albedo * ml.color * direct * (_KeyWarm.rgb - 1);
                c.rgb += albedo * d.bakedGI * (_AmbientBoost - 1) * occlusion;
                if (isCloth)                                                                       // sun through the cloth
                {   // VX70: back light through a single layer, a glow when the viewer looks toward the sun through it
                    half back = saturate(dot(-N, ml.direction));
                    half toward = pow(saturate(dot(V, -ml.direction)), 4.0) * saturate(back + 0.3);
                    c.rgb += transAlb * ml.color * (back * _Translucency + toward * _TransView) * ml.shadowAttenuation
                           * (1 - _LayerBlock * layers);
                }
                half ndv = saturate(dot(N, V));
                half rim = pow(1 - ndv, _RimPower) * (0.35 + 0.65 * saturate(N.y * 0.5 + 0.5));
                half sunK = saturate(dot(ml.color, half3(0.2126, 0.7152, 0.0722)) / 1.6);
                c.rgb += _RimColor.rgb * rim * (0.35 + 0.65 * sunK);
                c.rgb = MixFog(c.rgb, i.foldFog.y);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 size : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i);
                float3 p = i.positionOS.xyz; float3 n = i.normalOS; float fold;
                FlagDeform(p, n, fold, i.uv, i.size, i.color.a);          // the shadow waves with the cloth
                float3 pw = TransformObjectToWorld(p);
                float3 nw = TransformObjectToWorldNormal(n);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - pw);
                #else
                    float3 ld = _LightDirection;
                #endif
                nw = dot(nw, ld) < 0 ? -nw : nw;                          // two-sided: bias away from the light side
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs; return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 size : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i);
                float3 p = i.positionOS.xyz; float3 n = i.normalOS; float fold;
                FlagDeform(p, n, fold, i.uv, i.size, i.color.a);
                o.positionCS = TransformObjectToHClip(p); return o;
            }
            half frag(Varyings i) : SV_Target { return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float4 size : TEXCOORD1; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i);
                float3 p = i.positionOS.xyz; float3 n = i.normalOS; float fold;
                FlagDeform(p, n, fold, i.uv, i.size, i.color.a);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(n);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float3 n = NormalizeNormalPerPixel(i.normalWS);
                n = dot(n, GetWorldSpaceNormalizeViewDir(i.positionWS)) < 0 ? -n : n;
                return half4(n, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
