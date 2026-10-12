#ifndef OPENZ_RUST_INCLUDED
#define OPENZ_RUST_INCLUDED
// rust overlay for TeamLit, on only where a material sets _Rust > 0 (every other TeamLit material keeps 0 = unchanged). No texture
// (the wreck kit has vertex colours, no UVs): mesh-space 3D value noise, so the patches stay on the panels when the hull sinks.
//   faded, chalky, mottled paint  ->  chipped specks round each patch  ->  orange-brown rust patches with a thin bare-steel rim
//   + vertical rust streaks down the sides  + more rust low on the body (sills, wheel arches) and on the roof / bonnet
//   + darker grime from just over the waterline down (world y vs _RustWaterY).
// Fine detail fades with the pixel footprint (fwidth), so nothing shimmers at the RTS camera.

float OZRHash(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float OZRNoise(float3 x)
{
    float3 i = floor(x), f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    float a = OZRHash(i), b = OZRHash(i + float3(1, 0, 0)), c = OZRHash(i + float3(0, 1, 0)), d = OZRHash(i + float3(1, 1, 0));
    float e = OZRHash(i + float3(0, 0, 1)), g = OZRHash(i + float3(1, 0, 1)), h = OZRHash(i + float3(0, 1, 1)), k = OZRHash(i + float3(1, 1, 1));
    return lerp(lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y), lerp(lerp(e, g, f.x), lerp(h, k, f.x), f.y), f.z);
}

float OZRFbm(float3 p)
{
    return (0.5 * OZRNoise(p) + 0.25 * OZRNoise(p * 2.03 + 17.1) + 0.125 * OZRNoise(p * 4.01 + 31.7)) / 0.875;
}

// albedo (linear) -> the rusty albedo; rust = how much bare rust / steel this pixel shows (0..1, for the smoothness)
half3 OZRustOverlay(half3 albedo, float3 posOS, float3 nWS, float3 posWS, half amount, float waterY, float scale, out half rust)
{
    float3 p = posOS * max(scale, 0.01);
    float mpp = max(length(fwidth(posOS)), 1e-4);                          // metres per pixel here
    float wl = posWS.y - waterY;                                            // height over the water
    float low = saturate(1.0 - wl / 1.3);                                   // 1 at the waterline .. 0 at 1.3 m up (sills, arches)
    float up = saturate((nWS.y - 0.55) / 0.3);                              // roof / bonnet tops
    // ragged patches (0.15-0.4 m), about 35 % of the roof and upper body, 45-50 % low on the body (fbm sd 0.12, mean 0.5:
    // rust share = P(f > 1 - cover)); first look in Play (cover 0.68 on the roof) read as camouflage
    float f = OZRFbm(p * 3.0);
    float cover = amount * saturate(0.41 + 0.10 * low + 0.01 * up);
    float t = f - (1.0 - cover);                                            // > 0 inside a patch
    float aa = clamp(mpp * 3.3, 0.012, 0.08);                               // a soft edge of about 1.5 px
    half patch = (half)smoothstep(-aa, aa, t);
    half rim = patch * (half)(1.0 - smoothstep(aa, aa + 0.03, t));          // thin dark rust edge just inside the patch
    // chipped paint: specks round each patch (gone when smaller than about 2 px)
    float chipFade = 1.0 - smoothstep(0.03, 0.07, mpp);
    half chip = (half)(smoothstep(0.64, 0.70, OZRNoise(p * 7.0 + 5.3)) * (1.0 - smoothstep(0.0, 0.16, -t)) * chipFade) * (1 - patch);
    // streaks: tall thin noise down the sides, stronger low on the body
    float side = 1.0 - saturate(abs(nWS.y) * 1.6);
    float sfade = lerp(0.45, 1.0, 1.0 - smoothstep(0.04, 0.09, mpp));
    float sn = OZRNoise(float3(p.x * 6.0, p.y * 0.7, p.z * 6.0) + 11.0);
    half streak = (half)(smoothstep(0.60, 0.82, sn) * side * saturate(0.35 + 0.65 * low) * sfade) * amount * (1 - patch);
    // colours (linear): dark brown .. rust .. orange, varied slowly; bare steel
    float rv = OZRNoise(p * 3.1 + 2.0);
    half3 rustDark = half3(0.105, 0.032, 0.012), rustMid = half3(0.30, 0.085, 0.022), rustOrange = half3(0.48, 0.16, 0.035);
    half3 rustCol = lerp(lerp(rustDark, rustMid, (half)saturate(rv * 1.7)), rustOrange, (half)saturate(rv * 1.7 - 0.75));
    half3 steel = half3(0.20, 0.17, 0.145);
    // only painted metal rusts: dark glass, tyres and rubber keep their colour (first Play look: rust patches on the rear window)
    half lum = dot(albedo, half3(0.2126, 0.7152, 0.0722));
    half paint = (half)smoothstep(0.035, 0.09, lum);
    patch *= paint; rim *= paint; chip *= paint; streak *= paint;
    // faded paint: toward a warm chalky grey, mottled
    half3 c = lerp(albedo, lum * half3(1.06, 1.0, 0.9), 0.15 * amount) * (half)(0.93 + 0.12 * OZRNoise(p * 1.3 + 7.7));
    c = lerp(c, lerp(rustMid * 0.9, c * half3(0.62, 0.42, 0.3), 0.35), streak * 0.7);
    c = lerp(c, lerp(rustDark, steel, 0.25), chip);
    c = lerp(c, lerp(rustCol, rustDark * 0.7, rim * 0.8), patch);
    // grime: darker from just over the waterline down (a ragged top edge)
    float gTop = 0.38 + 0.14 * (OZRNoise(p * 2.6 + 3.1) - 0.5);
    half grime = (half)(1.0 - smoothstep(-0.05, gTop, wl)) * amount;
    c = lerp(c, c * half3(0.34, 0.32, 0.24) + half3(0.010, 0.010, 0.005), grime * 0.8);
    rust = saturate(max(patch, max(streak, chip)));
    return c;
}
#endif
