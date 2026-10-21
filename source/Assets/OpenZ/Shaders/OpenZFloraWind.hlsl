// OpenZ level 16: the trees, palms and plants in the wind and in the rain (OzFloraWind.cs sets everything this file reads).
// Included by OpenZ/TeamLit right in front of OZPalmWind, which hands every plant of level 16 to OZFloraWind below. A plant
// is a renderer on a run-time copy of its material with _OzWindPalm.w = -2 .. -7 (its kind, negative); every other TeamLit
// material has 0 (still) or 1 (a desert palm: OZPalmWind's own code, untouched, which lets every w below 0.5 go at once). It
// reads the position only (no normal, uv or colour): split vertices move together, and the colour, shadow and depth passes agree.
//
// The movement is the preparation's rule, step for step the same as OzFloraWindMath.Displace (the plain number twin the
// counted checks run): change one, change the other.
//   _OzWindPalm   (material copy)  xyz = crown pivot in mesh space (a hanging plant: unused), w = minus the kind: -2 palm,
//                                  -3 leafy tree, -4 small plant, -5 bare tree, -6 hanging plant, -7 clump of palms
//   _OzWindPalm2  (material copy)  x = crown radius, y = height (mesh units), z = 0 always moves / 1 tree cell / 2 small plant cell
//   _OZFloraWind  (global)         downwind x, downwind z, strength (calm 0.75, rain wind 1.5; 0 = still), gust 0-1
//   _OZFloraWind2 (global)         x = the wind's own clock (s, 35 % faster in rain), y = rain wind 0-1 (lean, leaves pushed),
//                                  z = ground guard (a point sinks at most this share of its height above the foot)
//   _OZFloraK[6]  (global, per kind) crown sway (rad), trunk bend (rad), leaf tip flutter (m)
//   _OZFloraL[6]  (global, per kind) rain: trunk lean (rad), crown lean (rad), leaves pushed downwind (m)
//   _OZFloraCell  (global)         x0, z0 of the plant cells, cell size (m)
//   _OZFloraFade  (global)         distance (m) from the camera to the middle of the plant's cell up to which the movement is
//                                  full, and from which it is none: tree cells (xy), small plant cells (zw). The far mesh of a
//                                  cell cannot move, so the movement has ended before the cell changes to it.
#ifndef OPENZ_FLORA_WIND_INCLUDED
#define OPENZ_FLORA_WIND_INCLUDED

float4 _OZFloraWind;
float4 _OZFloraWind2;
float4 _OZFloraK[6];
float4 _OZFloraL[6];
float4 _OZFloraCell;
float4 _OZFloraFade;
float4 _OZDownwash;    // DV118 the shuttle's downwash (View/OzFloraDownwash.cs): x, ground y, z, radius (0 = none)
float4 _OZDownwash2;   // its strength, its own clock
float4 _OZDownwash3;   // K256e its ABSOLUTE bend (View/OzFloraWind.cs): the jet axis' ground point x, z; the bend's envelope (eased in, springing back
                       // with one overshoot); the flutter's envelope (0 = none: the plants exactly as without it)

float OZFloraSm(float a, float b, float x)
{
    float t = saturate((x - a) / (b - a));
    return t * t * (3.0 - 2.0 * t);
}

// how far the wind moves the world point pw of a plant standing at foot; fade = 1 near the camera .. 0, S = wind strength
float3 OZFloraMove(float3 pw, float3 foot, float fade, float S, float3 W, float g, float storm)
{
    int row = (int)clamp(-_OzWindPalm.w - 1.5, 0.0, 5.0);                                         // kind 2 .. 7 (stored negative) -> row 0 .. 5
    float4 K = _OZFloraK[row], L = _OZFloraL[row];
    float sc = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));   // the placement scale
    float H = max(_OzWindPalm2.y * sc, 0.2), R = max(_OzWindPalm2.x * sc, 0.1);
    // this plant's own phase, tempo and amplitude (from its foot: the same every run); taller = slower and a smaller angle
    float h1 = OZPalmHash(foot.xz), h2 = OZPalmHash(foot.zx + 17.3);
    float sk = sqrt(7.5 / max(H, 0.05));
    float tempo = clamp(sk, 0.7, 1.5), kA = clamp(sk, 0.75, 1.25);
    float ph = h1 * 6.2831853, fq = (0.88 + 0.24 * h2) * tempo, amp = (0.8 + 0.4 * h2) * S * fade;
    float t = _OZFloraWind2.x;                                                                    // the wind's way, gust and rain lean come from OZFloraWind (with the downwash)
    float3 C = float3(-W.z, 0.0, W.x), U = float3(0.0, 1.0, 0.0);
    float env = (0.55 + 0.45 * g) * amp * K.x * kA;
    float aD = env * (0.35 + 0.65 * (0.6 * sin(2.64 * fq * t + ph) + 0.4 * sin(6.1 * fq * t + 2.3 * ph)));
    float aC = env * 0.45 * sin(1.95 * fq * t + 4.1 * ph);
    aD += L.y * kA * storm * (0.7 + 0.3 * g);                                                     // the crown leans with the rain wind
    float3 d;
    if (row == 4)
    {   // a hanging plant: its top stays, it swings out below, more toward its free end, and only along its own plane
        // (it hangs against rock: the part of the wind that blows along it moves it, the rest presses it on)
        float3 ax = float3(UNITY_MATRIX_M._m00, 0.0, UNITY_MATRIX_M._m20);
        ax = ax / max(length(ax), 1e-4);
        float depth = max(foot.y - pw.y, 0.0);
        float fH = saturate(depth / H);
        float sw = depth * (0.35 + 0.65 * fH);
        float fl = K.z * amp * (0.45 + 0.55 * g) * fH * fH;
        float u1h = sin(6.6 * fq * t + 1.9 * (pw.x - foot.x) / sc + 1.3 * (pw.z - foot.z) / sc + ph);
        float push = L.z * storm * (0.6 + 0.4 * g) * fH * fH;
        float along = dot(W, ax), across = dot(C, ax);
        float a = aD * along + aC * across;
        float m = sw * a + (fl * u1h + push) * along;
        d = ax * m + U * (0.5 * sw * a * a);
    }
    else
    {
        float3 top = mul(UNITY_MATRIX_M, float4(_OzWindPalm.xyz, 1.0)).xyz;                       // the crown pivot
        float3 r = pw - top, p = pw - foot, tp = top - foot;
        float dh = length(r.xz), dl = length(r);
        float wC, wF;
        if (row == 0)
        {   // a palm: the game's own palm weights (OZPalmWind)
            wC = saturate(OZFloraSm(-0.35 * sc, 0.05 * sc, r.y) + OZFloraSm(0.5 * sc, 0.9 * sc, dh) * OZFloraSm(-1.15 * sc, -0.85 * sc, r.y));
            wF = OZFloraSm(0.7 * sc, max(R, 1.0 * sc), dl) * wC;
        }
        else
        {   // leafy trees, small plants, clumps: by the crown's own size; the trunk axis runs from the foot to the pivot
            float u = saturate(p.y / max(tp.y, 0.1 * sc));
            float dax = length(p.xz - u * tp.xz);
            wC = saturate(OZFloraSm(-0.10 * H, 0.05 * H, r.y) + OZFloraSm(0.35 * R, 0.7 * R, dax) * OZFloraSm(-0.45 * H, -0.25 * H, r.y));
            wF = OZFloraSm(0.30 * R, R, dl) * wC;
        }
        wF *= wF;
        // 1. the crown sways about its pivot (a small turn: d = a x r), and leans with the rain wind
        d = wC * (aD * (W * r.y - U * dot(W, r)) + aC * (C * r.y - U * dot(C, r)));
        // 2. the trunk bends about the foot (growing with height squared; the crown rides on the top), and leans in rain
        float f = saturate(p.y / max(tp.y, 0.5 * sc));
        f = f * f * (1.0 - wC) + wC;
        float bend = K.y * kA * amp * (0.4 + 0.6 * g) * (0.65 + 0.35 * sin(1.31 * fq * t + 1.7 * ph));
        bend += L.x * kA * storm * (0.7 + 0.3 * g);
        d += bend * f * (W * p.y - U * dot(W, p));
        // 3. leaves and fronds flutter (growing to the tips, each on its own phase), and stream downwind in rain
        float3 rd = r / max(dl, 1e-3);
        float lp = 1.0 * rd.x + 0.8 * rd.z - 0.6 * dl / sc;
        float lp2 = row == 0 ? lp : lp + 1.7 * (0.9 * p.x + 1.3 * p.z + 0.7 * p.y) / sc;         // leaf clumps of one crown on their own phase
        float u1 = sin(6.6 * fq * t + 1.5 * lp + ph), u2 = sin(11.3 * fq * t + 2.6 * lp2 + 2.0 * ph), s1 = sin(4.7 * fq * t + 1.3 * lp + 1.3 * ph);
        float fa = K.z * amp * (0.45 + 0.55 * g) * wF;
        float3 el = U - rd * rd.y, side = float3(-r.z, 0.0, r.x) / max(dh, 0.5 * sc), wp = W - rd * dot(W, rd);
        d += fa * (el * (0.75 * u1 + 0.25 * u2) + side * (0.55 * s1) + wp * (0.25 + 0.2 * u1));
        d += (wF * L.z * storm * (0.6 + 0.4 * g)) * wp;
        // 4. the ground guard: a leaf near the ground never goes into it
        d.y = max(d.y, -_OZFloraWind2.z * max(p.y, 0.0));
    }
    return d;
}

// K256e (the shuttle's downwash, absolute): every plant round the jet axis leans AWAY from it by an angle of its own, whatever the level's
// wind and whatever its calm sway: WashDeg x its size factor (at least a WashTipM move of its top, at most WashMaxDeg), full out to
// WashFullM from its foot, half at WashHalfM, none from WashEndM, none right under the jet (inside WashCoreM the air blows straight
// down); with a flutter of WashFlutterDeg at 2.2-2.8 Hz. The envelopes come eased from the C# side (OzFloraWind). Step for step the same
// as OzFloraWindMath.DownwashBend (the plain number twin): change one, change the other.
static const float OZBendDeg = 15.0, OZBendMaxDeg = 45.0, OZBendTipM = 0.8, OZBendFullM = 7.5, OZBendHalfM = 10.0, OZBendEndM = 12.5, OZBendCoreM = 1.5;
static const float OZFlutterDeg = 3.0, OZFlutterHz = 2.2, OZFlutterHzVar = 0.6;
float3 OZFloraDownwashBend(float3 pw, float3 foot, float fade)
{
    float E = _OZDownwash3.z, F = _OZDownwash3.w;
    float2 v = foot.xz - _OZDownwash3.xy; float dd = length(v);
    float shape = (1.0 - 0.5 * OZFloraSm(OZBendFullM, OZBendHalfM, dd)) * (1.0 - OZFloraSm(OZBendHalfM, OZBendEndM, dd));
    float3 d = float3(0.0, 0.0, 0.0);
    if (shape <= 0.0 || (E == 0.0 && F <= 0.0)) return d;
    int row = (int)clamp(-_OzWindPalm.w - 1.5, 0.0, 5.0);
    float sc = length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20));
    float H = max(_OzWindPalm2.y * sc, 0.2);
    float kA = clamp(sqrt(7.5 / max(H, 0.05)), 0.75, 1.25);                                      // taller = stiffer (the sway's own size factor)
    float a0 = max(OZBendDeg * 0.017453292 * kA, min(OZBendTipM / H, OZBendMaxDeg * 0.017453292));
    float2 rd = dd > 1e-3 ? v / dd : float2(1.0, 0.0);
    float core = OZFloraSm(0.3, OZBendCoreM, dd);
    float h1 = OZPalmHash(foot.xz), h2 = OZPalmHash(foot.zx + 17.3);
    float fq = OZFlutterHz + OZFlutterHzVar * h2;
    float ang = shape * fade * (a0 * E * core + OZFlutterDeg * 0.017453292 * F * sin(6.2831853 * fq * _OZDownwash2.y + 6.2831853 * h1));
    if (row == 4)
    {   // a hanging plant swings out from its top along its own plane
        float3 ax = float3(UNITY_MATRIX_M._m00, 0.0, UNITY_MATRIX_M._m20); ax = ax / max(length(ax), 1e-4);
        float depth = max(foot.y - pw.y, 0.0), fH = saturate(depth / H), along = rd.x * ax.x + rd.y * ax.z;
        return ax * (ang * depth * fH * along);
    }
    float3 p = pw - foot;
    float f = saturate(p.y / H), pD = p.x * rd.x + p.z * rd.y;                                     // bent from the foot, more toward the top
    d = float3(ang * f * rd.x * p.y, ang * f * (-pD) - 0.5 * ang * ang * f * max(p.y, 0.0), ang * f * rd.y * p.y);
    return d;
}

float3 OZFloraWind(float3 pw)
{
    float S = _OZFloraWind.z;
    float3 foot = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);         // the plant's origin = its foot (a hanging plant: its top)
    float fade = 1.0;
    if (_OzWindPalm2.z > 0.5)
    {   // the plant stands in a cell that has a far mesh: the movement ends before the cell changes to it
        float cs = max(_OZFloraCell.z, 1.0);
        float2 mid = (floor((foot.xz - _OZFloraCell.xy) / cs) + 0.5) * cs + _OZFloraCell.xy;
        float dc = distance(_WorldSpaceCameraPos.xyz, float3(mid.x, 0.0, mid.y));
        float2 ff = _OzWindPalm2.z > 1.5 ? _OZFloraFade.zw : _OZFloraFade.xy;
        fade = 1.0 - OZFloraSm(ff.x, max(ff.y, ff.x + 0.01), dc);
    }
    float3 d = float3(0.0, 0.0, 0.0);
    float3 W = float3(_OZFloraWind.x, 0.0, _OZFloraWind.y);
    float g = _OZFloraWind.w, storm = _OZFloraWind2.y * fade;
    float dR = _OZDownwash.w, dk = _OZDownwash2.x;
    if (dk > 0.0 && dR > 0.01)
    {   // DV118 the shuttle's downwash: the plants round it lean out and shake (the twin: OzFloraWindMath.DownwashMix)
        float2 v = foot.xz - _OZDownwash.xz; float dd = length(v);
        float kw = dk * (1.0 - OZFloraSm(0.35 * dR, dR, dd));
        if (kw > 0.0)
        {
            float2 rd = dd > 1e-3 ? v / dd : W.xz;
            float2 dw = W.xz + (rd - W.xz) * OZFloraSm(0.0, 2.0, dd) + float2(1e-5, 0.0); dw = dw / length(dw);
            float h1 = OZPalmHash(foot.xz), S0 = S;
            S = min(S + 1.2 * kw, max(S, 2.1));
            storm = max(storm, 0.9 * kw * fade);
            float2 wv = W.xz * S0 + dw * 2.5 * kw + float2(1e-5, 0.0); wv = wv / length(wv);
            W = float3(wv.x, 0.0, wv.y);
            g = saturate(g + 0.5 * kw * (0.5 + 0.5 * sin(7.3 * _OZDownwash2.y + 6.2831853 * h1)));
        }
    }
    if (S > 0.0 && fade > 0.0) d = OZFloraMove(pw, foot, fade, S, W, g, storm);
    if (fade > 0.0 && (_OZDownwash3.z != 0.0 || _OZDownwash3.w > 0.0))
    {   // K256e the downwash's absolute bend, after the level wind; the ground guard over both (not for a hanging plant)
        d += OZFloraDownwashBend(pw, foot, fade);
        if ((int)clamp(-_OzWindPalm.w - 1.5, 0.0, 5.0) != 4) d.y = max(d.y, -_OZFloraWind2.z * max(pw.y - foot.y, 0.0));
    }
    return pw + d;
}

#endif
