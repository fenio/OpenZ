using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// makes M_BS01_Kit (TeamLit mode 3 like the body, vertex AO) and bakes each kit into its Z_bs01_* prefab through
    /// BS01TypeKit (anchors parented to the humanoid bones, per-type Model scale, feed belt / fuel hose cables, the pyro's
    /// hazard-yellow flamer tank). Run after "BS-01: 1 Import" and "BS-01: 2 Controller + prefabs"; re-running rebuilds
    /// cleanly (the previous kit is removed first). Scene instances of the prefabs update by themselves.
    /// </summary>
    public static class OpenZBS01Types
    {
        const string Dir = "Assets/OpenZ/Models/BS01/Types", Tex = Dir + "/Textures", Pref = "Assets/OpenZ/Prefabs", Mat = "Assets/OpenZ/Materials/Units";
        const string Atlas = Tex + "/BS01_Kit_BaseColor.png", PyroHazard = Tex + "/BS01_Weapon_Pyro_BaseColor_Hazard.png";
        static readonly BS01Type[] Types = { BS01Type.Grunt, BS01Type.Psycho, BS01Type.Tough, BS01Type.Sniper, BS01Type.Pyro };

        [MenuItem("OpenZ/BS-01: 4 Robot type kits (import + bake prefabs)")]
        public static string BakeAll() => Bake(true);

        [MenuItem("OpenZ/BS-01: 4a Robot type kits (keep the red pyro flamer tank)")]
        public static string BakeAllRedTank() => Bake(false);

        public static string Bake(bool hazardTank)
        {
            var log = new List<string>();
            ImportSettings(log);
            var mat = KitMaterial();
            if (!mat) return "M_BS01_Kit: no source material (run BS-01: 2 first)";
            Material hazard = hazardTank ? PyroHazardMaterial() : null;
            foreach (var t in Types)
            {
                string fbx = $"{Dir}/BS01_Type_{t}.fbx", path = $"{Pref}/Z_bs01_{t.ToString().ToLower()}.prefab";
                var kitModel = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
                if (!kitModel) { log.Add($"{t}: {fbx} missing"); continue; }
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) { log.Add($"{t}: {path} missing"); continue; }
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var kit = BS01TypeKit.Attach(root, t, kitModel, mat);
                    if (!kit) { log.Add($"{t}: attach failed"); continue; }
                    if (t == BS01Type.Pyro)
                    {
                        var w = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.StartsWith("BS01_Weapon_Pyro"));
                        var src = AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/M_BS01_Weapon_Pyro.mat");
                        var use = hazard ? hazard : src;
                        if (w && use) foreach (var r in w.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = use;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    var model = root.GetComponentInChildren<Animator>(true).transform;
                    log.Add($"{t}: {kit.CoreParts.Count} core + {kit.BackParts.Count} back meshes, Model scale {model.localScale.x:F3}/{model.localScale.y:F3}/{model.localScale.z:F3}");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();                                          // prefab assets only; scene instances follow
            return string.Join("\n", log);
        }

        [MenuItem("OpenZ/BS-01: 4b Remove robot type kits")]
        public static string RemoveAll()
        {
            var log = new List<string>();
            foreach (var t in Types)
            {
                string path = $"{Pref}/Z_bs01_{t.ToString().ToLower()}.prefab";
                if (!AssetDatabase.LoadAssetAtPath<GameObject>(path)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var kit = root.GetComponent<BS01TypeKit>();
                    if (kit) { kit.Remove(); Object.DestroyImmediate(kit); }
                    if (t == BS01Type.Pyro)
                    {
                        var w = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name.StartsWith("BS01_Weapon_Pyro"));
                        var src = AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/M_BS01_Weapon_Pyro.mat");
                        if (w && src) foreach (var r in w.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = src;
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path); log.Add(t + ": kit removed");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            return string.Join("\n", log);
        }

        // ---------------------------------------------------------------------------------------------------------------
        static void ImportSettings(List<string> log)
        {
            foreach (var t in Types)
            {
                var mi = AssetImporter.GetAtPath($"{Dir}/BS01_Type_{t}.fbx") as ModelImporter;
                if (!mi) continue;
                mi.preserveHierarchy = true;                                      // keep the BS01_Type_* root + KitBone_* anchors
                mi.animationType = ModelImporterAnimationType.None; mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.importVisibility = false;
                mi.importNormals = ModelImporterNormals.Import; mi.addCollider = false; mi.isReadable = false;
                mi.globalScale = 1f; mi.useFileScale = true;
                mi.SaveAndReimport();
            }
            foreach (var p in new[] { Atlas, PyroHazard })
            {
                var ti = AssetImporter.GetAtPath(p) as TextureImporter;
                if (!ti) continue;
                ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;   // A = team mask
                ti.mipmapEnabled = true; ti.filterMode = FilterMode.Bilinear; ti.wrapMode = TextureWrapMode.Clamp;
                ti.maxTextureSize = p == Atlas ? 1024 : 2048; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            log.Add("import settings: kits (preserve hierarchy, no materials), atlas (alpha = team mask)");
        }

        static void Set(Material m, string p, float v) { if (m.HasProperty(p)) m.SetFloat(p, v); }

        /// <summary>M_BS01_Kit: the body's TeamLit settings (mode 3, alpha = team mask) on the kit atlas, vertex RGB = baked AO.</summary>
        static Material KitMaterial()
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/M_BS01_Body.mat");
            if (!src) return null;
            string p = $"{Mat}/M_BS01_Kit.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, p); } else m.CopyPropertiesFromMaterial(src);
            m.shader = src.shader; m.name = "M_BS01_Kit";
            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Atlas));
            m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            Set(m, "_TeamMode", 3); Set(m, "_TeamRef", 0.18f); Set(m, "_ShadeMin", 0.6f); Set(m, "_ShadeMax", 1.25f);
            Set(m, "_Degrime", 0.2f); Set(m, "_DegrimeLod", 1.0f);          // flat colour cells: almost nothing to de-grime
            Set(m, "_Degreen", 0); Set(m, "_SatScale", 1); Set(m, "_ValScale", 1); Set(m, "_HueShift", 0);   // brass + hazard yellow sit in the olive hue band
            Set(m, "_Accent", 0); Set(m, "_CreamToTeam", 0); Set(m, "_TeamLift", 0);
            Set(m, "_UseVertexColor", 1); Set(m, "_VertexColorSRGB", 1);    // soft AO baked in Blender (0.6..1)
            m.enableInstancing = true;
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>DESIGN.md: the flamer's red fuel tank reads as a red-team marker on a blue pyro, so it goes hazard yellow.</summary>
        static Material PyroHazardMaterial()
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>($"{Mat}/M_BS01_Weapon_Pyro.mat");
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PyroHazard);
            if (!src || !tex) return null;
            string p = $"{Mat}/M_BS01_Weapon_Pyro_Hazard.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (!m) { m = new Material(src); AssetDatabase.CreateAsset(m, p); } else m.CopyPropertiesFromMaterial(src);
            m.shader = src.shader; m.name = "M_BS01_Weapon_Pyro_Hazard";
            m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }
    }
}
