using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The playable shuttle's look with other ships in the air (rule DV118 + the rules' traffic, Sim/ZWorld.ShuttleTraffic.cs; view only):
    /// <list type="bullet">
    /// <item>NOT IN STEP: level 16's own ship (else the first one drawn: the rules' PlayableShuttle) keeps the look as it was; every other
    /// ship cruises a little higher or lower (<see cref="CruiseVarMinM"/> .. <see cref="CruiseVarMaxM"/> m, up or down by its id: ships
    /// dropped one after another alternate), and its hover bob runs at its own pace (the waves up to <see cref="BobRateVar"/> faster or
    /// slower) and from its own moment (the phase from its id), so no two ships rise and sink together.</item>
    /// <item>GIVING WAY IN HEIGHT: when two ships are up within <see cref="PassNearM"/> of each other and their hulls would overlap in the
    /// picture (the camera's own view of both hulls: their silhouettes, the ship's plan outline from its pads' height to its pods' top and
    /// the cabin's roof above, as the camera sees them), the one drawn higher on the screen rises and the other dips, sharing what clears
    /// them by how far each is up (a ship climbing out or coming down gives little, the other the rest), at most <see cref="PassMaxM"/>
    /// each, starting up to <see cref="PassLeadSec"/> before they would overlap (the rules' flights and climbs, looked at now, at a third, two thirds and
    /// then), eased (<see cref="PassEaseSec"/>, at most <see cref="PassMaxMps"/>); with no camera the higher one rises and the lower one dips. The
    /// rules' separation keeps them apart on the ground plan; this keeps their hulls apart in the picture.</item>
    /// <item>THE LEAN: into the rules' avoid offset as into the strafe (its sideways speed and acceleration, the same lean rule).</item>
    /// </list>
    /// The height add rides on the cruise height (x the eased lift), so it is gone at the touchdown; the landing mark (OzShuttleShadow) and
    /// the night lights (OzShuttleNightLights) are each ship's own and follow it.
    /// </summary>
    public sealed partial class OzPlayableShuttle
    {
        /// <summary>The traffic look on (the height and bob variance, the giving way in height). Off: every ship drawn as level 16's own.</summary>
        public static bool TrafficLook = true;
        /// <summary>Every other ship's own cruise height step (m, its size; up or down by its id) and how much its bob's pace may differ.</summary>
        public static float CruiseVarMinM = 0.8f, CruiseVarMaxM = 1.5f, BobRateVar = 0.12f;
        /// <summary>The giving way in height: ships closer than this (m, flat) are looked at; at most this much each (m); the gap kept in the
        /// picture (share of the screen's height).</summary>
        public static float PassNearM = 22f, PassMaxM = 3.0f, PassGap = 0.006f;
        /// <summary>The giving way looks this far ahead (s, the rules' flight velocities), is eased over this time (s) at most this fast (m/s).</summary>
        public static float PassLeadSec = 1.5f, PassEaseSec = 0.3f, PassMaxMps = 5.0f;
        /// <summary>The hull's silhouette for the picture test (root space, m): the plan outline (ZObject.ShuttlePlan: hull, pods and legs seen
        /// from above) from SilY0 to SilY1 (the pods' top), and the cabin's roof (half width SilRoofW, from SilRoofZ0 to SilRoofZ1) at SilRoofY.</summary>
        public static float SilY0 = 1.0f, SilY1 = 4.5f, SilRoofY = 5.1f, SilRoofW = 1.2f, SilRoofZ0 = -2.4f, SilRoofZ1 = 2.0f;
        /// <summary>Proof: two hulls whose drawn heights differ less than this (m: the silhouette from its pads to the cabin roof + 1 m) are at
        /// the same layer (an overlap in the picture there looks like one going into the other); a larger gap = one flies over the other.</summary>
        public static float SameLayerM => SilRoofY - SilY0 + 1.0f;
        /// <summary>Proof: frames in which some ship gave way in height, and the most it gave (m).</summary>
        public static int PassFrames;
        public static float PassPeakM;

        /// <summary>This ship's own cruise height step (m; 0 for the reference ship) and its giving way in height now (m).</summary>
        public float CruiseAddM { get; private set; }
        public float PassAddM { get; private set; }
        /// <summary>The ship whose look is the reference (level 16's own, else the first one drawn): no variance.</summary>
        public bool TrafficReference { get; private set; }
        /// <summary>The bob's paces and phase this ship runs with (proof).</summary>
        public float BobPhaseSec { get; private set; }
        float passVel, passApplied, trafficLift, trafficLiftRate, trafficLastLift = -1f; bool trafficVaried;

        /// <summary>Pose: what the traffic adds to the cruise height (m) this frame (x the eased lift there).</summary>
        float TrafficAddM(float dt, float lift)
        {
            if (!trafficVaried) TrafficVary();
            if (!TrafficLook || Obj == null || game == null || game.World == null) { passApplied = 0f; return 0f; }
            if (trafficLastLift >= 0f && dt > 1e-5f) trafficLiftRate = Mathf.Lerp(trafficLiftRate, (lift - trafficLastLift) / dt, 1f - Mathf.Exp(-dt / 0.15f));   // how fast it climbs or comes down (eased)
            trafficLastLift = lift; trafficLift = lift;
            float want = PassWanted();
            if (want != 0f || PassAddM != 0f || passVel != 0f)
            {
                PassAddM = Mathf.SmoothDamp(PassAddM, want, ref passVel, PassEaseSec, PassMaxMps, dt);
                if (want == 0f && Mathf.Abs(PassAddM) < 0.002f && Mathf.Abs(passVel) < 0.01f) { PassAddM = 0f; passVel = 0f; }
            }
            if (PassAddM != 0f) { PassFrames++; PassPeakM = Mathf.Max(PassPeakM, Mathf.Abs(PassAddM)); }
            passApplied = PassAddM * Mathf.SmoothStep(0f, 1f, lift);
            return CruiseAddM + PassAddM;
        }

        /// <summary>Once: this ship's own cruise step and bob from its id (the reference ship keeps the look as it was).</summary>
        void TrafficVary()
        {
            trafficVaried = true;
            TrafficReference = Obj == null || game == null || game.World == null || Obj == game.World.PlayableShuttle;
            if (TrafficReference || !TrafficLook) { CruiseAddM = 0f; return; }
            int h = Hash(Obj.Id, 338);
            float u1 = (h & 1023) / 1023f, u2 = ((h >> 10) & 1023) / 1023f, u3 = ((h >> 20) & 1023) / 1023f;
            CruiseAddM = ((Obj.Id & 1) == 0 ? 1f : -1f) * Mathf.Lerp(CruiseVarMinM, CruiseVarMaxM, u1);
            float r1 = 1f + BobRateVar * (2f * u2 - 1f), r2 = 1f + BobRateVar * (1f - 2f * u3);
            bobHz *= r1; bobHz2 *= r2; driftHz *= r2;
            BobPhaseSec = (u3 + 0.37f * u1) % 1f / Mathf.Max(0.05f, bobHz);                               // up to one slow wave
            t += BobPhaseSec;
        }

        /// <summary>Its drawn place without its giving way in height (last frame's pose).</summary>
        Vector3 TrafficRest => transform.position - new Vector3(0f, passApplied, 0f);

        /// <summary>The giving way in height wanted now (m, + = up): half of what clears each overlap in the picture with a ship close by.</summary>
        float PassWanted()
        {
            if (!Obj.ShuttleAirborne || BrokenUp || dying) return 0f;
            var cam = Camera.main;
            Vector3 me = TrafficRest; float want = 0f;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (!o || o == this || o.Obj == null || o.BrokenUp || o.dying || o.Obj.Destroyed || !o.Obj.ShuttleAirborne) continue;
                Vector3 ot = o.TrafficRest;
                if (new Vector2(ot.x - me.x, ot.z - me.z).sqrMagnitude > PassNearM * PassNearM) continue;
                float need = 0f; bool upper = false;
                if (cam)
                {   // now, half way and a moment ahead where the rules' flights and climbs take both (it starts before they overlap); the side from the first overlap
                    for (int k = 0; k < 4; k++)
                    {
                        float L = PassLeadSec * k / 3f;
                        var meA = me + new Vector3(Obj.Dx / 8f * L, ClimbAhead(L), -Obj.Dy / 8f * L); var otA = ot + new Vector3(o.Obj.Dx / 8f * L, o.ClimbAhead(L), -o.Obj.Dy / 8f * L);
                        float n = PictureNeed(cam, meA, yaw, otA, o.yaw, o.Obj.Id, out bool u);
                        if (n > 0f && need <= 0f) upper = u;
                        need = Mathf.Max(need, n);
                    }
                }
                else { upper = me.y > ot.y || (me.y == ot.y && Obj.Id > o.Obj.Id); need = 0f; }
                if (need <= 0f) continue;
                float wa = Mathf.SmoothStep(0f, 1f, trafficLift), wb = Mathf.SmoothStep(0f, 1f, o.trafficLift);   // by how far each is up (what it gives is x that)
                float half = Mathf.Min(PassMaxM, need * wa / Mathf.Max(0.05f, wa * wa + wb * wb));
                want += upper ? half : -half;
            }
            return Mathf.Clamp(want, -PassMaxM, PassMaxM);
        }

        /// <summary>How much higher (m) its rest place is `sec` ahead by its climb or descent now (the rules' lift, eased rate).</summary>
        float ClimbAhead(float sec)
        {
            float h = (float)ZWorld.ShuttleCruiseM + CruiseAddM, l1 = Mathf.Clamp01(trafficLift + trafficLiftRate * sec);
            return h * (Mathf.SmoothStep(0f, 1f, l1) - Mathf.SmoothStep(0f, 1f, trafficLift));
        }

        /// <summary>The height (m, both ships together) that clears their silhouettes in the picture (0: they do not overlap), and whether this
        /// ship is the one drawn higher on the screen.</summary>
        float PictureNeed(Camera cam, Vector3 a, float yawA, Vector3 b, float yawB, int otherId, out bool upper)
        {
            upper = false;
            var pa = Silhouette2D(cam, a, yawA, out float ka, out float ca); var pb = Silhouette2D(cam, b, yawB, out float kb, out float cb, true);
            upper = ca > cb || (ca == cb && Obj.Id > otherId);
            if (pa == null || pb == null) return 0f;
            var lo = upper ? pb : pa; var hi = upper ? pa : pb;
            if (!Overlap(lo, hi, PassGap)) return 0f;
            float d0 = 0f, d1 = 0.6f;                                                                            // how far up (screen share) the upper one must go to be clear
            if (Overlap(lo, hi, PassGap, d1)) d0 = d1;
            else for (int i = 0; i < 11; i++) { float m = 0.5f * (d0 + d1); if (Overlap(lo, hi, PassGap, m)) d0 = m; else d1 = m; }
            float k = 0.5f * (ka + kb);
            return k > 1e-5f ? d1 / k : 0f;
        }

        static Vector3[] silhouette;
        /// <summary>The silhouette's points in root space (the plan outline at two heights, the roof's corners).</summary>
        static Vector3[] SilhouettePoints()
        {
            if (silhouette != null) return silhouette;
            var plan = ZObject.ShuttlePlan; int n = plan.Length / 2; var l = new List<Vector3>();
            for (int i = 0; i < n; i++) { l.Add(new Vector3((float)plan[2 * i], SilY0, (float)plan[2 * i + 1])); l.Add(new Vector3((float)plan[2 * i], SilY1, (float)plan[2 * i + 1])); }
            foreach (float x in new[] { -SilRoofW, SilRoofW }) foreach (float z in new[] { SilRoofZ0, SilRoofZ1 }) l.Add(new Vector3(x, SilRoofY, z));
            return silhouette = l.ToArray();
        }

        static readonly List<Vector2> silTmp = new List<Vector2>(), silHullA = new List<Vector2>(), silHullB = new List<Vector2>();
        /// <summary>The silhouette seen by the camera (viewport units, a convex outline), how far one metre of height moves it on the screen
        /// there, and its middle's height on the screen. second = into the second buffer (two at a time). Null: behind the camera.</summary>
        static List<Vector2> Silhouette2D(Camera cam, Vector3 at, float yawDeg, out float perM, out float cy, bool second = false)
        {
            perM = 0f; cy = 0f;
            var q = Quaternion.Euler(0f, yawDeg, 0f); var pts = SilhouettePoints(); silTmp.Clear();
            for (int i = 0; i < pts.Length; i++)
            {
                var v = cam.WorldToViewportPoint(at + q * pts[i]);
                if (v.z <= 0.1f) return null;
                silTmp.Add(new Vector2(v.x, v.y)); cy += v.y;
            }
            cy /= pts.Length;
            var mid = at + new Vector3(0f, 0.5f * (SilY0 + SilY1), 0f);
            perM = cam.WorldToViewportPoint(mid + Vector3.up).y - cam.WorldToViewportPoint(mid).y;
            var hull = second ? silHullB : silHullA; Hull(silTmp, hull);
            return hull;
        }

        /// <summary>Orders points by x, then y (one shared instance: sorting with it allocates nothing per frame).</summary>
        sealed class SilOrder : IComparer<Vector2>
        {
            public static readonly SilOrder I = new SilOrder();
            public int Compare(Vector2 a, Vector2 b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y);
        }

        /// <summary>The convex hull of the points (counter-clockwise; Andrew's monotone chain).</summary>
        static void Hull(List<Vector2> p, List<Vector2> h)
        {
            p.Sort(SilOrder.I); h.Clear();
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < p.Count; k++)
                {
                    var c = pass == 0 ? p[k] : p[p.Count - 1 - k];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], c) <= 0f) h.RemoveAt(h.Count - 1);
                    h.Add(c);
                }
                h.RemoveAt(h.Count - 1);
            }
        }
        static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>The two convex outlines overlap (closer than `gap`), the second moved up by `up` (separating axes).</summary>
        static bool Overlap(List<Vector2> a, List<Vector2> b, float gap, float up = 0f)
        {
            for (int s = 0; s < 2; s++)
            {
                var e = s == 0 ? a : b;
                for (int i = 0; i < e.Count; i++)
                {
                    var p0 = e[i]; var p1 = e[(i + 1) % e.Count]; var nrm = new Vector2(p1.y - p0.y, p0.x - p1.x); float nl = nrm.magnitude; if (nl < 1e-9f) continue; nrm /= nl;
                    float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                    foreach (var v in a) { float d = Vector2.Dot(v, nrm); amin = Mathf.Min(amin, d); amax = Mathf.Max(amax, d); }
                    foreach (var v in b) { float d = Vector2.Dot(new Vector2(v.x, v.y + up), nrm); bmin = Mathf.Min(bmin, d); bmax = Mathf.Max(bmax, d); }
                    if (amax + gap <= bmin || bmax + gap <= amin) return false;
                }
            }
            return true;
        }

        /// <summary>Proof: the two ships' silhouettes overlap in the camera's picture as they are drawn now.</summary>
        public static bool HullsOverlap(Camera cam, OzPlayableShuttle a, OzPlayableShuttle b)
        {
            if (!cam || !a || !b) return false;
            var pa = Silhouette2D(cam, a.transform.position, a.transform.eulerAngles.y, out _, out _); var pb = Silhouette2D(cam, b.transform.position, b.transform.eulerAngles.y, out _, out _, true);
            return pa != null && pb != null && Overlap(pa, pb, 0f);
        }

        /// <summary>Proof: how deep the two ships' silhouettes overlap in the picture as drawn now (pixels of the screen: the least shift on
        /// any axis that parts them; 0 when apart).</summary>
        public static float OverlapDepthPx(Camera cam, OzPlayableShuttle a, OzPlayableShuttle b)
        {
            if (!cam || !a || !b) return 0f;
            var pa = Silhouette2D(cam, a.transform.position, a.transform.eulerAngles.y, out _, out _); var pb = Silhouette2D(cam, b.transform.position, b.transform.eulerAngles.y, out _, out _, true);
            if (pa == null || pb == null) return 0f;
            float w = cam.pixelWidth, h = cam.pixelHeight, best = float.MaxValue;
            for (int s = 0; s < 2; s++)
            {
                var e = s == 0 ? pa : pb;
                for (int i = 0; i < e.Count; i++)
                {
                    var p0 = new Vector2(e[i].x * w, e[i].y * h); var p1 = new Vector2(e[(i + 1) % e.Count].x * w, e[(i + 1) % e.Count].y * h);
                    var nrm = new Vector2(p1.y - p0.y, p0.x - p1.x); float nl = nrm.magnitude; if (nl < 1e-6f) continue; nrm /= nl;
                    float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                    foreach (var q in pa) { float d = q.x * w * nrm.x + q.y * h * nrm.y; amin = Mathf.Min(amin, d); amax = Mathf.Max(amax, d); }
                    foreach (var q in pb) { float d = q.x * w * nrm.x + q.y * h * nrm.y; bmin = Mathf.Min(bmin, d); bmax = Mathf.Max(bmax, d); }
                    float ov = Mathf.Min(amax, bmax) - Mathf.Max(amin, bmin);
                    if (ov <= 0f) return 0f;
                    best = Mathf.Min(best, ov);
                }
            }
            return best == float.MaxValue ? 0f : best;
        }
    }
}
