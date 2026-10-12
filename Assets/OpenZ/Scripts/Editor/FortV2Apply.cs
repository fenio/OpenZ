using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// renderers of Z_fort.prefab (FortBase -> m01_fort_v2_base_mat, FortGunYaw / FortGunPitch / FortGunRecoil ->
    /// m01_fort_v2_gun_mat) and saves the prefab. Run OpenZ/Bake Building Fractures afterwards (the fracture pieces copy
    /// FortBase's materials, and FortBase gained the roof details).
    /// </summary>
    public static class FortV2Apply
    {
        const string PrefabPath = "Assets/OpenZ/Prefabs/Z_fort.prefab";
        const string BaseMatPath = "Assets/OpenZ/Materials/Units/m01_fort_v2_base_mat.mat";
        const string GunMatPath = "Assets/OpenZ/Materials/Units/m01_fort_v2_gun_mat.mat";
        static readonly string[] GunParts = { "FortGunYaw", "FortGunPitch", "FortGunRecoil" };

        [MenuItem("OpenZ/Fort v2/Apply Materials to Z_fort")]
        public static string Apply()
        {
            var baseMat = AssetDatabase.LoadAssetAtPath<Material>(BaseMatPath);
            var gunMat = AssetDatabase.LoadAssetAtPath<Material>(GunMatPath);
            if (!baseMat || !gunMat)
            {
                string err = "[OpenZ] Fort v2: materials missing (" + BaseMatPath + ", " + GunMatPath + ")";
                Debug.LogError(err);
                return err;
            }
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            int done = 0;
            try
            {
                foreach (var r in root.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material m = r.name == "FortBase" ? baseMat : GunParts.Contains(r.name) ? gunMat : null;
                    if (!m) continue;
                    r.sharedMaterials = Enumerable.Repeat(m, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
                    done++;
                }
                if (done != 4)
                    Debug.LogWarning("[OpenZ] Fort v2: expected 4 split fort renderers in Z_fort, found " + done +
                                     " (run OpenZ/Fort Gun/2 Swap Fort Mesh in Z_fort first)");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            AssetDatabase.SaveAssets();
            string msg = "[OpenZ] Fort v2: " + done + " renderers re-materialed in Z_fort. Next: OpenZ/Bake Building Fractures";
            Debug.Log(msg);
            return msg;
        }
    }
}
