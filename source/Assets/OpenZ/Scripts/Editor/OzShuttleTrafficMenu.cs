#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K338 menu (rule DV118, several shuttles in the air): the Play proof View/OzShuttleTrafficProof.cs on any level (the
    /// testing panel's drop of 2 red + 2 blue shuttles, they swap sides, a swarm to one point, the same at night). Its output folder:
    /// OzShuttleTrafficProof.Folder() (built at run time from the project's place, nothing named here). The counted check is menu
    /// OpenZ/V1.1/Shuttle: K338 Air Traffic Check (Editor/OzShuttleTrafficCount.cs, out of Play).</summary>
    public static class OzShuttleTrafficMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K338 Air Traffic Proof (any level, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K338 air traffic proof: start a level in Play first"); return; }
            OzShuttleTrafficProof.Run();
            Debug.Log("[OpenZ] K338 air traffic proof started; its report: OzShuttleTrafficProof.Report, the log and pictures in " + OzShuttleTrafficProof.Folder());
        }
    }
}
#endif
