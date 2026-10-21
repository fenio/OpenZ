#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K256f menu (rule DV118, the EMPTY shuttle and EXIT in the air): the Play proof View/OzShuttleEmptyProof.cs on level 16
    /// (the testing panel's empty row and drop, a squad ordered in, the EXIT button and cursor over ground, water and a robot, EXIT in the
    /// air, a lone pilot). Its output folder: OzShuttleEmptyProof.Folder() (built at run time from the project's place, nothing named here).
    /// The counted check is menu OpenZ/V1.1/Shuttle: K256f Empty Shuttle Check (Editor/OzShuttleEmptyCount.cs, out of Play).</summary>
    public static class OzShuttleEmptyMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256f Empty Shuttle Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256f empty shuttle proof: start level 16 in Play first"); return; }
            OzShuttleEmptyProof.Run();
            Debug.Log("[OpenZ] K256f empty shuttle proof started; its report: OzShuttleEmptyProof.Report, the log and pictures in " + OzShuttleEmptyProof.Folder());
        }
    }
}
#endif
