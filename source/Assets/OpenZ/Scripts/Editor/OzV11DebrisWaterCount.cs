using UnityEditor;
using UnityEngine;

namespace OpenZ.EditorTools
{
    /// <summary>
    /// Menus of the water landing check (the check itself runs in Play: View/OzWaterLandingProof.cs).
    /// OpenZ/V1.1/Count Pieces Falling Into Water (b27, Play mode): walks the level's drawn water against the rule, then throws
    /// one of every piece kind into water, into a shallow stream or ford and onto dry ground on the level that is running and
    /// counts; about four minutes; the result is the last line of the Console text
    /// "[OpenZ V1.1] b27 debris into water: PASS n/n on <level>" (also OzWaterLandingProof.Report and count.txt in its folder).
    /// OpenZ/V1.1/Pictures Turret And Wreck Piece In Water (b27, Play mode): five full-screen pictures from the normal camera
    /// and, for the one who checks, two looks from the camera's lowest height.
    /// </summary>
    public static class OzV11DebrisWaterCount
    {
        [MenuItem("OpenZ/V1.1/Count Pieces Falling Into Water (b27, Play mode)")]
        static void Count() => Debug.Log("[OpenZ V1.1] b27 debris into water: " + OzWaterLandingProof.Run(false));

        [MenuItem("OpenZ/V1.1/Pictures Turret And Wreck Piece In Water (b27, Play mode)")]
        static void Pictures() => Debug.Log("[OpenZ V1.1] b27 debris into water: " + OzWaterLandingProof.Run(true));
    }
}
