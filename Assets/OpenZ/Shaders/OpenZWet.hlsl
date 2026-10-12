// OpenZ weather_l17_v1: rain wetness shared by OpenZ/Sand (the L17 ground), OpenZ/WaterStylized (the L17 harbour and canals)
// and OpenZ/TeamLit (environment class only: rocks, cliffs, bridges, palette props). Include AFTER
// Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl.
//
// Globals only (no material property, SRP Batcher compatible), written every frame by OzWeather.cs while a level with weather
// runs and reset to 0 when it ends: _OzWetness 0 = dry = every function below returns its input unchanged (the callers also
// branch on it, a uniform branch), so nothing changes on L02, in the menu or on a dry night.
//   _OzWetness   0 dry .. 1 soaked (follows the rain with a lag: wets in ~12 s, dries in ~45 s after the shower)
//   _OzRain      current rain intensity 0..1 (drop ripples on puddles and water)
//   _OzWetSky    rgb = linear colour the wet ground reflects (night sky / fog, + the lightning flash), a unused
//   _OzWetParams x damp darkening, y puddle cover at full wetness, z puddle reflection gain, w ripple strength (0 = defaults)
#ifndef OPENZ_WET_INCLUDED
#define OPENZ_WET_INCLUDED

float _OzWetness;
float _OzRain;
float4 _OzWetSky;
float4 _OzWetParams;

float4 OZWParams() { return _OzWetParams.w > 0.0 ? _OzWetParams : float4(0.3, 0.34, 1.0, 1.0); }

float OZWHash(float2 p)
{
    float3 p3 = frac(float3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return frac((p3.x + p3.y) * p3.z);
}

float OZWNoise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3.0 - 2.0 * f);
    return lerp(lerp(OZWHash(i), OZWHash(i + float2(1, 0)), u.x), lerp(OZWHash(i + float2(0, 1)), OZWHash(i + float2(1, 1)), u.x), u.y);
}

// puddle mask 0..1 at world xz: low noise spots fill first; none before wetness 0.3, cover ~ params.y at wetness 1; flat ground only
half OZWPuddle(float2 xz, half ny)
{
    float4 P = OZWParams();
    float n = OZWNoise(xz * 0.22) * 0.70 + OZWNoise(xz * 0.71 + 7.3) * 0.26 + OZWNoise(xz * 2.3 + 1.7) * 0.04;
    half grow = (half)saturate((_OzWetness - 0.3) / 0.7);
    float thr = lerp(0.86, 0.5 + (0.5 - P.y) * 0.33, grow);                          // noise ~ mean .5, sd ~.13: cover ~ params.y at wetness 1
    half m = (half)smoothstep(thr, thr + 0.09, n) * grow;
    return m * (half)smoothstep(0.85, 0.95, ny);
}

// rain drop ring ripples: two offset layers of 0.55 m cells, one drop per active cell per cycle (the share of active cells
// follows the rain); returns a world-space slope (x, z). Faded out where a pixel covers more than ~8 cm (no shimmer at max zoom).
half2 OZWRipples(float2 xz, float t, half rain, float pxm)
{
    half2 s = half2(0.0, 0.0);
    half aa = (half)saturate(1.0 - (pxm - 0.03) / 0.05);
    if (rain <= 0.001 || aa <= 0.0) return s;
    [unroll] for (int L = 0; L < 2; L++)
    {
        float2 p = xz / 0.55 + L * float2(0.43, 0.71);
        float2 c = floor(p);
        float2 f = frac(p) - 0.5;
        float h1 = OZWHash(c + L * 19.7), h2 = OZWHash(c + 3.3 + L * 5.1), h3 = OZWHash(c + 9.1 + L * 2.3);
        float ph = frac(t * (0.9 + 0.3 * h2) + h1);                                    // drop life 0..1
        float2 d = f - (float2(h1, h2) - 0.5) * 0.5;                                    // drop centre inside the cell
        float r = length(d);
        float x = (r - ph * 0.45) * 18.0;                                               // one wavelength around the ring front
        float ring = sin(clamp(x, -3.14159, 3.14159)) * (1.0 - ph) * (1.0 - ph) * step(h3, rain);
        s += (half2)(d / max(r, 1e-3) * ring);
    }
    return s * (half)(0.35 * OZWParams().w) * aa;
}

// albedo before lighting: damp darkening everywhere, a water film darkens the puddles further. puddle = OZWPuddle (out)
half3 OZWetAlbedo(float2 xz, half3 albedo, half ny, out half puddle)
{
    float4 P = OZWParams();
    half w = (half)saturate(_OzWetness);
    puddle = OZWPuddle(xz, ny);
    albedo *= lerp((half)1.0, (half)(1.0 - P.x), w * (half)saturate(ny * 1.5));
    return albedo * lerp((half)1.0, (half)0.55, puddle);
}

// wet sheen added after lighting (the L17 ground has no URP specular / reflections: _OZ_NO_REFL_SPEC): a stylised sky reflection
// (floor so the RTS camera sees it at 55 deg, + Fresnel), the moon glint and every lamp / headlight / flash as a highlight
// on the rippled film; damp ground a soft broad sheen, puddles a sharp mirror.
half3 OZWetSheen(float3 positionWS, half3 nWS, half3 V, float2 screenUV, float4 shadowCoord, half puddle, float pxm)
{
    float4 P = OZWParams();
    half w = (half)saturate(_OzWetness);
    half gloss = saturate(w * (half)0.45 + puddle);
    if (gloss <= 0.001) return half3(0.0, 0.0, 0.0);
    half2 rip = OZWRipples(positionWS.xz, _Time.y, (half)_OzRain, pxm) * lerp((half)0.3, (half)1.0, puddle);
    // crumpled shards): damp ground 60 % flattened, puddles 95 % (a mirror); only the rain ripples break the reflection
    half3 nF = normalize(lerp(nWS, half3(0.0, 1.0, 0.0), lerp((half)0.6, (half)0.95, puddle)));
    half3 n = normalize(half3(nF.x + rip.x, nF.y, nF.z + rip.y));
    half ndv = saturate(dot(n, V));
    half fres = (half)0.04 + (half)0.96 * pow(1.0h - ndv, 5.0h);
    half3 c = (half3)_OzWetSky.rgb * ((lerp((half)0.05, (half)0.2, puddle) + fres * (half)0.6) * gloss * (half)P.z);
    Light ml = GetMainLight(shadowCoord);
    half3 H = SafeNormalize(float3(ml.direction) + float3(V));
    half pw = lerp((half)24.0, (half)260.0, puddle);
    c += ml.color * (pow(saturate(dot(n, H)), pw) * lerp((half)0.25, (half)1.1, puddle) * ml.shadowAttenuation * gloss * (half)P.z);
#if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
    {
        InputData inputData = (InputData)0;                     // LIGHT_LOOP_BEGIN reads inputData in the Forward+ cluster loop
        inputData.positionWS = positionWS;
        inputData.normalWS = n;
        inputData.viewDirectionWS = V;
        inputData.normalizedScreenSpaceUV = screenUV;
        uint lightCount = GetAdditionalLightsCount();
        half pwA = lerp((half)40.0, (half)160.0, puddle), gA = lerp((half)0.2, (half)1.1, puddle);
        LIGHT_LOOP_BEGIN(lightCount)
            Light light = GetAdditionalLight(lightIndex, positionWS);
            half3 radiance = light.color * (light.distanceAttenuation * light.shadowAttenuation);
            half3 h = SafeNormalize(float3(light.direction) + float3(V));
            half3 nS = normalize(half3(n.x * 2.2, 1.0, n.z * 2.2));                   // reflections break into streaks on the ripples
            c += radiance * ((pow(saturate(dot(n, h)), pwA) + (half)0.2 * pow(saturate(dot(nS, h)), (half)48.0)) * gA * gloss * (half)P.z);
        LIGHT_LOOP_END
    }
#endif
    return c;
}

// TeamLit environment class (rocks, cliffs, bridges, palette props): wet stone darkens, tops more than the sides
half OZWetEnvDarken(half ny)
{
    return 1.0h - (half)saturate(_OzWetness) * lerp((half)0.14, (half)0.3, saturate(ny));
}

#endif
