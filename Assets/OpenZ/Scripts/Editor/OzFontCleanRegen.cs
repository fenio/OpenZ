#if UNITY_EDITOR
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.TextCore.LowLevel;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Regenerates the Inter Tight SDF font asset after a font file update (same path, same file name, .meta untouched):
    ///  1. sets the font importer's Font Names to "Inter Tight" and reimports;
    ///  2. checks OZ_InterTight_SDF still has its build settings (sampling 90, padding 9, 2048 x 2048, SDFAA, Dynamic, single
    ///     atlas, clear dynamic data on build, "TextMeshPro/Distance Field") and that the new face metrics equal the old ones;
    ///  3. regenerates the asset IN PLACE: new face info, cleared glyph / character / feature tables and atlas, the same 78
    ///     characters seeded again. The asset, its GUID and its material / atlas sub-objects stay the same objects, so the
    ///     Intro scene reference and every Resources.Load keep working. It never deletes or recreates the asset (the menu
    ///     "OpenZ/UI Kit/2 Build Inter Tight SDF" does, which gives the asset a new GUID: do not use it for this job);
    ///  4. re-reads the files on disk and logs PASS / FAIL.
    /// Callable from the menu or from code: OpenZ.EditorTools.OzFontCleanRegen.Run()
    /// </summary>
    public static class OzFontCleanRegen
    {
        const string FontPath = "Assets/OpenZ/Resources/OpenZ/Kit/Fonts/InterTight-ExtraBold.ttf";
        const string AssetPath = "Assets/OpenZ/Resources/OpenZ/Kit/Fonts/OZ_InterTight_SDF.asset";
        const string FontGuid = "49c38475c3e49b542a4123a2768724ae";
        const string AssetGuid = "f4dea2ac7cbb13c4397ae31319fb0b7a";
        const string Family = "Inter Tight";
        const string Style = "ExtraBold";
        static readonly string Needle = "(old-family-name)";

        // The asset's character table before the swap (78 entries, table order). The build clears this dynamic data anyway
        // (m_ClearDynamicDataOnBuild 1): the player fills the atlas at run time from the .ttf.
        static readonly int[] Seed =
        {
            8230, 95, 65, 85, 78, 68, 69, 82, 32, 84, 67, 75, 48, 47, 55, 58, 76, 50, 183, 83, 66, 79, 89, 80, 70, 73, 72, 71, 45,
            77, 86, 88, 53, 90, 49, 37, 87, 43, 101, 108, 99, 116, 97, 117, 110, 105, 100, 114, 103, 98, 111, 120, 115, 109, 118,
            102, 104, 121, 112, 44, 107, 81, 119, 40, 41, 46, 122, 51, 8212, 57, 52, 56, 54, 215, 74, 33, 63, 39,
        };

        [MenuItem("OpenZ/Release/Font clean - regenerate Inter Tight SDF in place")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) { Fail("leave Play mode first"); return; }
            if (AssetDatabase.AssetPathToGUID(FontPath) != FontGuid) { Fail("font GUID is not " + FontGuid + " (file missing or .meta replaced): restore the backup"); return; }
            if (AssetDatabase.AssetPathToGUID(AssetPath) != AssetGuid) { Fail("SDF asset GUID is not " + AssetGuid + ": restore the backup"); return; }

            // 1. importer
            var imp = AssetImporter.GetAtPath(FontPath) as TrueTypeFontImporter;
            if (imp == null) { Fail("no TrueTypeFontImporter on " + FontPath); return; }
            if ((imp.fontTTFName ?? "").ToLowerInvariant().Contains(Needle))
            { Fail("the .ttf on disk is still the old file (TTF name '" + imp.fontTTFName + "'): copy font_clean_v1\\fonts\\InterTight-ExtraBold.ttf over it, then Assets > Refresh"); return; }
            if (!imp.includeFontData) { Fail("Include Font Data is off on the font importer"); return; }
            if (imp.fontNames == null || imp.fontNames.Length != 1 || imp.fontNames[0] != Family)
            {
                imp.fontNames = new[] { Family };
                imp.SaveAndReimport();
            }
            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (!font) { Fail("could not load the Font at " + FontPath); return; }

            // 2. the SDF asset must still carry the settings it was built with (OzUiSetup.BuildFontAsset)
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            if (!fa) { Fail("could not load " + AssetPath); return; }
            bool clearOnBuild = new SerializedObject(fa).FindProperty("m_ClearDynamicDataOnBuild").boolValue;
            var old = fa.faceInfo;
            string settings = $"sampling {old.pointSize}, padding {fa.atlasPadding}, atlas {fa.atlasWidth}x{fa.atlasHeight}, {fa.atlasRenderMode}, "
                            + $"{fa.atlasPopulationMode}, multi-atlas {fa.isMultiAtlasTexturesEnabled}, clear-on-build {clearOnBuild}, "
                            + $"shader {(fa.material && fa.material.shader ? fa.material.shader.name : "none")}";
            bool sameSettings = old.pointSize == 90 && fa.atlasPadding == 9 && fa.atlasWidth == 2048 && fa.atlasHeight == 2048
                && fa.atlasRenderMode == GlyphRenderMode.SDFAA && fa.atlasPopulationMode == AtlasPopulationMode.Dynamic
                && !fa.isMultiAtlasTexturesEnabled && clearOnBuild
                && fa.material && fa.material.shader && fa.material.shader.name == "TextMeshPro/Distance Field"
                && fa.sourceFontFile == font;
            if (!sameSettings) { Fail("unexpected SDF settings (" + settings + "): stop and report, do not improvise"); return; }

            // 3a. face info from the new file: names change, metrics must not
            FontEngine.InitializeFontEngine();
            if (FontEngine.LoadFontFace(font, 90) != FontEngineError.Success) { Fail("FontEngine could not load the new font"); return; }
            FaceInfo fi = FontEngine.GetFaceInfo();
            string diff = MetricDiff(old, fi);
            if (diff != null) { Fail("face metrics differ, so this is not the same glyph set: " + diff); return; }
            if (fi.familyName != Family || fi.styleName != Style) { Fail($"new face reads '{fi.familyName}' / '{fi.styleName}', expected '{Family}' / '{Style}'"); return; }
            fa.faceInfo = fi;

            // 3b. regenerate glyphs + atlas in place (what the inspector's gear menu > Reset does), then seed the same characters
            fa.ClearFontAssetData(true);
            var sb = new StringBuilder();
            foreach (int c in Seed) sb.Append(char.ConvertFromUtf32(c));
            fa.TryAddCharacters(sb.ToString(), out string missing);
            EditorUtility.SetDirty(fa);
            if (fa.atlasTexture) EditorUtility.SetDirty(fa.atlasTexture);
            if (fa.material) EditorUtility.SetDirty(fa.material);
            AssetDatabase.SaveAssets();
            TMPro_EventManager.ON_FONT_PROPERTY_CHANGED(true, fa);

            // 4. verify on disk
            string assetText = File.ReadAllText(AssetPath);
            string metaText = File.ReadAllText(FontPath + ".meta");
            byte[] ttf = File.ReadAllBytes(FontPath);
            bool clean = !assetText.ToLowerInvariant().Contains(Needle) && !metaText.ToLowerInvariant().Contains(Needle) && !BytesContain(ttf, Needle);
            bool guids = AssetDatabase.AssetPathToGUID(FontPath) == FontGuid && AssetDatabase.AssetPathToGUID(AssetPath) == AssetGuid;
            bool named = assetText.Contains("m_FamilyName: " + Family + "\n") || assetText.Contains("m_FamilyName: " + Family + "\r\n");
            int chars = fa.characterTable.Count;
            string summary = $"[OzFontClean] {(clean && guids && named && chars == Seed.Length ? "PASS" : "FAIL")}: family '{fa.faceInfo.familyName}' / '{fa.faceInfo.styleName}', "
                           + $"{chars} characters{(string.IsNullOrEmpty(missing) ? "" : ", missing " + missing)}, atlas {fa.atlasTexture.width}x{fa.atlasTexture.height}, "
                           + $"{settings}; GUIDs kept {guids}; old name gone from .ttf/.meta/.asset {clean}";
            if (clean && guids && named && chars == Seed.Length) Debug.Log(summary); else Debug.LogError(summary);
        }

        static string MetricDiff(FaceInfo a, FaceInfo b)
        {
            var sb = new StringBuilder();
            void Cmp(string n, float x, float y) { if (Mathf.Abs(x - y) > 0.001f) sb.Append($"{n} {x} -> {y}; "); }
            Cmp("pointSize", a.pointSize, b.pointSize);
            Cmp("lineHeight", a.lineHeight, b.lineHeight);
            Cmp("ascentLine", a.ascentLine, b.ascentLine);
            Cmp("capLine", a.capLine, b.capLine);
            Cmp("meanLine", a.meanLine, b.meanLine);
            Cmp("baseline", a.baseline, b.baseline);
            Cmp("descentLine", a.descentLine, b.descentLine);
            Cmp("underlineOffset", a.underlineOffset, b.underlineOffset);
            Cmp("underlineThickness", a.underlineThickness, b.underlineThickness);
            Cmp("strikethroughOffset", a.strikethroughOffset, b.strikethroughOffset);
            Cmp("tabWidth", a.tabWidth, b.tabWidth);
            return sb.Length == 0 ? null : sb.ToString();
        }

        static bool BytesContain(byte[] data, string needle)
        {
            // ASCII and both UTF-16 byte orders, any case

            foreach (var enc in new Encoding[] { Encoding.ASCII, Encoding.BigEndianUnicode, Encoding.Unicode })
            {
                byte[] lo = enc.GetBytes(needle.ToLowerInvariant()), up = enc.GetBytes(needle.ToUpperInvariant());
                for (int i = 0; i + lo.Length <= data.Length; i++)
                {
                    int k = 0;
                    while (k < lo.Length && (data[i + k] == lo[k] || data[i + k] == up[k])) k++;
                    if (k == lo.Length) return true;
                }
            }
            return false;
        }

        static void Fail(string why) => Debug.LogError("[OzFontClean] FAIL: " + why);
    }
}
#endif
