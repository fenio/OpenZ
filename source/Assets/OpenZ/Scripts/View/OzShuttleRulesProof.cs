using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K256e Play proof of the playable shuttle's rules on the ground (rule DV118, the third round; the review M6 rules and sounds, M7, M8
    /// rules), on level 16 in Play right after a FRESH level start (menu OpenZ/V1.1/Shuttle: K256e Rules Proof, Editor/OzShuttleRulesMenu.cs).
    /// Counted PASS / FAIL lines and full-screen pictures from the normal RTS camera at the standard 70 m, uncropped, from a FIXED camera
    /// (strips: the camera never follows the ship); *_extra_close pictures are never the proof. Set-up: the CPU teams removed, morning
    /// (day clock 0.10, held). Stages:
    ///  R1 the level start: the parked heading (the ramp toward the start camera, its lane on the screen and off the HUD panels), a grunt
    ///     squad in front of the dome boards: a 16-frame strip from the level's start camera; no robot more than 1.5 m off the back axis
    ///     where it waits, no two figures overlapping at the foot, the sounds (door open, the hydraulic AT the opening, 3 ramp steps at
    ///     0.12 or more, the close, the latch, All aboard!), seated = boarded;
    ///  R5 the hard obstacle: a grunt squad walks straight through the parked ship (with FRIENDLY UNITS OVERLAP on): 0 units inside;
    ///  R2 EXIT at the parked spot: the three in a column behind the ramp ("in line"), nobody inside the brackets; then a take-off with own
    ///     grunts standing beside the hull: it waits (at most 3 s) and blows them out, 0 units inside the outline while it stands and lifts;
    ///     the landing spot it flies to: nothing standing in its footprint + 0.5 m (no hard top above the ground, no plant crown);
    ///  R3 a landing where its own heading's ramp lane is blocked: the hover turn (at most 30 deg/s) onto a free side, then down;
    ///  R4 R to the repair station: the hover turn at most 30 deg/s, down on the deck (pictures at 70 m and 32 m); no robot or Rachel
    ///     line from the landing to the lift-off; then shot down on the deck: the bay stays closed and the crane away for 4 s while the
    ///     wreck clears, a light tank sent to the station waits and drives in after.
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_rules_proof, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleRulesProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_rules_proof");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_rules_proof");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleRulesProof").AddComponent<OzShuttleRulesProof>();
        }

        /// <summary>Level 16's start camera (the scene's RtsCamera: focus, height) and the HUD panels as shares of the screen (the unit panel,
        /// the minimap, the order bar, the top bar: x0, y0, x1, y1 from the top left, measured on the round-2 pictures at 2560 x 1600).</summary>
        public static Vector3 StartFocus = new Vector3(104f, 0f, -226.6f); public static float StartHeight = 70f;
        static readonly Rect[] Hud = { Rect.MinMaxRect(0.516f, 0.8125f, 0.791f, 0.99f), Rect.MinMaxRect(0.799f, 0.664f, 0.988f, 0.978f), Rect.MinMaxRect(0.008f, 0.928f, 0.422f, 0.981f), Rect.MinMaxRect(0.275f, 0.011f, 0.666f, 0.088f) };

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; ZObject s; OzPlayableShuttle ship; Team me = Team.Red, them = Team.Blue;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir; float T0;
        // per frame
        bool watchSquad; ZObject squadLead; float worstOffAxis, minFootPair = 99f; int axisSamples, pairSamples;
        bool watchInside; int insideFrames, insideWorst, insideSamples;
        // the whole take's sound log: OzSoundLog keeps only its last 2000 lines, so the new lines are written out every 2 s
        float soundFlushedTo = -1f, nextFlush; int soundLines; string soundPath;
        void FlushSounds(bool last = false)
        {
            if (soundFlushedTo < 0f || (!last && Time.unscaledTime < nextFlush)) return;
            nextFlush = Time.unscaledTime + 2f;
            var sbS = new StringBuilder(); float upTo = soundFlushedTo, now = last ? float.MaxValue : Time.unscaledTime;
            foreach (var e in OzSoundLog.Since(soundFlushedTo))
            {
                if (e.t <= soundFlushedTo || e.t >= now) continue;                                          // this frame's lines go out with the next flush (all of a frame at once)
                sbS.AppendLine($"  {e.t - T0,7:F2} {e.src} {e.key} {e.clip} {e.outcome} vol {e.vol:F2} dist {e.dist:F1}"); soundLines++;
                upTo = Mathf.Max(upTo, e.t);
            }
            soundFlushedTo = upTo;
            try { System.IO.File.AppendAllText(soundPath, sbS.ToString()); } catch (System.Exception ex) { Debug.LogWarning("[OzShuttleRulesProof] " + ex.Message); }
        }

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleRulesProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        Vector3 Wp(int x, int y) => ZGame.PxToWorld(x, y);
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
        bool Move(ZObject o, int x, int y) => W.SendWaypoints(me, o, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool Enter(ZObject sq) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
        static List<OzSoundLog.Entry> Plays(float t0, string key) => OzSoundLog.Since(t0).Where(e => e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP") || e.src == "DOOR")).ToList();
        static float FirstAt(float t0, string key) { var l = Plays(t0, key); return l.Count > 0 ? l[0].t : -1f; }

        /// <summary>A point in the ship's root space (x right, z nose, m) from a world point.</summary>
        Vector2 RootOf(Vector3 w) { double hd = s.ShuttleHeadingDeg * System.Math.PI / 180.0; var c = Wp(s.CenterX, s.CenterY); double dx = w.x - c.x, dy = -(w.z - c.z); return new Vector2((float)(-dx * System.Math.Sin(hd) + dy * System.Math.Cos(hd)), (float)(dx * System.Math.Cos(hd) + dy * System.Math.Sin(hd))); }

        /// <summary>The share of the screen (x, y from the top left) of a world point; false = behind the camera.</summary>
        static bool ScreenShare(Vector3 w, out Vector2 f)
        {
            var cam = Camera.main; f = default; if (!cam) return false;
            var p = cam.WorldToScreenPoint(w); if (p.z <= 0f) return false;
            f = new Vector2(p.x / Screen.width, 1f - p.y / Screen.height); return true;
        }
        static float HudGapPx(Vector2 f)
        {   // px off the nearest HUD panel (negative = under it) and off the screen's edges
            float best = 1e9f;
            foreach (var r in Hud) best = Mathf.Min(best, Mathf.Max(Mathf.Max(r.xMin - f.x, f.x - r.xMax) * Screen.width, Mathf.Max(r.yMin - f.y, f.y - r.yMax) * Screen.height));
            return Mathf.Min(best, Mathf.Min(Mathf.Min(f.x, 1f - f.x) * Screen.width, Mathf.Min(f.y, 1f - f.y) * Screen.height));
        }

        void Update()
        {
            FlushSounds();
            if (W == null || s == null || s.Destroyed) return;
            if (watchSquad && s.ShuttleState == ZShuttleState.Landed)
            {   // robots waiting behind the hull: on the back axis; figures near the ramp foot: never on top of each other
                foreach (var o in W.Objects)
                {
                    if (!o.IsRobot || o.Destroyed || o.KillAt >= 0 || o.Owner != me) continue;
                    if (squadLead == null || (o != squadLead && o.Leader != squadLead)) continue;
                    var r = RootOf(Wp(o.CenterX, o.CenterY));
                    bool waits = o.Waypoints.Count == 0 || !o.IsMoving || o.PS.wait != ZPathWait.None;
                    if (r.y < -(float)(ZObject.ShuttleHullBackM + 0.75) && r.y > -14f && waits) { axisSamples++; worstOffAxis = Mathf.Max(worstOffAxis, Mathf.Abs(r.x)); }
                }
                var box = GameObject.Find("OZ_BoardingCrew"); var foot = ship.Door != null ? ship.Door.FootPoint() : ship.transform.position;
                if (box)
                {
                    var near = new List<Vector3>();
                    foreach (Transform c in box.transform) if (c.gameObject.activeInHierarchy && new Vector2(c.position.x - foot.x, c.position.z - foot.z).magnitude < 2.5f) near.Add(c.position);
                    for (int i = 0; i < near.Count; i++) for (int j = i + 1; j < near.Count; j++) { pairSamples++; minFootPair = Mathf.Min(minFootPair, new Vector2(near[i].x - near[j].x, near[i].z - near[j].z).magnitude); }
                }
            }
            if (watchInside && (s.ShuttleState == ZShuttleState.Landed || (s.ShuttleState == ZShuttleState.Climb && s.ShuttleHeightM < 3.0)))
            {
                int n = W.ShuttleUnitsInside(s, 0.0); insideSamples++;
                if (n > 0) { insideFrames++; insideWorst = Mathf.Max(insideWorst, n); }
            }
        }

        IEnumerator All()
        {
            T0 = Time.unscaledTime; Application.targetFrameRate = 60;
            soundPath = System.IO.Path.Combine(dir, "rules_proof_sounds.txt");
            try { System.IO.File.WriteAllText(soundPath, "the whole take's sound log (written out every 2 s), seconds from the proof's start\n"); } catch { }
            soundFlushedTo = T0 - 0.001f; nextFlush = T0 + 2f;
            if (W == null || W.PlayableShuttle == null) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on, not 'Zod exact')"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s); me = s.Owner; them = me == Team.Red ? Team.Blue : Team.Red;
            if (!ship) { Log("the shuttle has no view"); fails++; Finish(); yield break; }
            Log($"K256e shuttle rules proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held; the rules' keep-out circles {W.ShuttleKeepOutCount} (hand-overs {ShuttleRearDoor.KeepOutFed})");
            Check("R0 the level's plants are in the rules' landing spot rule (keep-out circles handed over by the ship's view)", W.ShuttleKeepOutCount > 50 && ShuttleRearDoor.KeepOutFed > 0);
            yield return Wait(0.8f);
            yield return R1(); yield return R5(); yield return R2(); yield return R3(); yield return R4();
            Finish();
        }

        // ------------------------------------------------------------------ R1: the level start, the boarding toward the camera
        IEnumerator R1()
        {
            Log("R1 the level start: the parked heading and a boarding from the level's start camera");
            Cam(StartFocus, StartHeight); yield return Wait(0.5f);
            float hd = s.ShuttleHeadingDeg, yawOff = Mathf.Abs(Mathf.DeltaAngle(ship.YawDeg, hd + 90f));
            // the ramp toward the camera: the back's direction against the way from the ship to the camera (on the ground)
            double h = hd * System.Math.PI / 180.0; var back = new Vector2((float)-System.Math.Cos(h), (float)System.Math.Sin(h));   // world (x, z): the sim's y is -z
            var camP = Camera.main ? Camera.main.transform.position : Vector3.zero; var mid = ship.transform.position;
            var toCam = new Vector2(camP.x - mid.x, camP.z - mid.z).normalized;
            float faceOff = Vector2.Angle(back, toCam);
            float worstHud = 1e9f;
            foreach (double z in new[] { ZObject.ShuttleHullBackM, ZWorld.ShuttleBackM, ZWorld.ShuttleBackM + ZWorld.ShuttleFileM, ZWorld.ShuttleBackM + 2 * ZWorld.ShuttleFileM })
            {
                var (px, py) = (s.CenterX + back.x * z * 8.0, s.CenterY - back.y * z * 8.0);
                var w = Wp((int)px, (int)py); w.y = g.GroundY(w.x, w.z) + 0.3f;
                if (ScreenShare(w, out var f)) worstHud = Mathf.Min(worstHud, HudGapPx(f)); else worstHud = -1f;
            }
            Log($"parked heading {hd:F1} (the rules' L16ShuttleHeadingDeg {ZWorld.L16ShuttleHeadingDeg:F0}), drawn yaw {ship.YawDeg:F1} ({yawOff:F2} off); the back {faceOff:F1} deg off the way to the start camera; the ramp and the three boarding spots {worstHud:F0} px off the HUD panels / the edges");
            Check("R1 the parked ship faces the rules' heading at the level start (drawn yaw within 1 deg)", Mathf.Abs(hd - ZWorld.L16ShuttleHeadingDeg) < 0.5f && yawOff <= 1f);
            bool parkedFree = W.ShuttlePadFreeAt(s.CenterX, s.CenterY, s, hd, out string parkedWhy);
            Log($"the parked place under the landing spot rule (the level's drawn plants included): {(parkedFree ? "free" : "NOT free: " + parkedWhy)}");
            Check("R1 the parked place itself passes the landing spot rule: its footprint + 0.5 m and its ramp lane hold no plant crown, prop, closed tile or unit", parkedFree);
            Check("R1 its ramp faces the start camera (the back within 30 deg of the way to the camera)", faceOff <= 30f);
            Check("R1 the ramp and the squad's three boarding spots are on the screen and not under the unit panel, the minimap or the bars (>= 12 px)", worstHud >= 12f);
            // the squad, 10 m in front of the dome
            double fx = s.CenterX + System.Math.Cos(h) * 80, fy = s.CenterY + System.Math.Sin(h) * 80;
            var sq = Squad("grunt", (int)fx >> 4, (int)fy >> 4, me); yield return Wait(0.8f);
            if (sq == null) { Check("R1 a squad to board", false); yield break; }
            input.Selection.Clear(); input.Selection.Add(sq); foreach (var m in sq.Minions) input.Selection.Add(m);
            Cam(StartFocus, StartHeight); yield return null;
            float t0 = Time.unscaledTime; int opens0 = s.ShuttleRampOpens, shuts0 = s.ShuttleRampShuts, steps0 = BoardingView.ShuttleSteps, n = 1 + sq.Minions.Count(m => m != null && !m.Destroyed);
            squadLead = sq; worstOffAxis = 0f; minFootPair = 99f; axisSamples = pairSamples = 0; watchSquad = true;
            Check("R1 the ENTER order is taken", Enter(sq));
            for (int k = 0; k < 16; k++)
            {   // the fixed-camera strip: the squad walks to the back, up one by one, the ramp closes
                Shot($"R1_boarding_strip_{k:00}_h70");
                if (k == 0) yield return WaitFor(() => s.ShuttleRampPhase != ZShuttleRamp.Shut, 20f);
                else yield return Wait(0.8f);
            }
            yield return WaitFor(() => s.ShuttleRampShuts > shuts0, 15f);
            yield return Wait(0.6f); Shot("R1_level_start_ramp_shut_h70");
            watchSquad = false;
            var steps = Plays(t0, "crew_step"); int loud = steps.Count(e => e.vol >= 0.12f);
            float a = FirstAt(t0, "shuttle_door_open"), b = FirstAt(t0, "shuttle_ramp_hydraulic"), c = FirstAt(t0, "shuttle_door_close"), d = FirstAt(t0, "latch"), e2 = FirstAt(t0, "vo_shuttle_board");
            Log($"boarded {s.Passengers.Count} of {n}; opens +{s.ShuttleRampOpens - opens0}, shuts +{s.ShuttleRampShuts - shuts0}; seated at the shut {ShuttleRearDoor.SeatedAtShut}; hydraulics at an opening {ShuttleRearDoor.OpenHydraulics}");
            Log($"waiting robots off the back axis: worst {worstOffAxis:F2} m ({axisSamples} samples); figures at the foot: closest pair {minFootPair:F2} m ({pairSamples} pairs)");
            Log($"sounds: door_open {a - T0:F2}, hydraulic {b - T0:F2}, door_close {c - T0:F2}, latch {d - T0:F2}, All aboard {e2 - T0:F2}; ramp steps {steps.Count} (at 0.12 or more {loud}; volumes {string.Join(" ", steps.Select(x => x.vol.ToString("F2")))})");
            Check("R1 the squad boards through the ramp (open, shut, latched) and all sit down (seated = boarded)", s.Passengers.Count == n && s.ShuttleRampOpens > opens0 && s.ShuttleRampShuts > shuts0 && ShuttleRearDoor.SeatedAtShut == n);
            Check("R1 no waiting robot more than 1.5 m off the back axis", axisSamples > 0 && worstOffAxis <= 1.5f);
            Check("R1 no two figures overlap at the ramp foot (closest pair >= 0.8 m)", minFootPair >= 0.8f);
            Check("R1 the sounds: door open, the hydraulic AT the opening (within 0.3 s), 3 ramp steps at 0.12 or more, the close, the latch, All aboard!",
                  a > 0 && b >= a - 0.05f && b - a <= 0.3f && loud >= n && c > b && d >= c && e2 > 0);
        }

        // ------------------------------------------------------------------ R5: the hard obstacle
        IEnumerator R5()
        {
            Log("R5 a grunt squad ordered straight through the parked ship (FRIENDLY UNITS OVERLAP on)");
            bool ov = ZWorld.FriendsOverlap; ZWorld.FriendsOverlap = true;
            double h = s.ShuttleHeadingDeg * System.Math.PI / 180.0, rx = -System.Math.Sin(h), ry = System.Math.Cos(h);
            var sq = Squad("grunt", (int)(s.CenterX - rx * 96) >> 4, (int)(s.CenterY - ry * 96) >> 4, me); yield return Wait(0.6f);
            insideFrames = insideWorst = insideSamples = 0; watchInside = true;
            var far = W.ShuttleClamp((int)(s.CenterX + rx * 112), (int)(s.CenterY + ry * 112));
            Check("R5 the move order is taken", sq != null && Move(sq, far.x, far.y));
            Cam(ship.transform.position, 70f);
            yield return Wait(1.5f); Shot("R5_walking_round_the_ship_h70");
            yield return Wait(10f);
            watchInside = false; ZWorld.FriendsOverlap = ov;
            Log($"units inside the hull outline while it stood: {insideFrames} of {insideSamples} frames (worst {insideWorst})");
            Check("R5 nobody walks into the landed ship, not even with FRIENDLY UNITS OVERLAP on: 0 frames with a unit inside its outline", insideSamples > 0 && insideFrames == 0);
        }

        // ------------------------------------------------------------------ R2: EXIT in line, the take-off with robots beside it
        IEnumerator R2()
        {
            Log("R2 EXIT at the parked spot, then a take-off with own grunts beside the hull");
            if (s.Passengers.Count == 0) { Check("R2 a squad aboard", false); yield break; }
            Cam(ship.transform.position + new Vector3(0f, 0f, -3f), 70f);
            int shuts0 = s.ShuttleRampShuts, n = s.Passengers.Count; float t0 = Time.unscaledTime;
            var outIds = new HashSet<int>(s.Passengers);                                                   // run 27D: the squad that gets out (not R5's squad standing near)
            var before2 = new HashSet<int>(W.Objects.Where(o => o.Owner == me && o.IsRobot && !o.Destroyed).Select(o => o.Id));   // run 27D r3: the robots there before the EXIT
            yield return WaitFor(() => s.CanEjectCrew(), 6f);
            Check("R2 EXIT is taken", W.Eject(me, s));
            yield return WaitFor(() => s.Passengers.Count == 0, 6f);
            yield return WaitFor(() => s.ShuttleRampShuts > shuts0, 14f); yield return Wait(0.4f);
            var outs = W.Objects.Where(o => o.Owner == me && o.IsRobot && !o.Destroyed && o.KillAt < 0 && (outIds.Contains(o.Id) || (!before2.Contains(o.Id) && Vector3.Distance(Wp(o.CenterX, o.CenterY), Wp(s.CenterX, s.CenterY)) < 16f))).ToList();
            Log($"EXIT: the robots that came out: {outs.Count} (ids {string.Join(" ", outs.Select(o => o.Id))})");
            float lat = outs.Count > 0 ? outs.Max(o => Mathf.Abs(RootOf(Wp(o.CenterX, o.CenterY)).x)) : 99f;
            int behind = outs.Count(o => RootOf(Wp(o.CenterX, o.CenterY)).y < -(float)ZObject.ShuttleHullBackM - 1f);
            Shot("R2_exit_in_line_h70");
            int inside = W.ShuttleUnitsInside(s, 0.5);
            Log($"EXIT: {n} out; standing behind the ramp {behind}, the column's worst side offset {lat:F2} m; units within the outline + 0.5 m {inside}");
            Check("R2 the three stand in a column behind the ramp (on the back axis within 0.5 m), nobody within the hull outline + 0.5 m", behind >= n && lat <= 0.5f && inside == 0);
            // own grunts right beside the hull (inside 2 m of it), then a take-off
            double h = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            var side = Squad("grunt", (int)(s.CenterX - System.Math.Sin(h) * 44) >> 4, (int)(s.CenterY + System.Math.Cos(h) * 44) >> 4, me); yield return Wait(1.0f);
            int nud0 = ZObject.ShuttleLiftNudges, waits0 = ZObject.ShuttleLiftWaits; ZObject.ShuttleLiftWaitMax = 0;
            insideFrames = insideWorst = insideSamples = 0; watchInside = true;
            var spot = OpenSpot(260, 520);
            if (spot == null) { Check("R2 a free landing spot 30-65 m away", false); watchInside = false; yield break; }
            float tOrder = Time.unscaledTime;
            Check("R2 the move order is taken", Move(s, spot[0], spot[1]));
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Climb, 6f);
            float waited = Time.unscaledTime - tOrder;
            yield return WaitFor(() => s.ShuttleHeightM > 3.0, 6f);
            watchInside = false;
            Log($"take-off: waited {waited:F2} s for the units beside it (the rules' longest wait {ZObject.ShuttleLiftWaitMax:F2} s, waits +{ZObject.ShuttleLiftWaits - waits0}), robots blown aside {ZObject.ShuttleLiftNudges - nud0} steps; units inside the outline while it stood / lifted {insideFrames} of {insideSamples} frames");
            Check("R2 the take-off waits for the robots beside it (at most 3 s) and blows them out of the way", ZObject.ShuttleLiftWaits > waits0 && ZObject.ShuttleLiftNudges > nud0 && ZObject.ShuttleLiftWaitMax <= 3.05);
            Check("R2 0 units inside the outline while it stands and lifts off", insideSamples > 0 && insideFrames == 0);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 40f);
            yield return Wait(0.8f); Shot("R2_landed_on_a_clear_spot_h70");
            int things = ThingsInFootprint(out string what);
            Log($"the landing spot: {things} hard tops above the ground / plant crowns inside its footprint + 0.5 m {what}");
            Check("R2 nothing stands in its footprint + 0.5 m where it landed (no prop, hut or crown: the gap under the hull is its own)", things == 0);
        }

        /// <summary>A spot the rules would land on, minPx - maxPx from the ship (its own heading, or turned).</summary>
        int[] OpenSpot(int minPx, int maxPx)
        {
            long best = long.MaxValue; int[] r = null;
            for (int ty = 4; ty < W.Map.height - 4; ty++)
                for (int tx = 4; tx < W.Map.width - 4; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - s.CenterX) * (x - s.CenterX) + (long)(y - s.CenterY) * (y - s.CenterY);
                    if (d >= best || d < (long)minPx * minPx || d > (long)maxPx * maxPx) continue;
                    if (W.ShuttlePadFreeAt(x, y, s, s.ShuttleHeadingDeg, out _)) { best = d; r = new[] { x, y }; }
                }
            return r;
        }

        /// <summary>Things standing in the landed ship's footprint + 0.5 m: hard tops more than 0.3 m above the ground and plant crowns more
        /// than 0.6 m tall (the ship's own sky grid, which knows every drawn thing; the ship itself left out).</summary>
        int ThingsInFootprint(out string what)
        {
            what = ""; int n = 0; if (ship.Sky == null) return 0;
            for (float zr = -4f; zr <= 4f; zr += 0.5f)
                for (float xr = -4f; xr <= 4f; xr += 0.5f)
                {
                    if (ZObject.ShuttlePlanGap(0, 0, 0, zr * 8, xr * 8) >= 0.5) continue;       // (root space through heading 0: the nose = sim +x, the right = sim +y)
                    var w = ship.transform.TransformPoint(new Vector3(xr, 0f, zr));
                    ship.Sky.Query(w.x, w.z, 0.2f, true, out float ht, out float pt);
                    float gy = g.GroundY(w.x, w.z);
                    if (pt > gy + 0.6f || ht > gy + 0.3f + Mathf.Max(0f, MaxGroundNear(w) - gy)) { n++; if (what.Length < 160) what += $"({xr:F1},{zr:F1}: {(pt > gy + 0.6f ? "plant " + (pt - gy).ToString("F1") : "hard " + (ht - gy).ToString("F1"))} m) "; }
                }
            return n;
        }
        float MaxGroundNear(Vector3 w) { float m = float.MinValue; for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++) m = Mathf.Max(m, g.GroundY(w.x + i * 0.1f, w.z + j * 0.1f)); return m; }

        // ------------------------------------------------------------------ R3: a landing that turns to a free side first
        IEnumerator R3()
        {
            Log("R3 a landing where its own heading's ramp lane is blocked: the hover turn to a free side");
            int[] spot = null; double want = 0, bearing0 = 0;
            for (int ty = 4; ty < W.Map.height - 4 && spot == null; ty++)
                for (int tx = 4; tx < W.Map.width - 4 && spot == null; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - s.CenterX) * (x - s.CenterX) + (long)(y - s.CenterY) * (y - s.CenterY);
                    if (d < 200L * 200L || d > 560L * 560L) continue;
                    double hd0 = System.Math.Atan2(y - s.CenterY, x - s.CenterX) * 180.0 / System.Math.PI;   // it arrives facing its flight
                    bool anyFree = false;                                                                      // run 27D r3: blocked round the bearing, at the point and within 2 px of it
                    int[] offs = { 0, 0, -1, 0, 0, -1, -1, -1, 1, 0, 0, 1, 1, 1, -2, 0, 0, -2, 2, 0, 0, 2 };
                    for (int q = 0; q + 1 < offs.Length && !anyFree; q += 2) for (int k = -4; k <= 4 && !anyFree; k++) anyFree = W.ShuttlePadFreeAt(x + offs[q], y + offs[q + 1], s, hd0 + 5.0 * k, out _);
                    if (anyFree) continue;
                    if (W.ShuttleLandSpot(x, y, s, hd0, out double hd, out _) && System.Math.Abs(ZObject.ShuttleNormDeg(hd - hd0)) >= 30) { spot = new[] { x, y }; want = hd; bearing0 = hd0; }
                }
            if (spot == null) { Log("no such spot within 25-70 m"); Check("R3 a spot that needs the turn", false); yield break; }
            int turns0 = ZObject.ShuttleLandTurns; float rate = 0f; bool shot = false;
            Check("R3 the move order is taken", Move(s, spot[0], spot[1]));
            Cam(Wp(spot[0], spot[1]), 70f);
            yield return WaitFor(() =>
            {
                if (s.ShuttleLandTurning) { rate = Mathf.Max(rate, Mathf.Abs(s.ShuttleTurnRateDeg)); if (!shot) { shot = true; Shot("R3_hover_turn_to_a_free_side_h70"); } }
                return s.ShuttleState == ZShuttleState.Landed;
            }, 60f);
            yield return Wait(0.8f); Shot("R3_landed_ramp_to_the_free_side_h70");
            float off = Mathf.Abs(Mathf.DeltaAngle(s.ShuttleHeadingDeg, (float)want));
            Log($"the landing turn: turns +{ZObject.ShuttleLandTurns - turns0}, fastest {rate:F1} deg/s (rules), touchdown heading {s.ShuttleHeadingDeg:F1} (the free side's {want:F1}, {off:F2} off; the bearing it flew in on {bearing0:F1}, blocked within 20 deg of it)");
            Check("R3 it turns in a hover (at most 30 deg/s) so the ramp faces a free side, then lands on that heading (within 1 deg)", ZObject.ShuttleLandTurns > turns0 && rate <= 30.5f && off <= 1f);
        }

        /// <summary>run 27D: a line the repair station or the ship says (vo_shuttle_*, "starting repair", "vehicle repaired", the shuttle's
        /// landing line, the repair station's own lines); a factory's robot or a zone taken elsewhere on the level is not the station's.</summary>
        static bool OzShuttleStationLine(string key) => key.StartsWith("vo_shuttle_") || key == "ann_starting_repair" || key == "ann_vehicle_repaired" || key.StartsWith("ann_shuttle") || key.StartsWith("ann_repair");

        // ------------------------------------------------------------------ R4: the repair deck; shot down on it
        IEnumerator R4()
        {
            Log("R4 R to the repair station: the hover turn at most 30 deg/s, down on the deck; then shot down on the deck");
            ZObject station = null;
            foreach (var f in W.Flags) foreach (var b in f.LinkedObjects) if (b.Type == "repair" && station == null) { station = b; if (b.Owner != me) W.AwardZone(f, me); }
            if (station == null) { Check("R4 a repair station", false); yield break; }
            float t0 = Time.unscaledTime; float rate = 0f, accel = 0f, last = 0f; bool turning = false;
            s.SetHealth(s.MaxHealth / 2); input.Selection.Clear(); input.Selection.Add(s);
            Check("R4 R is taken", input.SendToRepair());
            Cam(Wp(station.CenterX, station.CenterY), 70f);
            yield return WaitFor(() =>
            {
                if (s.ShuttleDeckTurning)
                {
                    turning = true; float r = Mathf.Abs(s.ShuttleTurnRateDeg); rate = Mathf.Max(rate, r);
                    float dt = Time.deltaTime * Mathf.Max(0.05f, g.gameSpeed); if (dt > 0f && last > 0f) accel = Mathf.Max(accel, Mathf.Abs(r - last) / dt); last = r;
                }
                return s.ShuttleOnRepairPad;
            }, 90f);
            yield return Wait(1.2f); Shot("R4_on_the_deck_h70");
            if (rts) { rts.SetHeightImmediate(32f); yield return Wait(0.3f); Shot("R4_on_the_deck_32m_extra_close"); rts.SetHeightImmediate(70f); yield return Wait(0.3f); }
            var heard4 = OzSoundLog.Since(t0).Where(e => e.src == "VO" && e.outcome != null && e.outcome.StartsWith("PLAY") && (e.key.StartsWith("vo_shuttle_") || e.key.StartsWith("ann_"))).ToList();
            bool talk = heard4.Any(e => OzShuttleStationLine(e.key));                                       // run 27D: the station's / the ship's own lines
            Log("the level's own lines in that time (not the station's or the ship's): " + (heard4.Any(e => !OzShuttleStationLine(e.key)) ? string.Join(", ", heard4.Where(e => !OzShuttleStationLine(e.key)).Select(e => e.key + " " + (e.t - T0).ToString("F2"))) : "none"));
            Log($"the hover turn: {(turning ? "yes" : "NO")}, fastest {rate:F1} deg/s (the rules' turn rate), its rate change at most {accel:F0} deg/s^2 (frame-sampled); robot or Rachel lines from the order to now: {(talk ? "yes" : "none")}");
            Check("R4 over the station it turns at most 30 deg/s onto the deck heading, then lands ON the deck", turning && rate <= 30.5f && s.ShuttleOnRepairPad);
            Check("R4 no robot or Rachel line from the repair order to the landing on the deck", !talk);
            // the root check (the review M8): each pad's sole against the deck under that pad (the deck's middle is a lower lane, so the root over
            // the middle is no measure); the deck point and heading are kept (the only free pose once the gantry has rolled away, by the station's fit)
            var padsL = new[] { new Vector3(2.228f, 0f, 1.029f), new Vector3(-2.168f, 0f, 1.079f), new Vector3(1.829f, 0f, -2.843f), new Vector3(-1.771f, 0f, -2.838f) };
            float worstPad = 0f; int padsMeasured = 0; var padTxt = new StringBuilder();
            foreach (var pl in padsL)
            {
                var pw = ship.transform.TransformPoint(pl);
                if (!OzStationCrane.DeckY(station.Id, pw, out float dyk)) continue;
                float gapP = pw.y - dyk; padsMeasured++; worstPad = Mathf.Max(worstPad, Mathf.Abs(gapP)); padTxt.Append($"{gapP * 100f:+0.0;-0.0} ");
            }
            Log($"the pads' soles over the deck under each: {padTxt}cm ({padsMeasured} of 4 measured)");
            Check("R4 standing on the deck: every pad's sole within 6 cm of the deck under it (the root check against the pads' own deck heights)", padsMeasured == 4 && worstPad <= 0.06f);
            // shot down on the deck; a light tank sent to the station at once
            var tank = Vehicle("light", (station.CenterX >> 4) + 8, (station.CenterY >> 4) + 4, me); yield return Wait(0.4f);
            if (tank != null) tank.SetHealth(tank.MaxHealth / 2);
            s.DamageHealth(s.Health); W.UpdateObjectHealth(s); float tDead = (float)W.Time;
            if (tank != null) W.SendWaypoints(me, tank, new[] { new ZWaypoint { mode = WpMode.UnitRepair, refId = station.Id, x = station.CenterX, y = station.CenterY, playerGiven = true } });
            yield return Wait(1.0f); Shot("R4_wreck_on_the_deck_t1_h70");
            float bayHeld = 0f, craneHeld = 0f, tankIn = -1f;
            yield return WaitFor(() =>
            {
                float t = (float)W.Time - tDead;
                if (station.RepairOccupantId >= 0 && (tank == null || station.RepairOccupantId != tank.Id)) bayHeld = t;
                if (W.ShuttleCraneAway(station.Id)) craneHeld = t;
                if (tank != null && tankIn < 0f && tank.OnRepairPad(station.Id)) tankIn = t;
                return tankIn >= 0f || t > 30f;
            }, 60f);
            Shot("R4_deck_clear_next_vehicle_in_h70");
            Log($"shot down on the deck: the bay held by the wreck {bayHeld:F2} s, the crane away {craneHeld:F2} s, the light tank on the pad {tankIn:F2} s after (the rules' {ZWorld.ShuttleDeckWreckSec:F1} s)");
            Check("R4 the wreck on the deck: the bay stays closed and the crane away for 4 s, the next vehicle drives in only after", bayHeld >= 3.9f && craneHeld >= 3.9f && (tank == null || tankIn >= 4.0f));
        }

        void Finish()
        {
            FlushSounds(true);
            Report = $"{(fails == 0 ? "PASS" : "FAIL")} {passes} passed, {fails} failed, {frames} frames; the whole take's sound log {soundLines} lines (rules_proof_sounds.txt)";
            sb.Insert(0, Report + "\n");
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "rules_proof_log.txt"), sb.ToString());
            }
            catch (System.Exception ex) { Debug.LogWarning("[OzShuttleRulesProof] " + ex.Message); }
            Debug.Log("[OzShuttleRulesProof] " + Report + " -> " + dir);
            Destroy(gameObject);
        }
    }
}
