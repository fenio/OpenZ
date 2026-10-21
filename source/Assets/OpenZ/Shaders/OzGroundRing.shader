// OpenZ/GroundRing: a gun's range on the ground (View/OzGunRange.cs + OzGunRange.Ring.cs), drawn the way the legacy shooter draws its
// ground rings and of one family with the neutral flag's ring (OpenZ/NeutralFlagRing) and the units' range dots (AttackRangeRings):
//   - the range line: a fine anti-aliased annulus exactly at the range (full within 0.12 w, gone 0.42 w + the pixel footprint
//     out; w = the band width the view keeps at about four screen pixels), light added to the ground (HDR: its core blooms)
//   - the ink: the legacy's ground rings mix the ground's colour towards the ring's own colour where they draw (diffuse -> tone / 6,
//     its tones being x6) as well as adding light; here the marks (line, arcs, dashes) mix the ground towards the team colour lit
//     as the ground is (by day full, by night dim), so the ring reads on bright sand and concrete by day; by night the light carries it
//   - the arcs: a second, finer line just outside, broken into four arcs (the legacy's target marker), turning slowly
//   - the ticks: the legacy's capture-ring ticks, pow(.5 + .5 cos(n a), k), on a band just inside the line, as many and as far
//     apart as the units' range dots and dash-shaped like them, crawling round with them
//   - a soft halo either side of the line
//   - one outline for several guns of one type: the part of a ring inside another ring of the same type fades out exactly where
//     the two lines cross (per pixel, up to 16 other rings)
// Drawn on a patch made of the drawn ground's own triangles (OzGroundPatch): it lies on the ground, never in it, never above it;
// vertex alpha = the patch's mask (0 next to water / a cliff, lattice only). Premultiplied: rgb = ink x tint + light, a = ink.
Shader "OpenZ/GroundRing"
{
    Properties
    {
        [HDR] _Color ("Ring colour (the team's ground-disc colour x gain)", Color) = (1, 0.3, 0.2, 1)
        _Center ("x, z = the gun's rule centre, z = range (m), w = alpha", Vector) = (0, 0, 10, 1)
        _Look ("x = band width w (m), y = ticks on the ring, z = crawl (rad), w = night 0..1", Vector) = (0.15, 40, 0, 0)
        _Ink ("x = ink (the ground mixed towards the team colour), y = light level, z = the tint's light (day 0.8, night 0.12), w = spare", Vector) = (0.95, 0.22, 0.8, 0)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+50" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "GroundRing"
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
                float4 _Look;
                float4 _Ink;
                float _ZTest;
            CBUFFER_END
            // the other rings of the same gun type (x, z, range, spare): set per draw, never a material value
            float4 _Others[16];
            float _OtherCount;

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
                float2 q = i.positionWS.xz - _Center.xy;
                float R = _Center.z, alpha = _Center.w;
                float d = length(q), aa = max(fwidth(d), 0.01);
                float w = max(_Look.x, 0.02), crawl = _Look.z, x = d - R;
                float ang = atan2(q.y, q.x);

                // the range line, the ink under it, the arcs outside, the ticks inside, the halo
                float core = 1.0 - smoothstep(0.12 * w, 0.42 * w + aa, abs(x));
                float quadrant = abs(fmod(ang - crawl + 0.785398 + 62.831853, 1.570796) - 0.785398);
                float arcs = (1.0 - smoothstep(0.06 * w, 0.24 * w + aa, abs(x - 1.35 * w))) * smoothstep(0.15, 0.23, quadrant);
                float tickK = 0.5 + 0.5 * cos((ang - crawl) * max(_Look.y, 8.0));
                float ticks = pow(saturate(tickK), 4.0) * (1.0 - smoothstep(0.30 * w, 0.55 * w + aa, abs(x + 1.75 * w)));
                float halo = (1.0 - smoothstep(0.25 * w, 3.0 * w, abs(x))) * 0.22;

                // one outline for a group of one type: inside another ring of the type = left out, faded where the lines cross
                float keep = 1.0;
                for (int k = 0; k < 16; k++)
                {
                    if (k >= (int)_OtherCount) break;
                    float od = length(i.positionWS.xz - _Others[k].xy) - _Others[k].z;
                    keep *= smoothstep(-0.5 * w - aa, 0.5 * w + aa, od);
                }

                float m = alpha * keep * saturate(i.mask);
                float3 tone = _Color.rgb;
                float light = _Ink.y;
                float3 glow = tone * light * (core + arcs * 0.6 + ticks * 0.85 + halo);
                float inkA = saturate(_Ink.x * (core + arcs * 0.7 + ticks * 0.9)) * m;
                float3 tint = tone * _Ink.z;
                return float4(tint * inkA + glow * m, inkA);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
