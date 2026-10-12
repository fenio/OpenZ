// OpenZ/PyroTongues: the upright ground-fire tongues and embers of our earlier projects, ported line for line.
//  * every tongue = a quad rooted in the ground, spanning camera-right (flattened to the ground plane) x world-up, so it can
//    never flatten into a screen point; its own life cycle fract(seed + t speed .52), height mix(1.1, 3.6) x (.66 + .34 sin),
//    sway, feathered tongue body, base glow, flicker, and the yellow HDR core (12, 8.2, .48) that blooms;
//  * embers (kind 1): small rising sparks (.2 + .5 l + 1.55 l^2 up, shrinking .12 -> .035);
//  * alpha-composited orange body (THREE.NormalBlending, unpremultiplied: SrcAlpha, OneMinusSrcAlpha), double sided.
// colours mapped through _LegacyToUrp rows (tools/color_fit.py, tongue layer), URP fog.
// Day glow: _CoreGain multiplies the hot core (1 = the legacy value, kept at night). By day OpenZPyroFire raises it, so the
// core passes the day bloom threshold and blooms like the stream. The extra heat starts a little above the root, so the
// flat cut where a tongue meets the ground is not turned into a bright bar.
// MOTION BLUR option (global _OZFxSmear, x > 0 = on): a tongue is drawn out sideways by its own sway and thinned by the same
// share (_Smear x the global shutter), and it fades out and in where its cycle starts over instead of jumping.
// FLAME VARIETY: a share of the tongues (_BookShare, picked per tongue by its seed, so it never changes) is drawn from one of
// four looping flame flipbooks (_Books: four books of 8 x 8 frames side by side, one grey heat channel) through the same
// colour ramp, each with its own start frame, speed, mirror, width and a small lean; the other tongues are the drawn tongue
// as before. The hot core of both is a little lower than it was (_CoreTrim, _BookCore), and every tongue fades softly
// into the ground at its root.
Shader "OpenZ/PyroTongues"
{
    Properties
    {
        _LegacyToUrpR ("Legacy -> URP colour, row R", Vector) = (0.8460, -0.0317, -0.0032, 0)
        _LegacyToUrpG ("Legacy -> URP colour, row G", Vector) = (-0.0046, 0.4042, -0.0016, 0)
        _LegacyToUrpB ("Legacy -> URP colour, row B", Vector) = (-0.0464, -0.1561, 4.2025, 0)
        _Gain ("HDR gain on top (1 = legacy)", Float) = 1
        _CoreGain ("Hot core gain (1 = legacy; raised by day from C#)", Float) = 1
        _Smear ("Motion smoothing: sideways stretch by the sway (x the global shutter, 0 = none)", Float) = 4
        _Books ("Flame flipbooks: 4 books of 8 x 8 frames side by side, heat in R", 2D) = "black" {}
        _BookShare ("Share of the tongues drawn from the flipbooks (0 = none)", Range(0, 1)) = 0.5
        _BookFps ("Flipbook frames per second", Float) = 18
        _BookCore ("Hot core of a flipbook tongue (x the drawn tongue's core)", Float) = 0.8
        _BookLean ("Lean of a flipbook tongue, each way (share of its height)", Float) = 0.16
        _CoreTrim ("Hot core of the drawn tongue (1 = as it was)", Float) = 0.85
        _RootSoft ("Soft fade of a tongue into the ground (share of its height; 0 = the old cut)", Float) = 0.05
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+40" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "PyroTongues"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LegacyToUrpR, _LegacyToUrpG, _LegacyToUrpB;
                float _Gain;
                float _CoreGain;
                float _Smear;
                float _BookShare, _BookFps, _BookCore, _BookLean, _CoreTrim, _RootSoft;
            CBUFFER_END
            TEXTURE2D(_Books); SAMPLER(sampler_Books);
            float _OZPyroTime;
            float4 _OZFxSmear;      // motion smoothing global (MOTION BLUR option): x shutter s (0 = off), y longest stretch (share of the width), z fade (share of a cycle)

            struct Attributes
            {
                float3 base : POSITION;       // aBase (the tongue root on the ground)
                float4 d0 : TEXCOORD0;        // uv.xy (0..1; position = (uv.x - .5, uv.y)), aSeed, aSize
                float4 d1 : TEXCOORD1;        // aSpeed, aKind, pool fade, pool rise
                float4 d2 : TEXCOORD2;        // height scale, size scale, 0, 0
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 v : TEXCOORD1;         // kind, life, seed, fade
                float fog : TEXCOORD2;
                float4 b : TEXCOORD3;         // flipbook: book (0 = the drawn tongue, 1..4), frame 0..64, mirror, 0
            };



            Varyings vert(Attributes i)
            {
                Varyings o;
                float2 uv = i.d0.xy;
                float2 position = float2(uv.x - 0.5, uv.y);
                float aSeed = i.d0.z, aSize = i.d0.w, aSpeed = i.d1.x, aKind = i.d1.y;
                float2 pf = i.d1.zw;
                float hs = i.d2.x, ss = i.d2.y;
                float uTime = _OZPyroTime;
                // uRight: the camera's right axis, flattened to the ground plane (onBeforeRender)
                float3 uRight = UNITY_MATRIX_V[0].xyz; uRight.y = 0.0; uRight = normalize(uRight + float3(1e-5, 0.0, 0.0));
                float life = frac(aSeed + uTime * aSpeed * 0.52);
                float height = lerp(1.1, 3.6, frac(aSeed * 13.73)) * (0.66 + 0.34 * sin(life * 3.14159265)) * pf.y * hs;
                float sway = sin(uTime * 4.1 + aSeed * 41.0 + uv.y * 7.0) + 0.35 * sin(uTime * 6.3 + aSeed * 17.0 + uv.y * 10.0);
                // flame variety: a stable pick per tongue; a flipbook tongue is as wide as its picture asks (about half its height),
                // leans a little to one side and sways half as far (the picture moves by itself)
                float book = (aKind < 0.5 && frac(aSeed * 7.317 + 0.137) < _BookShare) ? 1.0 + floor(frac(aSeed * 23.71 + 0.31) * 4.0) : 0.0;
                float wide0 = aSize * ss * (0.85 + 0.15 * sin(life * 3.14159265));
                if (book > 0.5)
                {
                    height *= 1.3;                                           // the picture keeps head room over its flames
                    wide0 = lerp(wide0, height * 0.5, 0.65) * (0.82 + 0.4 * frac(aSeed * 31.7));
                    sway = sway * 0.5 + (frac(aSeed * 3.77) - 0.5) * 2.0 * _BookLean * height / max(0.22 * hs, 1e-3);
                }
                float3 p = i.base + uRight * (position.x * wide0 + sway * 0.22 * hs * uv.y * (book > 0.5 ? 1.0 : uv.y));
                p.y += position.y * height;
                float thin = 1.0;
                if (_OZFxSmear.x > 0.0 && _Smear > 0.0 && aKind < 0.5)
                {   // MOTION BLUR option: the tongue is drawn out sideways by its own sway (shutter x speed, most at its tip, at most
                    // _OZFxSmear.y of its width) and thinned by the same share
                    float swayVel = 4.1 * cos(uTime * 4.1 + aSeed * 41.0 + uv.y * 7.0) + 2.205 * cos(uTime * 6.3 + aSeed * 17.0 + uv.y * 10.0);
                    float wide = max(aSize * ss * 0.85, 1e-3);
                    float extra = min(abs(swayVel) * 0.22 * hs * uv.y * uv.y * _OZFxSmear.x * _Smear, wide * _OZFxSmear.y);
                    p += uRight * (position.x * extra);
                    thin = wide / (wide + extra);
                }
                if (aKind > 0.5)
                {   // embers
                    life = frac(aSeed + uTime / 1.1);
                    float size = lerp(0.12, 0.035, life) * pf.x;
                    p = i.base + uRight * (position.x * size + sin(aSeed * 31.0) * 0.5 * hs * life);
                    p.y += 0.2 + (0.5 * life + 1.55 * life * life) * hs + position.y * size;
                }
                o.positionCS = pf.x <= 0.0 ? float4(2.0, 2.0, 2.0, 1.0) : TransformWorldToHClip(p);
                o.uv = uv;
                o.v = float4(aKind, life, aSeed, pf.x * thin);
                o.b = float4(book, frac(aSeed * 0.731 + uTime * _BookFps * (0.8 + 0.4 * frac(aSeed * 9.1)) / 64.0) * 64.0, step(0.5, frac(aSeed * 5.13)), 0.0);
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float3 ToUrp(float3 c) { return max(0.0, float3(dot(_LegacyToUrpR.xyz, c), dot(_LegacyToUrpG.xyz, c), dot(_LegacyToUrpB.xyz, c))) * _Gain; }

            // one frame of one book: cells of 128 x 256 in a 4096 x 2048 picture, frame 0 at the top left of its book
            float BookHeat(float bk, float frame, float2 cuv)
            {
                float row = floor(frame / 8.0), colm = frame - row * 8.0;
                float2 uv = float2((bk * 8.0 + colm + cuv.x) / 32.0, 1.0 - (row + 1.0 - cuv.y) / 8.0);
                return SAMPLE_TEXTURE2D(_Books, sampler_Books, uv).r;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float vKind = i.v.x, vLife = i.v.y, vSeed = i.v.z, vFade = i.v.w;
                if (vKind > 0.5)
                {
                    float r = length(i.uv - 0.5), ea = (1.0 - smoothstep(0.12, 0.5, r)) * (1.0 - vLife) * vFade;
                    if (ea < 0.012) discard;
                    return half4(MixFog(ToUrp(float3(1.8, 0.35, 0.008)), i.fog), ea);
                }
                float3 bottom = float3(1.35, 0.35, 0.015);
                float3 middle = float3(1.1, 0.12, 0.004);
                float3 top = float3(0.45, 0.024, 0.001);
                if (i.b.x > 0.5)
                {   // a flipbook tongue: two neighbouring frames blended, the heat through the same colours as the drawn tongue
                    float yb = i.uv.y;
                    float2 cuv = float2(clamp(i.b.z > 0.5 ? 1.0 - i.uv.x : i.uv.x, 0.008, 0.992), clamp(yb, 0.004, 0.996));
                    float f0 = floor(i.b.y), f1 = f0 + 1.0; if (f1 > 63.5) f1 = 0.0;
                    float h = lerp(BookHeat(i.b.x - 1.0, f0, cuv), BookHeat(i.b.x - 1.0, f1, cuv), i.b.y - f0);
                    float ab = smoothstep(0.02, 0.20, h) * smoothstep(0.0, max(_RootSoft * 2.0, 1e-4), yb) * (0.80 + 0.20 * sin(vSeed * 93.0 + vLife * 22.0));
                    ab *= smoothstep(0.0, 0.10, vLife) * (1.0 - smoothstep(0.86, 1.0, vLife)) * vFade * 0.80;
                    if (ab < 0.012) discard;
                    float3 cb = lerp(top, middle, smoothstep(0.02, 0.14, h));
                    cb = lerp(cb, bottom, smoothstep(0.10, 0.32, h));
                    float coreB = smoothstep(0.30, 0.70, h) * (1.0 - smoothstep(0.30, 0.98, yb));
                    cb += float3(12.0, 8.2, 0.48) * (coreB * _BookCore * (1.0 + (_CoreGain - 1.0) * smoothstep(0.02, 0.22, yb)));
                    return half4(MixFog(ToUrp(cb * 1.12), i.fog), min(ab, 0.78));
                }
                float2 q = float2(i.uv.x, 1.0 - i.uv.y);
                float y = 1.0 - q.y;
                float dx = abs(q.x - 0.5 + 0.07 * sin(vSeed * 43.0 + vLife * 17.0 + y * 12.0) * y * y);
                float width = lerp(0.46, 0.12, pow(y, 0.85));
                float body = 1.0 - smoothstep(width * 0.22, width + 0.30, dx);
                float vertical = smoothstep(0.0, 0.06, y) * (1.0 - smoothstep(0.70, 1.0, y));
                float2 bq = float2(dx, (y - 0.10) * 1.6);
                float baseGlow = exp(-dot(bq, bq) * 9.0) * (1.0 - smoothstep(0.0, 0.5, y));
                float flicker = 0.78 + 0.22 * sin(vSeed * 93.0 + vLife * 22.0 + y * 11.0);
                float alpha = (body * vertical + baseGlow * 0.7) * flicker * (1.0 - vLife * 0.48) * vFade * 0.60;
                if (_OZFxSmear.x > 0.0 && _Smear > 0.0)
                {   // MOTION BLUR option: the tongue fades out and in where its cycle starts over (_OZFxSmear.z of the cycle each way)
                    float w = max(_OZFxSmear.z, 1e-3);
                    alpha *= smoothstep(0.0, w, vLife) * (1.0 - smoothstep(1.0 - w, 1.0, vLife));
                }
                alpha = min(alpha, 0.78) * smoothstep(0.0, max(_RootSoft, 1e-4), y);          // soft where the tongue meets the ground
                if (alpha < 0.012) discard;
                float3 col = lerp(bottom, middle, smoothstep(0.06, 0.50, y));
                col = lerp(col, top, smoothstep(0.45, 0.95, y));
                float core = exp(-dx * dx * 26.0) * (1.0 - smoothstep(0.05, 0.72, y));
                col += float3(12.0, 8.2, 0.48) * (core * _CoreTrim * (1.0 + (_CoreGain - 1.0) * smoothstep(0.02, 0.22, y)));
                // unpremultiplied warm HDR: only the yellow inner core crosses the bloom threshold
                return half4(MixFog(ToUrp(col * 1.12), i.fog), alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
