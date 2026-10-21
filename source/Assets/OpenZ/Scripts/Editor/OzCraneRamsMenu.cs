#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>K342: the menu of the repair station crane rams' Play proof (View/OzCraneRamsProof.cs). Output: OzCraneRamsProof.Folder().</summary>
    public static class OzCraneRamsMenu
    {
        [MenuItem("OpenZ/V1.1/Station: K342 Crane Rams Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K342 crane rams proof: enter Play on level 16 first (the option SHUTTLE on)"); return; }
            OzCraneRamsProof.Run();
            Debug.Log("[OpenZ] K342 crane rams proof started; its report: OzCraneRamsProof.Report, the log and pictures in " + OzCraneRamsProof.Folder());
        }
    }
}
#endif
