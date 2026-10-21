using System;
using System.Collections.Generic;
using System.Text;
#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;
#endif

namespace OpenZ.EditorTools
{
#if !OPENZ_HARNESS
    /// <summary>
    /// The K256e menus of builder E_env_audio_ui (the shuttle's third round): the Play proof E on level 16, the Play proof P on any level
    /// (the testing panel; run it on level 16 and on level 2), and the edit-mode counts (<see cref="OzShuttleEnvCounts"/>: the trees'
    /// downwash bend in plain numbers, the voices' rule; the same counts also run outside the editor). Output: OzShuttleEnvProof.Folder().
    /// </summary>
    public static class OzShuttleEnvMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256e Env Proof E (Level 16, Play)")]
        static void ProofE()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256e env proof: enter Play on level 16 first"); return; }
            OzShuttleEnvProof.Run("E");
            Debug.Log("[OpenZ] K256e env proof E started; its report: OzShuttleEnvProof.Report, the logs in " + OzShuttleEnvProof.Folder());
        }

        [MenuItem("OpenZ/V1.1/Shuttle: K256e Env Proof P (testing panel, any level, Play)")]
        static void ProofP()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256e env proof P: enter Play first (level 16, then level 2)"); return; }
            OzShuttleEnvProof.Run("P");
            Debug.Log("[OpenZ] K256e env proof P started; its report: OzShuttleEnvProof.Report");
        }

        [MenuItem("OpenZ/V1.1/Shuttle: K256e Env Counts (edit mode)")]
        static void Counts()
        {
            var text = OzShuttleEnvCounts.Run(out int fails, out int passes, null);
            Debug.Log("[OpenZ] K256e env counts: " + (fails == 0 ? "PASS" : "FAIL " + fails) + " (" + passes + " PASS)\n" + text);
            try { var dir = OzShuttleEnvProof.Folder(); System.IO.Directory.CreateDirectory(dir); System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "env_counts_edit_mode.txt"), text); }
            catch (Exception ex) { Debug.LogWarning("[OpenZ] K256e counts: " + ex.Message); }
        }
    }
#endif

    /// <summary>
    /// The counted rules of builder E_env_audio_ui (pure numbers, no scene): (1) the trees' downwash bend (OzFloraWindMath.DownwashBend and
    /// its envelope BendSpring, the twin of the plant shader): with the bend's envelope 0 every plant moves exactly as before, bit for bit;
    /// every plant type's top moves at least 0.8 m (at least 15 px at the 70 m camera, measured along the view, its shortest way) within
    /// 7 m of the jet axis whatever the level's wind; the lean's angle; the envelope eases in about 0.3 s and springs back with one
    /// overshoot within about 1.3 s; the flutter 2.2-2.8 Hz; the largest move within the drawing boxes' room. (2) The voices
    /// (OzShuttleVoiceGate): the round-2 log's cases replayed at 60 frames a second: no Rachel line over a shuttle line, a shuttle line
    /// asked while she speaks said after her, dropped after 3 s. With PLAIN_ONLY only the plain numbers (the original math, for the
    /// bit-for-bit compare outside the editor).
    /// </summary>
    public static class OzShuttleEnvCounts
    {
        static StringBuilder sb; static int fails, passes;
        static void Check(string what, bool ok) { if (ok) passes++; else fails++; sb.AppendLine((ok ? "PASS  " : "FAIL  ") + what); }
        static void Note(string what) => sb.AppendLine("      " + what);
        static string R(float v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        struct Plant { public string Label; public int Kind; public float Height, PivotY, Radius; }
        /// <summary>The level 16 plant shapes (their heights as the plant kit has them; pivot and crown radius as the game measures them).</summary>
        static readonly Plant[] Plants =
        {
            new Plant { Label = "tall leaning palm", Kind = OzFloraWindMath.Palm, Height = 7.79f, PivotY = 5.85f, Radius = 2.10f },
            new Plant { Label = "kapok tree", Kind = OzFloraWindMath.Tree, Height = 13.0f, PivotY = 8.25f, Radius = 2.40f },
            new Plant { Label = "swamp tree", Kind = OzFloraWindMath.Tree, Height = 8.0f, PivotY = 4.80f, Radius = 2.15f },
            new Plant { Label = "short bushy palm", Kind = OzFloraWindMath.Palm, Height = 4.31f, PivotY = 2.69f, Radius = 2.29f },
            new Plant { Label = "young palm", Kind = OzFloraWindMath.Small, Height = 2.12f, PivotY = 1.27f, Radius = 1.60f },
            new Plant { Label = "banana plant", Kind = OzFloraWindMath.Small, Height = 2.96f, PivotY = 1.80f, Radius = 1.42f },
            new Plant { Label = "broadleaf bush", Kind = OzFloraWindMath.Small, Height = 1.37f, PivotY = 0.70f, Radius = 1.50f },
            new Plant { Label = "reeds", Kind = OzFloraWindMath.Small, Height = 0.99f, PivotY = 0.55f, Radius = 0.90f },
            new Plant { Label = "fern cluster", Kind = OzFloraWindMath.Small, Height = 0.74f, PivotY = 0.40f, Radius = 1.20f },
            new Plant { Label = "dead tree", Kind = OzFloraWindMath.Bare, Height = 8.30f, PivotY = 5.0f, Radius = 2.0f },
            new Plant { Label = "palm clump", Kind = OzFloraWindMath.Clump, Height = 6.83f, PivotY = 4.3f, Radius = 3.0f },
            new Plant { Label = "hanging vine", Kind = OzFloraWindMath.Hang, Height = 4.60f, PivotY = 0f, Radius = 1.6f },
        };
        const float WindX = 0.9700247f, WindZ = -0.2430062f;
        /// <summary>Pixels a metre at the standard RTS camera (70 m, 32 deg view, 55 deg down, 1600 px high), across the view and along it.</summary>
        public const float PxPerMAcross = 32.67f, PxPerMAlong = 26.76f;

        static OzFloraWindMath.Wind Calm(float t) => new OzFloraWindMath.Wind { Wx = WindX, Wz = WindZ, S = OzFloraWindMath.CalmStrength, Storm = 0f, T = t, Gust = OzFloraWindMath.CalmGust(t) };

        /// <summary>The top of a plant (or a hanging plant's lowest end) moved: x, y, z.</summary>
        static void Tip(Plant p, OzFloraWindMath.Wind w, float footX, out float dx, out float dy, out float dz)
        {
            float ty = p.Kind == OzFloraWindMath.Hang ? -p.Height : p.Height;
            OzFloraWindMath.Displace(p.Kind, w, footX, 0f, 0f, footX, p.PivotY, 0f, 1f, p.Radius, p.Height, 1f, footX, ty, 0f, out dx, out dy, out dz, 1f, 0f);   // a hanging plant: its plane along the push (across it, the rock takes the push)
        }

        public static string Run(out int failCount, out int passCount, System.IO.TextWriter plain)
        {
            sb = new StringBuilder(); fails = passes = 0;
            sb.AppendLine("K256e E_env_audio_ui: the counts (the trees' downwash bend in plain numbers, the voices' rule)");
            // ---- 1. the plain numbers: calm, and with the K256b downwash mix 5 m off at strength 1.6 (the bit-for-bit compare outside)
            foreach (var p in Plants)
                for (int k = 0; k < 50; k++)
                {
                    float t = 20f + 0.2f * k; var w = Calm(t);
                    Tip(p, w, 0f, out float ax, out float ay, out float az);
                    var wd = w; wd.DwX = -5f; wd.DwZ = 0f; wd.DwR = 13f; wd.DwK = 1.6f; wd.DwT = t;
                    Tip(p, wd, 0f, out float bx, out float by, out float bz);
                    plain?.WriteLine($"plain {p.Label} {k} {R(ax)} {R(ay)} {R(az)} {R(bx)} {R(by)} {R(bz)}");
                }
#if !PLAIN_ONLY
            // ---- 2. the bend's envelope 0 (its ground point set): exactly the plain numbers
            {
                bool same = true; int n = 0;
                foreach (var p in Plants)
                    for (int k = 0; k < 50; k++)
                    {
                        float t = 20f + 0.2f * k; var w = Calm(t); var w0 = w; w0.DbX = -5f; w0.DbZ = 0f; w0.DbE = 0f; w0.DbF = 0f;
                        Tip(p, w, 0f, out float ax, out float ay, out float az); Tip(p, w0, 0f, out float bx, out float by, out float bz);
                        same &= ax == bx && ay == by && az == bz; n++;
                    }
                Check($"1 the bend's envelope 0: every plant moves exactly as without it ({n} samples, bit for bit)", same);
            }
            // ---- 3. every plant type: its top's move by the bend, in calm wind and with no level wind at all
            {
                bool all = true; var tbl = new StringBuilder();
                foreach (var p in Plants)
                {
                    float best = 0f, at10 = 0f, ang = 0f; bool still = true;
                    foreach (float d in new[] { 4f, 7f, 10f, 12.6f })
                    {
                        float mv = 0f;
                        for (int k = 0; k < 20; k++)
                        {
                            float t = 20f + 0.05f * k; var w = Calm(t);
                            var wb = w; wb.DbX = -d; wb.DbZ = 0f; wb.DbE = 1f; wb.DbF = 0f; wb.DwT = t;
                            Tip(p, w, 0f, out float ax, out float ay, out float az); Tip(p, wb, 0f, out float bx, out float by, out float bz);
                            float h = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)); mv = Math.Max(mv, h);
                        }
                        if (d <= 7f) best = Math.Max(best, mv); if (d == 10f) at10 = mv; if (d == 12.6f) still &= mv < 1e-4f;
                    }
                    { var w = Calm(20f); w.S = 0f; var wb = w; wb.DbX = -5f; wb.DbE = 1f; Tip(p, wb, 0f, out float cx, out float cy, out float cz); ang = (float)(Math.Atan2(Math.Abs(cx), p.Height) * 180.0 / Math.PI); }
                    float px = best * PxPerMAlong;
                    bool ok = (best >= 0.8f || px >= 15f) && still;
                    all &= ok;
                    tbl.AppendLine($"      {p.Label,-18} {p.Height,5:F2} m: top moved {best:F2} m within 7 m ({px:F0} px along the view, {best * PxPerMAcross:F0} across), {at10:F2} m at 10 m, none past 12.5 m {still}; with no level wind the top leans {ang:F1} deg{(ok ? "" : "  <- SHORT")}");
                }
                sb.Append(tbl.ToString());
                Check("2 every tree and plant type: its top moves at least 0.8 m (or 15 px along the 70 m view) within 7 m of the jet axis, less further out, nothing past 12.5 m", all);
            }
            {   // the lean of a kapok and a small tree at 6 / 8 / 10 m with no level wind at all
                var k = Plants[1]; var sw = Plants[2];
                float A(Plant p, float d) { var w = Calm(20f); w.S = 0f; var wb = w; wb.DbX = -d; wb.DbE = 1f; Tip(p, wb, 0f, out float x, out float y, out float z); return (float)(Math.Atan2(Math.Abs(x), p.Height) * 180.0 / Math.PI); }
                float k6 = A(k, 6f), k8 = A(k, 8f), k10 = A(k, 10f), s6 = A(sw, 6f), s8 = A(sw, 8f), s10 = A(sw, 10f);
                Note($"the lean (the top seen from the foot) with no level wind: kapok {k6:F1} deg at 6 m, {k8:F1} at 8 m, {k10:F1} at 10 m; swamp tree {s6:F1} / {s8:F1} / {s10:F1} deg");
                Check("3 the crowns lean away 10-15 deg within 6-8 m of the jet axis and 5-8 deg at 10 m (a kapok, a swamp tree)", k6 >= 10f && k6 <= 15f && s6 >= 10f && s6 <= 15f && k8 >= 10f && s8 <= 15f && k10 >= 5f && k10 <= 8f && s10 >= 5f && s10 <= 8f);
            }
            // ---- 4. the envelope: eased in, springing back once
            {
                float e = 0f, v = 0f, fl = 0f, last = 0f; int mode = 0; const float Dt = 1f / 60f;
                float tUp = -1f, t = 0f;
                for (int i = 0; i < 90; i++) { OzFloraWindMath.BendSpring(ref e, ref v, ref fl, ref mode, ref last, OzFloraWindMath.BendTarget(1.6f), Dt); t += Dt; if (tUp < 0f && e >= 0.85f) tUp = t; }
                float peak = e; float lo = 0f, tLo = 0f, settle = -1f; int overs = 0; float prev = e, pv = 0f; t = 0f;
                for (int i = 0; i < 180; i++)
                {
                    OzFloraWindMath.BendSpring(ref e, ref v, ref fl, ref mode, ref last, OzFloraWindMath.BendTarget(0f), Dt); t += Dt;
                    if (e < lo) { lo = e; tLo = t; }
                    float dv = e - prev; if (i > 0 && ((pv < 0f && dv >= 0f && e < -0.03f) || (pv > 0f && dv <= 0f && e > 0.03f))) overs++;   // a turn of the swing beyond 3 %
                    pv = dv; prev = e; if (settle < 0f && t > tLo && tLo > 0f && Math.Abs(e) < 0.05f && Math.Abs(v) < 0.3f) settle = t;
                }
                Note($"the envelope: 0.85 after {tUp:F2} s (full {peak:F2}); the spring-back: down to {lo:F2} after {tLo:F2} s, settled (within 0.05) after {settle:F2} s, overshoots beyond 3 % {overs}");
                Check("4 the bend eases in (0.85 within 0.35 s) and springs back with ONE overshoot (-0.1 .. -0.3) settled within 1.5 s", tUp > 0f && tUp <= 0.35f && lo <= -0.1f && lo >= -0.3f && settle > 0f && settle <= 1.5f && overs == 1);
                // the height: full while the pads are under 16 m, none from 20 m
                float H(float feet) { float r, k; FloraForShip(feet, out r, out k); return OzFloraWindMath.BendTarget(k); }
                Note($"the wanted bend by the pads' height: 6 m {H(6f):F2}, 12 m {H(12f):F2}, 16 m {H(16f):F2}, 18 m {H(18f):F2}, 20 m {H(20f):F2}, 24 m {H(24f):F2}");
                Check("5 the bend is full while the pads are under 16 m (the hull under 18 m), gone from 20 m", H(6f) >= 0.999f && H(12f) >= 0.999f && H(16f) >= 0.99f && H(20f) <= 0.06f && H(24f) == 0f);
            }
            // ---- 6. the flutter
            {
                var p = Plants[1]; float last = 0f; int ups = 0; float hi = 0f;
                for (int i = 0; i < 300; i++)
                {
                    float t = i / 100f; var w = Calm(20f); w.S = 0f; var wb = w; wb.DbX = -5f; wb.DbE = 0f; wb.DbF = 1f; wb.DwT = t;
                    Tip(p, wb, 0f, out float x, out float y, out float z); float a = (float)(Math.Atan2(x, p.Height) * 180.0 / Math.PI);
                    if (last < 0f && a >= 0f) ups++; last = a; hi = Math.Max(hi, Math.Abs(a));
                }
                Note($"the flutter of a kapok alone: {ups / 3f:F2} Hz, up to {hi:F1} deg at its top (the curve: 2 x {OzFloraWindMath.WashFlutterDeg:F0} deg at the trunk)");
                Check("6 a 2-3 Hz flutter of about 3 deg", ups / 3f >= 2f && ups / 3f <= 3f && hi >= 2.5f && hi <= 7f);
            }
            // ---- 7. the drawing boxes have room for the largest move (trees and palms 5.0 m, small plants 2.4 m)
            {
                float big = 0f, small = 0f;
                foreach (var p in Plants)
                {
                    var w = Calm(20f); w.S = 2.1f; w.Storm = 1f; var wb = w; wb.DbX = -3f; wb.DbE = 1.05f; wb.DbF = 1f; wb.DwX = -3f; wb.DwR = 13f; wb.DwK = 1.6f;
                    float mv = 0f; for (int k = 0; k < 40; k++) { wb.T = 20f + 0.1f * k; wb.DwT = wb.T; Tip(p, wb, 0f, out float x, out float y, out float z); mv = Math.Max(mv, (float)Math.Sqrt(x * x + y * y + z * z)); }
                    if (p.Kind == OzFloraWindMath.Small) small = Math.Max(small, mv); else big = Math.Max(big, mv);
                }
                Note($"the largest move of a top in rain wind, the downwash and its bend together: trees and palms {big:F2} m, small plants {small:F2} m (room 5.0 / 2.4 m)");
                Check("7 the drawing boxes (OzFloraWind.boundsPad 5.0 / 2.4 m) have room for the largest move", big <= 5.0f && small <= 2.4f);
            }
            // ---- 8. the voices
            Voices();
#endif
            sb.AppendLine($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS)");
            failCount = fails; passCount = passes;
            return sb.ToString();
        }

        /// <summary>OzFloraDownwash.ForShip as plain numbers (the shuttle's radius and strength by its pads' height).</summary>
        static void FloraForShip(float feetM, out float radius, out float k)
        {
            float u = Math.Max(0f, Math.Min(1f, (feetM - 6f) / 6f)); radius = 13f + (9f - 13f) * u;
            k = feetM <= 6f ? 1.6f : feetM <= 14f ? 1.6f + (0.9f - 1.6f) * (feetM - 6f) / 8f : 0.9f * (1f - Math.Max(0f, Math.Min(1f, (feetM - 14f) / 10f)));
        }

#if !PLAIN_ONLY
        /// <summary>A replay of two voice channels at 60 frames a second under OzShuttleVoiceGate: Alan's shuttle lines (asked at a time,
        /// with a length) and Rachel's lines (queued at a time, with a length); the spans they played.</summary>
        sealed class Replay
        {
            public readonly OzShuttleVoiceGate G = new OzShuttleVoiceGate();
            public readonly List<(float a, float b)> Alan = new List<(float, float)>(), Rachel = new List<(float, float)>();
            public int Dropped;
            public void Run(List<(float at, float len)> alan, List<(float at, float len)> rachel, float until)
            {
                const float Dt = 1f / 60f; float aEnd = -1f, rEnd = -1f; float heldLen = 0f;
                var rq = new List<(float at, float len)>(rachel); var aq = new List<(float at, float len)>(alan);
                for (float t = 0f; t < until; t += Dt)
                {
                    bool aOn = t < aEnd, rOn = t < rEnd;
                    G.Tick(t, aOn, rOn);
                    // Alan asked now
                    for (int i = aq.Count - 1; i >= 0; i--)
                        if (aq[i].at <= t)
                        {
                            if (G.ShuttleMustWait(t)) { G.Hold("vo_shuttle_landed", null, t); heldLen = aq[i].len; }
                            else if (!aOn) { Alan.Add((t, t + aq[i].len)); aEnd = t + aq[i].len; aOn = true; G.Tick(t, true, rOn); }
                            aq.RemoveAt(i);
                        }
                    int r = G.Poll(t, out _, out _);
                    if (r == 1 && !aOn) { Alan.Add((t, t + heldLen)); aEnd = t + heldLen; aOn = true; G.Tick(t, true, rOn); }
                    if (r == 2) Dropped++;
                    // Rachel's queue
                    if (!rOn && rq.Count > 0 && rq[0].at <= t && G.RachelMayStart(t)) { Rachel.Add((t, t + rq[0].len)); rEnd = t + rq[0].len; rq.RemoveAt(0); G.Tick(t, aOn, true); }
                }
            }
            public int Overlaps(out float worst)
            {
                int n = 0; worst = 0f;
                foreach (var a in Alan) foreach (var r in Rachel) { float o = Math.Min(a.b, r.b) - Math.Max(a.a, r.a); if (o > 1e-3f) { n++; worst = Math.Max(worst, o); } }
                return n;
            }
        }

        static void Voices()
        {
            // a. the round-2 case at 157.22: "Nice landing!" (1.61 s) and "New vehicle rolling out" asked 1.37 s into it (it cut in 0.24 s)
            var a = new Replay(); a.Run(new List<(float, float)> { (0f, 1.61f) }, new List<(float, float)> { (1.37f, 2.0f) }, 8f);
            int na = a.Overlaps(out float wa);
            Note($"a. Alan's landing line (1.61 s) with Rachel's line asked 1.37 s into it: Rachel starts at {(a.Rachel.Count > 0 ? a.Rachel[0].a : -1f):F2} s (Alan ends 1.61 s), overlaps {na}");
            Check("8a Rachel's line waits while the shuttle line plays + 0.3 s (the round-2 157.22 case: 0 overlaps, she starts at about 1.91 s)", na == 0 && a.Rachel.Count == 1 && a.Rachel[0].a >= 1.9f && a.Rachel[0].a <= 1.95f);
            // b. "Can't land here!" asked while Rachel speaks (the 68.53 and 321.73 cases)
            var b = new Replay(); b.Run(new List<(float, float)> { (0.5f, 1.2f) }, new List<(float, float)> { (0f, 2.0f) }, 8f);
            int nb = b.Overlaps(out float wb);
            Note($"b. Rachel speaking (2.0 s), \"Can't land here!\" asked at 0.5 s: said at {(b.Alan.Count > 0 ? b.Alan[0].a : -1f):F2} s, overlaps {nb}, held {b.G.Held}, said after her {b.G.HeldPlayed}");
            Check("8b a shuttle line asked while Rachel speaks is held and said after her + 0.3 s (0 overlaps, never ducked under her)", nb == 0 && b.Alan.Count == 1 && b.Alan[0].a >= 2.29f && b.Alan[0].a <= 2.35f);
            // c. held longer than 3 s: dropped
            var c = new Replay(); c.Run(new List<(float, float)> { (0.2f, 1.2f) }, new List<(float, float)> { (0f, 4.5f) }, 9f);
            Note($"c. Rachel speaking 4.5 s, a shuttle line asked at 0.2 s: played {c.Alan.Count}, dropped {c.Dropped}");
            Check("8c a shuttle line held longer than 3 s is dropped (no longer to the point)", c.Alan.Count == 0 && c.Dropped == 1);
            // d. a busy minute: four landings, Rachel's line after each, a factory line and an error line in between
            var d = new Replay();
            d.Run(new List<(float, float)> { (0f, 1.6f), (12f, 1.6f), (24.3f, 1.4f), (36f, 1.6f), (41f, 1.2f) },
                  new List<(float, float)> { (0.4f, 2.4f), (12.1f, 2.4f), (23.5f, 2.2f), (36.0f, 2.0f), (41.2f, 1.8f) }, 60f);
            int nd = d.Overlaps(out float wd);
            Note($"d. a busy minute (5 shuttle lines, 5 Rachel lines, asked together): Alan said {d.Alan.Count}, Rachel {d.Rachel.Count}, overlaps {nd} (worst {wd:F2} s), held {d.G.Held}, dropped {d.Dropped}");
            Check("8d a busy minute: every line said, 0 overlaps", nd == 0 && d.Alan.Count == 5 && d.Rachel.Count == 5);
        }
#endif
    }
}
