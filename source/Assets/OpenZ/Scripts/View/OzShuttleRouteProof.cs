using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K387 Play proof (rule DV118, view only: the route of a selected shuttle from the ship in the sky down to its goal on the ground,
    /// View/SelectionOverlay.ShuttleRoute.cs), on level 16 in Play (menu OpenZ/V1.1/Shuttle: K387 Route Proof, Editor/OzShuttleRouteMenu.cs).
    /// Counted PASS / FAIL lines and full-screen pictures from the normal RTS camera at 70 m. Set-up: the computer player's orders removed,
    /// the other teams hold their fire, 60 fps.
    /// <list type="bullet">
    /// <item>Flight 1 (morning, day clock 0.10 held): the ship is sent from P0 to A over open ground and selected; once it cruises its
    /// route shows (1 point: A), then B and C are queued as with Shift held (3 points: A, B, C; the input's own queue, put in for the overlay
    /// each frame and taken out again before the input reads it: no order is sent). Pictures K387_route1_day_70m, K387_route3_day_70m.
    /// After it lands at A: selected, nothing queued: K387_landed_70m (as every vehicle). Back to P0 unselected.</item>
    /// <item>Flight 2 (night, day clock 0.62 held): the same, K387_route1_night_70m, K387_route3_night_70m.</item>
    /// <item>R1 the route's first point is AT THE SHIP: within 0.3 m of the middle of its lowest drawn point, on every frame it cruises.</item>
    /// <item>R2 the last point is on the goal: within 0.05 m of the goal on the drawn surface (+ the dots' 0.1 m lift).</item>
    /// <item>R3 the height never rises on the final leg (after its highest point; cruising, 1 point: the highest point is its first).</item>
    /// <item>R4 never into the hull: every point under the ship's plan at least 0.1 m under its lowest point.</item>
    /// <item>R5 never through things: the margin over the clearance wanted (things + 0.6 m, plants + 1.0 m) never under 0.</item>
    /// <item>R6 no jitter: the start moves with the ship (frame to frame within 0.05 m of the ship's own step).</item>
    /// <item>R7 the dots are evenly spaced on screen (within each leg the gaps between neighbours within 25 % of their middle value).</item>
    /// <item>R8 3 points (K410, rule DV125: the ship flies the whole queue): the middle point B is drawn up in the air at the cruise height over
    /// its spot (x / z within 0.05 m), the last point C on the ground (0.05 m); the middle leg level, the last leg coming down (never rising).</item>
    /// <item>R9 night: the dots are dimmed by the b14 factor (0.40 at full night, 1 by day: the overlay's own materials).</item>
    /// <item>R10 not selected in the air: no route; R11 standing, selected, nothing queued: the overlay's vehicle code (no air route).</item>
    /// </list>
    /// <see cref="Folder"/>
    /// </summary>
    [DefaultExecutionOrder(150)]                                                                         // after the input (Update), before the overlay (205)
    public sealed class OzShuttleRouteProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_route_proof_k387");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_route_proof_k387");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleRouteProof").AddComponent<OzShuttleRouteProof>();
        }

        // level 16, open ground east of the robot factory (the K256g2 proof's line Z0): the run-up, the goal and the two queued points
        static readonly Vector2 P0o = new Vector2(118, -82), Ao = new Vector2(180, -82), B = new Vector2(171, -104), C = new Vector2(158, -106), Focus = new Vector2(160, -92);
        static Vector2 P0 = P0o, A = Ao;                                                                   // K420: landable on today's level 16 (View/OzProofLandSpot.cs)

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Team me = Team.Red; ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;
        Dictionary<ZObject, List<ZWaypoint>> queue; List<ZWaypoint> queued; bool inject, keepShown;
        System.Reflection.MethodInfo setSelectTime;
        readonly Dictionary<ZObject, int> heldFire = new Dictionary<ZObject, int>();
        // collected over both flights
        int frames, topFirst, spacingLegs, spacingBad, archLegs, archBad, endBad; float spacingWorst, endWorst, goalWorst;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            var f = typeof(PlayerInput).GetField("pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (input && f != null) queue = f.GetValue(input) as Dictionary<ZObject, List<ZWaypoint>>;
            var pr = typeof(PlayerInput).GetProperty("SelectTime"); setSelectTime = pr != null ? pr.GetSetMethod(true) : null;
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleRouteProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector2 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.y); rts.SetHeightImmediate(h); }
        static int Px(float x) => Mathf.Clamp(Mathf.FloorToInt(x * 8f / 16f), 1, 400) * 16 + 8;
        static ZWaypoint Wp(Vector2 w) => new ZWaypoint { mode = WpMode.Move, x = Px(w.x), y = Px(-w.y), playerGiven = true };
        bool MoveTo(Vector2 w) => W.SendWaypoints(me, s, new[] { Wp(w) });
        void Select(bool on) { input.Selection.Clear(); if (on) input.Selection.Add(s); }
        void SetClock(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }

        // before the overlay (205): the path kept shown (as just after a selection) and the queued points in the input's own queue; out
        // again at the frame's end (the input never sees them: nothing is sent)
        void LateUpdate()
        {
            if (keepShown && setSelectTime != null) setSelectTime.Invoke(input, new object[] { Time.unscaledTime });
            if (inject && queue != null && s != null && queued != null) queue[s] = queued;
        }
        IEnumerator Unqueue() { var eof = new WaitForEndOfFrame(); while (true) { yield return eof; if (queue != null && s != null && queue.ContainsKey(s) && queue[s] == queued) queue.Remove(s); } }

        void HoldFire()
        {
            foreach (var o in W.Objects)
            {
                if (o == null || o.Destroyed || o.Owner == me || o.Owner == Team.Neutral || o.AttackRadius <= 0 || heldFire.ContainsKey(o)) continue;
                heldFire[o] = o.AttackRadius; o.AttackRadius = 0;
            }
        }
        void OnDestroy()
        {
            inject = false; keepShown = false; if (queue != null && s != null && queue.ContainsKey(s) && queue[s] == queued) queue.Remove(s);
            foreach (var kv in heldFire) if (kv.Key != null && kv.Key.AttackRadius == 0) kv.Key.AttackRadius = kv.Value;
            heldFire.Clear();
        }

        /// <summary>The last drawn frame's route, read after the overlay drew it (a coroutine resumes before this frame's LateUpdates: the
        /// numbers are the last frame's): the first leg's highest point, the dots' screen spacing per leg, the queued legs' ends and arches.</summary>
        void ReadFrame(bool three)
        {
            var R = SelectionOverlay.RoutePts; var ends = SelectionOverlay.RouteLegEnds;
            if (SelectionOverlay.LastRouteFrameNo != Time.frameCount - 1 || R.Count < 2 || ends.Count == 0) return;
            frames++;
            int top = 0; for (int i = 1; i <= ends[0]; i++) if (R[i].y > R[top].y + 1e-4f) top = i;
            if (top == 0 || three) topFirst++;                                                            // K410: with 3 points the first leg runs level to A up in the air (R8 checks that route)
            if (s.ShuttleGoalX >= 0 && !three)
            {   // the first leg's end against the goal worked out here from the rules' goal (on the drawn surface + the dots' 0.1 m lift)
                var gw = ZGame.PxToWorld(s.ShuttleGoalX, s.ShuttleGoalY);
                goalWorst = Mathf.Max(goalWorst, (R[ends[0]] - SelectionOverlay.Seat(g, gw, 0.1f, false)).magnitude);
            }
            var cam = Camera.main;
            if (cam)
            {
                var dp = SelectionOverlay.DotPtsNow; var dl = SelectionOverlay.DotLegNow; var gaps = new List<float>();
                for (int leg = 0; leg < ends.Count; leg++)
                {
                    gaps.Clear(); Vector3 last = Vector3.zero; bool have = false;
                    for (int i = 0; i < dp.Count; i++)
                    {
                        if (dl[i] != leg) continue;
                        var sp = cam.WorldToScreenPoint(dp[i]);
                        if (have) gaps.Add(new Vector2(sp.x - last.x, sp.y - last.y).magnitude);
                        last = sp; have = true;
                    }
                    if (gaps.Count < 4) continue;
                    gaps.Sort(); float mid = gaps[gaps.Count / 2], worst = Mathf.Max(Mathf.Abs(gaps[0] / mid - 1f), Mathf.Abs(gaps[gaps.Count - 1] / mid - 1f));
                    spacingLegs++; spacingWorst = Mathf.Max(spacingWorst, worst); if (worst > 0.25f) spacingBad++;
                }
            }
            if (three && ends.Count >= 3)
            {
                Vector2[] qp = { B, C };
                for (int k = 0; k < 2; k++)
                {
                    int leg = ends.Count - 2 + k, a = ends[leg - 1] + 1, b = ends[leg];
                    // K410 (DV125): B is a middle point, drawn up in the air at the cruise height over its spot; C, the last point, on the ground
                    var want = SelectionOverlay.Seat(g, ZGame.PxToWorld(Px(qp[k].x), Px(-qp[k].y)), k == 0 ? 0f : 0.12f, false);
                    float hi = float.NegativeInfinity; for (int i = a; i <= b; i++) hi = Mathf.Max(hi, R[i].y);
                    float off; bool shape;
                    if (k == 0)
                    {
                        float up = R[b].y - want.y; off = new Vector2(R[b].x - want.x, R[b].z - want.z).magnitude;
                        if (up < (float)ZWorld.ShuttleCruiseM - 1.5f || up > (float)ZWorld.ShuttleCruiseM + 3f) off = Mathf.Max(off, 1f);
                        shape = SelectionOverlay.QueueLevelDevMaxM <= 0.3f;                                       // the middle leg level
                    }
                    else { off = (R[b] - want).magnitude; shape = hi <= R[a].y + 1e-3f; }                       // the last leg comes down, never above its start
                    endWorst = Mathf.Max(endWorst, off); if (off > 0.05f) endBad++;
                    archLegs++; if (!shape) archBad++;
                }
            }
        }

        IEnumerator Flight(bool night)
        {
            string tag = night ? "night" : "day";
            SetClock(night ? 0.62f : 0.10f);
            // the run-up: to P0 and down, not selected
            Select(false); keepShown = false; inject = false; MoveTo(P0);
            yield return WaitFor(() => s.ShuttleAirborne, 10f);
            yield return WaitFor(() => !s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Landed, 60f);
            yield return Wait(1.5f);
            // to A, selected, the path kept shown
            Select(true); keepShown = true; MoveTo(A);
            Cam(Focus, 70f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx > 0.6 * s.RealMoveSpeed, 30f);
            yield return null; yield return null;
            SelectionOverlay.ShuttleRouteReset();
            int n0 = frames;
            for (float t = 0f; t < 0.8f && s.ShuttleState == ZShuttleState.Cruise; t += Time.deltaTime) { yield return null; ReadFrame(false); }
            Shot("K387_route1_" + tag + "_70m"); yield return null; ReadFrame(false); yield return null;
            Log($"{tag}: night value {NightGrade.NightK:F2}, the dots' factor {SelectionOverlay.DotBrightness:F3}; route frames {frames - n0}, dots last frame {SelectionOverlay.RouteDotsNow}, legs {SelectionOverlay.RouteLegsNow}; goal {SelectionOverlay.LastGoal.x:F1}, {SelectionOverlay.LastGoal.z:F1}; start {SelectionOverlay.LastStart.y:F2} m (anchor {SelectionOverlay.LastAnchor.y:F2})");
            float dots = SelectionOverlay.DotBrightness;
            if (night) Check($"R9 night: the dots dimmed by the b14 factor ({dots:F3} <= 0.45, night value {NightGrade.NightK:F2})", NightGrade.NightK > 0.9f && dots <= 0.45f);
            else Check($"R9 day: the dots at full brightness ({dots:F3})", dots >= 0.999f);
            // 3 points: B and C queued as with Shift held
            queued = new List<ZWaypoint> { Wp(B), Wp(C) }; inject = true;
            for (float t = 0f; t < 0.5f && s.ShuttleState == ZShuttleState.Cruise; t += Time.deltaTime) { yield return null; ReadFrame(true); }
            Shot("K387_route3_" + tag + "_70m"); yield return null; ReadFrame(true); yield return null;
            inject = false;
            Log($"{tag}: cruising frames read {frames - n0}: start off the ship at most {SelectionOverlay.StartOffMaxM:F3} m, wobble {SelectionOverlay.StartWobbleMaxM:F3} m, last point off the goal {SelectionOverlay.GoalOffMaxM:F3} m, rise after the final peak {SelectionOverlay.FinalRiseMaxM:F4} m, gap under the ship's lowest point at least {SelectionOverlay.HullGapMinM:F2} m, margin over the clearance at least {SelectionOverlay.ClearMinM:F2} m");
            Check($"R1 {tag}: the route starts at the ship ({SelectionOverlay.StartOffMaxM:F3} m from the middle of its lowest point, <= 0.3)", SelectionOverlay.RouteFrames > 20 && SelectionOverlay.StartOffMaxM <= 0.3f);
            Check($"R2 {tag}: the first leg's last point is on the ship's goal ({goalWorst:F3} m from the goal on the drawn surface, <= 0.05)", frames > 20 && goalWorst <= 0.05f);
            Check($"R3 {tag}: the height never rises on the final leg (rise {SelectionOverlay.FinalRiseMaxM:F4} m; the first point the highest on {topFirst} of {frames} frames)", SelectionOverlay.FinalRiseMaxM <= 0.001f && topFirst >= frames - 1 && frames > 20);
            Check($"R4 {tag}: never into the hull (least gap under its lowest point {SelectionOverlay.HullGapMinM:F2} m, >= 0.1)", SelectionOverlay.HullGapMinM >= 0.1f && SelectionOverlay.HullGapMinM < 99f);
            Check($"R5 {tag}: never through things (least margin {SelectionOverlay.ClearMinM:F2} m, >= 0)", SelectionOverlay.ClearMinM >= -0.001f);
            Check($"R6 {tag}: no jitter (the start's step off the ship's own at most {SelectionOverlay.StartWobbleMaxM:F3} m, <= 0.05)", SelectionOverlay.StartWobbleMaxM <= 0.05f);
            Check($"R7 {tag}: dots evenly spaced on screen ({spacingBad} of {spacingLegs} legs off by more than 25 %, worst {spacingWorst * 100f:F0} %)", spacingLegs > 20 && spacingBad == 0);
            Check($"R8 {tag}: 3 points (K410): B up in the air over its spot, C on the ground ({endBad} of {archLegs} off, worst {endWorst:F3} m), the middle leg level and the last leg coming down ({archBad} not)", archLegs > 4 && endBad == 0 && archBad == 0);
            frames = topFirst = spacingLegs = spacingBad = archLegs = archBad = endBad = 0; spacingWorst = endWorst = goalWorst = 0f;
            // down at A: selected, nothing queued (as every vehicle); then not selected in the air
            yield return WaitFor(() => !s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Landed, 40f);
            yield return Wait(1.2f);
            int rf0 = SelectionOverlay.RouteFrames, lf = 0;
            for (float t = 0f; (t < 1f || lf <= 20) && t < 3f; t += Time.deltaTime) { yield return null; if (!s.ShuttleAirborne) lf++; }   // K419b: 21 frames at any frame rate (run 28 ran at 19 fps)
            if (!night) Shot("K387_landed_70m");
            if (lf <= 20) Log($"R11 {tag} (K419b): the ship was not standing at A after the wait: state {s.ShuttleState}, airborne {s.ShuttleAirborne}, destroyed {s.Destroyed}, hovers {s.ShuttleHovers}, why it did not land '{s.ShuttleNoLand}', strafe busy {s.ShuttleStrafeBusy}");
            Check($"R11 {tag}: standing, selected, nothing queued: the vehicle code ({SelectionOverlay.RouteFrames - rf0} air route frames of {lf} standing)", lf > 20 && SelectionOverlay.RouteFrames == rf0);
            keepShown = false; Select(false); MoveTo(P0);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise, 10f);
            int rf1 = SelectionOverlay.RouteFrames, uf = 0;
            for (float t = 0f; (t < 1f || uf <= 20) && t < 3f; t += Time.deltaTime) { yield return null; if (s.ShuttleAirborne) uf++; }   // K419b: 21 frames at any frame rate (run 28: "0 frames of 20" at 19 fps)
            Check($"R10 {tag}: not selected in the air: no route ({SelectionOverlay.RouteFrames - rf1} frames of {uf})", uf > 20 && SelectionOverlay.RouteFrames == rf1);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (queue == null || setSelectTime == null) { Log("the input's queue or its selection time was not found (PlayerInput.pending / SelectTime)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            int bots = W.Bots.Count; W.Bots.Clear(); HoldFire();
            OzProofHoldFire.Attach(gameObject, W, s, me, true, true, true, Log);                                       // K419b: units built later hold their fire too, the forts' big guns quiet, the ship cannot be shot down (run 28 lost it before the night flight)
            P0 = OzProofLandSpot.Near(W, s, P0o, 30f, Log, "P0"); A = OzProofLandSpot.Near(W, s, Ao, 30f, Log, "A");   // K420: R11 lands at A (it was 'blocked': the ship hovered)
            dn = FindFirstObjectByType<DayNight>(); if (dn) dn.running = false;
            StartCoroutine(Unqueue());
            Log($"K387 route proof (r2: from the ship in the sky down to the goal), {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; CPU removed {bots}, the other teams hold their fire ({heldFire.Count})");
            yield return Flight(false);
            yield return Flight(true);
            SetClock(0.10f);
            Finish();
        }

        void Finish()
        {
            inject = false; keepShown = false;
            Log($"K387 route proof: {passes} PASS, {fails} FAIL (expected 22 PASS)");
            Report = $"{passes} PASS, {fails} FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "route_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleRouteProof] log not written: " + e.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
