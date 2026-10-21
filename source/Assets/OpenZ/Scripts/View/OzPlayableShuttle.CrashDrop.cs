using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K408: the crash landing's way down as a real crash (View only; the rules' clock and the rules' contact moment are unchanged, so
    /// the slam, the bounce, the tilted rest, the self-righting and EXIT keep their moments). The old K256e way down drifted down the
    /// rules' eased curve and only sank a little faster in its last half second (5 m/s at the contact): it read as a soft landing.
    /// Now, on the rules' clock (tau = the rules' seconds left to the contact; the twin tools/k408_twin.py holds every number):
    /// <list type="bullet">
    /// <item>THE HANG (2.0 -> 0.8 s out): it sinks from its cruise height to <see cref="CrashDropStartM"/> m over the base, slowing the
    /// rules' way down at first and wobbling from <see cref="CrashDropWobbleFrom"/> s out; "Coming in hot!" is asked
    /// <see cref="CrashHotLeadSec"/> s before the contact.</item>
    /// <item>THE CUT (0.8 s out): the engines cough: the engine shutdown sputter, a falling whoosh and a puff of smoke from each engine pod.</item>
    /// <item>THE DROP (the last 0.8 s): it falls with a constant acceleration from <see cref="CrashDropStartMps"/> m/s to about 12.5 m/s
    /// at the contact (the last 6 m in about 0.7 s), leaning onto the side the rules name (14 degrees, 3.5 degrees nose down).</item>
    /// <item>THE HIT: the impact speed drawn is what the slam's sparks, dust and log use (it was a fixed 5 m/s), and a dust burst goes
    /// up with it. Everything after the contact is K256e's as before.</item>
    /// </list>
    /// Off (<see cref="CrashDropOn"/> = false, a test switch) = K256e's way down exactly as before. A normal landing never comes here.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        // ------------------------------------------------------------------ the numbers (the twin: tools/k408_twin.py)
        /// <summary>The faster crash on (a test switch; off = K256e's softer way down).</summary>
        public static bool CrashDropOn = true;
        /// <summary>The drop: its time (s before the contact), its start (m over the base; at most this part of the cruise height) and its
        /// sink speed at the start (m/s); the hang's wobble starts this many s before the contact.</summary>
        public static float CrashDropSec = 0.8f, CrashDropStartM = 7f, CrashDropTopK = 0.8f, CrashDropStartMps = 3.5f, CrashDropWobbleFrom = 1.6f;

        // ------------------------------------------------------------------ read by the Play checks (the last crash)
        /// <summary>Every engine cut of a crash's drop.</summary>
        public static int CrashDropCuts, CrashDropPuffs;
        /// <summary>The last crash's way down: the drawn sink speed just before the contact (m/s, on the rules' clock), the curve's own
        /// impact speed (m/s), the rules' seconds left when it was 6 m over its touch height, when the engines cut (s before the contact;
        /// -1 = not seen), the hit's speed the slam used (m/s), the fastest drawn sink while it still hung (m/s).</summary>
        public float CrashDropImpactMps { get; private set; }
        public float CrashDropModelMps { get; private set; }
        public float CrashDropLast6mSec { get; private set; } = -1f;
        public float CrashDropCutTau { get; private set; } = -1f;
        public float CrashDropSlamSeen { get; private set; }
        public float CrashDropHangMaxMps { get; private set; }

        float crashDropH = -1f, crashDropLastTau = -1f, crashDropPrevH = -1f, crashDropPrevTau = -1f; bool crashDropCut, crashDropFresh;

        /// <summary>CrashDescent (K408 on): one frame of the crash's way down. Returns the height over the base; roll / pitch (deg).</summary>
        float CrashDropDescent(float tau, float hRule, out float rollDeg, out float pitchDeg)
        {
            float side = roughSide >= 0f ? 1f : -1f;
            if (crashDropLastTau < 0f || tau > crashDropLastTau + 0.05f)
            {   // a new descent (the time left jumps up): its own numbers
                crashDropH = -1f; crashDropCut = false; crashDropPrevH = -1f; crashDropPrevTau = -1f; crashDropFresh = false;
                CrashDropImpactMps = 0f; CrashDropLast6mSec = -1f; CrashDropCutTau = -1f; CrashDropHangMaxMps = 0f;
            }
            crashDropLastTau = tau;
            // the cruise height this descent came down from (the rules' height over its eased share; K338's own cruise step included)
            float lift = tau / (float)ZWorld.ShuttleDescendSec;
            if (lift >= 0.4f) crashDropH = hRule / Mathf.SmoothStep(0f, 1f, lift);
            else if (crashDropH <= 0f) crashDropH = lift > 0.05f ? hRule / Mathf.SmoothStep(0f, 1f, lift) : (float)ZWorld.ShuttleCruiseM;
            // the part of the next 10 ms rule tick already gone: a smooth fall between the ticks
            float ts = game ? Mathf.Max(0f, tau - game.TickAlpha * (float)ZConst.TICK) : tau;
            float h = CrashDropPose(ts, crashDropH, side, OzMotion.Reduced, out rollDeg, out pitchDeg);
            CrashDropNumbers(crashDropH, side, out _, out float tl, out _, out float v1);
            CrashDropModelMps = v1;
            if (!crashDropCut && ts <= CrashDropSec) { crashDropCut = true; CrashDropCut(ts); }
            if (crashDropPrevH >= 0f && crashDropPrevTau > ts + 1e-4f)
            {
                float v = (crashDropPrevH - h) / (crashDropPrevTau - ts);
                if (ts > CrashDropSec) CrashDropHangMaxMps = Mathf.Max(CrashDropHangMaxMps, v);
                else { CrashDropImpactMps = v; crashDropFresh = true; }
            }
            if (CrashDropLast6mSec < 0f && h - tl <= 6f) CrashDropLast6mSec = ts;
            crashDropPrevH = h; crashDropPrevTau = ts;
            return h;
        }

        /// <summary>The drop's start height, the touch height (the lowest pad at the contact's lean), its acceleration and its impact speed
        /// for cruise height H (plain numbers: the twin's drop_numbers()).</summary>
        static void CrashDropNumbers(float H, float side, out float hS, out float touch, out float accel, out float v1)
        {
            touch = TouchLift(-CrashLeanDeg * side * Mathf.Deg2Rad, CrashPitchDeg * Mathf.Deg2Rad);
            hS = Mathf.Min(CrashDropStartM, CrashDropTopK * Mathf.Max(1f, H));
            float T = Mathf.Max(0.1f, CrashDropSec);
            accel = Mathf.Max(0f, 2f * (hS - touch - CrashDropStartMps * T) / (T * T));
            v1 = CrashDropStartMps + accel * T;
        }

        /// <summary>The crash's way down alone (plain numbers: the twin's new_pose()): height over the base, roll / pitch (deg).</summary>
        static float CrashDropPose(float tau, float H, float side, bool calm, out float rollDeg, out float pitchDeg)
        {
            rollDeg = 0f; pitchDeg = 0f;
            CrashDropNumbers(H, side, out float hS, out float touch, out float accel, out _);
            float T = Mathf.Max(0.1f, CrashDropSec), D = (float)ZWorld.ShuttleDescendSec;
            if (tau > T)
            {   // the hang: Hermite from (the top, the cruise height, not sinking) to (the cut, hS, sinking CrashDropStartMps); the wobble
                if (tau < CrashDropWobbleFrom && CrashDropWobbleFrom > T)
                {
                    float u0 = CrashDropWobbleFrom - tau, env = Mathf.Sin(Mathf.PI * u0 / (CrashDropWobbleFrom - T)) * (calm ? 0.5f : 1f);
                    rollDeg += CrashWobbleDeg * env * Mathf.Sin(2f * Mathf.PI * CrashWobbleHz * u0);
                    pitchDeg += 0.4f * CrashWobbleDeg * env * Mathf.Sin(2f * Mathf.PI * 1.9f * u0 + 1.1f);
                }
                float L = Mathf.Max(0.1f, D - T), q = Mathf.Clamp01((D - tau) / L);
                float h00 = 2f * q * q * q - 3f * q * q + 1f, h01 = -2f * q * q * q + 3f * q * q, h11 = q * q * q - q * q;
                return h00 * H + h01 * hS - h11 * L * CrashDropStartMps;
            }
            // the drop: a constant acceleration onto the touch height, leaning onto the low side
            float u = T - Mathf.Max(0f, tau), lean = Mathf.SmoothStep(0f, 1f, u / T);
            rollDeg = -CrashLeanDeg * side * lean; pitchDeg = CrashPitchDeg * lean;
            return Mathf.Max(touch, hS - CrashDropStartMps * u - 0.5f * accel * u * u);
        }

        /// <summary>The engines cut (tau s before the contact): the sputter, the falling whoosh, a puff of smoke from each engine pod.</summary>
        void CrashDropCut(float tau)
        {
            CrashDropCuts++; CrashDropCutTau = tau;
            if (tau < 0.1f) return;                                                                    // the drop was not seen: no late cough
            if (CrashCutAfterPose()) return;                                                          // K413: the cough on the drawn ship, right after the pose (was 2 m over the ground under it)
            var at = transform.position + Vector3.up * 1.5f;
            if (AudioDirector.Has("veh_engine_shutdown")) AudioDirector.Fire("veh_engine_shutdown", at, Obj.Id + 11, 0.9f, 1.25f);
            if (AudioDirector.Has("shuttle_strafe_whoosh")) AudioDirector.Fire("shuttle_strafe_whoosh", at, Obj.Id + 12, 0.8f, 0.8f);
            var fx = OpenZFxPlayer.I;
            if (fx && fx.isActiveAndEnabled)
            {
                fx.WreckSmoke(transform.TransformPoint(new Vector3(2.47f, 2.0f, -1.6f)), 0.4f, false);
                fx.WreckSmoke(transform.TransformPoint(new Vector3(-2.47f, 2.0f, -1.6f)), 0.4f, false);
                CrashDropPuffs += 2;
            }
        }

        /// <summary>Pose (the hover bob's share): how much of the bob a crash's way down keeps. All of it until the wobble starts, none from
        /// the engines' cut on, eased between, so the drop is one clean fall (the bob's own 0.7 m swing would otherwise be let go inside the
        /// last 2 m and could stall or speed the drawn fall there by up to 4 m/s). 1 for every other flight and with K408 off.</summary>
        float CrashDropBobK
        {
            get
            {
                if (!CrashDropOn || !roughArmed || Obj.ShuttleState != ZShuttleState.Descend) return 1f;
                float tau = (float)Obj.ShuttleLift * (float)ZWorld.ShuttleDescendSec;
                return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CrashDropSec, Mathf.Max(CrashDropSec + 0.05f, CrashDropWobbleFrom), tau));
            }
        }

        /// <summary>The hit's speed for the slam (m/s): the drawn one just before the contact, the curve's own when the drop was not drawn;
        /// K256e's fixed one with K408 off.</summary>
        float CrashSlamMps => !CrashDropOn ? CrashTouchMps : crashDropFresh && CrashDropImpactMps > 0.5f ? CrashDropImpactMps : (CrashDropModelMps > 0.5f ? CrashDropModelMps : CrashTouchMps);

        /// <summary>CrashSlam (after the two low pads' contacts): the hard hit's dust burst and the drop's line in the crash log.</summary>
        void CrashDropSlam()
        {
            if (!CrashDropOn) return;
            CrashDropSlamSeen = CrashSlamMps;
            wash?.Burst(1.1f);
            OzShuttleTouchdown.Log.AppendLine($"  K408 the drop: the engines cut {(CrashDropCutTau >= 0f ? CrashDropCutTau.ToString("F2") + " s" : "(not seen)")} before the contact, the last 6 m in {(CrashDropLast6mSec >= 0f ? CrashDropLast6mSec.ToString("F2") + " s" : "(not seen)")}, the hit at {CrashDropSlamSeen:F1} m/s (the curve's {CrashDropModelMps:F1}); the hang sank at most {CrashDropHangMaxMps:F1} m/s; a dust burst");
            crashDropFresh = false;
        }
    }
}
