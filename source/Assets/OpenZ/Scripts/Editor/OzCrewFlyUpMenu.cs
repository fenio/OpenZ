#if !OPENZ_HARNESS
using UnityEditor;

namespace OpenZ.EditorTools
{
    /// <summary>The K425 menu: the counted check of the crew bodies' kick out of Play (Editor/OzCrewFlyUpCheck.cs, View/OzCrewFlyPlan.cs).
    /// In Play every shuttle break writes its own line "[OpenZ] shuttle crew blown up first: ..." (View/OzShuttleCrewFall.FlyUp.cs).</summary>
    public static class OzCrewFlyUpMenu
    {
        [MenuItem("OpenZ/V1.1/Shuttle: K425 Crew Fly Up Check (no Play)")]
        static void CheckMenu() => OzV11Kit.RunMenu("K425 crew fly up check", OzCrewFlyUpCheck.Check);
    }
}
#endif
