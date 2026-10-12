using System.Collections.Generic;
using System.Linq;
using OpenZ.Sim;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// left solid ground under every span. For each bridge the river band is measured from the Zod water tiles on both
    /// sides (same width as the river around it) and carved under the span: render terrain and collider (copied mesh
    /// assets, so a re-import can't undo it), smooth banks, and a water quad at the river's surface height.
    /// </summary>
    public static class OpenZRiverCarve
    {
        const string Gen = "Assets/OpenZ/Models/Generated";

        [MenuItem("OpenZ/Carve river under bridges (open level)")]
        public static string Carve()
        {
            var scene = EditorSceneManager.GetActiveScene(); string lv = scene.name.StartsWith("L17") ? "L17" : "L02";
            var w = ZWorld.Create(Resources.Load<TextAsset>("OpenZ/default_settings").text, Resources.Load<TextAsset>($"OpenZ/Maps/{lv}_grid").text, 1);
            var rects = new List<Rect>(); var log = new List<string>(); waterVerts = null;
            foreach (var o in w.Objects.Where(x => x.IsBridge))
            {
                int tx0 = o.LocX / 16, ty0 = o.LocY / 16, tx1 = (o.LocX + o.WidthPix) / 16, ty1 = (o.LocY + o.HeightPix) / 16;
                bool vert = o.Type == "bridge_vert";                                                   // road N-S, river E-W
                bool Water(int x, int y) { var c = w.Map.ClassAt(x, y); return c == ZClass.Water || c == ZClass.DeepWater; }
                int a0 = int.MaxValue, a1 = int.MinValue;
                if (vert) { for (int y = ty0; y < ty1; y++) if (Water(tx0 - 1, y) || Water(tx1, y)) { a0 = Mathf.Min(a0, y); a1 = Mathf.Max(a1, y); } }
                else { for (int x = tx0; x < tx1; x++) if (Water(x, ty0 - 1) || Water(x, ty1)) { a0 = Mathf.Min(a0, x); a1 = Mathf.Max(a1, x); } }
                if (a0 > a1) { log.Add($"{o.Type} ({tx0},{ty0}): no water beside it"); continue; }
                // world rect (x right, z = -y): the whole bridge width across, the river band along
                Rect r = vert ? Rect.MinMaxRect(tx0 * 2f, -(a1 + 1) * 2f, tx1 * 2f, -a0 * 2f) : Rect.MinMaxRect(a0 * 2f, -ty1 * 2f, (a1 + 1) * 2f, -ty0 * 2f);
                // widen the band to the real 3D river beside the bridge (the level build made the water wider than Zod's
                var wv = WaterVerts(); var cxz = new Vector2((tx0 + tx1), -(ty0 + ty1));                       // centre in metres
                float lo = float.MaxValue, hi = float.MinValue;
                foreach (var q in wv)
                {
                    if (vert) { bool beside = (q.x > tx0 * 2f - 3.5f && q.x < tx0 * 2f - 0.3f) || (q.x > tx1 * 2f + 0.3f && q.x < tx1 * 2f + 3.5f); if (beside && Mathf.Abs(q.z - cxz.y) < 7f) { lo = Mathf.Min(lo, q.z); hi = Mathf.Max(hi, q.z); } }
                    else { bool beside = (q.z < -ty0 * 2f + 3.5f && q.z > -ty0 * 2f + 0.3f) || (q.z > -ty1 * 2f - 3.5f && q.z < -ty1 * 2f - 0.3f); if (beside && Mathf.Abs(q.x - cxz.x) < 7f) { lo = Mathf.Min(lo, q.x); hi = Mathf.Max(hi, q.x); } }
                }
                float half = (vert ? (ty1 - ty0) : (tx1 - tx0)) * 2f * 0.5f * 0.8f;                             // flat deck, no ramps
                float mid = vert ? cxz.y : cxz.x;
                if (lo < hi) { lo = Mathf.Max(lo - 0.3f, mid - half); hi = Mathf.Min(hi + 0.3f, mid + half); }
                else { lo = mid - half * 0.6f; hi = mid + half * 0.6f; }
                if (vert) r = Rect.MinMaxRect(r.xMin, Mathf.Min(r.yMin, lo), r.xMax, Mathf.Max(r.yMax, hi));
                else r = Rect.MinMaxRect(Mathf.Min(r.xMin, lo), r.yMin, Mathf.Max(r.xMax, hi), r.yMax);
                rects.Add(r); log.Add($"{o.Type} ({tx0},{ty0}): river band {(vert ? "rows" : "cols")} {a0}-{a1} -> {r.width:F0} x {r.height:F0} m");
            }
            if (rects.Count == 0) return string.Join("\n", log);
            var env = GameObject.Find("Environment");
            var water = env.transform.Find("WAT_Water"); float waterY = water ? water.GetComponent<Renderer>().bounds.min.y : -0.35f;
            float bedY = waterY - 0.75f;
            int changed = 0;
            foreach (var name in new[] { "TER_Ground", "COL_Terrain", "TER_RiverBed" })   // TER_RiverBed too: its banks stood above the water under the spans (the "thin stick")
            {
                var t = env.transform.Find(name); if (!t) continue;
                var mf = t.GetComponent<MeshFilter>(); var mc = t.GetComponent<MeshCollider>();
                var src = mf ? mf.sharedMesh : mc.sharedMesh;
                var mesh = Object.Instantiate(src); mesh.name = src.name + "_carved";
                var v = mesh.vertices; var M = t.localToWorldMatrix; var inv = t.worldToLocalMatrix;
                for (int i = 0; i < v.Length; i++)
                {
                    var p = M.MultiplyPoint3x4(v[i]);
                    foreach (var r in rects)
                    {
                        if (!r.Contains(new Vector2(p.x, p.z)) || p.y < bedY + 0.05f || p.y > waterY + 3f) continue;   // never touch cliffs
                        float d = Mathf.Min(Mathf.Min(p.x - r.xMin, r.xMax - p.x), Mathf.Min(p.z - r.yMin, r.yMax - p.z));
                        float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(d / 1.4f));                    // 1.4 m natural bank
                        p.y = Mathf.Lerp(p.y, bedY, k); v[i] = inv.MultiplyPoint3x4(p); changed++;
                    }
                }
                mesh.vertices = v; mesh.RecalculateNormals(); mesh.RecalculateBounds();
                System.IO.Directory.CreateDirectory(Gen);
                string path = $"{Gen}/{lv}_{name}_carved.asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(mesh, path);
                if (mf) mf.sharedMesh = mesh; if (mc) mc.sharedMesh = mesh;
                EditorUtility.SetDirty(t.gameObject);
            }
            // water under every span, at the river's surface height (subdivided for the depth-based colour and foam)
            var wmat = water ? water.GetComponent<Renderer>().sharedMaterial : null;
            foreach (Transform c in env.transform.Cast<Transform>().ToList()) if (c.name.StartsWith("WAT_BridgeSpan")) Object.DestroyImmediate(c.gameObject);
            int k2 = 0;
            foreach (var r in rects)
            {
                var go = new GameObject($"WAT_BridgeSpan_{k2++}"); go.transform.SetParent(env.transform, false);
                var mesh = Grid(new Rect(r.xMin - 0.4f, r.yMin - 0.4f, r.width + 0.8f, r.height + 0.8f), waterY, 1f);
                AssetDatabase.CreateAsset(mesh, $"{Gen}/{lv}_WAT_BridgeSpan_{k2}.asset");
                go.AddComponent<MeshFilter>().sharedMesh = mesh; var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterial = wmat; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes(); AssetDatabase.SaveAssets();
            return $"{lv}: carved {rects.Count} bridge spans ({changed} vertices), water at {waterY:F2}, bed {bedY:F2}\n" + string.Join("\n", log);
        }

        static List<Vector3> waterVerts;
        static List<Vector3> WaterVerts()
        {
            if (waterVerts != null) return waterVerts;
            waterVerts = new List<Vector3>();
            var t = GameObject.Find("Environment")?.transform.Find("WAT_Water"); if (!t) return waterVerts;
            var m = t.GetComponent<MeshFilter>().sharedMesh; var M = t.localToWorldMatrix;
            foreach (var v in m.vertices) waterVerts.Add(M.MultiplyPoint3x4(v));
            return waterVerts;
        }

        static Mesh Grid(Rect r, float y, float step)
        {
            int nx = Mathf.Max(1, Mathf.CeilToInt(r.width / step)), nz = Mathf.Max(1, Mathf.CeilToInt(r.height / step));
            var v = new List<Vector3>(); var uv = new List<Vector2>(); var t = new List<int>();
            for (int j = 0; j <= nz; j++) for (int i = 0; i <= nx; i++) { v.Add(new Vector3(r.xMin + r.width * i / nx, y, r.yMin + r.height * j / nz)); uv.Add(new Vector2(i / (float)nx, j / (float)nz)); }
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++) { int a = j * (nx + 1) + i; t.AddRange(new[] { a, a + nx + 1, a + 1, a + 1, a + nx + 1, a + nx + 2 }); }
            var m = new Mesh { name = "WAT_BridgeSpan" }; m.SetVertices(v); m.SetUVs(0, uv); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
    }
}
