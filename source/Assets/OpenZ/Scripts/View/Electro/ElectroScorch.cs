using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// electrobot_v1: the Lichtenberg scorch a tesla bolt burns into the ground where it strikes: a branching fern figure (2-3 m,
    /// random rotation and size, 4 procedural variants baked once at level load into a 2x2 atlas), laid on a terrain-conforming grid
    /// like ScorchDecals (ZGame walk surface / the rendered TER_ height, lifted 5 cm = over ScorchDecals' 3.5 cm, never co-planar;
    /// faded out over open water, at the grid rim and down cliff steps). Two looks from the same fern:
    ///  * the burn: OpenZ/ScorchDecal (multiplies the lit ground, never glows at night), fading over ~30 s;
    ///  * the hot flash: OpenZ/ElectroArc mode 3 (additive cyan, HDR) for the first 0.6 s: the fern glows, then cools to soot.
    /// At most 16 figures (the oldest is recycled). Our own procedural art.
    /// </summary>
    public sealed class ElectroScorch
    {
        public const int Max = 16, Tile = 256;
        public const float Lift = 0.05f, Life = 30f, FadeAt = 0.35f, HotSeconds = 0.6f;
        sealed class Mark
        {
            public Mesh burn, hot; public Vector3 at; public float born, radius; public MaterialPropertyBlock mpb; public bool active;
        }
        readonly List<Mark> marks = new List<Mark>();
        Material burnMat, hotMat; Texture2D atlas;
        static readonly int OpacityId = Shader.PropertyToID("_Opacity"), GainId = Shader.PropertyToID("_Gain");
        public int Active { get { int n = 0; foreach (var m in marks) if (m.active) n++; return n; } }
        public bool Ready => burnMat && atlas;

        public ElectroScorch(Material arcTemplate)
        {
            atlas = BuildAtlas();
            var sh = Shader.Find("OpenZ/ScorchDecal");
            if (sh) { burnMat = new Material(sh) { name = "ElectroScorch (runtime)", mainTexture = atlas }; burnMat.SetFloat("_Strength", 0.8f); }
            if (arcTemplate) { hotMat = new Material(arcTemplate) { name = "ElectroScorchHot (runtime)" }; hotMat.SetTexture("_MainTex", atlas); hotMat.SetFloat("_SoftDepth", 0f); }
            for (int i = 0; i < Max; i++) marks.Add(new Mark { mpb = new MaterialPropertyBlock() });
        }

        public void Dispose()
        {
            foreach (var m in marks) { if (m.burn) Object.Destroy(m.burn); if (m.hot) Object.Destroy(m.hot); }
            if (burnMat) Object.Destroy(burnMat); if (hotMat) Object.Destroy(hotMat); if (atlas) Object.Destroy(atlas);
        }

        static float Ground(float x, float z)
        {
            var s = TrackMarks.I ? TrackMarks.I.Surface : null;
            if (s != null && s.Sample(x, z, out float y, out _)) return y;
            return ZGame.I ? ZGame.I.GroundY(x, z) : 0f;
        }

        static bool Water(Vector3 p, float h)
        {
            var fx = OpenZFxPlayer.I;
            return fx ? fx.WetAt(p, h) : OpenZFxPlayer.IsWaterTile(p);
        }

        /// <summary>A figure of about `radius` m at `at` (x, z). False on water, in the air (> 2.5 m above the ground) or when not ready.</summary>
        public bool Add(Vector3 at, float radius, uint seed)
        {
            if (!Ready || float.IsNaN(at.x)) return false;
            float g = Ground(at.x, at.z);
            if (at.y - g > 2.5f || Water(new Vector3(at.x, g, at.z), g)) return false;
            Mark mk = null;
            foreach (var m in marks) if (!m.active) { mk = m; break; }
            if (mk == null) { mk = marks[0]; foreach (var m in marks) if (m.born < mk.born) mk = m; }
            var r = new ElectroBoltShape.Rng(seed);
            mk.active = true; mk.born = Time.time; mk.radius = radius * r.Range(0.8f, 1.25f); mk.at = new Vector3(at.x, g, at.z);
            if (!mk.burn) { mk.burn = new Mesh { name = "OZ_ElectroScorch" }; mk.burn.MarkDynamic(); }
            Build(mk.burn, mk.at, mk.radius, r.Range(0f, 360f), r.Int(4), false);
            if (hotMat)
            {
                if (!mk.hot) { mk.hot = new Mesh { name = "OZ_ElectroScorchHot" }; mk.hot.MarkDynamic(); }
                Build(mk.hot, mk.at, mk.radius, 0f, 0, true, mk.burn);
            }
            return true;
        }

        static readonly List<Vector3> bv = new List<Vector3>(441); static readonly List<Vector2> buv = new List<Vector2>(441);
        static readonly List<Color> bcol = new List<Color>(441); static readonly List<Vector4> b1 = new List<Vector4>(441), b2 = new List<Vector4>(441);
        static readonly List<int> btri = new List<int>(2400);

        /// <summary>ScorchDecals' grid (7-21 vertices a side, split along the diagonal that follows the ground). hot = the same grid for
        /// OpenZ/ElectroArc mode 3 (world-space vertices, cyan glow stream), copied from the burn mesh.</summary>
        static void Build(Mesh m, Vector3 c, float radius, float rotDeg, int variant, bool hot, Mesh from = null)
        {
            if (hot && from)
            {   // same vertices / uv as the burn grid, in world space, with the arc shader's streams
                from.GetVertices(bv); from.GetUVs(0, buv); var cols = new List<Color>(); from.GetColors(cols);
                b1.Clear(); b2.Clear(); bcol.Clear();
                for (int i = 0; i < bv.Count; i++)
                {
                    bv[i] += c + Vector3.up * 0.01f;
                    bcol.Add(new Color(0.12f, 0.62f, 1f, cols.Count > i ? cols[i].a : 1f));
                    b1.Add(new Vector4(0.3f, 3.5f, 2.6f, 0f)); b2.Add(new Vector4(3f, 0f, 0f, 0f));
                }
                btri.Clear(); from.GetTriangles(btri, 0);
                m.Clear(); m.SetVertices(bv); m.SetUVs(0, buv); m.SetColors(bcol); m.SetUVs(1, b1); m.SetUVs(2, b2); m.SetTriangles(btri, 0); m.RecalculateBounds();
                return;
            }
            float half = radius;
            int n = Mathf.Clamp(Mathf.CeilToInt(2f * half / 0.35f) + 1, 7, 21);
            bv.Clear(); buv.Clear(); bcol.Clear(); btri.Clear();
            var rot = Quaternion.Euler(0, rotDeg, 0);
            float u0 = (variant % 2) * 0.5f, v0 = (variant / 2) * 0.5f, pad = 2f / (2 * Tile);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float fx = i / (n - 1f) * 2f - 1f, fz = j / (n - 1f) * 2f - 1f;
                    var local = rot * new Vector3(fx * half, 0, fz * half);
                    float x = c.x + local.x, z = c.z + local.z, h = Ground(x, z);
                    bool step = Mathf.Abs(h - c.y) > 1.4f, wet = Water(new Vector3(x, h, z), h);
                    float rim = Mathf.Max(Mathf.Abs(fx), Mathf.Abs(fz));
                    float a = step || wet ? 0f : (1f - PopOut.Smooth(rim, 0.86f, 1f)) * (1f - PopOut.Smooth(Mathf.Abs(h - c.y), 0.8f, 1.4f));
                    bv.Add(new Vector3(local.x, (step ? c.y : h) - c.y + Lift, local.z));
                    buv.Add(new Vector2(u0 + pad + (fx * 0.5f + 0.5f) * (0.5f - 2 * pad), v0 + pad + (fz * 0.5f + 0.5f) * (0.5f - 2 * pad)));
                    bcol.Add(new Color(1f, 1f, 1f, a));
                }
            for (int j = 0; j < n - 1; j++)
                for (int i = 0; i < n - 1; i++)
                {
                    int k = j * n + i;
                    if (Mathf.Abs(bv[k].y - bv[k + n + 1].y) <= Mathf.Abs(bv[k + 1].y - bv[k + n].y))
                    { btri.Add(k); btri.Add(k + n); btri.Add(k + n + 1); btri.Add(k); btri.Add(k + n + 1); btri.Add(k + 1); }
                    else { btri.Add(k); btri.Add(k + n); btri.Add(k + 1); btri.Add(k + 1); btri.Add(k + n); btri.Add(k + n + 1); }
                }
            m.Clear(); m.SetVertices(bv); m.SetUVs(0, buv); m.SetColors(bcol); m.SetTriangles(btri, 0); m.RecalculateBounds();
        }

        public void Draw()
        {
            float now = Time.time;
            foreach (var m in marks)
            {
                if (!m.active) continue;
                float age = now - m.born;
                if (age >= Life) { m.active = false; continue; }
                if (burnMat)
                {
                    float op = PopOut.Smooth(age, 0f, 0.25f) * (1f - PopOut.Smooth(age, Life * FadeAt, Life));
                    m.mpb.SetFloat(OpacityId, op);
                    Graphics.DrawMesh(m.burn, Matrix4x4.Translate(m.at), burnMat, 0, null, 0, m.mpb, ShadowCastingMode.Off, false);
                }
                if (hotMat && m.hot && age < HotSeconds)
                {
                    float k = Mathf.Exp(-age / 0.16f) * (0.8f + 0.2f * Mathf.Sin(age * 90f));
                    m.mpb.SetFloat(GainId, k);                                                  // one block per mark: ScorchDecal ignores _Gain, ElectroArc _Opacity
                    Graphics.DrawMesh(m.hot, Matrix4x4.identity, hotMat, 0, null, 0, m.mpb, ShadowCastingMode.Off, false);
                }
            }
        }

        // ------------------------------------------------------------------ procedural Lichtenberg atlas (4 variants, 2 x 2 tiles)
        /// <summary>Each tile: 5-7 main channels from the centre, each a jittered walk (3 px steps, +-22 deg turns) that forks with a
        /// falling probability (30-55 deg, thinner, shorter), stamped as soft discs into a burn buffer; a small charred core; blurred
        /// once. rgb = soot tint (dark blue-grey), alpha = burn.</summary>
        static Texture2D BuildAtlas()
        {
            int W = Tile * 2;
            var buf = new float[W * W];
            for (int k = 0; k < 4; k++)
            {
                var r = new ElectroBoltShape.Rng(0xB5297A4Du + (uint)k * 7919u);
                int ox = (k % 2) * Tile, oy = (k / 2) * Tile; float cx = ox + Tile * 0.5f, cy = oy + Tile * 0.5f;
                int mains = 5 + r.Int(3);
                float a0 = r.Next() * 6.283f;
                for (int b = 0; b < mains; b++)
                    Branch(buf, W, ox, oy, ref r, cx, cy, a0 + b * 6.283f / mains + r.Range(-0.3f, 0.3f), Tile * r.Range(0.34f, 0.46f), 3.2f, 0);
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        float dx = (x - Tile * 0.5f) / (Tile * 0.5f), dy = (y - Tile * 0.5f) / (Tile * 0.5f), d = Mathf.Sqrt(dx * dx + dy * dy);
                        int i = (oy + y) * W + ox + x;
                        buf[i] = Mathf.Max(buf[i], Mathf.Clamp01(1f - d / 0.12f) * 0.95f);            // charred strike point
                        buf[i] = Mathf.Max(buf[i], Mathf.Clamp01(1f - d / 0.34f) * 0.18f);            // faint singe around it
                    }
            }
            var o = new float[W * W];
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                {
                    float s = 0f, w = 0f;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int xx = x + i, yy = y + j; if (xx < 0 || yy < 0 || xx >= W || yy >= W) continue;
                            float ww = i == 0 && j == 0 ? 4f : i == 0 || j == 0 ? 2f : 1f;
                            s += buf[yy * W + xx] * ww; w += ww;
                        }
                    o[y * W + x] = s / w;
                }
            var tex = new Texture2D(W, W, TextureFormat.RGBA32, true, false) { name = "OZ_Lichtenberg", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var px = new Color32[W * W];
            for (int i = 0; i < px.Length; i++)
            {
                float a = Mathf.Clamp01(o[i] * 1.25f);
                px[i] = new Color32(26, 28, 34, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px); tex.Apply(true, true);
            return tex;
        }

        static void Branch(float[] buf, int W, int ox, int oy, ref ElectroBoltShape.Rng r, float x, float y, float ang, float len, float width, int depth)
        {
            float step = 3f, walked = 0f;
            while (walked < len && width > 0.35f)
            {
                ang += r.Range(-0.38f, 0.38f);
                float nx = x + Mathf.Cos(ang) * step, ny = y + Mathf.Sin(ang) * step;
                Stamp(buf, W, ox, oy, nx, ny, width * (1f - 0.55f * walked / len));
                x = nx; y = ny; walked += step;
                if (depth < 4 && r.Next() < (depth == 0 ? 0.10f : 0.07f))
                {
                    float side = r.Next() < 0.5f ? -1f : 1f;
                    Branch(buf, W, ox, oy, ref r, x, y, ang + side * r.Range(0.5f, 0.95f), (len - walked) * r.Range(0.35f, 0.7f), width * 0.62f, depth + 1);
                }
            }
        }

        static void Stamp(float[] buf, int W, int ox, int oy, float x, float y, float rad)
        {
            int x0 = Mathf.Max(ox, Mathf.FloorToInt(x - rad - 1)), x1 = Mathf.Min(ox + Tile - 1, Mathf.CeilToInt(x + rad + 1));
            int y0 = Mathf.Max(oy, Mathf.FloorToInt(y - rad - 1)), y1 = Mathf.Min(oy + Tile - 1, Mathf.CeilToInt(y + rad + 1));
            for (int j = y0; j <= y1; j++)
                for (int i = x0; i <= x1; i++)
                {
                    float dx = i + 0.5f - x, dy = j + 0.5f - y, d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(rad + 0.5f - d);
                    // keep the tile edge clean (the grid rim fades too)
                    float ex = Mathf.Min(i - ox, ox + Tile - 1 - i), ey = Mathf.Min(j - oy, oy + Tile - 1 - j);
                    a *= Mathf.Clamp01(Mathf.Min(ex, ey) / 6f);
                    int k = j * W + i; if (a > buf[k]) buf[k] = a;
                }
        }
    }
}
