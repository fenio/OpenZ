using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The BS-01 bodies are one skinned mesh (not readable in players), so the parts are cut once here in
    /// the editor: every triangle goes to the bone that carries most of its skin weight, the bones are grouped into 11 body parts
    /// (head, torso shell, pelvis, shoulder pad + upper arm L / R, forearm + hand L / R, thigh L / R, shin + boot L / R; unknown
    /// extra bones such as the Electrobot's join their nearest known parent), and each part is stored in its anchor bone's space
    /// (bindpose), centred on its own bounds. RobotPopDeath places a part on its bone's pose of the death frame, so the parts sit
    /// exactly where the body was, then throws them. Output: Resources/OpenZ/RobotParts/&lt;type&gt;/NN_&lt;bone&gt;@cx,cy,cz.asset (the mesh
    /// takes the file name; cx,cy,cz = the bone-space centre). Idempotent (a rebake replaces the set). Menu: OpenZ/Deaths/Bake robot body parts.
    /// </summary>
    public static class OpenZRobotParts
    {
        const string OutRoot = "Assets/OpenZ/Resources/OpenZ/RobotParts";
        static readonly string[] Types = { "grunt", "psycho", "sniper", "tough", "pyro", "laser", "electrobot" };
        static readonly string[][] Groups =
        {
            new[] { "Head", "Neck", "HeadTop_End", "HeadFront" },
            new[] { "Spine2", "Spine1" },
            new[] { "Hips", "Spine" },
            new[] { "LeftArm", "LeftShoulder" },
            new[] { "LeftForeArm", "LeftHand" },
            new[] { "RightArm", "RightShoulder" },
            new[] { "RightForeArm", "RightHand" },
            new[] { "LeftUpLeg" },
            new[] { "LeftLeg", "LeftFoot", "LeftToeBase" },
            new[] { "RightUpLeg" },
            new[] { "RightLeg", "RightFoot", "RightToeBase" },
        };
        const int MinPartTris = 20;

        static string Short(string n)
        {
            int i = n.LastIndexOf("mixamorig", System.StringComparison.OrdinalIgnoreCase);
            if (i >= 0) n = n.Substring(i + 9);
            return n.TrimStart(':', '_');
        }

        static int GroupOf(Transform b)
        {
            for (var t = b; t; t = t.parent)
            {
                var s = Short(t.name);
                for (int g = 0; g < Groups.Length; g++) foreach (var n in Groups[g]) if (n == s) return g;
            }
            return -1;
        }

        [MenuItem("OpenZ/Deaths/Bake robot body parts (PD1)")]
        public static void BakeAll()
        {
            var log = new List<string>();
            foreach (var t in Types)
            {
                try { log.Add(t + ": " + Bake(t)); }
                catch (System.Exception e) { log.Add(t + ": FAILED " + e.Message); }
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log("[OpenZ] PD1 robot body parts:\n" + string.Join("\n", log));
        }

        static string Bake(string type)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/OpenZ/Prefabs/Z_bs01_{type}.prefab");
            if (!prefab) return "no prefab";
            SkinnedMeshRenderer smr = null;
            foreach (var s in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true)) if (s.sharedMesh && (!smr || s.sharedMesh.vertexCount > smr.sharedMesh.vertexCount)) smr = s;
            if (!smr) return "no skinned body";
            var m = smr.sharedMesh; var bones = smr.bones; var bp = m.bindposes;
            var boneGroup = new int[bones.Length];
            for (int i = 0; i < bones.Length; i++) boneGroup[i] = bones[i] ? GroupOf(bones[i]) : -1;
            var anchor = new int[Groups.Length];
            for (int g = 0; g < Groups.Length; g++)
            {
                anchor[g] = -1;
                for (int i = 0; i < bones.Length && anchor[g] < 0; i++) if (bones[i] && Short(bones[i].name) == Groups[g][0]) anchor[g] = i;
                for (int i = 0; i < bones.Length && anchor[g] < 0; i++) if (boneGroup[i] == g) anchor[g] = i;
            }

            var v = m.vertices; var n = m.normals; var tan = m.tangents; var col = m.colors; var bw = m.boneWeights;
            var uvs = new List<Vector4>[4];
            for (int ch = 0; ch < 4; ch++)
                if (m.HasVertexAttribute(VertexAttribute.TexCoord0 + ch)) { uvs[ch] = new List<Vector4>(); m.GetUVs(ch, uvs[ch]); }

            // triangles per group per sub-mesh
            var tris = new List<int>[Groups.Length][];
            for (int g = 0; g < Groups.Length; g++) tris[g] = new List<int>[m.subMeshCount];
            var w = new Dictionary<int, float>();
            for (int s = 0; s < m.subMeshCount; s++)
            {
                var idx = m.GetTriangles(s);
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    w.Clear();
                    for (int k = 0; k < 3; k++)
                    {
                        var b = bw[idx[i + k]];
                        Add(w, b.boneIndex0, b.weight0); Add(w, b.boneIndex1, b.weight1); Add(w, b.boneIndex2, b.weight2); Add(w, b.boneIndex3, b.weight3);
                    }
                    int best = -1; float bestW = -1f;
                    foreach (var kv in w) if (kv.Value > bestW && kv.Key >= 0 && kv.Key < bones.Length && boneGroup[kv.Key] >= 0) { best = kv.Key; bestW = kv.Value; }
                    if (best < 0) continue;
                    int grp = boneGroup[best];
                    (tris[grp][s] ??= new List<int>()).AddRange(new[] { idx[i], idx[i + 1], idx[i + 2] });
                }
            }

            string dir = $"{OutRoot}/{type}";
            if (Directory.Exists(dir)) foreach (var f in Directory.GetFiles(dir, "*.asset")) AssetDatabase.DeleteAsset(f.Replace('\\', '/'));
            else Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();

            int made = 0, totalTris = 0; var names = new List<string>();
            for (int g = 0; g < Groups.Length; g++)
            {
                int count = 0; foreach (var l in tris[g]) if (l != null) count += l.Count / 3;
                if (count < MinPartTris || anchor[g] < 0) continue;
                var toBone = bp[anchor[g]];
                var remap = new Dictionary<int, int>();
                var nv = new List<Vector3>(); var nn = new List<Vector3>(); var nt = new List<Vector4>(); var nc = new List<Color>();
                var nuv = new List<Vector4>[4]; for (int ch = 0; ch < 4; ch++) if (uvs[ch] != null) nuv[ch] = new List<Vector4>();
                var subs = new List<List<int>>();
                foreach (var list in tris[g])
                {
                    var outList = new List<int>();
                    if (list != null)
                        foreach (int o in list)
                        {
                            if (!remap.TryGetValue(o, out int j))
                            {
                                j = nv.Count; remap[o] = j;
                                nv.Add(toBone.MultiplyPoint3x4(v[o]));
                                if (n.Length == v.Length) nn.Add(toBone.MultiplyVector(n[o]).normalized);
                                if (tan.Length == v.Length) { var tv = toBone.MultiplyVector(tan[o]).normalized; nt.Add(new Vector4(tv.x, tv.y, tv.z, tan[o].w)); }
                                if (col.Length == v.Length) nc.Add(col[o]);
                                for (int ch = 0; ch < 4; ch++) if (nuv[ch] != null) nuv[ch].Add(uvs[ch][o]);
                            }
                            outList.Add(j);
                        }
                    subs.Add(outList);
                }
                var bb = new Bounds(nv[0], Vector3.zero); foreach (var p in nv) bb.Encapsulate(p);
                var c = bb.center; for (int i = 0; i < nv.Count; i++) nv[i] -= c;
                var pm = new Mesh { indexFormat = nv.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                pm.SetVertices(nv); if (nn.Count == nv.Count) pm.SetNormals(nn); if (nt.Count == nv.Count) pm.SetTangents(nt); if (nc.Count == nv.Count) pm.SetColors(nc);
                for (int ch = 0; ch < 4; ch++) if (nuv[ch] != null) pm.SetUVs(ch, nuv[ch]);
                pm.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) pm.SetTriangles(subs[s], s);
                if (nn.Count != nv.Count) pm.RecalculateNormals();
                pm.RecalculateBounds();
                var boneName = bones[anchor[g]].name;
                // CreateAsset renames the mesh to its file name, so the file name carries the bone and the centre
                pm.name = $"{g:00}_{boneName}@" + c.x.ToString("F5", CultureInfo.InvariantCulture) + "," + c.y.ToString("F5", CultureInfo.InvariantCulture) + "," + c.z.ToString("F5", CultureInfo.InvariantCulture);
                AssetDatabase.CreateAsset(pm, $"{dir}/{pm.name}.asset");
                made++; totalTris += count; names.Add(Short(boneName) + " " + count);
            }
            return $"{made} parts, {totalTris} of {m.triangles.Length / 3} tris ({string.Join(", ", names)})";
        }

        static void Add(Dictionary<int, float> w, int i, float x) { if (x <= 0f) return; w.TryGetValue(i, out var o); w[i] = o + x; }
    }
}
