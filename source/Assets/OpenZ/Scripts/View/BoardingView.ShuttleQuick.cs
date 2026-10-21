using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// with the rules' new moments. To the ramp foot at the rules' ZWorld.ShuttleWalkMps (3.5 m/s: the running clip at x1.1, was 3.2); up and
    /// down the ramp at <see cref="ShuttleRampWalk"/> (2.35 m/s, the walking clip; was 2.1 up and about 1.55 down); along the bay's aisle at
    /// <see cref="ShuttleAisleWalk"/> (2.0 m/s, was the APC's 1.5); sitting down <see cref="ShuttleSitInSec"/>, up from the bench
    /// <see cref="ShuttleSitOutSec"/> + <see cref="ShuttleStandSec"/>, the doorway sill and the step off the ramp <see cref="ShuttleSillSec"/>.
    /// The rules' budgets (ShuttleRampInSec / ShuttleRampOutSec) and the K418 doorway moments still fit every walk exactly (BoardingView.Shuttle.cs,
    /// BoardingView.ShuttleDoorway.cs); the file keeps its single line (0.5 s apart: 1.17 m on the ramp). The APC keeps its own walks.
    /// </summary>
    public partial class BoardingView
    {
        /// <summary>K422: the bay's aisle walk (m/s), the sit down / up and stand (s), the doorway sill and the step off the ramp's tip (s).</summary>
        public const float ShuttleAisleWalk = 2.0f, ShuttleSitInSec = 0.35f, ShuttleSitOutSec = 0.15f, ShuttleStandSec = 0.3f, ShuttleSillSec = 0.25f;

        /// <summary>Proof: the walks built (in / out) and, per walk, the paces drawn after every fit (m/s) and the moments (s), newest last.</summary>
        public static int QuickEnterBuilt, QuickExitBuilt;
        public static readonly List<string> QuickLog = new List<string>();
        /// <summary>Proof: the fastest ramp and aisle pace drawn (m/s) and the walks that would play the running clip on the ramp or in the aisle.</summary>
        public static float QuickRampMax, QuickAisleMax; public static int QuickRunClips;

        /// <summary>BuildShuttleEnter hook (right after WalkTo to the ramp foot): the walk there at the rules' pace (ZWorld.ShuttleWalkMps), so
        /// the figure is at the foot at the rules' moment without the late squeeze.</summary>
        void ShuttleApproachPace(List<Key> keys, int from, int footK)
        {
            float s = runSpeed / Mathf.Max(0.5f, (float)ZWorld.ShuttleWalkMps);
            for (int i = Mathf.Max(1, from); i <= footK && i < keys.Count; i++) keys[i].dur = Mathf.Max(0.02f, keys[i].dur * s);
        }

        /// <summary>BuildShuttleEnter / BuildShuttleExit hook (after the K418 doorway fit): the paces as drawn, for the Play proof.</summary>
        void ShuttleQuickNote(List<Key> keys, int footK, bool enter)
        {
            if (keys.Count <= footK) return;
            float Seg(int i) => i > 0 && i < keys.Count && keys[i].at != null && keys[i - 1].at != null ? (keys[i].at() - keys[i - 1].at()).magnitude : 0f;
            float V(int i) => i > 0 && i < keys.Count ? Seg(i) / Mathf.Max(0.02f, keys[i].dur) : 0f;
            float ramp, aisle, door, inside;
            if (enter)
            {   // foot -> tip -> sill -> just inside -> aisle -> seat
                ramp = V(footK + 2); aisle = V(footK + 4);
                door = 0f; for (int i = footK + 1; i <= footK + 3 && i < keys.Count; i++) door += keys[i].dur;
                inside = 0f; for (int i = footK + 1; i < keys.Count; i++) inside += keys[i].dur;
                QuickEnterBuilt++;
            }
            else
            {   // seat (0) -> seat (sit) -> aisle (stand) -> just inside -> sill -> tip -> foot
                ramp = V(5); aisle = V(3);
                door = 0f; for (int i = 1; i <= 3 && i < keys.Count; i++) door += keys[i].dur;
                inside = 0f; for (int i = 1; i <= footK && i < keys.Count; i++) inside += keys[i].dur;
                QuickExitBuilt++;
            }
            QuickRampMax = Mathf.Max(QuickRampMax, ramp); QuickAisleMax = Mathf.Max(QuickAisleMax, aisle);
            if (ramp > 2.4f || aisle > 2.4f) QuickRunClips++;
            QuickLog.Add($"{(enter ? "in " : "out")} ramp {ramp:F2} m/s, aisle {aisle:F2} m/s, {(enter ? "foot -> doorway" : "seat -> doorway")} {door:F2} s, {(enter ? "foot -> seat" : "seat -> off the foot")} {inside:F2} s");
            if (QuickLog.Count > 64) QuickLog.RemoveAt(0);
        }
    }
}
