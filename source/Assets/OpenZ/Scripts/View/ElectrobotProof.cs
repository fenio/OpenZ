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
    /// Real sim units and orders, the full look at the Game view size set before Play (2560x1600):
    ///  A. RobotsV3DetailLineup by day (the Electrobot next to grunt / psycho / tough / sniper / pyro / laser, red + blue rows).
    ///  B. The red fort's production window (the ELECTROBOT tile after LASER), then a real build of him (the factory timer is brought
    ///     forward to 4 s, harness only), the deploy strike frames, then him selected (unit panel: portrait, TESLA GATLING, glyph).
    ///  C. Close idle frames by day (red + blue).
    ///  D. Fights: vs a blue Tough (day), a blue light tank (night), a blue gun (night): windup, the bolt, a 60 fps crop strip of the
    ///     bolt and of the hit shake (Time.captureFramerate 60), stun frames at 0 / 7 / 14 s after the 2nd bolt; the target gets a move
    ///     order at that bolt and must hold its tile until the stun ends, and must keep firing at red grunts parked next to it.
    ///  E. Night idle, a bolt kill on a blue grunt (ragdoll), his own death (coil blow-out).
    ///  F. RobotsV3DetailLineup at night.
    /// Heat: stops at 86 C held 30 s or 90 C at once (GpuGuard temperature).
    /// </summary>
    public class ElectrobotProof : MonoBehaviour
    {
        static readonly string Root = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../.."));
        string job, Dir; float lastMark; bool done; int errors; float hot = -1f, lastSample = -1f; int peak = -1;
        readonly StringBuilder log = new StringBuilder();
        readonly List<string> verdicts = new List<string>();
        public static bool SkipLineups;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Auto()
        {
            string job = PlayerPrefs.GetString("oz_ebproof_auto", "");
            if (string.IsNullOrEmpty(job)) return;
            PlayerPrefs.DeleteKey("oz_ebproof_auto"); PlayerPrefs.Save();
            var go = new GameObject("ElectrobotProof"); DontDestroyOnLoad(go);
            go.AddComponent<ElectrobotProof>().job = job;
        }

        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        float T => (float)clock.Elapsed.TotalSeconds;
        void Mark(string s) { lastMark = T; log.AppendLine($"[{T,7:0.00}] {s}"); Debug.Log("[ebproof] " + s); }
        void Verdict(string step, bool ok, string what) { string v = $"{step}: {(ok ? "PASS" : "FAIL")} - {what}"; verdicts.Add(v); Mark("VERDICT " + v); }
        IEnumerator Wait(float s) { float end = T + s; while (T < end && !done) yield return null; }
        void OnEnable() { Application.logMessageReceived += OnLog; }
        void OnDisable() { Application.logMessageReceived -= OnLog; if (!done) Finish("disabled"); }
        void OnLog(string c, string st, LogType type)
        {
            if (c.StartsWith("[ebproof]")) return;
            if (type == LogType.Error || type == LogType.Exception) { errors++; log.AppendLine($"[{T,7:0.00}] ERROR {c.Split('\n')[0]}"); }
        }

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

        ZGame g; ZWorld W; RtsCamera cam; DayNight dn; Camera ucam; PlayerInput input;
        static void GameViewSize(int idx)
        {
            var gvt = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
            var p = gvt?.GetProperty("selectedSizeIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p == null) return;
            foreach (var w in Resources.FindObjectsOfTypeAll(gvt)) p.SetValue(w, idx);
        }
        static readonly MethodInfo setSel = typeof(PlayerInput).GetProperty("SelectTime").GetSetMethod(true);
        void Select(ZObject u) { input.Selection.Clear(); if (u != null) input.Selection.Add(u); setSel.Invoke(input, new object[] { Time.unscaledTime }); AudioDirector.DebugClickFrame = Time.frameCount; }

        /// <summary>Focus the camera so that tile point (px, py) sits left of the HUD (the selection panel + minimap cover the right).</summary>
        void Focus(float px, float py, float h, float hudShiftTiles = 0f)
        {
            var p = ZGame.PxToWorld((int)(px + hudShiftTiles * 16), (int)py); cam.focus = new Vector3(p.x, 0f, p.z); cam.SetHeightImmediate(h);
        }
        IEnumerator SkipBriefing()
        {
            float w0 = T; bool aborted = false; var pf = typeof(ZodBriefing).GetField("perf", BindingFlags.NonPublic | BindingFlags.Instance);
            double sim0 = W.Time;
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
                yield return null;
            }
            Mark($"briefing over after {T - w0:0.0} s");
        }

        // ---------------------------------------------------------------- frames
        IEnumerator Shot(string name, float settle = 0.4f)
        {
            yield return Wait(settle);
            string f = Path.Combine(Dir, name + ".png");
            ScreenCapture.CaptureScreenshot(f); yield return null; yield return null;
            Mark($"frame {name}.png {Screen.width}x{Screen.height} cam {cam.height:0} m, sim t {W.Time:0.00}");
        }
        /// <summary>A crop of this frame around a world point (for the 60 fps strips; small files, no full-frame PNG per frame).</summary>
        IEnumerator Crop(string name, Vector3 world, int w, int h)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            try
            {
                var s = ucam.WorldToScreenPoint(world);
                w = Mathf.Min(w, tex.width); h = Mathf.Min(h, tex.height);
                int x0 = Mathf.Clamp(Mathf.RoundToInt(s.x) - w / 2, 0, tex.width - w), y0 = Mathf.Clamp(Mathf.RoundToInt(s.y) - h / 2, 0, tex.height - h);
                var c = new Texture2D(w, h, TextureFormat.RGB24, false);
                c.SetPixels(tex.GetPixels(x0, y0, w, h)); c.Apply();
                File.WriteAllBytes(Path.Combine(Dir, name + ".png"), c.EncodeToPNG());
                Destroy(c);
            }
            finally { Destroy(tex); }
        }

        // ---------------------------------------------------------------- units
        readonly List<ZObject> staged = new List<ZObject>();
        static void Tough(ZObject o) { o.MaxHealth = 1000000000; o.SetHealth(o.MaxHealth); }
        ZObject Squad(string type, Team t, int tx0, int ty0, bool tough = true)
        {
            OzCheatSpawner.FreeTile(W, tx0, ty0, 1, true, out int tx, out int ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t); g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            staged.Add(o); staged.AddRange(o.Minions);
            if (tough) { Tough(o); foreach (var m in o.Minions) Tough(m); }
            return o;
        }
        ZObject Machine(string cat, string type, Team t, int tx0, int ty0)
        {
            OzCheatSpawner.FreeTile(W, tx0, ty0, 2, false, out int tx, out int ty);
            var o = W.CreateObject(cat, type, tx * 16, ty * 16, t); g.SpawnUnitView(o); staged.Add(o); Tough(o); return o;
        }
        void Remove(ZObject o)
        {   // harness clean-up: gone, not dead (no death FX)
            if (o == null) return;
            if (W.Get(o.Id) == o && !o.Destroyed) o.KillAt = W.Time;
            if (g.Views.TryGetValue(o.Id, out var v) && v) v.Hide();
        }
        void ClearStage() { foreach (var o in staged) Remove(o); staged.Clear(); }
        bool Order(Team t, ZObject who, ZObject target) =>
            W.SendWaypoints(t, who, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Attack, refId = target.Id, x = target.CenterX, y = target.CenterY } });
        bool Move(Team t, ZObject who, int px, int py) =>
            W.SendWaypoints(t, who, new List<ZWaypoint> { new ZWaypoint { mode = WpMode.Move, refId = -1, x = px, y = py } });
        UnitView View(ZObject o) => o != null && g.Views.TryGetValue(o.Id, out var v) ? v : null;
        Vector3 Pos(ZObject o) { var v = View(o); return v ? v.transform.position + Vector3.up * 1.2f : ZGame.PxToWorld(o.CenterX, o.CenterY); }
        IEnumerator TimeOfDay(float tod) { dn.running = false; NightLights.JumpTo(tod); yield return Wait(1.5f); }

        // ---------------------------------------------------------------- the run
        float t0;
        IEnumerator Start()
        {
            Dir = Path.Combine(Root, "work/records/milestones/electrobot", job); Directory.CreateDirectory(Dir); lastMark = T; t0 = Time.unscaledTime;
            Mark($"electrobot_v1 Play proof {job} {DateTime.Now:yyyy-MM-dd HH:mm:ss}, scene {SceneManager.GetActiveScene().name}, oz_testmode {PlayerPrefs.GetInt("oz_testmode", 0)}, screen {Screen.width}x{Screen.height}");
            StartCoroutine(Heat());
            float w0 = T;
            while ((!ZGame.I || ZGame.I.World == null) && T - w0 < 90f) yield return null;
            if (!ZGame.I || ZGame.I.World == null) { Finish("the level never came up within 90 s"); yield break; }
            g = ZGame.I; W = g.World; cam = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>(); ucam = cam.GetComponent<Camera>(); if (!ucam) ucam = Camera.main;
            input = FindFirstObjectByType<PlayerInput>();
            float h0 = cam.height;
            Mark($"level up after {T - w0:0.0} s, level {g.level}, default RTS height {h0:0.0} m, pitch {cam.pitch:0}; DV45 {ZWorld.ElectrobotDeviation}, DV46 {ZWorld.ElectrobotBotDeviation}, all levels {ZWorld.ElectrobotAllLevels}");
            int bots = W.Bots.Count; W.Bots.Clear();
            yield return SkipBriefing();
            if (done) yield break;
            dn.running = false;
            Mark($"CPU bots cleared ({bots}); day/night clock stopped");
            yield return TimeOfDay(0.08f);
            if (job.StartsWith("size")) { yield return SizeRun(h0); ClearStage(); Finish(null); yield break; }

            ZObject fort = null; foreach (var o in W.Objects) if (o.IsFort && o.Owner == Team.Red && !o.Destroyed) { fort = o; break; }

            // A. lineup by day (next to the camera focus: put it by the red fort's side of the map, away from the fight field)
            if (!SkipLineups && fort != null)
            {
                Focus(fort.CenterX, fort.CenterY - 16 * 12, h0);
                yield return Lineup(false);
                if (done) yield break;
            }

            // B. production window + a real build
            var hud = OpenZHud.I;
            if (fort != null && hud && hud.Production)
            {
                var list = ZBuildLists.For(fort.Type, fort.BuildLevel);
                int il = IndexOf(list, "laser"), ie = IndexOf(list, ZElectro.Type);
                Verdict("V6_build_list", ie >= 0 && (il < 0 || ie == il + 1), $"{fort.Type} level {fort.BuildLevel}: [{string.Join(", ", list)}]");
                Focus(fort.CenterX, fort.CenterY, 45f, 4f);
                hud.Production.Open(fort);
                yield return Shot("V6_production_window", 1.2f);
                var before = new HashSet<int>(); foreach (var o in W.Objects) if (o.IsElectrobot) before.Add(o.Id);
                if (fort.Building) W.StopBuilding(Team.Red, fort);
                bool st = W.StartBuilding(Team.Red, fort, ZElectro.Type);
                double dur = fort.BFinal - fort.BInit;
                Mark($"production: StartBuilding electrobot {st}, Zod build time here {dur:0.0} s (harness: brought forward to finish in 4 s)");
                yield return Shot("V6_production_window_producing", 0.8f);
                hud.Production.Close();
                fort.BInit = W.Time - dur + 4.0; fort.BFinal = W.Time + 4.0;
                float pw = T; ZObject made = null; float ts = Time.unscaledTime;
                while (T - pw < 15f && made == null && !done)
                {
                    foreach (var o in W.Objects) if (o.IsElectrobot && o.Owner == Team.Red && !before.Contains(o.Id)) { made = o; break; }
                    yield return null;
                }
                Verdict("V6_built", made != null, made != null ? $"#{made.Id} out of the fort after {T - pw:0.0} s wall" : "no Electrobot came out within 15 s");
                if (made != null)
                {
                    for (int i = 0; i < 8 && !done; i++)
                    {   // the deploy strike: 8 frames 0.12 s apart
                        string f = Path.Combine(Dir, $"deploy_{i:00}.png"); ScreenCapture.CaptureScreenshot(f);
                        yield return Wait(0.12f);
                    }
                    Mark("deploy frames deploy_00..07");
                    yield return Wait(3.0f);   // Rachel + his arrival line (~2.2 s)
                    var es = OzSoundLog.Since(ts); var keys = new List<string>();
                    foreach (var e in es) if (e.outcome != null && e.outcome.StartsWith("PLAY")) keys.Add(e.key);
                    Verdict("V6_deploy_sounds", keys.Contains("electro_deploy"), $"played after the build: [{string.Join(", ", keys)}]");
                    Select(made); Focus(made.CenterX, made.CenterY, 32f, 4f);
                    yield return Shot("V6_unit_panel", 1.2f);
                    Select(null);
                    Remove(made);
                }
                if (fort.Building) W.StopBuilding(Team.Red, fort);
            }
            else Verdict("V6_build_list", false, "no red fort / HUD production window");
            if (done) yield break;

            // the fight field: L17's clean 13 x 9 window (92,57) (HeavyLegacyProof), L02's (20,37)
            int ax = g.level == "L02" ? 20 : 92, ay = g.level == "L02" ? 37 : 57;
            // C. close idle frames by day, red + blue
            var eRed = Squad(ZElectro.Type, Team.Red, ax - 3, ay);
            var eBlue = Squad(ZElectro.Type, Team.Blue, ax + 3, ay);
            eRed.AttackRadius = 0; eBlue.AttackRadius = 0;
            // a row next to him for the size read (V2): grunt, tough, laser (slightly taller, not too big)
            var gSize = Squad("grunt", Team.Red, ax - 9, ay); foreach (var m in gSize.Minions) Remove(m); gSize.AttackRadius = 0;
            var tSize = Squad("tough", Team.Red, ax - 7, ay); foreach (var m in tSize.Minions) Remove(m); tSize.AttackRadius = 0;
            var lSize = Squad("laser", Team.Red, ax - 5, ay); foreach (var m in lSize.Minions) Remove(m); lSize.AttackRadius = 0;
            yield return Wait(1.5f);
            Focus(ax * 16 - 6 * 16, ay * 16, h0, 5f); yield return Shot("V2_row_rts_day", 0.6f);
            Focus(ax * 16 - 6 * 16, ay * 16, 22f, 2.5f); yield return Shot("V2_row_close_day", 0.8f);
            float hE = HeadHeight(eRed), hG = HeadHeight(gSize), hT = HeadHeight(tSize);
            var mE = Measure(eRed); var mG = Measure(gSize);
            Verdict("V2_size_info", true, $"geometry top (live pose): Electrobot body {mE.body:0.000} m, grunt body {mG.body:0.000} m -> {mE.body / Mathf.Max(0.01f, mG.body):0.000} x; head bone (info only, not the silhouette top): Electrobot {hE:0.00} m, grunt {hG:0.00} m, tough {hT:0.00} m (the full band check is the size* run)");
            Remove(gSize); Remove(tSize); Remove(lSize); staged.Remove(gSize); staged.Remove(tSize); staged.Remove(lSize);
            Focus(eRed.CenterX, eRed.CenterY, 18f, 2.5f); yield return Shot("V1_close_red_day_a", 1.0f); yield return Shot("V1_close_red_day_b", 1.3f);
            Focus(eBlue.CenterX, eBlue.CenterY, 18f, 2.5f); yield return Shot("V1_close_blue_day_a", 1.0f); yield return Shot("V1_close_blue_day_b", 1.3f);
            Focus((eRed.CenterX + eBlue.CenterX) / 2, eRed.CenterY, h0, 9f); yield return Shot("idle_rts_day", 0.8f);
            Remove(eBlue); staged.Remove(eBlue);
            if (done) yield break;

            // D. fights
            yield return Fight(eRed, "robot", ax, ay, false);
            if (done) yield break;
            yield return TimeOfDay(0.62f);
            Focus(eRed.CenterX, eRed.CenterY, 18f, 2.5f); yield return Shot("idle_night_a", 1.0f); yield return Shot("idle_night_b", 1.3f);
            yield return Fight(eRed, "tank", ax, ay, true);
            if (done) yield break;
            yield return Fight(eRed, "gun", ax, ay, true);
            if (done) yield break;

            // E. a bolt kill (ragdoll) and his own death, at night
            yield return Deaths(eRed, ax, ay);
            if (done) yield break;
            ClearStage();

            // F. the night lineup
            if (!SkipLineups && fort != null)
            {
                Focus(fort.CenterX, fort.CenterY - 16 * 12, h0);
                yield return Lineup(true);
            }
            Finish(null);
        }

        float HeadHeight(ZObject o)
        {
            var v = View(o); if (!v) return -1f;
            var an = v.GetComponentInChildren<Animator>(); var hd = an && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Head) : null;
            return hd ? hd.position.y - v.transform.position.y : -1f;
        }

        // PlayerPrefs oz_ebproof_auto = "size..." runs only this: grunt, tough, laser, Electrobot (red leaders, no minions) in one tile
        // row of the clean window; live geometry tops over 3 s; frames at the build size with the RTS camera, day + night, and a
        // ruler (bars in the row's own plane at the grunt's body top x1.00 / x1.10 / x1.15 and at the Electrobot's measured top).
        struct Tops { public float body, vis, head, feet; public string part; }
        Mesh bakeTmp;
        /// <summary>Geometry tops above the unit view's root (world m) in the live pose: the skinned BS01_Body baked as it is drawn (the
        /// package's Blender scale test measures the body mesh only), every mesh under the Model (kit pieces, weapon: the visible
        /// top) and the head bone (info only: it is not the silhouette top and it swings with the hold).</summary>
        Tops Measure(ZObject o)
        {
            var r = new Tops { body = -1f, vis = -1f, head = -1f, feet = 99f, part = "-" };
            var v = View(o); if (!v) return r;
            var an = v.GetComponentInChildren<Animator>(); if (!an) return r;
            if (!bakeTmp) bakeTmp = new Mesh();
            float y0 = v.transform.position.y;
            foreach (var rd in an.GetComponentsInChildren<Renderer>(false))
            {
                if (!rd.enabled) continue;
                var smr = rd as SkinnedMeshRenderer; Vector3[] vs = null;
                if (smr) { smr.BakeMesh(bakeTmp, true); vs = bakeTmp.vertices; }
                else if (rd is MeshRenderer)
                {   // kit pieces / weapons are not Read/Write in Play: their world bounds (a MeshRenderer's AABB is tight to its mesh)
                    var mf = rd.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
                    if (!mf.sharedMesh.isReadable) { float bt = rd.bounds.max.y - y0; if (bt > r.vis) { r.vis = bt; r.part = rd.name; } continue; }
                    vs = mf.sharedMesh.vertices;
                }
                if (vs == null || vs.Length == 0) continue;
                var M = rd.transform.localToWorldMatrix; float hi = float.NegativeInfinity, lo = float.PositiveInfinity;
                for (int i = 0; i < vs.Length; i++) { float y = M.MultiplyPoint3x4(vs[i]).y; if (y > hi) hi = y; if (y < lo) lo = y; }
                hi -= y0; lo -= y0;
                if (smr && rd.name == "BS01_Body") { r.body = hi; r.feet = lo; }
                if (hi > r.vis) { r.vis = hi; r.part = rd.name; }
            }
            var hd = an.isHuman ? an.GetBoneTransform(HumanBodyBones.Head) : null; if (hd) r.head = hd.position.y - y0;
            return r;
        }
        class Acc
        {
            public float bMin = 99, bMax = -99, bSum, vMin = 99, vMax = -99, vSum, hMin = 99, hMax = -99, fSum; public int n; public string part = "-";
            public void Add(Tops t)
            {
                if (t.body < 0f) return; n++;
                bMin = Mathf.Min(bMin, t.body); bMax = Mathf.Max(bMax, t.body); bSum += t.body;
                vMin = Mathf.Min(vMin, t.vis); vMax = Mathf.Max(vMax, t.vis); vSum += t.vis; part = t.part;
                hMin = Mathf.Min(hMin, t.head); hMax = Mathf.Max(hMax, t.head); fSum += t.feet;
            }
            public float B => n > 0 ? bSum / n : -1f;
            public float V => n > 0 ? vSum / n : -1f;
            public string Line(string k) => $"{k}: body top {B:0.000} m ({bMin:0.000}-{bMax:0.000}), visible top {V:0.000} m ({vMin:0.000}-{vMax:0.000}, top part {part}), head bone {hMin:0.000}-{hMax:0.000} m, body bottom {(n > 0 ? fSum / n : 0f):0.000} m, {n} samples";
        }
        GameObject Bar(string name, Vector3 a, Vector3 b, float y, Color c)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; Destroy(go.GetComponent<Collider>());
            var d = b - a; d.y = 0f; var mid = (a + b) * 0.5f; mid.y = y;
            go.transform.position = mid; go.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
            go.transform.localScale = new Vector3(0.04f, 0.04f, d.magnitude);
            var mr = go.GetComponent<MeshRenderer>(); var m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); m.SetColor("_BaseColor", c);
            mr.sharedMaterial = m; mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;
            return go;
        }
        IEnumerator SizeRun(float h0)
        {
            int ax = g.level == "L02" ? 20 : 92, ay = g.level == "L02" ? 37 : 57;
            string[] kinds = { "grunt", "tough", "laser", ZElectro.Type };
            var units = new ZObject[kinds.Length]; var acc = new Acc[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                int want = ax - 3 + 2 * i;
                units[i] = Squad(kinds[i], Team.Red, want, ay); foreach (var m in units[i].Minions) Remove(m); units[i].AttackRadius = 0; acc[i] = new Acc();
                units[i].PlayerHold = true;   // run size1: idle leaders auto-grabbed a flag / crate (Zod idle grab) and walked off; hold = a player order (DV38)
                Mark($"row {kinds[i]} #{units[i].Id}: asked tile ({want},{ay}), got ({units[i].CenterX / 16},{units[i].CenterY / 16})");
            }
            bool inRow = true; for (int i = 0; i < kinds.Length; i++) inRow &= units[i].CenterY / 16 == ay && units[i].CenterX / 16 == ax - 3 + 2 * i;
            Verdict("S0_row", inRow, "grunt / tough / laser / Electrobot leaders on tiles " + (ax - 3) + ".." + (ax + 3) + " step 2, row " + ay + " (one plane for the ruler)");
            yield return Wait(2.0f);
            for (int i = 0; i < kinds.Length; i++) { var v = View(units[i]); Mark($"view {kinds[i]} root {(v ? v.transform.position.ToString("F3") : "-")}"); }
            float s0 = T;
            while (T - s0 < 3.0f && !done) { for (int i = 0; i < kinds.Length; i++) acc[i].Add(Measure(units[i])); yield return Wait(0.1f); }
            for (int i = 0; i < kinds.Length; i++) Mark("SIZE " + acc[i].Line(kinds[i]));
            Acc G = acc[0], To = acc[1], L = acc[2], E = acc[3];
            float rb = E.B / G.B, rv = E.V / G.V;
            Verdict("S1_band_body", rb >= 1.10f && rb <= 1.15f, $"Electrobot body top {E.B:0.000} m / grunt body top {G.B:0.000} m = {rb:0.000} x (the package's method and band 1.10-1.15; its Blender scale test 2.629 / 2.309 = 1.139 x)");
            Verdict("S2_matches_package", Mathf.Abs(E.B - 2.629f) <= 0.04f, $"Electrobot body top in his live hold {E.B:0.000} m vs the package's calibrated 2.629 m ({(E.B / 2.629f - 1f) * 100f:+0.0;-0.0} %)");
            Verdict("S3_tallest", E.B > To.B && E.B > L.B, $"the tallest of the family (body): vs tough {E.B / To.B:0.000} x, vs laser {E.B / L.B:0.000} x (body); visible tops (grunt with his kit head {G.V:0.000} m, part {G.part}): {rv:0.000} x the grunt, {E.V / To.V:0.000} x the tough, {E.V / L.V:0.000} x the laser");
            Verdict("S4_head_bone_info", true, $"head bone (not the top; the Electrobot's head sits low between the coils): Electrobot {E.hMin:0.000}-{E.hMax:0.000} m, grunt {G.hMin:0.000}-{G.hMax:0.000} m, tough {To.hMin:0.000}-{To.hMax:0.000} m, laser {L.hMin:0.000}-{L.hMax:0.000} m");

            // frame + ruler on the planned tiles (run size1 framed on the grunt's live position after he had walked to a flag)
            float cx = ax * 16 + 8, cy = ay * 16 + 8;
            var vg = View(units[0]);
            Vector3 a = ZGame.PxToWorld((ax - 3) * 16 + 8, cy), b = ZGame.PxToWorld((ax + 3) * 16 + 8, cy);
            var dir = (b - a); dir.y = 0f; dir = dir.normalized; a -= dir * 1.3f; b += dir * 1.3f;
            float gy = vg ? vg.transform.position.y : 0f;
            var mid = (a + b) * 0.5f + Vector3.up * (gy + 1.4f);
            Focus(cx, cy, h0, 5f); yield return Shot("S1_row_rts_day", 0.6f);
            Focus(cx, cy, 22f, 2.5f); yield return Shot("S2_row_22m_day", 0.8f); yield return Crop("S2_row_22m_day_crop", mid, 1900, 850);
            var bars = new List<GameObject> {
                Bar("ruler_100", a, b, gy + G.B, new Color(1f, 1f, 1f)),
                Bar("ruler_110", a, b, gy + G.B * 1.10f, new Color(1f, 0.62f, 0.12f)),
                Bar("ruler_115", a, b, gy + G.B * 1.15f, new Color(1f, 0.62f, 0.12f)),
                Bar("ruler_eb", a, b, gy + E.B, new Color(0.2f, 0.75f, 1f)) };
            yield return Shot("S3_ruler_22m_day", 0.4f); yield return Crop("S3_ruler_22m_day_crop", mid, 1900, 850);
            foreach (var o in bars) Destroy(o);
            for (int i = 0; i < kinds.Length; i++) Mark($"after the frames {kinds[i]} on tile ({units[i].CenterX / 16},{units[i].CenterY / 16})");

            // live body top, at the end of the same row: Model scale x kB for this one view
            var eB = Squad(ZElectro.Type, Team.Red, ax + 5, ay); foreach (var m in eB.Minions) Remove(m); eB.AttackRadius = 0; eB.PlayerHold = true;
            yield return Wait(1.5f);
            float kB = 1.15f * G.B / E.B; var vB = View(eB); var anB = vB ? vB.GetComponentInChildren<Animator>() : null;
            if (anB) anB.transform.localScale *= kB;
            yield return Wait(0.5f);
            var B = new Acc(); float sb0 = T; while (T - sb0 < 1.5f && !done) { B.Add(Measure(eB)); yield return Wait(0.1f); }
            Mark("SIZE " + B.Line("electrobot_B"));
            Verdict("S5_variantB_info", true, $"variant B (Model scale x{kB:0.0000} = {1.0297053f * kB:0.0000}): body top {B.B:0.000} m = {B.B / G.B:0.000} x the grunt, {B.B / To.B:0.000} x the tough, {B.B / L.B:0.000} x the laser (tile ({eB.CenterX / 16},{eB.CenterY / 16}))");
            Vector3 b2 = ZGame.PxToWorld((ax + 5) * 16 + 8, cy) + dir * 1.3f; var mid2 = (a + b2) * 0.5f + Vector3.up * (gy + 1.4f);
            var bars2 = new List<GameObject> {
                Bar("ruler_100", a, b2, gy + G.B, new Color(1f, 1f, 1f)),
                Bar("ruler_110", a, b2, gy + G.B * 1.10f, new Color(1f, 0.62f, 0.12f)),
                Bar("ruler_115", a, b2, gy + G.B * 1.15f, new Color(1f, 0.62f, 0.12f)),
                Bar("ruler_eb", a, b2, gy + E.B, new Color(0.2f, 0.75f, 1f)) };
            Focus((ax + 1) * 16 + 8, cy, 26f, 3.5f); yield return Shot("S7_variantB_ruler_26m_day", 0.8f); yield return Crop("S7_variantB_ruler_26m_day_crop", mid2, 2300, 900);
            foreach (var o in bars2) Destroy(o);
            yield return Shot("S7b_variantB_26m_day", 0.3f); yield return Crop("S7b_variantB_26m_day_crop", mid2, 2300, 900);
            Focus((ax + 1) * 16 + 8, cy, h0, 5f); yield return Shot("S8_variantB_rts_day", 0.6f);
            Remove(eB); staged.Remove(eB);
            if (done) yield break;
            yield return TimeOfDay(0.62f);
            Focus(cx, cy, 22f, 2.5f); yield return Shot("S5_row_22m_night", 1.0f); yield return Crop("S5_row_22m_night_crop", mid, 1900, 850);
            Focus(cx, cy, h0, 5f); yield return Shot("S6_row_rts_night", 0.6f);
            if (bakeTmp) Destroy(bakeTmp);
        }

        static int IndexOf(IReadOnlyList<string> l, string s) { for (int i = 0; i < l.Count; i++) if (l[i] == s) return i; return -1; }

        IEnumerator Lineup(bool night)
        {
            RobotsV3DetailLineup.Begin(night);
            float w0 = T; while (!RobotsV3DetailLineup.Running && T - w0 < 3f && !done) yield return null;
            w0 = T; while (RobotsV3DetailLineup.Running && T - w0 < 40f && !done) { lastMark = T; yield return null; }
            Verdict("V2_lineup_" + (night ? "night" : "day"), !RobotsV3DetailLineup.Running, "frames milestones/robots_v3_detail_lineup_" + g.level + "_" + (night ? "night" : "day") + "_*.png");
            dn.running = false;
        }

        IEnumerator Fight(ZObject e, string kind, int ax, int ay, bool night)
        {
            string tn = night ? "night" : "day";
            // the Electrobot at (ax-3, ay); the target 8 tiles east (128 px, inside his 144 px); red grunts 2 tiles past the target
            ZObject target = kind == "robot" ? Squad("tough", Team.Blue, ax + 5, ay)
                           : kind == "tank" ? Machine("vehicle", "light", Team.Blue, ax + 5, ay)
                           : Machine("cannon", "gun", Team.Blue, ax + 5, ay);
            if (kind == "robot") foreach (var m in target.Minions) { Remove(m); }
            var grunts = Squad("grunt", Team.Red, ax + 6, ay + 2);
            grunts.Damage = 0; foreach (var m in grunts.Minions) m.Damage = 0;
            yield return Wait(1.2f);
            int d = (int)Mathf.Sqrt(Mathf.Pow(target.CenterX - e.CenterX, 2) + Mathf.Pow(target.CenterY - e.CenterY, 2));
            e.AttackRadius = e.U != null ? e.U.attack_radius : 144;
            int bolts0 = e.ElBolts, gH0 = grunts.Health; foreach (var m in grunts.Minions) gH0 += m.Health;
            float fx = (e.CenterX + target.CenterX) * 0.5f, fy = (e.CenterY + target.CenterY) * 0.5f;
            Focus(fx, fy, 30f, 3f);
            yield return Wait(0.6f);
            bool ok = Order(Team.Red, e, target);
            Mark($"{kind} ({tn}): Electrobot #{e.Id} ({e.CenterX >> 4},{e.CenterY >> 4}) -> {target.Type} #{target.Id} ({target.CenterX >> 4},{target.CenterY >> 4}), {d} px; red grunts #{grunts.Id}; order {ok}");
            // windup frame, then the bolt strip at 60 fps (captureFramerate: sim + view advance exactly 1/60 s per frame)
            float w0 = T;
            while (e.ElPhase != ElectroPhase.Windup && e.ElBolts == bolts0 && T - w0 < 6f && !done) yield return null;
            Time.captureFramerate = 60;
            try
            {
                int i = 0;
                // windup -> release -> ~0.5 s after the release
                long relTick = -1;
                while (i < 90 && !done)
                {
                    if (relTick < 0 && e.ElBolts > bolts0) relTick = W.TickIndex;
                    if (relTick >= 0 && W.TickIndex - relTick > 50) break;
                    if (i == 6 || (relTick >= 0 && W.TickIndex - relTick <= 30) || (relTick >= 0 && W.TickIndex - relTick > 30 && i % 3 == 0))
                    {
                        if (i == 6) { string f = Path.Combine(Dir, $"V3_windup_{kind}_{tn}.png"); ScreenCapture.CaptureScreenshot(f); }
                        else yield return Crop($"strip_bolt_{kind}_{tn}_{i:00}_t{(relTick >= 0 ? W.TickIndex - relTick : -1)}", new Vector3((Pos(e).x + Pos(target).x) * 0.5f, Pos(e).y, (Pos(e).z + Pos(target).z) * 0.5f), 1400, 800);
                    }
                    if (relTick >= 0 && W.TickIndex - relTick >= 6 && W.TickIndex - relTick < 8) { string f = Path.Combine(Dir, $"V3_bolt_{kind}_{tn}.png"); ScreenCapture.CaptureScreenshot(f); }
                    i++; yield return null;
                }
                Mark($"{kind}: bolt 1 at sim tick {relTick}, strip frames {i} (1/60 s each)");
            }
            finally { Time.captureFramerate = 0; }
            bool stun1 = target.Stunned; long end1 = target.StunEndTick;
            Mark($"{kind}: after bolt 1 stunned {stun1}, stun end tick {end1}, stuns {target.StunCount}, target HP {target.Health}");
            // bolt 2: refresh (never stacks), then the 60 fps shake strip on the target
            w0 = T; while (e.ElBolts < bolts0 + 2 && T - w0 < 8f && !done) yield return null;
            long b2 = W.TickIndex; long end2 = target.StunEndTick;
            bool refresh = kind == "gun" ? target.StunCount == 0 : end2 > end1 && end2 - b2 <= ZElectro.StunTicks + 2 && end2 - b2 >= ZElectro.StunTicks - 40;
            Verdict($"V4_{kind}_refresh", refresh, kind == "gun" ? $"gun: stuns {target.StunCount} (D5: jolt only)" : $"stun end after bolt 1 {end1}, after bolt 2 {end2} (bolt 2 at {b2}: +{end2 - b2} ticks = {ZElectro.StunTicks} restart, not stacked)");
            // stop him (the stun must now run its full 15 s), and give the target a move order it must not follow while stunned
            e.AttackRadius = 0; Move(Team.Red, e, e.CenterX, e.CenterY);
            int hx = target.CenterX, hy = target.CenterY;
            if (kind != "gun") Move(Team.Blue, target, target.CenterX + 6 * 16, target.CenterY);
            Time.captureFramerate = 60;
            try
            {
                for (int i = 0; i < 36 && !done; i++) yield return Crop($"strip_shake_{kind}_{tn}_{i:00}_t{W.TickIndex - b2}", Pos(target), 560, 560);   // every rendered frame (1/60 s)
            }
            finally { Time.captureFramerate = 0; }
            Focus(target.CenterX, target.CenterY, 24f, 2.5f);
            double s0 = W.Time;
            foreach (var at in new[] { 0.9, 7.0, 14.0 })
            {
                while (W.Time - s0 < at - 0.8 && !done) { lastMark = T; yield return null; }
                if (done) yield break;
                yield return Shot($"V4_stun_{kind}_{(int)at:00}s", 0f);
                Mark($"{kind}: stun +{at:0} s: stunned {target.Stunned}, left {target.StunTicksLeft} ticks, tile ({target.CenterX >> 4},{target.CenterY >> 4}), moved {Mathf.Abs(target.CenterX - hx) + Mathf.Abs(target.CenterY - hy)} px");
            }
            int heldMove = Mathf.Abs(target.CenterX - hx) + Mathf.Abs(target.CenterY - hy);
            int gH1 = grunts.Health; foreach (var m in grunts.Minions) gH1 += m.Health;
            Verdict($"V4_{kind}_fire_back", gH1 < gH0, $"red grunts next to it lost {gH0 - gH1} HP while it was hit / stunned");
            if (kind != "gun")
            {
                while (target.Stunned && W.Time - s0 < 17 && !done) { lastMark = T; yield return null; }
                double endAt = W.Time - s0; yield return Wait(1.5f);
                int after = Mathf.Abs(target.CenterX - hx) + Mathf.Abs(target.CenterY - hy);
                Verdict($"V4_{kind}_held", heldMove <= 2 && after > 4, $"held its tile {heldMove} px over the stun (ended at +{endAt:0.0} s after bolt 2), then moved {after} px on its order");
            }
            else Verdict("V4_gun_jolt", !target.Stunned, "the gun is never frozen (D5), frames show the 1.5 s jolt");
            Remove(target); Remove(grunts); foreach (var m in grunts.Minions) Remove(m);
            staged.Remove(target); staged.Remove(grunts);
            yield return Wait(0.8f);
        }

        IEnumerator Deaths(ZObject e, int ax, int ay)
        {
            var victim = Squad("grunt", Team.Blue, ax + 5, ay, false);
            foreach (var m in victim.Minions) Remove(m);
            yield return Wait(1.0f);
            e.AttackRadius = e.U != null ? e.U.attack_radius : 144;
            Focus((e.CenterX + victim.CenterX) * 0.5f, e.CenterY, 26f, 2.5f);
            yield return Wait(0.5f);
            int b0 = e.ElBolts; Order(Team.Red, e, victim);
            float w0 = T; while (e.ElBolts == b0 && T - w0 < 6f && !done) yield return null;
            for (int i = 0; i < 6 && !done; i++) { ScreenCapture.CaptureScreenshot(Path.Combine(Dir, $"deaths_zap_{i:00}.png")); yield return Wait(0.25f); }
            Verdict("V5_zap_kill", victim.Destroyed || victim.Health <= 0 || W.Get(victim.Id) == null, $"blue grunt HP {victim.Health} after bolt {e.ElBolts - b0}");
            e.AttackRadius = 0;
            // his own death: coil blow-out
            Focus(e.CenterX, e.CenterY, 22f, 2.5f); yield return Wait(0.6f);
            e.SetHealth(0); W.UpdateObjectHealth(e);
            for (int i = 0; i < 8 && !done; i++) { ScreenCapture.CaptureScreenshot(Path.Combine(Dir, $"deaths_self_{i:00}.png")); yield return Wait(0.15f); }
            staged.Remove(e);
            yield return Wait(1.0f);
        }

        void Finish(string why)
        {
            if (done) return; done = true;
            Time.captureFramerate = 0;
            try
            {
                Mark(why == null ? "DONE" : "STOPPED: " + why);
                log.AppendLine($"errors/exceptions in the Console during the run: {errors}; GPU peak (GpuGuard) {peak} C");
                log.AppendLine("## VERDICTS"); foreach (var v in verdicts) log.AppendLine("  " + v);
                log.AppendLine("## OzSoundLog (the whole run)");
                log.Append(OzSoundLog.Format(OzSoundLog.Since(t0)));
                if (OpenZElectroFx.I) { log.AppendLine("## OpenZElectroFx report"); log.AppendLine(OpenZElectroFx.I.Report()); }
                File.WriteAllText(Path.Combine(Dir, "proof_log.txt"), log.ToString());
                GameViewSize(9);
            }
            catch (Exception e) { Debug.LogError("[ebproof] finish: " + e); }
            UnityEditor.EditorApplication.isPlaying = false;
        }
    }
}
#endif
