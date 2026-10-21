using UnityEngine;

namespace OpenZ
{
    public partial class FlagView
    {
        /// <summary>The light flash of a captured building (the flag's sweep reaching a linked building): the team's flash colour, power
        /// 9, 13 m, 0.9 s, white-hot first 60 ms. Shared with the shuttle's capture (K389, View/OzShuttleCaptureFx.cs).</summary>
        public static void BuildingCaptureFlash(Vector3 at, Team team) => Fxm()?.AddLight(at, LightColor(team), 9f, 13f, 0.9f, 0.1f, true);

        /// <summary>The team's flash colour (light, brighter than the paint) of the flags' and buildings' capture flashes.</summary>
        public static Color CaptureLightColor(Team team) => LightColor(team);
    }
}
