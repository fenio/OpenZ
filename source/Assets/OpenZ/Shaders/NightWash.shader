// OpenZ/NightWash (night_v2): the light a small building lamp throws on its own wall and the ground below it, without a
// real Light (no budget, no shadow maps). Same technique as OpenZ/NightPool: the renderer is a unit cube (-0.5..0.5,
// NightMeshes.Cube) centred on the lamp and scaled to its reach; every scene pixel inside it is rebuilt from the camera
// depth texture and lit like by a small point lamp facing the cube's +z (the wall normal): smooth range window, N.L with
// a soft wrap (normal from the depth-reconstructed position), a forward lobe (a bulkhead lamp lights what is in front of
// and below it). Two passes, like NightPool:
//   "WashLight" (UniversalForward): Blend DstColor One -> dst * (1 + light): the lit wall keeps its own colour and detail
//   "WashGlow"  (SRPDefaultUnlit):  Blend One One      -> a faint additive share so near-black surfaces still show it
// Per renderer (MaterialPropertyBlock): _Color (HDR lamp colour x intensity x switch level x flicker). Needs the camera
// depth texture (PC_Renderer has it on). Queue Transparent-19: after the opaque world and NightPool, before the glows.
Shader "OpenZ/NightWash"
{
    Properties
    {
        [HDR] _Color ("Light (HDR)", Color) = (2.4, 1.8, 1.1, 1)
        _Wrap ("Wrap (light reaching faces turned away)", Range(0, 1)) = 0.3
        _Lobe ("Forward lobe (0 = all round, 1 = only in front)", Range(0, 1)) = 0.65
        _Core ("Near falloff radius (fraction of the reach)", Range(0.05, 1)) = 0.3
        _GlowShare ("Additive glow share", Range(0, 0.2)) = 0.02
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-19" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Wrap;
            half _Lobe;
            half _Core;
            half _GlowShare;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; };

        Varyings WashVert(Attributes v)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            return o;
        }

        // light (0..1) at the scene pixel behind this fragment; 0 outside the volume
        half WashAt(float4 positionCS)
        {
            float2 suv = GetNormalizedScreenSpaceUV(positionCS);
            float raw = SampleSceneDepth(suv);
            float3 wp = ComputeWorldSpacePosition(suv, raw, UNITY_MATRIX_I_VP);
            float3 n = normalize(cross(ddy(wp), ddx(wp)) + float3(0, 1e-7, 0));   // derivatives before any early return
            #if UNITY_REVERSED_Z
                if (raw <= 1e-6) return 0;
            #else
                if (raw >= 1.0 - 1e-6) return 0;
            #endif
            float3 op = TransformWorldToObject(wp);
            float r = length(op) * 2.0;                                      // 0 at the lamp .. 1 at the reach (sphere in the cube)
            if (r >= 1.0) return 0;
            float3 lampWS = TransformObjectToWorld(float3(0, 0, 0));
            float3 fwd = normalize(TransformObjectToWorldDir(float3(0, 0, 1)));
            float3 toLamp = lampWS + fwd * 0.08 - wp;                        // the lens sits a little in front of the wall
            float3 L = toLamp / max(length(toLamp), 1e-4);
            if (dot(n, _WorldSpaceCameraPos - wp) < 0) n = -n;
            half ndl = saturate((dot(n, L) + _Wrap) / (1.0 + _Wrap));
            half lobe = lerp(1.0, saturate(dot(-L, fwd) * 0.75 + 0.35), _Lobe);
            half window = saturate(1.0 - r * r); window *= window;           // smooth end of the reach
            half rc = r / max(_Core, 1e-3);
            half atten = window / (1.0 + rc * rc);                           // bright close to the lamp, soft further out
            return atten * ndl * lobe;
        }
        ENDHLSL

        Pass
        {
            Name "WashLight"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor One
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WashVert
            #pragma fragment WashLightFrag
            half4 WashLightFrag(Varyings i) : SV_Target
            {
                half k = WashAt(i.positionCS);
                return half4(_Color.rgb * k, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "WashGlow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WashVert
            #pragma fragment WashGlowFrag
            half4 WashGlowFrag(Varyings i) : SV_Target
            {
                half k = WashAt(i.positionCS);
                return half4(_Color.rgb * (k * _GlowShare), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
