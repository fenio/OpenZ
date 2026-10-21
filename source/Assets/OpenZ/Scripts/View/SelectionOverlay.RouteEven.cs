using System.Collections.Generic;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K419a (the route's Play faults, view only, rule DV118 untouched): two small changes to the selected shuttle's route drawing
    /// (View/SelectionOverlay.ShuttleRoute.cs, and K410's legs through View/SelectionOverlay.ShuttleQueue.cs when that kit is in: both draw
    /// their dots through DrawLeg).
    /// <list type="bullet">
    /// <item><b>Even dots on screen:</b> the dots were laid at equal screen ARC length along the curve. Where the curve has a corner or a
    /// steep step on screen (a lift over a rock ridge, a building or a tree crown, a climb toward the camera), two dots either side of it
    /// stood much closer than the rest (the Play proof: 12 of 57 legs over 25 %, the worst 41 %). Now each next dot is laid where the
    /// straight screen distance from the last one reaches the step (<see cref="DrawLegEven"/>): every gap the same, whatever the curve.
    /// The start, the end, the step, the first gap and the sizes are as before.</item>
    /// <item><b>A calmer start:</b> the start under the ship's lowest point followed that point's offset from the root through one
    /// exponential ease; now a critically damped one (<see cref="BellyEase"/>, the same time scale <see cref="BellyTauSec"/>): no jump on
    /// a step: the largest per-frame change about a quarter less on a bank, under half on a sudden step (the twin, tools/twin_belly.py; the
    /// Play proof's 0.051 m against 0.05). It lags up to about 0.3 m behind a fast bank: that part of the line is under the hull's plan
    /// (the depth test hides it), and R1 / R4 measure against the eased point as before.</item>
    /// </list>
    /// </summary>
    public partial class SelectionOverlay
    {
        /// <summary>Switch (on): the even screen spacing.</summary>
        public static bool EvenDots = true;
        /// <summary>Proof: legs drawn with the even spacing.</summary>
        public static int EvenLegs;

        readonly Dictionary<int, float> bellyVel = new Dictionary<int, float>();
        readonly List<Vector3> evenScr = new List<Vector3>(); readonly List<float> evenArc = new List<float>();

        /// <summary>The start's offset under the root (the ship's lowest point), critically damped toward `target` (exact for any frame time:
        /// no overshoot from rest), kept per ship.</summary>
        float BellyEase(int id, float target)
        {
            if (!bellyOff.TryGetValue(id, out float x)) { bellyOff[id] = target; bellyVel[id] = 0f; return target; }
            bellyVel.TryGetValue(id, out float v);
            float dt = Time.deltaTime, w = 1f / Mathf.Max(0.01f, BellyTauSec);
            if (dt > 0f)
            {
                float x0 = x - target, e = Mathf.Exp(-w * dt), b = v + w * x0;
                x = target + (x0 + b * dt) * e; v = (v - w * b * dt) * e;
            }
            bellyOff[id] = x; bellyVel[id] = v;
            return x;
        }

        /// <summary>DrawLeg with the even screen spacing (true = drawn here; false = no camera, or a point behind it: DrawLeg's own code).
        /// From the leg's end: a dot on it when endDot, then each next dot where the straight screen distance from the last dot reaches the
        /// screen length of `step` m of ground at the end; none nearer the start than `first` m of ground (measured along the curve, as
        /// before); each sized by its distance to the camera against the end's.</summary>
        bool DrawLegEven(bool endDot, float step, float first, float size, Material mat)
        {
            int n = legBuf.Count - 1; if (!EvenDots || n < 1) return false;
            var cam = Camera.main; if (cam == null) return false;
            evenScr.Clear(); evenArc.Clear();
            for (int i = n; i >= 0; i--)
            {
                var sp = cam.WorldToScreenPoint(legBuf[i]); if (sp.z <= 0f) return false;
                sp.z = 0f; evenScr.Add(sp);
                evenArc.Add(evenScr.Count == 1 ? 0f : evenArc[evenArc.Count - 1] + Vector3.Distance(evenScr[evenScr.Count - 2], sp));
            }
            var e = legBuf[n];
            float dEnd = Mathf.Max(0.1f, Vector3.Distance(cam.transform.position, e));
            float stepPx = step * Screen.height / (2f * dEnd * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
            if (stepPx < 0.5f) return true;
            float stopAt = evenArc[n] - first / step * stepPx;
            void Dot(int j, float t)
            {
                var p = Vector3.Lerp(legBuf[n - j], legBuf[n - j - 1], t);
                Draw(dot, p, size * Vector3.Distance(cam.transform.position, p) / dEnd, mat);
                RouteDots++; RouteDotsNow++; DotPtsNow.Add(p); DotLegNow.Add(RouteLegsNow - 1);
            }
            if (endDot && stopAt >= 0f) { Draw(dot, e, size, mat); RouteDots++; RouteDotsNow++; DotPtsNow.Add(e); DotLegNow.Add(RouteLegsNow - 1); }
            int seg = 0; float tt = 0f; var last = evenScr[0]; float r2 = stepPx * stepPx;
            for (int k = 0; k < 4000; k++)
            {
                bool found = false;
                while (seg < n)
                {
                    var a = Vector3.Lerp(evenScr[seg], evenScr[seg + 1], tt); var b = evenScr[seg + 1];
                    if ((b - last).sqrMagnitude < r2) { seg++; tt = 0f; continue; }
                    // the segment leaves the circle round the last dot: where |a + (b - a) u - last| = stepPx (a is inside it)
                    var dv = b - a; var fv = a - last;
                    float A = Vector3.Dot(dv, dv), B = 2f * Vector3.Dot(fv, dv), C = Vector3.Dot(fv, fv) - r2;
                    float u = A > 1e-12f ? (-B + Mathf.Sqrt(Mathf.Max(0f, B * B - 4f * A * C))) / (2f * A) : 1f;
                    tt += (1f - tt) * Mathf.Clamp01(u);
                    found = true; break;
                }
                if (!found) break;
                if (evenArc[seg] + (evenArc[seg + 1] - evenArc[seg]) * tt > stopAt) break;
                last = Vector3.Lerp(evenScr[seg], evenScr[seg + 1], tt);
                Dot(seg, tt);
            }
            EvenLegs++;
            return true;
        }
    }
}
