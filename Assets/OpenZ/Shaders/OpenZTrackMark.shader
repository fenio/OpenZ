// OpenZ/TrackMark: tyre, tread and foot prints in the sand (TrackMarks.cs). One dynamic mesh of quads in world space:
//   POSITION  world position on the drawn ground + 2 cm            COLOR.a   surface strength (1 sand/dirt, .2 road/concrete)
//             (ribbons are 1-4 quads across, footprints up to 4 x 3 quads, so every vertex hugs the TER_* terrain)
//   TEXCOORD0 x = across the print (0..1 incl. the rim padding), y = metres along a ribbon / 0..1 heel to toe for a foot
//   TEXCOORD1 x = birth, y = life, z = death (all four vertices of a quad), w = kind (0 tyre, 1 tread, 2 robot, 3 mech) + seed
// Look: the print is pressed sand, so it MULTIPLIES the lit ground (Blend DstColor SrcColor = 2 x src x dst, .5 = no change):
// compressed floor a little darker, tread grooves / cleats darker still, the pushed-up rim a touch lighter. It keeps the
// ground's lighting, shadows, AO and night tint and never glows. Fog pulls it back to neutral (like OpenZ/ScorchDecal).
// Life: solid until _FadeStart x life, then it dissolves grain by grain (world-space value noise against a rising threshold,
// per vertex, so a ribbon's older end goes first) and is gone at `life`; expired quads collapse in the vertex shader.
// No z-fighting by construction: ZTest Always + a manual test against the camera depth texture (_CameraDepthTexture, on in
// PC_RPAsset) with a tolerance of _DepthTolerance metres (10 cm on the exact TER_* heights, 18 cm when TrackMarks only has
// the decimated COL_Terrain proxy), so a print may sit a little under the drawn ground and still show, but anything standing
// in front of it (units, rocks, buildings) is never painted. TrackMarks switches to ZTest LEqual + Offset when the pipeline
// has no depth texture (_SceneDepthTest 0).
Shader "OpenZ/TrackMark"
{
    Properties
    {
        _Intensity ("Overall strength", Range(0, 2)) = 1
        _Compress ("Compressed sand darkening", Range(0, 0.6)) = 0.24
        _Groove ("Tread / cleat groove darkening", Range(0, 0.4)) = 0.13
        _Berm ("Pushed-up rim lightening", Range(0, 0.3)) = 0.07
        _BermPad ("Rim padding (fraction of width, = TrackMarks.BermPad)", Range(0, 0.5)) = 0.18
        _FadeStart ("Dissolve starts at this fraction of the life", Range(0, 1)) = 0.3
        _DissolveScale ("Dissolve grain (1/m)", Float) = 2.6
        _DepthTolerance ("Scene depth tolerance (m)", Float) = 0.1
        _SceneDepthTest ("Test against the scene depth texture (1) or the depth buffer (0)", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest (Always with the scene depth test)", Float) = 8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-160" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "TrackMultiply"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor SrcColor
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            Offset -1, -2
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Intensity; half _Compress; half _Groove; half _Berm; half _BermPad; half _FadeStart;
                float _DissolveScale; float _DepthTolerance; half _SceneDepthTest; half _ZTest;
            CBUFFER_END
            float _OZTrackTime;                                    // global, TrackMarks game clock (s)

            struct A { float3 pos : POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; float4 d : TEXCOORD1; };
            struct V
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 wp : TEXCOORD1;
                float4 k : TEXCOORD2;                              // x dissolve progress, y strength, z kind + seed, w eye depth
                half fog : TEXCOORD3;
            };

            V vert(A i)
            {
                V o;
                o.wp = TransformObjectToWorld(i.pos);
                o.pos = TransformWorldToHClip(o.wp);
                float age = _OZTrackTime - i.d.x, life = max(i.d.y, 0.01);
                o.k.x = saturate((age - life * _FadeStart) / max(life * (1.0 - _FadeStart), 0.01));
                o.k.y = i.col.a;
                o.k.z = i.d.w;
                o.k.w = -TransformWorldToView(o.wp).z;
                o.uv = i.uv;
                o.fog = ComputeFogFactor(o.pos.z);
                if (_OZTrackTime >= i.d.z) o.pos = float4(0.0, 0.0, 0.0, 0.0);   // expired or never written: no raster cost
                return o;
            }

            float Hash21(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }
            float SdRoundBox(float2 p, float2 b, float r) { float2 q = abs(p) - b + r; return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r; }
            float SdCapsule(float2 p, float2 a, float2 b, float r) { float2 pa = p - a, ba = b - a; float h = saturate(dot(pa, ba) / dot(ba, ba)); return length(pa - ba * h) - r; }

            // tyre (kind 0) and tank tread (kind 1) ribbons: x across (|x| < .5 = the print, beyond = rim), v = metres along
            void Ribbon(float2 uv, float2 duv, float kind, out float cover, out float groove, out float berm)
            {
                float x = (uv.x - 0.5) * (1.0 + 2.0 * _BermPad), ax = abs(x);
                float aa = max(duv.x * (1.0 + 2.0 * _BermPad), 1e-4);
                cover = 1.0 - smoothstep(0.5 - max(0.06, aa), 0.5 + aa, ax);
                berm = smoothstep(0.46, 0.53, ax) * (1.0 - smoothstep(0.53, 0.5 + _BermPad, ax));
                float v = uv.y, pv = max(duv.y, 1e-4);
                if (kind < 0.5)
                {   // tyre: chevron lugs, uncut centre rib
                    float pitch = 0.21, p = frac((v + ax * 0.34) / pitch);
                    float lug = smoothstep(0.06, 0.14, p) * (1.0 - smoothstep(0.46, 0.54, p)) * smoothstep(0.07, 0.12, ax);
                    lug = lerp(lug, 0.4, saturate(pv / pitch * 2.0 - 0.5));        // far away: the average, no moire
                    groove = cover * lug;
                }
                else
                {   // tank tread: grouser bars every 17 cm with a clean centre gap (guide horns)
                    float pitch = 0.17, p = frac(v / pitch);
                    float bar = smoothstep(0.0, 0.07, p) * (1.0 - smoothstep(0.42, 0.5, p));
                    float gap = 1.0 - smoothstep(0.035, 0.07, ax);
                    bar *= 1.0 - gap;
                    bar = lerp(bar, 0.42, saturate(pv / pitch * 2.0 - 0.5));
                    groove = cover * bar;
                    cover *= 1.0 - 0.45 * gap;
                }
            }

            // robot boot (kind 2) and mech foot (kind 3): q.x across -.5...5, q.y 0 heel .. 1 toe (rim beyond)
            void Foot(float2 uv, float2 duv, float kind, out float cover, out float groove, out float berm)
            {
                float2 q = float2((uv.x - 0.5) * (1.0 + 2.0 * _BermPad), (uv.y - 0.5) * (1.0 + 2.0 * _BermPad) + 0.5);
                float d;
                if (kind < 2.5)
                {   // chunky boot: forefoot pad + heel pad, cleat bars
                    float dFore = SdRoundBox(q - float2(0.0, 0.66), float2(0.44, 0.31), 0.16);
                    float dHeel = SdRoundBox(q - float2(0.0, 0.17), float2(0.36, 0.15), 0.12);
                    d = min(dFore, dHeel);
                    float c1 = frac((q.y - 0.42) / 0.12);
                    float cleat = step(0.4, q.y) * smoothstep(0.1, 0.2, c1) * (1.0 - smoothstep(0.55, 0.65, c1));
                    float heelBar = 1.0 - smoothstep(0.02, 0.05, abs(q.y - 0.17));
                    groove = max(cleat, heelBar) * (1.0 - smoothstep(-0.06, -0.02, d));
                }
                else
                {   // mech: round pad, three claw toes, heel spur; the claws dig in at their tips
                    float dPad = length(q - float2(0.0, 0.36)) - 0.3;
                    float dT0 = SdCapsule(q, float2(0.0, 0.5), float2(0.0, 0.96), 0.1);
                    float dT1 = SdCapsule(q, float2(-0.08, 0.48), float2(-0.36, 0.88), 0.09);
                    float dT2 = SdCapsule(q, float2(0.08, 0.48), float2(0.36, 0.88), 0.09);
                    float dH = SdCapsule(q, float2(0.0, 0.3), float2(0.0, 0.03), 0.08);
                    d = min(min(dPad, dH), min(dT0, min(dT1, dT2)));
                    groove = saturate((1.0 - smoothstep(0.0, 0.1, length(q - float2(0.0, 0.96))))
                           + (1.0 - smoothstep(0.0, 0.09, length(q - float2(-0.36, 0.88))))
                           + (1.0 - smoothstep(0.0, 0.09, length(q - float2(0.36, 0.88)))));
                }
                float aa = max(max(duv.x, duv.y) * (1.0 + 2.0 * _BermPad), 1e-4);   // |grad d| ~ 1 in q space
                cover = 1.0 - smoothstep(-aa, aa, d);
                berm = smoothstep(0.0, 0.02, d) * (1.0 - smoothstep(0.02, 0.1, d));
                groove *= cover;
                if (kind > 2.5) cover *= 1.25;                     // heavier: pressed deeper
            }

            float SceneEyeDepth(float2 suv)
            {
                float raw = SampleSceneDepth(suv);
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        raw = 1.0 - raw;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }

            half4 frag(V i) : SV_Target
            {
                float kind = floor(i.k.z + 0.001), seed = i.k.z - kind;
                float2 duv = fwidth(i.uv);                          // derivatives outside the per-kind branches
                float cover, groove, berm;
                if (kind < 1.5) Ribbon(i.uv, duv, kind, cover, groove, berm);
                else Foot(i.uv, duv, kind, cover, groove, berm);

                // dissolve: sand blows back into the print, grain by grain, the older end first
                float2 w = i.wp.xz * _DissolveScale;
                float n = VNoise(w + seed * 57.0) * 0.62 + VNoise(w * 3.3 + 11.0) * 0.38;
                float thr = i.k.x * 1.15 - 0.1;
                float amt = smoothstep(thr - 0.07, thr + 0.07, n) * (1.0 - 0.3 * i.k.x) * i.k.y * _Intensity;

                if (_SceneDepthTest > 0.5)
                {   // skip where something stands in front of the ground beyond the tolerance
                    float diff = i.k.w - SceneEyeDepth(GetNormalizedScreenSpaceUV(i.pos));
                    amt *= 1.0 - smoothstep(_DepthTolerance * 0.6, _DepthTolerance, diff);
                }
                clip(amt - 0.002);

                float f = 1.0 - cover * _Compress - groove * _Groove + berm * _Berm;
                f = lerp(1.0, f, saturate(amt));
                f = MixFogColor(half3(f, f, f), half3(1.0, 1.0, 1.0), i.fog).x;   // fogged distance: back to neutral
                return half4(f * 0.5, f * 0.5, f * 0.5, 0.5);        // 2 x src x dst: .5 = unchanged ground
            }
            ENDHLSL
        }
    }
    FallBack Off
}
