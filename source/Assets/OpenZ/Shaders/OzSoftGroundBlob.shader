// OpenZ/SoftGroundBlob: a soft dark mark lying on the drawn ground (the playable shuttle's shadow under it in the air: View/OzShuttleShadow.cs).
// Drawn on a grid laid on the drawn ground's own height (lifted 3 cm; vertex alpha 0 over water and down cliff steps, the scorch marks'
// rule). uv = the ellipse's own frame (-1 .. 1 on its two radii; the grid reaches 1.25 so the bounce (_Scale) fits). Premultiplied: rgb = tint x a, a = strength x the radial softness x the
// vertex alpha; the fog pulls a to 0. Queue Transparent-140: after the scorch marks, before the smoke.
Shader "OpenZ/SoftGroundBlob"
{
    Properties
    {
        [HDR] _Tint ("Tint (dark; red where it cannot land)", Color) = (0.03, 0.035, 0.03, 1)
        _Strength ("Strength (alpha at the middle)", Float) = 0.5
        _Core ("Full alpha out to this share of the radius", Float) = 0.35
        _Scale ("The ellipse's size (1 = the radii the uv carries)", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-140" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "SoftGroundBlob"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Offset -1, -2
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Strength;
                float _Core;
                float _Scale;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float mask : TEXCOORD1; float fog : TEXCOORD2; };

            V vert(A i)
            {
                V o;
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(w);
                o.uv = i.uv;
                o.mask = i.color.a;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                float d = length(i.uv) / max(_Scale, 0.01);
                float core = saturate(_Core);
                float soft = 1.0 - smoothstep(core, 1.0, d);
                soft *= soft * (3.0 - 2.0 * soft);
                float a = saturate(_Strength * soft * i.mask);
                a *= ComputeFogIntensity(i.fog);
                return float4(_Tint.rgb * a, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
