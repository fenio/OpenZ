#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// texture, sRGB, alpha none, NPOT kept, NO mip maps (the atan2 u-wrap would pick the smallest mip on a 1-px column = the classic
    /// panorama seam line), wrap U Repeat / V Clamp, Bilinear, max size 8192 for the _8k maps (4096 for the fallback), BC7 on
    /// Standalone (32 MiB per 8k map, one level resident at a time, only during the flyby), not readable.
    /// Menu: OpenZ / Flyby Sky / Import + Check: reimports the maps with this rule, makes Resources/OpenZ/Sky/M_FlybySky.mat (keeps the
    /// OpenZ/FlybySky shader in the player build) and logs one report line per map (size, format, mips, wrap, memory) + the json.
    /// </summary>
    public class OpenZFlybySkyImport : AssetPostprocessor
    {
        public const string Folder = "Assets/OpenZ/Resources/OpenZ/Sky/";
        public const string MatPath = Folder + "M_FlybySky.mat";
        static readonly string[] Maps = { "oz_sky_L02_desert_sunrise_8k.png", "oz_sky_L17_harbour_dawn_8k.png", "oz_sky_L02_desert_sunrise.png", "oz_sky_L17_harbour_dawn.png" };

        public override int GetPostprocessOrder() => 60;

        static bool Mine(string path)
        {
            path = path.Replace('\\', '/');
            return path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        void OnPreprocessTexture()
        {
            if (!Mine(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            int max = assetPath.EndsWith("_8k.png", StringComparison.OrdinalIgnoreCase) ? 8192 : 4096;
            ti.textureType = TextureImporterType.Default; ti.textureShape = TextureImporterShape.Texture2D;
            ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None; ti.alphaIsTransparency = false;
            ti.npotScale = TextureImporterNPOTScale.None; ti.mipmapEnabled = false; ti.streamingMipmaps = false; ti.isReadable = false;
            ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.anisoLevel = 0;
            ti.maxTextureSize = max; ti.textureCompression = TextureImporterCompression.CompressedHQ;
            var ps = ti.GetPlatformTextureSettings("Standalone");
            ps.overridden = true; ps.maxTextureSize = max; ps.format = TextureImporterFormat.BC7; ps.textureCompression = TextureImporterCompression.CompressedHQ;
            ti.SetPlatformTextureSettings(ps);
        }

        [MenuItem("OpenZ/Flyby Sky/Import + Check")]
        public static string ImportAndCheck()
        {
            var log = new StringBuilder();
            int ok = 0, found = 0;
            foreach (var f in Maps)
            {
                string p = Folder + f;
                if (!File.Exists(p)) { if (f.Contains("_8k")) log.AppendLine($"MISSING {p} (run the package's apply.py --apply first)"); continue; }
                found++;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate);
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                var ps = ti ? ti.GetPlatformTextureSettings("Standalone") : null;
                bool good = ti && tex && !ti.mipmapEnabled && ti.sRGBTexture && ti.wrapModeU == TextureWrapMode.Repeat && ti.wrapModeV == TextureWrapMode.Clamp
                            && ps != null && ps.overridden && ps.format == TextureImporterFormat.BC7 && tex.width == (f.Contains("_8k") ? 8192 : 4096);
                if (good) ok++;
                long mem = tex ? UnityEngine.Profiling.Profiler.GetRuntimeMemorySizeLong(tex) : 0;
                log.AppendLine($"{(good ? "OK  " : "OFF ")} {f}: {(tex ? tex.width + "x" + tex.height + " " + tex.format + " mips " + tex.mipmapCount : "not loaded")}, "
                             + $"wrap {(ti ? ti.wrapModeU + "/" + ti.wrapModeV : "-")}, sRGB {(ti && ti.sRGBTexture)}, Standalone {(ps != null && ps.overridden ? ps.format + " max " + ps.maxTextureSize : "not overridden")}, "
                             + $"editor memory {mem / 1048576f:F1} MiB");
            }
            var json = AssetDatabase.LoadAssetAtPath<TextAsset>(Folder + "sky_panoramas.json");
            var e2 = FlybySky.Entry("L02"); var e17 = FlybySky.Entry("L17");
            log.AppendLine(json ? $"OK   sky_panoramas.json: L02 {(e2 != null ? e2.textureHi : "MISSING")}, L17 {(e17 != null ? e17.textureHi : "MISSING")}" : "MISSING sky_panoramas.json");

            var sh = Shader.Find(FlybySky.ShaderName);
            if (!sh) log.AppendLine("MISSING shader " + FlybySky.ShaderName + " (did the package's Shaders/FlybySky.shader import?)");
            else
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
                if (!mat) { mat = new Material(sh) { name = "M_FlybySky" }; AssetDatabase.CreateAsset(mat, MatPath); }
                else if (mat.shader != sh) { mat.shader = sh; EditorUtility.SetDirty(mat); }
                // NO texture on the asset: a map referenced here would load with the material (32 MiB, both levels). FlybySky sets the
                // level's map on its runtime copy and unloads it after the fade.
                mat.SetTexture("_FlybySkyTex", null);
                EditorUtility.SetDirty(mat); AssetDatabase.SaveAssets();
                log.AppendLine($"OK   {MatPath} (shader {sh.name}, supported {sh.isSupported})");
            }
            string head = $"[OpenZ FlybySky] import + check: {ok}/{found} maps on the rule" + (ok == found && found >= 2 && json && sh ? " - READY" : " - CHECK THE LINES BELOW");
            var s = head + "\n" + log;
            Debug.Log(s);
            return s;
        }
    }
}
#endif
