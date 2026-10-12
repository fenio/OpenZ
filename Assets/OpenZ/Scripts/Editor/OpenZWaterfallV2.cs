using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// OpenZ/Details v2/Waterfall v2 (L02 D01). Rebuilds the falling water of the D01 waterfall from the REAL cliff:
    /// temporary mesh colliders of the cliff, lip rocks, ground and lake; a ball (0.12 m in the gully, 0.3 m on the face)
    /// rolls from under the top pool down the face with momentum (it can only bend downwards 15 deg per 5 cm, so it arcs
    /// over every ledge in a small free fall), its path is smoothed with the clearance kept, the lower 3.3 m flare out into
    /// the pond (the nearest point with >= 0.25 m of water and water on both sides of the sheet) and end 0.4 m UNDER the lake
    /// surface. Nine lanes across (0.85 m wide at the lip, 1.85 m at the plunge) are pushed out along the path normal until
    /// each sits >= 0.25 m off the rock. The sheet (WATER_Upper_Sheet_D01, OpenZ/WaterfallSheet) replaces the kit's flat
    /// WATER_Upper / WATER_Lower strips (renderers off, kept); a foam ring (WATER_Stream_Ring_D01, clipped to the water) lies
    /// on the lake at the plunge; FX_Foam / FX_Spray / FX_Mist / LIGHT_ / SND_ move to the plunge, FX_Spray_Top to the lip;
    /// the top pool (it floated ~0.6 m over its hollow) sits 8 cm over the hollow floor. Idempotent; also re-run by
    /// Details v2/Build and Place. View only: no sim change, no Zod tile touched. The lake itself is NOT carved (the water
    /// v4 bed map would have to be re-baked): the fall is routed into the pond instead.
    /// </summary>
    public static class OpenZWaterfallV2
    {
        const string SheetMesh = "Assets/OpenZ/Models/DetailsV2/WF_D01_Sheet_v2.asset";
        const string RingMesh = "Assets/OpenZ/Models/DetailsV2/WF_D01_Ring_v2.asset";
        const string MatPath = "Assets/OpenZ/Materials/DetailsV2/M_DV2_WaterfallSheet_L02.mat";
        const string WaterTex = "Assets/OpenZ/Models/DetailsV2/dv2_water_L02.png";
        static readonly string[] Solid = { "CLF3_Rock_N", "CLF3_RimRubble", "CLF3_Scree", "BODY_LipRocks_D01", "TER_RiverBed", "GND3_FloorPatch", "TER_Ground" };
        const int Lanes = 9;

        static GameObject tmp;
        static readonly Collider[] buf = new Collider[16];

        [MenuItem("OpenZ/Details v2/Waterfall v2 (rebuild L02 D01 from the cliff)")]
        public static string Menu()
        {
            var r = Apply(null);
            var s = EditorSceneManager.GetActiveScene(); EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s);
            AssetDatabase.SaveAssets();
            Debug.Log(r);
            return r;
        }

        static bool Blocked(Vector3 p, float r)
        {
            int n = Physics.OverlapSphereNonAlloc(p, r, buf, 1 << 2, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++) if (buf[i] && buf[i].transform.IsChildOf(tmp.transform) && buf[i].name != "WAT_Water") return true;
            return false;
        }

        /// <summary>Highest solid (bed / rock) under xz, and whether lake water lies over it.</summary>
        static float Bed(float x, float z, out float waterY)
        {
            waterY = float.NaN; float bed = -99f;
            foreach (var h in Physics.RaycastAll(new Vector3(x, 40f, z), Vector3.down, 60f, 1 << 2, QueryTriggerInteraction.Ignore))
            {
                if (!h.collider.transform.IsChildOf(tmp.transform)) continue;
                if (h.collider.name == "WAT_Water") waterY = h.point.y; else bed = Mathf.Max(bed, h.point.y);
            }
            return bed;
        }

        static void AddCollider(Transform src, Mesh m, string name)
        {
            var g = new GameObject(name); g.layer = 2; g.transform.SetParent(tmp.transform, false);
            g.transform.position = src.position; g.transform.rotation = src.rotation; g.transform.localScale = src.lossyScale;
            g.AddComponent<MeshCollider>().sharedMesh = m;
        }

        public static string Apply(Transform detailsRoot)
        {
            var sb = new StringBuilder("[OpenZ] waterfall v2: ");
            var wf = detailsRoot ? detailsRoot.GetComponentInChildren<OpenZ.WaterfallFx>(true)
                                 : Object.FindObjectsByType<OpenZ.WaterfallFx>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
            if (!wf) return sb.Append("no WaterfallFx in the open scene").ToString();
            var root = wf.transform;
            var kids = root.GetComponentsInChildren<Transform>(true);
            System.Func<string, Transform> kid = pre => kids.FirstOrDefault(t => t && OpenZ.DetailKit.Base(t.name).StartsWith(pre));
            var pond = kid("WATER_Pond"); var up = kid("WATER_Upper_D"); var lo = kid("WATER_Lower");
            if (!pond) return sb.Append("WATER_Pond missing").ToString();
            foreach (var old in kids.Where(t => t && (t.name.StartsWith("WATER_Upper_Sheet") || t.name.StartsWith("WATER_Stream_Ring"))).ToList())
                Object.DestroyImmediate(old.gameObject);

            tmp = new GameObject("__wf_v2_tmp");
            try
            {
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
                    if (mf.sharedMesh && (Solid.Contains(mf.name) || mf.name == "WAT_Water") && !mf.transform.IsChildOf(tmp.transform))
                        AddCollider(mf.transform, mf.sharedMesh, mf.name);
                var col = GameObject.Find("COL_Terrain");
                if (col && col.GetComponent<MeshCollider>()) AddCollider(col.transform, col.GetComponent<MeshCollider>().sharedMesh, "COL_Terrain");
                Physics.SyncTransforms();

                // ---- 1. the top pool sits on its hollow (it floated ~0.6 m)
                var pb = pond.GetComponent<Renderer>().bounds;
                float floor = -99f, wy;
                for (float a = 0f; a < 6.28f; a += 0.39f)
                    for (float rr = 0f; rr <= 0.8f; rr += 0.2f)
                    {
                        float x = pb.center.x + Mathf.Cos(a) * rr * pb.extents.x, z = pb.center.z + Mathf.Sin(a) * rr * pb.extents.z;
                        foreach (var h in Physics.RaycastAll(new Vector3(x, 40f, z), Vector3.down, 60f, 1 << 2))
                            if (h.collider.transform.IsChildOf(tmp.transform) && h.collider.name != "BODY_LipRocks_D01" && h.collider.name != "WAT_Water")
                                floor = Mathf.Max(floor, h.point.y);
                    }
                float pondY = Mathf.Min(pond.position.y, floor + 0.08f);
                if (floor > -90f) { var pp = pond.position; pp.y = pondY; pond.position = pp; } else pondY = pond.position.y;
                sb.Append("top pool y " + pondY.ToString("0.00") + " (hollow floor max " + floor.ToString("0.00") + "); ");

                // ---- 2. landing: nearest point in the pond under the fall with >= 0.25 m of water and water both sides
                float lakeY = float.NaN; Vector3 land = Vector3.zero; float best = 1e9f;
                var basePt = new Vector2(pb.center.x - 0.2f, -11.5f);
                for (float x = pb.center.x - 3.5f; x <= pb.center.x + 2.5f; x += 0.1f)
                    for (float z = basePt.y; z >= basePt.y - 4f; z -= 0.1f)
                    {
                        float b = Bed(x, z, out wy); if (float.IsNaN(wy) || b > wy - 0.25f) continue;
                        bool ok = true;
                        foreach (var o in new[] { new Vector2(-0.8f, 0f), new Vector2(0.8f, 0f), new Vector2(0f, 0.4f), new Vector2(0f, -0.45f), new Vector2(-0.6f, 0.3f), new Vector2(0.6f, 0.3f) })
                        { float w2; float b2 = Bed(x + o.x, z + o.y, out w2); if (float.IsNaN(w2) || b2 > w2 - 0.08f) { ok = false; break; } }
                        if (!ok) continue;
                        float d = (new Vector2(x, z) - basePt).sqrMagnitude;
                        if (d < best) { best = d; land = new Vector3(x, wy, z); lakeY = wy; }
                    }
                if (float.IsNaN(lakeY)) return sb.Append("FAILED: no pond point found under the fall").ToString();
                sb.Append("plunge " + land.ToString("F2") + " (lake y " + lakeY.ToString("0.00") + "); ");

                // ---- 3. the ball rolls down the face (momentum: it bends down at most 15 deg per 5 cm step)
                float yTop = pondY - 0.03f, xTop = pb.center.x - 0.05f, H = yTop - lakeY;
                System.Func<float, float> xAt = y => Mathf.Lerp(xTop, land.x, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((yTop - y) / H)));
                System.Func<float, float> rAt = y => Mathf.Lerp(0.12f, 0.3f, Mathf.Clamp01((yTop - y) / 1.5f));
                var path = new List<Vector3>();
                var p = new Vector3(xTop, yTop, pb.center.z - 0.25f);
                float theta = 90f; int stuck = 0;
                path.Add(p);
                float yD = lakeY + 3.3f;
                for (int it = 0; it < 6000 && p.y > yD; it++)
                {
                    const float ds = 0.05f;
                    bool moved = false;
                    for (float th = Mathf.Max(0f, theta - 15f); th <= 135f; th += 5f)
                    {
                        float rad = th * Mathf.Deg2Rad;
                        var q = p + new Vector3(0f, -Mathf.Cos(rad), -Mathf.Sin(rad)) * ds; q.x = xAt(q.y);
                        if (Blocked(q, rAt(q.y))) continue;
                        p = q; theta = th; moved = true; break;
                    }
                    if (!moved) { stuck++; p += new Vector3(0f, 0.02f, -0.03f); theta = 90f; if (stuck > 200) break; continue; }
                    path.Add(p);
                }
                sb.Append("ball path " + path.Count + " steps (stuck " + stuck + "), ends " + p.ToString("F2") + "; ");

                // resample every 0.12 m, smooth with the clearance kept
                var c = Resample(path, 0.12f);
                for (int pass = 0; pass < 8; pass++)
                {
                    var s2 = new List<Vector3>(c);
                    for (int i = 2; i < c.Count - 2; i++)
                    {
                        var avg = (c[i - 2] + c[i - 1] * 2f + c[i] * 2f + c[i + 1] * 2f + c[i + 2]) / 8f;
                        if (!Blocked(avg, rAt(avg.y) * 0.95f)) s2[i] = avg;
                    }
                    c = s2;
                }
                // flare: the lower 3.3 m leave the face in an arc and land in the pond, then go 0.4 m under the surface
                var last = c[c.Count - 1]; float zD = last.z; int flarePushed = 0;
                for (float y = last.y - 0.1f; y > lakeY - 0.42f; y -= 0.1f)
                {
                    float s = Mathf.Clamp01((last.y - y) / Mathf.Max(0.1f, last.y - lakeY));
                    float zf = y >= lakeY ? zD - (zD - land.z) * Mathf.Pow(s, 1.5f) : land.z - (lakeY - y) * 0.2f;
                    var q = new Vector3(xAt(y), y, zf);
                    int k = 0; if (y > lakeY + 0.3f) while (k < 50 && Blocked(q, 0.3f)) { q.z -= 0.03f; k++; }
                    if (k > 0) flarePushed++;
                    c.Add(q);
                }
                c = Resample(c, 0.12f);
                for (int pass = 0; pass < 3; pass++)
                {
                    var s2 = new List<Vector3>(c);
                    for (int i = 2; i < c.Count - 2; i++)
                    {
                        if (c[i].y > yD + 1.5f) continue;
                        var avg = (c[i - 2] + c[i - 1] * 2f + c[i] * 2f + c[i + 1] * 2f + c[i + 2]) / 8f;
                        if (!Blocked(avg, 0.28f)) s2[i] = avg;
                    }
                    c = s2;
                }
                sb.Append("flare from y " + last.y.ToString("0.0") + " (pushed " + flarePushed + "); ");

                // ---- 4. lanes
                int R = c.Count;
                var len = new float[R]; for (int i = 1; i < R; i++) len[i] = len[i - 1] + (c[i] - c[i - 1]).magnitude;
                float L = len[R - 1];
                var P = new Vector3[R, Lanes]; int pushed = 0;
                for (int i = 0; i < R; i++)
                {
                    var d = (c[Mathf.Min(R - 1, i + 1)] - c[Mathf.Max(0, i - 1)]).normalized;
                    var n = new Vector3(0f, -d.z, d.y); if (n.sqrMagnitude < 1e-6f) n = Vector3.back; n.Normalize();
                    if (n.z > 0.2f) n = -n;
                    float t = len[i] / L;
                    float W = 0.85f + 0.4f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.12f)) + 0.6f * Mathf.Pow(t, 1.5f);
                    for (int l = 0; l < Lanes; l++)
                    {
                        float u = l / (float)(Lanes - 1);
                        var q = c[i] + Vector3.right * ((u - 0.5f) * W);
                        float rl = Mathf.Min(0.25f, rAt(q.y));
                        int k = 0; if (q.y > lakeY + 0.25f) while (k < 60 && Blocked(q, rl)) { q += n * 0.03f; k++; }
                        if (k > 0) pushed++;
                        P[i, l] = q;
                    }
                }
                for (int pass = 0; pass < 3; pass++)
                    for (int i = 1; i < R - 1; i++)
                        for (int l = 0; l < Lanes; l++)
                        {
                            var a = P[i, l] * 0.5f + (P[i - 1, l] + P[i + 1, l]) * 0.125f;
                            if (l > 0 && l < Lanes - 1) a += (P[i, l - 1] + P[i, l + 1]) * 0.125f; else a += P[i, l] * 0.25f;
                            if (!Blocked(a, Mathf.Min(0.25f, rAt(a.y)) * 0.95f)) P[i, l] = a;
                        }
                // clearance audit (every vertex and every edge midpoint, 0.2 m)
                int bad = 0, checks = 0;
                for (int i = 0; i < R; i++)
                    for (int l = 0; l < Lanes; l++)
                    {
                        bool underPool = P[i, l].y > pondY - 0.3f && i < 6;           // the start hides under the top pool
                        if (P[i, l].y < lakeY + 0.05f || underPool) continue;
                        float ra = Mathf.Min(0.2f, 0.9f * rAt(P[i, l].y));
                        checks++; if (Blocked(P[i, l], ra)) bad++;
                        if (i + 1 < R) { checks++; if (Blocked((P[i, l] + P[i + 1, l]) * 0.5f, ra)) bad++; }
                    }
                sb.Append(R + " rows x " + Lanes + " lanes, length " + L.ToString("0.0") + " m, lanes pushed " + pushed + ", clearance audit " + bad + "/" + checks + " closer than 0.2 m (0.11 in the gully); ");

                // ---- 5. mesh (local to the waterfall root): uv v grows slower at the top (slow stream) than at the bottom (fast)
                var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var cols = new List<Color>(); var nrm = new List<Vector3>(); var tris = new List<int>();
                float v = 0f; var vs = new float[R];
                for (int i = 0; i < R; i++)
                {
                    if (i > 0) { float drop = Mathf.Clamp01((yTop - c[i].y) / H); v += (len[i] - len[i - 1]) / (1.0f + 3.0f * Mathf.Sqrt(drop)); }
                    vs[i] = v;
                }
                var foamRow = new float[R];
                for (int i = 1; i < R - 1; i++)
                {
                    float ang = Vector3.Angle(c[i] - c[i - 1], c[i + 1] - c[i]);
                    foamRow[i] = Mathf.Clamp01(ang / 7f);
                }
                var foamS = new float[R];
                for (int i = 0; i < R; i++)
                {
                    float f = 0f; for (int k = -3; k <= 3; k++) { int j = Mathf.Clamp(i + k, 0, R - 1); f = Mathf.Max(f, foamRow[j] * (1f - Mathf.Abs(k) / 4f)); }
                    float t = len[i] / L;
                    float lip = t < 0.08f ? 0.35f : 0f;
                    float plunge = Mathf.Clamp01(1f - (c[i].y - lakeY) / 1.8f) * 0.95f;
                    foamS[i] = Mathf.Clamp01(0.08f + f * 0.75f + lip + plunge);
                }
                for (int i = 0; i < R; i++)
                    for (int l = 0; l < Lanes; l++)
                    {
                        verts.Add(root.InverseTransformPoint(P[i, l]));
                        uvs.Add(new Vector2(l / (float)(Lanes - 1), vs[i]));
                        float a = Mathf.Clamp01(i / 3f) * 0.9f;
                        cols.Add(new Color(foamS[i], 1f, 0f, a));
                        var du = P[i, Mathf.Min(Lanes - 1, l + 1)] - P[i, Mathf.Max(0, l - 1)];
                        var dv = P[Mathf.Min(R - 1, i + 1), l] - P[Mathf.Max(0, i - 1), l];
                        var nn = Vector3.Cross(dv, du).normalized; if (nn.z > 0f) nn = -nn;
                        nrm.Add(root.InverseTransformDirection(nn));
                    }
                for (int i = 0; i < R - 1; i++)
                    for (int l = 0; l < Lanes - 1; l++)
                    {
                        int a0 = i * Lanes + l, a1 = a0 + 1, b0 = a0 + Lanes, b1 = b0 + 1;
                        tris.Add(a0); tris.Add(b0); tris.Add(a1); tris.Add(a1); tris.Add(b0); tris.Add(b1);
                    }
                var sheet = SaveMesh(SheetMesh, "WF_D01_Sheet_v2", verts, uvs, cols, nrm, tris);

                // foam ring on the lake at the plunge, clipped to the water
                verts.Clear(); uvs.Clear(); cols.Clear(); nrm.Clear(); tris.Clear();
                float[] ringR = { 0f, 0.2f, 0.45f, 0.7f, 0.95f, 1.2f }; float[] fo = { 1f, 0.95f, 0.75f, 0.5f, 0.3f, 0.1f }; float[] al = { 0.95f, 0.9f, 0.75f, 0.5f, 0.25f, 0f };
                const int Seg = 28;
                var center = new Vector3(land.x, lakeY + 0.025f, land.z);
                for (int ri = 0; ri < ringR.Length; ri++)
                    for (int s = 0; s <= Seg; s++)
                    {
                        float ang = s / (float)Seg * Mathf.PI * 2f;
                        var w = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang) * 0.85f) * ringR[ri];
                        float w3; float b = Bed(w.x, w.z, out w3);
                        float clip = Mathf.Clamp01((lakeY - 0.03f - b) / 0.12f);
                        verts.Add(root.InverseTransformPoint(w)); nrm.Add(Vector3.up);
                        uvs.Add(new Vector2(s / (float)Seg * 3f, ringR[ri] / 1.2f * 0.8f));
                        cols.Add(new Color(fo[ri], 0f, 0f, al[ri] * clip));
                    }
                for (int ri = 0; ri < ringR.Length - 1; ri++)
                    for (int s = 0; s < Seg; s++)
                    {
                        int a0 = ri * (Seg + 1) + s, a1 = a0 + 1, b0 = a0 + Seg + 1, b1 = b0 + 1;
                        tris.Add(a0); tris.Add(a1); tris.Add(b0); tris.Add(a1); tris.Add(b1); tris.Add(b0);
                    }
                var ring = SaveMesh(RingMesh, "WF_D01_Ring_v2", verts, uvs, cols, nrm, tris);

                // ---- 6. scene objects
                var mat = Mat(lakeY);
                Add(root, "WATER_Upper_Sheet_D01", sheet, mat);
                Add(root, "WATER_Stream_Ring_D01", ring, mat);
                if (up && up.GetComponent<Renderer>()) up.GetComponent<Renderer>().enabled = false;
                if (lo && lo.GetComponent<Renderer>()) lo.GetComponent<Renderer>().enabled = false;
                var ft = kid("FX_Foam"); if (ft) ft.position = new Vector3(land.x, lakeY + 0.05f, land.z);
                var sp = kids.FirstOrDefault(t => t && OpenZ.DetailKit.Base(t.name).StartsWith("FX_Spray") && !t.name.Contains("Top")); if (sp) sp.position = new Vector3(land.x, lakeY + 0.1f, land.z);
                var mi = kid("FX_Mist"); if (mi) mi.position = new Vector3(land.x, lakeY + 1.2f, land.z - 0.4f);
                var li = kid("LIGHT_"); if (li) li.position = new Vector3(land.x, lakeY + 1.8f, land.z - 0.7f);
                var so = kid("SND_"); if (so) so.position = new Vector3(land.x, lakeY + 2.6f, land.z + 0.2f);
                var st = kid("FX_Spray_Top");
                if (st)
                {   // the lip: the first steep row after the gully
                    int iL = 1; for (; iL < R - 1; iL++) { var d = c[iL + 1] - c[iL]; if (d.y < -0.8f * d.magnitude) break; }
                    st.position = c[iL] + Vector3.back * 0.1f;
                    sb.Append("lip " + st.position.ToString("F2") + "; ");
                }
                foreach (var r in new[] { up, lo }) if (r) EditorUtility.SetDirty(r.gameObject);
                EditorUtility.SetDirty(root.gameObject);
            }
            finally { if (tmp) Object.DestroyImmediate(tmp); tmp = null; }
            return sb.ToString();
        }

        static List<Vector3> Resample(List<Vector3> pts, float step)
        {
            var o = new List<Vector3> { pts[0] };
            float acc = 0f;
            for (int i = 1; i < pts.Count; i++)
            {
                var a = pts[i - 1]; var b = pts[i]; float seg = (b - a).magnitude; if (seg < 1e-5f) continue;
                float pos = step - acc;
                while (pos <= seg) { o.Add(Vector3.Lerp(a, b, pos / seg)); pos += step; }
                acc = seg - (pos - step);
            }
            if ((o[o.Count - 1] - pts[pts.Count - 1]).magnitude > step * 0.3f) o.Add(pts[pts.Count - 1]);
            return o;
        }

        static Mesh SaveMesh(string path, string name, List<Vector3> v, List<Vector2> uv, List<Color> col, List<Vector3> n, List<int> t)
        {
            var m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool fresh = !m;
            if (fresh) m = new Mesh();
            m.Clear(); m.name = name;
            m.SetVertices(v); m.SetUVs(0, uv); m.SetColors(col); m.SetNormals(n); m.SetTriangles(t, 0);
            m.RecalculateBounds();
            if (fresh) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
            return m;
        }

        static Material Mat(float lakeY)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
            if (!m) { m = new Material(Shader.Find("OpenZ/WaterfallSheet")); AssetDatabase.CreateAsset(m, MatPath); }
            m.shader = Shader.Find("OpenZ/WaterfallSheet");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(WaterTex));
            m.SetFloat("_FadeY", lakeY);
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void Add(Transform root, string name, Mesh mesh, Material mat)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root, false);
            g.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = g.AddComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = true;
            r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.BlendProbes;
        }
    }
}
