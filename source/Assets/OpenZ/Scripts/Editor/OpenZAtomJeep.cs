using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Imports AtomJeep.fbx (metres, identity nodes, custom normals), maps its seven materials to TeamLit (TeamMode 0, accent
    /// parts by the "owner" name, the README's colours), and swaps the Model child of Z_jeep. View only: the Zod jeep stats stay.
    /// </summary>
    public static class OpenZAtomJeep
    {
        const string Dir = "Assets/OpenZ/Models/Atom", Fbx = Dir + "/AtomJeep.fbx", MatDir = "Assets/OpenZ/Materials/Units", Prefab = "Assets/OpenZ/Prefabs/Z_jeep.prefab";

        struct Spec { public string tex; public Color32 col; public float metal, smooth; }
        static readonly Dictionary<string, Spec> Specs = new Dictionary<string, Spec>
        {
            ["AtomJeep_body"] = new Spec { tex = "atom_jeep_body_2048", col = new Color32(255, 255, 255, 255), metal = 0, smooth = 0.30f },
            ["AtomJeep_tire"] = new Spec { tex = "atom_jeep_tire_2048", col = new Color32(255, 255, 255, 255), metal = 0, smooth = 0.15f },
            ["AtomJeep_paint"] = new Spec { col = new Color32(204, 153, 51, 255), metal = 0, smooth = 0.32f },
            ["AtomGun_metal"] = new Spec { col = new Color32(107, 112, 120, 255), metal = 0.55f, smooth = 0.45f },
            ["AtomGun_dark"] = new Spec { col = new Color32(38, 38, 41, 255), metal = 0.2f, smooth = 0.3f },
            ["AtomJeep_seat"] = new Spec { col = new Color32(66, 46, 31, 255), metal = 0, smooth = 0.2f },
            ["AtomJeep_owner"] = new Spec { col = new Color32(153, 148, 140, 255), metal = 0, smooth = 0.4f },
        };

        [MenuItem("OpenZ/Atom Jeep: Import + swap Z_jeep")]
        public static string Run()
        {
            var imp = (ModelImporter)AssetImporter.GetAtPath(Fbx);
            imp.globalScale = 1; imp.useFileUnits = true; imp.bakeAxisConversion = false; imp.preserveHierarchy = true;
            imp.importNormals = ModelImporterNormals.Import; imp.importTangents = ModelImporterTangents.CalculateMikk;
            imp.animationType = ModelImporterAnimationType.None; imp.importAnimation = false; imp.importCameras = false; imp.importLights = false;
            imp.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            var log = new List<string>();
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(Fbx).OfType<Material>())
                imp.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.name), Mat(m.name, log));
            imp.SaveAndReimport();

            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
            var root = PrefabUtility.LoadPrefabContents(Prefab);
            try
            {
                var old = root.transform.Find("Model");
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(model);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model"; inst.transform.SetParent(root.transform, false);
                inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity; inst.transform.localScale = Vector3.one;
                if (old) { inst.transform.SetSiblingIndex(old.GetSiblingIndex()); Object.DestroyImmediate(old.gameObject); }
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.receiveShadows = true; }
                var b = inst.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((x, y) => { x.Encapsulate(y); return x; });
                PrefabUtility.SaveAsPrefabAsset(root, Prefab);
                log.Add($"Z_jeep: Model -> AtomJeep ({inst.GetComponentsInChildren<Renderer>(true).Length} renderers), size {b.size:F2}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
            var s = string.Join("\n", log); Debug.Log("[OpenZ] " + s); return s;
        }

        static Material Mat(string name, List<string> log)
        {
            string p = $"{MatDir}/M_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(Shader.Find("OpenZ/TeamLit")); AssetDatabase.CreateAsset(m, p); }
            Specs.TryGetValue(name, out var sp);
            OpenZStyle.ApplyUnitStyle(m, false);
            m.SetTexture("_BaseMap", sp.tex != null ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{sp.tex}.png") : null);
            m.SetColor("_BaseColor", sp.tex != null ? Color.white : (Color)sp.col);
            m.SetFloat("_TeamMode", 0); m.SetFloat("_Degreen", 0); m.SetFloat("_Accent", 1);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", sp.metal);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", sp.smooth);
            m.enableInstancing = true; EditorUtility.SetDirty(m); log.Add("material " + name + (sp.tex != null ? " (tex)" : "")); return m;
        }
    }
}
