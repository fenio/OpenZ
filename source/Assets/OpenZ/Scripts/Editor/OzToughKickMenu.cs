using UnityEditor;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The Tough's heavier rocket kick (View/ToughKick.cs, owner items 307 + 308): its report, the A/B switches and a Play stage
    /// (the Robot Types Show with the Tough only: a red Tough squad firing at harmless blue robots, then at a blue tank, near the camera).
    /// View only: nothing here changes the simulation.
    /// </summary>
    public static class OzToughKickMenu
    {
        const string Root = "OpenZ/Tough kick/";
        const string OptOn = Root + "Options/Heavier kick", OptFeet = Root + "Options/Boots planted (no body jolt)";

        [MenuItem(Root + "Report", false, 1)]
        static void Report() => Debug.Log("[OpenZ] " + ToughKick.Report());

        [MenuItem(Root + "Reset counters", false, 2)]
        static void ResetCounters() { ToughKick.ResetCounters(); Debug.Log("[OpenZ] ToughKick counters reset"); }

        [MenuItem(Root + "Show: Toughs firing (Play mode, day)", false, 20)]
        static void Show()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] Tough kick show: enter Play mode on L02 or L17 first (and close the level-start briefing)"); return; }
            if (RobotTypesShow.Running) { Debug.LogWarning("[OpenZ] Tough kick show: a Robot Types Show is running already"); return; }
            var before = RobotTypesShow.Order;
            RobotTypesShow.Order = new[] { "tough" };
            ToughKick.ResetCounters();
            if (!RobotTypesShow.Begin(false)) { RobotTypesShow.Order = before; return; }
            bool seen = false;
            EditorApplication.CallbackFunction back = null;
            back = () =>
            {   // the show reads Order while it runs: the full order comes back once it is over
                if (RobotTypesShow.Running) { seen = true; return; }
                if (!seen && Application.isPlaying) return;
                RobotTypesShow.Order = before; EditorApplication.update -= back;
                Debug.Log("[OpenZ] Tough kick show over: " + ToughKick.Report());
            };
            EditorApplication.update += back;
            Debug.Log($"[OpenZ] Tough kick show started ({RobotTypesShow.SegmentSeconds:F0} s near the camera focus); the report follows at its end");
        }

        [MenuItem(OptOn, false, 40)] static void ToggleOn() { ToughKick.On = !ToughKick.On; Debug.Log("[OpenZ] Tough kick " + (ToughKick.On ? "ON (heavier)" : "OFF (as before)")); }
        [MenuItem(OptOn, true)] static bool ToggleOnV() { Menu.SetChecked(OptOn, ToughKick.On); return true; }
        [MenuItem(OptFeet, false, 41)] static void ToggleFeet() { ToughKick.PlantFeet = !ToughKick.PlantFeet; Debug.Log("[OpenZ] Tough boots planted " + (ToughKick.PlantFeet ? "ON" : "OFF (UnitView's body jolt as before)")); }
        [MenuItem(OptFeet, true)] static bool ToggleFeetV() { Menu.SetChecked(OptFeet, ToughKick.PlantFeet); return true; }
    }
}
