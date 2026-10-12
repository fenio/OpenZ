// OpenZ/Water: M12 style stylised water. L02 turquoise river, L17 murky harbour/canal (set per material).
// Two scrolling ripple normal layers (lake_ripple_normal.png), fresnel sky tint, sun glint (HDR, feeds bloom), fog.
Shader "OpenZ/Water"
{
    Properties
    {
        _ShallowColor ("Colour", Color) = (0.25, 0.72, 0.69, 0.78)
        _SkyColor ("Fresnel Sky Colour", Color) = (0.55, 0.75, 0.95, 1)
        [NoScaleOffset] _RippleMap ("Ripple Normal", 2D) = "bump" {}
        _RippleScale ("Ripple Tiling layer 1 (per m)", Float) = 0.125
        _RippleScale2 ("Ripple Tiling layer 2 (per m)", Float) = 0.0435
        _RippleStrength ("Ripple Strength", Range(0,1)) = 0.35
        _FlowSpeed ("Flow Speed", Float) = 0.03
        _GlintPower ("Glint Power", Float) = 180
        _GlintIntensity ("Glint Intensity (HDR)", Float) = 2.5
        _FresnelStrength ("Fresnel Sky Strength", Range(0,1)) = 0.35
        _RippleShade ("View-independent ripple shading", Range(0,2)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-10" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor; half4 _SkyColor; float _RippleScale; float _RippleScale2; half _RippleStrength; float _FlowSpeed; half _GlintPower; half _GlintIntensity; half _FresnelStrength; half _RippleShade;
            CBUFFER_END
            TEXTURE2D(_RippleMap); SAMPLER(sampler_RippleMap);
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings vert(Attributes i)
            {
                Varyings o; VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.fog = ComputeFogFactor(p.positionCS.z); return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.positionWS.xz * _RippleScale;
                float t = _Time.y * _FlowSpeed;
                half3 n1 = UnpackNormal(SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, uv + float2(t, t * 0.6)));
                float2 uv2 = float2(i.positionWS.z, -i.positionWS.x) * _RippleScale2;
                half3 n2 = UnpackNormal(SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, uv2 - float2(t * 0.4, -t * 0.9)));
                half3 nt = normalize(half3((n1.xy + n2.xy) * _RippleStrength, 1));
                half3 n = normalize(half3(nt.x, nt.z, nt.y));            // tangent space -> world (water is flat, +Y up)
                half3 v = GetWorldSpaceNormalizeViewDir(i.positionWS);
                Light L = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half ndl = saturate(dot(n, L.direction)) * 0.5 + 0.5;
                half fres = pow(1 - saturate(dot(n, v)), 4);
                half3 col = _ShallowColor.rgb * (L.color * ndl * lerp(0.5, 1, L.shadowAttenuation) + SampleSH(n));
                col *= 1 + (nt.x * 0.8 + nt.y * 0.5) * _RippleShade;           // ripples visible from any angle (not only in the sun glint)
                col = lerp(col, _SkyColor.rgb * L.color, fres * _FresnelStrength);
                half3 h = normalize(L.direction + v);
                // large-scale mask breaks the regular sparkle grid of the two ripple layers
                half3 nm = UnpackNormal(SAMPLE_TEXTURE2D(_RippleMap, sampler_RippleMap, i.positionWS.xz * 0.013 + float2(t * 0.2, 0)));
                half glintMask = saturate(0.5 + nm.x * 2.5 + nm.y * 1.5);
                col += L.color * pow(saturate(dot(n, h)), _GlintPower) * _GlintIntensity * L.shadowAttenuation * glintMask
                       * saturate(1 - (length(_WorldSpaceCameraPos - i.positionWS) - 40) / 90);   // highlights fade with distance
                col = MixFog(col, i.fog);
                return half4(col, saturate(_ShallowColor.a + fres * 0.2));
            }
            ENDHLSL
        }
    }
}
