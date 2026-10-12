#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// One import rule for every texture the
    /// UI draws, applied on every (re)import AFTER OzKitImporter (GetPostprocessOrder 50 > 0), so kit.json keeps its sprite type,
    /// PPU, pivots and 9-slice borders and this only adds the quality settings:
    ///  - full source size: maxTextureSize = the next power of two at or above the PNG's own size (never downscaled), NPOT kept,
    ///    ignoreMipmapLimit on (a global texture quality limit never halves the UI), no mip streaming;
    ///  - mip maps ON (Kaiser; Box for the 256 px HD cursors, drawn at exactly 1/4, 1/2 ...), Trilinear, anisotropic, a small
    ///    negative mip bias for sharpness: portraits / icons shown scaled down stop shimmering and aliasing ("noisy, pixelated");
    ///  - no Point filter anywhere (the kit's static noise was Point);
    ///  - compression: kit sprites, icons and cursors uncompressed (RGBA32, as the kit ships), portraits and menu posters BC7 at
    ///    the best quality (visually lossless; RGB paintings), Standalone override set to match; sRGB on, alpha is transparency.
    /// Hardware cursor textures (Kit/Cursors/cur_*, Texture Type Cursor, Read/Write for OzKit.Green) keep their type and settings
    /// (only a Point filter would be lifted). New kit-less files get the kit's sprite set-up: Kit/Icons/ic_*_192 = Sprite, 400 PPU
    /// (the same 48 u native size as ic_*_96 at 200 PPU); Kit/Cursors/HD = Default (RawImage, OzSoftCursor).
    /// Menus: OpenZ / UI / Apply UI Image Quality (fixes every UI texture in bulk, reimports only the ones that change, writes
    /// </summary>
    public class OzUiImageQuality : AssetPostprocessor
    {
        public const string Res = "Assets/OpenZ/Resources/OpenZ/";
        /// <summary>Folders the UI loads textures from (Resources paths under Assets/OpenZ/Resources/OpenZ/).</summary>
        public static readonly string[] Roots = { Res + "Kit", Res + "UI", Res + "Menu" };
        public const float PortraitBias = -0.3f, SpriteBias = -0.25f;
        public const int PortraitAniso = 8, SpriteAniso = 4;
        /// <summary>true = portraits uncompressed too (RGBA32, ~1.4 MB each at 512, 5.6 MB at 1024) instead of BC7 best.</summary>
        public static bool PortraitsUncompressed = false;

        enum Kind { None, KitSprite, HwCursor, HdCursor, HdIcon, Portrait, Icon }

        public override int GetPostprocessOrder() => 50;                                               // after OzKitImporter (0)

        static Kind Classify(string path)
        {
            path = path.Replace('\\', '/');
            if (!path.StartsWith(Res, StringComparison.OrdinalIgnoreCase)) return Kind.None;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext != ".png" && ext != ".jpg" && ext != ".jpeg" && ext != ".tga") return Kind.None;
            var rel = path.Substring(Res.Length);
            if (rel.StartsWith("Kit/Cursors/HD/", StringComparison.OrdinalIgnoreCase)) return Kind.HdCursor;
            if (rel.StartsWith("Kit/Cursors/cur_", StringComparison.OrdinalIgnoreCase)) return Kind.HwCursor;
            if (rel.StartsWith("Kit/Icons/", StringComparison.OrdinalIgnoreCase) && rel.EndsWith("_192.png", StringComparison.OrdinalIgnoreCase)) return Kind.HdIcon;
            if (rel.StartsWith("Kit/", StringComparison.OrdinalIgnoreCase)) return Kind.KitSprite;
            if (rel.StartsWith("UI/Icons", StringComparison.OrdinalIgnoreCase)) return Kind.Icon;
            if (rel.StartsWith("UI/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("Menu/", StringComparison.OrdinalIgnoreCase)) return Kind.Portrait;
            return Kind.None;
        }

        /// <summary>Source pixel size from the file header (PNG IHDR / JPEG SOF), so the rule works before the first import.</summary>
        public static Vector2Int SourceSize(string assetPath)
        {
            try
            {
                var full = Path.GetFullPath(assetPath);
                using (var f = File.OpenRead(full))
                {
                    var h = new byte[32]; int n = f.Read(h, 0, 32);
                    if (n >= 24 && h[0] == 0x89 && h[1] == (byte)'P' && h[2] == (byte)'N' && h[3] == (byte)'G')
                        return new Vector2Int((h[16] << 24) | (h[17] << 16) | (h[18] << 8) | h[19], (h[20] << 24) | (h[21] << 16) | (h[22] << 8) | h[23]);
                    if (n >= 2 && h[0] == 0xFF && h[1] == 0xD8)
                    {
                        f.Position = 2;
                        while (f.Position < f.Length)
                        {
                            if (f.ReadByte() != 0xFF) continue;
                            int m = f.ReadByte(); if (m == 0xD8 || m == 0x01 || (m >= 0xD0 && m <= 0xD7)) continue;
                            int len = (f.ReadByte() << 8) | f.ReadByte();
                            if (m >= 0xC0 && m <= 0xC3) { f.ReadByte(); int hh = (f.ReadByte() << 8) | f.ReadByte(); int ww = (f.ReadByte() << 8) | f.ReadByte(); return new Vector2Int(ww, hh); }
                            f.Position += len - 2;
                        }
                    }
                }
            }
            catch (Exception) { }
            return new Vector2Int(0, 0);
        }

        static int MaxSizeFor(Vector2Int s)
        {
            int big = Mathf.Max(s.x, s.y), m = 32;
            if (big <= 0) return 2048;
            while (m < big && m < 16384) m *= 2;
            return m;
        }

        void OnPreprocessTexture()
        {
            var k = Classify(assetPath); if (k == Kind.None) return;
            Apply((TextureImporter)assetImporter, assetPath, k, false, out _);
        }

        /// <summary>Set the rule on the importer (no reimport; dry = only compare). Returns true when anything differs; `what` lists it.</summary>
        static bool Apply(TextureImporter ti, string path, Kind k, bool dry, out string what)
        {
            var sb = new StringBuilder();
            bool ch = false;
            void Set<T>(string name, T cur, T want, Action<T> set) { if (!EqualityComparer<T>.Default.Equals(cur, want)) { if (!dry) set(want); ch = true; sb.Append(name).Append(' ').Append(cur).Append("->").Append(want).Append("; "); } }

            if (k == Kind.HwCursor)
            {   // hardware set: OzKit.SetHardwareCursor / Green need it exactly as the kit imports it; only Point is lifted
                if (ti.filterMode == FilterMode.Point) Set("filter", ti.filterMode, FilterMode.Bilinear, v => ti.filterMode = v);
                what = sb.ToString(); return ch;
            }
            var src = SourceSize(path);
            int max = MaxSizeFor(src);
            bool portrait = k == Kind.Portrait;
            bool compress = portrait && !PortraitsUncompressed;

            if (k == Kind.HdCursor) Set("type", ti.textureType, TextureImporterType.Default, v => ti.textureType = v);
            if (k == Kind.HdIcon)
            {
                Set("type", ti.textureType, TextureImporterType.Sprite, v => ti.textureType = v);
                Set("spriteMode", ti.spriteImportMode, SpriteImportMode.Single, v => ti.spriteImportMode = v);
                Set("ppu", ti.spritePixelsPerUnit, 400f, v => ti.spritePixelsPerUnit = v);
                var st = new TextureImporterSettings(); ti.ReadTextureSettings(st);
                if (st.spriteMeshType != SpriteMeshType.FullRect || st.spriteGenerateFallbackPhysicsShape)
                { st.spriteMeshType = SpriteMeshType.FullRect; st.spriteGenerateFallbackPhysicsShape = false; if (!dry) ti.SetTextureSettings(st); ch = true; sb.Append("sprite mesh FullRect; "); }
            }
            Set("sRGB", ti.sRGBTexture, true, v => ti.sRGBTexture = v);
            Set("alphaIsTransparency", ti.alphaIsTransparency, true, v => ti.alphaIsTransparency = v);
            Set("npot", ti.npotScale, TextureImporterNPOTScale.None, v => ti.npotScale = v);
            Set("mipmaps", ti.mipmapEnabled, true, v => ti.mipmapEnabled = v);
            Set("mipFilter", ti.mipmapFilter, k == Kind.HdCursor ? TextureImporterMipFilter.BoxFilter : TextureImporterMipFilter.KaiserFilter, v => ti.mipmapFilter = v);
            Set("mipCoverage", ti.mipMapsPreserveCoverage, false, v => ti.mipMapsPreserveCoverage = v);
            Set("borderMip", ti.borderMipmap, false, v => ti.borderMipmap = v);
            Set("fadeout", ti.fadeout, false, v => ti.fadeout = v);
            Set("streaming", ti.streamingMipmaps, false, v => ti.streamingMipmaps = v);
            Set("ignoreMipLimit", ti.ignoreMipmapLimit, true, v => ti.ignoreMipmapLimit = v);
            Set("filter", ti.filterMode, FilterMode.Trilinear, v => ti.filterMode = v);
            Set("aniso", ti.anisoLevel, k == Kind.HdCursor ? 1 : portrait || k == Kind.Icon ? PortraitAniso : SpriteAniso, v => ti.anisoLevel = v);
            float bias = k == Kind.HdCursor ? 0f : portrait || k == Kind.Icon ? PortraitBias : SpriteBias;
            if (Mathf.Abs(ti.mipMapBias - bias) > 1e-4f) { if (!dry) ti.mipMapBias = bias; ch = true; sb.Append("mipBias->").Append(bias).Append("; "); }
            if (k == Kind.HdCursor || k == Kind.HdIcon || portrait || k == Kind.Icon)
                Set("wrap", ti.wrapMode, TextureWrapMode.Clamp, v => ti.wrapMode = v);
            Set("readable", ti.isReadable, false, v => ti.isReadable = v);
            Set("maxSize", ti.maxTextureSize, max, v => ti.maxTextureSize = v);
            Set("compression", ti.textureCompression, compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed, v => ti.textureCompression = v);
            if (compress) Set("quality", ti.compressionQuality, 100, v => ti.compressionQuality = v);

            // Standalone: an override (the portraits had 512 / CompressedHQ) must match; portraits get an explicit BC7 best override
            var ps = ti.GetPlatformTextureSettings("Standalone");
            if (compress || ps.overridden)
            {
                var want = compress ? TextureImporterFormat.BC7 : TextureImporterFormat.RGBA32;
                if (!ps.overridden || ps.maxTextureSize != max || ps.format != want || ps.compressionQuality != 100)
                {
                    sb.Append("Standalone ").Append(ps.overridden ? ps.maxTextureSize + "/" + ps.format : "default").Append("->").Append(max).Append('/').Append(want).Append("; ");
                    ps.overridden = true; ps.maxTextureSize = max; ps.format = want; ps.compressionQuality = 100;
                    ps.textureCompression = compress ? TextureImporterCompression.CompressedHQ : TextureImporterCompression.Uncompressed;
                    if (!dry) ti.SetPlatformTextureSettings(ps);
                    ch = true;
                }
            }
            what = sb.ToString();
            return ch;
        }

        static List<string> AllUiTextures()
        {
            var list = new List<string>();
            var roots = new List<string>();
            foreach (var r in Roots) if (AssetDatabase.IsValidFolder(r)) roots.Add(r);
            if (roots.Count == 0) return list;
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", roots.ToArray()))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Classify(p) != Kind.None && AssetImporter.GetAtPath(p) is TextureImporter) list.Add(p);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        [MenuItem("OpenZ/UI/Apply UI Image Quality")]
        public static void ApplyAll() => Run(true);

        [MenuItem("OpenZ/UI/UI Image Quality Report")]
        public static void ReportOnly() => Run(false);

        static void Run(bool fix)
        {
            var paths = AllUiTextures();
            var changed = new List<(string path, string what)>();
            var counts = new Dictionary<Kind, int>();
            foreach (var p in paths)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p); var k = Classify(p);
                counts[k] = (counts.TryGetValue(k, out var c) ? c : 0) + 1;
                if (Apply(ti, p, k, true, out var what)) changed.Add((p, what));
            }
            if (fix && changed.Count > 0)
            {
                try
                {
                    AssetDatabase.StartAssetEditing();
                    for (int i = 0; i < changed.Count; i++)
                    {
                        if (EditorUtility.DisplayCancelableProgressBar("OpenZ UI image quality", changed[i].path, (i + 1f) / changed.Count)) break;
                        var ti = (TextureImporter)AssetImporter.GetAtPath(changed[i].path);
                        Apply(ti, changed[i].path, Classify(changed[i].path), false, out _);
                        ti.SaveAndReimport();
                    }
                }
                finally { AssetDatabase.StopAssetEditing(); EditorUtility.ClearProgressBar(); }
                AssetDatabase.SaveAssets();
            }

            var sb = new StringBuilder();
            sb.AppendLine("# OpenZ UI image quality (" + (fix ? "applied" : "report only") + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
            sb.AppendLine();
            sb.AppendLine("Rule: OzUiImageQuality (ui_quality_v1). " + paths.Count + " UI textures; " + changed.Count + (fix ? " re-imported." : " would change."));
            sb.AppendLine();
            foreach (var kv in counts) sb.AppendLine("- " + kv.Key + ": " + kv.Value);
            sb.AppendLine();
            if (changed.Count > 0)
            {
                sb.AppendLine("| Texture | Changes |"); sb.AppendLine("|---|---|");
                foreach (var (p, w) in changed) sb.AppendLine("| " + p.Substring(Res.Length) + " | " + w.Replace("|", "/") + " |");
            }
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
            var outPath = Path.Combine(root, "work", "records", fix ? "ui_image_quality.md" : "ui_image_quality_report.md");
            try { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); File.WriteAllText(outPath, sb.ToString()); } catch (Exception e) { Debug.LogWarning("[OzUiImageQuality] " + e.Message); }
            Debug.Log("[OzUiImageQuality] " + (fix ? "applied: " : "report: ") + paths.Count + " UI textures, " + changed.Count + (fix ? " re-imported" : " off-rule") + " -> " + outPath);
        }
    }
}
#endif
