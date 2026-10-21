using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's climb in flight (view only). Over a cliff, a hill or a roof the hull rises eased and never faster than
    /// <see cref="climbTotalMps"/> on the screen, its hover bob included:
    /// <list type="bullet">
    /// <item>the drawn height eases up at most <see cref="climbMaxMps"/> (it was 3.6 m/s);</item>
    /// <item>while it climbs, the bob's clock runs slower, so that the bob's own up-and-down speed adds at most what is left of the
    /// limit (at a full climb its waves run at about a third of their pace); once it holds its height the clock runs at its own pace
    /// again. The bob's height and shape, its drift and every reader of BobNow (the shadow, the clearances) stay as they were; the
    /// hover bob itself is unchanged (no climb: full pace).</item>
    /// </list>
    /// At 3.6 m/s plus the bob's own speed (up to 2.1 m/s when its two waves rise together) a crossing climbed up to 5.3 m/s on the
    /// screen (a cliff 4.5 m/s, the polish proof's crossings 5.3 m/s).
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        [Tooltip("the drawn climb's top speed (m/s); the most the hull rises per second on the screen, the hover bob included (m/s)")]
        public float climbMaxMps = 3.2f, climbTotalMps = 3.8f;
        /// <summary>Proof: how far the bob's clock is behind the ship's time (s), its pace now (1 = its own), the slowest pace seen.</summary>
        public float BobClockLag { get; private set; }
        public float BobPaceNow { get; private set; } = 1f;
        public float BobPaceMin { get; set; } = 1f;
        float climbLastY; bool climbHaveY;

        /// <summary>The bob's top up-and-down speed (m/s) at its own pace: its two waves rising together.</summary>
        public float BobTopMps => bobM * 2f * Mathf.PI * (bobSlowShare * bobHz + (1f - bobSlowShare) * bobHz2);

        /// <summary>Pose: the bob and the drift on a clock that runs slower while the hull climbs (yNoBob: the drawn height before the
        /// bob, bw: how much of the bob).</summary>
        void ClimbBob(float bw, float dt, float yNoBob, out float rollDrift, out float pitchDrift)
        {
            float climb = climbHaveY && dt > 1e-5f ? (yNoBob - climbLastY) / dt : 0f;
            climbLastY = yNoBob; climbHaveY = true;
            float top = BobTopMps * Mathf.Clamp01(bw);
            float pace = top > 1e-4f ? Mathf.Clamp01((climbTotalMps - Mathf.Max(0f, climb)) / top) : 1f;
            BobPaceNow = pace; if (pace < BobPaceMin) BobPaceMin = pace;
            if (dt > 0f) BobClockLag += (1f - pace) * dt;
            float keep = t; t = keep - BobClockLag;
            try { Bob(bw, out rollDrift, out pitchDrift); }
            finally { t = keep; }
        }
    }
}
