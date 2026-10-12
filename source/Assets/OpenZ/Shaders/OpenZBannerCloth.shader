//
// One material per print and side (OpenZDetailsV2 builds them from the FBX slot names M_FuelBanner{A,B}_{D0,D1,Flap,Strips}_{Front,Back}):
//   * the print is a real cut-out: texture alpha (bullet holes, tears, the torn-off flap, the strip gaps) x the per-banner hole mask
//     (_HoleMask, RGBA, set by FuelBanner through a MaterialPropertyBlock: a = 0 where a new shot cut a hole, rgb = the scorch ring
//     multiply) is clipped at _Cutoff in EVERY pass (colour, shadow caster, depth, depth-normals), so shadows and SSAO have the holes;
//   * double-sided as two sheets 4 mm apart (kit): the front sheet carries the front print (uv as printed), the back sheet the _back
//     print with u' = 1 - u, so _MirrorU = 1 on back materials (the hole mask is sampled in front-print space and the sway pushes
//     both sheets the same way); Cull Back by default (_Cull), so each sheet shows only its own side;
//   * wind: TEXCOORD1 (FBX UVSway) = (x sway weight: sin(pi v) on the panels, pinned at both rods; 0 at the rod .. 1 at the torn end on
//     the strips; grows toward the free corner on the flap, y edge flutter). The cloth moves along its own normal with a travelling
//     sine driven by the same _OZFlagWind global the flags use (FlagView: strength, gust, phase, time), so banners and flags agree;
//     _SwayScale (MPB) = 0 freezes a piece lying on the ground (the fallen flap);
//   * shading like OpenZ/FlagCloth (clean stylized PBR + soft wrap fill, warm key, ambient boost, rim, a little sun through the
//     canvas) and the night_v2 day-sky fix; lit at night by the banner's own LIGHT_ point light (DetailLight, night budget).
Shader "OpenZ/BannerCloth"
{
    Properties
    {
        _BaseMap ("Print (RGBA: alpha = holes / tears)", 2D) = "white" {}
        _Cutoff ("Alpha clip", Range(0,1)) = 0.5
        _MirrorU ("Back sheet (print mirrored in u)", Float) = 0
        [NoScaleOffset] _HoleMask ("Run-time hole mask (FuelBanner MPB): a = keep, rgb = scorch multiply", 2D) = "white" {}
        _SwayAmp ("Sway amplitude (m at weight 1)", Range(0,0.6)) = 0.12
        _SwayScale ("Sway on / off (MPB)", Range(0,1)) = 1
        _Smoothness ("Smoothness", Range(0,1)) = 0.15
        _Translucency ("Sun through the canvas", Range(0,1)) = 0.22
        _SoftFill ("Soft wrap fill", Range(0,1.5)) = 0.6
        _AmbientBoost ("Ambient boost", Range(1,3)) = 1.25
        _KeyWarm ("Warm key tint", Color) = (1.06, 1.0, 0.92, 1)
        [HDR] _RimColor ("Rim light", Color) = (0.10, 0.09, 0.08, 1)
        _RimPower ("Rim power", Range(1,8)) = 3.2
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull (Back: each sheet shows its own print)", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half _Cutoff; half _MirrorU; half _SwayAmp; half _SwayScale; half _Smoothness; half _Translucency;
            half _SoftFill; half _AmbientBoost; half4 _KeyWarm; half4 _RimColor; half _RimPower; half _Cull;
        CBUFFER_END

        float4 _OZFlagWind;                                  // global (FlagView): strength 0.25-1.6, gust 0-1, phase (wraps at 2 pi), time

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_HoleMask); SAMPLER(sampler_HoleMask);

        // the cloth moves along its own normal (both sheets the same way: the back sheet's normal points the other way)
        float3 BannerSway(float3 posOS, float3 nOS, float2 sway)
        {
            float3 pw = TransformObjectToWorld(posOS);
            float s = _OZFlagWind.x, g = _OZFlagWind.y, t = _OZFlagWind.z;
            float ph = 0.55 * (pw.x + 0.6 * pw.z) + 0.35 * pw.y;
            float w = saturate(sway.x), e = saturate(sway.y);
            float amp = _SwayAmp * _SwayScale * (0.45 + 0.4 * s + 0.35 * g);
            // integer multiples of the wrapped phase keep the motion continuous when FlagView wraps it at 2 pi
            float d = amp * w * (sin(ph - t) + 0.35 * sin(2.3 * ph - 2.0 * t + 1.3) + e * 0.25 * sin(5.1 * ph - 3.0 * t + 0.7));
            float3 n = normalize(nOS) * (_MirrorU > 0.5 ? -1.0 : 1.0);
            return posOS + n * d;
        }

        // the cut: print alpha x run-time hole mask, clipped; returns the mask (rgb = scorch multiply)
        half4 BannerCut(float2 uv)
        {
            half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, TRANSFORM_TEX(uv, _BaseMap));
            float2 muv = float2(_MirrorU > 0.5 ? 1.0 - uv.x : uv.x, uv.y);
            half4 m = SAMPLE_TEXTURE2D(_HoleMask, sampler_HoleMask, muv);
            clip(tex.a * m.a - _Cutoff);
            return half4(tex.rgb * m.rgb, 1);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OpenZNightV2.hlsl"                                            // night_v2: day-sky reflection fix

            struct Attributes
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 sway : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; half fog : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                float3 p = BannerSway(i.positionOS.xyz, i.normalOS, i.sway);
                VertexPositionInputs vp = GetVertexPositionInputs(p);
                o.positionCS = vp.positionCS; o.positionWS = vp.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv; o.fog = ComputeFogFactor(vp.positionCS.z);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half3 albedo = BannerCut(i.uv).rgb;
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float3 N = normalize(i.normalWS);

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.alpha = 1; s.metallic = 0; s.smoothness = _Smoothness;
                s.occlusion = 1; s.normalTS = half3(0, 0, 1);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS; d.positionCS = i.positionCS;
                d.normalWS = N; d.viewDirectionWS = V;
                d.shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                d.fogCoord = i.fog;
                d.bakedGI = SampleSH(N);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 c = UniversalFragmentPBR(d, s);
                c.rgb = OZNightEnvFix(c.rgb, d, s, 0);

                Light ml = GetMainLight(d.shadowCoord);
                half ndl = dot(N, ml.direction);
                half direct = saturate(ndl) * ml.shadowAttenuation;
                half fill = saturate(saturate((ndl + 0.6) / 1.6) - saturate(ndl));                 // soft terminator
                c.rgb += albedo * ml.color * fill * _SoftFill * lerp(0.55, 1.0, ml.shadowAttenuation);
                c.rgb += albedo * ml.color * direct * (_KeyWarm.rgb - 1);
                c.rgb += albedo * d.bakedGI * (_AmbientBoost - 1);
                half back = saturate(dot(-N, ml.direction));                                       // sun through the canvas
                c.rgb += albedo * ml.color * back * _Translucency * ml.shadowAttenuation;
                half ndv = saturate(dot(N, V));
                half rim = pow(1 - ndv, _RimPower) * (0.35 + 0.65 * saturate(N.y * 0.5 + 0.5));
                half sunK = saturate(dot(ml.color, half3(0.2126, 0.7152, 0.0722)) / 1.6);
                c.rgb += _RimColor.rgb * rim * (0.35 + 0.65 * sunK);
                c.rgb = MixFog(c.rgb, i.fog);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 sway : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i);
                float3 p = BannerSway(i.positionOS.xyz, i.normalOS, i.sway);            // the shadow sways with the cloth
                float3 pw = TransformObjectToWorld(p);
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - pw);
                #else
                    float3 ld = _LightDirection;
                #endif
                nw = dot(nw, ld) < 0 ? -nw : nw;
                float4 cs = TransformWorldToHClip(ApplyShadowBias(pw, nw, ld));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs; o.uv = i.uv; return o;
            }
            half4 frag(Varyings i) : SV_Target { BannerCut(i.uv); return 0; }                // shadows with the holes
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 sway : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i);
                o.positionCS = TransformObjectToHClip(BannerSway(i.positionOS.xyz, i.normalOS, i.sway));
                o.uv = i.uv; return o;
            }
            half frag(Varyings i) : SV_Target { BannerCut(i.uv); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 sway : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 normalWS : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i);
                o.positionCS = TransformObjectToHClip(BannerSway(i.positionOS.xyz, i.normalOS, i.sway));
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = i.uv; return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                BannerCut(i.uv);
                return half4(NormalizeNormalPerPixel(i.normalWS), 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
