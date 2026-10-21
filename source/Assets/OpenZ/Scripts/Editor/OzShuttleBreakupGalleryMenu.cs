#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The K256e break-up proof's menus (rule DV118, the shuttle's third round; View/OzShuttleBreakupGallery.cs), level 16 in Play:
    /// G = the blast weapons at the hovering ship (no ground ring under it, the grenades and the fort's gun on the hull), K = a tank wreck
    /// to compare with and four kills that break in three ways (each after a fresh level start), GK = both. The output folder is
    /// OzShuttleBreakupGallery.Folder() (built at run time from the project's place).
    /// </summary>
    public static class OzShuttleBreakupGalleryMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256e Break-up Proof G (blast weapons at the hovering ship, Level 16, Play)")]
        static void ProofG() => Start("G");
        [MenuItem("OpenZ/V1.1/Shuttle: K256e Break-up Proof K (four kills, fresh Level 16 starts, Play)")]
        static void ProofK() => Start("K");
        [MenuItem("OpenZ/V1.1/Shuttle: K256e Break-up Proof GK (both, Level 16, Play)")]
        static void ProofGK() => Start("GK");

        static void Start(string mode)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256e break-up proof: enter Play on level 16 first"); return; }
            OzShuttleBreakupGallery.Run(mode);
            Debug.Log("[OpenZ] K256e break-up proof " + mode + " started; its report: OzShuttleBreakupGallery.Report, the logs in " + OzShuttleBreakupGallery.Folder());
        }
    }
}
#endif
