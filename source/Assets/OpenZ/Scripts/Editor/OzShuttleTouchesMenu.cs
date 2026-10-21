#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K256g menu (rule DV118, the shuttle's touches): the Play proof View/OzShuttleTouchesProof.cs on level 16 (the Options row
    /// SHUTTLE ORDERS, the take-off and the landing with the ship's own shadow and the landing mark crossed, the mark's jitter and strength in
    /// a cruise, the red aim, the legs' springs at 70 m and close, the cursor and the clicks over water, rocks and free ground, a mixed group,
    /// ANYWHERE, a crash landing's light settle). Its output folder: OzShuttleTouchesProof.Folder() (built at run time from the project's place,
    /// nothing named here). The counted check is menu OpenZ/V1.1/Shuttle: K256g Shuttle Orders Check (Editor/OzShuttleTouchesCount.cs, out of Play).</summary>
    public static class OzShuttleTouchesMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256g Touches Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256g touches proof: start level 16 in Play first"); return; }
            OzShuttleTouchesProof.Run();
            Debug.Log("[OpenZ] K256g touches proof started; its report: OzShuttleTouchesProof.Report, the log and pictures in " + OzShuttleTouchesProof.Folder());
        }
    }
}
#endif
