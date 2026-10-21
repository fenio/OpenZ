#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K389 menu (the shuttle's capture flash): the Play proof View/OzShuttleCaptureFxProof.cs on level 16 (a grunt squad takes
    /// an empty ship seen at 35 m and at 70 m: the flash, the lamps' start, the paint's ease, the capture cue; ALAN &amp; BRAD leave a third
    /// ship: the lights fade out). Its output folder: OzShuttleCaptureFxProof.Folder() (built at run time from the project's place).</summary>
    public static class OzShuttleCaptureFxMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K389 Capture Flash Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K389 capture flash proof: start level 16 in Play first"); return; }
            OzShuttleCaptureFxProof.Run();
            Debug.Log("[OpenZ] K389 capture flash proof started; its report: OzShuttleCaptureFxProof.Report, the log and pictures in " + OzShuttleCaptureFxProof.Folder());
        }
    }
}
#endif
