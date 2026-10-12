using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// fracture cells per entity in Blender; our imported buildings have none, so each model's triangles are clustered into
    /// Voronoi cells around area-weighted surface seeds (panels and chunks of the real shell), every cell gets back faces so a
    /// tumbling panel never turns invisible, and the pieces are saved as Resources/OpenZ/Fractures/F_&lt;building&gt;.prefab.
    /// Also splits the imported medium tank's turret into its own TurretYaw node (turret tracking + classic Z turret toss).
    /// </summary>
    public static class OpenZFracture
    {
        const string Prefabs = "Assets/OpenZ/Prefabs", OutDir = "Assets/OpenZ/Resources/OpenZ/Fractures";
        static readonly (string name, int cells)[] Buildings = { ("fort", 64), ("robot_factory", 34), ("vehicle_factory", 34), ("repair", 32), ("radar", 24) };

        [MenuItem("OpenZ/Bake Building Fractures")]
        public static string BakeAll()
        {
            Directory.CreateDirectory(OutDir);
            var log = new List<string>();
            foreach (var (name, cells) in Buildings) log.Add(Bake(name, cells));
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            return string.Join("\n", log);
        }

        struct Tri { public int a, b, c; public Vector3 centroid; public float area; }

        static string Bake(string name, int cellCount)
        {
            var pf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Z_{name}.prefab");
            if (!pf) return name + ": no prefab";
            var mf = pf.GetComponentInChildren<MeshFilter>(); var mr = mf.GetComponent<MeshRenderer>();
            var src = mf.sharedMesh; var M = pf.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var pos = src.vertices.Select(v => M.MultiplyPoint3x4(v)).ToArray();
            var nrm = src.normals.Select(n => M.MultiplyVector(n).normalized).ToArray();
            var uv = src.uv; var col = src.colors;
            var idx = src.triangles;
            var tris = new List<Tri>();
            for (int i = 0; i < idx.Length; i += 3)
            {
                Vector3 a = pos[idx[i]], b = pos[idx[i + 1]], c = pos[idx[i + 2]];
                tris.Add(new Tri { a = idx[i], b = idx[i + 1], c = idx[i + 2], centroid = (a + b + c) / 3f, area = Vector3.Cross(b - a, c - a).magnitude * 0.5f });
            }
            // area-weighted surface seeds with a minimum spacing (Poisson-ish), deterministic
            var rng = new System.Random(name.GetHashCode());
            float total = tris.Sum(t => t.area), minD = Mathf.Sqrt(total / cellCount) * 0.55f;
            var cdf = new float[tris.Count]; float acc = 0; for (int i = 0; i < tris.Count; i++) { acc += tris[i].area; cdf[i] = acc; }
            var seeds = new List<Vector3>();
            for (int tries = 0; seeds.Count < cellCount && tries < cellCount * 60; tries++)
            {
                float r = (float)rng.NextDouble() * total; int k = System.Array.BinarySearch(cdf, r); if (k < 0) k = ~k; k = Mathf.Min(k, tris.Count - 1);
                var p = tris[k].centroid;
                if (seeds.All(s => (s - p).sqrMagnitude >= minD * minD)) seeds.Add(p);
            }
            var cellOf = new int[tris.Count];
            for (int i = 0; i < tris.Count; i++)
            {
                int best = 0; float bd = float.MaxValue;
                for (int s = 0; s < seeds.Count; s++) { float d = (seeds[s] - tris[i].centroid).sqrMagnitude; if (d < bd) { bd = d; best = s; } }
                cellOf[i] = best;
            }
            // merge crumbs (< 24 triangles) into the nearest other seed
            var counts = new int[seeds.Count]; foreach (var c in cellOf) counts[c]++;
            for (int i = 0; i < tris.Count; i++)
                if (counts[cellOf[i]] < 24)
                {
                    int best = cellOf[i]; float bd = float.MaxValue;
                    for (int s = 0; s < seeds.Count; s++) if (counts[s] >= 24) { float d = (seeds[s] - tris[i].centroid).sqrMagnitude; if (d < bd) { bd = d; best = s; } }
                    cellOf[i] = best;
                }

            string meshPath = $"{OutDir}/F_{name}_meshes.asset";
            AssetDatabase.DeleteAsset(meshPath);
            var root = new GameObject("F_" + name);
            root.AddComponent<TeamPaint>();
            Mesh container = null; int pieces = 0, triOut = 0;
            for (int s = 0; s < seeds.Count; s++)
            {
                var mine = Enumerable.Range(0, tris.Count).Where(i => cellOf[i] == s).ToList();
                if (mine.Count == 0) continue;
                // flat-shaded icosphere sized to the cell), coloured by the building's own texture at the cell's largest face
                var cb = new Bounds(tris[mine[0]].centroid, Vector3.zero); int big = mine[0];
                foreach (var i in mine) { cb.Encapsulate(tris[i].centroid); if (tris[i].area > tris[big].area) big = i; }
                Vector2 cuv = uv.Length > 0 ? (uv[tris[big].a] + uv[tris[big].b] + uv[tris[big].c]) / 3f : Vector2.zero;
                Color ccol = col.Length > 0 ? (col[tris[big].a] + col[tris[big].b] + col[tris[big].c]) / 3f : Color.white;
                var half = Vector3.Max(new Vector3(0.28f, 0.24f, 0.28f), Vector3.Min(new Vector3(1.05f, 0.85f, 1.05f), cb.extents * 0.7f + Vector3.one * 0.18f));
                var m = Chunk($"F_{name}_{pieces:D2}", half, cuv, ccol, col.Length > 0, rng);
                var center = cb.center;                if (container == null) { AssetDatabase.CreateAsset(m, meshPath); container = m; } else AssetDatabase.AddObjectToAsset(m, meshPath);
                var go = new GameObject($"piece_{pieces:D2}");
                go.transform.SetParent(root.transform, false); go.transform.localPosition = center;
                go.AddComponent<MeshFilter>().sharedMesh = m;
                var r = go.AddComponent<MeshRenderer>(); r.sharedMaterials = mr.sharedMaterials;
                pieces++; triOut += mine.Count;
            }
            PrefabUtility.SaveAsPrefabAsset(root, $"{OutDir}/F_{name}.prefab");
            Object.DestroyImmediate(root);
            return $"{name}: {pieces} chunky pieces from {tris.Count} triangles";
        }

        /// <summary>A rounded low-poly chunk: icosphere (1 subdivision) with deterministic radial jitter, flat faces (unshared
        /// vertices, face normals), one UV / vertex colour so the whole chunk takes the building's colour at that spot.</summary>
        static Mesh Chunk(string name, Vector3 half, Vector2 uvAt, Color colAt, bool hasCol, System.Random rng)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var bv = new List<Vector3> { new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0), new Vector3(0, -1, t), new Vector3(0, 1, t),
                                         new Vector3(0, -1, -t), new Vector3(0, 1, -t), new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1) };
            var bf = new List<int> { 0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8, 3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1 };
            for (int i = 0; i < bv.Count; i++) bv[i] = bv[i].normalized;
            var mid = new Dictionary<long, int>(); var faces = new List<int>();
            int Mid(int a, int b) { long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a; if (mid.TryGetValue(k, out int r)) return r; bv.Add(((bv[a] + bv[b]) * 0.5f).normalized); mid[k] = bv.Count - 1; return bv.Count - 1; }
            for (int i = 0; i < bf.Count; i += 3)
            {
                int a = bf[i], b = bf[i + 1], c = bf[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                faces.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            for (int i = 0; i < bv.Count; i++) bv[i] = Vector3.Scale(bv[i] * (0.82f + (float)rng.NextDouble() * 0.3f), half);   // lumpy, not a ball
            var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var cl = new List<Color>(); var tri = new List<int>();
            for (int i = 0; i < faces.Count; i += 3)
            {
                Vector3 a = bv[faces[i]], b = bv[faces[i + 2]], c = bv[faces[i + 1]];                      // outward winding for Unity
                var fn = Vector3.Cross(b - a, c - a).normalized; if (Vector3.Dot(fn, a + b + c) < 0) { var x = b; b = c; c = x; fn = -fn; }
                foreach (var p in new[] { a, b, c }) { tri.Add(v.Count); v.Add(p); n.Add(fn); u.Add(uvAt); cl.Add(colAt); }
            }
            var m = new Mesh { name = name };
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, u); if (hasCol) m.SetColors(cl);
            m.SetTriangles(tri, 0); m.RecalculateBounds(); m.RecalculateTangents();
            return m;
        }

        /// <summary>
        /// the model's world orientation and scale, then attached to the hand), rifle removed. Saved over Z_tough (same GUID,
        /// so scenes and ZGame's prefab list keep pointing at it). The old mech stays as Z_tough_mech.</summary>
        [MenuItem("OpenZ/Build Tough (robot + bazooka)")]
        public static string BuildToughBazooka()
        {
            string toughPath = $"{Prefabs}/Z_tough.prefab", mechPath = $"{Prefabs}/Z_tough_mech.prefab";
            var old = AssetDatabase.LoadAssetAtPath<GameObject>(toughPath);
            if (old && old.GetComponentsInChildren<Transform>(true).All(t => t.name != "Bazooka") && !AssetDatabase.LoadAssetAtPath<GameObject>(mechPath))
                AssetDatabase.CopyAsset(toughPath, mechPath);
            var robotPf = AssetDatabase.LoadAssetAtPath<GameObject>($"{Prefabs}/Z_robot.prefab");
            var baz = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/OpenZ/Models/Legacy/bazooka.glb");
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/OpenZ/Animation/Robot/bazooka_idle.anim");
            if (!robotPf || !baz || !clip) return "tough: missing robot prefab, bazooka.glb or bazooka_idle";
            var root = (GameObject)PrefabUtility.InstantiatePrefab(robotPf);
            PrefabUtility.UnpackPrefabInstance(root, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            root.name = "Z_tough";
            try
            {
                var anim = root.GetComponentInChildren<Animator>();
                clip.SampleAnimation(anim.gameObject, 0f);                                                      // attach in the bazooka_idle pose
                var hand = root.GetComponentsInChildren<Transform>(true).First(t => t.name == "mixamorigRightHand");
                foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name == "Rifle_Heavy").ToList()) Object.DestroyImmediate(t.gameObject);
                var g = (GameObject)PrefabUtility.InstantiatePrefab(baz);
                PrefabUtility.UnpackPrefabInstance(g, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                g.name = "Bazooka";
                g.transform.SetPositionAndRotation(hand.position, anim.transform.rotation);
                g.transform.localScale = anim.transform.lossyScale;
                g.transform.SetParent(hand, true);
                foreach (var r in g.GetComponentsInChildren<Renderer>(true))
                {
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    var mf2 = r.GetComponent<MeshFilter>(); bool vcol = mf2 && mf2.sharedMesh && mf2.sharedMesh.colors.Length > 0;
                    r.sharedMaterials = r.sharedMaterials.Select(m => m ? OpenZSetup.UnitMaterial("bazooka", m, vcol, false) : m).ToArray();
                }
                var mz = g.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.Contains("Muzzle"));
                PrefabUtility.SaveAsPrefabAsset(root, toughPath);
                return $"tough: robot + bazooka ({g.GetComponentsInChildren<Renderer>(true).Length} renderers, muzzle {(mz ? mz.name : "none")}), mech kept as Z_tough_mech";
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>The imported medium tank is one mesh: triangles above the hull deck (y > 1.55 m) become a TurretYaw child
        /// (pivot at the turret ring), with a Muzzle at the barrel tip, so the turret can track targets and fly off on death.</summary>
        [MenuItem("OpenZ/Split Medium Tank Turret")]
        public static string SplitMediumTurret()
        {
            string path = $"{Prefabs}/Z_medium.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponentsInChildren<Transform>(true).Any(t => t.name == "TurretYaw")) return "medium: already split";
                var model = root.transform.Find("Model"); var mf = model.GetComponent<MeshFilter>(); var mr = model.GetComponent<MeshRenderer>();
                var src = mf.sharedMesh; var M = root.transform.worldToLocalMatrix * model.localToWorldMatrix;
                var pos = src.vertices.Select(v => M.MultiplyPoint3x4(v)).ToArray();
                var nrm = src.normals.Select(n => M.MultiplyVector(n).normalized).ToArray(); var uv = src.uv; var col = src.colors;
                var idx = src.triangles; const float deck = 1.55f; var pivot = new Vector3(0, deck, 0.54f);
                Mesh Build(System.Func<Vector3, bool> keep, Vector3 origin, string n)
                {
                    var map = new Dictionary<int, int>(); var v = new List<Vector3>(); var nn = new List<Vector3>(); var u = new List<Vector2>(); var cl = new List<Color>(); var t = new List<int>();
                    int Get(int o) { if (map.TryGetValue(o, out int j)) return j; j = v.Count; map[o] = j; v.Add(pos[o] - origin); nn.Add(nrm[o]); if (uv.Length > o) u.Add(uv[o]); if (col.Length > o) cl.Add(col[o]); return j; }
                    for (int i = 0; i < idx.Length; i += 3)
                    {
                        var c = (pos[idx[i]] + pos[idx[i + 1]] + pos[idx[i + 2]]) / 3f;
                        if (!keep(c)) continue;
                        t.Add(Get(idx[i])); t.Add(Get(idx[i + 1])); t.Add(Get(idx[i + 2]));
                    }
                    var m = new Mesh { name = n }; if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    m.SetVertices(v); m.SetNormals(nn); if (u.Count == v.Count) m.SetUVs(0, u); if (cl.Count == v.Count) m.SetColors(cl);
                    m.SetTriangles(t, 0); m.RecalculateBounds(); m.RecalculateTangents(); return m;
                }
                bool IsTurret(Vector3 c) => c.y > deck + 0.03f;
                var hull = Build(c => !IsTurret(c), Vector3.zero, "medium_hull");
                var tur = Build(IsTurret, pivot, "medium_turret");
                string meshPath = "Assets/OpenZ/Models/medium_split.asset";
                AssetDatabase.DeleteAsset(meshPath); AssetDatabase.CreateAsset(hull, meshPath); AssetDatabase.AddObjectToAsset(tur, meshPath);
                model.gameObject.SetActive(false);                                                          // keep the original, hidden
                var h = new GameObject("Hull"); h.transform.SetParent(root.transform, false);
                h.AddComponent<MeshFilter>().sharedMesh = hull; h.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                var ty = new GameObject("TurretYaw"); ty.transform.SetParent(root.transform, false); ty.transform.localPosition = pivot;
                ty.AddComponent<MeshFilter>().sharedMesh = tur; ty.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
                var mz = new GameObject("Muzzle"); mz.transform.SetParent(ty.transform, false); mz.transform.localPosition = new Vector3(0, 2.1f, -2.42f) - pivot;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"medium: hull {hull.triangles.Length / 3} tris, turret {tur.triangles.Length / 3} tris";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
