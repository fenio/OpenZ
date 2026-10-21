using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16: the tall thin-trunked jungle trees of the flora kit (the kapok shapes FL_TreeKapokA / B and their lite versions)
    /// had a trunk of a few thin bark shards that meet in points, so their small crowns seemed to float over the forest in the
    /// morning haze. On every import of the kit each of these shapes gets a SOLID trunk: a round column of bark (Sides faces) along
    /// the trunk's own middle line, from just under the foot up into the crown's base (TopShare of the height), its radius RBase of
    /// the height at the foot narrowing to RTop at the top; its faces take the bark strip of the shape's own longest trunk shard in
    /// the picture, so it is the same bark, tinted darker by vertex colour (the two kapok materials use vertex colour: the menu below
    /// switches it on; every old point is white). The old shards and the root flares stay round it.
    /// The far versions (_far) are left alone (the joined far meshes of the cells hold their points).
    /// The scene's plants share these meshes, so every such tree of the level follows; nothing in the scene changes.
    /// Check: the menu below reads the trunk widths of the four shapes as they are now.
    /// </summary>
    public class OzKapokTrunkPost : AssetPostprocessor
    {
        public const string Kit = "Assets/OpenZ/Models/L16/Flora/L16_flora_kit.fbx";
        public static int Sides = 8;
        /// <summary>Radius at the foot and at the top (share of the tree's height), the column's top (share of the height), its foot under the ground (share).</summary>
        public static float RBase = 0.034f, RTop = 0.018f, TopShare = 0.64f, Under = 0.02f;
        /// <summary>The column's bark: its spot in the kapok pictures and a darker tint on it (vertex colour; the kapok materials use
        /// vertex colour, every old point is white), so the trunk reads solid in the morning haze.</summary>
        public static Vector2 BarkUv = new Vector2(0.156f, 0.273f); public static Color BarkTint = new Color(0.62f, 0.56f, 0.5f, 1f);
        public static readonly string[] Mats = { "Assets/OpenZ/Models/L16/Flora/M_L16_Flora_JG_TreeKapokA_tex.mat", "Assets/OpenZ/Models/L16/Flora/M_L16_Flora_JG_TreeKapokB_tex.mat" };
        public static string Last = "not imported in this editor session";

        void OnPostprocessModel(GameObject root)
        {
            if (assetPath != Kit) return;
            int n = 0; var sb = new System.Text.StringBuilder();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                var t = mf.transform;
                if (!t.name.StartsWith("FL_TreeKapok") || t.name.EndsWith("_far") || !mf.sharedMesh) continue;
                string before = Trunk(mf.sharedMesh);
                if (AddTrunk(mf.sharedMesh)) { n++; sb.Append($" | {t.name}: trunk {before} -> {Trunk(mf.sharedMesh)}"); }
            }
            Last = $"kapok trunks made solid on import: {n} shape(s) (a column of {Sides} faces, {RBase:P1} of the height wide at the foot, {RTop:P1} at {TopShare:P0} of the height){sb}";
            Debug.Log("[OpenZ L16] " + Last);
        }

        /// <summary>The shape's long axis is its height; the foot is the end with fewer points (the crown holds most of them).</summary>
        static void Frame(Vector3[] v, Bounds b, out int up, out float foot, out float len)
        {
            var s = b.size; up = s.z >= s.x && s.z >= s.y ? 2 : s.y >= s.x ? 1 : 0;
            float lo = b.min[up], hi = b.max[up]; len = Mathf.Max(0.01f, hi - lo);
            int nLo = 0, nHi = 0;
            foreach (var p in v) { float f = (p[up] - lo) / len; if (f < 0.1f) nLo++; else if (f > 0.9f) nHi++; }
            foot = nLo <= nHi ? lo : hi;
        }

        static float H(Vector3 p, int up, float foot, float len) => Mathf.Abs(p[up] - foot) / len;
        static Vector2 Flat(Vector3 p, int up) => up == 2 ? new Vector2(p.x, p.y) : up == 1 ? new Vector2(p.x, p.z) : new Vector2(p.y, p.z);
        static Vector3 Point(Vector2 f, float h, int up, float foot, float len, float sign)
        {
            float a = foot + sign * h * len;
            return up == 2 ? new Vector3(f.x, f.y, a) : up == 1 ? new Vector3(f.x, a, f.y) : new Vector3(a, f.x, f.y);
        }
        static Vector3 Dir(Vector2 f, int up) => up == 2 ? new Vector3(f.x, f.y, 0f) : up == 1 ? new Vector3(f.x, 0f, f.y) : new Vector3(0f, f.x, f.y);

        /// <summary>The trunk's middle line: the mean of the trunk points low (5 to 25 % of the height) and high (25 to 55 %), each at
        /// its own mean height.</summary>
        static bool Axis(Vector3[] v, int up, float foot, float len, out Vector3 lo, out Vector3 hi)
        {
            lo = hi = Vector3.zero; int nl = 0, nh = 0;
            foreach (var p in v)
            {
                float h = H(p, up, foot, len); var f = Flat(p, up);
                if (h >= 0.05f && h < 0.25f) { lo += new Vector3(f.x, f.y, h); nl++; } else if (h >= 0.25f && h <= 0.55f) { hi += new Vector3(f.x, f.y, h); nh++; }
            }
            if (nl == 0 || nh == 0) return false;
            lo /= nl; hi /= nh; return hi.z - lo.z > 0.02f;
        }

        static Vector2 AxisAt(float h, Vector3 lo, Vector3 hi) { var p = Vector3.LerpUnclamped(lo, hi, (Mathf.Clamp(h, 0f, 0.7f) - lo.z) / (hi.z - lo.z)); return new Vector2(p.x, p.y); }

        /// <summary>Adds the column (see the class note). False: no trunk line found, or the shape has it already.</summary>
        public static bool AddTrunk(Mesh m)
        {
            int n0 = m.vertexCount; if (n0 == 0) return false;
            var v = new List<Vector3>(); m.GetVertices(v);
            Frame(v.ToArray(), m.bounds, out int up, out float foot, out float len);
            float sign = Mathf.Approximately(foot, m.bounds.min[up]) ? 1f : -1f;
            if (!Axis(v.ToArray(), up, foot, len, out var cLo, out var cHi)) return false;
            // the bark: the middle of the largest bark patch of the kapok pictures (bark colour 20 px round it, so it stays bark in the
            // smaller copies the engine draws far away), a little apart face by face
            var uv = new List<Vector2>(); m.GetUVs(0, uv); bool hasUv = uv.Count == n0;
            var tris = m.GetTriangles(0);
            var nr = new List<Vector3>(); m.GetNormals(nr); bool hasN = nr.Count == n0;
            var tg = new List<Vector4>(); m.GetTangents(tg); bool hasT = tg.Count == n0;
            var col = new List<Color>(); m.GetColors(col); if (col.Count != n0) { col.Clear(); for (int i = 0; i < n0; i++) col.Add(Color.white); }   // white: the old points keep their look
            var uv2 = new List<Vector2>(); m.GetUVs(1, uv2); bool hasUv2 = uv2.Count == n0;
            float[] rings = { -Under, 0.12f, 0.25f, 0.38f, 0.5f, TopShare };
            int S = Mathf.Max(5, Sides), ringN = S + 1;                                               // one seam point twice (the uv wraps)
            for (int r = 0; r < rings.Length; r++)
            {
                float h = rings[r], k = Mathf.InverseLerp(0f, TopShare, Mathf.Max(0f, h));
                float rad = Mathf.Lerp(RBase, RTop, k) * len; var mid = AxisAt(h, cLo, cHi);
                for (int s = 0; s <= S; s++)
                {
                    float ang = s * Mathf.PI * 2f / S; var d = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                    v.Add(Point(mid + d * rad, h, up, foot, len, sign));
                    if (hasN) nr.Add(Dir(d, up).normalized);
                    if (hasT) { var tdir = Dir(new Vector2(-d.y, d.x), up).normalized; tg.Add(new Vector4(tdir.x, tdir.y, tdir.z, 1f)); }
                    col.Add(BarkTint);
                    if (hasUv) uv.Add(BarkUv + new Vector2((s % 2) * 0.008f, k * 0.012f));
                    if (hasUv2) uv2.Add(Vector2.zero);
                }
            }
            var add = new List<int>();
            for (int r = 0; r + 1 < rings.Length; r++)
                for (int s = 0; s < S; s++)
                {
                    int a = n0 + r * ringN + s, b = a + 1, c = a + ringN, d = c + 1;
                    var out0 = (v[a] + v[b] + v[c] + v[d]) * 0.25f; var mid = Point(AxisAt((rings[r] + rings[r + 1]) * 0.5f, cLo, cHi), (rings[r] + rings[r + 1]) * 0.5f, up, foot, len, sign);
                    var outward = out0 - mid;
                    bool keep = Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), outward) > 0f;
                    if (keep) { add.Add(a); add.Add(b); add.Add(c); add.Add(b); add.Add(d); add.Add(c); }
                    else { add.Add(a); add.Add(c); add.Add(b); add.Add(b); add.Add(c); add.Add(d); }
                }
            if (v.Count > 65000 && m.indexFormat == UnityEngine.Rendering.IndexFormat.UInt16) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            if (hasN) m.SetNormals(nr);
            if (hasT) m.SetTangents(tg);
            m.SetColors(col);
            if (hasUv) m.SetUVs(0, uv);
            if (hasUv2) m.SetUVs(1, uv2);
            var all = new List<int>(tris); all.AddRange(add);
            m.SetTriangles(all, 0);
            m.RecalculateBounds();
            return true;
        }

        /// <summary>The trunk's width in one line: the median distance of its points from its middle line at 15-45 % of the height (m, the shape's own size).</summary>
        public static string Trunk(Mesh m)
        {
            var v = m.vertices; if (v.Length == 0) return "empty";
            Frame(v, m.bounds, out int up, out float foot, out float len);
            if (!Axis(v, up, foot, len, out var cLo, out var cHi)) return "no trunk found";
            var rs = new List<float>();
            foreach (var p in v) { float h = H(p, up, foot, len); if (h >= 0.15f && h <= 0.45f) rs.Add((Flat(p, up) - AxisAt(h, cLo, cHi)).magnitude); }
            if (rs.Count == 0) return "no trunk points";
            rs.Sort(); return $"radius {rs[rs.Count / 2]:F2} m (median of {rs.Count}, most {rs[rs.Count - 1]:F2} m), {v.Length} points, height {len:F1} m";
        }

        [MenuItem("OpenZ/L16 Jungle/Checks/Kapok trunks: widths now (edit mode)")]
        public static void TrunkMenu() => Debug.Log("[OpenZ L16] " + TrunkReport());

        /// <summary>The two kapok materials take the vertex colour (the column's darker bark); returns what it set.</summary>
        public static string MaterialsUseVertexColour()
        {
            var sb = new System.Text.StringBuilder("kapok materials:");
            foreach (var p in Mats)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(p); if (!m) { sb.Append(" missing " + p); continue; }
                m.SetFloat("_UseVertexColor", 1f); m.SetFloat("_VertexColorSRGB", 0f); EditorUtility.SetDirty(m); sb.Append($" {m.name} vertex colour on;");
            }
            AssetDatabase.SaveAssets(); return sb.ToString();
        }

        public static string TrunkReport()
        {
            var sb = new System.Text.StringBuilder("kapok trunks now:");
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(Kit))
                if (o is Mesh m && m.name.StartsWith("FL_TreeKapok")) sb.Append($" | {m.name}: {Trunk(m)}");
            return sb.ToString();
        }
    }
}
