using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// the finished world outside the map. 1 Import = importer + texture settings and the TeamLit materials (manifest values),
    /// FBX slots remapped by name. 2 Place (open level) = both FBX at the origin under Environment/CliffsV3, TER_Cliffs /
    /// TER_Apron and the manifest's remove list deactivated, reseat list moved, COL_Terrain swapped for the v3 collider (same
    /// name, layer and MeshCollider), then the river carve re-run. Idempotent: a placed level is detected and skipped.
    /// </summary>
    public static class OpenZCliffsV3
    {
        const string ModelDir = "Assets/OpenZ/Models/Environment/CliffsV3", TexDir = "Assets/OpenZ/Textures/CliffsV3", MatDir = "Assets/OpenZ/Materials/Env";

        static readonly (string mat, string tex, bool flat)[] Mats =
        {
            ("M_CliffV3_Rock_L02", "cliffv3_rock_L02_albedo", true), ("M_CliffV3_Top_L02", "cliffv3_top_L02_albedo", true),
            ("M_CliffV3_Dunes_L02", "cliffv3_dunes_L02_albedo", false), ("M_CliffV3_Rock_L17", "cliffv3_rock_L17_albedo", true),
            ("M_CliffV3_Top_L17", "cliffv3_top_L17_albedo", true), ("M_CliffV3_Concrete_L17", "cliffv3_concrete_L17_albedo", true),
            ("M_CliffV3_Facade_L17", "cliffv3_facade_L17_albedo", true), ("M_CliffV3_Ground_L17", "cliffv3_concrete_L17_albedo", false),
        };

        [MenuItem("OpenZ/Cliffs v3/1 Import (importers + materials)")]
        public static string Import()
        {
            var log = new List<string>();
            foreach (var p in Directory.GetFiles(TexDir, "*.png").Select(f => f.Replace('\\', '/')))
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                bool normal = p.EndsWith("_normal.png");
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal; ti.wrapMode = TextureWrapMode.Repeat; ti.mipmapEnabled = true;
                ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 8; ti.maxTextureSize = p.Contains("_rock_") ? 4096 : 2048;   // textures_v2: rock walls 2048 x 4096 (128 px/m), the rest as OpenZTexturesV2 rules
                ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.SaveAndReimport();       // BC7 (DXT1 blocked the clean gradients)
            }
            var mats = new Dictionary<string, Material>();
            foreach (var (name, tex, flat) in Mats) mats[name] = Mat(name, tex, flat);
            foreach (var extra in new[] { "M_L02_Ground", "M_L17_Ground", "M_Water_Harbour" })
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{extra}.mat"); if (m) mats[extra] = m;
            }
            foreach (var p in Directory.GetFiles(ModelDir, "*.fbx").Select(f => f.Replace('\\', '/')))
            {
                var imp = (ModelImporter)AssetImporter.GetAtPath(p);
                imp.globalScale = 1; imp.useFileUnits = true; imp.bakeAxisConversion = true;
                imp.importCameras = false; imp.importLights = false; imp.meshCompression = ModelImporterMeshCompression.Off;
                imp.isReadable = p.Contains("_cliffs_v3"); imp.indexFormat = ModelImporterIndexFormat.Auto;
                imp.importNormals = ModelImporterNormals.Import; imp.importTangents = ModelImporterTangents.None;
                imp.weldVertices = true; imp.keepQuads = false; imp.addCollider = false;
                imp.animationType = ModelImporterAnimationType.None; imp.importAnimation = false;
                imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                int remapped = 0; var missing = new List<string>();
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>())
                {
                    if (mats.TryGetValue(m.name, out var target)) { imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), target); remapped++; }
                    else missing.Add(m.name);
                }
                imp.SaveAndReimport();
                log.Add($"{Path.GetFileName(p)}: {remapped} slots remapped" + (missing.Count > 0 ? ", unmapped " + string.Join(",", missing) : ""));
            }
            AssetDatabase.SaveAssets();
            var s = string.Join("\n", log); Debug.Log("[OpenZ] Cliffs v3 import\n" + s); return s;
        }

        static Material Mat(string name, string tex, bool flat)
        {
            string p = $"{MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            m.shader = Shader.Find("OpenZ/TeamLit");
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/{tex}.png")); m.SetTextureScale("_BaseMap", Vector2.one);
            m.SetColor("_BaseColor", Color.white);
            void F(string k, float v) { if (m.HasProperty(k)) m.SetFloat(k, v); }
            void C(string k, Color v) { if (m.HasProperty(k)) m.SetColor(k, v); }
            F("_Triplanar", 0); F("_TriVertical", 0); F("_TopBlend", 0); F("_UseVertexColor", 1); F("_VertexColorSRGB", 1);
            F("_TeamMode", 0); F("_Accent", 0); F("_Degreen", 0); F("_HueShift", 0); F("_SatScale", 1); F("_ValScale", 1);
            F("_Smoothness", 0.05f); F("_Metallic", 0); F("_Stylize", 1); F("_Degrime", 0f /* textures_v2: no de-grime blur, the textures are clean */); F("_DegrimeLod", 2);
            C("_DarkColor", Color.black); F("_SoftFill", 0.6f); F("_AmbientBoost", 1.25f); C("_KeyWarm", new Color(1.06f, 1f, 0.92f));
            C("_RimColor", new Color(0.12f, 0.10f, 0.08f)); F("_RimPower", 3.2f); F("_FlatShade", flat ? 1 : 0);
            m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
        }

        [MenuItem("OpenZ/Cliffs v3/2 Place in open level")]
        public static string Place()
        {
            var scene = EditorSceneManager.GetActiveScene();
            string lv = scene.name.StartsWith("L17") ? "L17" : "L02";
            var env = GameObject.Find("Environment"); if (!env) return "no Environment";
            if (env.transform.Find("CliffsV3")) return lv + ": already placed";
            var log = new List<string>();
            var root = new GameObject("CliffsV3"); root.transform.SetParent(env.transform, false);
            foreach (var f in new[] { $"{lv}_cliffs_v3", $"{lv}_outer_v3" })
            {
                var src = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{f}.fbx");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(src);
                PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                go.transform.SetParent(root.transform, true); go.transform.position = Vector3.zero; go.transform.rotation = Quaternion.identity; go.transform.localScale = Vector3.one;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
                {
                    bool water = r.name.StartsWith("OUT3_SeaWater");
                    r.shadowCastingMode = water ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true;
                    GameObjectUtility.SetStaticEditorFlags(r.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                }
            }
            // markers: first (1,0,-1)
            var mk = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "MRK3_tile_0_0");
            log.Add(mk ? $"MRK3_tile_0_0 at {mk.position:F2} (expect (1,0,-1))" : "no MRK3_tile_0_0 marker");
            foreach (var t in root.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("MRK3_")).ToList()) t.gameObject.SetActive(false);

            int off = 0;
            foreach (var n in new[] { "TER_Cliffs", "TER_Apron" }) { var t = env.transform.Find(n); if (t) { t.gameObject.SetActive(false); off++; } }
            var all = env.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("DEC_")).GroupBy(t => t.name).ToDictionary(g => g.Key, g => g.ToList());
            int removed = 0, reseated = 0, missing = 0;
            string list = Path.Combine(OpenZPaths.RepoRoot, "projects", "blender", "export", "cliffs_z", $"cliffs_v3_scene_{lv}.txt");
            foreach (var line in File.ReadAllLines(list))
            {
                var p = line.Split(' '); if (p.Length < 2) continue;
                if (!all.TryGetValue(p[1], out var ts)) { missing++; continue; }
                foreach (var t in ts)
                {
                    if (p[0] == "remove") { t.gameObject.SetActive(false); removed++; }
                    else if (p[0] == "reseat" && p.Length >= 5)
                    { t.position += new Vector3(float.Parse(p[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(p[4], System.Globalization.CultureInfo.InvariantCulture)); reseated++; }
                }
            }
            log.Add($"deactivated {off} TER + {removed} DEC, reseated {reseated}, not found {missing}");

            // collider swap: the v3 COL_Terrain takes the old one's name, layer and place under Environment
            var oldCol = env.transform.Find("COL_Terrain");
            var newCol = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "COL_Terrain");
            if (newCol)
            {
                int layer = oldCol ? oldCol.gameObject.layer : newCol.gameObject.layer;
                if (oldCol) Object.DestroyImmediate(oldCol.gameObject);
                newCol.SetParent(env.transform, true); newCol.gameObject.layer = layer;
                var mr = newCol.GetComponent<MeshRenderer>(); if (mr) mr.enabled = false;
                var mc = newCol.GetComponent<MeshCollider>(); if (!mc) mc = newCol.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = newCol.GetComponent<MeshFilter>().sharedMesh;
                log.Add($"COL_Terrain swapped (layer {layer}, {mc.sharedMesh.triangles.Length / 3} tris)");
            }
            else log.Add("WARNING: no COL_Terrain in the v3 cliffs FBX");
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes();
            log.Add("carve: " + OpenZRiverCarve.Carve().Split('\n')[0]);
            var s = lv + ": " + string.Join("\n", log); Debug.Log("[OpenZ] Cliffs v3 placed\n" + s); return s;
        }
    }
}
