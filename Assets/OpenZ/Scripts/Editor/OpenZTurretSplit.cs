using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// "Model"), so UnitView turned the whole cannon, sandbags and base included, and because these imported models face +x
    /// rotating gun, like OpenZFracture.SplitMediumTurret did for the old medium tank: triangles are assigned by centroid in
    /// same rules; out/split_*_sheet.png), and nodes named for UnitAim:
    ///   gatling:        Base | TurretYaw (collar ring centre, 0.74 m) > BarrelSpin (barrel cluster axis, +x) > Muzzle
    ///   howitzer:       Base | TurretYaw (turntable ring centre x -0.546, 0.52 m) > BarrelPitch (trunnion in the cradle) > BarrelRecoil (tube) > Muzzle
    ///                   (howitzer_towers_v1: was x -0.42, 0.126 off the ring: the turret orbited its base when it turned;
    ///                   OpenZ/Howitzer v2/1 Apply then fits the chunky barrel)
    ///   missile_cannon: Base | TurretYaw (column top, 1.6 m) > BarrelPitch (pod axle, 2.24 m) > Muzzle_01..04 (rocket noses)
    /// Every part mesh carries the whole model's bounds, so TeamPaint's accent band (a fraction of mesh.bounds height) stays the
    /// one original band. The original "Model" stays in the prefab, disabled (OpenZ/Unsplit Cannon Turrets restores it). A prefab whose Model
    /// does not match the measured mesh (bounds off by > 5 cm) is left alone and reported. Scene instances update through
    /// </summary>
    public static class OpenZTurretSplit
    {
        const string Prefabs = "Assets/OpenZ/Prefabs", OutDir = "Assets/OpenZ/Models/Generated";

        enum Part { Base, Yaw, Pitch, Recoil, Spin }

        sealed class Rule
        {
            public string type; public Vector3 boundsMin, boundsMax;
            public Vector3 yawPivot; public Func<Vector3, bool> yaw;
            public Vector3 pitchPivot; public Func<Vector3, bool> pitch, recoil;
            public Vector3 spinPivot; public Func<Vector3, bool> spin;
            public (Vector3 pos, Vector3 dir)[] muzzles;
            public (Part part, int min)[] expect;                                                   // sanity: the rules hit the real gun
        }

        // howitzer tube axis (PCA of the tube triangles in prefab-root space): point C, direction D (42.4 deg up, +x)
        static readonly Vector3 HC = new Vector3(1.447f, 3.562f, -0.002f), HD = new Vector3(0.7386f, 0.6741f, -0.0049f).normalized;
        static (float s, float dist) HowAxis(Vector3 c) { var w = c - HC; float s = Vector3.Dot(w, HD); return (s, (w - s * HD).magnitude); }
        static float Sq(float v) => v * v;

        static readonly Rule[] Rules =
        {
            new Rule
            {
                type = "gatling", boundsMin = new Vector3(-2f, 0f, -1.691f), boundsMax = new Vector3(2f, 2.721f, 1.691f),
                yawPivot = new Vector3(-0.35f, 0.74f, 0f),
                yaw = c => (c.y > 0.74f && (Sq(c.x + 0.35f) + Sq(c.z) < Sq(1.05f) || (c.x < -1.2f && Mathf.Abs(c.z) < 1.12f))) || c.y > 1.3f,
                spinPivot = new Vector3(0.6f, 2.003f, 0f), spin = c => c.x > 0.6f && c.y > 1.62f && Mathf.Abs(c.z) < 0.42f,
                muzzles = new[] { (new Vector3(2.02f, 2.003f, 0f), Vector3.right) },
                expect = new[] { (Part.Spin, 1000), (Part.Yaw, 3500) }
            },
            new Rule
            {
                type = "howitzer", boundsMin = new Vector3(-2f, 0f, -1.562f), boundsMax = new Vector3(2f, 4.017f, 1.562f),
                yawPivot = new Vector3(-0.546f, 0.52f, 0f), yaw = c => c.y > 0.52f,   // howitzer_towers_v1: turntable ring centre (trimmed circle fits)
                pitchPivot = new Vector3(-0.18f, 2.08f, 0f), pitch = c => { var a = HowAxis(c); return a.dist < 0.42f && a.s > -2.3f; },
                recoil = c => { var a = HowAxis(c); return a.dist < 0.30f && a.s > -1.85f; },
                muzzles = new[] { (new Vector3(1.88f, 3.96f, 0f), HD) },
                expect = new[] { (Part.Recoil, 500), (Part.Pitch, 250), (Part.Yaw, 4000) }
            },
            new Rule
            {
                type = "missile_cannon", boundsMin = new Vector3(-1.948f, 0f, -2f), boundsMax = new Vector3(1.948f, 4.579f, 2f),
                yawPivot = new Vector3(0f, 1.6f, 0f), yaw = c => c.y > 1.6f,
                pitchPivot = new Vector3(-0.05f, 2.236f, 0f), pitch = c => c.y > 1.98f,
                muzzles = new[]
                {
                    (new Vector3(1.47f, 4.24f, -1.13f), new Vector3(0.990f, 0.139f, 0f)), (new Vector3(1.47f, 4.24f, 1.13f), new Vector3(0.990f, 0.139f, 0f)),
                    (new Vector3(1.58f, 3.62f, -1.14f), new Vector3(0.990f, 0.139f, 0f)), (new Vector3(1.58f, 3.62f, 1.14f), new Vector3(0.990f, 0.139f, 0f))
                },
                expect = new[] { (Part.Pitch, 5000), (Part.Yaw, 200) }
            },
        };

        [MenuItem("OpenZ/Split Cannon Turrets (gatling, howitzer, missile cannon)")]
        public static string SplitAll()
        {
            System.IO.Directory.CreateDirectory(OutDir);
            var log = Rules.Select(Split).ToList();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            var msg = string.Join("\n", log); Debug.Log("[OpenZ] " + msg);
            return msg;
        }

        [MenuItem("OpenZ/Unsplit Cannon Turrets")]
        public static string UnsplitAll()
        {
            var log = new List<string>();
            foreach (var r in Rules)
            {
                string path = $"{Prefabs}/Z_{r.type}.prefab";
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var model = root.transform.Find("Model");
                    var made = new[] { "Base", "TurretYaw" }.Select(n => root.transform.Find(n)).Where(t => t).ToList();
                    if (!model || made.Count == 0) { log.Add($"{r.type}: not split"); continue; }
                    foreach (var t in made) UnityEngine.Object.DestroyImmediate(t.gameObject);
                    model.gameObject.SetActive(true);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    log.Add($"{r.type}: restored the single mesh");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        static string Split(Rule r)
        {
            string path = $"{Prefabs}/Z_{r.type}.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) return $"{r.type}: no prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponentsInChildren<Transform>(true).Any(t => t.name.EndsWith("TurretYaw"))) return $"{r.type}: already has a turret, skipped";
                var model = root.transform.Find("Model");
                var mf = model ? model.GetComponent<MeshFilter>() : null; var mr = model ? model.GetComponent<MeshRenderer>() : null;
                if (!mf || !mr || !mf.sharedMesh) return $"{r.type}: no single-mesh Model, skipped";
                var src = mf.sharedMesh; var M = root.transform.worldToLocalMatrix * model.localToWorldMatrix;
                var pos = src.vertices.Select(v => M.MultiplyPoint3x4(v)).ToArray();
                Vector3 bmin = pos[0], bmax = pos[0]; foreach (var p in pos) { bmin = Vector3.Min(bmin, p); bmax = Vector3.Max(bmax, p); }
                if ((bmin - r.boundsMin).magnitude > 0.05f || (bmax - r.boundsMax).magnitude > 0.05f)
                    return $"{r.type}: Model bounds {bmin:F3}..{bmax:F3} differ from the measured mesh {r.boundsMin:F3}..{r.boundsMax:F3}; the model changed, rules not applied";
                var nrm = src.normals.Select(n => M.MultiplyVector(n).normalized).ToArray();
                var uv = src.uv; var uv2 = src.uv2; var col = src.colors; var tan = src.tangents;

                // classify every triangle of every submesh by its centroid (same order as split_preview.py: yaw, then pitch,
                // recoil, spin inside the yaw part)
                int subs = src.subMeshCount;
                var tris = new Dictionary<Part, List<int>[]>();
                foreach (Part p in Enum.GetValues(typeof(Part))) { tris[p] = new List<int>[subs]; for (int s = 0; s < subs; s++) tris[p][s] = new List<int>(); }
                for (int s = 0; s < subs; s++)
                {
                    var idx = src.GetTriangles(s);
                    for (int i = 0; i < idx.Length; i += 3)
                    {
                        var c = (pos[idx[i]] + pos[idx[i + 1]] + pos[idx[i + 2]]) / 3f;
                        var part = Part.Base;
                        if (r.yaw(c))
                        {
                            part = Part.Yaw;
                            if (r.pitch != null && r.pitch(c)) part = Part.Pitch;
                            if (r.recoil != null && r.recoil(c)) part = Part.Recoil;
                            if (r.spin != null && r.spin(c)) part = Part.Spin;
                        }
                        tris[part][s].Add(idx[i]); tris[part][s].Add(idx[i + 1]); tris[part][s].Add(idx[i + 2]);
                    }
                }

                foreach (var (part, min) in r.expect)
                {   // glTFast mirrors x (Jobs.cs ConvertVector3FloatToFloatInterleavedJob: tmp.x *= -1): the rules expect the
                    // barrel at +x; a model imported another way would leave these parts nearly empty -> do nothing
                    int n = tris[part].Sum(l => l.Count) / 3;
                    if (n < min) return $"{r.type}: only {n} triangles in {part} (expected >= {min}); orientation or model changed, rules not applied";
                }

                Mesh Build(Part p, Vector3 origin)
                {
                    var map = new Dictionary<int, int>();
                    var v = new List<Vector3>(); var n = new List<Vector3>(); var u = new List<Vector2>(); var u2 = new List<Vector2>(); var cl = new List<Color>(); var tg = new List<Vector4>();
                    var outSubs = new List<int>[subs];
                    for (int s = 0; s < subs; s++)
                    {
                        outSubs[s] = new List<int>();
                        foreach (int o in tris[p][s])
                        {
                            if (!map.TryGetValue(o, out int j))
                            {
                                j = v.Count; map[o] = j; v.Add(pos[o] - origin); n.Add(o < nrm.Length ? nrm[o] : Vector3.up);
                                if (uv.Length > o) u.Add(uv[o]); if (uv2.Length > o) u2.Add(uv2[o]); if (col.Length > o) cl.Add(col[o]);
                                if (tan.Length > o) { var t3 = M.MultiplyVector(new Vector3(tan[o].x, tan[o].y, tan[o].z)).normalized; tg.Add(new Vector4(t3.x, t3.y, t3.z, tan[o].w)); }
                            }
                            outSubs[s].Add(j);
                        }
                    }
                    if (v.Count == 0) return null;
                    var m = new Mesh { name = $"{r.type}_{p.ToString().ToLower()}" };
                    if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                    m.SetVertices(v); m.SetNormals(n);
                    if (u.Count == v.Count) m.SetUVs(0, u); if (u2.Count == v.Count) m.SetUVs(1, u2); if (cl.Count == v.Count) m.SetColors(cl);
                    m.subMeshCount = subs;
                    for (int s = 0; s < subs; s++) m.SetTriangles(outSubs[s], s);
                    if (tg.Count == v.Count) m.SetTangents(tg); else m.RecalculateTangents();
                    // TeamPaint puts the team accent band at _AccentFrom.._AccentTo (m09-m11 materials: 0.5-0.62) of each renderer's
                    // own mesh.bounds height (TeamPaint.cs Apply, TeamLit objY). Every part gets the WHOLE model's bounds (in its
                    // own space), so the base, turret and barrels share the one original band instead of three bands of their own.
                    m.bounds = new Bounds((bmin + bmax) * 0.5f - origin, bmax - bmin);
                    return m;
                }

                Vector3 pitchO = r.pitch != null ? r.pitchPivot : r.yawPivot, spinO = r.spin != null ? r.spinPivot : pitchO;
                var meshes = new (Part part, Mesh mesh)[]
                {
                    (Part.Base, Build(Part.Base, Vector3.zero)), (Part.Yaw, Build(Part.Yaw, r.yawPivot)), (Part.Pitch, Build(Part.Pitch, pitchO)),
                    (Part.Recoil, Build(Part.Recoil, pitchO)), (Part.Spin, Build(Part.Spin, spinO))
                };
                string meshPath = $"{OutDir}/{r.type}_turret_split.asset";
                AssetDatabase.DeleteAsset(meshPath);
                bool first = true;
                foreach (var (_, m) in meshes)
                {
                    if (!m) continue;
                    if (first) { AssetDatabase.CreateAsset(m, meshPath); first = false; } else AssetDatabase.AddObjectToAsset(m, meshPath);
                }

                GameObject Node(string name, Transform parent, Vector3 pivotRoot, Mesh mesh)
                {
                    var go = new GameObject(name);
                    go.transform.SetParent(parent, false);
                    go.transform.position = root.transform.TransformPoint(pivotRoot);
                    if (mesh)
                    {
                        go.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var rr = go.AddComponent<MeshRenderer>();
                        rr.sharedMaterials = mr.sharedMaterials; rr.shadowCastingMode = mr.shadowCastingMode; rr.receiveShadows = mr.receiveShadows;
                    }
                    return go;
                }
                Mesh Get(Part p) => meshes.First(x => x.part == p).mesh;
                model.gameObject.SetActive(false);                                                     // kept, disabled (Unsplit restores it)
                Node("Base", root.transform, Vector3.zero, Get(Part.Base));
                var yawN = Node("TurretYaw", root.transform, r.yawPivot, Get(Part.Yaw)).transform;
                Transform gun = yawN;
                if (r.pitch != null) gun = Node("BarrelPitch", yawN, r.pitchPivot, Get(Part.Pitch)).transform;
                if (r.recoil != null) gun = Node("BarrelRecoil", gun, r.pitchPivot, Get(Part.Recoil)).transform;
                if (r.spin != null) gun = Node("BarrelSpin", gun, r.spinPivot, Get(Part.Spin)).transform;
                for (int i = 0; i < r.muzzles.Length; i++)
                {
                    var (mp, md) = r.muzzles[i];
                    var mz = new GameObject(r.muzzles.Length == 1 ? "Muzzle" : $"Muzzle_{i + 1:D2}");
                    mz.transform.SetParent(gun, false);
                    mz.transform.position = root.transform.TransformPoint(mp);
                    mz.transform.rotation = Quaternion.LookRotation(-root.transform.TransformDirection(md.normalized), root.transform.up);   // fires along local -z
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                int Count(Part p) => tris[p].Sum(l => l.Count) / 3;
                return $"{r.type}: base {Count(Part.Base)} / turret {Count(Part.Yaw)} / pitch {Count(Part.Pitch)} / recoil {Count(Part.Recoil)} / spin {Count(Part.Spin)} tris, {r.muzzles.Length} muzzle(s)";
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
