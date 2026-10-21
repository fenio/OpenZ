#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K393 menu (the shuttle's damage smoke and fire like the other vehicles, trailing in flight, out after a repair): the
    /// Play proof View/OzShuttleDamageSmokeProof.cs on level 16. Output folder: OzShuttleDamageSmokeProof.Folder() (built at run time from
    /// the project's place).</summary>
    public static class OzShuttleDamageSmokeMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K393 Damage Smoke Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K393 damage smoke proof: start level 16 in Play first"); return; }
            OzShuttleDamageSmokeProof.Run();
            Debug.Log("[OpenZ] K393 damage smoke proof started; its report: OzShuttleDamageSmokeProof.Report, the log and pictures in " + OzShuttleDamageSmokeProof.Folder());
        }
    }
}
#endif
