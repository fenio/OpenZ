#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K413 menu (the flying shuttle's smoke at its own height, not on the ground under it): the Play proof
    /// View/OzShipSmokeHeightProof.cs on level 16. Output folder: OzShipSmokeHeightProof.Folder() (built at run time from the project's
    /// place).</summary>
    public static class OzShipSmokeHeightMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K413 Smoke Height Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K413 smoke height proof: start level 16 in Play first"); return; }
            OzShipSmokeHeightProof.Run();
            Debug.Log("[OpenZ] K413 smoke height proof started; its report: OzShipSmokeHeightProof.Report, the log and pictures in " + OzShipSmokeHeightProof.Folder());
        }
    }
}
#endif
