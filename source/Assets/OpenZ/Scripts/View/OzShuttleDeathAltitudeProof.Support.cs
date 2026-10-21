using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The K256h Play proof's support measures (test only). A body flagged as landed high up is fine when something holds it there: the
    /// old measure cast one ray down from its hips, so a body caught by an arm or a leg on a lying piece, leaning on one with its feet on
    /// the ground, or lying on another body counted as "landed in the air". Now every bone of the body is asked: one within the body's
    /// landing height of the ground, or one with a lying piece, still scenery (a rock, a trunk, a roof) or a landed body right under it,
    /// holds the body up. The pieces: every rested piece whose bottom is high over the ground must touch something under it
    /// (OzShuttleBreakup.Unsupported), and none may lie more than 3 cm inside another (the game's own promise), as two separate lines.
    /// </summary>
    public sealed partial class OzShuttleDeathAltitudeProof
    {
        /// <summary>A body's bone this close over the ground or water (m) touches it (the crew's own landing test: its lowest point within
        /// 0.25 m, + 5 cm for a bone's box on a slope).</summary>
        const float BoneNearM = 0.3f;
        readonly List<string> landedOnTop = new List<string>();
        readonly List<Collider> boneCols = new List<Collider>();

        /// <summary>Is the body held up where it is: one of its bones near the ground, or one with a lying piece, still scenery or a landed
        /// body right under it (within 0.35 m of the bone's bottom)? by = what holds it.</summary>
        bool BodySupported(OzShuttleCrewFall b, out string by)
        {
            by = "-";
            if (!b) return false;
            b.GetComponentsInChildren(boneCols);
            foreach (var c in boneCols)
            {
                if (!OzShuttleCrewFall.IsBone(c)) continue;                                              // the ragdoll's own bones only (K256k: a frozen body's are off and on no rigidbody: read from their capsules)
                var bb = BoneBox(c);
                var bottom = new Vector3(bb.center.x, bb.min.y, bb.center.z);
                if (Over(bottom) <= BoneNearM) { by = "the ground (" + c.name + ")"; return true; }
                foreach (var h in Physics.RaycastAll(bb.center, Vector3.down, bb.extents.y + 0.35f, ~0, QueryTriggerInteraction.Ignore))
                {
                    var o = h.collider; if (!o || o.transform.IsChildOf(b.transform)) continue;
                    if (o.gameObject.layer == ZGame.WalkLayer || CraterField.IsGroundCollider(o)) { by = "the ground (" + c.name + ")"; return true; }
                    var piece = o.GetComponentInParent<OzShuttleBreakup>();
                    if (piece) { if (!DrawnUnder(piece, bb)) continue; by = "the piece " + piece.PieceName + " (" + c.name + ")"; return true; }   // K256k: its drawn metal, not its hull over a hollow
                    var other = o.GetComponentInParent<OzShuttleCrewFall>();
                    if (other && other != b && other.Landed) { by = "a landed body (" + c.name + ")"; return true; }
                    if (!o.attachedRigidbody) { by = "scenery " + o.transform.root.name + " (" + c.name + ")"; return true; }
                }
            }
            return false;
        }

        /// <summary>A body flagged landed with the hips high: held up (logged) or up in the air (counted).</summary>
        void HighLanding(OzShuttleCrewFall b, float h)
        {
            if (BodySupported(b, out string by)) { if (landedOnTop.Count < 12) landedOnTop.Add($"{h:F2} m on {by}"); return; }
            landedHigh++; landedHighWorst = Mathf.Max(landedHighWorst, h);
        }

        /// <summary>The bodies at rest: hips at most 1 m over the ground, or held up by something under them. worst = the highest one not held.</summary>
        int BodiesHighAtRest(out float worst, out string held)
        {
            int n = 0; worst = 0f; var l = new List<string>();
            foreach (var b in OzShuttleCrewFall.Last)
            {
                if (!b) continue;
                float h = Over(b.BodyPos); if (h <= 1f) continue;
                if (BodySupported(b, out string by)) { l.Add($"{h:F2} m on {by}"); continue; }
                n++; worst = Mathf.Max(worst, h);
            }
            held = l.Count > 0 ? string.Join("; ", l) : "-";
            return n;
        }
    }
}
