using System.Collections.Generic;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K387: the route of a selected playable shuttle (rule DV118, view only: nothing in the rules changes). The owner: the ship's
    /// waypoints "appear on ground while he is in sky", and then.
    /// <list type="bullet">
    /// <item>WHERE IT STARTS: AT THE SHIP, right under the middle of its lowest drawn point (OzShuttleShadow.LowY, its pads / belly;
    /// <see cref="RouteGapM"/> under it, never under the drawn ground), following the ship every frame (the overlay runs after the
    /// ships: DefaultExecutionOrder 205, the ship is 200; the lowest point's offset from the ship's root is eased, so its legs never
    /// shake the line; the hover bob moves the whole line smoothly with the ship).</item>
    /// <item>THE CURVE: from the ship's height down to the goal on the ground (ZObject.ShuttleGoalX / Y, seated on the drawn surface),
    /// descending smoothly over the distance: height = goal + (start - goal) x F(u), u = the share of the flat distance,
    /// F(u) = 1 - 3u^2 + 2u^3 - k (u^3 - u^2), k = <see cref="EndSlopeK"/> = 1: F = u^3 - 2u^2 + 1. It leaves the ship level (no kink
    /// at the belly), sinks ever faster and comes into the goal at a gentle angle, as the ship's own flight (cruise, then down at the
    /// goal). The height never rises on the way down.</item>
    /// <item>TAKING OFF (ordered from the ground with the take-off held for the ramp, or climbing): the line first stays under the hull
    /// (<see cref="HullClearM"/>), then climbs over at most <see cref="RiseM"/> toward the cruise height (ZWorld.ShuttleCruiseM over the
    /// ground where it is; at most <see cref="RiseSlope"/> x the climb's length and half the leg, so it never turns back on screen) and
    /// glides down to the goal the same way from there; the climb shrinks to nothing as the ship's own climb ends (smooth hand-over).</item>
    /// <item>NEVER THROUGH THINGS: every metre of the way is held over what stands there (the ship's own clearance grid,
    /// OzShuttleSkyTop.Query: terrain, fort, buildings, rocks, props + <see cref="HardClearM"/>; trees and palms + <see cref="PlantClearM"/>),
    /// except the last <see cref="EndFreeM"/> before the goal (its landing pad is free by the rules) and under the ship itself. A lift is
    /// carried back toward the ship (the height still never rises on the way down) and smoothed; under the ship's plan the line stays
    /// under its lowest point (never into the hull). The overlay is depth tested: the hull covers what lies behind it.</item>
    /// <item>THE LOOK: the game's own waypoint dots (the same mesh, colours and b14 night dimming through the same materials), spaced
    /// evenly ON SCREEN (the screen length of the ground dots' step at the goal), each sized by its distance to the camera so a dot up in
    /// the air looks as big as one on the ground; counted from the goal (its dot sits on it), so the dots do not crawl as the ship flies.
    /// The goal gets the order mark ring every unit's target gets (its order echo, or while Shift-queuing).</item>
    /// <item>A SHIFT QUEUE (input.Pending): every queued point is ON THE GROUND (the ship lands at every goal it reaches, DV118: a move
    /// = climb, cruise, land), and each leg after it climbs out from there and glides down into the next point the same way (a low arch),
    /// in the queued dots' brighter look with their ring at each point. Not legs at the cruise height through the points: a point drawn
    /// 12 m up stands about 8 m away on screen from the place that was clicked (the camera looks down at 55 degrees), so it would not read
    /// as that place; on the ground each point is exactly where the player clicked, under its ring.</item>
    /// <item>STANDING (landed, no take-off held, nothing queued): as every other vehicle (the overlay's own code).</item>
    /// </list>
    /// </summary>
    public partial class SelectionOverlay
    {
        /// <summary>The route's numbers (m): the start under the ship's lowest point, the ship's plan radius (under it the line stays under
        /// the lowest point and does not rise), the flat distance of the rise after a take-off, the final slope factor (F'(1) = -k), the
        /// clearances over hard things and plants, the free stretch before the goal, the step of the clearance samples, their refresh (s),
        /// the easing of the lowest point (s), the shortest first leg drawn (over its goal: none), the climb's height per metre of its length.</summary>
        public static float RouteGapM = 0.15f, HullClearM = 4.3f, RiseM = 8f, EndSlopeK = 1f, HardClearM = 0.6f, PlantClearM = 1.0f, EndFreeM = 1.5f,
                            ClearSampleM = 1f, ClearRefreshSec = 0.25f, BellyTauSec = 0.2f, MinRouteM = 1f, RiseSlope = 0.6f, RiseBlendM = 1f;

        /// <summary>Proof (K387): the last drawn route (its curve: every sample of every leg in order) and the index of each leg's last
        /// sample; the ship's anchor (under its lowest point), the route's start, its first goal (seated); frames drawn, dots drawn, and the
        /// legs / dots of the last frame.</summary>
        public static readonly List<Vector3> RoutePts = new List<Vector3>();
        public static readonly List<int> RouteLegEnds = new List<int>();
        /// <summary>Proof (K387): the dots drawn in the last frame and the leg each belongs to (0 = the first).</summary>
        public static readonly List<Vector3> DotPtsNow = new List<Vector3>();
        public static readonly List<int> DotLegNow = new List<int>();
        public static Vector3 LastAnchor, LastStart, LastGoal;
        public static int LastRouteFrameNo = -1, RouteFrames, RouteDots, RouteLegsNow, RouteDotsNow;
        /// <summary>Proof (K387): the largest distance between the route's start and the ship's anchor (m); the largest frame-to-frame
        /// wobble of the start against the ship's own step (m); the largest distance between the first leg's end and its goal (m); the
        /// largest rise after the final leg's highest point (m, 0 = never rises); the least gap between the ship's lowest point and a route
        /// point inside its plan radius (m, under 0 = into the hull); the least margin over the clearance wanted along the way (m, under 0 =
        /// too close to something).</summary>
        public static float StartOffMaxM, StartWobbleMaxM, GoalOffMaxM, FinalRiseMaxM, HullGapMinM = float.PositiveInfinity, ClearMinM = float.PositiveInfinity;

        sealed class ClearSlot { public Vector3 goal, dir; public float len, at = -9f; public float[] need = new float[0]; }
        readonly Dictionary<long, ClearSlot> clearSlots = new Dictionary<long, ClearSlot>();
        readonly Dictionary<int, float> bellyOff = new Dictionary<int, float>();
        readonly Dictionary<int, Vector3> lastStartOf = new Dictionary<int, Vector3>(), lastRootOf = new Dictionary<int, Vector3>();
        readonly Dictionary<int, int> lastFrameOf = new Dictionary<int, int>();
        readonly List<Vector3> legBuf = new List<Vector3>(); readonly List<float> legPx = new List<float>(); readonly List<float> legReq = new List<float>();

        /// <summary>LateUpdate's waypoint path, for a shuttle: true = drawn here (in the air, a take-off held, or a Shift queue), false = it
        /// stands with nothing to show: the overlay's own code, as for every vehicle.</summary>
        bool ShuttleRoute(ZGame g, ZObject o, bool queuing)
        {
            input.Pending.TryGetValue(o, out var q);
            bool hasQ = q != null && q.Count > 0;
            bool takeOff = o.ShuttleState == ZShuttleState.Landed && o.ShuttleTakeOffHeld && o.ShuttleGoalX >= 0;
            bool air = o.ShuttleAirborne;
            if (!air && !takeOff && !hasQ) return false;
            var ship = OzPlayableShuttle.Of(o);
            if (ship && ship.BrokenUp) return true;                                                          // shot down: no route at all
            var root = ship ? ship.KickRouteRoot : g.CenterWorld(o);                                        // K436: never the rolled root of a flip (it circled the hull's middle)
            // the anchor: right under the middle of the ship's lowest drawn point (its offset from the root eased: no shake)
            float off = 0f;
            if (ship && ship.Shadow != null && ship.Shadow.DecalOn) off = ship.Shadow.LowY - root.y;       // up too: stowed legs lift the lowest point over the root
            off = BellyEase(o.Id, off);   // K419a: critically damped, no jump (SelectionOverlay.RouteEven.cs)
            var anchor = new Vector3(root.x, root.y + off, root.z);
            float gStart = Seat(g, anchor, 0f, OnDeck(anchor)).y;
            var start = new Vector3(anchor.x, Mathf.Max(anchor.y - RouteGapM, gStart + 0.1f), anchor.z);
            if (lastStartOf.TryGetValue(o.Id, out var ls) && lastRootOf.TryGetValue(o.Id, out var lr) && lastFrameOf.TryGetValue(o.Id, out int lf) && lf == Time.frameCount - 1)
                StartWobbleMaxM = Mathf.Max(StartWobbleMaxM, ((start - ls) - (root - lr)).magnitude);
            lastStartOf[o.Id] = start; lastRootOf[o.Id] = root; lastFrameOf[o.Id] = Time.frameCount;

            if (LastRouteFrameNo != Time.frameCount) { RoutePts.Clear(); RouteLegEnds.Clear(); DotPtsNow.Clear(); DotLegNow.Clear(); RouteLegsNow = 0; RouteDotsNow = 0; }
            LastRouteFrameNo = Time.frameCount; RouteFrames++;
            LastAnchor = anchor; LastStart = start;
            StartOffMaxM = Mathf.Max(StartOffMaxM, (start - anchor).magnitude);
            var sky = ship ? ship.Sky : null;
            float cruise = (float)ZWorld.ShuttleCruiseM;
            if (ShuttleQueueLegs(g, o, q, hasQ, air, takeOff, anchor, start, gStart, sky, queuing)) return true;   // K410 (DV125): two points or more: middle points in the air, level legs, only the last leg down (SelectionOverlay.ShuttleQueue.cs)
            Vector3 prev = start; float prevGround = gStart; bool fromShip = true; int leg = 0;
            if ((air || takeOff) && o.ShuttleGoalX >= 0)
            {
                var gp = Px(g, o.ShuttleGoalX, o.ShuttleGoalY, out bool gd);
                var end = gp + Vector3.up * 0.1f;
                LastGoal = gp;
                if (new Vector2(end.x - start.x, end.z - start.z).magnitude >= MinRouteM)
                {
                    // the rise: a take-off held on the ground, or climbing; it shrinks to nothing as the climb ends
                    float rise = 0f;
                    if (takeOff || o.ShuttleState == ZShuttleState.Climb)
                        rise = Mathf.Max(0f, gStart + cruise - RouteGapM - start.y) * Mathf.Clamp01(1f - (float)o.ShuttleLift);
                    int i0 = RoutePts.Count;
                    BuildLeg(g, sky, o.Id, leg++, start, rise, end, anchor.y);
                    DrawLeg(!hasQ, 1.2f, 0.8f, 0.12f, pathMat);
                    GoalOffMaxM = Mathf.Max(GoalOffMaxM, (RoutePts[RoutePts.Count - 1] - end).magnitude);
                    if (!hasQ) FinalCheck(i0);
                    fromShip = false;
                }
                // the goal's order mark: the ring every unit's target gets (its order echo, or while Shift-queuing)
                float mAge = Time.unscaledTime - input.MarkerTime, markLife = OpenZUnitOverlay.OrderMarkLife;
                if (queuing || (mAge >= 0f && mAge < markLife))
                {
                    float ms = queuing ? 0.45f : 0.45f * (mAge < 0.12f ? Mathf.Lerp(0.6f, 1.3f, mAge / 0.12f) : Mathf.Lerp(1.3f, 1.05f, (mAge - 0.12f) / (markLife - 0.12f)));
                    Draw(ring, gp + Vector3.up * 0.08f, ms, pathMat);
                }
                if (!fromShip) { prev = end; prevGround = gp.y; }
            }
            if (hasQ)
                for (int k = 0; k < q.Count; k++)
                {
                    var p = Px(g, q[k].x, q[k].y, out bool d);
                    var end = p + Vector3.up * 0.12f;
                    float rise = Mathf.Max(0f, prevGround + cruise - RouteGapM - prev.y);
                    if (fromShip && air && o.ShuttleState != ZShuttleState.Climb) rise = 0f;                // in the air already: straight from the ship
                    else if (fromShip && air) rise *= Mathf.Clamp01(1f - (float)o.ShuttleLift);
                    if (new Vector2(end.x - prev.x, end.z - prev.z).magnitude >= MinRouteM)
                    {
                        int i0 = RoutePts.Count;
                        BuildLeg(g, sky, o.Id, leg++, prev, rise, end, fromShip ? anchor.y : float.NaN);
                        DrawLeg(false, 0.9f, 0.6f, 0.16f, markerMat);
                        if (k == q.Count - 1) FinalCheck(i0);
                    }
                    Draw(ring, p + Vector3.up * 0.1f, 0.7f, markerMat);
                    prev = end; prevGround = p.y; fromShip = false;
                }
            return true;
        }

        /// <summary>F(u): 1 at the start (level), 0 at the goal, F'(1) = -k (a gentle angle into the goal); never rising for k in 0..2.</summary>
        public static float Glide(float u, float k) { u = Mathf.Clamp01(u); return 1f - 3f * u * u + 2f * u * u * u - k * (u * u * u - u * u); }

        /// <summary>One leg's curve into legBuf (and RoutePts): from s (in the air or on the ground) to e (on the drawn ground + its lift);
        /// rise = how far it climbs first (after the ship's plan radius); shipLowY = the ship's lowest point when the leg starts at the ship
        /// (NaN: at a queued point on the ground); the clearance over what stands on the way (cached per leg).</summary>
        void BuildLeg(ZGame g, OzShuttleSkyTop sky, int id, int leg, Vector3 s, float rise, Vector3 e, float shipLowY)
        {
            legBuf.Clear(); legReq.Clear();
            bool underShip = !float.IsNaN(shipLowY);
            var h = new Vector3(e.x - s.x, 0f, e.z - s.z); float L = h.magnitude;
            int n = Mathf.Clamp(Mathf.CeilToInt(L / 0.25f), 8, 480);
            // a climb first (a take-off, or out of a queued point on the ground): after the ship's plan when it starts at the ship (at once
            // from a queued point: the ship is not there yet), eased up over riseLen, at most RiseSlope x riseLen high (its steepest part
            // under 1.2: seen from the RTS camera a steeper climb toward the camera would turn back on screen, a hook) and at most half the
            // leg; the glide down begins where the climb ends (an arch, smooth at its top)
            float clear0 = underShip ? HullClearM : 0f;
            float riseLen = Mathf.Min(RiseM, Mathf.Max(0.5f, (L - clear0) * 0.35f));
            rise = Mathf.Min(rise, Mathf.Min(0.5f * L, RiseSlope * riseLen));
            float peak = s.y + rise, riseEnd = rise > 0f ? Mathf.Min(L, clear0 + riseLen) : 0f;
            var slot = Clearance(g, sky, id, leg, s, e, L);
            for (int i = 0; i <= n; i++)
            {
                float u = (float)i / n, sd = u * L, dGoal = L - sd;
                var p = s + h * u;
                // the glide starts where the climb ends; as the climb shrinks to nothing (the end of a take-off) the glide's start slides
                // back to the ship over the last RiseBlendM of it, so the curve never snaps when the climb ends
                float ug = rise > 0f ? Mathf.Lerp(u, Mathf.Clamp01((sd - riseEnd) / Mathf.Max(0.5f, L - riseEnd)), Mathf.Clamp01(rise / Mathf.Max(0.01f, RiseBlendM))) : u;
                float y = e.y + (peak - e.y) * Glide(ug, EndSlopeK);
                if (rise > 0f)
                {   // eased up to the arch's top over riseLen
                    float t = sd <= clear0 ? 0f : Mathf.Clamp01((sd - clear0) / riseLen);
                    y -= rise * (1f - t) * (1f - t);
                }
                // what this point must clear: under the ship's own plan and in the last stretch before the goal only the drawn ground
                float req = (dGoal < EndFreeM || (underShip && sd < HullClearM) || slot == null) ? Seat(g, p, 0.1f, false).y : Need(slot, dGoal);
                p.y = y; legBuf.Add(p); legReq.Add(req);
            }
            legBuf[n] = e;
            // the peak, then the lift: carried back from the goal (the height never rises on the way down), smoothed, carried again
            int top = 0; for (int i = 1; i <= n; i++) if (legBuf[i].y > legBuf[top].y) top = i;
            for (int pass = 0; pass < 3; pass++)
            {
                float env = float.NegativeInfinity;
                for (int i = n; i >= 0; i--)
                {
                    var p = legBuf[i];
                    if (i >= top) { env = Mathf.Max(env, legReq[i]); if (i < n) p.y = Mathf.Max(p.y, env); }
                    else p.y = Mathf.Max(p.y, legReq[i]);
                    legBuf[i] = p;
                }
                if (pass == 2) break;
                for (int i = top + 1; i < n; i++)
                {   // a three-tap average of a height that never rises never rises either
                    var p = legBuf[i]; p.y = (legBuf[i - 1].y + p.y + legBuf[i + 1].y) / 3f; legBuf[i] = p;
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
                else if (L - sd >= EndFreeM && slot != null) ClearMinM = Mathf.Min(ClearMinM, p.y - Need(slot, L - sd));
            }
            RoutePts.AddRange(legBuf); RouteLegEnds.Add(RoutePts.Count - 1); RouteLegsNow++;
        }

        /// <summary>The height a point dGoal m from the leg's end (back toward its start) must clear, between the samples.</summary>
        static float Need(ClearSlot c, float dGoal)
        {
            if (c.need.Length == 0) return float.NegativeInfinity;
            float f = Mathf.Max(0f, dGoal) / Mathf.Max(0.1f, ClearSampleM); int i = Mathf.FloorToInt(f);
            if (i >= c.need.Length - 1) return c.need[c.need.Length - 1];
            return Mathf.Lerp(c.need[i], c.need[i + 1], f - i);
        }

        /// <summary>The clearance samples of one leg, every ClearSampleM from its end back toward its start (so they stay put while the ship
        /// flies), asked again after ClearRefreshSec or when the leg's end or direction changed (null: the ship has no clearance grid).</summary>
        ClearSlot Clearance(ZGame g, OzShuttleSkyTop sky, int id, int leg, Vector3 s, Vector3 e, float L)
        {
            if (sky == null) return null;
            long key = ((long)id << 8) | (long)(leg & 0xFF);
            if (!clearSlots.TryGetValue(key, out var c)) { c = new ClearSlot(); clearSlots[key] = c; }
            var dir = new Vector3(s.x - e.x, 0f, s.z - e.z); dir = L > 1e-3f ? dir / L : Vector3.forward;
            float now = Time.unscaledTime;
            bool same = (c.goal - e).sqrMagnitude < 0.0025f && Vector3.Dot(c.dir, dir) > 0.9986f && c.len >= L;
            if (same && now - c.at < ClearRefreshSec) return c;
            int m = Mathf.CeilToInt(L / Mathf.Max(0.1f, ClearSampleM)) + 2;
            if (c.need.Length != m) c.need = new float[m];
            for (int j = 0; j < m; j++)
            {
                var p = e + dir * (j * ClearSampleM);
                sky.Query(p.x, p.z, 0.5f, true, out float ht, out float pt);
                float need = Seat(g, p, 0.1f, false).y;
                if (ht > float.MinValue) need = Mathf.Max(need, ht + HardClearM);
                if (pt > float.MinValue) need = Mathf.Max(need, pt + PlantClearM);
                c.need[j] = need;
            }
            c.goal = e; c.dir = dir; c.len = (m - 1) * ClearSampleM; c.at = now;
            return c;
        }

        /// <summary>The dots of the leg in legBuf: the game's dot, evenly spaced on screen (the screen length of `step` m of ground at the
        /// leg's end), counted from the end (a dot on it when endDot), none nearer the start than `first` m of ground; each sized by its
        /// distance to the camera against the end's.</summary>
        void DrawLeg(bool endDot, float step, float first, float size, Material mat)
        {
            if (DrawLegEven(endDot, step, first, size, mat)) return;   // K419a: every gap the same on screen, laid by the chord (SelectionOverlay.RouteEven.cs); no camera: the code below
            int n = legBuf.Count - 1; if (n < 1) return;
            var cam = Camera.main; var e = legBuf[n];
            legPx.Clear(); legPx.Add(0f);
            bool screen = cam != null;
            float dEnd = screen ? Mathf.Max(0.1f, Vector3.Distance(cam.transform.position, e)) : 1f;
            float stepPx = step;
            if (screen)
            {
                stepPx = step * Screen.height / (2f * dEnd * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad));
                for (int i = n - 1; i >= 0; i--)
                {
                    Vector3 a = cam.WorldToScreenPoint(legBuf[i]), b = cam.WorldToScreenPoint(legBuf[i + 1]);
                    if (a.z <= 0f || b.z <= 0f) { screen = false; break; }
                    legPx.Add(legPx[legPx.Count - 1] + new Vector2(a.x - b.x, a.y - b.y).magnitude);
                }
            }
            if (!screen)
            {   // no camera (or behind it): even in metres along the curve
                legPx.Clear(); legPx.Add(0f); stepPx = step;
                for (int i = n - 1; i >= 0; i--) legPx.Add(legPx[legPx.Count - 1] + Vector3.Distance(legBuf[i], legBuf[i + 1]));
            }
            if (stepPx < 0.5f) return;
            float stopAt = legPx[legPx.Count - 1] - first / step * stepPx;
            int j = 0;
            for (int k = endDot ? 0 : 1; k * stepPx <= stopAt && k < 4000; k++)
            {
                float want = k * stepPx;
                while (j < legPx.Count - 2 && legPx[j + 1] < want) j++;
                float seg = legPx[j + 1] - legPx[j], t = seg > 1e-5f ? Mathf.Clamp01((want - legPx[j]) / seg) : 0f;
                var p = Vector3.Lerp(legBuf[n - j], legBuf[n - j - 1], t);
                float sz = screen ? size * Vector3.Distance(cam.transform.position, p) / dEnd : size;
                Draw(dot, p, sz, mat); RouteDots++; RouteDotsNow++; DotPtsNow.Add(p); DotLegNow.Add(RouteLegsNow - 1);
            }
        }

        /// <summary>The final leg's check: after its highest point its height never rises (the largest rise is counted).</summary>
        static void FinalCheck(int i0)
        {
            int top = i0; for (int i = i0 + 1; i < RoutePts.Count; i++) if (RoutePts[i].y > RoutePts[top].y) top = i;
            for (int i = top + 1; i < RoutePts.Count; i++) FinalRiseMaxM = Mathf.Max(FinalRiseMaxM, RoutePts[i].y - RoutePts[i - 1].y);
        }

        /// <summary>Proof (K387): every counter back to its start.</summary>
        public static void ShuttleRouteReset()
        {
            RouteFrames = RouteDots = 0;
            StartOffMaxM = StartWobbleMaxM = GoalOffMaxM = FinalRiseMaxM = 0f;
            HullGapMinM = ClearMinM = float.PositiveInfinity;
        }
    }
}
