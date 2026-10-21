// K256g2: the playable shuttle's self-shadow while it flies (View/OzShuttleSelfShadow.cs, OpenZ/ShuttleSelfShadowDepth). Included by
// TeamLit's ForwardLit pass. Every value is set per renderer on the ship only (a property block); everywhere else _OzSelfShadowOn is 0
// and nothing changes (one uniform test). In the air the ship casts nothing into the sun's map (OzShuttleRealShadow), so its hull would
// lose the shadows of its own pods; this puts them back from the ship's own small map: the main light's share of the PBR result is
// taken out by (1 - self-shadow) exactly (the same light term URP adds: GetMainLight + LightingPhysicallyBased), the main shadow's
// strength and distance fade applied as URP applies them to the sun's map.
#ifndef OZ_SHUTTLE_SELF_SHADOW_INCLUDED
#define OZ_SHUTTLE_SELF_SHADOW_INCLUDED

float _OzSelfShadowOn;          // 1 on the flying shuttle's own renderers (property block), 0 everywhere else
float4x4 _OzSelfShadowMatrix;   // world -> (u, v, depth01): the map's uv (0..1) and the depth the map keeps
float4 _OzSelfShadowBias;       // x = depth bias (depth01 units), y = the filter's tap step (uv), z = normal offset (m), w = unused
TEXTURE2D(_OzSelfShadowTex);
SAMPLER(sampler_OzSelfShadowTex);

// 1 = lit by the main light as far as the ship itself goes, 0 = in its own shadow (a 3 x 3 filter)
half OZSelfShadowFactor(float3 positionWS, float3 nGeoWS)
{
    float3 p = positionWS + nGeoWS * _OzSelfShadowBias.z;
    float3 m = mul(_OzSelfShadowMatrix, float4(p, 1.0)).xyz;
    if (m.x <= 0.0 || m.y <= 0.0 || m.x >= 1.0 || m.y >= 1.0 || m.z <= 0.0 || m.z >= 1.0) return 1.0h;
    float ref = m.z - _OzSelfShadowBias.x;
    float lit = 0.0;
    [unroll] for (int j = -1; j <= 1; j++)
        [unroll] for (int k = -1; k <= 1; k++)
        {
            float s = SAMPLE_TEXTURE2D_LOD(_OzSelfShadowTex, sampler_OzSelfShadowTex, m.xy + float2(k, j) * _OzSelfShadowBias.y, 0).r;
            lit += s >= ref ? 1.0 : 0.0;
        }
    return (half)(lit / 9.0);
}

// After UniversalFragmentPBR: the ship's own shadow on the main light's term. Returns the factor (1 = nothing changed).
half OZSelfShadowApply(inout half4 c, InputData d, SurfaceData s, float3 positionWS, float3 nGeoWS)
{
    half k = 1.0h;
#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
    UNITY_BRANCH
    if (_OzSelfShadowOn > 0.5)
    {
        half f = OZSelfShadowFactor(positionWS, nGeoWS);
        f = LerpWhiteTo(f, GetMainLightShadowParams().x);                                    // the light's shadow strength
        f = lerp(f, 1.0h, GetMainLightShadowFade(positionWS));                               // the shadow distance's fade
        k = f;
        if (k < 0.999h)
        {
            SurfaceData s2 = s; BRDFData b2;
            InitializeBRDFData(s2, b2);
            BRDFData bc2 = CreateClearCoatBRDFData(s2, b2);
            half4 sm2 = CalculateShadowMask(d);
            AmbientOcclusionFactor ao2 = CreateAmbientOcclusionFactor(d, s2);
            Light l2 = GetMainLight(d, sm2, ao2);
            half3 main2 = LightingPhysicallyBased(b2, bc2, l2, d.normalWS, d.viewDirectionWS, s2.clearCoatMask, false);
            c.rgb = max(c.rgb - main2 * (1.0h - k), 0.0h);
        }
    }
#endif
    return k;
}
#endif
