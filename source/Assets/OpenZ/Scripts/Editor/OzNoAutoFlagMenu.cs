#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K438 menus (rule DV130, Sim/ZObject.NoAutoFlag.cs: a robot of the player's team under fire runs to no flag by itself; a flag
    /// the player clicked always): the counted check out of Play (Editor/OzNoAutoFlagCount.cs, headless, open ground) and the Play watch
    /// View/OzNoAutoFlagWatch.cs (a running level; its output folder: OzNoAutoFlagWatch.Folder(), built at run time from the project's place).</summary>
    public static class OzNoAutoFlagMenu
    {
        [MenuItem("OpenZ/V1.1/Robots: K438 No Flag Run Under Fire Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K438 no flag run of its own under fire check (DV130)", () => OzNoAutoFlagCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText));

        [MenuItem("OpenZ/V1.1/Robots: K438 No Flag Run Under Fire Watch (any level, Play)")]
        static void Watch()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K438 watch: start a level in Play first"); return; }
            OzNoAutoFlagWatch.Run();
            Debug.Log("[OpenZ] K438 watch started (judged after 3:00 of game time); its report: OzNoAutoFlagWatch.Report, the log in " + OzNoAutoFlagWatch.Folder());
        }
    }
}
#endif
