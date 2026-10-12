// Mesh = a tapered cone along +Z (uv.y 0 at the nozzle -> 1 at the tip, uv.x round it). Additive; the silhouette fades with the
// view angle (no hard edge), the tip tapers to nothing, a scrolling two-octave noise flickers it, white-yellow core -> orange body.
Shader "OpenZ/Intro/Flame"
{
    Properties
    {
        [HDR] _Color ("Body", Color) = (4.2, 1.7, 0.3, 1)
        [HDR] _Core ("Core", Color) = (5.5, 4.4, 1.6, 1)
        _Fade ("Tip fade power", Float) = 1.25
        _Soft ("Edge softness", Float) = 1.7
        _Noise ("Flicker amount", Range(0, 1)) = 0.55
        _Speed ("Scroll speed", Float) = 6
        _Alpha ("Alpha", Float) = 1
        _Cover ("Cover (how much it hides the bright sky behind it)", Range(0, 1)) = 0.7
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha                                                                // premultiplied: reads as a solid yellow flame even on the bright sky
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
            float4 _Color, _Core; float _Fade, _Soft, _Noise, _Speed, _Alpha, _Cover;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 nw : TEXCOORD1; float3 vw : TEXCOORD2; };
            V vert (A i)
            {
                V o; float3 wp = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(wp); o.uv = i.uv; o.nw = TransformObjectToWorldNormal(i.n); o.vw = GetWorldSpaceViewDir(wp);
                return o;
            }
            float h21 (float2 p) { p = frac(p * float2(123.34, 456.21)); p += dot(p, p + 45.32); return frac(p.x * p.y); }
            float vnoise (float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(h21(i), h21(i + float2(1, 0)), f.x), lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), f.x), f.y);
            }
            half4 frag (V i) : SV_Target
            {
                float v = saturate(i.uv.y);
                float ndv = abs(dot(normalize(i.nw), normalize(i.vw)));
                float edge = pow(ndv, _Soft);                                                     // soft silhouette, never a hard plank edge
                float ang = i.uv.x * 6.2831853;                                                   // seamless round the cone
                float2 q = float2(cos(ang), sin(ang)) * 1.6;
                float n = vnoise(q + float2(0, v * 5.0 - _Time.y * _Speed)) * 0.65 + vnoise(q * 2.3 + float2(3.1, v * 11.0 - _Time.y * _Speed * 1.7)) * 0.35;
                float tip = pow(saturate(1 - v), _Fade);                                          // tapers to nothing
                float a = tip * edge * lerp(1, 0.5 + n, _Noise) * saturate(v * 14 + 0.3);
                float3 c = lerp(_Core.rgb, _Color.rgb, saturate(v * 1.8 + (1 - ndv) * 0.9));
                return half4(c * a * _Alpha, saturate(a * _Alpha * _Cover));
            }
            ENDHLSL
        }
    }
}
