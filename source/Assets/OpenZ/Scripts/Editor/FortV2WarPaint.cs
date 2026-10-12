using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// war paint in place. Copies the chosen variant's FortV2_Gun_Albedo.png (RGB + team mask in alpha) and
    /// FortV2_Gun_TeamMask.png from Models/Gen/FortV2/WarPaint~/&lt;variant&gt;/ (a "~" folder: Unity never imports it) over
    /// the live files and reimports them. Same file names and guids, so m01_fort_v2_gun_mat, Z_fort and the fracture pieces
    /// pick the new paint up with no other change. Default = A Desert Shark.
    /// </summary>
    public static class FortV2WarPaint
    {
        const string Dir = "Assets/OpenZ/Models/Gen/FortV2";
        static readonly string[] Files = { "FortV2_Gun_Albedo.png", "FortV2_Gun_TeamMask.png" };

        [MenuItem("OpenZ/Fort v2/Gun War Paint/A Desert Shark (default)")] static void A() => Apply("A_DesertShark");
        [MenuItem("OpenZ/Fort v2/Gun War Paint/B Raked Claw Slashes")] static void B() => Apply("B_ClawSlashes");
        [MenuItem("OpenZ/Fort v2/Gun War Paint/C War-Paint Strokes")] static void C() => Apply("C_WarPaintStrokes");
        [MenuItem("OpenZ/Fort v2/Gun War Paint/S Old Straight Stripes (rollback)")] static void S() => Apply("S_StraightStripes");

        public static string Apply(string variant)
        {
            string src = Dir + "/WarPaint~/" + variant;
            foreach (var f in Files)
                if (!File.Exists(src + "/" + f))
                {
                    string err = "[OpenZ] Fort v2 war paint: missing " + src + "/" + f;
                    Debug.LogError(err);
                    return err;
                }
            foreach (var f in Files)
            {
                File.Copy(src + "/" + f, Dir + "/" + f, true);
                AssetDatabase.ImportAsset(Dir + "/" + f, ImportAssetOptions.ForceUpdate);
            }
            AssetDatabase.SaveAssets();
            string msg = "[OpenZ] Fort v2 gun war paint -> " + variant + " (FortV2_Gun_Albedo + FortV2_Gun_TeamMask replaced in place, guids kept)";
            Debug.Log(msg);
            return msg;
        }
    }
}
