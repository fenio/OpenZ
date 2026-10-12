using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// </summary>
    public static class OpenZCapture
    {
        [Serializable] class Spot { public string name; public float[] pos; public float[] look; public float vfov; }
        [Serializable] class SpotList { public Spot[] items; }

        public static string CaptureSpots(string lv, string scenePath, string outDir)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            string json = File.ReadAllText(Path.Combine(outDir, "spots.json"));
            // spots.json is {"L02": [...], "L17": [...]}: pull the level's array out for JsonUtility
            int i0 = json.IndexOf("\"" + lv + "\"", StringComparison.Ordinal);
            int a0 = json.IndexOf('[', i0), depth = 0, a1 = a0;
            for (int i = a0; i < json.Length; i++) { if (json[i] == '[') depth++; else if (json[i] == ']' && --depth == 0) { a1 = i; break; } }
            var spots = JsonUtility.FromJson<SpotList>("{\"items\":" + json.Substring(a0, a1 - a0 + 1) + "}").items;
            var cam = Camera.main;
            var rts = cam.GetComponent<RtsCamera>(); bool rtsOn = rts && rts.enabled; if (rts) rts.enabled = false;
            var dn = UnityEngine.Object.FindFirstObjectByType<DayNight>(); if (dn) dn.Apply(dn.timeOfDay);
            Vector3 p0 = cam.transform.position; Quaternion r0 = cam.transform.rotation; float f0 = cam.fieldOfView;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            for (int w = 0; w < 3; w++) { cam.targetTexture = rt; cam.Render(); }
            foreach (var s in spots)
            {
                var p = new Vector3(s.pos[0], s.pos[1], s.pos[2]); var t = new Vector3(s.look[0], s.look[1], s.look[2]);
                cam.transform.SetPositionAndRotation(p, Quaternion.LookRotation(t - p, Vector3.up));
                cam.fieldOfView = s.vfov; cam.targetTexture = rt; cam.Render(); cam.Render();   // second frame: textures/shadows resident
                RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
                File.WriteAllBytes(Path.Combine(outDir, $"unity_{lv}_{s.name}.png"), tex.EncodeToPNG());
            }
            cam.targetTexture = null; cam.transform.SetPositionAndRotation(p0, r0); cam.fieldOfView = f0;
            if (rts) rts.enabled = rtsOn;
            UnityEngine.Object.DestroyImmediate(rt); UnityEngine.Object.DestroyImmediate(tex);
            return $"{lv}: captured {spots.Length} spots";
        }
    }
}
