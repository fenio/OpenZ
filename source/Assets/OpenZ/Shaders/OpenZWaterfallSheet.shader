// sheet of the L02 D01 waterfall. A thin two-sided transparent ribbon baked by OpenZ/Details v2/Waterfall v2 (it follows the
// cliff face 0.3 m in front, bends over every ledge, lands in the pond). Two layers of the dv2 water streak texture scroll
// down the sheet (layer 1 = _BaseMap_ST, driven by WaterfallFx through a property block, layer 2 at 0.62x for parallax),
// white foam streaks break out of the brightest streaks, the side edges run brighter and softer, vertex colour R = foam
// (ledges, the lip, the plunge), G = side-edge mask (1 sheet, 0 the plunge foam ring), A = alpha. Lit like the stylized props: wrapped sun + shadows, SH ambient, URP additional
// lights (lamps / explosions light it), fog; the part below the lake surface (_FadeY) fades out.
Shader "OpenZ/WaterfallSheet"
{
    Properties
    {
        _BaseMap ("Streak texture", 2D) = "white" {}
        _BaseColor ("Water tint", Color) = (0.86, 1.18, 1.22, 1)
        _FoamColor ("Foam colour", Color) = (0.93, 0.98, 1, 1)
        [HDR] _EmissionColor ("Emission (night sheen, set by WaterfallFx)", Color) = (0, 0, 0, 1)
        _Alpha ("Body alpha", Range(0, 1)) = 0.9
        _FoamStreaks ("Foam streaks", Range(0, 1)) = 0.55
        _EdgeBright ("Bright edges", Range(0, 1)) = 0.4
        _EdgeSoft ("Soft side edge (u)", Range(0.01, 0.3)) = 0.09
        _FadeY ("Fade below this world height (lake surface)", Float) = -0.35
        _FadeRange ("Fade range (m)", Float) = 0.3
        _Wrap ("Light wrap", Range(0, 1)) = 0.55
        [Header(Look v2 for the jungle falls)]
        _LookV2 ("Look v2 on", Range(0, 1)) = 0
        _LipFoam ("Lip foam (share of v from the top)", Range(0.02, 0.5)) = 0.12
        _FootFoam ("Foot foam (share of v from the bottom)", Range(0.02, 0.6)) = 0.22
        _StreakCut ("Streak threshold (texture g)", Range(0.3, 0.98)) = 0.84
        _BodyAlpha ("Body alpha between the threads (x body alpha)", Range(0, 1)) = 0.35
        _BodyDark ("Body shade (1 = the water tint as is)", Range(0.3, 1.2)) = 0.8
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor; half4 _FoamColor; half4 _EmissionColor;
                half _Alpha; half _FoamStreaks; half _EdgeBright; half _EdgeSoft; float _FadeY; float _FadeRange; half _Wrap;
                half _LookV2; half _LipFoam; half _FootFoam; half _StreakCut; half _BodyAlpha; half _BodyDark;   // item 227 look v2
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V
            {
                float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; float3 n : TEXCOORD2;
                half4 col : COLOR; half fog : TEXCOORD3; UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.wp = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.n = TransformObjectToWorldNormal(i.n);
                o.uv = i.uv; o.col = i.col;
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }

            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float3 n = normalize(i.n); n = front ? n : -n;
                float2 uv1 = i.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;                                   // WaterfallFx scroll
                float2 uv2 = float2(i.uv.x * 1.63 + 0.37, i.uv.y * 0.58) + float2(-_BaseMap_ST.z * 0.5, _BaseMap_ST.w * 0.62);
                float2 uv3 = float2(i.uv.x * 2.9 + 0.11, i.uv.y * 1.35) + float2(0.0, _BaseMap_ST.w * 1.31);   // fast foam layer
                half3 t1 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv1).rgb;
                half3 t2 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv2).rgb;
                half3 t3 = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv3).rgb;
                half3 water = (t1 * 0.6 + t2 * 0.4) * _BaseColor.rgb;
                // foam streaks: the brightest streaks of the fast layer and of the two body layers break white
                half streak = saturate((max(t3.g, (t1.g + t2.g) * 0.5) - 0.6) * 9.0) * _FoamStreaks;
                half across = abs(i.uv.x * 2.0 - 1.0) * i.col.g;                                         // G = 0: no side edges (foam ring)
                half edge = smoothstep(0.7, 1.0, across) * _EdgeBright;                                 // brighter sides
                half foam = saturate(i.col.r + streak * (0.6 + 0.4 * i.col.r) + edge);
                half3 albedo = lerp(water, _FoamColor.rgb, foam * 0.9);
                half a2 = -1.0;                                                                           // item 227 look v2: its own alpha (< 0 = the old look's)
                if (_LookV2 > 0.5)
                {   // item 227 (the jungle falls read as flat white slabs from above: the level 16 sheets carry no foam vertex paint, so
                    // col.r = 1 made the whole sheet foam): a dark see-through body with bright fast threads, white foam only where the
                    // water breaks: the lip (top of v) and the boil at the foot (bottom of v); v grows with height on these sheets
                    half v = saturate(i.uv.y);
                    half lip = smoothstep(1.0 - _LipFoam, 1.0, v) * (0.55 + 0.45 * t3.g);
                    half foot = (1.0 - smoothstep(0.0, _FootFoam, v)) * (0.6 + 0.4 * t2.g) * smoothstep(0.0, 0.04, v);
                    half thread = saturate((max(t3.g, max(t1.g, t2.g)) - _StreakCut) / max(0.02, 1.0 - _StreakCut)) * _FoamStreaks;
                    half breaks = saturate(lip + foot);
                    half3 body = _BaseColor.rgb * (_BodyDark * (0.8 + 0.4 * t1.g));
                    foam = saturate(breaks + thread * (1.0 - breaks) + edge * 0.5);
                    albedo = lerp(body, _FoamColor.rgb, foam);
                    a2 = _Alpha * lerp(_BodyAlpha, 1.0, saturate(thread + breaks));
                    a2 = lerp(a2, 0.95, breaks * 0.8);
                }

                Light L = GetMainLight(TransformWorldToShadowCoord(i.wp));
                half nl = dot(n, L.direction);
                half wrap = saturate((nl + _Wrap) / (1.0 + _Wrap));
                half sh = lerp(0.35, 1.0, L.shadowAttenuation);                                          // thin water: soft shadow
                half3 lit = SampleSH(n) * 1.3 + L.color * ((0.3 + 0.7 * wrap) * sh * L.distanceAttenuation);
            #if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = i.wp; inputData.normalWS = n;
                    inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.wp);
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.pos);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light la = GetAdditionalLight(lightIndex, i.wp);
                        lit += la.color * (la.distanceAttenuation * la.shadowAttenuation * saturate((dot(n, la.direction) + 0.6) / 1.6));
                    LIGHT_LOOP_END
                }
            #endif
                half3 c = albedo * lit + _EmissionColor.rgb * (0.7 + 0.6 * foam);
                half a = lerp(_Alpha, 0.96, foam) * i.col.a;
                if (a2 >= 0.0) a = a2 * i.col.a;                                                            // item 227 look v2
                a *= smoothstep(0.0, _EdgeSoft, 1.0 - across);                                            // soft side edges
                a *= smoothstep(_FadeY - _FadeRange, _FadeY, i.wp.y);                                    // gone under the lake
                c = MixFog(c, i.fog);
                return half4(c, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
