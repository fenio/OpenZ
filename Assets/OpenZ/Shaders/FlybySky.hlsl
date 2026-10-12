// OpenZ (GPL-3.0)
// and the far haze of OpenZ/AtmosphereFullscreen. The same glow is added on both sides of the horizon, so the glow under a low sun
// never ends in a hard line where the painted sky meets the hazed far ground (sky_panoramas_v1 README, note 2 / mockB).
// All globals are written by OpenZ.FlybySky (Scripts/View/FlybySky.cs). _FlybySkyW = 0 (gameplay) means: nothing changes.
#ifndef OPENZ_FLYBYSKY_INCLUDED
#define OPENZ_FLYBYSKY_INCLUDED

float _FlybySkyW;            // 0 = gameplay (the gradient skybox, no change anywhere), 1 = the flyby sky fully on (eased fade)
float4 _FlybySunDir;         // xyz = world direction TO the DayNight sun, w = sun strength (0 under the horizon / away from sunrise)
float4 _FlybySunGlow;        // rgb = glow colour (linear), a = strength of the wide glow
float4 _FlybySunShape;       // x = tight-glow exponent, y = wide-glow exponent, z = cos(disc radius), w = cos(disc radius + soft edge)
float4 _FlybySunDisc;        // rgb = disc colour (linear, HDR so the bloom catches it)
float _FlybySeaY;            // world height of the SKIRT / sea plane: sky pixels below the horizon get the haze at their distance to it
float _FlybyHorizonBlend;    // sin(horizonBlendDeg): the painting blends from the haze colour at the horizon into the painting above
float _FlybySkyClear;        // 0 = the sky is fully fogged as before .. 1 = the painting over the haze band
float _FlybyHazeBandK;       // the haze band's height as a multiple of _FlybyHorizonBlend
float _FlybySunThrough;      // share of the HDR disc that shows through the haze band

half3 FlybyGlow(float3 d)
{
    float c = saturate(dot(d, _FlybySunDir.xyz));
    float g = pow(c, _FlybySunShape.x) + _FlybySunGlow.a * pow(c, _FlybySunShape.y);
    return (half3)(_FlybySunGlow.rgb * (g * _FlybySunDir.w));
}

// The sun disc mask on direction d (0..1, x the sun strength); the same shape as the dome draws.
float FlybySunDiscMask(float3 d)
{
    float c = dot(d, _FlybySunDir.xyz);
    return smoothstep(_FlybySunShape.w, _FlybySunShape.z, c) * smoothstep(-0.002, 0.006, d.y) * _FlybySunDir.w;
}

#endif
