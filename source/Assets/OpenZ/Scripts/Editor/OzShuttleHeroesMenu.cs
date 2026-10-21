#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K256f2 menu (rule DV118, heroes never stay stuck in a shuttle, no vehicle boards one): the Play proof
    /// View/OzShuttleHeroesProof.cs on level 16 (ALAN &amp; BRAD into an empty ship and out again, the ship empty and dark, the cursor with
    /// every vehicle and gun selected over a shuttle, a grunt squad taking the emptied ship). Its output folder: OzShuttleHeroesProof.Folder()
    /// (built at run time from the project's place, nothing named here). The counted check is menu OpenZ/V1.1/Shuttle: K256f2 Heroes Exit
    /// Check (Editor/OzShuttleHeroesCount.cs, out of Play).</summary>
    public static class OzShuttleHeroesMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256f2 Heroes Exit Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256f2 heroes exit proof: start level 16 in Play first"); return; }
            OzShuttleHeroesProof.Run();
            Debug.Log("[OpenZ] K256f2 heroes exit proof started; its report: OzShuttleHeroesProof.Report, the log and pictures in " + OzShuttleHeroesProof.Folder());
        }
    }
}
#endif
