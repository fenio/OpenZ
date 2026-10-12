// OpenZ/VehicleGlow: every small light of a vehicle in ONE additive draw (vehicle_lights package, URP 17, one pass).
// A vehicle's glow mesh (VehicleLights, built once per vehicle type and mount node) holds all its lamp elements; each
// element is a quad whose 4 vertices carry the element centre and are expanded in the vertex shader:
//   POSITION  element centre (object space)                        NORMAL   facing (lens / directional halo), 0 = omni
//   TEXCOORD0 xy corner (-1..1), z radius (object units), w mode    TEXCOORD1 rgb colour (linear, HDR), w channel (+10 = team tint)
//   mode 0 = soft halo billboard (depth-faded, pulled towards the camera so it never cuts into the hull)
//        1 = lens disc lying on the lamp glass (faces its normal: a tail light is dark from the front)
//        2 = hot core billboard (bright small centre: headlight bulb, beacon LED, exhaust heat)
//        3 = pre-expanded quad (object = world, NightProjectileGlow streaks): x across -1..1, y along 0 tail .. 1 head
// Per renderer (MaterialPropertyBlock): _ChA = (head, tail, brake, beacon A), _ChB = (beacon B, engine, eye, work), the
// channel intensities VehicleLights computes every frame (night level, braking, blink, throttle, power, damage), _Team
// (the owner's glow colour) and _Gain. Channel 8 = constant 1 (projectiles). All HDR: blooms (NightGrade lowers the
// bloom threshold at night). A zero channel collapses its quads in the vertex shader (no fill cost for lamps that are off).
Shader "OpenZ/VehicleGlow"
{
    Properties
    {
        _Gain ("Gain", Float) = 1
        _ChA ("Channels: head, tail, brake, beacon A", Vector) = (1, 1, 1, 1)
        _ChB ("Channels: beacon B, engine, eye, work", Vector) = (1, 1, 1, 1)
        [HDR] _Team ("Team glow colour (linear)", Color) = (1, 0.22, 0.12, 1)
        _Falloff ("Halo falloff", Range(0.5, 12)) = 3.2
        _ZPull ("Halo pull towards the camera (m)", Range(0, 3)) = 0.3
        _DepthFade ("Halo soft depth fade (m)", Range(0.02, 4)) = 0.3
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+12" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "VehicleGlow"
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
                float4 _ChA;
                float4 _ChB;
                half4 _Team;
                half _Gain;
                half _Falloff;
                float _ZPull;
                float _DepthFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 uv0 : TEXCOORD0;
                float4 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 q : TEXCOORD0;            // corner.xy, mode, 0
                half3 col : TEXCOORD1;           // colour x channel x facing x gain
            };

            float ChannelK(float ch)
            {
                if (ch > 7.5) return 1.0;
                float4 a = float4(ch < 0.5, abs(ch - 1.0) < 0.5, abs(ch - 2.0) < 0.5, abs(ch - 3.0) < 0.5);
                float4 b = float4(abs(ch - 4.0) < 0.5, abs(ch - 5.0) < 0.5, abs(ch - 6.0) < 0.5, abs(ch - 7.0) < 0.5);
                return dot(_ChA, a) + dot(_ChB, b);
            }

            Varyings GlowVert(Attributes v)
            {
                Varyings o;
                float mode = v.uv0.w;
                float2 corner = v.uv0.xy;
                float chRaw = v.uv1.w;
                float team = chRaw > 9.5 ? 1.0 : 0.0;
                float k = ChannelK(chRaw - team * 10.0);
                float3 rgb = v.uv1.rgb * lerp(float3(1, 1, 1), _Team.rgb, team);
                float3 centreWS = TransformObjectToWorld(v.positionOS.xyz);
                float scale = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 V = normalize(GetCameraPositionWS() - centreWS);
                bool hasN = dot(v.normalOS, v.normalOS) > 0.25;
                float3 nWS = hasN ? normalize(mul((float3x3)UNITY_MATRIX_M, v.normalOS)) : float3(0, 1, 0);
                float facing = 1.0;
                if (mode > 2.5)
                {   // pre-expanded (projectile streaks): the vertex is the final position
                    o.positionCS = TransformWorldToHClip(centreWS);
                }
                else if (mode > 0.5 && mode < 1.5)
                {   // lens disc on the glass, facing its normal
                    float3 t = normalize(cross(nWS, abs(nWS.y) < 0.95 ? float3(0, 1, 0) : float3(1, 0, 0)));
                    float3 b = cross(nWS, t);
                    o.positionCS = TransformWorldToHClip(centreWS + (t * corner.x + b * corner.y) * (v.uv0.z * scale));
                    facing = smoothstep(-0.05, 0.35, dot(nWS, V));
                }
                else
                {   // halo / core billboards around the centre, pulled a little towards the camera
                    float3 cVS = TransformWorldToView(centreWS);
                    cVS += normalize(-cVS) * (_ZPull * (mode < 0.5 ? 1.0 : 0.5));
                    cVS.xy += corner * (v.uv0.z * scale);
                    o.positionCS = TransformWViewToHClip(cVS);
                    if (hasN) facing = smoothstep(-0.3, 0.45, dot(nWS, V));
                }
                float w = k * facing * _Gain;
                o.col = (half3)(rgb * w);
                o.q = float4(corner, mode, 0);
                if (w <= 1e-4) o.positionCS = float4(0, 0, -2, 1);         // lamp off: the quad collapses (no fill)
                return o;
            }

            half4 GlowFrag(Varyings i) : SV_Target
            {
                float2 q = i.q.xy;
                float mode = i.q.z;
                half k;
                if (mode > 2.5)
                {   // streak: soft across, brightening towards the head
                    half across = saturate(1.0 - q.x * q.x);
                    k = exp(-q.x * q.x * 3.2) * across * pow(saturate(q.y), 1.6);
                }
                else
                {
                    float r2 = dot(q, q);
                    if (mode > 0.5 && mode < 1.5) k = saturate(1.0 - r2) * (0.4 + 0.6 * exp(-r2 * 2.5));      // lens glass
                    else
                    {
                        k = mode < 0.5 ? exp(-r2 * _Falloff) * saturate(1.0 - r2)                             // halo
                                       : exp(-r2 * 5.0) * saturate(1.0 - r2) * 1.6;                            // hot core
                        float2 suv = GetNormalizedScreenSpaceUV(i.positionCS);
                        float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                        float fragEye = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                        k *= saturate((sceneEye - fragEye) / max(_DepthFade, 1e-3));
                    }
                }
                return half4(i.col * k, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
