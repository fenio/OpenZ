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
    /// production_badge_v2: the import rule for the production badge art (Assets/OpenZ/Resources/OpenZ/ProductionBadge/x1..x4, baked by
    /// or slightly minified, never magnified, so the rule is the crisp one: Default texture (OzBadgeArt builds the sprites), NPOT kept
    /// (never rescaled), NO mip maps, Bilinear, Clamp, uncompressed RGBA32 (Standalone override too: no block artefacts on 1 u keylines),
    /// sRGB, alpha is transparency, not readable, no mip streaming, max size = the next power of two at or above the file.
    /// The folder is outside OzUiImageQuality's roots (Kit / UI / Menu), so the two rules never fight. The shipped .meta files already
    /// carry these settings; this postprocessor keeps them on every reimport.
    /// Menus: OpenZ / UI / Production Badge / Reimport Art (fix + reimport what is off-rule) and / Art Report (check only; also checks
    /// every texture's pixel size against the layout: u size from OzLayout x tier, and the face-drift check: the md5 of the live card
    /// art every baked face came from, OzProductionBadgeSources.txt, against the files in Resources/OpenZ/UI today; a new portrait or
    /// </summary>
    public class OzProductionBadgeImporter : AssetPostprocessor
    {
        public const string Folder = "Assets/OpenZ/Resources/OpenZ/ProductionBadge/";

        public override int GetPostprocessOrder() => 60;

        static bool Mine(string path)
        {
            path = path.Replace('\\', '/');
            return path.StartsWith(Folder, StringComparison.OrdinalIgnoreCase) && path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        void OnPreprocessTexture()
        {
            if (!Mine(assetPath)) return;
            Rule((TextureImporter)assetImporter, assetPath, false, out _);
        }

        static int MaxFor(string path)
        {
            var s = OzUiImageQuality.SourceSize(path);
            int big = Mathf.Max(s.x, s.y), m = 32;
            if (big <= 0) return 2048;
            while (m < big && m < 8192) m *= 2;
            return m;
        }

        static bool Rule(TextureImporter ti, string path, bool dry, out string what)
        {
            var sb = new StringBuilder(); bool ch = false;
            void Set<T>(string name, T cur, T want, Action<T> set) { if (!EqualityComparer<T>.Default.Equals(cur, want)) { if (!dry) set(want); ch = true; sb.Append(name).Append(' ').Append(cur).Append("->").Append(want).Append("; "); } }
            int max = MaxFor(path);
            Set("type", ti.textureType, TextureImporterType.Default, v => ti.textureType = v);
            Set("shape", ti.textureShape, TextureImporterShape.Texture2D, v => ti.textureShape = v);
            Set("sRGB", ti.sRGBTexture, true, v => ti.sRGBTexture = v);
            Set("alphaSource", ti.alphaSource, TextureImporterAlphaSource.FromInput, v => ti.alphaSource = v);
            Set("alphaIsTransparency", ti.alphaIsTransparency, true, v => ti.alphaIsTransparency = v);
            Set("npot", ti.npotScale, TextureImporterNPOTScale.None, v => ti.npotScale = v);
            Set("mipmaps", ti.mipmapEnabled, false, v => ti.mipmapEnabled = v);
            Set("streaming", ti.streamingMipmaps, false, v => ti.streamingMipmaps = v);
            Set("filter", ti.filterMode, FilterMode.Bilinear, v => ti.filterMode = v);
            Set("aniso", ti.anisoLevel, 1, v => ti.anisoLevel = v);
            Set("wrap", ti.wrapMode, TextureWrapMode.Clamp, v => ti.wrapMode = v);
            Set("readable", ti.isReadable, false, v => ti.isReadable = v);
            Set("maxSize", ti.maxTextureSize, max, v => ti.maxTextureSize = v);
            Set("compression", ti.textureCompression, TextureImporterCompression.Uncompressed, v => ti.textureCompression = v);
            var ps = ti.GetPlatformTextureSettings("Standalone");
            if (!ps.overridden || ps.format != TextureImporterFormat.RGBA32 || ps.maxTextureSize != max)
            {
                sb.Append("Standalone ").Append(ps.overridden ? ps.maxTextureSize + "/" + ps.format : "default").Append("->").Append(max).Append("/RGBA32; ");
                ps.overridden = true; ps.maxTextureSize = max; ps.format = TextureImporterFormat.RGBA32; ps.textureCompression = TextureImporterCompression.Uncompressed;
                if (!dry) ti.SetPlatformTextureSettings(ps);
                ch = true;
            }
            what = sb.ToString();
            return ch;
        }

        /// <summary>Expected size in u of a layer (OzBadgeArt.ExpectedU: the numbers OzProductionBadge draws it at).</summary>
        public static Vector2 ExpectedU(string name) => OzBadgeArt.ExpectedU(name);

        static List<string> All()
        {
            var list = new List<string>();
            var root = Folder.TrimEnd('/');
            if (!AssetDatabase.IsValidFolder(root)) return list;
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", new[] { root }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Mine(p) && AssetImporter.GetAtPath(p) is TextureImporter) list.Add(p);
            }
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        [MenuItem("OpenZ/UI/Production Badge/Reimport Art")]
        public static void Reimport() => Run(true);

        [MenuItem("OpenZ/UI/Production Badge/Art Report")]
        public static void Report() => Run(false);

        static void Run(bool fix)
        {
            var paths = All();
            var off = new List<(string, string)>();
            foreach (var p in paths)
            {
                var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                if (Rule(ti, p, true, out var w)) off.Add((p, w));
            }
            if (fix && off.Count > 0)
            {
                try
                {
                    AssetDatabase.StartAssetEditing();
                    foreach (var (p, _) in off) { var ti = (TextureImporter)AssetImporter.GetAtPath(p); Rule(ti, p, false, out _); ti.SaveAndReimport(); }
                }
                finally { AssetDatabase.StopAssetEditing(); }
                AssetDatabase.SaveAssets();
            }
            // pixel size check: every file = its layout size in u x its tier, at every tier
            var bad = new List<string>(); var tiers = new int[5];
            foreach (var p in paths)
            {
                var name = Path.GetFileNameWithoutExtension(p);
                var dir = Path.GetFileName(Path.GetDirectoryName(p));
                if (dir == null || dir.Length < 2 || dir[0] != 'x' || !int.TryParse(dir.Substring(1), out int t) || t < 1 || t > 4) { bad.Add(p + ": not in a x1..x4 folder"); continue; }
                tiers[t]++;
                var u = ExpectedU(name); var px = OzUiImageQuality.SourceSize(p);
                if (u == Vector2.zero) { bad.Add(p + ": unknown layer"); continue; }
                if (px.x != Mathf.RoundToInt(u.x * t) || px.y != Mathf.RoundToInt(u.y * t)) bad.Add($"{p}: {px.x}x{px.y} px, layout wants {u.x * t}x{u.y * t}");
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                if (tex && (tex.width != px.x || tex.height != px.y || tex.mipmapCount > 1)) bad.Add($"{p}: imported {tex.width}x{tex.height}, {tex.mipmapCount} mips");
            }
            var drift = Drift(out int faces);
            var sb = new StringBuilder();
            sb.AppendLine("# Production badge art (" + (fix ? "reimported" : "report") + ", " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")");
            sb.AppendLine();
            sb.AppendLine($"{paths.Count} textures (x1 {tiers[1]}, x2 {tiers[2]}, x3 {tiers[3]}, x4 {tiers[4]}); {off.Count} " + (fix ? "re-imported to the rule" : "off-rule") + $"; {bad.Count} size problems; {faces} faces checked against the live card art, {drift.Count} changed.");
            sb.AppendLine();
            foreach (var (p, w) in off) sb.AppendLine("- off-rule " + p.Substring(Folder.Length) + ": " + w);
            foreach (var b in bad) sb.AppendLine("- SIZE " + b);
            foreach (var d in drift) sb.AppendLine("- RE-BAKE " + d);
            var root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "..", ".."));
            var outPath = Path.Combine(root, "work", "records", "production_badge_art.md");
            try { Directory.CreateDirectory(Path.GetDirectoryName(outPath)); File.WriteAllText(outPath, sb.ToString()); } catch (Exception e) { Debug.LogWarning("[OzBadge] " + e.Message); }
            var msg = $"[OzBadge] {(fix ? "reimport" : "report")}: {paths.Count} textures, {off.Count} {(fix ? "fixed" : "off-rule")}, {bad.Count} size problems, {drift.Count} faces whose card art changed -> {outPath}";
            if (bad.Count > 0 || drift.Count > 0 || (!fix && off.Count > 0)) Debug.LogWarning(msg); else Debug.Log(msg);
        }

        /// <summary>Face drift: every baked face against the live card art it came from (the production window shows the live file; the
        /// badge shows the bake). Lines of OzProductionBadgeSources.txt: face | live asset path | md5 at bake | master | md5.</summary>
        static List<string> Drift(out int faces)
        {
            var list = new List<string>(); faces = 0;
            var src = Path.Combine(Application.dataPath, "OpenZ", "Scripts", "Editor", "OzProductionBadgeSources.txt");
            if (!File.Exists(src)) { list.Add("OzProductionBadgeSources.txt missing: the drift check cannot run (import the whole package)"); return list; }
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            using (var md5 = System.Security.Cryptography.MD5.Create())
                foreach (var line in File.ReadAllLines(src))
                {
                    if (line.StartsWith("#", StringComparison.Ordinal) || line.Trim().Length == 0) continue;
                    var f = line.Split('|'); if (f.Length < 3) continue;
                    faces++;
                    var live = Path.Combine(projectRoot, f[1]);
                    if (!File.Exists(live)) { list.Add(f[0] + ": its card art " + f[1] + " is gone"); continue; }
                    var h = BitConverter.ToString(md5.ComputeHash(File.ReadAllBytes(live))).Replace("-", "").ToLowerInvariant();
                    if (h != f[2].Trim()) list.Add(f[0] + ": " + f[1] + " changed since the bake (python tools/bake_badge.py, then tools/write_metas.py, then re-import)");
                }
            return list;
        }
    }
}
#endif
