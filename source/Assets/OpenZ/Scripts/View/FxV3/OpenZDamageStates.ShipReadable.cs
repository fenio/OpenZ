namespace OpenZ
{
    /// <summary>
    /// K419a (K393's damage smoke on the shuttle, view only; the BATCH 3 pictures at 70 m
    /// The same effect as the other vehicles (OpenZFxPlayer.WreckSmoke, OpenZPyroFire's small fire, their thresholds and sounds),
    /// only sized and laid for a ship twice a tank's height:
    /// <list type="bullet">
    /// <item>every puff and flame tongue at the full size for the ship (ShipPointShare 1.0, was 0.85: x1.18; x1.96 a tank's);</item>
    /// <item>the beat a little closer (<see cref="ShipBeatK"/> x0.8: battered every 0.24-0.36 s, burning every 0.24 s; the others' 0.3-0.45
    /// / 0.3 s);</item>
    /// <item>in flight one trail puff each 0.34 m of a point's way (was 0.4).</item>
    /// </list>
    /// About 1.7 x the smoke area per second of before, from the same 2-3 points on the ship's top; LOW still thins the extras as before.
    /// K413 (the smoke laid after the drawn pose) is what puts it on the hull instead of on the ground under it: this builds on it.
    /// </summary>
    public partial class OpenZDamageStates
    {
        /// <summary>The shuttle's beat interval x this (the others' beat x1).</summary>
        public static float ShipBeatK = 0.8f;
    }
}
