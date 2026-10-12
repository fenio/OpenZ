// OpenZ/AtmosphereFullscreen: volumetric fog + heat shimmer, run by URP's Full Screen Pass renderer feature
// (injection: before post-processing, depth required, colour fetched into _BlitTexture). plan/04 section 4.1c.
// Raymarch (16 steps, interleaved-gradient jitter) through a height fog with scrolling 3D FBM noise, lit by the main
// light (Henyey-Greenstein phase + main light shadow map -> god rays), ambient, and up to 16 lamps (night fog glow).
// L17: low "mystery fog" (0-3 m) denser over water (world-space water mask). L02: dusty haze + midday heat shimmer.
// Readability: the fog can never hide more than (1 - _MinTransmittance) of what is behind it.
// All parameters are globals written by OpenZ.Atmosphere from the DayNight cycle and the level preset.
Shader "OpenZ/AtmosphereFullscreen"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "Atmosphere"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "FlybySky.hlsl"

            float _AtmoEnabled;
            float _AtmoSteps;
            float _AtmoKeepAlpha;
            float4 _FogColor;          // rgb albedo of the fog
            float4 _FogAmbient;        // rgb ambient light on the fog
            float _FogDensity;         // base extinction per metre at the ground plane of play
            float _FogHeightFalloff;   // metres: density halves every ~0.7*falloff above _FogBase
            float _FogBase;            // height (m) of the fog floor
            float _FogNoiseScale, _FogNoiseAmount;
            float4 _FogWind;           // xyz wind velocity (m/s)
            float _FogMaxDistance;
            float _FogSunScatter, _FogG;
            float _MinTransmittance;
            float4 _HorizonColor; float _HorizonStart, _HorizonEnd;
            float _LowFogDensity, _LowFogHeight, _LowFogWaterBoost;   // L17 mystery fog / L02 blue-hour river mist
            float _LowFogMode;
            float4 _LowFogBanks;       // x bank noise scale (1/m), y cover threshold, z threshold softness, w depth over water (m)
            float4 _LowFogShape;       // x density factor over land, y water value beyond the map edge, z cover bonus over water, w band step share
            float4 _LowFogBanks2;      // l17_night_fog_read: x bank drift (x wind), y detail octave share, z sky floor height (m, the depthless sea skirt), w detail scale
            float4 _FogMapRect;        // x0, z0, sizeX, sizeZ of the water mask in world metres
            TEXTURE2D(_FogWaterMask); SAMPLER(sampler_FogWaterMask);
            float4 _FogLamps[16];      // xyz position, w range
            float4 _FogLampColors[16];
            float _FogLampCount;
            float _AtmoLampCullOff;
            float _ShimmerAmount;      // heat shimmer strength (0 off)
            float4 _AtmoHole;          // nuke_fx_v3: xyz centre, w radius of the nuke fireball (0 = none), set by NukeFx

            float Hash3(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float Noise3(float3 x)
            {
                float3 i = floor(x); float3 f = frac(x); f = f * f * (3 - 2 * f);
                return lerp(lerp(lerp(Hash3(i + float3(0,0,0)), Hash3(i + float3(1,0,0)), f.x), lerp(Hash3(i + float3(0,1,0)), Hash3(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash3(i + float3(0,0,1)), Hash3(i + float3(1,0,1)), f.x), lerp(Hash3(i + float3(0,1,1)), Hash3(i + float3(1,1,1)), f.x), f.y), f.z);
            }
            float Fbm(float3 p) { return 0.55 * Noise3(p) + 0.3 * Noise3(p * 2.03 + 11.7) + 0.15 * Noise3(p * 4.1 + 3.1); }
            float Ign(float2 px) { return frac(52.9829189 * frac(dot(px, float2(0.06711056, 0.00583715)))); }
            float HG(float c, float g) { float g2 = g * g; return (1 - g2) / (12.566371 * pow(max(1e-3, 1 + g2 - 2 * g * c), 1.5)); }

            float WaterMask(float3 p)
            {
                float2 uv = (p.xz - _FogMapRect.xy) / _FogMapRect.zw;
                if (any(uv < 0) || any(uv > 1)) return 0;
                return SAMPLE_TEXTURE2D_LOD(_FogWaterMask, sampler_FogWaterMask, uv, 0).r;
            }

            // eases to the level's outside value over 40 m, so the banks never run out in clamped streaks or stop at the border.
            float2 WaterNear(float3 p)
            {
                float2 uv = (p.xz - _FogMapRect.xy) / _FogMapRect.zw;
                float2 m = SAMPLE_TEXTURE2D_LOD(_FogWaterMask, sampler_FogWaterMask, saturate(uv), 0).rg;
                float2 o = max(0, max(-uv, uv - 1)) * _FogMapRect.zw;
                return lerp(m, _LowFogShape.yy, saturate(length(o) / 40.0));
            }

            // clear gaps; the band is deep over water and thin over land; denser and more frequent near the water.
            // (h 55-70, pitch 55, a ~78 x 62 m view): (1) the 45 m noise lattice gave one soft blob per screen and the 0.14 softness
            // spanned the whole FBM range (std ~0.12): a smooth ramp, no gaps; (2) the 3D haze FBM multiplied the banks again (0.2-1.5x
            // at 28 m), smearing what was left; (3) over the car park the band was 1.6 m at 0.35x: ~nothing; (4) the uniform night haze
            // (0.0012) gave the frame its +18 % blue-grey lift with no structure; (5) the skirt (depthless sky pixels) marched 480 m
            // under the floor at full density: a hard bright band at the map border. Now: 2D banks (~22 m lattice, two drifting
            // octaves, the detail warped by the base), thick in their cores and thin at their edges, real banks over land too (under
            // unit height), integrated exactly per ray segment (no step grain), clear gaps; the haze no longer thickens at night.
            float Noise2(float2 x)
            {
                float2 i = floor(x); float2 f = frac(x); f = f * f * (3 - 2 * f);
                float a = Hash3(float3(i, 0)), b = Hash3(float3(i + float2(1, 0), 0)), c = Hash3(float3(i + float2(0, 1), 0)), d = Hash3(float3(i + 1, 0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // mean extinction of the banks over the ray segment whose heights run from yA to yB (p = the step's sample point, for the
            // 2D bank pattern). The height profile (1 - h / top)^2 is integrated exactly; under the floor down to the water (-0.35 m)
            // it is 1, below that 0 (only the depthless skirt reaches there).
            float LowBankStep(float3 p, float yA, float yB)
            {
                float lo_ = min(yA, yB), hi_ = max(yA, yB);
                if (lo_ - _FogBase >= max(_LowFogHeight, _LowFogBanks.w) || hi_ < _FogBase - 0.35) return 0;   // segment outside the band: no fetch, no noise
                float near = WaterNear(p).y;
                float2 w = _FogWind.xz * (_Time.y * _LowFogBanks.x * _LowFogBanks2.x);
                float2 xz = p.xz * _LowFogBanks.x;
                float n0 = Noise2(xz - w);
                float n1 = Noise2(xz * _LowFogBanks2.w - w * 1.7 + float2(-w.y, w.x) * 0.6 + n0 * 1.4 + 5.2);   // wisps slide over the banks
                float n = lerp(n0, n1, _LowFogBanks2.y);
                float lo = _LowFogBanks.y - _LowFogShape.z * near;
                float patch = smoothstep(lo, lo + _LowFogBanks.z, n);
                if (patch <= 0) return 0;
                float top = max(0.1, lerp(_LowFogHeight, _LowFogBanks.w, near) * (0.45 + 0.55 * patch));   // domed: thick cores, thin edges
                float ha = clamp(lo_ - _FogBase, 0, top), hb = clamp(hi_ - _FogBase, 0, top);
                float ua = 1 - ha / top, ub = 1 - hb / top;
                float below = max(0, min(hi_, _FogBase) - max(lo_, _FogBase - 0.35));
                float span = hi_ - lo_;
                float um = saturate(1 - (p.y - _FogBase) / top);
                float prof = span > 1e-3 ? (top * (ua * ua * ua - ub * ub * ub) / 3 + below) / span : um * um;
                return _LowFogDensity * prof * lerp(_LowFogShape.x, _LowFogWaterBoost, near) * patch;
            }

            // ray distance of the march position u (0..1) with the band split (sb > 0) or uniform
            float BandT(float u, float sb, float tBand, float maxD)
            {
                if (sb <= 0) return u * maxD;
                return u < 1 - sb ? u / (1 - sb) * tBand : tBand + (u - (1 - sb)) / sb * (maxD - tBand);
            }

            // where it can matter: the low-fog FBM only inside its height band, the main FBM only where there is any density
            float Density(float3 p)
            {
                float3 q = p * _FogNoiseScale + _FogWind.xyz * _Time.y * _FogNoiseScale;
                float haze = _FogDensity * exp(-max(0, p.y - _FogBase) / max(0.1, _FogHeightFalloff));
                float low = 0;
                if (_LowFogMode > 0.5) low = 0;                                                  // l17_night_fog_read: the L17 banks are integrated per segment in Frag (LowBankStep), not x the haze FBM
                else if (_LowFogDensity > 0)
                {
                    float hMask = saturate(1 - (p.y - _FogBase) / max(0.1, _LowFogHeight));
                    if (hMask > 0)
                    {
                        float w = WaterMask(p);
                        low = _LowFogDensity * hMask * hMask * lerp(1, _LowFogWaterBoost, w) * saturate(Fbm(q * 1.7 + 5.3) * 1.8 - 0.25);
                    }
                }
                float d = haze + low * (1 - 0.8 * _FlybySkyW);
                if (d < 1e-6) return 0;
                return d * lerp(1, Fbm(q) * 1.6, _FogNoiseAmount);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float rawDepth = SampleSceneDepth(uv);
                float3 posWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 cam = _WorldSpaceCameraPos;
                float3 dir = posWS - cam; float dist = length(dir); dir /= max(dist, 1e-4);
                #if UNITY_REVERSED_Z
                    bool sky = rawDepth <= 1e-6;
                #else
                    bool sky = rawDepth >= 1 - 1e-6;
                #endif
                float farDist = sky ? 0 : dist;          // the sky gradient already ends in the horizon colour
                if (sky) dist = _FogMaxDistance;
                // nuke_fx_v3: the transparent fireball writes no depth, so its pixels were fogged down to the ground behind it (a
                // grey veil over the glow). Inside the fireball sphere the fog runs only to its front surface.
                if (_AtmoHole.w > 0)
                {
                    float3 oc = cam - _AtmoHole.xyz; float bq = dot(oc, dir);
                    float hq = bq * bq - (dot(oc, oc) - _AtmoHole.w * _AtmoHole.w);
                    if (hq > 0) { float tn = -bq - sqrt(hq); if (tn > 0 && tn < dist) dist = tn; }
                }

                // heat shimmer: screen refraction near the hot ground, masked by distance
                // still: the pixel must lie within 0.3 m of the ground base and face up, and the displaced sample must too
                // (so a structure's pixels are never pulled into the ground's wobble, and vice versa).
                float2 uvc = uv;
                float3 nWS = normalize(cross(ddy(posWS), ddx(posWS)) + float3(0, 1e-7, 0));
                if (_ShimmerAmount > 0 && !sky)
                {
                    float m = saturate((dist - 15) / 60) * saturate(1 - (posWS.y - _FogBase - 0.05) / 0.25)
                            * saturate((posWS.y - _FogBase + 0.25) / 0.1)                      // not the water (-0.35 m)
                            * smoothstep(0.85, 0.97, abs(nWS.y));
                    if (m > 0)
                    {
                        float3 sp = float3(posWS.xz * 0.35, _Time.y * 1.7);
                        float2 off = float2(Noise3(sp) - 0.5, Noise3(sp + 7.3) - 0.5);
                        float2 uvd = uv + off * _ShimmerAmount * m * 0.006;
                        float3 pd = ComputeWorldSpacePosition(uvd, SampleSceneDepth(uvd), UNITY_MATRIX_I_VP);
                        if (pd.y - _FogBase < 0.3 && pd.y - _FogBase > -0.25) uvc = uvd;
                    }
                }
                half4 src4 = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uvc);
                half3 col = src4.rgb;
                half outA = _AtmoKeepAlpha > 0.5 ? src4.a : 1;
                if (_AtmoEnabled < 0.5) return half4(col, outA);

                int STEPS = _AtmoSteps >= 1 ? (int)_AtmoSteps : 16;
                float maxD = min(dist, _FogMaxDistance);
                // l17_night_fog_read: the L17 sea skirt writes no depth, so its pixels marched 480 m (under the floor) uncapped while the
                // map next to them marched ~80 m: a hard bright band at the map border, day and night. In gameplay those rays now end on
                // the skirt plane and take the readability cap like the ground; the flyby (W >= 0.25) keeps its tuned look (eased).
                float floorK = 0;
                if (_LowFogMode > 0.5 && sky && dir.y < -0.01)
                {
                    floorK = saturate(1 - _FlybySkyW * 4);
                    maxD = lerp(maxD, min(maxD, max(0, cam.y - _LowFogBanks2.z) / -dir.y), floorK);
                }
                bool banks = _LowFogMode > 0.5 && _LowFogDensity > 0;
                float stepLen = maxD / STEPS;
                // uniform steps put ~1 sample in it (why the old low fog never read). With banks on, the same 16 steps are split: a share sb
                // (>= _LowFogShape.w) inside the band, the rest above it. sb = 0: the original uniform march, bit for bit.
                float sb = 0, tBand = maxD;
                if (_LowFogMode > 0.5 && _LowFogDensity > 0 && dir.y < -0.02)
                {
                    float bandTop = _FogBase + max(_LowFogHeight, _LowFogBanks.w);
                    if (cam.y > bandTop)
                    {
                        tBand = min(maxD, (cam.y - bandTop) / -dir.y);
                        float fb = 1 - tBand / max(maxD, 1e-3);
                        sb = fb > 1e-3 ? max(fb, _LowFogShape.w) : 0;
                    }
                }
                // shadow_fix 03: a STILL jitter pattern. The per-frame offset (+ frac(_Time.y * 7.1) * 97) only averages out under TAA,
                // and OpenZ runs MSAA + SMAA: every frame each pixel's 16 steps landed at new depths across the shadow-volume edges,
                // so the sun-scatter term inside every cast shadow boiled frame to frame (up to one step of in-scatter per pixel).
                float jitter = Ign(uv * _ScreenParams.xy);
                Light sun = GetMainLight();
                float3 phaseDir = (sky && _FlybySkyW > 0) ? normalize(lerp(sun.direction, _FlybySunDir.xyz, _FlybySkyW)) : sun.direction;   // sky only: the ground keeps its haze
                float phase = HG(dot(dir, phaseDir), _FogG) * 12.566371;   // normalised so isotropic = 1
                float trans = 1; float3 scat = 0;
                int lampCount = (int)_FogLampCount;
                // on it). A lamp further than its range w from the whole segment adds exactly 0 at every step (saturate(1 - ld / w) = 0),
                // so the step loop skips it; the 5 % + 5 cm margin keeps float rounding on the safe side, w <= 0 is always kept (as before).
                uint lampMask = 0;
                for (int lc = 0; lc < lampCount; lc++)
                {
                    float3 lo = _FogLamps[lc].xyz - cam;
                    float tc = clamp(dot(lo, dir), 0, maxD);
                    float3 dv = lo - dir * tc;
                    float rr = _FogLamps[lc].w * 1.05 + 0.05;
                    if (_AtmoLampCullOff > 0.5 || _FogLamps[lc].w <= 0 || dot(dv, dv) < rr * rr) lampMask |= 1u << (uint)lc;
                }
                [loop] for (int i = 0; i < STEPS; i++)
                {
                    float tS = (i + jitter) * stepLen, dS = stepLen;
                    if (sb > 0)
                    {
                        float u = (i + jitter) / STEPS;
                        if (u < 1 - sb) { tS = u / (1 - sb) * tBand; dS = tBand / ((1 - sb) * STEPS); }
                        else { tS = tBand + (u - (1 - sb)) / sb * (maxD - tBand); dS = (maxD - tBand) / (sb * STEPS); }
                    }
                    float3 p = cam + dir * tS;
                    float d = Density(p);
                    float extLow = 0;                                                                // l17_night_fog_read: the banks' optical depth over this step's own segment
                    if (banks)
                    {
                        float tA = BandT(i / (float)STEPS, sb, tBand, maxD), tB = BandT((i + 1) / (float)STEPS, sb, tBand, maxD);
                        extLow = LowBankStep(p, cam.y + dir.y * tA, cam.y + dir.y * tB) * (tB - tA) * (1 - 0.8 * _FlybySkyW);
                    }
                    if (d < 1e-5 && extLow < 1e-6) continue;
                    // two taps 1.2 m apart across the sun (+ a little softening): thin poles and posts no longer cut razor-thin
                    // shafts through the whole view (long red lines on screen); cliffs and buildings keep their god rays
                    float3 side = normalize(cross(sun.direction, float3(0, 1, 0.001))) * 0.6;
                    float sh = 0.5 * (MainLightRealtimeShadow(TransformWorldToShadowCoord(p + side)) + MainLightRealtimeShadow(TransformWorldToShadowCoord(p - side)));
                    sh = lerp(0.3, 1, sh);
                    float3 light = _FogAmbient.rgb + sun.color * (sh * phase * _FogSunScatter);
                    if (lampMask != 0)
                    for (int l = 0; l < lampCount; l++)
                    {
                        if ((lampMask & (1u << (uint)l)) == 0) continue;
                        float3 lp = _FogLamps[l].xyz - p; float ld = length(lp);
                        float a = saturate(1 - ld / _FogLamps[l].w);
                        light += _FogLampColors[l].rgb * (a * a);
                    }
                    float ext = d * dS + extLow;
                    float t = exp(-ext);
                    scat += trans * light * _FogColor.rgb * (1 - t);
                    trans *= t;
                    if (trans < 0.02) break;
                }
                // readability cap: fog never hides more than (1 - _MinTransmittance) of the scene behind it
                float capK = saturate((1 - _MinTransmittance) / max(1e-3, 1 - trans));
                float k = sky ? lerp(1, capK, floorK) : capK;                                    // l17_night_fog_read: the skirt is capped like the ground
                trans = lerp(1, trans, k); scat *= k;
                // horizon haze (critic #2: hard sea horizon under a flat grey sky in the L17 cliff notch): far geometry fades
                // into the background colour; starts well beyond any play distance, so it is not readability-capped
                half3 outc = col * trans + scat;
                // pixels under the horizon (the L17 sea skirt writes no depth, the skirt edge, the far-clip cut) get the haze at their distance
                // to the skirt plane: far ground, skirt edge and painted horizon meet in one colour, no band, no line. W = 0: unchanged.
                float hazeD = farDist;
                if (sky && _FlybySkyW > 0 && dir.y < 0) hazeD = max(0, cam.y - _FlybySeaY) / max(1e-4, -dir.y);
                float hz = smoothstep(_HorizonStart, _HorizonEnd, hazeD) * (sky ? _FlybySkyW : 1);
                // 158,116,92): the band under the horizon is the haze colour itself, the sky over it was the dome UNDER the volumetric fog
                // (+ in-scatter). The sky over the horizon now also eases into the same haze colour after the fog, over ~3x the painting's
                // horizon blend, so both sides meet in one colour. W = 0 (gameplay): hz stays 0 for the sky, unchanged.
                // painted dawn sky + its cloud streaks read (the dome is the sky beyond the fog volume); then the HDR sun disc through the haze
                float bandH = max(1e-3, _FlybyHorizonBlend * max(0.5, _FlybyHazeBandK));
                float clearK = 0;
                if (sky && _FlybySkyW > 0 && dir.y >= 0)
                {
                    hz = (1 - smoothstep(0, bandH, dir.y)) * _FlybySkyW;
                    clearK = _FlybySkyW * saturate(_FlybySkyClear) * smoothstep(0, bandH, dir.y);
                    outc = lerp(outc, col, clearK);
                }
                outc = lerp(outc, _HorizonColor.rgb + FlybyGlow(dir) * _FlybySkyW, hz);
                // 233,186,138 where the plain haze read 230,206,185 / 235,215,199 / 233,210,190): the sky and the haze light (not the lit
                // ground under it) are warmed to gold while the sunrise flyby sky is on; gameplay (W = 0) and noon / night (sun strength 0)
                // are untouched.
                if (_FlybySkyW > 0)
                {
                    half k = (half)(_FlybySkyW * saturate(_FlybySunDir.w * 1.6));
                    half3 dawn = lerp((half3)1, half3(1.22, 0.84, 0.48), k);                   // graded before the tonemapper, which mutes it
                    if (sky) outc *= dawn;
                    else
                    {   // the haze light warmed; the lit ground itself a little deeper and warmer (the reference's backlit dawn mesas)
                        half3 base = col * (half)(trans * (1 - hz));
                        outc = base * lerp((half3)1, half3(0.90, 0.76, 0.62), k) + (outc - base) * dawn;
                    }
                }
                if (sky && _FlybySkyW > 0)
                {
                    float disc = FlybySunDiscMask(dir);
                    if (disc > 0)
                    {
                        float vis = lerp(trans, 1, clearK) * (1 - hz);                       // the share of the dome's own disc that came through
                        outc += _FlybySunDisc.rgb * (disc * max(0, saturate(_FlybySunThrough) - vis) * _FlybySkyW);
                    }
                }
                return half4(outc, outA);
            }
            ENDHLSL
        }
    }
}
