using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// the sun and the radar dish running, grabs 13 consecutive frames of the Game view and measures how much the ground INSIDE
    /// cast shadows changes from frame to frame, against lit sand as the control:
    ///  * still camera: raw per-pixel luma difference (frame n+1 - n, and n+2 - n = 1/30 s at 60 fps);
    ///  * panning camera (autoDrift 4 m/s east): every image row is re-registered with the exact sub-pixel shift of its ground
    ///    point between the two recorded camera matrices, then differenced (the sand texture's resampling error shows up equally in
    ///    shadow and lit sand, so the gate compares shadow against lit).
    /// Masks: centre crop (no HUD); "shadow interior" = ground darker than 0.72 x the crop's 85th luma percentile in EVERY frame,
    /// eroded 3 px (moving shadow edges excluded); "lit" = brighter than 0.9 x p85 in every frame; units and their 8 px surround
    /// and the desert sand streak / dust devil particles are excluded (particles hidden during the capture).
    /// PASS (both levels, both spots): shadow interior mean |d| &lt;= lit mean |d| + 0.5 / 255, and shadow pixels with |d| >= 8 / 255
    /// (a visible 3 % light hole) &lt;= 0.02 % per frame pair, and no pixel toggles (up, down, up) by >= 6 / 255 in the 13 frames.
    /// Usage (Play, full look, 60 fps cap): OpenZ.ShadowFlickerProbe.Begin("radar", 30f, false);  ("radar" | "cliff" | x,z)
    /// </summary>
    public class ShadowFlickerProbe : MonoBehaviour
    {
        public static string Status = "idle";
        string spot; float height; bool pan; float tod; Vector3 focusOverride; bool hasFocus;
        const int Frames = 13;

        public static ShadowFlickerProbe Begin(string spot = "radar", float height = 30f, bool pan = false, float timeOfDay = -1f)
        {
            var p = new GameObject("OZ_ShadowFlickerProbe").AddComponent<ShadowFlickerProbe>();
            p.spot = spot; p.height = height; p.pan = pan; p.tod = timeOfDay; p.StartCoroutine(p.Run()); Status = "running"; return p;
        }
        public static ShadowFlickerProbe BeginAt(float x, float z, float height = 30f, bool pan = false, float timeOfDay = -1f)
        {
            var p = Begin("xz", height, pan, timeOfDay); p.focusOverride = new Vector3(x, 0, z); p.hasFocus = true; return p;
        }

        IEnumerator Run()
        {
            yield return null;                                                      // BeginAt sets the focus after Begin
            var cam = Camera.main; var rts = cam ? cam.GetComponent<RtsCamera>() : null;
            var dn = FindFirstObjectByType<DayNight>();
            if (!cam || !rts) { Status = "no RtsCamera"; Destroy(gameObject); yield break; }
            if (dn && tod >= 0f) { dn.timeOfDay = tod; dn.Apply(tod); }
            Vector3 focus = hasFocus ? focusOverride : FindSpot(spot, dn);
            var oldDrift = rts.autoDrift; rts.follow = null; rts.focus = focus; rts.autoDrift = Vector2.zero; rts.SetHeightImmediate(height);
            var hidden = new List<Renderer>();
            foreach (var ps in FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
            {
                var n = ps.gameObject.name;
                if (!n.StartsWith("SandStreaks") && !n.StartsWith("DustDevil")) continue;
                var r = ps.GetComponent<Renderer>(); if (r && r.enabled) { r.enabled = false; hidden.Add(r); }
            }
            yield return new WaitForSeconds(1.5f);
            if (pan) { rts.autoDrift = new Vector2(4f, 0f); yield return new WaitForSeconds(0.5f); }
            var vps = new List<Matrix4x4>(); var times = new List<float>(); var excl = new List<RectInt>(); var grabs = new List<RenderTexture>();
            int W = Screen.width, H = Screen.height;
            yield return new WaitForEndOfFrame();
            var reference = ScreenCapture.CaptureScreenshotAsTexture();               // CPU grab of the frame before: decides the RT orientation
            for (int i = 0; i < Frames; i++)
            {
                yield return new WaitForEndOfFrame();
                var rt = new RenderTexture(W, H, 0, RenderTextureFormat.ARGB32); rt.Create();
                ScreenCapture.CaptureScreenshotIntoRenderTexture(rt);                // GPU copy only: the 13 frames keep the real 60 fps pacing
                grabs.Add(rt);
                vps.Add(cam.projectionMatrix * cam.worldToCameraMatrix);
                times.Add(Time.time);
                if (i % 4 == 0)                                                       // units: masked with an 8 px margin (+ their motion over 4 frames)
                    foreach (var uv in FindObjectsByType<UnitView>(FindObjectsSortMode.None))
                        foreach (var r in uv.GetComponentsInChildren<Renderer>()) { var rc = ScreenRect(cam, r.bounds, W, H, 12); if (rc.width > 0) excl.Add(rc); }
            }
            rts.autoDrift = oldDrift;
            foreach (var r in hidden) if (r) r.enabled = true;
            // read back after the timed window; the render-texture copy is upside down on some APIs: pick the orientation that
            // matches the CPU reference grab
            var shots = new List<Color32[]>();
            var rb = new Texture2D(W, H, TextureFormat.RGBA32, false);
            bool? flip = null;
            var refPx = reference.width == W && reference.height == H ? reference.GetPixels32() : null; Destroy(reference);
            foreach (var rt in grabs)
            {
                var prev = RenderTexture.active; RenderTexture.active = rt;
                rb.ReadPixels(new Rect(0, 0, W, H), 0, 0); rb.Apply(false);
                RenderTexture.active = prev; rt.Release(); Destroy(rt);
                var px = rb.GetPixels32();
                if (flip == null)
                {
                    double a = 0, b = 0;
                    if (refPx != null)
                        for (int y = 0; y < H; y += 7) for (int x = 0; x < W; x += 7)
                        { a += Mathf.Abs(Luma(px[y * W + x]) - Luma(refPx[y * W + x])); b += Mathf.Abs(Luma(px[(H - 1 - y) * W + x]) - Luma(refPx[y * W + x])); }
                    flip = b < a;
                }
                if (flip == true)
                    for (int y = 0; y < H / 2; y++) for (int x = 0; x < W; x++) { var t = px[y * W + x]; px[y * W + x] = px[(H - 1 - y) * W + x]; px[(H - 1 - y) * W + x] = t; }
                shots.Add(px);
            }
            Destroy(rb);
            string rep = Analyse(shots, vps, times, excl, W, H, cam, focus, dn);
            string dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records"));
            System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "shadow_flicker_probe.md"), rep);
            Debug.Log("[OpenZ] ShadowFlickerProbe\n" + rep);
            Status = rep.Contains("PASS") ? "PASS" : "FAIL";
            Destroy(gameObject);
        }

        static Vector3 FindSpot(string spot, DayNight dn)
        {
            if (spot == "cliff") return new Vector3(30f, 0f, -90f);                 // L02 west wall: its afternoon / golden-hour shadow on the sand
            Transform dish = null;
            foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name == "anim_dish") { dish = t; break; }
            if (!dish) foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None)) if (t.name.ToLowerInvariant().Contains("radar")) { dish = t; break; }
            if (!dish) return Vector3.zero;
            var f = dn && dn.sun ? dn.sun.transform.forward : Vector3.down;
            var off = new Vector3(f.x, 0f, f.z) / Mathf.Max(0.25f, -f.y) * 4f;       // where a point 4 m up casts its shadow
            return new Vector3(dish.position.x, 0f, dish.position.z) + off;
        }

        static RectInt ScreenRect(Camera cam, Bounds b, int W, int H, int pad)
        {
            float x0 = 1e9f, y0 = 1e9f, x1 = -1e9f, y1 = -1e9f;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var s = cam.WorldToScreenPoint(c); if (s.z <= 0) return new RectInt(0, 0, 0, 0);
                x0 = Mathf.Min(x0, s.x); y0 = Mathf.Min(y0, s.y); x1 = Mathf.Max(x1, s.x); y1 = Mathf.Max(y1, s.y);
            }
            float k = W / (float)cam.pixelWidth;
            int ix0 = Mathf.Clamp((int)(x0 * k) - pad, 0, W), iy0 = Mathf.Clamp((int)(y0 * k) - pad, 0, H), ix1 = Mathf.Clamp((int)(x1 * k) + pad, 0, W), iy1 = Mathf.Clamp((int)(y1 * k) + pad, 0, H);
            return new RectInt(ix0, iy0, Mathf.Max(0, ix1 - ix0), Mathf.Max(0, iy1 - iy0));
        }

        static float Luma(Color32 c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        // screen x (pixels) of a world point under a recorded view-projection matrix
        static Vector2 Project(Matrix4x4 vp, Vector3 p, int W, int H)
        {
            var c = vp * new Vector4(p.x, p.y, p.z, 1f);
            return new Vector2((c.x / c.w * 0.5f + 0.5f) * W, (c.y / c.w * 0.5f + 0.5f) * H);
        }

        string Analyse(List<Color32[]> shots, List<Matrix4x4> vps, List<float> times, List<RectInt> excl, int W, int H, Camera cam, Vector3 focus, DayNight dn)
        {
            int x0 = (int)(W * 0.15f), x1 = (int)(W * 0.85f), y0 = (int)(H * 0.2f), y1 = (int)(H * 0.85f);
            int cw = x1 - x0, ch = y1 - y0, n = shots.Count;
            var L = new float[n][];
            for (int f = 0; f < n; f++)
            {
                L[f] = new float[cw * ch];
                for (int y = 0; y < ch; y++) for (int x = 0; x < cw; x++) L[f][y * cw + x] = Luma(shots[f][(y + y0) * W + x + x0]);
            }
            var tmp = (float[])L[0].Clone(); System.Array.Sort(tmp); float p85 = tmp[(int)(tmp.Length * 0.85f)];
            var shadow = new bool[cw * ch]; var lit = new bool[cw * ch]; var bad = new bool[cw * ch];
            foreach (var r in excl) for (int y = Mathf.Max(r.yMin, y0); y < Mathf.Min(r.yMax, y1); y++) for (int x = Mathf.Max(r.xMin, x0); x < Mathf.Min(r.xMax, x1); x++) bad[(y - y0) * cw + x - x0] = true;
            for (int i = 0; i < cw * ch; i++)
            {
                bool s = true, l = true;
                for (int f = 0; f < n; f++) { s &= L[f][i] < 0.72f * p85; l &= L[f][i] > 0.9f * p85; }
                shadow[i] = s && !bad[i]; lit[i] = l && !bad[i];
            }
            shadow = Erode(shadow, cw, ch, 3);
            // row registration for the pan case: shift of the ground point under each row between frames f and f+1
            var sb = new StringBuilder();
            double sumS = 0, sumL = 0; long cntS = 0, cntL = 0, holes = 0, toggles = 0; float maxS = 0; double sum2 = 0; long cnt2 = 0;
            var heat = new float[cw * ch];
            var dPrev = new float[cw * ch]; var ups = new int[cw * ch]; var downs = new int[cw * ch];
            for (int f = 0; f + 1 < n; f++)
            {
                for (int y = 0; y < ch; y++)
                {
                    float shift = 0f;
                    if (pan)
                    {
                        var ray = cam.ScreenPointToRay(new Vector3(0.5f * cam.pixelWidth, (y + y0 + 0.5f) * cam.pixelHeight / (float)H, 0));   // a pure x pan keeps each row's ground depth
                        float t = ray.direction.y < -1e-4f ? -ray.origin.y / ray.direction.y : 0f;
                        var g = ray.origin + ray.direction * t;
                        shift = Project(vps[f + 1], g, W, H).x - Project(vps[f], g, W, H).x;
                    }
                    for (int x = 0; x < cw; x++)
                    {
                        int i = y * cw + x; float xs = x + shift; int xi = Mathf.FloorToInt(xs); float fr = xs - xi;
                        if (xi < 0 || xi + 1 >= cw) continue;
                        float b = Mathf.Lerp(L[f + 1][y * cw + xi], L[f + 1][y * cw + xi + 1], fr);
                        float d = b - L[f][i]; float ad = Mathf.Abs(d);
                        if (shadow[i]) { sumS += ad; cntS++; if (ad > maxS) maxS = ad; if (ad >= 8f) holes++; heat[i] = Mathf.Max(heat[i], ad); }
                        else if (lit[i]) { sumL += ad; cntL++; }
                        if (!pan && shadow[i] && ad >= 6f)
                        {
                            if (d > 0 && dPrev[i] < 0) ups[i]++; if (d < 0 && dPrev[i] > 0) downs[i]++;
                            dPrev[i] = d;
                        }
                    }
                }
                if (!pan && f + 2 < n)                                                       // the 1/30 s pair (still camera)
                    for (int i = 0; i < cw * ch; i++) if (shadow[i]) { sum2 += Mathf.Abs(L[f + 2][i] - L[f][i]); cnt2++; }
            }
            for (int i = 0; i < cw * ch; i++) if (ups[i] + downs[i] >= 2) toggles++;
            double mS = cntS > 0 ? sumS / cntS : 0, mL = cntL > 0 ? sumL / cntL : 0;
            double holeShare = cntS > 0 ? 100.0 * holes / cntS : 0;
            float fps = (n - 1) / Mathf.Max(1e-4f, times[n - 1] - times[0]);
            bool pass = cntS > 2000 && mS <= mL + 0.5 && holeShare <= 0.02 && toggles == 0;
            sb.AppendLine($"\n## {System.DateTime.Now:dd MMM HH:mm:ss} ShadowFlickerProbe {spot} h {height:0} m {(pan ? "PANNING 4 m/s" : "STILL")} focus ({focus.x:0.0}, {focus.z:0.0}) t {(dn ? dn.timeOfDay : -1):0.000}");
            sb.AppendLine($"- {n} frames at {fps:0.0} fps, {W}x{H}; shadow interior {cntS / (n - 1)} px/pair, lit control {cntL / (n - 1)} px/pair, unit exclusions {excl.Count}");
            sb.AppendLine($"- frame-to-frame |d luma| (0-255): shadow mean {mS:0.00}, max {maxS:0.0}; lit mean {mL:0.00}" + (!pan && cnt2 > 0 ? $"; 1/30 s pair shadow mean {sum2 / cnt2:0.00}" : ""));
            sb.AppendLine($"- shadow pixels with |d| >= 8/255 (visible holes): {holeShare:0.000} % of shadow pixel-pairs; toggling pixels (up-down-up >= 6/255): {toggles}");
            sb.AppendLine($"- **{(pass ? "PASS" : "FAIL")}** (needs shadow mean <= lit mean + 0.5, holes <= 0.02 %, 0 toggles, >= 2000 shadow px)");
            string md = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/shadow_fix"));
            System.IO.Directory.CreateDirectory(md);
            var tex = new Texture2D(cw, ch, TextureFormat.RGB24, false);
            var px = new Color32[cw * ch];
            for (int i = 0; i < cw * ch; i++)
            {
                byte g = (byte)Mathf.Clamp(L[0][i] * 0.5f, 0, 255); float h = Mathf.Clamp01(heat[i] * 16f / 255f);
                px[i] = shadow[i] ? new Color32((byte)Mathf.Max(g, h * 255), (byte)(h * 140), (byte)(g * 0.6f), 255) : new Color32(g, g, g, 255);
            }
            tex.SetPixels32(px); tex.Apply();
            string png = System.IO.Path.Combine(md, $"probe_{spot}_{(pan ? "pan" : "still")}_{System.DateTime.Now:HHmmss}.png");
            System.IO.File.WriteAllBytes(png, tex.EncodeToPNG()); Destroy(tex);
            sb.AppendLine($"- heat map (shadow interior, |d| x16 in orange): {png}");
            return sb.ToString();
        }

        static bool[] Erode(bool[] m, int w, int h, int r)
        {
            var o = new bool[m.Length];
            for (int y = r; y < h - r; y++)
                for (int x = r; x < w - r; x++)
                {
                    bool ok = true;
                    for (int dy = -r; dy <= r && ok; dy++) for (int dx = -r; dx <= r; dx++) if (!m[(y + dy) * w + x + dx]) { ok = false; break; }
                    o[y * w + x] = ok;
                }
            return o;
        }
    }
}
