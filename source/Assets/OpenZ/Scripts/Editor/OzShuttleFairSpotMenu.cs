#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K437 menus (rule DV128, the shuttle lands where it really fits): the counted check out of Play (Editor/OzShuttleFairSpotCount.cs)
    /// and the Play proof View/OzShuttleFairSpotProof.cs on level 16 (its output folder: OzShuttleFairSpotProof.Folder(), built at run time from
    /// the project's place).</summary>
    public static class OzShuttleFairSpotMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K437 Fair Landing Spot Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K437 fair landing spot check (DV128)", () => OzShuttleFairSpotCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), OzV11Kit.MapText("L02"), OzV11Kit.MapText("L17")));

        [MenuItem("OpenZ/V1.1/Shuttle: K437 Island Landing Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K437 island landing proof: start level 16 in Play first"); return; }
            OzShuttleFairSpotProof.Run();
            Debug.Log("[OpenZ] K437 island landing proof started; its report: OzShuttleFairSpotProof.Report, the log and pictures in " + OzShuttleFairSpotProof.Folder());
        }
    }
}
#endif
