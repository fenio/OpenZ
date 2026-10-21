using UnityEditor;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// switch. View only: nothing here changes the simulation. Use with the K306 stage (menu OpenZ/V1.1/Tough: K306 Stage 2 Toughs vs
    /// grunts (Play)): reset the counters, stage, wait for the fight's end, report.
    /// </summary>
    public static class OzToughAimEaseMenu
    {
        const string Root = "OpenZ/V1.1/Tough: K324 Aim Ease ";
        const string OptOn = Root + "On (A/B)";

        [MenuItem(Root + "Report (Play)", false, 1)]
        static void Report() => Debug.Log("[OpenZ V1.1] K324 " + ToughAimEase.Report());

        [MenuItem(Root + "Reset counters", false, 2)]
        static void ResetCounters() { ToughAimEase.ResetCounters(); Debug.Log("[OpenZ V1.1] K324 Tough aim ease counters reset"); }

        [MenuItem(OptOn, false, 3)]
        static void Toggle() { ToughAimEase.On = !ToughAimEase.On; Debug.Log("[OpenZ V1.1] K324 Tough aim ease " + (ToughAimEase.On ? "ON (eased, held through a kill)" : "OFF (the raw aim of before)")); }
        [MenuItem(OptOn, true)] static bool ToggleV() { Menu.SetChecked(OptOn, ToughAimEase.On); return true; }
    }
}
