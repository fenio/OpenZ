// Unlit overlay for selection rings, order markers and path dots: vertex colour x _Color, alpha blended,
// no depth write, pulled towards the camera (Offset) so ground-hugging rings never z-fight with the terrain.
Shader "OpenZ/Overlay"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        _Scan ("Scanline strength on fill areas (0 = none)", Range(0, 1)) = 0
        _ScanPeriod ("Scanline period (screen pixels)", Float) = 4
        _ScanDrift ("Scanline roll (pixels a second)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Offset -2, -4
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Scan, _ScanPeriod, _ScanDrift;
            CBUFFER_END
            struct A { float4 pos : POSITION; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; half4 col : COLOR; half fill : TEXCOORD0; };
            // fill = 1 on the vertices of a wash (grey vertex colour), 0 on outlines (white) and their dark edges (black)
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.col = i.col * _Color; o.fill = step(0.5, i.col.r) * step(i.col.r, 0.9); return o; }
            half4 frag(V i) : SV_Target
            {
                half4 c = i.col;
                if (_Scan > 0.0)
                {   // fine horizontal lines fixed to the screen: they cannot shimmer when the camera moves or zooms; a slow roll at most
                    float y = i.pos.y + _Time.y * _ScanDrift;
                    c.a *= 1.0 - _Scan * i.fill * (0.5 + 0.5 * sin(y * (6.2831853 / max(_ScanPeriod, 2.0))));
                }
                return c;
            }
            ENDHLSL
        }
    }
}
