// OpenZ/SoftTrail: soft smoky ribbon for TrailRenderers (thrown grenades). Alpha blended (never additive, so it cannot
// read as a white streak), soft across the width from the line UV (v = 0..1 across, like OpenZ/Beam), vertex colour
// gradient along the trail, lit very simply by the main light + ambient so it darkens at night with the rest of the map.
Shader "OpenZ/SoftTrail"
{
    Properties
    {
        _Color ("Color (sRGB) x vertex colour", Color) = (0.62, 0.58, 0.50, 0.55)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            CBUFFER_END
            struct A { float4 pos : POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; float2 uv : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.col = i.col; o.uv = i.uv; return o; }
            half4 frag(V i) : SV_Target
            {
                float across = 1 - abs(i.uv.y * 2 - 1);
                half soft = smoothstep(0.0, 0.9, across);
                Light L = GetMainLight();
                half3 lit = SampleSH(half3(0, 1, 0)) + L.color * 0.55;
                return half4(_Color.rgb * i.col.rgb * lit, _Color.a * i.col.a * soft);
            }
            ENDHLSL
        }
    }
}
