using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// there; here once in the editor, so the break works in players where imported meshes are not readable). Every big mesh of a
    /// bins: x halves x z halves x 3 height layers (&lt;= 12 fragments, each keeping its triangles, UVs, colours and sub-meshes), saved
    /// under Assets/OpenZ/Models/Generated/WreckFragments, and parented as an INACTIVE child "OZ_WreckFragments" of that renderer
    /// (identity transform: the fragments sit exactly on the intact mesh). WreckPieces.Break turns them on and hides the source when
    /// the wreck breaks. Idempotent (a rebake replaces the old set).
    /// </summary>
    public static class OpenZWreckFragments
    {
        const string OutDir = "Assets/OpenZ/Models/Generated/WreckFragments";
        static readonly string[] Types = { "apc", "crane", "jeep", "light", "medium", "heavy", "missile_launcher", "gatling", "gun", "howitzer", "missile_cannon" };
        const int MinTris = 300, MinBinTris = 12;
        /// <summary>
        /// keeps every bin (no holes): before this the GUN had 0 fragments and a killed gun stood there whole.</summary>
        static int MinTrisFor(string type) => type == "gun" ? 100 : MinTris;
        static int MinBinFor(string type) => type == "gun" ? 1 : MinBinTris;

        [MenuItem("OpenZ/Deaths/Bake wreck fragments (gun only, DP1)")]
        public static void BakeGun()
        {
            if (!AssetDatabase.IsValidFolder(OutDir)) { Directory.CreateDirectory(OutDir); AssetDatabase.Refresh(); }
            Debug.Log("[OpenZ] wreck fragments gun: " + Bake("Assets/OpenZ/Prefabs/Z_gun.prefab", "gun"));
            AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ] wreck fragments gun, clipped: " + OpenZWreckClipBake.ClipPrefab("Assets/OpenZ/Prefabs/Z_gun.prefab", "gun"));   // soak_hitch_fix
            AssetDatabase.SaveAssets();
        }

        [MenuItem("OpenZ/Deaths/Bake wreck fragments (vehicles + cannons)")]
        public static void BakeAll()
        {
            if (!AssetDatabase.IsValidFolder(OutDir)) { Directory.CreateDirectory(OutDir); AssetDatabase.Refresh(); }
            var log = new List<string>();
            foreach (var t in Types)
            {
                string path = $"Assets/OpenZ/Prefabs/Z_{t}.prefab";
                if (!File.Exists(path)) { log.Add(t + ": no prefab"); continue; }
                log.Add(t + ": " + Bake(path, t));
            }
            AssetDatabase.SaveAssets();
            foreach (var t in Types)                                                                             // soak_hitch_fix: the runtime cut, baked
            {
                string path = $"Assets/OpenZ/Prefabs/Z_{t}.prefab";
                if (File.Exists(path)) log.Add(t + " clipped: " + OpenZWreckClipBake.ClipPrefab(path, t));
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[OpenZ] wreck fragments:\n" + string.Join("\n", log));
        }

        static bool UnderTurret(Transform t, Transform root)
        {
            for (var p = t; p && p != root; p = p.parent)
                if (p.name.Contains("TurretYaw") || p.name == "MechWaistYaw") return true;                  // flies on its own (Zod turret toss)
            return false;
        }

        static string Bake(string path, string type)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            int made = 0, meshes = 0;
            try
            {
                foreach (var old in root.GetComponentsInChildren<Transform>(true).Where(x => x.name == "OZ_WreckFragments").ToList()) Object.DestroyImmediate(old.gameObject);
                foreach (var f in Directory.GetFiles(OutDir, $"{type}_*.asset")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
                var cands = root.GetComponentsInChildren<MeshRenderer>(true)
                    .Where(r => r.gameObject.activeInHierarchy && r.enabled && r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh)
                    .Where(r => !UnderTurret(r.transform, root.transform))
                    .Select(r => (r, m: r.GetComponent<MeshFilter>().sharedMesh))
                    .Where(x => Tris(x.m) >= MinTrisFor(type))
                    .OrderByDescending(x => Tris(x.m)).Take(3).ToList();
                foreach (var (r, m) in cands)
                {
                    var frags = Split(m, MinBinFor(type));
                    if (frags.Count < 2) continue;
                    meshes++;
                    var holder = new GameObject("OZ_WreckFragments");
                    holder.transform.SetParent(r.transform, false);
                    int i = 0;
                    foreach (var fm in frags)
                    {
                        fm.name = $"{type}_{r.name}_{i}";
                        string ap = $"{OutDir}/{type}_{Safe(r.name)}_{i}.asset";
                        AssetDatabase.CreateAsset(fm, ap);
                        var go = new GameObject("frag_" + i); go.transform.SetParent(holder.transform, false);
                        go.AddComponent<MeshFilter>().sharedMesh = fm;
                        var mr = go.AddComponent<MeshRenderer>(); mr.sharedMaterials = r.sharedMaterials.Take(Mathf.Max(1, fm.subMeshCount)).ToArray();
                        mr.shadowCastingMode = r.shadowCastingMode; mr.receiveShadows = r.receiveShadows;
                        i++; made++;
                    }
                    holder.SetActive(false);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
                return $"{meshes} mesh(es) cut into {made} fragments";
            }
            catch (System.Exception e) { return "FAILED: " + e.Message + " (enable Read/Write on the model if the mesh is not readable)"; }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static string Safe(string s) { foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_'); return s.Replace(' ', '_'); }
        static int Tris(Mesh m) { int n = 0; for (int s = 0; s < m.subMeshCount; s++) n += (int)m.GetIndexCount(s) / 3; return n; }

        /// <summary>key = (x &gt; .5) + 2 (z &gt; .5) + 4 min(2, floor(3 y)) of the triangle centre in the mesh bounds.</summary>
        static List<Mesh> Split(Mesh src, int minBin)
        {
            var v = src.vertices; var n = src.normals; var uv = src.uv; var uv2 = src.uv2; var col = src.colors; var tan = src.tangents;
            var b = src.bounds; var size = Vector3.Max(b.size, Vector3.one * 1e-4f);
            var bins = new Dictionary<int, List<int>[]>();
            for (int s = 0; s < src.subMeshCount; s++)
            {
                var idx = src.GetTriangles(s);
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    var c = (v[idx[i]] + v[idx[i + 1]] + v[idx[i + 2]]) / 3f - b.min;
                    c = new Vector3(c.x / size.x, c.y / size.y, c.z / size.z);
                    int key = (c.x > 0.5f ? 1 : 0) + (c.z > 0.5f ? 2 : 0) + Mathf.Min(2, Mathf.FloorToInt(c.y * 3f)) * 4;
                    if (!bins.TryGetValue(key, out var per)) { per = new List<int>[src.subMeshCount]; bins[key] = per; }
                    (per[s] ??= new List<int>()).AddRange(new[] { idx[i], idx[i + 1], idx[i + 2] });
                }
            }
            var result = new List<Mesh>();
            foreach (var kv in bins.OrderBy(k => k.Key))
            {
                int total = kv.Value.Where(l => l != null).Sum(l => l.Count) / 3; if (total < minBin) continue;
                var remap = new Dictionary<int, int>(); var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nuv = new List<Vector2>(); var nuv2 = new List<Vector2>(); var nc = new List<Color>(); var nt = new List<Vector4>();
                var subs = new List<List<int>>();
                foreach (var list in kv.Value)
                {
                    var outList = new List<int>();
                    if (list != null)
                        foreach (int o in list)
                        {
                            if (!remap.TryGetValue(o, out int j))
                            {
                                j = nv.Count; remap[o] = j; nv.Add(v[o]);
                                if (n.Length == v.Length) nn.Add(n[o]); if (uv.Length == v.Length) nuv.Add(uv[o]); if (uv2.Length == v.Length) nuv2.Add(uv2[o]);
                                if (col.Length == v.Length) nc.Add(col[o]); if (tan.Length == v.Length) nt.Add(tan[o]);
                            }
                            outList.Add(j);
                        }
                    subs.Add(outList);
                }
                var m = new Mesh { indexFormat = nv.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
                m.SetVertices(nv); if (nn.Count == nv.Count) m.SetNormals(nn); if (nuv.Count == nv.Count) m.SetUVs(0, nuv); if (nuv2.Count == nv.Count) m.SetUVs(1, nuv2);
                if (nc.Count == nv.Count) m.SetColors(nc); if (nt.Count == nv.Count) m.SetTangents(nt);
                m.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) m.SetTriangles(subs[s], s);
                if (nn.Count != nv.Count) m.RecalculateNormals();
                m.RecalculateBounds();
                result.Add(m);
            }
            return result;
        }
    }
}
