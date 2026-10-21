#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K256g2 menu (rule DV118, the shuttle's landing mark on whatever is drawn, its self-shadow in the air, the hand-over at a
    /// kill in the air): the counted check out of Play (Editor/OzShuttleMarkCount.cs), the Play proof View/OzShuttleMarkProof.cs on level 16
    /// and its hand-over watch. Output folder: OzShuttleMarkProof.Folder() (built at run time from the project's place).</summary>
    public static class OzShuttleMarkMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256g2 Mark Check (out of Play)")]
        static void Count() { var r = OzShuttleMarkCount.Run(); Debug.Log("[OpenZ] K256g2 mark check: " + r); }

        [MenuItem("OpenZ/V1.1/Shuttle: K256g2 Mark Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256g2 mark proof: start level 16 in Play first"); return; }
            OzShuttleMarkProof.Run(false);
            Debug.Log("[OpenZ] K256g2 mark proof started; its report: OzShuttleMarkProof.Report, the log and pictures in " + OzShuttleMarkProof.Folder());
        }

        [MenuItem("OpenZ/V1.1/Shuttle: K256g2 Hand-over Watch (start or stop, Play)")]               // no "/" inside a menu name: Unity reads it as a submenu
        static void Watch()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256g2 hand-over watch: start level 16 in Play first"); return; }
            var on = Object.FindFirstObjectByType<OzShuttleMarkProof>();
            if (on) { OzShuttleMarkProof.stop = true; Debug.Log("[OpenZ] K256g2 hand-over watch stopped: " + OzShuttleMarkProof.Folder()); return; }
            OzShuttleMarkProof.stop = false; OzShuttleMarkProof.Run(true);
            Debug.Log("[OpenZ] K256g2 hand-over watch on: shoot the shuttle down in the air (or run the K256e Break-up Proof GK); the log in " + OzShuttleMarkProof.Folder());
        }
    }
}
#endif
