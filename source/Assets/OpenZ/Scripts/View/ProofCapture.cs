using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// for proof stills. In this Linear project Graphics.Blit of a CaptureScreenshotAsTexture texture into a default (sRGB)
    /// RenderTexture encodes the already-sRGB bytes a second time = every pixel brighter, the HUD hotbar 29 -> ~95 luma. Here:
    /// <see cref="Frame"/> = ScreenCapture.CaptureScreenshotAsTexture() as it is (the bytes on screen), scaled on the CPU when a
    /// width is asked (bilinear on the sRGB bytes, like ffmpeg / PIL), never through a Blit. Call after WaitForEndOfFrame.
    /// <see cref="SelfCheck"/> (timeScale 0, one frame): the helper frame vs ScreenCapture.CaptureScreenshot(path) (the engine's own
    /// PNG) and the hotbar luma; FAIL when MAE &gt; 3 or the hotbar luma &gt; 40 (the washed path reads ~95).
    /// </summary>
    public static class ProofCapture
    {
        /// <summary>The screen as it is. width &lt;= 0 or &gt;= the screen width = full resolution.</summary>
        public static Texture2D Frame(int width = 0)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            if (width <= 0 || width >= tex.width) return tex;
            var o = Scale(tex, width);
            Object.Destroy(tex);
            return o;
        }

        /// <summary>Saves the screen as JPG (full res or `width` wide). Call after WaitForEndOfFrame.</summary>
        public static void SaveJpg(string path, int width = 0, int quality = 92)
        {
            var t = Frame(width);
            File.WriteAllBytes(path, t.EncodeToJPG(quality));
            Object.Destroy(t);
        }

        /// <summary>Bilinear CPU scale of an sRGB-byte texture to `width` (aspect kept), RGB24.</summary>
        public static Texture2D Scale(Texture2D src, int width)
        {
            int sw = src.width, sh = src.height, w = Mathf.Max(1, width), h = Mathf.Max(1, Mathf.RoundToInt(sh * (w / (float)sw)));
            var s = src.GetPixels32(); var d = new Color32[w * h];
            float fx = sw / (float)w, fy = sh / (float)h;
            for (int y = 0; y < h; y++)
            {
                float sy = (y + 0.5f) * fy - 0.5f; int y0 = Mathf.Clamp((int)Mathf.Floor(sy), 0, sh - 1), y1 = Mathf.Min(y0 + 1, sh - 1); float ty = Mathf.Clamp01(sy - y0);
                for (int x = 0; x < w; x++)
                {
                    float sx = (x + 0.5f) * fx - 0.5f; int x0 = Mathf.Clamp((int)Mathf.Floor(sx), 0, sw - 1), x1 = Mathf.Min(x0 + 1, sw - 1); float tx = Mathf.Clamp01(sx - x0);
                    Color32 a = s[y0 * sw + x0], b = s[y0 * sw + x1], c = s[y1 * sw + x0], e = s[y1 * sw + x1];
                    float w00 = (1 - tx) * (1 - ty), w10 = tx * (1 - ty), w01 = (1 - tx) * ty, w11 = tx * ty;
                    d[y * w + x] = new Color32((byte)(a.r * w00 + b.r * w10 + c.r * w01 + e.r * w11 + 0.5f), (byte)(a.g * w00 + b.g * w10 + c.g * w01 + e.g * w11 + 0.5f),
                                               (byte)(a.b * w00 + b.b * w10 + c.b * w01 + e.b * w11 + 0.5f), 255);
                }
            }
            var o = new Texture2D(w, h, TextureFormat.RGB24, false); o.SetPixels32(d); o.Apply();
            return o;
        }

        /// <summary>The OLD (broken) path, kept only for the gamma A/B proof: GetTemporary default sRGB + Blit = double encode.</summary>
        public static Texture2D OldBlitFrame(int width)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            int w = Mathf.Min(width, tex.width), h = Mathf.RoundToInt(tex.height * (w / (float)tex.width));
            var rt = RenderTexture.GetTemporary(w, h, 0);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var o = new Texture2D(w, h, TextureFormat.RGB24, false); o.ReadPixels(new Rect(0, 0, w, h), 0, 0); o.Apply();
            RenderTexture.active = prev; RenderTexture.ReleaseTemporary(rt); Object.Destroy(tex);
            return o;
        }

        /// <summary>Median Rec.709 luma of the HUD hotbar slots (rects measured on 1280-wide frames: 16:9 x172-475 y690-704, 16:10
        /// x200-585 y742-782, y from the top); -1 for another aspect.</summary>
        public static float HotbarLuma(Texture2D t)
        {
            float asp = t.width / (float)t.height; float x0, x1, y0, y1;
            if (Mathf.Abs(asp - 16f / 9f) < 0.03f) { x0 = 172 / 1280f; x1 = 475 / 1280f; y0 = 690 / 720f; y1 = 704 / 720f; }
            else if (Mathf.Abs(asp - 1.6f) < 0.03f) { x0 = 200 / 1280f; x1 = 585 / 1280f; y0 = 742 / 800f; y1 = 782 / 800f; }
            else return -1f;
            int px0 = (int)(x0 * t.width), px1 = (int)(x1 * t.width), py0 = (int)(y0 * t.height), py1 = (int)(y1 * t.height);
            var px = t.GetPixels32(); var L = new List<float>();
            for (int y = py0; y < py1; y++) for (int x = px0; x < px1; x++)
                {
                    var c = px[(t.height - 1 - y) * t.width + x];                                          // texture rows are bottom-up
                    L.Add(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b);
                }
            if (L.Count == 0) return -1f;
            L.Sort(); return L[L.Count / 2];
        }

        /// <summary>Mean absolute RGB error (0-255) between two same-size textures, every 2nd pixel; -1 on a size mismatch.</summary>
        public static float Mae(Texture2D a, Texture2D b, RectInt? area = null)
        {
            if (a.width != b.width || a.height != b.height) return -1f;
            var pa = a.GetPixels32(); var pb = b.GetPixels32(); int w = a.width;
            var r = area ?? new RectInt(0, 0, a.width, a.height);
            double sum = 0; long n = 0;
            for (int y = r.yMin; y < r.yMax; y += 2) for (int x = r.xMin; x < r.xMax; x += 2)
                {
                    var ca = pa[y * w + x]; var cb = pb[y * w + x];
                    sum += Mathf.Abs(ca.r - cb.r) + Mathf.Abs(ca.g - cb.g) + Mathf.Abs(ca.b - cb.b); n += 3;
                }
            return n > 0 ? (float)(sum / n) : -1f;
        }

        /// <summary>Luma percentile (0-1) of a texture, every 3rd pixel.</summary>
        public static float LumaPct(Texture2D t, float pct)
        {
            var px = t.GetPixels32(); var L = new List<float>(px.Length / 9 + 1);
            for (int i = 0; i < px.Length; i += 3) { var c = px[i]; L.Add(0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b); }
            L.Sort(); return L.Count > 0 ? L[Mathf.Clamp((int)(pct * L.Count), 0, L.Count - 1)] : -1f;
        }

        public static bool LastOk; public static string LastCheck = "";

        /// <summary>Gamma self-check at the start of a proof run (timeScale 0 while it runs): the helper frame vs the engine's own
        /// ScreenCapture.CaptureScreenshot PNG of the same paused frame. Sets LastOk / LastCheck.</summary>
        public static IEnumerator SelfCheck(string dir, string tag)
        {
            float ts = Time.timeScale; Time.timeScale = 0f;
            Directory.CreateDirectory(dir);
            string refPath = Path.Combine(dir, "selfcheck_" + tag + "_ref.png");
            if (File.Exists(refPath)) File.Delete(refPath);
            yield return null; yield return null;
            // the engine PNG is requested in the Update phase (called after WaitForEndOfFrame it comes back black in the editor)
            // and captures the end of this frame, the same frame the helper grabs after WaitForEndOfFrame
            ScreenCapture.CaptureScreenshot(refPath);
            yield return new WaitForEndOfFrame();
            var mine = Frame();
            Texture2D refTex = null; float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 6f)
            {
                yield return null;
                if (!File.Exists(refPath)) continue;
                try { var bytes = File.ReadAllBytes(refPath); var rt = new Texture2D(2, 2); if (rt.LoadImage(bytes) && rt.width == mine.width) { refTex = rt; break; } Object.Destroy(rt); }
                catch (IOException) { }
            }
            float hot = HotbarLuma(mine), mae = refTex ? Mae(mine, refTex) : -1f, hotRef = refTex ? HotbarLuma(refTex) : -1f;
            LastOk = refTex && mae >= 0f && mae <= 3f && (hot < 0f || hot <= 40f);
            LastCheck = $"gamma self-check {tag}: {(LastOk ? "PASS" : "FAIL")} ({mine.width}x{mine.height}): helper vs CaptureScreenshot MAE {mae:F2} (<= 3), hotbar luma helper {hot:F1} / ref {hotRef:F1} (<= 40; the washed Blit path ~95)";
            Debug.Log("[ProofCapture] " + LastCheck);
            Object.Destroy(mine); if (refTex) Object.Destroy(refTex);
            Time.timeScale = ts;
        }

        public static string GammaStatus = "idle";

        /// <summary>The gamma A/B proof (Play, paused): gammaAB_&lt;map&gt;_ref.png (CaptureScreenshot), _new.jpg (helper, full),
        /// _new_1920.jpg / _new_1280.jpg (helper scaled), _old.jpg (the old Blit path, 1280) + the numbers in gammaAB_&lt;map&gt;.txt.</summary>
        public static string GammaAB(string dir)
        {
            var g = ZGame.I; string map = g ? g.level : "menu";
            var host = new GameObject("OZ_GammaAB").AddComponent<GammaHost>();
            host.StartCoroutine(host.Run(dir, map));
            GammaStatus = "running"; return "gamma A/B started on " + map;
        }

        sealed class GammaHost : MonoBehaviour
        {
            public IEnumerator Run(string dir, string map)
            {
                Directory.CreateDirectory(dir);
                float ts = Time.timeScale; Time.timeScale = 0f;
                string refPath = Path.Combine(dir, $"gammaAB_{map}_ref.png");
                if (File.Exists(refPath)) File.Delete(refPath);
                yield return null; yield return null;
                ScreenCapture.CaptureScreenshot(refPath);                                                  // Update phase (see SelfCheck)
                {
                    float tw = Time.realtimeSinceStartup;
                    while (!File.Exists(refPath) && Time.realtimeSinceStartup - tw < 6f) yield return null;
                    yield return null; yield return null;
                }
                yield return new WaitForEndOfFrame();
                var full = Frame(); var s1920 = Scale(full, 1920); var s1280 = Scale(full, 1280); var old = OldBlitFrame(1280);
                File.WriteAllBytes(Path.Combine(dir, $"gammaAB_{map}_new.jpg"), full.EncodeToJPG(92));
                File.WriteAllBytes(Path.Combine(dir, $"gammaAB_{map}_new_1920.jpg"), s1920.EncodeToJPG(92));
                File.WriteAllBytes(Path.Combine(dir, $"gammaAB_{map}_new_1280.jpg"), s1280.EncodeToJPG(92));
                File.WriteAllBytes(Path.Combine(dir, $"gammaAB_{map}_old.jpg"), old.EncodeToJPG(92));
                Texture2D refTex = null; float t0 = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - t0 < 6f)
                {
                    yield return null;
                    if (!File.Exists(refPath)) continue;
                    try { var rt = new Texture2D(2, 2); if (rt.LoadImage(File.ReadAllBytes(refPath)) && rt.width == full.width) { refTex = rt; break; } Object.Destroy(rt); }
                    catch (IOException) { }
                }
                var sb = new StringBuilder();
                sb.AppendLine($"gamma A/B {map} {System.DateTime.Now:HH:mm:ss}, screen {full.width}x{full.height}");
                if (refTex)
                {
                    var ref1280 = Scale(refTex, 1280);
                    sb.AppendLine($"hotbar luma: ref {HotbarLuma(refTex):F1}, new full {HotbarLuma(full):F1}, new 1920 {HotbarLuma(s1920):F1}, new 1280 {HotbarLuma(s1280):F1}, old 1280 {HotbarLuma(old):F1}");
                    int hh = Mathf.RoundToInt(ref1280.height * 0.12f);                                       // the HUD band (bottom 12 %)
                    var hud = new RectInt(0, 0, ref1280.width, hh);
                    sb.AppendLine($"MAE vs ref: new full {Mae(full, refTex):F2}; at 1280: new {Mae(s1280, ref1280):F2}, old {Mae(old, ref1280):F2}; HUD band: new {Mae(s1280, ref1280, hud):F2}, old {Mae(old, ref1280, hud):F2}");
                    sb.AppendLine($"frame luma p1 / median: ref {LumaPct(refTex, 0.01f):F1} / {LumaPct(refTex, 0.5f):F1}, new {LumaPct(full, 0.01f):F1} / {LumaPct(full, 0.5f):F1}, new 1280 {LumaPct(s1280, 0.01f):F1} / {LumaPct(s1280, 0.5f):F1}, old {LumaPct(old, 0.01f):F1} / {LumaPct(old, 0.5f):F1}");
                    Object.Destroy(ref1280); Object.Destroy(refTex);
                }
                else sb.AppendLine("no ref PNG read back");
                File.WriteAllText(Path.Combine(dir, $"gammaAB_{map}.txt"), sb.ToString());
                Debug.Log("[ProofCapture] " + sb.ToString().Replace("\n", " | "));
                Object.Destroy(full); Object.Destroy(s1920); Object.Destroy(s1280); Object.Destroy(old);
                Time.timeScale = ts; GammaStatus = "DONE " + sb.ToString().Replace("\n", " | ");
                Destroy(gameObject);
            }
        }
    }
}
