using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// into Assets/OpenZ/Resources/OpenZ/Craters (README_IMPORT.md of crater_patches_v1: scale 1, Read/Write ON, no mesh compression,
    /// Optimize Mesh NOTHING and no welding so the polar vertex order stays, normals imported, no colliders, no materials; decals
    /// sRGB except the 2x-multiply ones, alpha is transparency, mips, clamp, BC7). Menus: OpenZ > Craters.
    /// </summary>
    public class OpenZCraterAssets : AssetPostprocessor
    {
        public const string Folder = "Assets/OpenZ/Resources/OpenZ/Craters";

        void OnPreprocessModel()
        {
            if (!assetPath.Replace('\\', '/').StartsWith(Folder)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f; mi.useFileScale = true; mi.isReadable = true;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.optimizeMeshPolygons = false; mi.optimizeMeshVertices = false; mi.weldVertices = false; mi.keepQuads = false;
            mi.importNormals = ModelImporterNormals.Import; mi.importTangents = ModelImporterTangents.None;
            mi.addCollider = false; mi.importAnimation = false; mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = ModelImporterAnimationType.None;
        }

        void OnPreprocessTexture()
        {
            var p = assetPath.Replace('\\', '/');
            if (!p.StartsWith(Folder)) return;
            var ti = (TextureImporter)assetImporter;
            ti.textureType = TextureImporterType.Default; ti.isReadable = false;
            if (p.Contains("/Decals/"))
            {
                bool mul = Path.GetFileNameWithoutExtension(p).EndsWith("_Mul");
                ti.sRGBTexture = !mul;                                                    // 2x multiply: 0.5 must stay 0.5 (linear)
                ti.alphaSource = TextureImporterAlphaSource.FromInput; ti.alphaIsTransparency = true;
                ti.mipmapEnabled = true; ti.wrapMode = TextureWrapMode.Clamp; ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = 4;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;             // BC7 on PC
                ti.maxTextureSize = 1024;
            }
            else
            {   // CraterKit_Atlas: swatches, bilinear, no mips
                ti.sRGBTexture = true; ti.mipmapEnabled = false; ti.filterMode = FilterMode.Bilinear; ti.wrapMode = TextureWrapMode.Clamp;
                ti.textureCompression = TextureImporterCompression.Uncompressed; ti.maxTextureSize = 256;
            }
        }

        [MenuItem("OpenZ/Craters/1 Reimport assets (import settings)")]
        static void Reimport()
        {
            var guids = AssetDatabase.FindAssets("", new[] { Folder });
            int n = 0;
            foreach (var g in guids)
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (AssetDatabase.IsValidFolder(p)) continue;
                AssetDatabase.ImportAsset(p, ImportAssetOptions.ForceUpdate); n++;
            }
            Debug.Log($"[OpenZ Craters] reimported {n} asset(s) under {Folder}; now run OpenZ > Craters > 2 Check");
        }

        [MenuItem("OpenZ/Craters/2 Check")]
        static void Check()
        {
            bool ok = true; var sb = new System.Text.StringBuilder();
            foreach (var id in new[] { "Sand_A", "Sand_B", "Sand_C", "Sand_D", "Paved_A", "Paved_B" })
            {
                var go = Resources.Load<GameObject>("OpenZ/Craters/CraterPatch_" + id);
                if (!go) { sb.Append($"\n  MISSING CraterPatch_{id}"); ok = false; continue; }
                var mfs = go.GetComponentsInChildren<MeshFilter>(true);
                var p = mfs.FirstOrDefault(m => m.name == "CRATER_" + id)?.sharedMesh;
                var c = mfs.FirstOrDefault(m => m.name == "COL_CRATER_" + id)?.sharedMesh;
                var k = mfs.FirstOrDefault(m => m.name == "CRATER_" + id + "_Clods")?.sharedMesh;
                bool good = p && c && p.isReadable && c.isReadable && p.vertexCount == 2305 && c.vertexCount == 449;
                ok &= good;
                sb.Append($"\n  {(good ? "OK  " : "FAIL")} {id}: patch {(p ? p.vertexCount + " v" + (p.isReadable ? "" : " NOT READABLE") : "missing")} (expect 2305), " +
                          $"collider {(c ? c.vertexCount + " v" : "missing")} (expect 449), clods {(k ? k.vertexCount + " v" : "missing")}, uv1 {(p && p.uv2 != null && p.uv2.Length == p.vertexCount ? "yes" : "NO")}");
            }
            var kit = Resources.Load<GameObject>("OpenZ/Craters/CraterClodKit");
            sb.Append($"\n  clod kit: {(kit ? kit.GetComponentsInChildren<MeshFilter>(true).Length + " meshes" : "MISSING")}");
            foreach (var d in new[] { "CraterDecal_Sand_01_Mul", "CraterDecal_Sand_02_Mul", "CraterDecal_Sand_03_Mul", "CraterDecal_Paved_01", "CraterDecal_Paved_02" })
            {
                var t = Resources.Load<Texture2D>("OpenZ/Craters/Decals/" + d);
                if (!t) { ok = false; sb.Append($"\n  MISSING decal {d}"); continue; }
                var ti = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
                bool srgbOk = ti != null && ti.sRGBTexture == !d.EndsWith("_Mul");
                ok &= srgbOk; sb.Append($"\n  {(srgbOk ? "OK  " : "FAIL")} decal {d} {t.width}x{t.height} sRGB {(ti != null && ti.sRGBTexture ? "on" : "off")}");
            }
            var sand = Shader.Find("OpenZ/Sand");
            bool roleOk = sand && sand.FindPropertyIndex("_OzCraterRole") >= 0;
            ok &= roleOk; sb.Append($"\n  {(roleOk ? "OK  " : "FAIL")} Sand.shader has _OzCraterRole (crater clip + decal)");
            sb.Append($"\n  option {CraterField.Label}: {(CraterField.Enabled ? "ON" : "OFF")} ({CraterField.Pref})");
            Debug.Log($"[OpenZ Craters] check {(ok ? "PASS" : "FAIL")}:{sb}");
        }

        [MenuItem("OpenZ/Craters/Option ON")] static void On() => CraterField.Enabled = true;
        [MenuItem("OpenZ/Craters/Option OFF")] static void Off() => CraterField.Enabled = false;

        [MenuItem("OpenZ/Craters/Play: crater at the screen centre")]
        static void Test()
        {
            if (!Application.isPlaying) { Debug.Log("[OpenZ Craters] enter Play on L02 / L17 first"); return; }
            Debug.Log("[OpenZ Craters] test at the screen centre: " + CraterField.TestAtScreenCentre());
        }
    }
}
