using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The break-up proof's kill stage (K1-K4, K256j's Proof K) with the shuttle's big blast (K396, test only; OzShuttleBreakup.BigBlast.cs):
    /// every kill plays the big second-type explosion in its air form with the heavy boom and the deep rumble, the pieces leave the blast
    /// fast, and the split's body panels' field is measured against the bigger throw (the kit's twin: the split's field about 1.5 x as wide as
    /// before: the live game's last Proof K measured 23-28 m against the old 12-15 m line, so now 26-48 m; with the big blast switched off the old 12-15.5 m).
    /// </summary>
    public sealed partial class OzShuttleBreakupGallery
    {
        /// <summary>The split's body panels' field (m): least and most, with the big blast on / off.</summary>
        static float SplitFieldMin => OzShuttleBreakup.BigBlast ? 26f : 12f;
        static float SplitFieldMax => OzShuttleBreakup.BigBlast ? 48f : 15.5f;
        static string SplitFieldText => OzShuttleBreakup.BigBlast ? "26-48 m (the big blast's throw)" : "12-15 m";

        /// <summary>One kill's big blast lines (after the stage's own lines): the explosion, its sounds, the launch.</summary>
        void BigBlastLines(string tag, string how, float far)
        {
            bool thisBreak = Mathf.Abs(OzShuttleBreakup.LastBigTime - OzShuttleBreakup.LastBreakAt) < 0.01f;
            bool air = OzShuttleBreakup.LastInAir;
            Log($"{tag} K396 the big blast: {OzShuttleBreakup.LastBigKind} U {OzShuttleBreakup.LastBigU:F1} m, boom {OzShuttleBreakup.LastBoom}, rumble {OzShuttleBreakup.LastRumble}, kick {OzShuttleBreakup.LastKick} (SCREEN SHAKE {(OzShakeGate.SettingOn ? "on" : "off / calm")}), pockets {OzShuttleBreakup.LastSecondaries}; launch {OzShuttleBreakup.LastLaunchText}; the farthest piece {far:F1} m");
            Check($"K396 {tag} the big var2 explosion plays ({OzShuttleBreakup.LastBigKind}, U {OzShuttleBreakup.LastBigU:F1} m) with the heavy boom ({OzShuttleBreakup.LastBoom}) and the deep rumble ({OzShuttleBreakup.LastRumble})",
                  thisBreak && OzShuttleBreakup.LastBigKind == (air ? "Big air" : "Big ground") && OzShuttleBreakup.LastBoom && OzShuttleBreakup.LastRumble);
            if (!air) return;
            float med = how == OzShuttleDeathPlan.Split ? OzShuttleDeathAltitudeProof.LaunchMedianSplit : how == OzShuttleDeathPlan.Crack ? OzShuttleDeathAltitudeProof.LaunchMedianCrack : OzShuttleDeathAltitudeProof.LaunchMedianBurst;
            Check($"K396 {tag} the pieces start with the blast's impact (launch median {OzShuttleBreakup.LastLaunchMedian:F1} m/s >= {med:F1}) and land within a sensible range (the farthest {far:F1} m <= {OzShuttleDeathAltitudeProof.FieldFarMax:F0} + the ship's own way)",
                  OzShuttleBreakup.LastLaunchMedian >= med && far <= OzShuttleDeathAltitudeProof.FieldFarMax + new Vector2(OzShuttleBreakup.LastBreakVel.x, OzShuttleBreakup.LastBreakVel.z).magnitude * 2.5f);
        }
    }
}
