using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Heavy tank look from the assets of our earlier projects (OpenZ/Look menu): <b>Apply</b> puts the layout albedos
    /// (Models/Legacy/Toy/heavy_tank_*_legacy.png: black-grey metal, khaki armour, team accents, skulls) on the three heavy tank
    /// materials and switches TeamLit's _LegacyHeavy path on (out of the generic PaintKhaki remap and the generic team tint). <b>Revert</b> goes back to the toy paint.
    /// </summary>
    public static class OpenZHeavyLegacyLook
    {
        const string Mat = "Assets/OpenZ/Materials/Units/";
        const string Tex = "Assets/OpenZ/Models/Legacy/Toy/";
        static readonly (string mat, string legacy, string toy)[] Parts =
        {
            ("heavy_tank_HeavyTank_body_PBR", "heavy_tank_body_legacy.png", "heavy_tank_body_toy.png"),
            ("heavy_tank_HeavyTank_head_PBR", "heavy_tank_head_legacy.png", "heavy_tank_head_toy.png"),
            ("heavy_tank_HeavyTank_detail", "swatch_heavy_gunmetal.png", "swatch_khaki_deep.png"),
        };
        /// <summary>x khaki reference luminance (linear, the albedo's khaki 134,106,64), y empty panel grey (linear), z empty metal
        /// gain, w team strength.</summary>
        public static Vector4 Look = new Vector4(0.1575f, 0.42f, 0.8f, 1f);

        [MenuItem("OpenZ/Look/Heavy tank shooter-project look: Apply")]
        public static string Apply() => Set(true);

        [MenuItem("OpenZ/Look/Heavy tank shooter-project look: Revert")]
        public static string Revert() => Set(false);

        static string Set(bool on)
        {
            int n = 0;
            foreach (var p in Parts)
            {
                string tp = Tex + (on ? p.legacy : p.toy);
                Import(tp);
                var t = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                var m = AssetDatabase.LoadAssetAtPath<Material>(Mat + p.mat + ".mat");
                if (!t || !m) { Debug.LogError("[heavy_legacy] missing " + (t ? Mat + p.mat : tp)); continue; }
                m.SetTexture("_BaseMap", t);
                m.SetFloat("_LegacyHeavy", on ? 1f : 0f);
                m.SetVector("_HeavyLook", Look);
                EditorUtility.SetDirty(m); n++;
            }
            AssetDatabase.SaveAssets();
            string r = "[heavy_legacy] " + (on ? "APPLY" : "REVERT") + " " + n + "/3 materials";
            Debug.Log(r);
            return r;
        }

        static void Import(string path)
        {
            var ti = AssetImporter.GetAtPath(path) as TextureImporter;
            if (!ti) return;
            bool sw = path.Contains("swatch_");
            ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true;
            ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = false;   // alpha = paint / team mask (data)
            ti.mipmapEnabled = true; ti.mipmapFilter = TextureImporterMipFilter.BoxFilter;
            ti.anisoLevel = sw ? 0 : 8; ti.filterMode = sw ? FilterMode.Point : FilterMode.Trilinear;
            ti.wrapMode = TextureWrapMode.Repeat; ti.maxTextureSize = 2048;
            ti.textureCompression = TextureImporterCompression.CompressedHQ;   // BC7
            ti.SaveAndReimport();
        }
    }
}
