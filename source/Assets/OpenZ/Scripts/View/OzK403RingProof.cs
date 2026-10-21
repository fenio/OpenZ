using System.Collections;
using System.Collections.Generic;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K403 Play proof (view only), on level 16 in Play (menu OpenZ/V1.1/Shuttle: K403 Ring Steady Proof, Editor/OzK403RingMenu.cs): the
    /// selected shuttle's green ring stays steady while a squad gets out and boards again. Counted PASS / FAIL lines, read at the end of every
    /// frame (after the ring was drawn), and two 6-frame strips at the RTS camera's 35 m (the ship's place cropped from full-screen frames).
    /// <list type="bullet">
    /// <item>A (Play as normal): a grunt squad boards (ENTER), gets out (EXIT), boards again. Through all of it: the ring drawn every frame
    /// the ship is selected, 0 on / off toggles, 0 changes of the rules' landed state or of the mark (the soft shadow), its alpha 1 (steps
    /// under 0.01 a frame), its size changes only by the units' own slow pulse (at most StepMaxSec of it a frame; at most 2 turns of the
    /// size a second), its centre still (under 1 cm a frame).</item>
    /// <item>B (as the clips are recorded): EXIT under a fixed-step recording (Time.captureFramerate 60) with slow real frames (60 .. 280
    /// ms each, as the clip's exit had): the ring's size moves by at most one 1/60 s step of the pulse a frame and turns at most 2 times a
    /// second. The same frames are also read with the old real-time pulse (logged as the before: it jumped by its whole swing).</item>
    /// </list>
    /// <see cref="Folder"/>
    /// </summary>
    public sealed class OzK403RingProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_ring_k403");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_ring_k403");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzK403RingProof").AddComponent<OzK403RingProof>();
        }

        /// <summary>The camera height of the strips (m) and the crop round the ship (px).</summary>
        public static float CamM = 35f; public static int CropW = 460, CropH = 380;

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; Team me = Team.Red; ZObject s; OzPlayableShuttle ship;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir;
        int loadMs; bool loading;

        // ---- the per-frame watch of one stretch
        sealed class Watch
        {
            public string name; public int frames, selFrames, drawn, gaps, ringToggles, stateChanges, airToggles, markToggles;
            public float aMin = 9f, aMax = -1f, aStep, rStep, rStepOld, cStep; public int turns, turnsOld; public float sec;
            public bool haveLast; public bool lastDrawn, lastAir, lastMark; public ZShuttleState lastState; public float lastA, lastR, lastROld, lastDr, lastDrOld;
            public Vector3 lastC; public readonly StringBuilder csv = new StringBuilder("frame,unscaled_s,game_s,selected,drawn,alpha,outer_m,outer_old_m,cx,cz,state,airborne,mark_shown,aboard\n");
        }
        Watch w;
        readonly List<Texture2D> stripTex = new List<Texture2D>(); int stripLeft; string stripName;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
            StartCoroutine(EndOfFrames());
        }

        void Update()
        {
            if (loading && loadMs > 0) System.Threading.Thread.Sleep(60 + (Time.frameCount * 37 % 5) * 55);   // 60 .. 280 ms: the clip's slow exit frames
        }

        void OnDestroy() { if (loading) { loading = false; Time.captureFramerate = 0; } }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzK403RingProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Sel() { if (input) { input.Selection.Clear(); input.Selection.Add(s); } }
        void Cam() { if (!rts || !ship) return; rts.FreeLookOff(); var p = ship.transform.position; rts.focus = new Vector3(p.x, 0f, p.z); rts.SetHeightImmediate(CamM); }
        ZObject Squad(string type, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, me); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        bool Enter(ZObject sq) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = s.Id, x = s.CenterX, y = s.CenterY, playerGiven = true } });
        bool Standing => s != null && !s.Destroyed && s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne;

        // ---- read at the end of every frame: the ring as SelectionOverlay drew it this frame
        IEnumerator EndOfFrames()
        {
            var eof = new WaitForEndOfFrame();
            int draws0 = OzShuttleShadowRing.Draws;
            while (true)
            {
                yield return eof;
                int dn = OzShuttleShadowRing.Draws; bool drawnNow = dn != draws0 && OzShuttleShadowRing.LastId == (s != null ? s.Id : -1); draws0 = dn;
                if (w != null && s != null) Sample(w, drawnNow);
                if (stripLeft > 0 && ship) StripFrame();
            }
        }

        void Sample(Watch x, bool drawnNow)
        {
            bool sel = input && input.Selection.Contains(s);
            float a = drawnNow ? OzShuttleShadowRing.AlphaOf(s.Id) : 0f, r = drawnNow ? OzShuttleShadowRing.LastOuterM : 0f;
            float rOld = OzShuttleShadow.RingM * (1f + OzRingClock.PulseAmp * Mathf.Sin(Time.unscaledTime * OzRingClock.PulseRate));   // the old real-time pulse on the same frame (the before)
            var c = OzShuttleShadowRing.LastCentre; bool air = s.ShuttleAirborne, mark = ship && ship.Shadow != null && ship.Shadow.Shown;
            x.frames++; x.sec += Time.deltaTime;
            if (sel) x.selFrames++;
            if (sel && drawnNow) x.drawn++;
            if (sel && !drawnNow) x.gaps++;
            if (x.haveLast)
            {
                if (drawnNow != x.lastDrawn && sel) x.ringToggles++;
                if (s.ShuttleState != x.lastState) x.stateChanges++;
                if (air != x.lastAir) x.airToggles++;
                if (mark != x.lastMark) x.markToggles++;
                if (drawnNow && x.lastDrawn)
                {
                    x.aStep = Mathf.Max(x.aStep, Mathf.Abs(a - x.lastA));
                    float dr = r - x.lastR; x.rStep = Mathf.Max(x.rStep, Mathf.Abs(dr));
                    if (Mathf.Abs(dr) > 1e-5f) { if (x.lastDr != 0f && Mathf.Sign(dr) != Mathf.Sign(x.lastDr)) x.turns++; x.lastDr = dr; }
                    x.cStep = Mathf.Max(x.cStep, new Vector2(c.x - x.lastC.x, c.z - x.lastC.z).magnitude);
                }
                float dro = rOld - x.lastROld; x.rStepOld = Mathf.Max(x.rStepOld, Mathf.Abs(dro));
                if (Mathf.Abs(dro) > 1e-5f) { if (x.lastDrOld != 0f && Mathf.Sign(dro) != Mathf.Sign(x.lastDrOld)) x.turnsOld++; x.lastDrOld = dro; }
            }
            if (drawnNow) { x.aMin = Mathf.Min(x.aMin, a); x.aMax = Mathf.Max(x.aMax, a); }
            x.haveLast = true; x.lastDrawn = drawnNow; x.lastState = s.ShuttleState; x.lastAir = air; x.lastMark = mark; x.lastA = a; x.lastR = r; x.lastROld = rOld; x.lastC = c;
            x.csv.Append(Time.frameCount).Append(',').Append(Time.unscaledTime.ToString("F4")).Append(',').Append(W.Time.ToString("F4")).Append(',').Append(sel ? 1 : 0).Append(',')
                 .Append(drawnNow ? 1 : 0).Append(',').Append(a.ToString("F4")).Append(',').Append(r.ToString("F4")).Append(',').Append(rOld.ToString("F4")).Append(',')
                 .Append(c.x.ToString("F3")).Append(',').Append(c.z.ToString("F3")).Append(',').Append(s.ShuttleState).Append(',').Append(air ? 1 : 0).Append(',').Append(mark ? 1 : 0).Append(',')
                 .Append(s.Passengers.Count).Append('\n');
        }

        void Judge(Watch x, bool capture)
        {
            float perSec = x.sec > 0.5f ? x.turns / x.sec : 0f, perSecOld = x.sec > 0.5f ? x.turnsOld / x.sec : 0f;
            float stepCap = OzShuttleShadow.RingM * OzRingClock.PulseAmp * OzRingClock.PulseRate * (capture ? 1f / 60f : OzRingClock.StepMaxSec) * 1.05f + 0.0005f;
            Log($"{x.name}: {x.frames} frames ({x.sec:F1} s of game time), selected {x.selFrames}, ring drawn {x.drawn}, gaps {x.gaps}; toggles: ring {x.ringToggles}, landed state {x.stateChanges}, airborne {x.airToggles}, mark {x.markToggles}; " +
                $"alpha {x.aMin:F3} .. {x.aMax:F3} (step {x.aStep:F4}); size step {x.rStep * 100f:F2} cm a frame (cap {stepCap * 100f:F2}), turns {x.turns} ({perSec:F2}/s); centre step {x.cStep * 100f:F2} cm; " +
                $"the old real-time pulse on the same frames: step {x.rStepOld * 100f:F2} cm, turns {x.turnsOld} ({perSecOld:F2}/s)");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "k403_" + x.name + "_frames.csv"), x.csv.ToString()); } catch { }
            Check($"{x.name}: the ring drawn every selected frame (gaps {x.gaps}), 0 on / off toggles ({x.ringToggles})", x.drawn > 0 && x.gaps == 0 && x.ringToggles == 0);
            Check($"{x.name}: 0 changes of the landed state ({x.stateChanges}), airborne ({x.airToggles}), mark ({x.markToggles})", x.stateChanges == 0 && x.airToggles == 0 && x.markToggles == 0);
            Check($"{x.name}: alpha steady ({x.aMin:F3} .. {x.aMax:F3}, step {x.aStep:F4})", x.aMin >= 0.99f && x.aStep <= 0.01f);
            Check($"{x.name}: size only the slow pulse (step {x.rStep * 100f:F2} cm <= {stepCap * 100f:F2} cm, {perSec:F2} turns/s <= 2)", x.rStep <= stepCap && perSec <= 2f);
            Check($"{x.name}: centre still (step {x.cStep * 100f:F2} cm <= 1 cm)", x.cStep <= 0.01f);
        }

        // ---- the 6-frame strip: the ship's place cropped from 6 consecutive full-screen frames
        void StripFrame()
        {
            var cam = rts ? rts.GetComponent<Camera>() : Camera.main; if (!cam) { stripLeft = 0; return; }
            var full = ScreenCapture.CaptureScreenshotAsTexture(); if (!full) { stripLeft = 0; return; }
            var sp = cam.WorldToScreenPoint(ship.transform.position);
            int cw = Mathf.Min(CropW, full.width), ch = Mathf.Min(CropH, full.height);
            int x0 = Mathf.Clamp(Mathf.RoundToInt(sp.x) - cw / 2, 0, full.width - cw), y0 = Mathf.Clamp(Mathf.RoundToInt(sp.y) - ch / 2, 0, full.height - ch);
            var crop = new Texture2D(cw, ch, TextureFormat.RGB24, false); crop.SetPixels(full.GetPixels(x0, y0, cw, ch)); crop.Apply();
            Destroy(full); stripTex.Add(crop); stripLeft--;
            if (stripLeft > 0) return;
            var strip = new Texture2D(cw * stripTex.Count, ch, TextureFormat.RGB24, false);
            for (int i = 0; i < stripTex.Count; i++) strip.SetPixels(i * cw, 0, cw, ch, stripTex[i].GetPixels());
            strip.Apply();
            try { System.IO.File.WriteAllBytes(System.IO.Path.Combine(dir, stripName + ".png"), strip.EncodeToPNG()); Log($"strip {stripName}.png: 6 consecutive frames at {CamM:F0} m, {cw}x{ch} px each round the ship"); } catch (System.Exception e) { Log("strip failed: " + e.Message); }
            foreach (var t in stripTex) Destroy(t); stripTex.Clear(); Destroy(strip);
        }
        IEnumerator Strip(string name) { stripName = name; stripTex.Clear(); stripLeft = 6; while (stripLeft > 0) yield return null; }

        IEnumerator Board(string tag)
        {
            double hd = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            int fx = (int)(s.CenterX + System.Math.Cos(hd) * 80) >> 4, fy = (int)(s.CenterY + System.Math.Sin(hd) * 80) >> 4;
            var sq = Squad("grunt", fx, fy); yield return Wait(0.8f);
            int shuts0 = s.ShuttleRampShuts;
            Check($"{tag}: the ENTER order is taken", Enter(sq));
            yield return WaitFor(() => s.Passengers.Count > 0, 40f);
            yield return WaitFor(() => s.ShuttleRampShuts > shuts0, 20f);
            yield return Wait(0.6f);
            Log($"{tag}: {s.Passengers.Count} aboard ({s.PassengerType})");
        }
        IEnumerator BoardAgain(string tag, List<ZObject> outs)
        {
            int shuts0 = s.ShuttleRampShuts; ZObject lead = null;
            foreach (var o in outs) if (o != null && !o.Destroyed && o.KillAt < 0 && o.Leader == null) { lead = o; break; }
            if (lead == null && outs.Count > 0) lead = outs[0];
            Check($"{tag}: the ENTER order is taken", Enter(lead));
            yield return WaitFor(() => s.Passengers.Count > 0, 40f);
            yield return WaitFor(() => s.ShuttleRampShuts > shuts0, 20f);
            yield return Wait(0.6f);
            Log($"{tag}: {s.Passengers.Count} aboard again");
        }
        IEnumerator Exit(string tag, string strip, List<ZObject> outs)
        {
            int shuts0 = s.ShuttleRampShuts, n = s.Passengers.Count;
            var before = new HashSet<int>(); foreach (var o in W.Objects) if (o.Owner == me && o.Kind == ZKind.Robot) before.Add(o.Id);
            yield return WaitFor(() => s.CanEjectCrew(), 5f);
            Check($"{tag}: EXIT is taken ({n} aboard)", W.Eject(me, s));
            yield return WaitFor(() => s.Passengers.Count < n, 12f);
            yield return Strip(strip);                                                                    // the first figure is on its way out
            yield return WaitFor(() => s.Passengers.Count == 0, 12f);
            yield return WaitFor(() => s.ShuttleRampShuts > shuts0, 15f);
            yield return Wait(0.6f);
            outs.Clear(); foreach (var o in W.Objects) if (o.Owner == me && o.Kind == ZKind.Robot && !o.Destroyed && o.KillAt < 0 && !before.Contains(o.Id)) outs.Add(o);
            Log($"{tag}: out {outs.Count}, aboard {s.Passengers.Count}");
        }

        IEnumerator All()
        {
            yield return null;
            if (W == null || W.PlayableShuttle == null || !(ship = OzPlayableShuttle.Of(W.PlayableShuttle))) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; me = s.Owner;
            int bots = W.Bots.Count; W.Bots.Clear();
            Log($"K403 ring proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}; the shuttle #{s.Id} {s.ShuttleState}; CPU removed {bots}; camera {CamM:F0} m");
            yield return WaitFor(() => Standing, 60f);
            if (!Standing) { Log("the shuttle does not stand on the ground: land it first (it is selected now)"); Sel(); fails++; Finish(); yield break; }
            Sel(); Cam(); yield return Wait(0.5f);
            var outs = new List<ZObject>();

            // A: Play as normal: board, get out, board again
            w = new Watch { name = "A_play" };
            if (s.Passengers.Count == 0) yield return Board("A board");
            yield return Exit("A exit", "k403_strip_A_exit_35m", outs);
            yield return BoardAgain("A board again", outs);
            var a = w; w = null; Judge(a, false);

            // B: as the clips are recorded (fixed step 60, slow real frames)
            Cam(); yield return Wait(0.3f);
            Time.captureFramerate = 60; loading = true; loadMs = 1;
            w = new Watch { name = "B_fixed_step_recording" };
            yield return Exit("B exit (fixed step, slow frames)", "k403_strip_B_exit_fixedstep_35m", outs);
            var b = w; w = null;
            loading = false; loadMs = 0; Time.captureFramerate = 0;
            Judge(b, true);
            Log($"the rings' clock: {OzRingClock.Frames} frames, {OzRingClock.CaptureFrames} on the capture step, largest step {OzRingClock.StepMax:F3} s");
            yield return BoardAgain("B board again (Play as normal)", outs);
            Finish();
        }

        void Finish()
        {
            if (loading) { loading = false; Time.captureFramerate = 0; }
            Report = $"{passes} PASS, {fails} FAIL";
            Log("K403 ring proof: " + Report);
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "k403_ring_proof.txt"), sb.ToString()); } catch { }
            Destroy(gameObject);
        }
    }
}
