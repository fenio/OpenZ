using System.IO;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>Quick offscreen close-up shots for QC (temporary camera, main camera settings, PNG out).</summary>
    public static class OpenZShot
    {
        public static string Shot(Vector3 target, float dist, float pitch, float yaw, string path, int w = 1280, int h = 720)
        {
            var main = Camera.main;
            var go = new GameObject("QC_ShotCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>(); cam.CopyFrom(main);
            var ad = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); ad.renderPostProcessing = true;
            go.transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            go.transform.position = target - go.transform.forward * dist;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            for (int i = 0; i < 3; i++) cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            RenderTexture.active = null; cam.targetTexture = null;
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            return path;
        }

        /// <summary>
        /// Moving-camera shimmer / z-fighting check: renders frames at the game camera pose (pitch 55, FOV 32) while the
        /// focus pans by `step` metres per frame; frames go to outDir/prefix_NN.png for the flicker analysis script.
        /// </summary>
        public static string PanFrames(Vector3 focus, float height, int frames, Vector3 step, string outDir, string prefix, int w = 1920, int h = 1080)
        {
            var main = Camera.main;
            var go = new GameObject("QC_PanCam") { hideFlags = HideFlags.HideAndDontSave };
            var cam = go.AddComponent<Camera>(); cam.CopyFrom(main);
            var ad = go.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(); ad.renderPostProcessing = true;
            ad.antialiasing = UnityEngine.Rendering.Universal.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            Directory.CreateDirectory(outDir);
            float pitch = 55f;
            for (int f = 0; f < frames; f++)
            {
                var fc = focus + step * f;
                go.transform.rotation = Quaternion.Euler(pitch, 0, 0);
                go.transform.position = fc + new Vector3(0, height, -height / Mathf.Tan(pitch * Mathf.Deg2Rad));
                cam.targetTexture = rt;
                cam.Render(); cam.Render();
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, $"{prefix}_{f:00}.png"), tex.EncodeToPNG());
            }
            cam.targetTexture = null;
            Object.DestroyImmediate(tex); rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(go);
            return outDir;
        }
    }
}
