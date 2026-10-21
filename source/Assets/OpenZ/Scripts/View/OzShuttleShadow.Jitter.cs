using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The mark's jitter count for the K256g / K256g2 proofs (proof counters only; nothing drawn changes): which frames make a pair.
    /// A frame slower than <see cref="JitterHitchK"/> times the running frame time (and never under 0.03 s) is a hitch of the editor and
    /// starts the count again. At 60 fps that limit is 0.03 s, as before. With the frame cap at 30 the frames now count (the fixed
    /// 0.03 s limit left out every 30 fps frame: "jitter rms 0.00 (0 frames sampled)").
    /// The running frame time learns from the frames that count, and slowly from the others, so a lasting change of the frame rate is
    /// followed within about a second. A step's change is counted per frame in whole 60 fps frames (<see cref="JitterFrameSec"/>: 1/60 s
    /// at 60 fps, exactly as before; 2/60 at the 30 fps cap), so the px limits keep meaning "per drawn frame".
    /// </summary>
    public sealed partial class OzShuttleShadow
    {
        /// <summary>A hitch: this many times the running frame time (s), at least <see cref="JitterHitchMinSec"/>.</summary>
        public static float JitterHitchK = 1.6f, JitterHitchMinSec = 0.03f;
        float jitterAvgDt = -1f;

        /// <summary>The longest frame (s) that still makes a pair, learnt from `dt` (the frame being counted).</summary>
        float JitterHitchSec(float dt)
        {
            if (!(dt > 0f)) return JitterHitchMinSec;
            if (jitterAvgDt < 0f) jitterAvgDt = Mathf.Min(dt, 1f / 30f);
            float lim = Mathf.Max(JitterHitchMinSec, JitterHitchK * jitterAvgDt);
            jitterAvgDt = Mathf.Lerp(jitterAvgDt, Mathf.Min(dt, 0.25f), dt <= lim ? 0.05f : 0.02f);
            return lim;
        }

        /// <summary>The drawn frame in whole 60 fps frames (s): 1/60 at 60 fps, 2/60 at the 30 fps cap.</summary>
        float JitterFrameSec() => Mathf.Max(1f, Mathf.Round(Mathf.Max(jitterAvgDt, 0f) * 60f)) / 60f;
    }
}
