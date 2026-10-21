namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's boarding file on its rear ramp (view only). The rules send the squad's figures to the ramp foot
    /// ZWorld.ShuttleFileGapSec (0.55 s) apart; up the ramp each one walks at <see cref="ShuttleRampWalk"/> (never slower than the
    /// APC's walk), the short step from the foot onto the ramp's tip at the same pace. Two figures in the file then keep about 1.15 m
    /// between them along the deck (about 1.0 m on the ground plane at the ramp's 30 degrees): a natural single file up the ramp, as
    /// on the APC. At the APC's 1.5 m/s with a fixed 0.3 s step onto the tip, the second figure reached the foot while the first was
    /// still on the ramp's lower end: 0.75 m apart. The rules' moments (each figure at the foot, the ramp's hold) are unchanged: the
    /// figures only spend less time on the ramp and sit down a little sooner.
    /// </summary>
    public partial class BoardingView
    {
        /// <summary>The walk up the shuttle's ramp (m/s). The walking clip follows the pace (UnitView's walking clip under 2.4 m/s).</summary>
        public const float ShuttleRampWalk = 2.35f;   // K422: 2.1 -> 2.35 m/s (still the walking clip, under 2.4 m/s); the file 0.5 s apart keeps 1.17 m on the ramp
    }
}
