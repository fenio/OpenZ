using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Play check of the shuttle's ground dust flicker (view only, test only). On the level that is open: the CPU teams removed, the
    /// normal RTS camera FIXED on the pad (never following the ship), game time stepped at exactly 60 frames a second while recording
    /// (Time.captureFramerate; an in-editor capture, not a clip). For each of day / night x 70 m / 35 m one flight: 3 s of take-off frames
    /// from the lift-off burst, and the landing 60 m on from the moment the pads come under 9 m until 1 s after the touchdown (at most 6 s).
    /// K420 (the measure, round 2): every recorded frame is drawn TWICE by the game camera in the same moment, once with the dust and once
    /// with the dust's renderer hidden (A and B: the same ship, jets, lamps, figures and ground; the screen's panels are not in it). Round 1
    /// compared the take-off with a flight BACK from the other pad as B, and its dust area (what got lighter against the frame before the
    /// jets lit) took in the ship's own lights, flames, searchlight pools and walking figures, which B had elsewhere: that, not the dust, was
    /// most of its "dust flicker" (out/strip_dust_region.txt). Its 35 m lines found no dust area at all: the ship's screen box, grown by a
    /// third and joined over its whole path, covered the whole 35 m crop.
    /// <list type="bullet">
    /// <item>The crop covers the same ground at every height (24 x 15 m: 960 x 600 px at 70 m, 1920 x 1200 at 35 m), read into 480 x 300
    /// luma cells (box mean).</item>
    /// <item>The ship is left out frame by frame: its SH_ parts' screen box grown by 15 %, of the frame and its two neighbours.</item>
    /// <item>The dust area = the cells the dust changes by at least <see cref="DustMinStep"/> on average over the record (|A - B| after the
    /// 5 x 5 box blur).</item>
    /// <item>The FLICKER of a frame sequence = the mean over the area of |L(t) - (L(t-1) + L(t+1)) / 2| (L after the 5 x 5 box blur) and
    /// its 99th percentile. The dust's own flicker = A's minus B's (same cells, same frames). PASS: at most <see cref="FlickerMax"/>
    /// (mean) and <see cref="P99Max"/> (99 %). Also logged: the flicker of the dust's own picture D = A - B.</item>
    /// </list>
    /// Pictures: the game's frame 1.0 s after each lift-off (full screen + crop), the A strip of 6 frames, the B strip, and a heat picture
    /// of where the dust's own flicker sits (A's second difference over B's, x16; the ship's cells black). Output:
    /// </summary>
    public sealed class OzDustFlickerCheck : MonoBehaviour
    {
        /// <summary>PASS limits (luma of 255): the dust's own flicker (A - B), mean and 99th percentile; the dust area's least change.</summary>
        public static float FlickerMax = 0.15f, P99Max = 2.0f, DustMinStep = 3f;
        public static string Report = "idle";
        const int CropW = 960, CropH = 600, HW = CropW / 2, HH = CropH / 2;
        const float CropAtM = 70f;

        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "k416_dust_flicker_check");
            return System.IO.Path.Combine(Application.persistentDataPath, "k416_dust_flicker_check");
        }

        public static void Run()
        {
            Report = "running";
            new GameObject("OzDustFlickerCheck").AddComponent<OzDustFlickerCheck>();
        }

#if UNITY_EDITOR
        internal static class Menu
        {
            [UnityEditor.MenuItem("OpenZ/V1.1/Shuttle: K416 Dust Flicker Check (this level, Play: day + night, 70 m + 35 m)")]
            static void Go()
            {
                if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K416 dust flicker check: enter Play first (level 16 or level 2)"); return; }
                var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
                Run();
                Debug.Log("[OpenZ] K416 dust flicker check started; its report: OzDustFlickerCheck.Report, the output in " + Folder());
            }
        }
#endif

        sealed class Rec
        {
            public string name; public float h;
            public readonly List<byte[]> a = new List<byte[]>(), b = new List<byte[]>();
            public readonly List<RectInt> ship = new List<RectInt>();
            public RectInt crop; public int scale = 2;
            public float flickA = -1f, flickB = -1f, p99A = -1f, p99B = -1f, flickD = -1f, p99D = -1f; public int areaPx;
        }

        ZGame g; ZWorld W; RtsCamera rts; DayNight dn; Camera cam; ZObject s; OzPlayableShuttle ship; string dir, lvl;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, pictures, pairs, pairFails;
        RenderTexture rt; Texture2D read;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>(); cam = Camera.main;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void OnDestroy() { if (rt) { rt.Release(); Destroy(rt); } if (read) Destroy(read); Time.captureFramerate = 0; DustDrawn(true); }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzDustFlickerCheck] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator WaitReal(float sec) { float t0 = Time.unscaledTime; while (Time.unscaledTime - t0 < sec) yield return null; }

        // ------------------------------------------------------------------ places (as the K256e dust proof)
        static float SoftAt(int px, int py)
        {
            var tm = TrackMarks.I; if (!tm || tm.Surface == null) return 1f;
            var w = ZGame.PxToWorld(px, py); return tm.Surface.DustStrength(w.x, w.z);
        }
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
        bool Move(int x, int y) => W.SendWaypoints(s.Owner, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        Vector3 Ground(int px, int py) { var w = ZGame.PxToWorld(px, py); w.y = g.GroundY(w.x, w.z); return w; }
        Vector3 ShipGround() { var p = ship.transform.position; p.y = g.GroundY(p.x, p.z); return p; }
        IEnumerator Focus(Vector3 w, float h)
        {
            if (rts) { rts.FreeLookOff(); rts.follow = null; rts.focus = new Vector3(w.x, 0f, w.z); rts.SetHeightImmediate(h); }
            yield return WaitReal(0.6f);
        }
        ZObject PlaceShip()
        {   // a level without a shuttle of its own: one on open ground near the camera, the way the testing panel drops it
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
                    Log($"placed a {team} shuttle #{o.Id} on open ground at px {o.CenterX},{o.CenterY}");
                    return o;
                }
            return null;
        }

        /// <summary>The dust renderer of the ship's downwash on / off (B is the same moment without it).</summary>
        static void DustDrawn(bool on)
        {
            foreach (var r in FindObjectsByType<ParticleSystemRenderer>(FindObjectsSortMode.None))
                if (r && r.gameObject.name == "OZ_FxShuttleDownwash") r.enabled = on;
        }

        // ------------------------------------------------------------------ the double render
        void EnsureTargets()
        {
            int w = Mathf.Max(16, cam.pixelWidth), h = Mathf.Max(16, cam.pixelHeight);
            if (rt && (rt.width != w || rt.height != h)) { rt.Release(); Destroy(rt); rt = null; }
            if (!rt) { rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { name = "K420_DustPair" }; rt.Create(); }
        }

        /// <summary>The game camera drawn into the check's target now; the crop read back as luma cells (box mean of scale x scale, row 0 at the bottom).</summary>
        byte[] Draw(Rec r)
        {
            var req = new UnityEngine.Rendering.Universal.UniversalRenderPipeline.SingleCameraRequest { destination = rt };
            if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(cam, req)) UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(cam, req);
            else { var old = cam.targetTexture; cam.targetTexture = rt; cam.Render(); cam.targetTexture = old; }
            if (!read || read.width != r.crop.width || read.height != r.crop.height) { if (read) Destroy(read); read = new Texture2D(r.crop.width, r.crop.height, TextureFormat.RGBA32, false); }
            var prev = RenderTexture.active; RenderTexture.active = rt;
            read.ReadPixels(new Rect(r.crop.x, r.crop.y, r.crop.width, r.crop.height), 0, 0, false); read.Apply(false);
            RenderTexture.active = prev;
            var px = read.GetPixelData<Color32>(0); int k = r.scale, W0 = r.crop.width; var o = new byte[HW * HH]; float inv = 1f / (k * k);
            for (int y = 0; y < HH; y++)
                for (int x = 0; x < HW; x++)
                {
                    float sum = 0f;
                    for (int yy = 0; yy < k; yy++)
                    {
                        int row = Mathf.Min(y * k + yy, r.crop.height - 1) * W0;
                        for (int xx = 0; xx < k; xx++) { var q = px[row + Mathf.Min(x * k + xx, W0 - 1)]; sum += 0.2126f * q.r + 0.7152f * q.g + 0.0722f * q.b; }
                    }
                    o[y * HW + x] = (byte)Mathf.Clamp(Mathf.RoundToInt(sum * inv), 0, 255);
                }
            return o;
        }

        void CropFor(Rec r, Vector3 at)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt(2f * CropAtM / Mathf.Max(10f, r.h)), 1, 8);                    // 2 at 70 m, 4 at 35 m: the same ground
            int w = Mathf.Min(HW * k, cam.pixelWidth), h = Mathf.Min(HH * k, cam.pixelHeight);
            var sp = cam.WorldToScreenPoint(at);
            r.scale = k;
            r.crop = new RectInt(Mathf.Clamp(Mathf.RoundToInt(sp.x) - w / 2, 0, Mathf.Max(0, cam.pixelWidth - w)), Mathf.Clamp(Mathf.RoundToInt(sp.y) - h / 2, 0, Mathf.Max(0, cam.pixelHeight - h)), w, h);
        }

        /// <summary>The ship's screen box in the record's cells: its SH_ parts' bounds grown by 15 %.</summary>
        RectInt ShipBox(Rec r)
        {
            Vector2 mn = new Vector2(1e9f, 1e9f), mx = new Vector2(-1e9f, -1e9f);
            foreach (var rr in ship.GetComponentsInChildren<Renderer>())
            {
                if (!rr.enabled || !rr.gameObject.name.StartsWith("SH_")) continue;
                var b = rr.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var p = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    var sp = cam.WorldToScreenPoint(p); if (sp.z < 0f) continue;
                    mn = Vector2.Min(mn, new Vector2(sp.x, sp.y)); mx = Vector2.Max(mx, new Vector2(sp.x, sp.y));
                }
            }
            if (mn.x > mx.x) return new RectInt(0, 0, 0, 0);
            float gx = (mx.x - mn.x) * 0.15f, gy = (mx.y - mn.y) * 0.15f; int k = r.scale;
            int x0 = Mathf.FloorToInt((mn.x - gx - r.crop.x) / k), y0 = Mathf.FloorToInt((mn.y - gy - r.crop.y) / k);
            int x1 = Mathf.CeilToInt((mx.x + gx - r.crop.x) / k), y1 = Mathf.CeilToInt((mx.y + gy - r.crop.y) / k);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        IEnumerator Frame(Rec r, Vector3 at, string picture)
        {
            yield return new WaitForEndOfFrame();
            if (r.a.Count == 0) CropFor(r, at);
            if (picture != null)
            {   // the game's own frame (with the screen's panels), full + crop
                var tex = ScreenCapture.CaptureScreenshotAsTexture();
                if (tex)
                {
                    try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, picture + ".png"), tex.EncodeToPNG()); pictures++; } catch { }
                    float sx = tex.width / (float)Mathf.Max(1, cam.pixelWidth), sy = tex.height / (float)Mathf.Max(1, cam.pixelHeight);
                    int cx = Mathf.Clamp(Mathf.RoundToInt(r.crop.x * sx), 0, tex.width - 1), cy = Mathf.Clamp(Mathf.RoundToInt(r.crop.y * sy), 0, tex.height - 1);
                    int cw = Mathf.Min(Mathf.RoundToInt(r.crop.width * sx), tex.width - cx), ch = Mathf.Min(Mathf.RoundToInt(r.crop.height * sy), tex.height - cy);
                    var o = new Texture2D(cw, ch, TextureFormat.RGBA32, false);
                    o.SetPixels(tex.GetPixels(cx, cy, cw, ch)); o.Apply(false);
                    try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, picture + "_crop.png"), o.EncodeToPNG()); } catch { }
                    Destroy(o); Destroy(tex);
                }
            }
            EnsureTargets();
            byte[] A = null, B = null;
            try { DustDrawn(true); A = Draw(r); DustDrawn(false); B = Draw(r); }
            catch (System.Exception e) { pairFails++; if (pairFails <= 3) Log("the double render failed: " + e.Message); }
            finally { DustDrawn(true); }
            if (A == null || B == null) yield break;
            r.a.Add(A); r.b.Add(B); r.ship.Add(ShipBox(r)); pairs++;
        }

        // ------------------------------------------------------------------ the metric
        static float[] Box5(byte[] a)
        {
            var t = new float[HW * HH]; var o = new float[HW * HH];
            for (int y = 0; y < HH; y++)
                for (int x = 0; x < HW; x++)
                {
                    float s0 = 0f; for (int k = -2; k <= 2; k++) s0 += a[y * HW + Mathf.Clamp(x + k, 0, HW - 1)];
                    t[y * HW + x] = s0 * 0.2f;
                }
            for (int y = 0; y < HH; y++)
                for (int x = 0; x < HW; x++)
                {
                    float s0 = 0f; for (int k = -2; k <= 2; k++) s0 += t[Mathf.Clamp(y + k, 0, HH - 1) * HW + x];
                    o[y * HW + x] = s0 * 0.2f;
                }
            return o;
        }
        static bool In(RectInt b, int x, int y) => b.width > 0 && x >= b.xMin && x < b.xMax && y >= b.yMin && y < b.yMax;

        /// <summary>The dust area: the cells the dust changes by DustMinStep on average (outside the ship in that frame).</summary>
        static bool[] DustArea(Rec r)
        {
            var m = new bool[HW * HH]; int n = r.a.Count; if (n == 0) return m;
            var sum = new float[HW * HH]; var cnt = new int[HW * HH];
            for (int f = 0; f < n; f++)
            {
                var fa = Box5(r.a[f]); var fb = Box5(r.b[f]); var box = r.ship[f];
                for (int y = 0; y < HH; y++) for (int x = 0; x < HW; x++) { if (In(box, x, y)) continue; int i = y * HW + x; sum[i] += Mathf.Abs(fa[i] - fb[i]); cnt[i]++; }
            }
            for (int i = 0; i < m.Length; i++) m[i] = cnt[i] > n / 2 && sum[i] / cnt[i] >= DustMinStep;
            return m;
        }

        static void Second(List<byte[]> fr, List<RectInt> ship, bool[] area, out float mean, out float p99, float[] heat)
        {
            mean = -1f; p99 = -1f; int n = fr.Count; if (n < 3) return;
            var hist = new int[2001]; double sum = 0.0; long c = 0;
            float[] p0 = Box5(fr[0]), p1 = Box5(fr[1]);
            for (int t = 2; t < n; t++)
            {
                var p2 = Box5(fr[t]); RectInt b0 = ship[t - 2], b1 = ship[t - 1], b2 = ship[t];
                for (int y = 0; y < HH; y++)
                    for (int x = 0; x < HW; x++)
                    {
                        int i = y * HW + x; if (!area[i] || In(b0, x, y) || In(b1, x, y) || In(b2, x, y)) continue;
                        float e = Mathf.Abs(p1[i] - 0.5f * (p0[i] + p2[i]));
                        sum += e; c++; hist[Mathf.Clamp(Mathf.RoundToInt(e * 100f), 0, 2000)]++;
                        if (heat != null) heat[i] += e;
                    }
                p0 = p1; p1 = p2;
            }
            if (c == 0) return;
            mean = (float)(sum / c);
            long want = (long)(c * 0.99), acc = 0; p99 = 0f;
            for (int k = 0; k < hist.Length; k++) { acc += hist[k]; if (acc >= want) { p99 = k / 100f; break; } }
        }

        void Measure(Rec r, bool[] area)
        {
            r.areaPx = 0; foreach (var v in area) if (v) r.areaPx++;
            if (r.a.Count < 3 || r.areaPx < 200) return;
            var ha = new float[HW * HH]; var hb = new float[HW * HH];
            Second(r.a, r.ship, area, out r.flickA, out r.p99A, ha);
            Second(r.b, r.ship, area, out r.flickB, out r.p99B, hb);
            var d = new List<byte[]>(r.a.Count);
            for (int f = 0; f < r.a.Count; f++) { var x = new byte[HW * HH]; for (int i = 0; i < x.Length; i++) x[i] = (byte)Mathf.Clamp(128 + r.a[f][i] - r.b[f][i], 0, 255); d.Add(x); }
            Second(d, r.ship, area, out r.flickD, out r.p99D, null);
            // the heat picture: where A's second difference is over B's (x16, mean over the frames); the ship's last box black
            int n = Mathf.Max(1, r.a.Count - 2); var o = new Texture2D(HW, HH, TextureFormat.RGBA32, false); var last = r.ship[r.ship.Count - 1];
            for (int y = 0; y < HH; y++)
                for (int x = 0; x < HW; x++)
                {
                    int i = y * HW + x; byte v = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Max(0f, ha[i] - hb[i]) / n * 16f), 0, 255);
                    o.SetPixel(x, y, In(last, x, y) ? new Color32(0, 0, 0, 255) : area[i] ? new Color32(v, v, v, 255) : new Color32((byte)(v / 3), (byte)(v / 3), (byte)(40 + v / 3), 255));
                }
            o.Apply(false);
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, lvl + "_" + r.name + "_own_flicker_heat.png"), o.EncodeToPNG()); pictures++; } catch { }
            Destroy(o);
        }

        void Strip(List<byte[]> fr, string file)
        {
            if (fr.Count < 8) return;
            int k0 = Mathf.Min(fr.Count - 6, 60);
            var o = new Texture2D(HW * 3, HH * 2, TextureFormat.RGBA32, false);
            for (int k = 0; k < 6; k++)
            {
                var f = fr[k0 + k]; int ox = (k % 3) * HW, oy = (1 - k / 3) * HH;
                for (int y = 0; y < HH; y++) for (int x = 0; x < HW; x++) { byte v = f[y * HW + x]; o.SetPixel(ox + x, oy + y, new Color32(v, v, v, 255)); }
            }
            o.Apply(false);
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, file + ".png"), o.EncodeToPNG()); pictures++; } catch { }
            Destroy(o);
        }

        // ------------------------------------------------------------------ one flight: take-off at the ship's pad, landing at `to`
        IEnumerator Flight(string tag, int[] to, float h, Rec take, Rec land)
        {
            var at = ShipGround();
            yield return Focus(at, h);
            int b0 = OzShuttleDownwash.Bursts;
            if (!Move(to[0], to[1])) { Check($"{tag}: the move order is taken", false); yield break; }
            yield return WaitFor(() => OzShuttleDownwash.Bursts > b0, 12f);
            if (OzShuttleDownwash.Bursts <= b0) { Check($"{tag}: it took off", false); yield break; }
            if (take != null)
            {
                Time.captureFramerate = 60;
                for (int f = 0; f < 180; f++) yield return Frame(take, at, f == 60 ? lvl + "_" + tag + "_takeoff_1.0s" : null);
                Time.captureFramerate = 0;
            }
            var lat = Ground(to[0], to[1]);
            yield return WaitFor(() => ship.FeetHeight > 12f, 20f);                                          // well up and away first
            yield return Focus(lat, h);
            yield return WaitFor(() => ship.FeetHeight < 9f && s.ShuttleAirborne, 60f);
            if (land != null)
            {
                int b1 = OzShuttleDownwash.Bursts; Time.captureFramerate = 60; int after = -1;
                for (int f = 0; f < 360; f++)
                {
                    yield return Frame(land, lat, null);
                    if (after < 0 && OzShuttleDownwash.Bursts > b1) after = 0;
                    if (after >= 0 && ++after > 60) break;
                }
                Time.captureFramerate = 0;
            }
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 20f);
            yield return WaitReal(1.0f);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || !cam) { Log("no game"); fails++; Finish(); yield break; }
            lvl = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            Log($"K416 dust flicker check (K420 measure: A and B drawn in the same moment), {lvl}, {Screen.width}x{Screen.height}, quality {OzQuality.Name}");
            int bots = W.Bots.Count; W.Bots.Clear();
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            yield return WaitReal(1.0f);
            s = W.PlayableShuttle; ship = s != null ? OzPlayableShuttle.Of(s) : null;
            if (ship == null) { s = PlaceShip(); ship = s != null ? OzPlayableShuttle.Of(s) : null; yield return WaitReal(1.5f); }
            if (s == null || ship == null || ship.Wash == null) { Check("a drawn shuttle with its downwash", false); Finish(); yield break; }
            var m = ship.Wash.Mat;
            bool steady = m && m.shader && m.shader.name == "OpenZ/GroundDust" && m.HasProperty("_WinK") && m.HasProperty("_PoolLin");
            Check("the dust draws with OpenZ/GroundDust and its steady-colour numbers (_WinK, _PoolLin)", steady);
            Log($"set-up: CPU removed {bots}; ship #{s.Id}; dust shader {(m && m.shader ? m.shader.name : "none")}, ShaderPool {OzShuttleDownwash.ShaderPool}; the jets' light near the ground eased {OzJetLightSteady.On}");
            foreach (float clock in new[] { 0.10f, 0.65f })
                foreach (float h in new[] { 70f, 35f })
                {
                    string sfx = (clock > 0.5f ? "night" : "day") + "_" + h.ToString("0") + "m";
                    if (dn) { dn.timeOfDay = clock; dn.Apply(clock); yield return WaitReal(clock > 0.5f ? 3f : 1f); }
                    var to = Pad(s.CenterX, s.CenterY, 480, clock > 0.5f ? 90f : 0f, 180);
                    if (to == null) { Check($"{sfx}: open ground 60 m on to land", false); continue; }
                    int[] home = { s.CenterX, s.CenterY };
                    var aT = new Rec { name = "A_takeoff_" + sfx, h = h }; var aL = new Rec { name = "A_landing_" + sfx, h = h };
                    int p0 = pairs;
                    yield return Flight("A_" + sfx, to, h, aT, aL);
                    yield return Flight("back_" + sfx, home, h, null, null);                                   // back to the pad (not measured)
                    foreach (var r in new[] { aT, aL })
                    {
                        var area = DustArea(r);
                        Measure(r, area);
                        Strip(r.a, lvl + "_" + r.name + "_strip"); Strip(r.b, lvl + "_" + r.name + "_B_strip");
                        float own = r.flickA - Mathf.Max(0f, r.flickB), ownP = r.p99A - Mathf.Max(0f, r.p99B);
                        Log($"{r.name}: {r.a.Count} frame pairs at 60 (crop {r.crop.width}x{r.crop.height} px = 24 x 15 m, cells {r.scale}x{r.scale}), dust area {r.areaPx} cells; " +
                            $"with the dust {r.flickA:F3} (99 % {r.p99A:F2}); without it, same moments {r.flickB:F3} (99 % {r.p99B:F2}); the dust's own {own:F3} (99 % {ownP:F2}); the dust's picture A - B {r.flickD:F3} (99 % {r.p99D:F2})");
                        Check($"{r.name}: the dust's own flicker at most {FlickerMax:F2} (mean) and {P99Max:F1} (99 %) of 255", r.flickA >= 0f && r.flickB >= 0f && own <= FlickerMax && ownP <= P99Max);
                        r.a.Clear(); r.b.Clear(); r.ship.Clear();
                    }
                    if (pairs == p0) Log($"{sfx}: no frame pair was drawn (the double render: {pairFails} failure(s))");
                }
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); }
            if (rts) rts.SetHeightImmediate(70f);
            Finish();
        }

        void Finish()
        {
            Time.captureFramerate = 0; DustDrawn(true);
            Log($"RESULT: {passes} PASS, {fails} FAIL, {pictures} pictures, {pairs} frame pairs ({pairFails} double renders failed)");
            Report = $"done: {passes} PASS, {fails} FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "flicker_log_" + lvl + ".txt"), sb.ToString()); System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "done_" + lvl + ".txt"), Report); } catch { }
            Destroy(gameObject, 0.5f);
        }
    }
}
