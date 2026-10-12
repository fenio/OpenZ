using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The inside of a ruin as a collapsed building (on top of BuildingBreaker.Shell.cs: same objects, same life time).
    /// <list type="bullet">
    /// <item><b>Surface</b>: two textures generated once (slab concrete: slabs with dark seams, hairline cracks, stains, dust;
    /// broken concrete: aggregate stones and break lines). The floor and the inner wall faces take the slab texture, the broken
    /// wall tops the aggregate; all keep the level's concrete tint. Vertex tones add the large shapes: soot along the standing
    /// remains, the blast's scorch in the middle, dust drifts, soot at the foot of the inner walls.</item>
    /// <item><b>Floor</b> (one mesh per building): the draped grid of the plain floor with low mounds near the remains (none at
    /// the outline, low in the middle) and a few tilted slab pieces.</item>
    /// <item><b>Rubble</b> (one mesh per building, three materials): chunk library pieces baked into it (heaps and leaning slabs
    /// along the remains, mixed sizes, partly sunk; rough blocks when the library meshes are not readable) and reinforcing rods:
    /// bent stubs out of the broken wall tops, loose bent rods, bent rod grids.</item>
    /// </list>
    /// Placement comes from a seed of the building: the same every time for one building, different between buildings.
    /// Visual only: no colliders, nothing the rules read. Floor and rubble are drawn after the ground marks.
    /// </summary>
    public partial class BuildingBreaker
    {
        public static bool RuinDetail = true;
        /// <summary>Metres per repeat of the slab texture: on the floor, on the inner wall faces.</summary>
        public static float RuinTexMetres = 8f, RuinWallTexMetres = 5f;
        /// <summary>How far the base tones are taken toward soot with the detail on (floor, broken tops, inner wall faces); the vertex tones darken them further.</summary>
        public static float RuinFloorSoot = 0.3f, RuinTopSoot = 0.1f, RuinInnerSoot = 0.24f;
        /// <summary>Model parts higher than this over the model's base (m) keep rubble away; mound height (m); the broken tops' ragged
        /// inner edge (fraction of the wall thickness); the inner walls' tone at the floor (1 = none).</summary>
        public static float RuinSolidHeight = 0.5f, RuinMoundHeight = 0.42f, RuinCapRag = 0.4f, RuinWallFootTone = 0.5f;
        /// <summary>Rod radius (m): single rods (the chunk library's own rods are 0.04-0.05), grid rods; grid spacing (m).</summary>
        public static float RuinRodRadius = 0.045f, RuinGridRodRadius = 0.04f, RuinGridSpacing = 0.45f;
        /// <summary>Most triangles of one rubble mesh (rods included): buildings, forts.</summary>
        public static int RuinTriCap = 7000, RuinTriCapFort = 12000;
        public static Color RuinRebarTintL02 = new Color(0.55f, 0.5f, 0.46f), RuinRebarTintL17 = new Color(1f, 0.86f, 0.74f);
        public static Color RuinRebarFlatL02 = new Color(0.4f, 0.19f, 0.07f), RuinRebarFlatL17 = new Color(0.2f, 0.17f, 0.15f);
        /// <summary>The rubble is drawn right after the floor (both after the ground marks).</summary>
        public const int RuinRubbleQueue = ShellFloorQueue + 1;
        public static string LastRuinFloorStats = "";

        static Texture2D ruinSlabTex, ruinBrokenTex;
        static readonly Dictionary<Material, Material> ruinRubbleMats = new Dictionary<Material, Material>();
        static readonly Dictionary<Mesh, RuinSrc> ruinSrc = new Dictionary<Mesh, RuinSrc>();

        // ------------------------------------------------------------------------------------------------ noise, tones

        static int RuinMod(int a, int n) { int r = a % n; return r < 0 ? r + n : r; }

        static float RuinHash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393) ^ (uint)(y * 668265263) ^ (uint)(seed * 1442695041);
                h = (h ^ (h >> 15)) * 2246822519u; h = (h ^ (h >> 13)) * 3266489917u; h ^= h >> 16;
                return (h & 0xFFFFFFu) / 16777215f;
            }
        }

        /// <summary>The hash of every point of an n x n lattice (texture building: read from the table instead of hashed per texel).
        /// One table per seed (the texture layers use seeds below 256, each with one lattice size).</summary>
        static readonly float[][] ruinLattices = new float[256][];
        static float[] RuinLattice(int n, int seed)
        {
            var t = ruinLattices[seed & 255];
            if (t != null && t.Length == n * n) return t;
            t = new float[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++) t[y * n + x] = RuinHash(x, y, seed);
            ruinLattices[seed & 255] = t;
            return t;
        }

        /// <summary>0 below e0, 1 above e1, smooth between.</summary>
        static float RuinSS(float e0, float e1, float x) { float t = Mathf.Clamp01((x - e0) / Mathf.Max(1e-6f, e1 - e0)); return t * t * (3f - 2f * t); }

        static float RuinRange(System.Random rnd, float a, float b) => a + (b - a) * (float)rnd.NextDouble();

        /// <summary>Value noise 0-1 that repeats every `period` lattice steps.</summary>
        static float RuinNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y); float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
            int xa = RuinMod(x0, period), xb = RuinMod(x0 + 1, period), ya = RuinMod(y0, period), yb = RuinMod(y0 + 1, period);
            if (period <= 256)
            {   // a small repeating lattice (the textures): read from the table
                var t = RuinLattice(period, seed);
                return Mathf.Lerp(Mathf.Lerp(t[ya * period + xa], t[ya * period + xb], fx), Mathf.Lerp(t[yb * period + xa], t[yb * period + xb], fx), fy);
            }
            return Mathf.Lerp(Mathf.Lerp(RuinHash(xa, ya, seed), RuinHash(xb, ya, seed), fx), Mathf.Lerp(RuinHash(xa, yb, seed), RuinHash(xb, yb, seed), fx), fy);
        }

        /// <summary>Cell noise on an n x n lattice that repeats over 0-1: distance to the nearest and second nearest point (in cells), the nearest cell's id.</summary>
        static void RuinVoronoi(float u, float v, int n, int seed, out float f1, out float f2, out int id)
        {
            float x = u * n, y = v * n; int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float[] jx = RuinLattice(n, seed), jy = RuinLattice(n, seed + 17);
            float b1 = 81f, b2 = 81f; id = 0;
            for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
            {
                int gx = cx + i, gy = cy + j, k = RuinMod(gy, n) * n + RuinMod(gx, n);
                float dx = gx + 0.15f + 0.7f * jx[k] - x, dy = gy + 0.15f + 0.7f * jy[k] - y, d = dx * dx + dy * dy;
                if (d < b1) { b2 = b1; b1 = d; id = k; } else if (d < b2) b2 = d;
            }
            f1 = Mathf.Sqrt(b1); f2 = Mathf.Sqrt(b2);
        }

        /// <summary>A grey vertex tone for a linear-light factor (the concrete materials read vertex colours as sRGB).</summary>
        static Color32 RuinGrey(float kLinear)
        {
            byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.LinearToGammaSpace(Mathf.Clamp01(kLinear)) * 255f), 0, 255);
            return new Color32(g, g, g, 255);
        }

        /// <summary>Vertex tones for the wall vertices added since the last call: `foot` at yLo rising to `top` at yHi.</summary>
        static void RuinTone(List<Color32> pc, List<Vector3> pv, float foot, float top, float yLo, float yHi)
        {
            float span = Mathf.Max(1e-6f, yHi - yLo);
            for (int i = pc.Count; i < pv.Count; i++) pc.Add(RuinGrey(Mathf.Lerp(foot, top, Mathf.Clamp01((pv[i].y - yLo) / span))));
        }

        // ------------------------------------------------------------------------------------------------ textures

        /// <summary>Slab concrete: four slabs across with seams (some wide and dark, some thin) and a chipped light rim, hairline cracks in
        /// patches, mottling, soot stains, dust, patches of small rubble ground into the surface, a few pits.</summary>
        static float RuinSlabTexel(float u, float v)
        {
            float wu = u + 0.03f * (RuinNoise(u * 8f, v * 8f, 8, 11) - 0.5f), wv = v + 0.03f * (RuinNoise(u * 8f, v * 8f, 8, 12) - 0.5f);
            RuinVoronoi(wu, wv, 4, 21, out float f1, out float f2, out int id);
            float e = f2 - f1;
            float val = 0.78f + 0.22f * RuinHash(id, 5, 23);                                                  // every slab its own tone
            float seamK = 0.4f + 0.6f * RuinNoise(u * 6f, v * 6f, 6, 25);                                    // how open the seam is here
            val *= 1f - 0.64f * seamK * (1f - RuinSS(0.015f + 0.02f * seamK, 0.04f + 0.05f * seamK, e));     // the seam
            val += 0.05f * RuinSS(0.08f, 0.11f, e) * (1f - RuinSS(0.12f, 0.22f, e));                         // a chipped, lighter rim
            RuinVoronoi(wu + 0.37f, wv + 0.61f, 11, 31, out f1, out f2, out id);
            float gate = RuinSS(0.45f, 0.7f, RuinNoise(u * 3f, v * 3f, 3, 33));
            val *= 1f - 0.34f * gate * (1f - RuinSS(0.012f, 0.045f, f2 - f1));                               // hairline cracks, in patches
            float fbm = 0.5f * RuinNoise(u * 4f, v * 4f, 4, 41) + 0.25f * RuinNoise(u * 8f, v * 8f, 8, 42) + 0.125f * RuinNoise(u * 16f, v * 16f, 16, 43) + 0.125f * RuinNoise(u * 32f, v * 32f, 32, 44);
            val *= 0.86f + 0.24f * fbm;
            val *= 1f - 0.34f * RuinSS(0.5f, 0.8f, RuinNoise(u * 3f, v * 3f, 3, 51));                        // soot stains
            val += 0.1f * RuinSS(0.6f, 0.85f, RuinNoise(u * 2f, v * 2f, 2, 52));                             // dust
            RuinVoronoi(u + 0.13f, v + 0.29f, 88, 81, out f1, out f2, out id);                               // small rubble, in patches: stones about 9 cm, a third of the cells left as dark dust
            float grit = RuinSS(0.46f, 0.68f, RuinNoise(u * 5f, v * 5f, 5, 83));
            float stone = RuinHash(id, 9, 85) < 0.34f ? 0.62f : (0.74f + 0.5f * RuinHash(id, 10, 86)) * (0.68f + 0.32f * RuinSS(0.02f, 0.22f, f2 - f1));
            val = Mathf.Lerp(val, val * stone, 0.92f * grit);
            val *= 0.95f + 0.1f * RuinNoise(u * 96f, v * 96f, 96, 97);                                       // fine grain
            if (f1 < 0.2f && grit < 0.2f && RuinHash(id, 11, 99) < 0.07f) val *= 0.72f;                      // a few pits on the clean slabs
            return val;
        }

        /// <summary>Broken concrete: aggregate stones light and dark in a darker matrix, a few break lines.</summary>
        static float RuinBrokenTexel(float u, float v)
        {
            RuinVoronoi(u, v, 22, 61, out float f1, out float f2, out int id);
            float stone = 0.84f + 0.22f * RuinHash(id, 7, 63);
            float val = Mathf.Lerp(0.72f, stone, RuinSS(0.04f, 0.16f, f2 - f1));
            val *= 0.86f + 0.24f * RuinNoise(u * 6f, v * 6f, 6, 64);
            RuinVoronoi(u + 0.21f, v + 0.43f, 5, 65, out f1, out f2, out id);
            val *= 1f - 0.3f * (1f - RuinSS(0.015f, 0.05f, f2 - f1));
            return val;
        }

        /// <summary>The generated texture (made once): slab concrete 512 px, or broken concrete 256 px. Grey: every user tints it.</summary>
        static Texture2D RuinTex(bool broken)
        {
            var have = broken ? ruinBrokenTex : ruinSlabTex;
            if (have) return have;
            int size = broken ? 256 : 512; var px = new Color32[size * size]; var sw = System.Diagnostics.Stopwatch.StartNew();
            if (broken) RuinBrokenTexel(0.5f, 0.5f); else RuinSlabTexel(0.5f, 0.5f);                          // fills the lattice tables before the rows run side by side
            System.Threading.Tasks.Parallel.For(0, size, y =>
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size;
                    float val = broken ? RuinBrokenTexel(u, v) : RuinSlabTexel(u, v);
                    byte g = (byte)Mathf.Clamp(Mathf.RoundToInt(val * 255f), 40, 255);
                    px[y * size + x] = new Color32(g, g, g, 255);
                }
            });
            var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = broken ? "OZ_RuinBroken (generated)" : "OZ_RuinSlab (generated)", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            t.SetPixels32(px); t.Apply(true, true);
            if (broken) ruinBrokenTex = t; else ruinSlabTex = t;
            Debug.Log($"[OpenZ] ruin texture {t.name}: {size} px in {sw.Elapsed.TotalMilliseconds:0} ms");
            return t;
        }

        /// <summary>Puts a texture on one material slot of a shell renderer, keeping the tone ShellPaint set there.</summary>
        static void RuinSetMap(MeshRenderer r, int slot, Texture tex)
        {
            if (!r || !tex || slot >= r.sharedMaterials.Length) return;
            shellMpb ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(shellMpb, slot); shellMpb.SetTexture(BaseMapId, tex); r.SetPropertyBlock(shellMpb, slot);
        }

        // ------------------------------------------------------------------------------------------------ free floor, per building type

        /// <summary>Per building type, at the end of BuildShell: the floor cells free of standing remains (in the floor mask, no
        /// model part higher than RuinSolidHeight over them) and every cell's distance (m) to the nearest cell that is not free.</summary>
        static void RuinField(ShellSet s, List<int>[] cells, Vector3[] v, List<int> tl, float metres)
        {
            try
            {
                int N = ShellGrid; var b = s.box; float k = Mathf.Max(1e-4f, metres);
                s.metres = k; s.cellX = Mathf.Max(1e-3f, b.size.x * k / N); s.cellZ = Mathf.Max(1e-3f, b.size.z * k / N); s.free = 0;
                float solidY = b.min.y + RuinSolidHeight / k, dx = s.cellX, dz = s.cellZ, dd = Mathf.Sqrt(dx * dx + dz * dz);
                var d = new float[N * N];
                for (int i = 0; i < N * N; i++)
                {
                    bool solid = false; var l = cells[i];
                    if (l != null) for (int j = 0; j < l.Count && !solid; j++) { int t = l[j]; solid = v[tl[t]].y > solidY || v[tl[t + 1]].y > solidY || v[tl[t + 2]].y > solidY; }
                    bool free = s.mask[i] && !solid; d[i] = free ? 1e6f : 0f; if (free) s.free++;
                }
                // two sweeps: the distance to the nearest cell that is not free (outside the grid counts as not free)
                for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
                {
                    int i = z * N + x; if (d[i] == 0f) continue; float m = d[i];
                    m = Mathf.Min(m, (x > 0 ? d[i - 1] : 0f) + dx);
                    m = Mathf.Min(m, (z > 0 ? d[i - N] : 0f) + dz);
                    m = Mathf.Min(m, (x > 0 && z > 0 ? d[i - N - 1] : 0f) + dd);
                    m = Mathf.Min(m, (x < N - 1 && z > 0 ? d[i - N + 1] : 0f) + dd);
                    d[i] = m;
                }
                for (int z = N - 1; z >= 0; z--) for (int x = N - 1; x >= 0; x--)
                {
                    int i = z * N + x; if (d[i] == 0f) continue; float m = d[i];
                    m = Mathf.Min(m, (x < N - 1 ? d[i + 1] : 0f) + dx);
                    m = Mathf.Min(m, (z < N - 1 ? d[i + N] : 0f) + dz);
                    m = Mathf.Min(m, (x < N - 1 && z < N - 1 ? d[i + N + 1] : 0f) + dd);
                    m = Mathf.Min(m, (x > 0 && z < N - 1 ? d[i + N - 1] : 0f) + dd);
                    d[i] = m;
                }
                s.dist = d;
            }
            catch (System.Exception ex) { s.dist = null; Debug.LogWarning("[OpenZ] ruin floor field: " + ex.Message); }
        }

        // ------------------------------------------------------------------------------------------------ mesh buffer

        /// <summary>World-space vertices and up to three triangle lists (0 outer concrete, 1 broken concrete, 2 rods).</summary>
        sealed class RuinBuf
        {
            public readonly List<Vector3> v = new List<Vector3>(); public readonly List<Vector2> uv = new List<Vector2>(); public readonly List<Color32> col = new List<Color32>();
            public readonly List<int>[] tri = { new List<int>(), new List<int>(), new List<int>() };
            public int Tris => (tri[0].Count + tri[1].Count + tri[2].Count) / 3;

            public int Add(Vector3 p, Vector2 t, Color32 c) { v.Add(p); uv.Add(t); col.Add(c); return v.Count - 1; }

            /// <summary>A triangle wound to face away from `inside`.</summary>
            public void Out(int sub, int a, int b, int c, Vector3 inside)
            {
                if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), v[a] - inside) < 0f) { int t = b; b = c; c = t; }
                tri[sub].Add(a); tri[sub].Add(b); tri[sub].Add(c);
            }

            /// <summary>A triangle wound to face up.</summary>
            public void Up(int sub, int a, int b, int c)
            {
                if (Vector3.Cross(v[b] - v[a], v[c] - v[a]).y < 0f) { int t = b; b = c; c = t; }
                tri[sub].Add(a); tri[sub].Add(b); tri[sub].Add(c);
            }

            /// <summary>A bent five-sided rod along the points `p` (radius r, the last ring r * tip, a cap on the last point), list 2.</summary>
            public void Rod(List<Vector3> p, float r, float tip, Color32 tone)
            {
                int n = p.Count; if (n < 2) return;
                const int K = 5; int b0 = v.Count; float len = 0f; Vector3 nrm = Vector3.zero;
                for (int i = 0; i < n; i++)
                {
                    var dir = p[Mathf.Min(i + 1, n - 1)] - p[Mathf.Max(i - 1, 0)];
                    if (dir.sqrMagnitude < 1e-10f) dir = Vector3.up;
                    dir.Normalize();
                    if (i > 0) len += (p[i] - p[i - 1]).magnitude;
                    nrm = i == 0 ? Vector3.zero : nrm - dir * Vector3.Dot(nrm, dir);
                    if (nrm.sqrMagnitude < 1e-10f) nrm = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right);
                    nrm.Normalize(); var bin = Vector3.Cross(dir, nrm);
                    float rr = i == n - 1 ? r * tip : r;
                    for (int k = 0; k <= K; k++)
                    {
                        float a = k * Mathf.PI * 2f / K;
                        Add(p[i] + (nrm * Mathf.Cos(a) + bin * Mathf.Sin(a)) * rr, new Vector2((float)k / K, len * 3f), tone);
                    }
                }
                for (int i = 0; i + 1 < n; i++)
                {
                    var mid = (p[i] + p[i + 1]) * 0.5f; int r0 = b0 + i * (K + 1), r1 = r0 + K + 1;
                    for (int k = 0; k < K; k++) { Out(2, r0 + k, r1 + k, r1 + k + 1, mid); Out(2, r0 + k, r1 + k + 1, r0 + k + 1, mid); }
                }
                int cap = Add(p[n - 1], new Vector2(0.5f, len * 3f), tone), last = b0 + (n - 1) * (K + 1);
                for (int k = 0; k < K; k++) Out(2, cap, last + k, last + k + 1, p[n - 2]);
            }
        }

        /// <summary>The buffer as a mesh in the ruin object's space (`rt`), `subs` triangle lists.</summary>
        static Mesh RuinMesh(RuinBuf buf, Transform rt, string meshName, int subs)
        {
            if (buf.v.Count == 0 || buf.Tris == 0) return null;
            bool flip = rt.localToWorldMatrix.determinant < 0f;                                              // a mirrored model: keep the faces outward
            var lv = new List<Vector3>(buf.v.Count);
            for (int i = 0; i < buf.v.Count; i++) lv.Add(rt.InverseTransformPoint(buf.v[i]));
            var m = new Mesh { name = meshName };
            if (lv.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(lv); m.SetUVs(0, buf.uv); m.SetColors(buf.col);
            m.subMeshCount = subs;
            for (int sm = 0; sm < subs; sm++)
            {
                var t = buf.tri[sm];
                if (flip) for (int i = 0; i + 2 < t.Count; i += 3) { int k = t[i + 1]; t[i + 1] = t[i + 2]; t[i + 2] = k; }
                m.SetTriangles(t, sm, false);
            }
            m.RecalculateNormals(); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>A chunk library mesh as arrays (read once); null when the mesh is not readable.</summary>
        sealed class RuinSrc { public Vector3[] v; public Vector2[] uv; public Color32[] col; public int[][] tri; }

        static RuinSrc RuinSource(Mesh m)
        {
            if (!m) return null;
            if (ruinSrc.TryGetValue(m, out var src)) return src;
            src = null;
            if (m.isReadable)
            {
                src = new RuinSrc { v = m.vertices, uv = m.uv, col = m.colors32, tri = new int[m.subMeshCount][] };
                for (int i = 0; i < m.subMeshCount; i++) src.tri[i] = m.GetTriangles(i);
                if (src.uv == null || src.uv.Length != src.v.Length) src.uv = new Vector2[src.v.Length];
                if (src.col == null || src.col.Length != src.v.Length) src.col = null;
            }
            ruinSrc[m] = src;
            return src;
        }

        // ------------------------------------------------------------------------------------------------ one building

        /// <summary>The layout of one building's floor: where the free floor is, the mounds, and the builders of the floor and rubble meshes.</summary>
        sealed partial class RuinSite
        {
            public readonly ShellSet s; public readonly Transform rt; public readonly int seed;
            readonly ZGame game; readonly int N = ShellGrid;
            readonly float sizeM, halfMin, area, clear, cellMax, uvCos, uvSin, room; readonly Vector2 uvOff; readonly Vector3 centreW;
            readonly float[] weight, scratch; readonly List<Vector4> mounds = new List<Vector4>();                 // x, z (world), height, radius
            readonly List<Vector3> placed = new List<Vector3>(), plateSpots = new List<Vector3>();                 // x, z (world), radius
            int[] idx; float[] hW;
            int floorTris, plates, rubbleTris, rodTris, libPieces, blockPieces, leaning, stubs, loose, grids;
            /// <summary>Milliseconds spent building this floor and its rubble (the log line).</summary>
            public double ms;
            static readonly Color32 White = new Color32(255, 255, 255, 255);

            public RuinSite(ShellSet set, Transform ruin, int id)
            {
                s = set; rt = ruin; seed = id * 9973 + 39; game = ZGame.I;
                var rnd = new System.Random(seed); var b = s.box;
                centreW = rt.TransformPoint(new Vector3(b.center.x, b.min.y, b.center.z));
                sizeM = Mathf.Max(b.size.x, b.size.z) * s.metres; halfMin = 0.5f * Mathf.Min(b.size.x, b.size.z) * s.metres;
                cellMax = Mathf.Max(s.cellX, s.cellZ); area = s.free * s.cellX * s.cellZ;
                clear = sizeM * ShellWallThick + 0.5f * cellMax;                                             // from a blocked cell's centre to past the wall's inner face
                float ua = RuinRange(rnd, 0f, Mathf.PI * 2f); uvCos = Mathf.Cos(ua); uvSin = Mathf.Sin(ua);
                uvOff = new Vector2((float)rnd.NextDouble(), (float)rnd.NextDouble());
                // where pieces go: dense along the remains, denser in corners, thin in the middle
                weight = new float[N * N]; scratch = new float[N * N]; float far = 0f;
                for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
                {
                    int i = z * N + x; float d = s.dist[i]; if (d <= 0f) continue;
                    if (d > far) far = d;
                    int blocked = 0;
                    for (int k = 0; k < 8; k++)
                    {
                        int px = x + Mathf.RoundToInt(Mathf.Cos(k * Mathf.PI / 4f) * 2.4f / s.cellX), pz = z + Mathf.RoundToInt(Mathf.Sin(k * Mathf.PI / 4f) * 2.4f / s.cellZ);
                        if (px < 0 || pz < 0 || px >= N || pz >= N || s.dist[pz * N + px] <= 0f) blocked++;
                    }
                    weight[i] = (0.05f + Mathf.Exp(-d / 0.7f)) * (1f + 2.5f * Mathf.Max(0f, blocked / 8f - 0.4f));
                }
                room = Mathf.Max(0f, far - clear);                                                         // the widest free strip, from the wall's inner face to its middle (m)
                int want = Mathf.Clamp(Mathf.RoundToInt(area / 28f), 3, 9);
                for (int i = 0; i < want; i++)
                {
                    if (!Pick(rnd, Mathf.Min(0.8f, far * 0.5f), 3.5f, out float fx, out float fz)) break;
                    var w = World(fx, fz);
                    mounds.Add(new Vector4(w.x, w.z, RuinMoundHeight * RuinRange(rnd, 0.45f, 1f), Mathf.Clamp(sizeM * 0.09f, 0.9f, 2.2f) * RuinRange(rnd, 0.7f, 1.3f)));
                }
            }

            public string Stats() => $"floor {floorTris} tris ({s.cells} cells, {s.free} free, free strip {room:0.0} m, {mounds.Count} mounds, {plates} slab pieces), rubble {rubbleTris} tris: {libPieces} library + {blockPieces} block pieces ({leaning} leaning), rebar {stubs} wall stubs + {loose} loose + {grids} grids ({rodTris} tris), built in {ms:0.0} ms";

            Vector3 World(float fx, float fz) { var b = s.box; return rt.TransformPoint(new Vector3(b.min.x + b.size.x * fx / N, b.min.y, b.min.z + b.size.z * fz / N)); }

            void GridOf(Vector3 w, out float fx, out float fz)
            {
                var l = rt.InverseTransformPoint(w); var b = s.box;
                fx = (l.x - b.min.x) / Mathf.Max(1e-6f, b.size.x) * N; fz = (l.z - b.min.z) / Mathf.Max(1e-6f, b.size.z) * N;
            }

            float Dist(float fx, float fz) => fx < 0f || fz < 0f || fx >= N || fz >= N ? 0f : s.dist[Mathf.FloorToInt(fz) * N + Mathf.FloorToInt(fx)];

            /// <summary>True when free floor lies within two cells of the world point.</summary>
            bool FreeNear(Vector3 w)
            {
                GridOf(w, out float fx, out float fz); int cx = Mathf.FloorToInt(fx), cz = Mathf.FloorToInt(fz);
                for (int z = cz - 2; z <= cz + 2; z++) for (int x = cx - 2; x <= cx + 2; x++)
                    if (x >= 0 && z >= 0 && x < N && z < N && s.dist[z * N + x] > 0f) return true;
                return false;
            }

            float CornerDist(int gx, int gz)
            {
                float d = float.MaxValue;
                for (int oz = -1; oz <= 0; oz++) for (int ox = -1; ox <= 0; ox++)
                {
                    int x = gx + ox, z = gz + oz;
                    d = Mathf.Min(d, x < 0 || z < 0 || x >= N || z >= N ? 0f : s.dist[z * N + x]);
                }
                return d;
            }

            /// <summary>Unit level direction (world) from a floor point toward the nearest standing remains; zero when there is none.</summary>
            Vector3 ToWall(float fx, float fz)
            {
                float gx = (Dist(fx + 1.5f, fz) - Dist(fx - 1.5f, fz)) / s.cellX, gz = (Dist(fx, fz + 1.5f) - Dist(fx, fz - 1.5f)) / s.cellZ;
                var o = World(fx, fz); var dir = -((World(fx + 1f, fz) - o).normalized * gx + (World(fx, fz + 1f) - o).normalized * gz); dir.y = 0f;
                return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.zero;
            }

            /// <summary>A seeded free floor point whose distance to the standing remains lies in dMin-dMax (m); grid coordinates.</summary>
            bool Pick(System.Random rnd, float dMin, float dMax, out float fx, out float fz)
            {
                fx = 0f; fz = 0f; float total = 0f; int nn = N * N, last = -1;
                for (int i = 0; i < nn; i++) { float d = s.dist[i]; float w = d >= dMin && d <= dMax ? weight[i] : 0f; scratch[i] = w; total += w; if (w > 0f) last = i; }
                if (last < 0) return false;
                float pick = (float)rnd.NextDouble() * total; int hit = last;
                for (int i = 0; i < nn; i++) { if (scratch[i] <= 0f) continue; pick -= scratch[i]; if (pick <= 0f) { hit = i; break; } }
                fx = hit % N + RuinRange(rnd, 0.2f, 0.8f); fz = hit / N + RuinRange(rnd, 0.2f, 0.8f);
                return true;
            }

            static bool Free(List<Vector3> list, float x, float z, float r, float k = 0.75f)
            {
                for (int i = 0; i < list.Count; i++) { float dx = list[i].x - x, dz = list[i].y - z, m = k * (r + list[i].z); if (dx * dx + dz * dz < m * m) return false; }
                return true;
            }

            float Rc(float wx, float wz) => Mathf.Sqrt((wx - centreW.x) * (wx - centreW.x) + (wz - centreW.z) * (wz - centreW.z)) / Mathf.Max(0.5f, halfMin);

            /// <summary>Height added to the plain floor (m, never negative): mounds and a low roll; zero at the remains, low in the middle.</summary>
            float Relief(float wx, float wz, float d)
            {
                if (d <= 0f) return 0f;
                float h = 0f;
                for (int i = 0; i < mounds.Count; i++)
                {
                    var m = mounds[i]; float dx = wx - m.x, dz = wz - m.y, q = (dx * dx + dz * dz) / (m.w * m.w);
                    if (q < 6f) h += m.z * Mathf.Exp(-q);
                }
                h = Mathf.Min(h, RuinMoundHeight * 1.2f) + 0.07f * RuinNoise(wx * 0.9f + 31.7f, wz * 0.9f + 11.3f, 65536, seed & 1023);
                return h * RuinSS(0.2f, 1.4f, d) * Mathf.Lerp(0.3f, 1f, RuinSS(0.25f, 0.6f, Rc(wx, wz)));
            }

            /// <summary>The floor's large tones (linear factor): soot along the remains, the scorch in the middle, mottling, dust drifts.</summary>
            float ToneK(float wx, float wz, float d)
            {
                float k = Mathf.Lerp(0.42f, 1f, RuinSS(0f, Mathf.Clamp(sizeM * 0.16f, 1.2f, 3f), d));
                float rc = Rc(wx, wz) * (0.75f + 0.5f * RuinNoise(wx * 0.35f + 7.1f, wz * 0.35f + 3.3f, 65536, 77 + (seed & 255)));
                k *= 1f - 0.5f * (1f - RuinSS(0.22f, 0.62f, rc));
                k *= 0.74f + 0.26f * RuinNoise(wx * 0.8f + 1.7f, wz * 0.8f + 9.2f, 65536, 78);
                k *= 1f + 0.35f * RuinSS(0.55f, 0.85f, RuinNoise(wx * 0.22f + 5.5f, wz * 0.22f + 2.1f, 65536, 79 + (seed & 255)));
                return Mathf.Clamp(k, 0.2f, 1f);
            }

            float PieceTone(float wx, float wz, System.Random rnd) => RuinRange(rnd, 0.78f, 1f) * (1f - 0.35f * (1f - RuinSS(0.22f, 0.62f, Rc(wx, wz))));

            Vector2 Uv(float wx, float wz) { float k = 1f / Mathf.Max(0.5f, RuinTexMetres); return new Vector2((wx * uvCos - wz * uvSin) * k + uvOff.x, (wx * uvSin + wz * uvCos) * k + uvOff.y); }

            /// <summary>World height of the built floor at a grid point (the bare draped height off the floor).</summary>
            float FloorY(float fx, float fz)
            {
                int W = N + 1, x = Mathf.Clamp(Mathf.FloorToInt(fx), 0, N - 1), z = Mathf.Clamp(Mathf.FloorToInt(fz), 0, N - 1);
                float h00 = hW[z * W + x], h10 = hW[z * W + x + 1], h01 = hW[(z + 1) * W + x], h11 = hW[(z + 1) * W + x + 1];
                if (float.IsNaN(h00) || float.IsNaN(h10) || float.IsNaN(h01) || float.IsNaN(h11))
                {
                    var w = World(fx, fz); float gy = game ? game.GroundY(w.x, w.z) : w.y;
                    return Mathf.Max(w.y, gy) + ShellFloorLift;
                }
                float tx = Mathf.Clamp01(fx - x), tz = Mathf.Clamp01(fz - z);
                return Mathf.Lerp(Mathf.Lerp(h00, h10, tx), Mathf.Lerp(h01, h11, tx), tz);
            }

            float FloorYW(Vector3 w) { GridOf(w, out float fx, out float fz); return FloorY(fx, fz); }

            // -------------------------------------------------------------------------------------------- floor

            int Vert(RuinBuf buf, int gx, int gz)
            {
                int k = gz * (N + 1) + gx; if (idx[k] >= 0) return idx[k];
                var w = World(gx, gz); float gy = game ? game.GroundY(w.x, w.z) : w.y, d = CornerDist(gx, gz);
                w.y = Mathf.Max(w.y, gy) + ShellFloorLift + Relief(w.x, w.z, d);
                hW[k] = w.y; idx[k] = buf.Add(w, Uv(w.x, w.z), RuinGrey(ToneK(w.x, w.z, d)));
                return idx[k];
            }

            /// <summary>The floor: the masked cells draped on the terrain (never below the model's base) with the relief, then the slab pieces.</summary>
            public Mesh Floor()
            {
                int W = N + 1; var buf = new RuinBuf();
                idx = new int[W * W]; hW = new float[W * W];
                for (int i = 0; i < idx.Length; i++) { idx[i] = -1; hW[i] = float.NaN; }
                for (int z = 0; z < N; z++) for (int x = 0; x < N; x++)
                {
                    if (!s.mask[z * N + x]) continue;
                    int a = Vert(buf, x, z), c = Vert(buf, x + 1, z), d = Vert(buf, x + 1, z + 1), g = Vert(buf, x, z + 1);
                    buf.Up(0, a, g, d); buf.Up(0, a, d, c);
                }
                if (buf.Tris == 0) return null;
                Plates(buf);
                floorTris = buf.Tris;
                return RuinMesh(buf, rt, "ruin_floor", 1);
            }

            /// <summary>Tilted slab pieces: an uneven five to seven sided plate, one side risen, the other under the floor; its sides reach down into the floor.</summary>
            void Plates(RuinBuf buf)
            {
                var rnd = new System.Random(seed + 101);
                int want = Mathf.Clamp(Mathf.RoundToInt(area / 40f), 2, 7);
                for (int p = 0; p < want; p++)
                {
                    float r = Mathf.Clamp(sizeM * 0.085f, 0.8f, 1.8f) * RuinRange(rnd, 0.7f, 1.2f);
                    if (r > room) r = room;                                                                 // a narrow free strip (the fort's ring): a smaller plate
                    if (r < 0.45f) continue;
                    if (!Pick(rnd, clear + r, 1e6f, out float fx, out float fz)) continue;
                    var c = World(fx, fz); if (!Free(plateSpots, c.x, c.z, r * 1.2f)) continue;
                    c.y = FloorY(fx, fz);
                    float slope = Mathf.Tan(RuinRange(rnd, 5f, 14f) * Mathf.Deg2Rad), yaw = RuinRange(rnd, 0f, Mathf.PI * 2f), thick = RuinRange(rnd, 0.2f, 0.32f);
                    var rise = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                    float ta = RuinRange(rnd, 0f, Mathf.PI * 2f), tc = Mathf.Cos(ta), ts = Mathf.Sin(ta), tk = 1f / Mathf.Max(0.5f, RuinTexMetres), ou = (float)rnd.NextDouble(), ov = (float)rnd.NextDouble();
                    Vector2 PUv(Vector3 w) => new Vector2((w.x * tc - w.z * ts) * tk + ou, (w.x * ts + w.z * tc) * tk + ov);
                    float topK = Mathf.Min(1f, ToneK(c.x, c.z, Dist(fx, fz)) * 1.25f);
                    Color32 topTone = RuinGrey(topK), sideTone = RuinGrey(topK * 0.7f);
                    int corners = 5 + rnd.Next(3); float a0 = RuinRange(rnd, 0f, Mathf.PI * 2f);
                    var top = new Vector3[corners];
                    for (int k = 0; k < corners; k++)
                    {
                        float a = a0 + k * Mathf.PI * 2f / corners, rr = r * RuinRange(rnd, 0.7f, 1f);
                        var o = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                        top[k] = new Vector3(c.x + o.x, c.y + thick * 0.5f + Vector3.Dot(o, rise) * slope, c.z + o.z);
                    }
                    var mid = new Vector3(c.x, c.y + thick * 0.5f, c.z); var inside = mid - Vector3.up * (thick + r * slope);
                    int ci = buf.Add(mid, PUv(mid), topTone), first = buf.v.Count;
                    for (int k = 0; k < corners; k++) buf.Add(top[k], PUv(top[k]), topTone);
                    for (int k = 0; k < corners; k++) buf.Up(0, ci, first + k, first + (k + 1) % corners);
                    for (int k = 0; k < corners; k++)
                    {
                        Vector3 t0 = top[k], t1 = top[(k + 1) % corners];
                        var b0 = new Vector3(t0.x, Mathf.Min(FloorYW(t0) - 0.06f, t0.y - thick), t0.z); var b1 = new Vector3(t1.x, Mathf.Min(FloorYW(t1) - 0.06f, t1.y - thick), t1.z);
                        int i0 = buf.Add(t0, PUv(t0), sideTone), i1 = buf.Add(t1, PUv(t1), sideTone);
                        int i2 = buf.Add(b1, PUv(t1) + new Vector2(0f, (t1.y - b1.y) * tk), sideTone), i3 = buf.Add(b0, PUv(t0) + new Vector2(0f, (t0.y - b0.y) * tk), sideTone);
                        buf.Out(0, i0, i1, i2, inside); buf.Out(0, i0, i2, i3, inside);
                    }
                    plateSpots.Add(new Vector3(c.x, c.z, r)); plates++;
                }
            }

            // -------------------------------------------------------------------------------------------- rubble and rods

            /// <summary>The rubble mesh: rods first (wall stubs, grids, loose rods), then leaning slabs, heaps and mixed pieces up to the triangle cap.</summary>
            public Mesh Rubble(bool lib, bool fort)
            {
                var buf = new RuinBuf(); var rnd = new System.Random(seed + 202);
                int cap = fort ? RuinTriCapFort : RuinTriCap; float per100 = area / 100f;
                Stubs(buf, rnd); Grids(buf, rnd); LooseRods(buf, rnd);
                rodTris = buf.Tris;
                int nLean = Mathf.Clamp(Mathf.RoundToInt(1.3f * per100), 1, 5), nHeap = Mathf.Clamp(Mathf.RoundToInt(2.2f * per100), 2, 5);
                int nM = Mathf.Clamp(Mathf.RoundToInt(6f * per100), 4, 16), nS = Mathf.Clamp(Mathf.RoundToInt(18f * per100), 10, 40), nX = Mathf.Clamp(Mathf.RoundToInt(24f * per100), 14, 48);
                string leanCls = sizeM > 14f ? "L" : "M";
                for (int i = 0; i < nLean && buf.Tris < cap - 460; i++) Lean(buf, rnd, lib, leanCls);
                for (int i = 0; i < nHeap && buf.Tris < cap - 740; i++) Rest(buf, rnd, lib, "HEAP", i);
                for (int i = 0; i < nM && buf.Tris < cap - 320; i++) Rest(buf, rnd, lib, "M", i);
                for (int i = 0; i < nS && buf.Tris < cap - 120; i++) Rest(buf, rnd, lib, "S", i);
                for (int i = 0; i < nX && buf.Tris < cap - 60; i++) Rest(buf, rnd, lib, "XS", i);
                CoreRubble(buf, lib);                                                                       // rubble and bent rods on a broken inner block (BuildingBreaker.RuinCore.cs)
                rubbleTris = buf.Tris;
                return RuinMesh(buf, rt, "ruin_rubble", 3);
            }

            static Vector3 SizeOf(ConcreteChunkLib.Entry e, Vector3 fallback) => e != null && e.size_xyz != null && e.size_xyz.Length > 2 ? new Vector3(e.size_xyz[0], e.size_xyz[1], e.size_xyz[2]) : fallback;

            /// <summary>One piece at the world matrix `m`: the library chunk's mesh when it is readable, else a rough block of `size`; `k` = its tone.</summary>
            void AddPiece(RuinBuf buf, ConcreteChunkLib.Entry e, Matrix4x4 m, Vector3 size, float k, System.Random rnd)
            {
                var src = e != null ? RuinSource(e.mesh) : null;
                if (src == null) { Block(buf, m, size, k, rnd); blockPieces++; return; }
                bool flip = m.determinant < 0f; int b0 = buf.v.Count; float g = Mathf.LinearToGammaSpace(Mathf.Clamp01(k));
                for (int i = 0; i < src.v.Length; i++)
                {
                    Color32 c = src.col != null ? src.col[i] : White;
                    buf.Add(m.MultiplyPoint3x4(src.v[i]), src.uv[i], new Color32((byte)(c.r * g), (byte)(c.g * g), (byte)(c.b * g), 255));
                }
                for (int sm = 0; sm < src.tri.Length; sm++)
                {
                    int kind = e.slotKind != null && sm < e.slotKind.Length ? e.slotKind[sm] : 1;
                    var dst = buf.tri[kind == 0 ? 0 : kind == 2 ? 2 : 1]; var t = src.tri[sm];
                    for (int i = 0; i + 2 < t.Length; i += 3) { dst.Add(b0 + t[i]); dst.Add(b0 + t[flip ? i + 2 : i + 1]); dst.Add(b0 + t[flip ? i + 1 : i + 2]); }
                }
                libPieces++;
            }

            /// <summary>A rough block: a box with every corner pulled in by its own amount; the top keeps the cast face, the other faces are broken.</summary>
            void Block(RuinBuf buf, Matrix4x4 m, Vector3 size, float k, System.Random rnd)
            {
                var h = size * 0.5f; var centre = m.MultiplyPoint3x4(Vector3.zero); var tone = RuinGrey(k); var p = new Vector3[8];
                for (int i = 0; i < 8; i++)
                {
                    var q = new Vector3((i & 1) == 0 ? -h.x : h.x, (i & 2) == 0 ? -h.y : h.y, (i & 4) == 0 ? -h.z : h.z);
                    q.x *= RuinRange(rnd, 0.62f, 1f); q.y *= RuinRange(rnd, 0.7f, 1f); q.z *= RuinRange(rnd, 0.62f, 1f);
                    p[i] = m.MultiplyPoint3x4(q);
                }
                int[,] f = { { 0, 2, 6, 4 }, { 1, 3, 7, 5 }, { 0, 1, 5, 4 }, { 2, 3, 7, 6 }, { 0, 1, 3, 2 }, { 4, 5, 7, 6 } };
                float uk = 0.5f;
                for (int j = 0; j < 6; j++)
                {
                    int sub = j == 3 ? 0 : 1; float fu = (p[f[j, 1]] - p[f[j, 0]]).magnitude * uk, fv = (p[f[j, 3]] - p[f[j, 0]]).magnitude * uk;
                    int i0 = buf.Add(p[f[j, 0]], new Vector2(0f, 0f), tone), i1 = buf.Add(p[f[j, 1]], new Vector2(fu, 0f), tone);
                    int i2 = buf.Add(p[f[j, 2]], new Vector2(fu, fv), tone), i3 = buf.Add(p[f[j, 3]], new Vector2(0f, fv), tone);
                    buf.Out(sub, i0, i1, i2, centre); buf.Out(sub, i0, i2, i3, centre);
                }
            }

            /// <summary>A piece lying on the floor: in its own resting pose, a little tilted and sunk (one in three deep), or tumbled.</summary>
            void Rest(RuinBuf buf, System.Random rnd, bool lib, string cls, int n)
            {
                ConcreteChunkLib.Entry e = null;
                if (lib) e = cls == "HEAP" ? ConcreteChunkLib.Heap(n) : ConcreteChunkLib.Pick(rnd, cls);
                float len = cls == "HEAP" ? 1.9f : cls == "L" ? 2.3f : cls == "M" ? 1.2f : cls == "S" ? 0.6f : 0.32f;
                Vector3 size = SizeOf(e, new Vector3(len, len * (cls == "HEAP" ? 0.3f : 0.5f), len * 0.75f) * RuinRange(rnd, 0.85f, 1.15f));
                var sc = e != null ? ConcreteChunkLib.RandomScale(rnd, e.max_scale) : Vector3.one;
                float r = 0.45f * Vector3.Scale(size, sc).magnitude, hy = size.y * sc.y;
                // how close to the remains its centre may lie (part of it may sit inside the wall's thickness, never through it), and
                // how far small pieces may overlap the pieces already lying there (piles along the walls)
                bool small = cls == "S" || cls == "XS";
                float need = clear + r * (cls == "HEAP" ? 0.75f : small ? 0.55f : 0.7f), pile = small ? 0.4f : 0.75f;
                if (need > clear + room) need = clear + room * 0.8f;
                for (int tries = 0; tries < 10; tries++)
                {
                    if (!Pick(rnd, need, 1e6f, out float fx, out float fz)) return;
                    var w = World(fx, fz); if (!Free(placed, w.x, w.z, r, pile)) continue;
                    float fy = FloorY(fx, fz), cy; Quaternion q;
                    if (cls != "HEAP" && rnd.NextDouble() < 0.3)
                    {   // tumbled: any side up, part of it in the rubble
                        q = ConcreteChunkLib.RandomRotation(rnd); cy = fy + 0.2f * Mathf.Min(size.x * sc.x, Mathf.Min(hy, size.z * sc.z));
                    }
                    else
                    {   // resting in its own pose
                        float sink = cls == "HEAP" ? 0.05f : hy * (rnd.NextDouble() < 0.35 ? RuinRange(rnd, 0.38f, 0.55f) : RuinRange(rnd, 0.1f, 0.3f));
                        q = Quaternion.Euler(RuinRange(rnd, -9f, 9f), RuinRange(rnd, 0f, 360f), RuinRange(rnd, -9f, 9f));
                        cy = fy - (e != null ? e.rest_bottom_y : -0.5f * size.y) * sc.y - sink;
                    }
                    if (rnd.NextDouble() < 0.5) sc.x = -sc.x;
                    AddPiece(buf, e, Matrix4x4.TRS(new Vector3(w.x, cy, w.z), q, sc), size, PieceTone(w.x, w.z, rnd), rnd);
                    placed.Add(new Vector3(w.x, w.z, r));
                    return;
                }
            }

            /// <summary>A long piece leaning against the standing remains: its foot in the floor, its head at the wall's inner face.</summary>
            void Lean(RuinBuf buf, System.Random rnd, bool lib, string cls)
            {
                ConcreteChunkLib.Entry e = null;
                var pool = lib ? ConcreteChunkLib.Pool(cls) : null;
                if (pool != null && pool.Count > 0)
                    for (int t = 0; t < 12 && e == null; t++) { var cand = pool[rnd.Next(pool.Count)]; if (t >= 8 || cand.kind == "slab" || cand.kind == "shard" || cand.kind == "wedge" || cand.kind == "roof") e = cand; }
                Vector3 size = SizeOf(e, cls == "L" ? new Vector3(2.3f, 0.5f, 1.5f) : new Vector3(1.3f, 0.35f, 0.9f));
                var sc = e != null ? ConcreteChunkLib.RandomScale(rnd, e.max_scale) : Vector3.one;
                bool longX = size.x * sc.x >= size.z * sc.z;
                float halfLen = 0.5f * (longX ? size.x * sc.x : size.z * sc.z), halfWide = 0.5f * (longX ? size.z * sc.z : size.x * sc.x), halfThick = 0.5f * size.y * sc.y;
                float tilt = RuinRange(rnd, 48f, 66f) * Mathf.Deg2Rad, reach = halfLen * Mathf.Cos(tilt) + halfThick * Mathf.Sin(tilt);
                float d0 = Mathf.Max(0f, clear - cellMax) + reach;
                for (int tries = 0; tries < 10; tries++)
                {
                    if (!Pick(rnd, d0, d0 + 1.5f * cellMax, out float fx, out float fz)) return;
                    var toWall = ToWall(fx, fz); if (toWall == Vector3.zero) continue;
                    var c = World(fx, fz); var side = new Vector3(-toWall.z, 0f, toWall.x);
                    if (!Free(placed, c.x, c.z, halfWide)) continue;
                    GridOf(c + side * halfWide, out float ax, out float az); GridOf(c - side * halfWide, out float bx, out float bz);
                    if (Dist(ax, az) < d0 - 1.5f * cellMax || Dist(bx, bz) < d0 - 1.5f * cellMax) continue;       // a corner: its shoulders would enter the next wall
                    float cy = FloorYW(c - toWall * (halfLen * Mathf.Cos(tilt))) + halfLen * Mathf.Sin(tilt) + halfThick * Mathf.Cos(tilt) - 0.15f;
                    var fwd = toWall * Mathf.Cos(tilt) + Vector3.up * Mathf.Sin(tilt); var up = Vector3.up * Mathf.Cos(tilt) - toWall * Mathf.Sin(tilt);
                    var q = Quaternion.LookRotation(fwd, up); if (longX) q *= Quaternion.Euler(0f, -90f, 0f);
                    if (rnd.NextDouble() < 0.5) sc.x = -sc.x;
                    AddPiece(buf, e, Matrix4x4.TRS(new Vector3(c.x, cy, c.z), q, sc), size, PieceTone(c.x, c.z, rnd), rnd);
                    placed.Add(new Vector3(c.x, c.z, halfWide)); leaning++;
                    return;
                }
            }

            /// <summary>Bent rods out of the broken wall tops: about one per 2.4 m of wall, at least 1.1 m apart, bending over the inside.</summary>
            void Stubs(RuinBuf buf, System.Random rnd)
            {
                int have = s.stubPos.Count; if (have == 0) return;
                var b = s.box; int want = Mathf.Clamp(Mathf.RoundToInt(2f * (b.size.x + b.size.z) * s.metres / 2.4f), 5, 36);
                var chosen = new List<Vector3>(); var pts = new List<Vector3>();
                for (int t = 0; t < want * 12 && chosen.Count < want; t++)
                {
                    int i = rnd.Next(have); var p = rt.TransformPoint(s.stubPos[i]);
                    bool near = false; for (int j = 0; j < chosen.Count && !near; j++) near = (chosen[j] - p).sqrMagnitude < 1.21f;
                    if (near) continue;
                    var inw = rt.TransformDirection(s.stubIn[i]); inw.y = 0f; inw = inw.sqrMagnitude > 1e-8f ? inw.normalized : Vector3.forward;
                    if (!FreeNear(p + inw * clear)) continue;                                                // only walls that border the rubble floor, not a deck or machine that still stands
                    chosen.Add(p);
                    float len = RuinRange(rnd, 0.55f, 1.15f), straight = RuinRange(rnd, 0.45f, 0.7f), kink = RuinRange(rnd, 28f, 75f) * Mathf.Deg2Rad;
                    var over = Quaternion.AngleAxis(RuinRange(rnd, -75f, 75f), Vector3.up) * inw;
                    var lean = (Vector3.up + Quaternion.AngleAxis(RuinRange(rnd, 0f, 360f), Vector3.up) * Vector3.forward * Mathf.Tan(RuinRange(rnd, 4f, 16f) * Mathf.Deg2Rad)).normalized;
                    var bend = (lean * Mathf.Cos(kink) + over * Mathf.Sin(kink)).normalized;
                    pts.Clear();
                    pts.Add(p - Vector3.up * 0.15f); pts.Add(p + lean * (len * straight));
                    pts.Add(pts[1] + bend * (len * (1f - straight) * 0.6f));
                    pts.Add(pts[2] + (bend * Mathf.Cos(0.35f) + over * Mathf.Sin(0.35f)).normalized * (len * (1f - straight) * 0.4f));
                    buf.Rod(pts, RuinRodRadius, 0.6f, White); stubs++;
                }
            }

            /// <summary>Bent rod grids (one, or two on a large floor): two layers of bars, the low edge on the rubble, rising and folding toward the remains.</summary>
            void Grids(RuinBuf buf, System.Random rnd)
            {
                int count = area > 160f ? 2 : 1, bars = sizeM > 12f ? 5 : 4; float sp = Mathf.Max(0.25f, RuinGridSpacing), r = RuinGridRodRadius;
                float half = 0.5f * sp * (bars - 1), rad = half * 1.45f; var pts = new List<Vector3>();
                if (rad > room)
                {   // a narrow free strip (the fort's ring): four bars, closer together
                    bars = 4; sp = Mathf.Max(0.28f, room / (1.45f * 0.5f * (bars - 1))); half = 0.5f * sp * (bars - 1); rad = Mathf.Min(half * 1.45f, room);
                }
                if (room < 0.4f) return;
                for (int g = 0; g < count; g++)
                {
                    if (!Pick(rnd, clear + rad, clear + rad + 3f, out float fx, out float fz) && !Pick(rnd, clear + rad, 1e6f, out fx, out fz)) continue;
                    var c = World(fx, fz); if (!Free(placed, c.x, c.z, rad)) continue;
                    c.y = FloorY(fx, fz);
                    var ax = ToWall(fx, fz); if (ax == Vector3.zero) ax = Vector3.forward;
                    ax = Quaternion.AngleAxis(RuinRange(rnd, -25f, 25f), Vector3.up) * ax; var az = new Vector3(-ax.z, 0f, ax.x);
                    float tilt = Mathf.Tan(RuinRange(rnd, 8f, 20f) * Mathf.Deg2Rad), fold = Mathf.Tan(RuinRange(rnd, 22f, 42f) * Mathf.Deg2Rad), foldAt = RuinRange(rnd, -0.1f, 0.5f) * half;
                    for (int layer = 0; layer < 2; layer++) for (int i = 0; i < bars; i++)
                    {
                        pts.Clear(); float across = -half + i * sp + RuinRange(rnd, -0.04f, 0.04f);
                        for (int k = 0; k <= 4; k++)
                        {
                            float along = -half * 1.12f + k * (half * 2.24f / 4f);
                            float a = layer == 0 ? along : across, bq = layer == 0 ? across : along;
                            var w = c + ax * a + az * bq;
                            float sheet = c.y + 0.06f + (a + half) * tilt + Mathf.Max(0f, a - foldAt) * fold + layer * r * 1.8f;
                            w.y = Mathf.Max(sheet, FloorYW(w) + r * 0.6f + layer * r * 1.8f);
                            pts.Add(w);
                        }
                        buf.Rod(pts, r, 1f, White);
                    }
                    placed.Add(new Vector3(c.x, c.z, rad * 0.6f)); grids++;
                }
            }

            /// <summary>Loose bent rods lying on the rubble, one end lifted.</summary>
            void LooseRods(RuinBuf buf, System.Random rnd)
            {
                int count = Mathf.Clamp(Mathf.RoundToInt(area / 45f), 2, 6); var pts = new List<Vector3>();
                for (int i = 0; i < count; i++)
                {
                    bool narrow = room < 1.2f;
                    if (!Pick(rnd, clear + (narrow ? room * 0.6f : 1.2f), 1e6f, out float fx, out float fz)) continue;
                    var c = World(fx, fz); float yaw = RuinRange(rnd, 0f, Mathf.PI * 2f), len = RuinRange(rnd, 1.3f, 2.3f), lift = RuinRange(rnd, 0.12f, 0.5f), bow = RuinRange(rnd, -0.25f, 0.25f) * len;
                    var d = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
                    if (narrow)
                    {   // a narrow free strip: the rod lies along the wall, not across into it
                        var tw = ToWall(fx, fz);
                        if (tw != Vector3.zero) { d = Quaternion.AngleAxis(RuinRange(rnd, -20f, 20f), Vector3.up) * new Vector3(-tw.z, 0f, tw.x); bow *= 0.4f; }
                    }
                    var sd = new Vector3(-d.z, 0f, d.x);
                    pts.Clear();
                    for (int k = 0; k <= 4; k++)
                    {
                        float t = k / 4f; var w = c + d * ((t - 0.5f) * len) + sd * (bow * Mathf.Sin(t * Mathf.PI));
                        w.y = FloorYW(w) + RuinRodRadius * 0.5f + lift * t * t;
                        pts.Add(w);
                    }
                    buf.Rod(pts, RuinRodRadius, 0.6f, White); loose++;
                }
            }
        }

        // ------------------------------------------------------------------------------------------------ attach

        /// <summary>This building's floor: the detailed one, or the plain draped grid when the detail is off or could not be built.
        /// `site` = the detailed floor's layout (null with the plain floor).</summary>
        Mesh RuinFloorFor(ShellSet s, out RuinSite site)
        {
            site = null; Mesh floor = null;
            if (RuinDetail && s.dist != null && s.free > 0)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { site = new RuinSite(RuinCoreSet(s), ruinGo.transform, obj.Id) { core = ruinCore }; floor = site.Floor(); }
                catch (System.Exception ex) { Debug.LogWarning($"[OpenZ] ruin floor {name}: {ex.Message}"); floor = null; }
                if (site != null) site.ms = sw.Elapsed.TotalMilliseconds;
            }
            if (!floor) { site = null; floor = ShellFloor(s, ruinGo.transform); }
            return floor;
        }

        static Material RuinRubbleMat(Material src)
        {
            if (!src) return null;
            if (!ruinRubbleMats.TryGetValue(src, out var m) || !m) { m = new Material(src) { name = src.name + "_ruin", renderQueue = RuinRubbleQueue }; ruinRubbleMats[src] = m; }
            return m;
        }

        /// <summary>The rubble child of the ruin object: one mesh, three materials (outer concrete, broken concrete, rods), no collider.</summary>
        void AttachRubble(RuinSite site, bool lib, Material src)
        {
            Mesh m = null; var sw = System.Diagnostics.Stopwatch.StartNew();
            try { m = site.Rubble(lib, obj.IsFort); }
            catch (System.Exception ex) { Debug.LogWarning($"[OpenZ] ruin rubble {name}: {ex.Message}"); m = null; }
            site.ms += sw.Elapsed.TotalMilliseconds;
            if (m)
            {
                Material mo = RuinRubbleMat(lib ? ConcreteChunkLib.Outer : ShellFallback(src)), mf = lib ? RuinRubbleMat(ConcreteChunkLib.Fracture) : mo, mr = lib ? RuinRubbleMat(ConcreteChunkLib.Rebar) : mo;
                if (mo && mf && mr)
                {
                    var r = ShellChild("rubble", m, new[] { mo, mf, mr }, true);
                    r.gameObject.AddComponent<ShellOwnedMesh>().mesh = m;
                    RuinPaintRubble(r, lib);
                }
                else Destroy(m);
            }
            LastRuinFloorStats = name + ": " + site.Stats();
            Debug.Log("[OpenZ] ruin floor " + LastRuinFloorStats);
        }

        /// <summary>The rubble's tones: the level's concrete on the outer and broken faces (the pieces' own tones are in the vertex
        /// colours), the level's rod map on the rods; without the library flat tones.</summary>
        static void RuinPaintRubble(MeshRenderer r, bool lib)
        {
            shellMpb ??= new MaterialPropertyBlock();
            bool l17 = ZGame.I && ZGame.I.level == "L17";
            Color outer = lib ? ConcreteChunkLib.LevelOuter : ShellConcrete, frac = Color.Lerp(lib ? ConcreteChunkLib.LevelFracture : ShellConcrete, ConcreteChunkLib.ScorchTone, 0.1f);
            for (int i = 0; i < 3; i++)
            {
                shellMpb.Clear();
                if (i == 2)
                {
                    var map = lib ? (l17 ? ConcreteChunkLib.RebarL17 : ConcreteChunkLib.RebarL02) : null;
                    var c = map ? (l17 ? RuinRebarTintL17 : RuinRebarTintL02) : (l17 ? RuinRebarFlatL17 : RuinRebarFlatL02);
                    shellMpb.SetTexture(BaseMapId, map ? (Texture)map : Texture2D.whiteTexture);
                    shellMpb.SetColor(BaseColorIdV1, new Color(c.r, c.g, c.b, 1f));
                }
                else
                {
                    if (!lib) shellMpb.SetTexture(BaseMapId, Texture2D.whiteTexture);
                    var c = i == 0 ? outer : frac;
                    shellMpb.SetColor(BaseColorIdV1, new Color(c.r, c.g, c.b, 1f));
                }
                r.SetPropertyBlock(shellMpb, i);
            }
        }
    }
}
