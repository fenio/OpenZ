using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The K256e Play proof of the shuttle's dust, jets and parked wisps (rule DV118, round 3: the downwash as one soft rolling ring, the
    /// lift-off jets, the night dust, the parked ship's wisps). Menus (this file, editor only): OpenZ/V1.1/Shuttle: K256e Dust + Jets
    /// Proof D (level 16, Play) and D2 (level 2, Play: a shuttle is placed on open sand first). Counted PASS / FAIL lines, an uncapped
    /// sound log (flushed every 5 s), full-screen pictures from the normal RTS camera at the standard 70 m, the camera FIXED for each
    /// sequence (never following the ship), the ship in the middle of the frame and clear of the unit panel; every picture also as a
    /// 1:1 crop round the dust (named _crop, never the proof on its own). Set-up: the CPU teams removed, morning (day clock 0.10, held).
    /// <list type="bullet">
    /// <item>E1 the take-off by day on the level's own ground: the frame before, then +0.5, +1.0, +1.6 and +2.5 s; on each: the puffs
    /// alive, the strongest, the dust's median luminance on screen against the same pixels before the take-off and against the ground
    /// ring round the dust; at +1.0 s the puffs per side of the ship (north / east / south / west) and the share hidden behind the hull
    /// from the camera; the jets while the pads are under 3 m (the flame's metres under the mouth, the halo, the ground pool by day);
    /// the HIGH peak; every puff's life; the dust gone 2.5 s after the jets stopped raising it.</item>
    /// <item>E2 the touchdown 60 m on (the camera moved there first): +1.0 and +2.5 s, the dust gone by 2.5 s.</item>
    /// <item>E3 the same take-off with the LOW share: its peak at most 0.55 x HIGH.</item>
    /// <item>E4 the crash landing's burst (strength 2.2, thrown on the parked ship by its own downwash): the same soft puffs, gone by 2.5 s.</item>
    /// <item>E5 the parked ship's wisps (OzShuttleExhaust): beads born, the most opacity used (at most 0.2), the wisp's stretch.</item>
    /// <item>E6 at night (day clock 0.65): the take-off, its luminance against the ground: the dust at least 10 of 255 lighter.</item>
    /// <item>E7 the fort blast's ground fog (OzBlastFog.Skirt, its own code, on open ground) at the same zoom: the reference picture for
    /// the side-by-side.</item>
    /// </list>
    /// Output folder: <see cref="Folder"/> (built at run time from the project's place). Test only.
    /// </summary>
    public sealed class OzShuttleDownwashProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string OutDir = "";
        public static string Folder()
        {
            string sub = string.IsNullOrEmpty(OutDir) ? "shuttle_round3_dust_proof" : OutDir;
            if (System.IO.Path.IsPathRooted(sub)) return sub;
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", sub);
            return System.IO.Path.Combine(Application.persistentDataPath, sub);
        }

        public static void Run(string mode)
        {
            Report = "running " + mode;
            var go = new GameObject("OzShuttleDownwashProof"); var p = go.AddComponent<OzShuttleDownwashProof>(); p.mode = mode;
        }

#if UNITY_EDITOR
        internal static class Menu
        {
            [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K256e Dust + Jets Proof D (Level 16, day + night, Play)")]
            static void L16() => Go("D");
            [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K256e Dust + Jets Proof D2 (Level 2 sand, Play)")]
            static void L02() => Go("D2");
            static void Go(string m)
            {
                if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256e dust proof: enter Play first (level 16 for D, level 2 for D2)"); return; }
                var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
                OutDir = ""; Run(m);
                Debug.Log("[OpenZ] K256e dust proof " + m + " started; its report: OzShuttleDownwashProof.Report, the output in " + Folder());
            }
        }
#endif

        /// <summary>One measured picture.</summary>
        sealed class Meas { public int alive, alive05, dustPx, edgePx; public float maxA, dust = -1f, before = -1f, ring = -1f; }
        /// <summary>A puff's outline on screen: a luma step of at least this (of 255) over 2 px inside the dust that the same pixels before
        /// the take-off did not have; at most this share of the dust's pixels may have one (the off-engine twin: round 2 0.3-0.9 %, round 3
        /// 0.00-0.01 %).</summary>
        public static float EdgeStep = 6f, EdgeShareMax = 0.002f;

        string mode = "D", lvl = "L16", dir;
        ZGame g; ZWorld W; RtsCamera rts; DayNight dn; Camera cam; ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, pictures;
        float soundFlushAt, soundT0; string soundPath;
        Color32[] beforePx; int beforeW, beforeH;
        readonly List<Vector3> pos = new List<Vector3>(); readonly List<float> size = new List<float>(), alpha = new List<float>(), life = new List<float>();
        // per-frame tracking
        bool trackLow; float coreMaxM, haloMaxM, lifeMax; int lowFrames, poolDayFrames, lowDayFrames, peak; float lastRaiseAt = -1f;
        int burstsSeen = -1, burstsAfterTakeOff; float lastBurstAt = -1f;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>(); cam = Camera.main;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            soundT0 = soundFlushAt = Time.unscaledTime; soundPath = System.IO.Path.Combine(dir, "sound_log_" + mode + ".txt");
            try { System.IO.File.WriteAllText(soundPath, ""); } catch { }
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleDownwashProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator WaitReal(float sec) { float t0 = Time.unscaledTime; while (Time.unscaledTime - t0 < sec) yield return null; }
        IEnumerator Until(float at) { while (Time.unscaledTime < at) yield return null; }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP"))) n++; return n; }

        void FlushSounds(bool all = false)
        {
            if (!all && Time.unscaledTime < soundFlushAt + 5f) return;
            var es = OzSoundLog.Since(soundFlushAt); var now = Time.unscaledTime;
            var keep = new List<OzSoundLog.Entry>(); foreach (var e in es) if (e.t < now) keep.Add(e);
            try { System.IO.File.AppendAllText(soundPath, OzSoundLog.Format(keep)); } catch { }
            soundFlushAt = now;
        }

        void Update()
        {
            FlushSounds();
            if (OzShuttleDownwash.Bursts != burstsSeen) { if (burstsSeen >= 0) lastBurstAt = Time.unscaledTime; burstsSeen = OzShuttleDownwash.Bursts; }
            if (!ship || s == null) return;
            var jets = ship.Jets; if (jets == null) return;
            if (ship.Wash != null) peak = Mathf.Max(peak, ship.Wash.Alive);
            float k = OzShuttleDownwash.K(ship.FeetHeight) * Mathf.Clamp01(jets.Throttle / 0.7f);
            if (k > 0.001f) lastRaiseAt = Time.unscaledTime;
            if (trackLow && ship.FeetHeight < OzShuttleJets.LowBelowM && jets.Throttle > 0.05f)
            {
                lowFrames++; coreMaxM = Mathf.Max(coreMaxM, OzShuttleJets.LastCoreM); haloMaxM = Mathf.Max(haloMaxM, OzShuttleJets.LastHaloM);
                if (NightGrade.NightK < 0.1f) { lowDayFrames++; if (OzShuttleJets.LastPoolOn) poolDayFrames++; }
            }
        }

        // ------------------------------------------------------------------ places and orders
        int[] Pad(int cx, int cy, int r, float angDeg, int span)
        {
            for (int dr = 0; dr <= 240; dr += 16)
                for (int k = 0; k <= span; k += 10)
                    foreach (int sg in new[] { 1, -1 })
                    {
                        double a = (angDeg + sg * k) * System.Math.PI / 180.0;
                        var q = W.ShuttleClamp(cx + (int)(System.Math.Cos(a) * (r + dr)), cy + (int)(System.Math.Sin(a) * (r + dr)));
                        if (W.ShuttlePadFree(q.x, q.y, s, out _) && SoftAt(q.x, q.y) >= 0.5f) return new[] { q.x, q.y };
                    }
            return null;
        }
        static float SoftAt(int px, int py)
        {
            var tm = TrackMarks.I; if (!tm || tm.Surface == null) return 1f;
            var w = ZGame.PxToWorld(px, py); return tm.Surface.DustStrength(w.x, w.z);
        }
        bool Move(int x, int y) => W.SendWaypoints(s.Owner, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        Vector3 Ground(int px, int py) { var w = ZGame.PxToWorld(px, py); w.y = g.GroundY(w.x, w.z); return w; }
        Vector3 ShipGround() { var p = ship.transform.position; p.y = g.GroundY(p.x, p.z); return p; }
        IEnumerator Focus(Vector3 w)
        {
            if (rts) { rts.FreeLookOff(); rts.focus = new Vector3(w.x, 0f, w.z); rts.SetHeightImmediate(70f); }
            yield return WaitReal(0.5f);
        }

        ZObject PlaceShip()
        {   // level 2 has no shuttle of its own: one on open sand near the camera, the way the testing panel drops it
            var team = ZWorld.ShuttleTeam == Team.Neutral ? Team.Red : ZWorld.ShuttleTeam;
            var c = rts ? rts.focus : Vector3.zero; int cx = Mathf.RoundToInt(c.x * 8f), cy = Mathf.RoundToInt(-c.z * 8f);
            for (int r = 120; r <= 900; r += 48)
                for (int a = 0; a < 360; a += 30)
                {
                    var q = W.ShuttleClamp(cx + (int)(Mathf.Cos(a * Mathf.Deg2Rad) * r), cy + (int)(Mathf.Sin(a * Mathf.Deg2Rad) * r));
                    if (SoftAt(q.x, q.y) < 0.8f) continue;
                    var o = W.SpawnShuttle(q.x, q.y, team, rts ? rts.yaw - 90f : -90f, out string why);
                    if (o == null) continue;
                    OzPlayableShuttle.SpawnView(g, o);
                    Log($"placed a {team} shuttle #{o.Id} on open ground at px {o.CenterX},{o.CenterY} (dust strength {SoftAt(o.CenterX, o.CenterY):F2})");
                    return o;
                }
            return null;
        }

        // ------------------------------------------------------------------ pictures and measures
        IEnumerator Shot(string name, Vector3 at, bool measure, Meas m, bool keepAsBefore = false, int zoom = 1)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (!tex) { Log("no picture " + name); yield break; }
            string file = lvl + "_" + name;
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, file + ".png"), tex.EncodeToPNG()); pictures++; } catch (System.Exception e) { Log("picture " + file + ": " + e.Message); }
            Crop(tex, at, file + "_crop", zoom);
            if (keepAsBefore) { beforePx = tex.GetPixels32(); beforeW = tex.width; beforeH = tex.height; }
            if (measure && m != null) Measure(tex, at, m);
            Log($"picture {file}.png ({tex.width}x{tex.height}){(m != null && measure ? $": puffs {m.alive} (alpha >= 0.05: {m.alive05}, strongest {m.maxA:F2}), dust luma {m.dust:F0} on {m.dustPx} px, the same pixels before {m.before:F0}, the ground ring {m.ring:F0}; new hard edges in the dust {m.edgePx} px ({(m.dustPx > 0 ? 100f * m.edgePx / m.dustPx : 0f):F2} %)" : "")}");
            Destroy(tex);
        }

        void Crop(Texture2D tex, Vector3 at, string file, int zoom)
        {
            float sx = tex.width / (float)Mathf.Max(1, cam.pixelWidth), sy = tex.height / (float)Mathf.Max(1, cam.pixelHeight);
            var sp = cam.WorldToScreenPoint(at); int w = Mathf.Min(tex.width, 960 / zoom), h = Mathf.Min(tex.height, 600 / zoom);
            int x0 = Mathf.Clamp(Mathf.RoundToInt(sp.x * sx) - w / 2, 0, tex.width - w), y0 = Mathf.Clamp(Mathf.RoundToInt(sp.y * sy) - h / 2, 0, tex.height - h);
            var src = tex.GetPixels(x0, y0, w, h);
            var o = new Texture2D(w * zoom, h * zoom, TextureFormat.RGBA32, false);
            if (zoom == 1) o.SetPixels(src);
            else
            {
                var dst = new Color[w * zoom * h * zoom];
                for (int y = 0; y < h * zoom; y++) for (int x = 0; x < w * zoom; x++) dst[y * w * zoom + x] = src[(y / zoom) * w + x / zoom];
                o.SetPixels(dst);
            }
            o.Apply(false);
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, file + ".png"), o.EncodeToPNG()); } catch { }
            Destroy(o);
        }

        /// <summary>The ship's drawn hull on screen (its SH_ parts' bounds), grown by margin, in texture pixels.</summary>
        Rect HullRect(float sx, float sy, float margin)
        {
            Vector2 mn = new Vector2(1e9f, 1e9f), mx = new Vector2(-1e9f, -1e9f);
            foreach (var r in ship.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.name.StartsWith("SH_")) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var sp = cam.WorldToScreenPoint(c); if (sp.z < 0f) continue;
                    mn = Vector2.Min(mn, new Vector2(sp.x * sx, sp.y * sy)); mx = Vector2.Max(mx, new Vector2(sp.x * sx, sp.y * sy));
                }
            }
            var rr = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
            return new Rect(rr.x - rr.width * margin, rr.y - rr.height * margin, rr.width * (1f + 2f * margin), rr.height * (1f + 2f * margin));
        }

        /// <summary>The ground under the ship's sun shadow on screen (the hull's bounds cast along the main light onto the ground), grown by margin.</summary>
        Rect ShadowRect(float sx, float sy, float margin)
        {
            var sun = RenderSettings.sun; if (!sun) { foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None)) if (l.type == LightType.Directional && l.isActiveAndEnabled) { sun = l; break; } }
            if (!sun || sun.transform.forward.y > -0.05f) return new Rect(0, 0, 0, 0);
            var f = sun.transform.forward; Vector2 mn = new Vector2(1e9f, 1e9f), mx = new Vector2(-1e9f, -1e9f); bool any = false;
            foreach (var r in ship.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || !r.gameObject.name.StartsWith("SH_")) continue;
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    float gy = g.GroundY(c.x, c.z), t = (gy - c.y) / f.y; var hit = c + f * t;
                    var sp = cam.WorldToScreenPoint(hit); if (sp.z < 0f) continue;
                    mn = Vector2.Min(mn, new Vector2(sp.x * sx, sp.y * sy)); mx = Vector2.Max(mx, new Vector2(sp.x * sx, sp.y * sy)); any = true;
                }
            }
            if (!any) return new Rect(0, 0, 0, 0);
            var rr = Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
            return new Rect(rr.x - rr.width * margin, rr.y - rr.height * margin, rr.width * (1f + 2f * margin), rr.height * (1f + 2f * margin));
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        static float Step2(Color32[] px, int w, int h, int x, int y)
        {
            if (x < 1 || y < 1 || x >= w - 1 || y >= h - 1) return 0f;
            float gx = Mathf.Abs(Luma(px[y * w + x + 1]) - Luma(px[y * w + x - 1])), gy = Mathf.Abs(Luma(px[(y + 1) * w + x]) - Luma(px[(y - 1) * w + x]));
            return Mathf.Max(gx, gy);
        }
        static float Median(int[] hist)
        {
            int n = 0; foreach (var h in hist) n += h; if (n == 0) return -1f;
            int half = n / 2, acc = 0; for (int i = 0; i < hist.Length; i++) { acc += hist[i]; if (acc > half) return i; }
            return hist.Length - 1;
        }

        void Measure(Texture2D tex, Vector3 at, Meas m)
        {
            int w = tex.width, h = tex.height; float sx = w / (float)Mathf.Max(1, cam.pixelWidth), sy = h / (float)Mathf.Max(1, cam.pixelHeight);
            var px = tex.GetPixels32();
            ship.Wash.Snapshot(pos, size, alpha, life);
            var hull = HullRect(sx, sy, 0.08f);
            var mask = new bool[w * h]; var outR = new List<float>();
            var gsp = cam.WorldToScreenPoint(at); float gx = gsp.x * sx, gy = gsp.y * sy;
            float tanH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            m.alive = pos.Count; m.alive05 = 0; m.maxA = 0f;
            for (int i = 0; i < pos.Count; i++)
            {
                if (alpha[i] >= 0.05f) m.alive05++;
                m.maxA = Mathf.Max(m.maxA, alpha[i]); lifeMax = Mathf.Max(lifeMax, life[i]);
                if (alpha[i] < 0.12f) continue;
                var sp = cam.WorldToScreenPoint(pos[i]); if (sp.z <= 0.1f) continue;
                float rpx = size[i] * 0.5f * cam.pixelHeight / (2f * sp.z * tanH) * sy, cx = sp.x * sx, cy = sp.y * sy, rc = rpx * 0.4f;
                outR.Add(Mathf.Sqrt((cx - gx) * (cx - gx) + (cy - gy) * (cy - gy)) + rpx);
                int x0 = Mathf.Max(0, (int)(cx - rc)), x1 = Mathf.Min(w - 1, (int)(cx + rc)), y0 = Mathf.Max(0, (int)(cy - rc)), y1 = Mathf.Min(h - 1, (int)(cy + rc));
                for (int y = y0; y <= y1; y++) for (int x = x0; x <= x1; x++)
                    if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= rc * rc && !hull.Contains(new Vector2(x, y))) mask[y * w + x] = true;
            }
            var hd = new int[256]; var hb = new int[256]; var hr = new int[256]; bool haveBefore = beforePx != null && beforeW == w && beforeH == h;
            for (int i = 0; i < mask.Length; i++) if (mask[i]) { hd[Mathf.Clamp((int)Luma(px[i]), 0, 255)]++; m.dustPx++; if (haveBefore) hb[Mathf.Clamp((int)Luma(beforePx[i]), 0, 255)]++; }
            if (outR.Count > 0)
            {
                outR.Sort(); float R = outR[Mathf.Clamp((int)(outR.Count * 0.9f), 0, outR.Count - 1)], r0 = R * 1.1f, r1 = R * 1.4f;
                int x0 = Mathf.Max(0, (int)(gx - r1)), x1 = Mathf.Min(w - 1, (int)(gx + r1)), y0 = Mathf.Max(0, (int)(gy - r1)), y1 = Mathf.Min(h - 1, (int)(gy + r1));
                for (int y = y0; y <= y1; y += 2) for (int x = x0; x <= x1; x += 2)
                {
                    float d2 = (x - gx) * (x - gx) + (y - gy) * (y - gy);
                    if (d2 < r0 * r0 || d2 > r1 * r1 || hull.Contains(new Vector2(x, y)) || mask[y * w + x]) continue;
                    hr[Mathf.Clamp((int)Luma(px[y * w + x]), 0, 255)]++;
                }
            }
            m.dust = Median(hd); m.before = haveBefore ? Median(hb) : -1f; m.ring = Median(hr);
            if (haveBefore)
            {   // a puff's outline: a hard step in the dust that the ground before did not have (the hull and its sun shadow left out: they moved)
                var shade = ShadowRect(sx, sy, 0.1f); var hullBig = HullRect(sx, sy, 0.2f); m.edgePx = 0;
                for (int y = 1; y < h - 1; y++) for (int x = 1; x < w - 1; x++)
                {
                    int i = y * w + x; if (!mask[i]) continue;
                    var q = new Vector2(x, y); if (hullBig.Contains(q) || shade.Contains(q)) continue;
                    if (Step2(px, w, h, x, y) >= EdgeStep && Step2(px, w, h, x, y) - Step2(beforePx, w, h, x, y) >= EdgeStep) m.edgePx++;
                }
            }
        }

        /// <summary>The puffs per side of the ship's middle and the share whose middle the hull hides from the camera.</summary>
        string Sectors(Vector3 mid)
        {
            ship.Wash.Snapshot(pos, size, alpha, null);
            var bounds = new List<Bounds>(); foreach (var r in ship.GetComponentsInChildren<Renderer>()) if (r.enabled && r.gameObject.name.StartsWith("SH_")) bounds.Add(r.bounds);
            var n = new int[4]; var hid = new int[4]; var a = new float[4]; var cp = cam.transform.position;
            for (int i = 0; i < pos.Count; i++)
            {
                var d = pos[i] - mid; float ang = Mathf.Atan2(d.z, d.x) * Mathf.Rad2Deg;                 // 0 east, 90 north
                int sct = ang >= 45f && ang < 135f ? 0 : (ang >= -45f && ang < 45f ? 1 : (ang >= -135f && ang < -45f ? 2 : 3));   // N E S W
                n[sct]++; a[sct] += alpha[i];
                var ray = new Ray(pos[i], (cp - pos[i]).normalized); float far = Vector3.Distance(cp, pos[i]);
                foreach (var b in bounds) if (b.IntersectRay(ray, out float t) && t < far) { hid[sct]++; break; }
            }
            var o = new StringBuilder(); string[] nm = { "north", "east", "south", "west" };
            for (int k = 0; k < 4; k++) o.Append($"{nm[k]} {n[k]} (mean alpha {(n[k] > 0 ? a[k] / n[k] : 0f):F2}, hidden by the hull {(n[k] > 0 ? 100f * hid[k] / n[k] : 0f):F0} %)  ");
            return o.ToString();
        }

        // ------------------------------------------------------------------ the take-off series
        IEnumerator TakeOff(string tag, int[] to, bool measure, List<Meas> got)
        {
            var at = ShipGround();
            yield return Focus(at);
            yield return Shot(tag + "_before_h70", at, false, null, true);
            int b0 = OzShuttleDownwash.Bursts; peak = 0; trackLow = true;
            if (!Move(to[0], to[1])) { Check($"{tag}: the move order is taken", false); yield break; }
            yield return WaitFor(() => OzShuttleDownwash.Bursts > b0, 12f);
            if (OzShuttleDownwash.Bursts <= b0) { Check($"{tag}: it took off", false); yield break; }
            float t0 = Time.unscaledTime; burstsAfterTakeOff = OzShuttleDownwash.Bursts;
            foreach (float dt in new[] { 0.5f, 1.0f, 1.6f, 2.5f })
            {
                yield return Until(t0 + dt);
                var m = new Meas();
                yield return Shot($"{tag}_{dt:0.0}s_h70", at, measure, m);
                got.Add(m);
                if (dt == 1.0f) Log($"{tag} +1.0 s, the puffs round the ship's middle: {Sectors(at)}");
            }
            yield return WaitFor(() => ship.FeetHeight > 12f || Time.unscaledTime > t0 + 4f, 6f);
            trackLow = false;
            yield return WaitFor(() => Time.unscaledTime > lastRaiseAt + OzShuttleDownwash.GoneBySec, 8f);
            ship.Wash.Snapshot(pos, size, alpha, life); int left = 0; foreach (var x in alpha) if (x >= 0.05f) left++;
            Log($"{tag}: the jets stopped raising dust at the take-off; {OzShuttleDownwash.GoneBySec:F1} s later puffs with alpha >= 0.05: {left} (alive {pos.Count}); peak alive {peak}");
            Check($"{tag}: the take-off's dust is gone 2.5 s after the jets stopped raising it (0 puffs at alpha >= 0.05)", left == 0);
        }

        IEnumerator Touchdown(string tag, int[] to)
        {
            var at = Ground(to[0], to[1]);
            yield return Focus(at);
            int b0 = burstsAfterTakeOff;
            if (OzShuttleDownwash.Bursts > b0) Log($"{tag}: the touchdown came before the camera was there ({Time.unscaledTime - lastBurstAt:F1} s ago)");
            yield return WaitFor(() => OzShuttleDownwash.Bursts > b0, 60f);
            if (OzShuttleDownwash.Bursts <= b0) { Check($"{tag}: it landed", false); yield break; }
            yield return null;
            float t0 = lastBurstAt > 0f ? lastBurstAt : Time.unscaledTime;
            yield return Until(t0 + 1.0f); yield return Shot(tag + "_touchdown_1.0s_h70", at, false, null);
            yield return Until(t0 + OzShuttleDownwash.GoneBySec);
            yield return new WaitForEndOfFrame();
            ship.Wash.Snapshot(pos, size, alpha, life); int left = 0; foreach (var x in alpha) if (x >= 0.05f) left++;
            yield return Shot(tag + "_touchdown_2.5s_h70", at, false, null);
            Log($"{tag}: touchdown + {OzShuttleDownwash.GoneBySec:F1} s: puffs with alpha >= 0.05: {left} (alive {pos.Count}); pressed patches {OzShuttleDownwash.Patches}, left out on a deck {OzShuttleDownwash.PatchesSkipped}");
            Check($"{tag}: the touchdown's dust is gone by 2.5 s (0 puffs at alpha >= 0.05), nothing left lying", left == 0);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 20f);
            yield return WaitReal(1.0f);
        }

        // ------------------------------------------------------------------ the take
        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null) { Log("no game"); fails++; Finish(); yield break; }
            lvl = mode == "D2" ? "L02" : "L16";
            Log($"K256e dust + jets proof {mode}, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, quality {OzQuality.Name}");
            int bots = W.Bots.Count; W.Bots.Clear();
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            yield return WaitReal(1.0f);
            s = W.PlayableShuttle; ship = s != null ? OzPlayableShuttle.Of(s) : null;
            if (ship == null) { s = PlaceShip(); ship = s != null ? OzPlayableShuttle.Of(s) : null; yield return WaitReal(1.5f); }
            if (s == null || ship == null || ship.Wash == null) { Check("a drawn shuttle with its downwash", false); Finish(); yield break; }
            Log($"set-up: CPU removed {bots}, day 0.10 held, camera 70 m fixed per sequence; ship #{s.Id} at px {s.CenterX},{s.CenterY}; fog material {(ship.Wash.OwnSystem ? "own copy" : "MISSING (the old puffs)")}");
            Check("the downwash draws with its own soft fog system (no fallback puffs)", ship.Wash.OwnSystem);
            var c0 = ShipGround(); var raw = TrackMarks.I && TrackMarks.I.Surface != null ? TrackMarks.I.Surface.DustColor(c0.x, c0.z) : OpenZFxCatalog.M12Dust.color;
            var dc = OzShuttleDownwash.GroundColour(c0, out float st);
            Log($"the ground at the pad: colour {(Color32)raw}, dust strength {st:F2}; its dust colour {(Color32)dc}");

            // E5 the parked ship's wisps first (it stands still now)
            var ex = ship.GetComponent<OzShuttleExhaust>();
            if (ex)
            {
                int b0 = OzShuttleExhaust.Beads; OzShuttleExhaust.AlphaUsedMax = 0f;
                yield return Focus(c0); yield return WaitReal(3.5f);
                yield return Shot("e5_parked_wisps_h70", c0, false, null, false, 3);
                Log($"E5 wisps: beads born in 3.5 s {OzShuttleExhaust.Beads - b0} ({ex.Emitted} wisps since the start, mode {ex.Mode}, held '{ex.Held}'), the most opacity a bead had {OzShuttleExhaust.AlphaUsedMax:F2}, the wisp's height over its width {OzShuttleExhaust.StretchEst:F2}");
                Check("E5 the parked ship's wisps are on, thin and faint (beads born, opacity <= 0.20) and stretched upward (height >= 1.8 x width)",
                      OzShuttleExhaust.Beads > b0 && OzShuttleExhaust.AlphaUsedMax <= 0.2f && OzShuttleExhaust.StretchEst >= 1.8f);
            }
            else Check("E5 the parked ship has its exhaust wisp component", false);

            // E1 the take-off by day, E2 its touchdown 60 m on
            var day = new List<Meas>(); float t0 = Time.unscaledTime; coreMaxM = haloMaxM = 0f; lowFrames = poolDayFrames = lowDayFrames = 0;
            var to = Pad(s.CenterX, s.CenterY, 480, 0f, 180);
            if (to == null) { Check("E1 open ground 60 m on to land", false); Finish(); yield break; }
            yield return TakeOff("e1_day_takeoff", to, true, day);
            int peakHigh = peak;
            Log($"E1 the jets while the pads were under {OzShuttleJets.LowBelowM:F0} m: {lowFrames} frames, the flame under each mouth up to {coreMaxM:F2} m, the halo up to {haloMaxM:F2} m, the ground pool on by day in {poolDayFrames} of {lowDayFrames} frames");
            Check("E1 the jets at the lift-off: >= 1.2 m of flame under each mouth and the ground pool lit by day under 3 m", coreMaxM >= 1.2f && lowDayFrames > 0 && poolDayFrames >= lowDayFrames * 0.8f);
            Check($"E1 the downwash at the take-off: HIGH peak >= {OzShuttleDownwash.ProofPeakMin} puffs alive ({peakHigh})", peakHigh >= OzShuttleDownwash.ProofPeakMin);
            bool dayOk = true, rimOk = true; foreach (var m in day) { if (m.dustPx > 200 && m.dust >= 0f && m.dust + 0.5f < Mathf.Max(m.before, m.ring)) dayOk = false; if (m.dustPx > 200 && m.edgePx > EdgeShareMax * m.dustPx) rimOk = false; }
            Check($"E1 no puff with a visible rim: new hard edges in the dust at most {100f * EdgeShareMax:F1} % of its pixels in every picture", rimOk);
            Check("E1 by day the dust is never darker than the ground under it and round it (median luma, every picture with dust)", dayOk);
            Log($"E1 the light: the environment's raise {OzShuttleDownwash.LastUEnv:F2}, the material's {OzShuttleDownwash.LastU:F2} (the shader's own {OzShuttleDownwash.LastFogLum:F3}, the ground's {OzShuttleDownwash.LastGroundLum:F3}, the floor {OzShuttleDownwash.LastFloor:F3})");
            Log($"gust sounds in E1: {Played(t0, "shuttle_downwash_gust")}, take-off {Played(t0, "shuttle_takeoff")}");
            Check("E1 the burst has its gust sound", Played(t0, "shuttle_downwash_gust") > 0);
            yield return Touchdown("e2_day", to);

            // E3 the same take-off with the LOW share, back to where it stood
            var back = Pad(s.CenterX, s.CenterY, 360, 180f, 120);
            if (back != null)
            {
                OzShuttleDownwash.ShareOverride = 0.5f; OzShuttleJets.ShareOverride = 0.5f;
                var low = new List<Meas>();
                yield return TakeOff("e3_low_takeoff", back, false, low);
                int peakLow = peak;
                OzShuttleDownwash.ShareOverride = -1f; OzShuttleJets.ShareOverride = -1f;
                Log($"E3 LOW share: peak alive {peakLow} against HIGH {peakHigh} ({(peakHigh > 0 ? (float)peakLow / peakHigh : 0f):F2} x)");
                Check("E3 LOW: the downwash's peak <= 0.55 x HIGH", peakHigh > 0 && peakLow <= 0.55f * peakHigh);
                yield return Touchdown("e3_low", back);
            }
            else Check("E3 open ground to fly back to", false);

            // E4 the crash landing's burst on the parked ship (its own downwash, strength 2.2)
            {
                var at = ShipGround(); yield return Focus(at); yield return WaitFor(() => ship.Wash.Alive == 0, 4f);
                float t4 = Time.unscaledTime, g4 = Time.time; int p0 = OzShuttleDownwash.Puffs; ship.Wash.Burst(2.2f, 0f);   // the jets are off on the parked ship
                foreach (float dt in new[] { 0.5f, 1.0f, 1.6f })
                { while (Time.time < g4 + dt) yield return null; yield return Shot($"e4_crash_burst_{dt:0.0}s_h70", at, false, null); }   // run 27D: game time (a picture's long frame is not game time)
                while (Time.time < g4 + OzShuttleDownwash.GoneBySec) yield return null; yield return new WaitForEndOfFrame();
                ship.Wash.Snapshot(pos, size, alpha, life); int left = 0; foreach (var x in alpha) if (x >= 0.05f) left++;
                yield return Shot("e4_crash_burst_2.5s_h70", at, false, null);
                Log($"E4 the crash burst: {OzShuttleDownwash.Puffs - p0} puffs (haze {OzShuttleDownwash.HazeEmitted} in all), {OzShuttleDownwash.GoneBySec:F1} s later at alpha >= 0.05: {left}; gust {Played(t4, "shuttle_downwash_gust")}");
                Check("E4 the crash landing's burst is the same soft puffs, gone by 2.5 s, with its gust", OzShuttleDownwash.Puffs - p0 >= 20 && left == 0 && Played(t4, "shuttle_downwash_gust") > 0);
            }

            // E6 at night
            if (dn)
            {
                dn.timeOfDay = 0.65f; dn.Apply(0.65f); yield return WaitReal(3.0f);
                var night = new List<Meas>();
                var nto = Pad(s.CenterX, s.CenterY, 480, 90f, 180);
                if (nto != null)
                {
                    yield return TakeOff("e6_night_takeoff", nto, true, night);
                    bool ok = night.Count > 0; int seen = 0;
                    bool rimN = true;
                    foreach (var m in night) { if (m.dustPx < 200 || m.dust < 0f) continue; seen++; if (m.dust < Mathf.Max(m.before, m.ring) + 10f) ok = false; if (m.edgePx > EdgeShareMax * m.dustPx) rimN = false; }
                    Check($"E6 no puff with a visible rim at night (new hard edges at most {100f * EdgeShareMax:F1} % of the dust)", rimN);
                    Log($"E6 night: NightK {NightGrade.NightK:F2}, the environment's raise {OzShuttleDownwash.LastUEnv:F2}, the material's {OzShuttleDownwash.LastU:F2} (the shader's own {OzShuttleDownwash.LastFogLum:F3}, the ground's {OzShuttleDownwash.LastGroundLum:F3}, the floor {OzShuttleDownwash.LastFloor:F3}), pictures with dust {seen}");
                    Check("E6 at night the dust reads light: its median luma at least the ground's + 10 (before and round it), every picture with dust", ok && seen > 0);
                    yield return Touchdown("e6_night", nto);
                }
                else Check("E6 open ground to fly to at night", false);
                dn.timeOfDay = 0.10f; dn.Apply(0.10f); yield return WaitReal(1.0f);
            }

            // E7 the fort blast's ground fog at the same zoom (the reference for the side-by-side)
            {
                var ref0 = Pad(s.CenterX, s.CenterY, 520, 270f, 180);
                var at = ref0 != null ? Ground(ref0[0], ref0[1]) : ShipGround() + new Vector3(-50f, 0f, 0f);
                yield return Focus(at);
                float t7 = Time.unscaledTime; int rings = 0;
                for (int i = 0; i < 6; i++) { if (OzBlastFog.Skirt(at, 4f + 4f * i, i)) rings++; yield return WaitReal(0.3f); }
                foreach (float dt in new[] { 1.0f, 1.6f, 2.5f }) { yield return Until(t7 + dt); yield return Shot($"e7_fort_blast_fog_ref_{dt:0.0}s_h70", at, false, null); }
                Log($"E7 the fort blast's ground fog drawn by its own code: {rings} skirt rings ({OzBlastFog.Status(cam)})");
            }

            Log($"every puff's life at most {lifeMax:F2} s (no puff outlives {OzShuttleDownwash.GoneBySec - 0.1f:F1} s)");
            Check("every puff dies by 2.4 s (nothing lies on the ground)", lifeMax <= OzShuttleDownwash.GoneBySec - 0.1f + 1e-3f);
            Finish();
        }

        void Finish()
        {
            FlushSounds(true);
            Log($"RESULT {mode}: {passes} PASS, {fails} FAIL, {pictures} pictures");
            Report = $"done {mode}: {passes} PASS, {fails} FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log_" + mode + ".txt"), sb.ToString()); System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "done_" + mode + ".txt"), Report); } catch { }
            Destroy(gameObject, 0.5f);
        }
    }
}
