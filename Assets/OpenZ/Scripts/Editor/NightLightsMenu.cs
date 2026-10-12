using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Night package editor tools: OpenZ/Night menu (jump the DayNight clock, freeze it, log the NightLights status) and the
    /// GPU bridge: NightLightBudget reads GpuGuard's last nvidia-smi temperature so the night lights shed real lights and
    /// shadows before GpuGuard has to pause play mode (74 C: fewer lights, 77 C: no shadows; GpuGuard pauses at 78 C).
    /// </summary>
    [InitializeOnLoad]
    public static class NightLightsMenu
    {
        static NightLightsMenu() { NightLightBudget.GpuTemperature = () => GpuGuard.LastTemp; }

        static void Jump(float t, string name)
        {
            var dn = Object.FindFirstObjectByType<DayNight>();
            if (!dn) { Debug.LogWarning("[OpenZ] Night: no DayNight in the open scene"); return; }
            dn.timeOfDay = t; dn.Apply(t);
            if (!Application.isPlaying) { EditorUtility.SetDirty(dn); SceneView.RepaintAll(); }
            Debug.Log($"[OpenZ] Night: time of day -> {name} ({t:F3}){(Application.isPlaying ? "" : " (edit mode: scene lighting only, the night lights run in play mode)")}");
        }

        [MenuItem("OpenZ/Night/Jump To Day (0.08)")] static void Day() => Jump(0.08f, "day");
        [MenuItem("OpenZ/Night/Jump To Dusk (0.33)")] static void Dusk() => Jump(0.33f, "dusk");
        [MenuItem("OpenZ/Night/Jump To Blue Hour (0.43)")] static void Blue() => Jump(0.43f, "blue hour");
        [MenuItem("OpenZ/Night/Jump To Night (0.62)")] static void Night() => Jump(0.62f, "night");
        [MenuItem("OpenZ/Night/Jump To Dawn (0.895)")] static void Dawn() => Jump(0.895f, "dawn");

        [MenuItem("OpenZ/Night/Freeze Or Run Clock")]
        static void Freeze()
        {
            var dn = Object.FindFirstObjectByType<DayNight>(); if (!dn) return;
            dn.running = !dn.running;
            Debug.Log($"[OpenZ] Night: day/night clock {(dn.running ? "running" : "frozen")} at {dn.timeOfDay:F3}");
        }

        [MenuItem("OpenZ/Night/Log Night Lights Status")]
        static void Status()
        {
            if (!NightLights.I) { Debug.Log("[OpenZ] Night: NightLights runs in play mode in a level scene"); return; }
            Debug.Log(NightLights.I.Status());
        }
    }
}
