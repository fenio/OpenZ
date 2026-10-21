#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K433 menu (View/UnitAim.BehindFix.cs: a turret with several barrels turns round to a target behind it): the Play proof
    /// View/OzTurretBehindProof.cs on any level (its output folder: OzTurretBehindProof.Folder(), built at run time from the project's place).</summary>
    public static class OzTurretBehindMenu
    {
        [MenuItem("OpenZ/V1.1/Combat: K433 Turret Behind Proof (any level, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K433 turret behind proof: start a level in Play first"); return; }
            OzTurretBehindProof.Run();
            Debug.Log("[OpenZ] K433 turret behind proof started (about 50 s); its report: OzTurretBehindProof.Report, the log and pictures in " + OzTurretBehindProof.Folder());
        }
    }
}
#endif
