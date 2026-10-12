using System;

namespace OpenZ
{
    /// <summary>
    /// View only, pure logic (VehicleLights draws it). Deterministic per vehicle id: which lamp can fail (left / right),
    /// at what hull health it fails, and how:
    /// <list type="bullet">
    /// <item>
    /// has ONE lamp that fails (NB10 left 30 % without a fault), so below ~40 % hp every vehicle shows one lamp dead or flickering;</item>
    /// <item>the lamp fails below a per-vehicle threshold (NB10's 45-70 % of max, the former exempt 30 % at 40-43 %): 45 % of the
    /// vehicles it is dead (a spark + glass tinkle the moment it goes), 55 % it has a loose contact: a 0.3-0.9 s burst of fast
    /// flicker every 2-5 s (was 3.5-9 s), at 0.8 between the bursts (was 0.86); VehicleLights crackles each burst (ngt_lamp_flicker)
    /// and the lamp's glow, haze cone, headlamp glass, real spot and ground pool all follow it;</item>
    /// <item>below 20 % health a flickering lamp gives up for good (dead);</item>
    /// <item>repaired (repair station / crane: health back over the threshold) = the lamp works again.</item>
    /// </list>
    /// The flicker timing uses view time (per-vehicle phase), the choice of lamp / threshold / mode the vehicle id only.
    /// </summary>
    public static class HeadlightFaults
    {
        public static bool Enabled = true;
        /// <summary>lights_tail_v1 tuning: every vehicle has its lamp fault below AllBelow of its hull health; a flickering lamp dies
        /// below DieBelow (spark + glass tinkle); flicker bursts every PeriodMin .. PeriodMin + PeriodSpan s; Between = the level of
        /// the loose lamp between two bursts.</summary>
        public const float AllBelow = 0.40f, DieBelow = 0.20f, PeriodMin = 2f, PeriodSpan = 3f, Between = 0.8f;
        public enum Mode { None, Dead, Flicker }

        public struct Plan { public Mode mode; public int side; public float at, seed; }

        static uint Hash(uint x) { unchecked { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; } }
        static float U(uint h) => (h & 0xFFFFFFu) / 16777216f;

        /// <summary>The fault a vehicle id carries (stable for the whole match).</summary>
        public static Plan For(int id)
        {
            uint h = Hash((uint)id * 2654435761u + 0x9E3779B9u);
            var p = new Plan { seed = U(Hash(h ^ 0xA5A5A5u)) };
            float k = U(h);
            // lights_tail_v1: no exemption any more (NB10 left 30 % without a fault): same lamp / mode / threshold as NB10 for the
            // others, the former 30 % fail at 40-43 % hp
            p.mode = U(Hash(h + 17u)) < 0.45f ? Mode.Dead : Mode.Flicker;
            p.side = (Hash(h + 31u) & 1u) == 0 ? -1 : 1;
            p.at = k < 0.30f ? AllBelow + 0.03f * U(Hash(h + 71u)) : 0.45f + 0.25f * U(Hash(h + 53u));
            return p;
        }

        /// <summary>Light level 0..1 of the faulty lamp at health fraction hp and view time now (1 = a healthy lamp).</summary>
        public static float Level(Plan p, float hp, float now)
        {
            if (!Enabled || p.mode == Mode.None || hp > p.at) return 1f;
            if (p.mode == Mode.Dead || hp < DieBelow) return 0f;                              // lights_tail_v1: DieBelow (0.2)
            // loose contact: bursts of fast flicker now and then, a little dimmer between them. lights_tail_v1: every 2-5 s (NB10
            // 3.5-9 s), 0.3-0.9 s long, 0.8 between the bursts (NB10 0.86): it reads on a damaged vehicle at the RTS camera
            if (OzMotion.Reduced || !BurstAt(p, now, out _, out uint hk, out float t)) return Between;
            int step = (int)MathF.Floor(t * 24f);
            float r = U(Hash((uint)step * 2891336453u + hk));
            return r < 0.5f ? 0.05f : r < 0.75f ? 0.45f : 1f;
        }

        /// <summary>lights_tail_v1: true while the loose-contact lamp is inside a flicker burst; burst = the burst's running number
        /// (VehicleLights plays ngt_lamp_flicker once per burst of a lit lamp near the camera).</summary>
        public static bool Burst(Plan p, float hp, float now, out int burst)
        {
            burst = 0;
            if (!Enabled || OzMotion.Reduced || p.mode != Mode.Flicker || hp > p.at || hp < DieBelow) return false;
            return BurstAt(p, now, out burst, out _, out _);
        }

        static bool BurstAt(Plan p, float now, out int k, out uint hk, out float t)
        {
            float period = PeriodMin + PeriodSpan * p.seed;
            t = now + p.seed * 97f;
            k = (int)MathF.Floor(t / period);
            hk = Hash((uint)k * 747796405u + (uint)(p.seed * 1e6f));
            float dur = 0.3f + 0.6f * U(hk), start = (0.3f + 0.4f * U(Hash(hk + 7u))) * MathF.Max(0.01f, period - dur);   // mid-period jitter: burst to burst ~ the period
            float inPeriod = t - k * period;
            return inPeriod >= start && inPeriod <= start + dur;
        }

        /// <summary>True when the lamp is out for good at this health (the moment it goes gets a spark).</summary>
        public static bool IsDead(Plan p, float hp) => Enabled && p.mode != Mode.None && hp <= p.at && (p.mode == Mode.Dead || hp < DieBelow);   // lights_tail_v1: DieBelow
    }
}
