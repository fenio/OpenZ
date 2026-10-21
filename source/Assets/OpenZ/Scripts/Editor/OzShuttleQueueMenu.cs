#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K410 menus (rule DV125, the shuttle flies the whole Shift queue): the counted check out of Play (Editor/OzShuttleQueueCount.cs)
    /// and the Play proof View/OzShuttleQueueProof.cs on level 16 (its output folder: OzShuttleQueueProof.Folder(), built at run time from the
    /// project's place).</summary>
    public static class OzShuttleQueueMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K410 Route Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K410 route check (DV125)", () => OzShuttleQueueCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16")));

        [MenuItem("OpenZ/V1.1/Shuttle: K410 Route Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K410 route proof: start level 16 in Play first"); return; }
            OzShuttleQueueProof.Run();
            Debug.Log("[OpenZ] K410 route proof started; its report: OzShuttleQueueProof.Report, the log and pictures in " + OzShuttleQueueProof.Folder());
        }
    }
}
#endif
