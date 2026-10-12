// OpenZ/PyroFlame: the flame flipbook of our earlier projects, ported line for line (billboard vertex stage + flame fragment shader).
//  * vertex: each sprite's 4 vertices carry its centre; the quad corner is rotated and scaled in VIEW space, the 4x2 flipbook frame
//    is picked (per-sprite seeded UV mirroring = 4 variants per frame) and the whole sprite fades before its centre reaches its
//    floor (no hard line where a quad cuts the ground).
//  * fragment: taps stay inside the sprite's own atlas cell; sticky licks (soft > 0) get the 9-tap blur and a flattened erosion;
//    noise erosion eats the edges more as the sprite ages; HDR = rgb x (2.6 + 3.4 core) x (1 + boost); additive
//    (SrcAlpha, One), fragments under .008 alpha are discarded.
//  * OpenZ additions: the colour is mapped through _LegacyToUrp rows (tools/color_fit.py: one linear map, so it holds
//    for any pile of sprites), a soft-particle depth fade where a sprite meets a wall / hull, and URP fog like OpenZ/FxAdditive.
// Textures are imported LINEAR (sRGB off): three.js samples legacy-flame.png / legacy-noise.png as raw values (NoColorSpace).
Shader "OpenZ/PyroFlame"
{
    Properties
    {
        _FlameTex ("Flame flipbook 4x2 (legacy-flame.png, linear)", 2D) = "black" {}
        _NoiseTex ("Erosion noise (legacy-noise.png, linear, repeat)", 2D) = "gray" {}
        _LegacyToUrpR ("Legacy -> URP colour, row R", Vector) = (0.7267, -0.1432, -0.0145, 0)
        _LegacyToUrpG ("Legacy -> URP colour, row G", Vector) = (-0.0249, 0.3666, -0.0085, 0)
        _LegacyToUrpB ("Legacy -> URP colour, row B", Vector) = (-0.0131, -0.0442, 0.2328, 0)
        _Gain ("HDR gain on top (1 = legacy)", Float) = 1
        _SoftDepth ("Soft particle fade (m)", Float) = 0.35
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Pass
        {
            Name "PyroFlame"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha One
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

            TEXTURE2D(_FlameTex); SAMPLER(sampler_FlameTex);
            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _FlameTex_ST;
                float4 _NoiseTex_ST;
                float4 _LegacyToUrpR, _LegacyToUrpG, _LegacyToUrpB;
                float _Gain;
                float _SoftDepth;
            CBUFFER_END

            struct Attributes
            {
                float3 center : POSITION;     // sprite centre (world)
                float4 d0 : TEXCOORD0;        // corner.xy (-.5 .. .5), scale, rotation
                float4 d1 : TEXCOORD1;        // frame, alpha, floorY, life t
                float4 d2 : TEXCOORD2;        // seed, boost, soft, 0
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uv : TEXCOORD0;        // xy = atlas uv (vUv), zw = uv inside the cell (vLocal)
                float4 a : TEXCOORD1;         // alpha, life, seed, floor fade
                float4 b : TEXCOORD2;         // boost, soft, fog factor, view depth
                float4 screenPos : TEXCOORD3;
                float4 c : TEXCOORD4;         // the next flipbook frame: cell origin (xy), blend to it (z; 0 = stepped)
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                float c = cos(i.d0.w), s = sin(i.d0.w);
                float2 corner = float2(i.d0.x * c - i.d0.y * s, i.d0.x * s + i.d0.y * c) * i.d0.z;
                float3 vp = TransformWorldToView(i.center);
                vp.xy += corner;
                o.positionCS = TransformWViewToHClip(vp);
                float frame = floor(i.d1.x);          // whole part = the frame; a fraction (MOTION BLUR option) blends to the next one
                float frame1 = min(frame + 1.0, 7.0);
                o.c = float4(fmod(frame1, 4.0) * 0.25, 0.5 - floor(frame1 / 4.0) * 0.5, i.d1.x - frame, 0.0);
                float fx = fmod(frame, 4.0);
                float fy = floor(frame / 4.0);
                // per-sprite seeded UV mirroring: 4 orientation variants per flipbook frame
                float2 buv = i.d0.xy + 0.5;
                if (frac(i.d2.x * 3.0) > 0.5) buv.x = 1.0 - buv.x;
                if (frac(i.d2.x * 5.0) > 0.5) buv.y = 1.0 - buv.y;
                o.uv = float4((buv.x + fx) * 0.25, 1.0 - (1.0 - buv.y + fy) * 0.5, buv);
                float h = i.center.y - i.d1.z;
                o.a = float4(i.d1.y, i.d1.w, i.d2.x, smoothstep(-0.1, 0.6 * max(i.d0.z, 0.4), h));
                o.b = float4(i.d2.y, i.d2.z, ComputeFogFactor(o.positionCS.z), -vp.z);
                o.screenPos = ComputeScreenPos(o.positionCS);
                return o;
            }

            // SAMPLE INSIDE THE CELL, NEVER ACROSS IT (the 4x2 atlas): every tap clamped to this sprite's own cell
            float4 TapCell(float2 org, float2 loc)
            {
                return SAMPLE_TEXTURE2D(_FlameTex, sampler_FlameTex, org + clamp(loc, 0.0, 1.0) * float2(0.25, 0.5));
            }

            // one flipbook cell: a single tap, or the 9-tap box blur for the sticky licks (soft > 0); used for the frame blended in
            float4 CellColour(float2 org, float2 loc, float soft)
            {
                float4 tex = TapCell(org, loc);
                if (soft > 0.001)
                {
                    float r = 0.085 * soft;
                    float d = r * 0.7071;
                    tex = (tex
                        + TapCell(org, loc + float2( r, 0.0)) + TapCell(org, loc + float2(-r, 0.0))
                        + TapCell(org, loc + float2(0.0,  r)) + TapCell(org, loc + float2(0.0, -r))
                        + TapCell(org, loc + float2( d,  d))  + TapCell(org, loc + float2(-d,  d))
                        + TapCell(org, loc + float2( d, -d))  + TapCell(org, loc + float2(-d, -d))
                        ) * 0.111111;
                }
                return tex;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 vUv = i.uv.xy, vLocal = i.uv.zw;
                float vAlpha = i.a.x, vLife = i.a.y, vSeed = i.a.z, vFloorFade = i.a.w, vBoost = i.b.x, vSoft = i.b.y;
                float2 org = float2(vUv.x - vLocal.x * 0.25, vUv.y - vLocal.y * 0.5);
                float4 tex = TapCell(org, vLocal);
                if (vSoft > 0.001)
                {   // THE BLUR (sticky fire only): a real 9-tap box blur of the cell
                    float r = 0.085 * vSoft;
                    float d = r * 0.7071;
                    tex = (tex
                        + TapCell(org, vLocal + float2( r, 0.0)) + TapCell(org, vLocal + float2(-r, 0.0))
                        + TapCell(org, vLocal + float2(0.0,  r)) + TapCell(org, vLocal + float2(0.0, -r))
                        + TapCell(org, vLocal + float2( d,  d))  + TapCell(org, vLocal + float2(-d,  d))
                        + TapCell(org, vLocal + float2( d, -d))  + TapCell(org, vLocal + float2(-d, -d))
                        ) * 0.111111;
                }
                // MOTION BLUR option: blend to the next flipbook frame (the pool sends a fractional frame) instead of stepping
                if (i.c.z > 0.001) tex = lerp(tex, CellColour(i.c.xy, vLocal, vSoft), i.c.z);
                // per-fragment noise erosion: the threshold rises over the sprite's life (ragged dissolving edges)
                float nse = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, vLocal * 0.9 + float2(vSeed * 7.31, vSeed * 3.97)).r;
                nse = lerp(nse, 0.5, vSoft * 0.65);
                float th = lerp(0.10, 0.46, vLife);
                float erode = smoothstep(th, th + 0.46 + vSoft * 0.5, nse * 0.72 + tex.a * 0.5);
                // HDR: 2.6x base up to 6x in the dense core, x (1 + boost)
                float core = smoothstep(0.55, 0.95, tex.a);
                float3 col = tex.rgb * (2.6 + core * 3.4) * (1.0 + vBoost);
                float a = tex.a * vAlpha * erode * vFloorFade;
                if (a < 0.008) discard;
                col = max(0.0, float3(dot(_LegacyToUrpR.xyz, col), dot(_LegacyToUrpG.xyz, col), dot(_LegacyToUrpB.xyz, col))) * _Gain;
                // soft particles: fade where the sprite meets a hull / wall / the ground.
                // OUTSIDE the burning surface: a .35 m fade dimmed every lick lying on a robot or a hull to ~25 %, i.e. the sticky
                float softDepth = _SoftDepth * lerp(1.0, 0.15, saturate(vSoft));
                if (softDepth > 0.0)
                {
                    float2 suv = i.screenPos.xy / max(i.screenPos.w, 1e-5);
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    a *= saturate((sceneEye - i.b.w) / softDepth);
                }
                // fog: an additive sprite loses what the fog hides
                float fogVis = MixFog(float3(1.0, 1.0, 1.0), i.b.z).r - MixFog(float3(0.0, 0.0, 0.0), i.b.z).r;
                return half4(col * fogVis, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
