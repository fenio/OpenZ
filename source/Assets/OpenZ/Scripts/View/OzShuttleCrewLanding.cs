using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shot-down shuttle's crew land on the ground, not in the air (rule DV118, view only; OzShuttleDeathPlan.Lands). A thrown robot
    /// body's landing is its first hard contact (OzBodyLandingPart: the metal body thud, and OzShuttleCrewFall's Landed). The crew of a
    /// shuttle broken up high in the air fall among its pieces, so their first hard contact was often a flying piece or another body up
    /// there: the thud came 15 m up and the real landing was silent. Now, for the shuttle's crew only, a contact counts as the landing when
    /// the body touches the ground or the water, touches anything within <see cref="OzShuttleDeathPlan.BodyNearM"/> m of them, or comes
    /// down on something lying still (a roof, a rock, a piece that has landed, a body that lies); a knock against something still flying
    /// up in the air is no landing. Every other robot body lands as before.
    /// </summary>
    public static class OzShuttleCrewLanding
    {
        public static bool Enabled = true;
        /// <summary>Proof: contacts of the crew's bodies up in the air that were not their landing; landings; the last break's landing heights
        /// (one per body).</summary>
        public static int MidAir, Landings;
        public static readonly List<string> Last = new List<string>();
        static float lastThrow = -1f;

        /// <summary>OzBodyLandingPart, a hard contact before the landing: true = up in the air against something flying (not the landing).</summary>
        public static bool InAir(OzBodyLanding state, Collision c)
        {
            if (!Enabled || !state || c == null || c.contactCount == 0) return false;
            if (!state.TryGetComponent<OzShuttleCrewFall>(out var fall) || !fall) return false;          // not the shuttle's crew: as before
            if (OzShuttleCrewFall.LastThrowAt != lastThrow) { lastThrow = OzShuttleCrewFall.LastThrowAt; Last.Clear(); }
            var cp = c.GetContact(0);
            var touch = OzShuttleBreakup.CrewTouchOf(c.collider);                                             // K256k: a piece still tumbling is no landing
            float over = OzShuttleBreakup.OverSurface(cp.point);
            if (OzShuttleDeathPlan.Lands(touch, cp.normal.y, over, OzShuttleDeathPlan.BodyNearM))
            {
                Landings++;
                bool top = over > OzShuttleDeathPlan.BodyNearM && touch != OzShuttleDeathPlan.Touch.Ground && touch != OzShuttleDeathPlan.Touch.Water;
                if (Last.Count < 32) Last.Add($"{over:F2} m {(top ? "on top of " + (touch == OzShuttleDeathPlan.Touch.Scenery ? "scenery" : "a lying piece / body") : touch == OzShuttleDeathPlan.Touch.Ground ? "ground" : touch == OzShuttleDeathPlan.Touch.Water ? "water" : "near the ground")}");
                return false;
            }
            MidAir++;
            return true;
        }
    }
}
