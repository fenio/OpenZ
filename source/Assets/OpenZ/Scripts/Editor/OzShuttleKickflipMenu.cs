#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K344 menu (the shuttle's kickflip easter egg, view only): the counted check out of Play (Editor/OzShuttleKickflipCount.cs:
    /// the gesture, the arming, the curve's smoothness, the lamps (no beam through the ship), the rules untouched with the egg on and off), the Play proof on Level 16
    /// (View/OzShuttleKickflipProof.cs: real double right-clicks through the game's input, the flip at 70 m and 35 m, near / slow double
    /// clicks, night, a new order in the pop) and the show for the 60 fps clip (the two flips only, no checks).</summary>
    public static class OzShuttleKickflipMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K344 Kickflip Check (no Play)")]
        static void Count() => OzV11Kit.RunMenu("K344 kickflip check", () => OzShuttleKickflipCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText("L16"), 240, System.IO.Path.GetDirectoryName(Application.dataPath)));   // lamp fix: the ship's model files for the lamp part

        [MenuItem("OpenZ/V1.1/Shuttle: K344 Kickflip Proof (Level 16, Play)")]
        static void Proof()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K344 kickflip proof: start Level 16 in Play first"); return; }
            OzShuttleKickflipProof.Run();
            Debug.Log("[OpenZ] K344 kickflip proof started; its report: OzShuttleKickflipProof.Report, the log and pictures in " + OzShuttleKickflipProof.Folder());
        }

        [MenuItem("OpenZ/V1.1/Shuttle: K344 Kickflip Show for the clip (Level 16, Play)")]
        static void Show()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K344 kickflip show: start Level 16 in Play first"); return; }
            OzShuttleKickflipProof.Show();
            Debug.Log("[OpenZ] K344 kickflip show started (two far double right-clicks, the camera following at 70 m, then 35 m): record it now; pictures in " + OzShuttleKickflipProof.Folder());
        }
    }
}
#endif
