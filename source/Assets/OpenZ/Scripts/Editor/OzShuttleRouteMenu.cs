#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K387 menu (rule DV118, view only: the route of a selected shuttle from the ship in the sky, descending smoothly to its
    /// goal on the ground): the Play proof View/OzShuttleRouteProof.cs on level 16. Output folder: OzShuttleRouteProof.Folder() (built at
    /// run time from the project's place).</summary>
    public static class OzShuttleRouteMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K387 Route Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K387 route proof: start level 16 in Play first"); return; }
            OzShuttleRouteProof.Run();
            Debug.Log("[OpenZ] K387 route proof started; its report: OzShuttleRouteProof.Report, the log and pictures in " + OzShuttleRouteProof.Folder());
        }
    }
}
#endif
