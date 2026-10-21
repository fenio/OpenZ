using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The K256h Play proof's body measures, made exact (test only; K256k):
    /// <list type="bullet">
    /// <item>A body at rest is frozen: its bones' colliders are switched off, and a switched-off collider has no bounds, so every bone was
    /// skipped and every body lying higher than 1 m (on a dome, on a panel) counted as "nothing under it". The bones' boxes are now read
    /// from their capsules (<see cref="BoneBox"/>).</item>
    /// <item>A body held by a hull panel counts only where the panel's DRAWN metal is under the bone (<see cref="DrawnUnder"/>): the panel's
    /// collider is its convex hull, which spans the panel's hollow like a lid.</item>
    /// <item>"The bodies fall from up there": the fall from the top of the body's flight down to where it landed must be at least 0.7 x the
    /// height it could fall there (the pads' height over the ground less the height of what it landed on), see <see cref="FallLine"/>; a
    /// body thrown up first, or one landing on top of the heap, is measured fairly.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleDeathAltitudeProof
    {
        /// <summary>A bone's world box (its collider's bounds, or its capsule once the body is frozen): OzShuttleCrewFall.BoneBox.</summary>
        static Bounds BoneBox(Collider c) => OzShuttleCrewFall.BoneBox(c);

        /// <summary>A hull panel holds the bone only where its drawn metal is under it: OzShuttleCrewFall.DrawnUnder.</summary>
        static bool DrawnUnder(OzShuttleBreakup piece, Bounds bb) => OzShuttleCrewFall.DrawnUnder(piece, bb);

        /// <summary>The bodies' fall line: every body fell, from the top of its flight to where it landed, at least 0.7 x (the pads' height at
        /// the break less the height of what it landed on). text = the least fall and its need.</summary>
        static bool FallLine(out string text)
        {
            bool ok = true; float least = 99f, need = 0f; int n = 0;
            foreach (var b in OzShuttleCrewFall.Last)
            {
                if (!b || !b.Landed) continue;
                n++;
                float on = Mathf.Max(0f, b.LandedOverM - 0.3f);                                                 // hips lying: about 0.25 m over what holds them
                float want = 0.7f * Mathf.Max(0f, OzShuttleBreakup.LastBreakFeet - on);
                float fell = b.FellFromTopM;
                if (fell < least) { least = fell; need = want; }
                if (fell < want) ok = false;
            }
            text = $"the least fall from the top of a body's flight {least:F2} m against {need:F2} m (0.7 x the pads' {OzShuttleBreakup.LastBreakFeet:F2} m over what it landed on), {n} bodies";
            return ok && n > 0;
        }
    }
}
