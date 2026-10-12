#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace OpenZ.EditorTools
{
    /// <summary>
    ///  1. OpenZ / UI Kit / 1 Import TMP Essential Resources: TextMeshPro's shaders and settings (silent import, no window).
    ///  2. OpenZ / UI Kit / 2 Build Inter Tight SDF: OZ_InterTight_SDF (SDFAA, sampling 90, padding 9, 2048 atlas, dynamic,
    ///     "TextMeshPro/Distance Field" shader for the underlay / glow presets) from the kit's Inter Tight ExtraBold, next to
    ///     it in Resources/OpenZ/Kit/Fonts, with the guide's static character set (UI_STYLE_GUIDE.md 3.2).
    ///     Resources/OpenZ/UI/PortraitsBS01, which the HUD reads before the older portraits.
    /// Without steps 1-2 the HUD still runs (legacy uGUI Text with the same Inter Tight file); without 3 it shows the staged
    /// portraits.
    /// </summary>
    public static class OzUiSetup
    {
        const string KitFonts = "Assets/OpenZ/Resources/OpenZ/Kit/Fonts/";
        const string FontPath = KitFonts + "InterTight-ExtraBold.ttf";
        const string AssetPath = KitFonts + "OZ_InterTight_SDF.asset";
        const string PortraitDir = "Assets/OpenZ/Resources/OpenZ/UI/PortraitsBS01";
        // ASCII 32-126 + the guide's symbols (all present in Inter Tight; no U+2715, use the multiplication sign)
        const string Extra = "·•×→←↑↓▲▼▶◀■●◆★✓↻–—…°©−";

        [MenuItem("OpenZ/UI Kit/1 Import TMP Essential Resources")]
        public static void ImportEssentials()
        {
            if (Resources.Load<TMP_Settings>("TMP Settings") != null) { Debug.Log("[OzUi] TMP Essential Resources are already in the project"); return; }
            TMP_PackageResourceImporter.ImportResources(true, false, false);
            Debug.Log("[OzUi] TMP Essential Resources imported (Assets/TextMesh Pro). Next: OpenZ / UI Kit / 2 Build Inter Tight SDF");
        }

        [MenuItem("OpenZ/UI Kit/2 Build Inter Tight SDF")]
        public static void BuildFontAsset()
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (!font) { Debug.LogError("[OzUi] " + FontPath + " not found: copy the UI kit into Resources/OpenZ/Kit first"); return; }
            if (Resources.Load<TMP_Settings>("TMP Settings") == null) { Debug.LogError("[OzUi] import the TMP Essential Resources first (OpenZ / UI Kit / 1)"); return; }
            if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath)) AssetDatabase.DeleteAsset(AssetPath);
            var fa = TMP_FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, false);
            if (!fa) { Debug.LogError("[OzUi] TMP could not read the font (Include Font Data must be on)"); return; }
            fa.name = "OZ_InterTight_SDF";
            var full = Shader.Find("TextMeshPro/Distance Field");
            if (full) fa.material.shader = full;
            AssetDatabase.CreateAsset(fa, AssetPath);
            var tex = fa.atlasTextures[0]; tex.name = "OZ_InterTight_SDF Atlas"; AssetDatabase.AddObjectToAsset(tex, fa);
            fa.material.name = "OZ_InterTight_SDF Material"; AssetDatabase.AddObjectToAsset(fa.material, fa);
            var chars = new System.Text.StringBuilder();
            for (int c = 32; c <= 126; c++) chars.Append((char)c);
            chars.Append(Extra);
            fa.TryAddCharacters(chars.ToString(), out string missing);
            EditorUtility.SetDirty(fa); EditorUtility.SetDirty(tex);
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            Debug.Log($"[OzUi] {AssetPath} built ({fa.characterTable.Count} characters{(string.IsNullOrEmpty(missing) ? "" : ", missing: " + missing)})");
        }

        [MenuItem("OpenZ/UI Kit/3 Import BS-01 Portraits")]
        public static void ImportPortraits()
        {
            string job = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
            string[] sources =
            {
                Path.Combine(job, "Materials", "ui", "portraits_bs01", "512"), Path.Combine(job, "Materials", "ui", "portraits_bs01"),
                Path.Combine(job, "projects", "blender", "export", "bs01", "portraits", "512"), Path.Combine(job, "projects", "blender", "export", "bs01", "portraits"),
                Path.Combine(job, "projects", "blender", "export", "bs01"),
            };
            Directory.CreateDirectory(PortraitDir);
            int n = 0; string used = null;
            foreach (var src in sources)
            {
                if (!Directory.Exists(src)) continue;
                var files = Directory.GetFiles(src, "portrait_*.png");
                if (files.Length == 0) continue;
                foreach (var f in files) { File.Copy(f, Path.Combine(PortraitDir, Path.GetFileName(f)), true); n++; }
                used = src; break;
            }
            foreach (var src in new[] { Path.Combine(job, "Materials", "ui", "portraits_art", "512"), Path.Combine(job, "Materials", "ui", "portraits_art") })
            {
                if (!Directory.Exists(src)) continue;
                var files = Directory.GetFiles(src, "portrait_laser_*.png");
                foreach (var f in files) { File.Copy(f, Path.Combine(PortraitDir, Path.GetFileName(f)), true); n++; }
                if (files.Length > 0) break;
            }
            AssetDatabase.Refresh();
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PortraitDir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (!(AssetImporter.GetAtPath(p) is TextureImporter ti)) continue;
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = true; ti.alphaIsTransparency = true; ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.maxTextureSize = 512; ti.textureCompression = TextureImporterCompression.CompressedHQ;
                ti.SaveAndReimport();
            }
            Debug.Log(n > 0 ? $"[OzUi] {n} portraits copied to {PortraitDir} (BS-01 from {used ?? "nowhere"}); the HUD uses them before Resources/OpenZ/UI/Portraits"
                            : "[OzUi] no BS-01 portraits found yet: the HUD keeps the staged portraits");
        }
    }
}
#endif
