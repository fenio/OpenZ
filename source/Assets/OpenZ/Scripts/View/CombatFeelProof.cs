#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace OpenZ
{
    /// <summary>
    ///   tank shells the Toughs; every sim knock window, every sim rocket (MissileFired) and every drawn launch is logged per Tough.
    ///   every death / knockdown throw (HitReactions.ImpulseChecked) is checked against the shooter direction, and every ragdoll's
    ///   real flight (its first rigid body 0.9 s after the death) too.
    ///   rockets (OpenZFxPlayer.ProjTrace) gives the speed profile and the landing vs the sim's blast; 60 fps strip crops per launcher.
    /// </summary>
    [DefaultExecutionOrder(44)]   // LateUpdate reads ZWorld.Events before CombatView (50) clears them (GateBattery's order)
    public class CombatFeelProof : MonoBehaviour
    {
        static readonly string Dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../../work/records/milestones/bundle_combat_feel"));
        string job, dir; float lastMark; bool done; int errors; float hot = -1f, lastSample = -1f; int peak = -1;
        readonly StringBuilder log = new StringBuilder(), csvImp = new StringBuilder("t,victim,type,what,raw_dot,safe_dot,shooter\n"), csvProj = new StringBuilder("t,serial,weapon,eased,age,life,x,y,z,frame\n");
        readonly List<string> verdicts = new List<string>();
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] ## {s}"); Debug.Log("[cfeel] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) yield return null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_cfeel_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_cfeel_auto"); PlayerPrefs.Save();
            var go = new GameObject("CombatFeelProof"); DontDestroyOnLoad(go);
            go.AddComponent<CombatFeelProof>().job = job;
        }

        void OnEnable()
        {
            Application.logMessageReceived += OnLog;
            HitReactions.ImpulseChecked += OnImpulse;
            OpenZFxPlayer.ProjTrace = OnProj;
            LegacyMatch.Launched += OnLaunched;
            OzDeathTrace.BlastDrawn += OnBlastDrawn;
        }
        void OnDisable()
        {
            Application.logMessageReceived -= OnLog; HitReactions.ImpulseChecked -= OnImpulse; LegacyMatch.Launched -= OnLaunched; OzDeathTrace.BlastDrawn -= OnBlastDrawn;
            if (OpenZFxPlayer.ProjTrace == (Action<int, FxWeapon, float, float, Vector3, bool>)OnProj) OpenZFxPlayer.ProjTrace = null;
            if (!done) Finish("disabled");
        }
        void OnLog(string c, string st, LogType type) { if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c}"); } }

        // ---------------------------------------------------------------- heat
        static int GpuTemp()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = a.GetType("OpenZ.EditorTools.GpuGuard"); if (t == null) continue;
                var p = t.GetProperty("LastTemp"); return p != null ? (int)p.GetValue(null) : -1;
            }
            return -1;
        }
        IEnumerator Heat()
        {
            while (!done)
            {
                int gpu = GpuTemp(); peak = Mathf.Max(peak, gpu);
                float step = lastSample < 0f ? 0.5f : T - lastSample; lastSample = T;
                if (gpu >= 90) { Finish("GPU 90 C"); yield break; }
                if (gpu >= 86) { hot = hot < 0f ? step : hot + step; if (hot >= 30f) { Finish("GPU >= 86 C for 30 s"); yield break; } }
                else if (gpu >= 0 && gpu < 84) hot = -1f;
                if (T - lastMark > 90f) { Finish("watchdog: no progress for 90 s"); yield break; }
                yield return new WaitForSecondsRealtime(0.5f);
            }
        }

        // ---------------------------------------------------------------- sim / view helpers
        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; Camera ucam; Team me, them; float h0;
        readonly List<ZObject> staged = new List<ZObject>();
        static void Unkillable(ZObject o) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
        ZObject Squad(string type, Team t, int px, int py, bool unkillable)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 1, true, out int tx, out int ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            staged.Add(o); staged.AddRange(o.Minions);
            if (unkillable) { Unkillable(o); foreach (var m in o.Minions) Unkillable(m); }
            return o;
        }
        ZObject Machine(string cat, string type, Team t, int px, int py, bool unkillable)
        {
            OzCheatSpawner.FreeTile(W, px >> 4, py >> 4, 2, false, out int tx, out int ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); if (o == null) return null;
            g.SpawnUnitView(o); staged.Add(o); if (unkillable) Unkillable(o); return o;
        }
        bool Attack(ZObject who, ZObject target) =>
            W.SendWaypoints(who.Owner, who, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = target.Id, x = target.CenterX, y = target.CenterY } });
        void Hold(ZObject o) { if (o == null) return; o.Damage = 0; }
        void Clear()
        {
            foreach (var o in staged) if (o != null && !o.Destroyed && W.Get(o.Id) == o) { o.Waypoints.Clear(); o.Disengage(); o.SetHealth(0); W.UpdateObjectHealth(o); }
            staged.Clear();
        }
        void Focus(int px, int py, float h) { var p = ZGame.PxToWorld(px, py); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(h); }
        void Frame(string name)
        {
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(dir, name + ".png"), tex.EncodeToPNG());
            Mark($"frame {name}.png {tex.width}x{tex.height}, camera {cam.height:0} m");
            Destroy(tex);
        }
        List<ZObject> Members(ZObject leader) { var l = new List<ZObject> { leader }; l.AddRange(leader.Minions); return l; }
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        // ---------------------------------------------------------------- event capture (LateUpdate before CombatView)
        readonly List<(float t, double sim, ZEvent e)> events = new List<(float, double, ZEvent)>();
        void LateUpdate()
        {
            if (W == null) return;
            if (traceProj || traceBlasts) frameDt[Time.frameCount] = Time.deltaTime;             // FIX: the dt the FX player steps this frame (hitch-aware E checks)
            foreach (var e in W.Events)
                if (e.type == ZEventType.MissileFired || e.type == ZEventType.MissileExploded || e.type == ZEventType.Death) events.Add((T, W.Time, e));
            // knock windows of the watched Toughs
            foreach (var o in watch)
                if (o.KnockAt >= 0 && (!knockSeen.TryGetValue(o.Id, out var k) || k != o.KnockAt))
                {
                    knockSeen[o.Id] = o.KnockAt; windows.Add((o.Id, o.KnockAt, o.KnockUntil));
                    log.AppendLine($"[{T,7:0.00}] knock #{o.Id} sim {o.KnockAt:0.000} -> {o.KnockUntil:0.000}");
                }
            foreach (var o in watch)
                if (g.Views.TryGetValue(o.Id, out var v) && v) { bool r = v.Reacting; if (r) reactFrames++; if (r && !wasReact.Contains(o.Id)) wasReact.Add(o.Id); else if (!r) wasReact.Remove(o.Id); }
        }
        readonly List<ZObject> watch = new List<ZObject>();
        readonly Dictionary<int, double> knockSeen = new Dictionary<int, double>();
        readonly List<(int id, double at, double until)> windows = new List<(int, double, double)>();
        readonly HashSet<int> wasReact = new HashSet<int>(); int reactFrames;
        readonly List<(float t, int id, string type, bool reacting)> drawnLaunches = new List<(float, int, string, bool)>();
        void OnLaunched(ZObject a, string type, float delay)
        {
            if (a == null) return;
            bool r = g != null && g.Views.TryGetValue(a.Id, out var v) && v && v.Reacting;
            drawnLaunches.Add((T, a.Id, type, r));
        }

        // impulses
        struct Imp { public float t; public int id; public string type, what; public float raw, safe; public Vector3 p, s; }
        readonly List<Imp> imps = new List<Imp>();
        void OnImpulse(ZObject o, string what, Vector3 p, Vector3 raw, Vector3 safe, Vector3 shooter)
        {
            var s = Flat(p - shooter); if (s.sqrMagnitude < 1e-4f) return; s.Normalize();
            var a = Flat(p - raw); var b = Flat(p - safe);
            float ra = a.sqrMagnitude > 1e-6f ? Vector3.Dot(a.normalized, s) : 1f, sa = b.sqrMagnitude > 1e-6f ? Vector3.Dot(b.normalized, s) : 1f;
            imps.Add(new Imp { t = T, id = o.Id, type = o.Type, what = what, raw = ra, safe = sa, p = p, s = shooter });
            csvImp.AppendLine($"{T:0.000},{o.Id},{o.Type},{what},{ra:0.000},{sa:0.000},{shooter.x:0.0}/{shooter.z:0.0}");
            if (what.StartsWith("death") && g.Views.TryGetValue(o.Id, out var v) && v) StartCoroutine(Flight(v, o, p, shooter, what));
        }
        readonly List<(int id, string what, float dot, float dist)> flights = new List<(int, string, float, float)>();
        IEnumerator Flight(UnitView v, ZObject o, Vector3 p0, Vector3 shooter, string what)
        {
            float end = T + 2.5f; Rigidbody rb = null; Vector3 r0 = default; float t0 = 0f;
            while (T < end && v)
            {
                if (!rb) { var bodies = v.GetComponentsInChildren<Rigidbody>(); foreach (var b in bodies) if (b && !b.isKinematic) { rb = b; r0 = b.position; t0 = T; break; } }
                else if (T - t0 >= 0.9f) break;
                yield return null;
            }
            if (!rb) { log.AppendLine($"[{T,7:0.00}] flight #{o.Id} {o.Type} {what}: no ragdoll body ({(v && v.Popped ? "pop death" : "other death")})"); yield break; }
            var d = Flat(rb.position - r0); var s = Flat(p0 - shooter).normalized;
            float dot = d.sqrMagnitude > 1e-4f ? Vector3.Dot(d.normalized, s) : 1f;
            flights.Add((o.Id, what, dot, d.magnitude));
            log.AppendLine($"[{T,7:0.00}] flight #{o.Id} {o.Type} {what}: moved {d.magnitude:0.00} m in 0.9 s, dot with shooter->victim {dot:0.00}");
        }

        // projectiles
        readonly Dictionary<int, List<(float age, float life, Vector3 pos, FxWeapon w, bool ease)>> proj = new Dictionary<int, List<(float, float, Vector3, FxWeapon, bool)>>();
        bool traceProj;
        void OnProj(int serial, FxWeapon w, float age, float life, Vector3 pos, bool ease)
        {
            if (!traceProj) return;
            if (!proj.TryGetValue(serial, out var l)) proj[serial] = l = new List<(float, float, Vector3, FxWeapon, bool)>();
            l.Add((age, life, pos, w, ease));
            int fc = Time.frameCount;
            if (!flightEnd.TryGetValue(serial, out var fe)) fe = new FlightEnd { firstFrame = fc, firstAge = age, w = w, ease = ease, life = life };
            fe.lastFrame = fc; fe.lastAge = age; fe.lastPos = pos; flightEnd[serial] = fe;
            csvProj.AppendLine($"{T:0.000},{serial},{w},{(ease ? 1 : 0)},{age:0.0000},{life:0.0000},{pos.x:0.000},{pos.y:0.000},{pos.z:0.000},{fc}");
        }
        // blast CombatView draws (OzDeathTrace.BlastDrawn), so each sim blast is paired with the drawn rocket that ends on it
        struct FlightEnd { public int firstFrame, lastFrame; public float firstAge, lastAge, life; public Vector3 lastPos; public FxWeapon w; public bool ease; }
        readonly Dictionary<int, FlightEnd> flightEnd = new Dictionary<int, FlightEnd>();
        readonly List<(int frame, ZEvent e, Vector3 at)> blastsDrawn = new List<(int, ZEvent, Vector3)>();
        bool traceBlasts;
        readonly StringBuilder csvBlast = new StringBuilder("t,frame,shooter,target,x,y,z,sim_px_x,sim_px_y,radius_px\n");
        void OnBlastDrawn(ZEvent e, Vector3 at)
        {
            if (!traceBlasts || e.flag) return;                                                   // grenades are not rockets
            blastsDrawn.Add((Time.frameCount, e, at));
            csvBlast.AppendLine($"{T:0.000},{Time.frameCount},{e.objId},{e.otherId},{at.x:0.000},{at.y:0.000},{at.z:0.000},{e.x},{e.y},{e.radius}");
        }
        readonly Dictionary<int, float> frameDt = new Dictionary<int, float>();
        /// <summary>A flight whose last traced frame is followed by a frame long enough to carry it past its end (an editor hitch) ended there.</summary>
        bool EndedOnLongFrame(FlightEnd f) { float rem = f.life - f.lastAge; return rem > 0.04f && frameDt.TryGetValue(f.lastFrame + 1, out var d) && d + 1e-4f >= rem; }
        bool Airborne() { int fc = Time.frameCount; foreach (var kv in flightEnd) if (kv.Value.lastFrame >= fc - 1 && kv.Value.lastAge < kv.Value.life - 0.001f) return true; return false; }

        static void GameViewSize(int idx)
        {
            var gvt = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var p = gvt?.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) return;
            foreach (var w in Resources.FindObjectsOfTypeAll(gvt)) p.SetValue(w, idx);
        }
        IEnumerator SkipBriefing()
        {
            float w0 = T; bool aborted = false; var pf = typeof(ZodBriefing).GetField("perf", BindingFlags.NonPublic | BindingFlags.Instance);
            double sim0 = W.Time;
            Application.targetFrameRate = 24;                                                       // heat: nothing is captured while waiting
            while (T - w0 < 45f && !done)
            {
                if (ZodBriefing.BriefingActive)
                {
                    if (!aborted && ZodBriefing.I && pf != null && T - w0 > 0.6f)
                    {
                        var p = pf.GetValue(ZodBriefing.I);
                        if (p != null) { p.GetType().GetMethod("Abort").Invoke(p, new object[] { 0.22f }); aborted = true; Mark("briefing: Abort(0.22 s) sent"); }
                    }
                }
                else if (!g.paused && W.Time > sim0 + 0.5) break;
                lastMark = T;
                yield return null;
            }
            Application.targetFrameRate = 60;
            Mark($"briefing over after {T - w0:0.0} s");
        }

        // ---------------------------------------------------------------- the run
        IEnumerator Start()
        {
            lastMark = T; float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null) && T - w0 < 90f) { lastMark = T; yield return null; }
            if (!ZGame.I || ZGame.I.World == null) { Finish("the level never came up within 90 s"); yield break; }
            g = ZGame.I; W = g.World; cam = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>(); ucam = cam.GetComponent<Camera>(); if (!ucam) ucam = Camera.main;
            dir = Path.Combine(Dir, job); Directory.CreateDirectory(dir); h0 = cam.height;          // job "L02" = all phases, "L02_RE" = ragdoll + easing only
            StartCoroutine(Heat());
            me = g.player; them = me == Team.Red ? Team.Blue : Team.Red;
            Mark($"combat feel proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, screen {Screen.width}x{Screen.height}; DV51 {ZWorld.KnockHoldDeviation}, DV52 {ZWorld.RocketSpeedDeviation}, easing {OpenZFxPlayer.RocketEasing}, DV19 {HitReactions.Enabled}, DV11 {ZWorld.ToughPrepareDeviation}");
            int bots = W.Bots.Count; W.Bots.Clear();
            yield return SkipBriefing();
            if (done) yield break;
            bool run0 = dn && dn.running; if (dn) { dn.running = false; dn.timeOfDay = 0.08f; dn.Apply(0.08f); }
            ZObject F(Team t) { foreach (var o in W.Objects) if (o.IsFort && o.Owner == t && !o.Destroyed) return o; return null; }
            var fr = F(me); var fb = F(them);
            int cx = (fr.CenterX + fb.CenterX) / 2, cy = (fr.CenterY + fb.CenterY) / 2;
            Mark($"CPU bots cleared ({bots}); field centre px ({cx},{cy}) tile ({cx >> 4},{cy >> 4}); unit speeds {ZWorld.RocketSpeed("tough", W.S.Unit("tough").attack_missile_speed)} / {ZWorld.RocketSpeed("missile_cannon", W.S.Unit("missile_cannon").attack_missile_speed)} px/s (tough / missile cannon)");

            bool eOnly = job.EndsWith("_E");
            if (!job.EndsWith("_RE") && !eOnly)
            {
                yield return PhaseKnock(cx, cy);
                if (done) yield break;
                Clear(); yield return Wait(2.5f);
            }
            if (!eOnly)
            {
                yield return PhaseRagdoll(cx, cy);
                if (done) yield break;
                Clear(); yield return Wait(2f);
            }
            yield return PhaseEasing(cx, cy);
            if (dn) dn.running = run0;
            Finish(null);
        }

        // ---------------------------------------------------------------- K: no fire while knocked down (DV51)
        IEnumerator PhaseKnock(int cx, int cy)
        {
            var toughs = Squad("tough", me, cx - 40, cy, true);
            var target = Squad("grunt", them, cx + 44, cy, true);
            foreach (var m in Members(target)) Hold(m);
            var tank = Machine("vehicle", "heavy", them, cx - 40, cy - 118, true);
            if (toughs == null || target == null || tank == null) { Verdict("K staging", false, "spawn failed"); yield break; }
            watch.Clear(); watch.AddRange(Members(toughs));
            Focus(cx - 4, cy - 30, 34f);
            yield return Wait(0.6f);
            Mark($"K: red Tough squad #{toughs.Id} x{watch.Count} (unkillable) -> blue grunts #{target.Id} (harmless); blue heavy tank #{tank.Id} at ({tank.CenterX >> 4},{tank.CenterY >> 4}) shells the Toughs");
            foreach (var m in watch) Attack(m, target);
            Attack(tank, toughs);
            int e0 = events.Count, l0 = drawnLaunches.Count; float k0 = T;
            bool shotDown = false; int winSeen = 0;
            while (T - k0 < 16f && !done)
            {
                if (windows.Count > winSeen)
                {
                    var w = windows[windows.Count - 1]; winSeen = windows.Count;
                    if (!shotDown) { shotDown = true; StartCoroutine(KnockFrames(w)); }
                }
                yield return null;
            }
            // analysis per Tough
            var ids = new HashSet<int>(); foreach (var m in watch) ids.Add(m.Id);
            int inWin = 0, fired = 0, afterOk = 0, afterN = 0; var sb = new StringBuilder();
            var rockets = new List<(int id, double sim)>();
            for (int i = e0; i < events.Count; i++) { var ev = events[i]; if (ev.e.type == ZEventType.MissileFired && ids.Contains(ev.e.objId)) { rockets.Add((ev.e.objId, ev.sim)); fired++; } }
            foreach (var w in windows)
            {
                if (!ids.Contains(w.id)) continue;
                int n = 0; double first = -1;
                foreach (var r in rockets) { if (r.id != w.id) continue; if (r.sim > w.at + 1e-6 && r.sim < w.until - 1e-6) n++; if (r.sim >= w.until - 1e-6 && first < 0) first = r.sim; }
                inWin += n;
                if (first >= 0) { afterN++; if (first >= w.until - 1e-6) afterOk++; }
                sb.Append($"#{w.id} down {w.at:0.00}-{w.until:0.00} s: {n} rockets inside, first after +{(first >= 0 ? (first - w.until).ToString("0.00") : "none")} s; ");
            }
            int drawnDown = 0, drawn = 0;
            for (int i = l0; i < drawnLaunches.Count; i++) { var d = drawnLaunches[i]; if (!ids.Contains(d.id)) continue; drawn++; if (d.reacting) drawnDown++; }
            Mark($"K: {windows.Count} knock windows, {fired} sim rockets, {drawn} drawn launches ({drawnDown} while the view was down), view down-frames {reactFrames}; {sb}");
            Verdict("K1 knocked down", windows.Count >= 1, $"{windows.Count} knockdowns of the Tough squad by the heavy tank's shells");
            Verdict("K2 no sim rocket while down", windows.Count >= 1 && inWin == 0, $"{inWin} rockets inside the knock windows ({fired} fired in the phase)");
            Verdict("K3 no drawn launch while down", drawnDown == 0 && drawn > 0, $"{drawnDown} of {drawn} drawn launches while the Tough's view was knocked down");
            Verdict("K4 fires after the get-up", afterN >= 1 && afterOk == afterN, $"{afterOk}/{afterN} windows followed by a rocket at or after KnockUntil");
            watch.Clear();
        }
        IEnumerator KnockFrames((int id, double at, double until) w)
        {
            // one 60 fps strip from the blast to the first rocket after the get-up (frame n = n / 60 s: captureFramerate), with full
            // PNG key frames: lying (+1.0 s), getting up (KnockUntil - 0.25 s), the first rocket after it (KnockUntil + 0.95 s)
            double simNow = W.Time; float sp = Mathf.Max(0.05f, g.gameSpeed);
            Func<double, int> F = sim => Mathf.Max(0, Mathf.RoundToInt((float)((sim - simNow) / sp) * 60f));
            var keys = new Dictionary<int, string> { { F(w.at + 1.0), "K_tough_knocked_down" }, { F(w.until - 0.25), "K_tough_getting_up" }, { F(w.until + 0.95), "K_tough_first_shot_after" } };
            Mark($"K strip: knock #{w.id} {w.at:0.00} -> {w.until:0.00} s (sim now {simNow:0.00}), key frames {string.Join(", ", keys.Keys)}");
            yield return StripKeys("K_knock_60fps", F(w.until + 1.1), 3, keys);
        }

        // ---------------------------------------------------------------- R: ragdoll / knockback direction
        IEnumerator PhaseRagdoll(int cx, int cy)
        {
            int i0 = imps.Count, f0 = flights.Count;
            // R1 a heavy tank shell kills a red grunt squad, frames of the throw
            var tank = Machine("vehicle", "heavy", them, cx + 70, cy, true); int tankDmg = tank.Damage; tank.Damage = 0;   // no shot before the strip runs
            var grunts = Squad("grunt", me, cx - 40, cy, false);
            foreach (var m in Members(grunts)) { m.Damage = 0; m.SetHealth(Mathf.Min(m.Health, 300)); }
            Focus(cx + 10, cy, 30f); yield return Wait(0.5f);
            tank.Damage = tankDmg; Attack(tank, grunts);
            Mark($"R1: blue heavy tank #{tank.Id} -> red grunt squad #{grunts.Id} (300 HP, harmless)");
            int e1 = events.Count; float r0 = T; bool shot = false;
            while (T - r0 < 12f && !done && Alive(grunts))
            {   // the strip starts with the tank's shot: the shell's flight, the blast and the throws (2.2 s at 60 fps)
                if (!shot) for (int i = e1; i < events.Count; i++) if (events[i].e.type == ZEventType.MissileFired && events[i].e.objId == tank.Id) { shot = true; break; }
                if (shot) { yield return StripKeys("R1_tank_shell_kills_squad", 132, 2, new Dictionary<int, string> { { 54, "R1_tank_shell_throw_a" }, { 78, "R1_tank_shell_throw_b" } }); break; }
                yield return null;
            }
            yield return Wait(1.5f);
            Clear(); yield return Wait(1f);
            // R2 red Toughs rocket a blue grunt squad; R3 an MLRS on psychos; R4 a howitzer on grunts; R5 grenades
            var t2 = Squad("tough", me, cx - 60, cy - 30, true); var v2 = Squad("grunt", them, cx + 10, cy - 30, false);
            var ml = Machine("vehicle", "missile_launcher", me, cx - 80, cy + 40, true); var v3 = Squad("psycho", them, cx + 20, cy + 40, false);
            var hw = Machine("cannon", "howitzer", them, cx + 110, cy + 5, true); var v4 = Squad("grunt", me, cx - 20, cy + 5, false);
            var gr = Squad("grunt", me, cx - 20, cy - 70, true); var v5 = Squad("grunt", them, cx + 30, cy - 70, false);
            foreach (var l in new[] { v2, v3, v4, v5 }) if (l != null) foreach (var m in Members(l)) m.Damage = 0;
            if (gr != null) gr.Grenades = 6;
            Focus(cx, cy, 44f); yield return Wait(0.4f);
            foreach (var m in Members(t2)) Attack(m, v2);
            Attack(ml, v3); Attack(hw, v4);
            foreach (var m in Members(gr)) Attack(m, v5);
            Mark("R2-R5: red Toughs -> blue grunts, red MLRS -> blue psychos, blue howitzer -> red grunts, red grunts with 6 grenades -> blue grunts");
            float r2 = T; bool ov = false;
            while (T - r2 < 22f && !done && (Alive(v2) || Alive(v3) || Alive(v4) || Alive(v5)))
            {
                if (!ov && T - r2 > 3.2f) { ov = true; float hh = cam.height; cam.SetHeightImmediate(h0); yield return null; Frame("R_fight_default_rts_height"); cam.SetHeightImmediate(hh); }
                yield return null;
            }
            yield return Wait(1.5f);
            int n = imps.Count - i0, bad = 0, fixedN = 0, deaths = 0, reacts = 0; var kinds = new SortedDictionary<string, int>();
            for (int i = i0; i < imps.Count; i++)
            {
                var m = imps[i]; if (m.safe <= 0f) bad++; if (m.raw <= 0f && m.safe > 0f) fixedN++;
                if (m.what.StartsWith("death")) deaths++; else reacts++;
                kinds[m.what] = kinds.TryGetValue(m.what, out var c) ? c + 1 : 1;
            }
            var kb = new StringBuilder(); foreach (var kv in kinds) kb.Append($"{kv.Key} x{kv.Value}; ");
            int fl = flights.Count - f0, flBad = 0; float minDot = 1f;
            for (int i = f0; i < flights.Count; i++) { if (flights[i].dot <= 0f && flights[i].dist > 0.3f) flBad++; minDot = Mathf.Min(minDot, flights[i].dot); }
            float minSafe = 1f; for (int i = i0; i < imps.Count; i++) minSafe = Mathf.Min(minSafe, imps[i].safe);
            Mark($"R: {n} throws ({deaths} deaths, {reacts} knockbacks: {kb}), {fixedN} would have pointed at the shooter (raw dot <= 0) and were turned; min dot {minSafe:0.00}; ragdoll flights {fl}, min dot {minDot:0.00}");
            Verdict("R1 throws away from the shooter", n >= 6 && deaths >= 3 && bad == 0, $"{n} throws, {deaths} deaths, dot(push, shooter->victim) > 0 for {n - bad}/{n}, min {minSafe:0.00}; {fixedN} turned");
            Verdict("R2 real ragdoll flights away", fl >= 1 && flBad == 0, $"{fl} ragdoll flights measured over 0.9 s, {flBad} toward the shooter, min dot {minDot:0.00}");
        }
        int DeathCount() { int n = 0; foreach (var e in events) if (e.e.type == ZEventType.Death) n++; return n; }
        bool Alive(ZObject leader) { if (leader == null) return false; foreach (var m in Members(leader)) if (!m.Destroyed && m.KillAt < 0 && W.Get(m.Id) == m) return true; return false; }

        /// <summary>A strip at 60 fps (every `every`-th frame, JPG crop of the screen centre) for `secs` real seconds.</summary>
        IEnumerator Strip(string name, float secs, int every) => StripKeys(name, Mathf.RoundToInt(secs * 60f), every, null);

        /// <summary>A 60 fps strip of n frames (captureFramerate 60): every `every`-th frame a JPG crop of the screen centre, and a full
        /// PNG at each key frame.</summary>
        string stripBlastKey; int stripBlastSeen;
        IEnumerator StripKeys(string name, int n, int every, Dictionary<int, string> keys, int cropX = -1)   // cropX: left edge of the crop (-1 = centred)
        {
            var sdir = Path.Combine(dir, name); Directory.CreateDirectory(sdir);
            Time.captureFramerate = 60; int f = 0;
            while (f < n && !done)
            {
                yield return new WaitForEndOfFrame();
                bool key = keys != null && keys.ContainsKey(f);
                bool bkey = stripBlastKey != null && blastsDrawn.Count > stripBlastSeen;              // FIX: a full frame on the first drawn blast
                if (f % every == 0 || key || bkey)
                {
                    var tex = ScreenCapture.CaptureScreenshotAsTexture();
                    if (bkey) { File.WriteAllBytes(Path.Combine(dir, stripBlastKey + ".png"), tex.EncodeToPNG()); log.AppendLine($"[{T,7:0.00}] ## frame {stripBlastKey}.png {tex.width}x{tex.height} (strip {name} frame {f}: the blast frame, Time.frameCount {Time.frameCount})"); stripBlastKey = null; }
                    if (key) { File.WriteAllBytes(Path.Combine(dir, keys[f] + ".png"), tex.EncodeToPNG()); log.AppendLine($"[{T,7:0.00}] ## frame {keys[f]}.png {tex.width}x{tex.height} (strip {name} frame {f}, +{f / 60f:0.00} s)"); }
                    if (f % every == 0)
                    {
                        int cw = Mathf.Min(1600, tex.width), ch = Mathf.Min(1000, tex.height);
                        var crop = new Texture2D(cw, ch, TextureFormat.RGB24, false);
                        crop.SetPixels(tex.GetPixels(cropX >= 0 ? Mathf.Min(cropX, tex.width - cw) : (tex.width - cw) / 2, (tex.height - ch) / 2, cw, ch)); crop.Apply();
                        File.WriteAllBytes(Path.Combine(sdir, $"f{f:000}.jpg"), crop.EncodeToJPG(88));
                        Destroy(crop);
                    }
                    Destroy(tex);
                }
                f++; lastMark = T;
            }
            Time.captureFramerate = 0;
            Mark($"strip {name}: {n} frames at 60 fps, every {every}th saved");
        }

        // ---------------------------------------------------------------- E: rocket easing + DV52 speeds
        // 2 / 3 cut 3 missile cannon flights at the trace end), and every blast CombatView draws for this launcher is paired with the
        // drawn eased rocket whose last frame is just before it: frame gap, time left on the curve, distance to the DRAWN blast point
        // (bug #13 / IM1 put a vehicle hit on the hull, so the sim's ground point is only logged). "L02_E" adds the missile cannon at night.
        IEnumerator PhaseEasing(int cx, int cy)
        {
            var target = Machine("vehicle", "light", them, cx + 60, cy, true); Hold(target); target.MoveSpeed = 0; target.RealMoveSpeed = 0;
            var res = new StringBuilder(); bool allOk = true; int checkedN = 0, legsN = 0, pairsAll = 0, cutAll = 0, wholeAll = 0, longAll = 0;
            var legs = new List<(string cat, string type, bool night)> { ("robot", "tough", false), ("vehicle", "missile_launcher", false), ("cannon", "missile_cannon", false) };
            if (job.EndsWith("_E")) legs.Add(("cannon", "missile_cannon", true));
            foreach (var leg in legs)
            {
                string cat = leg.cat, type = leg.type, tag = type + (leg.night ? "_night" : "");
                if (dn) { float tod = leg.night ? 0.62f : 0.08f; if (Mathf.Abs(dn.timeOfDay - tod) > 0.01f) { dn.timeOfDay = tod; dn.Apply(tod); yield return Wait(1.2f); } }
                legsN++;
                var a = cat == "robot" ? Squad(type, me, cx - 40, cy, true) : Machine(cat, type, me, cx - 70, cy, true);
                if (a == null) { allOk = false; res.Append($"{tag}: spawn failed; "); continue; }
                if (cat == "robot") foreach (var m in a.Minions) { m.Damage = 0; }                    // one rocket at a time: the leader fires
                int aDmg = a.Damage; a.Damage = 0;
                // panel, the launcher on the left, and the strip crop is the left 1600 px (no HUD panel in it)
                Focus((a.CenterX + target.CenterX) / 2 + 65, cy, 36f); yield return Wait(0.5f);
                proj.Clear(); flightEnd.Clear(); blastsDrawn.Clear(); frameDt.Clear(); traceProj = true; traceBlasts = true;
                a.Damage = aDmg; Attack(a, target);
                float t0 = T; bool stripped = false; int launchSeen = drawnLaunches.Count;
                while (T - t0 < 9f && !done)
                {
                    if (!stripped && drawnLaunches.Count > launchSeen) { stripped = true; stripBlastKey = $"E_{tag}_blast_full"; stripBlastSeen = blastsDrawn.Count; yield return StripKeys($"E_{tag}_60fps", 84, 2, new Dictionary<int, string> { { 14, $"E_{tag}_launch_full" } }, 0); break; }
                    yield return null;
                }
                yield return Wait(2.5f);                                                            // more salvos inside the trace
                float q0 = T; while (Airborne() && T - q0 < 4f && !done) yield return null;
                traceProj = false; bool quiet = !Airborne();
                a.Waypoints.Clear(); a.Disengage(); a.Damage = 0;
                yield return Wait(0.4f); traceBlasts = false;                                       // the last rocket's blast is drawn on its landing frame
                // the speed profile of the first whole eased flight of this launcher
                List<(float age, float life, Vector3 pos, FxWeapon w, bool ease)> best = null;
                foreach (var kv in proj) if (kv.Value.Count > 8 && kv.Value[0].ease && kv.Value[0].age < 0.05f && (best == null || kv.Key < FirstKey(best))) best = kv.Value;
                if (best == null) { allOk = false; res.Append($"{tag}: no eased rocket traced; "); staged.Remove(target); Clear(); staged.Add(target); continue; }
                float life = best[0].life; float Early = 0f, Late = 0f; int ne = 0, nl = 0;
                for (int i = 1; i < best.Count; i++)
                {
                    float dt = best[i].age - best[i - 1].age; if (dt <= 1e-5f) continue;
                    float sp = (best[i].pos - best[i - 1].pos).magnitude / dt, tau = best[i].age / life;
                    if (tau < 0.12f) { Early += sp; ne++; } else if (tau > 0.6f) { Late += sp; nl++; }
                }
                float early = ne > 0 ? Early / ne : 0f, late = nl > 0 ? Late / nl : 0f, ratio = late > 0f ? early / late : 0f;
                // every drawn blast of this launcher <-> the eased rocket that ends on it
                int pairs = 0, unpaired = 0, badGap = 0, badRemain = 0, far = 0, worstGap = 0, hitchN = 0; float worstD = 0f, worstRemain = 0f;
                var used = new HashSet<int>(); var pl = new StringBuilder();
                foreach (var b in blastsDrawn)
                {
                    if (b.e.objId != a.Id) continue;
                    int bestS = -1; float bestD = float.MaxValue;
                    foreach (var kv in flightEnd)
                    {
                        var f = kv.Value; if (!f.ease || used.Contains(kv.Key)) continue;
                        int gap = b.frame - f.lastFrame; if (gap < 0 || gap > 6) continue;
                        float dd = Flat(f.lastPos - b.at).magnitude; if (dd < bestD) { bestD = dd; bestS = kv.Key; }
                    }
                    if (bestS < 0) { unpaired++; pl.Append($"blast frame {b.frame} UNPAIRED; "); continue; }
                    used.Add(bestS); var fe = flightEnd[bestS]; pairs++;
                    int g2 = b.frame - fe.lastFrame; float rem = fe.life - fe.lastAge;
                    var sim = ZGame.PxToWorld(b.e.x, b.e.y); float dSim = Flat(fe.lastPos - sim).magnitude;
                    bool longF = EndedOnLongFrame(fe); if (longF) hitchN++;
                    if (g2 != 1) badGap++; if (rem > 0.04f && !longF) badRemain++; if (bestD > 0.3f && !longF) far++;   // FIX: gone on the blast frame, on the drawn blast point
                    if (!longF) { worstD = Mathf.Max(worstD, bestD); worstRemain = Mathf.Max(worstRemain, rem); } worstGap = Mathf.Max(worstGap, g2);
                    string hit = b.e.otherId == target.Id ? ", hit the tank" : "";
                    if (longF) pl.Append($"#{bestS} ended on a long frame ({frameDt[fe.lastFrame + 1] * 1000f:0} ms dt >= its last {rem * 1000f:0} ms; blast on that same frame): ");
                    pl.Append($"#{bestS}{(fe.firstAge > 0.05f ? " (from mid-air)" : "")}: blast drawn {g2} frame(s) after its last frame, last frame {rem * 1000f:0} ms before its end, {bestD:0.00} m from the drawn blast, height diff {fe.lastPos.y - b.at.y:0.00} m ({dSim:0.00} m from the sim ground point{hit}); ");
                }
                int whole = 0, cut = 0;
                foreach (var kv in flightEnd) { var f = kv.Value; if (!f.ease || f.firstAge > 0.05f) continue; whole++; if (EndedOnLongFrame(f)) { longAll++; pl.Append($"#{kv.Key} ended on a long frame ({frameDt[f.lastFrame + 1] * 1000f:0} ms); "); continue; } if (f.life - f.lastAge > 0.04f) { cut++; pl.Append($"#{kv.Key} CUT {(f.life - f.lastAge) * 1000f:0} ms before its end; "); } }
                cutAll += cut; wholeAll += whole; pairsAll += pairs;
                bool ok = ratio > 0.5f && ratio < 0.8f && pairs >= 1 && unpaired == 0 && badGap == 0 && badRemain == 0 && far == 0 && cut == 0 && quiet;
                allOk &= ok; checkedN++;
                Mark($"E {tag}: {(ok ? "OK" : "FAIL")} - {pl}");
                res.Append($"{tag} ({best[0].w}): out of the tube {early:0.0} vs cruise {late:0.0} m/s = {ratio:0.00}; {pairs} drawn blasts paired ({unpaired} unpaired, {hitchN} on a long frame), worst: blast {worstGap} frame(s) after the last rocket frame, last frame {worstRemain * 1000f:0} ms before its end, {worstD:0.00} m from the drawn blast; {whole} whole flights traced, {cut} cut; ");
                staged.Remove(target); Clear(); staged.Add(target); yield return Wait(1f);
            }
            if (dn) { dn.timeOfDay = 0.08f; dn.Apply(0.08f); }
            Mark("E: " + res);
            Verdict("E1 rockets ease and land on the drawn blast", checkedN == legsN && allOk, res.ToString());
            Verdict("E3 no traced flight cut", cutAll == 0 && wholeAll > 0, $"{wholeAll} whole eased flights traced, {cutAll} cut before their end, {longAll} ended on a long (hitch) frame that carried them past their end, {pairsAll} drawn blasts paired");
            Verdict("E2 DV52 speeds live", ZWorld.RocketSpeedDeviation && W.S.Unit("tough").attack_missile_speed == 150,
                $"tough {ZWorld.RocketSpeed("tough", 150)} px/s, missile cannon {ZWorld.RocketSpeed("missile_cannon", 128)} px/s, MLRS {ZWorld.RocketSpeed("missile_launcher", 70)} px/s (Zod 150 / 128 / 70)");
        }
        int FirstKey(List<(float age, float life, Vector3 pos, FxWeapon w, bool ease)> l) { foreach (var kv in proj) if (kv.Value == l) return kv.Key; return int.MaxValue; }

        void Finish(string why)
        {
            if (done) return; done = true;
            try
            {
                Time.captureFramerate = 0;
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                var d = dir ?? Dir; Directory.CreateDirectory(d);
                File.WriteAllText(Path.Combine(d, "proof_log.txt"), log.ToString());
                File.WriteAllText(Path.Combine(d, "impulses.csv"), csvImp.ToString());
                File.WriteAllText(Path.Combine(d, "projectiles.csv"), csvProj.ToString());
                File.WriteAllText(Path.Combine(d, "blasts.csv"), csvBlast.ToString());
            }
            catch (Exception e) { Debug.LogError("[cfeel] finish: " + e); }
            OpenZFxPlayer.ProjTrace = null;
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
