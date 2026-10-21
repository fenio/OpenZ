using System.IO;
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K386 checks (menu OpenZ/V1.1/K386 ...), item 386: a crew figure grows with its vehicle while a new vehicle rolls out
    /// of the factory / fort door and sits on its seat in every frame. Reports, per-frame tables (csv) and pictures go to
    /// </summary>
    public static class OzK386Check
    {
        const string M = "OpenZ/V1.1/K386 ";
        static string Out { get { var p = Path.Combine(OpenZPaths.Records, "milestones", "k386_crew_rollout_check"); Directory.CreateDirectory(p); return p; } }

        static bool Ready()
        {
            if (!Application.isPlaying || !ZGame.I || ZGame.I.World == null) { Debug.LogWarning("[OpenZ K386] enter Play first (a level running, e.g. L02, fresh session)"); return false; }
            return true;
        }

        [MenuItem(M + "Crew Roll-Out: Every Vehicle From The Factory And The Fort, Per-Frame Seat vs Driver (Play)")]
        static void All() { if (Ready()) OzK386CrewRollProof.Run(Out, "all", false, false); }

        [MenuItem(M + "Jeep Roll-Out Strip AFTER: 6 Frames At 35 m + Per-Frame Table (Play)")]
        static void JeepAfter() { if (Ready()) OzK386CrewRollProof.Run(Out, "after", false, true); }

        [MenuItem(M + "Jeep Roll-Out Strip BEFORE: The Old Full-Size Driver, 6 Frames At 35 m (Play)")]
        static void JeepBefore() { if (Ready()) OzK386CrewRollProof.Run(Out, "before", true, true); }

        [MenuItem(M + "State")]
        static void State() =>
            Debug.Log($"[OpenZ K386] check {OzK386CrewRollProof.Report}; crew grows with its hull {(BoardingView.K386On ? "ON" : "OFF")}; drawn smaller with its hull {BoardingView.K386ScaledFrames} frames (smallest share {BoardingView.K386MinShare:0.000}); hull pushes followed {BoardingView.K386Follows} (largest {BoardingView.K386FollowMaxM:0.00} m); vehicles that grew out of a door {UnitView.ExitGrowStarted} (done {UnitView.ExitGrowDone}), door fits below full size {UnitView.ExitFitted} (smallest {UnitView.ExitFitMin:0.000})");
    }
}
