using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// The K256d Play proof of the shuttle's night lights (acceptance line 21), on level 16 in Play (menu OpenZ/V1.1/Shuttle: K256d Night
    /// Lights Proof, Editor/OzShuttleNightMenu.cs). Counted PASS / FAIL lines, the sound log, full-screen pictures from the normal RTS
    /// camera at the standard zoom (70 m), uncropped; extra close pictures are named *_extra_close and are never the proof. Set-up: the CPU
    /// teams removed, the day clock held by the proof (put back at the end), the camera following the ship.
    /// N1 by day: a flight to a place it cannot land (it hovers there): no night light burns. N2 dusk: the clock runs from day into the
    /// night while it hovers: the lights come up with the level's night share, smoothly. N3 at night, hovering: the nose searchlight
    /// sweeps; two enemies come: it takes the nearer, turns alarming (narrow, red-amber, the slow pulse, the beep); the nearer destroyed:
    /// it lets go and takes the other; that one destroyed: back to the calm white sweep. N4 CALM MODE: a third enemy: the alarm's colour
    /// without any pulse. N5 LOW: the lights offered as real lights (4 -> 2) and no shadow candidate; back. N6 dawn: the clock runs into
    /// the day: the lights go down with it; the night again. N7 the landing: the landing lights on the landing point while it flies in
    /// and comes down, out after the touchdown. N8 boarding at night: the ramp light while the squad walks in. N9 a flight past an enemy:
    /// the landing lights low at the take-off, off at cruise height, the lock in flight, let go once out of reach, the landing. N10
    /// unloading at night: the ramp light. N11 a shuttle dropped from the testing panel (its spawn path) at night, flown a hop: its own
    /// night lights. Test only.
    /// K256e (FIX_LIST M14 / M15 / S7): every "h70" picture is grabbed before the camera moves (a picture is taken at the END of its frame;
    /// round 2 moved the camera to the close height in the same frame, so 21_04 / 21_11 / 21_12 were the close view) and checked against
    /// its close twin (a mean difference of at least 15 / 255); the locked enemy in the middle of the screen for 21_04 / 21_05 / 21_07 /
    /// 21_14; the night stages hover over landable open ground (an own robot stood on the pad when it arrived, then left: a plain move order
    /// hovers on until the next order); the dropped ship's picture over open ground (no hut under it), the camera on it before its order;
    /// the dusk measured per second (at most 0.6 a second, at least 1.75 s from 5 % to 95 %); the landing lights held 2 s after the lift-off;
    /// two distinct landing cones (their spots at least 3 m apart); the whole take's sound log (no line cap) with its table per action; no
    /// two pictures the same file.
    /// </summary>
    public sealed class OzShuttleNightProof : MonoBehaviour
    {
        public static string Report = "idle";
        /// <summary>The output folder: the work tree's records folder found upward from the project at run time (its milestones'
        /// shuttle_night_lights_proof), else the player's own data folder.</summary>
        public static string Folder()
        {
            const string sub = "shuttle_night_lights_proof";
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", sub);
            return System.IO.Path.Combine(Application.persistentDataPath, sub);
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleNightProof").AddComponent<OzShuttleNightProof>();
        }

        ZGame g; ZWorld W; RtsCamera rts; DayNight dn; ZObject s; OzPlayableShuttle ship; OzShuttleNightLights night; Team me = Team.Red, them = Team.Blue;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir; float T0; bool follow = true;
        float clock0 = 0.10f; bool running0; bool calm0; int dayLit0; Transform watch; readonly List<ZObject> enemies = new List<ZObject>();
        readonly List<string> shots = new List<string>(); OzSoundTake take; Vector3? look;                    // K256e: the take's pictures, its whole sound log, a fixed look point

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; rts = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            calm0 = OzMotion.Reduced;
            if (dn) { clock0 = dn.timeOfDay; running0 = dn.running; }
            StartCoroutine(All());
        }

        void OnDestroy()
        {   // everything the proof changed goes back, even when it is stopped half way
            OzShuttleNightLights.ProofPreset = -1;
            if (OzMotion.Reduced != calm0) OzMotion.Reduced = calm0;
            if (dn) { dn.timeOfDay = clock0; dn.Apply(clock0); dn.running = running0; }
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleNightProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name)
        {
            var path = System.IO.Path.Combine(dir, name + ".png"); ScreenCapture.CaptureScreenshot(path); frames++; shots.Add(path);
            var cam = Camera.main;
            Log($"frame {name}.png (camera height {(rts ? rts.height : -1f):F1} m{(cam ? $", at {cam.transform.position.y:F1} m over the world's zero" : "")})");
            if (name.Contains("_h70") && rts) Check($"{name}: the standard 70 m camera when grabbed", Mathf.Abs(rts.height - 70f) < 0.5f);
        }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        IEnumerator Close(string name)
        {   // an extra close picture (never the proof), then back to the standard zoom. K256e: the h70 picture of this frame is grabbed
            // first (the end of its frame), only then the camera moves; the pair is checked to differ (the h70 one really at 70 m)
            if (!rts) yield break;
            yield return OzProofPictures.AfterGrab();
            rts.SetHeightImmediate(30f); yield return null; yield return null; Shot(name + "_extra_close");
            yield return OzProofPictures.AfterGrab();
            rts.SetHeightImmediate(70f); yield return null;
            string a = System.IO.Path.Combine(dir, name + "_h70.png"), b = System.IO.Path.Combine(dir, name + "_extra_close.png");
            yield return OzProofPictures.Written(a); yield return OzProofPictures.Written(b);
            float d = OzProofPictures.MeanLumaDiff(a, b);
            Log($"{name}: the h70 picture against its close twin: mean difference {d:F1} / 255");
            Check($"{name}_h70 is really the 70 m view (differs from its close twin by >= {OzProofPictures.TwinMin:F0} / 255)", d >= OzProofPictures.TwinMin);
        }
        /// <summary>K256e: a picture with the camera on a point (the locked enemy in the middle of the screen), then back to following.</summary>
        IEnumerator ShotAt(string name, Vector3 at)
        {
            look = at; yield return null; yield return null; yield return null;
            Shot(name); yield return OzProofPictures.AfterGrab(); look = null;
        }
        Vector3 PosOf(ZObject o) { var v = o != null && g.Views.TryGetValue(o.Id, out var uv) ? uv : null; return v ? v.transform.position : (o != null ? ZGame.PxToWorld(o.CenterX, o.CenterY) : Vector3.zero); }
        /// <summary>K256e: nothing standing (a hut, a rock, a building) within m of a map point (px).</summary>
        bool OpenAround(int x, int y, float m)
        {
            var c = ZGame.PxToWorld(x, y);
            foreach (var kv in g.StaticViews) { if (!kv.Value) continue; var p = kv.Value.transform.position; if (new Vector2(p.x - c.x, p.z - c.z).magnitude < m) return false; }
            return true;
        }
        /// <summary>K256e: units that leave without a trace (out of the rules, their views gone).</summary>
        void Vanish(ZObject o)
        {
            if (o == null) return;
            var all = new List<ZObject> { o }; all.AddRange(o.Minions);
            foreach (var x in all) { x.KillAt = 0; if (g.Views.TryGetValue(x.Id, out var v)) { g.Views.Remove(x.Id); if (v) Destroy(v.gameObject); } }
        }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP"))) n++; return n; }
        void SetClock(float t) { if (dn) { dn.timeOfDay = t; dn.Apply(t); } }

        // ------------------------------------------------------------------ helpers (as the K256b proof's)
        bool Move(int x, int y) => W.SendWaypoints(me, s, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool Enter(ZObject sq) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
        ZObject Squad(string type, int tx, int ty, Team t, int amount = -1)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t, amount); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        /// <summary>A blue robot this far (m) from the ship's ground point at this angle from its heading: it never fires and is never
        /// shot dead by anyone (the proof destroys it itself).</summary>
        ZObject Enemy(float distM, float offDeg)
        {
            double a = (s.ShuttleHeadingDeg + offDeg) * System.Math.PI / 180.0;
            int tx = (int)(s.CenterX + System.Math.Cos(a) * distM * 8.0) >> 4, ty = (int)(s.CenterY + System.Math.Sin(a) * distM * 8.0) >> 4;
            var o = Squad("grunt", tx, ty, them, 1); if (o == null) return null;
            o.Damage = 0; o.AttackRadius = 0; o.MaxHealth = o.Health = 1000000000;
            enemies.Add(o);
            return o;
        }
        void KillUnit(ZObject o) { if (o == null || o.Destroyed) return; o.SetHealth(0); W.UpdateObjectHealth(o); }
        /// <summary>The flat distance from the nose searchlight to a unit as drawn (m).</summary>
        float DistM(ZObject o)
        {
            var v = o != null && g.Views.TryGetValue(o.Id, out var uv) ? uv : null; var h = night.NoseHead;
            if (!v || !h) return 999f;
            var p = v.transform.position; return new Vector2(p.x - h.position.x, p.z - h.position.z).magnitude;
        }
        /// <summary>How far the nose searchlight's beam looks off a unit's middle (degrees).</summary>
        float BeamOff(ZObject o)
        {
            var v = o != null && g.Views.TryGetValue(o.Id, out var uv) ? uv : null; var h = night.NoseHead;
            if (!v || !h) return 180f;
            var q = v.transform.position; q.y = g.GroundY(q.x, q.z) + OzShuttleNightLights.AimRobotM;
            return Vector3.Angle(h.forward, q - h.position);
        }
        int[] Pad(int r, float angDeg, int span) => Pad(s, r, angDeg, span);
        int[] Pad(ZObject from, int r, float angDeg, int span)
        {
            for (int dr = 0; dr <= 200; dr += 16)
                for (int k = 0; k <= span; k += 10)
                    foreach (int sg in new[] { 1, -1 })
                    {
                        double a = (angDeg + sg * k) * System.Math.PI / 180.0;
                        var q = W.ShuttleClamp(from.CenterX + (int)(System.Math.Cos(a) * (r + dr)), from.CenterY + (int)(System.Math.Sin(a) * (r + dr)));
                        if (W.ShuttlePadFree(q.x, q.y, from, out _)) return new[] { q.x, q.y };
                    }
            return null;
        }
        int[] Spot(System.Func<int, int, string, bool> want, int near)
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

        // ------------------------------------------------------------------ per frame: the camera on the ship, the measurements
        bool rec; float recMaxStep, recLead, recLast = -1f; int recDowns, recUps, recFrames;
        float recMaxRate, recT, recAt05 = -1f, recAt95 = -1f;                                              // K256e: per second; when 5 % and 95 % were passed
        bool airWas; float sinceLift = 99f, litAfterLift;                                                  // K256e: the landing lights after the lift-off
        float pulseLo = 9f, pulseHi = -9f, yawLo = 999f, yawHi = -999f, landingHighCruise, cruiseT; int landingHighFrames, lowLandingFrames, cruiseFrames;
        void ResetRec() { recMaxStep = recLead = 0f; recLast = -1f; recDowns = recUps = recFrames = 0; pulseLo = 9f; pulseHi = -9f; yawLo = 999f; yawHi = -999f; landingHighCruise = 0f; landingHighFrames = lowLandingFrames = cruiseFrames = 0; recMaxRate = 0f; recT = 0f; recAt05 = recAt95 = -1f; }
        void Update()
        {
            if (s == null || !ship || ship.BrokenUp || night == null) return;
            if (look.HasValue && rts) rts.focus = new Vector3(look.Value.x, 0f, look.Value.z);              // K256e: the locked enemy in the middle
            else if (follow && rts) { var p = (watch ? watch : ship.transform).position; rts.focus = new Vector3(p.x, 0f, p.z); }
            var P = night.Plan;
            pulseLo = Mathf.Min(pulseLo, P.Pulse); pulseHi = Mathf.Max(pulseHi, P.Pulse);
            yawLo = Mathf.Min(yawLo, P.YawNow); yawHi = Mathf.Max(yawHi, P.YawNow);
            cruiseT = s.ShuttleState == ZShuttleState.Cruise ? cruiseT + Time.deltaTime : 0f;
            if (s.ShuttleAirborne && !airWas) { sinceLift = 0f; litAfterLift = 0f; }                      // K256e: the lift-off
            airWas = s.ShuttleAirborne; sinceLift += Time.deltaTime;
            if (s.ShuttleAirborne && sinceLift < 6f && P.Landing > 0.9f) litAfterLift += Time.deltaTime;
            if (s.ShuttleState == ZShuttleState.Cruise && ship.FeetHeight > 10f && cruiseT > 1.5f)       // K256e: the landing lights fade 1 s after their 2 s hold
            {                                                                                      // a second into the flight at cruise height
                cruiseFrames++;
                float goal = s.ShuttleGoalX >= 0 ? (ZGame.PxToWorld(s.ShuttleGoalX, s.ShuttleGoalY) - ZGame.PxToWorld(s.CenterX, s.CenterY)).magnitude : -1f;
                if (goal > OzShuttleNightPlan.ApproachM + 6f) { landingHighCruise = Mathf.Max(landingHighCruise, P.Landing); if (P.Landing > 0.05f) landingHighFrames++; }
            }
            if (s.ShuttleAirborne && ship.FeetHeight < 6f && ship.FeetHeight > 1.5f && s.ShuttleState == ZShuttleState.Climb && P.Landing > 0.9f) lowLandingFrames++;
            if (!rec) return;
            recFrames++;
            float n = P.Nose, gate = OzShuttleNightPlan.Gate(Mathf.Clamp01(NightGrade.NightK));
            if (recLast >= 0f) { recMaxStep = Mathf.Max(recMaxStep, Mathf.Abs(n - recLast)); if (n < recLast - 1e-4f) recDowns++; if (n > recLast + 1e-4f) recUps++; }
            if (recLast >= 0f && Time.deltaTime > 1e-4f) recMaxRate = Mathf.Max(recMaxRate, Mathf.Abs(n - recLast) / Time.deltaTime);   // K256e: per second
            recT += Time.deltaTime; if (recAt05 < 0f && n >= 0.05f) recAt05 = recT; if (recAt95 < 0f && n >= 0.95f) recAt95 = recT;
            recLead = Mathf.Max(recLead, n - gate); recLast = n;
        }

        // ------------------------------------------------------------------ the stages
        IEnumerator All()
        {
            T0 = Time.unscaledTime; Application.targetFrameRate = 60; dayLit0 = OzShuttleNightLights.DayLitFrames;
            take = OzSoundTake.Begin(System.IO.Path.Combine(dir, "sound_take_night.txt"));                    // K256e: the whole take, no line cap
            if (W == null || W.PlayableShuttle == null || !OzPlayableShuttle.I) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on, not 'Zod exact')"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = OzPlayableShuttle.I; me = s.Owner; them = me == Team.Red ? Team.Blue : Team.Red;
            night = ship.Searchlights != null ? ship.Searchlights.Night : null;
            Log($"K256d shuttle night lights proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, quality {OzQuality.Name}, CALM MODE {calm0}");
            Check("the ship's night lights are built (the nose searchlight, the ramp light, the landing lights' rule)", night != null && !night.Dead && night.NoseHead && night.NoseLight && night.RampLight && OzShuttleNightLights.Built > 0);
            if (night == null) { Finish(); yield break; }
            int bots = W.Bots.Count; W.Bots.Clear();
            if (OzMotion.Reduced) OzMotion.Reduced = false;
            SetClock(0.10f); if (dn) dn.running = false;
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            Log($"set-up: CPU removed {bots}, day 0.10 held, camera 70 m, CALM MODE off for the take");
            yield return Wait(1.0f);
            if (s.ShuttleState != ZShuttleState.Landed) yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 40f);
            yield return N1(); yield return N2(); yield return N3(); yield return N4(); yield return N5(); yield return N6();
            yield return N7(); yield return N8(); yield return N9(); yield return N10(); yield return N11();
            Finish();
        }

        int[] hoverAt;
        // N1: by day, a flight to a place it cannot land: nothing burns
        IEnumerator N1()
        {
            Log("N1 by day (clock 0.10): a flight to open landable ground with an own robot standing on it (it cannot land there and hovers; the robot then leaves: the ground under it is landable, it hovers on until the next order)");
            int day0 = OzShuttleNightLights.DayLitFrames; float t0 = Time.unscaledTime;
            hoverAt = Spot((x, y, why) => why == "" && OpenAround(x, y, 10f), 200);                            // K256e: landable open ground, no hut near
            if (hoverAt == null) { Check("N1 open landable ground to hover over", false); yield break; }
            var stand = Squad("grunt", hoverAt[0] >> 4, hoverAt[1] >> 4, me, 1); yield return Wait(0.6f);
            if (W.ShuttlePadFree(hoverAt[0], hoverAt[1], s, out _))
            {   // the robot did not end up on the pad: the round-2 stage (over rocks or a building it cannot land on)
                Log("N1 the robot is not on the pad: hovering over rocks or a building instead");
                Vanish(stand); stand = null;
                hoverAt = Spot((x, y, why) => why == "rocks" || why == "building", 160) ?? Spot((x, y, why) => why == "water", 160);
                if (hoverAt == null) { Check("N1 a place to hover over", false); yield break; }
            }
            Check("N1 the move order is taken", Move(hoverAt[0], hoverAt[1]));
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise, 12f); yield return Wait(1.5f);
            Shot("21_01_day_flight_no_lights_h70");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover, 40f);
            Log($"N1 hovering: it could not land because of \"{s.ShuttleNoLand}\"; the robot leaves");
            if (stand != null) Vanish(stand);
            yield return Wait(1.0f);
            bool landable = W.ShuttlePadFree(hoverAt[0], hoverAt[1], s, out string why1);
            var P = night.Plan;
            Log($"by day: frames any night light burned {OzShuttleNightLights.DayLitFrames - day0}; now landing {P.Landing:F2} nose {P.Nose:F2} ramp {P.Ramp:F2}; the belly lamps' level {ship.Searchlights.Level:F2}; state {s.ShuttleState}");
            Check("N1 by day no night light burns (landing lights, nose searchlight, ramp light): 0 frames", OzShuttleNightLights.DayLitFrames - day0 == 0 && P.Landing == 0f && P.Nose == 0f && ship.Searchlights.Level == 0f);
            Check($"N1 it hovers over landable open ground (the stage for the night; the pad now {(landable ? "free" : why1)}, no hut within 10 m)", s.ShuttleState == ZShuttleState.Hover && landable);
        }

        // N2: dusk while it hovers: the lights come up with the night share
        IEnumerator N2()
        {
            Log("N2 dusk: the clock from 0.36 to 0.62 over 10 s while it hovers");
            ResetRec(); rec = true; bool shot = false;
            float t = 0f;
            while (t < 10f)
            {
                SetClock(Mathf.Lerp(0.36f, 0.62f, t / 10f)); t += Time.deltaTime;
                if (!shot && NightGrade.NightK > 0.2f && NightGrade.NightK < 0.4f) { shot = true; Shot("21_02_dusk_lights_coming_up_h70"); }
                yield return null;
            }
            SetClock(0.62f); yield return Wait(0.6f); rec = false;
            var P = night.Plan;
            float upSec = recAt05 >= 0f && recAt95 >= 0f ? recAt95 - recAt05 : -1f;
            Log($"dusk: frames {recFrames}, the nose light's largest step a frame {recMaxStep:F3}, fastest {recMaxRate:F2} a second, from 5 % to 95 % in {upSec:F2} s, ahead of the night gate by {recLead:F3}, steps down {recDowns}; night share {NightGrade.NightK:F2}, nose {P.Nose:F2}");
            Check("N2 the lights come up with the night share: smoothly (at most 0.6 a second, at least 1.75 s from 5 % to 95 %), never ahead of it, never down, fully on at night", recMaxRate <= 0.6f && upSec >= 1.75f && recLead <= 0.01f && recDowns == 0 && P.Nose >= 0.95f);
        }

        // N3: the sweep, the lock on the nearer of two enemies, letting go, back to the white sweep
        IEnumerator N3()
        {
            Log("N3 at night, hovering: the sweep, then two enemies");
            ResetRec(); yield return Wait(9.5f);
            Log($"the sweep over one swing: the drum {yawLo:F1} .. {yawHi:F1} deg from the nose; nose {night.Plan.Nose:F2}; nose real light frames {OzShuttleNightLights.RealNoseFrames}, its pool frames {OzShuttleNightLights.PoolNoseFrames}");
            Check("N3 the nose searchlight sweeps the ground ahead in a slow arc (>= 40 deg from side to side in one swing), its light on the ground (a real light or its pool)", yawHi - yawLo >= 40f && night.Plan.Nose > 0.9f && (OzShuttleNightLights.RealNoseFrames > 0 || OzShuttleNightLights.PoolNoseFrames > 0));
            Shot("21_03_night_hover_nose_sweep_h70");
            var a = Enemy(13f, 25f); var b = Enemy(22f, -35f);
            yield return Wait(0.2f);
            float da = DistM(a), db = DistM(b); var near = da <= db ? a : b; var far = near == a ? b : a;
            Log($"enemies: #{(a != null ? a.Id : -1)} at {da:F1} m, #{(b != null ? b.Id : -1)} at {db:F1} m (reach {OzShuttleNightPlan.LockRangeM:F0} m)");
            float t0 = Time.unscaledTime; int beeps0 = OzShuttleNightLights.BeepsPlayed, lock0 = night.Plan.LockOns;
            yield return WaitFor(() => night.Plan.Locked, 4f);
            Check("N3 it takes the nearer enemy in reach", a != null && b != null && Mathf.Min(da, db) <= OzShuttleNightPlan.LockRangeM && night.Plan.LockId == near.Id);
            yield return Wait(0.8f);
            ResetRec(); yield return Wait(2.6f);
            var P = night.Plan; P.Colour(out float cr, out float cg, out float cb);
            float off = BeamOff(near);
            Log($"on it: alarm {P.Alarm:F2}, cone {P.AngleDeg:F1} deg, edge {P.InnerShare:F2}, colour {cr:F2} {cg:F2} {cb:F2}, the beam {off:F1} deg off its middle, pulse {pulseLo:F2} .. {pulseHi:F2}; beeps +{OzShuttleNightLights.BeepsPlayed - beeps0} (sound log: {Played(t0, OzShuttleNightLights.LockEvent)})");
            Check("N3 ALARMING on the enemy: narrow (<= 12 deg), hard-edged, amber-red, the beam on it (<= 6 deg), a slow warning pulse (0.55 .. 1)", P.Alarm >= 0.95f && P.AngleDeg <= 12f && P.InnerShare >= 0.75f && cr >= 0.95f && cg >= 0.35f && cg <= 0.5f && cb <= 0.15f && off <= 6f && pulseLo <= 0.62f && pulseHi >= 0.98f);
            Check("N3 the short lock-on beep, once (in the sound log)", OzShuttleNightLights.BeepsPlayed - beeps0 == 1 && night.Plan.LockOns - lock0 == 1 && Played(t0, OzShuttleNightLights.LockEvent) >= 1);
            yield return ShotAt("21_04_night_lock_alarm_red_h70", PosOf(near));                               // K256e: the locked enemy in the middle
            look = PosOf(near); yield return Close("21_04_night_lock_alarm_red"); look = null;
            // the nearer destroyed: let go, then the other
            int rel0 = night.Plan.Releases; KillUnit(near);
            yield return WaitFor(() => night.Plan.LockId == far.Id && night.Plan.Locked, 4f);
            Log($"#{near.Id} destroyed: let go +{night.Plan.Releases - rel0}, now on #{night.Plan.LockId} (locked {night.Plan.Locked})");
            Check("N3 the enemy destroyed: it lets go and takes the other one", night.Plan.Releases > rel0 && night.Plan.LockId == far.Id && night.Plan.Locked);
            yield return Wait(0.8f); yield return ShotAt("21_05_night_relock_second_enemy_h70", PosOf(far));
            KillUnit(far);
            yield return WaitFor(() => night.Plan.LockId < 0, 2f); yield return Wait(1.2f);
            ResetRec(); yield return Wait(3.0f);
            P.Colour(out cr, out cg, out cb);
            Log($"nobody left: alarm {P.Alarm:F3}, colour {cr:F2} {cg:F2} {cb:F2}, cone {P.AngleDeg:F1}, the drum {yawLo:F1} .. {yawHi:F1} deg in 3 s, pulse {pulseLo:F2} .. {pulseHi:F2}");
            Check("N3 the enemies gone: back to the calm white sweep (no alarm, white, the wide cone, sweeping, no pulse)", P.LockId < 0 && P.Alarm <= 0.01f && cg >= 0.9f && cb >= 0.98f && P.AngleDeg >= OzShuttleNightPlan.NoseAngleDeg - 0.1f && yawHi - yawLo >= 10f && pulseLo >= 0.999f);
            Shot("21_06_night_back_to_white_sweep_h70");
            yield break;
        }

        // N4: CALM MODE: the colour changes, the pulse never runs
        IEnumerator N4()
        {
            Log("N4 CALM MODE on: a third enemy");
            OzMotion.Reduced = true;
            int calmPulse0 = OzShuttleNightLights.CalmPulseFrames, calmAlarm0 = OzShuttleNightLights.CalmAlarmFrames, beeps0 = OzShuttleNightLights.BeepsPlayed;
            var c = Enemy(15f, 10f);
            yield return WaitFor(() => night.Plan.Locked, 4f); yield return Wait(0.8f);
            ResetRec(); yield return Wait(3.0f);
            var P = night.Plan; P.Colour(out float cr, out float cg, out float cb);
            Log($"CALM MODE on it: alarm {P.Alarm:F2}, colour {cr:F2} {cg:F2} {cb:F2}, cone {P.AngleDeg:F1}, pulse {pulseLo:F3} .. {pulseHi:F3}, frames dipping with CALM MODE {OzShuttleNightLights.CalmPulseFrames - calmPulse0} (alarm frames {OzShuttleNightLights.CalmAlarmFrames - calmAlarm0}), beeps +{OzShuttleNightLights.BeepsPlayed - beeps0}");
            Check("N4 CALM MODE: amber-red and narrow on the enemy, the beep plays, but no pulse (0 frames under full light)", c != null && P.LockId == c.Id && P.Alarm >= 0.95f && cr >= 0.95f && cg <= 0.5f && pulseLo >= 0.999f && OzShuttleNightLights.CalmPulseFrames - calmPulse0 == 0 && OzShuttleNightLights.CalmAlarmFrames - calmAlarm0 > 60 && OzShuttleNightLights.BeepsPlayed - beeps0 == 1);
            yield return ShotAt("21_07_night_lock_calm_mode_no_pulse_h70", PosOf(c));
            KillUnit(c); yield return WaitFor(() => night.Plan.LockId < 0, 2f);
            OzMotion.Reduced = false;
            yield return Wait(1.0f);
        }

        // N5: LOW: fewer real lights, no light shadows
        IEnumerator N5()
        {
            Log("N5 the presets: what the ship offers to the night budget");
            OzShuttleNightLights.ProofPreset = 0; yield return null; yield return null;
            int hi = night.OfferedNow, hiSh = night.ShadowCandidatesNow;
            OzShuttleNightLights.ProofPreset = 2; yield return null; yield return null;
            int lo = night.OfferedNow, loSh = night.ShadowCandidatesNow;
            yield return Wait(0.8f);
            bool landingPool = ship.Searchlights.LampPool(1) || !ship.Searchlights.On;
            Shot("21_08_night_low_preset_h70");
            Log($"offered real lights: HIGH {hi} (shadow candidates {hiSh}), LOW {lo} (shadow candidates {loSh}); the nose light shadows now {night.NoseLight.shadows}");
            Check("N5 LOW: 2 real lights instead of 4 (the second landing light and the ramp light keep lens and painted pool), no light shadows; HIGH: the nose searchlight may cast them", hi == 4 && hiSh == 1 && lo == 2 && loSh == 0 && night.NoseLight.shadows == LightShadows.None && landingPool);
            OzShuttleNightLights.ProofPreset = -1; yield return null;
        }

        // N6: dawn, then the night again
        IEnumerator N6()
        {
            Log("N6 dawn: the clock from 0.80 to 0.96 over 8 s while it hovers");
            ResetRec(); rec = true;
            float t = 0f; bool shot = false;
            while (t < 8f)
            {
                SetClock(Mathf.Lerp(0.80f, 0.96f, t / 8f)); t += Time.deltaTime;
                if (!shot && NightGrade.NightK < 0.4f && NightGrade.NightK > 0.15f) { shot = true; Shot("21_09_dawn_lights_going_down_h70"); }
                yield return null;
            }
            yield return Wait(0.8f); rec = false;
            var P = night.Plan;
            Log($"dawn: the nose light's largest step a frame {recMaxStep:F3}, steps up {recUps}; by day: nose {P.Nose:F2}, landing {P.Landing:F2}");
            Check("N6 the lights go down with the night share at dawn, smoothly, never up, out by day", recMaxStep <= 0.06f && recUps == 0 && P.Nose == 0f && P.Landing == 0f);
            SetClock(0.62f); yield return Wait(0.1f); float at01 = P.Nose; yield return Wait(2.3f);
            Log($"the night again at once: nose {at01:F2} after 0.1 s, {P.Nose:F2} after 2.4 s");
            Check("N6 a sudden change of the clock: they come on softly over 2 s (<= 0.1 after 0.1 s), fully after 2.4 s", at01 <= 0.1f && P.Nose >= 0.95f);
        }

        // N7: the landing at night
        IEnumerator N7()
        {
            Log("N7 the landing at night");
            var q = Pad(320, (float)s.ShuttleHeadingDeg + 180f, 140); if (q == null) q = Pad(320, 0f, 180);
            if (q == null) { Check("N7 a landing place 40 m away", false); yield break; }
            OzShuttleNightLights.WorstLandingOffDeg = 0f; int aim0 = OzShuttleNightLights.LandingAimFrames;
            Check("N7 the move order is taken", Move(q[0], q[1]));
            var goal = ZGame.PxToWorld(q[0], q[1]);
            yield return WaitFor(() => { var p = ship.transform.position; return s.ShuttleState == ZShuttleState.Cruise && new Vector2(p.x - goal.x, p.z - goal.z).magnitude < 16f; }, 40f);
            yield return Wait(0.2f);
            var P = night.Plan; bool inLit = P.Landing > 0.9f && P.LandingAtGoal;
            Shot("21_10_night_flying_in_landing_lights_on_the_pad_h70");
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 20f); yield return Wait(0.9f);
            float spots = SpotsApart();
            Shot("21_11_night_descent_landing_lights_h70");
            yield return Close("21_11_night_descent_landing_lights");
            bool downLit = P.Landing > 0.95f && P.LandingAtGoal;
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 15f);
            yield return Wait(0.5f); float after05 = P.Landing; float nose05 = P.Nose;
            yield return Wait(1.8f); float after23 = P.Landing;
            Log($"flying in: lit {inLit}; coming down: lit {downLit}; the lamps' worst look off the landing point over 2.5 m {OzShuttleNightLights.WorstLandingOffDeg:F1} deg ({OzShuttleNightLights.LandingAimFrames - aim0} frames); after the touchdown: {after05:F2} at 0.5 s (nose {nose05:F2}), {after23:F2} at 2.3 s");
            Check("N7 the landing lights burn while it flies in and comes down, aimed at the landing point (the lamps within 10 deg of it)", inLit && downLit && OzShuttleNightLights.LandingAimFrames - aim0 > 10 && OzShuttleNightLights.WorstLandingOffDeg <= 10f);
            Log($"N7 the two landing cones ({OzShuttleSearchlights.AngleDeg:F0} deg each): their spots on the ground {spots:F2} m apart (the front and the rear of the pad)");
            Check("N7 two distinct landing cones (front and rear of the pad, 25 deg each): their spots at least 3 m apart", spots >= 3f && OzShuttleSearchlights.AngleDeg <= 26f);
            Check("N7 landed: on for a moment, then out (eased); the nose searchlight out on the ground", after05 >= 0.9f && after23 <= 0.02f && nose05 <= 0.05f);
        }

        // N8: boarding at night: the ramp light
        IEnumerator N8()
        {
            Log("N8 boarding at night: a squad 10 m in front of the dome, ENTER");
            double hd = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            int fx = (int)(s.CenterX + System.Math.Cos(hd) * 80) >> 4, fy = (int)(s.CenterY + System.Math.Sin(hd) * 80) >> 4;
            var sq = Squad("grunt", fx, fy, me); yield return Wait(0.8f);
            int shuts0 = s.ShuttleRampShuts, ramp0 = OzShuttleNightLights.RampOnFrames, real0 = OzShuttleNightLights.RealRampFrames, pool0 = OzShuttleNightLights.PoolRampFrames;
            Check("N8 the ENTER order is taken", Enter(sq));
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Open, 30f); yield return Wait(1.0f);
            var P = night.Plan; float lit = P.Ramp;
            Shot("21_12_night_boarding_ramp_light_h70");
            yield return Close("21_12_night_boarding_ramp_light");
            yield return WaitFor(() => s.ShuttleRampShuts > shuts0, 20f); yield return Wait(1.5f);
            Log($"boarding: the ramp light {lit:F2} with the ramp down, frames on {OzShuttleNightLights.RampOnFrames - ramp0} (a real light {OzShuttleNightLights.RealRampFrames - real0}, its pool {OzShuttleNightLights.PoolRampFrames - pool0}); after the shut {P.Ramp:F2}; aboard {s.Passengers.Count}");
            Check("N8 the warm ramp light burns while the squad walks in (>= 0.9; a real light or its pool on the ground)", lit >= 0.9f && OzShuttleNightLights.RampOnFrames - ramp0 > 30 && (OzShuttleNightLights.RealRampFrames - real0 > 0 || OzShuttleNightLights.PoolRampFrames - pool0 > 0));
            Check("N8 the ramp shut: the ramp light out", P.Ramp <= 0.02f && s.Passengers.Count > 0);
        }

        // N9: a flight past an enemy: low the landing lights, high the nose searchlight, the lock in flight and letting go
        IEnumerator N9()
        {
            Log("N9 a flight past an enemy");
            var q = Pad(600, (float)s.ShuttleHeadingDeg, 120); if (q == null) q = Pad(600, 90f, 180);
            if (q == null) { Check("N9 a landing place 75 m away", false); yield break; }
            var from = ZGame.PxToWorld(s.CenterX, s.CenterY); var to = ZGame.PxToWorld(q[0], q[1]);
            var mid = Vector3.Lerp(from, to, 0.45f); var side = new Vector3(-(to - from).z, 0f, (to - from).x).normalized * 9f;
            int ex = (int)((mid.x + side.x) * 8f) >> 4, ey = (int)(-(mid.z + side.z) * 8f) >> 4;
            var d = Squad("grunt", ex, ey, them, 1);
            if (d != null) { d.Damage = 0; d.AttackRadius = 0; d.MaxHealth = d.Health = 1000000000; enemies.Add(d); }
            ResetRec();
            int acq0 = night.Plan.Acquires, rel0 = night.Plan.Releases, beeps0 = OzShuttleNightLights.BeepsPlayed;
            Check("N9 the move order is taken (after the ramp shut)", Move(q[0], q[1]));
            yield return WaitFor(() => s.ShuttleAirborne && ship.FeetHeight > 3.5f, 10f);
            Shot("21_13_night_takeoff_landing_lights_low_h70");
            bool locked = false; float lockedDist = 0f;
            yield return WaitFor(() =>
            {
                if (!locked && d != null && night.Plan.LockId == d.Id && night.Plan.Locked) { locked = true; lockedDist = DistM(d); }
                return (locked && night.Plan.Alarm > 0.9f) || s.ShuttleState == ZShuttleState.Descend;
            }, 45f);
            if (locked && d != null) yield return ShotAt("21_14_night_lock_in_flight_h70", PosOf(d));         // K256e: the locked enemy in the middle
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Descend, 45f);
            bool letGo = night.Plan.LockId < 0 || (d != null && night.Plan.LockId != d.Id); float goneDist = DistM(d);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 15f);
            Log($"the take-off: landing lights on low {lowLandingFrames} frames; at cruise height far from the pad: landing lights at most {landingHighCruise:F2} ({landingHighFrames} of {cruiseFrames} frames over 0.05); the enemy #{(d != null ? d.Id : -1)}: taken {night.Plan.Acquires - acq0} (at {lockedDist:F1} m), let go {night.Plan.Releases - rel0} (it is {goneDist:F1} m off when it comes down), beeps +{OzShuttleNightLights.BeepsPlayed - beeps0}");
            Log($"N9 the landing lights after the lift-off: on (>= 0.9) for {litAfterLift:F2} s (the hold {OzShuttleNightPlan.TakeoffHoldSec:F1} s, then a {OzShuttleNightPlan.TakeoffFadeSec:F1} s fade)");
            Check("N9 the landing lights burn low at the take-off (at least 1.9 s after the lift-off) and are off at cruise height (far from the landing point)", lowLandingFrames > 5 && litAfterLift >= 1.9f && landingHighFrames == 0);
            Check("N9 in flight it takes the enemy beside its way, with the beep, and lets go once it is out of reach", locked && night.Plan.Acquires > acq0 && night.Plan.Releases > rel0 && letGo && OzShuttleNightLights.BeepsPlayed > beeps0 && goneDist > OzShuttleNightPlan.LockRangeM);
            KillUnit(d);
        }

        // N10: unloading at night
        IEnumerator N10()
        {
            Log("N10 EXIT at night");
            if (s.Passengers.Count == 0) { Check("N10 a squad aboard for the exit", false); yield break; }
            int shuts0 = s.ShuttleRampShuts, ramp0 = OzShuttleNightLights.RampOnFrames;
            yield return WaitFor(() => s.CanEjectCrew(), 6f);
            Check("N10 EXIT is taken", W.Eject(me, s));
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Open, 6f); yield return Wait(1.6f);
            var P = night.Plan; float lit = P.Ramp;
            Shot("21_15_night_unloading_ramp_light_h70");
            yield return WaitFor(() => s.ShuttleRampShuts > shuts0, 15f); yield return Wait(1.5f);
            Log($"unloading: the ramp light {lit:F2} with the ramp down, frames on {OzShuttleNightLights.RampOnFrames - ramp0}; after the shut {P.Ramp:F2}");
            Check("N10 the warm ramp light burns while the squad walks out, and goes out after the ramp has shut", lit >= 0.9f && OzShuttleNightLights.RampOnFrames - ramp0 > 30 && P.Ramp <= 0.02f);
        }

        // N11: a shuttle dropped from the testing panel (its spawn path: OzCheatShuttle.Spawn) has the same night lights
        IEnumerator N11()
        {
            Log("N11 a shuttle dropped from the testing panel (P) at night, then flown a short hop");
            int[] q = null;
            foreach (float a in new[] { 90f, -90f, 180f, 0f, 45f, -45f, 135f, -135f }) { var c = Pad(320, (float)s.ShuttleHeadingDeg + a, 40); if (c != null && OpenAround(c[0], c[1], 9f)) { q = c; break; } }   // K256e: no hut near
            if (q == null) q = Pad(320, (float)s.ShuttleHeadingDeg + 90f, 160);
            if (q == null) { Check("N11 room for a dropped shuttle", false); yield break; }
            var o = OzCheatShuttle.Spawn(g, OpenZHud.I, me, new Vector2Int(q[0], q[1]));
            yield return null; yield return null;
            var v = OzPlayableShuttle.Of(o); var nl = v && v.Searchlights != null ? v.Searchlights.Night : null;
            Check("N11 the dropped shuttle carries its own night lights (the nose searchlight, the ramp light, the landing lights' rule)", o != null && v && nl != null && !nl.Dead && nl.NoseHead && nl.NoseLight && nl.RampLight && nl != night);
            if (nl == null) yield break;
            int[] to = null;
            foreach (float a in new[] { 0f, 30f, -30f, 60f, -60f, 90f, -90f }) { var c = Pad(o, 360, (float)o.ShuttleHeadingDeg + a, 30); if (c != null && OpenAround(c[0], c[1], 9f)) { to = c; break; } }
            if (to == null) to = Pad(o, 360, (float)o.ShuttleHeadingDeg, 180);
            watch = v.transform; yield return Wait(0.5f);                                                       // K256e: the camera on it BEFORE its order (its sounds are on screen)
            if (to == null || !W.SendWaypoints(me, o, new[] { new ZWaypoint { mode = WpMode.Move, x = to[0], y = to[1], playerGiven = true } })) { Check("N11 the dropped shuttle takes a move order", false); yield break; }
            float t11 = Time.unscaledTime;
            float lowMax = 0f, noseMax = 0f;
            yield return WaitFor(() => { if (o.ShuttleAirborne && v.FeetHeight < 7f) lowMax = Mathf.Max(lowMax, nl.Plan.Landing); if (o.ShuttleState == ZShuttleState.Cruise) noseMax = Mathf.Max(noseMax, nl.Plan.Nose); return o.ShuttleState == ZShuttleState.Cruise && noseMax > 0.9f && OpenAround(o.CenterX, o.CenterY, 7f); }, 20f);
            bool beam = nl.NoseBeamShown, pool = nl.NosePoolShown || nl.NoseLight.enabled, lens = nl.NoseLensShown;
            Shot("21_16_night_dropped_shuttle_lights_h70");
            Log($"N11 at the picture: over open ground {OpenAround(o.CenterX, o.CenterY, 7f)}; the nose beam drawn {beam}, its pool or real light {pool}, its lens {lens}; spool-up {Played(t11, "shuttle_spool_up")}, take-off {Played(t11, "shuttle_takeoff")} (heard on screen)");
            Check("N11 the dropped ship's picture: its nose beam and its pool (or real light) drawn, over open ground (no hut under it)", beam && pool && OpenAround(o.CenterX, o.CenterY, 7f));
            Check("N11 its take-off is heard (the camera was on it before the order: not dropped as off screen)", Played(t11, "shuttle_takeoff") > 0);
            yield return WaitFor(() => o.ShuttleState == ZShuttleState.Landed && !o.ShuttleAirborne, 40f);
            watch = null;
            Log($"the dropped shuttle #{(o != null ? o.Id : -1)}: landing lights low {lowMax:F2}, nose searchlight in flight {noseMax:F2}; ships with night lights {OzShuttleNightLights.Built}");
            Check("N11 its landing lights burn low and its nose searchlight in flight, as on level 16's own ship", lowMax >= 0.9f && noseMax >= 0.9f);
        }

        /// <summary>K256e: how far apart (m) the two landing lamps' spots are on the ground now.</summary>
        float SpotsApart()
        {
            var sl = ship.Searchlights; if (sl == null) return 0f;
            var a = sl.LampHead(0); var b = sl.LampHead(1); if (!a || !b) return 0f;
            Vector3 Hit(Transform h) { var d = h.forward; float gy = g.GroundY(h.position.x, h.position.z); float t = d.y < -0.05f ? (h.position.y - gy) / -d.y : 0f; return h.position + d * t; }
            var pa = Hit(a); var pb = Hit(b); return new Vector2(pa.x - pb.x, pa.z - pb.z).magnitude;
        }

        void Finish()
        {
            Check($"no night light ever burned by day in the whole take ({OzShuttleNightLights.DayLitFrames - dayLit0} frames)", OzShuttleNightLights.DayLitFrames - dayLit0 == 0);
            var dups = OzProofPictures.Duplicates(shots);
            Log($"pictures of the take {shots.Count}, the same file twice: {(dups.Count == 0 ? "none" : string.Join(", ", dups))}");
            Check("no two pictures of the take are the same file (md5)", dups.Count == 0);
            Log($"switch clicks: played {OzShuttleNightLights.Clicks}, merged into one change {OzShuttleNightLights.ClicksMerged}");
            if (take)
            {   // K256e: M13 counted over the WHOLE night take (no line cap); M15 the lock beep at 0.12 or more (the switch click's row also
                // holds the level's own lamps and the fort's relay, so its quietest is only listed in the table)
                take.Flush();
                int ov = take.VoiceOverlaps(T0, out string vw, out int nR, out int nS);
                Log($"voices over the whole take: Rachel {nR} lines, the shuttle's {nS} lines, overlapping pairs {ov} (worst: {vw})");
                Check("M13 no Rachel line ever overlaps a shuttle line in the whole night take (every line, no cap): 0", ov == 0);
                var r = take.Of(OzShuttleNightLights.LockEvent);
                Check($"M15 {OzShuttleNightLights.LockEvent} (the lock beep): played in the take ({(r != null ? r.played : 0)}), its quietest play at least 0.12 ({(r != null && r.played > 0 ? r.lo : 0f):F3})", r != null && r.played > 0 && r.lo >= 0.12f);
            }
            foreach (var e in enemies) KillUnit(e);
            var keys = new HashSet<string> { OzShuttleNightLights.LockEvent, "wpn_target_lock", "ngt_searchlight_on", "ngt_light_fizzle", "shuttle_door_open", "shuttle_door_close", "shuttle_ramp_hydraulic", "latch",
                                             "shuttle_spool_up", "shuttle_takeoff", "shuttle_landing", "vo_shuttle_board", "vo_shuttle_unload" };
            var snd = new StringBuilder(); sb.AppendLine("sound log (the night lights' and the ship's):");
            foreach (var e in OzSoundLog.Since(T0))
            {
                string line = $"  {e.t - T0,7:F2} {e.src} {e.key} {e.clip} {e.outcome} vol {e.vol:F2} dist {e.dist:F1}";
                snd.AppendLine(line); if (keys.Contains(e.key)) sb.AppendLine(line);
            }
            Log($"counters: frames {OzShuttleNightLights.Frames}, nose on {OzShuttleNightLights.NoseOnFrames} (real {OzShuttleNightLights.RealNoseFrames}, pool {OzShuttleNightLights.PoolNoseFrames}, shadowed {OzShuttleNightLights.ShadowNoseFrames}), ramp on {OzShuttleNightLights.RampOnFrames} (real {OzShuttleNightLights.RealRampFrames}, pool {OzShuttleNightLights.PoolRampFrames}), landing on {OzShuttleNightLights.LandingOnFrames}, alarm {OzShuttleNightLights.AlarmFrames} (calm {OzShuttleNightLights.CalmAlarmFrames}), pulse dips {OzShuttleNightLights.PulseDipFrames} (calm {OzShuttleNightLights.CalmPulseFrames}), beeps {OzShuttleNightLights.BeepsPlayed}");
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS), {frames} frames, {Time.unscaledTime - T0:F0} s");
            try
            {
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log_night.txt"), sb.ToString());
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "sound_log_night.txt"), snd.ToString());
            }
            catch (System.Exception ex) { Debug.LogError("[OzShuttleNightProof] could not write the logs: " + ex.Message); }
            if (take) take.End(new[] { OzShuttleNightLights.LockEvent, "ngt_searchlight_on", "ngt_light_fizzle", "shuttle_door_open", "shuttle_door_close", "shuttle_ramp_hydraulic", "latch",
                                       "shuttle_spool_up", "shuttle_takeoff", "shuttle_hover_loop", "shuttle_jet_burn_loop", "shuttle_landing", "shuttle_downwash_gust", "vo_shuttle_board", "vo_shuttle_unload" });   // K256e
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
            Destroy(gameObject);
        }
    }
}
