// OpenZ/ShadowFadeCaster: a shadow-caster-only shader for the playable shuttle's own sun / moon shadow while it eases in at a touchdown
// and out at a take-off (View/OzShuttleRealShadow.cs; the ship's meshes drawn once more with Graphics.DrawMesh, shadows only). URP has no
// per-object shadow strength, so _Fade (0 .. 1) keeps that share of the shadow map's texels in a fixed 4 x 4 ordered pattern (the soft
// shadow filter reads it as a shadow _Fade strong). The position and the bias are TeamLit's own ShadowCaster pass (the sun: the depth bias
// only; a punctual light: URP's ApplyShadowBias), so at _Fade = 1 it is the ship's own shadow texel for texel. No colour pass: the
// shader is never drawn on screen. Kept in builds by Resources/OpenZ/FX/M_OzShadowFadeCaster.
Shader "OpenZ/ShadowFadeCaster"
{
    Properties
    {
        _Fade ("Shadow kept (0 .. 1, ordered dither)", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" "IgnoreProjector"="True" }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Fade;
            CBUFFER_END
            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i);
                float3 pw = TransformObjectToWorld(i.positionOS.xyz);
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - pw);
                    float3 pb = ApplyShadowBias(pw, nw, ld);
                #else
                    float3 ld = _LightDirection;
                    float3 pb = pw + ld * _ShadowBias.x;                        // as TeamLit: the depth bias only (shadow_fix 01)
                #endif
                float4 cs = TransformWorldToHClip(pb);
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs; return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // ordered 4 x 4 Bayer threshold on the shadow map's texel grid (the pattern TeamLit's see-through cut uses)
                uint2 p = (uint2)i.positionCS.xy;
                uint x0 = p.x & 1u, y0 = p.y & 1u, x1 = (p.x >> 1) & 1u, y1 = (p.y >> 1) & 1u;
                uint b = ((((x0 ^ y0) << 1) | y0) << 2) | (((x1 ^ y1) << 1) | y1);
                clip(_Fade - ((half)b + 0.5) / 16.0);
                return 0;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
