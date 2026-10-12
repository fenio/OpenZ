// like shading = base colour lit by the sun + ambient, plus HDR emission (fireballs, cores, sparks feed the Bloom), and a
// per-instance alpha for the fading kinds (smoke, dust, rings). Blend / ZWrite / Cull are material properties so the same
// shader serves the opaque and the fading pools.
Shader "OpenZ/FxLit"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        _Alpha ("Alpha (per instance)", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 1
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 0
        [Enum(Off, 0, On, 1)] _ZWrite ("ZWrite", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        _Unlit ("Unlit amount", Range(0,1)) = 0
        [HDR] _RimColor ("Rim emission (fire edges)", Color) = (0,0,0,1)
        _RimPower ("Rim power (0 = no rim)", Float) = 0
        _SoftEdge ("Soft silhouette (dust)", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; half4 _EmissionColor; half _Unlit; half4 _RimColor; half _RimPower; half _SoftEdge;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Alpha)
            UNITY_INSTANCING_BUFFER_END(Props)
            struct A { float4 pos : POSITION; float3 n : NORMAL; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; half4 col : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A i)
            {
                V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.wp = TransformObjectToWorld(i.pos.xyz); o.pos = TransformWorldToHClip(o.wp); o.n = TransformObjectToWorldNormal(i.n); o.col = i.col;
                return o;
            }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float a = UNITY_ACCESS_INSTANCED_PROP(Props, _Alpha);
                float3 n = normalize(i.n);
                Light L = GetMainLight();
                half3 lit = L.color * saturate(dot(n, L.direction)) + SampleSH(n);
                float ndv = abs(dot(n, normalize(GetWorldSpaceViewDir(i.wp))));
                half3 emis = _RimPower > 0 ? lerp(_RimColor.rgb, _EmissionColor.rgb, pow(saturate(ndv), _RimPower)) : _EmissionColor.rgb;
                half3 c = _BaseColor.rgb * lerp(lit, half3(1, 1, 1), _Unlit) + emis;
                a *= lerp(1, smoothstep(0.02, 0.72, ndv), _SoftEdge);                 // dust soft edge
                return half4(c, saturate(_BaseColor.a * a));
            }
            ENDHLSL
        }
    }
}
