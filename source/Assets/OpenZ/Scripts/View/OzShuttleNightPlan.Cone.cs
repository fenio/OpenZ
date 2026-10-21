// OpenZ (GPL-3.0) - the playable shuttle's nose searchlight (rule DV118, view only): how far it may turn without its beam meeting its own
// ship. Plain numbers (nothing of the engine), so the same code is also counted outside the editor.
using System;

namespace OpenZ
{
    /// <summary>
    /// K256d2, THE CLEAR CONE of the nose searchlight. The drum hangs under the nose; the lock follows an enemy up to
    /// <see cref="LockYawMax"/> + <see cref="KeepYawExtra"/> degrees from the nose, and at a shallow drop (under about 36 degrees) a
    /// beam turned past about 93 degrees went THROUGH the ship's own front leg. Now:
    /// <list type="bullet">
    /// <item>THE EDGE (<see cref="ConeEdgeDeg"/>): per drop under the horizon and per side, the largest turn from the nose at which the
    /// drawn calm cone (the widest the beam gets; the alarm's is narrower) still clears every part of the ship by a few degrees: the
    /// table OzShuttleNightPlan.ConeTable.cs, made by the kit's tool from the ship's own model files (about 93 degrees at a drop of 15 to
    /// 21, 112 at 36, then widening by 4 degrees a degree; from about 42 degrees down it no longer touches any aim the lock can give).</item>
    /// <item>THE AIM: what the drum is asked to look at is followed exactly as before (the sweep, the enemy it follows, its rest), then its
    /// turn is eased into the edge (<see cref="ConeTurn"/>: the same up to <see cref="ConeKneeDeg"/> before the edge, then bending
    /// smoothly toward it, never past it): no snap, no step, the same at any frame rate. The follow no longer turns the long way round
    /// behind the ship (every aim lies inside the edge, so the short way is always the front).</item>
    /// <item>THE LOCK: a new enemy is taken only inside the clear cone: an enemy behind the side is left alone, the light sweeps on (or takes
    /// the nearest enemy that is inside it). An enemy it already follows that walks out of the cone: the beam holds at the edge on its
    /// side, still alarming, and follows it again when it comes back in; more than <see cref="ConeHoldDeg"/> past the edge it lets go (the
    /// old let-go: the soft click, back to the calm white sweep). Inside the cone the lock, the beep and the alarm are unchanged.</item>
    /// </list>
    /// </summary>
    public sealed partial class OzShuttleNightPlan
    {
        /// <summary>K256d2: the turn (deg) before the edge where the easing into it begins; how far past the edge (deg) a followed enemy
        /// may walk before the light lets go of it.</summary>
        public static float ConeKneeDeg = 6f, ConeHoldDeg = 15f;
        /// <summary>K256d2, tests only: false = the aim as before this fix (the counted check's "before" numbers).</summary>
        public static bool ConeLimitOn = true;

        float yawFree, pitchFree = 62f;
        /// <summary>K256d2: the drum's aim before the clear cone (what it follows; degrees).</summary>
        public float YawFree => yawFree;
        public float PitchFree => pitchFree;

        /// <summary>K256d2: the clear cone's edge (deg of turn from the nose) on the side of `yaw` (+ right) at this drop (deg under the
        /// horizon; read between the table's whole degrees on a straight line).</summary>
        public static float ConeEdgeDeg(float yaw, float pitchDeg)
        {
            var t = yaw < 0f ? ConeLeftDeg : ConeRightDeg;
            float f = (pitchDeg - ConeTablePitch0) / ConeTableStep;
            if (!(f > 0f)) return t[0];
            int n = t.Length;
            if (f >= n - 1) return t[n - 1];
            int i = (int)f; float k = f - i;
            return t[i] + (t[i + 1] - t[i]) * k;
        }

        static float ConePitch(float pitchDeg) => pitchDeg < PitchMin ? PitchMin : pitchDeg > PitchMax ? PitchMax : pitchDeg;

        /// <summary>K256d2: may the light take an enemy it sees at this turn and drop (inside the clear cone)?</summary>
        public static bool InClearCone(float yaw, float pitchDeg) => !ConeLimitOn || Math.Abs(yaw) <= ConeEdgeDeg(yaw, ConePitch(pitchDeg));

        /// <summary>K256d2: does the light keep an enemy it follows at this turn and drop (at most <see cref="ConeHoldDeg"/> past the edge)?</summary>
        public static bool KeptByCone(float yaw, float pitchDeg) => !ConeLimitOn || Math.Abs(yaw) <= ConeEdgeDeg(yaw, ConePitch(pitchDeg)) + ConeHoldDeg;

        /// <summary>K256d2: the drawn turn for an aim (yaw, pitch; deg): unchanged up to <see cref="ConeKneeDeg"/> before the edge, then
        /// eased toward the edge (tanh: the same slope where the easing begins, never past the edge).</summary>
        public static float ConeTurn(float yaw, float pitchDeg)
        {
            if (!ConeLimitOn) return yaw;
            float e = ConeEdgeDeg(yaw, pitchDeg), k = Math.Max(0.5f, Math.Min(ConeKneeDeg, e * 0.5f)), s = e - k, a = Math.Abs(yaw);
            if (a <= s) return yaw;
            float v = s + k * (float)Math.Tanh((a - s) / k);
            return yaw < 0f ? -v : v;
        }

        /// <summary>K256d2: one step of the drum's follow (a = this frame's share of the way): the aim follows what it is asked to look at as
        /// before; the drawn turn is that aim eased into the clear cone.</summary>
        void ConeFollow(float tYaw, float tPitch, float a)
        {
            if (!ConeLimitOn)
            {   // as before this fix (tests only)
                YawNow += Delta(YawNow, tYaw) * a; PitchNow += (tPitch - PitchNow) * a;
                yawFree = YawNow; pitchFree = PitchNow;
                return;
            }
            yawFree += (tYaw - yawFree) * a; pitchFree += (tPitch - pitchFree) * a;                 // every aim within +-180: the short way is the front
            PitchNow = pitchFree; YawNow = ConeTurn(yawFree, pitchFree);
        }
    }
}
