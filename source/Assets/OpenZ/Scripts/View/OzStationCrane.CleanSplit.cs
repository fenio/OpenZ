using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ
{
    /// <summary>
    /// K342b: the repair station's crane split without open faces. When a shuttle calls the crane away, <see cref="Split"/> no longer
    /// sorts the body mesh's triangles by their middles (which tore the west tower's cream top off with the beam and left a jagged
    /// hole in the deck where the east post stood, plus a piece of the post's front panel floating in the air): the body is cut by
    /// View/OzCraneCut.cs along the model's own joints, the gantry is exactly the part holding the beam, and both sides get closed caps
    /// in the station's own paint (the tower keeps its whole cream top; the deck is closed in its own colour where the post stood; the
    /// strut keeps its stub in its collar), and the thin strips of the gantry's walls the station keeps under a cut are drawn in their
    /// cap's paint (no red, white or dark outline around the seat and the socket). The cut is worked out once per body mesh (at the level's first frame, when the rams are
    /// built: no hitch when a shuttle calls) and its two meshes are made from it at every split. If the cut does not fit the model
    /// (another model, a cap that does not close), the old split is used and the log says why.
    /// </summary>
    public static partial class OzStationCrane
    {
        /// <summary>Proof: clean splits made, splits that fell back to the old cut, the last cut's report, its cap triangles, the
        /// model's own open edges and the open edges after the cut (equal = the cut left nothing open), the cut's time (ms).</summary>
        public static int CleanSplits, CleanFallbacks, CleanCapTris, CleanOpenBefore = -1, CleanOpenAfter = -1;
        public static float CleanCutMs;
        public static string CleanLog = "-";

        /// <summary>The deck's own paint for the socket where the east post stood: a point of a station face whose texel has the deck's
        /// colour around the post and stays even over the blurred mips TeamLit reads (the kit's tools/deck_texel.py), and that texel's
        /// UV (the fallback, Unity's v). The game reads the UV off the body mesh at that point (within 3 cm of a face).</summary>
        static readonly Vector3 DeckPaintAt = new Vector3(-3.0496f, 0.8330f, 3.8562f);
        static readonly Vector2 DeckPaintUv = new Vector2(0.48218f, 0.68530f);
        /// <summary>Proof: the deck's texel was read off the body's own face (else the kit's fallback).</summary>
        public static bool CleanDeckFound;

        static Vector2 DeckUv(Vector3[] F, List<int>[] tris, Vector2[] uv)
        {
            CleanDeckFound = false;
            if (uv == null || uv.Length != F.Length) return DeckPaintUv;
            float best = 0.03f; var res = DeckPaintUv; var q = DeckPaintAt;
            foreach (var l in tris)
                for (int i = 0; i + 2 < l.Count; i += 3)
                {
                    Vector3 a = F[l[i]], b = F[l[i + 1]], c = F[l[i + 2]];
                    var n = Vector3.Cross(b - a, c - a); float len = n.magnitude; if (len < 1e-10f) continue;
                    n /= len; float d = Mathf.Abs(Vector3.Dot(q - a, n)); if (d >= best) continue;
                    var p = q - n * Vector3.Dot(q - a, n);
                    Vector3 v0 = b - a, v1 = c - a, v2 = p - a;
                    float d00 = Vector3.Dot(v0, v0), d01 = Vector3.Dot(v0, v1), d11 = Vector3.Dot(v1, v1), d20 = Vector3.Dot(v2, v0), d21 = Vector3.Dot(v2, v1);
                    float den = d00 * d11 - d01 * d01; if (Mathf.Abs(den) < 1e-14f) continue;
                    float wb = (d11 * d20 - d01 * d21) / den, wc = (d00 * d21 - d01 * d20) / den, wa = 1f - wb - wc;
                    if (wa < -1e-3f || wb < -1e-3f || wc < -1e-3f) continue;
                    best = d; res = uv[l[i]] * wa + uv[l[i + 1]] * wb + uv[l[i + 2]] * wc; CleanDeckFound = true;
                }
            return res;
        }

        sealed class CutData
        {
            public OzCraneCut.Result r; public Vector3[] FP, V, N; public Vector4[] Tg; public Vector2[] U0, U1; public Color[] Col;
        }
        static readonly Dictionary<string, CutData> cutData = new Dictionary<string, CutData>();

        static Vector3 FrameOf(Transform root, Matrix4x4 m, Vector3 v) { var p = root.InverseTransformPoint(m.MultiplyPoint3x4(v)); return new Vector3(-p.x, p.y, -p.z); }

        /// <summary>The cut of this body mesh (worked out once per mesh and model placement; null = it does not fit, logged once).</summary>
        static CutData CutFor(MeshFilter mf, Transform root)
        {
            var mesh = mf ? mf.sharedMesh : null;
            if (!mesh || !root || !mesh.isReadable || mesh.name.EndsWith("_without_crane")) return null;
            var rel = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            string key = mesh.GetInstanceID() + ":" + Mathf.RoundToInt(rel.m00 * 1000f) + "," + Mathf.RoundToInt(rel.m11 * 1000f) + "," + Mathf.RoundToInt(rel.m22 * 1000f) + "," +
                         Mathf.RoundToInt(rel.m02 * 1000f) + "," + Mathf.RoundToInt(rel.m13 * 1000f) + "," + Mathf.RoundToInt(rel.m03 * 1000f) + "," + Mathf.RoundToInt(rel.m23 * 1000f);
            if (cutData.TryGetValue(key, out var have)) return have;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var vs = mesh.vertices; var m = mf.transform.localToWorldMatrix; int n0 = vs.Length;
            var F = new Vector3[n0]; for (int i = 0; i < n0; i++) F[i] = FrameOf(root, m, vs[i]);
            var nm = mesh.normals; Vector3[] FN = null;
            if (nm != null && nm.Length == n0)
            {
                FN = new Vector3[n0];
                for (int i = 0; i < n0; i++) { var d = root.InverseTransformDirection(mf.transform.TransformDirection(nm[i])); FN[i] = new Vector3(-d.x, d.y, -d.z); }
            }
            var tris = new List<int>[mesh.subMeshCount]; for (int k = 0; k < tris.Length; k++) tris[k] = new List<int>(mesh.GetTriangles(k));
            float sigma = OzCraneCut.Winding(F, tris, FN);
            var r = OzCraneCut.Run(F, tris, sigma);
            CutData cd = null;
            int nG = 0, nAll = 0; foreach (var l in r.part) nG += l.Count / 3; foreach (var l in r.rest) nAll += l.Count / 3; nAll += nG;
            if (!r.ok || nG < 0.2f * nAll || nG > 0.5f * nAll)
                Debug.LogWarning($"[OpenZ] crane (K342b): the clean cut does not fit {mesh.name} ({r.log}; gantry {nG} of {nAll}): the old split is used");
            else
            {
                cd = new CutData { r = r };
                int n = r.P.Count; cd.FP = r.P.ToArray();
                var V = new Vector3[n]; System.Array.Copy(vs, V, n0);
                var N = nm != null && nm.Length == n0 ? new Vector3[n] : null; if (N != null) System.Array.Copy(nm, N, n0);
                var tg0 = mesh.tangents; var Tg = tg0 != null && tg0.Length == n0 ? new Vector4[n] : null; if (Tg != null) System.Array.Copy(tg0, Tg, n0);
                var u0 = mesh.uv; var U0 = u0 != null && u0.Length == n0 ? new Vector2[n] : null; if (U0 != null) System.Array.Copy(u0, U0, n0);
                var u1 = mesh.uv2; var U1 = u1 != null && u1.Length == n0 ? new Vector2[n] : null; if (U1 != null) System.Array.Copy(u1, U1, n0);
                var c0 = mesh.colors; var Col = c0 != null && c0.Length == n0 ? new Color[n] : null; if (Col != null) System.Array.Copy(c0, Col, n0);
                var p3 = PaintUvs(mf, root);                                                          // the station's own texels (team paint, cream, dark): K342
                var paint = new[] { p3[0], p3[1], p3[2], DeckUv(F, tris, u0) };                // + the deck's own (OzCraneCut.PaintDeck)
                var capN = new Vector3[6];
                for (int a = 0; a < 3; a++)
                    for (int sg = 0; sg < 2; sg++)
                    {
                        var e = Vector3.zero; e[a] = sg == 0 ? -1f : 1f;
                        var w = root.TransformDirection(new Vector3(-e.x, e.y, -e.z));
                        capN[a * 2 + sg] = mf.transform.InverseTransformDirection(w).normalized;
                    }
                for (int i = n0; i < n; i++)
                {
                    int j = i - n0, a = r.srcA[j], b = r.srcB[j];
                    if (r.capAxis[j] == -2)
                    {   // a rim's point: its source's place, normal and the rest, the cap's paint
                        V[i] = V[a];
                        if (N != null) N[i] = N[a];
                        if (Tg != null) Tg[i] = Tg[a];
                        if (U0 != null) U0[i] = paint[Mathf.Clamp(r.capPaint[j], 0, paint.Length - 1)];
                        if (U1 != null) U1[i] = U1[a];
                        if (Col != null) Col[i] = Col[a];
                    }
                    else if (r.capAxis[j] < 0)
                    {   // a cut's point: its two sources mixed (the plane's place on the triangle's edge)
                        float t = r.srcT[j];
                        V[i] = Vector3.Lerp(V[a], V[b], t);
                        if (N != null) N[i] = Vector3.Lerp(N[a], N[b], t).normalized;
                        if (Tg != null) { var x = Vector3.Lerp((Vector3)Tg[a], (Vector3)Tg[b], t).normalized; Tg[i] = new Vector4(x.x, x.y, x.z, Tg[a].w); }
                        if (U0 != null) U0[i] = Vector2.Lerp(U0[a], U0[b], t);
                        if (U1 != null) U1[i] = Vector2.Lerp(U1[a], U1[b], t);
                        if (Col != null) Col[i] = Color.Lerp(Col[a], Col[b], t);
                    }
                    else
                    {   // a cap's point: the place of its source, the cap's own normal and paint
                        var cn = capN[r.capAxis[j] * 2 + (r.capSign[j] < 0 ? 0 : 1)];
                        V[i] = V[a];
                        if (N != null) N[i] = cn;
                        if (Tg != null) { var x = Vector3.Cross(cn, Mathf.Abs(cn.y) < 0.9f ? Vector3.up : Vector3.right).normalized; Tg[i] = new Vector4(x.x, x.y, x.z, 1f); }
                        if (U0 != null) U0[i] = paint[Mathf.Clamp(r.capPaint[j], 0, paint.Length - 1)];
                        if (U1 != null) U1[i] = U1[a];
                        if (Col != null) Col[i] = Col[a];
                    }
                }
                cd.V = V; cd.N = N; cd.Tg = Tg; cd.U0 = U0; cd.U1 = U1; cd.Col = Col;
            }
            sw.Stop(); CleanCutMs = (float)sw.Elapsed.TotalMilliseconds;
            CleanCapTris = r.capTris; CleanOpenBefore = r.openBefore; CleanOpenAfter = r.openAfter;
            CleanLog = $"{mesh.name}: {r.log} ({CleanCutMs:F0} ms, winding {sigma:F0}; the deck's texel {(CleanDeckFound ? "read off its own face" : "the kit's fallback")})";
            Debug.Log("[OpenZ] crane (K342b): clean cut " + (cd != null ? "ready" : "NOT used") + ": " + CleanLog);
            cutData[key] = cd;
            return cd;
        }

        /// <summary>Split's hook: the two meshes from the clean cut; false = the old split by the triangles' middles is used.</summary>
        static bool CleanSplit(St s, Mesh mesh, Vector3[] F, ref Vector3[] FP, List<int>[] restT, List<int>[] partT, ref int nG, ref int nAll)
        {
            var cd = CutFor(s.mf, s.root);
            if (cd == null || cd.r.rest.Length != mesh.subMeshCount) { CleanFallbacks++; return false; }
            nG = 0; nAll = 0;
            for (int k = 0; k < mesh.subMeshCount; k++)
            {
                restT[k] = new List<int>(cd.r.rest[k]); partT[k] = new List<int>(cd.r.part[k]);
                nG += partT[k].Count / 3; nAll += (partT[k].Count + restT[k].Count) / 3;
            }
            FP = cd.FP;
            Mesh Make(List<int>[] T, string name)
            {   // only the points its own triangles use (the station keeps none of the gantry's points, and the other way round)
                var map = new Dictionary<int, int>(); var keep = new List<int>();
                var TT = new List<int>[T.Length];
                for (int k = 0; k < T.Length; k++)
                {
                    TT[k] = new List<int>(T[k].Count);
                    foreach (int v in T[k]) { if (!map.TryGetValue(v, out int j)) { j = keep.Count; map[v] = j; keep.Add(v); } TT[k].Add(j); }
                }
                T2[] Pick<T2>(T2[] a) { if (a == null) return null; var o = new T2[keep.Count]; for (int i = 0; i < keep.Count; i++) o[i] = a[keep[i]]; return o; }
                var m = new Mesh { name = name };
                if (keep.Count > 65535) m.indexFormat = IndexFormat.UInt32;
                m.vertices = Pick(cd.V);
                if (cd.N != null) m.normals = Pick(cd.N);
                if (cd.Tg != null) m.tangents = Pick(cd.Tg);
                if (cd.U0 != null) m.uv = Pick(cd.U0);
                if (cd.U1 != null) m.uv2 = Pick(cd.U1);
                if (cd.Col != null) m.colors = Pick(cd.Col);
                m.subMeshCount = TT.Length; for (int k = 0; k < TT.Length; k++) m.SetTriangles(TT[k], k, false);
                m.RecalculateBounds(); return m;
            }
            s.rest = Make(restT, mesh.name + "_without_crane"); s.part = Make(partT, mesh.name + "_crane");
            CleanSplits++;
            return true;
        }
    }
}
