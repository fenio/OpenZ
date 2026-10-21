using System;
using System.Collections.Generic;

namespace OpenZ
{
    /// <summary>
    /// The level 16 plant sway as plain numbers. Pure C# (no UnityEngine): the headless check runs exactly this code.
    /// <list type="bullet">
    /// <item><see cref="Measure"/>: the shape of one plant from its points (trunk radius, where the crown starts, the crown
    /// pivot, the crown radius, the height), the way the preparation measured its five trees;</item>
    /// <item><see cref="Displace"/>: where the wind moves one point, the SAME steps in the SAME order as OZFloraWind in
    /// Shaders/OpenZFloraWind.hlsl (that file is the one that draws; this one measures, for the counted checks). Change one,
    /// change the other.</item>
    /// </list>
    /// Space: x east, y up, z north, metres. Kinds: a palm moves like the desert palms (the game's own weights), a leafy tree
    /// and a small plant by the crown's own size, a bare tree without leaf flutter, a hanging plant swings from its top in its
    /// own plane (it hangs against rock), a clump of palms takes the palms' numbers with the leafy weights (several crowns).
    /// Still things (logs, rocks, mounds, lily pads, the joined flowers) are not hooked at all.
    /// </summary>
    public static class OzFloraWindMath
    {
        public const int Still = 0, Palm = 2, Tree = 3, Small = 4, Bare = 5, Hang = 6, Clump = 7;
        public const int Kinds = 6;                                             // rows of the tables below: palm, tree, small, bare, hang, clump

        // At wind strength 1 and gust 1: crown sway (deg), trunk bend at the top (deg), leaf tip flutter (m);
        // in rain only: trunk lean (deg), crown lean (deg), leaves pushed downwind (m). Palm row = the desert palms' numbers.
        public static readonly float[] SwayDeg = { 4.5f, 2.0f, 3.0f, 1.0f, 2.5f, 4.5f };
        public static readonly float[] BendDeg = { 0.8f, 0.5f, 1.5f, 0.5f, 0.0f, 0.8f };
        public static readonly float[] FlutterM = { 0.22f, 0.16f, 0.06f, 0.0f, 0.05f, 0.22f };
        public static readonly float[] LeanTrunkDeg = { 3.0f, 1.5f, 5.0f, 1.5f, 0.0f, 3.0f };
        public static readonly float[] LeanCrownDeg = { 7.0f, 3.5f, 6.0f, 2.0f, 6.0f, 7.0f };
        public static readonly float[] PushM = { 0.35f, 0.25f, 0.10f, 0.0f, 0.10f, 0.35f };
        /// <summary>No point sinks further than this share of its own height above the foot: leaves near the ground never go into it.</summary>
        public const float GroundGuard = 0.6f;

        public const float CalmStrength = 0.75f, RainStrength = 1.5f;            // the wind strength the design's numbers are given at
        public const float RainTempo = 0.35f;                                    // in rain everything moves this much faster

        public static int Row(int kind) => kind < Palm ? 0 : kind > Clump ? Kinds - 1 : kind - Palm;

        public struct Shape
        {
            public int Kind;
            public float Px, Py, Pz;        // crown pivot, the plant's own space (origin = its foot; a hanging plant: its top)
            public float R;                 // crown radius
            public float H;                 // height (a hanging plant: its length)
            public float TrunkR, CrownBase; // for the record
        }

        public struct Wind
        {
            public float Wx, Wz;            // the way the wind goes (unit)
            public float S;                 // strength: calm 0.75, rain 1.5
            public float Gust;              // 0..1
            public float T;                 // the wind's own clock: seconds, running 35 % faster in rain
            public float Storm;             // 0 calm .. 1 rain wind (lean, push)
            /// <summary>K256e the downwash's absolute bend (<see cref="DownwashBend"/>): the jet axis' ground point x, z; the bend's envelope
            /// (0 .. 1, below 0 while it springs back); the flutter's envelope (0 = none). All 0 = the numbers without it, bit for bit.</summary>
            public float DbX, DbZ, DbE, DbF;
            public float DwX, DwY, DwZ, DwR, DwK, DwT;   // a local downwash (View/OzFloraDownwash.cs): its ground point, radius, strength, clock; strength 0 = none (the numbers without it)
        }

        // ------------------------------------------------------------------ small helpers (as the shader's)
        static float Frac(float x) => x - (float)Math.Floor(x);
        static float Sat(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        static float Clamp(float x, float a, float b) => x < a ? a : x > b ? b : x;
        static float Smooth(float a, float b, float x) { float t = Sat((x - a) / (b - a)); return t * t * (3f - 2f * t); }
        static float Sin(float x) => (float)Math.Sin(x);
        const float Deg = 0.017453292f;

        /// <summary>OZPalmHash of Shaders/TeamLit.shader.</summary>
        public static float Hash(float px, float py)
        {
            float qx = Frac(px * 0.1031f), qy = Frac(py * 0.1031f), qz = Frac(px * 0.1031f);
            float d = qx * (qy + 33.33f) + qy * (qz + 33.33f) + qz * (qx + 33.33f);
            qx += d; qy += d; qz += d;
            return Frac((qx + qy) * qz);
        }

        /// <summary>The gust the trees take in a rain spell (the flags' gust in calm weather comes from FlagView).</summary>
        public static float RainGust(float t) => Sat(0.78f + 0.22f * Sin(0.9f * t + 0.4f));
        /// <summary>FlagView's gust without its noise term: used where no flag wind is published.</summary>
        public static float CalmGust(float t) => Sat(0.45f + 0.4f * Sin(0.23f * t) * Sin(0.61f * t + 1.3f));

        /// <summary>Taller = slower and a smaller angle: tempo and angle factor of a plant of height h (m).</summary>
        public static void SizeK(float h, out float tempo, out float angle)
        {
            float k = (float)Math.Sqrt(7.5f / Math.Max(h, 0.05f));
            tempo = Clamp(k, 0.7f, 1.5f); angle = Clamp(k, 0.75f, 1.25f);
        }

        /// <summary>DV118, the shuttle's downwash on one plant (the twin of the lines in Shaders/OpenZFloraWind.hlsl OZFloraWind: change one,
        /// change the other). From the plant's foot: kw = k (1 - smoothstep(0.35 R, R, d)); the wind's way bent outward (blended to the level
        /// wind within 2 m of the middle), the strength up by 1.2 kw (at most 2.1, never down), the rain lean / leaf push at least 0.9 kw,
        /// the gust pumped by the rotor (its own clock, the plant's own phase). Strength 0: nothing changes.</summary>
        public static void DownwashMix(in Wind w, float footX, float footZ, float fade, ref float S, ref float g, ref float storm, ref float Wx, ref float Wz)
        {
            float R = w.DwR, kd = w.DwK;
            if (kd <= 0f || R <= 0.01f) return;
            float vx = footX - w.DwX, vz = footZ - w.DwZ, dd = (float)Math.Sqrt(vx * vx + vz * vz);
            float kw = kd * (1f - Smooth(0.35f * R, R, dd));
            if (kw <= 0f) return;
            float rx = dd > 1e-3f ? vx / dd : Wx, rz = dd > 1e-3f ? vz / dd : Wz, b2 = Smooth(0f, 2f, dd);
            float dx = Wx + (rx - Wx) * b2 + 1e-5f, dz = Wz + (rz - Wz) * b2, dl = (float)Math.Sqrt(dx * dx + dz * dz); dx /= dl; dz /= dl;
            float h1 = Hash(footX, footZ), S0 = S;
            S = Math.Min(S + 1.2f * kw, Math.Max(S, 2.1f));
            storm = Math.Max(storm, 0.9f * kw * fade);
            float wx = Wx * S0 + dx * 2.5f * kw + 1e-5f, wz = Wz * S0 + dz * 2.5f * kw, wl = (float)Math.Sqrt(wx * wx + wz * wz);
            Wx = wx / wl; Wz = wz / wl;
            g = Sat(g + 0.5f * kw * (0.5f + 0.5f * Sin(7.3f * w.DwT + 6.2831853f * h1)));
        }

        // ------------------------------------------------------------------ K256e: the downwash's ABSOLUTE bend
        /// <summary>K256e (the shuttle's downwash, absolute; the twin of OZFloraDownwashBend in Shaders/OpenZFloraWind.hlsl: change one,
        /// change the other). From the plant's foot, d m from the jet axis' ground point: the plant leans AWAY from the axis by
        /// max(WashDeg x its size factor, WashTipM / its height) (at most WashMaxDeg), x the bend's envelope, full out to WashFullM, half
        /// at WashHalfM, none from WashEndM, none right under the jet (inside WashCoreM), plus a flutter of WashFlutterDeg at WashFlutterHz ..
        /// WashFlutterHz + WashFlutterHzVar x the flutter's envelope. Bent from the foot (a point at height y moves angle x y x y / H: a curve),
        /// its top a little lower (the second order of the turn); a hanging plant swings from its top along its own plane. The level's
        /// wind is not read: a plant bends the same in calm and in rain.</summary>
        public static float WashDeg = 15f, WashMaxDeg = 45f, WashTipM = 0.8f, WashFullM = 7.5f, WashHalfM = 10f, WashEndM = 12.5f, WashCoreM = 1.5f;
        public static float WashFlutterDeg = 3f, WashFlutterHz = 2.2f, WashFlutterHzVar = 0.6f;
        /// <summary>The bend's envelope: the downwash strength k (OzFloraDownwash: 1.6 up to 6 m, 0.9 at 14 m, none from 24 m) gives the
        /// wanted bend smoothstep(WashKFrom, WashKFull, k): full while the pads are under about 16 m (the hull under 18), none from 20 m. It follows that
        /// target eased in (critically damped, WashUpW: about 0.3 s to 0.85), and springs back (WashDownW, WashDownZeta: one overshoot of
        /// about a sixth the other way, settled within about 1.2 s); the flutter's envelope eases in WashFlutterUpSec, out WashFlutterDownSec.</summary>
        public static float WashKFrom = 0.3f, WashKFull = 0.72f, WashUpW = 12f, WashDownW = 5.0f, WashDownZeta = 0.5f, WashFlutterUpSec = 0.15f, WashFlutterDownSec = 0.5f;

        public static float BendTarget(float k) => Smooth(WashKFrom, WashKFull, k);

        /// <summary>One step of the envelopes (dt game seconds; 0 = paused: nothing moves). mode: +1 easing in (the target rose last), -1
        /// springing back (it fell last); lastT: the target of the step before.</summary>
        public static void BendSpring(ref float e, ref float v, ref float fl, ref int mode, ref float lastT, float target, float dt)
        {
            if (dt <= 0f) return;
            if (target > lastT + 1e-4f) mode = 1; else if (target < lastT - 1e-4f) mode = -1;
            lastT = target;
            float w0 = mode < 0 ? WashDownW : WashUpW, z = mode < 0 ? WashDownZeta : 1f;
            int n = (int)Math.Ceiling(dt * 240f); if (n < 1) n = 1; if (n > 64) n = 64;
            float h = dt / n;
            for (int i = 0; i < n; i++) { v += (w0 * w0 * (target - e) - 2f * z * w0 * v) * h; e += v * h; }
            float ft = Sat(target), tc = ft > fl ? WashFlutterUpSec : WashFlutterDownSec;
            fl += (ft - fl) * (1f - (float)Math.Exp(-dt / Math.Max(0.01f, tc)));
            if (Math.Abs(e) < 1e-4f && Math.Abs(v) < 1e-3f && target <= 0f) { e = 0f; v = 0f; }
            if (fl < 1e-4f && target <= 0f) fl = 0f;
        }

        /// <summary>K256e: the absolute bend added to (dx, dy, dz) for one point (the arguments as <see cref="Displace"/>); the ground
        /// guard over the sum (not for a hanging plant).</summary>
        public static void DownwashBend(int kind, in Wind w, float footX, float footY, float footZ, float sc, float h, float fade,
                                        float x, float y, float z, float axX, float axZ, ref float dx, ref float dy, ref float dz)
        {
            float E = w.DbE, F = w.DbF;
            float vx = footX - w.DbX, vz = footZ - w.DbZ, dd = (float)Math.Sqrt(vx * vx + vz * vz);
            float shape = (1f - 0.5f * Smooth(WashFullM, WashHalfM, dd)) * (1f - Smooth(WashHalfM, WashEndM, dd));
            if (shape <= 0f || (E == 0f && F <= 0f)) return;
            float H = Math.Max(h, 0.2f);
            float kA = Clamp((float)Math.Sqrt(7.5f / Math.Max(H, 0.05f)), 0.75f, 1.25f);
            float a0 = Math.Max(WashDeg * Deg * kA, Math.Min(WashTipM / H, WashMaxDeg * Deg));
            float rx = dd > 1e-3f ? vx / dd : 1f, rz = dd > 1e-3f ? vz / dd : 0f;
            float core = Smooth(0.3f, WashCoreM, dd);
            float h1 = Hash(footX, footZ), h2 = Hash(footZ + 17.3f, footX + 17.3f);
            float fq = WashFlutterHz + WashFlutterHzVar * h2;
            float ang = shape * fade * (a0 * E * core + WashFlutterDeg * Deg * F * Sin(6.2831853f * fq * w.DwT + 6.2831853f * h1));
            if (kind == Hang)
            {
                float depth = Math.Max(footY - y, 0f), fH = Sat(depth / H), along = rx * axX + rz * axZ;
                float m = ang * depth * fH * along;
                dx += m * axX; dz += m * axZ;
                return;
            }
            float px = x - footX, py = y - footY, pz = z - footZ;
            float f = Sat(py / H), pD = px * rx + pz * rz;
            dx += ang * f * rx * py;
            dy += ang * f * (-pD) - 0.5f * ang * ang * f * Math.Max(py, 0f);
            dz += ang * f * rz * py;
            float floor = -GroundGuard * Math.Max(py, 0f);
            if (dy < floor) dy = floor;
        }

        // ------------------------------------------------------------------ the movement of one point
        /// <summary>
        /// Where the wind moves the world point (x, y, z) of a plant. foot = the plant's origin in the world, top = its crown
        /// pivot in the world, sc = its placement scale, r / h = crown radius and height in the world (m), fade = 1 near the
        /// camera .. 0 where the far version takes over. axX / axZ = a hanging plant's own sideways direction in the world (unit).
        /// </summary>
        public static void Displace(int kind, in Wind w, float footX, float footY, float footZ, float topX, float topY, float topZ,
                                    float sc, float r, float h, float fade, float x, float y, float z, out float dx, out float dy, out float dz,
                                    float axX = 1f, float axZ = 0f)
        {
            DisplaceWind(kind, w, footX, footY, footZ, topX, topY, topZ, sc, r, h, fade, x, y, z, out dx, out dy, out dz, axX, axZ);
            if (kind >= Palm && fade > 0f && (w.DbE != 0f || w.DbF > 0f)) DownwashBend(kind, w, footX, footY, footZ, sc, h, fade, x, y, z, axX, axZ, ref dx, ref dy, ref dz);   // K256e
        }

        /// <summary>The level's wind (with the K256b downwash mix) alone: the movement as it was before K256e.</summary>
        static void DisplaceWind(int kind, in Wind w, float footX, float footY, float footZ, float topX, float topY, float topZ,
                                 float sc, float r, float h, float fade, float x, float y, float z, out float dx, out float dy, out float dz,
                                 float axX, float axZ)
        {
            dx = dy = dz = 0f;
            if (kind < Palm || fade <= 0f) return;
            float S = w.S, g = w.Gust, storm = w.Storm * fade, Wx = w.Wx, Wz = w.Wz;
            if (w.DwK > 0f) DownwashMix(w, footX, footZ, fade, ref S, ref g, ref storm, ref Wx, ref Wz);   // DV118: the shuttle's downwash, the shader's own mix
            if (S <= 0f) return;
            int row = Row(kind);
            float H = Math.Max(h, 0.2f), R = Math.Max(r, 0.1f);
            float h1 = Hash(footX, footZ), h2 = Hash(footZ + 17.3f, footX + 17.3f);
            SizeK(H, out float tempo, out float kA);
            float ph = h1 * 6.2831853f, fq = (0.88f + 0.24f * h2) * tempo, amp = (0.8f + 0.4f * h2) * S * fade;
            float t = w.T;
            float Cx = -Wz, Cz = Wx;
            float kSway = SwayDeg[row] * Deg * kA, kBend = BendDeg[row] * Deg * kA;
            float env = (0.55f + 0.45f * g) * amp * kSway;
            float aD = env * (0.35f + 0.65f * (0.6f * Sin(2.64f * fq * t + ph) + 0.4f * Sin(6.1f * fq * t + 2.3f * ph)));
            float aC = env * 0.45f * Sin(1.95f * fq * t + 4.1f * ph);
            aD += LeanCrownDeg[row] * Deg * kA * storm * (0.7f + 0.3f * g);

            if (kind == Hang)
            {   // a hanging plant: its top stays, it swings out below, more toward its free end, and only along its own
                // plane (it hangs against rock: the part of the wind that blows along it moves it, the rest presses it on)
                float depth = Math.Max(footY - y, 0f);
                float fH = Sat(depth / H);
                float sw = depth * (0.35f + 0.65f * fH);
                float fl = FlutterM[row] * amp * (0.45f + 0.55f * g) * fH * fH;
                float u1h = Sin(6.6f * fq * t + 1.9f * (x - footX) / sc + 1.3f * (z - footZ) / sc + ph);
                float push = PushM[row] * storm * (0.6f + 0.4f * g) * fH * fH;
                float along = Wx * axX + Wz * axZ, across = Cx * axX + Cz * axZ;
                float a = aD * along + aC * across;
                float m = sw * a + (fl * u1h + push) * along;
                dx = m * axX; dz = m * axZ;
                dy = 0.5f * sw * a * a;                                           // the end of a swinging length rises a little
                return;
            }

            float rx = x - topX, ry = y - topY, rz = z - topZ;
            float dh = (float)Math.Sqrt(rx * rx + rz * rz), dl = (float)Math.Sqrt(rx * rx + ry * ry + rz * rz);
            float px = x - footX, py = y - footY, pz = z - footZ;
            float tx = topX - footX, ty = topY - footY, tz = topZ - footZ;
            float wC, wF;
            if (kind == Palm)
            {   // the game's palm weights (OZPalmWind); a clump of palms has several crowns and takes the leafy weights below
                wC = Sat(Smooth(-0.35f * sc, 0.05f * sc, ry) + Smooth(0.5f * sc, 0.9f * sc, dh) * Smooth(-1.15f * sc, -0.85f * sc, ry));
                wF = Smooth(0.7f * sc, Math.Max(R, 1.0f * sc), dl) * wC; wF *= wF;
            }
            else
            {   // leafy trees and small plants: by the crown's own size; the trunk axis runs from the foot to the pivot
                float u = Sat(py / Math.Max(ty, 0.1f * sc));
                float ax = px - u * tx, az = pz - u * tz;
                float dax = (float)Math.Sqrt(ax * ax + az * az);
                wC = Sat(Smooth(-0.10f * H, 0.05f * H, ry) + Smooth(0.35f * R, 0.7f * R, dax) * Smooth(-0.45f * H, -0.25f * H, ry));
                wF = Smooth(0.30f * R, R, dl) * wC; wF *= wF;
            }
            // 1. the crown sways about its pivot (a small turn: d = a x r), and leans with the rain wind
            float rW = rx * Wx + rz * Wz, rC = rx * Cx + rz * Cz;
            dx = wC * (aD * (Wx * ry) + aC * (Cx * ry));
            dy = wC * (aD * (-rW) + aC * (-rC));
            dz = wC * (aD * (Wz * ry) + aC * (Cz * ry));
            // 2. the trunk bends about the foot (growing with height squared; the crown rides on the top), and leans in rain
            float f = Sat(py / Math.Max(ty, 0.5f * sc));
            f = f * f * (1f - wC) + wC;
            float bend = kBend * amp * (0.4f + 0.6f * g) * (0.65f + 0.35f * Sin(1.31f * fq * t + 1.7f * ph));
            bend += LeanTrunkDeg[row] * Deg * kA * storm * (0.7f + 0.3f * g);
            float pW = px * Wx + pz * Wz;
            dx += bend * f * (Wx * py);
            dy += bend * f * (-pW);
            dz += bend * f * (Wz * py);
            // 3. leaves and fronds flutter (growing to the tips, each on its own phase), and stream downwind in rain
            float il = 1f / Math.Max(dl, 1e-3f);
            float rdx = rx * il, rdy = ry * il, rdz = rz * il;
            float lp = 1.0f * rdx + 0.8f * rdz - 0.6f * dl / sc;
            float lp2 = kind == Palm ? lp : lp + 1.7f * (0.9f * px + 1.3f * pz + 0.7f * py) / sc;   // leaf clumps of one crown on their own phase
            float u1 = Sin(6.6f * fq * t + 1.5f * lp + ph), u2 = Sin(11.3f * fq * t + 2.6f * lp2 + 2.0f * ph), s1 = Sin(4.7f * fq * t + 1.3f * lp + 1.3f * ph);
            float fa = FlutterM[row] * amp * (0.45f + 0.55f * g) * wF;
            float elx = -rdx * rdy, ely = 1f - rdy * rdy, elz = -rdz * rdy;
            float isd = 1f / Math.Max(dh, 0.5f * sc);
            float sdx = -rz * isd, sdz = rx * isd;
            float rdW = rdx * Wx + rdz * Wz;
            float wpx = Wx - rdx * rdW, wpy = -rdy * rdW, wpz = Wz - rdz * rdW;
            float ke = 0.75f * u1 + 0.25f * u2, ks = 0.55f * s1, kw = 0.25f + 0.2f * u1;
            float kp = wF * PushM[row] * storm * (0.6f + 0.4f * g);
            dx += fa * (elx * ke + sdx * ks + wpx * kw) + kp * wpx;
            dy += fa * (ely * ke + wpy * kw) + kp * wpy;
            dz += fa * (elz * ke + sdz * ks + wpz * kw) + kp * wpz;
            // 4. the ground guard: a leaf near the ground never goes into it
            float floor = -GroundGuard * Math.Max(py, 0f);
            if (dy < floor) dy = floor;
        }

        // ------------------------------------------------------------------ the shape of one plant
        static float Percentile(List<float> v, float q)
        {   // linear between the two nearest ranks (as the preparation measured)
            if (v.Count == 0) return 0f;
            v.Sort();
            float pos = q * 0.01f * (v.Count - 1); int i = (int)Math.Floor(pos); int j = Math.Min(i + 1, v.Count - 1);
            return v[i] + (v[j] - v[i]) * (pos - i);
        }

        /// <summary>
        /// The shape of a plant from its points: xyz = n points (x, up, z), origin at the foot, metres at its natural size.
        /// Slices of 0.25 m: the trunk radius is the middle one of the slices between 10 % and 30 % of the height; the crown
        /// starts at the first slice above a quarter of the height that is wider than 0.8 m and 2.5 trunk radii (60 % of the
        /// height if none is); the pivot stands over the middle of the crown, for a palm at the height where most points
        /// crowd round that middle (the bud), for everything else at the crown's start; the crown radius holds 95 % of the
        /// crown's points.
        /// </summary>
        public static Shape Measure(float[] xyz, int n, int kind)
        {
            var s = new Shape { Kind = kind };
            if (n <= 0) return s;
            float H = float.MinValue, lowest = float.MaxValue;
            for (int i = 0; i < n; i++) { float y = xyz[3 * i + 1]; if (y > H) H = y; if (y < lowest) lowest = y; }
            if (kind == Hang)
            {   // origin at the top: the length below it, the half width
                float len = Math.Max(0.1f, -lowest); var hw = new List<float>(n);
                for (int i = 0; i < n; i++) hw.Add((float)Math.Sqrt(xyz[3 * i] * xyz[3 * i] + xyz[3 * i + 2] * xyz[3 * i + 2]));
                s.H = len; s.R = Math.Max(0.15f, Percentile(hw, 95f)); s.Px = s.Py = s.Pz = 0f; s.CrownBase = 0f;
                return s;
            }
            if (H <= 0.05f) { s.H = 0.05f; s.R = 0.15f; return s; }
            int ns = (int)Math.Ceiling(H / 0.25f - 1e-6f); if (ns < 1) ns = 1;
            var rad = new float[ns];
            var bx = new List<float>[ns]; var bz = new List<float>[ns];
            for (int i = 0; i < ns; i++) { bx[i] = new List<float>(); bz[i] = new List<float>(); }
            for (int i = 0; i < n; i++)
            {
                float y = xyz[3 * i + 1]; if (y < 0f) continue;
                int k = (int)Math.Floor(y / 0.25f); if (k >= ns) continue;       // the top point itself lies on the last edge: not in a slice (as the reference)
                bx[k].Add(xyz[3 * i]); bz[k].Add(xyz[3 * i + 2]);
            }
            var tmp = new List<float>();
            for (int k = 0; k < ns; k++)
            {
                if (bx[k].Count < 3) continue;
                var mx = new List<float>(bx[k]); var mz = new List<float>(bz[k]);
                float cx = Percentile(mx, 50f), cz = Percentile(mz, 50f);
                tmp.Clear();
                for (int i = 0; i < bx[k].Count; i++) { float ex = bx[k][i] - cx, ez = bz[k][i] - cz; tmp.Add((float)Math.Sqrt(ex * ex + ez * ez)); }
                rad[k] = Percentile(tmp, 90f);
            }
            var lo = new List<float>();
            for (int k = 0; k < ns; k++) { float z0 = k * 0.25f; if (z0 >= 0.10f * H && z0 < 0.30f * H && rad[k] > 0f) lo.Add(rad[k]); }
            float rt = lo.Count > 0 ? Percentile(lo, 50f) : 0.3f;
            float zc = 0.6f * H;
            for (int k = 0; k < ns; k++) { float z0 = k * 0.25f; if (z0 >= 0.25f * H && rad[k] > Math.Max(0.8f, 2.5f * rt)) { zc = z0; break; } }
            var crx = new List<float>(); var crz = new List<float>(); var cry = new List<float>();
            for (int i = 0; i < n; i++) if (xyz[3 * i + 1] >= zc) { crx.Add(xyz[3 * i]); cry.Add(xyz[3 * i + 1]); crz.Add(xyz[3 * i + 2]); }
            if (crx.Count == 0) { s.H = H; s.R = 0.15f; s.Py = zc; s.CrownBase = zc; s.TrunkR = rt; return s; }
            float mcx = Percentile(new List<float>(crx), 50f), mcz = Percentile(new List<float>(crz), 50f);
            float zp = zc;
            if (kind == Palm)
            {
                var near = new List<float>();
                for (int i = 0; i < crx.Count; i++) { float ex = crx[i] - mcx, ez = crz[i] - mcz; if (Math.Sqrt(ex * ex + ez * ez) < 0.45) near.Add(cry[i]); }
                if (near.Count > 10)
                {   // bins of 0.2 m from the crown's start up to the top; the fullest one (the first of equals)
                    int nb = 0; while (zc + (nb + 1) * 0.2f < H + 0.2f - 1e-6f) nb++;
                    if (nb < 1) nb = 1;
                    var hist = new int[nb];
                    float topEdge = zc + nb * 0.2f;
                    foreach (float yv in near)
                    {
                        if (yv < zc || yv > topEdge) continue;
                        int b = (int)Math.Floor((yv - zc) / 0.2f); if (b >= nb) b = nb - 1;   // the last bin holds its upper edge
                        hist[b]++;
                    }
                    int best = 0; for (int b = 1; b < nb; b++) if (hist[b] > hist[best]) best = b;
                    zp = zc + best * 0.2f + 0.1f;
                }
                else zp = 0.86f * H;
            }
            tmp.Clear();
            for (int i = 0; i < crx.Count; i++) { float ex = crx[i] - mcx, ez = crz[i] - mcz; tmp.Add((float)Math.Sqrt(ex * ex + ez * ez)); }
            s.Px = mcx; s.Py = zp; s.Pz = mcz; s.R = Math.Max(0.15f, Percentile(tmp, 95f)); s.H = H; s.TrunkR = rt; s.CrownBase = zc;
            return s;
        }

        // ------------------------------------------------------------------ which plant is what (by the name of its mesh)
        /// <summary>Kind of a level 16 plant shape by a part of its mesh name (the plant kit names them FL_...), and its
        /// natural height in metres (0 = not a plant that moves). Order matters: the first part found wins.</summary>
        static readonly (string part, int kind, float height)[] Table =
        {
            ("_stump", Still, 0f), ("_top", Still, 0f),                                              // the two halves of a felled tree (OzTreeFall cuts FL_x into FL_x_stump and FL_x_top): first, so the tree's own row below never takes them
            ("LilyPads", Still, 0f), ("LogFallen", Still, 0f), ("RockMossy", Still, 0f), ("Mound", Still, 0f), ("Flowers", Still, 0f),
            ("Stump", Still, 0f), ("Break", Still, 0f), ("BREAK", Still, 0f),                         // what is left of a felled tree, if another step names it so
            ("VineCurtain", Hang, 4.60f),
            ("PalmYoung", Small, 2.12f), ("PalmClump", Clump, 6.83f), ("PalmFan", Palm, 5.21f), ("PalmShortBushy", Palm, 4.31f),
            ("PalmSlender", Palm, 10.46f), ("PalmTallLean", Palm, 7.79f),
            ("TreeDead", Bare, 0f),
            ("TreeButtress", Tree, 9.00f), ("TreeCanopyA", Tree, 7.50f), ("TreeCanopyB", Tree, 6.60f), ("TreeCanopyC", Tree, 0f),
            ("TreeEmergent", Tree, 12.63f), ("TreeKapokA", Tree, 13.00f), ("TreeKapokB", Tree, 11.18f), ("TreeSwampMossA", Tree, 8.00f),
            ("TreeSwampMossB", Tree, 7.04f), ("TreeUmbrella", Tree, 0f), ("TreeVineA", Tree, 9.40f), ("TreeVineB", Tree, 8.24f),
            ("Mangrove", Tree, 0f),
            ("BananaPlant", Small, 2.96f), ("BushBroadleaf", Small, 0f), ("FernCluster", Small, 0f), ("Reeds", Small, 0f),
        };

        /// <summary>The kind of a plant by its mesh name; Still (0) for a name the table does not know (it then stands still
        /// and is counted as unknown).</summary>
        public static int KindOf(string meshName, out float naturalHeight, out bool known)
        {
            naturalHeight = 0f; known = false;
            if (string.IsNullOrEmpty(meshName)) return Still;
            foreach (var row in Table)
                if (meshName.IndexOf(row.part, StringComparison.Ordinal) >= 0) { naturalHeight = row.height; known = true; return row.kind; }
            return Still;
        }

        public static string KindName(int kind) => kind == Palm ? "palm" : kind == Tree ? "tree" : kind == Small ? "small" : kind == Bare ? "bare" : kind == Hang ? "hanging" : kind == Clump ? "palm clump" : "still";
    }
}
