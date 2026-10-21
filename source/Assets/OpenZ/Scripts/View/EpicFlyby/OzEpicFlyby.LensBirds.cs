#if UNITY_EDITOR
// OpenZ (GPL-3.0) - epic flybys: parrots of the level that fly close past the lens (the sunrise flyby's first seconds), as well as the
// far flock at the sun (OzEpicFlyby.Birds.cs). Drawn only: the birds are the level's own parrot model lent by its animal code
// (JungleFauna.LendParrot: not steered, not shrunk near the lens), placed here every captured frame and given back after the take.
// No rule reads them. Every bird flies one straight line laid out in the camera's frame of the group's second `at`; a bird is drawn
// while it is on its line or still in the view (a line runs on until the bird has left the picture), so no bird appears or vanishes
// inside the picture. Wing beats are the parrot's own flap loop; the flock's wing flutter and squawks as world sounds of the level (2D).
using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    public sealed partial class OzEpicFlyby
    {
        /// <summary>One group of birds that flies past the lens: count birds (all groups together at most 8), each on a straight line from
        /// `from` (at second at - lead) to `to` (at second at + tail), both in the camera's frame of second `at` (m: x right, y up, z ahead);
        /// each bird is offset from that line by up to `spread` (m, x y z) and in time by up to `stagger` (s); size = scale of the level's
        /// parrot (1 = 1.42 m wings); flap = wing-beat speed (1 = the model's own); gain = the flutter's level (squawks at 0.45 of it).</summary>
        [System.Serializable] public class LensBirds
        {
            public int count, seed; public float at, lead, tail, stagger, scale, flap, gain; public float[] from, to, spread; public string note;
        }

        sealed class LensBird { public Transform h; public Vector3 a, b; public float t0, t1, phase, roll; public bool done, shown; public int group; }
        readonly List<LensBird> lensBirds = new List<LensBird>();
        bool lensBuilt, lensWarned; bool[] lensCalled;
        int lensFramesSeen, lensLent; float lensFirstSeen = -1f, lensLastSeen = -1f, lensBiggest; int lensMostAtOnce;

        void BuildLensBirds()
        {
            lensBuilt = true;
            var groups = sc.lens_birds; if (groups == null || groups.Length == 0) return;
            lensCalled = new bool[groups.Length * 2];
            for (int gi = 0; gi < groups.Length; gi++)
            {
                var q = groups[gi]; if (q == null || q.count <= 0 || q.from == null || q.from.Length < 3 || q.to == null || q.to.Length < 3) continue;
                Pose(Mathf.Max(0f, q.at), out var cp, out var cl, out _, out _);
                var fwd = cl - cp; if (fwd.sqrMagnitude < 1e-4f) fwd = Vector3.forward;
                var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
                var rnd = new System.Random((q.seed != 0 ? q.seed : sc.seed) * 31 + gi);
                var sp = q.spread != null && q.spread.Length >= 3 ? new Vector3(q.spread[0], q.spread[1], q.spread[2]) : new Vector3(1f, 0.6f, 1f);
                float lead = q.lead > 0f ? q.lead : 1f, tail = q.tail > 0f ? q.tail : 1f;
                for (int i = 0; i < q.count; i++)
                {
                    if (lensBirds.Count >= 8) break;
                    var h = JungleFauna.LendParrot();
                    if (!h) { if (!lensWarned) { lensWarned = true; Note("WARNING lens birds: the level lent no parrot (no animal code on this level, or none left): bird " + lensBirds.Count + " and on left out"); } break; }
                    lensLent++;
                    var off = new Vector3(((float)rnd.NextDouble() * 2f - 1f) * sp.x, ((float)rnd.NextDouble() * 2f - 1f) * sp.y, ((float)rnd.NextDouble() * 2f - 1f) * sp.z);
                    float dt = ((float)rnd.NextDouble() * 2f - 1f) * q.stagger;
                    var b = new LensBird
                    {
                        h = h, group = gi, phase = (float)rnd.NextDouble(), roll = ((float)rnd.NextDouble() * 2f - 1f) * 10f,
                        a = cp + rot * (new Vector3(q.from[0], q.from[1], q.from[2]) + off),
                        b = cp + rot * (new Vector3(q.to[0], q.to[1], q.to[2]) + off),
                        t0 = q.at - lead + dt, t1 = q.at + tail + dt,
                    };
                    float s = q.scale > 0f ? q.scale : 1f; h.localScale = Vector3.one * s;
                    h.gameObject.SetActive(false);
                    lensBirds.Add(b);
                }
            }
            Note("lens birds: " + lensBirds.Count + " parrots of the level lent for " + groups.Length + " group(s) past the lens");
        }

        /// <summary>Each captured frame (after the camera is placed): every lens bird on its line, its wing beat, the flock's sounds.</summary>
        void TickLensBirds(float t)
        {
            if (sc == null || sc.lens_birds == null || sc.lens_birds.Length == 0 || !cam) return;
            if (!lensBuilt) BuildLensBirds();
            var groups = sc.lens_birds;
            for (int gi = 0; gi < groups.Length; gi++)
            {   // the flutter as the group comes to the lens, a squawk as it passes
                var q = groups[gi]; if (q == null || lensCalled == null) continue;
                float gain = q.gain > 0f ? q.gain : 0.45f;
                if (!lensCalled[gi * 2] && t >= q.at - 0.45f) { lensCalled[gi * 2] = true; PlaySfx(gi % 2 == 0 ? "sd_parrot_flock_flutter_a" : "sd_parrot_flock_flutter_b", gain); }
                if (!lensCalled[gi * 2 + 1] && t >= q.at + 0.1f) { lensCalled[gi * 2 + 1] = true; PlaySfx(gi % 2 == 0 ? "sd_parrot_squawk_a" : "sd_parrot_squawk_d", gain * 0.45f); }
            }
            int seen = 0; float th = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            foreach (var b in lensBirds)
            {
                if (b.done || !b.h) continue;
                float k = (t - b.t0) / Mathf.Max(0.05f, b.t1 - b.t0);
                var dir = b.b - b.a; var p = b.a + dir * k;
                p += Vector3.up * (Mathf.Sin((t + b.phase) * 3.1f) * 0.12f);
                var vp = cam.WorldToViewportPoint(p);
                bool inView = vp.z > 0.3f && vp.x > -0.08f && vp.x < 1.08f && vp.y > -0.08f && vp.y < 1.08f;
                bool show = (k >= 0f && k <= 1f) || inView;
                if (!show) { if (b.shown || k > 1f) { if (k > 1f) b.done = true; b.h.gameObject.SetActive(false); b.shown = false; } continue; }
                if (!b.shown) { b.h.gameObject.SetActive(true); b.shown = true; }
                var q = groups[b.group];
                var look = dir.sqrMagnitude > 1e-4f ? dir.normalized : Vector3.forward;
                var r = Quaternion.LookRotation(look, Vector3.up) * Quaternion.Euler(-6f, 0f, b.roll + Mathf.Sin((t + b.phase) * 1.9f) * 7f);
                b.h.SetPositionAndRotation(p, r);
                JungleFauna.LendParrotPose(b.h, (t + b.phase) * (q.flap > 0f ? q.flap : 1f));
                if (inView && vp.z > 0.3f && vp.x >= 0f && vp.x <= 1f && vp.y >= 0f && vp.y <= 1f)
                {
                    seen++;
                    float share = 1.42f * b.h.localScale.x / (2f * vp.z * th); if (share > lensBiggest) lensBiggest = share;
                }
            }
            if (curI >= 0 && seen > 0)
            {
                lensFramesSeen++; if (lensFirstSeen < 0f) lensFirstSeen = t; lensLastSeen = t; if (seen > lensMostAtOnce) lensMostAtOnce = seen;
            }
        }

        void ReleaseLensBirds()
        {
            if (lensBuilt && lensBirds.Count > 0)
                Note("lens birds: " + lensLent + " parrots past the lens, in the picture from " + lensFirstSeen.ToString("F2") + " s to " + lensLastSeen.ToString("F2") + " s (" + lensFramesSeen +
                     " frames), most at once " + lensMostAtOnce + ", the largest wing span " + lensBiggest.ToString("F2") + " of the frame height (birds are not units: the RTS rule does not apply)");
            foreach (var b in lensBirds) if (b.h) { b.h.gameObject.SetActive(true); b.h.localScale = Vector3.one; JungleFauna.LendParrotReturn(b.h); }
            lensBirds.Clear(); lensBuilt = false; lensCalled = null;
        }
    }
}
#endif
