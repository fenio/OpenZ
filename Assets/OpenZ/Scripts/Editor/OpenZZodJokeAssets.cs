using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// glove poses + ink as Sprites (sRGB, RGBA32, no mips); *_order.png as linear R8 (sRGB off, no mips, no compression, bilinear).
    /// Menus: OpenZ > Zod > GENERAL joke.
    /// </summary>
    public class OpenZZodJokeAssets : AssetPostprocessor
    {
        public const string Folder = "Assets/OpenZ/Resources/OpenZ/Zod/Joke";

        void OnPreprocessTexture()
        {
            var p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            ti.mipmapEnabled = false; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Bilinear; ti.isReadable = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed; ti.npotScale = TextureImporterNPOTScale.None;
            var ps = ti.GetDefaultPlatformTextureSettings();
            if (p.EndsWith("_order.png"))
            {
                ti.textureType = TextureImporterType.Default; ti.sRGBTexture = false; ti.alphaSource = TextureImporterAlphaSource.None;
                ps.format = TextureImporterFormat.R8;
            }
            else
            {
                ti.textureType = TextureImporterType.Sprite; ti.spriteImportMode = SpriteImportMode.Single; ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
                ps.format = TextureImporterFormat.RGBA32;
            }
            ps.textureCompression = TextureImporterCompression.Uncompressed;
            ti.SetPlatformTextureSettings(ps);
        }

        [MenuItem("OpenZ/Zod/GENERAL joke/1 Reimport sprites")]
        static void Reimport()
        {
            int n = 0;
            foreach (var g in AssetDatabase.FindAssets("", new[] { Folder }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (AssetDatabase.IsValidFolder(p)) continue;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); n++;
            }
            Debug.Log($"[ZodCameo] GENERAL joke: reimported {n} asset(s); run OpenZ > Zod > GENERAL joke > 2 Check");
        }

        [MenuItem("OpenZ/Zod/GENERAL joke/2 Check")]
        static void Check()
        {
            bool ok = true; var sb = new System.Text.StringBuilder();
            foreach (var s in new[] { "glove_marker_enter", "glove_marker_strike", "glove_marker_write", "strike_ink", "general_ink" })
            { bool has = Resources.Load<Sprite>("OpenZ/Zod/Joke/" + s); ok &= has; sb.Append($"\n  {(has ? "OK  " : "FAIL")} sprite {s}"); }
            foreach (var s in new[] { "strike_order", "general_order" })
            {
                var t = Resources.Load<Texture2D>("OpenZ/Zod/Joke/" + s);
                var ti = t ? AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter : null;
                bool good = t && ti != null && !ti.sRGBTexture; ok &= good; sb.Append($"\n  {(good ? "OK  " : "FAIL")} order mask {s} (linear)");
            }
            foreach (var s in new[] { "lettering", "glove_marker" }) { bool has = Resources.Load<TextAsset>("OpenZ/Zod/Joke/" + s); ok &= has; sb.Append($"\n  {(has ? "OK  " : "FAIL")} {s}.json"); }
            bool sh = Resources.Load<Shader>("OpenZ/Zod/Joke/OzInkReveal"); ok &= sh; sb.Append($"\n  {(sh ? "OK  " : "FAIL")} shader OpenZ/UI/InkReveal");
            bool ahem = Resources.Load<AudioClip>("OpenZ/Zod/Voice/zod_ahem"); ok &= ahem; sb.Append($"\n  {(ahem ? "OK  " : "FAIL")} Zod/Voice/zod_ahem (sfx_batch_v1 apply_import)");
            Debug.Log($"[ZodCameo] GENERAL joke check {(ok ? "PASS" : "FAIL")}:{sb}");
        }

        [MenuItem("OpenZ/Zod/GENERAL joke/Play: report the last joke")]
        static void Report() => Debug.Log("[ZodCameo] " + ZodBriefing.DebugJoke());
    }
}
