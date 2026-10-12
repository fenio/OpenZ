// Additive HDR beam for LineRenderers (laser core + glow trail): colour x intensity feeds the Bloom, vertex alpha fades it,
// soft falloff across the beam width from the line UV (v = 0..1 across).
Shader "OpenZ/Beam"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 4, 0.6, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+20" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Blend One One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            struct A { float4 pos : POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.col = i.col; o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float across = 1 - abs(i.uv.y * 2 - 1);
                float soft = across * across;
                return half4(_Color.rgb * i.col.rgb * i.col.a * soft, 0);
            }
            ENDHLSL
        }
    }
}
