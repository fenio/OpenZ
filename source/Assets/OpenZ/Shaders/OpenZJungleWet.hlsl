// OpenZ level 16: rain wetness of the layered jungle ground (OpenZ/JungleGround). Include AFTER OpenZWet.hlsl.
//
// The dry look of every ground kind is untouched: with _OzWetness 0 the ground shader does not come here at all (its own
// uniform branch), so the calm grass and its transitional grass are exactly what they were as soon as the ground has dried.
// While wet (OzJungleWeather.cs writes the globals; the shared ones are OpenZWet.hlsl's):
//   - every kind darkens (damp), flat faces more than steep ones; the grass and its transitional grass take a share of it
//     (_OzJungleWet.x) and nothing else here: they ONLY darken, and OZWetSheen adds the soft sheen of damp ground;
//   - puddles (a darker water film here; the mirror with the drop rings is OZWetSheen's) lie only where the road, the bank
//     mud or the wet gorge floor own the place, on flat ground, on about a quarter of it at full wetness (_OzWetParams.y).
//     No puddle of this kind lies on grass, verge, rock, moss or paving.
// Rain puddles on the low / worn grass and on the gravel paths (OZJLowPuddleAlbedo, below; the ground shader calls it while
// _OzLowPuddle.x > 0): a few irregular hollows per patch of the transitional grass and in the middle lane of the longer gravel
// paths, from the puddle picture (OzJungleWeather.LowPuddles.cs), filled by a water level of their own that rises while it rains
// and falls slowly after it; never on slopes, rock, ruins, roads, water or under a building. Flat dark water (on a path: its
// darker earth's colour); its mirror and rain rings are OZWetSheen's.
// wa / wb = the ground's own weight maps at this place (A: grass, verge, mud, rock; B: wet floor, road, swamp bed, moss top).
#ifndef OPENZ_JUNGLE_WET_INCLUDED
#define OPENZ_JUNGLE_WET_INCLUDED

float4 _OzJungleWet;   // global: x = share of the damp darkening that grass takes (0 = default 0.7), y = puddle film (0 = default 0.55)
float4 _OzLowPuddle;   // global: low-grass puddles, x = water level 0..1 (0 = none), y = reach (0 = default 0.6), z = water film (0 = default 0.35), w = edge softness (0 = default 0.05)

half3 OZJWetAlbedo(float2 xz, half3 albedo, half ny, half4 wa, half4 wb, out half puddle)
{
    float4 P = OZWParams();
    half w = (half)saturate(_OzWetness);
    half grassK = _OzJungleWet.x > 0.0 ? (half)_OzJungleWet.x : (half)0.7;
    half film = _OzJungleWet.y > 0.0 ? (half)_OzJungleWet.y : (half)0.55;
    half sum = max(wa.r + wa.g + wa.b + wa.a + wb.r + wb.g + wb.b + wb.a, (half)0.001);
    half soil = saturate((wa.b + wb.r + wb.g) / sum);                               // bank mud + wet gorge floor + road
    half grass = saturate((wa.r + wa.g) / sum);                                      // grass + verge (the transitional grass)
    puddle = OZWPuddle(xz, ny) * (half)smoothstep(0.6, 0.9, soil);
    half damp = w * (half)saturate(ny * 1.5) * (half)P.x * lerp((half)1.0, grassK, grass);
    albedo *= (half)1.0 - damp;
    return albedo * lerp((half)1.0, film, puddle);
}

// Rain puddles on the low / worn grass and the gravel paths. lowD = the puddle picture's depth of the hollow here (0 = never water), lowG = (the
// transitional grass's share of the layer blend, the blended layer height) here, flat = the ground's colour without its fine
// detail (the far picture; on a path its darker earth). Everything of a hollow deeper than 1 - level x reach is water: the deepest points fill first and
// the edge follows the ground's own leafy height a little (irregular, never a circle); a damp darker margin lies round each
// puddle; flat ground only. The water hides most of the ground's detail and is darker and browner (the film).
// puddle (inout) = the larger of the road's and the low grass's puddle here (the sheen's mirror and rings follow it).
half3 OZJLowPuddleAlbedo(half3 albedo, half3 flat, half lowD, half2 lowG, half ny, inout half puddle)
{
    half L = (half)saturate(_OzLowPuddle.x);
    if (L <= 0.001 || lowD <= 0.004) return albedo;
    half reach = _OzLowPuddle.y > 0.0 ? (half)_OzLowPuddle.y : (half)0.6;
    half film = _OzLowPuddle.z > 0.0 ? (half)_OzLowPuddle.z : (half)0.35;
    half soft = _OzLowPuddle.w > 0.0 ? (half)_OzLowPuddle.w : (half)0.05;
    half h = lowD + ((lowG.x - 0.5) * 0.06 + (0.5 - lowG.y) * 0.30) * saturate(lowD * 6.0);
    half thr = 1.0 - L * reach;
    half on = saturate(L * 20.0) * (half)smoothstep(0.95, 0.985, ny);
    half p = (half)smoothstep(thr, thr + soft, h) * on;
    half rim = (half)smoothstep(thr - 0.12, thr, h) * on * (1.0 - p);
    half3 water = lerp(albedo, flat, (half)0.7);
    water = lerp(water, dot(water, half3(0.2126, 0.7152, 0.0722)) * half3(1.0, 0.95, 0.82), (half)0.4) * film;
    albedo *= 1.0 - 0.12 * rim;
    puddle = max(puddle, p);
    return lerp(albedo, water, p);
}

#endif
