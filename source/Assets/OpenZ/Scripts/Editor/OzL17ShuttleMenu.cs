#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K444 menus (rule DV134, level 17's own shuttles): the counted check out of Play (Editor/OzL17ShuttleCount.cs; run it after
    /// a level 17 Play session and it also measures the scene's drawn props, which ZGame keeps for the parking rule) and the Play proof
    /// View/OzL17ShuttleProof.cs on level 17 (its output folder: OzL17ShuttleProof.Folder(), found at run time from the project's place).</summary>
    public static class OzL17ShuttleMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K444 Level 17 Shuttles Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K444 level 17 shuttles check (DV134)", () => OzL17ShuttleCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L17"), OzV11Kit.MapText("L16"),
            OzV11Kit.MapText("L02"), OzL17ShuttleCount.EditorKeepOut(), OzL17ShuttleCount.CpuSoakSec, OzL17ShuttleCount.CpuSeeds));

        [MenuItem("OpenZ/V1.1/Shuttle: K444 Level 17 Shuttles Proof (Level 17, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K444 level 17 shuttles proof: start level 17 in Play first (EASY or MEDIUM with the SHUTTLE box on shows both ships)"); return; }
            OzL17ShuttleProof.Run();
            Debug.Log("[OpenZ] K444 level 17 shuttles proof started; its report: OzL17ShuttleProof.Report, the log and pictures in " + OzL17ShuttleProof.Folder());
        }
    }
}
#endif
