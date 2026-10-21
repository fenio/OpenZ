using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K394 checks (menu OpenZ/V1.1/K394 ...), item 394: every drawn round of a fort's roof defender leaves his muzzle
    /// and flies to its target (straight at the flying shuttle; over the fort's real parapet only where the straight line would run
    /// are staged for the run).
    /// </summary>
    public static class OzK394Check
    {
        const string M = "OpenZ/V1.1/K394 ";
        static string Out { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "k394_roof_rounds"); Directory.CreateDirectory(p); return p; } }

        static bool Ready()
        {
            if (!Application.isPlaying || !ZGame.I || ZGame.I.World == null) { Debug.LogWarning("[OpenZ K394] enter Play first (a level with forts running, e.g. L16, L02 or L17, fresh session)"); return false; }
            return true;
        }

        [MenuItem(M + "Roof Rounds AFTER: Shuttle By The Blue Fort, 20 Sniper Rounds + Pictures (Play)")]
        static void After() { if (Ready()) OzK394RoofShotLog.Run(Out, "after_" + ZGame.I.level, false); }

        [MenuItem(M + "Roof Rounds BEFORE: The Old Bent Path, Same Stage (Play)")]
        static void Before() { if (Ready()) OzK394RoofShotLog.Run(Out, "before_" + ZGame.I.level, true); }

        [MenuItem(M + "State")]
        static void State() =>
            Debug.Log($"[OpenZ K394] check {OzK394RoofShotLog.Report}; roof rounds over the real parapet {(OzK394RoofShots.On ? "ON" : "OFF")}; roof rounds {OzK394RoofShots.RoofRounds}, over the wall {OzK394RoofShots.OverWall}, straight {OzK394RoofShots.Straight}; rounds bent in all {ShotClearance.RoundsBent}");
    }
}
