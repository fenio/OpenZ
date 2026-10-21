#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K388 menu (rule DV118, the shuttle's legs fold in under the belly in flight): the Play proof View/OzShuttleLegsFoldProof.cs on level 16 (a
    /// take-off, the cruise, a descent and touchdown at 70 m and at 35 m with picture strips, a hover over water, a crash landing). Its output
    /// folder: OzShuttleLegsFoldProof.Folder() (built at run time from the project's place, nothing named here).</summary>
    public static class OzShuttleLegsFoldMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K388 Legs Fold Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K388 legs fold proof: start level 16 in Play first"); return; }
            OzShuttleLegsFoldProof.Run();
            Debug.Log("[OpenZ] K388 legs fold proof started; its report: OzShuttleLegsFoldProof.Report, the log and pictures in " + OzShuttleLegsFoldProof.Folder());
        }
    }
}
#endif
