using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shuttle jets' two thruster point lights near the ground (view only). They flicker like a flame (0.85 + 0.3 x a noise at 7 a
    /// second) and light the ground round the ship; the ground dust takes its colour from that lit ground (OpenZ/GroundDust reads the
    /// camera's opaque copy), so while the dust is up the whole lit sheet round the ship pulsed with them, most at night by the jets.
    /// While the dust is up (the downwash strength OzShuttleDownwash.K of the ship's height: full under 3 m, none over 11 m) the lights
    /// now breathe slowly and gently instead (<see cref="NearRate"/>, <see cref="NearBase"/> + <see cref="NearDepth"/>); higher up they
    /// flicker as before. CALM SCREEN keeps them steady as before. <see cref="On"/> off = the old flicker at every height.
    /// </summary>
    public static class OzJetLightSteady
    {
        /// <summary>On: the gentle light while the dust is up. Off: the flame flicker at every height (as before).</summary>
        public static bool On = true;
        /// <summary>While the dust is up: the noise's rate (1/s), the light's base and depth (x its intensity).</summary>
        public static float NearRate = 2.2f, NearBase = 0.95f, NearDepth = 0.10f;
        /// <summary>Proof: frames a light was eased (the dust up).</summary>
        public static int EasedFrames;

        /// <summary>Light i's flicker factor at the jets' age (s) for a ship whose root is feetM over the ground.</summary>
        public static float Flicker(float age, int i, bool calm, float feetM)
        {
            if (calm) return 1f;
            float live = 0.85f + 0.3f * Mathf.PerlinNoise(age * 7f + i * 3.1f, 0.37f);
            if (!On) return live;
            float k = OzShuttleDownwash.K(feetM);
            if (k <= 0.001f) return live;
            EasedFrames++;
            float near = NearBase + NearDepth * Mathf.PerlinNoise(age * NearRate + i * 3.1f, 0.37f);
            return Mathf.Lerp(live, near, k);
        }
    }
}
