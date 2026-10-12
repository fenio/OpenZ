using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// ratio 8, attack 3 ms, release 220 ms), so twenty explosions at once squash instead of clipping. Sits on the AudioListener
    /// rig (CombatAudio.EnsureRig); OnAudioFilterRead on the listener's GameObject processes everything Unity mixes.
    /// Soft knee as in the Web Audio DynamicsCompressor.
    /// audio_v2 (T19 audit P1): the knee now sits ABOVE the threshold like Web Audio's (the port started compressing 6 dB early and
    /// squashed every voice line into a duck of the whole world), +4 dB make-up (Chrome adds about +6 for these settings; the take
    /// measured -27.2 LUFS for gameplay), and a -1 dBFS ceiling so the make-up can never clip.
    /// </summary>
    public class LegacyMixCompressor : MonoBehaviour
    {
        public float thresholdDb = -16f, kneeDb = 12f, ratio = 8f, attack = 0.003f, release = 0.22f;
        [Tooltip("audio_v2: make-up gain after the compressor (dB)")] public float makeupDb = 4f;
        [Tooltip("audio_v2: sample-peak ceiling after the make-up (dBFS), instant attack")] public float ceilingDb = -1f, limiterRelease = 0.05f;
        [Tooltip("audio_v2: Web Audio knee (compression starts AT the threshold); off = the old centred knee")] public bool kneeAboveThreshold = true;
        float lim = 1f, aLim;
        public bool bypass;
        /// <summary>Largest gain reduction (dB) in the last audio block, for the Play-mode check.</summary>
        public float GainReductionDb { get; private set; }
        /// <summary>audio_v2: largest ceiling-limiter reduction (dB) in the last audio block.</summary>
        public float LimiterDb { get; private set; }
        float env, aAtt, aRel; int rate;

        void Awake() { rate = AudioSettings.outputSampleRate; Coefs(); }
        void OnValidate() { if (rate > 0) Coefs(); }
        void Coefs() { aAtt = Mathf.Exp(-1f / (Mathf.Max(0.0005f, attack) * rate)); aRel = Mathf.Exp(-1f / (Mathf.Max(0.01f, release) * rate)); aLim = Mathf.Exp(-1f / (Mathf.Max(0.005f, limiterRelease) * rate)); }

        void OnAudioFilterRead(float[] data, int channels)
        {
            if (bypass || rate <= 0) { GainReductionDb = 0f; return; }
            float slope = 1f - 1f / Mathf.Max(1f, ratio), half = kneeDb * 0.5f, grMax = 0f, knee = Mathf.Max(0.01f, kneeDb);
            float makeup = Mathf.Pow(10f, makeupDb / 20f), ceil = Mathf.Pow(10f, ceilingDb / 20f), limMin = 1f;
            for (int i = 0; i < data.Length; i += channels)
            {
                float peak = 0f;
                for (int c = 0; c < channels; c++) { float a = data[i + c]; if (a < 0f) a = -a; if (a > peak) peak = a; }
                float over = (peak > 1e-6f ? 20f * Mathf.Log10(peak) : -120f) - thresholdDb, gr;
                if (kneeAboveThreshold)
                {   // Web Audio: quadratic knee from the threshold up to threshold + knee, then the full ratio
                    if (over <= 0f) gr = 0f;
                    else if (over >= knee) gr = slope * (over - half);
                    else gr = slope * over * over / (2f * knee);
                }
                else if (over <= -half) gr = 0f;
                else if (over >= half) gr = over * slope;
                else { float x = over + half; gr = slope * x * x / (2f * knee); }
                env = gr > env ? aAtt * env + (1f - aAtt) * gr : aRel * env + (1f - aRel) * gr;
                float g = (env > 0.001f ? Mathf.Pow(10f, -env / 20f) : 1f) * makeup;
                float outPeak = peak * g;                                                              // audio_v2: -1 dBFS ceiling, instant attack
                float need = outPeak > ceil ? ceil / outPeak : 1f;
                lim = Mathf.Min(need, aLim * lim + (1f - aLim));                                       // instant attack, release to 1
                g *= lim; if (lim < limMin) limMin = lim;
                if (g != 1f) for (int c = 0; c < channels; c++) data[i + c] *= g;
                if (env > grMax) grMax = env;
            }
            GainReductionDb = grMax;
            LimiterDb = limMin < 1f ? -20f * Mathf.Log10(limMin) : 0f;
        }
    }
}
