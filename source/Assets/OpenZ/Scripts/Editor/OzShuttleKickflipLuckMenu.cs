#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K411 menu (the kickflip by itself, view only): the Play proof on Level 16 (View/OzShuttleKickflipLuckProof.cs: the chance
    /// at 100 % for the test, real single right-clicks and a Shift queue through the game's input, then back to 5 %). The counted part (R1 ..
    /// R10) runs inside the K344 check (OpenZ/V1.1/Shuttle: K344 Kickflip Check (no Play)).</summary>
    public static class OzShuttleKickflipLuckMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K411 Kickflip By Itself Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K411 kickflip-by-itself proof: start Level 16 in Play first"); return; }
            OzShuttleKickflipLuckProof.Run();
            Debug.Log("[OpenZ] K411 kickflip-by-itself proof started (chance 100 % for the test, back to 5 % at the end); its report: OzShuttleKickflipLuckProof.Report, the log and pictures in " + OzShuttleKickflipLuckProof.Folder());
        }
    }
}
#endif
