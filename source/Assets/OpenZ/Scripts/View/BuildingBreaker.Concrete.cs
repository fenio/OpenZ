using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// The fort's big flying parts are no longer the cut-off shell of the model (thin double-sided
    /// walls on a jagged cut = torn cardboard) but closed, thick, chunky concrete: each part becomes a block / slab of the part's size
    /// (the thin axis raised to at least 0.8 m), with chipped bevelled edges, a craggy fracture face at one end and a rougher top,
    /// flat-shaded low-poly facets in the fort's light concrete (a few tones, lightly scorched) and 2-4 bent rebar stubs sticking out of
    /// the fracture face. Heavy physics and the concrete thud stay (BuildingBreaker.Ruin: mass x3, low bounce, quick settle).
    /// </summary>
    public partial class BuildingBreaker
    {
        public static bool ConcreteParts = true, ConcreteAllBuildings = true;   // first Play check: the nuke also broke the factory / radar next to the fort into the old torn shells
        public static float ConcreteMinThick = 0.8f, ConcreteMaxSize = 2.2f;
        public static int ConcreteBlocks { get; private set; }
        static readonly Dictionary<Material, Material> concreteMats = new Dictionary<Material, Material>();

        /// <summary>Swaps a big part's visual for a concrete block; returns the block's size (m) for the collider and the mass.</summary>
        Vector3 MakeConcretePart(GameObject root, GameObject vis, Vector3 size, int seed, Material src)
        {
            var sz = size;
            float minD = Mathf.Min(sz.x, Mathf.Min(sz.y, sz.z)), maxD = Mathf.Max(sz.x, Mathf.Max(sz.y, sz.z));
            float midD = sz.x + sz.y + sz.z - minD - maxD;
            float thick = Mathf.Max(minD, ConcreteMinThick, 0.35f * midD);
            for (int a = 0; a < 3; a++) if (Mathf.Approximately(sz[a], minD)) { sz[a] = thick; break; }
            // 0.55-1.0 size, so no two blocks match
            var szr = new System.Random(seed * 7919 + 13);
            for (int a = 0; a < 3; a++) sz[a] = Mathf.Clamp(sz[a] * (0.55f + 0.45f * (float)szr.NextDouble()), 0.6f, ConcreteMaxSize);
            var mesh = ConcreteBlock(sz, seed, out var brokenAxis, out var brokenSign);
            vis.transform.localPosition = Vector3.zero; vis.transform.localRotation = Quaternion.identity; vis.transform.localScale = Vector3.one;
            vis.GetComponent<MeshFilter>().sharedMesh = mesh;
            var r = vis.GetComponent<MeshRenderer>();
            var m = ConcreteMat(src);
            r.sharedMaterials = new[] { m };
            // concrete tone: light cream-grey, varied, a little scorched
            var rnd = new System.Random(seed);
            float v = 0.92f + 0.12f * (float)rnd.NextDouble(), sc = 0.08f + 0.26f * (float)rnd.NextDouble();
            var c = Color.Lerp(new Color(0.72f * v, 0.69f * v, 0.64f * v), new Color(0.22f, 0.19f, 0.17f), sc); c.a = 1f;
            mpbV1 ??= new MaterialPropertyBlock(); mpbV1.Clear();
            mpbV1.SetTexture(BaseMapId, Texture2D.whiteTexture); mpbV1.SetColor(BaseColorIdV1, c);
            r.SetPropertyBlock(mpbV1);
            // rebar stubs out of the fracture face
            int n = 2 + rnd.Next(3);
            var h = sz * 0.5f; int a1 = (brokenAxis + 1) % 3, a2 = (brokenAxis + 2) % 3;
            var rust = new Color(0.26f, 0.15f, 0.10f, 1f);
            for (int i = 0; i < n; i++)
            {
                var p = Vector3.zero;
                p[brokenAxis] = brokenSign * h[brokenAxis] * 0.82f;
                p[a1] = ((float)rnd.NextDouble() * 2f - 1f) * h[a1] * 0.62f;
                p[a2] = ((float)rnd.NextDouble() * 2f - 1f) * h[a2] * 0.62f;
                var nrm = Vector3.zero; nrm[brokenAxis] = brokenSign;
                var dir = Quaternion.Euler(((float)rnd.NextDouble() - 0.5f) * 44f, ((float)rnd.NextDouble() - 0.5f) * 44f, ((float)rnd.NextDouble() - 0.5f) * 44f) * nrm;
                float len = 0.28f + 0.42f * (float)rnd.NextDouble(), rad = 0.026f + 0.01f * (float)rnd.NextDouble();
                var s1 = Rebar(vis.transform, p, dir, len, rad, m, rust);
                if (rnd.NextDouble() < 0.6)
                {   // bent end
                    var d2 = Quaternion.AngleAxis(25f + 30f * (float)rnd.NextDouble(), Vector3.Cross(dir, Random.onUnitSphere).normalized) * dir;
                    Rebar(vis.transform, p + dir * (len - 0.14f), d2, 0.12f + 0.18f * (float)rnd.NextDouble(), rad, m, rust);
                }
            }
            ConcreteBlocks++;
            return sz;
        }

        static Transform Rebar(Transform parent, Vector3 from, Vector3 dir, float len, float rad, Material m, Color rust)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "rebar"; var col = go.GetComponent<Collider>(); if (col) Object.Destroy(col);
            go.layer = parent.gameObject.layer;
            var t = go.transform; t.SetParent(parent, false);
            t.localRotation = Quaternion.FromToRotation(Vector3.up, dir);
            t.localPosition = from + dir * (len * 0.5f - 0.12f);                                      // 12 cm cast into the concrete
            t.localScale = new Vector3(rad * 2f, len * 0.5f + 0.06f, rad * 2f);
            var r = go.GetComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.On;
            mpbV1 ??= new MaterialPropertyBlock(); mpbV1.Clear();
            mpbV1.SetTexture(BaseMapId, Texture2D.whiteTexture); mpbV1.SetColor(BaseColorIdV1, rust);
            r.SetPropertyBlock(mpbV1);
            return t;
        }

        static Material ConcreteMat(Material src)
        {
            if (!src) src = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (concreteMats.TryGetValue(src, out var dm) && dm) return dm;
            dm = new Material(src) { name = src.name + "_concrete" };
            if (dm.HasProperty("_TeamMode")) dm.SetFloat("_TeamMode", 0f);
            if (dm.HasProperty("_Accent")) dm.SetFloat("_Accent", 0f);
            if (dm.HasProperty("_FlatShade")) dm.SetFloat("_FlatShade", 1f);
            if (dm.HasProperty("_Smoothness")) dm.SetFloat("_Smoothness", 0.16f);
            if (dm.HasProperty("_Metallic")) dm.SetFloat("_Metallic", 0f);
            concreteMats[src] = dm; chunkMats[dm] = dm;                                               // warmed with the chunk materials
            return dm;
        }

        static float Hash(Vector3 p, int seed, int k)
        {
            unchecked
            {
                int x = Mathf.RoundToInt(p.x * 997f), y = Mathf.RoundToInt(p.y * 997f), z = Mathf.RoundToInt(p.z * 997f);
                uint hsh = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ seed * 2654435761u ^ k * 40503);
                hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                return (hsh & 0xffffff) / (float)0xffffff * 2f - 1f;
            }
        }

        /// <summary>A closed, chunky broken-concrete block (flat-shaded facets): chipped bevels, a craggy fracture face on one end
        /// of the longest axis, a rough top. Lattice points are displaced by a hash of their position, so faces stay sealed.</summary>
        public static Mesh ConcreteBlock(Vector3 size, int seed, out int brokenAxis, out float brokenSign)
        {
            Vector3 h = size * 0.5f;
            float minD = Mathf.Min(size.x, Mathf.Min(size.y, size.z));
            float cr = Mathf.Min(0.2f, minD * 0.16f);
            Vector3 hi = h - Vector3.one * cr;
            brokenAxis = size.x >= size.z ? (size.x >= size.y ? 0 : 1) : (size.z >= size.y ? 2 : 1);
            if (brokenAxis == 1) brokenAxis = size.x >= size.z ? 0 : 2;                              // a fracture END, never the bottom
            brokenSign = (seed & 1) == 0 ? 1f : -1f;
            int bAx = brokenAxis; float bS = brokenSign;
            float[][] coords = new float[3][];
            for (int a = 0; a < 3; a++)
            {
                float lo = -h[a] + cr, up = h[a] - cr;
                coords[a] = new[] { -h[a], -h[a] + cr * 0.4f, lo, Mathf.Lerp(lo, up, 0.25f), Mathf.Lerp(lo, up, 0.5f), Mathf.Lerp(lo, up, 0.75f), up, h[a] - cr * 0.4f, h[a] };
            }
            int N = 9;
            System.Func<Vector3, Vector3> displace = p =>
            {
                var pin = new Vector3(Mathf.Clamp(p.x, -hi.x, hi.x), Mathf.Clamp(p.y, -hi.y, hi.y), Mathf.Clamp(p.z, -hi.z, hi.z));
                var n = p - pin; n = n.sqrMagnitude > 1e-12f ? n.normalized : Vector3.up;
                var q = pin + n * cr;
                // brokenness: 1 at the fracture end, fading over 30 % of the length; the top a little rough too
                float dEnd = h[bAx] - q[bAx] * bS, b = 1f - Mathf.Clamp01(dEnd / Mathf.Max(0.3f, size[bAx] * 0.3f));
                float top = 1f - Mathf.Clamp01((h.y - q.y) / Mathf.Max(0.2f, size.y * 0.25f));
                float amp = minD * (0.035f + 0.2f * b * b + 0.06f * top);
                var j = new Vector3(Hash(q, seed, 1), Hash(q, seed, 2), Hash(q, seed, 3));
                float inward = Mathf.Abs(Hash(q, seed, 4));
                var off = j * amp * 0.55f - n * amp * (0.35f + 0.65f * inward);
                // corner chips: lattice corners (two or three axes on the rim) pulled in
                int rim = 0; for (int a = 0; a < 3; a++) if (Mathf.Abs(Mathf.Abs(q[a]) - h[a]) < cr * 0.5f || Mathf.Abs(p[a]) >= h[a] - 1e-4f) rim++;
                if (rim >= 3) off -= n * minD * (0.06f + 0.08f * Mathf.Abs(Hash(q, seed, 5)));
                return q + off;
            };
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            var grid = new Vector3[N * N];
            for (int f = 0; f < 6; f++)
            {
                int ax = f / 2, a1 = (ax + 1) % 3, a2 = (ax + 2) % 3; float sgn = f % 2 == 0 ? 1f : -1f;
                for (int i = 0; i < N; i++)
                    for (int k = 0; k < N; k++)
                    {
                        var p = Vector3.zero; p[ax] = sgn * h[ax]; p[a1] = coords[a1][i]; p[a2] = coords[a2][k];
                        grid[i * N + k] = displace(p);
                    }
                for (int i = 0; i < N - 1; i++)
                    for (int k = 0; k < N - 1; k++)
                    {
                        Vector3 v0 = grid[i * N + k], v1 = grid[i * N + k + 1], v2 = grid[(i + 1) * N + k], v3 = grid[(i + 1) * N + k + 1];
                        if (sgn > 0f) { Tri(verts, norms, tris, v0, v3, v1); Tri(verts, norms, tris, v0, v2, v3); }
                        else { Tri(verts, norms, tris, v0, v1, v3); Tri(verts, norms, tris, v0, v3, v2); }
                    }
            }
            var m = new Mesh { name = "OZ_ConcreteBlock", indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(verts); m.SetNormals(norms); m.SetTriangles(tris, 0);
            var uv = new List<Vector2>(verts.Count); foreach (var v in verts) uv.Add(new Vector2(v.x + v.z, v.y));
            m.SetUVs(0, uv); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        static void Tri(List<Vector3> vs, List<Vector3> ns, List<int> ts, Vector3 a, Vector3 b, Vector3 c)
        {
            var n = Vector3.Cross(b - a, c - a); if (n.sqrMagnitude < 1e-12f) return;
            n.Normalize(); int i = vs.Count;
            vs.Add(a); vs.Add(b); vs.Add(c); ns.Add(n); ns.Add(n); ns.Add(n); ts.Add(i); ts.Add(i + 1); ts.Add(i + 2);
        }
    }
}
