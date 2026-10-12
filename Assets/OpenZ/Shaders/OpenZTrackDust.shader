// mesh, lit by the sun + ambient, alpha blended, soft silhouette smoothstep(.02, .72, |N.V|)) plus a PER-INSTANCE colour
// (_Tint, the rendered ground under the puff, linear) next to the per-instance _Alpha, a little light wrap so the puffs read
// soft instead of shaded balls, and fog. Drawn with Graphics.DrawMeshInstanced (MaterialPropertyBlock arrays _Alpha / _Tint).
Shader "OpenZ/TrackDust"
{
    Properties
    {
        _SoftEdge ("Soft silhouette", Range(0, 1)) = 1
        _Wrap ("Light wrap", Range(0, 1)) = 0.45
        _Ambient ("Ambient", Range(0, 2)) = 1
        _Alpha ("Alpha (per instance)", Float) = 1
        _Tint ("Colour (per instance, linear)", Vector) = (0.6, 0.45, 0.28, 1)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "Dust"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _SoftEdge; half _Wrap; half _Ambient;
            CBUFFER_END
            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float, _Alpha)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Tint)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct A { float4 pos : POSITION; float3 n : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float3 n : TEXCOORD0; float3 wp : TEXCOORD1; half fog : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };

            V vert(A i)
            {
                V o;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                o.wp = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(o.wp);
                o.n = TransformObjectToWorldNormal(i.n);
                o.fog = ComputeFogFactor(o.pos.z);
                return o;
            }

            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                float a = UNITY_ACCESS_INSTANCED_PROP(Props, _Alpha);
                float4 tint = UNITY_ACCESS_INSTANCED_PROP(Props, _Tint);
                float3 n = normalize(i.n);
                Light L = GetMainLight();
                half wrap = saturate((dot(n, L.direction) + _Wrap) / (1.0 + _Wrap));
                half3 lit = L.color * wrap + SampleSH(n) * _Ambient;
                half3 c = tint.rgb * lit;
                float ndv = abs(dot(n, normalize(GetWorldSpaceViewDir(i.wp))));
                a *= lerp(1.0, smoothstep(0.02, 0.72, ndv), _SoftEdge);    // dust soft edge
                c = MixFog(c, i.fog);
                return half4(c, saturate(a));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
