#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K426 menus (rule DV118, Sim/ZWorld.ShuttleExitSooner.cs: EXIT taken from the touchdown, a crash's wait shorter): the counted
    /// check out of Play (Editor/OzShuttleExitSoonerCount.cs, headless on level 16's data) and the Play proof View/OzShuttleExitSoonerProof.cs
    /// on level 16 (its output folder: OzShuttleExitSoonerProof.Folder(), built at run time from the project's place).</summary>
    public static class OzShuttleExitSoonerMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K426 Exit Sooner Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K426 exit sooner check (DV118)", () => OzShuttleExitSoonerCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16")));

        [MenuItem("OpenZ/V1.1/Shuttle: K426 Exit Sooner Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K426 exit sooner proof: start level 16 in Play first"); return; }
            OzShuttleExitSoonerProof.Run();
            Debug.Log("[OpenZ] K426 exit sooner proof started; its report: OzShuttleExitSoonerProof.Report, the log and pictures in " + OzShuttleExitSoonerProof.Folder());
        }
    }
}
#endif
