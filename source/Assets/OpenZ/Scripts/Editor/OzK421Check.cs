using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Version 1.1 kit K421 (menu OpenZ/V1.1/K421 ...): the roof-shot rule of K394 knows a fort's roof defender by who he is, so his
    /// rounds keep the roof path however high he holds his rifle; tower guns and the fort's big gun keep the old test. The Play check is
    /// K394's (OpenZ/V1.1/K394 Roof Rounds AFTER); this menu prints the K421 counts after it.
    /// </summary>
    public static class OzK421Check
    {
        [MenuItem("OpenZ/V1.1/K421 Roof Defenders: State")]
        static void State() =>
            Debug.Log($"[OpenZ K421] roof defenders known by the man {(OzK421RoofDefenders.On ? "ON" : "OFF")} (K394 {(OzK394RoofShots.On ? "ON" : "OFF")}); " +
                      $"his rounds known by the man {OzK421RoofDefenders.Matches} (outside the K394 deck band {OzK421RoofDefenders.OverBand}), " +
                      $"refused as a gun's round {OzK421RoofDefenders.GunRefused}; last outside the band: {OzK421RoofDefenders.LastOverBand}; " +
                      $"K394 roof rounds {OzK394RoofShots.RoofRounds}, over the wall {OzK394RoofShots.OverWall}, straight {OzK394RoofShots.Straight}");
    }
}
