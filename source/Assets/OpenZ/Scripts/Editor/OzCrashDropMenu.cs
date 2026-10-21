#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K408 menu (the shuttle's crash landing comes down as a real crash, View/OzPlayableShuttle.CrashDrop.cs): the Play proof
    /// View/OzCrashDropProof.cs on level 16 (a crash on screen, a normal landing). Its output folder: OzCrashDropProof.Folder() (built at
    /// run time from the project's place, nothing named here).</summary>
    public static class OzCrashDropMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K408 Faster Crash Landing Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K408 faster crash landing proof: start level 16 in Play first"); return; }
            OzCrashDropProof.Run();
            Debug.Log("[OpenZ] K408 faster crash landing proof started; its report: OzCrashDropProof.Report, the log, the height curves and the pictures in " + OzCrashDropProof.Folder());
        }
    }
}
#endif
