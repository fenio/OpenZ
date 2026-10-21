// OpenZ (GPL-3.0) - the playable shuttle's night lights (rule DV118, view only): their rules in plain numbers. Nothing of the engine is
// used in this file, so the same code is also counted outside the editor. OzShuttleNightLights feeds it once a frame and draws what it says.
using System;
using System.Collections.Generic;

namespace OpenZ
{
    /// <summary>
    /// What each of the shuttle's night lights does this frame, from the ship's state, the level's night share and the enemies around it.
    /// <list type="bullet">
    /// <item>NIGHT: every light is eased on and off with the level's night share (NightGrade.NightK): nothing burns by day, at dusk they
    /// come up with the dark (fully lit from a night share of <see cref="FullAtNight"/>), at dawn they go down with it. Each light also
    /// switches on and off softly (<see cref="UpSec"/> / <see cref="DownSec"/>, the fort searchlights' times).</item>
    /// <item>(a) THE TWO LANDING LIGHTS under the hull: on while it comes down, while it is low in the air (under
    /// <see cref="LowFeetM"/>: the take-off, a low hover, the landing), while it flies the last <see cref="ApproachM"/> to its landing
    /// point, and for <see cref="AfterTouchSec"/> after the touchdown; aimed at the landing point while it comes down or flies in to it
    /// (<see cref="LandingAtGoal"/>), else straight down. Off at cruise height.</item>
    /// <item>(b) THE RAMP LIGHT over the back door: a warm light on the ramp and its foot while the ramp is down (the squad walking in or
    /// out), coming up as the ramp opens and going down after it has shut.</item>
    /// <item>(c) THE NOSE SEARCHLIGHT: on while it flies (pads over <see cref="NoseMinFeetM"/>); it sweeps the ground ahead in a slow arc
    /// (<see cref="SweepDeg"/> to each side of the nose, one swing every <see cref="SweepSec"/> s, a little nod).</item>
    /// <item>(d) THE LOCK: the nose searchlight takes the nearest enemy (robot, vehicle or gun) within <see cref="LockRangeM"/> and
    /// <see cref="LockYawMax"/> degrees of the nose and follows it (the fort searchlights' tracking: they follow what they caught as
    /// long as it stays in reach). When the beam is on it, it turns ALARMING: narrower (<see cref="NoseAngleDeg"/> to
    /// <see cref="AlarmAngleDeg"/>), with a harder edge, red-amber, brighter, with a slow warning pulse (<see cref="PulseSec"/>), and a
    /// short lock-on beep. When the enemy is gone (destroyed, out of reach, no longer an enemy, not drawn) it lets go and after a short
    /// pause takes the next one or eases back to the calm white sweep. CALM MODE (no flashes or blinking): the colour, the narrow beam
    /// and the beep stay, the pulse never runs.</item>
    /// <item>GRAPHICS QUALITY LOW: fewer real lights (<see cref="RealLights"/>: one landing light and the nose searchlight light the
    /// ground for real; the second landing light and the ramp light keep their glowing lens and the painted pool of light), and no
    /// light shadows (<see cref="ShadowCandidate"/>: on HIGH and MEDIUM the nose searchlight may cast shadows, as the fort's do).</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleNightPlan                                                    // K256d2: + OzShuttleNightPlan.Cone.cs (the nose searchlight's clear cone)
    {
        /// <summary>The rules' flight states (the values of ZShuttleState).</summary>
        public const int Landed = 0, Climb = 1, Cruise = 2, Descend = 3, Hover = 4;
        /// <summary>The lamps: the two landing lights, the ramp light, the nose searchlight.</summary>
        public const int LandingL = 0, LandingR = 1, RampLamp = 2, NoseLamp = 3, Lamps = 4;

        // ------------------------------------------------------------------ the numbers (to tune by eye in the game: numbers, not designs)
        /// <summary>The night share at which the lights burn fully; under it they come up with it (0 by day).</summary>
        public static float FullAtNight = 0.45f;
        /// <summary>Switching on / off (s): the landing lights and the nose searchlight (the fort searchlights' times), the ramp light.</summary>
        public static float UpSec = 0.35f, DownSec = 0.6f, RampUpSec = 0.5f, RampDownSec = 0.8f;
        /// <summary>The landing lights: on under this height (m), so long after the touchdown (s), within this of the landing point (m).</summary>
        public static float LowFeetM = 8f, AfterTouchSec = 1.5f, ApproachM = 24f;
        /// <summary>The nose searchlight burns while the pads are this high (m).</summary>
        public static float NoseMinFeetM = 3f;
        /// <summary>The sweep: degrees to each side of the nose, seconds of one swing, degrees under the horizon, the nod; the drum's rest
        /// (off: it looks down), its limits (degrees under the horizon); the aim's rates (sweeping, following: the fort searchlights').</summary>
        public static float SweepDeg = 28f, SweepSec = 9f, SweepPitchDeg = 30f, NodDeg = 3f, RestPitchDeg = 62f, PitchMin = 6f, PitchMax = 80f;
        public static float SweepRate = 2.4f, TrackRate = 4f;
        /// <summary>The lock: reach (m, flat), kept while within reach x KeepRangeK; degrees each side of the nose (kept with KeepYawExtra
        /// more); a look round every ScanSec; the pause after it let go (s); on the target when the beam is within OnTargetDeg (or after
        /// SwingMaxSec).</summary>
        public static float LockRangeM = 30f, KeepRangeK = 1.15f, LockYawMax = 110f, KeepYawExtra = 15f, ScanSec = 0.25f, CooldownSec = 0.8f;
        public static float OnTargetDeg = 5f, SwingMaxSec = 1.0f;
        /// <summary>The alarm: easing in / out (s), the warning pulse's period (s) and depth (share of the light taken at its dip).</summary>
        public static float AlarmUpSec = 0.3f, AlarmDownSec = 0.8f, PulseSec = 1.25f, PulseDepth = 0.45f;
        /// <summary>The nose searchlight's cone (degrees), calm and alarming; the alarm's extra strength; its edge (the inner cone's share).</summary>
        public static float NoseAngleDeg = 22f, AlarmAngleDeg = 11f, AlarmGain = 1.35f, CalmInner = 0.35f, AlarmInner = 0.8f;
        /// <summary>Colours (sRGB triples): the calm white (the fort searchlights'), the alarm's red-amber.</summary>
        public static readonly float[] CalmRgb = { 0.86f, 0.92f, 1f }, AlarmRgb = { 1f, 0.45f, 0.05f };       // K256e: amber-red (was 1, 0.32, 0.07: read as the red ground patch)
        /// <summary>K256e: at dusk the lights never come up faster than this share a second (the night gate is followed at most so fast:
        /// at least 2 s from dark to full; FIX_LIST M14 N2); at dawn and by day they go down with the gate as before.</summary>
        public static float NightUpPerSec = 0.5f;
        /// <summary>K256e: at a take-off the landing lights stay on at least TakeoffHoldSec after the lift-off (and while the pads are under
        /// LowFeetM), then fade out over TakeoffFadeSec (FIX_LIST M14: they burned only about 0.5 s and could not be seen).</summary>
        public static float TakeoffHoldSec = 2f, TakeoffFadeSec = 1f;

        // ------------------------------------------------------------------ one frame's input
        public struct Frame
        {
            /// <summary>Game seconds of this frame (0 while paused).</summary>
            public float Dt;
            /// <summary>Real seconds of this frame (0 = not known: then <see cref="Dt"/>). The warning pulse never runs faster than in
            /// real time (as the fort's alarm lights), so a higher game speed never turns it into a quick blink.</summary>
            public float RealDt;
            /// <summary>The rules' flight state (<see cref="Landed"/> .. <see cref="Hover"/>).</summary>
            public int State;
            public bool Airborne, Alive;
            /// <summary>The pads over what they would stand on (m).</summary>
            public float FeetM;
            /// <summary>The rules' landing point from the ship's ground point (m, flat; below 0 = none).</summary>
            public float GoalDistM;
            /// <summary>The drawn ramp's opening (0 shut .. 1 down).</summary>
            public float RampOpen01;
            /// <summary>The level's night share (0 day .. 1 night).</summary>
            public float NightK;
            /// <summary>CALM MODE · NO FLASHES OR BLINKING.</summary>
            public bool Calm;
        }

        /// <summary>An enemy the caller sees now: its id, its flat distance from the nose searchlight (m), the turn (degrees, + = right
        /// of the nose) and the drop (degrees under the horizon) the drum needs to look at it.</summary>
        public struct Seen { public int Id; public float DistM, YawDeg, PitchDeg; }

        // ------------------------------------------------------------------ what the caller hands in before Step
        /// <summary>True: fill <see cref="Candidates"/> this frame (every enemy in view), before Step.</summary>
        public bool ScanDue { get; private set; } = true;
        public readonly List<Seen> Candidates = new List<Seen>(16);
        /// <summary>The followed enemy as the caller measured it this frame (LockSeen false = it is gone or not drawn).</summary>
        public bool LockSeen; public float LockDistM, LockYawDeg, LockPitchDeg;

        // ------------------------------------------------------------------ what Step says
        /// <summary>The night share's gate (0 day .. 1 night).</summary>
        public float Night { get; private set; }
        /// <summary>Each light's wanted level and its eased level (0 .. 1, the night gate included).</summary>
        public float LandingTarget { get; private set; }
        public float Landing { get; private set; }
        public float RampTarget { get; private set; }
        public float Ramp { get; private set; }
        public float NoseTarget { get; private set; }
        public float Nose { get; private set; }
        /// <summary>The landing lights look at the landing point (else straight down).</summary>
        public bool LandingAtGoal { get; private set; }
        /// <summary>The followed enemy (-1 none); the beam is on it (the alarm runs).</summary>
        public int LockId { get; private set; } = -1;
        public bool Locked { get; private set; }
        /// <summary>The alarm (0 calm .. 1 alarming, eased) and the warning pulse (1 = full light; always 1 with CALM MODE).</summary>
        public float Alarm { get; private set; }
        public float Pulse { get; private set; } = 1f;
        /// <summary>The drum's turn from the nose and its drop under the horizon (degrees).</summary>
        public float YawNow { get; private set; }
        public float PitchNow { get; private set; } = 62f;
        /// <summary>The beep plays this frame (the beam has come onto a new enemy).</summary>
        public bool BeepNow { get; private set; }
        /// <summary>Counts: enemies taken, beams on them (= beeps), let go.</summary>
        public int Acquires { get; private set; }
        public int LockOns { get; private set; }
        public int Beeps { get; private set; }
        public int Releases { get; private set; }
        /// <summary>The plan's own clock (game seconds).</summary>
        public float Clock { get; private set; }

        float sinceAir = 1e6f, cooldown, scanT, swingT, pulseClock; bool wasAir;
        float nightUp, airT; bool airNow;                                                                   // K256e: the night gate as followed; time since the lift-off

        // ------------------------------------------------------------------ the rules
        public static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;
        static float Smooth01(float x) { x = Clamp01(x); return x * x * (3f - 2f * x); }
        static float Lerp(float a, float b, float k) => a + (b - a) * Clamp01(k);
        static float Toward(float v, float to, float step) => v < to ? Math.Min(to, v + step) : Math.Max(to, v - step);
        static float Delta(float a, float b) { float d = (b - a) % 360f; if (d > 180f) d -= 360f; if (d < -180f) d += 360f; return d; }

        /// <summary>The night gate of a night share: 0 by day, eased up to 1 at <see cref="FullAtNight"/>.</summary>
        public static float Gate(float nightK) => Smooth01(nightK / Math.Max(0.01f, FullAtNight));
        /// <summary>A level eased toward its target: up in upSec, down in downSec (linear, as every night light).</summary>
        public static float Ease(float level, float target, float dt, float upSec, float downSec) =>
            Toward(level, target, dt / Math.Max(0.01f, target > level ? upSec : downSec));
        /// <summary>Real lights a ship offers to the night-light budget on a preset (HIGH 0, MEDIUM 1: all four; LOW 2: two).</summary>
        public static int RealLights(int preset) { int n = 0; for (int i = 0; i < Lamps; i++) if (Offered(i, preset)) n++; return n; }
        /// <summary>Is lamp i offered to the budget as a real light on this preset (LOW: the first landing light and the nose only).</summary>
        public static bool Offered(int lamp, int preset) => preset < 2 || lamp == LandingL || lamp == NoseLamp;
        /// <summary>May lamp i cast shadows (the budget's searchlight shadows): the nose searchlight, not on LOW.</summary>
        public static bool ShadowCandidate(int lamp, int preset) => lamp == NoseLamp && preset < 2;

        /// <summary>The nose searchlight now: its cone (degrees), its edge (the inner cone's share), its strength (x its light; the
        /// level, the pulse and the alarm's extra), its colour (sRGB).</summary>
        public float AngleDeg => Lerp(NoseAngleDeg, AlarmAngleDeg, Alarm);
        public float InnerShare => Lerp(CalmInner, AlarmInner, Alarm);
        public float NoseStrength => Nose * Pulse * Lerp(1f, AlarmGain, Alarm);
        public void Colour(out float r, out float g, out float b)
        {
            r = Lerp(CalmRgb[0], AlarmRgb[0], Alarm); g = Lerp(CalmRgb[1], AlarmRgb[1], Alarm); b = Lerp(CalmRgb[2], AlarmRgb[2], Alarm);
        }

        /// <summary>One frame. Before it: when <see cref="ScanDue"/>, fill <see cref="Candidates"/>; while <see cref="LockId"/> >= 0, set
        /// LockSeen / LockDistM / LockYawDeg / LockPitchDeg from that enemy as drawn now.</summary>
        public void Step(Frame f)
        {
            float dt = f.Dt > 0f ? f.Dt : 0f;
            Clock += dt; BeepNow = false;
            float gate = f.Alive ? Gate(f.NightK) : 0f;
            nightUp = gate <= nightUp ? gate : Math.Min(gate, nightUp + dt * NightUpPerSec);              // K256e: up at most NightUpPerSec a second, down with the gate
            Night = nightUp;
            // (a) the landing lights
            if (f.Airborne) { sinceAir = 0f; wasAir = true; } else sinceAir += dt;
            if (f.Airborne) { if (!airNow) airT = 0f; airT += dt; } else airT = 0f;                       // K256e: the time since the lift-off
            airNow = f.Airborne;
            bool hold = f.Airborne && airT < TakeoffHoldSec;
            bool low = f.Airborne && f.FeetM < LowFeetM;
            bool down = f.Airborne && f.State == Descend;
            bool approach = f.Airborne && f.State == Cruise && f.GoalDistM >= 0f && f.GoalDistM <= ApproachM;
            bool touched = !f.Airborne && wasAir && sinceAir < AfterTouchSec;
            LandingAtGoal = (down || approach) && f.GoalDistM >= 0f;
            LandingTarget = low || down || approach || touched || hold ? Night : 0f;
            Landing = Ease(Landing, LandingTarget, dt, UpSec, f.Airborne && airT < TakeoffHoldSec + TakeoffFadeSec + 0.5f ? TakeoffFadeSec : DownSec);   // K256e: after a take-off they fade over 1 s
            // (b) the ramp light
            RampTarget = !f.Airborne && f.Alive && f.RampOpen01 > 0.02f ? Night * Clamp01(f.RampOpen01 * 3f) : 0f;
            Ramp = Ease(Ramp, RampTarget, dt, RampUpSec, RampDownSec);
            // (c) the nose searchlight
            NoseTarget = f.Airborne && f.FeetM > NoseMinFeetM ? Night : 0f;
            Nose = Ease(Nose, NoseTarget, dt, UpSec, DownSec);
            // (d) the lock: kept while the enemy stays in reach, else let go; a look round every ScanSec
            bool canLock = f.Alive && Nose >= 0.5f;
            if (LockId >= 0 && !(canLock && LockSeen && LockDistM <= LockRangeM * KeepRangeK && Math.Abs(LockYawDeg) <= LockYawMax + KeepYawExtra && KeptByCone(LockYawDeg, LockPitchDeg))) Release();   // K256d2: held at the clear cone's edge up to ConeHoldDeg past it
            if (cooldown > 0f) cooldown = Math.Max(0f, cooldown - dt);
            if (ScanDue)
            {
                if (LockId < 0 && cooldown <= 0f && canLock)
                {
                    int best = -1;
                    for (int i = 0; i < Candidates.Count; i++)
                    {
                        var c = Candidates[i];
                        if (c.DistM > LockRangeM || Math.Abs(c.YawDeg) > LockYawMax || !InClearCone(c.YawDeg, c.PitchDeg)) continue;   // K256d2: only inside the clear cone (OzShuttleNightPlan.Cone.cs)
                        if (best < 0 || c.DistM < Candidates[best].DistM || (c.DistM == Candidates[best].DistM && c.Id < Candidates[best].Id)) best = i;
                    }
                    if (best >= 0)
                    {
                        var c = Candidates[best];
                        LockId = c.Id; Locked = false; swingT = 0f; Acquires++;
                        LockSeen = true; LockDistM = c.DistM; LockYawDeg = c.YawDeg; LockPitchDeg = c.PitchDeg;
                    }
                }
                scanT = ScanSec;
            }
            else scanT -= dt;
            ScanDue = scanT <= 0f;
            // the aim: off = the drum looks down; following = on the enemy; else the slow sweep ahead with its nod
            float tYaw, tPitch, rate;
            if (NoseTarget <= 0f && Nose <= 0.01f) { tYaw = 0f; tPitch = RestPitchDeg; rate = SweepRate; }
            else if (LockId >= 0) { tYaw = LockYawDeg; tPitch = LockPitchDeg; rate = TrackRate; }
            else
            {
                double ph = Clock * 2.0 * Math.PI / Math.Max(1f, SweepSec);
                tYaw = SweepDeg * (float)Math.Sin(ph); tPitch = SweepPitchDeg + NodDeg * (float)Math.Sin(ph * 2.3); rate = SweepRate;
            }
            tPitch = tPitch < PitchMin ? PitchMin : tPitch > PitchMax ? PitchMax : tPitch;
            float a = 1f - (float)Math.Exp(-rate * dt);
            ConeFollow(tYaw, tPitch, a);                                                       // K256d2: the follow as before, the drawn turn eased into the clear cone (OzShuttleNightPlan.Cone.cs)
            if (LockId >= 0 && !Locked)
            {   // the beam comes onto the enemy: the alarm starts and the beep plays, once per enemy
                swingT += dt;
                if ((Math.Abs(Delta(YawNow, tYaw)) <= OnTargetDeg && Math.Abs(PitchNow - tPitch) <= OnTargetDeg) || swingT >= SwingMaxSec)
                { Locked = true; LockOns++; Beeps++; BeepNow = true; pulseClock = 0f; }
            }
            Alarm = Ease(Alarm, Locked ? 1f : 0f, dt, AlarmUpSec, AlarmDownSec);
            pulseClock += f.RealDt > 0f ? Math.Min(dt, f.RealDt) : dt;                            // never faster than real time (game speed x4: still 0.8 a second)
            Pulse = f.Calm || Alarm <= 0f ? 1f : 1f - PulseDepth * Alarm * (0.5f - 0.5f * (float)Math.Cos(pulseClock * 2.0 * Math.PI / Math.Max(0.2f, PulseSec)));
        }

        void Release()
        {
            LockId = -1; Locked = false; LockSeen = false; Releases++; cooldown = CooldownSec;
        }

        /// <summary>The ship is gone (shot down): every light out at once, the lock let go.</summary>
        public void Out()
        {
            Landing = Ramp = Nose = Alarm = 0f; LandingTarget = RampTarget = NoseTarget = 0f; Pulse = 1f; nightUp = 0f; Night = 0f;
            if (LockId >= 0) Release();
        }
    }
}
