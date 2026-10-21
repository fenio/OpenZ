using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>K425 counted check, out of Play (menu OpenZ/V1.1/Shuttle: K425 Crew Fly Up Check (no Play), Editor/OzCrewFlyUpMenu.cs): the
    /// shot-down shuttle's crew bodies are blown up first (View/OzCrewFlyPlan.cs). Counts the kicks of 4,000 bodies (ship ids 1-400, ten
    /// bodies each, their throws' seeds as BoardingView.ShuttleCrewLost makes them) thrown on the ground, hovering, cruising, climbing and
    /// from high up: the ranges, the rise, the flight time, the landing near the pieces, every body different, the same kill the same way.</summary>
    public static class OzCrewFlyUpCheck
    {
        struct Case { public string name; public float h, ship, up; public bool keep; public Case(string n, float hh, float s, float u, bool k) { name = n; h = hh; ship = s; up = u; keep = k; } }
        static readonly Case[] Cases =
        {
            new Case("ground 1.0 m", 1.0f, 0f, 0f, true), new Case("ground 2.0 m", 2.0f, 0f, 0f, true), new Case("hover 8 m", 8f, 0f, 0f, true),
            new Case("hover 12 m", 12f, 0f, 0f, true), new Case("hover 16 m", 16f, 0f, 0f, true), new Case("cruise 12 m at 5.3 m/s", 12f, 5.3f, 0f, true),
            new Case("climbing 6 m at 2 m/s and +3 m/s", 6f, 2f, 3f, true), new Case("sinking 10 m at -3 m/s", 10f, 0f, -3f, true),
            new Case("fast 12 m at 8 m/s", 12f, 8f, 0f, false), new Case("high 40 m", 40f, 0f, 0f, true), new Case("high 60 m", 60f, 0f, 0f, true),
        };

        /// <summary>The check's own limits (not the knobs): the longest flight (s; the ragdoll simulates 6 s) and the farthest landing from the
        /// throw's start (m; the Play proofs want the bodies within 16 m of the pieces' middle: 9 m + a seat 2.5 m off the middle + a slide).</summary>
        public const float FlightLimitSec = 4.2f, ReachLimitM = 9f;

        /// <summary>BoardingView.ShuttleCrewLost's seed of body n of ship id.</summary>
        public static uint Seed(int id, int n) => unchecked((uint)(id * 73856093) ^ (uint)((n + 1) * 19349663) ^ 0x9e3779b9u);

        /// <summary>Body k's throw turned k x 36 degrees from the ship's way: the ship's speed along and across it.</summary>
        static OzCrewFlyPlan.Plan Make(Case c, int id, int k, float share)
        {
            float t = k * 36f * Mathf.Deg2Rad;
            return OzCrewFlyPlan.Make(Seed(id, k), c.h, c.ship * Mathf.Cos(t), c.ship * Mathf.Sin(t), c.up, share);
        }

        static string V(bool pass, string what) => (pass ? "PASS  " : "FAIL  ") + what;
        static string F(float x) => x.ToString("F2", CultureInfo.InvariantCulture);

        public static string Check()
        {
            var sb = new StringBuilder(); int pass = 0, fail = 0;
            void L(bool ok, string what) { sb.AppendLine(V(ok, what)); if (ok) pass++; else fail++; }
            const int Ships = 400, Per = 10; const float PushShare = 0.9f;
            // 1 the ranges
            int bad = 0, n = 0; string firstBad = "-";
            foreach (var c in Cases)
                for (int id = 1; id <= Ships; id++) for (int k = 0; k < Per; k++)
                {
                    var p = Make(c, id, k, PushShare); n++;
                    bool ok = p.up <= OzCrewFlyPlan.Up1 + 1e-4f && (p.up >= OzCrewFlyPlan.Up0 - 1e-4f || p.upCapped)
                              && p.outward <= OzCrewFlyPlan.Out1 + 1e-4f && p.outward >= OzCrewFlyPlan.OutFloor - 1e-4f && (p.outward >= OzCrewFlyPlan.Out0 - 1e-4f || p.outCapped)
                              && p.spin >= OzCrewFlyPlan.Spin0 - 1e-4f && p.spin <= OzCrewFlyPlan.Spin1 + 1e-4f
                              && Mathf.Abs(p.axis.magnitude - 1f) < 1e-3f && Mathf.Abs(p.axis.y) <= OzCrewFlyPlan.AxisTilt + 1e-4f
                              && Mathf.Abs(p.outSet - Mathf.Max(0f, p.outward - PushShare)) < 1e-4f;
                    if (!ok) { bad++; if (firstBad == "-") firstBad = $"{c.name} ship {id} body {k}: up {F(p.up)} out {F(p.outward)} spin {F(p.spin)}"; }
                }
            L(bad == 0, $"every body its kick within the ranges (up {F(OzCrewFlyPlan.Up0)}-{F(OzCrewFlyPlan.Up1)} m/s or lowered, out {F(OzCrewFlyPlan.Out0)}-{F(OzCrewFlyPlan.Out1)} m/s or lowered to >= {F(OzCrewFlyPlan.OutFloor)}, spin {F(OzCrewFlyPlan.Spin0)}-{F(OzCrewFlyPlan.Spin1)} rad/s, a flat-ish axis, the chest's hit counted): {n - bad} of {n} (first off: {firstBad})");
            // 2 up first: hovering / on the ground, every body rises
            var rises = new List<float>(); int lowered = 0;
            foreach (var c in Cases)
            {
                if (c.ship > 0f || c.up != 0f || c.h > 16f) continue;
                for (int id = 1; id <= Ships; id++) for (int k = 0; k < Per; k++) { var p = OzCrewFlyPlan.Make(Seed(id, k), c.h, 0f, 0f, 0f, PushShare); rises.Add(p.apexM); if (p.upCapped) lowered++; }
            }
            rises.Sort();
            float rMin = rises[0], rMed = rises[rises.Count / 2], rMax = rises[rises.Count - 1];
            L(rMin >= 1.8f && rMed >= 3.5f && lowered == 0, $"they fly up first (on the ground and hovering up to 16 m): every body rises over where it was thrown at least 1.8 m: {F(rMin)} / {F(rMed)} / {F(rMax)} m (min / median / max), the up kick lowered {lowered} times (want 0)");
            // 3 against the old soft throw
            float oldG = 0f, oldA = 0f;
            for (int i = 0; i <= 20; i++) { float u = i / 20f; foreach (float pw in new[] { 0.55f, 1f, 1.6f }) { float g = OzCrewFlyPlan.OldUp(false, pw, u) * 1.1f, a = OzCrewFlyPlan.OldUp(true, pw, u) * 1.1f; oldG = Mathf.Max(oldG, g * g / (2f * OzCrewFlyPlan.G)); oldA = Mathf.Max(oldA, a * a / (2f * OzCrewFlyPlan.G)); } }
            L(rMin > 4f * oldG && rMed > oldA, $"no plain drop any more: the old soft throw rose at most {F(oldG)} m on the ground and {F(oldA)} m in the air (strongest hit, x1.1); the kick at least {F(rMin)} m, median {F(rMed)} m");
            // 4 lands in time (the ragdoll simulates 6 s)
            float worstT = 0f; string worstTc = "-";
            foreach (var c in Cases) for (int id = 1; id <= Ships; id++) for (int k = 0; k < Per; k++) { var p = Make(c, id, k, PushShare); if (p.flightSec > worstT) { worstT = p.flightSec; worstTc = c.name; } }
            L(worstT <= FlightLimitSec + 1e-3f && 6f - worstT >= 1.8f, $"every body lands within {F(FlightLimitSec)} s, up to 60 m high (the longest {F(worstT)} s, {worstTc}): at least {F(6f - worstT)} s left to land and settle before the ragdoll's 6 s freeze");
            // 5 lands among the pieces
            float worstFar = 0f; string worstFarC = "-"; float fastFar = 0f; int fastLowered = 0, fastN = 0;
            foreach (var c in Cases)
                for (int id = 1; id <= Ships; id++) for (int k = 0; k < Per; k++)
                {
                    var p = Make(c, id, k, PushShare); float far = p.reachM;
                    if (c.keep) { if (far > worstFar) { worstFar = far; worstFarC = c.name; } }
                    else { fastFar = Mathf.Max(fastFar, far); fastN++; if (p.upForReach) fastLowered++; }
                }
            L(worstFar <= ReachLimitM + 1e-3f, $"every body lands within {F(ReachLimitM)} m of where a piece thrown from its place lands (its kick and the ship's way during its longer flight; ships up to 5.3 m/s, every way round: the farthest {F(worstFar)} m, {worstFarC})");
            sb.AppendLine($"INFO  a fast ship (8 m/s at 12 m): the farthest {F(fastFar)} m; the up kick lowered for the reach {fastLowered} of {fastN} (never under {F(OzCrewFlyPlan.Up0)} m/s)");
            // 6 every body different, the whole range used
            int alike = 0; float uMin = 99f, uMax = 0f, oMin = 99f, oMax = 0f; double s1 = 0, s2 = 0; int m = 0;
            for (int id = 1; id <= Ships; id++)
            {
                var ps = new OzCrewFlyPlan.Plan[Per];
                for (int k = 0; k < Per; k++)
                {
                    ps[k] = OzCrewFlyPlan.Make(Seed(id, k), 12f, 0f, 0f, 0f, PushShare);
                    uMin = Mathf.Min(uMin, ps[k].up); uMax = Mathf.Max(uMax, ps[k].up); oMin = Mathf.Min(oMin, ps[k].out0); oMax = Mathf.Max(oMax, ps[k].out0); s1 += ps[k].up; s2 += ps[k].up * ps[k].up; m++;
                }
                bool same = false;
                for (int a = 0; a < Per && !same; a++) for (int b = a + 1; b < Per; b++)
                    if (Mathf.Abs(ps[a].up - ps[b].up) < 0.05f && Mathf.Abs(ps[a].out0 - ps[b].out0) < 0.05f && Mathf.Abs(ps[a].spin - ps[b].spin) < 0.05f) { same = true; break; }
                if (same) alike++;
            }
            float sd = (float)Math.Sqrt(Math.Max(0.0, s2 / m - (s1 / m) * (s1 / m)));
            L(alike == 0 && uMin < OzCrewFlyPlan.Up0 + 0.2f && uMax > OzCrewFlyPlan.Up1 - 0.2f && oMin < OzCrewFlyPlan.Out0 + 0.2f && oMax > OzCrewFlyPlan.Out1 - 0.2f && sd >= 1.2f,
              $"each body different: ships with two bodies alike (up, out and spin within 0.05) {alike} of {Ships}; the draws use the whole range: up {F(uMin)}-{F(uMax)} m/s (stdev {F(sd)}), out {F(oMin)}-{F(oMax)} m/s");
            // 7 the same kill throws the same way
            int diff = 0;
            for (int id = 1; id <= Ships; id++) for (int k = 0; k < Per; k++)
            {
                var a = OzCrewFlyPlan.Make(Seed(id, k), 9f, 3f, -1f, 1f, PushShare); var b = OzCrewFlyPlan.Make(Seed(id, k), 9f, 3f, -1f, 1f, PushShare);
                if (a.up != b.up || a.outward != b.outward || a.spin != b.spin || a.axis != b.axis || a.flightSec != b.flightSec) diff++;
            }
            L(diff == 0, $"the same kill throws the same way (every kick made twice): differing {diff} of {Ships * Per}");
            // 8 the switch
            L(OzCrewFlyPlan.On, "the switch OzCrewFlyPlan.On is on (off = the old soft throw)");
            sb.AppendLine($"RESULT {(fail == 0 ? "PASS" : "FAIL")} ({pass} PASS, {fail} FAIL)");
            return sb.ToString();
        }

        /// <summary>A table of kicks for the kit's twin (CSV: case, ship id, body, seed, up, out, outSet, spin, axis x y z, flight, apex, reach, drift, flags).</summary>
        public static string Samples()
        {
            var sb = new StringBuilder("case,id,n,seed,up,out,outset,spin,ax,ay,az,flight,apex,reach,drift,upcap,upreach,outcap\n");
            foreach (var c in Cases)
                foreach (int id in new[] { 1, 7, 614, 999 })
                    for (int k = 0; k < 4; k++)
                    {
                        uint s = Seed(id, k); var p = Make(c, id, k, 0.9f);
                        sb.Append(c.name).Append(',').Append(id).Append(',').Append(k).Append(',').Append(s).Append(',')
                          .Append(string.Join(",", R(new[] { p.up, p.outward, p.outSet, p.spin, p.axis.x, p.axis.y, p.axis.z, p.flightSec, p.apexM, p.reachM, p.driftM })))
                          .Append(',').Append(p.upCapped ? 1 : 0).Append(',').Append(p.upForReach ? 1 : 0).Append(',').Append(p.outCapped ? 1 : 0).Append('\n');
                    }
            return sb.ToString();
        }

        static string[] R(float[] a) { var r = new string[a.Length]; for (int i = 0; i < a.Length; i++) r[i] = a[i].ToString("R", CultureInfo.InvariantCulture); return r; }
    }
}
