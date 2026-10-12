#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Applies the OpenZ UI kit's import settings (kit.json, drawn-in-code 2x sprites) to every texture under
    /// Assets/OpenZ/Resources/OpenZ/Kit/: Sprite (2D and UI), Single, 200 PPU (1 canvas unit = 2 px), Mesh Type Full Rect,
    /// 9-slice borders, pivots, no mip maps, Bilinear (Point for the static noise), Clamp (Repeat for tiling textures),
    /// uncompressed, sRGB, alpha is transparency. Cursor textures get Texture Type Cursor. Copy kit.json with the PNGs.
    /// Menu: OpenZ / UI Kit / Reapply Import Settings (after kit.json changed or was copied after the PNGs).
    /// </summary>
    public class OzKitImporter : AssetPostprocessor
    {
        public const string KitFolder = "Assets/OpenZ/Resources/OpenZ/Kit/";

#pragma warning disable 0649   // fields are filled by JsonUtility
        [Serializable]
        class Entry
        {
            public string file, type, draw, wrap, filter;
            public int[] size_px, unity_border_LBRT, hotspot_px;
            public float[] pivot;
        }

        [Serializable]
        class KitFile { public Entry[] sprites; }
#pragma warning restore 0649

        static Dictionary<string, Entry> map;
        static DateTime stamp;

        static Dictionary<string, Entry> Map()
        {
            var p = Path.Combine(Application.dataPath, "OpenZ/Resources/OpenZ/Kit/kit.json");
            if (!File.Exists(p)) return null;
            var t = File.GetLastWriteTimeUtc(p);
            if (map != null && t == stamp) return map;
            var kit = JsonUtility.FromJson<KitFile>(File.ReadAllText(p));
            map = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            if (kit != null && kit.sprites != null)
                foreach (var e in kit.sprites)
                    if (!string.IsNullOrEmpty(e.file)) map[e.file.Replace('\\', '/')] = e;
            stamp = t;
            return map;
        }

        void OnPreprocessTexture()
        {
            var path = assetPath.Replace('\\', '/');
            if (!path.StartsWith(KitFolder, StringComparison.OrdinalIgnoreCase)) return;
            var m = Map();
            if (m == null) { Debug.LogWarning("[OzKit] " + KitFolder + "kit.json not found: copy it with the PNGs, then OpenZ/UI Kit/Reapply Import Settings"); return; }
            if (!m.TryGetValue(path.Substring(KitFolder.Length), out var e)) return;

            var ti = (TextureImporter)assetImporter;
            bool cursor = e.type == "Cursor";
            ti.textureType = cursor ? TextureImporterType.Cursor : TextureImporterType.Sprite;
            ti.sRGBTexture = true;
            ti.alphaIsTransparency = true;
            ti.mipmapEnabled = false;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            int big = e.size_px != null && e.size_px.Length == 2 ? Mathf.Max(e.size_px[0], e.size_px[1]) : 1024;
            ti.maxTextureSize = big > 4096 ? 8192 : big > 2048 ? 4096 : big > 1024 ? 2048 : 1024;
            ti.wrapMode = e.wrap == "Repeat" ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            ti.filterMode = e.filter == "Point" ? FilterMode.Point : FilterMode.Bilinear;
            if (cursor) return;

            ti.spriteImportMode = SpriteImportMode.Single;
            ti.spritePixelsPerUnit = 200f;
            var s = new TextureImporterSettings();
            ti.ReadTextureSettings(s);
            s.spriteMeshType = SpriteMeshType.FullRect;
            s.spriteGenerateFallbackPhysicsShape = false;
            s.spriteAlignment = (int)SpriteAlignment.Custom;
            s.spritePivot = e.pivot != null && e.pivot.Length == 2 ? new Vector2(e.pivot[0], e.pivot[1]) : new Vector2(0.5f, 0.5f);
            var b = e.unity_border_LBRT;
            s.spriteBorder = b != null && b.Length == 4 ? new Vector4(b[0], b[1], b[2], b[3]) : Vector4.zero;
            ti.SetTextureSettings(s);
        }

        [MenuItem("OpenZ/UI Kit/Reapply Import Settings")]
        static void Reapply()
        {
            map = null;
            AssetDatabase.ImportAsset(KitFolder.TrimEnd('/'), ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            Debug.Log("[OzKit] import settings re-applied from kit.json");
        }
    }
}
#endif
