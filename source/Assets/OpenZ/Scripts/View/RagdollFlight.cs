using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// missile death is ERobotTurrent (erobotturrent.cpp:22-53): the body flips through the air for lifetime = 3.5 + 0.1 x (rand() % 10)
    /// = 3.5-4.4 s, rising (sprite scale 1.3-3.3) and landing at a random spot within max_horz / max_vert = 100 px per axis
    /// (erobotturrent.h:9). Before this, OpenZ threw the ragdoll with a ~1 s ballistic hop. Now the ragdoll launched on the
    /// explosion frame (UnitView.Die, same frame as the blast and the death sound) flies Zod's time: a controlled ballistic arc of
    /// T = Zod's lifetime (seeded per robot), apex H = rise x 1.7 m x (robot height / 3.5 m), landing distance D up to
    /// <see cref="MaxLandM"/> AWAY from the blast centre, tumbling about one to two turns; gravity is reduced for the flight
    /// (g' = 8 H / T^2 applied as an upward counter-acceleration on every bone) so the body lands at T, then real physics settles it.
    /// (a knob). View only: the sim deleted the robot on the death tick like Zod.
    /// </summary>
    public class RagdollFlight : MonoBehaviour
    {
        /// <summary>
        /// Off: the blast throws the ragdoll with full
        /// gravity (RobotRagdoll.Launch + RagdollFlail); Zod's 3.5-4.4 s sprite flight stays available as a switch.</summary>
        public static bool ZodFlight = false;
        public static float MaxLandM = 6f, ApexPerRise = 1.7f;
        public static int Flights { get; private set; }

        Rigidbody[] bodies; float T, g, age;

        public float Duration => T;

        /// <summary>Starts the flight on a ragdoll RobotRagdoll.Launch just built. seed: per robot (Zod rand()).</summary>
        public static RagdollFlight Begin(GameObject robot, Vector3 blast, uint seed, float robotHeight)
        {
            if (!ZodFlight || !robot) return null;
            var f = robot.AddComponent<RagdollFlight>();
            f.Init(blast, seed, robotHeight);
            Flights++;
            return f;
        }

        void Init(Vector3 blast, uint seed, float robotHeight)
        {
            bodies = GetComponentsInChildren<Rigidbody>();
            T = 3.5f + 0.1f * (seed % 10u);                                                     // erobotturrent.cpp:28
            float rise = 1.3f + 0.01f * ((seed >> 4) % 200u);                                   // erobotturrent.cpp:44
            float hs = Mathf.Clamp(robotHeight / 3.5f, 0.35f, 1.3f);
            float H = rise * ApexPerRise * hs;
            g = 8f * H / (T * T);
            float vy = 4f * H / T;
            var away = transform.position - blast; away.y = 0f;
            if (away.sqrMagnitude < 0.01f) { float a = (seed >> 12) % 6283u / 1000f; away = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)); }
            away.Normalize();
            float D = MaxLandM * (0.35f + 0.65f * ((seed >> 16) & 1023u) / 1023f);
            var v = away * (D / T) + Vector3.up * vy;
            Rigidbody hips = null;
            foreach (var rb in bodies)
            {
                if (!rb) continue;
                rb.linearVelocity = v;
                if (!hips) hips = rb;                                                              // RobotRagdoll adds the hips first
            }
            if (hips)
            {   // Zod die5: the body flips over in the air (33 frames over the flight): one to two slow turns about the side axis
                float turns = 1f + ((seed >> 8) & 255u) / 255f;
                hips.angularVelocity = Vector3.Cross(Vector3.up, away) * (turns * 2f * Mathf.PI / T) + Vector3.up * (((seed >> 20) & 1u) == 0u ? 0.6f : -0.6f);
            }
        }

        void FixedUpdate()
        {
            age += Time.fixedDeltaTime;
            if (age >= T * 0.97f) { enabled = false; return; }                                    // lands: real gravity settles it
            float lift = Physics.gravity.magnitude - g;
            foreach (var rb in bodies) if (rb && !rb.isKinematic) rb.AddForce(Vector3.up * lift, ForceMode.Acceleration);
        }
    }
}
