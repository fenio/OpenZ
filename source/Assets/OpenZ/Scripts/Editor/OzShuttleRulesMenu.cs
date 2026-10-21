#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K256e menu (rule DV118, the shuttle's third round, the rules on the ground): the Play proof View/OzShuttleRulesProof.cs on
    /// level 16, started right after a FRESH level start (the parked heading, the boarding from the level's start camera, the hard obstacle,
    /// EXIT in line, the take-off waiting for robots beside the hull, the landing turn to a free side, the repair deck and a wreck on it).
    /// Its output folder: OzShuttleRulesProof.Folder() (built at run time from the project's place, nothing named here).</summary>
    public static class OzShuttleRulesMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256e Rules Proof (fresh Level 16 start, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256e rules proof: start level 16 in Play first (a fresh start: the proof begins with the parked ship)"); return; }
            OzShuttleRulesProof.Run();
            Debug.Log("[OpenZ] K256e rules proof started; its report: OzShuttleRulesProof.Report, the log and pictures in " + OzShuttleRulesProof.Folder());
        }
    }
}
#endif
