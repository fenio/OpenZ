using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// windows, CSV in Application.persistentDataPath). Session only: Play exit removes it and restores the 60 fps cap.</summary>
    static class OpenZPerfMenu
    {
        const string StartItem = "OpenZ/Perf/Probe: start (uncapped, this Play only)", StopItem = "OpenZ/Perf/Probe: stop", MarkItem = "OpenZ/Perf/Probe: mark window";

        [MenuItem(StartItem)] static void StartProbe()
        {
            OzPerfProbe.TempSource = () => GpuGuard.LastTemp;
            OzPerfProbe.Begin("editor menu");
        }
        [MenuItem(StartItem, true)] static bool StartOk() => Application.isPlaying && !OzPerfProbe.I;

        [MenuItem(StopItem)] static void StopProbe() => OzPerfProbe.End();
        [MenuItem(StopItem, true)] static bool StopOk() => Application.isPlaying && OzPerfProbe.I;

        [MenuItem(MarkItem)] static void MarkWindow() => OzPerfProbe.Mark("manual");
        [MenuItem(MarkItem, true)] static bool MarkOk() => Application.isPlaying && OzPerfProbe.I;

        const string StormItem = "OpenZ/Perf/Moment: night rain storm now (900 s)";
        [MenuItem(StormItem)] static void Storm() { if (OzWeather.Force(900f)) OzPerfProbe.Mark("storm"); else Debug.LogWarning("[OpenZ Perf] no OzWeather in this level"); }
        [MenuItem(StormItem, true)] static bool StormOk() => Application.isPlaying;
    }
}
