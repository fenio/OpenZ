using System;

namespace OpenZ
{
    /// <summary>
    /// Nothing here touches the sim: damage, range, cooldowns and hit rolls stay the Zod Engine's (zobject.cpp:2102-2213). These
    /// classes only decide WHEN a visible round / bolt / crack / pose happens on top of the sim's rolls:
    /// <list type="bullet">
    /// <item>MgBurst: the psycho's machine gun. Every Zod client fire tick (rpsycho.cpp:151-172, one bullet per 0.17 s) arms it; it
    /// plays MG bursts whose rounds sit exactly on the round onsets of the burst clip being heard (6-10 rounds at 12-15 rounds/s,
    /// 0.10-0.30 s pauses), about 11 visible rounds/s against the grunt's 1 bolt/s.</item>
    /// <item>SniperCadence: an aimed shot every 1.05-1.6 s (Zod's client: 0.78 s, rsniper.cpp:151-175) with a visible aim before
    /// it; the killing roll always shows at once. Damage keeps Zod's 0.4 s rolls.</item>
    /// <item>PickStance: stand / kneel / prone, random but stable per engagement (hash of unit id and engagement number).</item>
    /// <item>ToughTimeline: carry -> raise -> settle (ready) -> fire recoil -> Zod's 0.7-1.0 s aim hold (rtough.cpp:211) -> lower.</item>
    /// </list>
    /// </summary>
    public static class WeaponsV2Logic
    {
        // ------------------------------------------------------------------------------------------ deterministic random
        public struct Rng
        {
            uint s;
            public Rng(int seed) { s = (uint)(seed * 747796405 + 2891336453u) | 1u; Next(); Next(); }
            public float Next() { s ^= s << 13; s ^= s >> 17; s ^= s << 5; return (s & 0xFFFFFF) / 16777216f; }
            public float Range(float a, float b) => a + (b - a) * Next();
            public int Int(int n) => n <= 1 ? 0 : Math.Min(n - 1, (int)(Next() * n));
        }

        public static int Hash(int a, int b) { unchecked { uint h = (uint)a * 0x9E3779B1u ^ ((uint)b + 0x7F4A7C15u) * 0x85EBCA77u; h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; return (int)(h & 0x7FFFFFFF); } }

        static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        static float Smooth(float x) { x = Clamp01(x); return x * x * (3f - 2f * x); }

        // ------------------------------------------------------------------------------------------ psycho machine gun
        public sealed class MgClip
        {
            public readonly string name; public readonly float dur; public readonly float[] onsets;
            public MgClip(string name, float dur, float[] onsets) { this.name = name; this.dur = dur; this.onsets = onsets; }
        }

        /// <summary>
        /// the round onsets measured on them (tools/clips.json). A burst fires one tracer on every onset.</summary>
        public static readonly MgClip[] MgClips =
        {
            new MgClip("wv2_mg_1", 0.950f, new[] { 0.008f, 0.056f, 0.124f, 0.196f, 0.264f, 0.336f, 0.408f, 0.480f }),
            new MgClip("wv2_mg_2", 0.950f, new[] { 0.008f, 0.056f, 0.120f, 0.188f, 0.256f, 0.328f, 0.396f, 0.468f, 0.536f, 0.600f }),
            new MgClip("wv2_mg_3", 0.950f, new[] { 0.008f, 0.072f, 0.136f, 0.208f, 0.276f, 0.348f, 0.420f, 0.484f }),
            new MgClip("wv2_mg_4", 0.950f, new[] { 0.008f, 0.088f, 0.180f, 0.272f, 0.360f, 0.448f, 0.532f }),
            new MgClip("wv2_mg_5", 1.000f, new[] { 0.008f, 0.060f, 0.128f, 0.204f, 0.280f, 0.356f, 0.432f, 0.504f }),
            new MgClip("wv2_mg_6", 1.000f, new[] { 0.008f, 0.056f, 0.124f, 0.200f, 0.272f, 0.344f, 0.420f, 0.496f, 0.564f, 0.632f }),
        };
        /// <summary>Pause between bursts (s) and how long one Zod client fire tick keeps the gun armed (the psycho's tick is 0.14-0.2 s).</summary>
        public const float MgPauseMin = 0.10f, MgPauseMax = 0.30f, MgArmHold = 0.40f;

        public sealed class MgBurst
        {
            public float ArmedUntil = -1f, NextBurstAt, BurstStart = -1f;
            public int Clip = -1, NextRound, LastClip = -1, Bursts, Rounds;
            Rng rng;
            public MgBurst(int seed) { rng = new Rng(seed); }
            public bool InBurst => Clip >= 0;
            public void Arm(float now) { if (now + MgArmHold > ArmedUntil) ArmedUntil = now + MgArmHold; }
            public bool Armed(float now) => now < ArmedUntil;

            /// <summary>Advances to `now`. Returns the clip index when a new burst starts this step (the caller plays that clip), else -1;
            /// roundsDue = rounds whose onset passed (normally 0 or 1 per frame).</summary>
            public int Step(float now, out int roundsDue)
            {
                roundsDue = 0; int started = -1;
                if (Clip < 0 && now < ArmedUntil && now >= NextBurstAt)
                {
                    int c = rng.Int(MgClips.Length);
                    if (c == LastClip) c = (c + 1 + rng.Int(MgClips.Length - 1)) % MgClips.Length;    // never the same burst twice in a row
                    Clip = c; BurstStart = now; NextRound = 0; Bursts++; started = c;
                }
                if (Clip >= 0)
                {
                    var clip = MgClips[Clip];
                    while (NextRound < clip.onsets.Length && now - BurstStart >= clip.onsets[NextRound]) { NextRound++; roundsDue++; Rounds++; }
                    if (NextRound >= clip.onsets.Length)
                    {
                        LastClip = Clip; Clip = -1;
                        NextBurstAt = BurstStart + clip.onsets[clip.onsets.Length - 1] + rng.Range(MgPauseMin, MgPauseMax);
                    }
                }
                return started;
            }
        }

        /// <summary>Mean visible MG rounds per second while firing continuously (for the parity note and the harness).</summary>
        public static double MgMeanRate()
        {
            double rounds = 0, time = 0;
            foreach (var c in MgClips) { rounds += c.onsets.Length; time += c.onsets[c.onsets.Length - 1] + 0.5 * (MgPauseMin + MgPauseMax); }
            return rounds / time;
        }

        // ------------------------------------------------------------------------------------------ sniper
        /// <summary>Visible aimed shot every SniperGapMin-Max s; the aim (laser sight + scope glint) shows SniperAimLead s before a
        /// shot is allowed and stays until it fires; a new engagement waits SniperFirstAim s before its first visible shot.</summary>
        public const float SniperGapMin = 1.05f, SniperGapMax = 1.6f, SniperAimLead = 0.6f, SniperFirstAim = 0.75f;
        /// <summary>retry: a sniper going prone fires its first aimed shot only when it lies steady (prone down 0.8 s + a breath).</summary>
        public const float SniperProneFirstAim = 1.05f;

        public sealed class SniperCadence
        {
            public float NextAllowed = -1f, LastShot = -100f, EngagedAt = -100f;
            public int Shots, Skipped, Kills;
            Rng rng;
            public SniperCadence(int seed) { rng = new Rng(seed); }

            /// <summary>A new target: the first visible shot waits for the aim.</summary>
            public void Engage(float now) { EngagedAt = now; NextAllowed = Math.Max(NextAllowed, now + SniperFirstAim); }

            /// <summary>Zod's client clock wants to draw a shot: allowed when the aim is done, or always for the killing roll.</summary>
            public bool Allow(float now, bool kill) => Allow(now, kill, false);

            /// <summary>hold = the body is not steady yet (retry: going prone): only the killing roll shows.</summary>
            public bool Allow(float now, bool kill, bool hold)
            {
                if (kill || (now >= NextAllowed && !hold))
                {
                    if (kill) Kills++;
                    LastShot = now; NextAllowed = now + rng.Range(SniperGapMin, SniperGapMax); Shots++;
                    return true;
                }
                Skipped++;
                return false;
            }

            /// <summary>Aim visual weight 0..1: ramps in over SniperAimLead before the next allowed shot, holds until it fires, and is
            /// 0 for the 0.25 s after a shot (the rifle kicks).</summary>
            public float Aim(float now)
            {
                if (now - LastShot < 0.25f) return 0f;
                float lead = NextAllowed - now;
                return lead <= 0f ? 1f : Smooth(1f - lead / SniperAimLead);
            }
        }

        public enum Stance { Stand, Kneel, Prone }
        /// <summary>Target at >= LongRange x attack radius = a long shot: prone 35 %, kneel 43 %, stand 22 %; closer: kneel 40 %.</summary>
        public const float LongRange = 0.55f;
        public const float KneelDown = 0.45f, KneelUp = 0.35f, ProneDown = 0.80f, ProneUp = 0.60f;

        public static Stance PickStance(int unitId, int engagement, float range01)
        {
            var r = new Rng(Hash(unitId * 7919 + 13, engagement));
            float u = r.Next();
            if (range01 >= LongRange) return u < 0.35f ? Stance.Prone : u < 0.78f ? Stance.Kneel : Stance.Stand;
            return u < 0.40f ? Stance.Kneel : Stance.Stand;
        }

        // ------------------------------------------------------------------------------------------ tough bazooka
        public enum ToughPhase { Carry, Raise, Ready, Recoil, Hold }

        /// <summary>The Tough's bazooka rhythm. Raise01: 0 = carried (muzzle up on the shoulder), 1 = on target. With deviation DV11 (the
        /// default) the sim fires the first rocket from the carry 0.8 s after the engage and the view is raised + settled in 0.6 s (it
        /// hurries at game speed 2-4x), so nothing is held. Zod exact (DV11 off, fires on the engage tick): CombatView's rocket is held
        /// until Ready (at most 0.6 x its flight, so it still lands on the sim's blast).</summary>
        public sealed class ToughTimeline
        {
            /// <summary>LowerDelay = ZWorld.ToughCarrySeconds (DV11, 1.6 s; also robot_models_v2's BS01Stance.toughLinger): a target within
            /// it finds the tube still up and the sim fires on Zod's cycle; after it the sim prepares again while the view raises again.</summary>
            public const float RaiseTime = 0.38f, SettleTime = 0.22f, RecoilTime = 0.35f, LowerDelay = 1.6f, LowerTime = 0.5f;
            /// <summary>DV11 (ZObject.ToughPrepare.cs): the sim's first rocket from the carry leaves 0.8 s after the engage.</summary>
            public const float SimPrepare = 0.8f;
            public const float ZodAimHoldMin = 0.7f, ZodAimHoldMax = 1.0f;                           // rtough.cpp:211
            public ToughPhase Phase = ToughPhase.Carry;
            public float Raise01, RaisedAt = -100f, FiredAt = -100f, HoldUntil = -100f, LastTargetAt = -100f, HurryUntil = -100f;
            public int Fired;

            /// <summary>A rocket is held for the raise but must leave by `until` (0.6 x its flight): raise faster so the tube is on the
            /// target when it leaves (close targets).</summary>
            public void Hurry(float until) { if (until > HurryUntil) HurryUntil = until; }
            Rng rng;
            public ToughTimeline(int seed) { rng = new Rng(seed); }

            public void Step(float now, float dt, bool target)
            {
                if (target) LastTargetAt = now;
                bool keep = target || now - LastTargetAt < LowerDelay || now - FiredAt < RecoilTime + ZodAimHoldMin;
                float prev = Raise01;
                float rate = 1f / RaiseTime;
                if (now < HurryUntil) rate = Math.Max(rate, (1f - Raise01) / Math.Max(0.02f, HurryUntil - now));
                Raise01 = keep ? Math.Min(1f, Raise01 + dt * rate) : Math.Max(0f, Raise01 - dt / LowerTime);
                if (Raise01 >= 1f && prev < 1f) RaisedAt = now;
                if (Raise01 <= 0f) Phase = ToughPhase.Carry;
                else if (Raise01 < 1f) Phase = ToughPhase.Raise;
                else if (now - FiredAt < RecoilTime) Phase = ToughPhase.Recoil;
                else if (now < HoldUntil) Phase = ToughPhase.Hold;
                else Phase = now - RaisedAt >= SettleTime ? ToughPhase.Ready : ToughPhase.Raise;
            }

            /// <summary>Seconds until the bazooka is raised and settled (0 = ready to fire).</summary>
            public float ReadyIn(float now)
            {
                if (Raise01 < 1f) return (1f - Raise01) * RaiseTime + SettleTime;
                return Math.Max(0f, RaisedAt + SettleTime - now);
            }

            /// <summary>Zod: two fire frames of 0.05-0.08 s, then the aim frame is held 0.7-1.0 s (rtough.cpp:138-220).</summary>
            public void OnFire(float now) { FiredAt = now; HoldUntil = now + rng.Range(0.10f, 0.16f) + rng.Range(ZodAimHoldMin, ZodAimHoldMax); Fired++; }

            /// <summary>Displayed raise (eased), 0 carry .. 1 aim.</summary>
            public float RaiseShown => Smooth(Raise01);

            /// <summary>retry, DV11: the sim's first rocket leaves in `realLeft` s (ZObject.ToughReadyAt, real time at the game speed):
            /// raise faster when the normal raise + settle would not be done by then, so the rocket always leaves a steady tube.</summary>
            public void SyncToSim(float now, float realLeft)
            {
                if (realLeft > 0f && ReadyIn(now) > realLeft - 0.02f) Hurry(now + Math.Max(0.02f, realLeft - SettleTime));
            }
        }

        /// <summary>Recoil envelope: fast attack (15 ms), 0.17 s decay, peak ~1 at ~50 ms (UnitView.Recoil shape, stronger tail).</summary>
        public static float Kick(float age) => age < 0f || age > 1.2f ? 0f : (1f - (float)Math.Exp(-age / 0.015f)) * (float)Math.Exp(-age / 0.17f) * 1.35f;

        /// <summary>Settle wobble after a kick or a raise: a damped 2.4 Hz swing, 0 at age 0.</summary>
        public static float Wobble(float age) => age < 0f || age > 1.5f ? 0f : (float)(Math.Exp(-age / 0.22f) * Math.Sin(age * Math.PI * 2 * 2.4));

        // ------------------------------------------------------------------------------------------ laser
        /// <summary>Zod's laser draws one 16 px (2 m) white-cored green dash moving at 300 px/s (elaser.cpp:9, bullet_n00/01.png);
        /// OpenZ fires a double tap of 1.7 m dashes per visible shot (Star Wars blaster rhythm), 0.085 s apart, at Zod's speed.</summary>
        public const int LaserBoltsPerShot = 2;
        public const float LaserBoltGap = 0.085f, LaserBoltLength = 1.7f;
    }
}
