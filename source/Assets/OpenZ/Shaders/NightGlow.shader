// OpenZ/NightGlow: additive HDR glows of the night lights (URP 17, one pass, SRP Batcher compatible per material).
//  _Mode 0 = camera-facing halo (the quad's uv spans the sprite; the object origin is the centre, its X scale the radius):
//            a soft Gaussian that fades into nearby geometry instead of cutting it (camera depth texture)
//  _Mode 1 = flat lens / window disc (uv radial falloff, facing its own normal, dims at grazing angles)
//  _Mode 2 = solid glowing bulb or beacon dome (N.V shaded, bright centre, softer rim)
// Per renderer (MaterialPropertyBlock): _Color (HDR, already x intensity x switch level x flicker). All modes feed the
// night bloom (NightGrade lowers the bloom threshold at night).
Shader "OpenZ/NightGlow"
{
    Properties
    {
        [HDR] _Color ("Colour (HDR)", Color) = (4, 3, 2, 1)
        [Enum(Halo,0,Lens,1,Bulb,2)] _Mode ("Mode", Float) = 0
        _Falloff ("Halo / lens falloff", Range(0.5, 12)) = 3.5
        _Core ("Hot core (halo)", Range(0, 4)) = 1.2
        _ZPull ("Halo pull towards the camera (m)", Range(0, 3)) = 0.4
        _DepthFade ("Halo soft depth fade (m)", Range(0.02, 4)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+12" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "NightGlow"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GlowVert
            #pragma fragment GlowFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Mode;
                half _Falloff;
                half _Core;
                float _ZPull;
                float _DepthFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 positionWS : TEXCOORD2;
            };

            Varyings GlowVert(Attributes v)
            {
                Varyings o;
                o.uv = v.uv;
                if (_Mode < 0.5)
                {
                    // billboard: every vertex sits at the object origin, pushed out in view space by its uv corner
                    float3 centreWS = TransformObjectToWorld(float3(0, 0, 0));
                    float radius = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                    float3 centreVS = TransformWorldToView(centreWS);
                    float3 toCam = normalize(-centreVS);
                    centreVS += toCam * _ZPull;
                    centreVS.xy += (v.uv * 2.0 - 1.0) * radius;
                    o.positionCS = TransformWViewToHClip(centreVS);
                    o.positionWS = centreWS;
                    o.normalWS = float3(0, 1, 0);
                }
                else
                {
                    o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                    o.positionCS = TransformWorldToHClip(o.positionWS);
                    o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                }
                return o;
            }

            half4 GlowFrag(Varyings i) : SV_Target
            {
                half k;
                if (_Mode < 0.5)
                {
                    float2 q = i.uv * 2.0 - 1.0; float r2 = dot(q, q);
                    k = exp(-r2 * _Falloff) * saturate(1.0 - r2) + _Core * exp(-r2 * _Falloff * 6.0);
                    float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                    float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    float fragEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                    k *= saturate((sceneEye - fragEye) / max(_DepthFade, 1e-3));
                }
                else
                {
                    float3 V = normalize(GetCameraPositionWS() - i.positionWS);
                    half ndv = abs(dot(normalize(i.normalWS), V));
                    if (_Mode < 1.5)
                    {
                        float2 q = i.uv * 2.0 - 1.0; float r2 = dot(q, q);
                        k = saturate(1.0 - r2) * (0.35 + 0.65 * exp(-r2 * _Falloff)) * (0.45 + 0.55 * ndv);
                    }
                    else k = 0.45 + 0.55 * ndv * ndv;
                }
                return half4(_Color.rgb * k, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
