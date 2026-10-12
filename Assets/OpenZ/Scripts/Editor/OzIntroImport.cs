using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Robots (IC_Alan / IC_Brad): Generic rig, Preserve Hierarchy ON, the Armature|ic_* takes renamed to ic_* (the empty Key|...
    /// blendshape takes dropped), loop clips flagged. Props: baked transforms, no animation. Normal maps as NormalMap.</summary>
    public sealed class OzIntroImport : AssetPostprocessor
    {
        const string Root = "Assets/OpenZ/Intro/";
        static readonly HashSet<string> Loops = new HashSet<string>
        {
            "ic_lie_hungover", "ic_sit_hold_head", "ic_head_scratch", "ic_stand_can_mug", "ic_unconvinced", "ic_seat_idle",
            "ic_seat_bounce_eager", "ic_seat_arms_crossed", "ic_seat_scream", "ic_seat_doze", "idle", "walking", "run_forward"
        };

        static bool IsRobot(string p) => p.EndsWith("IC_Alan.fbx") || p.EndsWith("IC_Brad.fbx");

        void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Root)) return;
            var m = (ModelImporter)assetImporter;
            m.globalScale = 1f;
            m.useFileScale = true;
            // read their vertices at run time: Read/Write on for those two, off for the rest
            m.isReadable = assetPath.EndsWith("IC_Shuttle.fbx") || assetPath.EndsWith("IC_FuelCan.fbx");
            m.importBlendShapes = true;
            m.importCameras = false;
            m.importLights = false;
            m.meshCompression = ModelImporterMeshCompression.Off;
            if (IsRobot(assetPath))
            {
                m.preserveHierarchy = true;
                m.animationType = ModelImporterAnimationType.Generic;
                m.importAnimation = true;
                m.resampleCurves = true;
                m.animationCompression = ModelImporterAnimationCompression.KeyframeReduction;
            }
            else
            {
                m.animationType = ModelImporterAnimationType.None;
                m.importAnimation = false;
                m.bakeAxisConversion = false;
            }
        }

        void OnPreprocessAnimation()
        {
            if (!assetPath.StartsWith(Root) || !IsRobot(assetPath)) return;
            var m = (ModelImporter)assetImporter;
            var list = new List<ModelImporterClipAnimation>();
            foreach (var c in m.defaultClipAnimations)
            {
                string n = c.takeName ?? c.name;
                if (n.StartsWith("Key|") || n.StartsWith("Key.")) continue;
                int bar = n.IndexOf('|');
                c.name = bar >= 0 ? n.Substring(bar + 1) : n;
                c.loopTime = Loops.Contains(c.name);
                c.loopPose = false;
                list.Add(c);
            }
            if (list.Count > 0) m.clipAnimations = list.ToArray();
        }

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root)) return;
            var t = (TextureImporter)assetImporter;
            if (assetPath.EndsWith("_Normal.png")) t.textureType = TextureImporterType.NormalMap;
            t.mipmapEnabled = true;
            t.anisoLevel = 8;
            t.maxTextureSize = 4096;
            t.textureCompression = TextureImporterCompression.CompressedHQ;
            if (assetPath.EndsWith("IC_CanRing_BaseColor.png")) t.alphaIsTransparency = true;
            // end title (staging intro_end_title_v1 README): metal / smoothness maps are linear data; the 4136 face maps keep full size
            if (assetPath.Contains("_MetalSmooth")) { t.sRGBTexture = false; t.alphaIsTransparency = false; }
            if (assetPath.Contains("/EndTitle/OPENZ_Logo3D_Face")) t.maxTextureSize = 8192;
            if (assetPath.Contains("/EndTitle/OPENZ_Logo3D_Face") || assetPath.Contains("/EndTitle/OPENZ_BulletHole")) t.wrapMode = TextureWrapMode.Clamp;
        }

        void OnPreprocessAudio()
        {
            if (!assetPath.StartsWith(Root)) return;
            var a = (AudioImporter)assetImporter;
            a.forceToMono = false;
            a.loadInBackground = false;
            var s = a.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = 0.9f;
            a.defaultSampleSettings = s;
        }
    }
}
