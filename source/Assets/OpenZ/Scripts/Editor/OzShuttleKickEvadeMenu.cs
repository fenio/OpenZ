#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K436 menu (the kickflip as a real manoeuvre: the dash, the harder target, the evasive self-flip, the calm frame): the
    /// counted check out of Play (Editor/OzShuttleKickCount.cs, also the DV129 row) and the Play proof on Level 16
    /// (View/OzShuttleKickEvadeProof.cs).</summary>
    public static class OzShuttleKickEvadeMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K436 Kickflip Evade Check (no Play)")]
        static void Count() => OzV11Kit.RunMenu("K436 kickflip evade check", () => OzShuttleKickCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), 3));

        [MenuItem("OpenZ/V1.1/Shuttle: K436 Kickflip Evade Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K436 kickflip evade proof: start Level 16 in Play first"); return; }
            OzShuttleKickEvadeProof.Run();
            Debug.Log("[OpenZ] K436 kickflip evade proof started; its report: OzShuttleKickEvadeProof.Report, the log and pictures in " + OzShuttleKickEvadeProof.Folder());
        }
    }
}
#endif
