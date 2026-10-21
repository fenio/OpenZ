// OpenZ/TeamLit: URP PBR lit shader for every OpenZ model.
// Albedo = _BaseMap * _BaseColor (* vertex colour when _UseVertexColor = 1, M12 palette props / legacy vehicles).
// Team paint (Z colours: red player, blue CPU, grey neutral) replaces the model's "army" paint:
//   _TeamMode 1 = hue band (olive paint 0.12-0.34, heavy tank teal 0.42-0.64), brightness kept
//   _TeamMode 2 = vertex-colour alpha mask (robot skin armour plates)
// _TeamColor is set per object with a MaterialPropertyBlock (OpenZ.TeamPaint), so one material serves all teams.
// a light livery tint (owned) or flat grey paint + dark accents (neutral = empty), and a team rim (day: accent objects, night:
// robots too). Per renderer (TeamPaint, SetVector = linear): _OZTeamVivid (w: 0 unset, 1 neutral, 2 owned), _OZTeamGlow,
// _OZPanel / _OZPanelY. Globals (TeamReadability): _OZTeamLook, _OZTeamRim. All 0 = the v1 look, unchanged.
Shader "OpenZ/TeamLit"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        [Toggle] _UseVertexColor ("Use Vertex Colour", Float) = 0
        [Toggle] _VertexColorSRGB ("Vertex Colours are sRGB (Blender FBX)", Float) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.35
        _Metallic ("Metallic", Range(0,1)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        // fort_v2 optional maps (all off by default: every existing TeamLit material renders exactly as before)
        [Toggle] _UseNormalMap ("fort_v2: use Normal Map", Float) = 0
        [Normal][NoScaleOffset] _BumpMap ("fort_v2: Normal Map (tangent, OpenGL)", 2D) = "bump" {}
        _BumpScale ("fort_v2: Normal strength", Range(0,2)) = 1
        [Toggle] _UseMaskMap ("fort_v2: use Mask Map (R metallic, A smoothness)", Float) = 0
        [NoScaleOffset] _MaskMap ("fort_v2: Mask Map", 2D) = "white" {}
        [NoScaleOffset] _EmissionMap ("fort_v2: Emission Map", 2D) = "black" {}
        [HDR] _EmissionMapColor ("fort_v2: Emission Map tint (black = off)", Color) = (0,0,0,1)
        _EmissionNight ("fort_v2: Emission Map 0 = always .. 1 = night only (_OZNight)", Range(0,1)) = 1
        [Enum(None,0,HueBand,1,VertexAlphaMask,2,TextureAlphaMask,3)] _TeamMode ("Team Mode", Float) = 1
        _TeamColor ("Team Colour (linear)", Color) = (0.171,0.171,0.171,1)
        _HueMin ("Team Hue Min", Range(0,1)) = 0.12
        _HueMax ("Team Hue Max", Range(0,1)) = 0.34
        _SatMin ("Team Saturation Min", Range(0,1)) = 0.32
        _TeamRef ("Team Brightness Reference (linear V)", Range(0.05,1)) = 0.30
        _ValMax ("Team Paint Max Value (cream walls are brighter)", Range(0,1)) = 0.6
        _ShadeMin ("Team Paint Min Shade", Range(0,2)) = 0.85
        _ShadeMax ("Team Paint Max Shade (cap: bright armour stays saturated)", Range(0.5,2)) = 1.6
        _Degreen ("Leftover army green -> grey", Range(0,1)) = 1
        _MaskDilate ("Team mask dilation (texels, 0 = off)", Range(0,3)) = 0
        _MaskArea ("Team mask area test (extra mips, 0 = off)", Range(0,4)) = 2
        _CreamToTeam ("Robot skin: light armour plates take the team colour", Range(0,1)) = 0
        [Toggle] _Accent ("Accent mode: paint is neutral, team only on accents", Float) = 0
        _NeutralPaint ("Neutral paint (sRGB)", Color) = (0.70, 0.68, 0.62, 1)
        _AccentFrom ("Accent band from (fraction of mesh height)", Range(0,2)) = 2
        _AccentTo ("Accent band to (fraction of mesh height)", Range(0,2)) = 2
        _TeamTone ("Team colour toned toward grey", Range(0,1)) = 0.15
        [Toggle] _BandWorld ("Accent band in world Y (skinned robots, set per frame)", Float) = 0
        _OwnerPart ("Owner part (per renderer)", Float) = 0
        _AccentLo ("Accent band low (object Y, per renderer)", Float) = 100000
        _AccentHi ("Accent band high (object Y, per renderer)", Float) = -100000
        _MaskSatBoost ("Team mask extra min saturation", Range(0,0.3)) = 0.06
        _HueShift ("Hue shift of non-team colours", Range(-0.5,0.5)) = 0
        _HueMin2 ("Second Team Hue Min (off when > max)", Range(0,1)) = 1
        _HueMax2 ("Second Team Hue Max", Range(0,1)) = 0
        _SatMin2 ("Second Team Saturation Min", Range(0,1)) = 0.3
        _TeamLift ("Team colour lift toward white (readability)", Range(0,1)) = 0
        [Toggle] _Triplanar ("World triplanar (cliffs)", Float) = 0
        [Toggle] _TriVertical ("Triplanar strata run vertically (Z sandstone striations)", Float) = 0
        _TopBlend ("Plateau tops: clean top colour on up-facing faces", Range(0,1)) = 0
        _TopColor ("Plateau top colour (sRGB)", Color) = (0.67, 0.52, 0.39, 1)
        _TopMin ("Plateau blend start (normal.y)", Range(0,1)) = 0.62
        _TopMax ("Plateau blend end (normal.y)", Range(0,1)) = 0.8
        _TriScale ("Triplanar horizontal scale (1/m)", Float) = 0.045
        _TriHeight ("Triplanar strata height (m per texture)", Float) = 18
        _SatScale ("Non-team Saturation Scale", Range(0,2)) = 1
        _ValScale ("Non-team Value Scale", Range(0,2)) = 1
        [Header(Stylized units (clean bright low poly look))]
        [Toggle] _Stylize ("Stylize (units)", Float) = 0
        _Degrime ("De-grime (blend to a blurred mip)", Range(0,1)) = 0.5
        _DegrimeLod ("De-grime mip level", Range(0,6)) = 2.5
        _DarkColor ("Dark lift colour (linear, added to blacks)", Color) = (0.05, 0.056, 0.07, 1)
        _DarkThreshold ("Dark lift threshold (linear luminance)", Range(0.01,0.5)) = 0.12
        _SoftFill ("Soft wrap fill", Range(0,1.5)) = 0.7
        _AmbientBoost ("Ambient boost", Range(1,3)) = 1.35
        _KeyWarm ("Warm key tint", Color) = (1.08, 1.0, 0.9, 1)
        [HDR] _RimColor ("Rim light", Color) = (0.55, 0.5, 0.42, 1)
        _RimPower ("Rim power", Range(1,8)) = 3.2
        [Header(teamcolor_v2 per renderer from TeamPaint (leave 0 on materials))]
        _OZTeamVivid ("Vivid team albedo (linear) + w: 0 unset, 1 neutral, 2 owned", Vector) = (0, 0, 0, 0)
        _OZTeamGlow ("Team rim / night glow (linear)", Vector) = (0, 0, 0, 0)
        _OZDead ("Dead robot: 0 alive .. 1 dead (per renderer from TeamPaint / OzDeadTint)", Range(0, 1)) = 0
        _OZPanel ("Team panel rect, mesh space (cx, cz, half x, half z)", Vector) = (0, 0, 0, 0)
        _OZPanelY ("Team panel: from mesh y, edge soft, corner, strength", Vector) = (0, 0, 0, 0)
        _OZFortPaint ("TG2 fort palette on tower guns: cream (linear) + w: reference luminance, 0 = off", Vector) = (0, 0, 0, 0)
        [Header(Misc)]
        [Toggle] _FlatShade ("Flat low-poly facets (face normals)", Float) = 0
        _NightClass ("Night class (night_v2): -1 auto, 0 environment, 1 unit, 2 building, 3 map item", Float) = -1
        [Header(unitlook_v2 clean unit shading set by OpenZ Look Unit look v2)]
        [Toggle] _UnitLook ("Unit look: clean unit shading (OpenZUnitLook.hlsl)", Float) = 0
        [Toggle] _PaintFromAlpha ("Unit look: paint mask = texture alpha (the *_clean.png albedos)", Float) = 0
        _UnitRamp ("Unit ramp: from N.L, to N.L, Lambert share, key side shift (keep 0)", Vector) = (-0.05, 0.85, 0.8, 0)
        _UnitShade ("Unit shade: ambient, warm bounce in shade, highlight cap, matte (1 = no sheen)", Vector) = (1.0, 0.15, 0.1, 0)
        _UnitKick ("Unit kicker: from, to, day strength, night widen", Vector) = (0.74, 0.93, 0.8, 0.14)
        [Toggle] _SeeThru ("See-through cutout around units behind it (rock pillars, RockSeeThru)", Float) = 0
        [HideInInspector] _OZCamFade ("K440: share of the object cut away near the game camera (0 = drawn .. 1 = gone; per renderer, OzShuttleCamFade)", Range(0, 1)) = 0
        [Header(HT heavytank legacy look)]
        [Toggle] _LegacyHeavy ("Heavy tank: legacy look (gunmetal + khaki + team accents, black-grey empty)", Float) = 0
        _HeavyLook ("Heavy look: khaki ref lum, empty panel grey, empty metal gain, team strength", Vector) = (0.1575, 0.42, 0.8, 1)
        _Rust ("Rust overlay amount, 0 off (L17 wrecks)", Range(0,1)) = 0
        _RustWaterY ("Rust: waterline world y (grime)", Float) = -1000
        _RustScale ("Rust: pattern scale", Float) = 1
        [HideInInspector] _OzWindPalm ("palm_wind: crown pivot (trunk top, mesh space), w 1 = moves in the wind (PalmWind run-time copy)", Vector) = (0, 0, 0, 0)
        [HideInInspector] _OzWindPalm2 ("palm_wind: x = crown radius (mesh units)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        LOD 300

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            float4 _BaseMap_TexelSize;
            half _MaskDilate; half _MaskArea; half _MaskSatBoost; half _CreamToTeam;
            half _Accent; half4 _NeutralPaint; half _AccentFrom; half _AccentTo; half _TeamTone; half _BandWorld; half _OwnerPart; float _AccentLo; float _AccentHi;
            half _HueShift;
            half4 _BaseColor;
            half _UseVertexColor;
            half _VertexColorSRGB;
            half _Smoothness;
            half _Metallic;
            half4 _EmissionColor;
            half _TeamMode;
            half4 _TeamColor;
            half _HueMin;
            half _HueMax;
            half _SatMin;
            half _TeamRef;
            half _ValMax;
            half _ShadeMin;
            half _ShadeMax;
            half _Degreen;
            half _HueMin2;
            half _HueMax2;
            half _SatMin2;
            half _TeamLift;
            half _Triplanar; half _TriVertical; half _TopBlend; half4 _TopColor; half _TopMin; half _TopMax;
            float _TriScale;
            float _TriHeight;
            half _SatScale;
            half _ValScale;
            half _Stylize;
            half _Degrime;
            half _DegrimeLod;
            half4 _DarkColor;
            half _DarkThreshold;
            half _SoftFill;
            half _AmbientBoost;
            half4 _KeyWarm;
            half4 _RimColor;
            half _RimPower;
            float4 _OZTeamVivid;   // teamcolor_v2
            float4 _OZTeamGlow;
            half _OZDead;   // item 228: a dead robot's figure greyer, luminance kept (OzDeadTint)
            float4 _OZPanel;
            float4 _OZPanelY;
            float4 _OZFortPaint;   // TG2 (TeamPaint.FortPalette)
            half _FlatShade;
            half _NightClass;   // night_v2
            half _UnitLook; half _PaintFromAlpha; half4 _UnitRamp; half4 _UnitShade; half4 _UnitKick;   // unitlook_v2
            half _SeeThru;
            half _OZCamFade;   // K440: the cut near the game camera (OzShuttleCamFade, per renderer; 0 = drawn as always)
            half _LegacyHeavy; half4 _HeavyLook;
            half _UseNormalMap; half _BumpScale; half _UseMaskMap; half4 _EmissionMapColor; half _EmissionNight;   // fort_v2 optional maps
            half _Rust; float _RustWaterY; float _RustScale;
            float4 _OzWindPalm; float4 _OzWindPalm2;   // palm_wind (PalmWind.cs run-time palm copies only; w 0 = still)
        CBUFFER_END
        // night package (NightGrade): 0 by day .. 1 at night, and the cool moonlit rim colour (linear), globals
        half _OZNight;
        half4 _OZNightRim;
        half4 _OZNightFill;
        // rocks_v2 (VX18): see-through cutout. RockSeeThru.cs fills _OZSeeThru with up to 16 units hidden behind a rock pillar
        // (xyz = world centre, w = hole radius in m, animated 0 -> radius). Only materials with _SeeThru = 1 (the RockZ rocks)
        // react: a rock pixel between the camera and such a unit is dithered away, in the colour and the depth passes alike.
        float4 _OZSeeThru[16];
        float _OZSeeThruCount;
        // teamcolor_v2 globals (TeamReadability; 0 until set = the v1 look): _OZTeamLook = (accent vivid, livery tint, team
        // panels, neutral grey), _OZTeamRim = (day team rim mix, night team rim mix, team rim gain, night panel lift)
        half4 _OZTeamLook;
        // _OZToyKhaki = (target khaki, linear rgb; w = mix 0..1), _OZToyKhakiRef = linear luminance of the toy albedos' usual paint
        half4 _OZToyKhaki; half _OZToyKhakiRef;
        // x = highlight cap gain, y = paint smoothness add, z = bare-metal smoothness add, w = soft environment reflection (by day)
        half4 _OZSheen;
        half4 _OZTeamRim;
        float4 _OZSeeThruParams;   // x = rock coverage kept in the hole, y = soft rim (fraction of the radius), z = front margin (m), w = rim darken
        half OZSeeThruCut(float3 positionWS)
        {
            if (_SeeThru < 0.5 || _OZSeeThruCount < 0.5) return 0;
            float3 cam = _WorldSpaceCameraPos.xyz;
            float3 d = positionWS - cam;
            float dl = length(d);
            float3 dir = d / max(dl, 1e-4);
            half cut = 0;
            int n = (int)min(_OZSeeThruCount, 16.0);
            [loop] for (int k = 0; k < n; k++)
            {
                float4 u = _OZSeeThru[k];
                float3 cu = u.xyz - cam;
                float t = dot(cu, dir);                                   // the unit's distance along this pixel's view ray
                if (u.w > 0.001 && dl < t - _OZSeeThruParams.z)            // this rock pixel lies in front of the unit
                {
                    float r = length(cu - dir * t);                       // unit centre -> view ray
                    float soft = max(_OZSeeThruParams.y, 0.05);
                    cut = max(cut, (half)(1.0 - smoothstep(u.w * (1.0 - soft), u.w, r)));
                }
            }
            return cut;
        }
        // ordered 4x4 Bayer dither on the pixel grid (same pattern in every pass, so depth and colour agree); returns the cut
        half OZSeeThruClip(float3 positionWS, float4 positionCS)
        {
            half cut = OZSeeThruCut(positionWS);
            if (cut > 0.001)
            {
                uint2 p = (uint2)positionCS.xy;
                uint x0 = p.x & 1u, y0 = p.y & 1u, x1 = (p.x >> 1) & 1u, y1 = (p.y >> 1) & 1u;
                uint b = ((((x0 ^ y0) << 1) | y0) << 2) | (((x1 ^ y1) << 1) | y1);
                half keep = 1.0 - cut * (1.0 - _OZSeeThruParams.x);
                clip(keep - ((half)b + 0.5) / 16.0);
            }
            return cut;
        }
        // K440 (item 441): the game camera never sees into a shuttle. OzShuttleCamFade sets _OZCamFade per renderer as the ship comes near
        // the eye (0 = drawn .. 1 = gone; every other object keeps 0 and leaves at the first test). The see-through cut's own 4x4 Bayer
        // pattern on the pixel grid with one threshold for the whole object: at a pixel either every surface of it is drawn or none is, so
        // where its hull is cut its inside is cut too. Colour, depth and depth-normals alike (they agree); never its shadow.
        void OZCamFadeClip(float4 positionCS)
        {
            if (_OZCamFade > 0.001)
            {
                uint2 p = (uint2)positionCS.xy;
                uint x0 = p.x & 1u, y0 = p.y & 1u, x1 = (p.x >> 1) & 1u, y1 = (p.y >> 1) & 1u;
                uint b = ((((x0 ^ y0) << 1) | y0) << 2) | (((x1 ^ y1) << 1) | y1);
                clip((1.0 - _OZCamFade) - ((half)b + 0.5) / 16.0);
            }
        }
        // palms_v3 BODY_ mesh moves in the level wind (the flags' and banners' direction, strength and gusts). Only PalmWind's
        // run-time copies of the palm materials have _OzWindPalm.w = 1; every other TeamLit material keeps 0 and leaves at the
        // first (uniform) branch. Every pass calls it on the same world position, so colour, shadow, depth and depth-normals
        // agree. It reads the position only (no normal / uv / colour): split vertices move together, the mesh stays closed.
        float4 _OZPalmWind;    // global (PalmWind): downwind x, downwind z, strength (flags: desert 1, city 0.4; 0 = still), gust 0-1
        float4 _OZPalmWindK;   // global (PalmWind): crown sway (rad), trunk bend (rad), frond tip flutter (m), 0
        float OZPalmHash(float2 p)
        {
            float3 q = frac(p.xyx * 0.1031);
            q += dot(q, q.yzx + 33.33);
            return frac((q.x + q.y) * q.z);
        }
        #include "OpenZFloraWind.hlsl"                                                                  // level 16: trees and plants in the wind and the rain (OzFloraWind.cs)
        float3 OZPalmWind(float3 pw)
        {
            if (_OzWindPalm.w < -1.5) pw = OZFloraWind(pw);                                              // a level 16 plant (run-time material copy, kind stored negative); the next line lets it go, the palms below are untouched
            if (_OzWindPalm.w < 0.5 || _OZPalmWind.z <= 0.0) return pw;
            float3 foot = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);         // BODY_'s origin = the palm's foot
            float3 top = mul(UNITY_MATRIX_M, float4(_OzWindPalm.xyz, 1.0)).xyz;                         // the trunk top = the crown pivot
            float sc = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));   // the placement scale
            // this palm's own phase, tempo and amplitude (from its foot: deterministic, the same every run)
            float h1 = OZPalmHash(foot.xz), h2 = OZPalmHash(foot.zx + 17.3);
            float ph = h1 * 6.2831853, fq = 0.88 + 0.24 * h2, amp = (0.8 + 0.4 * h2) * _OZPalmWind.z;
            float g = _OZPalmWind.w, t = _Time.y;
            float3 W = float3(_OZPalmWind.x, 0.0, _OZPalmWind.y), C = float3(-W.z, 0.0, W.x), U = float3(0.0, 1.0, 0.0);
            float3 r = pw - top;
            float dh = length(r.xz), dl = length(r);
            // the crown: everything above the top's last ~0.35 m, and everything off the trunk axis near it (fronds hanging
            // below the top, coconuts); the trunk below stays out (0)
            float wC = saturate(smoothstep(-0.35 * sc, 0.05 * sc, r.y) + smoothstep(0.5 * sc, 0.9 * sc, dh) * smoothstep(-1.15 * sc, -0.85 * sc, r.y));
            // 1. crown sway about the trunk top (small rotation: d = a x r): a downwind lean on the gust envelope + layered sines
            //    (~1.5-3 deg at full desert wind), a smaller cross sway
            float env = (0.55 + 0.45 * g) * amp * _OZPalmWindK.x;
            float aD = env * (0.35 + 0.65 * (0.6 * sin(2.64 * fq * t + ph) + 0.4 * sin(6.1 * fq * t + 2.3 * ph)));
            float aC = env * 0.45 * sin(1.95 * fq * t + 4.1 * ph);
            float3 d = wC * (aD * (W * r.y - U * dot(W, r)) + aC * (C * r.y - U * dot(C, r)));
            // 2. the trunk bends a little about the foot (~0.5 deg at the top, growing with height^2; the crown rides on the top)
            float H = max(top.y - foot.y, 0.5 * sc);
            float f = saturate((pw.y - foot.y) / H);
            f = lerp(f * f, 1.0, wC);
            float bend = _OZPalmWindK.y * amp * (0.4 + 0.6 * g) * (0.65 + 0.35 * sin(1.31 * fq * t + 1.7 * ph));
            float3 rb = pw - foot;
            d += bend * f * (W * rb.y - U * dot(W, rb));
            // 3. frond flutter: grows to the tips, each frond on its own phase (its direction from the pivot), a slow wave running
            //    out to the tip; up / down, a sideways flick round the pivot (what the RTS camera above sees) and a push downwind
            float wF = smoothstep(0.7 * sc, max(_OzWindPalm2.x, 1.0) * sc, dl) * wC; wF *= wF;
            float3 rd = r / max(dl, 1e-3);
            float lp = 1.0 * rd.x + 0.8 * rd.z - 0.6 * dl / sc;
            float u1 = sin(6.6 * fq * t + 1.5 * lp + ph), u2 = sin(11.3 * fq * t + 2.6 * lp + 2.0 * ph), s1 = sin(4.7 * fq * t + 1.3 * lp + 1.3 * ph);
            float fa = _OZPalmWindK.z * amp * (0.45 + 0.55 * g) * wF;            // metres at the tip, any palm size
            // every direction is across the frond (a small turn about the pivot, not a stretch along it): up, round, downwind
            float3 el = U - rd * rd.y, side = float3(-r.z, 0.0, r.x) / max(dh, 0.5 * sc), wp = W - rd * dot(W, rd);
            d += fa * (el * (0.75 * u1 + 0.25 * u2) + side * (0.55 * s1) + wp * (0.25 + 0.2 * u1));
            return pw + d;
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
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "OpenZNightV2.hlsl"                                            // night_v2: night classes, day-sky reflection fix
            #include "OpenZWet.hlsl"                                                // weather_l17_v1: rain wetness (globals; 0 = dry = unchanged)
            #include "OpenZRust.hlsl"

            // the sun's normal-offset shadow bias moves from the CASTER to the RECEIVER. URP's ApplyShadowBias pushed every caster
            // vertex inward along its OWN normal; the cliffs, rocks, radar and every flat-shaded low-poly model carry split normals
            // (71-100 % of welded vertices sit on a hard edge), so the inset tore each mesh apart along every facet edge in the
            // shadow map: sub-texel slits that re-rasterise whenever the sun or the radar dish turns = flickering light holes.
            // The ShadowCaster pass keeps only the depth bias; here the lookup point moves out along the receiver's geometric
            // normal by exactly what URP used to inset: normal bias x URP kernel x cascade texel x (1 - N.L). Same acne protection,
            // nothing left to crack. _OZShadowRecvBias = asset normal bias x URP kernel (ShadowStability sets it; 0 -> 0.3 x 2.5).
            float _OZShadowRecvBias;
            float3 OZShadowRecvPos(float3 positionWS, float3 nGeoWS)
            {
            #if defined(_MAIN_LIGHT_SHADOWS) || defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                #if defined(_MAIN_LIGHT_SHADOWS_CASCADE)
                    half ci = ComputeCascadeIndex(positionWS);
                #else
                    half ci = half(0.0);
                #endif
                float uvPerMetre = length(_MainLightWorldToShadow[ci][0].xyz);                  // atlas uv per metre (0 past the last cascade)
                float texel = uvPerMetre > 1e-8 ? _MainLightShadowmapSize.x / uvPerMetre : 0.0; // world size of one shadow texel
                float k = _OZShadowRecvBias > 0.0 ? _OZShadowRecvBias : 0.75;
                float invNdotL = 1.0 - saturate(dot(nGeoWS, _MainLightPosition.xyz));
                return positionWS + nGeoWS * (invNdotL * k * texel);
            #else
                return positionWS;
            #endif
            }

            // night_v2: the material's night class. _NightClass >= 0 is explicit (buildings 2, crates / huts 3, rocks /
            // cliffs 0 are set in their .mat files); -1 = auto: a unit when stylized / accent with a unit rim and
            // neither triplanar nor see-through (the unit, vehicle and cannon materials), else environment.
            // unit rims (sRGB 0.55-0.62) are 0.22-0.28 linear luminance, the rocks / cliffs v3 rim (sRGB 0.12) 0.011. The
            // first threshold (0.3, an sRGB value) made EVERY unit "environment" (no night rim / fill). 0.05 separates both.
            half OZNightClass()
            {
                if (_NightClass > -0.5) return _NightClass;
                half rimLum = dot(_RimColor.rgb, half3(0.2126, 0.7152, 0.0722));
                return step(0.5, max(_Stylize, _Accent)) * step(0.05, rimLum) * step(_Triplanar, 0.5) * step(_SeeThru, 0.5);
            }

            #include "OzShuttleSelfShadow.hlsl"                                     // K256g2: the flying shuttle's own self-shadow (_OzSelfShadowOn 0 = unchanged)
            #include "OpenZUnitLook.hlsl"                                           // unitlook_v2: clean unit shading

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap); TEXTURE2D(_MaskMap); SAMPLER(sampler_MaskMap);            // fort_v2
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; half4 color : TEXCOORD3; half fogFactor : TEXCOORD4; float objY : TEXCOORD5;
                float2 objXZ : TEXCOORD6;   // teamcolor_v2: mesh-space xz (team panels)
                float4 tangentWS : TEXCOORD7;   // fort_v2: xyz tangent, w bitangent sign (normal map)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes i)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o);
                VertexPositionInputs p = GetVertexPositionInputs(i.positionOS.xyz);
                p.positionWS = OZPalmWind(p.positionWS); p.positionCS = TransformWorldToHClip(p.positionWS);   // palm_wind (palms only)
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.color = i.color; o.objY = i.positionOS.y;
                o.objXZ = i.positionOS.xz;   // teamcolor_v2
                o.tangentWS = float4(TransformObjectToWorldDir(i.tangentOS.xyz, false), i.tangentOS.w * GetOddNegativeScale());   // fort_v2
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half3 RgbToHsv(half3 c)
            {
                half4 K = half4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
                half4 p = lerp(half4(c.bg, K.wz), half4(c.gb, K.xy), step(c.b, c.g));
                half4 q = lerp(half4(p.xyw, c.r), half4(c.r, p.yzx), step(p.x, c.r));
                half d = q.x - min(q.w, q.y);
                half e = 1.0e-4;
                return half3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
            }
            half3 HsvToRgb(half3 c)
            {
                half3 p = abs(frac(c.xxx + half3(1.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
                return c.z * lerp(1.0.xxx, saturate(p - 1.0), c.y);
            }

            half Band(half h, half lo, half hi) { return smoothstep(lo - 0.015, lo + 0.015, h) * (1 - smoothstep(hi - 0.015, hi + 0.015, h)); }

            // hue-band ("olive paint") mask of one linear albedo sample
            half OliveMask(half3 albedo)
            {
                half3 hsv = RgbToHsv(albedo);
                half band = Band(hsv.x, _HueMin, _HueMax);
                half band2 = _HueMax2 > _HueMin2 ? Band(hsv.x, _HueMin2, _HueMax2) : 0;
                return max(band * smoothstep(_SatMin - 0.06, _SatMin + 0.04, hsv.y), band2 * smoothstep(_SatMin2 - 0.06, _SatMin2 + 0.04, hsv.y))
                       * step(0.01, hsv.z) * (1 - smoothstep(_ValMax - 0.05, _ValMax + 0.05, hsv.z));
            }

            // teamcolor_v2: team panels (TeamPaint.PanelRules). gOZObjXZ = the fragment's mesh-space xz, set in frag before
            // ApplyTeam; the rule rect is a rounded rect in mesh xz (_OZPanel: centre, half size; _OZPanelY.z corner) above the
            // mesh height _OZPanelY.x, soft edge _OZPanelY.y. _OZPanelY.w > 0: a surface panel on up-facing metal (vehicle and
            // cannon turret tops, frag); < 0: paint mode, the model's own paint panels inside the rect (building roofs, ApplyTeam)
            static float2 gOZObjXZ;
            static half gOZPaintPanel;   // the paint-mode panel weight ApplyTeam found (frag: faint night glow like the surface panels)
            half OZPanelRect(float2 xz)
            {
                float2 phs = max(_OZPanel.zw, 1e-4);
                float pcr = min(phs.x, phs.y) * _OZPanelY.z;
                float2 pdd = abs(xz - _OZPanel.xy) - (phs - pcr);
                float psd = length(max(pdd, 0.0)) + min(max(pdd.x, pdd.y), 0.0) - pcr;
                float psw = max(_OZPanelY.y, 1e-4);
                return (half)(1.0 - smoothstep(-psw, psw, psd));
            }
            half OZPanelAbove(float objY)
            {
                float psw = max(_OZPanelY.y, 1e-4);
                return (half)smoothstep(_OZPanelY.x - psw, _OZPanelY.x + psw, objY);
            }

            half3 ApplyTeam(half3 albedo, half3 maskAlbedo, half areaMask, half4 vcol, half texA, half dilated, float objY, out half maskOut)
            {
                // hue/saturation test on LINEAR albedo, like the Blender OPENZ_TeamRecolor group; soft thresholds so mask
                // edges blend instead of leaving cream pinstripes (critic #15)
                half3 hsv = RgbToHsv(albedo);
                // grimy texels can't flip in and out of the team colour; value/shading still come from the real albedo
                // when its blurred mip is clearly saturated paint AND a much blurrier mip (area test) still is: thin stains,
                // drips and dirt wash out to cream at that size, panels don't
                half3 mh = RgbToHsv(maskAlbedo);
                half softMask = OliveMask(maskAlbedo) * smoothstep(_SatMin + _MaskSatBoost - 0.05, _SatMin + _MaskSatBoost + 0.03, mh.y) * areaMask;
                // unitlook_v2: the *_clean.png albedos carry the paint mask in alpha (decided once per flat region offline, on
                // the full-res texture): no hue test on blurred mips (DIAGNOSIS #4: 8-28 % of texels on the wrong side)
                if (_UnitLook > 0.5 && _PaintFromAlpha > 0.5) softMask = smoothstep(0.3, 0.7, texA);
                // toypaint_v1: toy albedos (_PaintFromAlpha 2): alpha 0 = not paint, 0.5 = khaki paint, 1 = team panel (paint too)
                if (_UnitLook > 0.5 && _PaintFromAlpha > 1.5) softMask = smoothstep(0.12, 0.38, texA);
                half band = Band(hsv.x, _HueMin, _HueMax);
                half band2 = _HueMax2 > _HueMin2 ? Band(hsv.x, _HueMin2, _HueMax2) : 0;
                half mask = 0;
                if (_TeamMode > 0.5 && _TeamMode < 1.5)            // olive paint (+ optional second band, e.g. heavy tank orange)
                    mask = softMask;
                else if (_TeamMode > 1.5 && _TeamMode < 2.5)       // robot skin armour: vertex alpha, plus olive paint the alpha missed
                    mask = max(max(smoothstep(0.3, 0.7, vcol.a), softMask),
                               _CreamToTeam * smoothstep(0.1, 0.2, dot(maskAlbedo, half3(0.2126, 0.7152, 0.0722))));   // cream plates (blurred), not the dark joints
                else if (_TeamMode > 2.5)                          // texture alpha owner mask
                    mask = step(0.5, texA);
                if (_UnitLook > 0.5 && _TeamMode > 2.5) mask = smoothstep(0.3, 0.7, texA);   // unitlook_v2: mip-soft plates, no red / grey speckle
                // no green team: leftover low-saturation army green / bright lime becomes neutral grey metal
                half degreen = saturate(band * step(0.05, hsv.y) * (_TeamMode > 0.5 ? 1 - mask : 1 - step(_SatMin, hsv.y))   // team models: olive that is not paint (stains, rims)
                                      + band * step(0.18, hsv.x) * step(_ValMax, hsv.z)) * _Degreen;
                half3 other = HsvToRgb(half3(frac(hsv.x + _HueShift), saturate(hsv.y * _SatScale * (1 - degreen)), hsv.z * _ValScale));
                half shade = clamp(RgbToHsv(maskAlbedo).z / _TeamRef, _ShadeMin, _ShadeMax);
                half3 team = lerp(_TeamColor.rgb, half3(1, 1, 1), _TeamLift);
                team.g = max(team.g, team.b * 0.32);   // critic L02 #6: deep linear blue under the warm key + ACES reads violet; red unaffected (all modes)
                // (toned) team colour only on owner parts, glow lamps and a thin band (tower caps, turret ring, roof trim)
                half paint = mask;
                half3 ozToyOther = other;   // toypaint_v1: the toy albedo's own paint colour (used below instead of the flat _NeutralPaint)
                if (_OZToyKhaki.w > 0.001 && _PaintFromAlpha > 1.5)
                {   // paint_khaki: keep the albedo's own gradient / AO (its luminance), take the target khaki hue and level
                    half ozL = dot(ozToyOther, half3(0.2126, 0.7152, 0.0722));
                    half3 ozK = _OZToyKhaki.rgb * (ozL / max(_OZToyKhakiRef, 1e-3));
                    ozToyOther = lerp(ozToyOther, ozK, _OZToyKhaki.w * saturate(paint));
                }
                if (_Accent > 0.5)
                {
                    team = lerp(team, dot(team, half3(0.2126, 0.7152, 0.0722)).xxx, _TeamTone);
                    half band = smoothstep(_AccentLo - 0.02, _AccentLo + 0.02, objY) * (1 - smoothstep(_AccentHi - 0.02, _AccentHi + 0.02, objY));
                    half accent = saturate(band + _OwnerPart);                                            // robots: chest + shoulder band
                    mask = _OwnerPart > 0.5 ? max(mask, 0.85) : mask * accent;
                    half nshade = clamp(RgbToHsv(maskAlbedo).z / _TeamRef, 0.8, 1.08);
                    other = lerp(other, SRGBToLinear(_NeutralPaint.rgb) * _BaseColor.rgb * nshade, saturate(paint - mask));   // critic #8: _BaseColor carries the wreck char
                    // colour (owned) or a dark unlit grey (neutral); owned paint takes a light livery tint, neutral paint goes flat
                    // grey (empty vehicles, neutral buildings). w of _OZTeamVivid: 0 = not set (v1), 1 = neutral, 2 = owned
                    half v2on = step(0.5, _OZTeamVivid.w), owned = step(1.5, _OZTeamVivid.w);
                    half3 npaint = SRGBToLinear(_NeutralPaint.rgb) * _BaseColor.rgb;
                    half3 accentCol = owned > 0.5 ? (half3)_OZTeamVivid.rgb : dot(npaint, half3(0.2126, 0.7152, 0.0722)).xxx * 0.35;
                    team = lerp(team, accentCol, _OZTeamLook.x * v2on);
                    half pa = saturate(paint - mask);
                    half3 filt = lerp(half3(1, 1, 1), (half3)(_OZTeamVivid.rgb / max(max(_OZTeamVivid.r, max(_OZTeamVivid.g, _OZTeamVivid.b)), 1e-3)), _OZTeamLook.y * owned);
                    other = lerp(other, other * filt, pa);
                    other = lerp(other, dot(other, half3(0.2126, 0.7152, 0.0722)).xxx * 0.94, pa * (1 - owned) * v2on * _OZTeamLook.w);
                    // paint-mode panels (buildings, _OZPanelY.w < 0): the model's own paint panels inside the rule rect and above
                    // the rule height (roof trim, fan ring, crane beam) carry the vivid team colour; neutral keeps them flat grey
                    half ppan = _OZPanelY.w < -0.001 ? OZPanelRect(gOZObjXZ) * OZPanelAbove(objY) * paint * (half)(-_OZPanelY.w) * _OZTeamLook.z * owned : 0;
                    mask = max(mask, ppan);
                    gOZPaintPanel = ppan;
                }
                // albedos carry the finished paint (close khaki tones, gentle gradient, soft AO), so their paint keeps the texture
                // colour instead of the flat _NeutralPaint; teamcolor_v2's cues stay: owned paint takes half its livery tint,
                if (_UnitLook > 0.5 && _PaintFromAlpha > 1.5 && _Accent > 0.5)
                {
                    half ozOwned = step(1.5, _OZTeamVivid.w), ozSet = step(0.5, _OZTeamVivid.w);
                    half ozPa = saturate(paint - mask);
                    half3 ozFilt = lerp(half3(1, 1, 1), (half3)(_OZTeamVivid.rgb / max(max(_OZTeamVivid.r, max(_OZTeamVivid.g, _OZTeamVivid.b)), 1e-3)), 0.5 * _OZTeamLook.y * ozOwned);
                    half3 ozT = lerp(ozToyOther, ozToyOther * ozFilt, ozPa);
                    other = lerp(ozT, dot(ozT, half3(0.2126, 0.7152, 0.0722)) * half3(0.48, 0.46, 0.435), ozPa * (1 - ozOwned) * ozSet * _OZTeamLook.w);
                }
                // grey: teamcolor_v2's neutral grey (lum x 0.94) x 0.6
                else if (_UnitLook > 0.5 && _Accent > 0.5 && _PaintFromAlpha < 0.5 && _UseVertexColor > 0.5 && _TeamMode > 0.5 && _TeamMode < 1.5)
                    other = lerp(other, other * half3(0.60, 0.575, 0.545), saturate(paint - mask) * step(0.5, _OZTeamVivid.w) * (1 - step(1.5, _OZTeamVivid.w)) * _OZTeamLook.w);
                // Warm paint texels (orange / khaki / olive, any
                // paint mode) take the fort cream at their own relative brightness; dark / grey metal, lamps and the team parts stay
                if (_OZFortPaint.w > 0.001)
                {
                    half3 ozOh = RgbToHsv(other);
                    half ozWarm = Band(ozOh.x, 0.0, 0.25) * smoothstep(0.18, 0.32, ozOh.y) * smoothstep(0.02, 0.05, ozOh.z);
                    half ozFl = dot(other, half3(0.2126, 0.7152, 0.0722)) / _OZFortPaint.w;
                    half ozToyPaint = saturate(paint - mask) * step(1.5, _PaintFromAlpha) * step(0.5, _UnitLook);   // toy albedos: their paint mask itself (the perch deck's khaki can be too dull for the hue test)
                    other = lerp(other, _OZFortPaint.rgb * lerp(clamp(0.55 + 0.45 * ozFl, 0.62, 1.1), clamp(0.78 + 0.22 * ozFl, 0.86, 1.1), ozToyPaint), saturate(max(ozWarm * (1 - mask), ozToyPaint)));   // half the tonal range: darker paint still reads cream
                }
                half3 c = lerp(other, team * shade, mask);
                // cream pinstripes (critic #15): the 1-2 texel olive->trim transition fails the sat/value test; texels next to
                // paint take the paint too, unless they are dark panel seams (kept dark, like the Blender recolour)
                // light olive / warm cream strokes only (hue near the paint band, some saturation): neutral grey metal
                // highlights next to the paint stay grey (no red specks on the fort grilles)
                half stroke = Band(hsv.x, _HueMin - 0.08, _HueMax + 0.04) * smoothstep(0.05, 0.09, hsv.y);
                half dm = (1 - mask) * dilated * smoothstep(0.07, 0.13, hsv.z) * stroke * areaMask;             // grey metal never
                c = lerp(c, (_Accent > 0.5 ? SRGBToLinear(_NeutralPaint.rgb) * _BaseColor.rgb : team) * clamp(hsv.z / _TeamRef, _ShadeMin, 1.0), dm);
                maskOut = saturate(mask + dm);
                return c;
            }

            half4 frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half seeCut = OZSeeThruClip(i.positionWS, i.positionCS);          // rocks_v2: units behind rock pillars stay visible
                OZCamFadeClip(i.positionCS);                                      // K440: a shuttle near the game camera is cut whole (never seen into)
                half4 tex;
                if (_Triplanar > 0.5)
                {
                    float3 nw = abs(normalize(i.normalWS)); float wx = pow(nw.x, 4) + 1e-3, wz = pow(nw.z, 4) + 1e-3, wy = pow(nw.y, 4);
                    float v = i.positionWS.y / _TriHeight;
                    float2 uvx = float2(i.positionWS.z * _TriScale, v), uvz = float2(i.positionWS.x * _TriScale, v);
                    if (_TriVertical > 0.5) { uvx = float2(v * 0.35, i.positionWS.z * _TriScale * 3.0); uvz = float2(v * 0.35, i.positionWS.x * _TriScale * 3.0); }   // columns, not layers
                    half4 tx = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvx);
                    half4 tz = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvz);
                    // flat ledges: the stratum's blurred colour at this height (side projections smeared into streaks, critic #11)
                    half4 ty = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, float2(i.positionWS.x * _TriScale, v), 5);
                    if (_Stylize > 0.5)
                    {
                        half4 bx = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uvx, _DegrimeLod);
                        half4 bz = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, uvz, _DegrimeLod);
                        tx = lerp(tx, bx, _Degrime); tz = lerp(tz, bz, _Degrime);
                    }
                    tex = (tx * wx + tz * wz + ty * wy) / (wx + wz + wy) * _BaseColor;
                }
                else tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                if (_Stylize > 0.5 && _Triplanar < 0.5)
                {   // de-grime: stains, scratches and dirt are high-frequency detail; a blurred mip keeps the panel colours
                    half4 soft = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, i.uv, _DegrimeLod) * _BaseColor;
                    tex.rgb = lerp(tex.rgb, soft.rgb, _Degrime);
                }
                half3 vc = _VertexColorSRGB > 0.5 ? SRGBToLinear(i.color.rgb) : i.color.rgb;
                half3 albedo = tex.rgb * lerp(half3(1, 1, 1), vc, _UseVertexColor);
                if (_TopBlend > 0)
                {
                    // (soft blend from normal.y 0.62 to 0.8), steep sides keep the strata; a gentle world-space variation
                    float up = normalize(i.normalWS).y;
                    half topW = smoothstep(_TopMin, _TopMax, up) * _TopBlend;
                    float2 wp = i.positionWS.xz;
                    half var = 0.93 + 0.07 * sin(wp.x * 0.37 + sin(wp.y * 0.21) * 2.0) * sin(wp.y * 0.29 + sin(wp.x * 0.17) * 2.0);
                    albedo = lerp(albedo, SRGBToLinear(_TopColor.rgb) * var, topW);
                }
                half dilated = 0;
                if (_MaskDilate > 0 && _TeamMode > 0.5 && _TeamMode < 1.5 && _Triplanar < 0.5)
                {
                    float2 o = _BaseMap_TexelSize.xy * _MaskDilate;
                    half3 tint = _BaseColor.rgb * lerp(half3(1, 1, 1), vc, _UseVertexColor);
                    dilated = max(max(OliveMask(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv + float2(o.x, 0)).rgb * tint),
                                      OliveMask(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv - float2(o.x, 0)).rgb * tint)),
                                  max(OliveMask(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv + float2(0, o.y)).rgb * tint),
                                      OliveMask(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv - float2(0, o.y)).rgb * tint)));
                }
                half3 maskAlbedo = albedo;
                if (_Triplanar < 0.5 && _TeamMode > 0.5 && _TeamMode < 2.5)
                    maskAlbedo = SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, i.uv, max(_DegrimeLod, 2.0) + 0.8).rgb * _BaseColor.rgb * lerp(half3(1, 1, 1), vc, _UseVertexColor);
                if (_UnitLook > 0.5) maskAlbedo = albedo;   // unitlook_v2: clean flat albedos, no fixed-LOD mask sample (DIAGNOSIS #4)
                half areaMask = 1;
                if (_MaskArea > 0 && _Triplanar < 0.5 && _TeamMode > 0.5 && _TeamMode < 2.5)
                    areaMask = smoothstep(0.12, 0.4, OliveMask(SAMPLE_TEXTURE2D_LOD(_BaseMap, sampler_BaseMap, i.uv, max(_DegrimeLod, 2.0) + 0.8 + _MaskArea).rgb
                                                             * _BaseColor.rgb * lerp(half3(1, 1, 1), vc, _UseVertexColor)));
                // toypaint_v1: toy albedos carry their team panels in alpha (the model's own panels, crisp): teamcolor_v2's
                // rectangle panel below is skipped for them (it reads _OZPanelY through this local copy until the #undef)
                float4 ozToyPanelY = (_UnitLook > 0.5 && _PaintFromAlpha > 1.5) ? float4(0, 0, 0, 0) : _OZPanelY;
                #define _OZPanelY ozToyPanelY
                half3 preTeam = albedo;   // teamcolor_v2: albedo before the team paint (panel shading, dark seams)
                gOZObjXZ = i.objXZ; gOZPaintPanel = 0;   // teamcolor_v2: paint-mode panels in ApplyTeam
                half teamMask; albedo = ApplyTeam(albedo, maskAlbedo, areaMask, i.color, tex.a, dilated, _BandWorld > 0.5 ? i.positionWS.y : i.objY, teamMask);
                // teamcolor_v2: team panel on the up-facing surfaces of turret tops / roofs (TeamPaint.PanelRules: a rounded rect
                // in mesh-space xz above a mesh height), owned accent objects only; dark seams, hatches and recesses stay
                half panel = 0;
                if (_Accent > 0.5 && _OZTeamVivid.w > 1.5 && _OZPanelY.w > 0.001 && _OZTeamLook.z > 0.001)
                {
                    panel = OZPanelRect(i.objXZ) * OZPanelAbove(i.objY)
                          * smoothstep(0.5, 0.72, normalize(i.normalWS).y) * smoothstep(0.02, 0.06, dot(preTeam, half3(0.2126, 0.7152, 0.0722)))
                          * (half)_OZPanelY.w * _OZTeamLook.z;
                    half pshade = clamp(RgbToHsv(preTeam).z / max(_TeamRef, 0.05), 0.85, 1.12);
                    albedo = lerp(albedo, (half3)_OZTeamVivid.rgb * pshade, panel);
                    teamMask = max(teamMask, panel);
                }
                panel = max(panel, gOZPaintPanel);   // teamcolor_v2: paint-mode panels glow faintly at night too (rim-night)
                #undef _OZPanelY
                // toypaint_v1: the team panel of a toy albedo (alpha 1) = the vivid team colour as clean paint, shaded by the
                // texture's own soft AO and gradient (value / _TeamRef); owned only (neutral: the grey paint, unset: the khaki)
                if (_UnitLook > 0.5 && _PaintFromAlpha > 1.5 && _Accent > 0.5 && _OZTeamVivid.w > 1.5)
                {
                    half ozTp = smoothstep(0.62, 0.88, tex.a) * saturate(_OZTeamLook.z);
                    half ozTs = clamp(RgbToHsv(preTeam).z / max(_TeamRef, 0.05), 0.45, 1.2);
                    albedo = lerp(albedo, (half3)_OZTeamVivid.rgb * ozTs, ozTp);
                    teamMask = max(teamMask, ozTp);
                    panel = max(panel, ozTp);
                }
                // Heavy tank look (black-grey when empty, khaki with team accents and the skull when owned): an exception to the subtle
                // team rule, heavy tank only (_LegacyHeavy 1 on its three materials). Its albedos
                // dark steel tracks, deep military khaki armour blocks (alpha 0.5), team accents (alpha 1: turret cheek blocks + the front
                // skull, stored as khaki so they share its shading), bone skulls. Owned = that paint as is (no PaintKhaki remap, no livery
                // M12 neutral: black-grey metal, pale grey panels, no team colour. The wreck char (_BaseColor)
                // and the team colour's charred factor are kept (once each).
                if (_LegacyHeavy > 0.5)
                {
                    half3 hLumW = half3(0.2126, 0.7152, 0.0722);
                    half3 hBase = max(_BaseColor.rgb * lerp(half3(1, 1, 1), vc, _UseVertexColor), 1e-3);
                    half3 hRaw = preTeam / hBase;                                   // the albedo texel without the wreck char
                    half hL = dot(hRaw, hLumW);
                    half hK = max(dot(_BaseColor.rgb, hLumW), 1e-3);                // charred factor (1 intact), also in _TeamColor
                    half hPanel = smoothstep(0.30, 0.45, tex.a);                     // khaki blocks + team accents
                    half hTeam = smoothstep(0.62, 0.88, tex.a);                      // team accents
                    half hOwned = step(1.5, _OZTeamVivid.w), hNeutral = step(0.5, _OZTeamVivid.w) * (1 - hOwned);
                    half hR = clamp(hL / max(_HeavyLook.x, 1e-3), 0.5, 1.25);        // the texel's shading relative to the khaki
                    half3 hTc = (half3)_OZTeamVivid.rgb / hK;                        // the readable team colour of the siblings' toy panels (linear, SetVector; _TeamColor goes through SetColor's sRGB step)
                    half3 hOwn = lerp(hRaw, hTc * hR * _HeavyLook.w + hRaw * (1 - _HeavyLook.w), hTeam * hOwned);
                    half3 hGrey = lerp(hL * _HeavyLook.z, _HeavyLook.y * hR, hPanel) * half3(1.0, 0.985, 0.955);   // warm grey like TeamColors.Neutral
                    // + the game-wide owned-tank cue (teamcolor_v2 "whose tank is where"): the team panel on the turret roof
                    // (TeamPaint.PanelRules HeavyTankTurret), so the team reads at the default zoom next to the light / medium tanks
                    half hTop = 0;
                    if (_OZPanelY.w > 0.001 && _OZTeamLook.z > 0.001)
                        hTop = OZPanelRect(i.objXZ) * OZPanelAbove(i.objY) * smoothstep(0.5, 0.72, normalize(i.normalWS).y)
                             * (half)_OZPanelY.w * saturate(_OZTeamLook.z) * hOwned * (1 - hPanel);
                    hOwn = lerp(hOwn, hTc * clamp(hL / 0.0338, 0.75, 1.15), hTop);   // 0.0338 = linear lum of the hull gunmetal (48,52,57)
                    albedo = lerp(hOwn, hGrey, hNeutral) * hBase;
                    teamMask = max(hTeam, hTop) * hOwned;
                    panel = teamMask;
                }
                half ozRust = 0;
                if (_Rust > 0.001)
                    albedo = OZRustOverlay(albedo, float3(i.objXZ.x, i.objY, i.objXZ.y), normalize(i.normalWS), i.positionWS, _Rust, _RustWaterY, _RustScale, ozRust);
                if (_Stylize > 0.5)
                {   // black armour, joints and guns -> a readable dark gunmetal (added, so painted detail keeps its value order)
                    half lum = dot(albedo, half3(0.2126, 0.7152, 0.0722));
                    albedo += _DarkColor.rgb * (1 - smoothstep(0.0, _DarkThreshold, lum)) * (1 - teamMask);
                }

                SurfaceData s = (SurfaceData)0;
                s.albedo = albedo; s.alpha = 1; s.metallic = _Metallic * (1 - teamMask); s.smoothness = _Smoothness;
                s.occlusion = 1; s.emission = _EmissionColor.rgb; s.normalTS = half3(0, 0, 1);
                if (_UseMaskMap > 0.5)
                {   // fort_v2: per-texel metallic / smoothness (team paint stays non-metallic)
                    half4 mm = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, i.uv);
                    s.metallic = mm.r * (1 - teamMask); s.smoothness = mm.a;
                }
                s.smoothness = lerp(s.smoothness, 0.06h, ozRust); s.metallic *= 1 - ozRust;

                // fort_v2: emission map (windows, roof lamps), faded in with the night; black tint = off (default)
                s.emission += SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uv).rgb * _EmissionMapColor.rgb
                              * lerp(1.0h, saturate(_OZNight), _EmissionNight);

                InputData d = (InputData)0;
                d.positionWS = i.positionWS;
                d.positionCS = i.positionCS;
                d.normalWS = NormalizeNormalPerPixel(i.normalWS);
                if (_UseNormalMap > 0.5 && _Triplanar < 0.5)
                {   // fort_v2: tangent-space normal map (MikkTSpace, baked in Blender)
                    half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uv), _BumpScale);
                    float3 tWS = normalize(i.tangentWS.xyz);
                    float3 bWS = i.tangentWS.w * cross(normalize(i.normalWS), tWS);
                    d.normalWS = NormalizeNormalPerPixel(mul(nTS, float3x3(tWS, bWS, normalize(i.normalWS))));
                }
                if (_FlatShade > 0.5)
                {   // clean low-poly look: one normal per face (screen derivatives), facing the same side as the vertex normal
                    float3 fn = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                    d.normalWS = dot(fn, d.normalWS) < 0 ? -fn : fn;
                }
                d.viewDirectionWS = GetWorldSpaceNormalizeViewDir(i.positionWS);
                // shadow_fix 01: receiver-side normal offset along the geometric normal (the facet normal when _FlatShade, never the normal map)
                float3 ozShadowN = _FlatShade > 0.5 ? (float3)d.normalWS : (float3)NormalizeNormalPerPixel(i.normalWS);
                d.shadowCoord = TransformWorldToShadowCoord(OZShadowRecvPos(i.positionWS, ozShadowN));
                d.fogCoord = i.fogFactor;
                d.bakedGI = SampleSH(d.normalWS);
                d.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                d.shadowMask = half4(1, 1, 1, 1);

                half4 c = UniversalFragmentPBR(d, s);
                half ozSSK = OZSelfShadowApply(c, d, s, i.positionWS, ozShadowN);                // K256g2: the flying shuttle's pods shade its hull (1 = unchanged)
                half nClass = OZNightClass();                                            // night_v2
                c.rgb = OZNightEnvFix(c.rgb, d, s, nClass);                              // night_v2: no day-sky reflection at night
                if (_Stylize > 0.5)
                {
                    Light ml = GetMainLight(d.shadowCoord);
                    ml.shadowAttenuation *= ozSSK;                                           // K256g2: the same self-shadow on the stylized key light
                    half ndl = dot(d.normalWS, ml.direction);
                    half direct = saturate(ndl) * ml.shadowAttenuation;
                    // soft shading: a wrap-light fill only on the terminator and the dark side (no hard black falloff)
                    half fill = saturate(saturate((ndl + 0.6) / 1.6) - saturate(ndl));
                    c.rgb += albedo * ml.color * fill * _SoftFill * lerp(0.55, 1.0, ml.shadowAttenuation);
                    c.rgb += albedo * ml.color * direct * (_KeyWarm.rgb - 1);                    // warm key light
                    c.rgb += albedo * d.bakedGI * (_AmbientBoost - 1);                            // brighter, cleaner ambient
                    // rim light: the silhouette pops off the sand; scaled by the sun so night rims stay subtle
                    half ndv = saturate(dot(d.normalWS, d.viewDirectionWS));
                    half rim = pow(1 - ndv, _RimPower) * (0.35 + 0.65 * saturate(d.normalWS.y * 0.5 + 0.5));
                    half sunK = saturate(dot(ml.color, half3(0.2126, 0.7152, 0.0722)) / 1.6);
                    // night_v2: the day rim below is rim * (0.35 + 0.65 sunK); its 0.35 floor kept a third of the DAY rim at
                    // night on rocks, cliffs, crates and huts. Only units keep it (readability); for every other class the
                    // floor fades with the night: rim is rescaled to rim * (0.35 (1 - night) + 0.65 sunK) (same by day)
                    if (!(nClass > 0.5 && nClass < 1.5))
                        rim *= (0.35 * (1.0 - saturate(_OZNight)) + 0.65 * sunK) / (0.35 + 0.65 * sunK);
                    c.rgb += _RimColor.rgb * rim * (0.35 + 0.65 * sunK);
                }
                // teamcolor_v2: by day the rim of owned stylized accent objects (vehicles, cannons) leans to the team glow, a thin
                // team edge (= lerp(_RimColor, glow, x) on the stylize rim above); it fades with _OZNight, the night rim takes over
                if (_Stylize > 0.5 && _Accent > 0.5 && _OZTeamVivid.w > 1.5 && _OZTeamRim.x > 0.001)
                {
                    Light tml = GetMainLight();
                    half tsunK = saturate(dot(tml.color, half3(0.2126, 0.7152, 0.0722)) / 1.6);
                    half tndv = saturate(dot(d.normalWS, d.viewDirectionWS));
                    half trim = pow(1 - tndv, _RimPower) * (0.35 + 0.65 * saturate(d.normalWS.y * 0.5 + 0.5));
                    c.rgb += ((half3)_OZTeamGlow.rgb * _OZTeamRim.z - _RimColor.rgb) * (_OZTeamRim.x * trim * (0.35 + 0.65 * tsunK) * (1.0 - saturate(_OZNight)));
                }
                // night package: a cool moonlit rim keeps units, vehicles, cannons, buildings and map items readable against
                // the dark ground at night. Only materials that carry a rim by day (stylized units / crates / huts, accent
                // buildings and vehicles; the environment materials have _RimColor 0) and never the triplanar cliffs: rocks,
                // cliffs, bridges and the M12 palette props stay as they are
                // Unit-look materials (_UnitLook 1: vehicles,
                // cannons, robots, weapons) replace the PBR + stylize result above with the clean unit shading of
                // OpenZUnitLook.hlsl (form key ramp, trilight ambient + warm bounce, one capped highlight, every additional
                // light) plus a thin kicker on the top silhouette edge; owned accent objects lean that edge to the team glow like
                // teamcolor_v2's day team rim. The night block below draws its moonlit / team rim on the same edge (wider).
                half ozUnit = OZUnitOn();
                half ozKd = OZKickerDot(d.normalWS, d.viewDirectionWS);
                half ozKw = fwidth(ozKd);                                        // derivatives outside flow control
                half ozNrmW = OZNormalWidth(d.normalWS);
                half ozKickN = OZKicker(ozKd, ozKw, _UnitKick.x - _UnitKick.w, _UnitKick.y);
                if (ozUnit > 0.5)
                {
                    half ozVis;
                    // bare metal (joints, barrels, boots, guns): low-saturation texels that are not team paint; paint gets less
                    half ozMx = max(max(albedo.r, albedo.g), albedo.b), ozMn = min(min(albedo.r, albedo.g), albedo.b);
                    half ozMetal = (1.0 - saturate(teamMask)) * (1.0 - smoothstep(0.10, 0.30, (ozMx - ozMn) / max(ozMx, 1e-3)));
                    // no raised highlight cap (hiGain 1), no environment gloss; the material's own capped highlight only
                    half ozMatte = step(0.5, _UnitShade.w);
                    ozMetal *= 1.0 - ozMatte;
                    half ozSm = saturate(s.smoothness + lerp(_OZSheen.y, _OZSheen.z, ozMetal) * (1.0 - ozMatte));
                    c.rgb = OZUnitShade(d, albedo, min(ozSm, 0.82), s.emission, ozNrmW, ozVis, ozMetal, _OZSheen.w * (1.0 - saturate(_OZNight)) * (1.0 - ozMatte), lerp(_OZSheen.x, 1.0, ozMatte));
                    Light ozL = GetMainLight();
                    half ozSunK = saturate(dot(ozL.color, half3(0.2126, 0.7152, 0.0722)) / 1.6);
                    half ozTeamDay = _OZTeamRim.x * step(0.5, _Accent) * step(1.5, _OZTeamVivid.w) * (1.0 - saturate(_OZNight));
                    half3 ozKcol = lerp(_RimColor.rgb, (half3)_OZTeamGlow.rgb * _OZTeamRim.z, ozTeamDay);
                    c.rgb += ozKcol * (OZKicker(ozKd, ozKw, _UnitKick.x, _UnitKick.y) * _UnitKick.z
                                       * (0.35 * (1.0 - saturate(_OZNight)) + 0.65 * ozSunK) * lerp(0.5, 1.0, ozVis));
                }
                // (end of unitlook_v2; the night package of the comment above continues here)
                // stylized / accent material with a day rim; since rocks_v2 / cliffs v3 (_RimColor 0.12) that included the
                // rock pillars and cliffs, and always the buildings, crates and huts: the moonlit fill (x albedo) on every
                // face, lit or in shadow, plus the blue rim = lilac glowing rocks, pale glowing buildings. Per night class now
                // (OpenZNightV2.hlsl, NightV2.cs): environment none, units as tuned, buildings a faint rim only (their lamps
                // and floods light them), map items a soft rim + a little fill.
                half2 nk = OZNightRimFill(nClass);
                half nightRim = saturate(_OZNight) * nk.x;
                half nightFill = saturate(_OZNight) * nk.y;
                if (nightRim + nightFill > 0.001)
                {
                    half ndvN = saturate(dot(d.normalWS, d.viewDirectionWS));
                    half rimN = pow(1.0 - ndvN, 1.8) * (0.4 + 0.6 * saturate(d.normalWS.y * 0.5 + 0.5));   // wider rim (was 2.6): reads at 70 m
                    if (ozUnit > 0.5) rimN = ozKickN;   // unitlook_v2: the moonlit / team rim on the top edge only, never on the sides
                    // teamcolor_v2: at night the rim of owned units takes the team glow (robots too), team panels glow faintly
                    half teamAware = step(0.5, max(_Accent, _TeamMode)) * step(1.5, _OZTeamVivid.w);
                    c.rgb += lerp(_OZNightRim.rgb, (half3)_OZTeamGlow.rgb * _OZTeamRim.z, _OZTeamRim.y * teamAware) * (rimN * nightRim);
                    c.rgb += (half3)_OZTeamGlow.rgb * (panel * _OZTeamRim.w * nightRim);
                    // soft moonlit fill in the unit's own colour (blue stays blue, never a silhouette); tops a bit brighter
                    c.rgb += albedo * _OZNightFill.rgb * (nightFill * (0.7 + 0.3 * saturate(d.normalWS.y)));   // night_v2: own class factor
                }
                c.rgb *= 1.0 - seeCut * _OZSeeThruParams.w;                         // rocks_v2: darker rim frames the unit
                if (_OzWetness > 0.001 && nClass < 0.5) c.rgb *= OZWetEnvDarken((half)d.normalWS.y);   // weather_l17_v1: wet stone (environment class only)
                c.rgb = lerp(c.rgb, dot(c.rgb, half3(0.2126, 0.7152, 0.0722)).xxx * 1.15, saturate(_OZDead) * 0.55);   // item 228: a dead robot (OzDeadTint): greyer, luminance kept (+15 %), hue kept; 0 on everything alive
                c.rgb = MixFog(c.rgb, i.fogFactor);
                return half4(c.rgb, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection;
            float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; };
            Varyings vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i);
                float3 pw = OZPalmWind(TransformObjectToWorld(i.positionOS.xyz));                       // palm_wind (palms only)
                float3 nw = TransformObjectToWorldNormal(i.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 ld = normalize(_LightPosition - pw);
                    float3 pb = ApplyShadowBias(pw, nw, ld);
                #else
                    // shadow_fix 01: the sun keeps URP's depth bias only (_ShadowBias.x < 0 = away from the light), no inset along
                    // the split vertex normals, so flat-shaded meshes stay closed in the shadow map; ForwardLit's OZShadowRecvPos
                    // applies the normal offset on the receiver instead.
                    float3 ld = _LightDirection;
                    float3 pb = pw + ld * _ShadowBias.x;
                #endif
                float4 cs = TransformWorldToHClip(pb);
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs; return o;
            }
            half4 frag(Varyings i) : SV_Target { return 0; }
            ENDHLSL
        }

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
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); o.positionWS = OZPalmWind(TransformObjectToWorld(i.positionOS.xyz)); o.positionCS = TransformWorldToHClip(o.positionWS); return o; }   // palm_wind
            half frag(Varyings i) : SV_Target { OZSeeThruClip(i.positionWS, i.positionCS); OZCamFadeClip(i.positionCS); return i.positionCS.z; } // rocks_v2; K440
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
            Varyings vert(Attributes i) { Varyings o; UNITY_SETUP_INSTANCE_ID(i); o.positionWS = OZPalmWind(TransformObjectToWorld(i.positionOS.xyz)); o.positionCS = TransformWorldToHClip(o.positionWS); o.normalWS = TransformObjectToWorldNormal(i.normalOS); return o; }   // palm_wind
            half4 frag(Varyings i) : SV_Target { OZSeeThruClip(i.positionWS, i.positionCS); OZCamFadeClip(i.positionCS); return half4(NormalizeNormalPerPixel(i.normalWS), 0); } // rocks_v2; K440
            ENDHLSL
        }

        Pass
        {   // per-object motion vectors (rigid and skinned motion): flying parts streak when the MOTION BLUR option is on
            Name "MotionVectors"
            Tags { "LightMode"="MotionVectors" }
            ColorMask RG
            HLSLPROGRAM
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ObjectMotionVectors.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
