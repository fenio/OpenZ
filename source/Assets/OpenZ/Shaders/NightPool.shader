// OpenZ/NightPool: projected light pool, the budget's impostor for a night light that has no real Light this frame
// (URP 17, camera depth texture, no normals). The renderer is a unit cube (NightMeshes.Cube, -0.5..0.5) placed and
// scaled over the lit footprint (x/z = the pool ellipse, y = the height band it may light). Every pixel of the scene
// behind the cube is reconstructed from the depth buffer and lit when it falls inside the ellipse, so the pool drapes
// over sand, roads, kerbs, rubble and units like a real light. Two passes:
//   "PoolLight" (UniversalForward): Blend DstColor One -> dst * (1 + light): albedo-correct brightening
//   "PoolGlow"  (SRPDefaultUnlit):  Blend One One      -> a faint additive glow so black ground still shows the pool
// Per renderer (MaterialPropertyBlock): _Color (HDR light colour x intensity x switch level, x (1 - real light share)).
Shader "OpenZ/NightPool"
{
    Properties
    {
        [HDR] _Color ("Light (HDR)", Color) = (6, 4, 2.4, 1)
        _Softness ("Edge softness", Range(0.05, 1)) = 0.55
        _GlowShare ("Additive glow share", Range(0, 0.2)) = 0.035
        _HeightFade ("Height band fade", Range(0.05, 0.5)) = 0.45
        _WallLight ("Light kept on walls / faces turned away", Range(0, 1)) = 0.12
        _DropFade ("Vertical falloff (m) below / above the pool plane", Range(0.2, 4)) = 1.1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Softness;
            half _GlowShare;
            half _HeightFade;
            half _WallLight;
            half _DropFade;
        CBUFFER_END

        struct Attributes { float4 positionOS : POSITION; };
        struct Varyings { float4 positionCS : SV_POSITION; };

        Varyings PoolVert(Attributes v)
        {
            Varyings o;
            o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
            return o;
        }

        // light share (0..1) at the scene pixel behind this fragment; 0 outside the volume
        half PoolAt(float4 positionCS)
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
            float3 a = abs(op);
            if (a.x > 0.5 || a.y > 0.5 || a.z > 0.5) return 0;
            float r = length(op.xz) * 2.0;                                   // 0 centre .. 1 ellipse edge
            half fall = saturate((1.0 - r) / max(_Softness, 1e-3));
            fall = fall * fall * (3.0 - 2.0 * fall);
            half band = 1.0 - smoothstep(0.5 - _HeightFade, 0.5, a.y);       // fade at the top / bottom of the band
            // bank walls) keep only _WallLight, and the pool fades with the vertical distance from its plane instead of
            // lighting a whole wall evenly from top to bottom. Normal from the depth-reconstructed position's derivatives.
            if (dot(n, _WorldSpaceCameraPos - wp) < 0) n = -n;
            half up = saturate(n.y);
            half facing = lerp(_WallLight, 1.0, up * up);
            float plane = TransformObjectToWorld(float3(0, 0, 0)).y - 0.35;  // the ground under the pool centre (PlacePool: gy + 0.35)
            float dy = (wp.y - plane) / max(_DropFade, 1e-3);
            half drop = 1.0 / (1.0 + dy * dy);
            return fall * band * facing * drop;
        }
        ENDHLSL

        Pass
        {
            Name "PoolLight"
            Tags { "LightMode"="UniversalForward" }
            Blend DstColor One
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PoolVert
            #pragma fragment PoolLightFrag
            half4 PoolLightFrag(Varyings i) : SV_Target
            {
                half k = PoolAt(i.positionCS);
                return half4(_Color.rgb * k, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "PoolGlow"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Front
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex PoolVert
            #pragma fragment PoolGlowFrag
            half4 PoolGlowFrag(Varyings i) : SV_Target
            {
                half k = PoolAt(i.positionCS);
                return half4(_Color.rgb * (k * _GlowShare), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
