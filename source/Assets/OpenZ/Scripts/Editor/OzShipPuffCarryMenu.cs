#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K432 menu (a flying, burning shuttle's puffs appear on its hull): the Play proof View/OzShipPuffCarryProof.cs on
    /// level 16. Output folder: OzShipPuffCarryProof.Folder() (built at run time from the project's place).</summary>
    public static class OzShipPuffCarryMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K432 Puffs On The Hull Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K432 puffs on the hull proof: start level 16 in Play first"); return; }
            OzShipPuffCarryProof.Run();
            Debug.Log("[OpenZ] K432 puffs on the hull proof started; its report: OzShipPuffCarryProof.Report, the log and pictures in " + OzShipPuffCarryProof.Folder());
        }
    }
}
#endif
