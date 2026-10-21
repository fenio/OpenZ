#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K256d2 menu (the shuttle's nose searchlight never shines through its own ship; view only): the counted check out of Play
    /// (Editor/OzShuttleNoseConeCount.cs: the edge, every aim the rules can give, the limit's shape, the lock at the cone, 30 / 60 / 144 fps,
    /// the night lights' own counts) and the Play proof on Level 16 at night (View/OzShuttleNoseConeProof.cs: an enemy behind the side,
    /// the beam held at the edge, its pictures).</summary>
    public static class OzShuttleNoseConeMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256d2 Nose Light Clear Cone Check (no Play)")]
        static void Count() => OzV11Kit.RunMenu("K256d2 nose light clear cone check", () => OzShuttleNoseConeCount.Check(System.IO.Path.GetDirectoryName(Application.dataPath)));

        [MenuItem("OpenZ/V1.1/Shuttle: K256d2 Nose Light Clear Cone Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256d2 nose light proof: start Level 16 in Play first"); return; }
            OzShuttleNoseConeProof.Run();
            Debug.Log("[OpenZ] K256d2 nose light clear cone proof started; its report: OzShuttleNoseConeProof.Report, the log and pictures in " + OzShuttleNoseConeProof.Folder());
        }
    }
}
#endif
