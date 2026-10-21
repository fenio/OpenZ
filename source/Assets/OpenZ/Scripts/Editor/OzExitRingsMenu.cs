#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K431 menu (item 430, View/BoardingView.ExitRings.cs: the rings, frame and health bar of robots getting out of a carrier
    /// follow their drawn figures): the Play proof View/OzExitRingsProof.cs on level 16 (its output folder: OzExitRingsProof.Folder(), built
    /// at run time from the project's place).</summary>
    public static class OzExitRingsMenu
    {
        [MenuItem("OpenZ/V1.1/Boarding: K431 Exit Rings Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K431 exit rings proof: start level 16 in Play first"); return; }
            OzExitRingsProof.Run();
            Debug.Log("[OpenZ] K431 exit rings proof started; its report: OzExitRingsProof.Report, the log and pictures in " + OzExitRingsProof.Folder());
        }
    }
}
#endif
