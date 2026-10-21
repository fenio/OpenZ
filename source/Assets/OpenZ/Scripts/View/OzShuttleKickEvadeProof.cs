#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K436 Play proof (menu OpenZ/V1.1/Shuttle: K436 Kickflip Evade Proof (Level 16, Play), Editor/OzShuttleKickEvadeMenu.cs): the kickflip
    /// as a real manoeuvre on Level 16. Real right-clicks through the game's own input (queued input events inside the game only, as the
    /// K344 proof does). Set-up: the CPU teams removed, morning held, 60 fps, the camera at the normal 70 m following the ship.
    ///  K1 a far double right-click: the rules' window opens with the pop (the dash), the speed rises to about x2.4 and eases back; per frame
    ///     the selection frame keeps its size and sits on the drawn hull (its centre within 12 px of the drawn hull's middle); the long whoosh
    ///     played to its end; pictures at the pop, 90, 180, 270 deg and the catch;
    ///  K2 a double right-click during the flip: ignored, no second flip;
    ///  K3 a new single right-click 70 m to the side 1 s into the flip: the flip rolls on, per frame the drawn yaw never turns back, the frame
    ///     and the route line's start never jump;
    ///  K4 a long flight over blue gun squads and Toughs (they cannot move or die), the ship selected: it flips by itself, the pilot says one
    ///     of the evasive lines (no other voice line over it);
    ///  K5 the same not selected: it flips by itself, no line ("not selected");
    ///  K6 the counts of the rules' harder target (guns' rolls and rockets in the flips).
    /// Output: <see cref="Folder"/> (k436_proof_log.txt, pictures). Test only.
    /// </summary>
    public sealed class OzShuttleKickEvadeProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_kickflip_evade_proof");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_kickflip_evade_proof");
        }
        public static void Run()
        {
            Report = "running";
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
            new GameObject("OzShuttleKickEvadeProof").AddComponent<OzShuttleKickEvadeProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Camera cam;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, shots; string dir;
        readonly List<ZObject> staged = new List<ZObject>();

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); cam = Camera.main;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleKickEvadeProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); shots++; Log($"picture {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.unscaledDeltaTime; yield return null; } }
        IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        void Mouse0(Vector2 pos, bool right)
        {
            if (Mouse.current == null) return;
            var st = new MouseState { position = pos }; if (right) st = st.WithButton(MouseButton.Right);
            InputSystem.QueueStateEvent(Mouse.current, st);
        }
        IEnumerator RightClick(Vector2 pos) { Mouse0(pos, false); yield return Frames(2); Mouse0(pos, true); yield return Frames(2); Mouse0(pos, false); yield return Frames(2); }

        bool Spot(float minM, float maxM, out Vector2Int px, out Vector2 screen)
        {
            px = default; screen = default;
            for (int ring = 0; ring < 6; ring++)
                for (int a = 0; a < 24; a++)
                {
                    float r = Mathf.Lerp(minM, maxM, ring / 5f) * 8f, t = a * Mathf.PI / 12f;
                    var q = W.ShuttleClamp(s.CenterX + (int)(Mathf.Cos(t) * r), s.CenterY + (int)(Mathf.Sin(t) * r));
                    if (!W.ShuttlePadFree(q.x, q.y, s, out _)) continue;
                    var wp = ZGame.PxToWorld(q.x, q.y); wp.y = g.GroundY(wp.x, wp.z);
                    var vp = cam.WorldToViewportPoint(wp);
                    if (vp.z <= 0f || vp.x < 0.15f || vp.x > 0.85f || vp.y < 0.25f || vp.y > 0.85f) continue;
                    var sp = cam.WorldToScreenPoint(wp);
                    px = new Vector2Int(q.x, q.y); screen = new Vector2(sp.x, sp.y); return true;
                }
            return false;
        }

        void Select(bool on) { input.Selection.Clear(); if (on) input.Selection.Add(s); }
        void CamOn() { if (!rts) return; rts.FreeLookOff(); rts.follow = null; var c = ship.transform.position; rts.focus = new Vector3(c.x, 0f, c.z); rts.SetHeightImmediate(70f); }
        IEnumerator Landed() { yield return WaitFor(() => (s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && s.ShuttleRampPhase == ZShuttleRamp.Shut) || s.ShuttleState == ZShuttleState.Hover, 60f); }
        Vector2 Scr(Vector3 w) { var p = cam.WorldToScreenPoint(w); return new Vector2(p.x, p.y); }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null || cam == null) { Log("no game (Level 16 in Play)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = s != null ? OzPlayableShuttle.Of(s) : null;
            if (s == null || !ship) { Log("no playable shuttle on this level (Level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            Log($"K436 kickflip evade proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s; rule DV129 {(ZWorld.ShuttleKickDeviation ? "on" : "OFF")}, the dash x{ZWorld.ShuttleKickPeak:F1}, hit share x{ZWorld.ShuttleKickHitMul:F2}");
            int bots = W.Bots.Count; W.Bots.Clear();
            var dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            s.MaxHealth = s.Health = 3000000;                                                                   // it must live through the staged fire
            Log($"set-up: CPU removed {bots}, day 0.10 held, the ship's hit points raised for the test");
            yield return Wait(0.5f);
            yield return Flip("k1", true, false);
            yield return Flip("k3", false, true);
            yield return Self("k4", true);
            yield return Self("k5", false);
            Log($"K6 the harder target (rules' counts since the level started): guns' rolls in flips {ZWorld.ShuttleKickShots}, hits {ZWorld.ShuttleKickShotHits}, misses made by the flip {ZWorld.ShuttleKickShotDodged}; rockets / shells fired in flips {ZWorld.ShuttleKickRockets}, aimed past {ZWorld.ShuttleKickRocketsPast}; blasts in flight rolled {ZWorld.ShuttleKickBlastRolls}, dodged {ZWorld.ShuttleKickBlastDodged}");
            Check("K6 the harder target worked in Play (at least one gun miss or rocket aimed past made by a flip)", ZWorld.ShuttleKickShotDodged + ZWorld.ShuttleKickRocketsPast + ZWorld.ShuttleKickBlastDodged > 0);
            Finish();
        }

        /// <summary>A far double right-click and the flip watched per frame; k1: pictures and a double click during it (K2); k3: a new order
        /// 70 m to the side 1 s into the flip.</summary>
        IEnumerator Flip(string tag, bool pictures, bool turn)
        {
            yield return Landed(); CamOn(); yield return Wait(0.6f);
            if (!Spot(42f, 60f, out var px, out var sp)) { Check($"{tag}: a free landing spot 42 .. 60 m away on screen", false); yield break; }
            Select(true);
            var fl = ship.Kickflip; int starts0 = fl.Starts, ign0 = fl.IgnoredInFlip, ord0 = ship.KickSimOrders, wf0 = ship.KickWhooshFull, kseq0 = s.ShuttleKickSeq;
            yield return RightClick(sp); yield return RightClick(sp + new Vector2(2f, 1f));
            if (rts) { rts.follow = ship.transform; rts.SetHeightImmediate(70f); }
            yield return WaitFor(() => fl.Starts > starts0, 25f);
            if (fl.Starts == starts0) { Check($"{tag}: the double click's flip started", false); if (rts) rts.follow = null; yield break; }
            yield return null;
            Check($"{tag}: the double click's flip popped and the rules' window opened with it (orders {ship.KickSimOrders - ord0}, the rules' flips {s.ShuttleKickSeq - kseq0}, window {s.ShuttleKickT:F2} s)", ship.KickSimOrders == ord0 + 1 && s.ShuttleKickSeq == kseq0 + 1);
            double v0 = System.Math.Max(1.0, s.ShuttleKickV0Px), peak = 0; float fw0 = -1f, fh0 = -1f, frameDev = 0f, centreMax = 0f, rootStep = 0f; int frames = 0; bool[] took = new bool[5];
            Vector2 prevHull = default, centre0 = default; bool havePrevHull = false;
            bool dbl = false, turned = false; Vector3 lastRoot = ship.KickRouteRoot; float lastYaw = ship.transform.eulerAngles.y, lastYawRate = 0f; int yawFlips = 0;
            while (fl.Phase == OzKickflipPhase.Flip || fl.Phase == OzKickflipPhase.Settle)
            {
                yield return new WaitForEndOfFrame();                                                  // the picture's pose: the frame drawn in this frame against the drawn hull
                frames++;
                peak = System.Math.Max(peak, (s.ShuttleSpeedPx + s.ShuttleKickBoostPx) / v0);
                var hs = Scr(ship.transform.TransformPoint(OzPlayableShuttle.KickPivot));                // the drawn hull's middle in this frame's picture
                float move = havePrevHull ? Vector2.Distance(hs, prevHull) : 0f; prevHull = hs; havePrevHull = true;   // the frame is laid a frame early: the hull's own step is allowed
                if (input.MeshRect(s, out var r))
                {
                    if (fw0 < 0f) { fw0 = r.width; fh0 = r.height; centre0 = r.center - hs; }
                    else
                    {
                        frameDev = Mathf.Max(frameDev, Mathf.Max(Mathf.Abs(r.width / fw0 - 1f), Mathf.Abs(r.height / fh0 - 1f)));
                        centreMax = Mathf.Max(centreMax, Vector2.Distance(r.center - hs, centre0) - move);   // against the level ship's own centre offset at the pop
                    }
                }
                var root = ship.KickRouteRoot; rootStep = Mathf.Max(rootStep, Vector3.Distance(root, lastRoot) - Mathf.Max(0.05f, (float)((s.ShuttleSpeedPx + s.ShuttleKickBoostPx) / 8.0) * Time.deltaTime * 2f)); lastRoot = root;
                bool level = ship.KickLevelPose(out _, out var lr); float yaw = (level ? lr : ship.transform.rotation).eulerAngles.y;
                float yr = Mathf.DeltaAngle(lastYaw, yaw) / Mathf.Max(1e-4f, Time.deltaTime); if (Mathf.Abs(yr) > 1f && Mathf.Abs(lastYawRate) > 1f && Mathf.Sign(yr) != Mathf.Sign(lastYawRate)) yawFlips++;
                if (Mathf.Abs(yr) > 1f) lastYawRate = yr; lastYaw = yaw;
                float ar = Mathf.Abs((float)ship.KickNow.RollDeg);
                if (pictures)
                {
                    int want = -1; float T = (float)fl.T;
                    if (!took[0] && T >= 0.25f) want = 0; else if (!took[1] && ar >= 90f) want = 1; else if (!took[2] && ar >= 180f) want = 2; else if (!took[3] && ar >= 270f) want = 3; else if (!took[4] && T >= 2.4f) want = 4;
                    if (want >= 0) { took[want] = true; Shot($"k436_{tag}_{(want == 0 ? "pop" : want == 4 ? "catch" : (want * 90) + "deg")}_70m"); }
                }
                if (pictures && !dbl && fl.T > 0.8)
                {   // K2: a double right-click during the flip
                    dbl = true; var q = sp + new Vector2(-260f, 40f);
                    yield return RightClick(q); yield return RightClick(q + new Vector2(2f, 1f));
                }
                if (turn && !turned && fl.T > 1.0 && Spot(60f, 80f, out var px2, out var sp2))
                {   // K3: a new single order to the side
                    turned = true; yield return RightClick(sp2);
                    Log($"{tag}: new order at {fl.T:F2} s into the flip to px ({px2.x}, {px2.y})");
                }
            }
            if (rts) rts.follow = null;
            Check($"{tag}: the dash: the speed peaked at x{peak:F2} of its start (want about x{ZWorld.ShuttleKickPeak:F1}) and the flip rolled to its end ({frames} frames)", peak >= ZWorld.ShuttleKickPeak - 0.15 && peak <= ZWorld.ShuttleKickPeak + 0.1 && fl.Ends >= 1);
            Check($"{tag}: the selection frame stays calm: its size changed at most {frameDev * 100f:F1} % (< 6 %), its centre stays on the drawn hull's middle (at most {centreMax:F1} px off its offset at the pop beyond the hull's own step in a frame, <= 8 px: it moves with the hull, never turned with it)", frameDev < 0.06f && centreMax <= 8f);
            Check($"{tag}: the route line's start never jumped (largest step beyond the flight's own {rootStep:F2} m <= 0.25)", rootStep <= 0.25f);
            if (pictures)
            {
                Check($"K2 a double right-click during the flip: ignored ({fl.IgnoredInFlip - ign0}), no second flip ({fl.Starts - starts0} flip)", fl.IgnoredInFlip > ign0 && fl.Starts == starts0 + 1);
                yield return WaitFor(() => !ship.KickWhooshWatching, 2f);
                Check($"{tag}: the long whoosh played to its end ({ship.KickWhooshFull - wf0})", ship.KickWhooshFull == wf0 + 1);
            }
            if (turn) Check($"K3 a new order 1 s into the flip: it rolled on and turned onto the new course without turning back (drawn yaw reversals {yawFlips})", turned && yawFlips == 0);
            yield return Landed();
        }

        /// <summary>A long flight over staged enemy fire; selected or not: the self-flip and its line.</summary>
        IEnumerator Self(string tag, bool selected)
        {
            yield return Landed(); CamOn(); yield return Wait(0.6f);
            Vector2Int goal = default; bool found = false;
            for (int a = 0; a < 36 && !found; a++)
            {
                float t = a * Mathf.PI / 18f;
                for (float m = 130f; m >= 95f && !found; m -= 5f)
                {
                    var q = W.ShuttleClamp(s.CenterX + (int)(Mathf.Cos(t) * m * 8f), s.CenterY + (int)(Mathf.Sin(t) * m * 8f));
                    if (Vector2.Distance(new Vector2(q.x, q.y), new Vector2(s.CenterX, s.CenterY)) / 8f < 90f || !W.ShuttlePadFree(q.x, q.y, s, out _)) continue;
                    goal = new Vector2Int(q.x, q.y); found = true;
                }
            }
            if (!found) { Check($"{tag}: a landable spot 95 .. 130 m away", false); yield break; }
            foreach (var o in staged) if (o != null && !o.Destroyed) { o.MaxHealth = 1; o.SetHealth(0); }
            staged.Clear();
            for (int k = 0; k < 3; k++)
            {   // guns and Toughs under the middle of the way (they cannot move or die)
                float f = 0.35f + 0.15f * k; int ux = (int)(s.CenterX + (goal.x - s.CenterX) * f) + (k == 1 ? -40 : 40), uy = (int)(s.CenterY + (goal.y - s.CenterY) * f);
                var o = W.CreateRobotGroup(k == 1 ? "tough" : "grunt", ux - 8, uy - 8, Team.Blue);
                if (o == null) continue;
                staged.Add(o); o.MoveSpeed = 0; o.MaxHealth = o.Health = 100000000;
                foreach (var m in o.Minions) if (m != null) { m.MoveSpeed = 0; m.MaxHealth = m.Health = 100000000; staged.Add(m); }
            }
            Select(selected);
            int self0 = ship.KickSelfFlips, lines0 = ship.KickEvadeLines, skips0 = ship.KickEvadeLineSkips; float t0 = Time.unscaledTime;
            W.SendWaypoints(s.Owner, s, new[] { ZWaypoint.Move(goal.x, goal.y) });
            if (rts) { rts.follow = ship.transform; rts.SetHeightImmediate(70f); }
            Log($"{tag}: a {Vector2.Distance(goal, new Vector2(s.CenterX, s.CenterY)) / 8f:F0} m flight over {staged.Count} staged blue robots, the ship {(selected ? "selected" : "NOT selected")}");
            bool pic = false;
            float tw = 0f;
            while (tw < 40f && ship.KickSelfFlips == self0) { tw += Time.unscaledDeltaTime; yield return null; }
            if (ship.KickSelfFlips > self0 && selected && !pic) { yield return Wait(0.5f); Shot($"k436_{tag}_selfflip_70m"); pic = true; }
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed || s.ShuttleState == ZShuttleState.Hover, 60f);
            if (rts) rts.follow = null;
            bool flipped = ship.KickSelfFlips > self0;
            Check($"{tag}: on the long flight under fire it flipped by itself ({ship.KickSelfFlips - self0})", flipped);
            if (selected)
            {
                int overlaps = 0; string said = "";
                var log = OzSoundLog.Since(t0);
                float lineAt = -1f;
                foreach (var e in log) if (e.key == OzPlayableShuttle.KickEvadeLine && e.outcome != null && e.outcome.StartsWith("PLAY")) { lineAt = e.t; said = e.clip; break; }
                if (lineAt >= 0f) foreach (var e in log) if (e.src == "VO" && e.key != OzPlayableShuttle.KickEvadeLine && e.outcome != null && e.outcome.StartsWith("PLAY") && e.t > lineAt - 0.2f && e.t < lineAt + 2.6f) overlaps++;
                Check($"{tag}: selected and on screen: the pilot said an evasive line ({ship.KickEvadeLines - lines0}; clip {said}), no other voice line over it ({overlaps})", ship.KickEvadeLines > lines0 && lineAt >= 0f && overlaps == 0);
            }
            else Check($"{tag}: not selected: no evasive line ({ship.KickEvadeLines - lines0} said, {ship.KickEvadeLineSkips - skips0} skipped: '{ship.KickEvadeWhy}')", ship.KickEvadeLines == lines0 && ship.KickEvadeLineSkips > skips0 && ship.KickEvadeWhy == "not selected");
            yield return Wait(11f);                                                                    // the self-flip's pause
        }

        void Finish()
        {
            foreach (var o in staged) if (o != null && !o.Destroyed) { o.MaxHealth = 1; o.SetHealth(0); }
            Application.targetFrameRate = 30;
            Log($"K436 kickflip evade proof: {passes} PASS, {fails} FAIL, {shots} pictures in {dir}");
            Report = fails == 0 && passes > 0 ? $"PASS ({passes})" : $"FAIL ({fails} of {passes + fails})";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "k436_proof_log.txt"), sb.ToString());
            Destroy(gameObject);
        }
    }
}
#endif
