// OpenZ night_v2: shared night terms for OpenZ/TeamLit, OpenZ/Sand and OpenZ/FlagCloth (URP 17). Include AFTER
// Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl (uses InputData, SurfaceData, BRDFData).
//
//  1. Night classes. TeamLit's night rim + moonlit fill (night package) reached every stylized / accent material with a
//     day rim: since rocks_v2 and cliffs v3 (_RimColor 0.12) that included the rock pillars and the cliffs, and always the
//     buildings, crates and huts. Now each class has its own rim / fill (NightV2.cs pushes them, defaults below).
//  2. Day-sky reflections. The environment reflection (reflection probe atlas / skybox cubemap) is generated once from
//     the day sky; DayNight only recolours the sky material, so every smooth or metallic surface kept reflecting daylight
//     at night. OZNightEnvFix removes the part of that reflection the current sky no longer gives (ratio from NightV2).
// Globals only (no material properties): SRP Batcher compatible. While NightV2 is not running the globals are 0: the rim /
// fill fall back to the built-in defaults and the reflection is left unchanged.
#ifndef OPENZ_NIGHT_V2_INCLUDED
#define OPENZ_NIGHT_V2_INCLUDED

float4 _OZNightV2;      // x = 1 while NightV2 runs, y = sky ratio (current sky ambient / the day sky ambient, 0..1)
float4 _OZNightRimK;    // night rim per class:  x environment, y unit, z building, w map item
float4 _OZNightFillK;   // night fill per class: same order
float4 _OZNightEnvW;

// class: 0 environment, 1 unit, 2 building, 3 map item
half OZPick(float4 v, half cls) { return cls < 0.5h ? (half)v.x : cls < 1.5h ? (half)v.y : cls < 2.5h ? (half)v.z : (half)v.w; }

half2 OZNightRimFill(half cls)
{
    float4 r = _OZNightV2.x > 0.5 ? _OZNightRimK : float4(0.0, 0.0, 0.3, 0.5);
    float4 f = _OZNightV2.x > 0.5 ? _OZNightFillK : float4(0.0, 1.0, 0.0, 0.3);
    return half2(OZPick(r, cls), OZPick(f, cls));
}

// multiplier of the environment reflection for this class right now (1 by day and while NightV2 is not running)
half OZEnvScale(half cls)
{
    if (_OZNightV2.x < 0.5) return 1.0h;
    return lerp(1.0h, saturate((half)_OZNightV2.y), saturate(OZPick(_OZNightEnvW, cls)));
}

// The environment specular UniversalFragmentPBR added for these inputs (same functions, same arguments as URP 17's
// GlobalIllumination(): GlossyEnvironmentReflection x EnvironmentBRDFSpecular x indirect AO).
half3 OZEnvSpecular(InputData d, SurfaceData s)
{
    BRDFData brdf;
    half alpha = s.alpha;
    InitializeBRDFData(s.albedo, s.metallic, s.specular, s.smoothness, alpha, brdf);
    half3 R = reflect(-d.viewDirectionWS, d.normalWS);
    half fresnel = Pow4(1.0h - saturate(dot(d.normalWS, d.viewDirectionWS)));
    AmbientOcclusionFactor ao = CreateAmbientOcclusionFactor(d, s);
    half3 env = GlossyEnvironmentReflection(R, d.positionWS, brdf.perceptualRoughness, 1.0h, d.normalizedScreenSpaceUV);
    return EnvironmentBRDFSpecular(brdf, fresnel) * env * ao.indirectAmbientOcclusion;
}

// c (the UniversalFragmentPBR result) with the day-sky reflection scaled to the current sky
half3 OZNightEnvFix(half3 c, InputData d, SurfaceData s, half cls)
{
    half k = OZEnvScale(cls);
    if (k > 0.999h) return c;
    return max(c - OZEnvSpecular(d, s) * (1.0h - k), half3(0.0h, 0.0h, 0.0h));
}

#endif
