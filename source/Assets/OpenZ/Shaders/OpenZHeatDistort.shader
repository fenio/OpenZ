// OpenZ/HeatDistort (nuke_fx_v3): screen-space refraction from URP's opaque texture (_CameraOpaqueTexture, PC_RPAsset
// m_RequireOpaqueTexture 1, the same source OpenZWaterStylized samples for its bed).
//  _Mode 0  Shimmer: a camera-facing quad (uv 0-1) above / around a hot body (the nuke fireball, a heavy shell): animated
//           value-noise offset inside a soft round mask (_Inner .. 1 of the radius), strongest around the rim.
//  _Mode 1  Wave: a band mesh (uv.y 0 at the ground .. 1 at the top, normals pointing OUT of the wave) = the fort
//           shockwave's refraction front: the background is pushed along the screen-space outward normal with a
//           sin(pi v) profile, plus a faint brightening of the compressed air.
//           rising, mostly sideways waviness, soft sides, fades in over the flames and out with height.
// Drawn at Transparent-15 (2985): after the opaques, BEFORE the water (2990, which draws its own undistorted surface over
// it: a copy of the opaque texture over the river would erase the water surface) and every other transparent, so it bends
// the ground and buildings but never re-draws the fire, smoke or UI over themselves. Premultiplied blend: alpha = mask, so
// outside the mask nothing changes. Cheap: one opaque-texture sample per pixel, no loops.
// Built-in material: Resources/OpenZ/FX/M_HeatDistort.mat (keeps the shader in player builds; scripts instance it).
Shader "OpenZ/HeatDistort"
{
    Properties
    {
        _Mode ("Mode (0 shimmer quad, 1 wave band, 2 rising plume)", Float) = 0
        _Strength ("Offset strength (fraction of the screen height)", Float) = 0.006
        _NoiseScale ("Shimmer noise cells across the quad", Float) = 6
        _Speed ("Shimmer boil speed", Float) = 2.2
        _Inner ("Shimmer mask inner fade (0-1 of the radius)", Float) = 0.15
        _Bright ("Wave brightening", Float) = 0.08
        _Alpha ("Overall weight", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-15" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "HeatDistort"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Mode, _Strength, _NoiseScale, _Speed, _Inner, _Bright, _Alpha;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 dirSS : TEXCOORD1; float4 grab : TEXCOORD2; };

            V vert(A i)
            {
                V o;
                float3 wp = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(wp);
                o.uv = i.uv;
                // screen-space direction of the outward normal (wave mode)
                float3 nw = normalize(TransformObjectToWorldNormal(i.n));
                float4 a = o.pos, b = TransformWorldToHClip(wp + nw);
                float2 sa = a.xy / max(abs(a.w), 1e-4), sb = b.xy / max(abs(b.w), 1e-4);
                float2 d = sb - sa;
                #if UNITY_UV_STARTS_AT_TOP
                    d.y = -d.y;
                #endif
                o.dirSS = d / max(length(d), 1e-4);
                o.grab = ComputeScreenPos(o.pos);
                return o;
            }

            float Hash(float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.grab.xy / max(i.grab.w, 1e-4);
                float2 off; float mask; float bright = 0;
                if (_Mode < 0.5)
                {
                    float2 q = i.uv * 2.0 - 1.0; float r = length(q);
                    mask = smoothstep(0.0, max(_Inner, 1e-3), r) * (1.0 - smoothstep(0.55, 1.0, r));
                    float t = _Time.y * _Speed;
                    float2 np = i.uv * _NoiseScale;
                    off = float2(VNoise(np + float2(0.0, t)) - 0.5, VNoise(np * 1.3 + float2(7.1, -t * 1.2)) - 0.5) * 2.0;
                }
                else if (_Mode > 1.5)
                {
                    // top): hot air rising, wavy mostly sideways, soft sides, fading in over the flames and out with height
                    // noise octaves + thin rising ripples bent by the noise (more, smaller, smoother waves), softer sides and ends
                    float sx = abs(i.uv.x * 2.0 - 1.0);
                    mask = (1.0 - smoothstep(0.15, 1.0, sx)) * smoothstep(0.0, 0.28, i.uv.y) * (1.0 - smoothstep(0.35, 1.0, i.uv.y));
                    mask *= mask * (3.0 - 2.0 * mask);
                    float t = _Time.y * _Speed;
                    float2 np = float2(i.uv.x * _NoiseScale, i.uv.y * _NoiseScale * 1.6 - t);
                    float n1 = VNoise(np) - 0.5, n2 = VNoise(np * 2.3 + float2(5.2, -t * 0.9)) - 0.5;
                    float rip = sin(i.uv.y * _NoiseScale * 6.0 - t * 5.0 + n1 * 5.0 + i.uv.x * 3.0);
                    off = float2(n1 * 0.55 + n2 * 0.3 + rip * 0.22, (VNoise(np * 1.7 + float2(3.3, -t * 0.6)) - 0.5) * 0.35) * 2.0;
                }
                else
                {
                    float prof = sin(saturate(i.uv.y) * 3.14159265);
                    mask = prof * prof;
                    off = i.dirSS * (0.6 + 0.4 * VNoise(float2(i.uv.x * 90.0, _Time.y * 3.0)));
                    bright = _Bright * mask;
                }
                mask *= _Alpha;
                if (mask < 0.002) return half4(0, 0, 0, 0);
                float2 suv = uv + off * (_Strength * mask) * float2(_ScreenParams.y / _ScreenParams.x, 1.0);
                half3 col = SampleSceneColor(saturate(suv)) * (1.0 + bright);
                return half4(col * mask, mask);
            }
            ENDHLSL
        }
    }
}
