#if !OPENZ_HARNESS
using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>The K434 menus (rule DV127, Sim/ZBot.EasySlow.cs: the EASY computer takes its big decisions 30 % slower): the counted check out
    /// of Play (Editor/OzEasySlowCount.cs, headless on levels 2, 16 and 17) and the Play watch View/OzEasySlowWatch.cs (a level started on EASY;
    /// its output folder: OzEasySlowWatch.Folder(), built at run time from the project's place).</summary>
    public static class OzEasySlowMenu
    {
        [MenuItem("OpenZ/V1.1/CPU: K434 Easy Slower Decisions Check (out of Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K434 easy computer slower decisions check (DV127)", () => OzEasySlowCount.Check(OzV11Kit.SettingsText, OzV11Kit.MapText));

        [MenuItem("OpenZ/V1.1/CPU: K434 Easy Slower Decisions Watch (any level on EASY, Play)")]
        static void Watch()
        {
            if (!Application.isPlaying) { Debug.LogWarning("[OpenZ] K434 easy computer watch: start a level on EASY in Play first (right at its start)"); return; }
            OzEasySlowWatch.Run();
            Debug.Log("[OpenZ] K434 easy computer watch started; its report: OzEasySlowWatch.Report, the log in " + OzEasySlowWatch.Folder());
        }
    }
}
#endif
