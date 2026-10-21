using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256e (FIX_LIST S2, "the ramp hardly reads by day": a dark-grey ramp on a black hull): the shuttle's ramp leaf gets a light-grey metal
    /// tread on its INNER face (albedo about 0.6, fine tread lines) with yellow / black hazard stripes along its two sides and its tip, so the
    /// open ramp reads at 70 m like the APC's door. View only. How: the leaf's mesh (ShuttleRearDoor's DR_RampLeaf) is copied once per
    /// shape with a second part: its inner triangles again, 5 mm toward the inside, with flat texture coordinates across and along the
    /// deck, drawn with a light painted copy of the hull's own material. The original triangles stay as they were (part 0, the hull's
    /// paint), so whatever reads the leaf's mesh with its first material alone (the break-up's piece) still has a whole leaf. The outer
    /// skin keeps the hull's paint and team mask. Added by OzShuttleNightLights at the ship's bind; it waits for the door's kit.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class OzShuttleRampPaint : MonoBehaviour
    {
        public static float Grey = 0.60f, Lines = 0.50f, Lift = 0.005f, SideBand = 0.11f, TipBand = 0.09f, StripeK = 9f;
        public static Color HazardYellow = new Color(1f, 0.78f, 0.10f), HazardBlack = new Color(0.07f, 0.07f, 0.07f);
        /// <summary>Proof: leaves painted, their inner triangles; the last line.</summary>
        public static int Painted, InnerTris; public static string LastLog = "-";

        static readonly Dictionary<Mesh, Mesh> meshes = new Dictionary<Mesh, Mesh>();
        static readonly Dictionary<Material, Material> mats = new Dictionary<Material, Material>();
        static Texture2D tex;
        float giveUp; bool done;

        /// <summary>Adds the painter to a shuttle's root (once).</summary>
        public static void Apply(Transform root)
        {
            if (!root || root.GetComponent<OzShuttleRampPaint>()) return;
            root.gameObject.AddComponent<OzShuttleRampPaint>().giveUp = Time.unscaledTime + 15f;
        }

        void LateUpdate()
        {
            if (done) { enabled = false; return; }
            if (Time.unscaledTime > giveUp) { LastLog = "no ramp leaf found on " + name; enabled = false; return; }
            Transform leaf = null;
            foreach (var t in GetComponentsInChildren<Transform>(true)) if (t.name == "DR_RampLeaf") { leaf = t; break; }
            if (!leaf) return;
            var mf = leaf.GetComponent<MeshFilter>(); var mr = leaf.GetComponent<MeshRenderer>();
            if (!mf || !mr || !mf.sharedMesh || !mr.sharedMaterial) return;
            done = true;
            if (mr.sharedMaterials.Length > 1) { LastLog = "the ramp leaf is painted already"; return; }
            var src = mf.sharedMesh;
            if (!src.isReadable) { LastLog = "the ramp leaf's mesh is not readable: unpainted"; Debug.LogWarning("[OpenZ] shuttle ramp paint: " + LastLog); return; }
            if (!meshes.TryGetValue(src, out var m) || !m) { m = Build(src, leaf, transform); meshes[src] = m; }
            if (!m) { LastLog = "no inner face found on the ramp leaf: unpainted"; return; }
            mf.sharedMesh = m;
            mr.sharedMaterials = new[] { mr.sharedMaterial, Paint(mr.sharedMaterial) };
            var tp = GetComponentInParent<TeamPaint>(); if (tp) tp.Apply();
            Painted++;
            LastLog = $"ramp leaf painted on {name}: {m.GetTriangles(1).Length / 3} inner triangles (light metal, hazard edges)";
        }

        /// <summary>The leaf's mesh with a second part: the inner triangles (their face turned toward the ship's middle), lifted 5 mm inward,
        /// with flat coordinates (u across, v from the hinge to the tip).</summary>
        static Mesh Build(Mesh src, Transform leaf, Transform root)
        {
            var vs = src.vertices; var tris = src.triangles;
            var toRoot = root.worldToLocalMatrix * leaf.localToWorldMatrix;
            var mid = new Vector3(0f, 2.6f, 0f);                                                          // the hull's middle (root space)
            var inner = new List<int>();
            for (int i = 0; i + 2 < tris.Length; i += 3)
            {
                Vector3 a = vs[tris[i]], b = vs[tris[i + 1]], c = vs[tris[i + 2]];
                var n = Vector3.Cross(b - a, c - a); if (n.sqrMagnitude < 1e-12f) continue;
                var nr = toRoot.MultiplyVector(n).normalized; var cr = toRoot.MultiplyPoint3x4((a + b + c) / 3f);
                if (Vector3.Dot(nr, mid - cr) > 0.25f * (mid - cr).magnitude) inner.Add(i);                 // faces the ship's inside
            }
            if (inner.Count == 0) return null;
            // the deck's frame in root space: across = the root's x, along = from the hinge side to the tip (the farthest from the middle)
            float x0 = 1e9f, x1 = -1e9f; Vector3 lo = Vector3.zero, hi = Vector3.zero; float dLo = 1e9f, dHi = -1e9f;
            foreach (int i in inner)
                for (int k = 0; k < 3; k++)
                {
                    var p = toRoot.MultiplyPoint3x4(vs[tris[i + k]]);
                    x0 = Mathf.Min(x0, p.x); x1 = Mathf.Max(x1, p.x);
                    float d = (p - mid).magnitude; if (d < dLo) { dLo = d; lo = p; } if (d > dHi) { dHi = d; hi = p; }
                }
            var along = new Vector3(0f, hi.y - lo.y, hi.z - lo.z); float len = along.magnitude; along = len > 1e-4f ? along / len : Vector3.back;
            // every channel of the original is kept (the outer skin looks exactly as before); the copies of the inner vertices take the
            // original's values, their own flat coordinates in channel 0 and their face's way
            int n0 = vs.Length;
            var nv = new List<Vector3>(vs);
            var nn = new List<Vector3>(); src.GetNormals(nn); bool hasN = nn.Count == n0;
            var tg = new List<Vector4>(); src.GetTangents(tg); bool hasT = tg.Count == n0;
            var col = new List<Color32>(); src.GetColors(col); bool hasC = col.Count == n0;
            var uv = new List<Vector4>[4]; var hasU = new bool[4];
            for (int ch = 0; ch < 4; ch++) { uv[ch] = new List<Vector4>(); src.GetUVs(ch, uv[ch]); hasU[ch] = uv[ch].Count == n0; }
            if (!hasU[0]) { uv[0].Clear(); for (int i = 0; i < n0; i++) uv[0].Add(Vector4.zero); hasU[0] = true; }
            var t1 = new List<int>();
            foreach (int i in inner)
            {
                Vector3 a = vs[tris[i]], b = vs[tris[i + 1]], c = vs[tris[i + 2]];
                var n = Vector3.Cross(b - a, c - a).normalized;
                float lift = Lift / Mathf.Max(1e-4f, toRoot.MultiplyVector(n).magnitude);
                for (int k = 0; k < 3; k++)
                {
                    int vi = tris[i + k]; var p = vs[vi]; var pr = toRoot.MultiplyPoint3x4(p);
                    float u = Mathf.InverseLerp(x0, x1, pr.x), v = Mathf.Clamp01(Vector3.Dot(new Vector3(0f, pr.y - lo.y, pr.z - lo.z), along) / Mathf.Max(0.01f, len));
                    nv.Add(p + n * lift);
                    if (hasN) nn.Add(n);
                    if (hasT) tg.Add(tg[vi]);
                    if (hasC) col.Add(col[vi]);
                    for (int ch = 0; ch < 4; ch++) if (hasU[ch]) uv[ch].Add(ch == 0 ? new Vector4(u, v, 0f, 0f) : uv[ch][vi]);
                    t1.Add(nv.Count - 1);
                }
            }
            var m = Object.Instantiate(src); m.name = src.name + "_painted";
            if (nv.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.subMeshCount = 1; m.SetTriangles(tris, 0, false);
            m.SetVertices(nv);
            if (hasN) m.SetNormals(nn);
            if (hasT) m.SetTangents(tg);
            if (hasC) m.SetColors(col);
            for (int ch = 0; ch < 4; ch++) if (hasU[ch]) m.SetUVs(ch, uv[ch]);
            m.subMeshCount = 2; m.SetTriangles(tris, 0, false); m.SetTriangles(t1, 1, false);
            m.RecalculateBounds();
            InnerTris = t1.Count / 3;
            return m;
        }

        static readonly int BaseMapId = Shader.PropertyToID("_BaseMap"), BaseColorId = Shader.PropertyToID("_BaseColor"), EmissionId = Shader.PropertyToID("_EmissionColor"),
                            SmoothId = Shader.PropertyToID("_Smoothness"), MetalId = Shader.PropertyToID("_Metallic"), TeamModeId = Shader.PropertyToID("_TeamMode"),
                            VcId = Shader.PropertyToID("_UseVertexColor"), PfaId = Shader.PropertyToID("_PaintFromAlpha"), BumpId = Shader.PropertyToID("_BumpMap"),
                            EmMapColorId = Shader.PropertyToID("_EmissionMapColor");

        /// <summary>The deck's material: the hull's own material, painted light metal with the hazard texture.</summary>
        static Material Paint(Material hull)
        {
            if (mats.TryGetValue(hull, out var m) && m) return m;
            m = new Material(hull) { name = "OZ_ShuttleRampDeck" };
            if (m.HasProperty(BaseMapId)) m.SetTexture(BaseMapId, Tex());
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, Color.white); else m.color = Color.white;
            if (m.HasProperty(SmoothId)) m.SetFloat(SmoothId, 0.38f);
            if (m.HasProperty(MetalId)) m.SetFloat(MetalId, 0.55f);
            if (m.HasProperty(VcId)) m.SetFloat(VcId, 0f);
            if (m.HasProperty(PfaId)) m.SetFloat(PfaId, 0f);
            if (m.HasProperty(TeamModeId)) m.SetFloat(TeamModeId, 0f);
            if (m.HasProperty(BumpId)) m.SetTexture(BumpId, null);
            if (m.HasProperty(EmissionId)) m.SetColor(EmissionId, Color.black);
            if (m.HasProperty(EmMapColorId)) m.SetColor(EmMapColorId, Color.black);
            mats[hull] = m;
            return m;
        }

        /// <summary>The tread: light grey metal with fine lines across, diagonal yellow / black hazard bands along both sides and the tip.</summary>
        static Texture2D Tex()
        {
            if (tex) return tex;
            const int W = 128, H = 256;
            tex = new Texture2D(W, H, TextureFormat.RGBA32, true) { name = "OZ_ShuttleRampTread", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color32[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float u = (x + 0.5f) / W, v = (y + 0.5f) / H;
                    bool band = u < SideBand || u > 1f - SideBand || v > 1f - TipBand;
                    Color c;
                    if (band) c = Mathf.Repeat((u + v * 0.5f) * StripeK, 1f) < 0.5f ? HazardYellow : HazardBlack;
                    else { bool line = Mathf.Repeat(v * 22f, 1f) < 0.12f; c = new Color(line ? Lines : Grey, line ? Lines : Grey, (line ? Lines : Grey) + 0.02f); }
                    px[y * W + x] = c;
                }
            tex.SetPixels32(px); tex.Apply(true, false);
            return tex;
        }
    }
}
