using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The landing lamps' aim (view only; their look is unchanged: the same lamps, lenses, halos, cones and pools, only where they point).
    /// Both lamps sit at the front of the belly, their housings 20 degrees forward of straight down; one lights the front of the pad,
    /// one its rear (3 m to each side of the landing point). Coming down over the point, the rear lamp has to turn 65 to 75 degrees
    /// from its housing (pads 5 .. 2.5 m up); it was allowed 55, so it stayed up to 20 degrees short of the pad's rear. Now:
    /// <list type="bullet">
    /// <item>each head may turn up to <see cref="SteadyMaxDeg"/> from its housing;</item>
    /// <item>its aim is eased in the WORLD (at <see cref="SteadyRate"/>), so the hull's wobble, drift and nose-down on the way down no
    /// longer swing the beam off the point (it was eased in the hull's frame and lagged behind every roll).</item>
    /// </list>
    /// Off the landing (aim = false) the heads go back to their housings exactly as before (AimLanding).
    /// </summary>
    public sealed partial class OzShuttleSearchlights
    {
        /// <summary>The most a landing lamp's head turns from its housing (deg) and the ease of its aim (1/s).</summary>
        public static float SteadyMaxDeg = 80f, SteadyRate = 9f;
        Vector3[] steadyW; bool steadyOn;

        /// <summary>OzShuttleNightLights.Step: the landing lamps onto the points beside the landing point (on), else back to their housings.</summary>
        internal void AimLandingSteady(bool on, Vector3 at, Vector3 side, float spread, float dt)
        {
            if (dead || dt <= 0f) return;
            if (!on) { steadyOn = false; AimLanding(false, at, side, spread, OzShuttleNightLights.LandingMaxAimDeg, OzShuttleNightLights.LandingAimRate, dt); return; }   // back to the housings at the old pace
            if (steadyW == null || steadyW.Length != lamps.Length) { steadyW = new Vector3[lamps.Length]; steadyOn = false; }
            float a = 1f - Mathf.Exp(-SteadyRate * dt);
            for (int i = 0; i < lamps.Length; i++)
            {
                var L = lamps[i]; if (L == null || !L.head) continue;
                var house = root.TransformDirection(L.aimL);
                var want = house;
                var d = at + side * (i == 0 ? spread : -spread) - L.head.position;
                if (d.sqrMagnitude > 1e-4f)
                {
                    d.Normalize();
                    float ang = Vector3.Angle(house, d);
                    want = ang > SteadyMaxDeg ? Vector3.Slerp(house, d, SteadyMaxDeg / ang) : d;
                }
                if (!steadyOn || steadyW[i].sqrMagnitude < 0.5f) steadyW[i] = L.head.forward;                 // from where the head looks now
                var w = Vector3.Slerp(steadyW[i], want, a);
                float wa = Vector3.Angle(house, w);
                if (wa > SteadyMaxDeg) w = Vector3.Slerp(house, w, SteadyMaxDeg / wa);                            // the hull turned under it: never past its limit
                steadyW[i] = w.normalized;
                L.head.rotation = Quaternion.LookRotation(steadyW[i], Mathf.Abs(steadyW[i].y) > 0.95f ? root.forward : Vector3.up);
            }
            steadyOn = true;
        }
    }
}
