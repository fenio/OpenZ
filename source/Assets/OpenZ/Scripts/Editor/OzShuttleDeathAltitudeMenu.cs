#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// The K256h proof's menus (rule DV118, the shuttle's death at its drawn altitude; View/OzShuttleDeathAltitudeProof.cs), level 16 in
    /// Play: all seven kills (cruise: rifle fire, outright; hovering: real rifle fire, a rocket, a howitzer shell; low after take-off:
    /// rifle fire; landed: on the ground), each after a fresh level start, or only the two rifle kills in the air (cruise and real fire
    /// while hovering). The output folder is OzShuttleDeathAltitudeProof.Folder() (built at run time from the project's place).
    /// </summary>
    public static class OzShuttleDeathAltitudeMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K256h Death Altitude Proof (seven kills, fresh Level 16 starts, Play)")]
        static void ProofAll() => Start("all");
        [MenuItem("OpenZ/V1.1/Shuttle: K256h Death Altitude Proof (rifle kills in the air: C1 + H1, Play)")]
        static void ProofRifle() => Start("C1,H1");

        static void Start(string mode)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K256h death altitude proof: enter Play on level 16 first"); return; }
            OzShuttleDeathAltitudeProof.Run(mode);
            Debug.Log("[OpenZ] K256h death altitude proof " + mode + " started; its report: OzShuttleDeathAltitudeProof.Report, the log in " + OzShuttleDeathAltitudeProof.Folder());
        }
    }
}
#endif
