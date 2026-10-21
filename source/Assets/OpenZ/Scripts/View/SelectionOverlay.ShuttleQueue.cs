using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K410 (rule DV125, the shuttle flies the whole Shift queue): the route drawing of a selected shuttle with two points or more (on top of
    /// K387's route from the ship in the sky, View/SelectionOverlay.ShuttleRoute.cs, which still draws every one-point route).
    /// <list type="bullet">
    /// <item>THE POINTS: the route the ship still has to fly (its middle points left, ZObject.ShuttleViaX / Y, then its goal) and, while Shift
    /// is held, the Shift points after it (input.Pending: what the click will send, View/OzShuttleQueueInput.cs).</item>
    /// <item>EVERY MIDDLE POINT IN THE AIR: drawn as a point up at the cruise height over its ground spot (the height the route leaves the
    /// ship at while it cruises: ZWorld.ShuttleCruiseM - RouteGapM over the drawn ground there; held over what stands there with the ship's
    /// clearance grid), with a small ring on the ground right under it and a faint drop of small dots between the two (so the player sees
    /// the place that was clicked and that the ship passes over it up there).</item>
    /// <item>THE LEGS: from the ship to the first middle point and between middle points LEVEL at the cruise height (a take-off or a climb:
    /// first flat under the ship, then an eased climb to the cruise height, shrinking with the ship's own climb; held over trees, rocks and
    /// buildings and never into the hull, as K387's legs); only the LAST leg comes down, smoothly, to the goal on the ground (K387's glide:
    /// level out of the last middle point, sinking ever faster, into the goal at a gentle angle; it never rises on the way down).</item>
    /// <item>THE LOOK: K387's dots (evenly spaced on screen, sized by distance, the b14 night dimming); the route still to fly in the path
    /// look, the Shift points in the queued look; the goal's order ring (or the queued ring) on the last point only. The ship's landing mark
    /// (K256g2 / K401) takes its landing look only in the final descent (it never comes down at a middle point).</item>
    /// </list>
    /// </summary>
    public partial class SelectionOverlay
    {
        /// <summary>The middle points' marks: the point in the air (r), the ring on the ground (r), the drop's dots (r) and their step (m), the
        /// drop's ends kept clear (m under the point, over the ground).</summary>
        public static float MidDotR = 0.26f, MidRingR = 0.45f, DropDotR = 0.05f, DropStepM = 0.9f, DropTopM = 0.6f, DropFootM = 0.5f;

        /// <summary>Proof (K410): the last frame's middle points in the air, their ground spots (seated), the last point (seated + its dots'
        /// lift); the largest step of a level leg off the straight line between its ends where nothing lifted it (m); frames drawn; the number
        /// of route points and of middle points in the last frame; the drop dots drawn in the last frame.</summary>
        public static readonly List<Vector3> QueueAirPts = new List<Vector3>(), QueueGroundPts = new List<Vector3>();
        public static Vector3 QueueLastPt;
        public static float QueueLevelDevMaxM;
        public static int QueueFrames, QueuePointsNow, QueueMidsNow, QueueDropDotsNow, QueueRingsNow, LastQueueFrameNo = -1;

        readonly List<(int x, int y)> qPts = new List<(int, int)>();
        readonly List<float> qStraight = new List<float>(); readonly List<bool> qLifted = new List<bool>();   // reused every frame

        /// <summary>ShuttleRoute's hook (after the ship's anchor and the route's start are known): two route points or more = drawn here (true);
        /// one point (or none): K387's one-point route (false).</summary>
        bool ShuttleQueueLegs(ZGame g, ZObject o, List<ZWaypoint> q, bool hasQ, bool air, bool takeOff, Vector3 anchor, Vector3 start, float gStart, OzShuttleSkyTop sky, bool queuing)
        {
            if (!ZWorld.ShuttleQueueDeviation) return false;
            qPts.Clear();
            bool fly = (air || takeOff) && o.ShuttleGoalX >= 0 && (!hasQ || OzShuttleQueueInput.Flying(o));
            if (fly)
            {
                for (int i = 0; i < o.ShuttleViaCount; i++) qPts.Add((o.ShuttleViaX(i), o.ShuttleViaY(i)));
                qPts.Add((o.ShuttleGoalX, o.ShuttleGoalY));
            }
            int simCount = qPts.Count;
            if (hasQ) foreach (var w in q) qPts.Add((w.x, w.y));
            int n = qPts.Count;
            if (n < 2) return false;

            if (LastQueueFrameNo != Time.frameCount) { QueueAirPts.Clear(); QueueGroundPts.Clear(); QueueDropDotsNow = 0; QueueRingsNow = 0; QueueMidsNow = 0; }
            LastQueueFrameNo = Time.frameCount; QueueFrames++; QueuePointsNow = n;
            float cruise = (float)ZWorld.ShuttleCruiseM, relH = cruise - RouteGapM;
            Vector3 prev = start; int leg = 0;
            for (int k = 0; k < n; k++)
            {
                bool last = k == n - 1, simLeg = k < simCount;
                var gp = Px(g, qPts[k].x, qPts[k].y, out bool deck);
                Material mat = simLeg ? pathMat : markerMat;
                float step = simLeg ? 1.2f : 0.9f, first = simLeg ? 0.8f : 0.6f, size = simLeg ? 0.12f : 0.16f;
                if (!last)
                {   // a middle point: up in the air over its spot
                    var ap = new Vector3(gp.x, gp.y + relH, gp.z);
                    if (sky != null)
                    {
                        sky.Query(ap.x, ap.z, 0.5f, true, out float ht, out float pt);
                        if (ht > float.MinValue) ap.y = Mathf.Max(ap.y, ht + HardClearM);
                        if (pt > float.MinValue) ap.y = Mathf.Max(ap.y, pt + PlantClearM);
                    }
                    if (new Vector2(ap.x - prev.x, ap.z - prev.z).magnitude >= MinRouteM)
                    {
                        float rise = 0f;
                        if (k == 0 && (takeOff || o.ShuttleState == ZShuttleState.Climb || !air)) rise = Mathf.Max(0f, ap.y - prev.y);
                        BuildLevelLeg(g, sky, o.Id, leg++, prev, rise, ap, k == 0 ? anchor.y : float.NaN);
                        DrawLeg(false, step, first, size, mat);
                    }
                    Draw(dot, ap, MidDotR * (simLeg ? 1f : 1.1f), mat);                                   // the point in the air
                    Draw(ring, gp + Vector3.up * 0.08f, MidRingR, mat); QueueRingsNow++;                   // the ring on the ground under it
                    for (float y = ap.y - DropTopM; y > gp.y + DropFootM; y -= DropStepM)                  // the faint drop between them
                    { Draw(dot, new Vector3(gp.x, y, gp.z), DropDotR, mat); QueueDropDotsNow++; }
                    QueueAirPts.Add(ap); QueueGroundPts.Add(gp); QueueMidsNow++;
                    prev = ap;
                }
                else
                {   // the last point: the only leg that comes down, onto the goal on the ground
                    var end = gp + Vector3.up * (simLeg ? 0.1f : 0.12f);
                    QueueLastPt = end; LastGoal = gp;
                    if (new Vector2(end.x - prev.x, end.z - prev.z).magnitude >= MinRouteM)
                    {
                        int i0 = RoutePts.Count;
                        BuildLeg(g, sky, o.Id, leg++, prev, 0f, end, float.NaN);
                        DrawLeg(simLeg && !hasQ, step, first, size, mat);
                        FinalCheck(i0);
                    }
                    if (!simLeg) Draw(ring, gp + Vector3.up * 0.1f, 0.7f, markerMat);                    // the queued ring (Shift points)
                    else
                    {   // the goal's order mark: the ring every unit's target gets (its order echo, or while Shift-queuing)
                        float mAge = Time.unscaledTime - input.MarkerTime, markLife = OpenZUnitOverlay.OrderMarkLife;
                        if (queuing || (mAge >= 0f && mAge < markLife))
                        {
                            float ms = queuing ? 0.45f : 0.45f * (mAge < 0.12f ? Mathf.Lerp(0.6f, 1.3f, mAge / 0.12f) : Mathf.Lerp(1.3f, 1.05f, (mAge - 0.12f) / (markLife - 0.12f)));
                            Draw(ring, gp + Vector3.up * 0.08f, ms, pathMat);
                        }
                    }
                }
            }
            return true;
        }

        /// <summary>One level leg into legBuf (and RoutePts): from s to the air point e at the cruise height. rise = how far the ship is under
        /// the cruise height (a take-off or a climb: flat under the ship's plan, then an eased climb, at most RiseSlope steep on screen terms,
        /// shrinking to the plain level line as the climb ends); shipLowY = the ship's lowest point when the leg starts at the ship (NaN: at a
        /// middle point). Held over what stands on the way (K387's clearance samples), never into the hull.</summary>
        void BuildLevelLeg(ZGame g, OzShuttleSkyTop sky, int id, int leg, Vector3 s, float rise, Vector3 e, float shipLowY)
        {
            legBuf.Clear(); legReq.Clear();
            bool underShip = !float.IsNaN(shipLowY);
            var h = new Vector3(e.x - s.x, 0f, e.z - s.z); float L = h.magnitude;
            int n = Mathf.Clamp(Mathf.CeilToInt(L / 0.25f), 8, 480);
            float clear0 = underShip ? HullClearM : 0f;
            float climbLen = Mathf.Clamp(rise / Mathf.Max(0.05f, RiseSlope), 0.5f, Mathf.Max(0.5f, (L - clear0) * 0.6f));
            float wClimb = Mathf.Clamp01((rise - 0.25f) / 0.5f);                                                  // a take-off / climb: the climb profile; cruising: the straight level line
            var slot = Clearance(g, sky, id, leg, s, e, L);
            var straight = qStraight; var lifted = qLifted; straight.Clear(); lifted.Clear();
            for (int i = 0; i <= n; i++)
            {
                float u = (float)i / n, sd = u * L, dGoal = L - sd;
                var p = s + h * u;
                float lin = Mathf.Lerp(s.y, e.y, u);
                float t = sd <= clear0 ? 0f : Mathf.Clamp01((sd - clear0) / climbLen);
                float climb = s.y + (e.y - s.y) * t * t * (3f - 2f * t);
                float y = Mathf.Lerp(lin, climb, wClimb);
                float req = ((underShip && sd < HullClearM) || slot == null) ? Seat(g, p, 0.1f, false).y : Need(slot, dGoal);
                straight.Add(y); lifted.Add(false);
                p.y = y; legBuf.Add(p); legReq.Add(req);
            }
            legBuf[n] = e;
            for (int pass = 0; pass < 3; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    var p = legBuf[i];
                    if (legReq[i] > p.y) { p.y = legReq[i]; lifted[i] = true; legBuf[i] = p; }
                }
                if (pass == 2) break;
                for (int i = 1; i < n; i++)
                {
                    if (!lifted[i] && !lifted[i - 1] && !lifted[i + 1]) continue;
                    var p = legBuf[i]; p.y = Mathf.Max(straight[i], (legBuf[i - 1].y + p.y + legBuf[i + 1].y) / 3f); legBuf[i] = p;
                }
            }
            for (int i = 0; i <= n; i++)
            {
                var p = legBuf[i]; float sd = (float)i / n * L;
                bool inPlan = underShip && new Vector2(p.x - s.x, p.z - s.z).magnitude < HullClearM;
                if (inPlan)
                {   // never into the hull: under the ship's plan the line stays at or under its start
                    if (p.y > s.y) { p.y = s.y; legBuf[i] = p; }
                    HullGapMinM = Mathf.Min(HullGapMinM, shipLowY - p.y);
                }
                else
                {
                    if (slot != null) ClearMinM = Mathf.Min(ClearMinM, p.y - Need(slot, L - sd));
                    if (!lifted[i] && wClimb <= 0f) QueueLevelDevMaxM = Mathf.Max(QueueLevelDevMaxM, Mathf.Abs(p.y - straight[i]));
                }
            }
            RoutePts.AddRange(legBuf); RouteLegEnds.Add(RoutePts.Count - 1); RouteLegsNow++;
        }

        /// <summary>Proof (K410): the counters back to their start.</summary>
        public static void ShuttleQueueReset() { QueueFrames = 0; QueueLevelDevMaxM = 0f; }
    }
}
