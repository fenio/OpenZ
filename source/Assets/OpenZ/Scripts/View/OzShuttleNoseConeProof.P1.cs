#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256d2 nose light proof, P1 (test only): an enemy past the clear cone's edge, staged at the ship's real hover height. The old
    /// staging put it 95 deg off the heading at a distance clamped to 26 m: with the ship hovering 15.5 m up it stood 31-33 deg under the
    /// light, where the edge is 99-104 deg, so it stood INSIDE the cone and taking it was right (run 28 P1: 99.0 deg, the edge 102.0).
    /// <list type="bullet">
    /// <item>The spot is searched round the ship as the light sees it (the same turn and drop the proof reads): at least
    /// <see cref="P1PastDeg"/> past the edge, at least <see cref="P1RoomDeg"/> inside the old 110 deg lock zone, within the lock range
    /// less <see cref="P1RangeSpareM"/>; the best of them (the most room on both sides) first, the next if the free-tile search moved it.</item>
    /// <item>The enemy then stands still (it never walks: run 28's re-stage saw it walk into the cone within 1.3 s).</item>
    /// <item>Every frame from its placement on is judged: a take (or a beep) while it had stood outside the cone for 0.25 s (counted from
    /// its placement, where it stands past the edge) is the fault; a take
    /// after it came inside (the hovering ship turning) is right and is counted apart.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleNoseConeProof
    {
        public static float P1PastDeg = 4f, P1RoomDeg = 2f, P1RangeSpareM = 3f, P1OutsideShare = 0.6f;

        /// <summary>A world point as the nose searchlight sees a robot there: turn from the nose, drop (deg), flat distance from its post (m).</summary>
        bool SeenFrom(Vector3 w, out float yaw, out float drop, out float flat)
        {
            yaw = drop = flat = 0f; var h = night != null ? night.NoseHead : null;
            if (!h || !ship) return false;
            var q = new Vector3(w.x, g.GroundY(w.x, w.z) + OzShuttleNightLights.AimRobotM, w.z);
            var piv = h.parent ? h.parent.position : h.position;
            var d = ship.transform.InverseTransformDirection(q - piv);
            OzNoseConeBeam.Aim(d.x, d.y, d.z, out double yy, out double pp); yaw = (float)yy; drop = (float)pp;
            flat = new Vector2(q.x - piv.x, q.z - piv.z).magnitude;
            return true;
        }

        static void StandStill(ZObject o)
        {
            if (o == null) return;
            o.MoveSpeed = 0; o.RealMoveSpeed = 0;
            foreach (var m in o.Minions) { m.MoveSpeed = 0; m.RealMoveSpeed = 0; }
        }

        IEnumerator P1PastEdge(System.Action<ZObject> got)
        {
            var spots = new List<(float score, float dist, float off, float yaw, float drop)>();
            for (float off = 80f; off <= 135f; off += 1f)
                for (float dm = 8f; dm <= 34f; dm += 1f)
                {
                    var q = At(dm, off); var w = ZGame.PxToWorld(q.x, q.y);
                    if (!SeenFrom(w, out float y, out float d, out float fl) || fl > OzShuttleNightPlan.LockRangeM - P1RangeSpareM) continue;
                    if (OzShuttleNightPlan.InClearCone(y, d)) continue;
                    float past = Mathf.Abs(y) - OzShuttleNightPlan.ConeEdgeDeg(y, d), room = OzShuttleNightPlan.LockYawMax - Mathf.Abs(y);
                    if (past < P1PastDeg || room < P1RoomDeg) continue;
                    spots.Add((Mathf.Min(past - P1PastDeg, room - P1RoomDeg), dm, off, y, d));
                }
            spots.Sort((a, b) => b.score.CompareTo(a.score));
            Log($"P1 spots past the edge and inside the old lock zone: {spots.Count}" + (spots.Count > 0 ? $"; the best {spots[0].dist:F0} m away, {spots[0].off:F0} deg off the heading (the light sees it at {spots[0].yaw:F1} deg, {spots[0].drop:F1} down; the edge there {OzShuttleNightPlan.ConeEdgeDeg(spots[0].yaw, spots[0].drop):F1})" : ""));
            ZObject e1 = null; float y1 = 0f, d1 = 0f; int tries = 0, acqTry = night.Plan.Acquires, beepTry = night.Plan.Beeps;
            for (int i = 0; i < spots.Count && tries < 6 && e1 == null; i += 3, tries++)
            {
                var c = spots[i]; acqTry = night.Plan.Acquires; beepTry = night.Plan.Beeps;                   // the counts from the moment this enemy is placed
                var o = Enemy(c.dist, c.off); if (o == null) continue;
                StandStill(o); yield return null; yield return null;
                if (SeenAt(o, out y1, out d1) && !OzShuttleNightPlan.InClearCone(y1, d1) && Mathf.Abs(y1) - OzShuttleNightPlan.ConeEdgeDeg(y1, d1) >= P1PastDeg - 1f && Mathf.Abs(y1) <= OzShuttleNightPlan.LockYawMax) e1 = o;
                else { Log($"P1 try {tries + 1}: the free tile moved it to {y1:F1} deg, {d1:F1} down (not past the edge by {P1PastDeg - 1f:F0} deg): the next spot"); Vanish(o); enemies.Remove(o); }
            }
            got(e1);
            // a take or beep from its placement on counts: it stood past the edge from the first frame (checked above), so a take in
            // those frames or in the loop's first 0.25 s is the fault, not a take after it came inside
            int acq0 = e1 != null ? acqTry : night.Plan.Acquires, beeps0 = e1 != null ? beepTry : night.Plan.Beeps, acqSeen = acq0, beepSeen = beeps0;
            int frames = 0, outside = 0, takenOut = 0, takenIn = 0, beepOut = 0; float outRun = e1 != null ? 0.25f : 0f, ylo = 999f, yhi = -999f, pastLo = 999f, pastHi = -999f, t = 0f;
            while (t < 9.5f)
            {
                float y = 0f, d = 0f; bool seen = e1 != null && SeenAt(e1, out y, out d), outNow = seen && !OzShuttleNightPlan.InClearCone(y, d);
                if (seen) { float p = Mathf.Abs(y) - OzShuttleNightPlan.ConeEdgeDeg(y, d); pastLo = Mathf.Min(pastLo, p); pastHi = Mathf.Max(pastHi, p); }
                if (night.Plan.Acquires > acqSeen) { if (outRun >= 0.25f) takenOut += night.Plan.Acquires - acqSeen; else takenIn += night.Plan.Acquires - acqSeen; acqSeen = night.Plan.Acquires; }
                if (night.Plan.Beeps > beepSeen) { if (outRun >= 0.25f) beepOut += night.Plan.Beeps - beepSeen; beepSeen = night.Plan.Beeps; }
                outRun = outNow ? outRun + Time.deltaTime : 0f;
                if (night.Plan.LockId < 0) { ylo = Mathf.Min(ylo, night.Plan.YawNow); yhi = Mathf.Max(yhi, night.Plan.YawNow); }
                frames++; if (outNow) outside++;
                t += Time.deltaTime; yield return null;
            }
            Log($"P1 the enemy #{(e1 != null ? e1.Id : -1)} standing at {y1:F1} deg, {d1:F1} deg down (the edge there {OzShuttleNightPlan.ConeEdgeDeg(y1, d1):F1}; the old lock zone {OzShuttleNightPlan.LockYawMax:F0}); past the edge in {outside} of {frames} frames ({pastLo:F1} .. {pastHi:F1} deg past it); "
                + $"taken {night.Plan.Acquires - acq0} (while it stood past the edge {takenOut}, after it came inside {takenIn}), beeps {night.Plan.Beeps - beeps0} (past the edge {beepOut}); the sweep while free {ylo:F1} .. {yhi:F1} deg");
            Check("P1 an enemy beside the ship past the edge (inside the old 110 deg lock zone: the old rules took it) is not taken: no take and no beep while it stands past the edge (judged every frame), the light sweeps on ahead (>= 40 deg side to side)",
                e1 != null && frames > 0 && outside >= P1OutsideShare * frames && takenOut == 0 && beepOut == 0 && yhi - ylo >= 40f);
        }
    }
}
#endif
