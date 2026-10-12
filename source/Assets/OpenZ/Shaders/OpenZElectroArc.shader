// OpenZ/ElectroArc (electrobot_v1): every electric FX of the Electrobot in ONE additive HDR pass: the tesla bolts, their forks, the
// ground strikes, the crawling stun arcs, the coil / emitter crackle (camera-facing ribbons built on the CPU by ElectroArcs.cs), the
// charge / impact glows (camera-facing quads, round profile), the shock ring and the stun timer ring on the ground and the hot flash
// of the Lichtenberg scorch (conforming grids, textured).
//  * ribbon profile across the strip (uv.y -1..1): a white-hot core (Gaussian, width = core fraction of the ribbon) over a coloured
//    glow falloff; both HDR (core x12-20, glow x3-5: over URP's bloom threshold by day and at night, like the pyro jet), with a
//    travelling bead flicker along the arc (hash noise, scrolled by time) so a held arc never reads as a static neon tube.
//  * vertex stream: POSITION world; COLOR rgb = glow colour (linear), a = strength 0..1; TEXCOORD0 = (along 0..1, across -1..1);
//    TEXCOORD1 = (core fraction, core gain, glow gain, seed); TEXCOORD2.x = mode (0 ribbon, 1 round glow, 2 ring band, 3 textured,
//    4 soft aura ribbon: a wide Gaussian across with a faint bead, 5 ground disc: brightest at the centre, uv.y -1 centre .. 1 rim).
//  * additive (One One), no depth write, a short soft-depth fade where an arc meets a hull or the ground (no hard seam lines), URP fog
//    like OpenZ/PyroFlame (an additive FX loses what the fog hides). Our own shader, no third-party code.
Shader "OpenZ/ElectroArc"
{
    Properties
    {
        _MainTex ("Mask (mode 3: Lichtenberg flash, alpha)", 2D) = "white" {}
        _CoreColor ("Core colour (linear)", Color) = (1, 1, 1, 1)
        _Gain ("Overall HDR gain", Float) = 1
        _SoftDepth ("Soft depth fade (m)", Float) = 0.12
        _BeadSpeed ("Bead scroll speed", Float) = 9
        _BeadAmount ("Bead flicker amount", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+60" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "ElectroArc"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _CoreColor;
                float _Gain, _SoftDepth, _BeadSpeed, _BeadAmount;
            CBUFFER_END

            struct Attributes
            {
                float3 positionWS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 d1 : TEXCOORD1;
                float4 d2 : TEXCOORD2;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 d1 : TEXCOORD1;
                float4 b : TEXCOORD2;          // mode, fog factor, view depth, unused
                float4 screenPos : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformWorldToHClip(i.positionWS);
                o.color = i.color; o.uv = i.uv; o.d1 = i.d1;
                float3 vp = TransformWorldToView(i.positionWS);
                o.b = float4(i.d2.x, ComputeFogFactor(o.positionCS.z), -vp.z, 0);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            float Hash11(float p) { p = frac(p * 0.1031); p *= p + 33.33; p *= p + p; return frac(p); }
            float Noise1(float x) { float i = floor(x), f = frac(x); float u = f * f * (3.0 - 2.0 * f); return lerp(Hash11(i), Hash11(i + 1.0), u); }

            half4 frag(Varyings i) : SV_Target
            {
                float mode = i.b.x;
                float coreFrac = max(i.d1.x, 0.02), coreGain = i.d1.y, glowGain = i.d1.z, seed = i.d1.w;
                float3 glowCol = i.color.rgb;
                float strength = i.color.a;
                float core, glow;
                if (mode < 0.5)
                {   // ribbon: across profile + beads along the arc
                    float d = saturate(abs(i.uv.y));
                    float c = d / coreFrac;
                    core = exp(-c * c * 2.2);
                    glow = pow(1.0 - d, 2.4) * (0.55 + 0.45 * exp(-d * d * 6.0));
                    float bead = Noise1(i.uv.x * 11.0 + seed * 17.0 - _Time.y * _BeadSpeed) * 0.6 + Noise1(i.uv.x * 29.0 + seed * 5.0 + _Time.y * _BeadSpeed * 1.7) * 0.4;
                    float beadK = 1.0 + _BeadAmount * (bead * 2.0 - 1.0);
                    core *= beadK; glow *= lerp(1.0, beadK, 0.6);
                }
                else if (mode < 1.5)
                {   // round glow (charge ball, flash, halo): uv = -1..1 in both axes
                    float r = saturate(length(i.uv));
                    float c = r / coreFrac;
                    core = exp(-c * c * 2.5);
                    glow = pow(1.0 - r, 2.2);
                }
                else if (mode < 2.5)
                {   // ring band on the ground: uv.y = -1 (inner edge) .. 1 (outer edge), uv.x = 0..1 around (arc fill in d1.w)
                    float d = saturate(abs(i.uv.y));
                    core = exp(-d * d / max(coreFrac * coreFrac, 1e-4) * 2.0);
                    glow = pow(1.0 - d, 1.8);
                    if (seed < 0.999 && i.uv.x > seed) { core = 0; glow = 0; }   // the stun timer: the ring drains
                }
                else if (mode < 3.5)
                {   // textured decal flash (Lichtenberg fern): mask alpha x vertex strength
                    half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                    core = t.a * t.a; glow = t.a;
                }
                else if (mode < 4.5)
                {   // soft aura around a main bolt: a Gaussian across (no hard edge at the ribbon's side), a faint slow bead
                    float d = saturate(abs(i.uv.y));
                    core = 0.0;
                    glow = exp(-d * d * 3.2) * (1.0 - d * d);
                    float bead = Noise1(i.uv.x * 5.0 + seed * 3.0 - _Time.y * _BeadSpeed * 0.5);
                    glow *= 0.85 + 0.3 * bead;
                }
                else
                {   // ground disc (the blue ground wash): radius share rr = 0 at the centre .. 1 at the rim
                    float rr = saturate((i.uv.y + 1.0) * 0.5);
                    core = exp(-rr * rr / max(coreFrac * coreFrac, 1e-4) * 2.0);
                    glow = pow(1.0 - rr, 1.6);
                }
                float3 col = (_CoreColor.rgb * core * coreGain + glowCol * glow * glowGain) * strength * _Gain;
                if (_SoftDepth > 0.0 && (mode < 1.5 || (mode > 3.5 && mode < 4.5)))
                {
                    float2 suv = i.screenPos.xy / max(i.screenPos.w, 1e-5);
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    col *= saturate((sceneEye - i.b.z) / _SoftDepth);
                }
                float fogVis = MixFog(float3(1.0, 1.0, 1.0), i.b.y).r - MixFog(float3(0.0, 0.0, 0.0), i.b.y).r;
                col *= fogVis;
                if (max(col.r, max(col.g, col.b)) < 0.0005) discard;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
