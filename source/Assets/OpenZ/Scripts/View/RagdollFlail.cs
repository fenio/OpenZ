using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// ragdoll's flight the arm and leg bodies get short random torque kicks (seeded per robot, view only) and a spin at launch, so
    /// the limbs windmill; the kicks fade out before landing and stop once the body touches down (real physics settles it).
    /// </summary>
    public class RagdollFlail : MonoBehaviour
    {
        public static bool On = true;
        public static float FlailSeconds = 0.9f, KickEvery = 0.11f, Torque = 7f, LaunchSpin = 9f;
        public static int Started { get; private set; }
        Rigidbody[] limbs; float age, next, strength; uint rng;

        public static RagdollFlail Begin(GameObject robot, float strength, uint seed)
        {
            if (!On || !robot) return null;
            var f = robot.AddComponent<RagdollFlail>(); f.Init(strength, seed); Started++; return f;
        }

        void Init(float s, uint seed)
        {
            strength = s; rng = seed | 1u;
            var list = new System.Collections.Generic.List<Rigidbody>();
            foreach (var rb in GetComponentsInChildren<Rigidbody>())
            {
                string n = rb.name.ToLowerInvariant();
                if (n.Contains("arm") || n.Contains("leg") || n.Contains("forearm")) list.Add(rb);
            }
            limbs = list.ToArray();
            foreach (var rb in limbs) if (rb && !rb.isKinematic) rb.angularVelocity += RandDir() * LaunchSpin * strength * (0.5f + Rand());
        }

        float Rand() { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return (rng & 0xFFFFFF) / 16777216f; }
        Vector3 RandDir() => new Vector3(Rand() - 0.5f, Rand() - 0.5f, Rand() - 0.5f).normalized;

        void FixedUpdate()
        {
            age += Time.fixedDeltaTime;
            if (age > FlailSeconds || limbs == null) { Destroy(this); return; }
            if (age < next) return;
            next = age + KickEvery;
            float fade = 1f - age / FlailSeconds;
            foreach (var rb in limbs) if (rb && !rb.isKinematic) rb.AddTorque(RandDir() * Torque * strength * fade * rb.mass, ForceMode.Impulse);
        }
    }
}
