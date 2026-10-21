using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OpenZ.Sim;
using UnityEngine;

namespace OpenZ
{
    /// <summary>
    /// K389 Play proof: the shuttle's capture effect (View/OzShuttleCaptureFx.cs) on level 16 in Play (menu OpenZ/V1.1/Shuttle: K389
    /// Capture Flash Proof, Editor/OzShuttleCaptureFxMenu.cs). Counted PASS / FAIL lines and full-screen pictures from the normal RTS
    /// camera (uncropped). Set-up: the CPU teams removed, morning (day clock 0.10, held).
    ///  C1 (35 m) and C2 (70 m): the testing panel's drop of an empty ship beside the player's fort, a grunt squad of the player's team
    ///     ordered in. The moment the ship turns lit: the capture flash shown once (the building flash's light over the roof, the halo), the
    ///     lamps start low and stutter for 0.8 s then burn steady, the team panels ease from grey to the team colour (about 0.6 s, never
    ///     back), the vehicles' capture cue in the sound log; a strip of 6 pictures over the first second.
    ///  C3 (70 m, level 16's ALAN &amp; BRAD): the heroes into a third empty ship, EXIT on the ground: as they leave, no flash, the lights
    ///     fade out over about 0.4 s (lit, falling) and then go dark (mode 2); the engine shut-down in the sound log (K256f2).
    /// Output: <see cref="Folder"/> (the work tree's records, milestones/shuttle_capture_fx_proof, found upward at run time). Test only.
    /// </summary>
    public sealed class OzShuttleCaptureFxProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder()
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", "shuttle_capture_fx_proof");
            return System.IO.Path.Combine(Application.persistentDataPath, "shuttle_capture_fx_proof");
        }
        public static void Run()
        {
            Report = "running";
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("OzShuttleCaptureFxProof").AddComponent<OzShuttleCaptureFxProof>();
        }

        /// <summary>The strip's moments after the ship turns lit (seconds).</summary>
        static readonly float[] Strip = { 0f, 0.1f, 0.25f, 0.45f, 0.65f, 1.0f };

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; OpenZHud hud; Team me = Team.Red;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes, frames; string dir;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            hud = OpenZHud.I;
            dir = Folder(); System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleCaptureFxProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        void Cam(Vector3 focus, float h) { if (!rts) return; rts.FreeLookOff(); rts.focus = new Vector3(focus.x, 0f, focus.z); rts.SetHeightImmediate(h); }
        ZObject Squad(string type, int tx, int ty)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, me, -1); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        bool Enter(ZObject sq, ZObject ship0) => sq != null && W.SendWaypoints(me, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = ship0.Id, x = ship0.CenterX, y = ship0.CenterY, playerGiven = true } });
        void Select(params ZObject[] os) { input.Selection.Clear(); foreach (var o in os) if (o != null) input.Selection.Add(o); }

        int[] Spot(int cx, int cy, int minR, int maxR)
        {
            for (int r = minR; r <= maxR; r += 16)
                for (int a = 0; a < 32; a++)
                {
                    double t = a * System.Math.PI / 16;
                    var q = W.ShuttleClamp(cx + (int)(System.Math.Cos(t) * r), cy + (int)(System.Math.Sin(t) * r));
                    if (W.ShuttlePadFree(q.x, q.y, null, out _)) return new[] { q.x, q.y };
                }
            return null;
        }

        static int Played(float from, float to, string key)
        {
            int n = 0;
            foreach (var e in OzSoundLog.Since(from)) if (e.t <= to && e.key == key && e.outcome != null && e.outcome.StartsWith("PLAY")) n++;
            return n;
        }

        IEnumerator All()
        {
            Application.targetFrameRate = 60;
            if (W == null || hud == null || input == null) { Log("no game (level 16 in Play)"); fails++; Finish(); yield break; }
            if (!ZWorld.ShuttlePlayableDeviation) { Log("the shuttle rule is off ('Zod exact')"); fails++; Finish(); yield break; }
            if (!OzShuttleCaptureFx.On) { Log("OzShuttleCaptureFx.On is false"); fails++; Finish(); yield break; }
            me = g.player;
            Log($"K389 shuttle capture flash proof, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, world time {W.Time:F1} s");
            int bots = W.Bots.Count; W.Bots.Clear();
            dn = FindFirstObjectByType<DayNight>(); if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            Log($"set-up: CPU removed {bots}, day 0.10 held");
            yield return Wait(0.5f);
            var fort = W.Objects.FirstOrDefault(o => o.IsFort && o.Owner == me && !o.Destroyed) ?? W.Objects.FirstOrDefault(o => o.Owner == me && !o.Destroyed);
            if (fort == null) { Log("no fort or unit of the player's team"); fails++; Finish(); yield break; }
            int cx = fort.CenterX, cy = fort.CenterY;
            yield return Capture("c1", 35f, cx, cy, 360);
            yield return Capture("c2", 70f, cx, cy, 760);
            yield return Emptied(cx, cy);
            Finish();
        }

        // ------------------------------------------------------------------ C1 / C2: a squad takes an empty ship
        IEnumerator Capture(string tag, float h, int cx, int cy, int minR)
        {
            string T = tag.ToUpperInvariant();
            var q = Spot(cx, cy, minR, minR + 900);
            if (q == null) { Check(T + " a free place for the drop", false); yield break; }
            var s = OzCheatShuttle.Spawn(g, hud, Team.Neutral, new Vector2Int(q[0], q[1]));
            var ship = s != null ? OzPlayableShuttle.Of(s) : null;
            if (s == null || !ship) { Check(T + " the panel drops an empty shuttle with its view", false); yield break; }
            var look = ship.GetComponent<OzShuttleEmptyLook>(); var paint = ship.GetComponent<TeamPaint>();
            yield return Wait(0.3f);
            var fx = ship.GetComponent<OzShuttleCaptureFx>();
            Check(T + " the dropped empty ship is dark (lights mode 2) and grey", look && look.IsDark && look.Lights && look.Lights.Mode == 2 && paint && paint.Team == Team.Neutral && fx);
            double hd = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            var sq = Squad("grunt", (int)(s.CenterX - System.Math.Cos(hd) * 96) >> 4, (int)(s.CenterY - System.Math.Sin(hd) * 96) >> 4);
            yield return Wait(0.5f);
            Cam(ship.transform.position, h);
            Check(T + " the ENTER order of a grunt squad on the empty ship is taken", Enter(sq, s));
            yield return WaitFor(() => s.Drivers.Count > 0, 40f);
            int flashes0 = OzShuttleCaptureFx.Flashes, halos0 = PlacementPop.Halos;
            float waited = 0f;
            while (look && look.IsDark && waited < 15f) { waited += Time.deltaTime; yield return null; }
            if (!look || look.IsDark) { Check(T + " the ship turns lit once the squad sits", false); yield break; }
            float t0 = Time.unscaledTime, s0 = Time.time;
            // per frame over 1.4 s: the lights' level, the paint's ease, the strip's pictures
            var pw = new List<float>(); var ek = new List<float>(); var tt = new List<float>();
            int next = 0;
            while (Time.time - s0 < 1.4f)
            {
                float u = Time.time - s0;
                pw.Add(fx.Shown); ek.Add(fx.Ease); tt.Add(u);
                if (next < Strip.Length && u >= Strip[next]) { Shot($"k389_{tag}_t{Mathf.RoundToInt(Strip[next] * 1000f):0000}_h{h:0}"); next++; }
                yield return null;
            }
            float flashT = OzShuttleCaptureFx.LastFlashAt;
            bool once = OzShuttleCaptureFx.Flashes == flashes0 + 1 && PlacementPop.Halos == halos0 + 1;
            float roof = ship.transform.position.y + 5.0f;
            Log($"{T} lit at world {W.Time:F2} s: flashes {OzShuttleCaptureFx.Flashes - flashes0}, halos {PlacementPop.Halos - halos0}, team {OzShuttleCaptureFx.LastTeam}, halo {OzShuttleCaptureFx.LastHaloSize:F1} m, " +
                $"light at {OzShuttleCaptureFx.LastLightAt.y:F2} m (ship root {ship.transform.position.y:F2} m), flash {flashT - t0:+0.00;-0.00} s from the first lit frame seen");
            Check(T + " the capture flash is shown once at the switch: the building flash's light over the roof and the halo, 7 to 13 m wide", once && Mathf.Abs(flashT - t0) < 0.1f
                  && OzShuttleCaptureFx.LastHaloSize > 7f && OzShuttleCaptureFx.LastHaloSize < 13f && OzShuttleCaptureFx.LastLightAt.y > roof && OzShuttleCaptureFx.LastTeam == me);
            int lows = 0; float maxEarly = 0f, minLate = 1f;
            for (int i = 0; i < pw.Count; i++)
            {
                if (tt[i] < 0.8f && pw[i] < 0.2f) lows++;
                if (tt[i] < 0.05f) maxEarly = Mathf.Max(maxEarly, pw[i]);
                if (tt[i] > 1.0f) minLate = Mathf.Min(minLate, pw[i]);
            }
            Log($"{T} lights: first frames {maxEarly:F2}, low (stutter) frames in the first 0.8 s {lows}, the lowest after 1.0 s {minLate:F2}; levels {string.Join(" ", pw.Take(48).Select(x => x.ToString("F2")))}");
            Check(T + " the lamps start as a captured vehicle's: low at first, stuttering in the first 0.8 s, steady at full after 1 s", maxEarly < 0.5f && lows >= 2 && minLate > 0.99f);
            bool mono = true; float at1 = -1f, first = ek.Count > 0 ? ek[0] : 1f;
            for (int i = 1; i < ek.Count; i++) { if (ek[i] + 1e-4f < ek[i - 1]) mono = false; if (at1 < 0f && ek[i] >= 0.999f) at1 = tt[i]; }
            Log($"{T} paint ease: first {first:F2}, done at {at1:F2} s, never back {mono}; values {string.Join(" ", ek.Take(48).Select(x => x.ToString("F2")))}; the paint's team {(paint ? paint.Team.ToString() : "-")}");
            Check(T + " the team panels ease from the grey to the team colour: under 0.25 at first, never back, done in 0.5 to 0.8 s", first < 0.25f && mono && at1 >= 0.5f && at1 <= 0.8f && paint && paint.Team == me);
            int cap = Played(flashT - 0.3f, flashT + 0.6f, "capture"), eng = Played(flashT - 0.3f, flashT + 0.8f, "engine_start");
            Log($"{T} sound log round the flash: capture {cap}, engine_start {eng}\n" + OzSoundLog.Format(OzSoundLog.Since(flashT - 0.5f).Where(e => e.t <= flashT + 1f).ToList()));
            Check(T + " the vehicles' capture cue is heard with the flash (the sound log's capture row, PLAY)", cap > 0);
            Check(T + " the ship is lit in its team's neon (lights mode 0)", look.Lights && look.Lights.Mode == 0 && look.Lights.NeonTeam == TeamColors.Shown(me));
            Select(s); yield return Wait(0.3f);
            Shot($"k389_{tag}_after_h{h:0}");
        }

        // ------------------------------------------------------------------ C3: the crew leaves (the heroes out on the ground)
        IEnumerator Emptied(int cx, int cy)
        {
            var brad = W.Objects.FirstOrDefault(o => o.IsHero && o.HeroMemberName == "brad" && !o.Destroyed && o.KillAt < 0 && o.Owner == me && !o.IsMinion);
            if (brad == null) { Log("C3 skipped: no ALAN & BRAD on this level (the hero squads off)"); yield break; }
            var q = Spot(brad.CenterX, brad.CenterY, 240, 900);
            if (q == null) { Check("C3 a free place for the drop", false); yield break; }
            var s = OzCheatShuttle.Spawn(g, hud, Team.Neutral, new Vector2Int(q[0], q[1]));
            var ship = s != null ? OzPlayableShuttle.Of(s) : null;
            if (s == null || !ship) { Check("C3 the panel drops an empty shuttle", false); yield break; }
            var look = ship.GetComponent<OzShuttleEmptyLook>();
            Cam(ship.transform.position, 70f);
            Check("C3 the ENTER order of ALAN & BRAD on the empty ship is taken", Enter(brad, s));
            yield return WaitFor(() => s.Drivers.Count > 0, 40f);
            yield return WaitFor(() => look && !look.IsDark && s.ShuttleRampPhase == ZShuttleRamp.Shut, 20f); yield return Wait(1.5f);
            var fx = ship.GetComponent<OzShuttleCaptureFx>();
            int flashes0 = OzShuttleCaptureFx.Flashes;
            Select(s); yield return Wait(0.3f);
            if (!s.CanEjectCrew()) { Check("C3 EXIT is offered on the ground with the heroes aboard", false); yield break; }
            float a0 = Time.unscaledTime;
            hud.ExitSelected();
            float waited = 0f;
            while (look && !look.IsDark && waited < 20f) { waited += Time.deltaTime; yield return null; }
            if (!look || !look.IsDark) { Check("C3 the ship goes dark once the crew has left", false); yield break; }
            float s0 = Time.time; float litUntil = -1f, darkAt = -1f, prev = 2f; bool falling = true;
            var pw = new List<float>(); int next = 0; float[] at = { 0f, 0.2f, 0.4f, 0.6f };
            while (Time.time - s0 < 1.0f)
            {
                float u = Time.time - s0, p = fx ? fx.Shown : -1f; pw.Add(p);
                if (look.Lights && look.Lights.Mode != 2) litUntil = u; else if (darkAt < 0f) darkAt = u;
                if (p > prev + 1e-4f) falling = false; prev = p;
                if (next < at.Length && u >= at[next]) { Shot($"k389_c3_emptied_t{Mathf.RoundToInt(at[next] * 1000f):0000}_h70"); next++; }
                yield return null;
            }
            Log($"C3 emptied: lights mode 0 until {litUntil:F2} s, mode 2 from {darkAt:F2} s, levels {string.Join(" ", pw.Take(40).Select(x => x.ToString("F2")))}; flashes {OzShuttleCaptureFx.Flashes - flashes0}; " +
                $"engine shut-down in the sound log {Played(a0, Time.unscaledTime, OzShuttleHeroesView.PowerDownEvent)}");
            Check("C3 as the crew leaves the lights fade out as a vehicle's (about 0.4 s, only falling), then dark; no capture flash", litUntil >= 0.25f && darkAt > 0f && darkAt <= 0.7f && falling && OzShuttleCaptureFx.Flashes == flashes0);
        }

        void Finish()
        {
            Report = $"{(fails == 0 ? "PASS" : "FAIL")} {passes} passed, {fails} failed, {frames} frames";
            sb.Insert(0, Report + "\n");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "capture_fx_proof_log.txt"), sb.ToString()); }
            catch (System.Exception ex) { Debug.LogWarning("[OzShuttleCaptureFxProof] " + ex.Message); }
            Debug.Log("[OzShuttleCaptureFxProof] " + Report + " -> " + dir);
            Destroy(gameObject);
        }
    }
}
