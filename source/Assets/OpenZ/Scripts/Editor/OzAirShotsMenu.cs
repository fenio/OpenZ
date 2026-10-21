#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// K337's menus (the rounds fired at the flying shuttle that miss it; View/OzAirShots.cs): in Play on level 16, the log (View/OzAirShotsLog.cs:
    /// the live projectiles, the memory, the kit's counts, pictures of the first air bursts of misses) with the K256b weapons gallery G (every enemy
    /// weapon in turn at the flying ship, then the four kills), or the log alone for any fight. The output folder is OzAirShotsLog.Folder().
    /// </summary>
    public static class OzAirShotsMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K337 Missed Air Shots Proof (weapons gallery G + the log, Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K337 proof: enter Play on level 16 first"); return; }
            OzAirShotsLog.Begin(180f);
            OzShuttlePolishProof.OutDir = "";
            OzShuttlePolishProof.Run("G");
            Debug.Log("[OpenZ] K337 proof started (the K256b gallery G + the log); the log and its summary in " + OzAirShotsLog.Folder());
        }

        [MenuItem("OpenZ/V1.1/Shuttle: K337 Missed Air Shots Log only (3 min or more, Play)")]
        static void LogOnly()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K337 log: enter Play first"); return; }
            OzAirShotsLog.Begin(180f);
            Debug.Log("[OpenZ] K337 log started (at least 180 s); the log and its summary in " + OzAirShotsLog.Folder());
        }
    }
}
#endif
