using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// old plank / slab bridges (same child names and footprint, GUIDs kept), their six new materials are mapped to TeamLit
    /// vertex-colour materials, and only the six bridge prefabs are rebuilt (intact / damaged / destroyed per biome), so the
    /// rest of today's tuned prefabs and materials are untouched. The L02 bridges go back to the kit's height (they were
    /// lowered 0.8 m for the old ramp-less plank deck; the new kit has its own stone ramps).
    /// </summary>
    public static class OpenZBridges
    {
        const string Env = "Assets/OpenZ/Models/Environment", Pref = "Assets/OpenZ/Prefabs", MatEnv = "Assets/OpenZ/Materials/Env";

        [MenuItem("OpenZ/Bridges: Import Z bridge kit")]
        public static string Import()
        {
            string src = Path.Combine(OpenZPaths.RepoRoot, "projects", "blender", "export", "bridges_z");
            foreach (var f in new[] { "BRG_desert", "BRG_city" }) File.Copy(Path.Combine(src, f + ".fbx"), $"{Env}/{f}.fbx", true);
            AssetDatabase.Refresh();
            foreach (var f in new[] { "BRG_desert", "BRG_city" })
            {
                string p = $"{Env}/{f}.fbx";
                var imp = (ModelImporter)AssetImporter.GetAtPath(p);
                imp.importCameras = false; imp.importLights = false; imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard; imp.bakeAxisConversion = true;
                imp.isReadable = true;                                                                  // chunks get runtime convex colliders
                foreach (var m in AssetDatabase.LoadAllAssetsAtPath(p).OfType<Material>())
                    if (m.name.StartsWith("M_BRGZ")) imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), Mat(m.name));
                imp.SaveAndReimport();
            }
            int n = 0;
            foreach (var biome in new[] { "desert", "city" })
                foreach (var state in new[] { "intact", "damaged", "destroyed" })
                {
                    var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"{Env}/BRG_{biome}.fbx");
                    var child = fbx.transform.Cast<Transform>().FirstOrDefault(t => t.name == $"BRG_{biome}_{state}");
                    if (!child) { Debug.LogWarning("missing " + biome + " " + state); continue; }
                    var root = new GameObject($"Z_bridge_{biome}_{state}");
                    var inst = Object.Instantiate(child.gameObject, root.transform, false); inst.name = "Model"; inst.transform.localPosition = Vector3.zero;
                    var rs = inst.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                    inst.transform.localPosition = new Vector3(-b.center.x, 0, -b.center.z);
                    foreach (var r in rs) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; }
                    root.AddComponent<TeamPaint>();
                    PrefabUtility.SaveAsPrefabAsset(root, $"{Pref}/Z_bridge_{biome}_{state}.prefab"); Object.DestroyImmediate(root); n++;
                }
            AssetDatabase.SaveAssets();
            return $"bridge kit imported, {n} prefabs";
        }

        /// <summary>README look: TeamLit, vertex colours (sRGB), TeamMode 0, stylized flat shading, steel a bit glossier.</summary>
        static Material Mat(string name)
        {
            string p = $"{MatEnv}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            m.SetTexture("_BaseMap", null); m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_UseVertexColor", 1); m.SetFloat("_VertexColorSRGB", 1); m.SetFloat("_TeamMode", 0);
            m.SetFloat("_Stylize", 1); m.SetFloat("_FlatShade", 1); m.SetFloat("_Degrime", 0); m.SetFloat("_Degreen", 0);
            m.SetFloat("_Smoothness", name.Contains("Steel") ? 0.3f : 0.12f); m.SetFloat("_Metallic", 0);
            m.SetColor("_RimColor", Color.black);
            if (m.HasProperty("_SoftFill")) m.SetFloat("_SoftFill", 0.6f);
            if (m.HasProperty("_AmbientBoost")) m.SetFloat("_AmbientBoost", 1.25f);
            if (m.HasProperty("_KeyWarm")) m.SetColor("_KeyWarm", new Color(1.06f, 1f, 0.92f));
            m.SetColor("_DarkColor", new Color(0.02f, 0.012f, 0.008f)); m.SetFloat("_DarkThreshold", 0.08f);
            m.enableInstancing = true; EditorUtility.SetDirty(m); return m;
        }

        /// <summary>
        /// down so the deck top sits 6 cm above its higher bank (terrain collider sampled 1.5 m beyond both deck ends). The kit's
        /// ramps sink into the banks; the water under the bridge is untouched. Works on the open level scene; re-runnable.</summary>
        [MenuItem("OpenZ/Bridges: Decks at ground height (open scene)")]
        public static string DecksToGround()
        {
            var scene = EditorSceneManager.GetActiveScene();
            var zroot = GameObject.Find("ZodObjects"); var terr = GameObject.Find("COL_Terrain");
            var tc = terr ? terr.GetComponent<Collider>() : null;
            if (!zroot || !tc) return "no ZodObjects / COL_Terrain in " + scene.name;
            float Ty(float x, float z) => tc.Raycast(new Ray(new Vector3(x, 200, z), Vector3.down), out var h, 400) ? h.point.y : float.NaN;
            var log = new System.Text.StringBuilder();
            foreach (Transform t in zroot.transform.Cast<Transform>().ToArray())
            {
                if (!t.name.StartsWith("bridge_")) continue;
                var under = zroot.transform.Find("underlay_" + t.name);
                if (!under) { log.Append($"{t.name}: no underlay; "); continue; }
                var ur = under.GetComponentInChildren<Renderer>(); if (!ur) continue;
                var ub = ur.bounds; bool vert = t.name.StartsWith("bridge_vert");
                float e1 = vert ? Ty(ub.center.x, ub.min.z - 1.5f) : Ty(ub.min.x - 1.5f, ub.center.z);
                float e2 = vert ? Ty(ub.center.x, ub.max.z + 1.5f) : Ty(ub.max.x + 1.5f, ub.center.z);
                float bank = Mathf.Max(float.IsNaN(e1) ? -9 : e1, float.IsNaN(e2) ? -9 : e2);
                if (bank < -5) { log.Append($"{t.name}: no bank; "); continue; }
                float dy = bank + 0.06f - ub.max.y;
                if (Mathf.Abs(dy) < 0.02f) { log.Append($"{t.name}: ok; "); continue; }
                Undo.RecordObjects(new Object[] { t, under }, "Bridge decks to ground");
                t.position += Vector3.up * dy; under.position += Vector3.up * dy;
                EditorUtility.SetDirty(t); EditorUtility.SetDirty(under);
                log.Append($"{t.name}: deck {ub.max.y:F2} -> {ub.max.y + dy:F2} (banks {e1:F2}/{e2:F2}); ");
            }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes();
            return scene.name + ": " + log;
        }

        /// <summary>L02: the bridges back at the kit height (+0.8 m), underlays too.</summary>
        public static string RestoreL02Heights()
        {
            var scene = EditorSceneManager.GetActiveScene(); if (!scene.name.StartsWith("L02")) return "open L02 first";
            int n = 0;
            foreach (Transform t in GameObject.Find("ZodObjects").transform)
                if ((t.name.StartsWith("bridge_") || t.name.StartsWith("underlay_bridge_")) && t.position.y < (t.name.StartsWith("bridge_") ? -0.5f : 0.5f))
                { t.position += Vector3.up * 0.8f; EditorUtility.SetDirty(t); n++; }
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveOpenScenes();
            return $"{n} L02 bridge objects restored to the kit height";
        }
    }
}
