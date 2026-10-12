// uv.y = 0 at the nozzle / centre, 1 at the tip / rim; the colour fades out toward the tip with a white-hot core near 0.
Shader "OpenZ/Intro/Glow"
{
    Properties
    {
        [HDR] _Color ("Colour", Color) = (4, 1.6, 0.4, 1)
        [HDR] _Core ("Core", Color) = (6, 5, 3.5, 1)
        _CoreLen ("Core length (uv)", Float) = 0.35
        _Fade ("Fade power", Float) = 1.6
        _Alpha ("Alpha", Float) = 1
        _Ring ("Ring mode (soft band around uv.y 0.5)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
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
            float4 _Color, _Core; float _CoreLen, _Fade, _Alpha, _Ring;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert (A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 frag (V i) : SV_Target
            {
                float y = saturate(i.uv.y);
                float a;
                float3 c;
                if (_Ring > 0.5) { float b = 1 - abs(y - 0.5) * 2; a = pow(saturate(b), 2.2); c = _Color.rgb; }
                else
                {
                    a = pow(1 - y, _Fade);
                    float edge = 1 - abs(i.uv.x - 0.5) * 2;                     // soft sides on the cone strip
                    a *= saturate(edge * 2.5);
                    c = lerp(_Core.rgb, _Color.rgb, saturate(y / max(_CoreLen, 0.01)));
                }
                return half4(c * a * _Alpha, 1);
            }
            ENDHLSL
        }
    }
}
