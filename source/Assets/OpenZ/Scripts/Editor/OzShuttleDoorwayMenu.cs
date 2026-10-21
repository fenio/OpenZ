#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K418 menus (rule DV126, the shuttle destroyed while a squad walks in or out, and every other in-between moment): the counted
    /// check out of Play (Editor/OzShuttleDoorwayCount.cs) and the Play proof View/OzShuttleDoorwayProof.cs on level 16 (its output folder:
    /// OzShuttleDoorwayProof.Folder(), built at run time from the project's place).</summary>
    public static class OzShuttleDoorwayMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K418 Death Edge Cases Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K418 death edge-case check (DV126)", () => OzShuttleDoorwayCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16")));

        [MenuItem("OpenZ/V1.1/Shuttle: K418 Death Edge Cases Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K418 death edge-case proof: start level 16 in Play first (EASY, the SHUTTLE box ON)"); return; }
            OzShuttleDoorwayProof.Run();
            Debug.Log("[OpenZ] K418 death edge-case proof started; its report: OzShuttleDoorwayProof.Report, the log and pictures in " + OzShuttleDoorwayProof.Folder());
        }
    }
}
#endif
