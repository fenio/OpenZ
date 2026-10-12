using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// the Options row MECH FOOT IK. Both menus write the same saved setting as the row (MechWalk.On = oz_mech_foot_ik, default ON) and
    /// work in edit mode and in Play (live: the next LateUpdate switches). The checkmark shows the saved state.
    /// OFF = the previous mech walk without the foot lock (MechWalk.Legacy + the Travel-timed steps, prints and dust).
    /// </summary>
    public static class OpenZMechFootIK
    {
        const string OnPath = "OpenZ/Mech/Foot IK On", OffPath = "OpenZ/Mech/Foot IK Off";
        const string StatsPath = "OpenZ/Mech/Foot IK slide stats (log, then reset)";

        [MenuItem(OnPath)]
        static void TurnOn() => Set(true);

        [MenuItem(OnPath, true)]
        static bool TurnOnCheck() { Menu.SetChecked(OnPath, MechWalk.On); return true; }

        [MenuItem(OffPath)]
        static void TurnOff() => Set(false);

        [MenuItem(OffPath, true)]
        static bool TurnOffCheck() { Menu.SetChecked(OffPath, !MechWalk.On); return true; }

        static void Set(bool v)
        {
            MechWalk.On = v;                                                                          // logs "[OpenZ Options] mech foot IK: ..."
            Menu.SetChecked(OnPath, v); Menu.SetChecked(OffPath, !v);
            Debug.Log($"[OpenZ Mech] foot IK {(v ? "ON" : "OFF")} from the editor menu ({(Application.isPlaying ? "live in this Play session" : "saved for the next Play")}; the Options row shows the same setting)");
        }

        /// <summary>The A/B numbers: one line per walked part (flat, slope, bridge), then a clean count for the next part.</summary>
        [MenuItem(StatsPath)]
        static void LogStats()
        {
            Debug.Log(MechWalk.SlideStats + (Application.isPlaying ? "" : " (not in Play: nothing walked)"));
            MechWalk.ResetSlide();
        }
    }
}
