// OpenZ/FlagCaptureFx: the flag capture moment on the ground and in the air (capture pop + starburst, as in our earlier projects):
//   _Mode 0 ring    : terrain-draped disc around the flag: capture ring with a bounce (0.13 sin(17t) e^-3t), chase
//                     (3 lobes), flash (e^-5.5t), halo, wash, plus a shock wave running out to _Center.w; fades 1.1-2.25 s
//   _Mode 1 wash    : terrain-draped zone rectangle: the new colour sweeps out from the flag (front radius eases out over
//                     _Anim.y s), bright front band, glowing trail, then fades (tint hold _Anim.w keeps a faint tint)
//   _Mode 2 border  : the ZoneMarkers band mesh: old colour outside the front, new colour inside, glowing front
//                     (ZoneMarkers.ShowBorders off by default: the band then fades in, sweeps, and fades out again)
//   _Mode 3 sparks  : particle quads (vertex colour), additive glow
//   _Mode 4 confetti: particle quads (vertex colour), solid
// Premultiplied alpha (Blend One OneMinusSrcAlpha): alpha 0 = additive light, alpha 1 = solid colour.
// Ground modes get their values from a MaterialPropertyBlock (a handful of transient renderers, disabled when idle);
// particles use two material copies with _Mode set. Colours arrive linear (Material/MPB SetColor converts).
Shader "OpenZ/FlagCaptureFx"
{
    Properties
    {
        [Enum(Ring,0,ZoneWash,1,Border,2,Sparks,3,Confetti,4)] _Mode ("Mode", Float) = 0
        _NewColor ("New owner colour (a = line alpha)", Color) = (1,1,1,0.5)
        _OldColor ("Old owner colour (a = line alpha)", Color) = (1,1,1,0.5)
        _Center ("Flag ground point (xyz), outer radius (w)", Vector) = (0,0,0,8)
        _Anim ("x = age (s), y = sweep duration (s), z = capture ring radius, w = tint hold (wash) / line fade (border)", Vector) = (-1,1,1.6,0)
        _Glow ("Glow gain", Range(0,4)) = 0.45
        _GroundBand ("Ground modes: scene surfaces up to this far in front still receive it (m of eye depth, 0 = depth buffer test)", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("Depth test (Always with the ground band)", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+60" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "FlagCaptureFx"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off
            Offset -2, -4
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Mode;
                float4 _NewColor;
                float4 _OldColor;
                float4 _Center;
                float4 _Anim;
                float _Glow;
                float _GroundBand;
                float _ZTest;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; float eye : TEXCOORD2; };

            V vert(A i)
            {
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.eye = -TransformWorldToView(o.positionWS).z;
                o.color = i.color; o.uv = i.uv;
                return o;
            }

            float EaseOut(float k) { k = saturate(k); return 1 - (1 - k) * (1 - k); }

            float4 Shade(V i)
            {
                float t = _Anim.x;
                if (_Mode > 2.5)
                {
                    float2 q = i.uv * 2 - 1;
                    if (_Mode < 3.5)
                    {   // sparks: soft round hot dot, additive
                        float m = saturate(1 - dot(q, q)); m *= m;
                        return float4(i.color.rgb * m * i.color.a * _Glow * 4.0, 0);
                    }
                    // confetti: solid little card, a fake tilt shade across it
                    float a = i.color.a;
                    float3 c = i.color.rgb * (0.78 + 0.3 * i.uv.x);
                    return float4(c * a, a);
                }

                float2 d2 = i.positionWS.xz - _Center.xz;
                float d = length(d2);
                float aa = max(fwidth(d), 0.02);
                float3 nc = _NewColor.rgb;

                if (_Mode < 0.5)
                {   // ---- ring around the flag (sfSectors loop)
                    if (t < 0) return 0;
                    float ang = atan2(d2.y, d2.x);
                    float fade = 1 - smoothstep(1.1, 2.25, t);
                    float rim = 1 - smoothstep(_Center.w * 0.85, _Center.w, d);
                    float R0 = _Anim.z + 0.13 * sin(t * 17) * exp(-t * 3);                        // bounce
                    float ring = 1 - smoothstep(0.065, 0.16 + aa, abs(d - R0));
                    float flash = exp(-t * 5.5);
                    float pulse = 0.9 + 0.1 * sin(t * 8);
                    float chase = pow(0.5 + 0.5 * cos(ang * 3 - t * 9), 12);
                    float halo = 1 - smoothstep(0.12, 0.65, abs(d - R0));
                    float k = saturate(t / 0.9);
                    float waveR = lerp(0.4, _Center.w * 0.92, EaseOut(k));                          // shock wave outwards
                    float wave = (1 - smoothstep(0.05, 0.22 + aa, abs(d - waveR))) * (1 - smoothstep(0.35, 0.95, t));
                    float sweep = pow(0.5 + 0.5 * cos(ang * 6 - t * 13), 8);
                    float wash = (1 - smoothstep(0, R0 * 1.8, d)) * flash * 0.38;
                    float3 glow = nc * (ring * (1.65 * pulse + chase * 5 + flash * 3) + halo * (0.2 + flash * 0.55)
                                        + wave * (2.4 + sweep * 2) + wash);
                    return float4(glow * _Glow * fade * rim * i.color.a, 0);
                }

                float dur = max(_Anim.y, 0.05);
                float front = _Center.w * EaseOut(t / dur);
                float inside = 1 - smoothstep(front - 0.35, front + 0.05, d);
                float band = (1 - smoothstep(0, 0.9 + aa, abs(d - front))) * (1 - smoothstep(0.85 * dur, 1.15 * dur, t));

                if (_Mode < 1.5)
                {   // ---- zone wash: new colour sweeps out, glowing trail behind the front, then fades to the tint hold
                    if (t < 0) return 0;
                    float settle = 1 - smoothstep(dur, dur + 1.0, t);
                    float trail = exp(-max(front - d, 0) / 4.0) * inside;
                    float tint = inside * (_Anim.w + 0.16 * settle);
                    float3 glow = nc * (band * 1.2 + trail * 0.35) * settle * _Glow * 2.2;
                    float a = i.color.a;
                    return float4((nc * tint + glow) * a, tint * a);
                }

                // ---- border lines: old colour ahead of the front, new behind it, a glint on the front; _Anim.w fades the whole
                // band in and out when ZoneMarkers.ShowBorders is off (borders then only appear for the sweep)
                float fadeW = saturate(_Anim.w);
                float lineA = lerp(_OldColor.a, _NewColor.a, inside) * i.color.a * fadeW;
                float3 lc = lerp(_OldColor.rgb, nc, inside);
                float3 glint = nc * band * _Glow * 3.0 * i.color.a * fadeW;
                return float4(lc * lineA + glint, lineA);
            }

            // The ground modes lie on a 1 m height grid, so small rises, crater rims and low props cut them with a hard edge
            // under the depth buffer test. With _GroundBand > 0 the material draws with ZTest Always and tests the camera depth
            // texture here: a scene surface up to 0.6 x band (eye depth) in front of the layer still receives it in full, it fades
            // out up to the band, and anything further in front (units, buildings) hides it. Particles keep the depth buffer test.
            float4 frag(V i) : SV_Target
            {
                float4 c = Shade(i);
                if (_Mode < 2.5 && _GroundBand > 0.0)
                {
                    float raw = SampleSceneDepth(GetNormalizedScreenSpaceUV(i.positionCS));
                    float sceneEye;
                    if (unity_OrthoParams.w > 0.5)
                    {
                        #if UNITY_REVERSED_Z
                            raw = 1.0 - raw;
                        #endif
                        sceneEye = lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                    }
                    else sceneEye = LinearEyeDepth(raw, _ZBufferParams);
                    c *= 1.0 - smoothstep(_GroundBand * 0.6, _GroundBand, i.eye - sceneEye);
                }
                return c;
            }
            ENDHLSL
        }
    }
    FallBack Off
}
