#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// (Editor/OzShuttleQuickBoardCount.cs) and the Play proof View/OzShuttleQuickBoardProof.cs on level 16 (its output folder:
    /// OzShuttleQuickBoardProof.Folder(), built at run time from the project's place).</summary>
    public static class OzShuttleQuickBoardMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K422 Faster Boarding Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K422 faster boarding check", () => OzShuttleQuickBoardCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16")));

        [MenuItem("OpenZ/V1.1/Shuttle: K422 Faster Boarding Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K422 faster boarding proof: start level 16 in Play first (EASY, the SHUTTLE box ON)"); return; }
            OzShuttleQuickBoardProof.Run();
            Debug.Log("[OpenZ] K422 faster boarding proof started; its report: OzShuttleQuickBoardProof.Report, the log and pictures in " + OzShuttleQuickBoardProof.Folder());
        }
    }
}
#endif
