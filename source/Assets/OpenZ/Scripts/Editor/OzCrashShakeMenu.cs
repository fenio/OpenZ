#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K402 menu (the shuttle's crash landing shakes the camera when it is on screen, View/OzCrashLandingShake.cs): the Play
    /// proof View/OzCrashShakeProof.cs on level 16 (a crash on screen, one off screen, one with the shake option off, one in calm mode, a
    /// normal landing). Its output folder: OzCrashShakeProof.Folder() (built at run time from the project's place, nothing named here).</summary>
    public static class OzCrashShakeMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K402 Crash Landing Shake Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K402 crash landing shake proof: start level 16 in Play first"); return; }
            OzCrashShakeProof.Run();
            Debug.Log("[OpenZ] K402 crash landing shake proof started; its report: OzCrashShakeProof.Report, the log and the shake curves in " + OzCrashShakeProof.Folder());
        }
    }
}
#endif
