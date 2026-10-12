// OpenZ (GPL-3.0)
// camera only) while ZodFlyover / a CaptureKit cine recording runs; gameplay never draws it and keeps OpenZ/SkyGradient.
// The painted panorama (sky_panoramas_v1, equirect, horizon at v = 0.5, the clear sun corridor at u = 0.5) is turned so its corridor
// sits on the DayNight sun, a sun disc + glow are drawn on the sun direction, and the horizon blends into the haze colour
// (_HorizonColor, which FlybySky drives to the painting's own horizon colour: sky_panoramas_v1 option B).
// Fade: the output is lerp(gradient sky, flyby sky, _FlybySkyW). The gradient term is exactly OpenZ/SkyGradient (the same DayNight
// colours), so the dome appears and disappears with no pop. It is drawn after the skybox (queue Geometry+501) at the far plane with
// ZTest LEqual: only pixels nothing was drawn on (the sky) take it; it writes no depth, so the atmosphere pass still sees sky there.
Shader "OpenZ/FlybySky"
{
    Properties
    {
        [NoScaleOffset] _FlybySkyTex ("Panorama (equirect, horizon v 0.5, sun corridor u 0.5)", 2D) = "grey" {}
        _SkyTop ("Gradient zenith (copied from the gameplay skybox)", Color) = (0.62, 0.72, 0.86, 1)
        _Horizon ("Gradient horizon (copied from the gameplay skybox)", Color) = (0.86, 0.78, 0.64, 1)
        _Exponent ("Gradient exponent (copied from the gameplay skybox)", Range(0.1, 3)) = 0.6
        _PanoGainH ("Painting gain at the horizon, away from sunrise", Color) = (1, 1, 1, 1)
        _PanoGainZ ("Painting gain at the zenith, away from sunrise", Color) = (1, 1, 1, 1)
        _Away ("0 sunrise (painting as painted) .. 1 graded to the DayNight colours", Range(0, 1)) = 0
        _SkyYaw ("Extra yaw of the painting (turns; 0 = corridor centred on the sun)", Float) = 0
        _CloudShift ("Flyby sunrise: the painted cloud banks are slid this far toward the sun (rad; shapes kept)", Float) = 0.26
        _CloudShiftRamp ("Flyby sunrise: over this angle from the sun the slide eases in (rad, inside the clear corridor)", Float) = 0.17
    }
    SubShader
    {
        Tags { "Queue"="Geometry+501" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            Name "FlybySky"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "FlybySky.hlsl"

            TEXTURE2D(_FlybySkyTex); SAMPLER(sampler_FlybySkyTex);
            float4 _HorizonColor;    // global (Atmosphere / FlybySky): the haze colour the far ground fades into
            CBUFFER_START(UnityPerMaterial)
            half4 _SkyTop, _Horizon; half _Exponent; half4 _PanoGainH, _PanoGainZ; half _Away; float _SkyYaw; float _CloudShift, _CloudShiftRamp;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                float3 ws = TransformObjectToWorld(i.pos.xyz);
                o.dir = ws - _WorldSpaceCameraPos;                 // the dome is centred on the camera: exact view direction per pixel
                o.pos = TransformWorldToHClip(ws);
                #if UNITY_REVERSED_Z
                    o.pos.z = o.pos.w * 1e-6;                        // on the far plane: only empty (sky) pixels pass LEqual
                #else
                    o.pos.z = o.pos.w * (1 - 1e-6);
                #endif
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                float3 d = normalize(i.dir);
                // the gameplay sky, term for term OpenZ/SkyGradient (fade start / end = no pop)
                half3 grad = lerp(_Horizon.rgb, _SkyTop.rgb, pow(saturate(d.y), _Exponent));
                // the painting, its clear corridor locked to the sun azimuth (u grows with atan2(x, z): not mirrored)
                float3 s = _FlybySunDir.xyz;
                // wide, so a 49 deg frame round the sun showed no clouds. Slide both cloud banks toward the sun by _CloudShift (their shapes
                // kept: slope 1 outside the ramp), eased in over the first _CloudShiftRamp of the corridor and out again behind the camera
                // (120-180 deg), so the mapping stays continuous all round: no seam anywhere.
                float da = atan2(d.x, d.z) - atan2(s.x, s.z);
                da -= 6.2831853 * round(da * 0.15915494);
                float ada = abs(da);
                float slide = _CloudShift * smoothstep(0.0, max(1e-3, _CloudShiftRamp), ada) * (1.0 - smoothstep(2.0944, 3.1416, ada));
                float u = frac(0.5 + (da + sign(da) * slide) * 0.15915494 + _SkyYaw);
                float v = 0.5 + asin(clamp(d.y, -1.0, 1.0)) * 0.31830989;
                half3 pano = SAMPLE_TEXTURE2D_LOD(_FlybySkyTex, sampler_FlybySkyTex, float2(u, v), 0).rgb;   // no mips: no seam column
                half3 gain = lerp(_PanoGainH.rgb, _PanoGainZ.rgb, (half)smoothstep(0.0, 0.6, d.y));
                pano *= lerp((half3)1, gain, _Away);                                                         // never a sunrise at night
                float h = smoothstep(0.0, max(1e-3, _FlybyHorizonBlend), d.y);
                half3 sky = lerp((half3)_HorizonColor.rgb, pano, (half)h) + FlybyGlow(d);                  // the same glow is in the far haze
                float c = dot(d, s);
                float disc = smoothstep(_FlybySunShape.w, _FlybySunShape.z, c) * smoothstep(-0.002, 0.006, d.y);
                sky += (half3)_FlybySunDisc.rgb * (half)(disc * _FlybySunDir.w);
                return half4(lerp(grad, sky, (half)saturate(_FlybySkyW)), 1);
            }
            ENDHLSL
        }
    }
}
