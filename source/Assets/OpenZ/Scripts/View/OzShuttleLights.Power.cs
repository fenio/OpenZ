using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzShuttleLights
    {
        float power = 1f;
        /// <summary>K389: every level the clock gives (lamps, band, engines, neon, the two ground glows) is drawn x this (0 dark .. 1
        /// full; 1 unless the capture's start or the crew's leaving is shown: View/OzShuttleCaptureFx.cs). Mode 2 stays dark.</summary>
        public float Power { get => power; set => power = Mathf.Clamp01(value); }
    }
}
