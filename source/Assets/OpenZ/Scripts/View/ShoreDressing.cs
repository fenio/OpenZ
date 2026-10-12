using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    /// details only fit the few non-walkable bank spots, so the water edges get LOW walk-over dressing: green grass tufts,
    /// reeds at the waterline, pebbles and small flat stones, all under 0.3 m, in irregular groups along the banks and around
    /// the bridge ends (never a continuous line). The bank line comes from the RENDERED terrain (TrackSurface.RenderY: the
    /// carved river bed and the widened channels) against the flat water plane. No collider, no sim change, not on roads, not
    /// under bridge decks, not on steep cliffs. Deterministic (fixed seed). URP Lit with a tiny generated swatch texture (grass /
    /// reed gradients base -> tip, pebble tones); grass + reeds two-sided without shadow casting, stones cast shadows.
    /// View only; L02 only (L17 has quay walls).
    /// </summary>
    [DefaultExecutionOrder(98)]
    public class ShoreDressing : MonoBehaviour
    {
        public static bool On = true;
        public static ShoreDressing I { get; private set; }
        public int groups, tufts, reeds, pebbles, stones, verts, bankCells;
        public readonly List<Vector3> GroupCentres = new List<Vector3>();
        public float WaterY { get; private set; }

        const float Step = 0.5f, MaxH = 0.3f;
        readonly List<GameObject> made = new List<GameObject>();
        readonly List<Mesh> meshes = new List<Mesh>();
        Material matSoft, matStone; Texture2D tex;
        TrackSurface surf; float x0, z0; int nx, nz; float[] H;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            SceneManager.sceneLoaded -= OnLoaded;
            SceneManager.sceneLoaded += OnLoaded;
            for (int i = 0; i < SceneManager.sceneCount; i++) OnLoaded(SceneManager.GetSceneAt(i), LoadSceneMode.Additive);
        }

        static void OnLoaded(Scene s, LoadSceneMode m)
        {
            if (!On || !Application.isPlaying || !s.IsValid() || !s.name.StartsWith("L02")) return;
            if (I && I.gameObject.scene == s) return;
            var go = new GameObject("ShoreDressing");
            SceneManager.MoveGameObjectToScene(go, s);
            go.AddComponent<ShoreDressing>();
        }

        void Awake() { I = this; }
        void OnDestroy()
        {
            if (I == this) I = null;
            foreach (var mm in meshes) if (mm) Destroy(mm);
            if (matSoft) Destroy(matSoft); if (matStone) Destroy(matStone); if (tex) Destroy(tex);
        }

        IEnumerator Start()
        {
            float t0 = Time.realtimeSinceStartup;
            while (!(ZGame.I && ZGame.I.World != null && TrackMarks.Active && TrackMarks.I.Surface != null) && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            yield return null;
            try { Build(); }
            catch (System.Exception e) { Debug.LogWarning("[OpenZ] shore dressing: " + e); }
        }

        public void SetVisible(bool v) { foreach (var g in made) if (g) g.SetActive(v); }

        // ------------------------------------------------------------------------------------------ build
        void Build()
        {
            var W = ZGame.I ? ZGame.I.World : null;
            if (W == null || W.Map.planet == "city") return;
            surf = TrackMarks.I.Surface;
            float wy = float.NegativeInfinity; Bounds gb = new Bounds(new Vector3(72, 0, -90), new Vector3(144, 1, 180));
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
            {
                if (r.name.StartsWith("WAT_") && r.gameObject.activeInHierarchy) wy = Mathf.Max(wy, r.bounds.max.y);
                if (r.name == "TER_Ground") gb = r.bounds;
            }
            if (float.IsNegativeInfinity(wy)) wy = -0.35f;
            WaterY = wy;
            x0 = gb.min.x + 1f; z0 = gb.min.z + 1f;
            nx = Mathf.FloorToInt((gb.size.x - 2f) / Step); nz = Mathf.FloorToInt((gb.size.z - 2f) / Step);
            H = new float[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                    H[j * nx + i] = surf.RenderY(x0 + i * Step, z0 + j * Step, out float y) ? y : float.NaN;

            // bank cells: dry, just above the water, a wet cell within 1 m, gentle slope, not a road, nothing standing above
            var cand = new List<Vector4>();   // x, z, water dir x, water dir z
            for (int j = 2; j < nz - 2; j++)
                for (int i = 2; i < nx - 2; i++)
                {
                    float h = H[j * nx + i];
                    if (float.IsNaN(h) || h < wy + 0.02f || h > wy + 0.4f) continue;
                    float lo = h; int li = 0, lj = 0; float slope = 0f;
                    for (int dj = -2; dj <= 2; dj++)
                        for (int di = -2; di <= 2; di++)
                        {
                            float hn = H[(j + dj) * nx + i + di]; if (float.IsNaN(hn)) continue;
                            if (hn < lo) { lo = hn; li = di; lj = dj; }
                            if (Mathf.Abs(di) + Mathf.Abs(dj) == 1) slope = Mathf.Max(slope, Mathf.Abs(hn - h));
                        }
                    if (lo > wy - 0.04f || slope > 0.4f) continue;
                    float x = x0 + i * Step, z = z0 + j * Step;
                    if (!Clear(W, x, z, h)) continue;
                    var d = new Vector2(li, lj).normalized;
                    cand.Add(new Vector4(x, z, d.x, d.y));
                }
            bankCells = cand.Count;

            var rng = new System.Random(20260930);
            for (int k = cand.Count - 1; k > 0; k--) { int r = rng.Next(k + 1); var t = cand[k]; cand[k] = cand[r]; cand[r] = t; }

            var soft = new Builder(); var hard = new Builder();
            var grid = new Dictionary<long, List<Vector2>>();
            foreach (var c in cand)
            {
                float x = c.x, z = c.y;
                float mask = Mathf.PerlinNoise(x * 0.085f + 13.1f, z * 0.085f + 7.7f);
                if (mask < 0.34f) continue;                                                          // long gaps: never a continuous line
                float gap = 2.8f + (float)rng.NextDouble() * 2.6f;
                if (Near(grid, x, z, gap)) continue;
                AddGrid(grid, x, z);
                var wd = new Vector3(c.z, 0f, c.w); if (wd.sqrMagnitude < 0.01f) wd = Vector3.forward;
                Group(W, rng, new Vector3(x, 0f, z), wd.normalized, Mathf.Lerp(0.55f, 1.35f, (float)rng.NextDouble()) * Mathf.Lerp(0.8f, 1.2f, mask), soft, hard);
                GroupCentres.Add(new Vector3(x, surf.RenderY(x, z, out float gy) ? gy : 0f, z));
                groups++;
            }

            MakeMaterials();
            var root = transform;
            soft.Emit(root, "SHD_Grass", matSoft, ShadowCastingMode.Off, meshes, made);
            hard.Emit(root, "SHD_Stones", matStone, ShadowCastingMode.On, meshes, made);
            verts = soft.V.Count + hard.V.Count;
            Debug.Log($"[OpenZ] shore dressing L02: water y {wy:F2}, {bankCells} bank cells, {groups} groups: {tufts} grass tufts, {reeds} reed clumps, " +
                      $"{pebbles} pebbles, {stones} flat stones ({verts} v, {made.Count} chunks), all <= {MaxH:F2} m, no collider");
        }

        bool Clear(ZWorld W, float x, float z, float h)
        {
            int tx = Mathf.FloorToInt(x * 0.5f), ty = Mathf.FloorToInt(-z * 0.5f);
            if (W.Map.ClassAt(tx, ty) == ZClass.Road) return false;
            if (Physics.Raycast(new Ray(new Vector3(x, 60f, z), Vector3.down), out var hit, 120f, ~0, QueryTriggerInteraction.Ignore) && hit.point.y > h + 0.35f) return false;   // deck, building, rock
            return true;
        }

        static long Key(int a, int b) => ((long)a << 32) ^ (uint)b;
        static void AddGrid(Dictionary<long, List<Vector2>> g, float x, float z)
        {
            var k = Key(Mathf.FloorToInt(x / 8f), Mathf.FloorToInt(z / 8f));
            if (!g.TryGetValue(k, out var l)) g[k] = l = new List<Vector2>();
            l.Add(new Vector2(x, z));
        }
        static bool Near(Dictionary<long, List<Vector2>> g, float x, float z, float d)
        {
            int cx = Mathf.FloorToInt(x / 8f), cz = Mathf.FloorToInt(z / 8f);
            for (int a = -1; a <= 1; a++)
                for (int b = -1; b <= 1; b++)
                    if (g.TryGetValue(Key(cx + a, cz + b), out var l))
                        foreach (var p in l) if ((p - new Vector2(x, z)).sqrMagnitude < d * d) return true;
            return false;
        }

        bool Y(float x, float z, out float y) { y = 0; return surf.RenderY(x, z, out y) && !float.IsNaN(y); }

        void Group(ZWorld W, System.Random rng, Vector3 c, Vector3 wd, float s, Builder soft, Builder hard)
        {
            var tan = new Vector3(-wd.z, 0f, wd.x);
            float R() => (float)rng.NextDouble();
            float wy = WaterY;
            // grass tufts: a dense irregular clump stretched along the bank, hugging the waterline (reads as a green fringe at h 70)
            int nt = Mathf.RoundToInt(Mathf.Lerp(24f, 50f, R()) * s);
            for (int k = 0; k < nt; k++)
            {
                float a = (R() * 2f - 1f), b = (R() * 2f - 1f);
                var p = c + tan * (a * Mathf.Abs(a) * 1.9f * s) + wd * (b * 0.6f * s - 0.2f);
                if (!Y(p.x, p.z, out float y) || y < wy + 0.005f || y > wy + 0.75f || !Clear(W, p.x, p.z, y)) continue;
                bool bush = R() < 0.3f;
                Tuft(soft, rng, new Vector3(p.x, y - 0.01f, p.z), R() < 0.55f ? 0 : R() < 0.6f ? 1 : 2,
                     bush ? Mathf.Lerp(0.2f, 0.29f, R()) : Mathf.Lerp(0.13f, 0.26f, R()), bush ? 14 + rng.Next(6) : 9 + rng.Next(6), bush ? 0.24f + R() * 0.12f : 0.12f + R() * 0.12f);
                tufts++;
            }
            // reeds at the waterline (some stand in the shallows)
            int nr = rng.Next(1, 5);
            for (int k = 0; k < nr; k++)
            {
                var p = c + wd * (0.1f + R() * 0.9f) + tan * ((R() * 2f - 1f) * 1.4f * s);
                if (!Y(p.x, p.z, out float y) || y < wy - 0.16f || y > wy + 0.12f) continue;
                float baseY = Mathf.Max(y, wy - 0.16f);
                Tuft(soft, rng, new Vector3(p.x, baseY - 0.01f, p.z), R() < 0.5f ? 3 : 4, Mathf.Min(MaxH - 0.01f, wy - baseY + Mathf.Lerp(0.2f, 0.28f, R())), 5 + rng.Next(5), 0.035f + R() * 0.04f, true);
                reeds++;
            }
            // pebbles and a few small flat stones along the edge
            int np = rng.Next(2, 9);
            for (int k = 0; k < np; k++)
            {
                var p = c + tan * ((R() * 2f - 1f) * 1.6f * s) + wd * ((R() * 2f - 1f) * 0.6f);
                if (!Y(p.x, p.z, out float y) || y < wy - 0.02f || !Clear(W, p.x, p.z, y)) continue;
                Pebble(hard, rng, new Vector3(p.x, y, p.z), Mathf.Lerp(0.035f, 0.11f, R() * R()), R() < 0.6f ? 5 : 6);
                pebbles++;
            }
            int ns = R() < 0.55f ? rng.Next(1, 3) : 0;
            for (int k = 0; k < ns; k++)
            {
                var p = c + tan * ((R() * 2f - 1f) * 1.2f * s) + wd * (R() * 0.5f - 0.2f);
                if (!Y(p.x, p.z, out float y) || y < wy - 0.02f || !Clear(W, p.x, p.z, y)) continue;
                FlatStone(hard, rng, new Vector3(p.x, y, p.z), Mathf.Lerp(0.13f, 0.3f, R()), R() < 0.5f ? 7 : 6);
                stones++;
            }
        }

        // ------------------------------------------------------------------------------------------ shapes
        static Vector2 Uv(int sw, float v) => new Vector2((sw + 0.5f) / 8f, Mathf.Lerp(0.06f, 0.94f, v));

        void Tuft(Builder b, System.Random rng, Vector3 p, int sw, float h, int blades, float spread, bool reed = false)
        {
            float R() => (float)rng.NextDouble();
            h = Mathf.Min(h, MaxH);
            for (int k = 0; k < blades; k++)
            {
                float ang = (k + R() * 0.8f) / blades * Mathf.PI * 2f;
                var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang));
                var root = p + dir * (R() * spread * 0.5f);
                float lean = reed ? Mathf.Lerp(0.03f, 0.18f, R()) : Mathf.Lerp(0.3f, 0.85f, R());
                float bh = h * Mathf.Lerp(0.65f, 1f, R());
                float w = reed ? Mathf.Lerp(0.014f, 0.024f, R()) : Mathf.Lerp(0.04f, 0.075f, R());
                var side = new Vector3(-dir.z, 0f, dir.x);
                side = Quaternion.AngleAxis((R() - 0.5f) * 60f, Vector3.up) * side;
                var tip = root + dir * (bh * lean) + Vector3.up * (bh * Mathf.Sqrt(1f - lean * lean * 0.5f));
                var mid = Vector3.Lerp(root, tip, 0.5f) - dir * (bh * lean * 0.18f) + Vector3.up * (bh * 0.04f);
                int i0 = b.V.Count;
                b.Add(root - side * w, Uv(sw, 0f)); b.Add(root + side * w, Uv(sw, 0f));
                b.Add(mid - side * w * 0.62f, Uv(sw, 0.5f)); b.Add(mid + side * w * 0.62f, Uv(sw, 0.5f));
                b.Add(tip, Uv(sw, 1f));
                b.T.AddRange(new[] { i0, i0 + 2, i0 + 1, i0 + 1, i0 + 2, i0 + 3, i0 + 2, i0 + 4, i0 + 3 });
                if (reed && k == 0 && R() < 0.45f)
                {   // a small seed head on one reed stalk (still under 0.3 m)
                    var t2 = tip + Vector3.up * 0.0f; int j0 = b.V.Count; float hw = 0.011f;
                    b.Add(t2 - Vector3.up * 0.05f - side * hw, Uv(4, 1f)); b.Add(t2 - Vector3.up * 0.05f + side * hw, Uv(4, 1f));
                    b.Add(t2 - side * hw, Uv(4, 1f)); b.Add(t2 + side * hw, Uv(4, 1f));
                    b.T.AddRange(new[] { j0, j0 + 2, j0 + 1, j0 + 1, j0 + 2, j0 + 3 });
                }
            }
        }

        void Pebble(Builder b, System.Random rng, Vector3 p, float r, int sw)
        {
            float R() => (float)rng.NextDouble();
            var rot = Quaternion.Euler((R() - 0.5f) * 20f, R() * 360f, (R() - 0.5f) * 20f);
            float sy = Mathf.Lerp(0.42f, 0.7f, R());
            var pts = new Vector3[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            for (int k = 0; k < 6; k++) pts[k] = rot * Vector3.Scale(pts[k] * r * Mathf.Lerp(0.75f, 1.2f, R()), new Vector3(1f, sy, Mathf.Lerp(0.7f, 1.1f, R())));
            var c = p + Vector3.up * (r * sy * 0.35f);
            int[] f = { 2, 0, 4, 2, 4, 1, 2, 1, 5, 2, 5, 0, 3, 4, 0, 3, 1, 4, 3, 5, 1, 3, 0, 5 };
            for (int k = 0; k < f.Length; k += 3)
            {
                var a = c + pts[f[k]]; var bb = c + pts[f[k + 2]]; var cc = c + pts[f[k + 1]];   // reversed: Unity front faces are clockwise
                float v = Vector3.Cross(bb - a, cc - a).normalized.y * 0.5f + 0.5f;           // tops lighter
                int i0 = b.V.Count;
                b.Add(a, Uv(sw, v)); b.Add(bb, Uv(sw, v)); b.Add(cc, Uv(sw, v));
                b.T.AddRange(new[] { i0, i0 + 1, i0 + 2 });
                b.Flat(i0);
            }
        }

        void FlatStone(Builder b, System.Random rng, Vector3 p, float r, int sw)
        {
            float R() => (float)rng.NextDouble();
            int n = 6 + rng.Next(3); float hgt = Mathf.Lerp(0.035f, 0.075f, R()); float yaw = R() * 360f;
            var tilt = Quaternion.Euler((R() - 0.5f) * 10f, yaw, (R() - 0.5f) * 10f);
            var top = new Vector3[n]; var bot = new Vector3[n];
            for (int k = 0; k < n; k++)
            {
                float a = (k + (R() - 0.5f) * 0.5f) / n * Mathf.PI * 2f; float rr = r * Mathf.Lerp(0.72f, 1.05f, R());
                var o = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr * 0.8f);
                top[k] = p + tilt * (o * 0.86f + Vector3.up * hgt); bot[k] = p + tilt * (o - Vector3.up * 0.02f);
            }
            var tc = p + tilt * (Vector3.up * (hgt + 0.006f));
            for (int k = 0; k < n; k++)
            {
                int k2 = (k + 1) % n, i0 = b.V.Count;
                b.Add(tc, Uv(sw, 1f)); b.Add(top[k2], Uv(sw, 0.92f)); b.Add(top[k], Uv(sw, 0.92f));
                b.T.AddRange(new[] { i0, i0 + 1, i0 + 2 }); b.Flat(i0);
                int j0 = b.V.Count;
                b.Add(top[k], Uv(sw, 0.5f)); b.Add(top[k2], Uv(sw, 0.5f)); b.Add(bot[k2], Uv(sw, 0.1f)); b.Add(bot[k], Uv(sw, 0.1f));
                b.T.AddRange(new[] { j0, j0 + 1, j0 + 2, j0, j0 + 2, j0 + 3 }); b.FlatQuad(j0);
            }
        }

        // ------------------------------------------------------------------------------------------ materials
        void MakeMaterials()
        {
            // 8 swatches, vertical gradient base (v 0) -> tip / top (v 1)
            Color32[,] sw =
            {
                { new Color32(58, 96, 34, 255),  new Color32(150, 186, 72, 255) },   // 0 fresh grass
                { new Color32(66, 88, 36, 255),  new Color32(160, 172, 80, 255) },   // 1 olive grass
                { new Color32(92, 100, 44, 255), new Color32(194, 186, 100, 255) },  // 2 sun-dried grass
                { new Color32(48, 76, 40, 255),  new Color32(124, 146, 70, 255) },   // 3 reed green
                { new Color32(60, 78, 40, 255),  new Color32(156, 122, 74, 255) },   // 4 reed, brown tips / seed heads
                { new Color32(132, 124, 112, 255), new Color32(186, 176, 158, 255) },// 5 grey pebble
                { new Color32(158, 120, 86, 255), new Color32(204, 166, 124, 255) }, // 6 sandstone pebble
                { new Color32(118, 112, 104, 255), new Color32(170, 162, 150, 255) } // 7 flat grey stone
            };
            tex = new Texture2D(32, 16, TextureFormat.RGBA32, false) { name = "ShoreDressingSwatches", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 32; x++)
                    tex.SetPixel(x, y, Color32.Lerp(sw[x / 4, 0], sw[x / 4, 1], y / 15f));
            tex.Apply(false, true);
            var sh = Shader.Find("Universal Render Pipeline/Lit");
            matSoft = new Material(sh) { name = "ShoreGrass" };
            matSoft.SetTexture("_BaseMap", tex); matSoft.SetColor("_BaseColor", Color.white);
            matSoft.SetFloat("_Smoothness", 0.12f); matSoft.SetFloat("_Metallic", 0f);
            matSoft.SetFloat("_Cull", 0f);                                                          // blades: both faces
            matStone = new Material(sh) { name = "ShoreStones" };
            matStone.SetTexture("_BaseMap", tex); matStone.SetColor("_BaseColor", Color.white);
            matStone.SetFloat("_Smoothness", 0.18f); matStone.SetFloat("_Metallic", 0f);
        }

        // ------------------------------------------------------------------------------------------ mesh builder
        sealed class Builder
        {
            public readonly List<Vector3> V = new List<Vector3>();
            public readonly List<Vector3> N = new List<Vector3>();
            public readonly List<Vector2> U = new List<Vector2>();
            public readonly List<int> T = new List<int>();
            public void Add(Vector3 p, Vector2 uv) { V.Add(p); U.Add(uv); N.Add(Vector3.up); }   // grass: sky-facing normals, soft like the ground
            public void Flat(int i0)
            {
                var n = Vector3.Cross(V[i0 + 1] - V[i0], V[i0 + 2] - V[i0]).normalized;
                n = Vector3.Lerp(n, Vector3.up, 0.25f).normalized;
                N[i0] = N[i0 + 1] = N[i0 + 2] = n;
            }
            public void FlatQuad(int i0)
            {
                var n = Vector3.Cross(V[i0 + 1] - V[i0], V[i0 + 2] - V[i0]).normalized;
                n = Vector3.Lerp(n, Vector3.up, 0.25f).normalized;
                N[i0] = N[i0 + 1] = N[i0 + 2] = N[i0 + 3] = n;
            }

            /// <summary>Split into 24 m chunks (frustum culling), one GameObject each, no collider.</summary>
            public void Emit(Transform root, string name, Material mat, ShadowCastingMode shadows, List<Mesh> meshes, List<GameObject> made)
            {
                if (T.Count == 0) return;
                var chunks = new Dictionary<long, List<int>>();
                for (int t = 0; t < T.Count; t += 3)
                {
                    var c = (V[T[t]] + V[T[t + 1]] + V[T[t + 2]]) / 3f;
                    long k = ((long)Mathf.FloorToInt(c.x / 24f) << 32) ^ (uint)Mathf.FloorToInt(c.z / 24f);
                    if (!chunks.TryGetValue(k, out var l)) chunks[k] = l = new List<int>();
                    l.Add(t);
                }
                foreach (var kv in chunks)
                {
                    var map = new Dictionary<int, int>(); var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var tri = new List<int>();
                    foreach (int t in kv.Value)
                        for (int e = 0; e < 3; e++)
                        {
                            int src = T[t + e];
                            if (!map.TryGetValue(src, out int dst)) { dst = v.Count; map[src] = dst; v.Add(V[src]); n.Add(N[src]); u.Add(U[src]); }
                            tri.Add(dst);
                        }
                    var m = new Mesh { name = name };
                    if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
                    m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, u); m.SetTriangles(tri, 0); m.RecalculateBounds();
                    var go = new GameObject(name + "_" + made.Count);
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = mat; mr.shadowCastingMode = shadows; mr.receiveShadows = true;
                    mr.lightProbeUsage = LightProbeUsage.Off; mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    meshes.Add(m); made.Add(go);
                }
            }
        }
    }
}
