// OpenZ/ShuttleSelfShadowDepth: the playable shuttle's own shadow map while it flies (View/OzShuttleSelfShadow.cs): its casting meshes
// drawn along the main light into a small float map that keeps, per texel, the nearest depth (0 at the light's side of the ship's box
// .. 1 at the far side; Blend Min, cleared to 1). Only the ship's own TeamLit renderers read it (OzShuttleSelfShadow.hlsl), so the ship
// keeps its self-shadow (the pods on the hull) in the air while the ground under it gets none (the ship casts nothing into the sun's
// map up there: View/OzShuttleRealShadow.cs).
Shader "OpenZ/ShuttleSelfShadowDepth"
{
    Properties { }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "ShuttleSelfShadowDepth"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend One One
            BlendOp Min
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4x4 _OzSSClip;      // world -> the map's clip space (GL.GetGPUProjectionMatrix(.., true) x the light's view)
            float4 _OzSSDepth;       // xyz = the light's direction / the box's depth, w = the offset: depth01 = dot(xyz, world) + w

            struct A { float4 positionOS : POSITION; };
            struct V { float4 positionCS : SV_POSITION; float depth : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                float3 w = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = mul(_OzSSClip, float4(w, 1.0));
                o.depth = dot(_OzSSDepth.xyz, w) + _OzSSDepth.w;
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                return float4(saturate(i.depth), 0, 0, 1);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
