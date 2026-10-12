// spike strength (the few bright stars); _Gain lifts the bright ones over the bloom threshold.
Shader "OpenZ/Intro/Star"
{
    Properties
    {
        _Gain ("Gain", Float) = 2.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Gain;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 col : COLOR; };
            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.col = i.col; return o; }
            half4 frag (V i) : SV_Target
            {
                float2 q = (i.uv - 0.5) * 2;
                float r2 = dot(q, q);
                float core = exp(-r2 * 22);
                float halo = exp(-r2 * 5) * 0.22;
                float spike = (exp(-abs(q.x) * 34) * saturate(1 - abs(q.y)) + exp(-abs(q.y) * 34) * saturate(1 - abs(q.x))) * i.col.a * 0.7;
                float k = (core + halo + spike) * saturate(1 - r2 * 0.98);
                return half4(i.col.rgb * k * _Gain, 1);
            }
            ENDHLSL
        }
    }
}
