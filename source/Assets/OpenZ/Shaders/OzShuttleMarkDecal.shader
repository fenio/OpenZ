// OpenZ/ShuttleMarkDecal: the playable shuttle's landing mark in the air (View/OzShuttleShadow.cs), projected straight down onto
// WHATEVER IS DRAWN under the ship: the drawn ground, rims and cliffs, roofs, forts, the repair station's deck, tower, beam and rams,
// tree crowns, bridges, and the drawn water surface. A box (the mark's footprint, from below the ground up to the ship's lowest drawn
// point) is drawn with its back faces; each pixel reads the scene depth (_CameraDepthTexture, copied after the opaques), rebuilds the
// world point there and gives it the mark's alpha: the ellipse of the ship's eased heading and smoothed centre (the radii x _Grow x the
// bounce _Scale), fully dark out to _Core of the radius (SoftGroundBlob's softness curve), faded on steep faces (the surface normal
// from four depth taps, the nearer side of each pair: no smear across a silhouette; full under 40 degrees, none over 50), and the fog.
// Two passes of the same mesh (two materials, View/OzShuttleShadow.cs): _WaterPass 0 at the old mark's queue (Transparent-140) on
// every pixel with no drawn water over it; _WaterPass 1 after the water (queue 2995) on the pixels under a drawn water surface, the
// point moved up the view ray onto that surface (_WaterTex: the level's drawn water heights, View/OzWaterSurface.cs). Premultiplied:
// rgb = tint x a, a = strength x the softness x the fades. Nothing above the ship's lowest point (its own hull) is ever darkened; under
// it the mark fades over _TopFade (m), and at a touchdown the box's top stays over the landing surface (View/OzShuttleShadow.cs).
Shader "OpenZ/ShuttleMarkDecal"
{
    Properties
    {
        [HDR] _Tint ("Tint (dark; red where it cannot land)", Color) = (0.03, 0.035, 0.03, 1)
        _Strength ("Strength (alpha at the middle)", Float) = 0.13
        _Core ("Full alpha out to this share of the radius", Float) = 0.5
        _Scale ("The bounce (1 = the radii)", Float) = 1
        _Grow ("The radii's growth with height (1 + 0.02 h)", Float) = 1
        _WaterPass ("0 = the drawn surface without water over it, 1 = the drawn water surface", Float) = 0
        _MarkBox ("x, top y (the ship's lowest point), z, bottom y", Vector) = (0, 0, 0, -100)
        _TopFade ("The fade under the top (m): soft, never a cut", Float) = 0.3
        _MarkAxes ("sin yaw, cos yaw, along m, across m", Vector) = (0, 1, 3.5, 3.3)
        _WaterST ("water grid: x0, z0, 1 / cell, on", Vector) = (0, 0, 2, 0)
        _WaterSize ("water grid: width, height (cells)", Vector) = (1, 1, 0, 0)
        _WaterTex ("water heights (R, -10000 = none)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-140" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ShuttleMarkDecal"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Strength;
                float _Core;
                float _Scale;
                float _Grow;
                float _WaterPass;
                float4 _MarkBox;
                float _TopFade;
                float4 _MarkAxes;
                float4 _WaterST;
                float4 _WaterSize;
            CBUFFER_END
            TEXTURE2D(_WaterTex);

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; };

            V vert(A i)
            {
                V o;
                o.positionCS = TransformWorldToHClip(TransformObjectToWorld(i.positionOS.xyz));
                return o;
            }

            // the scene's world point at a screen uv (false: the sky)
            bool ScenePoint(float2 uv, out float3 p)
            {
                float raw = SampleSceneDepth(uv);
            #if UNITY_REVERSED_Z
                bool sky = raw <= 1e-7;
            #else
                bool sky = raw >= 1.0 - 1e-7;
                raw = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, raw);
            #endif
                p = ComputeWorldSpacePosition(uv, raw, UNITY_MATRIX_I_VP);
                return !sky;
            }

            // the drawn water surface's height at xz (-10000: no drawn water there)
            float WaterY(float2 xz)
            {
                if (_WaterST.w < 0.5) return -10000.0;
                int2 ij = (int2)floor((xz - _WaterST.xy) * _WaterST.z);
                if (ij.x < 0 || ij.y < 0 || ij.x >= (int)_WaterSize.x || ij.y >= (int)_WaterSize.y) return -10000.0;
                return LOAD_TEXTURE2D(_WaterTex, ij).r;
            }

            // the surface's vertical share (|normal.y|) from four depth taps: per axis the nearer neighbour (no smear at a silhouette)
            float NormalUp(float2 uv, float3 p)
            {
                float2 px = 1.0 / _ScaledScreenParams.xy;
                float3 pr, pl, pu, pd;
                ScenePoint(uv + float2(px.x, 0), pr); ScenePoint(uv - float2(px.x, 0), pl);
                ScenePoint(uv + float2(0, px.y), pu); ScenePoint(uv - float2(0, px.y), pd);
                float3 dx = dot(pr - p, pr - p) < dot(p - pl, p - pl) ? pr - p : p - pl;
                float3 dy = dot(pu - p, pu - p) < dot(p - pd, p - pd) ? pu - p : p - pd;
                float3 n = cross(dy, dx);
                float l = length(n);
                return l > 1e-12 ? abs(n.y) / l : 1.0;
            }

            float4 frag(V i) : SV_Target
            {
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float3 p;
                if (!ScenePoint(uv, p)) discard;
                if (p.y > _MarkBox.y || p.y < _MarkBox.w) discard;                          // above the ship's lowest point (its own hull) or below the box
                float wy = WaterY(p.xz);
                bool underWater = wy > p.y;
                float up = 1.0;
                if (_WaterPass > 0.5)
                {
                    if (!underWater) discard;
                    // onto the drawn water surface along the view ray (its height at the new place once more)
                    float3 c = _WorldSpaceCameraPos, r = p - c;
                    float t = r.y < -1e-5 ? saturate((wy - c.y) / r.y) : 1.0;
                    float3 q = c + r * t;
                    float wy2 = WaterY(q.xz);
                    if (wy2 > -9999.0 && r.y < -1e-5) { t = saturate((wy2 - c.y) / r.y); q = c + r * t; }
                    p = q;
                    if (p.y > _MarkBox.y) discard;
                }
                else
                {
                    if (underWater) discard;                                                   // that pixel is the water pass's
                }
                // the ellipse (along the nose, across to its right), its softness
                float2 d = p.xz - _MarkBox.xz;
                float g = max(_Grow, 0.01) * max(_Scale, 0.01);
                float u = (d.x * _MarkAxes.x + d.y * _MarkAxes.y) / (_MarkAxes.z * g);
                float v = (d.x * _MarkAxes.y - d.y * _MarkAxes.x) / (_MarkAxes.w * g);
                float rr = sqrt(u * u + v * v);
                if (rr >= 1.0) discard;
                float core = saturate(_Core);
                float soft = 1.0 - smoothstep(core, 1.0, rr);
                soft *= soft * (3.0 - 2.0 * soft);
                if (_WaterPass < 0.5) up = NormalUp(uv, p);
                float wall = saturate((up - 0.643) / (0.766 - 0.643));                         // none over 50 degrees, full under 40
                float top = saturate((_MarkBox.y - p.y) / max(_TopFade, 1e-3));                // soft under the top (finish fix: no hard cut while the top passes)
                float a = saturate(_Strength * soft * wall * top);
                float4 pcs = TransformWorldToHClip(p);
                // without fog (it would erase the mark on a fog-free level), MixFogColor leaves 1 untouched there
                a *= MixFogColor(float3(1.0, 1.0, 1.0), float3(0.0, 0.0, 0.0), (float)ComputeFogFactor(pcs.z)).r;
                return float4(_Tint.rgb * a, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
