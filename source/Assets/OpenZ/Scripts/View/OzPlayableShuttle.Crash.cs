using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's CRASH landing (rule DV118: the rules decide it and own its clock, ZObject.ShuttleRoughLanding /
    /// ShuttleRoughSide / ShuttleTouchdownAt / ShuttleLevelStartAt / ShuttleLevelledAt; no hit points are lost). View only.
    /// A SCRIPTED pose on the rules' clock (the same picture at any frame rate and game speed; the twin tools/e_flight_twin.py of the
    /// kit holds every number of it, line for line):
    /// <list type="bullet">
    /// <item>THE WAY DOWN: from 1.3 s before the contact it wobbles (4 degrees of roll at 2.4 Hz, eased in and out), Alan calls "Coming in
    /// hot!" 1.0 s before the contact (a crash never says the landing line), and the last 0.5 s it drops fast (5 m/s at the contact),
    /// leaning 14 degrees to the side the rules name, 3.5 degrees nose down.</item>
    /// <item>THE SLAM (0 s): the low side's pads hit: the crash and the heavy metal sounds, sparks and a ring of dust at each pad, two
    /// gouges in the ground, a few torn bits of plating, a camera kick; robots within 6 m of the hull flinch (a stagger within 3 m).
    /// The low side REBOUNDS 0.32 m (on gravity) while the hull rolls over.</item>
    /// <item>THE OTHER SIDE (0.33 s): its pads bang down while the first side is still up: a clank, sparks and a second puff.</item>
    /// <item>THE SLUMP (0.51 s): the first side comes down again and its struts give 0.10 m; the hull slumps onto that side, its other
    /// side lifting (a damped swing with one small overshoot), and it SLIDES 1.15 m along its flight (slewing 5 degrees) with the
    /// scrape's sound, streaks of dust thrown out from under the scraping pads and a dark scrape mark 5-6 m long left in the ground
    /// (it fades after 25 s).</item>
    /// <item>AT REST (from about 0.7 s to the rules' levelling start, 2.4 s): crooked, 13.7 degrees, the high side's pads 0.85 m off the
    /// ground, a thin smoke wisp from the low pod.</item>
    /// <item>RIGHTED (2.4 -> 3.6 s, the rules' levelling): the levelling hydraulic and a servo; it rights itself and slides back onto the
    /// rules' point and heading, exactly level at the rules' levelled moment (the ramp, EXIT and a take-off wait for it).</item>
    /// </list>
    /// OzShuttleTouchdown stays the state holder (its Active: the landed ring stays hidden, the older proof's checks; its statics and its
    /// log are written here), its physics is no longer stepped. The dust comes through the ship's own downwash (OzShuttleDownwash: Burst,
    /// Ring, Streak), the fort blast's soft fog family. Every visible action here has its sound.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        // ------------------------------------------------------------------ the numbers (the twin: tools/e_flight_twin.py)
        /// <summary>The lean (deg, to the low side) and nose-down pitch (deg) at the contact, the speed at the contact (m/s), the fast last
        /// part of the descent (s).</summary>
        public static float CrashLeanDeg = 14f, CrashPitchDeg = 3.5f, CrashTouchMps = 5f, CrashFastSec = 0.5f;
        /// <summary>The low side's rebound (m), the moment the other side comes down (s after the contact), its sink just after the slam
        /// (m/s) and its own small rebound (m).</summary>
        public static float CrashHopM = 0.32f, CrashHighAt = 0.33f, CrashHighSinkMps = 1.5f, CrashHighHopM = 0.06f;
        /// <summary>The low side's struts give (m) over (s); the high side's lowest pad at rest (m); the slump's swing (damping ratio,
        /// angular frequency rad/s).</summary>
        public static float CrashStrutM = 0.10f, CrashStrutSec = 0.18f, CrashRestHighM = 0.85f, CrashSlumpZeta = 0.6f, CrashSlumpOmega = 11f;
        /// <summary>The nose's rocking after the slam (decay s, Hz), the slide (m) over (s) and the slew with it (deg).</summary>
        public static float CrashPitchTau = 0.35f, CrashPitchHz = 2.3f, CrashSkidM = 1.15f, CrashSkidSec = 1.0f, CrashSlewDeg = 5f;
        /// <summary>The wobble on the way down (deg, Hz, from / to s before the contact) and Alan's call (s before the contact).</summary>
        public static float CrashWobbleDeg = 4f, CrashWobbleHz = 2.4f, CrashWobbleFrom = 1.3f, CrashWobbleTo = 0.5f, CrashHotLeadSec = 1.45f;   // K408: 1.0 -> 1.45 s, the line before the engines cut (View/OzPlayableShuttle.CrashDrop.cs)
        /// <summary>The scrape mark's life (s) and width (m); robots within this many metres of the hull flinch.</summary>
        public static float CrashScrapeLife = 25f, CrashScrapeWidth = 1.1f, CrashFlinchM = 6f;
        const float CrashGrav = 9.81f, HullRadiusM = 3.3f;

        // ------------------------------------------------------------------ read by the Play checks (the last crash)
        /// <summary>The crash is being drawn now.</summary>
        public bool CrashOn => crashOn;
        /// <summary>The last crash: the low side's rebound (m), when the other side came down (s), the mean tilt at rest (deg), the high
        /// side's lowest pad at rest (m), how long it rested at 13 degrees or more (s), how long before the contact "Coming in hot!" was asked
        /// (s; -1 = not asked), the scrape mark's length (m), the robots that flinched, the side (+1 / -1).</summary>
        public float CrashHopSeen { get; private set; }
        public float CrashHighAtSeen { get; private set; } = -1f;
        /// <summary>run 27D: the crash clock of the frame before the other side came down (the moment lies between the two frames).</summary>
        public float CrashHighPrevT { get; private set; } = -1f;
        float crashPrevT, crashRestFrom = -1f;
        public float CrashRestTiltSeen { get; private set; }
        public float CrashHighPadUpSeen { get; private set; }
        public float CrashRestHeldSec { get; private set; }
        public float CrashHotLead { get; private set; } = -1f;
        public float CrashScrapeLen { get; private set; }
        public int CrashFlinched { get; private set; }
        public float CrashSide => crashSide;
        /// <summary>The rules' seconds since the contact of the crash being drawn, and its roll now (deg).</summary>
        public float CrashT => crashClock;
        public float CrashRollNow { get; private set; }
        /// <summary>Every crash landing drawn, every "Coming in hot!" asked by a crash, and every landing line asked on a crash (0).</summary>
        public static int CrashesDrawn, CrashHotCalls, CrashLandedLines;

        bool crashOn, crashLevelled, hotSaid; float crashSide = 1f, crashLs, crashLe, crashClock, crashH0, crashStrutNow, crashPuffT, crashSmokeT, crashRestSum; int crashEv, crashRestN;
        Vector3 crashDir = Vector3.forward, crashRoot0; float crashYaw0;
        static readonly float[] padH4 = new float[4];

        // ------------------------------------------------------------------ the way down
        /// <summary>The rough descent (tau = the rules' seconds left to the contact; hRule = the rules' height over the base): the wobble,
        /// Alan's call and the fast last half second. Returns the height over the base; roll / pitch (deg) are added to the pose.</summary>
        float CrashDescent(float tau, float hRule, out float rollDeg, out float pitchDeg)
        {
            rollDeg = 0f; pitchDeg = 0f;
            if (!hotSaid && tau <= CrashHotLeadSec)
            {   // "Coming in hot!": a crash is called before it hits, never with the landing line
                hotSaid = true; CrashHotLead = -1f;                                                   // K407: the landing's one voice is picked now (View/OzPlayableShuttle.LandingVoice.cs)
                if (CrashHotIsTheVoice()) { CrashHotLead = tau; CrashHotCalls++; Voice("vo_shuttle_hot"); LastAlanId = "vo_shuttle_hot"; LastAlanAt = Time.unscaledTime; }
            }
            if (CrashDropOn) return CrashDropDescent(tau, hRule, out rollDeg, out pitchDeg);             // K408: the hang, the engines cut, the accelerating drop (View/OzPlayableShuttle.CrashDrop.cs)
            return CrashDescentPose(tau, hRule, roughSide >= 0f ? 1f : -1f, OzMotion.Reduced, out rollDeg, out pitchDeg);
        }

        /// <summary>The rough descent's pose alone (plain numbers: the twin's descent()): height over the base, roll / pitch (deg).</summary>
        static float CrashDescentPose(float tau, float hRule, float side, bool calm, out float rollDeg, out float pitchDeg)
        {
            rollDeg = 0f; pitchDeg = 0f;
            if (tau < CrashWobbleFrom && tau > CrashWobbleTo)
            {
                float u = CrashWobbleFrom - tau, env = Mathf.Sin(Mathf.PI * u / (CrashWobbleFrom - CrashWobbleTo)) * (calm ? 0.5f : 1f);
                rollDeg += CrashWobbleDeg * env * Mathf.Sin(2f * Mathf.PI * CrashWobbleHz * u);
                pitchDeg += 0.4f * CrashWobbleDeg * env * Mathf.Sin(2f * Mathf.PI * 1.9f * u + 1.1f);
            }
            if (tau > CrashFastSec) return hRule;
            float s = Mathf.Clamp01(tau / CrashFastSec), lean = Mathf.SmoothStep(0f, 1f, 1f - s);
            rollDeg += -CrashLeanDeg * side * lean; pitchDeg += CrashPitchDeg * lean;
            float touch = TouchLift(rollDeg * Mathf.Deg2Rad, pitchDeg * Mathf.Deg2Rad);
            float l0 = CrashFastSec / (float)ZWorld.ShuttleDescendSec, cruise = (float)ZWorld.ShuttleCruiseM;
            float h0 = cruise * Mathf.SmoothStep(0f, 1f, l0), v0 = cruise * 6f * l0 * (1f - l0) / (float)ZWorld.ShuttleDescendSec;   // the rules' height and sink speed there
            // Hermite on the time left: (CrashFastSec, h0, sinking v0) -> (0, touch, sinking CrashTouchMps)
            float h00 = 2 * s * s * s - 3 * s * s + 1, h10 = s * s * s - 2 * s * s + s, h01 = -2 * s * s * s + 3 * s * s, h11 = s * s * s - s * s;
            return h00 * touch + h10 * CrashFastSec * CrashTouchMps + h01 * h0 + h11 * CrashFastSec * v0;
        }

        /// <summary>A pad sole's height over the ground fit (root space pad i, the root's height y, roll phi, pitch th in rad, its strut c):
        /// Unity's Euler(pitch, 0, roll) = roll first, then pitch.</summary>
        static float PadHeight(int i, float y, float phi, float th, float c)
        {
            var p = PadSoles[i];
            return y + (p.x * Mathf.Sin(phi) + c * Mathf.Cos(phi)) * Mathf.Cos(th) - p.z * Mathf.Sin(th);
        }

        /// <summary>How high the root must be for the lowest pad to just touch (struts out).</summary>
        static float TouchLift(float phi, float th)
        {
            float lo = float.MinValue;
            for (int i = 0; i < 4; i++) lo = Mathf.Max(lo, -PadHeight(i, 0f, phi, th, 0f));
            return lo;
        }

        /// <summary>The root's height and roll (rad) that stand the lowest low-side pad at hL and the lowest high-side pad at hH.</summary>
        static void CrashSolve(float side, float hL, float hH, float th, float strut, out float y, out float phi)
        {
            phi = -side * 10f * Mathf.Deg2Rad; y = 0f;
            float ct = Mathf.Cos(th), st = Mathf.Sin(th);
            for (int it = 0; it < 6; it++)
            {
                float sp = Mathf.Sin(phi), cp = Mathf.Cos(phi);
                int l = -1, h = -1; float lv = float.MaxValue, hv = float.MaxValue;
                for (int i = 0; i < 4; i++)
                {
                    float v = PadSoles[i].x * sp * ct - PadSoles[i].z * st;
                    if (PadSoles[i].x * side > 0f) { if (v < lv) { lv = v; l = i; } }
                    else if (v < hv) { hv = v; h = i; }
                }
                float xl = PadSoles[l].x, zl = PadSoles[l].z, xh = PadSoles[h].x, zh = PadSoles[h].z;
                float a = ((hL - strut * cp * ct + zl * st) - (hH + zh * st)) / ((xl - xh) * ct);
                a = Mathf.Clamp(a, -0.6f, 0.6f);
                phi = Mathf.Asin(a);
                y = hH + zh * st - xh * a * ct;
            }
        }

        /// <summary>The pose t s after the contact (the rules' clock): the root's height over the ground fit, roll and pitch (deg), the
        /// low side's strut stroke (m), the slide (m along the flight) and the slew (deg).</summary>
        void CrashPose(float t, out float y, out float rollDeg, out float pitchDeg, out float strut, out float skid, out float slew)
        {
            float side = crashSide, v0 = Mathf.Sqrt(2f * CrashGrav * CrashHopM), tL2 = 2f * v0 / CrashGrav;
            float hL = t < tL2 ? Mathf.Max(0f, v0 * t - 0.5f * CrashGrav * t * t) : 0f;
            float hH;
            if (t <= CrashHighAt)
            {
                float aH = 2f * (crashH0 - CrashHighSinkMps * CrashHighAt) / (CrashHighAt * CrashHighAt);
                hH = Mathf.Max(0f, crashH0 - CrashHighSinkMps * t - 0.5f * aH * t * t);
            }
            else
            {
                float vb = Mathf.Sqrt(2f * CrashGrav * CrashHighHopM), tau = t - CrashHighAt;
                hH = tau < 2f * vb / CrashGrav ? Mathf.Max(0f, vb * tau - 0.5f * CrashGrav * tau * tau) : 0f;
                if (t > tL2)
                {   // the slump onto the broken side: the other side lifts (a damped swing, one small overshoot)
                    float u = t - tL2, zw = CrashSlumpZeta * CrashSlumpOmega, wd = CrashSlumpOmega * Mathf.Sqrt(1f - CrashSlumpZeta * CrashSlumpZeta);
                    hH += CrashRestHighM * (1f - Mathf.Exp(-zw * u) * (Mathf.Cos(wd * u) + zw / wd * Mathf.Sin(wd * u)));
                }
            }
            strut = t > tL2 ? CrashStrutM * Mathf.SmoothStep(0f, 1f, (t - tL2) / CrashStrutSec) : 0f;
            pitchDeg = CrashPitchDeg * Mathf.Exp(-t / CrashPitchTau) * Mathf.Cos(2f * Mathf.PI * CrashPitchHz * t);
            float sk = t < CrashSkidSec ? 1f - (1f - t / CrashSkidSec) * (1f - t / CrashSkidSec) : 1f;
            float k = t >= crashLs ? Mathf.SmoothStep(0f, 1f, (t - crashLs) / Mathf.Max(0.1f, crashLe - crashLs)) : 0f;
            hH *= 1f - k; strut *= 1f - k; pitchDeg *= 1f - k; sk *= 1f - k; hL *= 1f - k;
            CrashSolve(side, hL, hH, pitchDeg * Mathf.Deg2Rad, strut, out y, out float phi);
            rollDeg = phi * Mathf.Rad2Deg;
            if (t >= crashLe) { y = 0f; rollDeg = 0f; pitchDeg = 0f; strut = 0f; sk = 0f; }
            skid = CrashSkidM * sk; slew = CrashSlewDeg * side * sk;
        }

        // ------------------------------------------------------------------ the touch and every frame of it
        /// <summary>Landed(): a crash landing begins (flightDir = where it flew; the rules' levelling start / end, s after the contact).</summary>
        void CrashBegin(Vector3 flightDir, float levelStartSec, float levelEndSec)
        {
            crashOn = true; crashLevelled = false; crashEv = 0; crashClock = 0f; crashPuffT = 0f; crashSmokeT = 0f; crashRestSum = 0f; crashRestN = 0;
            crashSide = roughSide >= 0f ? 1f : -1f;
            crashLs = levelStartSec > 0.2f ? levelStartSec : OzShuttleTouchdown.CrashSec + OzShuttleTouchdown.RestSec;
            crashLe = levelEndSec > crashLs ? levelEndSec : crashLs + OzShuttleTouchdown.LevelSec;
            crashDir = new Vector3(flightDir.x, 0f, flightDir.z); crashDir = crashDir.sqrMagnitude > 1e-4f ? crashDir.normalized : transform.forward;
            crashRoot0 = transform.position; crashYaw0 = yaw;
            float phi0 = -CrashLeanDeg * crashSide * Mathf.Deg2Rad, th0 = CrashPitchDeg * Mathf.Deg2Rad, y0 = TouchLift(phi0, th0);
            crashH0 = float.MaxValue;
            for (int i = 0; i < 4; i++) if (PadSoles[i].x * crashSide <= 0f) crashH0 = Mathf.Min(crashH0, PadHeight(i, y0, phi0, th0, 0f));
            CrashHopSeen = 0f; CrashHighAtSeen = -1f; CrashRestTiltSeen = 0f; CrashHighPadUpSeen = 0f; CrashRestHeldSec = 0f; CrashScrapeLen = 0f; CrashFlinched = 0;
            CrashHighPrevT = -1f; crashRestFrom = -1f; crashPrevT = 0f;
            if (!hotSaid) CrashHotLead = -1f;
            CrashesDrawn++;
            // the touchdown stays the state holder: its Active (the landed ring waits, the older proof), its numbers and its log
            OzShuttleTouchdown.LeanDeg = CrashLeanDeg; OzShuttleTouchdown.PitchDeg = CrashPitchDeg; OzShuttleTouchdown.TouchSpeed = CrashSlamMps;
            OzShuttleTouchdown.StrutDrop = CrashStrutM; OzShuttleTouchdown.Skid = CrashSkidM; OzShuttleTouchdown.SkidSec = CrashSkidSec; OzShuttleTouchdown.SlewDeg = CrashSlewDeg;
            touch.Arm(crashSide); touch.Begin(crashDir, crashLs, crashLe);
            OzShuttleTouchdown.Log.AppendLine($"  drawn on the rules' clock (a scripted pose): wobble from {CrashWobbleFrom:F1} s out, \"Coming in hot!\" {(CrashHotLead >= 0f ? CrashHotLead.ToString("F2") + " s before the contact" : "not asked (the descent was not seen)")}; the low side rebounds {CrashHopM:F2} m, the other side down at {CrashHighAt:F2} s, at rest the high pads {CrashRestHighM:F2} m up");
        }

        /// <summary>Pose(): one frame of the crash (the rules' clock), its pose added to the ground fit; its sounds and effects on their moments.</summary>
        void CrashPoseStep(float dt, ref Vector3 pos, ref float rollAdd, ref float pitchAdd, ref float yawAdd)
        {
            float t = Obj.ShuttleTouchdownAt >= 0 ? (float)(game.World.Time - Obj.ShuttleTouchdownAt) : crashClock + dt;
            if (t < 0f) t = 0f;
            crashPrevT = crashClock; crashClock = t;
            CrashPose(t, out float y, out float rollDeg, out float pitchDeg, out crashStrutNow, out float skid, out float slew);
            // the crash owns the whole roll and pitch while it is drawn (the flight's eased lean and nose are about 0 on the ground)
            pos.y += y; pos += crashDir * skid;
            rollAdd += rollDeg - bank; pitchAdd += pitchDeg - pitch; yawAdd += slew;
            CrashRollNow = rollDeg;
            // the counts (the touchdown's statics too: the older proof reads them)
            float phi = rollDeg * Mathf.Deg2Rad, th = pitchDeg * Mathf.Deg2Rad, lowSide = float.MaxValue, highSide = float.MaxValue, lowest = float.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                bool low = PadSoles[i].x * crashSide > 0f;
                padH4[i] = PadHeight(i, y, phi, th, low ? crashStrutNow : 0f); lowest = Mathf.Min(lowest, padH4[i]);
                if (low) lowSide = Mathf.Min(lowSide, padH4[i]); else highSide = Mathf.Min(highSide, padH4[i]);
            }
            OzShuttleTouchdown.WorstPadIn = Mathf.Max(OzShuttleTouchdown.WorstPadIn, Mathf.Min(0.03f, -lowest));
            if (CrashHighAtSeen < 0f) { CrashHopSeen = Mathf.Max(CrashHopSeen, lowSide); OzShuttleTouchdown.BounceM = CrashHopSeen; }
            if (Mathf.Abs(rollDeg) >= 6f) OzShuttleTouchdown.TiltHeld += dt;
            if (Mathf.Abs(rollDeg) >= 13f && t < crashLs) { if (crashRestFrom < 0f) crashRestFrom = t; CrashRestHeldSec = t - crashRestFrom; }   // run 27D: on the rules' clock (the first to the last frame at 13 deg or more)
            if (t >= OzShuttleTouchdown.CrashSec - 0.15f && t < crashLs) { crashRestSum += Mathf.Abs(rollDeg); crashRestN++; CrashRestTiltSeen = crashRestSum / crashRestN; OzShuttleTouchdown.RestTiltDeg = CrashRestTiltSeen; CrashHighPadUpSeen = highSide; }
            OzShuttleTouchdown.PitchPeak = Mathf.Max(OzShuttleTouchdown.PitchPeak, Mathf.Abs(pitchDeg));
            OzShuttleTouchdown.SlideM = Mathf.Max(OzShuttleTouchdown.SlideM, skid);
            if (!crashLevelled && Obj.ShuttleRamp01 > 0.0) RampWhileRighting++;
            // the moments
            float tL2 = 2f * Mathf.Sqrt(2f * CrashGrav * CrashHopM) / CrashGrav;
            if ((crashEv & 1) == 0) { crashEv |= 1; CrashSlam(); }
            if ((crashEv & 2) == 0 && t >= CrashHighAt) { crashEv |= 2; CrashHighSide(t); }
            if ((crashEv & 4) == 0 && t >= tL2) { crashEv |= 4; CrashSlump(t); }
            if (t > tL2 && t < CrashSkidSec - 0.05f && (crashPuffT -= dt) <= 0f) { crashPuffT = 0.09f; CrashSkidDust(); }
            if (t > tL2 + 0.1f && t < crashLs + 0.5f && (crashSmokeT -= dt) <= 0f) { crashSmokeT = 0.28f; CrashWisp(); }
            if ((crashEv & 8) == 0 && t >= crashLs) { crashEv |= 8; CrashLevelStart(t); }
            if (!crashLevelled && t >= crashLe)
            {
                if (!SuspBegin(SpringCrashK)) SpringBegin(SpringCrashK);                                 // K256g3: it drops level onto its four legs on the visible suspension (OzPlayableShuttle.Suspension.cs); K256g's light spring when the rig is not fitted
                crashLevelled = true; OzShuttleTouchdown.LevelTook = t - crashLs; OzShuttleTouchdown.LevelOff = Mathf.Abs(t - crashLe);
                OzShuttleTouchdown.Log.AppendLine($"  levelled at {t:F2} s ({t - crashLs:F2} s after the levelling began, {Mathf.Abs(t - crashLe):F3} s off the rules' moment)");
            }
            if (t >= crashLe + 0.4f || (t >= crashLe && Obj.ShuttleState != ZShuttleState.Landed)) CrashEnd(t);   // done (or a take-off the moment it may)
        }

        void CrashEnd(float t)
        {
            OzShuttleTouchdown.Log.AppendLine($"  done at {t:F2} s (pads at most {OzShuttleTouchdown.WorstPadIn * 100f:F1} cm in the ground, pitch peak {OzShuttleTouchdown.PitchPeak:F1} deg, bounce {CrashHopSeen:F2} m, the other side down at {CrashHighAtSeen:F2} s, rest tilt {CrashRestTiltSeen:F1} deg held {CrashRestHeldSec:F2} s at 13 deg or more, the high pads {CrashHighPadUpSeen:F2} m up, slide {OzShuttleTouchdown.SlideM:F2} m, robots flinched {CrashFlinched})");
            crashOn = false; crashStrutNow = 0f;
            if (touch.Active) touch.Stop();
        }

        /// <summary>OnBrokenUp: a crash being drawn stops with the ship.</summary>
        void CrashStop() { crashOn = false; crashStrutNow = 0f; }

        /// <summary>The strut stroke of pad i now (m; the low side's legs slide up into their pods), 0 without a crash.</summary>
        float CrashStrut(int i) => crashOn && PadSoles[i].x * crashSide > 0f ? crashStrutNow : 0f;

        Vector3 PadWorld(int i, float up = 0.05f) => transform.TransformPoint(new Vector3(PadSoles[i].x, up, PadSoles[i].z));

        // ------------------------------------------------------------------ the moments' sounds and effects
        void CrashContact(int i, string side, float speed)
        {
            OzShuttleTouchdown.Contacts++;
            OzShuttleTouchdown.Log.AppendLine($"  pad contact {i} ({side} side) at {crashClock:F3} s, {speed:F2} m/s");
            var at = PadWorld(i);
            OpenZHitSparks.Burst(at + Vector3.up * 0.2f, Vector3.down, FxWeapon.MachineGun, FxSurface.Metal, 1.4f + 0.2f * speed, false);
            wash?.Ring(at, 0.6f, 10, Mathf.Clamp(0.25f * speed, 0.6f, 1.4f));
        }

        /// <summary>0 s: the low side's pads slam down.</summary>
        void CrashSlam()
        {
            OzShuttleTouchdown.FirstSlam = CrashSlamMps;                                              // K408: the drawn hit's speed (View/OzPlayableShuttle.CrashDrop.cs)
            int a = -1, b = -1;
            for (int i = 0; i < 4; i++) if (PadSoles[i].x * crashSide > 0f) { if (a < 0) a = i; else b = i; }
            CrashContact(a, "low", CrashSlamMps); CrashContact(b, "low", CrashSlamMps * 0.9f);
            CrashDropSlam();                                                                         // K408: the hard hit's dust burst and its log line
            var at = PadWorld(a); var au = CombatAudio.I;
            if (au)
            {
                au.PlayPitched("wreck_crash", at, 1f, 0f, Obj.Id, 0.85f, LegacyMatch.BankTankImpact);
                au.Play("turret", at, 0.8f, 0f, Obj.Id, OzSoundV3.BankMetalHeavy);
            }
            var outward = transform.right * crashSide; outward.y = 0f;
            ScorchDecals.Add(PadWorld(a, 0f), 0.6f, ScorchKind.Contact); ScorchDecals.Add(PadWorld(b, 0f), 0.55f, ScorchKind.Contact);   // the two gouges where they hit
            RobotPopDeath.Chips(at + Vector3.up * 0.4f, outward, Color.Lerp(TeamColors.Of(Obj.Owner), new Color(0.42f, 0.44f, 0.47f), 0.5f), 4, 0.8f,
                                RobotDeathRule.Hash(Obj.Id, Obj.ShuttleLandings, 91u), false);                                                // bits of plating torn off (they clatter where they land)
            OzCrashLandingShake.Slam(transform.position + Vector3.up * 2f, at);                       // K402: on screen, the game's explosion shake (by the camera's distance and the crash's distance from the screen centre; the shake option and calm mode), the camera's own kick as its second part (View/OzCrashLandingShake.cs)
            CrashFlinch(at);
            OzShuttleTouchdown.Log.AppendLine($"  wreck_crash + metal crash, sparks and dust at the two low pads, two gouges, 4 bits of plating, the camera kick; robots flinched {CrashFlinched}");
        }

        /// <summary>The robots near the hull flinch (a stagger close in), away from the slam; view only, seeded by their ids.</summary>
        void CrashFlinch(Vector3 at)
        {
            if (game == null) return;
            var c = transform.position;
            foreach (var kv in game.Views)
            {
                var v = kv.Value;
                if (!v || v.Obj == null || v.Obj.Kind != ZKind.Robot || v.Obj.Destroyed || !v.isActiveAndEnabled) continue;
                var p = v.transform.position; float d = new Vector2(p.x - c.x, p.z - c.z).magnitude - HullRadiusM;
                if (d > CrashFlinchM) continue;
                v.React(at, d <= 3f ? 2 : 1, RobotDeathRule.Hash(v.Obj.Id, Obj.Id, 77u), 0.05f + 0.015f * Mathf.Max(0f, d));
                CrashFlinched++;
            }
        }

        /// <summary>0.33 s: the other side bangs down: a clank, sparks and a second puff.</summary>
        void CrashHighSide(float t)
        {
            CrashHighAtSeen = t; CrashHighPrevT = crashPrevT;
            int a = -1, b = -1;
            for (int i = 0; i < 4; i++) if (PadSoles[i].x * crashSide <= 0f) { if (a < 0) a = i; else b = i; }
            CrashContact(a, "high", 4.9f); CrashContact(b, "high", 4.5f);
            var at = PadWorld(a);
            AudioDirector.Fire("dst_debris_metal", at, Obj.Id, 0.9f);
            var au = CombatAudio.I; if (au) au.Play("turret", at, 0.55f, 0f, Obj.Id + 7, OzSoundV3.BankMetalHeavy);
            wash?.Burst(0.8f);
            OzCrashLandingShake.Bounce(transform.position + Vector3.up * 2f);                         // K402: the bounce, a smaller kick (View/OzCrashLandingShake.cs)
            OzShuttleTouchdown.Log.AppendLine("  dst_debris_metal + metal crash at the second side, sparks, a second puff");
        }

        /// <summary>0.51 s: the first side comes down again, its struts give, it slumps onto them and slides: the scrape.</summary>
        void CrashSlump(float t)
        {
            int a = -1, b = -1;
            for (int i = 0; i < 4; i++) if (PadSoles[i].x * crashSide > 0f) { if (a < 0) a = i; else b = i; }
            CrashContact(a, "low", 2.5f); CrashContact(b, "low", 2.3f);
            var at = PadWorld(a);
            AudioDirector.Fire("dst_debris_metal", at, Obj.Id + 5, 0.7f, 0.78f);                                                    // the struts give: a deep clank
            var sc = transform.TransformPoint(new Vector3(crashSide * 2f, 0.1f, -0.9f));
            if (AudioDirector.Has("shuttle_skid_scrape")) AudioDirector.Fire("shuttle_skid_scrape", sc, Obj.Id, 1f);
            else AudioDirector.Fire("dst_debris_metal", sc, Obj.Id + 3, 0.8f, 0.7f);
            OzShuttleTouchdown.Scrapes++;
            CrashScrapeLen = ScrapeMark.Lay(this);
            OzShuttleTouchdown.Log.AppendLine($"  the struts give ({CrashStrutM:F2} m), it slumps and slides: the scrape's sound at {t:F2} s, a scrape mark {CrashScrapeLen:F1} m long");
        }

        /// <summary>While it slides: dust thrown out from under the scraping pads (low streaks).</summary>
        void CrashSkidDust()
        {
            if (wash == null) return;
            var outward = transform.right * crashSide; outward.y = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (PadSoles[i].x * crashSide <= 0f) continue;
                OzShuttleTouchdown.SkidPuffs += wash.Streak(PadWorld(i), outward + crashDir * 0.6f, 0.8f);
            }
        }

        /// <summary>While it rests crooked: a thin smoke wisp from the low pod's nozzle.</summary>
        void CrashWisp()
        {
            var fx = OpenZFxPlayer.I; if (!fx || !fx.isActiveAndEnabled) return;
            if (!PuffOnShip(new Vector3(crashSide * 2.47f, 2.0f, -1.6f), 0.45f, false))               // K413: from the drawn low pod, right after the pose
                fx.WreckSmoke(transform.TransformPoint(new Vector3(crashSide * 2.47f, 2.0f, -1.6f)), 0.45f, false);
        }

        /// <summary>The rules' levelling starts: the hydraulic rights it.</summary>
        void CrashLevelStart(float t)
        {
            var at = transform.position + Vector3.up * 1.2f;
            if (AudioDirector.Has("shuttle_level_hydraulic")) AudioDirector.Fire("shuttle_level_hydraulic", at, Obj.Id, 1f);
            if (AudioDirector.Has("rob_servo")) AudioDirector.Fire("rob_servo", at, Obj.Id, 0.6f, 0.9f);
            OzShuttleTouchdown.Log.AppendLine($"  levelling starts at {t:F2} s (shuttle_level_hydraulic + rob_servo)");
        }

        // ------------------------------------------------------------------ the scrape mark
        /// <summary>The dark scrape left by the crash's low side: one soft dark band on the drawn ground from where its back pad hit to
        /// where its front pad comes to rest (5-6 m), streaky across, faded in as it forms, held, then faded out over the last 40 % of its
        /// life; the shadow's own shader (OpenZ/SoftGroundBlob), its grid laid on the drawn ground, alpha 0 over water and off a step.</summary>
        sealed class ScrapeMark
        {
            static readonly List<ScrapeMark> live = new List<ScrapeMark>();
            static int tickedFrame = -1;
            static readonly int TintId = Shader.PropertyToID("_Tint"), StrengthId = Shader.PropertyToID("_Strength"), CoreId = Shader.PropertyToID("_Core"), ScaleId = Shader.PropertyToID("_Scale");
            public static int Laid;
            GameObject go; Mesh mesh; Material mat; MeshRenderer mr; float born, life; readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            const int NL = 26, NW = 7;
            public static Color Tint = new Color(0.07f, 0.055f, 0.04f);
            public static float Strength = 0.62f, Core = 0.5f, FadeInSec = 0.45f;

            static float GroundAt(float x, float z)
            {
                var tm = TrackMarks.I;
                if (tm && tm.Surface != null && tm.Surface.RenderY(x, z, out float y)) return y;
                var g = ZGame.I; return g ? g.GroundY(x, z) : 0f;
            }

            /// <summary>Lays the mark of this crash; returns its length (m), 0 = not laid.</summary>
            public static float Lay(OzPlayableShuttle s)
            {
                var m0 = Resources.Load<Material>("OpenZ/FX/M_OzSoftGroundBlob");
                var sh = m0 && m0.shader ? m0.shader : Shader.Find("OpenZ/SoftGroundBlob");
                if (!sh || !sh.isSupported) return 0f;
                float side = s.crashSide;
                var rot0 = Quaternion.Euler(0f, s.crashYaw0, 0f);
                int back = -1, front = -1;
                for (int i = 0; i < 4; i++) if (PadSoles[i].x * side > 0f) { if (PadSoles[i].z < 0f) back = i; else front = i; }
                var fw = rot0 * Vector3.forward;
                var a = s.crashRoot0 + rot0 * new Vector3(PadSoles[back].x, 0f, PadSoles[back].z) - fw * 0.4f;
                var restRot = Quaternion.Euler(0f, s.crashYaw0 + CrashSlewDeg * side, 0f);
                var b = s.crashRoot0 + s.crashDir * CrashSkidM + restRot * new Vector3(PadSoles[front].x, 0f, PadSoles[front].z) + fw * 0.4f;
                a.y = b.y = 0f;
                var along = b - a; float len = along.magnitude; if (len < 1f) return 0f;
                along /= len; var across = new Vector3(along.z, 0f, -along.x); var mid = (a + b) * 0.5f;
                var m = new ScrapeMark { life = CrashScrapeLife, born = Time.time };
                m.mat = new Material(sh) { name = "OZ_ShuttleScrape (runtime)" };
                m.go = new GameObject("OZ_FxShuttleScrape");
                m.go.AddComponent<MeshFilter>().sharedMesh = m.mesh = new Mesh { name = "OZ_ShuttleScrapeGrid" };
                m.mr = m.go.AddComponent<MeshRenderer>(); m.mr.sharedMaterial = m.mat;
                m.mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; m.mr.receiveShadows = false;
                var verts = new Vector3[NL * NW]; var uvs = new Vector2[NL * NW]; var cols = new Color[NL * NW]; var tri = new int[(NL - 1) * (NW - 1) * 6];
                var fx = OpenZFxPlayer.I; float cy = GroundAt(mid.x, mid.z), hw = CrashScrapeWidth * 0.5f, hl = len * 0.5f;
                uint seed = RobotDeathRule.Hash(s.Obj.Id, s.Obj.ShuttleLandings, 53u);
                for (int j = 0; j < NL; j++)
                    for (int i = 0; i < NW; i++)
                    {
                        int k = j * NW + i; float u = i / (NW - 1f) * 2f - 1f, v = j / (NL - 1f) * 2f - 1f;
                        var p = mid + across * (u * hw) + along * (v * hl);
                        float y = GroundAt(p.x, p.z), alpha = 1f;
                        if (fx && fx.isActiveAndEnabled && fx.WetAt(new Vector3(p.x, y, p.z))) alpha = 0f;           // never on the water
                        if (Mathf.Abs(y - cy) > 0.6f) alpha = 0f;                                                  // nor off a step
                        float streak = 0.55f + 0.45f * Mathf.Pow(Mathf.Cos((u * 0.5f + 0.5f) * Mathf.PI * 2.5f), 2f);
                        float grain = 0.8f + 0.2f * (((seed >> (j % 24)) & 1u) == 1u ? 1f : 0f);
                        verts[k] = new Vector3(p.x, y + 0.035f, p.z); uvs[k] = new Vector2(u, v); cols[k] = new Color(1f, 1f, 1f, alpha * streak * grain);
                    }
                int t3 = 0;
                for (int j = 0; j < NL - 1; j++) for (int i = 0; i < NW - 1; i++) { int q = j * NW + i; tri[t3++] = q; tri[t3++] = q + NW; tri[t3++] = q + 1; tri[t3++] = q + 1; tri[t3++] = q + NW; tri[t3++] = q + NW + 1; }
                m.mesh.vertices = verts; m.mesh.uv = uvs; m.mesh.colors = cols; m.mesh.triangles = tri; m.mesh.RecalculateBounds();
                m.Apply(0f);
                Object.Destroy(m.go, CrashScrapeLife + 2f);                                                       // never left behind
                live.Add(m); Laid++;
                return len;
            }

            void Apply(float k)
            {
                mpb.SetColor(TintId, Tint); mpb.SetFloat(StrengthId, Strength * k); mpb.SetFloat(CoreId, Core); mpb.SetFloat(ScaleId, 1f);
                mr.SetPropertyBlock(mpb);
            }

            /// <summary>Once a frame (OzPlayableShuttle.Update, any ship, even one in pieces): the fades.</summary>
            public static void Tick()
            {
                if (tickedFrame == Time.frameCount) return;
                tickedFrame = Time.frameCount;
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    var m = live[i]; float age = Time.time - m.born;
                    if (!m.go || age >= m.life) { Free(m); live.RemoveAt(i); continue; }
                    float k = Mathf.Clamp01(age / FadeInSec) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.6f * m.life, m.life, age)));
                    if (m.mr) m.Apply(k);
                }
            }

            static void Free(ScrapeMark m) { if (m.go) Object.Destroy(m.go); if (m.mesh) Object.Destroy(m.mesh); if (m.mat) Object.Destroy(m.mat); }

            /// <summary>Proof: marks alive now.</summary>
            public static int Alive => live.Count;
        }

        /// <summary>Proof: scrape marks laid and alive now.</summary>
        public static int ScrapesLaid => ScrapeMark.Laid;
        public static int ScrapesAlive => ScrapeMark.Alive;
        static void ScrapeTick() => ScrapeMark.Tick();
    }
}
