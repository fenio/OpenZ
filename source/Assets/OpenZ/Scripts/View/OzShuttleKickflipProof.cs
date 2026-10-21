#if UNITY_EDITOR
using System.Collections;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// K344 Play proof: the shuttle's kickflip easter egg (View/OzPlayableShuttle.Kickflip.cs), on Level 16 in Play (menu OpenZ/V1.1/Shuttle:
    /// K344 Kickflip Proof (Level 16, Play), Editor/OzShuttleKickflipMenu.cs). The game's pointer is moved and clicked by queued input events
    /// inside the game only (as the input self-test does: no desktop input), so the clicks go through PlayerInput exactly as a player's.
    /// Counted PASS / FAIL lines and full-screen pictures from the normal RTS camera (uncropped). Set-up: the CPU teams removed, morning
    /// (day clock 0.10, held), 60 fps. Stages:
    ///  K1 a far double right-click (45 .. 60 m) with the camera at 70 m following the ship: armed by the gesture, the flip in the steady
    ///     cruise, per frame no step in the drawn angle, its rate or acceleration; the frame (the click box) steady through it; the main
    ///     jets' flare and the attitude puffs drawn; the five sounds (pop, roll, two puffs, catch); pictures at the pop, 90, 180, 270 deg,
    ///     the catch;
    ///  K2 the same at 35 m (closer);
    ///  K3 a near double right-click (15 m): the order flies, nothing flips;
    ///  K4 a slow double right-click (0.6 s apart), far: nothing flips;
    ///  K5 at night (day clock 0.62): a far double right-click: the belly searchlights still point at the ground at 180 deg; lamp fix (review
    ///     K344): in every frame of the flip each drawn beam cone and each lit real light (the belly lamps, the nose searchlight) measured
    ///     against the ship's own model (View/OzShuttleKickflip.LampCheck.cs) in the drawn pose: none passes through the ship; the nose
    ///     searchlight lit again after the catch; pictures at the pop, 90, 180, 270 deg and the catch (night);
    ///  K6 a far double right-click and, in the pop, a new single order: eased back to level, never a step.
    /// <see cref="Show"/> = K1 and K2 only, slowly, without checks: for the 60 fps clip. Output: <see cref="Folder"/>. Test only.
    /// </summary>
    public sealed class OzShuttleKickflipProof : MonoBehaviour
    {
        public static string Report = "idle";
        static bool showOnly;
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_kickflip_proof");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_kickflip_proof");
        }
        public static void Run() { Report = "running"; showOnly = false; Make(); }
        public static void Show() { Report = "running (show)"; showOnly = true; Make(); }
        static void Make()
        {
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
            new GameObject("OzShuttleKickflipProof").AddComponent<OzShuttleKickflipProof>();
        }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; Camera cam; Team me = Team.Red;
        ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, shots; string dir;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); cam = Camera.main;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleKickflipProof] " + t); }
        void Check(string what, bool ok) { if (showOnly) return; if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
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
        /// <summary>One right click at a screen point through the game's own input (press 2 frames, release, 2 frames).</summary>
        IEnumerator RightClick(Vector2 pos) { Mouse0(pos, false); yield return Frames(2); Mouse0(pos, true); yield return Frames(2); Mouse0(pos, false); yield return Frames(2); }

        /// <summary>A free landing spot between minM and maxM from the ship that is in the middle of the screen now (px of the spot, screen point).</summary>
        bool SpotOnScreen(float minM, float maxM, out Vector2Int px, out Vector2 screen) => SpotOnScreen(s.CenterX, s.CenterY, minM, maxM, out px, out screen)
            || OzProofFarSpot.Frame(W, g, s, rts, cam, minM, maxM, out px, out screen, Log);                         // K419b: not on screen from the ship's 70 m view: searched round it and framed with it (View/OzProofFarSpot.cs)
        bool SpotOnScreen(int cx, int cy, float minM, float maxM, out Vector2Int px, out Vector2 screen)
        {
            px = default; screen = default;
            for (int ring = 0; ring < 6; ring++)
                for (int a = 0; a < 24; a++)
                {
                    float r = Mathf.Lerp(minM, maxM, ring / 5f) * 8f, t = a * Mathf.PI / 12f;
                    var q = W.ShuttleClamp(cx + (int)(Mathf.Cos(t) * r), cy + (int)(Mathf.Sin(t) * r));
                    if (!W.ShuttlePadFree(q.x, q.y, s, out _)) continue;
                    var wp = ZGame.PxToWorld(q.x, q.y); wp.y = g.GroundY(wp.x, wp.z);
                    var vp = cam.WorldToViewportPoint(wp);
                    if (vp.z <= 0f || vp.x < 0.18f || vp.x > 0.82f || vp.y < 0.3f || vp.y > 0.82f) continue;
                    var sp = cam.WorldToScreenPoint(wp);
                    if (!OzPlayableShuttle.LandableAt(W, s, wp, out _)) continue;
                    px = new Vector2Int(q.x, q.y); screen = new Vector2(sp.x, sp.y); return true;
                }
            return false;
        }

        void Select() { input.Selection.Clear(); input.Selection.Add(s); }
        void CamOn(float h) { if (!rts) return; rts.FreeLookOff(); rts.follow = null; var c = ship.transform.position; rts.focus = new Vector3(c.x, 0f, c.z); rts.SetHeightImmediate(h); }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || input == null || cam == null) { Log("no game (Level 16 in Play)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = s != null ? OzPlayableShuttle.Of(s) : null;
            if (s == null || !ship) { Log("no playable shuttle on this level (Level 16, the shuttle rule on)"); fails++; Finish(); yield break; }
            me = s.Owner;
            Log($"K344 kickflip proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s{(showOnly ? " (show: no checks)" : "")}");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            foreach (var id in new[] { "shuttle_flip_pop", "shuttle_flip_roll", "shuttle_flip_puff", "shuttle_flip_catch", "shuttle_flip_whoosh" }) Check($"the sound row {id} is in the table", AudioDirector.Has(id));
            {   // K423: the flip's rows are action sounds (they were at the lowest foley priority 176: dropped / silenced first when many sounds play)
                int P(string id) { var d = AudioDirector.I ? AudioDirector.I.Def(id) : null; return d != null ? d.priority : 999; }
                Check($"K423 the flip's sounds are action sounds: priority whoosh {P("shuttle_flip_whoosh")} (<= 40), pop {P("shuttle_flip_pop")}, catch {P("shuttle_flip_catch")}, roll {P("shuttle_flip_roll")} (<= 60), puff {P("shuttle_flip_puff")} (<= 96); the steal and the engine's voice limit take lower ones first (foley 176, ambience 220)",
                      P("shuttle_flip_whoosh") <= 40 && P("shuttle_flip_pop") <= 60 && P("shuttle_flip_catch") <= 60 && P("shuttle_flip_roll") <= 60 && P("shuttle_flip_puff") <= 96);
            }
            yield return Wait(0.5f);
            yield return FarFlip("k1", 70f, true);
            yield return FarFlip("k2", 35f, true);
            if (!showOnly)
            {
                yield return NotFlip("k3 near", 12f, 17f, 0.12f);
                yield return NotFlip("k4 slow", 45f, 60f, 0.6f);
                if (dn) { dn.timeOfDay = 0.62f; dn.Apply(0.62f); yield return Wait(1.5f); }
                yield return FarFlip("k5_night", 70f, true, true);                                         // lamp fix: the night pictures too (no beam through the hull)
                if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); yield return Wait(1f); }
                yield return PopInterrupt();
            }
            Finish();
        }

        /// <summary>A far double right-click and the flip watched frame by frame (the camera following the ship at height h).</summary>
        IEnumerator FarFlip(string tag, float h, bool pictures, bool night = false)
        {
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && s.ShuttleRampPhase == ZShuttleRamp.Shut, 30f);
            var lamps = night ? new LampWatch(ship) : null;                                               // lamp fix: the ship's model read and probed before the clicks (no hitch in the flip)
            CamOn(70f); yield return Wait(0.6f);                                                         // the click from the normal 70 m view (a 42 .. 60 m spot is on screen there)
            Vector2Int px; Vector2 sp;                                                                     // K420: at 35 m the flight across the screen (View/OzProofFlipAim.cs)
            bool spot = h < 60f ? OzProofFlipAim.Across(W, g, s, rts, cam, 42f, 60f, out px, out sp, Log) : SpotOnScreen(42f, 60f, out px, out sp);
            if (!spot) { Check($"{tag}: a free landing spot 42 .. 60 m away on screen", false); yield break; }
            Select();
            var fl = ship.Kickflip; int starts0 = fl.Starts, ends0 = fl.Ends, snd0 = ship.KickSounds, puff0 = ship.KickPuffs, flare0 = ship.KickFlareFrames, dbl0 = OzShuttleKickflip.ShipDoubles, gim0 = ship.KickGimbals;
            int tr0 = ship.KickTrailFrames, stk0 = ship.KickStreakFrames, wh0 = ship.KickWhooshes; ship.KickTrailProofReset();
            int whF0 = ship.KickWhooshFired, whFull0 = ship.KickWhooshFull, whCut0 = ship.KickWhooshCut, whDrop0 = ship.KickWhooshDropped, whVirt0 = ship.KickWhooshVirtualFrames;   // K423   // round 2 (K390)
            float arcRise = 0f, arcSide = 0f, lowGap = 999f, flipSec = 0f; int outOfView = 0; var hull = KickHull(ship);
            Log($"{tag}: double right-click at screen ({sp.x:F0}, {sp.y:F0}) = px ({px.x}, {px.y}), {Vector2.Distance(new Vector2(px.x, px.y), new Vector2(s.CenterX, s.CenterY)) / 8f:F1} m from the ship; camera {h:F0} m");
            yield return RightClick(sp); yield return RightClick(sp + new Vector2(2f, 1f));
            yield return null;
            Check($"{tag}: the double right-click was seen as one (the ship in both clicks' selection) and armed the flip", OzShuttleKickflip.ShipDoubles == dbl0 + 1 && (fl.Phase == OzKickflipPhase.Armed || fl.Phase == OzKickflipPhase.Flip));
            Check($"{tag}: the order itself went to the rules as before (its goal = the clicked spot)", (s.ShuttleGoalX, s.ShuttleGoalY) == W.ShuttleClamp(px.x, px.y));
            if (rts) { rts.follow = h < 60f ? OzProofFlipAim.For(gameObject, ship, rts) : ship.transform; rts.SetHeightImmediate(h); }   // K420: at 35 m aimed at the flip's middle in the air (the whole arc must be in view to start); else following the ship
            Log($"RECORD {tag}: the flight to the flip starts now ({Time.realtimeSinceStartup:F1} s since start), camera {h:F0} m");
            yield return WaitFor(() => fl.Starts > starts0, 25f);
            bool started = fl.Starts > starts0;
            Check($"{tag}: the flip started in the steady cruise (state {s.ShuttleState}, lift {s.ShuttleLift:F2}, speed {s.ShuttleSpeedPx / 8:F2} m/s, {(started ? "yes" : "NO")})", started && s.ShuttleState == ZShuttleState.Cruise && s.ShuttleLift >= 0.999);
            if (!started) { if (rts) rts.follow = null; yield break; }
            // per frame through the flip: the drawn angle, its rate and acceleration; the frame (click box) size
            float pr = 0f, pv = 0f, pa = 0f, maxDA = 0f, maxDV = 0f, maxDAcc = 0f, bA = 0f, bV = 0f, bAcc = 0f, fw0 = -1f, fh0 = -1f, frameDev = 0f; int frames = 0; bool[] took = new bool[5]; bool downAt180 = true; int down180 = 0;
            float jerk = (float)OzKickflipCurve.PeakJerk;
            lamps?.Begin();                                                                              // lamp fix: count from the flip's start
            while (fl.Phase == OzKickflipPhase.Flip || fl.Phase == OzKickflipPhase.Settle)
            {
                yield return null;                                                                     // after Update: the level pose (the frame's own pose)
                if (input.MeshRect(s, out var r)) { if (fw0 < 0f) { fw0 = r.width; fh0 = r.height; } else frameDev = Mathf.Max(frameDev, Mathf.Max(Mathf.Abs(r.width / fw0 - 1f), Mathf.Abs(r.height / fh0 - 1f))); }
                yield return new WaitForEndOfFrame();                                                  // the picture's pose
                var k = ship.KickNow; float dt = Mathf.Min(Time.deltaTime, 0.05f) * (g ? g.gameSpeed : 1f);
                float a = (float)k.RollDeg, v = (float)k.RollRate, ac = (float)k.RollAcc; if (!k.Active) { a = Mathf.Abs(pr) > 180f ? Mathf.Sign(pr) * 360f : 0f; v = 0f; ac = 0f; }
                if (dt > 0f && frames > 0)
                {
                    float vm = Mathf.Max(Mathf.Abs(pv), Mathf.Abs(v)), am = Mathf.Max(Mathf.Abs(pa), Mathf.Abs(ac)); jerk = Mathf.Max(jerk, (float)fl.SettleJerk);
                    maxDA = Mathf.Max(maxDA, Mathf.Abs(a - pr)); bA = Mathf.Max(bA, vm * dt + 0.5f * am * dt * dt + jerk * dt * dt * dt / 6f + 1e-4f);
                    maxDV = Mathf.Max(maxDV, Mathf.Abs(v - pv)); bV = Mathf.Max(bV, am * dt + 0.5f * jerk * dt * dt + 1e-3f);
                    maxDAcc = Mathf.Max(maxDAcc, Mathf.Abs(ac - pa)); bAcc = Mathf.Max(bAcc, jerk * dt * 1.001f + 1e-2f);
                }
                pr = a; pv = v; pa = ac; frames++;
                if (ship.KickLevelPose(out var lvp, out var lvr))
                {   // round 2: the drawn hull's middle against the level pose's (the arc), the drawn hull's lowest point over the ground, in view
                    var mid = ship.transform.TransformPoint(OzPlayableShuttle.KickPivot); var off = mid - (lvp + lvr * OzPlayableShuttle.KickPivot);
                    arcRise = Mathf.Max(arcRise, off.y); arcSide = Mathf.Max(arcSide, Mathf.Abs(Vector3.Dot(off, lvr * Vector3.right)));
                    foreach (var hr in hull) if (hr && hr.enabled) lowGap = Mathf.Min(lowGap, hr.bounds.min.y - g.GroundY(hr.bounds.center.x, hr.bounds.center.z));
                    var vp = cam.WorldToViewportPoint(mid); if (vp.z <= 0f || vp.x < 0.02f || vp.x > 0.98f || vp.y < 0.02f || vp.y > 0.98f) outOfView++;
                }
                flipSec += dt;
                float ar = Mathf.Abs(a);
                if (night && ar > 160f && ar < 200f && ship.Searchlights != null)
                {   // the belly lamps at 180 degrees: their heads still look down at the ground (gimballed)
                    for (int i = 0; i < 2; i++) { var hd = FindDeepT(ship.transform, "OZ_FxSearchHead" + i); if (hd) { down180++; if (hd.forward.y >= -0.2f) downAt180 = false; } }
                }
                lamps?.Frame();
                if (pictures)
                {
                    float T = (float)fl.T; int want = -1;
                    if (!took[0] && T >= 0.25f) want = 0; else if (!took[1] && ar >= 90f) want = 1; else if (!took[2] && ar >= 180f) want = 2; else if (!took[3] && ar >= 270f) want = 3; else if (!took[4] && T >= (float)OzKickflipCurve.TotalSec - 0.1f) want = 4;
                    if (want >= 0) { took[want] = true; Shot($"k344_{tag}_{(want == 0 ? "pop" : want == 4 ? "catch" : (want * 90) + "deg")}_{h:F0}m"); }
                }
            }
            if (rts) rts.follow = null;
            Check($"{tag}: {frames} frames, no step: largest change per frame angle {maxDA:F2} deg (its rate allows {bA:F2}), rate {maxDV:F1} deg/s (its acceleration allows {bV:F1}), acceleration {maxDAcc:F0} deg/s^2 (its jerk allows {bAcc:F0})", maxDA <= bA && maxDV <= bV && maxDAcc <= bAcc && frames > 20);
            Check($"{tag}: rolled to the end and level again ({fl.Ends - ends0} end)", fl.Ends == ends0 + 1 && !ship.KickDrawn);
            Check($"{tag}: the frame (the click box and the brackets) steady through the flip: its size changed at most {frameDev * 100:F1} % (the level pose's own bob)", frameDev < 0.06f);
            Check($"{tag}: the main jets flared ({ship.KickFlareFrames - flare0} frames) and the attitude jets puffed ({ship.KickPuffs - puff0} puffs) on screen", ship.KickFlareFrames > flare0 && ship.KickPuffs > puff0);
            Check($"{tag}: the five flip sounds fired on screen ({ship.KickSounds - snd0}: the pop, the roll, two puffs, the catch; last {ship.KickLastSound})", ship.KickSounds - snd0 >= 5);
            // round 2 (K390): the smooth trails, the arc, the room, the view, the long whoosh
            Check($"{tag}: round 2: the jets' trails drawn as smooth ribbons ({ship.KickTrailFrames - tr0} frames; the largest gap between two neighbouring points {ship.KickTrailMaxGap:F2} m <= 0.45; the trail reached {ship.KickTrailMaxReach:F1} m from the nozzle along its curl, 2.5 .. 12) and the attitude jets' streaks ({ship.KickStreakFrames - stk0} frames)",
                  ship.KickTrailFrames - tr0 > 30 && ship.KickTrailMaxGap <= 0.45f && ship.KickTrailMaxReach >= 2.5f && ship.KickTrailMaxReach <= 12f && ship.KickStreakFrames > stk0);
            float share = (float)fl.ArcK, wantRise = (float)(OzKickflipCurve.ArcRiseM * share), wantSide = (float)(OzKickflipCurve.ArcSideM * share);
            Check($"{tag}: round 2: the barrel roll's arc: the hull's middle rose {arcRise:F1} m (the arc {wantRise:F1} m + the hop) and swung {arcSide:F1} m to the side ({wantSide:F1} m; the room allowed {share * 100f:F0} % of it); the flip took {flipSec:F2} s of game time (the curve {OzKickflipCurve.TotalSec:F2} s + its tail)",
                  arcRise >= wantRise * 0.9f && arcSide >= wantSide * 0.9f && arcSide <= wantSide + 0.3f && flipSec >= (float)OzKickflipCurve.TotalSec * 0.97f);
            Check($"{tag}: round 2: the drawn hull kept {lowGap:F1} m over the ground at its lowest (>= 4) and stayed in view ({outOfView} frames out)", lowGap >= 4f && outOfView == 0);
            Check($"{tag}: round 2: the long whoosh fired once for the flip ({ship.KickWhooshes - wh0})", ship.KickWhooshes - wh0 == 1);
            // K423: the hull kept travelling forward on screen; the long whoosh was really heard (its voice played out, never cut, never virtual)
            Check($"{tag}: K423 the hull kept travelling forward on screen through the flip: its pace along the flight's screen line {ship.KickFwdMinShare * 100f:F0} .. {ship.KickFwdMaxShare * 100f:F0} % of the level path's over {ship.KickFwdFrames} frames ({ship.KickFwdStall} under 50 %, {ship.KickFwdBack} backwards); in the picture of each frame at most {ship.KickFwdOffsetPx:F1} px off the level ship along that line; the largest lead along the flight {ship.KickLeadMax:F1} m",
                  ship.KickFwdFrames > 20 && ship.KickFwdMinShare >= 0.9f && ship.KickFwdBack == 0 && ship.KickFwdOffsetPx <= 2f);
            yield return WaitFor(() => !ship.KickWhooshWatching, 2f);
            Check($"{tag}: K423 the long whoosh was heard to its end: started with a voice {ship.KickWhooshFired - whF0}, played out {ship.KickWhooshFull - whFull0}, cut short {ship.KickWhooshCut - whCut0}, no voice {ship.KickWhooshDropped - whDrop0}, frames silenced by the voice limit {ship.KickWhooshVirtualFrames - whVirt0}",
                  ship.KickWhooshFired - whF0 == 1 && ship.KickWhooshFull - whFull0 == 1 && ship.KickWhooshCut == whCut0 && ship.KickWhooshDropped == whDrop0 && ship.KickWhooshVirtualFrames == whVirt0);
            if (night) Check($"{tag}: at night the belly searchlights still point at the ground at 180 deg ({down180} samples, lamp heads kept on aim {ship.KickGimbals - gim0} times)", down180 > 0 && downAt180 && ship.KickGimbals > gim0);
            if (lamps != null)
            {   // lamp fix (review K344): no beam or real light through the ship; the nose searchlight lit again after the catch
                yield return new WaitForEndOfFrame();
                var nb = ship.KickLampBeam(OzKickflipLamps.Nose);
                bool noseBack = nb && nb.enabled && ship.KickLampFade(OzKickflipLamps.Nose) >= 0.999f;
                int through = lamps.BeamThrough[0] + lamps.BeamThrough[1] + lamps.BeamThrough[2] + lamps.LightThrough[0] + lamps.LightThrough[1] + lamps.LightThrough[2];
                Check($"{tag}: no lamp shines through the ship: every drawn beam cone and lit real light measured against the ship's model in the drawn pose of each of {lamps.Frames} frames{(lamps.Error != null ? " (" + lamps.Error + ")" : "")}: beams drawn belly L {lamps.BeamFrames[0]}, belly R {lamps.BeamFrames[1]}, nose {lamps.BeamFrames[2]} frames, through the ship {lamps.BeamThrough[0]} / {lamps.BeamThrough[1]} / {lamps.BeamThrough[2]}; real lights lit {lamps.LightFrames[0]} / {lamps.LightFrames[1]} / {lamps.LightFrames[2]}, through {lamps.LightThrough[0]} / {lamps.LightThrough[1]} / {lamps.LightThrough[2]}; dimmed by the flip in {ship.KickLampDimFrames - lamps.Dim0} frames (out {ship.KickLampOutFrames - lamps.Out0}, lowest fade {lamps.MinFade:F2}); the nose searchlight lit again at full after the catch: {(noseBack ? "yes" : "NO")}",
                      lamps.Error == null && through == 0 && lamps.BeamFrames[2] > 0 && noseBack);
            }
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed, 30f);
        }

        /// <summary>Lamp fix (review K344): the drawn beams and real lights of the belly lamps and the nose searchlight in the picture's pose,
        /// each frame, against the ship's own model (OzKickflipShipModel: its triangles in the root space of the ship as drawn).</summary>
        sealed class LampWatch
        {
            readonly OzPlayableShuttle ship; readonly OzKickflipShipModel model; readonly OzKickflipShipModel.Probe[] probe = new OzKickflipShipModel.Probe[3];
            readonly MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            public readonly int[] BeamFrames = new int[3], BeamThrough = new int[3], LightFrames = new int[3], LightThrough = new int[3];
            public int Frames, Dim0, Out0; public float MinFade = 1f; public string Error;

            public LampWatch(OzPlayableShuttle s)
            {
                ship = s; Begin();
                model = OzKickflipShipModel.Load(System.IO.Directory.GetParent(Application.dataPath).FullName); Error = model.Error;
                if (Error == null)
                    for (int h = 0; h < 3; h++) { var o = OzKickflipLamps.FieldOrigin[h]; probe[h] = model.ProbeFor(o[0], o[1], o[2], h == OzKickflipLamps.Nose ? 0.2 : 0.06, h != OzKickflipLamps.Nose); }
            }

            public void Begin() { if (ship) { Dim0 = ship.KickLampDimFrames; Out0 = ship.KickLampOutFrames; } }

            public void Frame()
            {
                if (Error != null || !ship) return;
                Frames++;
                var T = ship.transform;
                for (int h = 0; h < 3; h++)
                {
                    MinFade = Mathf.Min(MinFade, ship.KickLampFade(h));
                    var b = ship.KickLampBeam(h);
                    if (b && b.enabled)
                    {
                        if (OzPlayableShuttle.KickLampPerMaterial(h)) b.GetPropertyBlock(mpb, 0); else b.GetPropertyBlock(mpb);
                        var c = mpb.GetColor(NightMeshes.ColorId);
                        if (c.r + c.g + c.b > 1e-4f)
                        {   // the cone mesh: apex at its origin, axis +z, length 1, end radius 1 (NightMeshes.Cone), as its transform draws it
                            BeamFrames[h]++;
                            Vector3 o = T.InverseTransformPoint(b.transform.position), e = T.InverseTransformPoint(b.transform.TransformPoint(0f, 0f, 1f)), rim = T.InverseTransformPoint(b.transform.TransformPoint(1f, 0f, 1f));
                            Vector3 ax = e - o; float len = ax.magnitude, half = Vector3.Angle(ax, rim - o);
                            if (len > 0.01f && model.Cone(probe[h], o.x, o.y, o.z, ax.x, ax.y, ax.z, half, 0.004, len)) BeamThrough[h]++;
                        }
                    }
                    var l = ship.KickLampLight(h);
                    if (l && l.enabled && l.intensity > 0.01f)
                    {
                        LightFrames[h]++;
                        Vector3 o = T.InverseTransformPoint(l.transform.position), a = T.InverseTransformDirection(l.transform.forward);
                        float range = l.range / Mathf.Max(1e-4f, T.lossyScale.x);
                        if (model.Cone(probe[h], o.x, o.y, o.z, a.x, a.y, a.z, l.spotAngle * 0.5f, 0.004, range)) LightThrough[h]++;
                    }
                }
            }
        }

        /// <summary>Round 2: the ship's own drawn parts (the model's SH_ pieces: hull, pods, legs, nozzles), for the lowest point over the ground.</summary>
        static System.Collections.Generic.List<Renderer> KickHull(OzPlayableShuttle s)
        {
            var l = new System.Collections.Generic.List<Renderer>();
            foreach (var r in s.GetComponentsInChildren<MeshRenderer>(true)) if (r.name.StartsWith("SH_")) l.Add(r);
            return l;
        }

        static Transform FindDeepT(Transform t, string n) { if (t.name == n) return t; foreach (Transform c in t) { var r = FindDeepT(c, n); if (r) return r; } return null; }

        /// <summary>A double right-click that must not flip: near (minM .. maxM) or too slow (gap s between the clicks).</summary>
        IEnumerator NotFlip(string tag, float minM, float maxM, float gap)
        {
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && s.ShuttleRampPhase == ZShuttleRamp.Shut, 30f);
            CamOn(70f); yield return Wait(0.6f);
            if (!SpotOnScreen(minM, maxM, out var px, out var sp)) { Check($"{tag}: a free landing spot {minM:F0} .. {maxM:F0} m away on screen", false); yield break; }
            Select();
            var fl = ship.Kickflip; int starts0 = fl.Starts, arms0 = fl.Arms, flights0 = s.ShuttleFlights;
            yield return RightClick(sp); if (gap > 0.2f) yield return Wait(gap); yield return RightClick(sp + new Vector2(2f, 1f));
            if (rts) { rts.follow = ship.transform; rts.SetHeightImmediate(70f); }                                    // K419b: back to 70 m after a far spot's framing
            yield return WaitFor(() => s.ShuttleFlights > flights0, 12f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLift <= 0, 30f);
            yield return Wait(0.5f);
            if (rts) rts.follow = null;
            Check($"{tag}: the order flew (goal = the spot) and nothing flipped (armed {fl.Arms - arms0}, started {fl.Starts - starts0})", (s.ShuttleGoalX, s.ShuttleGoalY) == W.ShuttleClamp(px.x, px.y) && fl.Starts == starts0 && fl.Arms == arms0);
        }

        /// <summary>K6: a far double right-click and, in the pop, a new single order: eased back to level.</summary>
        IEnumerator PopInterrupt()
        {
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && s.ShuttleLevelled && s.ShuttleRampPhase == ZShuttleRamp.Shut, 30f);
            CamOn(70f); yield return Wait(0.6f);
            if (!SpotOnScreen(45f, 60f, out var px, out var sp)) { Check("k6: a free landing spot on screen", false); yield break; }
            Select();
            var fl = ship.Kickflip; int ab0 = fl.Aborts, st0 = fl.Starts;
            yield return RightClick(sp); yield return RightClick(sp + new Vector2(2f, 1f));
            if (rts) { rts.follow = ship.transform; rts.SetHeightImmediate(70f); }                                    // K419b: back to 70 m after a far spot's framing
            yield return WaitFor(() => fl.Starts > st0, 25f);
            if (fl.Starts == st0) { Check("k6: the flip started", false); if (rts) rts.follow = null; yield break; }
            if (!SpotOnScreen(s.CenterX, s.CenterY, 22f, 40f, out var px2, out var sp2)) { Check("k6: a free landing spot for the new order on screen", false); if (rts) rts.follow = null; yield break; }
            Mouse0(sp2, true); yield return null; Mouse0(sp2, false); yield return null;                   // a new order (a quick single click) to a landable spot, still in the pop
            Log($"k6: the new order at px ({px2.x}, {px2.y}), {fl.T:F2} s into the flip");
            float maxStep = 0f, prev = (float)ship.KickNow.PitchUpDeg; int n = 0;
            while (fl.Phase == OzKickflipPhase.Flip || fl.Phase == OzKickflipPhase.Settle) { yield return new WaitForEndOfFrame(); float p = ship.KickNow.Active ? (float)ship.KickNow.PitchUpDeg : 0f; maxStep = Mathf.Max(maxStep, Mathf.Abs(p - prev)); prev = p; n++; if (n > 400) break; }
            if (rts) rts.follow = null;
            Check($"k6: a new order in the pop: eased back to level ({fl.Aborts - ab0} eased back; the nose's largest change per frame {maxStep:F2} deg over {n} frames)", fl.Aborts == ab0 + 1 && maxStep < 1.2f);
        }

        void Finish()
        {
            Report = showOnly ? $"show done, {shots} pictures" : $"{passes} PASS, {fails} FAIL, {shots} pictures";
            Log("K344 kickflip proof: " + Report + " -> " + dir);
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, showOnly ? "k344_show_log.txt" : "k344_proof_log.txt"), sb.ToString()); } catch (System.Exception e) { Debug.LogWarning("[OzShuttleKickflipProof] log not written: " + e.Message); }
            Destroy(gameObject);
        }
    }
}
#endif
