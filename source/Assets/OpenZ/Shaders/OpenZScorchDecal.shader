// OpenZ/ScorchDecal: explosion scorch marks as terrain-conforming mesh decals (ScorchDecals.cs builds a grid that follows
// decal MULTIPLIES what is already drawn (Blend DstColor Zero): the lit, shadowed, AO'd ground keeps its shading and the
// mark never glows at night. Soft edges come from the procedural atlas alpha x the grid's vertex alpha (0 at the rim,
// over water and down cliff steps) x _Opacity (per decal, fade-in / fade-out). Fog: the factor is pulled back to 1 by the
// fog amount (MixFogColor with white), so distant marks do not darken the fog. Offset pulls it toward the camera; it never
// writes depth. Queue Transparent-150: after all opaque ground and units, before smoke, water and selection overlays.
Shader "OpenZ/ScorchDecal"
{
    Properties
    {
        _MainTex ("Scorch atlas (2x2 variants, rgb tint, a burn)", 2D) = "white" {}
        _Opacity ("Opacity (per decal)", Range(0, 1)) = 1
        _Strength ("Max darkening", Range(0, 1)) = 0.85
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-150" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ScorchMultiply"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; half _Opacity; half _Strength;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; half4 col : COLOR; half fog : TEXCOORD1; };
            V vert(A i)
            {
                V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.col = i.col;
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                half4 t = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                half a = saturate(t.a * i.col.a * _Opacity) * _Strength;
                half3 factor = lerp(half3(1, 1, 1), t.rgb, a);          // 1 = untouched ground, tint = full burn
                factor = MixFogColor(factor, half3(1, 1, 1), i.fog);    // fogged distance: back toward 1
                return half4(factor, 1);
            }
            ENDHLSL
        }
    }
}
