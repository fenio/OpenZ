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
    /// The K256b Play proof of the playable shuttle (rule DV118, the polish round), on level 16 in Play (menu OpenZ/V1.1/Shuttle: K256b
    /// Play Proof A / B / A clip pace, Editor/OzShuttlePolishMenu.cs). Modelled on the APC door proof (ApcDoorProof): counted PASS / FAIL
    /// lines, the sound log, full-screen pictures from the normal RTS camera at the standard zoom (70 m), uncropped; extra close pictures are
    /// named *_extra_close and are never the proof. Set-up: the CPU teams removed, morning (day clock 0.10, held), the red-side repair
    /// station given to red, the camera following the ship's ground point.
    /// Stages (A): S1 select + hover (the frame on the real outline, the name chip), S2 boarding through the rear ramp, S3 take-off /
    /// cruise / a 90 degree re-order (jets always on, no ring in the air, the shadow, the frame on the flying ship, the lean, the
    /// downwash, a LOW take-off for the particle share), S4 over the fort, the pyramid and a kapok cell + a 10 s hover (clearance, climb
    /// speed, bob), S5 over the river (the red shadow, the error and "Can't land here!"), S6 three landings with Rachel forced (her line
    /// never over Alan's), S7 a forced rough landing, S8 EXIT down the ramp, S9 a take-off beside trees (crowns leaning, the rustle), S10
    /// through the parrots' circuit, S11 R to the repair station (the crane rolls away, a level hover turn, down ON the deck on the
    /// rules' deck point and heading, no part in the station, no landing voices, the crane back after it left), S12 at night (the
    /// searchlights), S15 the testing overlay (P): a shuttle of each side dragged in, drawn in full, the player's one flown and landed,
    /// S14 shot down in the air over a blue group with a squad aboard (the break-up, everyone lost). Every take also counts that no robot
    /// line ever plays over Rachel's landing line. (P, any level, also level 2): S15 alone.
    /// (B, a fresh level start; also at clip pace): S13 destroyed on the ground with a squad aboard. (G) the weapons gallery: the ship flies back and forth
    /// over open ground while every enemy weapon in turn fires at it (the rifles, guns, flame, lasers and tesla bolts of the seven
    /// robots, the jeep, the three tanks, the missile launcher, an APC's crew, the four guns, grenades, then near the blue fort its own
    /// gun), healed between them, every drawn shot counted (where it started, where it ended, its height over the ground: never at the
    /// ship's ground point); then, each after a fresh start of the level with a squad aboard, kills by a rocket salvo, a howitzer shell,
    /// the fort's gun and rifle fire (the break-up thrown along the killing round, the squad's bodies falling among the pieces).
    /// Clip pace (A, B or G): the same staging at the natural pace, no pictures, waiting for a go.txt written after its ready.txt
    /// (take_log.txt with CAPTION lines) for a recording. Output folder: <see cref="Folder"/> (built at run time from the project's place).
    /// Test only.
    /// </summary>
    public sealed partial class OzShuttlePolishProof : MonoBehaviour                              // + OzShuttlePolishProof.Order.cs (the door sounds' order)
    {
        public static string Report = "idle";
        /// <summary>A sub-folder name for the output (empty = the default); <see cref="Folder"/> builds the full path at run time.</summary>
        public static string OutDir = "";
        /// <summary>The output folder: the work tree's records folder found upward from the project at run time (its milestones'
        /// shuttle_polish_proof, or OutDir there), else the player's own data folder.</summary>
        public static string Folder()
        {
            string sub = string.IsNullOrEmpty(OutDir) ? "shuttle_polish_proof" : OutDir;
            if (System.IO.Path.IsPathRooted(sub)) return sub;
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", sub);
            return System.IO.Path.Combine(Application.persistentDataPath, sub);
        }
        public static void Run(string mode)
        {
            Report = "running " + mode;
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            var go = new GameObject("OzShuttlePolishProof"); var p = go.AddComponent<OzShuttlePolishProof>(); p.mode = mode;
        }

        // the ship's plan outline (root space x, z; SH_Hull + pods + legs, from the model)
        static readonly float[] Plan = { -3.194f, -0.057f, -3.182f, -0.499f, -3.156f, -0.724f, -3.138f, -0.873f, -3.000f, -1.228f, -2.332f, -2.822f, -2.328f, -2.830f, -2.237f, -2.999f, -2.119f, -3.169f, -1.998f, -3.329f, 2.003f, -3.333f, 2.190f, -3.073f, 2.267f, -2.955f, 2.353f, -2.817f, 3.003f, -1.225f, 3.143f, -0.842f, 3.151f, -0.822f, 3.174f, -0.620f, 3.194f, -0.442f, 3.194f, -0.041f, 3.188f, 0.182f, 3.066f, 0.902f, 3.050f, 0.930f, 2.446f, 1.587f, 1.024f, 2.892f, 0.945f, 2.963f, 0.763f, 3.114f, 0.517f, 3.243f, 0.326f, 3.305f, 0.133f, 3.333f, -0.190f, 3.323f, -0.358f, 3.294f, -0.531f, 3.237f, -0.771f, 3.106f, -1.057f, 2.856f, -2.441f, 1.584f, -3.054f, 0.911f, -3.073f, 0.839f, -3.153f, 0.351f, -3.184f, 0.162f, -3.191f, 0.078f };

        string mode = "A"; bool clip;
        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; OpenZUnitOverlay overlay; DayNight dn; ZObject s; OzPlayableShuttle ship; Team me = Team.Red, them = Team.Blue;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir; float T0; bool follow = true;
        // per-frame measurements
        int ghostInside, groundFrames; float worstShadowOff, worstFrameOff, bankPeak, gapMin = 999f, climbMax, birdMin = 999f;
        bool measureGap, measureBirds, measureFrame; string gapWhere = "-";
        // the nose with the acceleration, the lean into a strafe (with the glide, its fastest change, how often it turned back)
        float pitchAccMax = -99f, pitchBrakeMin = 99f, pitchCruiseAbs, cruiseSteady, strafeLeanPeak, strafeWrongWay, leanRateMax, lastLean, leanExt; int strafeFrames, leanDir, leanTurns;
        // the lean into turns: frames with the outer side down, the turn's bank while it hardly moves
        int bankWrong; float slowBankMax, voiceT0, prevDt;
        OzSoundTake take;                                                                               // K256e: the whole take's sound log (FIX_LIST M15: no 2000-line cap)

        void Start()
        {
            clip = mode == "A_clip" || mode == "B_clip" || mode == "G_clip";
            if (mode.StartsWith("G")) DontDestroyOnLoad(gameObject);                                   // the kills each start the level afresh
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); overlay = FindFirstObjectByType<OpenZUnitOverlay>();
            dir = Folder();
            System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttlePolishProof] " + t); if (clip) TakeLog(t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name)
        {
            if (clip) { Log("(clip pace: no picture " + name + ")"); return; }
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png");
        }
        void TakeLog(string t) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "take_log.txt"), System.DateTime.Now.ToString("HH:mm:ss.fff") + " " + t + "\n"); } catch { } }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }

        static void MouseEv(Vector2 p, MouseButton? b = null)
        {
            var st = new MouseState { position = p }; if (b.HasValue) st = st.WithButton(b.Value);
            InputSystem.QueueStateEvent(Mouse.current, st);
        }
        IEnumerator Hover(Vector2 p, int n = 4) { for (int i = 0; i < n; i++) { MouseEv(p); yield return null; } }
        IEnumerator Click(Vector2 p, MouseButton b) { MouseEv(p); yield return null; MouseEv(p, b); yield return null; yield return null; MouseEv(p); yield return null; yield return null; }

        UnitView V(ZObject o) => o != null && g.Views.TryGetValue(o.Id, out var v) ? v : null;
        void Sel() { input.Selection.Clear(); input.Selection.Add(s); }
        ZObject Squad(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        ZObject Vehicle(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject("vehicle", type, tx * 16, ty * 16, t); if (o != null) g.SpawnUnitView(o);
            return o;
        }
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool Enter(ZObject sq) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
        int[] Pad(int r, float angDeg, int span)
        {
            for (int dr = 0; dr <= 200; dr += 16)
                for (int k = 0; k <= span; k += 10)
                    foreach (int sg in new[] { 1, -1 })
                    {
                        double a = (angDeg + sg * k) * System.Math.PI / 180.0;
                        var q = W.ShuttleClamp(s.CenterX + (int)(System.Math.Cos(a) * (r + dr)), s.CenterY + (int)(System.Math.Sin(a) * (r + dr)));
                        if (W.ShuttlePadFree(q.x, q.y, s, out _)) return new[] { q.x, q.y };
                    }
            return null;
        }
        int[] Spot(System.Func<int, int, string, bool> want, int near = 0)
        {
            long best = long.MaxValue; int[] r = null;
            for (int ty = 4; ty < W.Map.height - 4; ty++)
                for (int tx = 4; tx < W.Map.width - 4; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - s.CenterX) * (x - s.CenterX) + (long)(y - s.CenterY) * (y - s.CenterY);
                    if (d >= best || d < (long)near * near) continue;
                    W.ShuttlePadFree(x, y, s, out string why);
                    if (want(x, y, why)) { best = d; r = new[] { x, y }; }
                }
            return r;
        }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP") || e.src == "DOOR")) n++; return n; }
        static float FirstAt(float t0, string key) { foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.src == "DOOR")) return e.t; return -1f; }
        bool Inside(Vector3 w, float margin)
        {   // inside the ship's plan outline grown by margin (root space)
            var l = ship.transform.InverseTransformPoint(w); int n = Plan.Length / 2; bool c = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = Plan[i * 2], zi = Plan[i * 2 + 1], xj = Plan[j * 2], zj = Plan[j * 2 + 1];
                if ((zi > l.z) != (zj > l.z) && l.x < (xj - xi) * (l.z - zi) / (zj - zi + 1e-9f) + xi) c = !c;
            }
            if (c) return true;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {   // the distance to each edge
                var a = new Vector2(Plan[j * 2], Plan[j * 2 + 1]); var b = new Vector2(Plan[i * 2], Plan[i * 2 + 1]); var p = new Vector2(l.x, l.z);
                var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                if ((a + ab * t - p).magnitude < margin) return true;
            }
            return false;
        }

        /// <summary>The exact screen rectangle of every SH_ part's vertices (dense) as drawn now.</summary>
        Rect ExactRect()
        {
            var cam = Camera.main; Vector2 mn = new Vector2(1e9f, 1e9f), mx = new Vector2(-1e9f, -1e9f);
            foreach (var r in ship.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.name.StartsWith("SH_") || r.gameObject.name.Contains("Glow")) continue;
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var m = r.transform.localToWorldMatrix; var vs = mf.sharedMesh.vertices; int step = Mathf.Max(1, vs.Length / 3000);
                for (int i = 0; i < vs.Length; i += step) { var sp = cam.WorldToScreenPoint(m.MultiplyPoint3x4(vs[i])); if (sp.z < 0) continue; mn = Vector2.Min(mn, sp); mx = Vector2.Max(mx, sp); }
            }
            return Rect.MinMaxRect(mn.x, mn.y, mx.x, mx.y);
        }

        void Update()
        {
            if (s == null || !ship || ship.BrokenUp) return;
            if (follow && rts) { var p = ship.transform.position; rts.focus = new Vector3(p.x, 0f, p.z); }
            // ghosts and live robots walking on the ground never inside the hull outline (+0.3 m); the doorway corridor and the ramp are allowed
            var box = GameObject.Find("OZ_BoardingCrew");
            if (box && s.ShuttleState == ZShuttleState.Landed)
                foreach (Transform c in box.transform)
                {
                    if (!c.gameObject.activeInHierarchy) continue;
                    var l = ship.transform.InverseTransformPoint(c.position);
                    if (l.y > 0.35f || (Mathf.Abs(l.x) < 0.95f && l.z < -1.5f)) continue;                 // up the ramp / in the doorway / seated
                    groundFrames++; if (Inside(c.position, 0.3f)) ghostInside++;
                }
            if (s.ShuttleAirborne)
            {
                var sh = ship.Shadow; var gp = ship.transform.position;
                if (sh != null && sh.Shown) worstShadowOff = Mathf.Max(worstShadowOff, new Vector2(sh.Centre.x - gp.x, sh.Centre.z - gp.z).magnitude);
                bankPeak = Mathf.Max(bankPeak, Mathf.Abs(ship.BankDeg));
                if (s.ShuttleState == ZShuttleState.Cruise) climbMax = Mathf.Max(climbMax, ship.ClimbRate);
                if (s.ShuttleState == ZShuttleState.Cruise)
                {   // the nose: down speeding up, up braking, level cruising (the rules' eased acceleration)
                    float acc = Mathf.Max(1f, (float)s.RealMoveSpeed) / (float)ZWorld.ShuttleAccelSec, k = (float)s.ShuttleAccelPx / acc;
                    if (k > 0.8f) pitchAccMax = Mathf.Max(pitchAccMax, ship.PitchNowDeg);
                    if (k < -0.8f) pitchBrakeMin = Mathf.Min(pitchBrakeMin, ship.PitchNowDeg);
                    cruiseSteady = Mathf.Abs(k) < 0.03f && s.ShuttleSpeedPx > 0.95 * s.RealMoveSpeed ? cruiseSteady + Time.deltaTime : 0f;
                    if (cruiseSteady > 1.0f && ship.ClimbRate < 0.3f && ship.ClimbRate > -0.3f) pitchCruiseAbs = Mathf.Max(pitchCruiseAbs, Mathf.Abs(ship.PitchNowDeg));
                }
                float sv = (float)s.ShuttleStrafeVelPx / 8f, ln = ship.StrafeLeanDeg;
                if (Mathf.Abs(sv) > 1f)
                {   // the lean with the glide (gliding to the right = the right side down = a negative lean)
                    strafeFrames++; strafeLeanPeak = Mathf.Max(strafeLeanPeak, Mathf.Abs(ln));
                    if (ln * sv > 0.5f) strafeWrongWay = Mathf.Max(strafeWrongWay, Mathf.Abs(ln));
                }
                if (prevDt > 0f) leanRateMax = Mathf.Max(leanRateMax, Mathf.Abs(ln - lastLean) / prevDt);
                if (leanDir >= 0 && ln > leanExt) leanExt = ln; else if (leanDir <= 0 && ln < leanExt) leanExt = ln;
                if (leanDir >= 0 && ln < leanExt - 0.5f) { if (leanDir > 0) leanTurns++; leanDir = -1; leanExt = ln; }
                else if (leanDir <= 0 && ln > leanExt + 0.5f) { if (leanDir < 0) leanTurns++; leanDir = 1; leanExt = ln; }
                lastLean = ln;
                // the lean into turns: the inner side down; level while it turns on the spot (climbing, hovering, over the station)
                float vmx = Mathf.Max(1f, (float)s.RealMoveSpeed);
                if (Mathf.Abs(s.ShuttleTurnRateDeg) > 15f && s.ShuttleSpeedPx > 0.5f * vmx && Mathf.Abs(ship.TurnBankDeg) > 1f && ship.TurnBankDeg * s.ShuttleTurnRateDeg > 0f) bankWrong++;
                if (s.ShuttleSpeedPx < 5.0 && ship.FeetHeight > 2f) slowBankMax = Mathf.Max(slowBankMax, Mathf.Abs(ship.TurnBankDeg));
                if (measureFrame && input.MeshRect(s, out var fr)) { var ex = ExactRect(); worstFrameOff = Mathf.Max(worstFrameOff, (fr.center - ex.center).magnitude); }
                if (measureGap && ship.Sky != null && ship.FeetHeight >= 1f)                          // K256e: from 1 m up (the ground it stands on and lifts off is its own)
                {   // the hull's sample points (the hull box corners, the two pods) over what stands under them
                    foreach (var q in HullPoints())
                    {
                        ship.Sky.Query(q.x, q.z, 0.5f, true, out float ht, out float pt);
                        float top = Mathf.Max(ht, pt);
                        if (top > float.MinValue && q.y - top < gapMin) { gapMin = q.y - top; gapWhere = $"{s.ShuttleState} at ({q.x:F1}, {q.y:F2}, {q.z:F1}) over a {(pt > ht ? "plant" : "hard")} top {top:F2} m (root {ship.transform.position.y:F2} m, feet {ship.FeetHeight:F1} m)"; }
                    }
                }
            }
            prevDt = Mathf.Min(Time.deltaTime, 0.05f) * Mathf.Max(0.05f, g ? g.gameSpeed : 1f);
            if (measureBirds)
            {
                var pos = new List<Vector3>(); var span = new List<float>(); var drawn = new List<bool>();
                JungleFauna.Flying(pos, span, drawn);
                foreach (var p in pos) birdMin = Mathf.Min(birdMin, JungleFauna.ShipClearance(p));
            }
        }

        IEnumerable<Vector3> HullPoints()
        {
            var t = ship.transform;
            foreach (var x in new[] { -2.2f, 2.2f }) foreach (var y in new[] { 0.6f, 5.1f }) foreach (var z in new[] { -3.2f, 3.3f }) yield return t.TransformPoint(new Vector3(x, y, z));
            yield return t.TransformPoint(new Vector3(2.47f, 2.6f, -0.1f)); yield return t.TransformPoint(new Vector3(-2.47f, 2.6f, -0.1f));
        }

        IEnumerator All()
        {
            T0 = Time.unscaledTime; Application.targetFrameRate = 60;
            take = OzSoundTake.Begin(System.IO.Path.Combine(dir, "sound_take" + TakeTag() + ".txt"));   // K256e (FIX_LIST M15 / M13): every line of the take and its table per action
            if (mode.StartsWith("P"))
            {   // the testing overlay's shuttles alone, on any level (level 2 has no shuttle of its own)
                if (W == null) { Log("no game"); fails++; Finish(); yield break; }
                if (W.PlayableShuttle != null) { s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s); if (ship) me = s.Owner; else s = null; }
                if (s == null) me = ZWorld.ShuttleTeam;
                them = me == Team.Red ? Team.Blue : Team.Red;
                Log($"K256c shuttle proof P (the testing overlay), {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; the level's own shuttle {(s != null ? "#" + s.Id : "none")}");
                W.Bots.Clear(); dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
                if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
                yield return Wait(1.0f);
                yield return S15();
                Finish(); yield break;
            }
            if (W == null || W.PlayableShuttle == null || !OzPlayableShuttle.I) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on, not 'Zod exact')"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = OzPlayableShuttle.I; me = s.Owner; them = me == Team.Red ? Team.Blue : Team.Red;
            Log($"K256b shuttle polish proof {mode}, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, quality {OzQuality.Name}");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            ZObject station = null; foreach (var f in W.Flags) foreach (var b in f.LinkedObjects) if (b.Type == "repair" && station == null && (b.Owner == me || b.Owner == Team.Neutral || true)) { station = b; if (b.Owner != me) W.AwardZone(f, me); }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            Log($"set-up: CPU removed {bots}, day 0.10 held, repair station {(station != null ? "#" + station.Id + " " + station.Owner : "none")}, camera 70 m; door kit: {ShuttleRearDoor.KitLog}");
            Check("the rear door kit is fitted on the ship (no stand-in)", ship.Door != null && ShuttleRearDoor.Built > 0);
            if (clip)
            {
                string ready = System.IO.Path.Combine(dir, "ready.txt"), go = System.IO.Path.Combine(dir, "go.txt");
                System.IO.File.WriteAllText(ready, "ready " + mode);
                var readyAt = System.IO.File.GetLastWriteTimeUtc(ready);
                Log("waiting for a go.txt written after ready.txt (" + mode + ")");
                yield return WaitFor(() => System.IO.File.Exists(go) && System.IO.File.GetLastWriteTimeUtc(go) >= readyAt, 600f);   // a go.txt of an earlier take does not start this one
                GameBoot.CapFps = 60; Application.targetFrameRate = 60; Log("go");
            }
            yield return Wait(1.0f);
            if (mode.StartsWith("B")) { yield return StageGroundDeath(); Finish(); yield break; }
            if (mode.StartsWith("G")) { yield return Gallery(); yield return KillSeries(); Finish(); yield break; }
            yield return S1(); yield return S2(); yield return S3(); yield return S4(); yield return S5(); yield return S6(); yield return S7();
            yield return S8(); yield return S9(); yield return S10(); yield return S11(station); yield return S12(); yield return S15(); yield return S14();
            Finish();
        }

        // ------------------------------------------------------------------ S1: select + hover
        IEnumerator S1()
        {
            Log("S1 select + hover");
            input.Selection.Clear(); yield return null;
            var p = input.ScreenRect(s).center;
            yield return Click(p, MouseButton.Left); yield return Hover(p, 6);
            bool sel = input.Selection.Contains(s);
            input.MeshRect(s, out var fr); var ex = ExactRect();
            float dl = Mathf.Abs(fr.xMin - ex.xMin), dr = Mathf.Abs(fr.xMax - ex.xMax), db = Mathf.Abs(fr.yMin - ex.yMin), dt = Mathf.Abs(fr.yMax - ex.yMax);
            Log($"frame {fr} exact {ex}: sides off L {dl:F1} R {dr:F1} B {db:F1} T {dt:F1} px; hover member {(overlay && overlay.HoverMember != null ? overlay.HoverMember.Type + "#" + overlay.HoverMember.Id : "-")}, LastHoverId {OzShuttleOverlay.LastHoverId}");
            Check("S1 left-click selects the shuttle", sel);
            Check("S1 the frame is the ship's real outline (each side within 6 px of its parts' projection)", Mathf.Max(Mathf.Max(dl, dr), Mathf.Max(db, dt)) <= 6f);
            Check("S1 the pointer on the ship shows its name chip (the overlay's hover member is the shuttle)", overlay && overlay.HoverMember == s && OzShuttleOverlay.LastHoverId == s.Id);
            Shot("01_selected_landed_name_h70");
        }

        // ------------------------------------------------------------------ S2: boarding through the rear ramp
        IEnumerator S2()
        {
            Log("S2 boarding: a grunt squad 10 m in front of the dome, ENTER");
            double hd = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            int fx = (int)(s.CenterX + System.Math.Cos(hd) * 80) >> 4, fy = (int)(s.CenterY + System.Math.Sin(hd) * 80) >> 4;
            var sq = Squad("grunt", fx, fy, me); yield return Wait(0.8f);
            float t0 = Time.unscaledTime; int opens0 = s.ShuttleRampOpens, shuts0 = s.ShuttleRampShuts; ghostInside = groundFrames = 0;
            Check("S2 the ENTER order is taken", Enter(sq));
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Open, 30f);
            yield return WaitFor(() => s.Passengers.Count > 0 || ship.Door.Seated > 0, 2f);
            Shot("02a_ramp_open_squad_at_foot");
            yield return WaitFor(() => s.Passengers.Count > 0, 10f);
            float boardedAt = Time.unscaledTime;
            yield return Wait(1.2f); Shot("02b_on_the_ramp_single_file");
            yield return WaitFor(() => ship.Door.Seated >= 1, 4f);
            Shot("02c_seated_lit_bay"); yield return null;
            if (!clip && rts) { rts.SetHeightImmediate(30f); yield return Wait(0.3f); Shot("02c_extra_close"); yield return null; rts.SetHeightImmediate(70f); }
            yield return WaitFor(() => s.ShuttleRampShuts > shuts0, 15f);
            yield return Wait(0.6f); Shot("02d_ramp_shut");
            Log($"boarded {s.Passengers.Count} {s.PassengerType}; opens +{s.ShuttleRampOpens - opens0}, shuts +{s.ShuttleRampShuts - shuts0}; ground figure samples {groundFrames}, inside the hull outline {ghostInside}; steps {BoardingView.ShuttleSteps}, seated {ship.Door.Seated}");
            Check("S2 the squad boards through the ramp (open, then shut and latched)", s.Passengers.Count > 0 && s.ShuttleRampOpens > opens0 && s.ShuttleRampShuts > shuts0);
            Check("S2 no figure on the ground ever inside the hull's outline (+0.3 m)", ghostInside == 0);
            float a = FirstAt(t0, "shuttle_door_open"), b = FirstAt(t0, "shuttle_ramp_hydraulic"), c = FirstAt(t0, "shuttle_door_close"), d = FirstAt(t0, "latch"), e = FirstAt(t0, "crew_in"), f = FirstAt(t0, "vo_shuttle_board");
            Log($"sounds: door_open {a - T0:F2}, ramp_hydraulic {b - T0:F2}, door_close {c - T0:F2}, latch {d - T0:F2}, crew_in {e - T0:F2}, All aboard {f - T0:F2}");
            Check("S2 the door sounds in their order: open with its hydraulic (together: within 0.3 s, never before it), close, latch; crew in; All aboard!", a > 0 && b >= a - 0.05f && b - a <= 0.3f && c > b && d >= c && e > 0 && f > 0);
        }

        // ------------------------------------------------------------------ S3: take-off, cruise, a 90 degree re-order
        IEnumerator S3()
        {
            Log("S3 take-off, cruise 60 m east, a 90 degree re-order halfway");
            Sel(); OzShuttleJets.AirFrames = OzShuttleJets.AirFramesUnder = 0; OzShuttleShadow.RingSkips = 0; OzShuttleDownwash.PeakAlive = 0; worstShadowOff = 0f; bankPeak = 0f; worstFrameOff = 0f; bankWrong = 0; slowBankMax = 0f;
            float t0 = Time.unscaledTime;
            var q = Pad(480, 0f, 60); if (q == null) { Check("S3 a landing place 60 m east", false); yield break; }
            Check("S3 the move order is taken (after the ramp shut)", Move(q[0], q[1]));
            yield return WaitFor(() => s.ShuttleAirborne && ship.FeetHeight > 1.5f, 10f);
            Shot("03a_takeoff_jets_dust_h70");
            int peakHigh = 0; yield return WaitFor(() => { peakHigh = Mathf.Max(peakHigh, ship.Wash.Alive); return s.ShuttleState == ZShuttleState.Cruise; }, 8f);
            measureFrame = true;
            yield return Wait(2.0f);
            // the pointer on the flying ship: the frame, the bar and the name chip on it
            var hp = input.ScreenRect(s).center; yield return Hover(hp, 6);
            Shot("03b_cruise_frame_bar_shadow_h70");
            var r2 = Pad(420, -90f, 60);
            if (r2 != null) { Move(r2[0], r2[1]); Log("re-order 90 degrees: north"); }
            float bk = 0f; yield return WaitFor(() => { bk = Mathf.Max(bk, Mathf.Abs(ship.BankDeg)); return bk > 8f && Mathf.Abs(ship.BankDeg) >= bk - 0.2f; }, 4f);
            Shot("09a_banked_turn_h70");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 40f);
            measureFrame = false;
            Log($"airborne frames {OzShuttleJets.AirFrames}, under T 0.7: {OzShuttleJets.AirFramesUnder}; ring draws left out {OzShuttleShadow.RingSkips}; shadow off its ground point {worstShadowOff:F2} m; frame centre off the drawn hull {worstFrameOff:F1} px; bank peak {bankPeak:F1} deg; downwash peak alive {peakHigh} (HIGH share)");
            Check("S3 the jets burn every frame off the ground (throttle >= 0.7)", OzShuttleJets.AirFrames > 30 && OzShuttleJets.AirFramesUnder == 0);
            Check("S3 no ring drawn in the air (the shadow instead), the shadow straight under the ship (<= 0.3 m)", OzShuttleShadow.RingSkips > 0 && worstShadowOff <= 0.3f);
            Check("S3 the frame flies with the ship (its centre within 10 px of the drawn hull)", worstFrameOff <= 10f);
            Check("S3 it leans 12-20 degrees into the turn (K256e S1: at most 20, 0.28 per deg/s of turn)", bankPeak >= 12f && bankPeak <= 20.5f);
            Log($"the lean into turns: frames with the outer side down {bankWrong}; the turn's bank while it hardly moves (under 5 px/s: the climb's turn on the spot) at most {slowBankMax:F2} deg");
            Check("S3 it banks only while it really flies a turn, the inner side down (0 frames the wrong way), and stays level turning on the spot (<= 2 deg under 5 px/s)", bankWrong == 0 && slowBankMax <= 2f);
            Log($"the nose with the acceleration: speeding up {pitchAccMax:F1} deg down, braking {pitchBrakeMin:F1} deg, cruising at most {pitchCruiseAbs:F2} deg");
            Check("S3 the nose dips a little speeding up (>= 2.5 deg down) and lifts braking (>= 2.5 deg up), level while it cruises (<= 1 deg)", pitchAccMax >= 2.5f && pitchBrakeMin <= -2.5f && pitchCruiseAbs <= 1.0f);
            Check($"S3 the downwash at the take-off: >= {OzShuttleDownwash.ProofPeakMin} soft dust puffs alive (HIGH)", peakHigh >= OzShuttleDownwash.ProofPeakMin);
            // the same take-off with the LOW share
            var back = Pad(300, 180f, 90);
            if (back != null)
            {
                float was = OzQuality.Level == 0 ? 1f : OzQuality.Level == 1 ? 0.75f : 0.5f;
                yield return WaitFor(() => ship.Wash.Alive < 10, 8f);
                OzShuttleDownwash.ShareOverride = 0.5f; OzShuttleJets.ShareOverride = 0.5f;
                Move(back[0], back[1]); int peakLow = 0;
                yield return WaitFor(() => { peakLow = Mathf.Max(peakLow, ship.Wash.Alive); return s.ShuttleState == ZShuttleState.Cruise; }, 10f);
                OzShuttleDownwash.ShareOverride = -1f; OzShuttleJets.ShareOverride = -1f;
                Log($"the same take-off with the LOW share: peak alive {peakLow} (HIGH {peakHigh}, now the preset's {was:F2})");
                Check("S3 LOW: the downwash's share <= 0.55 x HIGH", peakHigh > 0 && peakLow <= 0.55f * peakHigh);
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 40f);
            }
            Check("S3 no round dust balls from the shuttle (FxManager.Dust): the fallback puffs only without the fog shader", OzShuttleDownwash.FallbackPuffs == 0 || !ship.Wash.OwnSystem);
            Log($"sounds: jet burn loop {Played(t0, "shuttle_jet_burn_loop")}, spool {Played(t0, "shuttle_spool_up")}, take-off {Played(t0, "shuttle_takeoff")}, gust {Played(t0, "shuttle_downwash_gust")}, landing {Played(t0, "shuttle_landing")}");
            Check("S3 the sounds of the flight: spool-up, take-off, the jet burn loop, the gust ring, the landing", Played(t0, "shuttle_spool_up") > 0 && Played(t0, "shuttle_takeoff") > 0 && Played(t0, "shuttle_jet_burn_loop") > 0 && Played(t0, "shuttle_downwash_gust") > 0 && Played(t0, "shuttle_landing") > 0);
        }

        // ------------------------------------------------------------------ S4: over the fort, the pyramid, a kapok cell; a 10 s hover
        IEnumerator S4()
        {
            Log("S4 over the red fort, the big pyramid, a kapok cell; a 10 s hover");
            gapMin = 999f; climbMax = 0f; measureGap = true;
            var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == me && !o.Destroyed);
            if (fort != null)
            {
                var far = W.ShuttleClamp(fort.CenterX + (fort.CenterX - s.CenterX), fort.CenterY + (fort.CenterY - s.CenterY));
                Move(far.x, far.y);
                yield return WaitFor(() => { var c = ZGame.PxToWorld(fort.CenterX, fort.CenterY); var p = ship.transform.position; return s.ShuttleState == ZShuttleState.Cruise && new Vector2(p.x - c.x, p.z - c.z).magnitude < 4f; }, 40f);
                Shot("05a_over_the_fort_h70");
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed, 40f);
            }
            var big = L16Pyramid.All.Where(p => p).OrderByDescending(p => p.size).FirstOrDefault();
            if (big) { var b = big.transform.position; Move((int)(b.x * 8f), (int)(-b.z * 8f)); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed, 50f); }
            var fw = OzFloraWind.I; OzFloraWind.Plant kap = null;
            if (fw) foreach (var p in fw.All) if (p != null && p.R && !p.Gone && p.Height * p.R.transform.lossyScale.y > 11f) { if (kap == null || Vector3.Distance(p.R.transform.position, ship.transform.position) < Vector3.Distance(kap.R.transform.position, ship.transform.position)) kap = p; }
            if (kap != null)
            {
                var k = kap.R.transform.position; Move((int)(k.x * 8f), (int)(-k.z * 8f));
                yield return WaitFor(() => { var p = ship.transform.position; return new Vector2(p.x - k.x, p.z - k.z).magnitude < 4f || s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed; }, 50f);
                Shot("05b_over_kapoks_h70");
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed, 30f);
            }
            measureGap = false;
            // a 10 s hover where it cannot land (a unit on the pad)
            var hov = Spot((x, y, why) => why == "units" || why == "building" || why == "rocks", 160);
            float bobLo = 9f, bobHi = -9f;
            if (hov != null)
            {
                Move(hov[0], hov[1]); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover, 40f);
                yield return Wait(1.5f);
                float t = 0f; while (t < 10f) { bobLo = Mathf.Min(bobLo, ship.BobNow); bobHi = Mathf.Max(bobHi, ship.BobNow); t += Time.deltaTime; yield return null; }
            }
            float amp = (bobHi - bobLo) * 0.5f;
            Log($"clearance over the fort / pyramid / kapoks: smallest gap {gapMin:F2} m ({gapWhere}); fastest climb {climbMax:F2} m/s; hover bob amplitude {amp:F2} m");
            Check("S4 nothing high ever cuts through the ship (gap >= 1.0 m, counted from 1 m up; K256e M7 keeps its footprint clear)", gapMin >= 1.0f);
            Check("S4 it climbs smoothly (at most 4.05 m/s)", climbMax <= 4.05f);
            Check("S4 it bobs in a hover (0.55-0.75 m; K256e M11: 0.7 m)", amp >= 0.55f && amp <= 0.75f);
        }

        // ------------------------------------------------------------------ S5: over the river: the red bouncing shadow
        IEnumerator S5()
        {
            Log("S5 onto the river");
            float t0 = Time.unscaledTime;
            var w = Spot((x, y, why) => why == "water", 120);
            if (w == null) { Check("S5 a water target", false); yield break; }
            Sel(); Move(w[0], w[1]);
            yield return WaitFor(() => s.ShuttleState != ZShuttleState.Hover, 5f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover, 40f);
            yield return Wait(0.8f);
            float lo = 9f, hi = -9f, red = 0f, t = 0f;
            while (t < 2f) { var sh = ship.Shadow; red = Mathf.Max(red, sh.TintNow.r); lo = Mathf.Min(lo, sh.ScaleNow); hi = Mathf.Max(hi, sh.ScaleNow); t += Time.deltaTime; yield return null; }
            Shot("10a_hover_red_shadow_h70"); Shot("03c_hover_over_water_spray_h70");
            Log($"shadow tint r {red:F2}, scale swing {hi - lo:F2}; ui_error {Played(t0, "ui_error")}, Can't land here {Played(t0, "vo_shuttle_no_land")}; water frames {OzShuttleDownwash.WaterFrames}");
            Check("S5 where it cannot land its shadow turns red (r >= 0.9 x the mark's red; K256e M4: a darkening red, never a glow) and bounces (scale swing >= 0.12)", red >= 0.9f * OzShuttleShadow.Red.r && hi - lo >= 0.12f);
            Check("S5 the error blip and \"Can't land here!\"", Played(t0, "ui_error") > 0 && (Played(t0, "vo_shuttle_no_land") > 0 || ship.LastVoice == "vo_shuttle_no_land"));
        }

        // ------------------------------------------------------------------ S6: three landings, Rachel forced: never with a robot line
        IEnumerator S6()
        {
            Log("S6 three landings with Rachel's line forced; a robot line asked while she speaks");
            OzPlayableShuttle.ProofRachelEvery = true; float t0 = Time.unscaledTime; int ok = 0, n = 0, held0 = AudioDirector.ShuttleHeld, played0 = AudioDirector.ShuttleHeldPlayed; bool asked = false;
            for (int i = 0; i < 3; i++)
            {
                var q = Pad(260, i * 120f, 120); if (q == null) continue;
                int l0 = s.ShuttleLandings; Move(q[0], q[1]);
                yield return WaitFor(() => s.ShuttleLandings > l0, 40f);
                if (i == 0) Shot("06a_touchdown_h70");
                if (i == 0)
                {   // the player selects the ship while Rachel speaks: its select line must wait for her (held, then said)
                    yield return WaitFor(() => AudioDirector.RachelLandingNow, 6f);
                    if (AudioDirector.RachelLandingNow) { yield return Wait(0.4f); AudioDirector.Say("vo_select_x_shuttle"); asked = true; }
                }
                yield return Wait(5.5f);
                n++;
                float aAt = ship.LastAlanAt, rAt = ship.LastRachelAt;
                var alan = OzSoundLog.Since(aAt - 0.05f).FirstOrDefault(e => (e.key == "vo_shuttle_landed" || e.key == "vo_shuttle_hot") && e.outcome != null && !e.outcome.StartsWith("DROP"));
                var rach = OzSoundLog.Since(aAt - 0.05f).FirstOrDefault(e => e.key == "ann_shuttle_landing" && e.outcome != null && !e.outcome.StartsWith("DROP"));
                float aLen = VoLen(alan.key, alan.clip), rStart = rach.key != null ? rach.t : -1f;
                float gap = rStart - (aAt + aLen);
                Log($"landing {i + 1}: Alan {alan.clip} at {aAt - T0:F2} for {aLen:F2} s, Rachel {(rach.key != null ? rach.clip : "none")} at {rStart - T0:F2}; gap {gap:F2} s");
                if (rach.key == null || gap >= 0.25f) ok++;
            }
            OzPlayableShuttle.ProofRachelEvery = false;
            int over = VoiceOverlaps(t0, out string worst);
            Log($"robot lines held behind her landing line +{AudioDirector.ShuttleHeld - held0}, said after it +{AudioDirector.ShuttleHeldPlayed - played0}; queue lines that went before her waiting line {AudioDirector.ShuttleQueueSkips}; overlaps {over} {worst}");
            Check("S6 Alan's and Rachel's landing lines never overlap (gap >= 0.25 s) on every landing", n > 0 && ok == n);
            Check("S6 a robot line asked while Rachel's landing line plays is held and said after her line; no alan_* line inside any ann_shuttle_landing", asked && AudioDirector.ShuttleHeld > held0 && AudioDirector.ShuttleHeldPlayed > played0 && over == 0);
        }

        /// <summary>A voice line's length as played (s): its clip by name, at the row's lowest pitch (the longest it can be).</summary>
        static float VoLen(string key, string clip)
        {
            var ad = AudioDirector.I; var d = ad && key != null ? ad.Def(key) : null;
            if (d != null && d.loaded != null) foreach (var c in d.loaded) if (c && c.name == clip) return c.length / Mathf.Max(0.3f, d.pitchMul * (1f - Mathf.Max(0f, d.pitchVar)));
            return ClipLen(clip);
        }

        /// <summary>The count of robot lines (alan_main / alan_alert, the heroes too) whose clip intervals cross a Rachel shuttle landing line
        /// in the sound log since t0 (0 = they never overlap).</summary>
        static int VoiceOverlaps(float t0, out string worst)
        {
            var land = new List<(float a, float b)>(); var robot = new List<(float a, float b, string k)>(); worst = "";
            foreach (var e in OzSoundLog.Since(t0))
            {
                if (e.src != "VO" || e.outcome == null || !e.outcome.StartsWith("PLAY")) continue;
                float len = VoLen(e.key, e.clip);
                if (e.key == AudioDirector.ShuttleLandingLine) land.Add((e.t, e.t + len));
                else if (e.outcome.Contains(" alan_")) robot.Add((e.t, e.t + len, e.key));
            }
            int n = 0;
            foreach (var l in land) foreach (var r in robot) if (r.a < l.b - 0.02f && r.b > l.a + 0.02f) { n++; worst = $"({r.k} {r.a - t0:F2}..{r.b - t0:F2} over Rachel {l.a - t0:F2}..{l.b - t0:F2})"; }
            return n;
        }

        /// <summary>K256e (FIX_LIST M8): the worst of the four pads' soles against the deck under each (m; 9 when not all four are over it).</summary>
        float PadsOverDeck(int stationId)
        {
            var pads = new[] { new Vector3(2.228f, 0f, 1.029f), new Vector3(-2.168f, 0f, 1.079f), new Vector3(1.829f, 0f, -2.843f), new Vector3(-1.771f, 0f, -2.838f) };
            float worst = 0f; int n = 0;
            foreach (var pl in pads) { var pw = ship.transform.TransformPoint(pl); if (!OzStationCrane.DeckY(stationId, pw, out float dy)) continue; n++; worst = Mathf.Max(worst, Mathf.Abs(pw.y - dy)); }
            return n == 4 ? worst : 9f;
        }

        /// <summary>K256e: the take's file tag (as the logs').</summary>
        string TakeTag() => (mode.StartsWith("G") ? "_G" : mode.StartsWith("B") ? "_B" : mode.StartsWith("P") ? "_P_" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name : "") + (clip ? "_clip" : "");

        static float ClipLen(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return 1.5f;
            var c = Resources.Load<AudioClip>("OpenZ/Audio/Shuttle/" + clip); return c ? c.length : 1.5f;
        }

        // ------------------------------------------------------------------ S7: a forced rough landing
        IEnumerator S7()
        {
            Log("S7 a rough landing (forced): a crash landing, then righted level (Rachel's line forced too, for S8's voices)");
            float t0 = Time.unscaledTime; int r0 = ship.RoughLandings; int ramp0 = OzPlayableShuttle.RampWhileRighting; bool exitEarly = false, exitLate = false; double levelledAt = -1; int scr0 = OzShuttleTouchdown.Scrapes;
            OzPlayableShuttle.ProofRoughNext = true;                                                  // the rules' switch: the next landing is a crash landing
            OzPlayableShuttle.ProofRachelEvery = true; voiceT0 = Time.unscaledTime;                    // her joke after the crash, while S8's EXIT comes
            var q = Pad(260, 45f, 120); if (q == null) { Check("S7 a landing place", false); yield break; }
            Move(q[0], q[1]);
            yield return WaitFor(() => OzShuttleTouchdown.Contacts > 0 && ship.Touch.Active, 40f);
            Shot("07a_rough_first_contact_h70");
            levelledAt = s.ShuttleLevelledAt; exitEarly = s.CanEjectCrew() || s.Passengers.Count == 0;   // K426: EXIT is taken from the first contact (the ramp still waits for the level)
            yield return WaitFor(() => ship.Touch.Active && OzShuttleTouchdown.TiltHeld > 0.3f, 3f);
            Shot("07b_rough_resting_tilted_h70");
            yield return WaitFor(() => W.Time >= levelledAt + 0.05, 6f); exitLate = s.CanEjectCrew() || s.Passengers.Count == 0;
            yield return WaitFor(() => !ship.Touch.Active, 6f);
            yield return Wait(0.3f); Shot("07c_rough_levelled_h70");
            Log(OzShuttleTouchdown.Log.ToString().TrimEnd());
            Log($"rough landings {ship.RoughLandings - r0}; tilt held {OzShuttleTouchdown.TiltHeld:F2} s; levelled in {OzShuttleTouchdown.LevelTook:F2} s; worst pad {OzShuttleTouchdown.WorstPadIn * 100f:F1} cm in; wreck_crash {Played(t0, "wreck_crash")}, level hydraulic {Played(t0, "shuttle_level_hydraulic")}");
            string lg = OzShuttleTouchdown.Log.ToString(); int iLow = lg.IndexOf("(low side)"), iHigh = lg.IndexOf("(high side)");
            Check("S7 a rough landing: the low side's pads first, then the high side", ship.RoughLandings > r0 && iLow >= 0 && iHigh > iLow);
            Check("S7 the crash sound at the first contact", Played(t0, "wreck_crash") > 0 || lg.Contains("wreck_crash"));
            Log($"the crash: first slam {OzShuttleTouchdown.FirstSlam:F2} m/s, the bounce {OzShuttleTouchdown.BounceM:F2} m, at rest tilted {OzShuttleTouchdown.RestTiltDeg:F1} deg (over 6 deg for {OzShuttleTouchdown.TiltHeld:F2} s), slide {OzShuttleTouchdown.SlideM:F2} m, scrape {OzShuttleTouchdown.Scrapes - scr0}, dust puffs {OzShuttleTouchdown.SkidPuffs}");
            Check("S7 it reads as a CRASH: the first slam >= 4.5 m/s, it bounces >= 0.15 m, slides 1.0-1.2 m with its scrape", OzShuttleTouchdown.FirstSlam >= 4.5f && OzShuttleTouchdown.BounceM >= 0.15f && OzShuttleTouchdown.SlideM >= 1.0f && OzShuttleTouchdown.SlideM <= 1.21f && OzShuttleTouchdown.Scrapes > scr0);
            Check("S7 it rests crooked (>= 6 deg, about 8) for >= 0.7 s (K426: 0.9 before the shorter rest), then levels within 1.25 s", OzShuttleTouchdown.RestTiltDeg >= 6f && OzShuttleTouchdown.TiltHeld >= 0.7f && OzShuttleTouchdown.LevelTook >= 0f && OzShuttleTouchdown.LevelTook <= 1.25f);
            Check("S7 no pad ever more than 3 cm under the ground", OzShuttleTouchdown.WorstPadIn <= 0.031f);
            Check("S7 the levelling's hydraulic", Played(t0, "shuttle_level_hydraulic") > 0);
            Log($"crash landing on the rules' clock: drawn level {OzShuttleTouchdown.LevelOff:F3} s off the rules' moment; pitch peak {OzShuttleTouchdown.PitchPeak:F1} deg; skid dust puffs {OzShuttleTouchdown.SkidPuffs}; the ramp moved while righting {OzPlayableShuttle.RampWhileRighting - ramp0} frames; EXIT while righting {exitEarly}, after {exitLate}");
            Check("S7 the rules' crash landing: it rocks in both axes (pitch >= 1 deg), slides with dust, stands level on the rules' moment (<= 0.05 s)", OzShuttleTouchdown.PitchPeak >= 1f && OzShuttleTouchdown.SkidPuffs > 0 && OzShuttleTouchdown.LevelOff >= 0f && OzShuttleTouchdown.LevelOff <= 0.05f);
            Check("S7 the door waits: no ramp until it stands level again (K426: EXIT is taken from the first contact and carried out then)", OzPlayableShuttle.RampWhileRighting == ramp0 && exitEarly && exitLate);
        }

        // ------------------------------------------------------------------ S8: EXIT down the ramp
        IEnumerator S8()
        {
            Log("S8 EXIT");
            if (s.Passengers.Count == 0) { OzPlayableShuttle.ProofRachelEvery = false; Check("S8 a squad aboard for the exit", false); yield break; }
            float t0 = Time.unscaledTime; int shuts0 = s.ShuttleRampShuts, n = s.Passengers.Count;
            yield return WaitFor(() => s.CanEjectCrew(), 5f);
            Check("S8 EXIT is taken", W.Eject(me, s));
            yield return WaitFor(() => s.ShuttleRamp01 > 0.8 * ZWorld.ShuttleExitRamp01, 3f); Shot("08a_exit_ramp_dropping_seated_h70");   // K422: the squad gets up at ZWorld.ShuttleExitRamp01 of the swing
            int seated = BoardingView.I ? BoardingView.I.ShuttleWaitingNow : 0;
            yield return WaitFor(() => s.Passengers.Count == 0, 4f);
            yield return Wait(2.0f); Shot("08b_exit_down_the_ramp_h70");
            yield return WaitFor(() => s.ShuttleRampShuts > shuts0, 12f);
            yield return Wait(0.6f); Shot("08c_exit_standing_in_line_ramp_shut_h70");
            var outs = W.Objects.Where(o => o.Owner == me && o.Kind == ZKind.Robot && !o.Destroyed && o.KillAt < 0 && Vector3.Distance(ZGame.PxToWorld(o.CenterX, o.CenterY), ZGame.PxToWorld(s.CenterX, s.CenterY)) < 16f).ToList();
            int inside = outs.Count(o => Inside(V(o) ? V(o).transform.position : ZGame.PxToWorld(o.CenterX, o.CenterY), 0.3f));
            Log($"EXIT: {n} aboard, seated waiting while the ramp came down {seated}; out {outs.Count}, inside the hull outline {inside}; Everybody out {Played(t0, "vo_shuttle_unload")}, door open {Played(t0, "shuttle_door_open")}, latch {Played(t0, "latch")}");
            Check("S8 they wait seated while the ramp comes down, then walk out down it and stand behind the ship", seated > 0 && outs.Count >= n && inside == 0 && s.ShuttleRampShuts > shuts0);
            Check("S8 the exit's sounds: Everybody out!, the door, the latch", Played(t0, "vo_shuttle_unload") > 0 && Played(t0, "shuttle_door_open") > 0 && Played(t0, "latch") > 0);
            ExitOrder(t0);                                                                               // no latch while the ramp stands open, no jump-landing thud (OzShuttlePolishProof.Order.cs)
            OzPlayableShuttle.ProofRachelEvery = false;
            int over = VoiceOverlaps(voiceT0, out string worst);
            Log($"S7-S8 voices: Rachel's lines {Played(voiceT0, AudioDirector.ShuttleLandingLine)}, robot lines held {AudioDirector.ShuttleHeld}, said after her {AudioDirector.ShuttleHeldPlayed}; overlaps {over} {worst}");
            Check("S8 no robot line (Touchdown, Everybody out, select / order lines) over Rachel's landing line from the crash to the exit", over == 0);
        }

        // ------------------------------------------------------------------ S9: a take-off beside trees
        IEnumerator S9()
        {
            Log("S9 a take-off beside a tree cluster, a low pass");
            float t0 = Time.unscaledTime; int r0 = OzFloraDownwash.Rustles;
            var fw = OzFloraWind.I; int[] near = null;
            if (fw && fw.Crowns.Count > 0)
            {
                var c = fw.Crowns.OrderBy(k => Vector2.Distance(new Vector2(k.x, k.z), new Vector2(ship.transform.position.x, ship.transform.position.z))).First();
                near = Spot((x, y, why) => why == "" && Vector2.Distance(new Vector2(x / 8f, -y / 8f), new Vector2(c.x, c.z)) < 10f, 0);
            }
            if (near != null) { Move(near[0], near[1]); yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 50f); }
            var away = Pad(320, 200f, 120); if (away != null) Move(away[0], away[1]);
            yield return WaitFor(() => ship.FeetHeight > 3f, 8f);
            Shot("11a_takeoff_trees_leaning_h70");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 40f);
            Log($"crowns hit hard {OzFloraDownwash.HitNow}, rustles {OzFloraDownwash.Rustles - r0}, sound lines {Played(t0, "shuttle_tree_rustle")}");
            Check("S9 the downwash shakes the crowns (the tree rustle)", OzFloraDownwash.Rustles > r0 && Played(t0, "shuttle_tree_rustle") > 0);
        }

        // ------------------------------------------------------------------ S10: through the parrots
        IEnumerator S10()
        {
            Log("S10 through the parrots' circuit");
            float t0 = Time.unscaledTime; birdMin = 999f; measureBirds = true; int f0 = JungleFauna.ShipFrights + JungleFauna.ShipFlyerBursts;
            for (int i = 0; i < JungleFaunaData.ParrotRoutes.Length && i < 2; i++)
            {
                var r = JungleFaunaData.ParrotRoutes[i][0];
                var q = W.ShuttleClamp((int)(r.x * 8f), (int)(-r.y * 8f)); Move(q.x, q.y);
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Landed, 50f);
                yield return Wait(10f);
            }
            measureBirds = false;
            Log($"closest bird to the ship's keep-out {birdMin:F2} m; frights / bursts +{JungleFauna.ShipFrights + JungleFauna.ShipFlyerBursts - f0}; {JungleFauna.ShipStatus()}; {JungleFauna.ShipGuardStatus()}");
            Check("S10 no parrot ever inside the ship's keep-out (clearance >= 3 m)", birdMin >= 3f);
            Check("S10 the parrots take fright from the ship", JungleFauna.ShipFrights + JungleFauna.ShipFlyerBursts > f0);
        }

        // ------------------------------------------------------------------ S11: R to the repair station: down ON its deck, the crane away
        IEnumerator S11(ZObject station)
        {
            Log("S11 half health, R: the repair station (the crane rolls away, a level hover turn, down ON the deck, the crane back after it left)");
            if (station == null) { Check("S11 a repair station", false); yield break; }
            float t0 = Time.unscaledTime; int rough0 = ship.RoughLandings, crane0 = OzPlayableShuttle.CraneInTheWay, aways0 = OzStationCrane.Aways, backs0 = OzStationCrane.Backs;
            s.SetHealth(s.MaxHealth / 2); Sel();
            Check("S11 R is taken", input.SendToRepair());
            // the hover turn over the deck: at most 45 deg/s (the rules' time), level (no bank turning on the spot)
            float yawRate = 0f, turnBank = 0f, lastYaw = ship.YawDeg; bool turning = false, crane = false;
            yield return WaitFor(() =>
            {
                float dt = Time.deltaTime, gs = Mathf.Max(0.05f, g.gameSpeed);
                if (s.ShuttleDeckTurning) { turning = true; if (dt > 0f) yawRate = Mathf.Max(yawRate, Mathf.Abs(Mathf.DeltaAngle(lastYaw, ship.YawDeg)) / (dt * gs)); turnBank = Mathf.Max(turnBank, Mathf.Abs(ship.TurnBankDeg)); }
                if (OzStationCrane.SlideK(station.Id) > 0.3f && !crane) { crane = true; Shot("13a_crane_rolling_away_ship_turning_h70"); }
                lastYaw = ship.YawDeg; return s.ShuttleState == ZShuttleState.Descend;
            }, 90f);
            float slideAtDescent = OzStationCrane.SlideK(station.Id);
            yield return Wait(0.9f); Shot("13b_down_onto_the_deck_h70");
            yield return WaitFor(() => s.ShuttleOnRepairPad, 20f);
            yield return Wait(1.0f); Shot("13c_on_the_deck_repairing_h70"); yield return null;
            if (!clip && rts) { rts.SetHeightImmediate(32f); yield return Wait(0.3f); Shot("13c_extra_close"); yield return null; rts.SetHeightImmediate(70f); }
            s.ShuttleDeckPoint(station, out int dx, out int dy);
            float off = Mathf.Sqrt((s.CenterX - dx) * (s.CenterX - dx) + (s.CenterY - dy) * (s.CenterY - dy));
            float yawOff = Mathf.Abs(Mathf.DeltaAngle(ship.YawDeg, ZWorld.ShuttleDeckHeadingDeg + 90f));
            int hits = OzStationCrane.ShipHits(station.Id, ship.transform, out float deep, out string where);
            OzStationCrane.DeckY(station.Id, ship.transform.position, out float deckY);
            float stand = ship.transform.position.y - deckY;
            bool voices = OzSoundLog.Since(t0).Any(e => (e.key == "vo_shuttle_landed" || e.key == "vo_shuttle_hot" || e.key == "ann_shuttle_landing") && e.outcome != null && !e.outcome.StartsWith("DROP"));
            Log($"S11 hover turn: {(turning ? "yes" : "NO")}, fastest {yawRate:F1} deg/s (rules' time), its bank at most {turnBank:F2} deg; the crane at the descent's start {slideAtDescent:F2} of its way; frames under 7.5 m before it was away {OzPlayableShuttle.CraneInTheWay - crane0}");
            Log($"S11 on the deck: {off:F1} px from the deck point, drawn yaw {ship.YawDeg:F1} (the deck heading's {ZWorld.ShuttleDeckHeadingDeg + 90f:F1}), root {stand:F3} m over the deck, ship vertices inside the station {hits} (worst {deep:F2} m {where}); landing voices {voices}; starting repair {Played(t0, "ann_starting_repair")}; rough {ship.RoughLandings - rough0}; crane sounds {Played(t0, "shuttle_station_crane")}");
            Check("S11 over the station it turns level onto the deck heading (at most 30 deg/s: K256e M8, its turn bank <= 2 deg) while the crane rolls away", turning && yawRate <= 32f && turnBank <= 2f);
            Check("S11 the crane is all the way out of its way before it comes down (never under 7.5 m with the crane in the way), with its sound", slideAtDescent >= 0.999f && OzPlayableShuttle.CraneInTheWay == crane0 && OzStationCrane.Aways > aways0 && Played(t0, "shuttle_station_crane") > 0);
            float padsOff = PadsOverDeck(station.Id);                                                           // K256e M8: each pad's sole against the deck under it (the deck's middle is a lower lane: the root over it is no measure)
            Check($"S11 it lands ON the deck: on the rules' deck point (<= 2 px) and heading (<= 1 deg), standing on the deck (every pad's sole within 6 cm of the deck under it: {padsOff * 100f:F1} cm)", s.ShuttleOnRepairPad && off <= 2f && yawOff <= 1f && padsOff <= 0.06f);
            Check("S11 no part of the ship inside the station (its tower, boxes, the crane where it is now, the deck): 0 vertices", hits == 0);
            Check("S11 no landing line, no Rachel joke, never rough on the station, and no \"starting repair\" (K256e M8: silent for the shuttle)", !voices && ship.RoughLandings == rough0 && Played(t0, "ann_starting_repair") == 0);
            yield return WaitFor(() => !s.ShuttleOnRepairPad, 15f);
            yield return WaitFor(() => s.ShuttleCraneStation < 0, 30f);
            float tb = Time.unscaledTime;
            yield return WaitFor(() => !OzStationCrane.Split(station.Id), 6f);
            Shot("13d_crane_back_ship_gone_h70");
            Log($"S11 the crane back {Time.unscaledTime - tb:F2} s after the ship let it go; the station's mesh whole again {!OzStationCrane.Split(station.Id)}; moves away +{OzStationCrane.Aways - aways0}, back +{OzStationCrane.Backs - backs0}");
            Check("S11 after it has left the crane rolls back (with its sound) and the station is whole again", OzStationCrane.Backs > backs0 && !OzStationCrane.Split(station.Id) && OzStationCrane.SlideK(station.Id) <= 0f && Played(tb, "shuttle_station_crane") > 0);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleOnRepairPad, 80f);
        }

        // ------------------------------------------------------------------ S12: at night
        IEnumerator S12()
        {
            Log("S12 night (day clock 0.62): take-off and landing");
            float tNight = Time.unscaledTime;
            if (dn) { dn.timeOfDay = 0.62f; dn.Apply(0.62f); }
            yield return Wait(1.5f);
            int on0 = OzShuttleSearchlights.OnFrames, real0 = OzShuttleSearchlights.RealFrames, pool0 = OzShuttleSearchlights.PoolFrames; float t0 = Time.unscaledTime;
            var q = Pad(300, 300f, 120); if (q != null) Move(q[0], q[1]);
            yield return WaitFor(() => ship.FeetHeight > 3f, 8f); yield return Wait(0.5f);
            Shot("04a_night_takeoff_searchlights_h70");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 40f); yield return Wait(1.2f);
            Shot("04b_night_landing_h70");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 20f);
            Log($"searchlights: on frames {OzShuttleSearchlights.OnFrames - on0}, a real light {OzShuttleSearchlights.RealFrames - real0}, the pool {OzShuttleSearchlights.PoolFrames - pool0}; switch-on sound {Played(tNight, "ngt_searchlight_on")}");
            Check("S12 at night both belly searchlights burn in the air (a real light granted or the ground pool drawn)", OzShuttleSearchlights.OnFrames - on0 > 30 && (OzShuttleSearchlights.RealFrames - real0 > 0 || OzShuttleSearchlights.PoolFrames - pool0 > 0));
            Check("S12 the searchlights' switch sound", Played(tNight, "ngt_searchlight_on") > 0);
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); }
            yield return Wait(1f);
        }

        // ------------------------------------------------------------------ S15: the testing overlay (P): more shuttles, each a full one
        static RectTransform FindUi(string panel, string cell)
        {
            var hud = OpenZHud.I; if (!hud || !hud.Root) return null;
            foreach (var t in hud.Root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == panel) foreach (var c in t.GetComponentsInChildren<RectTransform>(true)) if (c.name == cell) return c;
            return null;
        }

        /// <summary>A free 6 x 6 m pad (ShuttlePadFree) 20 - 60 m from `near` (world), the nearest such; null = none.</summary>
        int[] FreePad(Vector3 near, int minPx = 200)
        {
            long best = long.MaxValue; int[] r = null; int cx = Mathf.RoundToInt(near.x * 8f), cy = Mathf.RoundToInt(-near.z * 8f);
            for (int ty = 4; ty < W.Map.height - 4; ty++)
                for (int tx = 4; tx < W.Map.width - 4; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best || d < (long)minPx * minPx || d > 480L * 480L) continue;
                    if (W.ShuttlePadFree(x, y, null, out _)) { best = d; r = new[] { x, y }; }
                }
            return r;
        }

        IEnumerator DragIn(RectTransform cell, Vector2 drop)
        {
            Vector2 c = RectTransformUtility.WorldToScreenPoint(null, cell.TransformPoint(cell.rect.center));
            MouseEv(c); yield return null; yield return null; MouseEv(c, MouseButton.Left); yield return null; yield return null;
            for (int k = 1; k <= 8; k++) { MouseEv(Vector2.Lerp(c, drop, k / 8f), MouseButton.Left); yield return null; }
            MouseEv(drop); yield return null; yield return null; yield return null;
        }

        IEnumerator S15()
        {
            Log("S15 the testing overlay (P): a shuttle of each side dragged onto the map, each drawn in full; the player's one flown and landed");
            if (!OzCheatSpawner.I) { Check("S15 the testing overlay is there (the dev flag on)", false); yield break; }
            int n0 = OzCheatShuttle.Spawned, v0 = OzPlayableShuttle.Spawned;
            var keepS = s; var keepShip = ship; Team mine = s != null ? s.Owner : ZWorld.ShuttleTeam, theirs = mine == Team.Red ? Team.Blue : Team.Red;
            var cam = Camera.main; var mid = rts ? rts.focus : (ship ? ship.transform.position : Vector3.zero);
            follow = false;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.P)); yield return null; yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; yield return null;
            Check("S15 P opens the testing overlay", OzCheatSpawner.Visible);
            var made = new List<ZObject>();
            foreach (var (col, team) in new[] { ("Cheat" + (mine == Team.Red ? "Red" : "Blue"), mine), ("Cheat" + (theirs == Team.Red ? "Red" : "Blue"), theirs) })
            {
                var cell = FindUi(col, "Cell_" + ZWorld.ShuttleType);
                if (!cell) { Check($"S15 the {col} column has the shuttle's tile", false); continue; }
                var raw = cell.GetComponentInChildren<UnityEngine.UI.RawImage>(true);
                Check($"S15 the {col} column's shuttle tile shows the shuttle's own portrait", raw && raw.texture && raw.texture.name.Contains("shuttle"));
                var pad = FreePad(mid, made.Count == 0 ? 160 : 260); if (pad == null) { Check("S15 open ground to drop it on", false); continue; }
                if (rts) { var pw = ZGame.PxToWorld(pad[0], pad[1]); rts.focus = new Vector3((pw.x + mid.x) * 0.5f, 0f, (pw.z + mid.z) * 0.5f); yield return Wait(0.4f); }
                var wp = ZGame.PxToWorld(pad[0], pad[1]); wp.y = g.GroundY(wp.x, wp.z);
                var sp = cam.WorldToScreenPoint(wp);
                int before = OzCheatShuttle.Spawned;
                yield return DragIn(cell, new Vector2(sp.x, sp.y)); yield return Wait(0.5f);
                var o = OzCheatShuttle.Spawned > before ? OzCheatShuttle.Last : null;
                var sv = o != null ? OzPlayableShuttle.Of(o) : null;
                bool bound = o != null && g.Views.TryGetValue(o.Id, out var uv) && uv;
                Log($"S15 {team} shuttle dragged in: #{(o != null ? o.Id : -1)} at px {(o != null ? o.CenterX + "," + o.CenterY : "-")}, drawn {(sv ? "yes" : "NO")}{(sv ? $" (scene ship {sv.SceneShip}, door {(sv.Door ? "fitted" : "MISSING")}, foot lamps on the legs {sv.FootParts}, view {(bound ? "bound" : "MISSING")})" : "")}; refused {OzCheatShuttle.Refused} ({OzCheatShuttle.LastWhy})");
                Check($"S15 the dragged {team} shuttle is a full one: the rules' unit with its own view (UnitView, the rear ramp door, jets, lights, foot lamps), not a half-made one",
                      o != null && o.IsShuttle && o.Owner == team && sv && !sv.SceneShip && sv.Door && sv.Jets != null && sv.FootParts > 0 && bound);
                if (o != null) made.Add(o);
            }
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(Key.P)); yield return null; yield return null;
            InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); yield return null; yield return null;
            Shot("15a_panel_shuttles_dropped_h70");
            // the player's one flies and lands like level 16's own ship
            var mineS = made.FirstOrDefault(o => o.Owner == mine);
            if (mineS != null && OzPlayableShuttle.Of(mineS))
            {
                s = mineS; ship = OzPlayableShuttle.Of(mineS); follow = true; Sel();
                int air0 = OzShuttleJets.AirFrames; float f0 = Time.unscaledTime;
                var q = Pad(320, 0f, 180);
                if (q != null) Check("S15 the dragged shuttle takes a move order", Move(q[0], q[1]));
                yield return WaitFor(() => s.ShuttleAirborne && ship.FeetHeight > 4f, 15f);
                Shot("15b_panel_shuttle_flying_h70");
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 50f);
                yield return Wait(2.0f);
                Log($"S15 its flight: airborne frames {OzShuttleJets.AirFrames - air0}, landings {s.ShuttleLandings}; spool {Played(f0, "shuttle_spool_up")}, take-off {Played(f0, "shuttle_takeoff")}, jet loop {Played(f0, "shuttle_jet_burn_loop")}, landing {Played(f0, "shuttle_landing")}, Touchdown {Played(f0, "vo_shuttle_landed") + Played(f0, "vo_shuttle_hot")}");
                Check("S15 it flies and lands with its jets, its sounds and its landing line", OzShuttleJets.AirFrames - air0 > 30 && s.ShuttleLandings > 0 && Played(f0, "shuttle_takeoff") > 0 && Played(f0, "shuttle_landing") > 0 && Played(f0, "vo_shuttle_landed") + Played(f0, "vo_shuttle_hot") > 0);
                Shot("15c_panel_shuttle_landed_h70");
            }
            else Check("S15 the player's dragged shuttle to fly", false);
            s = keepS; ship = keepShip; follow = s != null; if (s != null) Sel();
            Log($"S15 shuttles dragged in {OzCheatShuttle.Spawned - n0}, views made from the unit prefab {OzPlayableShuttle.Spawned - v0}, drawn shuttles now {OzPlayableShuttle.Count}");
        }

        // ------------------------------------------------------------------ S14 (A): shot down in the air over a blue group
        IEnumerator S14()
        {
            Log("S14 a squad aboard, flown over a blue group, shot down in the air");
            if (s.Passengers.Count == 0)
            {
                var lead = W.Objects.FirstOrDefault(o => !o.Destroyed && o.Owner == me && o.Kind == ZKind.Robot && o.Leader == null && Vector3.Distance(ZGame.PxToWorld(o.CenterX, o.CenterY), ZGame.PxToWorld(s.CenterX, s.CenterY)) < 30f);
                if (lead == null) lead = Squad("grunt", (s.CenterX >> 4) - 4, (s.CenterY >> 4), me);
                Enter(lead); yield return WaitFor(() => s.Passengers.Count > 0, 25f); yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut, 12f);
            }
            int aboard = s.Passengers.Count; int lost0 = W.ShuttlePassengersLost; int bodies0 = BoardingView.ShuttleBodies; BodyMark();
            var q = Pad(320, 30f, 90); if (q == null) { Check("S14 a target", false); yield break; }
            int mx = ((s.CenterX + q[0]) / 2) >> 4, my = ((s.CenterY + q[1]) / 2) >> 4;
            var foes = new List<ZObject> { Squad("tough", mx + 2, my + 3, them), Squad("tough", mx - 2, my + 3, them), Vehicle("light", mx, my + 5, them) };
            foreach (var f in foes) if (f != null) W.SendWaypoints(them, f, new[] { new ZWaypoint { mode = WpMode.Attack, refId = s.Id, x = s.CenterX, y = s.CenterY, attackTo = true } });
            s.SetHealth(s.MaxHealth / 3); Sel();
            float t0 = Time.unscaledTime; Move(q[0], q[1]);
            yield return WaitFor(() => s.Destroyed && ship.BrokenUp, 40f);
            if (!s.Destroyed) { Log("not shot down in 40 s: destroyed by the check"); s.SetHealth(1); yield return WaitFor(() => s.ShuttleAirborne, 10f); s.DamageHealth(s.Health); W.UpdateObjectHealth(s); yield return WaitFor(() => ship.BrokenUp, 5f); }
            follow = false;
            yield return Wait(0.5f); Shot("14a_air_break_pieces_flying_h70");
            yield return WaitFor(() => OzShuttleBreakup.LastAllRested(), 8f);
            Log($"S14 the pieces rested {Time.unscaledTime - t0:F1} s after the hit");
            yield return Wait(1.5f); Shot("14b_air_break_pieces_landed_burning_h70");
            yield return BreakChecks("S14", t0, aboard, lost0, bodies0, true);
        }

        IEnumerator BreakChecks(string tag, float t0, int aboard, int lost0, int bodies0, bool air)
        {
            int pieces = OzShuttleBreakup.Shown(); int sh = 0; foreach (var r in ship.GetComponentsInChildren<Renderer>(true)) if (r.enabled && r.gameObject.activeInHierarchy && r.gameObject.name.StartsWith("SH_")) sh++;
            int deep = OzShuttleBreakup.ThroughEachOther(out float worst, out string which); int bad = OzShuttleBreakup.BadRests(out string bw);
            Log($"{tag}: {(OzShuttleBreakup.LastInAir ? "in the air" : "on the ground")}, pieces shown {pieces}, SH_ parts still drawn {sh}, all rested {OzShuttleBreakup.LastAllRested()}, worst overlap {worst * 100f:F1} cm ({which}), floating / sunk {bad} ({bw}); lost {W.ShuttlePassengersLost - lost0} of {aboard}, bodies {BoardingView.ShuttleBodies - bodies0}; crash {Played(t0, "turret")}, wreck_crash {Played(t0, "wreck_crash")}, clanks {Played(t0, "dst_debris_metal")}");
            Check(tag + " it falls apart into >= 12 modelled pieces; nothing of the whole ship is drawn after", pieces >= 12 && sh == 0);
            Check(tag + " the pieces come to rest within 8 s, lying properly (overlap <= 3 cm, none floating or sunk)", OzShuttleBreakup.LastAllRested() && worst <= 0.031f && bad == 0);
            Check(tag + " the pieces crash and clank as they land", Played(t0, "turret") + Played(t0, "wreck_crash") > 0 && Played(t0, "dst_debris_metal") > 0);
            Check(tag + " everyone aboard is lost with it (rule), their bodies tumble out", W.ShuttlePassengersLost - lost0 == aboard && aboard > 0 && BoardingView.ShuttleBodies - bodies0 >= aboard);
            yield return BodyChecks(tag, aboard, air);
        }

        int thud0, whoosh0;
        void BodyMark() { thud0 = OzBodySounds.ThudAsked; whoosh0 = OzBodySounds.WhooshAsked; }

        /// <summary>The squad's bodies fell out among the pieces as the game's thrown bodies and lie on the ground with them.</summary>
        IEnumerator BodyChecks(string tag, int aboard, bool air)
        {
            yield return WaitFor(() => OzShuttleCrewFall.LandedNow() >= OzShuttleCrewFall.Last.Count, 8f);
            yield return Wait(0.5f);
            int n = OzShuttleCrewFall.Last.Count, rag = 0; foreach (var b in OzShuttleCrewFall.Last) if (b && b.Ragdoll) rag++;
            int fl = OzShuttleCrewFall.Floating(0.35f, out float worst); float far = OzShuttleCrewFall.FarthestFromPieces();
            float fell = 0f; foreach (var b in OzShuttleCrewFall.Last) if (b) fell = Mathf.Max(fell, b.FellM);
            Log($"{tag} bodies: {n} thrown ({rag} ragdolls), landed {OzShuttleCrewFall.LandedNow()}, on a piece {OzShuttleCrewFall.OnPiece}, pushed out of a piece {OzShuttleCrewFall.PushedOut}, floating {fl} (worst {worst:F2} m), farthest from the pieces' middle {far:F1} m, fell up to {fell:F1} m; whooshes asked +{OzBodySounds.WhooshAsked - whoosh0}, thuds asked +{OzBodySounds.ThudAsked - thud0}");
            Check(tag + " the bodies fall out among the pieces and lie on the ground with them (all landed, none floating, within 16 m of the pieces)", n >= aboard && OzShuttleCrewFall.LandedNow() >= n && fl == 0 && far <= 16f);
            Check(tag + " the thrown bodies are heard: the whoosh as they leave, the thud as they land", (!air || OzBodySounds.WhooshAsked > whoosh0) && OzBodySounds.ThudAsked > thud0);
        }

        // ------------------------------------------------------------------ S13 (B): destroyed on the ground with a squad aboard
        IEnumerator StageGroundDeath()
        {
            Log("S13 a squad aboard, destroyed on the ground by a blue heavy tank");
            if (clip) TakeLog("CAPTION a squad boards the landed shuttle through its rear ramp");
            var sq = Squad("grunt", (s.CenterX >> 4) + 3, (s.CenterY >> 4) + 4, me); yield return Wait(0.6f);
            Enter(sq); yield return WaitFor(() => s.Passengers.Count > 0, 30f); yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut, 15f);
            int aboard = s.Passengers.Count, lost0 = W.ShuttlePassengersLost, bodies0 = BoardingView.ShuttleBodies; BodyMark();
            Log($"S13 {aboard} aboard, the ramp shut; a blue heavy tank attacks the landed ship");
            if (clip) TakeLog("CAPTION destroyed on the ground with the squad aboard: everyone inside is lost");
            var tank = Vehicle("heavy", (s.CenterX >> 4) + 10, (s.CenterY >> 4), them);
            if (tank != null) W.SendWaypoints(them, tank, new[] { new ZWaypoint { mode = WpMode.Attack, refId = s.Id, x = s.CenterX, y = s.CenterY, attackTo = true } });
            s.SetHealth(Mathf.Max(1, s.MaxHealth / 10));
            float t0 = Time.unscaledTime;
            yield return WaitFor(() => ship.BrokenUp, 30f);
            if (!ship.BrokenUp) { Log("not destroyed in 30 s: destroyed by the check"); s.DamageHealth(s.Health); W.UpdateObjectHealth(s); yield return WaitFor(() => ship.BrokenUp, 5f); }
            follow = false;
            yield return WaitFor(() => OzShuttleBreakup.LastAllRested(), 8f);
            yield return Wait(1.0f); Shot("14c_ground_break_h70");
            yield return BreakChecks("S13", t0, aboard, lost0, bodies0, false);
            if (clip) yield return Wait(2.5f);                                                          // clip pace: the wreck and the bodies stay in the take a moment
        }

        // ------------------------------------------------------------------ G: the weapons gallery
        struct Gun { public string key, label, kind, type; public Gun(string k, string l, string kd, string t) { key = k; label = l; kind = kd; type = t; } }
        static readonly Gun[] Guns =
        {
            new Gun("grunt", "grunt rifles", "squad", "grunt"), new Gun("psycho", "psycho machine guns", "squad", "psycho"),
            new Gun("sniper", "sniper rifles", "squad", "sniper"), new Gun("tough", "tough bazookas", "squad", "tough"),
            new Gun("pyro", "pyro flamethrowers", "squad", "pyro"), new Gun("laser", "laser rifles", "squad", "laser"),
            new Gun("electrobot", "electrobot tesla bolts", "squad", "electrobot"),
            new Gun("jeep", "jeep gun", "vehicle", "jeep"), new Gun("light", "light tank", "vehicle", "light"),
            new Gun("medium", "mech rockets", "vehicle", "medium"), new Gun("heavy", "heavy tank", "vehicle", "heavy"),
            new Gun("missile_launcher", "missile launcher salvo", "vehicle", "missile_launcher"),
            new Gun("apc crew tough", "APC crew (toughs at the hatches)", "apc", "tough"),
            new Gun("gatling", "gatling gun", "cannon", "gatling"), new Gun("gun", "gun cannon", "cannon", "gun"),
            new Gun("howitzer", "howitzer", "cannon", "howitzer"), new Gun("missile_cannon", "missile cannon", "cannon", "missile_cannon"),
            new Gun("grenade grunt", "grunt grenades", "grenades", "grunt"),
            new Gun("fort gun", "the fort's own gun", "fort", "fort"),
        };
        /// <summary>The kills, each after a fresh start of the level with a squad aboard (a rocket salvo, a howitzer shell, the fort's gun, rifle fire).</summary>
        static readonly Gun[] KillGuns =
        {
            new Gun("missile_launcher", "a rocket salvo", "vehicle", "missile_launcher"), new Gun("howitzer", "a howitzer shell", "cannon", "howitzer"),
            new Gun("fort gun", "the fort's gun", "fort", "fort"), new Gun("grunt", "rifle fire", "squad", "grunt"),
        };
        bool patrolOn, patrolKick; int patrolCx, patrolCy; Vector2 patrolU; int patrolSign = 1;
        readonly List<ZObject> gallery = new List<ZObject>();

        ZObject Cannon(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject("cannon", type, tx * 16, ty * 16, t); if (o != null) g.SpawnUnitView(o);
            return o;
        }
        void Attack(ZObject o) { if (o != null && !o.Destroyed && s != null) W.SendWaypoints(them, o, new[] { new ZWaypoint { mode = WpMode.Attack, refId = s.Id, x = s.CenterX, y = s.CenterY, attackTo = true } }); }
        /// <summary>The gallery's shooters leave without a trace (no death drawn): out of the rules, their views gone.</summary>
        void Clear()
        {
            foreach (var o in gallery)
            {
                if (o == null) continue;
                var all = new List<ZObject> { o }; all.AddRange(o.Minions);
                foreach (var x in all) { x.KillAt = 0; if (g.Views.TryGetValue(x.Id, out var v)) { g.Views.Remove(x.Id); if (v) Destroy(v.gameObject); } }
            }
            gallery.Clear();
        }
        void Heal() { if (s != null && !s.Destroyed && s.Health < s.MaxHealth) s.SetHealth(s.MaxHealth); }
        /// <summary>The player's own armed robots, vehicles and guns within m of the shooters' spot leave without a trace (they would
        /// shoot the shooters before these fire at the ship).</summary>
        int ClearOwnNear(int tx, int ty, float m)
        {
            int n = 0; var c = ZGame.PxToWorld(tx * 16 + 8, ty * 16 + 8);
            foreach (var o in W.Objects)
                if (o != s && o.Owner == me && !o.Destroyed && o.KillAt < 0 && !o.IsShuttle && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && !o.IsFortDefender && o.Leader == null
                    && Vector3.Distance(ZGame.PxToWorld(o.CenterX, o.CenterY), c) < m) { gallery.Add(o); n++; }
            Clear();
            return n;
        }
        /// <summary>For the fort's own gun: the enemy's guns, vehicles and robots round the fort leave (not its roof defenders), so the
        /// shots counted there are the fort gun's.</summary>
        int ClearNear(ZObject fort, float m)
        {
            int n = 0; var near = new List<ZObject>();
            foreach (var o in W.Objects)
                if (o != fort && o.Owner == them && !o.Destroyed && o.KillAt < 0 && (o.Kind == ZKind.Robot || o.Kind == ZKind.Vehicle || o.Kind == ZKind.Cannon) && !o.IsFortDefender && o.Leader == null
                    && Vector3.Distance(ZGame.PxToWorld(o.CenterX, o.CenterY), ZGame.PxToWorld(fort.CenterX, fort.CenterY)) < m) near.Add(o);
            foreach (var o in near) { gallery.Add(o); n++; }
            Clear();
            return n;
        }

        /// <summary>Open ground for the gallery: a pad 40 - 90 m from the ship, the patrol along x (12 m each way on the map) and a robot spot
        /// 12 m to its side for the shooters. False: none found.</summary>
        bool GalleryPlace(int nearPx, out int cx, out int cy, out int sx, out int sy)
        {
            cx = cy = sx = sy = 0; long best = long.MaxValue;
            for (int ty = 8; ty < W.Map.height - 8; ty++)
                for (int tx = 8; tx < W.Map.width - 8; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - s.CenterX) * (x - s.CenterX) + (long)(y - s.CenterY) * (y - s.CenterY);
                    if (d < (long)nearPx * nearPx || d > (long)(nearPx + 400) * (nearPx + 400) || d >= best) continue;
                    if (!W.ShuttlePadFree(x, y, s, out _)) continue;
                    int stx = tx, sty = ty + 6;                                                               // 12 m to the south of the line
                    bool ok = true;
                    for (int k = -2; k <= 2 && ok; k++) ok = W.Path.TilePassable(stx + k, sty, true) && W.Path.TilePassable(stx + k, sty + 1, true);
                    for (int k = -6; k <= 6 && ok; k += 3) { var q = W.ShuttleClamp(x + k * 16, y); ok = q.x == x + k * 16; }
                    if (!ok) continue;
                    best = d; cx = x; cy = y; sx = stx; sy = sty;
                }
            return best < long.MaxValue;
        }

        /// <summary>Back and forth along the patrol line through (patrolCx, patrolCy): the goal 30 m beyond the middle, turned round once it
        /// is 4 m past it (it never arrives, so it never lands; it slows, swings round and speeds up again at each end).</summary>
        IEnumerator Patrol()
        {
            patrolSign = 1; int lastSign = 0;
            while (patrolOn && s != null && !s.Destroyed)
            {
                float along = ((s.CenterX - patrolCx) * patrolU.x + (s.CenterY - patrolCy) * patrolU.y) / 8f;
                if (patrolSign > 0 && along > 4f) patrolSign = -1; else if (patrolSign < 0 && along < -4f) patrolSign = 1;
                if (patrolSign != lastSign || patrolKick)
                {
                    lastSign = patrolSign; patrolKick = false;
                    var q = W.ShuttleClamp(patrolCx + (int)(patrolU.x * 240f * patrolSign), patrolCy + (int)(patrolU.y * 240f * patrolSign));
                    Move(q.x, q.y);
                }
                yield return null;
            }
        }

        /// <summary>The drawn ship near the patrol line's middle and up at its height.</summary>
        bool OnPatrol() { var c = ZGame.PxToWorld(patrolCx, patrolCy); var p = ship.transform.position; return s.ShuttleAirborne && ship.FeetHeight > 8f && new Vector2(p.x - c.x, p.z - c.z).magnitude < 14f; }

        IEnumerator Gallery()
        {
            Log("G the weapons gallery: the shuttle flies back and forth over open ground while each enemy weapon in turn fires at it, healed between them");
            if (!GalleryPlace(320, out patrolCx, out patrolCy, out int stx, out int sty)) { Check("G an open place for the gallery", false); yield break; }
            patrolU = new Vector2(1f, 0f);
            int own = ClearOwnNear(stx, sty, 45f); int hpMax0 = s.MaxHealth; s.MaxHealth = 1000000; s.SetHealth(s.MaxHealth);
            Log($"G the patrol line through tile ({patrolCx >> 4},{patrolCy >> 4}) east-west, the shooters at tile ({stx},{sty}); camera 70 m, following the ship; the player's own armed units within 45 m of the shooters leave: {own}; the ship's hit points held at {s.MaxHealth} for the gallery (put back to {hpMax0} after it)");
            patrolOn = true; StartCoroutine(Patrol());
            yield return WaitFor(OnPatrol, 50f);
            OzShuttleAim.ProbeReset(); OzShuttleAim.Recording = true; strafeLeanPeak = 0f; strafeWrongWay = 0f; strafeFrames = 0; leanRateMax = 0f; leanTurns = 0;
            int strafes0 = s.ShuttleStrafes, returns0 = s.ShuttleStrafeReturns, nn = 0, whoosh0 = ship.StrafeWhooshes; float gt0 = Time.unscaledTime;
            foreach (var gun in Guns)
            {
                if (s == null || s.Destroyed) break;
                nn++;
                if (gun.kind == "fort") { yield return FortSegment(gun, nn); continue; }
                yield return OneGun(gun, stx, sty, nn, false);
            }
            patrolOn = false; OzShuttleAim.Recording = false;
            if (s != null && !s.Destroyed) { s.MaxHealth = hpMax0; s.SetHealth(hpMax0); }
            Log("G the drawn shots at the flying ship, per weapon:\n" + OzShuttleAim.ProbeText().TrimEnd());
            foreach (var gun in Guns)
            {
                OzShuttleAim.Probes.TryGetValue(gun.key, out var p);
                if (p == null) { Check($"G {gun.label}: fires at the flying ship", false); continue; }
                bool fired = p.shots > 0, low = p.low + p.blastsLow == 0, hull = p.onHull == p.hits, skyOk = p.inSky == p.misses, start = p.startsFar == 0;
                Check($"G {gun.label}: fires at the flying ship ({p.shots} shots), each from its muzzle, up into the ship (hits on the hull {p.onHull}/{p.hits}, misses into the sky {p.inSky}/{p.misses}, none ending low)", fired && low && hull && skyOk && start);
            }
            OzShuttleAim.Probes.TryGetValue("salvo extras", out var ex);
            Check("G the salvo's extra rockets burst round the flying ship, never on the ground under it", ex == null || ex.extrasLow == 0);
            int glides = s.ShuttleStrafes - strafes0, backs = s.ShuttleStrafeReturns - returns0;
            Log($"G strafes under fire: glides out {glides}, drifts back {backs}; the lean with them up to {strafeLeanPeak:F1} deg ({strafeFrames} frames), the wrong way at most {strafeWrongWay:F1} deg, its fastest change {leanRateMax:F1} deg/s, turned back {leanTurns} times; whooshes {ship.StrafeWhooshes - whoosh0} (sound log {Played(gt0, "shuttle_strafe_whoosh")})");
            Check("G under fire it strafes in ONE smooth glide to the side and leans with it (3-10 deg, never the wrong way)", glides > 0 && strafeLeanPeak >= 3f && strafeLeanPeak <= 10.5f && strafeWrongWay <= 0.5f);
            Check("G the lean never flickers: it changes at most 25 deg/s and turns back at most twice per glide or drift", leanRateMax <= 25f && leanTurns <= 2 * (glides + backs) + 1);
            Check("G every glide out has its thruster whoosh", ship.StrafeWhooshes - whoosh0 >= glides && Played(gt0, "shuttle_strafe_whoosh") > 0);
            Clear(); Heal();
        }

        /// <summary>One weapon of the gallery: its shooters 12 m to the side (or the kill's), the attack on the ship, at least 4 drawn shots
        /// (6 - 16 s), the ship healed whenever it is below half (never in a kill), a picture of the first shot in flight.</summary>
        IEnumerator OneGun(Gun gun, int stx, int sty, int nn, bool kill)
        {
            Log($"G {nn:00} {gun.label}{(kill ? " (the kill)" : "")}"); if (clip) TakeLog("CAPTION " + gun.label);
            var made = new List<ZObject>();
            switch (gun.kind)
            {
                case "squad": made.Add(Squad(gun.type, stx, sty, them)); break;
                case "grenades": { var sq = Squad(gun.type, stx, sty, them); if (sq != null) sq.Grenades = 8; made.Add(sq); break; }
                case "vehicle": made.Add(Vehicle(gun.type, stx, sty, them)); break;
                case "cannon": made.Add(Cannon(gun.type, stx, sty, them)); break;
                case "apc":
                {
                    var apc = Vehicle("apc", stx, sty, them); var crew = Squad(gun.type, stx + 3, sty, them); made.Add(apc);
                    if (apc != null && crew != null)
                    {
                        W.SendWaypoints(them, crew, new[] { new ZWaypoint { mode = WpMode.Enter, refId = apc.Id, x = apc.CenterX, y = apc.CenterY } });
                        yield return WaitFor(() => apc.Passengers.Count > 0, 12f);
                        if (apc.Passengers.Count == 0) { made.Add(crew); Log("G   the crew did not board: the APC's driver fires alone"); }
                    }
                    break;
                }
            }
            made.RemoveAll(o => o == null);
            gallery.AddRange(made);
            foreach (var o in made) Attack(o);
            OzShuttleAim.Probes.TryGetValue(gun.key, out var p0); int shots0 = p0 != null ? p0.shots : 0;
            float t = 0f, nudge = 2f; bool pic = false;
            while (t < (kill ? 40f : 16f))
            {
                if (s == null || s.Destroyed || ship.BrokenUp) break;
                if (!kill && s.Health < s.MaxHealth / 2) Heal();
                if ((nudge -= Time.deltaTime) <= 0f) { nudge = 2f; foreach (var o in made) if (!o.Destroyed && o.AttackObject != s && o.Waypoints.Count == 0) Attack(o); }   // an idle shooter is put on it again
                OzShuttleAim.Probes.TryGetValue(gun.key, out var p); int shots = (p != null ? p.shots : 0) - shots0;
                if (!pic && shots > 0) { pic = true; yield return Wait(0.25f); Shot($"G{nn:00}_{gun.key.Replace(' ', '_')}_shot_h70"); continue; }
                if (!kill && shots >= 4 && t >= 6f) break;
                t += Time.deltaTime; yield return null;
            }
            if (kill) yield break;
            Clear(); Heal(); yield return Wait(1.5f); Heal();
        }

        /// <summary>A patrol middle in front of the enemy fort (its gun reaches the whole line, outside its rear arc, off its walls).</summary>
        bool FortSpot(ZObject fort, out int bx, out int by)
        {
            bx = by = 0;
            var hu = W.S != null ? W.S.Unit("howitzer") : null; long reach = hu != null ? ZObject.FortGunRange(hu) - 16 : 220;
            for (int r = 120; r <= 260; r += 5)
                for (int a = 0; a < 360; a += 15)
                {
                    int x = fort.CenterX + (int)(Mathf.Cos(a * Mathf.Deg2Rad) * r), y = fort.CenterY + (int)(Mathf.Sin(a * Mathf.Deg2Rad) * r);
                    x = (x >> 4) * 16 + 8; y = (y >> 4) * 16 + 8;                                     // a tile's middle (the clamp's own grid)
                    var q = W.ShuttleClamp(x, y); if (q.x != x || q.y != y) continue;
                    bool ok = true;
                    foreach (int dx in new[] { -72, 0, 72 })
                    {
                        long ex = x + dx - fort.CenterX, ey = y - fort.CenterY;
                        ok &= fort.FortGunCanReach(x + dx, y) && ex * ex + ey * ey <= reach * reach;
                    }
                    if (ok) { bx = x; by = y; return true; }
                }
            return false;
        }

        /// <summary>The fort's own gun: the patrol moved within its reach in front of the blue fort (outside its rear arc, off its walls).</summary>
        IEnumerator FortSegment(Gun gun, int nn)
        {
            var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == them && !o.Destroyed && !o.FortGunDestroyed);
            if (fort == null) { Log("G no enemy fort with its gun on this map"); Check("G the fort's gun: an enemy fort", false); yield break; }
            if (!FortSpot(fort, out int bx, out int by)) { Check("G the fort's gun: a place in its reach", false); yield break; }
            Log($"G {nn:00} {gun.label}: the patrol moves to tile ({bx >> 4},{by >> 4}), {Mathf.Sqrt((bx - fort.CenterX) * (bx - fort.CenterX) + (by - fort.CenterY) * (by - fort.CenterY)) / 8f:F0} m from the fort #{fort.Id}; the enemy's other shooters round it leave: {ClearNear(fort, 45f)}");
            if (clip) TakeLog("CAPTION " + gun.label);
            patrolCx = bx; patrolCy = by; patrolU = new Vector2(1f, 0f); patrolKick = true;
            float fly = 0f; while (fly < 80f && !OnPatrol()) { if (s.Health < s.MaxHealth * 0.6f) Heal(); fly += Time.deltaTime; yield return null; }   // over the enemy's ground: healed on the way
            OzShuttleAim.Probes.TryGetValue(gun.key, out var p0); int shots0 = p0 != null ? p0.shots : 0;
            float t = 0f; bool pic = false;
            while (t < 70f)
            {
                if (s == null || s.Destroyed) break;
                if (s.Health < s.MaxHealth * 0.6f) Heal();
                OzShuttleAim.Probes.TryGetValue(gun.key, out var p); int shots = (p != null ? p.shots : 0) - shots0;
                if (!pic && shots > 0) { pic = true; yield return Wait(0.6f); Shot($"G{nn:00}_fort_gun_shot_h70"); continue; }
                if (shots >= 3) break;
                t += Time.deltaTime; yield return null;
            }
            yield return Wait(2f); Heal();
        }

        // ------------------------------------------------------------------ G: the kills, each after a fresh level start
        IEnumerator KillSeries()
        {
            for (int k = 0; k < KillGuns.Length; k++)
            {
                var gun = KillGuns[k];
                Log($"K{k + 1} a fresh start of level 16 for the kill by {gun.label}");
                if (clip) TakeLog("CAPTION shot down by " + gun.label);
                var old = g; OzScenes.Restart();                                                          // the game's own restart (the pause menu's)
                bool seen = false; float waited = 0f;
                while (waited < 150f)
                {
                    if (ZodBriefing.BriefingActive) seen = true;
                    var ng = ZGame.I;
                    if (ng && ng != old && ng.World != null && ng.World.PlayableShuttle != null && OzPlayableShuttle.I && !ZodBriefing.BriefingActive && (seen || waited > 20f)) break;
                    waited += Time.unscaledDeltaTime; yield return null;
                }
                if (!Bind()) { Check($"K{k + 1} level 16 back with its shuttle", false); yield break; }
                yield return Wait(1.5f);
                // a squad aboard
                var sq = Squad("grunt", (s.CenterX >> 4) + 3, (s.CenterY >> 4) + 4, me); yield return Wait(0.6f);
                Enter(sq); yield return WaitFor(() => s.Passengers.Count > 0, 30f); yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut, 15f);
                int aboard = s.Passengers.Count, lost0 = W.ShuttlePassengersLost, bodies0 = BoardingView.ShuttleBodies; BodyMark();
                int stx = 0, sty = 0;
                if (gun.kind == "fort")
                {
                    var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == them && !o.Destroyed && !o.FortGunDestroyed);
                    if (fort == null) { Check($"K{k + 1} an enemy fort", false); continue; }
                    if (!FortSpot(fort, out patrolCx, out patrolCy)) { Check($"K{k + 1} a place in the fort gun's reach", false); continue; }
                    Log($"K{k + 1} the enemy's other shooters round the fort leave: {ClearNear(fort, 45f)}");
                }
                else if (!GalleryPlace(320, out patrolCx, out patrolCy, out stx, out sty)) { Check($"K{k + 1} an open place", false); continue; }
                patrolU = new Vector2(1f, 0f); patrolOn = true; StartCoroutine(Patrol());
                float fly = 0f; while (fly < 80f && !OnPatrol()) { if (s.Health < s.MaxHealth * 0.6f) Heal(); fly += Time.deltaTime; yield return null; }
                follow = true; float t0 = Time.unscaledTime;
                if (gun.kind != "fort") { s.SetHealth(1); yield return OneGun(gun, stx, sty, 0, true); }       // the first real hit brings it down
                else
                {   // the fort's shell: the ship is let down to 1 HP the moment the fort fires at it (healed of anything else until then)
                    bool was = OzShuttleAim.Recording; OzShuttleAim.Recording = true; OzShuttleAim.Probes.TryGetValue(gun.key, out var fp); int f0 = fp != null ? fp.shots : 0; float wt = 0f;
                    while (wt < 60f && !ship.BrokenUp)
                    {
                        OzShuttleAim.Probes.TryGetValue(gun.key, out fp);
                        if (fp != null && fp.shots > f0) { s.SetHealth(1); break; }
                        if (s.Health < s.MaxHealth * 0.6f) Heal();
                        wt += Time.deltaTime; yield return null;
                    }
                    OzShuttleAim.Recording = was;
                }
                yield return WaitFor(() => ship.BrokenUp, 40f);
                if (!ship.BrokenUp) { Check($"K{k + 1} {gun.label} brings it down within 40 s", false); s.DamageHealth(s.Health); W.UpdateObjectHealth(s); yield return WaitFor(() => ship.BrokenUp, 5f); }
                patrolOn = false; follow = false;
                yield return Wait(0.35f); Shot($"K{k + 1}_{gun.key.Replace(' ', '_')}_breaking_h70");
                yield return WaitFor(() => OzShuttleBreakup.LastAllRested(), 8f);
                yield return Wait(1.0f); Shot($"K{k + 1}_{gun.key.Replace(' ', '_')}_landed_h70");
                // where the pieces went against the killing round's way
                Vector3 c = Vector3.zero; int n = 0; foreach (var pc in OzShuttleBreakup.LastPieces) if (pc) { c += pc.transform.position; n++; }
                if (n > 0) c /= n;
                var off = c - OzShuttleBreakup.LastCentre; off.y = 0f; var kd = OzShuttleBreakup.LastKillDir; kd.y = 0f;
                float along = kd.sqrMagnitude > 1e-4f ? Vector3.Dot(off, kd.normalized) : 0f;
                Log($"K{k + 1} killed by {OzShuttleBreakup.LastKillKind} (power {OzShuttleBreakup.LastKillPower:F2}) {(OzShuttleBreakup.LastInAir ? "in the air" : "on the ground")}: the pieces' middle {off.magnitude:F1} m from where it broke, {along:F1} m along the killing round's way");
                Check($"K{k + 1} {gun.label}: it breaks apart thrown along the killing round (the pieces' middle >= 0.5 m along its way)", OzShuttleBreakup.LastKillKind != "-" && along >= 0.5f);
                yield return BreakChecks($"K{k + 1}", t0, aboard, lost0, bodies0, OzShuttleBreakup.LastInAir);
                Clear();
            }
        }

        /// <summary>After a level start: the new level's game, world, ship, input and camera, the CPU removed, the morning held, 70 m.</summary>
        bool Bind()
        {
            g = ZGame.I; W = g ? g.World : null; if (W == null || W.PlayableShuttle == null || !OzPlayableShuttle.I) return false;
            s = W.PlayableShuttle; ship = OzPlayableShuttle.I; me = s.Owner; them = me == Team.Red ? Team.Blue : Team.Red;
            input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); overlay = FindFirstObjectByType<OpenZUnitOverlay>();
            W.Bots.Clear(); dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            gallery.Clear(); follow = true;
            Log($"level 16 again: shuttle #{s.Id}, CPU removed, day 0.10 held, camera 70 m");
            return true;
        }

        void Finish()
        {
            sb.AppendLine("sound log (the shuttle's):");
            var keys = new HashSet<string> { "shuttle_door_open", "shuttle_door_close", "shuttle_ramp_hydraulic", "latch", "crew_in", "crew_step", "vo_shuttle_board", "vo_shuttle_unload", "vo_shuttle_landed", "vo_shuttle_hot", "vo_shuttle_no_land", "ann_shuttle_landing", "ann_starting_repair", "ui_error",
                                             "shuttle_strafe_whoosh", "shuttle_station_crane", "bld_repair_hoist", "shuttle_skid_scrape", "vo_select_x_shuttle", "vo_order_x_shuttle_move",
                                             "shuttle_spool_up", "shuttle_takeoff", "shuttle_hover_loop", "shuttle_jet_burn_loop", "shuttle_landing", "shuttle_downwash_gust", "shuttle_tree_rustle", "shuttle_level_hydraulic", "rob_servo", "wreck_crash", "turret", "dst_debris_metal", "dst_turret_toss", "exp_debris_rain", "dst_wreck_creak", "ngt_searchlight_on", "ngt_light_fizzle", "shuttle_crash_fall" };
            var snd = new StringBuilder();
            foreach (var e in OzSoundLog.Since(T0))
            {
                string line = $"  {e.t - T0,7:F2} {e.src} {e.key} {e.clip} {e.outcome} vol {e.vol:F2} dist {e.dist:F1}";
                snd.AppendLine(line); if (keys.Contains(e.key)) sb.AppendLine(line);
            }
            int over = VoiceOverlaps(T0, out string worst);
            Check($"the whole take: no robot line ever over Rachel's landing line (robot lines held {AudioDirector.ShuttleHeld}, said after her {AudioDirector.ShuttleHeldPlayed}, dropped {AudioDirector.ShuttleHeldDropped}) {worst}", over == 0);
            if (take)
            {   // K256e (FIX_LIST M13 / M15): over the WHOLE take (no line cap): Rachel's lines against the shuttle's; the table per action
                take.Flush();
                int vo = take.VoiceOverlaps(T0, out string vw, out int nR, out int nS);
                Check($"M13 the whole take (every line, no cap): no Rachel line over a shuttle line ({nR} Rachel lines, {nS} shuttle lines; worst: {vw})", vo == 0);
                take.End(keys); take = null;
            }
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS), {frames} frames, {Time.unscaledTime - T0:F0} s; break-up plan: {OzShuttleBreakup.LastPlan}");
            try
            {
                string tagF = (mode.StartsWith("G") ? "_G" : mode.StartsWith("B") ? "_B" : mode.StartsWith("P") ? "_P_" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name : "") + (clip ? "_clip" : "");
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log" + tagF + ".txt"), sb.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "sound_log" + tagF + ".txt"), snd.ToString());
                if (mode.StartsWith("G")) System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "gallery_shots" + tagF + ".txt"), OzShuttleAim.ProbeText() + "\n" + OzShuttleAim.Lines);
                if (clip) System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "done.txt"), "done");
            }
            catch (System.Exception ex) { Debug.LogError("[OzShuttlePolishProof] could not write the logs: " + ex.Message); }
            if (dn) dn.running = true;
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
        }
    }
}
