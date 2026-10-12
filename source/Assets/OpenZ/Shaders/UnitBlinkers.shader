// OpenZ/UnitBlinkers (lights_roads_v1): the small blinking lights of every vehicle, cannon and building (beacons, status LEDs,
// antenna tips, neon strips, window glows) in ONE additive draw per mount node, URP 17, one pass. Same scheme as
// OpenZ/VehicleGlow (vehicle_lights package): each element is a quad whose 4 vertices carry the element centre and are
// expanded in the vertex shader, so a type's mesh is built once and shared by every unit of that type.
//   POSITION  element centre (object space)          NORMAL   facing (surface normal of an LED / panel), 0 = omni
//   TANGENT   xyz = panel long axis (object space), w = panel half length (object units)
//   TEXCOORD0 xy corner (-1..1), z radius / panel half height (object units), w mode
//   TEXCOORD1 rgb colour (linear, HDR), w = channel + 10 x tint (tint 0 = the rgb, 1 = x _Team, 2 = x _Status)
//   mode 0 = soft halo billboard (depth-faded, pulled towards the camera: never cuts into the hull)
//        1 = LED lens disc lying on its surface (dark from behind)
//        2 = hot core billboard (the LED / beacon bulb)
//        4 = panel lying on its surface (neon strip, lit window): soft-edged rectangle, facing its normal
//        5 = panel glow: a larger soft rectangle around a panel (depth-faded, pulled a little towards the camera)
// Per renderer (MaterialPropertyBlock, UnitBlinkers.cs): _ChA = (beacon A, beacon B, tip, status LED), _ChB = (data LED,
// neon, window, power LED) = the channel intensities of this unit this frame (night level, blink, owner, damage, production),
// _Team (owner glow colour, linear) and _Status (status LED colour, linear). All HDR: blooms at night (NightGrade lowers the
// bloom threshold), stays a small soft accent by day. A zero channel collapses its quads (no fill for lights that are off).
Shader "OpenZ/UnitBlinkers"
{
    Properties
    {
        _Gain ("Gain", Float) = 1
        _ChA ("Channels: beacon A, beacon B, tip, status LED", Vector) = (1, 1, 1, 1)
        _ChB ("Channels: data LED, neon, window, power LED", Vector) = (1, 1, 1, 1)
        [HDR] _Team ("Team glow colour (linear)", Color) = (1, 0.2, 0.11, 1)
        [HDR] _Status ("Status LED colour (linear)", Color) = (0.25, 1, 0.35, 1)
        _Falloff ("Halo falloff", Range(0.5, 12)) = 3.4
        _ZPull ("Halo pull towards the camera (m)", Range(0, 3)) = 0.25
        _DepthFade ("Halo soft depth fade (m)", Range(0.02, 4)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+12" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "UnitBlinkers"
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BlinkVert
            #pragma fragment BlinkFrag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ChA;
                float4 _ChB;
                half4 _Team;
                half4 _Status;
                half _Gain;
                half _Falloff;
                float _ZPull;
                float _DepthFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
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
                float4 a = float4(ch < 0.5, abs(ch - 1.0) < 0.5, abs(ch - 2.0) < 0.5, abs(ch - 3.0) < 0.5);
                float4 b = float4(abs(ch - 4.0) < 0.5, abs(ch - 5.0) < 0.5, abs(ch - 6.0) < 0.5, abs(ch - 7.0) < 0.5);
                return dot(_ChA, a) + dot(_ChB, b);
            }

            Varyings BlinkVert(Attributes v)
            {
                Varyings o;
                float mode = v.uv0.w;
                float2 corner = v.uv0.xy;
                float chRaw = v.uv1.w;
                float tint = floor((chRaw + 0.5) / 10.0);
                float k = ChannelK(chRaw - tint * 10.0);
                float3 rgb = v.uv1.rgb * (tint > 1.5 ? _Status.rgb : tint > 0.5 ? _Team.rgb : float3(1, 1, 1));
                float3 centreWS = TransformObjectToWorld(v.positionOS.xyz);
                float scale = length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 V = normalize(GetCameraPositionWS() - centreWS);
                bool hasN = dot(v.normalOS, v.normalOS) > 0.25;
                float3 nWS = hasN ? normalize(mul((float3x3)UNITY_MATRIX_M, v.normalOS)) : float3(0, 1, 0);
                float facing = 1.0;
                if (mode > 3.5)
                {   // panels on their surface (4) and their glow (5)
                    float3 t = mul((float3x3)UNITY_MATRIX_M, v.tangentOS.xyz);
                    t = normalize(t - nWS * dot(t, nWS));
                    float3 b = cross(nWS, t);
                    float3 p = centreWS + (t * (corner.x * v.tangentOS.w) + b * (corner.y * v.uv0.z)) * scale;
                    if (mode > 4.5) p += V * (_ZPull * 0.3);
                    o.positionCS = TransformWorldToHClip(p);
                    facing = smoothstep(-0.05, 0.3, dot(nWS, V));
                }
                else if (mode > 0.5 && mode < 1.5)
                {   // LED lens disc on its surface
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
                if (w <= 1e-4) o.positionCS = float4(0, 0, -2, 1);         // off: the quad collapses (no fill)
                return o;
            }

            half DepthSoft(float4 positionCS)
            {
                float2 suv = GetNormalizedScreenSpaceUV(positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float fragEye = LinearEyeDepth(positionCS.z, _ZBufferParams);
                return (half)saturate((sceneEye - fragEye) / max(_DepthFade, 1e-3));
            }

            half4 BlinkFrag(Varyings i) : SV_Target
            {
                float2 q = i.q.xy;
                float mode = i.q.z;
                half k;
                if (mode > 4.5)
                {   // panel glow: soft pillow, fading into the scene
                    float2 s = saturate(1.0 - q * q);
                    k = (half)(s.x * s.x * s.y * s.y) * DepthSoft(i.positionCS);
                }
                else if (mode > 3.5)
                {   // panel: soft-edged rectangle, a brighter core line along it
                    float2 a = abs(q);
                    k = (half)(smoothstep(1.0, 0.78, a.x) * smoothstep(1.0, 0.55, a.y) * (0.65 + 0.35 * exp(-q.y * q.y * 3.0)));
                }
                else
                {
                    float r2 = dot(q, q);
                    if (mode > 0.5 && mode < 1.5) k = (half)(saturate(1.0 - r2) * (0.45 + 0.55 * exp(-r2 * 2.5)));   // LED lens
                    else
                    {
                        k = (half)(mode < 0.5 ? exp(-r2 * _Falloff) * saturate(1.0 - r2)                             // halo
                                              : exp(-r2 * 5.0) * saturate(1.0 - r2) * 1.6);                           // hot core
                        k *= DepthSoft(i.positionCS);
                    }
                }
                return half4(i.col * k, 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
