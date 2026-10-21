using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K256g Play proof: the shuttle's touches (rule DV118; owner items 334 + 336), on level 16 in Play (menu OpenZ/V1.1/Shuttle: K256g
    /// Touches Proof, Editor/OzShuttleTouchesMenu.cs). Counted PASS / FAIL lines and full-screen pictures from the normal RTS camera (70 m, and
    /// close for the springs), uncropped. Set-up: the CPU teams removed, morning (day clock 0.10, held), 60 fps. The game's pointer is moved
    /// and its right button pressed by queued input events inside the game only (as the testing panel's own self-test does): no desktop
    /// input. Stages:
    ///  T0 the Options row SHUTTLE ORDERS: its tip, its tab (ADVANCED), its default (LANDABLE SPOTS ONLY), both values;
    ///  T1 a take-off: the ship's own shadow eases out and the landing mark in (0.5 s), no own shadow from 2.5 m up, never both full;
    ///     the cruise: the mark's frame-to-frame jitter in px at the camera (the ship's own for comparison), its strength (faint, fainter
    ///     fast); the right button held over the water: the mark turns red; let go: no order (the blip), it flies on;
    ///  T2 a normal landing at 70 m and one close: the legs' springs (the dip, the rebound, the spread, settled, the pads on the ground, the
    ///     creak), the ship's own shadow eases in while the mark eases out; pictures at the touchdown, the deepest dip, the rebound's top and
    ///  T3 landed and selected: the cursor over water, over rocks (NO) and over free ground (move); a right-click on the water: no order,
    ///     the blip; a mixed group (the ship + a grunt squad) at a place only the ship cannot land: the squad goes, the ship stays; ANYWHERE:
    ///     the move cursor over the water and the click flies it there (it hovers); the option put back;
    ///  T4 a crash landing (the rules' proof switch): the light spring the moment it stands level.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_touches_proof, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleTouchesProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_touches_proof");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_touches_proof");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleTouchesProof").AddComponent<OzShuttleTouchesProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; OpenZHud hud; OpenZCursors cursors; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir; int optWas;
        // per frame: the cross-fade of the ship's own shadow and the mark
        bool watch; float fadeT, firstOff = -1f, lastOn = -1f, firstIn = -1f, fullIn = -1f; bool wasAir;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            hud = OpenZHud.I; cursors = FindFirstObjectByType<OpenZCursors>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            optWas = OzShuttleOrdersOption.Setting;
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleTouchesProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        Vector3 Wp(int x, int y) => ZGame.PxToWorld(x, y);
        Camera CamNow => rts ? rts.GetComponent<Camera>() : Camera.main;
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP"))) n++; return n; }
        float PxPerM(Vector3 at) { var c = CamNow; if (!c) return 0f; var a = c.WorldToScreenPoint(at); var b = c.WorldToScreenPoint(at + c.transform.right); return new Vector2(b.x - a.x, b.y - a.y).magnitude; }

        // ------------------------------------------------------------------ the game's own pointer (queued input events inside the game)
        void PointAt(Vector2 p) { if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = p }); }
        void Press(Vector2 p, bool right) { if (Mouse.current != null) InputSystem.QueueStateEvent(Mouse.current, new MouseState { position = p }.WithButton(MouseButton.Right, right)); }
        IEnumerator CursorAt(Vector2 p, System.Action<string> got) { PointAt(p); yield return null; yield return null; PointAt(p); yield return Wait(0.3f); got(cursors ? cursors.Current : "?"); }
        IEnumerator RightClick(Vector2 p, float hold = 0f) { PointAt(p); yield return null; yield return null; Press(p, true); yield return null; if (hold > 0f) yield return Wait(hold); Press(p, false); yield return null; yield return null; }
        Vector2 ScreenAt(int x, int y) { var c = CamNow; var w = Wp(x, y); w.y = g.GroundY(w.x, w.z); var sp = c ? c.WorldToScreenPoint(w) : Vector3.zero; return new Vector2(sp.x, sp.y); }
        bool OnScreen(Vector2 p) => p.x > UnityEngine.Screen.width * 0.15f && p.x < UnityEngine.Screen.width * 0.85f && p.y > UnityEngine.Screen.height * 0.22f && p.y < UnityEngine.Screen.height * 0.85f;

        // ------------------------------------------------------------------ places
        /// <summary>A tile middle where the ship can never land for `why` (or can land: why ""), between minR and maxR px from (cx, cy).</summary>
        int[] Place(string why, int cx, int cy, int minR, int maxR, System.Func<int, int, bool> also = null)
        {
            long best = long.MaxValue; int[] r = null; int e = ZWorld.ShuttleEdgeTiles;
            for (int ty = e; ty < W.Map.height - e; ty++)
                for (int tx = e; tx < W.Map.width - e; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best || d < (long)minR * minR || d > (long)maxR * maxR) continue;
                    if (also != null && !also(tx, ty)) continue;
                    bool never = W.ShuttleNeverLands(x, y, s, out string wy);
                    if (why == "" ? (!never && W.ShuttlePadFree(x, y, s, out _)) : (never && wy == why)) { best = d; r = new[] { x, y }; }
                }
            return r;
        }

        void Update()
        {
            if (!watch || s == null || !ship) return;
            float k = ship.RealShadow != null ? ship.RealShadow.K : 1f; bool air = s.ShuttleAirborne;
            fadeT += Time.deltaTime;
            if (air && !wasAir) { fadeT = 0f; firstOff = -1f; lastOn = -1f; }
            if (!air && wasAir) { fadeT = 0f; firstIn = -1f; fullIn = -1f; }
            if (air) { if (k < 0.999f && firstOff < 0f) firstOff = fadeT; if (k > 0.001f) lastOn = fadeT; }
            else { if (k > 0.001f && firstIn < 0f) firstIn = fadeT; if (k >= 0.999f && fullIn < 0f) fullIn = fadeT; }
            wasAir = air;
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || hud == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation || W.PlayableShuttle == null) { Log("no playable shuttle (level 16, the shuttle rule on, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            me = g.player; s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s);
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            Log($"K256g touches proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {UnityEngine.Screen.width}x{UnityEngine.Screen.height}, world time {W.Time:F1} s; the option SHUTTLE ORDERS was {OzShuttleOrdersOption.Name}");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held; the ship #{s.Id} {s.ShuttleState} at {s.CenterX},{s.CenterY}; its own shadow: the soft fade {(ship.RealShadow == null || ship.RealShadow.Soft ? "there" : "MISSING (switches at the middle)")}");
            OzShuttleOrdersOption.Setting = OzShuttleOrdersOption.LandableSpotsOnly;
            watch = true; wasAir = s.ShuttleAirborne;
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 30f); yield return Wait(0.8f);
            yield return T0(); yield return T1(); yield return T2(70f, "h70"); yield return T2(18f, "close"); yield return T3(); yield return T4();
            Finish();
        }

        void Finish()
        {
            watch = false;
            OzShuttleOrdersOption.Setting = optWas;                                                                // the player's own setting back
            Log($"K256g touches proof: {passes} PASS, {fails} FAIL, {frames} pictures (the option SHUTTLE ORDERS back to {OzShuttleOrdersOption.Name})");
            Report = fails == 0 ? $"PASS {passes}" : $"FAIL {fails} of {passes + fails}";
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "touches_proof_log.txt"), sb.ToString());
            Destroy(gameObject, 0.5f);
        }

        // ------------------------------------------------------------------ T0: the Options row
        IEnumerator T0()
        {
            string tip = OzSettingTips.For(OzShuttleOrdersOption.Label);
            int tab = OzSettingTips.TabOf(OzShuttleOrdersOption.Label);
            bool def = !PlayerPrefs.HasKey(OzShuttleOrdersOption.Pref) || optWas == OzShuttleOrdersOption.LandableSpotsOnly;
            OzShuttleOrdersOption.Step(1); string n1 = OzShuttleOrdersOption.Name; bool any = !OzShuttleOrdersOption.LandableOnly;
            OzShuttleOrdersOption.Step(1); string n0 = OzShuttleOrdersOption.Name; bool land = OzShuttleOrdersOption.LandableOnly;
            Log($"T0 the row {OzShuttleOrdersOption.Label}: tab {OzSettingTips.TabNames[tab]}, values {n0} / {n1}, saved as {OzShuttleOrdersOption.Pref} (the player's own value was {OzShuttleOrdersOption.Names[optWas]}); tip: {tip}");
            Check("T0 the Options row SHUTTLE ORDERS has its tip and stands on the ADVANCED tab (after SHUTTLE: both are built last there)", tip != null && tab == OzSettingTips.TabAdvanced);
            Check("T0 its two values flip at once (LANDABLE SPOTS ONLY / ANYWHERE, read live) and LANDABLE SPOTS ONLY is the default", any && land && n0 == "LANDABLE SPOTS ONLY" && n1 == "ANYWHERE" && def);
            yield return null;
        }

        // ------------------------------------------------------------------ T1: the take-off, the cruise, the red aim
        IEnumerator T1()
        {
            Log("T1 the take-off (the ship's own shadow out, the mark in), the cruise (the mark: jitter and strength), the red aim over the water");
            var far = Place("", s.CenterX, s.CenterY, 560, 900); var wat = Place("water", s.CenterX, s.CenterY, 200, 1400);
            if (far == null) { Check("T1 a landing place 70 .. 110 m away", false); yield break; }
            Select(s); Cam(ship.transform.position, 70f); yield return Wait(0.8f);
            int air0 = OzPlayableShuttle.AirOwnShadowFrames, two0 = OzPlayableShuttle.TwoShadowFrames;
            Check("T1 the move order is taken", Move(far[0], far[1]));
            yield return WaitFor(() => s.ShuttleAirborne, 10f);
            float t = 0f; int shot = 0;
            while (t < 1.2f) { if (shot < 3 && t >= shot * 0.25f) { Shot($"t1_takeoff_{shot:00}_h70"); shot++; } t += Time.deltaTime; yield return null; }
            float rk = ship.RealShadow != null ? ship.RealShadow.K : -1f;
            Log($"take-off: the own shadow first under 1 at {firstOff:F2} s, last over 0 at {lastOn:F2} s after it left the ground; now {rk:F2}, the mark's weight {ship.Shadow.MarkWeight:F2}");
            Check($"T1 at the take-off the ship's own shadow eases out over about 0.5 s (gone 0.4 .. 0.65 s after it leaves the ground; it starts within 0.10 s + one frame = {0.1f + Mathf.Clamp(Time.smoothDeltaTime, 1f / 60f, 1f / 30f):F3} s) while the mark eases in", firstOff >= 0f && firstOff < 0.1f + Mathf.Clamp(Time.smoothDeltaTime, 1f / 60f, 1f / 30f) && lastOn >= 0.35f && lastOn <= 0.65f && rk == 0f && ship.Shadow.MarkWeight > 0.99f);
            // the cruise: the mark's jitter and strength
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise && s.ShuttleSpeedPx >= 0.85 * s.RealMoveSpeed && ship.FeetHeight >= 8f, 25f);
            OzShuttleShadow.JitterMaxM = 0f; OzShuttleShadow.JitterSum2 = 0f; OzShuttleShadow.ShipJitterMaxM = 0f; OzShuttleShadow.ShipJitterSum2 = 0f; OzShuttleShadow.JitterFrames = 0;
            float strLo = 9f, strHi = 0f; t = 0f; bool shotCruise = false;
            while (t < 4f && s.ShuttleState == ZShuttleState.Cruise)
            {
                Cam(ship.transform.position, 70f);
                strLo = Mathf.Min(strLo, ship.Shadow.StrengthNow); strHi = Mathf.Max(strHi, ship.Shadow.StrengthNow);
                if (!shotCruise && t > 1.5f) { shotCruise = true; Shot("t1_cruise_faint_mark_no_sun_shadow_h70"); }
                t += Time.deltaTime; yield return null;
            }
            int n = Mathf.Max(1, OzShuttleShadow.JitterFrames); float ppm = PxPerM(ship.Shadow.Centre);
            float jRms = Mathf.Sqrt(OzShuttleShadow.JitterSum2 / n) * ppm, jMax = OzShuttleShadow.JitterMaxM * ppm, sRms = Mathf.Sqrt(OzShuttleShadow.ShipJitterSum2 / n) * ppm, sMax = OzShuttleShadow.ShipJitterMaxM * ppm;
            Log($"cruise {t:F1} s ({OzShuttleShadow.JitterFrames} frames, {ppm:F1} px per m at the mark): the mark's frame-to-frame jitter rms {jRms:F2} px, worst {jMax:F2} px; the ship's own ground point rms {sRms:F2} px, worst {sMax:F2} px; strength {strLo:F3} .. {strHi:F3}; cells sampled {OzShuttleShadow.CellsSampled}");
            Check("T1 the mark glides with the ship: frame-to-frame jitter rms <= 0.3 px and worst <= 1.5 px at the camera (round 3 stepped 0.25 m = 5-7 px)", OzShuttleShadow.JitterFrames > 60 && jRms <= 0.3f && jMax <= 1.5f);
            Check("T1 the mark reads at cruise speed by day (strength 0.24 .. 0.45: a soft disc, selected a little stronger; K256g's faint 0.03 .. 0.10 could not be seen)", strHi <= 0.45f && strLo >= 0.24f);
            Check("T1 no own shadow while it flies 2.5 m or more up, never both full", OzPlayableShuttle.AirOwnShadowFrames == air0 && OzPlayableShuttle.TwoShadowFrames == two0);
            // the right button held over the water: red; let go: no order
            if (wat != null)
            {
                var wp = ScreenAt(wat[0], wat[1]);
                if (!OnScreen(wp)) { Cam((ship.transform.position + Wp(wat[0], wat[1])) * 0.5f, 70f); yield return null; yield return null; wp = ScreenAt(wat[0], wat[1]); }
                int goalX = s.ShuttleGoalX, goalY = s.ShuttleGoalY, ref0 = OzShuttleOrderAim.RefusedClicks; float t0 = Time.unscaledTime; float redMax = 0f;
                PointAt(wp); yield return null; yield return null; Press(wp, true);
                float h = 0f; while (h < 0.8f) { redMax = Mathf.Max(redMax, ship.Shadow.RedNow); h += Time.deltaTime; yield return null; }
                Shot("t1_aim_over_water_red_mark_h70");
                Press(wp, false); yield return null; yield return null; yield return null;
                bool kept = s.ShuttleGoalX == goalX && s.ShuttleGoalY == goalY;
                Log($"the right button held 0.8 s over the water at {wat[0]},{wat[1]}: red up to {redMax:F2}; let go: the goal kept {kept}, clicks refused {OzShuttleOrderAim.RefusedClicks - ref0}, ui_order_invalid {Played(t0, "ui_order_invalid")}");
                Check("T1 in the air, the right button over the water: the mark turns red (aiming feedback); let go: no order (it flies on), the refusal blip", redMax >= 0.9f && kept && OzShuttleOrderAim.RefusedClicks == ref0 + 1 && Played(t0, "ui_order_invalid") > 0);
            }
            else Check("T1 water within 175 m for the red aim", false);
        }

        // ------------------------------------------------------------------ T2: a normal landing (the springs, the shadows crossed)
        IEnumerator T2(float camH, string tag)
        {
            Log($"T2 a normal landing, the camera at {camH:F0} m ({tag}): the legs' springs, the ship's own shadow in and the mark out");
            for (int tryN = 0; tryN < 4; tryN++)
            {
                if (s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne)
                {   // fly on to the next place (the first landing: the flight of T1)
                    var q = Place("", s.CenterX, s.CenterY, 320, 640); if (q == null) { Check("T2 a landing place", false); yield break; }
                    Select(s); Move(q[0], q[1]);
                    yield return WaitFor(() => s.ShuttleAirborne, 10f);
                }
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
                if (s.ShuttleRoughLanding) { Log("   the rules made this one a crash landing: another flight"); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 30f); yield return Wait(0.5f); continue; }
                Cam(ship.transform.position, camH); rts.focus = new Vector3(ship.transform.position.x, 0f, ship.transform.position.z);
                Log($"   >>> OBS: a 60 fps take of this landing now ({tag})");
                int sp0 = OzPlayableShuttle.SpringsStarted; float t0 = Time.unscaledTime;
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 20f);
                Shot($"t2_{tag}_00_touchdown");
                float t = 0f; bool deep = false, up = false; float dipMax = 0f; float last = 0f;
                while (t < 1.6f)
                {
                    float d = ship.SpringDipNow; dipMax = Mathf.Max(dipMax, d);
                    if (!deep && d > 0.05f && d < last) { deep = true; Shot($"t2_{tag}_01_deepest_dip"); }
                    if (deep && !up && d < -0.02f && d > last) { up = true; Shot($"t2_{tag}_02_rebound_top"); }
                    last = d; t += Time.deltaTime; yield return null;
                }
                Shot($"t2_{tag}_03_settled");
                yield return Wait(0.6f);
                float rk = ship.RealShadow != null ? ship.RealShadow.K : -1f;
                Log($"   the suspension (K256g3): started {OzPlayableShuttle.SpringsStarted - sp0}, the hull sank {OzPlayableShuttle.SuspSinkSeen:F3} m ({OzPlayableShuttle.SuspOverSeen:F3} m past parked), rebound {OzPlayableShuttle.SuspReboundSeen:F3} m, the struts splayed {OzPlayableShuttle.SuspTurnSeen:F1} deg, settled {OzPlayableShuttle.SuspSettleSeen:F2} s, the pads slid {OzPlayableShuttle.SuspPadSlideMax * 100f:F2} cm / sank {OzPlayableShuttle.SuspPadSinkMax * 100f:F2} cm; hiss {Played(t0, "shuttle_leg_hiss")}, thud {Played(t0, "shuttle_leg_thud")}, creaks {Played(t0, "shuttle_leg_spring")}; " +
                    $"the own shadow first over 0 at {firstIn:F2} s, full at {fullIn:F2} s after the touchdown (now {rk:F2}), the mark's weight {ship.Shadow.MarkWeight:F2}");
                float ck = OzMotion.Reduced ? 0.5f : 1f;                                                           // CALM SCREEN: half
                Check($"T2 {tag}: the hull sinks {(ck < 1f ? 0.44f : 0.50f):F2} .. 0.62 m onto its legs (K256g3: past the parked pose {0.07f * ck:F2} .. {0.18f * ck:F2} m), {(ck < 1f ? "no visible rebound (CALM SCREEN)" : "one soft rebound (0.015 m or more)")}, the struts splay 4 .. 12 deg, settled in 0.9 .. 1.7 s", OzPlayableShuttle.SpringsStarted == sp0 + 1 && OzPlayableShuttle.SuspSinkSeen >= (ck < 1f ? 0.44f : 0.50f) && OzPlayableShuttle.SuspSinkSeen <= 0.62f && OzPlayableShuttle.SuspOverSeen <= 0.18f * ck + 0.01f && (ck < 1f || (OzPlayableShuttle.SuspOverSeen >= 0.07f && OzPlayableShuttle.SuspReboundSeen >= 0.015f)) && OzPlayableShuttle.SuspTurnSeen >= 4f && OzPlayableShuttle.SuspTurnSeen <= 12f && OzPlayableShuttle.SuspSettleSeen >= (ck < 1f ? 0.5f : 0.9f) && OzPlayableShuttle.SuspSettleSeen <= 1.7f);   // K256g3
                Check($"T2 {tag}: the pads stay planted (K256g3, on the drawn feet: slide <= 0.5 cm, never into the ground by more than 0.5 cm) while the hull sinks and the struts slide", OzPlayableShuttle.SuspPadSlideMax <= 0.005f && OzPlayableShuttle.SuspPadSinkMax <= 0.005f);   // K256g3
                Check($"T2 {tag}: the hydraulic hiss, the struts' thud and the creak play (every visible action has its sound)", Played(t0, "shuttle_leg_hiss") >= 1 && Played(t0, "shuttle_leg_thud") >= 1 && Played(t0, "shuttle_leg_spring") >= 1);   // K256g3
                Check($"T2 {tag}: at the touchdown the ship's own shadow eases in over about 0.5 s (full 0.4 .. 0.65 s after it; it starts within 0.10 s + one frame = {0.1f + Mathf.Clamp(Time.smoothDeltaTime, 1f / 60f, 1f / 30f):F3} s) while the mark eases out (weight 0 now)", firstIn >= 0f && firstIn < 0.1f + Mathf.Clamp(Time.smoothDeltaTime, 1f / 60f, 1f / 30f) && fullIn >= 0.35f && fullIn <= 0.65f && rk == 1f && ship.Shadow.MarkWeight < 0.01f);
                yield break;
            }
            Check($"T2 {tag}: a normal landing in 4 flights", false);
        }

        // ------------------------------------------------------------------ T3: the cursor and the orders, landed
        IEnumerator T3()
        {
            Log("T3 landed and selected: the cursor and the click over water, rocks and free ground; a mixed group; ANYWHERE");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 30f);
            var wat = Place("water", s.CenterX, s.CenterY, 120, 1400); var rock = Place("rocks", s.CenterX, s.CenterY, 120, 1400); var free = Place("", s.CenterX, s.CenterY, 200, 420);
            Select(s);
            foreach (var (name, at, want) in new[] { ("water", wat, "nono"), ("rocks", rock, "nono"), ("free ground", free, "move") })
            {
                if (at == null) { Check($"T3 a place: {name}", false); continue; }
                Cam(Wp(at[0], at[1]), 70f); yield return Wait(0.4f);
                var p = ScreenAt(at[0], at[1]); string cur = "?";
                yield return CursorAt(p, c => cur = c);
                Shot($"t3_cursor_over_{name.Replace(' ', '_')}_h70");
                Check($"T3 the cursor over {name} at {at[0]},{at[1]}: {want} (got {cur})", cur == want);
            }
            // review: over a flag the game's cursor is GRAB for any selection that can move; the shuttle never takes a flag and its click there
            // is refused: with only the shuttle selected it is NO
            ZObject flagT = null; long flagD = long.MaxValue;
            foreach (var o in W.Objects)
            {
                if (o.Destroyed || o.Type != "flag") continue;
                long dd = (long)(o.CenterX - s.CenterX) * (o.CenterX - s.CenterX) + (long)(o.CenterY - s.CenterY) * (o.CenterY - s.CenterY);
                if (dd < flagD && dd < 1400L * 1400L) { flagD = dd; flagT = o; }
            }
            if (flagT != null)
            {
                Cam(Wp(flagT.CenterX, flagT.CenterY), 70f); yield return Wait(0.4f);
                string curF = "?"; yield return CursorAt(ScreenAt(flagT.CenterX, flagT.CenterY), c => curF = c);
                Shot("t3_cursor_over_flag_h70");
                Check($"T3 the cursor over the flag #{flagT.Id} at {flagT.CenterX},{flagT.CenterY} (hover: {(input.Hover == flagT ? "the flag" : input.Hover != null ? input.Hover.Type : "none")}): nono, not grab (got {curF})", curF == "nono");
            }
            else Log("T3 no flag within 175 m: the flag cursor is not looked at");
            // the click on the water: no order, the blip
            if (wat != null)
            {
                Cam(Wp(wat[0], wat[1]), 70f); yield return Wait(0.4f);
                int f0 = s.ShuttleFlights, n0 = s.ShuttleNoLandOrders; float t0 = Time.unscaledTime;
                yield return RightClick(ScreenAt(wat[0], wat[1]));
                yield return Wait(2f);
                Log($"a right-click on the water: flights {s.ShuttleFlights - f0}, its orders taken out {s.ShuttleNoLandOrders - n0} ({OzShuttleOrderAim.LastWhy}), ui_order_invalid {Played(t0, "ui_order_invalid")}");
                Check("T3 a right-click on the water gives the shuttle no order (no take-off) and the refusal blip plays", s.ShuttleFlights == f0 && s.ShuttleNoLandOrders == n0 + 1 && s.ShuttleState == ZShuttleState.Landed && Played(t0, "ui_order_invalid") > 0);
            }
            // a mixed group: the ship + a grunt squad, a place only the ship cannot land (beside a building, a robot can walk there)
            var mix = Place("building", s.CenterX, s.CenterY, 160, 900, (tx, ty) => W.Map.ClassAt(tx, ty) == ZClass.Ground && W.Path.TilePassable(tx, ty, true) && !W.Objects.Any(o => !o.Destroyed && o.Contains(tx * 16 + 8, ty * 16 + 8)));
            var near = Place("", s.CenterX, s.CenterY, 72, 260);
            if (mix != null && near != null)
            {
                OzCheatSpawner.FreeTile(W, near[0] >> 4, near[1] >> 4, 1, true, out int gx, out int gy);
                var sq = W.CreateRobotGroup("grunt", gx * 16, gy * 16, me); if (sq != null) { g.SpawnUnitView(sq); foreach (var m in sq.Minions) g.SpawnUnitView(m); }
                yield return Wait(0.5f);
                Select(s, sq); Cam(Wp(mix[0], mix[1]), 70f); yield return Wait(0.4f);
                var p = ScreenAt(mix[0], mix[1]); string cur = "?"; yield return CursorAt(p, c => cur = c);
                int f0 = s.ShuttleFlights; float t0 = Time.unscaledTime;
                yield return RightClick(p); yield return Wait(1.5f);
                bool squadGoes = sq != null && (sq.Waypoints.Count > 0 || sq.IsMoving), shipStays = s.ShuttleFlights == f0 && s.ShuttleState == ZShuttleState.Landed;
                Shot("t3_mixed_group_squad_goes_ship_stays_h70");
                Log($"the ship + a grunt squad, the cursor over {mix[0]},{mix[1]} (the ship: never, beside a building): {cur}; the click: the squad goes {squadGoes}, the ship stays {shipStays}, ui_order_invalid {Played(t0, "ui_order_invalid")}");
                Check("T3 a mixed group: the move cursor (the others can go), the squad takes the order, the shuttle none, no refusal blip (the game's way for orders some units cannot take)", cur == "move" && squadGoes && shipStays && Played(t0, "ui_order_invalid") == 0);
            }
            else Check("T3 a place beside a building for the mixed group", false);
            // ANYWHERE: the move cursor over the water and the click flies it there; then back
            if (wat != null)
            {
                OzShuttleOrdersOption.Setting = OzShuttleOrdersOption.Anywhere; Select(s);
                Cam(Wp(wat[0], wat[1]), 70f); yield return Wait(0.4f);
                var p = ScreenAt(wat[0], wat[1]); string cur = "?"; yield return CursorAt(p, c => cur = c);
                int f0 = s.ShuttleFlights;
                yield return RightClick(p);
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover, 60f);
                Cam(ship.transform.position, 70f); yield return Wait(0.6f);
                Shot("t3_anywhere_hovers_over_water_h70");
                Log($"ANYWHERE: the cursor over the water {cur}; the click: flights {s.ShuttleFlights - f0}, now {s.ShuttleState} ({s.ShuttleNoLand})");
                Check("T3 ANYWHERE: the move cursor over the water, the click flies it there and it hovers (the orders as they were)", cur == "move" && s.ShuttleFlights == f0 + 1 && s.ShuttleState == ZShuttleState.Hover);
                OzShuttleOrdersOption.Setting = OzShuttleOrdersOption.LandableSpotsOnly;
                var back = Place("", s.CenterX, s.CenterY, 160, 900); if (back != null) { Move(back[0], back[1]); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 60f); }
            }
        }

        // ------------------------------------------------------------------ T4: a crash landing's light settle
        IEnumerator T4()
        {
            Log("T4 a crash landing (the rules' proof switch): the light spring the moment it stands level");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled, 30f);
            var q = Place("", s.CenterX, s.CenterY, 320, 640); if (q == null) { Check("T4 a landing place", false); yield break; }
            OzPlayableShuttle.ProofRoughNext = true; int c0 = OzPlayableShuttle.SpringsCrash; float t0 = Time.unscaledTime;
            Select(s); Move(q[0], q[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 60f);
            Cam(ship.transform.position, 70f);
            yield return WaitFor(() => OzPlayableShuttle.SpringsCrash > c0, 20f);
            yield return Wait(0.15f); Shot("t4_crash_level_light_spring_h70");
            yield return Wait(1.2f);
            Log($"crash landing: rough {s.ShuttleRoughLanding}, the light spring started {OzPlayableShuttle.SpringsCrash - c0}, its dip {OzPlayableShuttle.SpringDipSeen:F3} m, settled {OzPlayableShuttle.SpringSettleSeen:F2} s, creaks {Played(t0, "shuttle_leg_spring")}");
            float ck = OzMotion.Reduced ? 0.5f : 1f;
            Check($"T4 the crash landing's final settle springs lightly (one spring, dip {0.07f * ck:F3} .. {0.12f * ck:F3} m)", OzPlayableShuttle.SpringsCrash == c0 + 1 && OzPlayableShuttle.SpringDipSeen >= 0.07f * ck && OzPlayableShuttle.SpringDipSeen <= 0.12f * ck);
            OzPlayableShuttle.ProofRoughNext = false;
        }
    }
}
