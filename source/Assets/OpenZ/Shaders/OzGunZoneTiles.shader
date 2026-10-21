// The gun placement zone (OzGunZone): a soft deep-green tint over the ground where a gun may stand, the gun cells (one cell per
// real gun spot of 2 x 2 tiles, OzGunCells) a shade lighter than the darker olive channels between them, with thin faint borders
// whose corners are cut at 45 degrees with rounded joins, a faint "+" where the gun's centre goes, a barely visible "+" where
// cells meet, a thin zone outline and L-shaped corner brackets (a second, inner L on the corner cell's own corner), thin frames
// round objects in the zone. The area's edge (tint and outline) is the area's tiles seen through a small smooth box: straight
// edges stay where they are, a one-tile stair becomes a soft eased step. Next to a static object (a tree, rock, hut, building,
// gun: the objects the placement rule tests) the outline and the brackets fade out, so they never run over trunks or roots.
// Everything is drawn per pixel from the zone's tile data (three small point textures) on a mesh laid on the drawn ground, so
// every line lies exactly on the ground: on slopes, hills and in craters. Line widths stay the same on screen at every zoom
// (pixels at 1080 lines, scaled with the screen); sizes along the ground are metres. Alpha blended, no depth write, a small
// depth offset.
Shader "OpenZ/GunZoneTiles"
{
    Properties
    {
        [HideInInspector] _Cells ("Zone tiles (point)", 2D) = "black" {}
        [HideInInspector] _Corners ("Zone tile corners (point)", 2D) = "black" {}
        [HideInInspector] _Objects ("Objects on and round each zone tile (point)", 2D) = "black" {}
        _Zone ("Zone: west x, north z (world m), tiles east, tiles south", Vector) = (0, 0, 1, 1)
        _FillColor ("Fill colour (inside a cell)", Color) = (0.0706, 0.2275, 0.1882, 1)
        _GapColor ("Fill colour of the channels between cells and of area in no cell", Color) = (0.0902, 0.2157, 0.0745, 1)
        _LineColor ("Line colour", Color) = (0.8431, 0.902, 0.7804, 1)
        _Host ("Building centre xy (zone m), seconds shown, sweep speed m/s", Vector) = (0, 0, 10, 0)
        _Geo ("Cell inset, corner cut, plus arm, joint arm (m)", Vector) = (0.23, 0.5, 0.2, 0.16)
        _Geo2 ("Outline offset, bracket arm, bracket cut (m), pixel scale", Vector) = (0.08, 2.3, 0.8, 1)
        _Geo3 ("Inner bracket arm, cell corner join round, bracket join round, area edge ease (half width) (m)", Vector) = (1.4, 0.1, 0.1, 0.75)
        _Width ("Widths px @1080: cell border, plus, joint, outline", Vector) = (1.2, 1.0, 1.0, 1.5)
        _Width2 ("Widths px @1080: bracket, cell-to-channel step, hole frame; tint share inside a cell", Vector) = (2.3, 1.6, 1.1, 0.72)
        _Alpha1 ("Opacity: fill (channels), cell border, plus (each stroke), joint (each stroke)", Vector) = (0.33, 0.1, 0.13, 0.06)
        _Alpha2 ("Opacity: outline, bracket, hole frame", Vector) = (0.24, 0.7, 0.1, 0)
        _ObjFade ("Next to objects: reach (half width m), fade, start, full", Vector) = (1.1, 0.92, 0.05, 0.4)
        _Fx ("Fade, shimmer strength, shimmer period m, shimmer speed m/s", Vector) = (1, 0.1, 26, 4)
        _Night ("Line gain day, line gain night, fill at night, cell px fade start", Vector) = (1, 0.45, 0.6, 26)
        _BracketCount ("Brackets", Float) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+49" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Offset -1, -2
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // _Cells, one texel per zone tile (i east, j south), bytes:
            //   r: 1 area (a gun may cover it), 2 in a cell, 4 east half of its cell, 8 south half, 16 hole (an object inside the
            //      area), 32 ground that may carry the outline (plain ground or road, not water or rock)
            //   g, b, a: the eight neighbours (1 N, 2 S, 4 W, 8 E, 16 NW, 32 NE, 64 SW, 128 SE): g in the area, b a hole, a ground
            //      that may carry the outline
            // _Corners, one texel per tile corner (the north-west corner of tile (i, j)): r 1 = a joint "+" here
            // _Objects, one texel per zone tile: r 1 = an object the placement rule tests stands on this tile, g = the same for the
            //      eight neighbours (bits as above; also outside the zone)
            TEXTURE2D(_Cells);
            TEXTURE2D(_Corners);
            TEXTURE2D(_Objects);
            float4 _Brackets[16];                  // xy = corner (zone m), z = kind (bit 1: the area lies west, bit 2: north, bit 4: a cell's own corner: inner L), w = line offset (m, signed: - outside)
            float _OZNight;                        // NightGrade global: 0 by day .. 1 at night

            CBUFFER_START(UnityPerMaterial)
            float4 _Zone, _FillColor, _GapColor, _LineColor, _Host, _Geo, _Geo2, _Geo3, _Width, _Width2, _Alpha1, _Alpha2, _ObjFade, _Fx, _Night;
            float _BracketCount, _ZTest;
            CBUFFER_END

            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 w : TEXCOORD0; };

            V vert(A i)
            {
                V o;
                o.w = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(o.w);
                return o;
            }

            // a line of half width h px seen at d px from its centre (box filtered)
            float Cover(float d, float h) { return saturate(h + 0.5 - d); }

            // pixels from p (relative to a "+" centre) to the two strokes of a "+" with arms of length arm: x horizontal, y vertical
            float2 PlusPx2(float2 p, float arm, float2 k)
            {
                float2 q = abs(p);
                return float2(length(float2(max(q.x - arm, 0.0), q.y) * k), length(float2(q.x, max(q.y - arm, 0.0)) * k));
            }

            // opacity of a "+" of two strokes, each of opacity a: where they cross, the mark reads brighter
            float PlusA(float2 dpx, float h, float a)
            {
                return 1.0 - (1.0 - Cover(dpx.x, h) * a) * (1.0 - Cover(dpx.y, h) * a);
            }

            // a smooth box of half width h (smoothstep edges): its share left of t, and its slope there
            float2 BoxF(float t, float h)
            {
                float u = saturate((t + h) / (2.0 * h));
                return float2(u * u * (3.0 - 2.0 * u), 6.0 * u * (1.0 - u) / (2.0 * h));
            }

            // nine tile bits (q = (dj + 1) * 3 + (di + 1)) from a tile's own bit and its eight neighbours' byte (1 N .. 128 SE)
            uint Nine(uint nb, bool own)
            {
                return ((nb >> 4) & 1u) | ((nb & 1u) << 1) | (((nb >> 5) & 1u) << 2) | (((nb >> 2) & 1u) << 3) | (own ? 16u : 0u)
                     | (((nb >> 3) & 1u) << 5) | (((nb >> 6) & 1u) << 6) | (((nb >> 1) & 1u) << 7) | (((nb >> 7) & 1u) << 8);
            }

            // the 3 x 3 tiles round the pixel (nine bits) seen through a separable smooth box of half width h (<= 2 m); f = the
            // pixel's metres in its tile (0..2). x = the share covered, yz = its gradient per metre
            float3 Blur3(uint occ, float2 f, float h)
            {
                float2 a = BoxF(-f.x, h), b = BoxF(2.0 - f.x, h), c = BoxF(-f.y, h), e = BoxF(2.0 - f.y, h);
                float3 wx = float3(a.x, b.x - a.x, 1.0 - b.x), dwx = float3(-a.y, a.y - b.y, b.y);
                float3 wy = float3(c.x, e.x - c.x, 1.0 - e.x), dwy = float3(-c.y, c.y - e.y, e.y);
                float3 r = float3(0.0, 0.0, 0.0);
                [unroll] for (int q = 0; q < 9; q++)
                {
                    float o = (float)((occ >> (uint)q) & 1u);
                    int i = q % 3, j = q / 3;
                    r += o * float3(wx[i] * wy[j], dwx[i] * wy[j], wx[i] * dwy[j]);
                }
                return r;
            }

            // signed distance (m, + outside) of the quadrant u >= o with its corner cut at 45 degrees (cut c) and the cut's two
            // joins rounded (smooth maximum r); yz = its gradient
            float3 CornerSd(float2 u, float o, float c, float r)
            {
                float a1 = o - u.x, a2 = o - u.y;
                float mx = max(a1, a2);
                float2 gm = a1 >= a2 ? float2(-1.0, 0.0) : float2(0.0, -1.0);
                float cut = (2.0 * o + c - u.x - u.y) * 0.70710678;
                float w = saturate(0.5 + 0.5 * (cut - mx) / max(r, 1e-4));
                return float3(mx + (cut - mx) * w + r * w * (1.0 - w), gm + (float2(-0.70710678, -0.70710678) - gm) * w);
            }

            // pixels to the zero line of a distance field (metres, gradient per metre; k = pixels per metre along x / y)
            float PxTo(float sd, float2 g, float2 k) { return abs(sd) / max(length(g / k), 1e-6); }

            // pixels from u (metres from the corner into the area, axes turned by s) to a bracket's L: the corner cut and rounded
            // as CornerSd, each arm of length arm with a round end
            float LPx(float2 u, float2 s, float o, float c, float r, float arm, float2 k)
            {
                float3 cs = CornerSd(u, o, c, r);
                float d = PxTo(cs.x, cs.yz * s, k);
                if (u.x > arm) d = length(float2(u.x - arm, u.y - o) * k);
                if (u.y > arm) d = length(float2(u.x - o, u.y - arm) * k);
                if (u.x > arm && u.y > arm) d = 99.0;
                return d;
            }

            // one candidate edge of the area: metres to it and what lies beyond it
            void Cand(bool use, float dist, bool hole, bool draw, inout float bm, inout bool bh, inout bool bd)
            {
                if (use && dist < bm) { bm = dist; bh = hole; bd = draw; }
            }

            half4 frag(V i) : SV_Target
            {
                float2 m = float2(i.w.x - _Zone.x, _Zone.y - i.w.z);                 // zone metres: x east, y south
                float2 k = 1.0 / max(float2(length(float2(ddx(m.x), ddy(m.x))), length(float2(ddx(m.y), ddy(m.y)))), 1e-5);   // pixels per metre along x / y
                float ps = _Geo2.w;
                int zw = (int)_Zone.z, zh = (int)_Zone.w;
                float2 tl = m * 0.5;
                int2 ti = (int2)floor(tl);
                bool inZone = ti.x >= 0 && ti.y >= 0 && ti.x < zw && ti.y < zh;
                int2 tc = clamp(ti, int2(0, 0), int2(zw - 1, zh - 1));
                uint4 d = (uint4)(LOAD_TEXTURE2D(_Cells, tc) * 255.0 + 0.5);
                uint2 ob = (uint2)(LOAD_TEXTURE2D(_Objects, tc).rg * 255.0 + 0.5);
                float2 f = (tl - (float2)ti) * 2.0;                                    // metres inside the tile, 0..2
                bool area = inZone && (d.r & 1u) != 0u;
                bool cell = area && (d.r & 2u) != 0u;
                bool ownHole = (d.r & 16u) != 0u, ownDraw = (d.r & 32u) != 0u;
                bool onMesh = inZone && (area || ownDraw);                             // the tiles the zone's mesh covers

                // ---- the nearest edge of the area: what lies beyond it (an object inside the zone, ground that carries the outline)
                uint same = area ? d.g : (~d.g & 255u);                                // neighbours on my side of the edge
                float bm = 99.0; bool bh = false, bd = false;
                // for a pixel inside the area, what lies beyond an edge is the neighbour; outside, it is this tile itself
                #define BEYOND(bit) (area ? (d.b & (bit)) != 0u : ownHole), (area ? (d.a & (bit)) != 0u : ownDraw)
                Cand((same & 1u) == 0u, f.y, BEYOND(1u), bm, bh, bd);
                Cand((same & 2u) == 0u, 2.0 - f.y, BEYOND(2u), bm, bh, bd);
                Cand((same & 4u) == 0u, f.x, BEYOND(4u), bm, bh, bd);
                Cand((same & 8u) == 0u, 2.0 - f.x, BEYOND(8u), bm, bh, bd);
                Cand((same & 16u) == 0u && (same & 5u) == 5u, length(f), BEYOND(16u), bm, bh, bd);
                Cand((same & 32u) == 0u && (same & 9u) == 9u, length(float2(2.0 - f.x, f.y)), BEYOND(32u), bm, bh, bd);
                Cand((same & 64u) == 0u && (same & 6u) == 6u, length(float2(f.x, 2.0 - f.y)), BEYOND(64u), bm, bh, bd);
                Cand((same & 128u) == 0u && (same & 10u) == 10u, length(2.0 - f), BEYOND(128u), bm, bh, bd);
                #undef BEYOND

                // ---- the eased area: the 3 x 3 tiles' area seen through the smooth box (straight edges exact, stairs eased)
                float h = _Geo3.w;
                float3 fA = Blur3(inZone ? Nine(d.g, area) : 0u, f, h);
                float gApx = max(length(fA.yz / k), 1e-6);                             // the share per pixel across the edge
                // the objects round the pixel (trees, rocks, huts, buildings, guns): the outline and the brackets fade next to them
                float oT = saturate((Blur3(Nine(ob.g, (ob.r & 1u) != 0u), f, _ObjFade.x).x - _ObjFade.z) / max(_ObjFade.w - _ObjFade.z, 1e-4));
                float objKeep = 1.0 - _ObjFade.y * oT * oT * (3.0 - 2.0 * oT);

                // ---- tint: inside the eased edge (anti-aliased), on the mesh's tiles only
                float fill = onMesh ? saturate((fA.x - 0.5) / gApx + 0.5) : 0.0;

                // ---- outline: the eased edge moved out by the offset (in where the ground beyond may not carry it); hole frames
                float off = _Geo2.x;
                bool outer = !bh && onMesh && !(ownHole && !area);
                float outlinePx = abs(fA.x - BoxF(bd ? -off : off, h).x) / gApx;
                float outline = (outer ? Cover(outlinePx, 0.5 * _Width.w * ps) : 0.0) * objKeep;
                float holePx = abs(fA.x - BoxF(0.05, h).x) / gApx;
                float frame = (bh && area && !cell) ? Cover(holePx, 0.5 * _Width2.z * ps) : 0.0;

                // ---- the cell: its thin border (straight sides, corners cut at 45 degrees with rounded joins) and the "+" where the
                //      gun's centre stands
                float2 cl = float2((d.r & 4u) != 0u ? 2.0 : 0.0, (d.r & 8u) != 0u ? 2.0 : 0.0) + f - 2.0;
                float2 sg = float2(cl.x >= 0.0 ? 1.0 : -1.0, cl.y >= 0.0 ? 1.0 : -1.0);
                float3 cs = CornerSd((2.0 - _Geo.x) - abs(cl), 0.0, _Geo.y, _Geo3.y);  // + outside the cell's border
                float sdb = cs.x;
                float borderPx = PxTo(sdb, -cs.yz * sg, k);
                float border = cell ? Cover(borderPx, 0.5 * _Width.x * ps) : 0.0;
                float plus = cell ? PlusA(PlusPx2(cl, _Geo.z, k), 0.5 * _Width.y * ps, _Alpha1.z) : 0.0;

                // ---- joints: the faint "+" at a tile corner where cells meet and a gun may stand centred
                float2 kc = round(tl);
                int2 ci = clamp((int2)kc, int2(0, 0), int2(zw, zh));
                uint cf = (uint)(LOAD_TEXTURE2D(_Corners, ci).r * 255.0 + 0.5);
                float joint = (cf & 1u) != 0u ? PlusA(PlusPx2((tl - kc) * 2.0, _Geo.w, k), 0.5 * _Width.z * ps, _Alpha1.w) : 0.0;

                // ---- corner brackets: the same eased corner, the outline cut where a bracket stands; where the area's corner is
                //      a cell's own corner, a second, inner L on that cell's border; next to an object they fade as the outline
                float bracketPx = 99.0, bracketKeep = 1.0;
                float arm = _Geo2.y;
                int nbk = (int)_BracketCount;
                [loop] for (int b = 0; b < 16; b++)
                {
                    if (b >= nbk) break;
                    float4 B = _Brackets[b];
                    int kind = (int)(B.z + 0.5);
                    float2 s = float2((kind & 1) != 0 ? -1.0 : 1.0, (kind & 2) != 0 ? -1.0 : 1.0);
                    float2 u = (m - B.xy) * s;
                    if (!(abs(u.x) < arm + 1.0 && abs(u.y) < arm + 1.0)) continue;
                    bracketPx = min(bracketPx, LPx(u, s, B.w, _Geo2.z, _Geo3.z, arm, k));
                    if ((kind & 4) != 0) bracketPx = min(bracketPx, LPx(u, s, _Geo.x, _Geo.y, _Geo3.y, _Geo3.x, k));
                    if (u.x < arm && u.y < arm) outline = 0.0;
                    if (bracketPx < 6.0) bracketKeep = objKeep;
                }
                float bracket = Cover(bracketPx, 0.5 * _Width2.x * ps) * bracketKeep;

                // ---- motion: the cells come in from the building (a short sweep), a slow sheen travels over the lines
                float appear = _Host.w > 0.0 ? saturate((_Host.z - length(m - _Host.xy) / _Host.w) / 0.18) : 1.0;
                float band = frac((m.x + 0.6 * m.y) / _Fx.z - _Time.y * _Fx.w / _Fx.z) - 0.5;
                float sheen = 1.0 + _Fx.y * exp(-band * band * 40.0);

                // ---- far zoom: the cell marks fade before they turn to noise (a cell under about 26 pixels)
                float cellPx = 4.0 * min(k.x, k.y);
                float detail = saturate((cellPx - _Night.w) / 16.0) * appear;

                float aBorder = border * _Alpha1.y * detail, aPlus = plus * detail, aJoint = joint * detail;
                float aOutline = outline * _Alpha2.x, aBracket = bracket * _Alpha2.y, aFrame = frame * _Alpha2.z * detail;
                float lines = 1.0 - (1.0 - aBorder) * (1.0 - aPlus) * (1.0 - aJoint) * (1.0 - aOutline) * (1.0 - aBracket) * (1.0 - aFrame);
                lines = saturate(lines * sheen);

                float night = saturate(_OZNight);
                // inside the cell's border a shade lighter, the step to the channel eased over a pixel or two; far off (detail 0)
                // the whole cell takes the cell's shade, so the thin channels never break into a moire
                float inner = cell ? lerp(1.0, saturate(-sign(sdb) * borderPx / max(_Width2.y, 1e-3) + 0.5), detail) : 0.0;
                float fa = fill * _Alpha1.x * lerp(1.0, _Width2.w, inner) * lerp(1.0, _Night.z, night) * lerp(0.4, 1.0, appear);
                float3 lc = _LineColor.rgb * lerp(_Night.x, _Night.y, night);
                float aOut = fa + lines - fa * lines;
                float3 fc = lerp(_GapColor.rgb, _FillColor.rgb, inner);                // the cells' deep green, the channels' darker olive
                float3 rgb = (fc * fa * (1.0 - lines) + lc * lines) / max(aOut, 1e-4);
                return half4(rgb, aOut * _Fx.x * (inZone ? 1.0 : 0.0));
            }
            ENDHLSL
        }
    }
}
