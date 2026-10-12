// GroundGrime.cs lays terrain / deck conforming grid meshes (+3 cm) and this shader paints procedural wear on them.
// It MULTIPLIES what is already drawn with a factor that can also lighten: Blend DstColor SrcColor = 2 x src x dst, the shader
// outputs factor / 2 (0.5 = untouched). So the lit, shadowed ground / deck keeps its shading, day and night, and the wear
// never glows. Offset pulls it toward the camera (no z-fight), no depth write, Cull Off, Queue Transparent-149 (after the
// scorch decals, before smoke, water and the selection overlays).
//   uv0 = local metres (x = along the exit / travel axis, y = across), uv1 = (mode, seed), uv2 = (deck half length, half width),
//   colour a = mask (fan / grid edge fade), colour r = deckness (1 on the bridge plates, 0 on the ground).
//   mode 0 = trodden sand at an L02 door, 1 = dusty concrete scuff at an L17 door, 2 = bridge deck + approach.
Shader "OpenZ/Grime"
{
    Properties
    {
        _Strength ("Strength", Range(0, 2)) = 1
        _LightTint ("Trodden sand (lighter, scuffed)", Color) = (1.3, 1.22, 1.1, 1)
        _DarkTint ("Compressed / tyre scuff", Color) = (0.80, 0.76, 0.72, 1)
        _DustTint ("Concrete dust", Color) = (1.5, 1.42, 1.28, 1)
        _DeckSand ("Sand drift on the steel deck", Color) = (1.34, 1.12, 0.86, 1)
        _RoadSand ("Sand drift on the approach", Color) = (1.28, 1.1, 0.86, 1)
        _Scratch ("Bright metal scratch", Color) = (1.32, 1.32, 1.34, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-149" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "GrimeMultiply2x"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor SrcColor
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half _Strength; half4 _LightTint, _DarkTint, _DustTint, _DeckSand, _RoadSand, _Scratch;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 uv1 : TEXCOORD1; float2 uv2 : TEXCOORD2; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 md : TEXCOORD1; float2 hl : TEXCOORD2; half4 col : COLOR; half fog : TEXCOORD3; };
            V vert(A i)
            {
                V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.md = i.uv1; o.hl = i.uv2; o.col = i.col;
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }
            float h21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vn(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
            }
            float fbm(float2 p) { return vn(p) * 0.55 + vn(p * 2.03 + 7.1) * 0.3 + vn(p * 4.1 + 3.7) * 0.15; }
            // anti-aliased thin feature: 1 inside half-width w of distance d, faded when it gets thinner than a pixel (no shimmer)
            float thinLine(float d, float w) { float aa = max(fwidth(d), 1e-4); return saturate((w - d) / aa + 0.5) * saturate(w / aa); }

            half3 DoorWear(float2 p, float seed, half m, bool city)
            {
                float n = fbm(p * 0.5 + seed), n3 = fbm(p * 1.4 + seed * 2.1);
                // the trodden fan (m = ground factor only): widens away from the door, lumpy soft edges, never a disc or a mat
                float halfW = 1.0 + 0.45 * max(p.x, 0);
                float fan = smoothstep(-0.3, 0.7, p.x) * (1 - smoothstep(1.2, 4.6, p.x + (n - 0.5) * 1.8))
                          * (1 - smoothstep(halfW * 0.25, halfW * 1.1, abs(p.y) + (n3 - 0.5) * 1.0 + (n - 0.5) * 0.6));
                float blob = smoothstep(0.08, 0.8, fan + (n - 0.5) * 0.5) * smoothstep(0.02, 0.5, m);
                half3 f = lerp(half3(1, 1, 1), city ? _DustTint.rgb : _LightTint.rgb, blob * (city ? (0.75 + 0.45 * n3) : (0.5 + 0.7 * n3)));
                // a few soft disturbed clumps
                float sp = smoothstep(0.78, 0.9, vn(p * 3.2 + seed * 5.3)) * blob;
                f *= lerp(half3(1, 1, 1), _DarkTint.rgb * 1.12, sp * 0.3);
                // one tyre / track scuff pair leaving the door, slightly curving, fading out
                float bend = (h21(float2(seed, 1.7)) - 0.5) * 0.18;
                float c = p.y - bend * p.x * p.x;
                float gauge = 0.78 + 0.12 * h21(float2(seed, 4.2));
                float lane = saturate(1 - abs(abs(c) - gauge) / 0.2);
                float laneA = smoothstep(0.0, 0.5, p.x) * (1 - smoothstep(1.8, 4.2, p.x + (n - 0.5) * 1.2)) * smoothstep(0.1, 0.5, m);
                float tread = 0.55 + 0.45 * sin(p.x * 6.2831 / 0.2);
                float broken = smoothstep(0.25, 0.6, vn(float2(p.x * 1.6, c * 3 + seed)));
                f *= lerp(half3(1, 1, 1), city ? _DarkTint.rgb * 0.7 : _DarkTint.rgb, saturate(lane * laneA * broken) * (city ? 0.85 : 0.55) * (0.65 + 0.35 * tread));
                return f;
            }

            half3 BridgeWear(float2 p, float seed, half deck, float halfLen, float halfW, half m)
            {
                float endDist = halfLen - abs(p.x);                                                 // > 0 on the deck, < 0 past its ends
                float n = fbm(float2(p.x * 0.7, p.y * 1.3) + seed), n2 = fbm(p * 2.4 + seed * 2.3);
                // sand / dust drifts on the deck ends and the ramps, spilling onto the approach and fading into the road / ground:
                // tongues (the edge wanders across the width), thicker at the kerbs, thin in the wheel lanes
                float edge = endDist + (n - 0.5) * 2.8 + (n2 - 0.5) * 0.8;
                float drift = smoothstep(1.4, -0.6, edge) * smoothstep(-3.4, -0.6, endDist + (n - 0.5) * 1.6);
                drift *= lerp(0.45 + 0.55 * n2, 1, smoothstep(0.4, 0.95, abs(p.y) / max(halfW, 0.1)));
                float kerb = smoothstep(0.7, 0.05, halfW - abs(p.y) + (n2 - 0.5) * 0.6) * 0.5;      // a little sand along the kerbs
                float sand = saturate((max(drift, kerb * step(0.5, deck)) - 0.3 + (n2 - 0.5) * 0.7) * 1.9) * 0.8;
                sand *= smoothstep(0.15, 0.7, m + (n2 - 0.5) * 0.5);                                 // soft, broken outline
                half3 f = lerp(half3(1, 1, 1), lerp(_RoadSand.rgb, _DeckSand.rgb, deck), sand);
                half3 f0 = f;                                                                        // branch-free (fwidth below)
                {
                    // light dust film + darker worn lanes where the wheels run
                    f *= lerp(half3(1, 1, 1), half3(1.05, 1.04, 1.02), smoothstep(0.4, 0.85, fbm(p * 1.3 + seed * 1.9)) * 0.6);
                    float lanes = saturate(1 - abs(abs(p.y) - 1.35) / 0.5) * (0.55 + 0.45 * vn(float2(p.x * 0.7, p.y * 2 + seed)));
                    f *= lerp(half3(1, 1, 1), half3(0.88, 0.87, 0.86), lanes * 0.8);
                    // small random metal scratches (bright, a few cm wide), deterministic per bridge
                    float2 cell = floor(p / 0.9), q = p - (cell + 0.5) * 0.9;
                    float s = 0;
                    [unroll] for (int k = 0; k < 2; k++)
                    {
                        float2 id = cell + seed * 0.13 + k * 17.3;
                        float on = step(h21(id), 0.42);
                        float a = (h21(id + 3.1) - 0.5) * (h21(id + 9.4) < 0.7 ? 0.6 : 3.1);        // mostly along the travel, some across
                        float2 dir = float2(cos(a), sin(a)), c = (float2(h21(id + 1.3), h21(id + 2.7)) - 0.5) * 0.5;
                        float len = 0.18 + 0.32 * h21(id + 5.9);
                        float2 r = q - c; float t = clamp(dot(r, dir), -len, len);
                        float d = length(r - dir * t);
                        s = max(s, on * thinLine(d, 0.025 + 0.015 * h21(id + 7.7)) * (1 - abs(t) / len * 0.6));
                    }
                    f = lerp(f, f * _Scratch.rgb, s * 0.85);
                    // a few darker scuffs
                    float sc = smoothstep(0.8, 0.9, vn(p * 2.2 + seed * 4.7));
                    f *= lerp(half3(1, 1, 1), half3(0.86, 0.85, 0.84), sc * 0.7);
                }
                return lerp(f0, f, step(0.5, deck));
            }

            half4 frag(V i) : SV_Target
            {
                half m = i.col.a;
                float mode = i.md.x, seed = i.md.y;
                half3 f = mode < 1.5 ? DoorWear(i.uv, seed, m, mode > 0.5) : BridgeWear(i.uv, seed, i.col.r, i.hl.x, i.hl.y, m);
                f = lerp(half3(1, 1, 1), f, saturate(_Strength * smoothstep(0.0, 0.25, m)));
                f = MixFogColor(f, half3(1, 1, 1), i.fog);
                return half4(f * 0.5, 0.5);
            }
            ENDHLSL
        }
    }
}
