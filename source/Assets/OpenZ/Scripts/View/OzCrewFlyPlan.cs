using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The shot-down shuttle's crew bodies are blown UP first (rule DV118, view only: the kill and the crew's deaths are the rules' as
    /// before). The bodies used to leave the breaking ship with a soft shove (on the ground well under 1 m/s up: a plain drop). Now the
    /// blast gives every body its own strong kick, drawn from its throw's seed (the ship and the body: the same kill throws the same way
    /// every time, no two bodies alike):
    /// <list type="bullet">
    /// <item>up <see cref="Up0"/>..<see cref="Up1"/> m/s (it rises about 1.8 .. 6.2 m over where it started), out <see cref="Out0"/>..<see cref="Out1"/> m/s
    /// along its throw's own flat way (away from the hull's middle and the killing hit), a spin of <see cref="Spin0"/>..<see cref="Spin1"/> rad/s
    /// about its own mostly flat axis (it tumbles end over end, at most <see cref="AxisTilt"/> of the axis upright), all on top of the
    /// ship's own velocity;</item>
    /// <item>it lands within <see cref="MaxFlightSec"/> s (the ragdoll keeps simulating for 6 s: time left to land and settle with its thud):
    /// a body high up gets less up kick if it would fly longer;</item>
    /// <item>it lands within <see cref="MaxReachM"/> m of where it was thrown, the ship's way during its longer flight than a piece's counted (the
    /// bodies stay among the wreck's pieces): the out kick is lowered when it would fly farther, never under <see cref="OutFloor"/> m/s; when
    /// even that is too far (a fast ship), the up kick is lowered too, never under <see cref="Up0"/>.</item>
    /// </list>
    /// Pure numbers (no engine call): OzShuttleCrewFall.FlyUp.cs gives them to the bodies, the check Editor/OzCrewFlyUpCheck.cs counts them.
    /// Switch: <see cref="On"/> (off = the bodies' old soft throw).
    /// </summary>
    public static class OzCrewFlyPlan
    {
        /// <summary>Switch: the crew's bodies are blown up first (on).</summary>
        public static bool On = true;
        /// <summary>The kick's ranges: up and out (m/s), the spin (rad/s), the share of the spin axis that may stand upright (0 = flat).</summary>
        public static float Up0 = 6f, Up1 = 11f, Out0 = 3f, Out1 = 7f, Spin0 = 2.5f, Spin1 = 6f, AxisTilt = 0.4f;
        /// <summary>The longest flight (s), the farthest landing from the throw's start (m, the ship's way included), the least out kick (m/s).</summary>
        public static float MaxFlightSec = 4.2f, MaxReachM = 9f, OutFloor = 1.5f;
        /// <summary>A piece's typical up throw out of the blast (m/s): the reference flight the ship's way is counted against.</summary>
        public static float PieceUpMps = 2f;
        public const float G = 9.81f;

        /// <summary>One body's kick. up / outward = the kick (m/s; outward is the body's whole flat speed away from the ship, the chest's own hit
        /// included); outSet = what is given to its bones flat (outward less the chest hit's share); spin (rad/s) about axis; flightSec / apexM
        /// / reachM = its flight (s), how high it rises over its start (m), how far from a plainly thrown piece's landing it comes down (m, its
        /// kick and the ship's way); driftM = how much farther the ship's way carries it than such a piece; up0 / out0 = the draws before any cap.</summary>
        public struct Plan
        {
            public float up, outward, outSet, spin, flightSec, apexM, reachM, driftM, up0, out0;
            public Vector3 axis; public bool upCapped, upForReach, outCapped;
        }

        /// <summary>The k-th draw of a seed, 0..1 (a fixed mix: the kit's twin repeats it exactly).</summary>
        public static float U(uint seed, int k)
        {
            uint h = unchecked(seed ^ ((uint)(k + 1) * 0x9E3779B1u) ^ 0x425A11u);
            h = unchecked(h ^ (h >> 16)); h = unchecked(h * 0x7feb352du); h = unchecked(h ^ (h >> 15)); h = unchecked(h * 0x846ca68bu); h = unchecked(h ^ (h >> 16));
            return (h & 0xFFFFFFu) / 16777215f;
        }

        /// <summary>Time (s) until a body thrown up at `up` m/s from `h` m over the ground is back on it.</summary>
        public static float FlightSec(float up, float h) => (up + Mathf.Sqrt(up * up + 2f * G * Mathf.Max(0f, h))) / G;

        /// <summary>The kick of one body. seed = its throw's seed; heightM = its height over the ground (m); shipAlongMps / shipAcrossMps = the
        /// ship's flat speed along the body's flat throw and across it (m/s); shipUpMps = the ship's climb (m/s, negative = sinking);
        /// pushShare = the flat speed the chest's own hit adds to the whole body along its throw (m/s).</summary>
        public static Plan Make(uint seed, float heightM, float shipAlongMps, float shipAcrossMps, float shipUpMps, float pushShare)
        {
            var p = new Plan();
            p.up0 = p.up = Mathf.Lerp(Up0, Up1, U(seed, 0));
            p.out0 = p.outward = Mathf.Lerp(Out0, Out1, U(seed, 1));
            p.spin = Mathf.Lerp(Spin0, Spin1, U(seed, 2));
            float a = U(seed, 3) * 6.2831853f, z = (U(seed, 4) * 2f - 1f) * AxisTilt, r = Mathf.Sqrt(Mathf.Max(0f, 1f - z * z));
            p.axis = new Vector3(r * Mathf.Cos(a), z, r * Mathf.Sin(a));
            float h = Mathf.Max(0f, heightM), al = shipAlongMps, ac = Mathf.Abs(shipAcrossMps);
            float T0 = FlightSec(Mathf.Max(0f, shipUpMps) + PieceUpMps, h);                                   // a piece's flight from there
            if (FlightSec(p.up + shipUpMps, h) > MaxFlightSec)
            {   // up high: as much up kick as still lands in time
                p.up = Mathf.Max(0f, (0.5f * G * MaxFlightSec * MaxFlightSec - h) / MaxFlightSec - shipUpMps);
                p.upCapped = true;
            }
            if (Far(OutFloor, p.up, h, al, ac, shipUpMps, T0) > MaxReachM && (al != 0f || ac != 0f))
            {   // a fast ship carries a long flight past the pieces even with the least out kick: a lower kick (never under Up0 for this)
                float lo = Mathf.Min(Up0, p.up), hi = p.up;                                                      // lo: the highest kick found that lands near enough
                if (Far(OutFloor, lo, h, al, ac, shipUpMps, T0) <= MaxReachM)
                    for (int i = 0; i < 16; i++) { float m = 0.5f * (lo + hi); if (Far(OutFloor, m, h, al, ac, shipUpMps, T0) > MaxReachM) hi = m; else lo = m; }
                p.up = Mathf.Min(p.up, lo); p.upCapped = true; p.upForReach = true;
            }
            float T = FlightSec(p.up + shipUpMps, h), d = Mathf.Max(0f, T - T0);
            p.driftM = Mathf.Sqrt(al * al + ac * ac) * d;
            float room = MaxReachM * MaxReachM - ac * d * ac * d;                                               // where it lands from the pieces' middle:
            float outMax = room > 0f ? (Mathf.Sqrt(room) - al * d) / Mathf.Max(0.1f, T) : 0f;                  // |kick x T + the ship's way x (T - T0)| <= MaxReachM
            outMax = Mathf.Max(OutFloor, outMax);
            if (p.outward > outMax) { p.outward = outMax; p.outCapped = true; }
            p.outSet = Mathf.Max(0f, p.outward - Mathf.Max(0f, pushShare));
            float vy = Mathf.Max(0f, p.up + shipUpMps);
            p.flightSec = T; p.apexM = vy * vy / (2f * G); p.reachM = Far(p.outward, p.up, h, al, ac, shipUpMps, T0);
            return p;
        }

        /// <summary>Where a body with this out / up kick lands from a plainly thrown piece's landing (m): its kick's way plus the ship's way
        /// during its longer flight (along and across its throw).</summary>
        public static float Far(float outward, float up, float h, float shipAlong, float shipAcross, float shipUp, float T0)
        {
            float T = FlightSec(up + shipUp, h), d = Mathf.Max(0f, T - T0), x = outward * T + shipAlong * d, y = shipAcross * d;
            return Mathf.Sqrt(x * x + y * y);
        }

        /// <summary>The old soft throw's up speed (m/s) for the same body (RobotRagdoll.Launch: 1.7 .. 2.5 x strength x lift; the shuttle's
        /// crew: strength 0.5 + 0.15 x power on the ground, (0.85 + 0.25 x power) x the big blast's 1.6 in the air, lift 0.5 / 0.9), for the check.</summary>
        public static float OldUp(bool air, float power, float u) => Mathf.Lerp(1.7f, 2.5f, u) * (air ? (0.85f + 0.25f * power) * 1.6f : 0.5f + 0.15f * power) * (air ? 0.9f : 0.5f);
    }
}
