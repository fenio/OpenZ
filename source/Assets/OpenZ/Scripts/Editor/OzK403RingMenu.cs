#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K403 menu: the Play proof View/OzK403RingProof.cs on level 16 (the selected shuttle's ring steady while a squad gets
    /// out and boards again, in Play and in a fixed-step recording). Output folder: OzK403RingProof.Folder().</summary>
    public static class OzK403RingMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K403 Ring Steady Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K403 ring proof: start level 16 in Play first (the shuttle on the ground)"); return; }
            OzK403RingProof.Run();
            Debug.Log("[OpenZ] K403 ring proof started; its report: OzK403RingProof.Report, the log, the frame tables and the strips in " + OzK403RingProof.Folder());
        }
    }
}
#endif
