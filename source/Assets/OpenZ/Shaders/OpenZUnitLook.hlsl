// OpenZ unitlook_v2: clean unit shading for OpenZ/TeamLit materials with _UnitLook = 1 (vehicles, cannons, robots and their
// weapons). Include AFTER Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl, inside the ForwardLit
// pass (it reads the UnityPerMaterial properties _UnitRamp / _UnitShade / _UnitKick / _UnitLook of TeamLit).
//
// Measured causes (unitlook_v2 DIAGNOSIS.md #3, #5,
// #7, #8): the DayNight sun sits BEHIND the RTS camera (azimuth -19 deg at t 0.08, front light), and on top of URP PBR the
// stylize stack added a wrap fill, an ambient boost, a warm key and a view-fresnel rim that lights every left / right face at
// full strength: top, front and side faces of a vehicle landed within 0.09 of each other (display luminance). Metallic 0.6 with
// smoothness 0 turned the paint khaki-dark, and the metal sheen and rim sparkled on dense meshes.
//
// The unit look replaces that stack for unit materials only (rocks, cliffs, buildings, props: unchanged):
//  * the key is the real sun / moon (shadows and shading agree); _UnitRamp.w can turn it toward the camera's left for a more
//    3/4 look, but faces turned away from the real light still take its self-shadow, so keep it at 0 (measured: no gain)
//  * a soft ramp smoothstep(_UnitRamp.x, _UnitRamp.y, N.L) (-0.05 .. 0.85) with a Lambert gradient inside the lit side
//    (_UnitRamp.z 0.80). The sun is behind the camera, so the top (N.L 0.85) and the camera-facing front (N.L 0.50) are both
//    lit; this wide ramp keeps them apart (a narrower first tuning left the heavy and medium tank's top and front only
//    0.04-0.05 apart). 12 real units at the RTS camera, day: heavy tank
//    top / front / shade side 0.72 / 0.63 / 0.50 display, light tank 0.73 / 0.56 / 0.45 (T19 look: 0.67 / 0.62 / 0.59)
//  * shade = trilight ambient (SH, x _UnitShade.x, SSAO) + a warm sun-coloured bounce (_UnitShade.y): never black
//  * dielectric only; one soft highlight, capped at _UnitShade.z of the key, its lobe widened where the normal changes inside
//    a pixel (no sparkle on 25k-triangle robots at 70 m); no environment reflection
//  * every additional light (fire, blasts, muzzle flashes, headlights, lamps) lights the unit (Forward+ cluster loop)
//  * the rim becomes a thin kicker on the top silhouette edge (faces turning up and away from the camera), anti-aliased with
//    fwidth, 0.5 x in shadow; never on the side faces. By night the same edge (a little wider) carries the moonlit / team rim.
#ifndef OPENZ_UNIT_LOOK_INCLUDED
#define OPENZ_UNIT_LOOK_INCLUDED

half _OZUnitLookOff;    // global A/B switch (menu OpenZ/Look/Unit look A-B, Play mode): 1 = the old PBR + stylize shading

half OZUnitOn()
{
    return step(0.5h, _UnitLook) * step(_OZUnitLookOff, 0.5h);
}

// the form key: the light's horizontal direction pushed toward the camera's left by _UnitRamp.w, elevation kept
float3 OZUnitKeyDir(float3 L)
{
    float3 camRight = UNITY_MATRIX_V[0].xyz;                          // world-space camera right (row 0 of the view matrix)
    float2 r = camRight.xz; float rl = length(r); r = rl > 1e-4 ? r / rl : float2(1.0, 0.0);
    float2 h = L.xz; float hl = length(h);
    float2 k = h - r * _UnitRamp.w;
    float kl = length(k);
    float2 hd = kl > 1e-4 ? k / kl * hl : h;
    return normalize(float3(hd.x, L.y, hd.y));
}

// 1 on faces that turn up and away from the camera (the top silhouette edge on screen), 0.57 on flat tops, <= 0 on faces that
// look at the camera or sideways. Computed outside flow control (the kicker needs its screen derivative).
half OZKickerDot(half3 N, half3 V)
{
    half3 up = half3(0.0h, 1.0h, 0.0h);
    half3 kd = up - V * dot(up, V);                                   // world up projected off the view ray = screen up
    half kl = length(kd);
    kd = kl > 1e-3h ? kd / kl : half3(0.0h, 0.0h, 1.0h);
    return dot(N, kd);
}

half OZKicker(half x, half w, half lo, half hi)
{
    return smoothstep(lo - w, hi + w, x);
}

// how much the shading normal turns inside one pixel (0 on flat panels, ~1 on sub-pixel geometry); outside flow control
half OZNormalWidth(half3 N)
{
    half3 dn = fwidth(N);
    return saturate(dot(dn, dn) * 4.0h);
}

half3 OZUnitAddLight(Light l, half3 N, half3 V, half3 albedo, half e)
{
    half atten = l.distanceAttenuation * l.shadowAttenuation;
    half ndl = dot(N, l.direction);
    half diff = saturate((ndl + 0.2h) / 1.2h);                        // a small wrap: fire and lamps read on round robots
    half3 H = SafeNormalize(l.direction + V);
    half sp = min(pow(saturate(dot(N, H)), e) * (e + 2.0h) * 0.005h, _UnitShade.z) * saturate(ndl * 4.0h);
    return l.color * (atten * (albedo * diff + sp));
}

// the lit colour of a unit pixel (direct key ramp + ambient + bounce + highlight + additional lights + emission)
half3 OZUnitShade(InputData inputData, half3 albedo, half smoothness, half3 emission, half nrmW, out half vis, half metal, half envK, half hiGain)
{
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData.normalizedScreenSpaceUV, 1.0h);
    Light ml = GetMainLight(inputData, inputData.shadowMask, aoFactor);
    half3 N = inputData.normalWS, V = inputData.viewDirectionWS;
    half3 K = (half3)OZUnitKeyDir(ml.direction);
    half ndl = dot(N, K);
    half ramp = smoothstep(_UnitRamp.x, _UnitRamp.y, ndl);
    half lamb = 1.0h - _UnitRamp.z + _UnitRamp.z * saturate(ndl);
    vis = ml.shadowAttenuation * ml.distanceAttenuation;
    half lit = ramp * lamb * vis;
    half3 col = albedo * ml.color * lit;
    col += albedo * inputData.bakedGI * (_UnitShade.x * aoFactor.indirectAmbientOcclusion);                // trilight ambient
    col += albedo * ml.color * (_UnitShade.y * (1.0h - ramp * vis) * aoFactor.indirectAmbientOcclusion);   // warm bounce in shade
    // one soft highlight (Blinn-Phong, energy-normalised, x 0.04 dielectric), capped; geometric AA widens the lobe
    half e = exp2(10.0h * saturate(smoothness) + 1.0h);
    e = max(e / (1.0h + e * nrmW), 2.0h);
    half3 H = SafeNormalize(K + V);
    half sp = pow(saturate(dot(N, H)), e) * (e + 2.0h) * 0.005h;
    // reflection (no rim: constant weight, never stronger at grazing angles) gives painted metal a gentle gloss by day
    half hg = hiGain > 0.0h ? hiGain : 1.0h;
    col += ml.color * (min(sp, _UnitShade.z * hg * lerp(0.8h, 1.3h, metal)) * lit);
    if (envK > 0.0h)
    {
        half3 R = reflect(-V, N);
        half3 env = GlossyEnvironmentReflection(R, PerceptualSmoothnessToPerceptualRoughness(smoothness), aoFactor.indirectAmbientOcclusion);
        col += env * (envK * lerp(0.45h, 1.0h, metal) * lerp(0.6h, 1.0h, vis));
    }
    #if defined(_ADDITIONAL_LIGHTS)
        uint pixelLightCount = GetAdditionalLightsCount();
        #if USE_CLUSTER_LIGHT_LOOP
        [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
        {   // same structure as URP's UniversalFragmentPBR (Lighting.hlsl): directional additional lights first
            CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
            Light al = GetAdditionalLight(lightIndex, inputData, inputData.shadowMask, aoFactor);
            col += OZUnitAddLight(al, N, V, albedo, e);
        }
        #endif
        LIGHT_LOOP_BEGIN(pixelLightCount)
            Light l = GetAdditionalLight(lightIndex, inputData, inputData.shadowMask, aoFactor);
            col += OZUnitAddLight(l, N, V, albedo, e);
        LIGHT_LOOP_END
    #endif
    return col + emission;
}

#endif
