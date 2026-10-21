using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K418 P5 (DV126, view only): a robot saved from a destroyed shuttle stands on its rules place. The view's own spacing (SolveSeparation)
    /// pushes overlapping figures apart by a drawn offset that drifts back only while the robot walks.
    /// The squad let out of a shuttle stands on its back slots next to each other and next to the ship; when the ship was destroyed with
    /// two of them still inside, the two saved ones were left standing up to 1.07 m off their rules places for good (run 28, P5).
    /// <see cref="SettleOnSimSpot"/> lets the drawn offset drift back for a while although the robot stands: it steps back onto its place
    /// (the walk is drawn: the drawn body moves faster than the walk threshold), at the spacing's own pace (0.8 m/s), and only while
    /// nothing pushes it (no overlap now). BoardingView calls it for each saved robot of a destroyed shuttle's ramp file.
    /// </summary>
    public partial class UnitView
    {
        float settleUntil = -1f;
        /// <summary>Proof: settles asked for (all robots).</summary>
        public static int Settles;

        /// <summary>The drawn spacing offset drifts back onto the rules place for the next sec seconds, also while the robot stands.</summary>
        public void SettleOnSimSpot(float sec)
        {
            settleUntil = Mathf.Max(settleUntil, Time.time + Mathf.Max(0f, sec)); Settles++;
        }

        bool SettleNow() => settleUntil > Time.time;
    }
}
