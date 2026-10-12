// OpenZ/WaterV4Wet (water_v4 package, rev2): the soft wet dark-sand band on the L02 river banks, just above the waterline.
// Drawn on its own proxy mesh (L02_ShoreWetBand, built by OpenZ > Water v4 from the bed map; deliberately not named WAT_*, see OpenZWaterV4.cs: a flat band of 0.5 m cells
// around every shore, a little above the ground), because the river mesh ends only ~0.8 m (median, 0 m in places) past the
// waterline and would cut the band with a hard edge. The mesh is only a screen-space coverage proxy: the fragment reads the
// scene depth, finds the ground point under the pixel and multiplies it toward _WetColor when that point IS the terrain
// (bed-map height test: units, props, decks and rubble are never darkened), lies above the water and within _WetWidth of
// the waterline. Never darker than _WetColor (never black). Multiply blend, so it keeps the ground paint, roads and
// scorch decals underneath (wet sand is just darker). Lighter at night so the bank never turns into a black rim.
Shader "OpenZ/WaterV4Wet"
{
    Properties
    {
        _WetColor ("Wet sand multiplier (never black; shore_band_fix: a faint damp tint)", Color) = (0.99, 0.985, 0.975, 1)
        _WetWidth ("Wet band width (m from the waterline)", Range(0, 2)) = 0.5
        _WetMaxHeight ("Wet band: ground at most this high above the water (m)", Range(0.05, 1.5)) = 0.6
        _WetLap ("Wet band lapping", Range(0, 1)) = 0.35
        _WaterLevel ("Water level (m, set by the setup)", Float) = -0.35
        [NoScaleOffset] _BedMap ("Bed map (R extra depth, G shore distance, B terrain height, A across)", 2D) = "black" {}
        _BedRect ("Bed map rect (x0, z0, size x, size z in m)", Vector) = (-24, -204, 192, 228)
        _BedEncode ("Bed map decode (extra max, sd min, sd range, y min)", Vector) = (1.2, -2, 6, -1.6)
        _BedEncodeY ("Bed map decode (y range, -, -, -)", Vector) = (2.2, 0, 0, 0)
    }

    SubShader
    {
        // after the opaque ground and its decals, before the water (Transparent-10) and every other transparent
        Tags { "RenderType"="Transparent" "Queue"="Transparent-20" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "ShoreWet"
            Tags { "LightMode"="UniversalForward" }

            Blend DstColor Zero
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WetVert
            #pragma fragment WetFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WetColor; float _WetWidth; float _WetMaxHeight; half _WetLap; float _WaterLevel;
                float4 _BedRect; float4 _BedEncode; float4 _BedEncodeY;
            CBUFFER_END
            TEXTURE2D(_BedMap); SAMPLER(sampler_BedMap);
            float _OZNight;

            struct WetA { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct WetV { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            WetV WetVert(WetA v)
            {
                WetV o = (WetV)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                return o;
            }

            half4 WetFrag(WetV i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float rawDepth = SampleSceneDepth(screenUV);
                float3 camPos = GetCameraPositionWS();
                float surfaceEye = -TransformWorldToView(i.positionWS).z;
                float sceneEye = IsPerspectiveProjection() ? LinearEyeDepth(rawDepth, _ZBufferParams) : LinearDepthToEyeDepth(rawDepth);
                float3 sceneWS = IsPerspectiveProjection() ? camPos + (i.positionWS - camPos) * (sceneEye / max(surfaceEye, 1e-4))
                                                           : i.positionWS + GetViewForwardDir() * (sceneEye - surfaceEye);
                float above = sceneWS.y - _WaterLevel;                                       // m above the water level
                half inBand = (half)(step(-0.05, above) * (1.0 - smoothstep(_WetMaxHeight * 0.7, _WetMaxHeight, above)));

                float2 uv = (sceneWS.xz - _BedRect.xy) / max(_BedRect.zw, 1e-3);
                float4 m = SAMPLE_TEXTURE2D_LOD(_BedMap, sampler_BedMap, uv, 0);
                half inside = (half)(step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0));
                float sd = _BedEncode.y + m.g * _BedEncode.z;                                // + in the water
                float terrainY = _BedEncode.w + m.b * _BedEncodeY.x;
                half ground = (half)(1.0 - smoothstep(0.05, 0.14, abs(sceneWS.y - terrainY)));   // the terrain, not a unit

                float outM = max(-sd, 0.0);                                                  // m from the waterline
                float t = _Time.y;
                half lap = (half)(0.5 + 0.5 * sin(t * 0.9 + sceneWS.x * 0.37 + sceneWS.z * 0.23));
                float w = max(_WetWidth * (1.0 - _WetLap * 0.3 * lap), 1e-3);
                half wet = (half)((1.0 - smoothstep(w * 0.3, w, outM)) * step(sd, 0.05));
                // a slightly darker line where the last lap reached (freshly wet), soft
                wet = saturate(wet + (half)(_WetLap * 0.3 * (1.0 - smoothstep(0.0, 0.07, abs(outM - w * 0.22))) * step(outM, w)));
                wet *= inBand * ground * inside * (1.0 - 0.4 * saturate((half)_OZNight));
                return half4(lerp((half3)1.0, _WetColor.rgb, wet), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
