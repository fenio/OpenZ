// Two-colour sky for oblique views (menu previews, flybys, the L17 cliff notch): horizon = the DayNight fog/haze colour
// (same as the atmosphere pass's horizon haze, so the far sea melts into it), zenith = the DayNight sky colour.
Shader "OpenZ/SkyGradient"
{
    Properties
    {
        _SkyTop ("Zenith", Color) = (0.62, 0.72, 0.86, 1)
        _Horizon ("Horizon", Color) = (0.86, 0.78, 0.64, 1)
        _Exponent ("Gradient exponent", Range(0.1, 3)) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _SkyTop, _Horizon; half _Exponent;
            CBUFFER_END
            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.dir = i.pos.xyz; return o; }
            half4 frag(V i) : SV_Target
            {
                float y = normalize(i.dir).y;
                return half4(lerp(_Horizon.rgb, _SkyTop.rgb, pow(saturate(y), _Exponent)), 1);
            }
            ENDHLSL
        }
    }
}
