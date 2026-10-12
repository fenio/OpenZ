// vertices pushed along the normal by 3-octave simplex FBM scrolling upward, the eroding alpha
// (smoothstep(1 - opacity, 1.2 - opacity, density)) and the rolling bands.
//
// The colour is no longer "lit smoke -> orange crust -> core" x one emission gain, alpha-blended (the clay look:
// every hot part went through the same (1 + E) gain, ACES squeezed them all into one pastel cream, the fake lighting shaded
// it like a solid, and the gain had to stay low so it never went white). Now:
//  - a TEMPERATURE per pixel: T = noise density + hot centre (cap centre / facing, the stem's core) + the timeline heat
//    (_Heat 1 at the blast -> ~.2 cold), so the core is white-hot and the limb and troughs fall to orange, deep orange,
//    ember, smoke; the hot centre cools and shrinks over time.
//  - a blackbody-like ramp in LINEAR HDR set from C# with SetVector (never SetColor: that gamma-decodes): white-hot
//    (1,.72,.42) > hot (1,.26,.045) > fire (1,.10,.012) > deep orange (1,.05,.007) > ember (.6,.035,.006), times _GlowGain
//    (7 at the blast, ~3 while it rises, ~1.9 at the end). The G/R ratios were chosen through URP's ACES (tools/nuke_model.py,
//    pastel; only the small white-hot centre goes white.
//  - the fire is EMISSIVE and not lit (no clay shading); only the smoke is lit (and dimmed at night by _SmokeLight).
//  - premultiplied blend (Blend One OneMinusSrcAlpha): rgb = smoke x occlusion + fire emission, alpha = occlusion.
//    Cool fire occludes (.95, saturated orange over the bright sand), the white-hot core is mostly additive (.3): it glows
//    over whatever is behind it.
//  - a hot limb (fresnel x _RimGlow, fixed orange) and the rolling bands on the emission.
// nuke_fx_v3b (first real gate run, L17): the white band was never reached by the noise alone after ~1 s and the warm
// "white" (1,.72,.42) x 3 tone-maps to luma ~240, below the 245 white-hot bar. Now an explicit shrinking white-hot CORE
// (_CoreRadius from C#, over the cap's top / the side facing us, the stem's axis) lifts T, and the white band is a neutral
// (1,.88,.7) x _WhiteGain (2.2): x3 gain = 6.6 linear = luma ~249 under URP ACES (ACES(1.0) is only 213).
// Transparent, no depth write, no fog. Materials are created at run time by NukeFx (never an asset a bulk pass can touch).
Shader "OpenZ/Nuke"
{
    Properties
    {
        _ColorWhite ("White-hot (linear)", Vector) = (1, 0.88, 0.7, 1)
        _WhiteGain ("Extra gain on the white-hot band", Float) = 2.2
        _CoreRadius ("White-hot core radius (cap object xz, 0-1)", Float) = 0.6
        _CoreBoost ("Temperature added in the core", Float) = 0.4
        _LimbCool ("v3c: temperature taken off the cap's limb", Float) = 0
        _LimbDim ("v3c: emission kept at the limb (G falls faster)", Float) = 1
        _ColorHot ("Hot (linear)", Vector) = (1, 0.26, 0.045, 1)
        _ColorFire ("Fire (linear)", Vector) = (1, 0.10, 0.012, 1)
        _ColorDeep ("Deep orange (linear)", Vector) = (1, 0.05, 0.007, 1)
        _ColorEmber ("Ember (linear)", Vector) = (0.6, 0.035, 0.006, 1)
        _ColorSmoke ("Smoke albedo (linear)", Vector) = (0.10, 0.085, 0.07, 1)
        _GlowGain ("HDR gain on the fire (linear)", Float) = 3
        _Heat ("Timeline heat (1 blast .. 0 cold)", Float) = 1
        _NoiseTemp ("Temperature from noise density", Float) = 1.6
        _CentreTemp ("Temperature from the hot centre", Float) = 0.9
        _OccCool ("Occlusion of cool fire / smoke", Float) = 0.95
        _OccHot ("Occlusion of white-hot fire (rest additive)", Float) = 0.3
        _SmokeLight ("Smoke brightness (sun + ambient, 0-1)", Float) = 0.9
        _RimGlow ("Hot limb (fresnel) gain", Float) = 0.9
        _NoiseScale ("Noise scale", Float) = 1.8
        _Distortion ("Distortion", Float) = 0.5
        _Opacity ("Opacity", Float) = 0.85
        _Progress ("Progress", Float) = 0
        _ShapeId ("Shape (0 cap, 1 stem)", Float) = 0
        _NukeTime ("Time", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float4 _ColorWhite, _ColorHot, _ColorFire, _ColorDeep, _ColorEmber, _ColorSmoke;
            float _GlowGain, _Heat, _NoiseTemp, _CentreTemp, _OccCool, _OccHot, _SmokeLight, _RimGlow, _WhiteGain, _CoreRadius, _CoreBoost, _LimbCool, _LimbDim;
            float _NoiseScale, _Distortion, _Opacity, _Progress, _ShapeId, _NukeTime;
            CBUFFER_END

            float3 mod289(float3 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float4 mod289(float4 x) { return x - floor(x * (1.0 / 289.0)) * 289.0; }
            float4 permute(float4 x) { return mod289(((x * 34.0) + 1.0) * x); }
            float4 taylorInvSqrt(float4 r) { return 1.79284291400159 - 0.85373472095314 * r; }
            float snoise(float3 v)
            {
                const float2 C = float2(1.0 / 6.0, 1.0 / 3.0);
                const float4 D = float4(0.0, 0.5, 1.0, 2.0);
                float3 i = floor(v + dot(v, C.yyy));
                float3 x0 = v - i + dot(i, C.xxx);
                float3 g = step(x0.yzx, x0.xyz);
                float3 l = 1.0 - g;
                float3 i1 = min(g.xyz, l.zxy);
                float3 i2 = max(g.xyz, l.zxy);
                float3 x1 = x0 - i1 + C.xxx;
                float3 x2 = x0 - i2 + C.yyy;
                float3 x3 = x0 - D.yyy;
                i = mod289(i);
                float4 p = permute(permute(permute(i.z + float4(0.0, i1.z, i2.z, 1.0)) + i.y + float4(0.0, i1.y, i2.y, 1.0)) + i.x + float4(0.0, i1.x, i2.x, 1.0));
                float n_ = 0.142857142857;
                float3 ns = n_ * D.wyz - D.xzx;
                float4 j = p - 49.0 * floor(p * ns.z * ns.z);
                float4 x_ = floor(j * ns.z);
                float4 y_ = floor(j - 7.0 * x_);
                float4 x = x_ * ns.x + ns.yyyy;
                float4 y = y_ * ns.x + ns.yyyy;
                float4 h = 1.0 - abs(x) - abs(y);
                float4 b0 = float4(x.xy, y.xy);
                float4 b1 = float4(x.zw, y.zw);
                float4 s0 = floor(b0) * 2.0 + 1.0;
                float4 s1 = floor(b1) * 2.0 + 1.0;
                float4 sh = -step(h, float4(0.0, 0.0, 0.0, 0.0));
                float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
                float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
                float3 p0 = float3(a0.xy, h.x);
                float3 p1 = float3(a0.zw, h.y);
                float3 p2 = float3(a1.xy, h.z);
                float3 p3 = float3(a1.zw, h.w);
                float4 norm = taylorInvSqrt(float4(dot(p0, p0), dot(p1, p1), dot(p2, p2), dot(p3, p3)));
                p0 *= norm.x; p1 *= norm.y; p2 *= norm.z; p3 *= norm.w;
                float4 m = max(0.6 - float4(dot(x0, x0), dot(x1, x1), dot(x2, x2), dot(x3, x3)), 0.0);
                m = m * m;
                return 42.0 * dot(m * m, float4(dot(p0, x0), dot(p1, x1), dot(p2, x2), dot(p3, x3)));
            }
            float fbm(float3 x)
            {
                float v = 0.0, a = 0.5; float3 shift = float3(100.0, 100.0, 100.0);
                [unroll] for (int i = 0; i < 3; ++i) { v += a * snoise(x); x = x * 2.0 + shift; a *= 0.5; }
                return v;
            }

            // temperature ramp (linear HDR before _GlowGain): ember < deep orange < fire < hot < white-hot
            float3 Ramp(float T)
            {
                float3 c = _ColorEmber.rgb * smoothstep(0.10, 0.30, T);
                c = lerp(c, _ColorDeep.rgb, smoothstep(0.30, 0.45, T));
                c = lerp(c, _ColorFire.rgb, smoothstep(0.45, 0.62, T));
                c = lerp(c, _ColorHot.rgb, smoothstep(0.62, 0.80, T));
                return lerp(c, _ColorWhite.rgb * _WhiteGain, smoothstep(0.90, 0.985, T));
            }

            struct A { float4 pos : POSITION; float3 n : NORMAL; };
            struct V { float4 pos : SV_POSITION; float3 vpos : TEXCOORD0; float3 vn : TEXCOORD1; float noise : TEXCOORD2; float3 opos : TEXCOORD3; };

            V vert(A i)
            {
                V o;
                float3 nc = i.pos.xyz * _NoiseScale; nc.y -= _NukeTime * 0.3;
                float n = fbm(nc);
                float disp = n * _Distortion; if (_ShapeId > 0.5) disp *= 0.5;
                float3 p = i.pos.xyz + i.n * disp;
                float3 wp = TransformObjectToWorld(p);
                o.pos = TransformWorldToHClip(wp);
                o.vpos = TransformWorldToView(wp);
                o.vn = normalize(mul((float3x3)UNITY_MATRIX_V, TransformObjectToWorldNormal(i.n)));
                o.noise = n; o.opos = i.pos.xyz;
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 vn = normalize(i.vn);
                float3 viewDir = normalize(-i.vpos);
                float ndv = saturate(dot(viewDir, vn));
                float density = saturate(i.noise * 0.5 + 0.5);
                float cover = smoothstep(1.0 - _Opacity, 1.2 - _Opacity, density);
                if (cover < 0.004) return half4(0, 0, 0, 0);

                // temperature: noise density + hot centre (cap: centre of the cap and the side facing us; stem: its core) + timeline
                float centre = _ShapeId > 0.5 ? ndv : saturate(1.0 - length(i.opos.xz)) * 0.6 + ndv * 0.4;
                // v3b: the white-hot core, shrinking with _CoreRadius (cap: around the top pole, facing us; stem: its axis)
                float coreMask = _ShapeId > 0.5 ? smoothstep(0.55, 0.95, ndv) * 0.7
                               : (1.0 - smoothstep(_CoreRadius * 0.45, _CoreRadius, length(i.opos.xz))) * smoothstep(0.2, 0.6, ndv);
                float T = saturate((density - 0.5) * _NoiseTemp + 0.5 + (centre - 0.5) * _CentreTemp + (_Heat - 0.5) + coreMask * _CoreBoost);
                // v3c: deep-orange limb around the white core (cap only: _LimbCool / _LimbDim are 0 / 1 on the stem)
                float limb = _ShapeId > 0.5 ? 0.0 : smoothstep(0.55, 0.95, length(i.opos.xz));
                T = saturate(T - limb * _LimbCool * (1.0 - coreMask));
                float3 fire = Ramp(T);
                float dim = lerp(1.0, _LimbDim, limb);
                fire *= float3(dim, dim * dim, dim * dim);
                float fireMask = smoothstep(0.12, 0.30, T);

                // rolling fire bands scrolling out over the cap (and up the stem): reads as a rolling mushroom from the RTS camera
                float radial = _ShapeId > 0.5 ? i.opos.y * 3.0 : length(i.opos.xz) * 4.5 - i.opos.y * 1.5;
                float roll = 0.5 + 0.5 * sin(radial * 3.14159 - _NukeTime * 7.0 + i.noise * 2.0);
                float fres = pow(1.0 - ndv, 2.5);
                // hot limb: backlit fire along the silhouette in a fixed orange (not the ramp: the limb is cooler, it would go dull)
                float3 emission = fire * (_GlowGain * (0.8 + 0.35 * roll)) + (0.5 * (_ColorDeep.rgb + _ColorFire.rgb)) * (fres * _RimGlow * _GlowGain);

                // smoke only is lit (soft wrap from above), glowing faintly from the fire inside
                float3 lightDir = normalize(float3(0.5, 1.0, 0.5));
                float lighting = dot(vn, lightDir) * 0.5 + 0.5;
                float3 smoke = _ColorSmoke.rgb * ((0.55 + 0.45 * lighting) * _SmokeLight) + _ColorEmber.rgb * (_GlowGain * 0.06 * (1.0 - T));

                float occl = lerp(_OccCool, _OccHot, smoothstep(0.72, 0.97, T));
                float3 rgb = (smoke * (occl * (1.0 - fireMask)) + emission) * cover;
                return half4(rgb, occl * cover);
            }
            ENDHLSL
        }
    }
}
