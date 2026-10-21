using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's flight look (rule DV118, view only; the twin tools/e_flight_twin.py of the kit holds the same numbers):
    /// <list type="bullet">
    /// <item>THE HOVER BOB: 0.7 m, a slow 3 s wave (0.33 Hz, 70 %) and a quicker one (0.8 Hz, 30 %), only well off the ground (full from
    /// 3 m of its pads), half with CALM SCREEN; a roll drift of 1.5 degrees a quarter of the bob's period ahead of it (it rolls as it rises
    /// and sinks) and a slow pitch drift of 1.5 degrees of its own. At the 70 m camera a 3 s hover rises and sinks 18 px or more (the
    /// round-2 bob read about 7 px. The soft shadow breathes with <see cref="OzPlayableShuttle.BobNow"/>
    /// (OzShuttleShadow), and the clearances over what stands under it carry the bob (OzShuttleSkyTop).</item>
    /// <item>THE BANK: it leans 0.28 degrees per degree a second of the rules' turn rate (a 64 deg/s turn: about 18 degrees), at most 20,
    /// eased in 0.3 s and never faster than 26 degrees a second (a 90 degree re-order: peak about 18, fastest change about 25 deg/s).</item>
    /// <item>THE NOSE: 7 degrees down at full acceleration, 7 up at full braking (5 hardly read at 70 m), level while cruising.</item>
    /// <item>THE STRAFE'S LOG: every glide of the rules' strafe (its metres out, how long out, held and back, its peak sideways speed and the
    /// time between two turns of its sideways direction) into <see cref="GlideLog"/>.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        [Tooltip("the hover bob: its slow wave (Hz) and that wave's share, the quicker wave (Hz); the roll / pitch drift (deg) and the pitch drift's own wave (Hz)")]
        public float bobHz = 0.33f, bobSlowShare = 0.7f, bobHz2 = 0.8f, driftDeg = 1.5f, driftHz = 0.26f;
        [Tooltip("the lean into turns: degrees per deg/s of the rules' turn rate, its fastest change (deg/s)")]
        public float bankPerTurn = 0.28f, bankRateMax = 26f;

        /// <summary>The bob now as a share of its full swing (-1 .. +1, + = up).</summary>
        public float BobNorm => bobM > 1e-4f ? Mathf.Clamp(BobNow / bobM, -1f, 1f) : 0f;
        /// <summary>Proof (reset by the proofs): the largest bank (deg) and its fastest change (deg per second of game time) while airborne.</summary>
        public float BankPeak { get; set; }
        public float BankRatePeak { get; set; }
        float bankLast; bool bankLastSet;

        /// <summary>The bob and the drift (bw = how much of them: 0 near the ground, 1 well off it).</summary>
        void Bob(float bw, out float rollDrift, out float pitchDrift)
        {
            float w1 = 2f * Mathf.PI * bobHz * t;
            BobNow = bobM * (bobSlowShare * Mathf.Sin(w1) + (1f - bobSlowShare) * Mathf.Sin(2f * Mathf.PI * bobHz2 * t + 1.3f)) * bw;
            rollDrift = driftDeg * Mathf.Cos(w1) * bw;                                                       // a quarter period ahead of the bob
            pitchDrift = driftDeg * Mathf.Sin(2f * Mathf.PI * driftHz * t + 2.4f) * bw;
        }

        /// <summary>After the bank's ease: its peak and fastest change (game time) while it flies.</summary>
        void BankNote(float dt)
        {
            if (dt > 1e-5f && bankLastSet && Obj != null && Obj.ShuttleAirborne)
            {
                BankRatePeak = Mathf.Max(BankRatePeak, Mathf.Abs(bank - bankLast) / dt);
                BankPeak = Mathf.Max(BankPeak, Mathf.Abs(bank));
            }
            if (dt > 1e-5f) { bankLast = bank; bankLastSet = true; }
        }

        // ------------------------------------------------------------------ the strafe's glides, logged
        /// <summary>Every glide of the rules' strafe, one line each (metres, seconds, peak speed, the gap between two turns of direction).</summary>
        public static readonly StringBuilder GlideLog = new StringBuilder();
        /// <summary>The last finished glide of this ship: metres out, seconds out, held, back, the peak sideways speed (m/s); the shortest
        /// time between two turns of its sideways direction seen (s; large = none yet); glides finished.</summary>
        public float GlideOutM { get; private set; }
        public float GlideOutSec { get; private set; }
        public float GlideHoldSec { get; private set; }
        public float GlideBackSec { get; private set; }
        public float GlidePeakMps { get; private set; }
        public float GlideFlipGapMin { get; private set; } = 999f;
        public int Glides { get; private set; }
        int glideSeen = -1, glideBackSeen = -1, glideLastDir; bool glideOn; float glideStartAt, glideOutAt, glideBackAt, glideLastDirAt, glidePeakM, glidePeakV; int glideSideNow;
        const float GlideStillMps = 0.05f;

        void GlideWatch()
        {
            if (Obj == null || game == null || game.World == null) return;
            float now = (float)game.World.Time, off = (float)Obj.ShuttleStrafePx / 8f, v = (float)Obj.ShuttleStrafeVelPx / 8f;
            if (float.IsNaN(off) || float.IsNaN(v)) return;
            if (glideSeen < 0) { glideSeen = Obj.ShuttleStrafes; glideBackSeen = Obj.ShuttleStrafeReturns; }
            int dir = v > GlideStillMps ? 1 : v < -GlideStillMps ? -1 : 0;
            if (dir != 0 && dir != glideLastDir)
            {
                if (glideLastDir != 0) GlideFlipGapMin = Mathf.Min(GlideFlipGapMin, now - glideLastDirAt);
                glideLastDir = dir; glideLastDirAt = now;
            }
            if (Obj.ShuttleStrafes > glideSeen)
            {
                glideSeen = Obj.ShuttleStrafes; glideOn = true; glideStartAt = now; glideOutAt = -1f; glideBackAt = -1f; glidePeakM = 0f; glidePeakV = 0f; glideSideNow = dir;
            }
            if (!glideOn) { glideBackSeen = Obj.ShuttleStrafeReturns; return; }
            glidePeakM = Mathf.Max(glidePeakM, Mathf.Abs(off)); glidePeakV = Mathf.Max(glidePeakV, Mathf.Abs(v));
            if (glideSideNow == 0) glideSideNow = dir;
            if (glideOutAt < 0f && now - glideStartAt > 0.1f && dir == 0) glideOutAt = now;                     // out: it holds
            if (glideOutAt >= 0f && glideBackAt < 0f && dir != 0 && dir != glideSideNow) glideBackAt = now;   // drifting back
            if (Obj.ShuttleStrafeReturns > glideBackSeen)
            {
                glideBackSeen = Obj.ShuttleStrafeReturns; glideOn = false; Glides++;
                GlideOutM = glidePeakM; GlideOutSec = glideOutAt >= 0f ? glideOutAt - glideStartAt : -1f;
                GlideHoldSec = glideOutAt >= 0f && glideBackAt >= 0f ? glideBackAt - glideOutAt : -1f;
                GlideBackSec = glideBackAt >= 0f ? now - glideBackAt : -1f; GlidePeakMps = glidePeakV;
                if (GlideLog.Length > 20000) GlideLog.Clear();
                GlideLog.AppendLine($"glide #{Glides} of shuttle #{Obj.Id} to its {(glideSideNow > 0 ? "right" : "left")}: out {GlideOutM:F2} m in {GlideOutSec:F2} s (peak sideways speed {GlidePeakMps:F2} m/s), held {GlideHoldSec:F2} s, back in {GlideBackSec:F2} s; the shortest time between two turns of its sideways direction so far {GlideFlipGapMin:F2} s");
            }
        }
    }
}
