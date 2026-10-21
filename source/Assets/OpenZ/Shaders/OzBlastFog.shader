// OpenZ/BlastFog: the ground dust of a fort's blast drawn as low fog (OzBlastFog.cs, OpenZNukeMushroom's base surge).
// One pass, premultiplied alpha (Blend One OneMinusSrcAlpha), no keywords (nothing a player build can strip).
//  - shape: the game's own 4096 smoke flipbook (Resources/OpenZ/FX/Smoke: density in alpha) + the 1024 billow detail, eroded more and
//    more over life, inside a wide soft radial falloff: no disc, no ball outline;
//  - volume soft particle: every particle is treated as a ball of fog (centre and size come with the particle); the share of the
//    ball's depth that lies in front of the scene is the share of its density that is drawn, so where the ground (or a ruin, a tree)
//    cuts a puff it thins out smoothly to nothing: never a hard line where fog meets the terrain;
//  - fade in / out: the particle colour's alpha (the effect's own curve over life) x the erosion over life;
//  - light: sun with a wide wrap + sky ambient, so it reads as lit dust by day and goes dark at night like the rest of the scene.
// Vertex streams (as OpenZFxPlayer's channels): TEXCOORD0 = uv.xy, age 0..1, stable random; TEXCOORD1 = velocity xyz, size;
// TEXCOORD2.xyz = particle centre (world).
Shader "OpenZ/BlastFog"
{
    Properties
    {
        _SmokeAtlas ("Smoke flipbook 8x8 (density in alpha; linear)", 2D) = "black" {}
        _SmokeDetail ("Billow detail (tileable; linear)", 2D) = "grey" {}
        _Density ("Density", Range(0, 2)) = 1
        _EdgeSoft ("Radial softness (share of the radius)", Range(0.05, 1)) = 0.6
        _DetailAmt ("Detail erosion", Range(0, 1)) = 0.45
        _DetailScale ("Detail repeats across a puff", Float) = 2.2
        _Dissolve ("Extra erosion at the end of life", Range(0, 1)) = 0.6
        _BallK ("Fog ball radius (share of the particle half size)", Range(0.2, 1.5)) = 0.85
        _SunK ("Sun light share", Range(0, 2)) = 0.75
        _AmbK ("Sky light share", Range(0, 2)) = 0.85
        _Wrap ("Light wrap", Range(0, 1)) = 0.6
        _Spin ("Spin over life (rad)", Float) = 0.35
        _Boil ("Detail drift over life", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }

        Pass
        {
            Name "BlastFog"
            Tags { "LightMode"="UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FogVert
            #pragma fragment FogFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SmokeAtlas_ST;
                float4 _SmokeDetail_ST;
                float _Density;
                float _EdgeSoft;
                float _DetailAmt;
                float _DetailScale;
                float _Dissolve;
                float _BallK;
                float _SunK;
                float _AmbK;
                float _Wrap;
                float _Spin;
                float _Boil;
            CBUFFER_END
            TEXTURE2D(_SmokeAtlas); SAMPLER(sampler_SmokeAtlas);
            TEXTURE2D(_SmokeDetail); SAMPLER(sampler_SmokeDetail);

            struct FogAttributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float4 uv         : TEXCOORD0;
                float4 pvel       : TEXCOORD1;
                float4 pctr       : TEXCOORD2;
            };

            struct FogVaryings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float4 uv         : TEXCOORD0;
                float4 ctrSize    : TEXCOORD1;     // xyz centre (world), w size (m)
            };

            FogVaryings FogVert(FogAttributes v)
            {
                FogVaryings o = (FogVaryings)0;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(positionWS);
                o.color = v.color;
                o.uv = v.uv;
                o.ctrSize = float4(v.pctr.xyz, max(v.pvel.w, 1e-3));
                return o;
            }

            float4 FogFrag(FogVaryings i) : SV_Target
            {
                float2 q = i.uv.xy * 2.0 - 1.0;
                float r2 = dot(q, q);
                if (r2 >= 1.0) discard;
                float age = saturate(i.uv.z);
                float rnd = i.uv.w;

                // the flipbook puff: per-particle variant row, mirror, rotation; two frames blended over life
                float ang = rnd * 6.2831853 + age * _Spin;
                float cs = cos(ang), sn = sin(ang);
                float2 qr = float2(cs * q.x - sn * q.y, sn * q.x + cs * q.y);
                qr.x *= frac(rnd * 7.31) < 0.5 ? -1.0 : 1.0;
                float row = floor(frac(rnd * 13.17) * 7.999);
                float ft = age * 7.0;
                float c0 = min(floor(ft), 6.0);
                float fb = saturate(ft - c0);
                float2 fuv = clamp(qr * 0.42 + 0.5, 0.004, 0.996);          // a little inside the frame: the puff's own edge stays soft
                float2 uv0 = (float2(c0, row) + fuv) * 0.125;
                float d0 = SAMPLE_TEXTURE2D(_SmokeAtlas, sampler_SmokeAtlas, uv0).a;
                float d1 = SAMPLE_TEXTURE2D(_SmokeAtlas, sampler_SmokeAtlas, uv0 + float2(0.125, 0.0)).a;
                float dens = lerp(d0, d1, fb);
                float2 duv = qr * (0.5 * _DetailScale) + float2(rnd * 17.3, rnd * 5.1) + float2(0.0, age * _Boil * 0.12);
                float det = SAMPLE_TEXTURE2D(_SmokeDetail, sampler_SmokeDetail, duv).r * 0.62
                          + SAMPLE_TEXTURE2D(_SmokeDetail, sampler_SmokeDetail, duv * 2.37 + 0.31).g * 0.38;
                float erode = _DetailAmt * (1.0 + _Dissolve * 2.0 * smoothstep(0.35, 1.0, age));
                dens = saturate(dens * 1.15 - erode * (1.0 - det));
                // wide soft radial falloff: no outline at any size
                float rr = sqrt(r2);
                float radial = 1.0 - smoothstep(1.0 - _EdgeSoft, 1.0, rr);
                radial *= radial;
                float cover = dens * radial * _Density * i.color.a;

                // volume soft particle: the ball of fog behind this pixel (the quad faces the camera, its centre is the ball's)
                float R = i.ctrSize.w * 0.5 * _BallK;
                float half_ = R * sqrt(saturate(1.0 - r2));                  // half the ball's depth along the view at this pixel
                float ctrEye = -TransformWorldToView(i.ctrSize.xyz).z;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float front = ctrEye - half_;
                float inFront = saturate((sceneEye - front) / max(2.0 * half_, 0.05));
                cover *= inFront * (2.0 - inFront);                            // eased: thins smoothly to nothing where the scene cuts the ball
                if (cover <= 0.002) discard;

                // light: sun (wrapped) + sky
                Light mainLight = GetMainLight();
                float3 nrm = normalize(float3(qr.x * 0.35, 1.0, qr.y * 0.35));
                float ndl = saturate((dot(nrm, mainLight.direction) + _Wrap) / (1.0 + _Wrap));
                float3 light = mainLight.color * (ndl * _SunK * mainLight.distanceAttenuation) + SampleSH(float3(0.0, 1.0, 0.0)) * _AmbK;
                float3 albedo = i.color.rgb * (0.86 + 0.14 * det);
                float3 rgb = albedo * light;
                return float4(rgb * cover, cover);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
