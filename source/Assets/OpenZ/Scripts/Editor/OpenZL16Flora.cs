using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Level 16 trees and plants, placed into the OPEN level 16 scene (the scene is never rebuilt), from the list the flora
    /// export wrote (every plant of the last blockout with its shape, place, turn and size):
    /// - L16_Flora under Environment: one object per 32 m cell and family (T trees, P small plants). Near: every plant of the
    ///   cell as its own object sharing the shape's mesh and materials (the engine draws equal ones together). Far: ONE mesh
    ///   per cell joined from the thinned, vertex-coloured far shapes, one material, no shadows. A level-of-detail switch per cell.
    /// - the map's own trees and mounds (map objects 17 to 20, which block in the rules): the desert stand-in under each
    ///   scene object is switched off and the jungle shape stands in its place, with the turn and size of the blockout.
    /// A tall plant stands only on tiles that are closed (rock, the map sides, or tiles the level data closes under it).
    /// </summary>
    public static partial class OpenZL16Flora                                            // DV88: + OpenZL16Flora.TreeFar.cs (menu 8b: the breakable trees' runs in the far meshes)
    {
        const string Dir = "Assets/OpenZ/Models/L16/Flora";
        const string Tex = "Assets/OpenZ/Textures/L16/Flora";
        const string Kit = Dir + "/L16_flora_kit.fbx";
        const string FarAsset = Dir + "/L16_flora_far.asset";
        static string PlacesPath => Path.Combine(OpenZPaths.RepoRoot, "work", "staging", "v1_1_20261012", "jungle_import", "J6_flora", "out", "L16_flora_places.json");

        [System.Serializable] class MatRow { public string key, image; public float[] color; }
        [System.Serializable] class Row { public string name, data, family, kind; public int[] cell, tile; public float[] loc, quat_wxyz, scale; }
        [System.Serializable] class Doc { public float cell_m; public Row[] objects, map_objects; public MatRow[] materials; }

        static Matrix4x4 PlaceMatrix(Row o, out Vector3 pos, out Quaternion rot, out Vector3 scl)
        {
            OpenZL16Rocks.Place(new OpenZL16Rocks.BObj { loc = o.loc, quat_wxyz = o.quat_wxyz, scale = o.scale }, out pos, out rot, out scl);
            return Matrix4x4.TRS(pos, rot, scl);
        }

        static void Apply(Transform t, Matrix4x4 m)
        {
            Vector3 x = m.GetColumn(0), y = m.GetColumn(1), z = m.GetColumn(2);
            float sx = x.magnitude, sy = y.magnitude, sz = z.magnitude;
            if (Vector3.Dot(Vector3.Cross(x, y), z) < 0f) sx = -sx;
            t.position = m.GetColumn(3); t.rotation = Quaternion.LookRotation(z / sz, y / sy); t.localScale = new Vector3(sx, sy, sz);
        }

        [MenuItem("OpenZ/L16 Jungle/8 Trees and plants (per cell with far versions; open level 16 scene)")]
        public static void FloraMenu() => Debug.Log("[OpenZ L16] " + Flora());

        public static string Flora()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (!scene.name.StartsWith("L16")) return "open the level 16 scene first";
            var env = GameObject.Find("Environment"); if (!env) return "Environment missing";
            if (!File.Exists(PlacesPath)) return "list missing: " + PlacesPath;
            var doc = JsonUtility.FromJson<Doc>(File.ReadAllText(PlacesPath));

            // ---- pictures and materials
            Directory.CreateDirectory(Tex); var mats = new Dictionary<string, Material>(); var sb = new System.Text.StringBuilder();
            foreach (var m in doc.materials)
            {
                if (!string.IsNullOrEmpty(m.image))
                {
                    string dst = $"{Tex}/{m.key}{Path.GetExtension(m.image)}";
                    if (!File.Exists(dst)) { if (!File.Exists(m.image)) { sb.Append($" [picture missing: {m.key}]"); continue; } File.Copy(m.image, dst); AssetDatabase.ImportAsset(dst); }
                    OpenZL16Water.ImportTex(dst);
                    mats[m.key] = OpenZL16Water.LitMat("M_L16_Flora_" + m.key, dst, 0.04f, Dir);
                }
                else
                {
                    var fm = OpenZL16Water.LitMat("M_L16_Flora_" + m.key, null, 0.04f, Dir);
                    if (m.color != null && m.color.Length >= 3) fm.SetColor("_BaseColor", new Color(m.color[0], m.color[1], m.color[2], 1f));
                    mats[m.key] = fm;
                }
            }
            var far = OpenZL16Water.LitMat("M_L16_Flora_far", null, 0.02f, Dir);
            far.SetFloat("_UseVertexColor", 1); far.SetFloat("_VertexColorSRGB", 1); EditorUtility.SetDirty(far);

            // ---- the shapes
            var mi = (ModelImporter)AssetImporter.GetAtPath(Kit); if (mi == null) return "missing " + Kit;
            mi.importCameras = false; mi.importLights = false; mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.bakeAxisConversion = true; mi.isReadable = true; mi.meshCompression = ModelImporterMeshCompression.Off; mi.addCollider = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; mi.materialLocation = ModelImporterMaterialLocation.InPrefab; mi.SaveAndReimport();
            var kit = AssetDatabase.LoadAssetAtPath<GameObject>(Kit);
            var shape = new Dictionary<string, Transform>(); foreach (Transform t in kit.transform) shape[t.name] = t;
            Material[] MatsOf(Transform proto)
            {
                var ms = proto.GetComponent<MeshRenderer>().sharedMaterials;
                for (int i = 0; i < ms.Length; i++) { string k = ms[i] ? ms[i].name.Replace("EXP_", "") : ""; ms[i] = mats.TryGetValue(k, out var mm) ? mm : far; }
                return ms;
            }

            var old = env.transform.Find("L16_Flora"); if (old) Object.DestroyImmediate(old.gameObject);
            var root = new GameObject("L16_Flora").transform; root.SetParent(env.transform, false);
            var ground = env.transform.Find("TER_Ground"); int layer = ground ? ground.gameObject.layer : 0;
            AssetDatabase.DeleteAsset(FarAsset);
            var cells = new Dictionary<string, (Transform t, List<Renderer> near, List<CombineInstance> far, bool trees)>();
            var matCache = new Dictionary<string, Material[]>(); var list = OpenZL16Rocks.LoadList().ToDictionary(o => o.name, o => o);
            int placed = 0, noShape = 0; long nearTris = 0; float worst = 0f; int checkedBoxes = 0;
            foreach (var o in doc.objects)
            {
                if (!shape.TryGetValue(o.data, out var proto)) { noShape++; continue; }
                string key = $"{o.family}_{o.cell[0]}_{o.cell[1]}";
                if (!cells.TryGetValue(key, out var c))
                {
                    var ct = new GameObject(key).transform; ct.SetParent(root, false);
                    c = (ct, new List<Renderer>(), new List<CombineInstance>(), o.family == "T"); cells[key] = c;
                }
                var m = PlaceMatrix(o, out _, out _, out _);
                var go = Object.Instantiate(proto.gameObject, c.t, false); go.name = o.name; go.layer = layer;
                Apply(go.transform, m * Matrix4x4.TRS(proto.localPosition, proto.localRotation, proto.localScale));
                var r = go.GetComponent<MeshRenderer>();
                if (!matCache.TryGetValue(o.data, out var ms)) { ms = MatsOf(proto); matCache[o.data] = ms; }
                r.sharedMaterials = ms; r.shadowCastingMode = c.trees ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
                c.near.Add(r); placed++;
                var mesh = go.GetComponent<MeshFilter>().sharedMesh; for (int si = 0; si < mesh.subMeshCount; si++) nearTris += mesh.GetIndexCount(si) / 3;
                if (shape.TryGetValue(o.data + "_far", out var fp))
                    c.far.Add(new CombineInstance { mesh = fp.GetComponent<MeshFilter>().sharedMesh, transform = m * Matrix4x4.TRS(fp.localPosition, fp.localRotation, fp.localScale) });
                if (placed % 23 == 0 && list.TryGetValue(o.name, out var lo) && lo.min != null) { worst = Mathf.Max(worst, OpenZL16Rocks.BoxError(lo, go.transform, Quaternion.identity, true)); checkedBoxes++; }
            }

            // ---- far mesh and the switch per cell
            long farTris = 0; bool first = true;
            foreach (var kv in cells)
            {
                var c = kv.Value; var lods = new List<LOD>();
                lods.Add(new LOD(c.trees ? 0.9f : 0.95f, c.near.ToArray()));           // the quality setting doubles these distances (bias 2): near shapes up to a camera height of about 125 m over the cell
                if (c.far.Count > 0)
                {
                    var fm = new Mesh { name = "FAR_" + kv.Key, indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
                    fm.CombineMeshes(c.far.ToArray(), true, true); fm.RecalculateBounds(); fm.UploadMeshData(false);
                    if (first) { AssetDatabase.CreateAsset(fm, FarAsset); first = false; } else AssetDatabase.AddObjectToAsset(fm, FarAsset);
                    var fg = new GameObject("FAR"); fg.transform.SetParent(c.t, false); fg.layer = layer;
                    fg.AddComponent<MeshFilter>().sharedMesh = fm; var fr = fg.AddComponent<MeshRenderer>(); fr.sharedMaterial = far;
                    fr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lods.Add(new LOD(c.trees ? 0.01f : 0.08f, new Renderer[] { fr })); farTris += fm.GetIndexCount(0) / 3;
                }
                var lg = c.t.gameObject.AddComponent<LODGroup>(); lg.fadeMode = LODFadeMode.None; lg.SetLODs(lods.ToArray()); lg.RecalculateBounds();
            }

            // ---- the map's own trees and mounds
            int swapped = 0; var missing = new List<string>();
            foreach (var o in doc.map_objects)
            {
                var so = GameObject.Find($"ZodObjects/{o.kind}_{o.tile[0]}_{o.tile[1]}");
                if (!so || !shape.TryGetValue(o.data, out var proto)) { missing.Add($"{o.kind}_{o.tile[0]}_{o.tile[1]}"); continue; }
                var standIn = so.transform.Find("Model"); if (standIn) standIn.gameObject.SetActive(false);
                var was = so.transform.Find("JungleModel"); if (was) Object.DestroyImmediate(was.gameObject);
                var m = PlaceMatrix(o, out var pos, out _, out _);
                var go = Object.Instantiate(proto.gameObject, so.transform, false); go.name = "JungleModel";
                Apply(go.transform, m * Matrix4x4.TRS(proto.localPosition, proto.localRotation, proto.localScale));
                go.transform.position = new Vector3(so.transform.position.x, so.transform.position.y, so.transform.position.z);   // on the scene object's own spot and ground height
                var r = go.GetComponent<MeshRenderer>(); r.sharedMaterials = MatsOf(proto); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                swapped++;
            }
            AssetDatabase.SaveAssets(); EditorSceneManager.MarkSceneDirty(scene);
            return $"trees and plants: {placed} placed in {cells.Count} cells ({cells.Count(x => x.Value.trees)} tree cells), {nearTris} triangles near (shared shapes: {shape.Count / 2}), {farTris} far; " +
                   $"largest box difference to the blockout on {checkedBoxes} samples {worst:0.00} m; shapes missing for {noShape}; map trees and mounds swapped in: {swapped} of {doc.map_objects.Length}" +
                   (missing.Count > 0 ? " (not found: " + string.Join(", ", missing.Take(8)) + ")" : "") + sb;
        }
    }
}
