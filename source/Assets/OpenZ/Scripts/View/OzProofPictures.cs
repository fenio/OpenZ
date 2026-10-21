using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Picture hygiene for the Play proofs (K256e, FIX_LIST rule 2 and S7): a picture named "h70" is taken from the normal RTS camera at the
    /// standard 70 m and the frame is really drawn at that height (a picture is grabbed at the END of its frame: the camera must not move
    /// in that frame; the round-2 night proof moved it to the close height in the same frame, so its "h70" pictures were the close view);
    /// each h70 picture differs from its close twin by at least <see cref="TwinMin"/> of 255 (mean luma difference); no two pictures of a
    /// take are the same file (md5); a strip is a FIXED camera's frames side by side in one sheet. Test only.
    /// </summary>
    public static class OzProofPictures
    {
        public static float TwinMin = 15f;

        /// <summary>Waits until the file is written (its size stable over two frames), at most max real seconds.</summary>
        public static IEnumerator Written(string path, float max = 5f)
        {
            float t0 = Time.unscaledTime; long last = -1;
            while (Time.unscaledTime - t0 < max)
            {
                long n = -1;
                try { if (System.IO.File.Exists(path)) n = new System.IO.FileInfo(path).Length; } catch { n = -1; }
                if (n > 0 && n == last) yield break;
                last = n; yield return null;
            }
        }

        /// <summary>Waits for the end of the frame and one more frame: a picture asked this frame is on disk before the camera moves.</summary>
        public static IEnumerator AfterGrab() { yield return new WaitForEndOfFrame(); yield return null; }

        static Texture2D Load(string path)
        {
            try
            {
                if (!System.IO.File.Exists(path)) return null;
                var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                return t.LoadImage(System.IO.File.ReadAllBytes(path)) ? t : null;
            }
            catch { return null; }
        }

        /// <summary>The mean luma difference (0..255) of two pictures on a 160 x 100 grid (-1: one is missing).</summary>
        public static float MeanLumaDiff(string a, string b)
        {
            var A = Load(a); var B = Load(b);
            if (!A || !B) { if (A) Object.Destroy(A); if (B) Object.Destroy(B); return -1f; }
            const int nx = 160, ny = 100; double sum = 0;
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++)
                {
                    float u = (i + 0.5f) / nx, v = (j + 0.5f) / ny;
                    var ca = A.GetPixelBilinear(u, v); var cb = B.GetPixelBilinear(u, v);
                    float la = 0.299f * ca.r + 0.587f * ca.g + 0.114f * ca.b, lb = 0.299f * cb.r + 0.587f * cb.g + 0.114f * cb.b;
                    sum += Mathf.Abs(la - lb) * 255f;
                }
            Object.Destroy(A); Object.Destroy(B);
            return (float)(sum / (nx * ny));
        }

        /// <summary>The files among these that are the same bytes as an earlier one (md5): "b = a".</summary>
        public static List<string> Duplicates(IEnumerable<string> paths)
        {
            var seen = new Dictionary<string, string>(); var dup = new List<string>();
            using (var md5 = System.Security.Cryptography.MD5.Create())
                foreach (var p in paths)
                {
                    if (!System.IO.File.Exists(p)) continue;
                    string h;
                    try { h = System.BitConverter.ToString(md5.ComputeHash(System.IO.File.ReadAllBytes(p))); } catch { continue; }
                    if (seen.TryGetValue(h, out var first)) dup.Add(System.IO.Path.GetFileName(p) + " = " + System.IO.Path.GetFileName(first));
                    else seen[h] = p;
                }
            return dup;
        }

        /// <summary>A strip sheet: the frames side by side (cols per row, each cellW wide, the frames' own aspect), numbered, as a JPEG.
        /// False: a frame is missing.</summary>
        public static bool Sheet(IList<string> frames, int cols, int cellW, string outPath)
        {
            if (frames == null || frames.Count == 0) return false;
            var first = Load(frames[0]); if (!first) return false;
            float aspect = (float)first.height / Mathf.Max(1, first.width); Object.Destroy(first);
            int cellH = Mathf.Max(8, Mathf.RoundToInt(cellW * aspect)), rows = (frames.Count + cols - 1) / cols, gap = 6;
            int W = cols * cellW + (cols + 1) * gap, H = rows * cellH + (rows + 1) * gap;
            var outT = new Texture2D(W, H, TextureFormat.RGB24, false); var px = new Color32[W * H];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(16, 16, 16, 255);
            for (int f = 0; f < frames.Count; f++)
            {
                var t = Load(frames[f]); if (!t) { Object.Destroy(outT); return false; }
                int cx = gap + (f % cols) * (cellW + gap), cy = H - (gap + (f / cols + 1) * (cellH + gap)) + gap;
                for (int y = 0; y < cellH; y++)
                    for (int x = 0; x < cellW; x++)
                    {
                        var c = t.GetPixelBilinear((x + 0.5f) / cellW, (y + 0.5f) / cellH);
                        px[(cy + y) * W + cx + x] = c;
                    }
                // the frame's number: a small white block per frame index in the top left corner (no font needed)
                for (int k = 0; k <= f && k < 40; k++)
                    for (int y = 0; y < 6; y++) for (int x = 0; x < 4; x++)
                        { int X = cx + 4 + (k % 20) * 6 + x, Y = cy + cellH - 10 - (k / 20) * 8 - y; if (X < W && Y >= 0) px[Y * W + X] = new Color32(255, 255, 255, 255); }
                Object.Destroy(t);
            }
            outT.SetPixels32(px); outT.Apply();
            try { System.IO.File.WriteAllBytes(outPath, outT.EncodeToJPG(88)); } catch { Object.Destroy(outT); return false; }
            Object.Destroy(outT);
            return true;
        }

        /// <summary>The same sheet for any pictures (PNG or JPEG frames).</summary>
        public static bool SheetJpg(IList<string> frames, int cols, int cellW, string outPath) => Sheet(frames, cols, cellW, outPath);

        /// <summary>A crop of a picture round a screen point (pixels, y up), halfW x halfH each way, saved as a JPEG at its own size (a crop of
        /// half the screen each way is a 2x zoom).</summary>
        public static bool Crop(string src, Vector2 centre, int halfW, int halfH, string outPath)
        {
            var t = Load(src); if (!t) return false;
            int x0 = Mathf.Clamp(Mathf.RoundToInt(centre.x) - halfW, 0, Mathf.Max(0, t.width - 2 * halfW)), y0 = Mathf.Clamp(Mathf.RoundToInt(centre.y) - halfH, 0, Mathf.Max(0, t.height - 2 * halfH));
            int w = Mathf.Min(2 * halfW, t.width), h = Mathf.Min(2 * halfH, t.height);
            var o = new Texture2D(w, h, TextureFormat.RGB24, false); o.SetPixels(t.GetPixels(x0, y0, w, h)); o.Apply();
            bool ok = true;
            try { System.IO.File.WriteAllBytes(outPath, o.EncodeToJPG(90)); } catch { ok = false; }
            Object.Destroy(t); Object.Destroy(o);
            return ok;
        }

        /// <summary>Two pictures side by side at the same height (h px), as a JPEG.</summary>
        public static bool Pair(string a, string b, string outPath, int h = 800)
        {
            var A = Load(a); var B = Load(b);
            if (!A || !B) { if (A) Object.Destroy(A); if (B) Object.Destroy(B); return false; }
            int wa = Mathf.RoundToInt(h * (float)A.width / Mathf.Max(1, A.height)), wb = Mathf.RoundToInt(h * (float)B.width / Mathf.Max(1, B.height)), gap = 8, W = wa + wb + gap;
            var px = new Color32[W * h]; for (int i = 0; i < px.Length; i++) px[i] = new Color32(16, 16, 16, 255);
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < wa; x++) px[y * W + x] = A.GetPixelBilinear((x + 0.5f) / wa, (y + 0.5f) / h);
                for (int x = 0; x < wb; x++) px[y * W + wa + gap + x] = B.GetPixelBilinear((x + 0.5f) / wb, (y + 0.5f) / h);
            }
            var o = new Texture2D(W, h, TextureFormat.RGB24, false); o.SetPixels32(px); o.Apply();
            bool ok = true;
            try { System.IO.File.WriteAllBytes(outPath, o.EncodeToJPG(90)); } catch { ok = false; }
            Object.Destroy(A); Object.Destroy(B); Object.Destroy(o);
            return ok;
        }

        /// <summary>The same crop of two pictures (round a screen point, half px each way), side by side, as a JPEG.</summary>
        public static bool CropPair(string a, string b, Vector2 centre, int half, string outPath)
        {
            string ta = outPath + ".a.jpg", tb = outPath + ".b.jpg";
            if (!Crop(a, centre, half, half, ta) || !Crop(b, centre, half, half, tb)) return false;
            bool ok = Pair(ta, tb, outPath, 2 * half);
            try { System.IO.File.Delete(ta); System.IO.File.Delete(tb); } catch { }
            return ok;
        }
    }
}
