#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K443 menus (rule DV131, Sim/ZWorld.ShuttleHp.cs: every shuttle starts with the light tank's hit points x 1.2): the counted
    /// check out of Play (Editor/OzShuttleHpCount.cs) and the Play proof View/OzShuttleHpProof.cs on level 16 (its output folder:
    /// OzShuttleHpProof.Folder(), built at run time from the project's place).</summary>
    public static class OzShuttleHpMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K443 More HP Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K443 shuttle more hit points check (DV131)", () => OzShuttleHpCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), OzV11Kit.MapText("L02")));

        [MenuItem("OpenZ/V1.1/Shuttle: K443 More HP Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K443 shuttle hit points proof: start level 16 in Play first (EASY or MEDIUM, the SHUTTLE box ON)"); return; }
            OzShuttleHpProof.Run();
            Debug.Log("[OpenZ] K443 shuttle hit points proof started; its report: OzShuttleHpProof.Report, the log and pictures in " + OzShuttleHpProof.Folder());
        }
    }
}
#endif
