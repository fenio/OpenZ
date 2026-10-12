using System;

namespace OpenZ
{
    /// <summary>
    /// View only, pure
    /// logic (VehicleLights applies it to the lamp glows, the haze cones, the real headlight spot and the NightPool impostor).
    /// Deterministic per vehicle id (the same vehicle always looks the same, the Sim is never read or written), VERY subtle:
    /// <list type="bullet">
    /// <item>glow size (lens, hot bulb, halo) 5 steps of 3.5 %: 0.93 .. 1.07;</item>
    /// <item>lamp power (glow channel, cone, pool, real spot intensity) continuous 0.89 .. 1.11;</item>
    /// <item>beam reach (cone length, pool length, spot range) 5 steps of 5.5 %: 0.89 .. 1.11, loosely following the power
    /// (a brighter lamp mostly reaches a bit further);</item>
    /// <item>colour temperature: 30 % a bit warmer (x 1.03 / 0.995 / 0.90), 40 % as is, 30 % a bit cooler (x 0.955 / 1.0 / 1.10),
    /// luminance kept within 0.5 %;</item>
    /// <item>35 % of the vehicles have one lamp a little weaker (x 0.84 .. 0.92): the lamp on HeadlightFaults' failing side, so the
    /// weak lamp is the one that goes first when the vehicle is shot up.</item>
    /// </list>
    /// Size, colour and reach are stepped so the glow / cone meshes stay shared (at most 15 glow variants x 5 reach variants per
    /// </summary>
    public static class HeadlightVariety
    {
        public static bool Enabled = true;
        public const float SizeStep = 0.035f, ReachStep = 0.055f, PowerSpread = 0.11f, WeakShare = 0.35f;

        public struct Look
        {
            public float size, power, reach, weak, tr, tg, tb;
            public int sizeStep, tempStep, reachStep;
            /// <summary>Mesh-cache key of the stepped parts (size + colour step).</summary>
            public string Key => "v" + sizeStep + tempStep;
            public string Temp => tempStep == 0 ? "warm" : tempStep == 2 ? "cool" : "neutral";
            public override string ToString() => $"size {size:F3} power {power:F3} reach {reach:F3} {Temp} weak {weak:F2}";
        }

        public static readonly Look Plain = new Look { size = 1f, power = 1f, reach = 1f, weak = 1f, tr = 1f, tg = 1f, tb = 1f, sizeStep = 2, tempStep = 1, reachStep = 2 };

        static uint Hash(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; } }
        static float U(uint h) => (h & 0xFFFFFFu) / 16777216f;

        /// <summary>The headlight look of a vehicle id (stable for the whole match; its own hash stream, independent of HeadlightFaults).</summary>
        public static Look For(int id)
        {
            if (!Enabled) return Plain;
            uint h = Hash(unchecked((uint)id * 2246822519u + 0x68E31DA4u));
            var l = new Look();
            l.sizeStep = (int)(Hash(h + 3u) % 5u);
            l.size = 1f + (l.sizeStep - 2) * SizeStep;
            l.power = 1f + (2f * U(Hash(h + 11u)) - 1f) * PowerSpread;
            float rp = (l.power - 1f) / PowerSpread * 2f + (U(Hash(h + 19u)) - 0.5f) * 1.2f;
            l.reachStep = Math.Max(0, Math.Min(4, (int)MathF.Round(rp) + 2));
            l.reach = 1f + (l.reachStep - 2) * ReachStep;
            float t = U(Hash(h + 29u));
            l.tempStep = t < 0.3f ? 0 : t < 0.7f ? 1 : 2;
            if (l.tempStep == 0) { l.tr = 1.03f; l.tg = 0.995f; l.tb = 0.90f; }
            else if (l.tempStep == 2) { l.tr = 0.955f; l.tg = 1.0f; l.tb = 1.10f; }
            else { l.tr = 1f; l.tg = 1f; l.tb = 1f; }
            l.weak = U(Hash(h + 37u)) < WeakShare ? 0.84f + 0.08f * U(Hash(h + 41u)) : 1f;
            return l;
        }
    }
}
