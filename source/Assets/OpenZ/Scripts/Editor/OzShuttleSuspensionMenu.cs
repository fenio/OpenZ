#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K256g3 menu (rule DV118, the shuttle's visible leg suspension): the Play proof View/OzShuttleSuspensionProof.cs on level 16
    /// (a normal landing at 70 m and at 35 m, a take-off, a crash landing and the repair station's deck, each with a 10-frame strip). Its
    /// output folder: OzShuttleSuspensionProof.Folder() (built at run time from the project's place, nothing named here).</summary>
    public static class OzShuttleSuspensionMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256g3 Suspension Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256g3 suspension proof: start level 16 in Play first"); return; }
            OzShuttleSuspensionProof.Run();
            Debug.Log("[OpenZ] K256g3 suspension proof started; its report: OzShuttleSuspensionProof.Report, the log and pictures in " + OzShuttleSuspensionProof.Folder());
        }
    }
}
#endif
