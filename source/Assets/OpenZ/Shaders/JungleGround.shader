// OpenZ/JungleGround: the level 16 ground. Not one painted picture: three weight maps say which ground kind owns a place
// (8 points per metre), ten repeating layer pictures (160 points per metre, height in the fourth channel) are mixed for
// every screen point. Where two kinds meet, the one that stands higher at that point wins (grass ends as single tufts over
// rock, pebbles show through bank mud), so no border is a cross-fade band. Steep faces get the cliff rock laid on from the
// side. Each layer is read twice (the second copy turned 37 degrees, chosen in patches) against a visible repeat.
// Far away (a screen point covers more than _FarPx.y metres) the layers are replaced by the picture baked from them.
// Keeps the ground's shared parts: crater cut and crater patch, rain wetness, the night include, fog, depth passes.
// Everything is laid on by world position (x east, z north, y up), so any terrain mesh and the crater patches can use it.
Shader "OpenZ/JungleGround"
{
    Properties
    {
        [NoScaleOffset] _BaseMap ("Far ground picture (the layers baked from above)", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1,1,1,1)
        _Smoothness ("Smoothness", Range(0,1)) = 0.06
        [NoScaleOffset] _Layers ("Layers: grass, verge, mud, rock, wet floor, road, swamp bed, moss top, ruin paving, cliff side (A = height)", 2DArray) = "" {}
        [NoScaleOffset] _SplatA ("Weights A: grass, verge, mud, rock", 2D) = "black" {}
        [NoScaleOffset] _SplatB ("Weights B: wet floor, road, swamp bed, moss top", 2D) = "black" {}
        [NoScaleOffset] _SplatC ("Weights C: ruin paving, shade (1 = open), wetness", 2D) = "white" {}
        [NoScaleOffset] _GroundNoise ("Noise: colour drift, patch choice, border wobble, fine break", 2D) = "gray" {}
        _GroundRect ("Ground pictures: west x, south z, 1 / width, 1 / depth (m)", Vector) = (-24, -280, 0.00390625, 0.0032894737)
        _LayerTile ("Layer tile (m)", Float) = 12.8
        _FarPx ("Far picture: layers full under, picture full over (m per screen point)", Vector) = (0.075, 0.14, 0, 0)
        _CraterMud ("Crater bowl: bare earth amount", Range(0,1)) = 0.8
        [NoScaleOffset] _RoadStuds ("Road studs: one point per map tile (R north, G south, B west, A east side)", 2D) = "black" {}
        _StudColor ("Road stud colour", Color) = (0.824, 0.808, 0.761, 1)
        _StudRule ("Road studs: size, inset from the road edge, pitch (m), amount (0 = none)", Vector) = (0.22, 0.37, 1.3333333, 0)
        _MapTiles ("Map size in tiles", Vector) = (104, 128, 0, 0)
        [NoScaleOffset] _RimCut ("Map sides: where the rebuilt sides replace this ground (white = not drawn)", 2D) = "black" {}
        _RimCutOn ("Map sides cut: 0 off, 1 on", Float) = 0
        _GrassMood ("Grass patches: strength (0 = none), look calibration of the layers (y = saturation, z = brightness)", Vector) = (1, 1, 1, 0)
        _LookCal ("Look calibration against the approved pictures: gain on red, green, blue of the stored colour, a = saturation", Vector) = (1, 1, 1, 1)
        _GrassTint ("Grass: base tint on the stored colour (look calibration of the grass against the approved pictures, plain numbers)", Vector) = (1, 1, 1, 1)
        _GrassWarm ("Grass patches: the drier warm green (tint on the stored colour, plain numbers)", Vector) = (1.058, 1.008, 1.070, 1)
        _GrassCool ("Grass patches: the greyer cool green (tint on the stored colour, plain numbers)", Vector) = (0.961, 0.969, 1.123, 1)
        [NoScaleOffset] _LeafyMask ("Grass fields over the ground pictures' rectangle (R: 1 = inside a field of the transitional grass, 0 = the plain calm grass)", 2D) = "black" {}
        _CalmAmt ("Calm ground: amount on the grass, on the verge, on the far picture (0 = as stored); grass fields (1 = on)", Vector) = (0, 0, 0, 0)
        _FieldRule ("Grass fields: share of the grass weight that goes to the verge inside a field (0 = no fields); mask values between which the far picture gives way to the layers", Vector) = (0, 0.02, 0.15, 0)
        _CalmRule ("Calm ground: blur of the soft copy (times), soft detail kept, fine detail kept, blur of the far picture (times)", Vector) = (8, 0.5, 0.15, 4)
        _CalmDrift ("Calm grass: how much of the slow brightness drift stays (1 = all)", Range(0,1)) = 1
        _TintMud ("Look calibration, bank mud and swamp bed: tint on the stored colour (plain numbers; w 1 = the four tints are in use)", Vector) = (1, 1, 1, 0)
        _TintWet ("Look calibration, wet gorge floor: tint on the stored colour", Vector) = (1, 1, 1, 1)
        _TintPaving ("Look calibration, old paving: tint on the stored colour", Vector) = (1, 1, 1, 1)
        _TintSide ("Look calibration, cliff rock on steep faces: tint on the stored colour", Vector) = (1, 1, 1, 1)
        [NoScaleOffset] _CrackMask ("Road wear over the map's tiles, 16 points per metre (R broken out of the road: 0.5 = the edge, G moss, B broken edge)", 2D) = "black" {}
        _CrackRule ("Road wear: amount (0 = none), tone of a break's shaded edge (times the road's stored colour), lighter crumbled rim beside a break (share), moss amount", Vector) = (0, 0.75, 0, 1)
        _CrackEdge ("Road wear: metres of the picture's ramp, metres the road picture's height moves the edge of a break, drop of the road at a broken edge, metres the moss's edge moves with its amount", Vector) = (0.25, 0.1, 2.4, 0.1)
        _CrackFar ("Road wear: full under, none over (ground m per screen point); metres the lighter rim reaches beside a break", Vector) = (0.075, 0.1, 0.2, 0)
        _CrackMoss ("Road wear: stored colour of the moss (plain numbers)", Vector) = (0.25, 0.27, 0.19, 1)
        _CrackEarth ("Road wear: stored colour of the bare earth in a wide break (plain numbers); a = metres inside the edge that stay in shadow", Vector) = (0.26, 0.22, 0.14, 0.5)
        [HideInInspector] _OzCraterRole ("Deformable terrain: 0 none, 1 ground cut under craters, 2 crater patch", Float) = 0
        [HideInInspector][NoScaleOffset] _OzCraterDecal ("Deformable terrain: crater decal (UV1)", 2D) = "gray" {}
        [HideInInspector] _OzCraterDecalMul ("Deformable terrain: decal is 2x multiply (linear) instead of straight alpha", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor; half _Smoothness;
            float4 _GroundRect; float _LayerTile; float4 _FarPx; half _CraterMud;
            half4 _StudColor; float4 _StudRule; float4 _MapTiles; half _RimCutOn;
            float4 _GrassMood; float4 _GrassWarm; float4 _GrassCool; float4 _LookCal; float4 _GrassTint;
            float4 _CalmAmt; float4 _CalmRule; float4 _FieldRule; float _CalmDrift;
            float4 _TintMud; float4 _TintWet; float4 _TintPaving; float4 _TintSide;
            float4 _CrackRule; float4 _CrackEdge; float4 _CrackFar; float4 _CrackMoss; float4 _CrackEarth;
            half _OzCraterRole; half _OzCraterDecalMul;
        CBUFFER_END
        // deformable terrain (CraterField.cs): up to 12 craters, xy = centre (world xz), z = clip radius, w = ease
        float4 _OzCraters[12]; float _OzCraterCount;
        void OZCraterClip(float3 positionWS)
        {   // role 1 (the rendered ground): no pixel inside a crater; its patch draws the bowl there. Every pass, so depth / SSAO agree.
            if (_OzCraterRole < 0.5 || _OzCraterRole > 1.5 || _OzCraterCount < 0.5) return;
            int n = (int)min(_OzCraterCount, 12.0);
            [loop] for (int k = 0; k < n; k++)
            {
                float2 d = positionWS.xz - _OzCraters[k].xy;
                if (dot(d, d) < _OzCraters[k].z * _OzCraters[k].z) clip(-1);
            }
        }
        // the rebuilt map sides have their own meshes: the old ground under them is not drawn (every pass, so depth agrees)
        TEXTURE2D(_RimCut); SAMPLER(sampler_RimCut);
        void OZJRimClip(float3 positionWS)
        {
            if (_RimCutOn < 0.5) return;
            float2 uv = (positionWS.xz - _GroundRect.xy) * _GroundRect.zw;
            if (SAMPLE_TEXTURE2D_LOD(_RimCut, sampler_RimCut, uv, 0).r > 0.5) clip(-1);
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OpenZNightV2.hlsl"                                            // day-sky reflection fix at night
            #include "OpenZWet.hlsl"                                                // rain wetness (globals; 0 = dry = unchanged)
            #include "OpenZJungleWet.hlsl"                                          // level 16: the road and bare soil's puddles (grass only darkens) + the low grass's and the gravel paths' own puddles
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D_ARRAY(_Layers); SAMPLER(sampler_Layers);
            TEXTURE2D(_SplatA); SAMPLER(sampler_SplatA);
            TEXTURE2D(_SplatB); TEXTURE2D(_SplatC);
            TEXTURE2D(_GroundNoise); SAMPLER(sampler_GroundNoise);
            TEXTURE2D(_OzCraterDecal); SAMPLER(sampler_OzCraterDecal);
            TEXTURE2D(_RoadStuds); SAMPLER(sampler_point_clamp);
            TEXTURE2D(_CrackMask);                                                  // road wear (read with sampler_linear_clamp)
            TEXTURE2D(_LeafyMask); SAMPLER(sampler_linear_clamp);
            TEXTURE2D(_OzLowPuddleMask);                                            // rain puddles on the low grass and the gravel paths (global: OzJungleWeather.LowPuddles.cs): R = depth of a hollow, B = a path's puddle; read with sampler_linear_clamp
            TEXTURE2D(_OzTrackMask);                                                // the gravel paths (globals: OzL16GravelTracks.cs): R path, G pebble density, B verge; read with sampler_linear_clamp
            TEXTURE2D(_OzGravelTex); TEXTURE2D(_OzGravelAux);                       // the gravel: pebble colour + rank (A) / pebble cover, contact shadow, earth detail; read with sampler_Layers
            float4 _OzTrackRule; float4 _OzTrackFar;                               // amount, strength in the middle, pebble strength, contact shadow / full under, none over (m per screen point), edge sharpness, edge moved by the ground's height
            TEXTURE2D(_OzTrodMask);                                                 // trodden grass round the start camp and the shuttle (globals: OzL16TroddenCamp.cs): R the grass's share handed to the transitional grass, G worn; read with sampler_linear_clamp, inside its window only
            float4 _OzTrodRule; float4 _OzTrodRect; float4 _OzTrodTint; float4 _OzTrodFlat;   // amount (0 = none), share to the transitional grass, worn strength, worn edge moved by the ground's height / the window: west x, south z, 1 / width, 1 / depth (m) / the worn grass's tint on its stored colour, w: its share that goes to the flattened colour / the flattened, drier colour (stored)
            float4 _OzGravelEarth; float4 _OzGravelEarth2; float4 _OzGravelVerge;   // stored colours: pale earth (w: metres of the gravel picture), darker earth, brownish verge (w: amount)
            TEXTURE2D(_OzJoinMask);                                                 // where the paths meet the roads (globals: OzL16GravelJoins.cs): R spill over the road's edge, G pebbles on the asphalt, B studs taken off, A the mouth zone on the asphalt; read with sampler_linear_clamp
            float4 _OzJoinRule; float4 _OzJoinEarth;                               // amount (0 = none), strength at the mouth, -, studs taken off / stored colour of the pale dust over the asphalt

            float OZJHash(float2 p) { float3 p3 = frac(float3(p.xyx) * 0.1031); p3 += dot(p3, p3.yzx + 33.33); return frac((p3.x + p3.y) * p3.z); }

            // one side of a road tile: d = distance from that side into the tile, a = place along the side inside the tile (0..2 m),
            // along = the world coordinate the studs are counted on, code = the side's value from the stud picture, line = which edge line
            half OZJStudSide(float d, float a, float along, float code, float lineId, float pxm)
            {
                if (code < 0.5) return 0;
                float c = round(code) - 1.0; float lo = fmod(c, 2.0), hi = floor(c * 0.5);
                if ((lo < 0.5 && a < 0.75) || (hi < 0.5 && a > 1.25)) return 0;                // nothing right at a corner or where the edge stops
                float pitch = _StudRule.z, hs = _StudRule.x * 0.5;
                float k = floor(along / pitch); float m = along - (k + 0.5) * pitch; float e = d - _StudRule.y;
                float h = OZJHash(float2(k, lineId));
                if (h < 0.10) return 0;                                                          // gone
                float box = max(abs(m), abs(e)) - hs;
                float h2 = OZJHash(float2(lineId + 7.3, k + 3.1));
                if (h2 < 0.30) box = max(box, ((h2 < 0.15 ? m : -m) + (frac(h2 * 37.0) < 0.5 ? e : -e)) - hs * 1.1);   // chipped: one corner broken off
                half cov = (half)saturate(0.5 - box / max(pxm, 1e-4));
                return cov * (h < 0.32 ? (half)0.5 : (half)0.92);                                 // faded / whole
            }

            // worn white studs along both road edges (the look of the original map), from the tile sides in _RoadStuds
            half3 OZJStuds(float3 P, float pxm, half3 albedo)
            {
                half fade = (half)(_StudRule.w * saturate((0.2 - pxm) / 0.1));
                if (fade <= 0.001) return albedo;
                float2 q = float2(P.x, -P.z) * 0.5; float2 t = floor(q);
                if (t.x < 0.0 || t.y < 0.0 || t.x >= _MapTiles.x || t.y >= _MapTiles.y) return albedo;
                half4 sd = SAMPLE_TEXTURE2D_LOD(_RoadStuds, sampler_point_clamp, float2((t.x + 0.5) / _MapTiles.x, 1.0 - (t.y + 0.5) / _MapTiles.y), 0) * 5.0;
                if (sd.r + sd.g + sd.b + sd.a < 0.5) return albedo;
                float2 l = (q - t) * 2.0;                                                        // metres from the tile's west and north sides
                half s = OZJStudSide(l.y, l.x, P.x, sd.r, t.y * 4.0 + 0.0, pxm);
                s = max(s, OZJStudSide(2.0 - l.y, l.x, P.x, sd.g, t.y * 4.0 + 1.0, pxm));
                s = max(s, OZJStudSide(l.x, l.y, -P.z, sd.b, t.x * 4.0 + 2.0, pxm));
                s = max(s, OZJStudSide(2.0 - l.x, l.y, -P.z, sd.a, t.x * 4.0 + 3.0, pxm));
                half kill = 0;                                                                   // where a gravel path meets the road: no studs across its mouth (the join picture's B; back when the mouths' amount or the paths are off)
                [branch] if (s > 0.001 && _OzJoinRule.x > 0.001 && _OzJoinRule.w > 0.001 && _OzTrackRule.x > 0.001)
                    kill = SAMPLE_TEXTURE2D_LOD(_OzJoinMask, sampler_linear_clamp, float2(P.x / (2.0 * _MapTiles.x), 1.0 + P.z / (2.0 * _MapTiles.y)), 0).b * (half)saturate(_OzJoinRule.w);
                return lerp(albedo, _StudColor.rgb, s * fade * (1.0 - kill));
            }

            static const float OZJ_RELIEF[9] = { 1.0, 0.9, 0.7, 0.9, 0.85, 0.75, 0.5, 0.9, 1.0 };
            static const float OZJ_WOB[9] = { 1.0, -1.0, 0.5, -0.6, 0.2, -0.35, 0.7, -0.8, 0.9 };
            static const float OZJ_BIAS[9] = { 0.0, 0.22, 0.05, 0.0, 0.0, 0.05, 0.0, 0.0, 0.0 };
            static const float OZJ_CA = 0.79863551, OZJ_SA = 0.60181502;          // the second copy of each layer is turned 37 degrees
            static const float OZJ_LOD = 0.785;                                     // derivative scale = mip bias -0.35

            float OZJSm(float t) { t = saturate(t); return t * t * (3.0 - 2.0 * t); }

            // large soft patches on the grass: mostly the base green, here and there a slightly drier warm green or a slightly
            // greyer cool green (two octaves of smooth noise from the world place, patches of about 25 to 60 m, no picture, so
            // nothing repeats). Returns the tint for pure grass, worked on the stored (sRGB) values like the recipe tints.
            float OZJVNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                return lerp(lerp(OZJHash(i), OZJHash(i + float2(1, 0)), f.x), lerp(OZJHash(i + float2(0, 1)), OZJHash(i + float2(1, 1)), f.x), f.y);
            }
            // the level light and the tone curve of the game shift the approved colours: this brings the stored colour back
            half3 OZJCal(half3 cs) { half l = dot(cs, half3(0.299, 0.587, 0.114)); return max(lerp((half3)l, cs, (half)_LookCal.a) * (half3)_LookCal.rgb, (half3)0); }
            half3 OZJMood(float2 xz)
            {
                float m = 0.62 * OZJVNoise(xz / 34.0 + 7.3) + 0.38 * OZJVNoise(float2(0.8 * xz.x - 0.6 * xz.y, 0.6 * xz.x + 0.8 * xz.y) / 15.0 + 3.1);
                float mm = (m - 0.5) * 2.6 * _GrassMood.x;
                half warm = (half)smoothstep(0.25, 0.85, mm), cool = (half)smoothstep(0.25, 0.85, -mm);
                return (half3)_GrassTint.rgb * lerp((half3)1, (half3)_GrassWarm.rgb, warm) * lerp((half3)1, (half3)_GrassCool.rgb, cool);
            }

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv1 : TEXCOORD1; };
            struct V { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD1; float3 normalWS : TEXCOORD2; float2 uv1 : TEXCOORD4; };

            V vert(A i)
            {
                V o; VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS; o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv1 = i.uv1;                                                              // crater decal UV (patch only)
                return o;
            }

            // the layered ground at a world point (linear colour). dpx / dpy: screen derivatives of the world position.
            // lowG (out): the transitional grass's share of the blend and the blended height at this point (the low grass's rain puddles)
            half3 OZJLayers(float3 P, float3 dpx, float3 dpy, float3 nrm, float2 guv, half3 paint, bool patch, half3 mood, half fld, out half2 lowG)
            {
                lowG = half2(0.0, 0.5);
                float invT = 1.0 / _LayerTile;
                half4 sa = SAMPLE_TEXTURE2D_LOD(_SplatA, sampler_SplatA, guv, 0);
                half4 sb = SAMPLE_TEXTURE2D_LOD(_SplatB, sampler_SplatA, guv, 0);
                half4 sc4 = SAMPLE_TEXTURE2D_LOD(_SplatC, sampler_SplatA, guv, 0);
                float w[9] = { sa.r, sa.g, sa.b, sa.a, sb.r, sb.g, sb.b, sb.a, sc4.r };
                // a grass field: the grass hands a share of its weight to the verge, so the field is the transitional grass and its
                // edge is drawn by the same height blend as every grass / verge border (the sum of the weights does not change)
                float give = w[0] * fld; w[0] -= give; w[1] += give;
                // trodden grass round the start camp and the shuttle (level 16; OzL16TroddenCamp.cs, amount 0 = none): a share of the grass
                // goes to the transitional grass there (the picture's R), so the same height blend draws it in small leafy blotches of
                // the drier grass; G is the worn look, laid on after the gravel paths (below)
                half2 trod = (half2)0;
                [branch] if (_OzTrodRule.x > 0.001)
                {
                    float2 tdu = (P.xz - _OzTrodRect.xy) * _OzTrodRect.zw;
                    [branch] if (tdu.x > 0.0 && tdu.y > 0.0 && tdu.x < 1.0 && tdu.y < 1.0)
                        trod = SAMPLE_TEXTURE2D_LOD(_OzTrodMask, sampler_linear_clamp, tdu, 0).rg * (half)saturate(_OzTrodRule.x);
                    float tgv = w[0] * trod.x * _OzTrodRule.y; w[0] -= tgv; w[1] += tgv;
                }
                // the gravel paths of level 16 are laid on after the height blend below (the layers' own weights stay as they are)
                float wsum = 0.0; int k;
                [unroll] for (k = 0; k < 9; k++) wsum += w[k];
                if (wsum < 0.02) return paint;                                              // outside the painted ground
                float winv = 1.0 / wsum;
                half ao = sc4.g, wet = sc4.b;
                // noise: R colour drift 64 m, G patch choice 19 m, B border wobble 4.7 m, A fine break 1.3 m (picture rows run north)
                float2 nq = float2(P.x, -P.z), ndx = float2(dpx.x, -dpx.z), ndy = float2(dpy.x, -dpy.z);
                half nm = SAMPLE_TEXTURE2D_GRAD(_GroundNoise, sampler_GroundNoise, nq / 64.0, ndx / 64.0, ndy / 64.0).r - 0.5;
                half n2 = SAMPLE_TEXTURE2D_GRAD(_GroundNoise, sampler_GroundNoise, nq / 19.0, ndx / 19.0, ndy / 19.0).g - 0.5;
                half ne = SAMPLE_TEXTURE2D_GRAD(_GroundNoise, sampler_GroundNoise, nq / 4.7, ndx / 4.7, ndy / 4.7).b - 0.5;
                half nf = SAMPLE_TEXTURE2D_GRAD(_GroundNoise, sampler_GroundNoise, nq / 1.3, ndx * (0.4 / 1.3), ndy * (0.4 / 1.3)).a - 0.5;
                // steep only where the true face AND its surroundings are steep (no single-cell patches)
                float3 ng = cross(dpy, dpx); ng *= rsqrt(max(dot(ng, ng), 1e-24)); if (ng.y < 0.0) ng = -ng;
                float slope = min(degrees(acos(saturate(ng.y))), degrees(acos(saturate(nrm.y))) + 10.0);
                float side = patch ? 0.0 : OZJSm((slope - 40.0) / 10.0);

                float2 uvL = P.xz * invT, dxL = dpx.xz * (invT * OZJ_LOD), dyL = dpy.xz * (invT * OZJ_LOD);
                float2 uvR = float2(OZJ_CA * P.x - OZJ_SA * P.z, OZJ_SA * P.x + OZJ_CA * P.z) * invT + float2(0.37, 0.39);
                float2 dxR = float2(OZJ_CA * dxL.x - OZJ_SA * dxL.y, OZJ_SA * dxL.x + OZJ_CA * dxL.y);
                float2 dyR = float2(OZJ_CA * dyL.x - OZJ_SA * dyL.y, OZJ_SA * dyL.x + OZJ_CA * dyL.y);

                float sc[10]; half3 col[10]; half ht[10];
                [unroll] for (k = 0; k < 9; k++)
                {
                    sc[k] = -1e9; col[k] = half3(0, 0, 0); ht[k] = 0;
                    float wi = w[k] * winv;
                    [branch] if (wi > 0.015)
                    {
                        half4 t1 = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Layers, sampler_Layers, uvL, k, dxL, dyL);
                        half4 t2 = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Layers, sampler_Layers, uvR, k, dxR, dyR);
                        half kk = k == 8 ? 0.0 : (half)OZJSm(n2 * 7.0 + 0.5 + (t2.a - t1.a) * 0.8);     // the paving keeps one direction
                        half4 t = lerp(t1, t2, kk);
                        float edge = min(1.0, 6.0 * min(wi, 1.0 - wi));
                        sc[k] = 1.5 * wi + OZJ_BIAS[k] * edge + OZJ_RELIEF[k] * (t.a - 0.5) + OZJ_WOB[k] * edge * (1.5 * ne + 0.6 * nf) - 1.4 * side;
                        col[k] = t.rgb; ht[k] = t.a;
                        // road wear (level 16, road layer only; flat: colour and which ground kind wins, no height): cracks, broken
                        // patches, moss in some, a broken edge here and there, from one map-wide picture of 16 points per metre.
                        // R is a distance ramp (0.5 = the edge of what is broken out): the edge is drawn one screen point sharp at any
                        // zoom and is moved by the road picture's own height (crumbs); near its edge a break is in shadow, the middle
                        // of a wide one shows bare earth, beside it lies a lighter crumbled rim. All of it fades out with distance
                        // while the far picture is still under a third in, so nothing pops.
                        [branch] if (k == 5 && _CrackRule.x > 0.001)
                        {
                            float cpx = max(length(dpx), length(dpy));                                   // ground metres per screen point
                            half cf = (half)(_CrackRule.x * saturate((_CrackFar.y - cpx) / max(_CrackFar.y - _CrackFar.x, 1e-4)));
                            [branch] if (cf > 0.001)
                            {
                                half3 ck = SAMPLE_TEXTURE2D_LOD(_CrackMask, sampler_linear_clamp, float2(P.x / (2.0 * _MapTiles.x), 1.0 + P.z / (2.0 * _MapTiles.y)), 0).rgb;
                                sc[k] -= _CrackEdge.z * ck.b * cf;                                       // broken edge: the ground kind beside the road wins here
                                [branch] if (ck.r > 0.004)
                                {
                                    float caa = min(cpx, 0.4 * _CrackEdge.x);                              // one screen point of softening at the edge (10 cm at most)
                                    float cdE = (0.5 - ck.r) * _CrackEdge.x + _CrackEdge.y * (t.a - 0.33);   // metres outside the edge of the break
                                    half cin = (half)saturate(0.5 - cdE / caa);                            // 1 inside the break
                                    half cdeep = (half)OZJSm((-cdE - _CrackEarth.a) / max(0.5 * _CrackEdge.x - _CrackEarth.a, 1e-4));   // 0 near the edge (in shadow), 1 in the middle of a wide break
                                    half crim = (half)saturate(2.0 - 2.0 * cdE / max(_CrackFar.z, 1e-4)) * (1.0 - cin) * saturate(0.75 + 2.2 * ne) * cf;   // the crumbled rim beside it: full on its inner half, in stretches
                                    half cmo = ck.g * (half)_CrackRule.w;
                                    half ctuft = saturate((cmo - 0.30 + 0.6 * ne + 1.2 * nf) / 0.25);        // moss in tufts: thick or thin by the picture's amount
                                    half cmoss = (half)saturate(0.5 - (cdE - _CrackEdge.w * (cmo - 0.6) + 0.10 * nf) / caa) * ctuft * cf;   // over the edge where it is thick, inside it where thin
                                    half3 crs = LinearToSRGB(t.rgb);                                       // tones on the stored colour, like the recipe's tints
                                    half3 chole = lerp(crs * lerp((half)_CrackRule.y, (half)1, saturate((t.a - 0.5) * 1.6)), (half3)_CrackEarth.rgb * (0.7 + 0.6 * t.a), cdeep);   // the road's pebbles stay lighter in the shaded edge
                                    crs *= 1.0 + (half)_CrackRule.z * crim;
                                    crs = lerp(crs, chole, cin * cf);
                                    crs = lerp(crs, (half3)_CrackMoss.rgb * (0.6 + 1.2 * t.a), cmoss);
                                    col[k] = SRGBToLinear(crs);
                                }
                            }
                        }
                        // calm ground (grass, verge): the leaf detail of the layer is taken down to a quiet surface that units read
                        // on: the layer's mean colour, a part of its soft shapes (the same picture read blurred) and a little of
                        // its fine detail. The height above is untouched, so every border keeps its shape. The same everywhere,
                        // in the grass fields too.
                        half calmK = k == 0 ? (half)_CalmAmt.x : k == 1 ? (half)_CalmAmt.y : (half)0;
                        [branch] if (calmK > 0.01)
                        {
                            half3 s1 = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Layers, sampler_Layers, uvL, k, dxL * _CalmRule.x, dyL * _CalmRule.x).rgb;
                            half3 s2 = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Layers, sampler_Layers, uvR, k, dxR * _CalmRule.x, dyR * _CalmRule.x).rgb;
                            half3 soft = lerp(s1, s2, kk);
                            half3 mean = SAMPLE_TEXTURE2D_ARRAY_LOD(_Layers, sampler_Layers, uvL, k, 16).rgb;
                            col[k] = lerp(t.rgb, mean + (soft - mean) * (half)_CalmRule.y + (t.rgb - soft) * (half)_CalmRule.z, calmK);
                        }
                    }
                }
                sc[9] = -1e9; col[9] = half3(0, 0, 0); ht[9] = 0;
                [branch] if (side > 0.01)
                {   // the cliff rock, laid on from the side: along the wall and down, by the direction the face looks
                    float ax = pow(abs(ng.x), 8.0), az = pow(abs(ng.z), 8.0); half wz = (half)(az / max(ax + az, 1e-12));
                    float s3 = invT * OZJ_LOD;
                    half4 tA = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Layers, sampler_Layers, float2(P.z * invT + 0.5, P.y * invT), 9, float2(dpx.z, dpx.y) * s3, float2(dpy.z, dpy.y) * s3);
                    half4 tB = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Layers, sampler_Layers, P.xy * invT, 9, dpx.xy * s3, dpy.xy * s3);
                    half4 tc = lerp(tA, tB, wz);
                    sc[9] = 1.5 * (2.0 * side - 0.5) + 0.9 * (tc.a - 0.5); col[9] = tc.rgb; ht[9] = tc.a;
                }
                float ma = -1e9, m2 = -1e9;
                [unroll] for (k = 0; k < 10; k++) { if (sc[k] > ma) { m2 = ma; ma = sc[k]; } else if (sc[k] > m2) m2 = sc[k]; }
                float bs = 0.0, veg = 0.0, grass = 0.0; half3 c = half3(0, 0, 0); half hc = 0; half3 lt = half3(0, 0, 0);
                [unroll] for (k = 0; k < 10; k++)
                {
                    float b = max(sc[k] - (ma - 0.07), 0.0);
                    bs += b; c += (half)b * col[k]; hc += (half)b * ht[k];
                    lt += (half)b * (k == 2 || k == 6 ? (half3)_TintMud.rgb : k == 4 ? (half3)_TintWet.rgb : k == 8 ? (half3)_TintPaving.rgb : k == 9 ? (half3)_TintSide.rgb : (half3)1);   // look calibration per kind
                    if (k < 2) veg += b; else if (k == 7) veg += 0.6 * b;
                    if (k == 0) grass += b; else if (k == 1) grass += 0.5 * b;                 // the patches belong to the grass (half on the grassy verge)
                }
                float binv = 1.0 / max(bs, 1e-5); c *= (half)binv; hc *= (half)binv; veg *= binv;
                // the gravel paths (level 16): from the doors to the road. A gravel picture of its own (pebbles of mixed sizes on pale
                // earth, 160 points per metre like the layers, read twice like them: the second copy turned 37 degrees, chosen in
                // patches) is laid over the grass and the verge only (never water, rock, mud or road) where the path picture says so:
                // the earth at the path's strength (the grass shows through the rest), its edge moved by the ground's own height (leafy,
                // not a line), a ragged brownish bare-earth margin along it; each pebble shows where the picture's pebble density is above its rank (they
                // thin out one by one into the grass), with its contact shadow; the big stones only at the thin edge. No ruts, no
                // specks. Fades out with distance like the road wear (the far picture has no paths). Globals from OzL16GravelTracks.cs;
                // amount 0 = none (every other level). Where a path meets a road (the join picture, OzL16GravelJoins.cs; its amount 0 =
                // none) the gravel and its pale earth spill over the road's edge in a ragged fan: on the asphalt of a mouth the join
                // picture's R and G stand for the path picture (thick at the edge, thinner to its end where the road's own grain breaks
                // it up, a few loose pebbles beyond it, never the big stones), the mouth is a little denser on both sides of the edge (no
                // straight line there), and the road's studs are taken off across it (OZJStuds).
                [branch] if (_OzTrackRule.x > 0.001)
                {
                    float tpx = max(length(dpx), length(dpy));                                  // ground metres per screen point
                    half tfd = (half)(_OzTrackRule.x * saturate((_OzTrackFar.y - tpx) / max(_OzTrackFar.y - _OzTrackFar.x, 1e-4)));   // the fade with distance
                    float2 tuv = float2(P.x / (2.0 * _MapTiles.x), 1.0 + P.z / (2.0 * _MapTiles.y));
                    half4 jn = (half4)0;                                                        // the join picture here (R, G times its amount)
                    [branch] if (_OzJoinRule.x > 0.001 && tfd > 0.001)
                        jn = SAMPLE_TEXTURE2D_LOD(_OzJoinMask, sampler_linear_clamp, tuv, 0) * half4((half)saturate(_OzJoinRule.x), (half)saturate(_OzJoinRule.x), 1.0, 1.0);
                    half jrd = (half)saturate(1.0 - veg) * jn.a;                                 // the asphalt's share of this point inside a mouth
                    half tf = tfd * (half)saturate(veg + jrd);                                  // grass and verge; the asphalt only at a mouth
                    [branch] if (tf > 0.001)
                    {
                        half3 tk = SAMPLE_TEXTURE2D_LOD(_OzTrackMask, sampler_linear_clamp, tuv, 0).rgb;
                        half jrt = (half)saturate(1.0 - veg) * (half)saturate(jn.a * 8.0);       // the asphalt anywhere inside a mouth's zone, its soft fringe too
                        tk = lerp(tk, half3(jn.r, jn.g, 0.0), jrt);                              // there: the spill and its pebbles, no bare-earth margin (a path running on over the road never shows through the fringe)
                        [branch] if (tk.r + tk.g + tk.b + jn.r > 0.004)
                        {
                            float invG = 1.0 / max(_OzGravelEarth.w, 0.5);
                            float2 uvG = P.xz * invG, dxG = dpx.xz * (invG * OZJ_LOD), dyG = dpy.xz * (invG * OZJ_LOD);
                            float2 uvG2 = float2(OZJ_CA * P.x - OZJ_SA * P.z, OZJ_SA * P.x + OZJ_CA * P.z) * invG + float2(0.37, 0.39);
                            float2 dxG2 = float2(OZJ_CA * dxG.x - OZJ_SA * dxG.y, OZJ_SA * dxG.x + OZJ_CA * dxG.y);
                            float2 dyG2 = float2(OZJ_CA * dyG.x - OZJ_SA * dyG.y, OZJ_SA * dyG.x + OZJ_CA * dyG.y);
                            half4 gs1 = SAMPLE_TEXTURE2D_GRAD(_OzGravelTex, sampler_Layers, uvG, dxG, dyG), gx1 = SAMPLE_TEXTURE2D_GRAD(_OzGravelAux, sampler_Layers, uvG, dxG, dyG);
                            half4 gs2 = SAMPLE_TEXTURE2D_GRAD(_OzGravelTex, sampler_Layers, uvG2, dxG2, dyG2), gx2 = SAMPLE_TEXTURE2D_GRAD(_OzGravelAux, sampler_Layers, uvG2, dxG2, dyG2);
                            half kg = (half)OZJSm(n2 * 7.0 + 0.5 + (gx2.r - gx1.r) * 0.8);
                            half4 gs = lerp(gs1, gs2, kg), gx = lerp(gx1, gx2, kg);
                            half ramp = saturate((tk.r - 0.5) * (half)_OzTrackFar.z + 0.5 + (0.5 - hc) * (half)_OzTrackFar.w * saturate(4.0 * tk.r * (1.0 - tk.r)));
                            half vg = saturate(tk.b + (0.5 - hc) * (half)_OzTrackFar.w * saturate(4.0 * tk.b * (1.0 - tk.b))) * (half)_OzGravelVerge.w;   // the brownish bare-earth margin (about 1 m past the gravel), its outer line leafy too
                            half cov = max(ramp, vg * 0.95) * (half)_OzTrackRule.y * tf;
                            half rj = saturate((jn.r - 0.5) * (half)_OzTrackFar.z + 0.5 + (0.5 - hc) * (half)_OzTrackFar.w * saturate(4.0 * jn.r * (1.0 - jn.r)));
                            cov = max(cov, rj * (half)_OzJoinRule.y * tf);                          // a mouth: denser on both sides of the road's edge, its end broken up by the ground's own grain
                            half3 earth = SRGBToLinear(lerp((half3)_OzGravelEarth2.rgb, (half3)_OzGravelEarth.rgb, gx.b));
                            earth = lerp(earth, SRGBToLinear((half3)_OzGravelVerge.rgb * (0.8 + 0.4 * gx.b)), vg);   // the margin has the earth's own patches and grit
                            earth = lerp(earth, SRGBToLinear((half3)_OzJoinEarth.rgb * (0.8 + 0.4 * gx.b)), jrd);   // over the asphalt: the pale dust's own colour (the earth's is set against the grass showing through)
                            half show = saturate((tk.g - gs.a) * 30.0);                             // a pebble shows where the density is above its rank
                            show *= gs.a < 0.05 ? saturate((0.5 - tk.g) * 8.0) * saturate((0.85 - tk.r) * 6.0) * (1.0 - jrd) : (half)1;   // the big stones only at the edge, never on the asphalt
                            c = lerp(c, earth, cov);
                            c *= 1.0 - gx.g * show * (half)_OzTrackRule.w * tf;                     // its contact shadow (light from the top left)
                            half pk = gx.r * show * (half)_OzTrackRule.z * tf;
                            c = lerp(c, gs.rgb, pk);
                            half keep = (1.0 - cov) * (1.0 - pk);                                   // the grass's own tints stay off the path
                            grass *= keep; veg *= keep;
                        }
                    }
                }
                [branch] if (trod.y > 0.002)
                {   // worn (the trodden grass at the camp: soft patches, the trails, the ring round the fire, the steps): the grass a
                    // little paler and browner, its leaves' light and dark evened out toward one drier colour; more in the low parts of
                    // the blend (a leafy edge, no line); only on the grass and the verge (no earth, no mud)
                    half wv = saturate(trod.y + (0.5 - hc) * (half)_OzTrodRule.w * saturate(4.0 * trod.y * (1.0 - trod.y))) * (half)_OzTrodRule.z * (half)saturate(veg);
                    half3 cw = LinearToSRGB(c) * (half3)_OzTrodTint.rgb;
                    cw = lerp(cw, (half3)_OzTrodFlat.rgb, (half)_OzTrodTint.w);
                    c = lerp(c, SRGBToLinear(saturate(cw)), wv);
                }
                lowG = half2((half)saturate(max(sc[1] - (ma - 0.07), 0.0) * binv), hc);   // the verge's (transitional grass's) share of the blend, the blended height
                half edgeD = m2 > -1e8 ? (half)exp(-clamp(ma - m2, 0.0, 50.0) / 0.12) : (half)0;
                // the recipe's tints were worked out on the stored (sRGB) values
                half3 cs = LinearToSRGB(c) * (lt * (half)binv);
                cs *= 1.0 - 0.20 * edgeD * saturate(1.2 - 1.6 * hc);                         // a little darker where two kinds meet and the point is low
                half calmG = (half)saturate(grass * binv) * (half)_CalmAmt.x;                                          // how much of this point is calm grass
                cs *= 1.0 + 0.18 * nm * lerp((half)1, (half)_CalmDrift, calmG); cs.r *= 1.0 + 0.08 * nm * (half)veg; cs.b *= 1.0 - 0.12 * nm * (half)veg;   // slow colour drift (less on calm grass)
                cs *= pow(max(ao, (half)0.001), (half)0.9) * (1.0 - 0.36 * wet);            // baked shade in hollows, wetness above the waterline
                cs *= lerp((half3)1, mood, (half)saturate(grass * binv));                 // grass patches: borders stay as sharp as the layer blend
                return SRGBToLinear(OZJCal(cs));
            }

            half4 frag(V i) : SV_Target
            {
                OZCraterClip(i.positionWS);                                                  // deformable terrain (role 1 only)
                OZJRimClip(i.positionWS);
                float3 P = i.positionWS;
                float2 guv = (P.xz - _GroundRect.xy) * _GroundRect.zw;
                float3 dpx = ddx(P), dpy = ddy(P);
                float ozPxm = max(length(dpx.xz), length(dpy.xz));                           // ground metres per pixel
                float px3 = max(length(dpx), length(dpy));
                half3 paint = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, guv).rgb;
                half far = (half)smoothstep(_FarPx.x, _FarPx.y, px3);
                bool patch = _OzCraterRole > 1.5;
                half3 nrm0 = NormalizeNormalPerPixel(i.normalWS);
                half4 sgr = SAMPLE_TEXTURE2D_LOD(_SplatA, sampler_SplatA, guv, 0);                 // the far picture gets the same patches where it is grass
                half field = SAMPLE_TEXTURE2D_LOD(_LeafyMask, sampler_linear_clamp, guv, 0).r * (half)_CalmAmt.w;         // 1 = inside a grass field
                half fld = field * (half)_FieldRule.x;
                far *= (half)1 - (half)smoothstep(_FieldRule.y, _FieldRule.z, field) * (half)step(0.001, _FieldRule.x);   // the far picture has no fields: the layers at every distance there (no pop)
                half3 mood = OZJMood(P.xz);
                float2 gdx = ddx(guv) * _CalmRule.w, gdy = ddy(guv) * _CalmRule.w;
                [branch] if (_CalmAmt.z > 0.01)                                               // calm grass far away: the far picture read softer inside the grass
                    paint = lerp(paint, SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, guv, gdx, gdy).rgb, (half)_CalmAmt.z * smoothstep(0.9, 1.0, sgr.r));
                [branch] if (_CalmDrift < 0.999)
                {   // the far picture has the full drift baked in: taken down on calm grass like the layers
                    half nmF = SAMPLE_TEXTURE2D_LOD(_GroundNoise, sampler_GroundNoise, float2(P.x, -P.z) / 64.0, 0).r - 0.5;
                    half calmF = smoothstep(0.9, 1.0, sgr.r) * (half)_CalmAmt.x;
                    paint = SRGBToLinear(LinearToSRGB(paint) * (1.0 + 0.18 * nmF * lerp((half)1, (half)_CalmDrift, calmF)) / (1.0 + 0.18 * nmF));
                }
                half3 farTint = half3(1, 1, 1);
                [branch] if (_TintMud.w > 0.5)
                {   // the far picture was baked without the per-kind calibration: laid on by its weights and by the slope
                    half4 wB = SAMPLE_TEXTURE2D_LOD(_SplatB, sampler_SplatA, guv, 0); half wWet = wB.r, wPav = SAMPLE_TEXTURE2D_LOD(_SplatC, sampler_SplatA, guv, 0).r;
                    half sideF = patch ? (half)0 : (half)OZJSm((degrees(acos(saturate(nrm0.y))) - 40.0) / 10.0);
                    farTint = lerp(lerp(lerp(lerp((half3)1, (half3)_TintMud.rgb, saturate(sgr.b + wB.b)), (half3)_TintWet.rgb, wWet), (half3)_TintPaving.rgb, wPav), (half3)_TintSide.rgb, sideF);
                }
                half3 paintM = SRGBToLinear(OZJCal(LinearToSRGB(paint) * farTint * lerp((half3)1, mood, saturate(sgr.r + 0.5 * sgr.g))));
                half3 albedo = paintM;
                half2 lowG = half2(saturate((sgr.g + sgr.r * fld) / max(sgr.r + sgr.g + sgr.b + sgr.a, (half)0.001)), 0.5);   // far: the verge's weight (with the fields' share), a middle height
                [branch] if (far < 0.995)
                {
                    half2 lowL;
                    albedo = lerp(OZJLayers(P, dpx, dpy, nrm0, guv, paint, patch, mood, fld, lowL), paintM, far);
                    lowG = lerp(lowL, lowG, far);
                }
                albedo *= _BaseColor.rgb;
                if (_StudRule.w > 0.001) albedo = OZJStuds(P, px3, albedo);
                if (patch)
                {   // the crater patch: bare earth in the bowl, then the crater decal (UV1): no second draw, cannot z-fight
                    half4 dc = SAMPLE_TEXTURE2D(_OzCraterDecal, sampler_OzCraterDecal, i.uv1);
                    float invT = 1.0 / _LayerTile;
                    half3 mud = SAMPLE_TEXTURE2D_ARRAY_GRAD(_Layers, sampler_Layers, P.xz * invT, 2, dpx.xz * invT, dpy.xz * invT).rgb;
                    albedo = lerp(albedo, mud * (half)0.85, dc.a * _CraterMud);
                    albedo = _OzCraterDecalMul > 0.5 ? albedo * lerp((half3)1, dc.rgb * 2, dc.a) : lerp(albedo, dc.rgb, dc.a);
                }
                half ozWetP = 0;                                                             // rain: damp ground + puddles (OpenZWet.hlsl)
                if (_OzWetness > 0.001) albedo = OZJWetAlbedo(P.xz, albedo, nrm0.y, sgr, SAMPLE_TEXTURE2D_LOD(_SplatB, sampler_SplatA, guv, 0), ozWetP);
                [branch] if (_OzLowPuddle.x > 0.001)                                         // rain puddles on the low grass and the gravel paths (OpenZJungleWet.hlsl; 0 = none: every other level, dry)
                {
                    float2 luv = float2(P.x / (2.0 * _MapTiles.x), 1.0 + P.z / (2.0 * _MapTiles.y)), lsc = float2(0.5 / _MapTiles.x, 0.5 / _MapTiles.y);
                    half3 lowM = SAMPLE_TEXTURE2D_GRAD(_OzLowPuddleMask, sampler_linear_clamp, luv, dpx.xz * lsc, dpy.xz * lsc).rgb;   // R = depth of a hollow, B = a gravel path's puddle
                    half onPath = lowM.b * (half)(_OzTrackRule.x * saturate((_OzTrackFar.y - px3) / max(_OzTrackFar.y - _OzTrackFar.x, 1e-4)));   // a path's puddles: as far as its gravel is drawn
                    half3 flatW = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, guv, 4.5).rgb;     // the water's colour: the far picture read about 1.5 m soft, calibrated as paintM
                    flatW = SRGBToLinear(OZJCal(LinearToSRGB(flatW) * farTint * lerp((half3)1, mood, saturate(sgr.r + 0.5 * sgr.g)))) * _BaseColor.rgb;
                    flatW = lerp(flatW, SRGBToLinear(OZJCal((half3)_OzGravelEarth2.rgb)) * _BaseColor.rgb, onPath * (half)_OzTrackRule.y);   // on a path: its darker earth (the far picture has no paths)
                    albedo = OZJLowPuddleAlbedo(albedo, flatW, lowM.r * (1.0 - lowM.b + onPath), lowG, nrm0.y, ozWetP);
                }

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.alpha = 1; s.metallic = 0; s.smoothness = _Smoothness; s.occlusion = 1; s.normalTS = half3(0, 0, 1);
                InputData inputData = (InputData)0;
                inputData.positionWS = P; inputData.positionCS = i.positionCS;
                inputData.normalWS = nrm0;
                inputData.viewDirectionWS = GetWorldSpaceNormalizeViewDir(P);
                inputData.shadowCoord = TransformWorldToShadowCoord(P);
                inputData.fogCoord = 0; inputData.bakedGI = SampleSH(inputData.normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                inputData.shadowMask = half4(1, 1, 1, 1);
                half4 c = UniversalFragmentPBR(inputData, s);
                c.rgb = OZNightEnvFix(c.rgb, inputData, s, 0);                          // ground = environment class
                if (_OzWetness > 0.001 || _OzLowPuddle.x > 0.001)                           // wet sheen, puddle reflections, drop ripples (the low grass's and the paths' puddles outlast the damp)
                    c.rgb += OZWetSheen(P, (half3)inputData.normalWS, (half3)inputData.viewDirectionWS, inputData.normalizedScreenSpaceUV, inputData.shadowCoord, ozWetP, ozPxm);
                c.rgb = MixFog(c.rgb, ComputeFogFactor(TransformWorldToHClip(P).z));         // fog factor per pixel
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        // shadow caster: switched on per material (the cliff material casts, the flat ground does not: it only receives)
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i);
                float3 positionWS = TransformObjectToWorld(i.positionOS.xyz); float3 normalWS = TransformObjectToWorldNormal(i.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS; return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

        // the ground's own depth passes with the crater clip, so the SSAO prepass sees the bowl, not the cut-away flat ground
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; };
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }
            half frag(Varyings i) : SV_Target { OZCraterClip(i.positionWS); OZJRimClip(i.positionWS); return i.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); o.positionWS = TransformObjectToWorld(i.positionOS.xyz); o.positionCS = TransformWorldToHClip(o.positionWS); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { OZCraterClip(i.positionWS); OZJRimClip(i.positionWS); return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
