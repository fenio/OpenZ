using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Generic rig with an avatar from the model (AmbientLife plays the single take through a manual PlayableGraph), the
    /// same axis handling as the other OpenZ Blender FBX (Bake Axis Conversion, file units), exact poses at the sampled
    /// frames (no keyframe reduction), no imported materials / cameras / lights / blend shapes (AmbientLife assigns
    /// OpenZ/TeamLit at run time), readable bone transforms (no Optimize Game Objects). The shared 8x8 colour palette gets
    /// no mip maps so far birds keep clean colours (swatches must not bleed into each other).
    /// </summary>
    public class CritterImport : AssetPostprocessor
    {
        static bool Mine(string path) => path.Replace('\\', '/').Contains("/Resources/OpenZ/Critters/");

        void OnPreprocessModel()
        {
            if (!Mine(assetPath)) return;
            var mi = (ModelImporter)assetImporter;
            mi.globalScale = 1f; mi.useFileScale = true; mi.bakeAxisConversion = true;
            mi.importCameras = false; mi.importLights = false; mi.importBlendShapes = false; mi.importVisibility = false;
            mi.materialImportMode = ModelImporterMaterialImportMode.None;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.optimizeGameObjects = false;
            mi.importAnimation = true;
            mi.animationCompression = ModelImporterAnimationCompression.Off;
            mi.resampleCurves = true;
            mi.addCollider = false;
        }

        void OnPreprocessTexture()
        {
            if (!Mine(assetPath)) return;
            var ti = (TextureImporter)assetImporter;
            ti.sRGBTexture = true; ti.alphaSource = TextureImporterAlphaSource.None;
            if (assetPath.EndsWith("critter_palette.png")) { ti.mipmapEnabled = false; ti.filterMode = FilterMode.Bilinear; ti.textureCompression = TextureImporterCompression.Uncompressed; }
            else { ti.mipmapEnabled = true; ti.maxTextureSize = 512; }
        }
    }
}
