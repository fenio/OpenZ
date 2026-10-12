// OpenZ/PyroGround: the burning ground surface of the flamer fire of our earlier projects, ported from its napalm ground shader
// and terrain hook (diffuse x (1 - napalm.a), emissive += napalm.rgb x napalm.a x 5.2). There it is injected into the terrain
// material; OpenZ draws it as a ground-conforming grid 4 cm over the terrain (Offset -1, no
// z-fight) with Blend One OneMinusSrcAlpha, which is exactly dst x (1 - a) + emissive: the same result on the lit ground.
//  * seeded three-lobe footprint (the same shape that places the tongues), near-black char, a thin hot edge, hot flowing spots
//    (two scrolling fBm octaves), glowing cracks, and the HDR orange heart sqrt(mask) x (2.1, .65, .02);
//  * OpenZ: a soft warm glow on the ground around the patch (_OZPatch.z: full when the light budget gave the patch no real light,
//    colour mapped through _LegacyToUrp rows (tools/color_fit.py, ground layer), URP fog.
Shader "OpenZ/PyroGround"
{
    Properties
    {
        _LegacyToUrpR ("Legacy -> URP colour, row R", Vector) = (0.5239, -0.0115, -0.0012, 0)
        _LegacyToUrpG ("Legacy -> URP colour, row G", Vector) = (-0.0013, 0.2018, -0.0005, 0)
        _LegacyToUrpB ("Legacy -> URP colour, row B", Vector) = (-0.0269, -0.0904, 4.1172, 0)
        _Gain ("HDR gain on top (1 = legacy)", Float) = 1
        _GlowColor ("Ground glow colour (linear, HDR)", Color) = (1.0, 0.242, 0.024, 1)
        _GlowGain ("Ground glow strength", Float) = 0.9
        _GlowFalloff ("Ground glow falloff", Float) = 5.0
        [HideInInspector] _OZPatch ("Patch (fadeZ, seed, glow, fade)", Vector) = (1, 0, 1, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+4" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "PyroGround"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _LegacyToUrpR, _LegacyToUrpG, _LegacyToUrpB;
                float _Gain;
                float4 _GlowColor;
                float _GlowGain, _GlowFalloff;
                float4 _OZPatch;
            CBUFFER_END
            float _OZPyroTime;

            struct Attributes { float3 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 m : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(i.positionOS));
                o.uv = i.uv;
                o.m = float2(i.color.r, i.color.a);          // distance / glow radius, dry mask
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float V3NapalmHash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float V3NapalmNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(V3NapalmHash(i), V3NapalmHash(i + float2(1, 0)), f.x), lerp(V3NapalmHash(i + float2(0, 1)), V3NapalmHash(i + float2(1, 1)), f.x), f.y);
            }
            float V3NapalmFbm(float2 p)
            {
                float v = 0.0, a = 0.5;
                [unroll] for (int k = 0; k < 5; k++) { v += a * V3NapalmNoise(p); p = p * 2.03 + 13.17; a *= 0.5; }
                return v;
            }
            float V3NapalmBlob(float2 p, float2 c, float2 s) { float2 q = (p - c) / s; return exp(-dot(q, q) * 2.25); }

            // v3NapalmSurface(uv, time, fade, seed)
            float4 V3NapalmSurface(float2 vUv, float uTime, float uFade, float uSeed)
            {
                float2 p = (vUv - 0.5) * 2.0;
                float s1 = frac(uSeed * 7.31), s2 = frac(uSeed * 3.97);
                float angle = uSeed * 2.399, c = cos(angle), s = sin(angle);
                float2 q = float2((p.x * c + p.y * s) * 0.92, (-p.x * s + p.y * c) * 1.14);
                float field = V3NapalmBlob(q, float2(-0.34, -0.05), float2(0.55, 0.42))
                            + V3NapalmBlob(q, float2(0.17, 0.12), float2(0.55, 0.46))
                            + V3NapalmBlob(q, float2(0.43, -0.2), float2(0.4, 0.32));
                float shapeAA = max(fwidth(field), 0.10);
                float mask = smoothstep(0.04 - shapeAA, 0.56 + shapeAA, field);
                float edge = (1.0 - smoothstep(0.06, 0.2 + shapeAA, abs(field - 0.3))) * 0.25;
                // hot flowing spots (two scrolling fBm octaves) + glowing cracks
                float flow = V3NapalmFbm(p * 3.6 + float2(uTime * 0.27, -uTime * 0.16) + s1 * 4.0);
                float flow2 = V3NapalmFbm(p * 4.8 + float2(-uTime * 0.14, uTime * 0.29) - s2 * 6.0);
                float hot = mask * smoothstep(0.43, 0.82, flow * 0.72 + flow2 * 0.38);
                float crackPhase = (flow2 + p.x * 0.35) * 11.0;
                float crackAA = max(fwidth(crackPhase), 0.035);
                float cracks = mask * (1.0 - smoothstep(0.24 - crackAA, 0.48 + crackAA, abs(sin(crackPhase)))) * (1.0 - smoothstep(0.35, 1.1, crackAA));
                // near-black char base + HDR hot colours
                float3 col = float3(0.012, 0.006, 0.002);
                col += edge * float3(0.95, 0.11, 0.008) * 0.95;
                col += hot * float3(1.0, 0.24, 0.015) * 1.6;
                col += cracks * float3(1.0, 0.20, 0.008) * 0.45;
                col += sqrt(mask) * float3(2.1, 0.65, 0.02);
                float alpha = max(mask * 0.22, edge * 0.12) * uFade;
                if (alpha < 0.01) return float4(0, 0, 0, 0);
                return float4(col, alpha);
            }

            half4 frag(Varyings i) : SV_Target
            {
                float fadeZ = _OZPatch.x, seed = _OZPatch.y, glow = _OZPatch.z;
                float dry = i.m.y;
                float4 n = V3NapalmSurface(i.uv, _OZPyroTime, fadeZ, seed);
                float3 e = n.rgb * n.a * 5.2;                                              // emissive
                e = max(0.0, float3(dot(_LegacyToUrpR.xyz, e), dot(_LegacyToUrpG.xyz, e), dot(_LegacyToUrpB.xyz, e))) * _Gain;
                // the patch's own warm light on the ground around it (flickers with the fire)
                float r = i.m.x;
                float g = exp(-r * r * _GlowFalloff) * (1.0 - smoothstep(0.75, 1.0, r)) * glow * _GlowGain * (0.8 + 0.2 * fadeZ);
                e += _GlowColor.rgb * g;
                float a = n.a * dry;
                float fogVis = MixFog(float3(1.0, 1.0, 1.0), i.fog).r - MixFog(float3(0.0, 0.0, 0.0), i.fog).r;
                return half4(e * dry * fogVis, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
