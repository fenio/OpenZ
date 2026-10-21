#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K430b menus (UI/OzShuttleAirOffers.cs: no EXIT and no boarding offered while the shuttle is up in the air): the counted
    /// check out of Play (Editor/OzShuttleAirCursorsCount.cs, headless on level 16's data) and the Play proof View/OzShuttleAirCursorsProof.cs
    /// on level 16 (its output folder: OzShuttleAirCursorsProof.Folder(), built at run time from the project's place).</summary>
    public static class OzShuttleAirCursorsMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K430b Air Cursors Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K430b air cursors check", () => OzShuttleAirCursorsCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16")));

        [MenuItem("OpenZ/V1.1/Shuttle: K430b Air Cursors Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K430b air cursors proof: start level 16 in Play first"); return; }
            OzShuttleAirCursorsProof.Run();
            Debug.Log("[OpenZ] K430b air cursors proof started; its report: OzShuttleAirCursorsProof.Report, the log and pictures in " + OzShuttleAirCursorsProof.Folder());
        }
    }
}
#endif
