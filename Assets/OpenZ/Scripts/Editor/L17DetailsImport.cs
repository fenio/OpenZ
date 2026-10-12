using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// l17_details_v1: import settings for the detail kit in Assets/OpenZ/Resources/OpenZ/L17Details (harbour outfalls,
    /// grates, floor lights, hatches, car wrecks; desert rabbit and burrow): the OpenZ Blender FBX axis handling (Bake Axis
    /// Conversion, file units), static meshes only (no rig, no animation, no cameras / lights), no imported materials
    /// (HarbourDetails / DesertCritters assign OpenZ/TeamLit with the vertex colours at run time), readable meshes (the
    /// floor-light lens and rim meshes are drawn instanced), imported normals, no mesh compression (small parts, exact bevels).
    /// Sounds in Resources/OpenZ/Sfx/dtl_*.wav: 3D one-shots, the pour loop streams.
    /// </summary>
    public class L17DetailsImport : AssetPostprocessor
    {
        static bool Mine(string path) => path.Replace("\\", "/").Contains("/Resources/OpenZ/L17Details/");

        void OnPreprocessModel()
        {
            if (!Mine(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = true;
            mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.importVisibility = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = ModelImporterAnimationType.None;
            mi.importAnimation = false;
            mi.isReadable = true;
            mi.meshCompression = ModelImporterMeshCompression.Off;
            mi.importNormals = ModelImporterNormals.Import;
            mi.addCollider = false;
            mi.optimizeGameObjects = false;
        }

        void OnPreprocessAudio()
        {
            var p = assetPath.Replace("\\", "/");
            if (!p.Contains("/Resources/OpenZ/Sfx/dtl_")) return;
            var ai = (AudioImporter)assetImporter;
            var s = ai.defaultSampleSettings;
            s.loadType = p.EndsWith("_loop.wav") ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis; s.quality = 0.7f;
            ai.defaultSampleSettings = s;
            ai.forceToMono = true;
        }
    }
}
