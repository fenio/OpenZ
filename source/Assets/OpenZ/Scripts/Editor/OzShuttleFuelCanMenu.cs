#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The K397 proof's menus (the rocket fuel cans that fly out of the dying shuttle; View/OzShuttleFuelCanProof.cs), level 16 in Play:
    /// the shuttle death proof's air burst (H2, hovering, a rocket) and ground kill (G1, landed), or all seven of its kills, each after a
    /// fresh level start, with the cans watched, counted and pictured from 35 m. The output folder is OzShuttleFuelCanProof.Folder().
    /// </summary>
    public static class OzShuttleFuelCanMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K397 Fuel Cans Proof (air burst H2 + ground G1, fresh Level 16 starts, Play)")]
        static void ProofTwo() => Start("H2,G1");
        [MenuItem("OpenZ/V1.1/Shuttle: K397 Fuel Cans Proof (all seven death proof kills, Play)")]
        static void ProofAll() => Start("all");

        static void Start(string kills)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K397 fuel cans proof: enter Play on level 16 first"); return; }
            OzShuttleFuelCanProof.Run(kills);
            Debug.Log("[OpenZ] K397 fuel cans proof " + kills + " started; its report: OzShuttleFuelCanProof.Report, the log and pictures in " + OzShuttleFuelCanProof.Folder());
        }
    }
}
#endif
