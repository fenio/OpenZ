using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// Hand props held the way people hold them: a drink can stays UPRIGHT in world space (long axis vertical, top up) the whole
    /// time it is held, and only during a sip does its top tilt toward the mouth (at most CanSipMaxDeg from vertical); a phone is
    /// held in portrait (long side vertical, the top leaning back at most PhoneLookTiltDeg so the screen faces the eyes), and stands
    /// upright beside the head during a call. The hand sockets turn with the wrist animation, so the prop's world rotation is
    /// rebuilt from world up every frame (after the Animator), keeping the twist of the hand and the grip point in the fist.
    /// Every held frame is measured (prop axis vs world up) per robot type and phase for the angle report. View only.
    /// </summary>
    public static class PropUpright
    {
        /// <summary>Can: full sip tilt (deg from vertical) when the can is within CanSipNear of the mouth, none beyond CanSipFar (authored m).</summary>
        public static float CanSipMaxDeg = 35f, CanSipNear = 0.30f, CanSipFar = 0.62f;
        /// <summary>Mouth point from the Head bone (authored m): up, forward.</summary>
        public static float MouthUp = 0.10f, MouthFwd = 0.16f, EyesUp = 0.20f;
        /// <summary>Phone: the top leans back this far (deg) while the robot looks at it; during a call it stands this close to upright.</summary>
        public static float PhoneLookTiltDeg = 28f, PhoneCallTiltDeg = 4f;
        /// <summary>Phone grip point along its height (0 = bottom, 1 = top): looking / at the ear.</summary>
        public static float PhoneGripLook = 0.2f, PhoneGripCall = 0.42f;
        /// <summary>The fist counts as "at the ear" when it is more than this far (authored m) to the side of the head and more to the
        /// side than in front of it; the call screen shows from that point (eased over PhoneCallEase s).</summary>
        public static float PhoneCallSide = 0.16f, PhoneCallEase = 0.25f;
        /// <summary>Phone: the air kept between it and the head / chest / shoulder (authored m); the farthest it is pushed level off the
        /// head (authored m: less than the fist is wide, so it stays in the hand); the lowest point of its height the fist may end up
        /// holding when the phone slides up through the fist to come out of the chest or the shoulder (0 = bottom, 1 = top).</summary>
        public static float PhoneGap = 0.03f, PhoneClearMax = 0.08f, PhoneGripMin = 0.12f;

        // ------------------------------------------------------------------ maths
        /// <summary>World direction of the can's top: world up, tilted toward the mouth by w (0..1) x CanSipMaxDeg.</summary>
        public static Vector3 CanTop(Vector3 canPos, Vector3 mouth, Vector3 robotFront, float w, out float tiltDeg)
        {
            var h = mouth - canPos; h.y = 0f;
            if (h.sqrMagnitude < 1e-6f) h = -robotFront;
            h.Normalize();
            tiltDeg = Mathf.Clamp01(w) * CanSipMaxDeg;
            float r = tiltDeg * Mathf.Deg2Rad;
            return Vector3.up * Mathf.Cos(r) + h * Mathf.Sin(r);
        }

        /// <summary>Sip weight from the can-to-mouth distance (world m) at robot scale k.</summary>
        public static float SipWeight(float dist, float k) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(CanSipFar * k, CanSipNear * k, dist));

        /// <summary>Turns t (minimal rotation, the hand's twist kept) so its local axis `localTop` points along `worldTop`.</summary>
        public static void AlignAxis(Transform t, Vector3 localTop, Vector3 worldTop)
        {
            if (!t) return;
            var cur = t.rotation * localTop;
            t.rotation = Quaternion.FromToRotation(cur, worldTop) * t.rotation;
        }

        /// <summary>Angle (deg) between t's local axis and world up.</summary>
        public static float AxisAngle(Transform t, Vector3 localTop) => t ? Vector3.Angle(t.rotation * localTop, Vector3.up) : 0f;

        // ------------------------------------------------------------------ measurement (the angle sheet / report)
        sealed class Stat { public float max, sum, atDeep, deepW = -1f; public int n; }
        static readonly Dictionary<string, Stat> stats = new Dictionary<string, Stat>();
        /// <summary>The last measured angle per emitter id (the frame capture prints it next to each frame).</summary>
        public static readonly Dictionary<int, string> Live = new Dictionary<int, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { stats.Clear(); Live.Clear(); fits.Clear(); }

        public static void Reset() { stats.Clear(); Live.Clear(); fits.Clear(); }

        /// <summary>One held frame: `who` (robot type or "alan" / "brad"), `phase` (can_hold, can_sip, phone_look, phone_call), the
        /// prop axis angle vs world up and the depth of the move (sip weight; the deepest frame's angle is kept).</summary>
        public static void Note(int id, string who, string phase, float angle, float depth)
        {
            string key = who + "|" + phase;
            if (!stats.TryGetValue(key, out var s)) stats[key] = s = new Stat();
            s.n++; s.sum += angle; s.max = Mathf.Max(s.max, angle);
            if (depth >= s.deepW) { s.deepW = depth; s.atDeep = angle; }
            Live[id] = $"{who} {phase} {angle:F1} deg (depth {depth:F2})";
        }

        sealed class Fit { public int n, capped; public float max; }
        static readonly Dictionary<string, Fit> fits = new Dictionary<string, Fit>();

        /// <summary>One held phone frame of robot type `who`: how far (authored m) the phone was pushed off the grip point to clear the
        /// body, and whether PhoneClearMax cut the push short (the phone may still touch the body on that frame).</summary>
        public static void NotePhoneFit(string who, float push, bool capped)
        {
            if (!fits.TryGetValue(who, out var f)) fits[who] = f = new Fit();
            f.n++; f.max = Mathf.Max(f.max, push); if (capped) f.capped++;
        }

        public static string Report()
        {
            var sb = new StringBuilder();
            var keys = new List<string>(stats.Keys); keys.Sort();
            foreach (var k in keys)
            {
                var s = stats[k];
                sb.Append($"{k.Replace('|', ' ')}: max {s.max:F1} deg, mean {s.sum / Mathf.Max(1, s.n):F1}, at deepest {s.atDeep:F1} (depth {s.deepW:F2}), frames {s.n}\n");
            }
            var fkeys = new List<string>(fits.Keys); fkeys.Sort();
            foreach (var fk in fkeys)
            {
                var f = fits[fk];
                sb.Append($"{fk} phone_fit: max push {f.max:F2} m, frames cut short {f.capped} of {f.n}\n");
            }
            return sb.ToString();
        }
    }
}
