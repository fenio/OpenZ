using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K410 Play proof (rule DV125: the shuttle flies the whole Shift queue; the route drawing View/SelectionOverlay.ShuttleQueue.cs), on level
    /// 16 in Play (menu OpenZ/V1.1/Shuttle: K410 Route Proof, Editor/OzShuttleQueueMenu.cs). Counted PASS / FAIL lines and full-screen pictures
    /// from the normal RTS camera at 70 m. Set-up as K387's proof: the computer player's orders removed, the other teams hold their fire, 60 fps.
    /// <list type="bullet">
    /// <item>Flight 1 (morning, day clock 0.10 held): from P0 the ship gets a 3-point route A, B, C (one SEND_WAYPOINTS, what a Shift queue
    /// sends), selected, its route kept shown. Pictures K410_route3_day_70m (on its way to A: A and B up in the air, C on the ground),
    /// K410_route3_after_A_day_70m (A passed: B in the air, C), K410_route_landed_day_70m (down at C).</item>
    /// <item>Flight 2 (night, day clock 0.62 held): back from C through B2 and A2 to P0, K410_route3_night_70m.</item>
    /// <item>Flight 3 (day): a one-point order to D, then a Shift point E added while it flies (the click's own append,
    /// View/OzShuttleQueueInput.cs): it flies through D in the air and lands at E. Picture K410_shift_append_day_70m (the Shift point shown
    /// as it is added: D in the air, E on the ground).</item>
    /// <item>Q1 every middle point flown through in the air: the world's pass list (ZWorld.ShuttlePasses) has each point, within 3.5 m, at the
    /// cruise height (lift 1), cruising.</item>
    /// <item>Q2 no descent, landing or hover before the last point; exactly one landing per route, on its last point.</item>
    /// <item>Q3 the drawing, every frame it shows: each middle point drawn in the air over its ground spot (its x / z within 0.05 m, its height
    /// over the drawn ground between the cruise height - 1.5 m and + 3 m (a tree under it lifts it)), a ring under each; the legs between them
    /// level (off the straight line at most 0.3 m where nothing lifted them).</item>
    /// <item>Q4 the last leg: it ends on the last point on the ground (0.05 m) and never rises on the way down (K387's counter).</item>
    /// <item>Q5 the landing mark's landing look only at the last point: the ship comes down once per route, at its last point.</item>
    /// <item>Q6 the Shift point added in flight: the list sent = the route still to fly + the new point; D flown through in the air, landed at E.</item>
    /// </list>
    /// <see cref="Folder"/>
    /// </summary>
    [DefaultExecutionOrder(150)]                                                                         // after the input (Update), before the overlay (205)
    public sealed class OzShuttleQueueProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_queue_proof_k410");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_queue_proof_k410");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleQueueProof").AddComponent<OzShuttleQueueProof>();
        }

        // level 16, open ground east of the robot factory (K387's proof ground; P0 and C are K387's landing spots): the run-up, the day route
        // (A, B in the air, landing at C), the night route back (B2, A2 in the air, landing at P0), the one-point order D (= C) and the Shift
        // point E (= P0)
        static readonly Vector2 A = new Vector2(150, -78), B = new Vector2(171, -104), B2 = new Vector2(158, -106), A2 = new Vector2(140, -95), Focus = new Vector2(150, -92),
                                P0o = new Vector2(118, -82), Co = new Vector2(180, -82);
        // K420: the landing points as the run found them landable (View/OzProofLandSpot.cs: P0 lies over water, C is 'blocked' on today's level 16)
        static Vector2 P0 = P0o, C = Co, D = Co, E = P0o;

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Team me = Team.Red; ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;
        Dictionary<ZObject, List<ZWaypoint>> queue; List<ZWaypoint> queued; bool inject, keepShown;
        System.Reflection.MethodInfo setSelectTime;
        readonly Dictionary<ZObject, int> heldFire = new Dictionary<ZObject, int>();
        // collected per flight
        int frames, midBad, ringBad, lastBad, mids; float midWorstXZ, midLow = 99f, midHigh = -99f, lastWorst;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            var f = typeof(PlayerInput).GetField("pending", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (input && f != null) queue = f.GetValue(input) as Dictionary<ZObject, List<ZWaypoint>>;
            var pr = typeof(PlayerInput).GetProperty("SelectTime"); setSelectTime = pr != null ? pr.GetSetMethod(true) : null;
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleQueueProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector2 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.y); rts.SetHeightImmediate(h); }
        static int Px(float x) => Mathf.Clamp(Mathf.FloorToInt(x * 8f / 16f), 1, 400) * 16 + 8;
        static ZWaypoint Wp(Vector2 w) => new ZWaypoint { mode = WpMode.Move, x = Px(w.x), y = Px(-w.y), playerGiven = true };
        bool Send(params Vector2[] pts) { var l = new List<ZWaypoint>(); foreach (var p in pts) l.Add(Wp(p)); return W.SendWaypoints(me, s, l); }
        void Select(bool on) { input.Selection.Clear(); if (on) input.Selection.Add(s); }
        void SetClock(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }
        bool AtPoint(Vector2 p) => Mathf.Abs((float)s.ShuttleBaseX - Px(p.x)) <= 2f && Mathf.Abs((float)s.ShuttleBaseY - Px(-p.y)) <= 2f;
        bool Down => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0;

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

        /// <summary>The last drawn frame's route (read after the overlay drew it: a coroutine resumes before this frame's LateUpdates).</summary>
        void ReadFrame()
        {
            if (SelectionOverlay.LastQueueFrameNo != Time.frameCount - 1) return;
            frames++;
            var air = SelectionOverlay.QueueAirPts; var gnd = SelectionOverlay.QueueGroundPts;
            float cruise = (float)ZWorld.ShuttleCruiseM;
            for (int i = 0; i < air.Count && i < gnd.Count; i++)
            {
                mids++;
                float xz = new Vector2(air[i].x - gnd[i].x, air[i].z - gnd[i].z).magnitude, hgt = air[i].y - gnd[i].y;
                midWorstXZ = Mathf.Max(midWorstXZ, xz); midLow = Mathf.Min(midLow, hgt); midHigh = Mathf.Max(midHigh, hgt);
                if (xz > 0.05f || hgt < cruise - 1.5f || hgt > cruise + 3f) midBad++;
            }
            if (SelectionOverlay.QueueRingsNow != air.Count) ringBad++;
            if (s.ShuttleGoalX >= 0 && !(queue != null && queue.ContainsKey(s)))
            {   // the last point against the rules' goal (on the drawn surface + the dots' 0.1 m lift)
                var want = SelectionOverlay.Seat(g, ZGame.PxToWorld(s.ShuttleGoalX, s.ShuttleGoalY), 0.1f, false);
                float off = (SelectionOverlay.QueueLastPt - want).magnitude; lastWorst = Mathf.Max(lastWorst, off); if (off > 0.05f) lastBad++;
            }
        }

        void ResetFrame() { frames = midBad = ringBad = lastBad = mids = 0; midWorstXZ = 0f; midLow = 99f; midHigh = -99f; lastWorst = 0f; SelectionOverlay.ShuttleQueueReset(); SelectionOverlay.ShuttleRouteReset(); }

        IEnumerator RunUp(Vector2 p)
        {
            Select(false); keepShown = false; inject = false;
            if (AtPoint(p) && Down) yield break;
            Send(p);
            yield return WaitFor(() => s.ShuttleAirborne, 10f);
            yield return WaitFor(() => Down, 60f);
            yield return Wait(1.5f);
        }

        /// <summary>One 3-point route from where it stands (a, b, c): the counted checks and the pictures.</summary>
        IEnumerator Route(string tag, Vector2 a, Vector2 b, Vector2 c, bool night)
        {
            SetClock(night ? 0.62f : 0.10f);
            int pass0 = W.ShuttlePassSeq, land0 = s.ShuttleLandings, descents = 0, early = 0; bool wasDescend = false; Vector2Int descendAt = new Vector2Int(-1, -1);
            Select(true); keepShown = true; Cam(Focus, 70f);
            bool sent = Send(a, b, c);
            Log($"{tag}: route sent ({sent}): middle points {s.ShuttleViaCount}, goal {s.ShuttleGoalX / 8f:F1},{s.ShuttleGoalY / 8f:F1} m");
            ResetFrame();
            bool shot1 = false, shot2 = false; float t = 0f;
            while (t < 90f && !(Down && s.ShuttleLandings > land0))
            {
                yield return null; t += Time.deltaTime; ReadFrame();
                bool desc = s.ShuttleState == ZShuttleState.Descend;
                if (desc && !wasDescend) { descents++; descendAt = new Vector2Int((int)s.ShuttleBaseX, (int)s.ShuttleBaseY); }
                wasDescend = desc;
                if (s.ShuttleViaCount > 0 && s.ShuttleAirborne && (desc || s.ShuttleState == ZShuttleState.Hover || s.ShuttleLandTurning)) early++;
                if (!shot1 && s.ShuttleState == ZShuttleState.Cruise && s.ShuttleViaCount == 2 && s.ShuttleSpeedPx > 0.6 * s.RealMoveSpeed) { yield return null; Shot("K410_route3_" + tag + "_70m"); shot1 = true; }
                if (!shot2 && !night && s.ShuttleViaCount == 1 && s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx > 0.5 * s.RealMoveSpeed) { yield return null; Shot("K410_route3_after_A_" + tag + "_70m"); shot2 = true; }
            }
            // the passes the world listed for this flight
            int n = 0, nearOk = 0, liftOk = 0, cruiseOk = 0; float worst = 0f;
            foreach (var p in W.ShuttlePasses)
            {
                if (p.seq <= pass0 || p.id != s.Id) continue;
                n++; float d = (float)p.distPx / 8f; worst = Mathf.Max(worst, d);
                if (d <= 3.5f) nearOk++; if (p.lift >= 0.999) liftOk++; if (p.state == ZShuttleState.Cruise) cruiseOk++;
                Log($"{tag}: pass {p.index} at {p.x / 8f:F1},{p.y / 8f:F1} m: {d:F2} m from it, turn {p.turnDeg:F0} deg, {p.speedPx / 8f:F2} m/s, lift {p.lift:F2}, {p.state}");
            }
            Check($"Q1 {tag}: both middle points flown through in the air ({n} passes, {nearOk} within 3.5 m (worst {worst:F2} m), lift 1 at {liftOk}, cruising at {cruiseOk})", n == 2 && nearOk == 2 && liftOk == 2 && cruiseOk == 2);
            bool atC = AtPoint(c) && Down;
            Check($"Q2 {tag}: no descent, landing or hover before the last point ({early} frames); one landing ({s.ShuttleLandings - land0}), on the last point ({atC})", early == 0 && s.ShuttleLandings - land0 == 1 && atC);
            Log($"{tag}: drawn frames {frames} (queue frames {SelectionOverlay.QueueFrames}); middle points drawn {mids}: x/z off their spot at most {midWorstXZ:F3} m, height over the ground {midLow:F2}..{midHigh:F2} m; level legs off the straight line at most {SelectionOverlay.QueueLevelDevMaxM:F3} m; last point off the goal at most {lastWorst:F3} m; rise after the final peak {SelectionOverlay.FinalRiseMaxM:F4} m");
            Check($"Q3 {tag}: middle points drawn in the air over their spots ({midBad} of {mids} off: x/z <= 0.05 m, height cruise -1.5..+3 m), a ring under each ({ringBad} frames short), legs level (<= 0.3 m: {SelectionOverlay.QueueLevelDevMaxM:F3})",
                  frames > 30 && mids > 30 && midBad == 0 && ringBad == 0 && SelectionOverlay.QueueLevelDevMaxM <= 0.3f);
            Check($"Q4 {tag}: the last leg ends on the last point on the ground ({lastBad} frames off, worst {lastWorst:F3} m) and never rises on the way down ({SelectionOverlay.FinalRiseMaxM:F4} m)", frames > 30 && lastBad == 0 && SelectionOverlay.FinalRiseMaxM <= 0.001f);
            bool descC = descendAt.x >= 0 && Mathf.Abs(descendAt.x - Px(c.x)) <= 2 && Mathf.Abs(descendAt.y - Px(-c.y)) <= 2;
            Check($"Q5 {tag}: the landing look of the mark only at the last point: one descent ({descents}), over the last point ({descC})", descents == 1 && descC);
            yield return Wait(1.0f);
            if (!night) Shot("K410_route_landed_" + tag + "_70m");
            keepShown = false;
        }

        IEnumerator ShiftAppend()
        {
            SetClock(0.10f);
            int pass0 = W.ShuttlePassSeq, land0 = s.ShuttleLandings;
            Select(true); keepShown = true; Cam(Focus, 70f);
            Send(D);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx > 0.6 * s.RealMoveSpeed, 30f);
            // the Shift point shown as it is added (the input's own queue), then sent the way the click sends it
            queued = new List<ZWaypoint> { Wp(E) }; inject = true;
            for (int i = 0; i < 20; i++) { yield return null; ReadFrame(); }
            Shot("K410_shift_append_day_70m"); yield return null;
            inject = false; yield return null;
            var pend = new Dictionary<ZObject, List<ZWaypoint>> { [s] = new List<ZWaypoint> { Wp(E) } };
            Log($"Q6 at the Shift click (K420): state {s.ShuttleState}, flying on to its goal {OzShuttleQueueInput.Flying(s)}, middle points {s.ShuttleViaCount}, goal {s.ShuttleGoalX / 8f:F1},{s.ShuttleGoalY / 8f:F1} m, {Mathf.Sqrt((float)((s.ShuttleGoalX - s.ShuttleBaseX) * (s.ShuttleGoalX - s.ShuttleBaseX) + (s.ShuttleGoalY - s.ShuttleBaseY) * (s.ShuttleGoalY - s.ShuttleBaseY))) / 8f:F1} m to go");
            OzShuttleQueueInput.Click(true, true); OzShuttleQueueInput.Prefix(W, pend);
            var list = pend[s];
            bool prefixed = list.Count == 2 && list[0].x == s.ShuttleGoalX && list[0].y == s.ShuttleGoalY && list[1].x == Px(E.x) && list[1].y == Px(-E.y);
            W.SendWaypoints(me, s, list);
            bool took = s.ShuttleViaCount == 1 && s.ShuttleGoalX == Px(E.x) && s.ShuttleGoalY == Px(-E.y);
            int early = 0;
            float t = 0f;
            while (t < 60f && !(Down && s.ShuttleLandings > land0)) { yield return null; t += Time.deltaTime; if (s.ShuttleViaCount > 0 && (s.ShuttleState == ZShuttleState.Descend || s.ShuttleState == ZShuttleState.Hover)) early++; }
            int n = 0; float worst = 0f; foreach (var p in W.ShuttlePasses) if (p.seq > pass0 && p.id == s.Id) { n++; worst = Mathf.Max(worst, (float)p.distPx / 8f); }
            Check($"Q6 the Shift point added while it flies to D: the list sent = the route still to fly + E ({prefixed}), taken (D a middle point, E the goal: {took}); D flown through in the air ({n}, {worst:F2} m), no early descent ({early}), landed once at E ({AtPoint(E) && Down})",
                  prefixed && took && n == 1 && worst <= 3.5f && early == 0 && s.ShuttleLandings - land0 == 1 && AtPoint(E) && Down);
            keepShown = false; Select(false);
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (queue == null || setSelectTime == null) { Log("the input's queue or its selection time was not found (PlayerInput.pending / SelectTime)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttleQueueDeviation) { Log("the route rule DV125 is off (ZWorld.ShuttleQueueDeviation)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            int bots = W.Bots.Count; W.Bots.Clear(); HoldFire();
            OzProofHoldFire.Attach(gameObject, W, s, me, true, true, true, Log);                                       // K420: units built later hold their fire too, the ship cannot be shot down
            P0 = OzProofLandSpot.Near(W, s, P0o, 30f, Log, "P0 (= E)"); C = OzProofLandSpot.Near(W, s, Co, 30f, Log, "C (= D)"); D = C; E = P0;   // K420
            dn = FindFirstObjectByType<DayNight>(); if (dn) dn.running = false;
            StartCoroutine(Unqueue());
            Log($"K410 route proof (DV125: the whole Shift queue, middle points in the air), {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; CPU removed {bots}, the other teams hold their fire ({heldFire.Count})");
            yield return RunUp(P0);
            yield return Route("day", A, B, C, false);
            yield return Route("night", B2, A2, P0, true);
            yield return RunUp(P0);
            yield return ShiftAppend();
            SetClock(0.10f);
            Finish();
        }

        void Finish()
        {
            inject = false; keepShown = false;
            Log($"K410 route proof: {passes} PASS, {fails} FAIL (expected 11 PASS)");
            Report = $"{passes} PASS, {fails} FAIL";
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "queue_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleQueueProof] log not written: " + e.Message); }
            Destroy(gameObject, 1f);
        }
    }
}
