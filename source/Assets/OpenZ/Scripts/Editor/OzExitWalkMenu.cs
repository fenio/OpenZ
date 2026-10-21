#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// out of Play (Editor/OzExitWalkCount.cs) and the Play proof View/OzExitWalkProof.cs on level 16 (its output folder:
    /// OzExitWalkProof.Folder(), built at run time from the project's place).</summary>
    public static class OzExitWalkMenu
    {
        [MenuItem("OpenZ/V1.1/Boarding: K441 Exit Walk Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K441 exit walk check (DV132)", () => OzExitWalkCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16")));

        [MenuItem("OpenZ/V1.1/Boarding: K441 Exit Walk Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K441 exit walk proof: start level 16 in Play first (the SHUTTLE box ON)"); return; }
            OzExitWalkProof.Run();
            Debug.Log("[OpenZ] K441 exit walk proof started; its report: OzExitWalkProof.Report, the log, the traces and pictures in " + OzExitWalkProof.Folder());
        }
    }
}
#endif
