// OpenZ/GroundDust: the shuttle's take-off / landing downwash as dust raised off the ground it stands on (OzShuttleDownwash.Ground.cs).
// The shape is the fort blast fog's (OpenZ/BlastFog: the game's 8x8 smoke flipbook, billow detail eroded over life, a wide soft radial
// falloff, the volume soft particle against the scene depth). The COLOUR is the ground's own: per particle (in the vertex stage, the same
// for its four corners) the camera's opaque copy is read round the puff's place on screen (a centre tap and two rings of six), every tap
// checked against the depth copy and kept only when what it sees lies no higher than the puff itself (the ground, never the ship's hull,
// a unit or a tree above it); their brightness-weighted mean is the lit ground under the puff. That ground is pulled toward its soil when it is planted
// (green), a little toward a pale dry tint, kept at the ground's own brightness and lifted (_Lift by day, _NightLift at night, the moon's
// blue cast partly taken out): light, translucent dust in the ground's colour, never darker than the ground it rises from.
// The particle colour's rgb x _MulScale is a multiplier the script works out for the light that is drawn after the opaque copy (the jets'
// ground pool): the dust over the pool is lit as the pool lights the ground. Its alpha is the opacity (the effect's curve over life).
// No taps kept (the puff is in front of the hull everywhere round it): _Fallback (the script's estimate of the lit ground).
// One pass, premultiplied alpha, no keywords. Needs the camera's depth and opaque copies (on in every graphics preset's pipeline asset);
// the script falls back to OpenZ/BlastFog when the shader or the copies are missing.
// Vertex streams (as OzBlastFog.MakeSystem sets them): TEXCOORD0 = uv.xy, age 0..1, stable random; TEXCOORD1 = velocity xyz, size;
// TEXCOORD2.xyz = particle centre (world).
// Soft edges: every corner of the camera-facing quad is moved along its view ray to the front of the puff's ball (the same pixels), so
// the depth test never cuts the dust along the line where the ground passes in front of the quad; the fade does all of that, softly.
// Steady colour: the ground is read through a smooth window over a lattice fixed in the world (not single pixels round the puff's screen
// point, which hopped to new pixels every frame and flickered), each read held to the brightest lit ground; the jets' pool light is
// taken at the puff's place now from the material (_PoolLin / _PoolCentre / _PoolShape), so overlapping puffs carry the same light.
Shader "OpenZ/GroundDust"
{
    Properties
    {
        _SmokeAtlas ("Smoke flipbook 8x8 (density in alpha; linear)", 2D) = "black" {}
        _SmokeDetail ("Billow detail (tileable; linear)", 2D) = "grey" {}
        _Density ("Density", Range(0, 2)) = 1
        _EdgeSoft ("Radial softness (share of the radius)", Range(0.05, 1)) = 1
        _DetailAmt ("Detail erosion", Range(0, 1)) = 0.4
        _DetailScale ("Detail repeats across a puff", Float) = 1.2
        _Dissolve ("Extra erosion at the end of life", Range(0, 1)) = 0.9
        _BallK ("Fog ball radius (share of the particle half size)", Range(0.2, 1.5)) = 0.85
        _Spin ("Spin over life (rad)", Float) = 0.25
        _Boil ("Detail drift over life", Float) = 0.6
        _Lift ("Dust over the ground's brightness by day", Range(1, 2)) = 1.25
        _NightLift ("Dust over the ground's brightness at night", Range(1, 2)) = 1.22
        _PaleMix ("Pull toward the pale dry tint", Range(0, 1)) = 0.4
        _SoilMix ("Pull of a planted ground toward its soil", Range(0, 1)) = 0.6
        _NightWarm ("Share of the moon's blue cast taken out", Range(0, 1)) = 0.35
        _Night ("Night share (set by the script)", Range(0, 1)) = 0
        _MulScale ("Particle colour scale of the light multiplier", Float) = 8
        _TapRadius ("Taps' outer ring (share of the puff's screen radius)", Range(0.1, 1.5)) = 0.8
        _TapMaxPx ("Taps' outer ring at most (px)", Float) = 140
        _Rings ("Tap rings (1 = LOW: the centre + 6, 2 = the centre + 12)", Float) = 2
        _AboveTol ("A tap may see this far over the puff's centre (m)", Float) = 0.25
        _PullK ("Quad moved toward the camera (share of the ball radius)", Range(0, 1.5)) = 1
        _FadeMinK ("Least depth of the soft fade (share of the ball radius)", Range(0, 2)) = 0.6
        _WinK ("Ground window: x the taps' ring", Range(0.5, 3)) = 1.4
        _TapMaxLum ("Brightest ground a read may count (linear luminance)", Float) = 1
        _PoolLin ("Jets' ground pool light (linear; set by the script)", Vector) = (0, 0, 0, 0)
        _PoolCentre ("Pool centre xyz, yaw w (rad; set by the script)", Vector) = (0, 0, 0, 0)
        _PoolShape ("Pool across, along (m), edge softness, share (set by the script)", Vector) = (8, 7, 0.55, 1)
        _SoilChroma ("Soil colour (linear, its chroma is used)", Color) = (0.342, 0.262, 0.133, 1)
        _PaleChroma ("Pale dry tint (linear, its chroma is used)", Color) = (0.571, 0.509, 0.393, 1)
        _Fallback ("Lit ground estimate when no tap is kept (linear)", Color) = (0.3, 0.26, 0.2, 1)
        _SunK ("(unused here: kept for the shared light code)", Float) = 0.75
        _AmbK ("(unused here: kept for the shared light code)", Float) = 0.85
        _Wrap ("(unused here: kept for the shared light code)", Float) = 0.6
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "PreviewType"="Plane" }

        Pass
        {
            Name "GroundDust"
            Tags { "LightMode"="UniversalForward" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DustVert
            #pragma fragment DustFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _SmokeAtlas_ST;
                float4 _SmokeDetail_ST;
                float _Density;
                float _EdgeSoft;
                float _DetailAmt;
                float _DetailScale;
                float _Dissolve;
                float _BallK;
                float _Spin;
                float _Boil;
                float _Lift;
                float _NightLift;
                float _PaleMix;
                float _SoilMix;
                float _NightWarm;
                float _Night;
                float _MulScale;
                float _TapRadius;
                float _TapMaxPx;
                float _Rings;
                float _AboveTol;
                float _PullK;
                float _FadeMinK;
                float _WinK;
                float _TapMaxLum;
                float4 _PoolLin;
                float4 _PoolCentre;
                float4 _PoolShape;
                float4 _SoilChroma;
                float4 _PaleChroma;
                float4 _Fallback;
                float _SunK;
                float _AmbK;
                float _Wrap;
            CBUFFER_END
            TEXTURE2D(_SmokeAtlas); SAMPLER(sampler_SmokeAtlas);
            TEXTURE2D(_SmokeDetail); SAMPLER(sampler_SmokeDetail);

            struct DustAttributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;
                float4 uv         : TEXCOORD0;
                float4 pvel       : TEXCOORD1;
                float4 pctr       : TEXCOORD2;
            };

            struct DustVaryings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;        // rgb: the dust's lit colour (linear), a: opacity
                float4 uv         : TEXCOORD0;
                float4 ctrSize    : TEXCOORD1;    // xyz centre (world), w size (m)
            };

            float DustLum(float3 c) { return dot(c, float3(0.2126, 0.7152, 0.0722)); }

            // 0..1: how green (planted) a linear ground colour is (hue 0.15..0.5, saturation over 0.12), as the script's DustOf
            float Greenness(float3 lin)
            {
                float3 s = pow(max(lin, 1e-5), 1.0 / 2.2);
                float mx = max(s.r, max(s.g, s.b)), mn = min(s.r, min(s.g, s.b)), d = mx - mn;
                float sat = d / max(mx, 1e-5);
                if (sat < 0.12 || d < 1e-5) return 0.0;
                float h;
                if (mx == s.r) h = (s.g - s.b) / d;
                else if (mx == s.g) h = (s.b - s.r) / d + 2.0;
                else h = (s.r - s.g) / d + 4.0;
                h = frac(h / 6.0 + 1.0);
                return smoothstep(0.15, 0.22, h) * (1.0 - smoothstep(0.48, 0.56, h));
            }

            // the ground's colour (linear, as lit) -> the dust it raises (linear, lit)
            float3 DustOfGround(float3 g)
            {
                float L = DustLum(g);
                float3 soil = _SoilChroma.rgb / max(DustLum(_SoilChroma.rgb), 1e-4) * L;
                float3 pale = _PaleChroma.rgb / max(DustLum(_PaleChroma.rgb), 1e-4) * L;
                float3 c = lerp(g, soil, _SoilMix * Greenness(g));
                c = lerp(c, pale, _PaleMix);
                float n = saturate(_Night);
                float3 cast = c / max(DustLum(c), 1e-5);
                c *= pow(max(cast, 1e-3), -_NightWarm * n);
                c *= L / max(DustLum(c), 1e-5);
                return c * lerp(_Lift, _NightLift, n);
            }

            // One level of the ground lattice: points on a world grid of spacing s round the puff (round(centre / s) + -n..n, on the
            // puff's height; they stay where they are while the puff moves), each weighted by a smooth window: Rw across the view,
            // Rw x stretch along it (the old screen ring's footprint on the ground). A point counts when what its pixel sees is no higher
            // than the puff (soft over _AboveTol: the hull, a unit, a tree never count); its colour is a bilinear read of the opaque
            // copy held to _TapMaxLum (a lamp or a glow pixel never outweighs the ground). accC: colour x weight x brightness (rgb) and
            // weight x brightness (a); accK: the window kept (x) and the whole window (y).
            void LatticeLevel(float3 ctr, float s, int n, float Rw, float2 hdir, float stretch, float maxY, out float4 accC, out float2 accK)
            {
                accC = 0.0; accK = 0.0;
                float2 c0 = round(ctr.xz / s);
                float invR2 = 1.0 / (Rw * Rw);
                float2 scr = _ScaledScreenParams.xy;
                [loop] for (int j = -n; j <= n; j++)
                {
                    [loop] for (int i = -n; i <= n; i++)
                    {
                        float2 P = (c0 + float2(i, j)) * s;
                        float2 dl = P - ctr.xz;
                        float al = dot(dl, hdir) / stretch, ac = dl.y * hdir.x - dl.x * hdir.y;
                        float q = 1.0 - (al * al + ac * ac) * invR2;
                        if (q <= 0.0) continue;
                        float w = q * q;
                        accK.y += w;
                        float3 pw = float3(P.x, ctr.y, P.y);
                        float4 pcs = TransformWorldToHClip(pw);
                        if (pcs.w <= 1e-3) continue;
                        float4 sp = ComputeScreenPos(pcs);
                        float2 uv = sp.xy / sp.w;
                        float2 pxf = uv * scr;
                        float edge = saturate(min(min(pxf.x, scr.x - pxf.x), min(pxf.y, scr.y - pxf.y)) * 0.125);   // soft over 8 px at the screen's edge
                        if (edge <= 0.0) continue;
                        uint2 px = (uint2)clamp(pxf, 0.0, scr - 1.0);
                        float raw = LOAD_TEXTURE2D_X(_CameraDepthTexture, px).r;
                        #if UNITY_REVERSED_Z
                            if (raw <= 1e-6) continue;                                   // nothing drawn there (the far plane)
                        #else
                            if (raw >= 1.0 - 1e-6) continue;
                        #endif
                        float eye = LinearEyeDepth(raw, _ZBufferParams);
                        float3 pVS = TransformWorldToView(pw);
                        float3 hitVS = pVS * (eye / max(-pVS.z, 1e-4));
                        float hitY = mul(UNITY_MATRIX_I_V, float4(hitVS, 1.0)).y;
                        float keep = saturate(0.5 + (maxY - hitY) / max(_AboveTol, 1e-3));
                        float3 c = SAMPLE_TEXTURE2D_X_LOD(_CameraOpaqueTexture, sampler_LinearClamp, ClampAndScaleUVForBilinear(uv, _CameraOpaqueTexture_TexelSize.xy), 0).rgb;
                        float L = DustLum(c);
                        float k = min(1.0, _TapMaxLum / max(L, 1e-6));
                        c *= k; L *= k;
                        float wk = w * keep * edge;
                        accC += float4(c, 1.0) * (wk * (L + 0.02));
                        accK.x += wk;
                    }
                }
            }

            DustVaryings DustVert(DustAttributes v)
            {
                DustVaryings o = (DustVaryings)0;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.uv = v.uv;
                float size = max(v.pvel.w, 1e-3);
                o.ctrSize = float4(v.pctr.xyz, size);
                // The quad faces the camera through the puff's centre. Where the ground (or the hull) passes in front of that plane the
                // depth test would throw the dust away along a hard straight line while the soft fade below is still at three quarters.
                // Each corner moves along its own view ray toward the camera to the front of the ball (the same pixel on screen, only
                // nearer), so the depth test only ever cuts where the fade has already reached nothing.
                float3 cornerVS = TransformWorldToView(positionWS);
                float eyeC = max(-cornerVS.z, 1e-3);
                float pull = min(size * 0.5 * _BallK * _PullK, max(eyeC - 2.0 * _ProjectionParams.y, 0.0));   // never through the near plane
                if (unity_OrthoParams.w > 0.5) cornerVS.z += pull;                                                // orthographic: along the view
                else cornerVS *= (eyeC - pull) / eyeC;                                                          // perspective: along its ray
                o.positionCS = TransformWViewToHClip(cornerVS);

                // The ground round the puff (the same for all four corners): a smooth window over a lattice fixed in the world (see
                // LatticeLevel), as wide as the taps' ring was (_TapRadius x the puff's screen radius, at most _TapMaxPx) x _WinK. Two
                // lattice levels (spacing sa and 2 sa, sa a power of two of 1/8 m) are cross-faded by the window's size, so the lattice
                // never jumps while the puff grows. The window slides with the puff but its points stay put: the colour changes
                // smoothly from frame to frame (13 single-pixel taps round the puff's screen point hopped to new pixels every frame).
                float4 ctrCS = TransformWorldToHClip(v.pctr.xyz);
                float w = max(ctrCS.w, 1e-3);
                float radPx = size * 0.5 / w * abs(UNITY_MATRIX_P[1][1]) * 0.5 * _ScaledScreenParams.y;     // the puff's half size on screen (px)
                float ringW = size * 0.5 * min(_TapRadius, _TapMaxPx / max(radPx, 1e-3));                  // the taps' ring in metres
                float Rw = max(_WinK * ringW, 0.05);
                float3 rayD = unity_OrthoParams.w > 0.5 ? -UNITY_MATRIX_V[2].xyz : normalize(v.pctr.xyz - _WorldSpaceCameraPos);
                float hl = length(rayD.xz);
                float2 hdir = hl > 1e-4 ? rayD.xz / hl : float2(0.0, 1.0);
                float stretch = 1.0 / max(abs(rayD.y), 0.35);                                               // a screen disc on the sloped ground
                bool low = _Rings < 1.5;                                                                     // LOW: a smaller lattice
                int nA = low ? 3 : 5, nB = low ? 2 : 3;
                float fitA = low ? 2.5 : 4.0;                                                                // the window = fitA x the finest spacing
                float smin = Rw * stretch / fitA;
                float sa = 0.125 * exp2(ceil(log2(max(smin, 1e-3) / 0.125)));
                float fa = saturate(log2(sa / max(smin, 1e-3))), fb = 1.0 - fa;
                float maxY = v.pctr.y + _AboveTol;
                float4 cA, cB; float2 kA, kB;
                LatticeLevel(v.pctr.xyz, sa, nA, Rw, hdir, stretch, maxY, cA, kA);
                LatticeLevel(v.pctr.xyz, 2.0 * sa, nB, Rw, hdir, stretch, maxY, cB, kB);
                float tA = max(kA.y, 1e-6), tB = max(kB.y, 1e-6);
                float4 accC = fa * cA / tA + fb * cB / tB;
                float share = fa * kA.x / tA + fb * kB.x / tB;
                // brightness-weighted mean of what the window sees of the ground (a puff over a lit patch takes its light)
                float3 ground = accC.a > 1e-6 ? accC.rgb / accC.a : _Fallback.rgb;
                ground = lerp(_Fallback.rgb, ground, saturate(share * 6.5));          // a window that sees little ground is partly trusted
                // the jets' ground pool (drawn after the opaque copy) at the puff's place NOW, as it lights the ground under it
                float3 pd = v.pctr.xyz - _PoolCentre.xyz;
                float cyw = cos(_PoolCentre.w), syw = sin(_PoolCentre.w);
                float pax = (pd.x * cyw - pd.z * syw) / max(_PoolShape.x, 1e-3), paz = (pd.x * syw + pd.z * cyw) / max(_PoolShape.y, 1e-3);
                float pf = saturate((1.0 - 2.0 * sqrt(pax * pax + paz * paz)) / max(_PoolShape.z, 1e-3));
                float3 poolMul = min(1.0 + _PoolLin.rgb * (pf * pf * (3.0 - 2.0 * pf) * _PoolShape.w), _MulScale);
                float3 mulLight = max(v.color.rgb * _MulScale, 0.0) * poolMul;          // the particle's own multiplier (1 with the script's pool numbers)
                o.color = float4(DustOfGround(ground) * mulLight, v.color.a);
                return o;
            }

            float4 DustFrag(DustVaryings i) : SV_Target
            {
                float2 q = i.uv.xy * 2.0 - 1.0;
                float r2 = dot(q, q);
                if (r2 >= 1.0) discard;
                float age = saturate(i.uv.z);
                float rnd = i.uv.w;

                // the flipbook puff (as OpenZ/BlastFog): per-particle variant row, mirror, rotation; two frames blended over life
                float ang = rnd * 6.2831853 + age * _Spin;
                float cs = cos(ang), sn = sin(ang);
                float2 qr = float2(cs * q.x - sn * q.y, sn * q.x + cs * q.y);
                qr.x *= frac(rnd * 7.31) < 0.5 ? -1.0 : 1.0;
                float row = floor(frac(rnd * 13.17) * 7.999);
                float ft = age * 7.0;
                float c0 = min(floor(ft), 6.0);
                float fb = saturate(ft - c0);
                float2 fuv = clamp(qr * 0.42 + 0.5, 0.004, 0.996);
                float2 uv0 = (float2(c0, row) + fuv) * 0.125;
                float d0 = SAMPLE_TEXTURE2D(_SmokeAtlas, sampler_SmokeAtlas, uv0).a;
                float d1 = SAMPLE_TEXTURE2D(_SmokeAtlas, sampler_SmokeAtlas, uv0 + float2(0.125, 0.0)).a;
                float dens = lerp(d0, d1, fb);
                float2 duv = qr * (0.5 * _DetailScale) + float2(rnd * 17.3, rnd * 5.1) + float2(0.0, age * _Boil * 0.12);
                float det = SAMPLE_TEXTURE2D(_SmokeDetail, sampler_SmokeDetail, duv).r * 0.62
                          + SAMPLE_TEXTURE2D(_SmokeDetail, sampler_SmokeDetail, duv * 2.37 + 0.31).g * 0.38;
                float erode = _DetailAmt * (1.0 + _Dissolve * 2.0 * smoothstep(0.35, 1.0, age));
                dens = saturate(dens * 1.15 - erode * (1.0 - det));
                float rr = sqrt(r2);
                float radial = 1.0 - smoothstep(1.0 - _EdgeSoft, 1.0, rr);
                radial *= radial;
                float cover = dens * radial * _Density * i.color.a;

                // volume soft particle: thins smoothly to nothing where the ground (or anything) cuts the ball of dust
                float R = i.ctrSize.w * 0.5 * _BallK;
                float half_ = R * sqrt(saturate(1.0 - r2));
                float ctrEye = -TransformWorldToView(i.ctrSize.xyz).z;
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float front = ctrEye - half_;
                float inFront = saturate((sceneEye - front) / max(max(2.0 * half_, _FadeMinK * R), 0.05));   // never a step at the rim
                cover *= inFront * (2.0 - inFront);
                if (cover <= 0.002) discard;

                float3 rgb = i.color.rgb * (0.86 + 0.14 * det);
                return float4(rgb * cover, cover);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
