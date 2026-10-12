using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// exact local transform (-0.009206772, 6.073964, 0.0055036545) x 10.53449, gets the existing fort material
    /// (m01_fort_mat) on every part, FortBase first (OpenZFracture.Bake uses the first MeshFilter), and the swap is refused
    /// unless the renderer bounds before and after match to 2 cm. FortGunView (runtime) then finds the gun nodes by name.
    /// </summary>
    public static class FortGunSwap
    {
        const string PrefabPath = "Assets/OpenZ/Prefabs/Z_fort.prefab";
        const string MatPath = "Assets/OpenZ/Materials/Units/m01_fort_mat.mat";
        static readonly string[] ModelPaths = { "Assets/OpenZ/Models/Gen/Fort_Split.fbx", "Assets/OpenZ/Models/Gen/Fort_Split.glb" };

        /// <summary>FBX import settings: metre file units kept (Blender wrote UnitScaleFactor 100 = 1 m), no axis baking (the file
        /// is already Y-up with every node unrotated), the Blender custom normals imported as they are, meshes readable
        /// (FlagView cuts the baked flag out of FortBase at run time and needs mesh.isReadable), no materials/animation/rig.</summary>
        [MenuItem("OpenZ/Fort Gun/1 Import Settings (Fort_Split.fbx)")]
        public static string ConfigureImporter()
        {
            var mi = AssetImporter.GetAtPath(ModelPaths[0]) as ModelImporter;
            if (!mi) return "Fort_Split.fbx not found under Assets/OpenZ/Models/Gen (the .glb twin keeps the glTFast settings of m01_fort.glb)";
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = false;
            mi.isReadable = true;
            mi.importNormals = ModelImporterNormals.Import; mi.importTangents = ModelImporterTangents.CalculateMikk;
            mi.importBlendShapes = false; mi.importCameras = false; mi.importLights = false; mi.importVisibility = false;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.importAnimation = false; mi.animationType = ModelImporterAnimationType.None;
            mi.SaveAndReimport();
            return "Fort_Split.fbx import settings applied";
        }

        [MenuItem("OpenZ/Fort Gun/2 Swap Fort Mesh in Z_fort")]
        public static string Swap()
        {
            string path = ModelPaths.FirstOrDefault(p => AssetDatabase.LoadAssetAtPath<GameObject>(p));
            if (path == null) return "no Fort_Split model under Assets/OpenZ/Models/Gen";
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var old = root.transform.Find("Model");
                if (!old) return "Z_fort has no Model child";
                if (old.GetComponentsInChildren<Transform>(true).Any(t => t.name == "FortGunYaw")) return "Z_fort already carries the split fort";
                var before = RendererBounds(old.gameObject);
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(asset);
                PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                inst.name = "Model";
                inst.transform.SetParent(root.transform, false);
                inst.transform.SetLocalPositionAndRotation(old.localPosition, old.localRotation);
                inst.transform.localScale = old.localScale;
                inst.transform.SetSiblingIndex(old.GetSiblingIndex());
                var oldR = old.GetComponentInChildren<Renderer>();
                var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
                if (!mat && oldR) mat = oldR.sharedMaterial;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                {
                    if (mat) r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                    r.shadowCastingMode = ShadowCastingMode.On; r.receiveShadows = true;
                }
                var fb = inst.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "FortBase");
                if (fb) fb.SetAsFirstSibling();
                var after = RendererBounds(inst);
                string cmp = $"bounds before {before.center} {before.size}, after {after.center} {after.size}";
                if ((before.center - after.center).magnitude > 0.02f || (before.size - after.size).magnitude > 0.02f)
                {
                    Object.DestroyImmediate(inst);
                    return "NOT swapped, the split fort does not sit where the old one did (" + cmp + "): check the import settings";
                }
                Object.DestroyImmediate(old.gameObject);
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                AssetDatabase.SaveAssets();
                string msg = $"Z_fort: Model -> {System.IO.Path.GetFileName(path)} ({inst.GetComponentsInChildren<Renderer>(true).Length} renderers, material {(mat ? mat.name : "none")}), {cmp}. Next: OpenZ/Bake Building Fractures";
                Debug.Log("[OpenZ] " + msg);
                return msg;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static Bounds RendererBounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
        }
    }
}
