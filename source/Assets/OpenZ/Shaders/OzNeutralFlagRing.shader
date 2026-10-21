// OpenZ/NeutralFlagRing: the neutral flag's ground cue (View/OzNeutralFlagWaves.cs + OzNeutralFlagWaves.Ring.cs), drawn the way the
// legacy shooter draws its capture rings (its surface-feedback sector ring while a capture runs, copied element by element; the capture
// pop of that same ring is OpenZ/FlagCaptureFx's, already in the game):
//   - light ADDED to the ground (premultiplied, alpha 0 = the legacy's "totalEmissiveRadiance +="): the ground's own texture stays
//     visible through it; never paint, never a solid line
//   - the ring: full within 6.5 cm of its radius, gone 16 cm out (a soft band, its edge spread over the pixel footprint: fwidth)
//   - the sweep: the ring fills round from its start angle as the legacy's capture progress does, a brighter head at its front
//   - 24 fine ticks on a band 38 cm inside the ring, lit where the sweep has passed
//   - a wide soft halo either side of the ring (from 12 cm to 65 cm), a fifth of the ring's light
//   - the breathing: 0.9 + 0.1 sin(8 t)
//   (those centimetres are the legacy's, on its 4.85 m ring; here they are scaled to this ring's radius, _Ring.y, so every
//   proportion of the legacy's ring is kept)
// Per pixel from the distance to the pole, on the polar grid the view drapes over the rendered ground (the grid only carries the shape
// of the ground; vertex colour alpha = its mask, 0 over water / down a step). The per-episode values (alpha, progress, night) arrive
// in a property block; the light level by day / by night is the one thing added for this game (the legacy's terrain is lit for its
// own exposure; by night the ground here is dark).
Shader "OpenZ/NeutralFlagRing"
{
    Properties
    {
        _Color ("Ring colour (the legacy's default ring tone)", Color) = (1, 0.827, 0.353, 1)
        _Center ("Pole foot (xyz), spare (w)", Vector) = (0, 0, 0, 0)
        _Anim ("x = alpha (the legacy's c.w), y = sweep progress 0..1.02 (the legacy's tone.w), z = night 0..1, w = spare", Vector) = (0, 0, 0, 0)
        _Ring ("x = ring radius (m), y = the legacy's widths scaled to this radius (radius / 4.85), z = light level by day, w = by night", Vector) = (2.5, 0.515, 0.35, 0.11)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "NeutralFlagRing"
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Offset -2, -4
            Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _Center;
                float4 _Anim;
                float4 _Ring;
                float _ZTest;
            CBUFFER_END

            struct A { float4 positionOS : POSITION; float4 color : COLOR; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float mask : TEXCOORD1; };

            V vert(A i)
            {
                V o;
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.mask = i.color.a;
                return o;
            }

            float4 frag(V i) : SV_Target
            {
                // ---- the legacy's sector ring, line for line: c = (x, z, radius, alpha), tone = (colour, progress), no celebration
                // (k scales the legacy's radial widths, metres on its 4.85 m ring, to this ring's radius: the proportions are its)
                float2 q = i.positionWS.xz - _Center.xz;
                float R = _Ring.x, k = _Ring.y;
                float d = length(q), aa = max(fwidth(d), 0.025);
                // the angle round the pole, 0..1, from north clockwise as the legacy's (its world's z runs the other way: hence -q.y)
                float a = fmod(atan2(-q.y, q.x) + 1.570796 + 6.283185, 6.283185) / 6.283185;
                float alpha = _Anim.x, progressNow = _Anim.y;
                float pulse = 0.9 + 0.1 * sin(_Time.y * 8.0);
                float ring = 1.0 - smoothstep(0.065 * k, 0.16 * k + aa, abs(d - R));
                float progress = 1.0 - smoothstep(progressNow - 0.012, progressNow + 0.002, a);
                float headK = max(0.0, 1.0 - abs(a - progressNow) / 0.045);
                float head = headK * headK;
                float tickK = abs(0.5 + 0.5 * cos(a * 6.283185 * 24.0));
                float ticks = pow(tickK, 12.0) * progress;
                float tickBand = 1.0 - smoothstep(0.10 * k, 0.20 * k + aa, abs(d - (R - 0.38 * k)));
                float3 light = _Color.rgb * alpha * (ring * progress * (1.65 + head * 5.0) * pulse + tickBand * ticks * 0.9);
                float halo = 1.0 - smoothstep(0.12 * k, 0.65 * k, abs(d - R));
                light += _Color.rgb * alpha * halo * 0.2;

                // ---- this game: the light level by day / by night, the grid's mask; alpha 0 = light added to the ground
                float level = lerp(_Ring.z, _Ring.w, saturate(_Anim.z));
                return float4(light * level * saturate(i.mask), 0.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
