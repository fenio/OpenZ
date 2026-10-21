using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// own style (strength, life, flicker phase and rate). Before, every light's flicker ran at the same two rates with the phase of
    /// its pool slot (slot i: i x 13.37, so neighbouring slots were 0.8 rad apart: the lights of a wreck field flickered nearly
    /// together). A light of anything else keeps exactly what it had: its slot's phase and rate 1. Hooks: AddLight takes the style
    /// (FireLightStyle), StepLights asks FireFlicker for a flickering light (OpenZFxPlayer.cs). No allocation per frame.
    /// </summary>
    public partial class OpenZFxPlayer
    {
        /// <summary>Play check: fire lights that took a fire's own style since the program started.</summary>
        public static int FireLightsStyled;
        float[] fireBeatRate;

        void FireLightStyle(FxLight x)
        {
            int i = lights.IndexOf(x); if (i < 0) return;
            if (fireBeatRate == null || fireBeatRate.Length != lights.Count) { fireBeatRate = new float[lights.Count]; for (int j = 0; j < fireBeatRate.Length; j++) fireBeatRate[j] = 1f; }
            if (OzFireDesync.TakeLight(out float gain, out float life, out float seed, out float rate))
            {
                x.intensity *= gain; x.life = Mathf.Max(0.02f, x.life * life); x.seed = seed; fireBeatRate[i] = rate; FireLightsStyled++;
            }
            else { x.seed = i * 13.37f; fireBeatRate[i] = 1f; }                                                 // BuildLights' own phase
        }

        float FireFlicker(FxLight x, float t)
        {
            float r = 1f;
            if (fireBeatRate != null) { int i = lights.IndexOf(x); if (i >= 0 && i < fireBeatRate.Length) r = fireBeatRate[i]; }
            return 1f + x.flicker * (0.55f * Mathf.Sin(t * 8.3f * r + x.seed) + 0.45f * Mathf.Sin(t * 23.7f * r + x.seed * 2f));
        }
    }
}
