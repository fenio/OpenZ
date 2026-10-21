using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Foot lock v2 (item 203.3): the switch and the Play check.
    /// "OpenZ/Legs/Foot lock v2 (oz_foot_lock_v2)": turns the switch on / off, now (RobotLegIK.FootLockV2, also in Play) and for every
    /// next start (PlayerPrefs oz_foot_lock_v2, 1 = on, the default). Off = the stance lock exactly as before.
    /// "OpenZ/Legs/Foot lock v2: Play check (watch only)": the same without moving any squad (the robots must already walk).
    /// </summary>
    public static class OzFootLockMenu
    {
        const string SwitchMenu = "OpenZ/Legs/Foot lock v2 (oz_foot_lock_v2)";

        [MenuItem(SwitchMenu)]
        static void Toggle()
        {
            bool on = !RobotLegIK.FootLockV2;
            RobotLegIK.FootLockV2 = on;
            PlayerPrefs.SetInt(RobotLegIK.FootLockV2Pref, on ? 1 : 0); PlayerPrefs.Save();
            Debug.Log("[FootLockV2] " + (on ? "ON: planted boots stay on their spot (foot lock v2)" : "OFF: the stance lock as before") + " (" + RobotLegIK.FootLockV2Pref + " = " + (on ? 1 : 0) + ", now and at every next start)");
        }

        [MenuItem(SwitchMenu, true)]
        static bool ToggleCheck() { Menu.SetChecked(SwitchMenu, RobotLegIK.FootLockV2); return true; }

        [MenuItem("OpenZ/Legs/Foot lock v2: Play check (8 s OFF, 8 s ON, walks squads)")]
        static void CheckDrive() => Run(true);

        [MenuItem("OpenZ/Legs/Foot lock v2: Play check (watch only)")]
        static void CheckWatch() => Run(false);

        static void Run(bool drive)
        {
            if (!Application.isPlaying) { Debug.LogWarning("[FootLockV2] enter Play first (camera on player squads)"); return; }
            OzFootLockProbe.Begin(8f, drive);
        }
    }
}
