using UnityEngine;

namespace OpenZ
{
    public partial class PlacementPop
    {
        /// <summary>Halos drawn for other effects (K389: the shuttle's capture).</summary>
        public static int Halos { get; private set; }

        /// <summary>The placement flash's bloom halo (the camera-facing glow, its size shrinking to 70 % and its gain falling k squared
        /// over its life) at `at`, for another effect (K389: the shuttle's capture, View/OzShuttleCaptureFx.cs).</summary>
        public static void Halo(Vector3 at, float sizeM, float gain, float life, Color color)
        {
            Instance.flashes.Add(new Flash { at = at, life = Mathf.Max(0.02f, life), size = sizeM, gain = gain, color = color });
            Halos++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetHalos() { Halos = 0; }
    }
}
