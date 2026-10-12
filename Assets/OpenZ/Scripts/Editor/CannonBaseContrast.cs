using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// missile cannon) are split into a static "Base" (sandbag ring + plinth) and the rotating gun (OpenZTurretSplit), but every
    /// part shares ONE TeamLit material, so the tan canvas (clean albedo ~#91764C) and the khaki neutral paint of the gun
    /// (#A17B41 rendered) sit at the same value and hue. This gives the Base renderer its own copy of the cannon material:
    ///   - _BaseColor tint (0.74, 0.79, 0.66): the sandbags ~25 % darker and a touch greener (olive-drab canvas, sRGB ~#6B5D32),
    ///   - _Degreen 0 on the copy only, so TeamLit's "no green team" pass leaves that olive-drab canvas alone (the gun keeps it),
    ///   - _NeutralPaint (0.645, 0.642, 0.713): the few painted plinth / base-plate texels under the gun read as a dark gunmetal
    ///     plate (renders ~#484C50: TeamLit reads _NeutralPaint through SRGBToLinear again and multiplies by _BaseColor, so the
    ///     value is (target / tint)^(1/2.2) per channel), which also draws a dark contact line where the gun collar meets its base.
    /// The gun material is not touched (OpenZUnitLookV2 owns its values). Materials/Units/&lt;cannon&gt;_base.mat is re-derived
    /// from the gun material on every run (CopyPropertiesFromMaterial + the three overrides), so later look passes on the gun
    /// material carry over. Scene instances follow the prefabs. Revert puts the shared material back.
    /// </summary>
    public static class CannonBaseContrast
    {
        const string MatDir = "Assets/OpenZ/Materials/Units", PrefabDir = "Assets/OpenZ/Prefabs";
        public static readonly Color BaseTint = new Color(0.74f, 0.79f, 0.66f, 1f);
        public static readonly Color BasePlate = new Color(0.645f, 0.642f, 0.713f, 1f);          // renders ~(72, 76, 80) sRGB through the tint
        static readonly (string prefab, string mat)[] Cannons =
        {
            ("Z_gatling", "m09_gatling_mat"), ("Z_howitzer", "m10_howitzer_mat"), ("Z_missile_cannon", "m11_missile_cannon_mat"),
        };

        [MenuItem("OpenZ/Cannons/Base Contrast (sandbags darker than the gun)")]
        public static string Apply() => Run(false);

        [MenuItem("OpenZ/Cannons/Base Contrast - Revert")]
        public static string Revert() => Run(true);

        static string Run(bool revert)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[OpenZ] CannonBaseContrast: stop Play first"); return "stop Play first"; }
            var log = new StringBuilder();
            foreach (var (prefabName, matName) in Cannons)
            {
                string pPath = $"{PrefabDir}/{prefabName}.prefab";
                var src = AssetDatabase.LoadAssetAtPath<Material>($"{MatDir}/{matName}.mat");
                if (!src) { log.Append($"{prefabName}: {matName} missing; "); continue; }
                Material use = src;
                if (!revert)
                {
                    string bPath = $"{MatDir}/{matName.Replace("_mat", "")}_base.mat";
                    var bm = AssetDatabase.LoadAssetAtPath<Material>(bPath);
                    if (!bm) { bm = new Material(src) { name = System.IO.Path.GetFileNameWithoutExtension(bPath) }; AssetDatabase.CreateAsset(bm, bPath); }
                    else { bm.shader = src.shader; bm.CopyPropertiesFromMaterial(src); }
                    bm.shaderKeywords = src.shaderKeywords;
                    bm.enableInstancing = src.enableInstancing;
                    if (bm.HasProperty("_BaseColor")) bm.SetColor("_BaseColor", BaseTint);
                    if (bm.HasProperty("_Degreen")) bm.SetFloat("_Degreen", 0f);
                    if (bm.HasProperty("_NeutralPaint")) bm.SetColor("_NeutralPaint", BasePlate);
                    EditorUtility.SetDirty(bm);
                    use = bm;
                }
                var root = PrefabUtility.LoadPrefabContents(pPath);
                try
                {
                    Transform b = null;
                    foreach (var t in root.GetComponentsInChildren<Transform>(true)) if (t.name == "Base" && t.GetComponent<MeshRenderer>()) { b = t; break; }
                    if (!b) { log.Append($"{prefabName}: no Base renderer (run OpenZ/Split Cannon Turrets first); "); continue; }
                    var r = b.GetComponent<MeshRenderer>();
                    var mats = r.sharedMaterials;
                    int n = 0;
                    for (int i = 0; i < mats.Length; i++)
                        if (mats[i] == src || (mats[i] && mats[i].name == matName.Replace("_mat", "") + "_base")) { mats[i] = use; n++; }
                    r.sharedMaterials = mats;
                    PrefabUtility.SaveAsPrefabAsset(root, pPath);
                    log.Append($"{prefabName}: Base -> {use.name} ({n} slot(s)); ");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            string msg = $"[OpenZ] CannonBaseContrast {(revert ? "reverted" : "applied")}: {log}";
            Debug.Log(msg);
            return msg;
        }
    }
}
