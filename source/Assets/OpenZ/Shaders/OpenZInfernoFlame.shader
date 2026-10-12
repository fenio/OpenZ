// Tall licking flame columns over the fort ruin. Every column = a vertical billboard rooted
// in the ruin (camera-right flattened to the ground x world-up, so it never turns into a flat disc from the RTS camera), 4-6 m tall:
//  * shape: a tongue that narrows to the top, its edges and tip licked by two upward-scrolling noise layers (legacy-noise), with
//    detached wisps near the top; each column has its own life cycle (height pulse, sway at the tip);
//  * colour: a heat ramp white-yellow core at the base -> orange -> deep red tips, HDR so the hot core blooms;
//  * premultiplied blend (One, OneMinusSrcAlpha): mostly additive light, a little body so it reads over bright sand by day.
// Data (static mesh built by OzFortInferno): POSITION = column root, TEXCOORD0 = (corner u, corner v, seed, width m),
// TEXCOORD1 = (height m, speed, phase, 0). _K = the blaze strength (OzFortInferno.K), _Gain = HDR gain.
// The white stage of the heat ramp is gone (yellow core peaks at HDR ~3, orange ~2, deep red
// tips), every fragment fades against the scene depth (_SoftFade m: no hard line where a column meets rubble or a wall), the
// base fades in over the lowest 18 % instead of 7 %, and _Mode 1 draws the EMBER BED: a flat quad (POSITION = world corner,
// TEXCOORD0 = (u, v, seed, 0)) of slowly crawling glowing cracks, deep red to orange, under the columns.
Shader "OpenZ/InfernoFlame"
{
    Properties
    {
        _MainTex ("Flame flipbook (4x2)", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "gray" {}
        _K ("Strength", Float) = 1
        _Gain ("HDR gain", Float) = 1
        _Body ("Body opacity", Float) = 0.42
        _SoftFade ("Depth fade (m, 0 = off)", Float) = 0.9
        _Mode ("0 flame columns, 1 ember bed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+38" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "InfernoFlame"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
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
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _NoiseTex_ST;
                float _K, _Gain, _Body, _SoftFade, _Mode;
            CBUFFER_END

            struct Attributes
            {
                float3 root : POSITION;
                float4 d0 : TEXCOORD0;   // u, v, seed, width
                float4 d1 : TEXCOORD1;   // height, speed, phase, 0
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 v : TEXCOORD1;    // seed, life, time, height
                float fog : TEXCOORD2;
                float3 positionWS : TEXCOORD3;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float u = i.d0.x, v = i.d0.y, seed = i.d0.z, width = i.d0.w;
                float height = i.d1.x, speed = i.d1.y, phase = i.d1.z;
                float t = _Time.y;
                float life = frac(phase + t * speed * 0.35);
                float3 right = UNITY_MATRIX_V[0].xyz; right.y = 0.0; right = normalize(right + float3(1e-5, 0.0, 0.0));
                float k = saturate(_K);
                float h = height * (0.78 + 0.22 * sin(life * 6.2831853 + seed * 7.0)) * (0.55 + 0.45 * k);
                float w = width * (0.9 + 0.1 * sin(t * 3.1 + seed * 11.0)) * (0.7 + 0.3 * k);
                float sway = (sin(t * 2.3 + seed * 41.0) + 0.45 * sin(t * 5.1 + seed * 17.0 + v * 4.0)) * 0.22 * h * v * v;
                float3 p = i.root + right * ((u - 0.5) * w + sway);
                p.y += v * h;
                if (_Mode > 0.5) p = i.root;                                  // ember bed: the corner is already in world space
                o.positionCS = TransformWorldToHClip(p);
                o.uv = float2(u, v);
                o.v = float4(seed, life, t, h);
                o.fog = ComputeFogFactor(o.positionCS.z);
                o.positionWS = p;
                return o;
            }

            float Soft(Varyings i)
            {
                if (_SoftFade <= 0.0) return 1.0;
                float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float fragEye = -TransformWorldToView(i.positionWS).z;
                return saturate((sceneEye - fragEye) / _SoftFade);
            }

            half4 Bed(Varyings i)
            {
                float seed = i.v.x, t = i.v.z;
                float2 q = i.uv - 0.5;
                float r = length(q) * 2.0;
                float n0 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, q * 0.9 + seed).r;
                float mask = 1.0 - smoothstep(0.35 + 0.3 * n0, 1.0, r);
                if (mask < 0.01) discard;
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, q * 2.1 + float2(seed, t * 0.021)).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, q * 5.3 - float2(t * 0.034, seed * 0.7)).g;
                float cracks = smoothstep(0.42, 0.78, n1 * 0.62 + n2 * 0.48);
                float flick = 0.82 + 0.18 * sin(t * 6.1 + n1 * 9.0) * sin(t * 2.3 + n2 * 5.0);
                float k = saturate(_K);
                float3 col = lerp(float3(0.75, 0.09, 0.012), float3(2.6, 0.78, 0.09), cracks);
                col = lerp(col, float3(3.0, 1.55, 0.3), smoothstep(0.85, 1.0, cracks) * (1.0 - r));
                float glow = mask * (0.3 + 0.7 * cracks) * flick * (0.55 + 0.45 * k) * Soft(i);
                float a = saturate(glow);
                col = MixFog(col * _Gain, i.fog);
                return half4(col * a, a * 0.55);
            }

            half4 frag(Varyings i) : SV_Target
            {
                if (_Mode > 0.5) return Bed(i);
                float seed = i.v.x, life = i.v.y, t = i.v.z, h = i.v.w;
                float u = i.uv.x, v = i.uv.y;
                float rise = t * (1.15 + 0.35 * frac(seed * 3.7));
                // two upward-scrolling noise layers (in metres up the column, so tall columns get more licks)
                float vy = v * h;
                float n1 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * 0.55 + seed, vy * 0.16 - rise * 0.55)).r;
                float n2 = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, float2(u * 1.25 - seed * 0.7, vy * 0.34 - rise * 1.1)).g;
                float warp = (n1 - 0.5) * (0.18 + 0.55 * v);
                float x = abs(u - 0.5 + warp);
                float wv = lerp(0.47, 0.05, pow(saturate(v), 0.75));
                float body = 1.0 - smoothstep(wv * 0.45, wv, x);
                // licks: the top erodes into tongues and wisps
                float erode = saturate(n2 * 0.95 + (1.0 - v) * 0.75 - 0.18);
                float dens = body * smoothstep(0.18, 0.55, erode) * smoothstep(0.0, 0.18, v) * (1.0 - smoothstep(0.86, 1.0, v));
                if (dens < 0.01) discard;
                // flipbook detail: one of the four bright frames, the frame's middle scrolled up through the column
                float fr = floor(frac(seed * 5.3 + t * 0.9) * 4.0);
                float2 fuv = float2((fr + 0.2 + 0.6 * saturate(u + warp * 0.4)) / 4.0, 0.5 + (0.2 + 0.6 * frac(vy * 0.09 - rise * 0.32 + seed)) * 0.5);
                float4 fb = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, fuv);
                float lum = dot(fb.rgb, float3(0.33, 0.45, 0.22));
                // heat: hot at the base and the centre line, cooling to the tips
                float heat = saturate(dens * 1.05 - v * 0.62 + 0.28 + (lum - 0.4) * 0.35 - x * 0.6);
                float3 red = float3(0.85, 0.1, 0.012), orange = float3(2.0, 0.5, 0.05), yellow = float3(3.0, 1.5, 0.28);
                float3 col = lerp(red, orange, smoothstep(0.12, 0.45, heat));
                col = lerp(col, yellow, smoothstep(0.62, 0.95, heat));
                col *= (0.6 + 0.85 * lum) * _Gain * (0.65 + 0.35 * saturate(_K));
                float a = saturate(dens * (0.75 + 0.4 * lum)) * Soft(i);
                col = MixFog(col, i.fog);
                return half4(col * a, a * _Body);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
