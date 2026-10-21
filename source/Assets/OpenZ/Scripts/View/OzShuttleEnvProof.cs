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
    /// The K256e Play proof of builder E_env_audio_ui (the shuttle's third round; menu OpenZ/V1.1/Shuttle: K256e Env Proof, Editor/
    /// OzShuttleEnvMenu.cs). Counted PASS / FAIL lines, the WHOLE take's sound log (OzSoundTake: no line cap) with its table per action,
    /// full-screen pictures from the normal RTS camera at the standard 70 m (grabbed before the camera moves; checked), strips from a FIXED
    /// camera as numbered frames and one sheet each; *_extra_close pictures are never the proof.
    /// Mode E (level 16): E1 the trees' downwash bend (M10: a low pass over a kapok, a palm and a small plant; the tip moved in pixels at
    /// 70 m per plant; the downwash numbers of three crowns), E2 the voices (M13: three landings each with Rachel's line, one with a vehicle
    /// finished at the touchdown, a "Can't land here!" asked while Rachel speaks; every Rachel line against every shuttle line: 0
    /// overlaps), E3 the repair station (M8: the crane rolls 5 m away and back in a fixed strip, the hook reeled up, the gap to the dome, 0
    /// ship points in the station and 0 station points in the ship, no ship voice and no Rachel line from the landing to the lift-off,
    /// the hover turn's peak), E4 the parrots (M20: straight through a flying group, a 2x crop strip), E5 the cover cases (S3: shooters
    /// under tree crowns, shooters behind a hut with the ship over the hut), E6 the badges (S5: a badge under the flying hull fades), E7
    /// the ramp by day beside an APC's open door (S2), then the sound table (M15) and the picture hygiene (S7).
    /// Mode P (any level, also level 2; M17): the testing panel open with the shuttle's tile in both columns, a drag half way, a shuttle of
    /// each side dropped (both in the frame), the dragged own ship boarded and unloaded (the ramp, the door, the latch, the crew), no part
    /// of a dropped ship inside a building. Test only.
    /// </summary>
    public sealed class OzShuttleEnvProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static string Folder(string sub = "shuttle_env_proof")
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
                if (System.IO.Directory.Exists(System.IO.Path.Combine(d.FullName, "work", "records")))
                    return System.IO.Path.Combine(d.FullName, "work", "records", "milestones", sub);
            return System.IO.Path.Combine(Application.persistentDataPath, sub);
        }
        /// <summary>The owner's round-1 pictures (for the side-by-side), found upward from the project: null when not there.</summary>
        static string OwnerPicture(string file)
        {
            var d = new System.IO.DirectoryInfo(Application.dataPath);
            for (int i = 0; i < 8 && d != null; i++, d = d.Parent)
            {
                var st = System.IO.Path.Combine(d.FullName, "work", "staging");
                if (!System.IO.Directory.Exists(st)) continue;
                foreach (var stage in System.IO.Directory.GetDirectories(st))
                {
                    var p = System.IO.Path.Combine(stage, "features", "k256b_shuttle_polish", "owner_pictures", file);
                    if (System.IO.File.Exists(p)) return p;
                }
            }
            return null;
        }
        public static void Run(string mode)
        {
            Report = "running " + mode;
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            var go = new GameObject("OzShuttleEnvProof"); go.AddComponent<OzShuttleEnvProof>().mode = mode;
        }

        string mode = "E";
        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; DayNight dn; ZObject s; OzPlayableShuttle ship; Team me = Team.Red, them = Team.Blue;
        readonly StringBuilder sb = new StringBuilder(); int fails, passes; string dir; float T0; OzSoundTake take;
        readonly List<string> shots = new List<string>(); readonly List<ZObject> made = new List<ZObject>();
        Vector3? look; bool follow = true;

        void Start()
        {
            g = ZGame.I; W = g ? g.World : null; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>(); dn = FindFirstObjectByType<DayNight>();
            dir = Folder(mode == "P" ? "shuttle_env_proof_P_" + (g ? g.level : "x") : "shuttle_env_proof");
            System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(All());
        }

        void OnDestroy() { OzPlayableShuttle.ProofRachelEvery = false; if (take) take.End(); }

        // ------------------------------------------------------------------ helpers
        void Log(string t) { sb.AppendLine(t); Debug.Log("[OzShuttleEnvProof] " + t); }
        void Check(string what, bool ok) { if (ok) passes++; else fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        string P(string name) => System.IO.Path.Combine(dir, name + ".png");
        void Shot(string name)
        {
            var path = P(name); ScreenCapture.CaptureScreenshot(path); shots.Add(path);
            Log($"frame {name}.png (camera {(rts ? rts.height : -1f):F1} m)");
            if (name.EndsWith("_h70") && rts && Mathf.Abs(rts.height - 70f) >= 0.5f) Check($"{name}: grabbed at the standard 70 m", false);
        }
        IEnumerator ShotAt(string name, Vector3 at) { look = at; yield return null; yield return null; yield return null; Shot(name); yield return OzProofPictures.AfterGrab(); look = null; }
        IEnumerator Close(string name, float h)
        {   // an extra close picture (never the proof) after the h70 one of this frame is grabbed; the pair checked to differ
            if (!rts) yield break;
            yield return OzProofPictures.AfterGrab();
            rts.SetHeightImmediate(h); yield return null; yield return null; Shot(name + "_extra_close");
            yield return OzProofPictures.AfterGrab();
            rts.SetHeightImmediate(70f); yield return null;
            yield return OzProofPictures.Written(P(name + "_h70")); yield return OzProofPictures.Written(P(name + "_extra_close"));
            float d = OzProofPictures.MeanLumaDiff(P(name + "_h70"), P(name + "_extra_close"));
            Check($"{name}_h70 is the 70 m view (differs from its close twin by {d:F1} / 255, at least {OzProofPictures.TwinMin:F0})", d >= OzProofPictures.TwinMin);
        }
        /// <summary>A fixed-camera strip: n frames every step s (the camera held on `at`), then one sheet.</summary>
        IEnumerator Strip(string name, Vector3 at, int n, float step, System.Action<int> each = null)
        {
            look = at; yield return null; yield return null;
            var fr = new List<string>();
            for (int i = 0; i < n; i++)
            {
                string f = $"{name}_f{i:00}_h70"; Shot(f); fr.Add(P(f)); each?.Invoke(i);
                yield return OzProofPictures.AfterGrab();
                if (i < n - 1) yield return Wait(Mathf.Max(0f, step - Time.deltaTime));
            }
            look = null;
            foreach (var f in fr) yield return OzProofPictures.Written(f);
            bool ok = OzProofPictures.Sheet(fr, Mathf.Min(4, n), 640, System.IO.Path.Combine(dir, name + "_sheet.jpg"));
            Log($"strip {name}: {n} frames every {step:F2} s from a fixed camera, sheet {(ok ? name + "_sheet.jpg" : "NOT MADE")}");
        }
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        IEnumerator Wait(float s0) { float t = 0f; while (t < s0) { t += Time.deltaTime; yield return null; } }
        static void MouseEv(Vector2 p, MouseButton? b = null) { var st = new MouseState { position = p }; if (b.HasValue) st = st.WithButton(b.Value); InputSystem.QueueStateEvent(Mouse.current, st); }
        static void Key1(Key k) { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(k)); }
        static void Key0() { InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState()); }

        bool Move(int x, int y) => Move(s, x, y);
        bool Move(ZObject o, int x, int y) => W.SendWaypoints(me, o, new[] { new ZWaypoint { mode = WpMode.Move, x = x, y = y, playerGiven = true } });
        bool Enter(ZObject sq, ZObject into) => sq != null && into != null && W.SendWaypoints(sq.Owner, sq, new[] { new ZWaypoint { mode = WpMode.Enter, refId = into.Id, x = into.CenterX, y = into.CenterY, playerGiven = true } });
        void Attack(ZObject o) { if (o != null && !o.Destroyed && s != null) W.SendWaypoints(o.Owner, o, new[] { new ZWaypoint { mode = WpMode.Attack, refId = s.Id, x = s.CenterX, y = s.CenterY, attackTo = true } }); }
        ZObject Squad(string type, int tx, int ty, Team t, int amount = -1)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 1, true, out tx, out ty);
            var o = W.CreateRobotGroup(type, tx * 16, ty * 16, t, amount); if (o == null) return null;
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            made.Add(o); return o;
        }
        ZObject Vehicle(string type, int tx, int ty, Team t)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, 2, false, out tx, out ty);
            var o = W.CreateObject("vehicle", type, tx * 16, ty * 16, t); if (o != null) { g.SpawnUnitView(o); made.Add(o); }
            return o;
        }
        void Vanish(ZObject o)
        {
            if (o == null) return;
            var all = new List<ZObject> { o }; all.AddRange(o.Minions);
            foreach (var x in all) { x.KillAt = 0; if (g.Views.TryGetValue(x.Id, out var v)) { g.Views.Remove(x.Id); if (v) Destroy(v.gameObject); } }
        }
        Vector3 PosOf(ZObject o) { var v = o != null && g.Views.TryGetValue(o.Id, out var uv) ? uv : null; return v ? v.transform.position : (o != null ? ZGame.PxToWorld(o.CenterX, o.CenterY) : Vector3.zero); }
        static int Px(float m) => Mathf.RoundToInt(m * 8f);
        int[] FreePadNear(Vector3 near, int minPx, int maxPx, System.Func<int, int, bool> also = null)
        {
            long best = long.MaxValue; int[] r = null; int cx = Mathf.RoundToInt(near.x * 8f), cy = Mathf.RoundToInt(-near.z * 8f);
            for (int ty = 4; ty < W.Map.height - 4; ty++)
                for (int tx = 4; tx < W.Map.width - 4; tx++)
                {
                    int x = tx * 16 + 8, y = ty * 16 + 8; long d = (long)(x - cx) * (x - cx) + (long)(y - cy) * (y - cy);
                    if (d >= best || d < (long)minPx * minPx || d > (long)maxPx * maxPx) continue;
                    if (!W.ShuttlePadFree(x, y, s, out _) || (also != null && !also(x, y))) continue;
                    best = d; r = new[] { x, y };
                }
            return r;
        }
        bool OpenAround(int x, int y, float m)
        {
            var c = ZGame.PxToWorld(x, y);
            foreach (var kv in g.StaticViews) { if (!kv.Value) continue; var p = kv.Value.transform.position; if (new Vector2(p.x - c.x, p.z - c.z).magnitude < m) return false; }
            return true;
        }
        static float ClipLen(string clip)
        {
            if (string.IsNullOrEmpty(clip)) return 1.5f;
            foreach (var c in Resources.FindObjectsOfTypeAll<AudioClip>()) if (c && c.name == clip) return c.length;
            return 1.5f;
        }

        // ------------------------------------------------------------------ per frame
        float yawRate, lastYaw, winYaw0, winT; bool turnRec, winOn;
        void Update()
        {
            if (look.HasValue && rts) rts.focus = new Vector3(look.Value.x, 0f, look.Value.z);
            else if (follow && rts && ship && !ship.BrokenUp) { var p = ship.transform.position; rts.focus = new Vector3(p.x, 0f, p.z); }
            if (turnRec && s != null && ship && s.ShuttleDeckTurning)
            {   // run 27D: the drawn turn rate over windows of 0.25 s of game time (one frame's step over a short frame read 2 x the rate)
                if (!winOn) { winOn = true; winYaw0 = ship.YawDeg; winT = 0f; }
                winT += Time.deltaTime;
                if (winT >= 0.25f) { yawRate = Mathf.Max(yawRate, Mathf.Abs(Mathf.DeltaAngle(winYaw0, ship.YawDeg)) / winT); winYaw0 = ship.YawDeg; winT = 0f; }
            }
            else winOn = false;
            if (ship) lastYaw = ship.YawDeg;
        }

        IEnumerator All()
        {
            T0 = Time.unscaledTime; Application.targetFrameRate = 60;
            take = OzSoundTake.Begin(System.IO.Path.Combine(dir, "sound_take_" + mode + ".txt"));
            if (W == null) { Log("no game"); fails++; Finish(); yield break; }
            W.Bots.Clear();
            if (dn) { dn.timeOfDay = 0.10f; dn.Apply(0.10f); dn.running = false; }
            if (rts) { rts.FreeLookOff(); rts.SetHeightImmediate(70f); }
            if (mode == "P")
            {
                if (W.PlayableShuttle != null) { s = W.PlayableShuttle; ship = OzPlayableShuttle.Of(s); if (ship) me = s.Owner; else s = null; }
                if (s == null) me = ZWorld.ShuttleTeam;
                them = me == Team.Red ? Team.Blue : Team.Red;
                Log($"K256e env proof P (the testing panel), {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name} ({g.level}), {Screen.width}x{Screen.height}");
                yield return Wait(1f); yield return Panel(); Finish(); yield break;
            }
            if (W.PlayableShuttle == null || !OzPlayableShuttle.I) { Log("no playable shuttle (level 16 in Play, the option SHUTTLE on)"); fails++; Finish(); yield break; }
            s = W.PlayableShuttle; ship = OzPlayableShuttle.I; me = s.Owner; them = me == Team.Red ? Team.Blue : Team.Red;
            Log($"K256e env proof E, {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}, {Screen.width}x{Screen.height}, quality {OzQuality.Name}; CPU removed, day 0.10 held, camera 70 m");
            yield return Wait(1f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 30f);
            yield return Trees();
            yield return Voices();
            yield return Station();
            yield return Birds();
            yield return Cover();
            yield return Badges();
            yield return RampByDay();
            Finish();
        }

        // ------------------------------------------------------------------ E1: the trees' downwash bend (M10)
        struct PlantAt { public OzFloraWind.Plant p; public Vector3 foot, top; public float sc, r, h, fade; public string label; }
        PlantAt Of(OzFloraWind fw, OzFloraWind.Plant p, string label)
        {
            var t = p.R.transform; float sc = t.lossyScale.y;
            return new PlantAt { p = p, foot = t.position, top = t.TransformPoint(p.PivotMesh), sc = sc, r = p.Radius * sc, h = p.Height * sc, fade = fw.FadeAt(t.position, p.Family), label = label };
        }
        /// <summary>The tip of a plant (its top) moved by the wind now, with and without the downwash's bend: the screen pixels between.</summary>
        float TipPx(PlantAt a, OzFloraWindMath.Wind w, out float metres)
        {
            var cam = Camera.main; metres = 0f; if (!cam) return 0f;
            var tip = a.foot + Vector3.up * a.h;
            var w0 = w; w0.DbE = 0f; w0.DbF = 0f;
            OzFloraWindMath.Displace(a.p.Kind, w, a.foot.x, a.foot.y, a.foot.z, a.top.x, a.top.y, a.top.z, a.sc, a.r, a.h, a.fade, tip.x, tip.y, tip.z, out float dx, out float dy, out float dz);
            OzFloraWindMath.Displace(a.p.Kind, w0, a.foot.x, a.foot.y, a.foot.z, a.top.x, a.top.y, a.top.z, a.sc, a.r, a.h, a.fade, tip.x, tip.y, tip.z, out float ex, out float ey, out float ez);
            var p1 = tip + new Vector3(dx, dy, dz); var p0 = tip + new Vector3(ex, ey, ez);
            metres = (p1 - p0).magnitude;
            var s1 = cam.WorldToScreenPoint(p1); var s0 = cam.WorldToScreenPoint(p0);
            return s1.z > 0f && s0.z > 0f ? new Vector2(s1.x - s0.x, s1.y - s0.y).magnitude : 0f;
        }

        IEnumerator Trees()
        {
            Log("E1 the trees' downwash bend (M10): a low pass over a kapok, a palm and a small plant standing close together");
            var fw = OzFloraWind.I;
            if (!fw) { Check("E1 the level's plant wind is running", false); yield break; }
            var all = fw.All.Where(p => p != null && p.R && !p.Gone && p.R.gameObject.activeInHierarchy).ToList();
            OzFloraWind.Plant kapok = null, palm = null, small = null; float bestD = 1e9f;
            foreach (var k in all.Where(p => p.Kind == OzFloraWindMath.Tree && p.R.name.IndexOf("Kapok", System.StringComparison.OrdinalIgnoreCase) >= 0 || (p.Kind == OzFloraWindMath.Tree && p.R.GetComponent<MeshFilter>() && p.R.GetComponent<MeshFilter>().sharedMesh && p.R.GetComponent<MeshFilter>().sharedMesh.name.Contains("Kapok"))))
            {
                var kp = k.R.transform.position;
                var pa = all.Where(p => p.Kind == OzFloraWindMath.Palm).OrderBy(p => (p.R.transform.position - kp).sqrMagnitude).FirstOrDefault();
                var sm = all.Where(p => p.Kind == OzFloraWindMath.Small).OrderBy(p => (p.R.transform.position - kp).sqrMagnitude).FirstOrDefault();
                if (pa == null || sm == null) continue;
                float d = (pa.R.transform.position - kp).magnitude + (sm.R.transform.position - kp).magnitude;
                if (d < bestD) { bestD = d; kapok = k; palm = pa; small = sm; }
            }
            if (kapok == null) { kapok = all.FirstOrDefault(p => p.Kind == OzFloraWindMath.Tree); }
            if (kapok == null || palm == null || small == null) { Check("E1 a kapok, a palm and a small plant near each other", false); yield break; }
            var A = new[] { Of(fw, kapok, "kapok"), Of(fw, palm, "palm"), Of(fw, small, "small plant") };
            var mid = (A[0].foot + A[1].foot + A[2].foot) / 3f;
            Log($"E1 the three: kapok {A[0].h:F1} m at ({A[0].foot.x:F1}, {A[0].foot.z:F1}), palm {A[1].h:F1} m {(A[1].foot - A[0].foot).magnitude:F1} m from it, small plant {A[2].h:F1} m {(A[2].foot - A[0].foot).magnitude:F1} m from it");
            // the pass: from a pad on one side of the three to a pad on the other side, straight over their middle
            var from = PosOf(s); var way = new Vector3(mid.x - from.x, 0f, mid.z - from.z); if (way.sqrMagnitude < 1f) way = Vector3.right; way.Normalize();
            var start = FreePadNear(mid - way * 30f, 0, 200); var end = FreePadNear(mid + way * 30f, 0, 200);
            if (start == null || end == null) { Check("E1 pads on both sides of the three", false); yield break; }
            if (Move(start[0], start[1])) yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 60f);
            yield return Wait(1.5f);
            // calm: the three before the ship comes
            yield return ShotAt("e1_trees_calm_h70", mid);
            float[] maxPx = new float[3], maxM = new float[3]; float kMax = 0f, eMax = 0f, eMin = 0f;
            Check("E1 the pass over the three is ordered", Move(end[0], end[1]));
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise, 15f);
            follow = false; look = mid;
            int n = 0; bool washShot = false; float t0 = Time.unscaledTime; var logged = new StringBuilder();
            var fr = new List<string>(); float nextFrame = 0f, step = 0.35f; int strip = 0;
            while (Time.unscaledTime - t0 < 20f && s.ShuttleState != ZShuttleState.Descend && s.ShuttleState != ZShuttleState.Landed)
            {
                var w = fw.CurrentWind();
                for (int i = 0; i < 3; i++) { float px = TipPx(A[i], w, out float m); if (px > maxPx[i]) maxPx[i] = px; if (m > maxM[i]) maxM[i] = m; }
                kMax = Mathf.Max(kMax, OzFloraDownwash.K); eMax = Mathf.Max(eMax, OzFloraWind.BendE); eMin = Mathf.Min(eMin, OzFloraWind.BendE);
                var sp = PosOf(s); float dmid = new Vector2(sp.x - mid.x, sp.z - mid.z).magnitude;
                if (dmid < 22f && strip < 12 && Time.unscaledTime >= nextFrame)
                {   // the 12-frame strip from the fixed camera while the ship passes
                    string f = $"e1_trees_pass_f{strip:00}_h70"; Shot(f); fr.Add(P(f)); strip++; nextFrame = Time.unscaledTime + step;
                }
                if (!washShot && dmid < 3f) { washShot = true; Shot("e1_trees_downwash_h70"); }
                if (n++ % 30 == 0 && dmid < 16f)
                {   // the downwash numbers of the three crowns (FIX_LIST M10: log kw, S and the tip offset in Play)
                    foreach (var a in A)
                    {
                        float dd = new Vector2(a.foot.x - OzFloraDownwash.Centre.x, a.foot.z - OzFloraDownwash.Centre.z).magnitude, R = OzFloraDownwash.R;
                        float kw = OzFloraDownwash.K * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f * R, R, dd)));
                        float S = Mathf.Min(w.S + 1.2f * kw, Mathf.Max(w.S, 2.1f));
                        TipPx(a, w, out float tm);
                        logged.AppendLine($"     t {Time.unscaledTime - t0:F2} s {a.label}: {dd:F1} m from the jet axis, k {OzFloraDownwash.K:F2}, kw {kw:F2}, S {w.S:F2} -> {S:F2}, bend envelope {OzFloraWind.BendE:F2}, tip moved {tm:F2} m by the bend");
                    }
                }
                yield return null;
            }
            look = null; follow = true;
            // the spring-back: the downwash ends as the engines spool down after the landing at the far pad (one overshoot, then still)
            float tl = Time.unscaledTime;
            while (Time.unscaledTime - tl < 40f && !(s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && OzFloraDownwash.K <= 0.001f)) { eMax = Mathf.Max(eMax, OzFloraWind.BendE); eMin = Mathf.Min(eMin, OzFloraWind.BendE); yield return null; }
            tl = Time.unscaledTime;
            while (Time.unscaledTime - tl < 3f) { eMin = Mathf.Min(eMin, OzFloraWind.BendE); yield return null; }
            foreach (var f in fr) yield return OzProofPictures.Written(f);
            OzProofPictures.Sheet(fr, 4, 640, System.IO.Path.Combine(dir, "e1_trees_pass_sheet.jpg"));
            Log("E1 the downwash numbers of the three crowns during the pass:\n" + logged.ToString().TrimEnd());
            yield return Wait(0.2f);
            yield return OzProofPictures.Written(P("e1_trees_calm_h70")); yield return OzProofPictures.Written(P("e1_trees_downwash_h70"));
            var cam = Camera.main; var cs = cam ? cam.WorldToScreenPoint(mid) : Vector3.zero;
            OzProofPictures.CropPair(P("e1_trees_calm_h70"), P("e1_trees_downwash_h70"), new Vector2(cs.x, cs.y), 420, System.IO.Path.Combine(dir, "e1_trees_calm_vs_downwash_crop.jpg"));
            for (int i = 0; i < 3; i++) Log($"E1 {A[i].label} ({A[i].h:F1} m): its top moved by the bend up to {maxM[i]:F2} m = {maxPx[i]:F0} px at 70 m");
            Log($"E1 the downwash strength up to {kMax:F2}, the bend's envelope {eMin:F2} .. {eMax:F2} (below 0 = the spring-back's overshoot); strip frames {strip}");
            Check("E1 every plant type bends visibly in the downwash: its top moves at least 15 px at 70 m (kapok, palm, small plant)", maxPx.All(v => v >= 15f));
            Check("E1 the bend eases in and springs back with one overshoot when the downwash ends (envelope up to >= 0.95, then down to <= -0.1)", eMax >= 0.95f && eMin <= -0.1f);
            Check("E1 the 12-frame fixed-camera strip of the pass", strip >= 12);
        }

        // ------------------------------------------------------------------ E2: the voices (M13)
        struct Span { public float a, b; public string key; }
        List<Span> Spans(float t0, bool rachel)
        {
            var o = new List<Span>();
            foreach (var e in OzSoundLog.Since(t0))
            {
                if (e.src != "VO" || e.outcome == null || !e.outcome.StartsWith("PLAY")) continue;
                bool isR = e.outcome.Contains(" rachel");
                if (rachel != isR) continue;
                if (!rachel && !OzShuttleVoiceGate.IsShuttleLine(e.key)) continue;
                o.Add(new Span { a = e.t, b = e.t + ClipLen(e.clip), key = e.key });
            }
            return o;
        }
        int Overlaps(float t0, out string worst)
        {
            int n = 0; worst = "-"; float w = 0f;
            var R = Spans(t0, true); var A = Spans(t0, false);
            foreach (var r in R) foreach (var a in A)
                {
                    float ov = Mathf.Min(r.b, a.b) - Mathf.Max(r.a, a.a);
                    if (ov > 0.02f) { n++; if (ov > w) { w = ov; worst = $"{a.key} at {a.a - T0:F2} s and {r.key} at {r.a - T0:F2} s overlap {ov:F2} s"; } }
                }
            return n;
        }

        IEnumerator Voices()
        {
            Log("E2 the voices (M13): three landings with Rachel's line each, one with a vehicle finished at the touchdown, \"Can't land here!\" asked while Rachel speaks");
            float t0 = Time.unscaledTime; var G = AudioDirector.ShuttleGate;
            int held0 = G.Held, said0 = G.HeldPlayed, waits0 = G.RachelHeldFrames;
            OzPlayableShuttle.ProofRachelEvery = true;
            for (int k = 0; k < 4; k++)
            {
                var here = PosOf(s); double a = (k * 97 + 30) * System.Math.PI / 180.0;
                var q = FreePadNear(here + new Vector3((float)System.Math.Cos(a), 0f, (float)System.Math.Sin(a)) * 28f, 0, 240, (x, y) => OpenAround(x, y, 8f) && (ZGame.PxToWorld(x, y) - here).magnitude > 18f);
                if (q == null || !Move(q[0], q[1])) { Check($"E2 landing {k + 1}: a pad", false); continue; }
                int l0 = ship.Landings;
                yield return WaitFor(() => ship.Landings > l0, 60f);
                if (k == 3) { AudioDirector.Announce("ann_vehicle_manufactured"); Log("E2 landing 4: a vehicle finished at the touchdown (the factory's own line asked now, as its UnitBuilt event asks it)"); }
                yield return Wait(6.5f);
            }
            OzPlayableShuttle.ProofRachelEvery = false;
            // "Can't land here!" asked while Rachel speaks
            var blocked = FreePadNear(PosOf(s), 160, 400, (x, y) => OpenAround(x, y, 8f));
            ZObject stand = null;
            if (blocked != null) { stand = Squad("grunt", blocked[0] >> 4, blocked[1] >> 4, me, 1); yield return Wait(0.5f); }
            if (blocked != null && Move(blocked[0], blocked[1]))
            {
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Cruise, 10f);
                var goal = ZGame.PxToWorld(blocked[0], blocked[1]);
                yield return WaitFor(() => { var p = PosOf(s); return new Vector2(p.x - goal.x, p.z - goal.z).magnitude < 9f; }, 40f);
                AudioDirector.Announce("ann_starting_repair");                                                  // Rachel speaks while it finds the pad taken
                yield return WaitFor(() => s.ShuttleState == ZShuttleState.Hover, 20f);
                yield return Wait(5f);
            }
            if (stand != null) Vanish(stand);
            int ov, nR, nA; string worst;
            if (take) { take.Flush(); ov = take.VoiceOverlaps(t0, out worst, out nR, out nA); }               // every line of the stage (the in-Play log keeps only its last 2000)
            else { ov = Overlaps(t0, out worst); nR = Spans(t0, true).Count; nA = Spans(t0, false).Count; }
            Log($"E2 Rachel lines {nR}, shuttle lines {nA}; shuttle lines held behind Rachel +{G.Held - held0} (said after her +{G.HeldPlayed - said0}, dropped {G.HeldDropped}), Rachel's frames held behind a shuttle line +{G.RachelHeldFrames - waits0} (let go by the cap {G.RachelCapped}); overlaps {ov} (worst: {worst})");
            Check("E2 three or more landings with Rachel's line, and shuttle lines in the take", nR >= 3 && nA >= 3);
            Check("E2 no Rachel line ever overlaps a shuttle line (every pair in the whole stage): 0", ov == 0);
            Check("E2 a shuttle line asked while Rachel speaks is held and said after her (never under her)", G.Held - held0 > 0 && G.HeldPlayed - said0 > 0);
            yield return Wait(1f);
        }

        // ------------------------------------------------------------------ E3: the repair station (M8)
        /// <summary>Station vertices (its body and the crane where it is) inside the drawn ship: the ship as 0.1 m columns of its SH_ parts'
        /// points (lowest .. highest per column, root space); a station vertex inside a column's span (3 cm in from its ends) counts.</summary>
        int StationInShip(ZObject station, out string where)
        {
            where = "-";
            if (!g.StaticViews.TryGetValue(station.Id, out var go) || !go) return -1;
            var root = ship.transform; var cols = new Dictionary<long, Vector2>(); int part = 0;
            foreach (var r in ship.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.name.StartsWith("SH_")) continue;
                part++;                                                                                     // run 27D: columns per part (a column from a pad to the hull's top held the deck under the hull)
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var m = root.worldToLocalMatrix * r.transform.localToWorldMatrix; var vs = mf.sharedMesh.vertices; var tr = mf.sharedMesh.triangles;
                for (int i = 0; i + 2 < tr.Length; i += 3)
                {
                    Vector3 a = m.MultiplyPoint3x4(vs[tr[i]]), b = m.MultiplyPoint3x4(vs[tr[i + 1]]), c = m.MultiplyPoint3x4(vs[tr[i + 2]]);
                    for (int u = 0; u <= 4; u++) for (int v = 0; v <= 4 - u; v++)
                        {
                            var p = a + (b - a) * (u / 4f) + (c - a) * (v / 4f);
                            long key = (((long)Mathf.FloorToInt(p.x / 0.1f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 0.1f)) * 64 + part;
                            cols[key] = cols.TryGetValue(key, out var y) ? new Vector2(Mathf.Min(y.x, p.y), Mathf.Max(y.y, p.y)) : new Vector2(p.y, p.y);
                        }
                }
            }
            int hits = 0;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
            {
                var r = mf.GetComponent<Renderer>(); if (!r || !r.enabled || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var m = root.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                foreach (var v0 in mf.sharedMesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v0);
                    long key0 = ((long)Mathf.FloorToInt(p.x / 0.1f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 0.1f);
                    for (int q = 1; q <= part; q++)
                        if (cols.TryGetValue(key0 * 64 + q, out var y) && p.y > y.x + 0.03f && p.y < y.y - 0.03f) { hits++; if (where == "-") where = $"{mf.name} at ({p.x:F2}, {p.y:F2}, {p.z:F2}) in the ship's space"; break; }
                }
            }
            return hits;
        }
        /// <summary>The plan gap (m) between the crane gantry's drawn bounds and the ship's hull (its SH_Hull part).</summary>
        float CraneGap(int stationId)
        {
            if (!OzStationCrane.GantryBounds(stationId, out var gb)) return -1f;
            // run 27D: the plan gap between the gantry's and the hull's own vertices (two world boxes of a diagonal ship overlap when the parts do not)
            var hv = new List<Vector3>(); var gv = new List<Vector3>();
            foreach (var r in ship.GetComponentsInChildren<MeshRenderer>())
                if (r.enabled && r.gameObject.name.StartsWith("SH_Hull")) { var mf = r.GetComponent<MeshFilter>(); if (mf && mf.sharedMesh && mf.sharedMesh.isReadable) { var vs = mf.sharedMesh.vertices; for (int i = 0; i < vs.Length; i += 6) hv.Add(r.transform.TransformPoint(vs[i])); } }
            var stF = typeof(OzStationCrane).GetField("st", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            var dict = stF != null ? stF.GetValue(null) as System.Collections.IDictionary : null; GameObject gan = null;
            if (dict != null && dict.Contains(stationId)) { var o = dict[stationId]; var gf = o.GetType().GetField("gantry"); gan = gf != null ? gf.GetValue(o) as GameObject : null; }
            if (gan) foreach (var mf in gan.GetComponentsInChildren<MeshFilter>()) if (mf.sharedMesh && mf.sharedMesh.isReadable) { var vs = mf.sharedMesh.vertices; for (int i = 0; i < vs.Length; i += 4) gv.Add(mf.transform.TransformPoint(vs[i])); }
            if (hv.Count == 0 || gv.Count == 0) return -1f;
            float best = 1e9f;
            foreach (var a in gv) foreach (var b in hv) { float d2 = (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z); if (d2 < best) best = d2; }
            return Mathf.Sqrt(best);
        }

        IEnumerator Station()
        {
            Log("E3 the repair station (M8): half health, R; the crane rolls away and back (a fixed strip), the hook up, down ON the deck, no voices");
            ZObject station = null; foreach (var f in W.Flags) foreach (var b in f.LinkedObjects) if (b.Type == "repair" && station == null) { station = b; if (b.Owner != me) W.AwardZone(f, me); }
            if (station == null) { Check("E3 a repair station", false); yield break; }
            var sp = g.CenterWorld(station);
            float t0 = Time.unscaledTime; int aways0 = OzStationCrane.Aways, backs0 = OzStationCrane.Backs;
            s.SetHealth(s.MaxHealth / 2); input.Selection.Clear(); input.Selection.Add(s);
            Check("E3 R is taken", input.SendToRepair());
            yawRate = 0f; turnRec = true;
            // the crane rolling away: 6 frames over its 2 s from the fixed camera on the station
            yield return WaitFor(() => s.ShuttleCraneStation == station.Id && s.ShuttleCraneCallAt >= 0, 60f);
            yield return Strip("e3_crane_rolling_away", sp, 6, 0.4f);
            float gapAtDescent = -1f;
            yield return WaitFor(() => { if (s.ShuttleState == ZShuttleState.Descend && gapAtDescent < 0f) gapAtDescent = CraneGap(station.Id); return s.ShuttleOnRepairPad; }, 60f);
            turnRec = false;
            float landT = Time.unscaledTime;
            yield return Wait(1.0f);
            yield return ShotAt("e3_on_the_deck_h70", sp);
            look = sp; yield return Close("e3_on_the_deck", 32f); look = null;
            int shipIn = OzStationCrane.ShipHits(station.Id, ship.transform, out float deep, out string w1);
            int stIn = StationInShip(station, out string w2);
            float gapNow = CraneGap(station.Id);
            Log($"E3 the crane: {OzStationCrane.ParkLog}; its slide now {OzStationCrane.SlideK(station.Id):F2}; the plan gap to the hull when the descent began {gapAtDescent:F2} m, now {gapNow:F2} m");
            Log($"E3 on the deck: ship points in the station {shipIn} (worst {deep:F2} m {w1}); station points in the ship {stIn} ({w2}); the hover turn's peak {yawRate:F1} deg/s");
            Check("E3 the crane rolls fully off the deck (its slide 1.0) before the ship comes down, with a gap of at least 1.5 m to the hull", OzStationCrane.Aways > aways0 && gapAtDescent >= 1.5f);
            Check("E3 0 ship points in the station and 0 station points in the ship (the grey box gone)", shipIn == 0 && stIn == 0);
            Check("E3 the hover turn over the deck at most 30 deg/s", yawRate <= 30.5f);
            // the side by side with the owner's o4
            var o4 = OwnerPicture("o4_on_repair_pad_nose_over_edge.png");
            yield return OzProofPictures.Written(P("e3_on_the_deck_extra_close"));
            if (o4 != null) OzProofPictures.Pair(o4, P("e3_on_the_deck_extra_close"), System.IO.Path.Combine(dir, "e3_on_the_deck_vs_owner_o4.jpg"));
            yield return WaitFor(() => !s.ShuttleOnRepairPad, 25f);
            float liftT = Time.unscaledTime;
            yield return WaitFor(() => s.ShuttleCraneStation < 0, 30f);
            yield return Strip("e3_crane_rolling_back", sp, 6, 0.4f);
            int talk = 0; string said = "-", others = "";
            foreach (var e in OzSoundLog.Since(landT - 3f))
            {
                if (e.t > liftT + 1f) break;
                if (e.src == "VO" && e.outcome != null && e.outcome.StartsWith("PLAY") && (e.key.StartsWith("vo_shuttle_") || e.key.StartsWith("ann_")))
                {   // run 27D: the station's / the ship's own lines count; the level's others (a factory's robot, a zone) are listed
                    bool own = e.key.StartsWith("vo_shuttle_") || e.key == "ann_starting_repair" || e.key == "ann_vehicle_repaired" || e.key.StartsWith("ann_shuttle") || e.key.StartsWith("ann_repair");
                    if (own) { talk++; said = e.key; } else others += (others.Length > 0 ? ", " : "") + e.key;
                }
            }
            Log("E3 the level's own lines in that time (not the station's or the ship's): " + (others.Length > 0 ? others : "none"));
            var hoist = take.Of("shuttle_crane_hoist");
            Log($"E3 from the landing to the lift-off: ship voices and Rachel lines {talk} (last {said}); starting repair kept silent for the shuttle {AudioDirector.ShuttleRepairMuted}; crane back +{OzStationCrane.Backs - backs0}; the hoist row played {(hoist != null ? hoist.played : 0)}, quietest {(hoist != null && hoist.played > 0 ? hoist.lo : 0f):F3}");
            Check("E3 no vo_shuttle_* and no ann_* line from the station landing to the lift-off (S11 flipped: ann_starting_repair 0 plays)", talk == 0);
            Check("E3 the crane rolls back after the ship has left, with its sounds", OzStationCrane.Backs > backs0);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleOnRepairPad, 80f);
        }

        // ------------------------------------------------------------------ E4: the parrots (M20)
        IEnumerator Birds()
        {
            Log("E4 the parrots (M20): straight through a flying group; a 2x crop strip of 10 frames");
            var pos = new List<Vector3>(); var span = new List<float>(); var drawn = new List<bool>();
            Vector3 group = Vector3.zero; int best = 0; float t0 = Time.unscaledTime;
            while (Time.unscaledTime - t0 < 40f && best < 3)
            {
                JungleFauna.Flying(pos, span, drawn);
                for (int i = 0; i < pos.Count; i++)
                {
                    if (!drawn[i]) continue; int n = 0; var c = Vector3.zero;
                    for (int j = 0; j < pos.Count; j++) if (drawn[j] && (pos[j] - pos[i]).magnitude < 10f) { n++; c += pos[j]; }
                    if (n > best) { best = n; group = c / n; }
                }
                if (best < 3) yield return Wait(0.5f);
            }
            if (best < 3) { Check("E4 a flying parrot group (3 or more drawn)", false); yield break; }
            var from = PosOf(s); var way = new Vector3(group.x - from.x, 0f, group.z - from.z); if (way.sqrMagnitude < 1f) way = Vector3.right; way.Normalize();
            var end = FreePadNear(group + way * 30f, 0, 240);
            if (end == null || !Move(end[0], end[1])) { Check("E4 a pad beyond the group", false); yield break; }
            int fr0 = JungleFauna.ShipFrights + JungleFauna.ShipFlyerBursts; float closest = 999f;
            yield return WaitFor(() => { var p = PosOf(s); return new Vector2(p.x - group.x, p.z - group.z).magnitude < 26f; }, 40f);
            var full = new List<string>(); int seenIn = 0; var crops = new List<string>();
            look = group; yield return null; yield return null;
            for (int i = 0; i < 10; i++)
            {
                JungleFauna.Flying(pos, span, drawn);
                var cam = Camera.main; var gc = cam.WorldToScreenPoint(group); int inCrop = 0;
                for (int j = 0; j < pos.Count; j++)
                {
                    if (!drawn[j]) continue; closest = Mathf.Min(closest, JungleFauna.ShipClearance(pos[j]));
                    var q = cam.WorldToScreenPoint(pos[j]); if (q.z > 0f && Mathf.Abs(q.x - gc.x) < Screen.width / 4f && Mathf.Abs(q.y - gc.y) < Screen.height / 4f) inCrop++;
                }
                if (inCrop > 0) seenIn++;
                string f = $"e4_parrots_f{i:00}_h70"; Shot(f); full.Add(P(f)); yield return OzProofPictures.AfterGrab();
                yield return OzProofPictures.Written(P(f));
                string c = System.IO.Path.Combine(dir, $"e4_parrots_f{i:00}_crop2x.jpg");
                OzProofPictures.Crop(P(f), new Vector2(gc.x, gc.y), Screen.width / 4, Screen.height / 4, c); crops.Add(c);
                yield return Wait(0.25f);
            }
            look = null;
            OzProofPictures.SheetJpg(crops, 5, 512, System.IO.Path.Combine(dir, "e4_parrots_crop2x_sheet.jpg"));
            Log($"E4 the group's birds in the 2x crop in {seenIn} of 10 frames; the closest bird to the ship's keep-out {closest:F2} m; frights / bursts +{JungleFauna.ShipFrights + JungleFauna.ShipFlyerBursts - fr0}");
            Check("E4 the parrots are seen in at least 8 of the 10 crop frames", seenIn >= 8);
            Check("E4 they take fright and keep clear of the hull (never inside the keep-out: >= 3 m)", JungleFauna.ShipFrights + JungleFauna.ShipFlyerBursts > fr0 && closest >= 3f);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 40f);
        }

        // ------------------------------------------------------------------ E5: the cover cases (S3)
        IEnumerator Cover()
        {
            Log("E5 the cover cases (S3): shooters under tree crowns; shooters behind a hut with the ship over the hut");
            var fw = OzFloraWind.I; int hpMax0 = s.MaxHealth; s.MaxHealth = 1000000; s.SetHealth(s.MaxHealth);
            // (a) under the crowns: a tree cell near the ship, the shooters at a crown's foot, the ship hovering over them
            Vector4 crown = Vector4.zero; bool haveCrown = false; float bd = 1e9f; var here = PosOf(s);
            if (fw) foreach (var c in fw.Crowns) { float d = new Vector2(c.x - here.x, c.z - here.z).magnitude; if (d > 20f && d < bd && c.w > 1.5f) { bd = d; crown = c; haveCrown = true; } }
            if (haveCrown)
            {
                int tx = Px(crown.x) >> 4, ty = Px(-crown.z) >> 4;
                var sq = Squad("laser", tx, ty, them); var sq2 = Squad("grunt", tx + 1, ty, them);
                OzShuttleAim.ProbeReset(); OzShuttleAim.Recording = true; float t0 = Time.unscaledTime;
                Move(Px(crown.x), Px(-crown.z));
                yield return WaitFor(() => s.ShuttleAirborne && (s.ShuttleState == ZShuttleState.Hover || s.ShuttleState == ZShuttleState.Descend), 40f);
                Attack(sq); Attack(sq2);
                yield return Wait(8f);
                var mid = (PosOf(s) + new Vector3(crown.x, 0f, crown.z)) * 0.5f;
                yield return ShotAt("e5a_shooters_under_crowns_h70", mid);
                OzShuttleAim.Recording = false;
                int shots = OzShuttleAim.Probes.Values.Sum(p => p.shots);
                Log($"E5a shooters under the crown at ({crown.x:F1}, {crown.z:F1}) (radius {crown.w:F1} m): drawn shots at the flying ship {shots}; hull hits heard (laser) +{AudioDirector.HullHitsEnergy}");
                Check("E5a shooters under tree crowns still shoot the flying ship (drawn shots > 0)", shots > 0);
                Check("E5a a laser hit on the hull is heard (shuttle_hull_hit_energy)", take.Heard("shuttle_hull_hit_energy"));
                Vanish(sq); Vanish(sq2);
            }
            else Check("E5a a tree crown near the ship", false);
            // (b) behind a hut: the ship over the hut, the shooters on its far side
            GameObject hut = null; ZObject hutObj = null; bd = 1e9f; here = PosOf(s);
            foreach (var o in W.Objects)
            {
                if (o.Destroyed || o.Type == null || o.Type.IndexOf("hut", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (!g.StaticViews.TryGetValue(o.Id, out var v) || !v) continue;
                float d = (v.transform.position - here).magnitude; if (d < bd) { bd = d; hut = v; hutObj = o; }
            }
            if (hut)
            {
                var hp = hut.transform.position; var away = new Vector3(hp.x - here.x, 0f, hp.z - here.z); if (away.sqrMagnitude < 1f) away = Vector3.right; away.Normalize();
                var at = hp + away * 6f;
                var sq = Squad("grunt", Px(at.x) >> 4, Px(-at.z) >> 4, them);
                OzShuttleAim.ProbeReset(); OzShuttleAim.Recording = true;
                Move(hutObj.CenterX, hutObj.CenterY);
                yield return WaitFor(() => s.ShuttleAirborne && s.ShuttleState == ZShuttleState.Hover, 40f);
                Attack(sq);
                yield return Wait(8f);
                yield return ShotAt("e5b_shooters_behind_hut_ship_over_it_h70", (hp + at + PosOf(s)) / 3f);
                OzShuttleAim.Recording = false;
                int shots = OzShuttleAim.Probes.Values.Sum(p => p.shots);
                Log($"E5b the ship hovering over the hut ({hutObj.Type} #{hutObj.Id}, it could not land: {s.ShuttleNoLand}), the shooters 6 m behind it: drawn shots {shots}");
                Check("E5b shooters behind a hut still shoot the ship flying over the hut (drawn shots > 0)", shots > 0);
                Vanish(sq);
            }
            else Check("E5b a hut on the level", false);
            s.MaxHealth = hpMax0; s.SetHealth(hpMax0);
            var q = FreePadNear(PosOf(s), 120, 400); if (q != null) Move(q[0], q[1]);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 50f);
        }

        // ------------------------------------------------------------------ E6: the badges under the flying ship (S5)
        IEnumerator Badges()
        {
            Log("E6 the badges (S5): a flight over a building with its badge; the badge fades under the flying hull");
            yield return Wait(0.5f);
            ZObject b = null; float bd = 1e9f; var here = PosOf(s);
            foreach (var id in OpenZUnitOverlay.BadgeRects.Keys.ToList()) { var o = W.Get(id); if (o == null || o.Destroyed) continue; float d = (g.CenterWorld(o) - here).magnitude; if (d < bd && d > 25f) { bd = d; b = o; } }
            if (b == null) { Check("E6 a building with its badge on screen", false); yield break; }
            var bp = g.CenterWorld(b); var way = new Vector3(bp.x - here.x, 0f, bp.z - here.z).normalized;
            var end = FreePadNear(bp + way * 30f, 0, 240);
            if (end == null || !Move(end[0], end[1])) { Check("E6 a pad beyond the building", false); yield break; }
            int f0 = OzShuttleBadgeFade.FadedFrames; bool shot = false;
            yield return WaitFor(() => { if (!shot && OzShuttleBadgeFade.FadedNow > 0) { shot = true; Shot("e6_badge_faded_under_the_ship_h70"); } return s.ShuttleState == ZShuttleState.Descend || s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne && s.ShuttleLandings > 0 && OzShuttleBadgeFade.FadedFrames > f0 + 30; }, 60f);
            Log($"E6 over {b.Type} #{b.Id}: badge frames faded under the hull {OzShuttleBadgeFade.FadedFrames - f0}; picture {(shot ? "taken" : "none")}");
            Check("E6 a building's badge fades while the flying hull is over it (never drawn over the ship)", OzShuttleBadgeFade.FadedFrames > f0 && shot);
            yield return WaitFor(() => s.ShuttleState == ZShuttleState.Landed && !s.ShuttleAirborne, 40f);
        }

        // ------------------------------------------------------------------ E7: the ramp by day beside an APC's open door (S2)
        IEnumerator RampByDay()
        {
            Log("E7 the ramp by day (S2): the ship's open ramp and an APC's open door side by side");
            Check($"E7 the ramp's inner face is painted ({OzShuttleRampPaint.LastLog})", OzShuttleRampPaint.Painted > 0 && OzShuttleRampPaint.InnerTris > 0);
            double hd = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            int bx = (int)(s.CenterX - System.Math.Cos(hd) * 70) >> 4, by = (int)(s.CenterY - System.Math.Sin(hd) * 70) >> 4;   // behind the ship (its ramp's side)
            var sq = Squad("grunt", bx, by, me);
            var apc = Vehicle("apc", (s.CenterX >> 4) + 7, (s.CenterY >> 4), me);
            var sq2 = apc != null ? Squad("grunt", (apc.CenterX >> 4) + 3, (apc.CenterY >> 4) + 3, me) : null;
            yield return Wait(0.8f);
            Enter(sq, s); if (apc != null) Enter(sq2, apc);
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Open, 30f); yield return Wait(0.8f);
            var mid = apc != null ? (PosOf(s) + PosOf(apc)) * 0.5f : PosOf(s);
            yield return ShotAt("e7_ramp_and_apc_door_by_day_h70", mid);
            yield return OzProofPictures.Written(P("e7_ramp_and_apc_door_by_day_h70"));
            var cam = Camera.main;
            if (cam && apc != null)
            {
                var a = cam.WorldToScreenPoint(PosOf(s)); var b2 = cam.WorldToScreenPoint(PosOf(apc));
                OzProofPictures.Crop(P("e7_ramp_and_apc_door_by_day_h70"), new Vector2(a.x, a.y), 260, 200, System.IO.Path.Combine(dir, "e7_ramp_crop.jpg"));
                OzProofPictures.Crop(P("e7_ramp_and_apc_door_by_day_h70"), new Vector2(b2.x, b2.y), 260, 200, System.IO.Path.Combine(dir, "e7_apc_door_crop.jpg"));
                OzProofPictures.Pair(System.IO.Path.Combine(dir, "e7_ramp_crop.jpg"), System.IO.Path.Combine(dir, "e7_apc_door_crop.jpg"), System.IO.Path.Combine(dir, "e7_ramp_beside_apc_door.jpg"));
            }
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut, 30f);
            if (apc != null) Vanish(apc);
        }

        // ------------------------------------------------------------------ P: the testing panel (M17)
        static RectTransform FindUi(string panel, string cell)
        {
            var hud = OpenZHud.I; if (!hud || !hud.Root) return null;
            foreach (var t in hud.Root.GetComponentsInChildren<RectTransform>(true))
                if (t.name == panel) foreach (var c in t.GetComponentsInChildren<RectTransform>(true)) if (c.name == cell) return c;
            return null;
        }
        /// <summary>The dropped ship's SH_ vertices inside a building's drawn parts (each part's bounds), and where.</summary>
        int InBuildings(OzPlayableShuttle sv, out string where)
        {
            where = "-"; int n = 0;
            var parts = new List<Renderer>();
            foreach (var o in W.Objects)
            {
                if (o.Destroyed || o.Kind != ZKind.Building || !g.StaticViews.TryGetValue(o.Id, out var go) || !go) continue;
                foreach (var r in go.GetComponentsInChildren<MeshRenderer>()) if (r.enabled) parts.Add(r);
            }
            foreach (var r in sv.GetComponentsInChildren<MeshRenderer>())
            {
                if (!r.enabled || !r.gameObject.name.StartsWith("SH_")) continue;
                var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable) continue;
                var m = r.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices)
                {
                    var p = m.MultiplyPoint3x4(v);
                    foreach (var b in parts)
                    {
                        var bb = b.bounds; bb.Expand(-0.1f);
                        if (bb.Contains(p)) { n++; if (where == "-") where = $"{r.name} in {b.name}"; break; }
                    }
                }
            }
            return n;
        }
        IEnumerator DragTo(RectTransform cell, Vector2 drop, string halfShot)
        {
            Vector2 c = RectTransformUtility.WorldToScreenPoint(null, cell.TransformPoint(cell.rect.center));
            MouseEv(c); yield return null; yield return null; MouseEv(c, MouseButton.Left); yield return null; yield return null;
            for (int k = 1; k <= 8; k++)
            {
                MouseEv(Vector2.Lerp(c, drop, k / 8f), MouseButton.Left); yield return null;
                if (k == 4 && halfShot != null) { Shot(halfShot); yield return OzProofPictures.AfterGrab(); MouseEv(Vector2.Lerp(c, drop, k / 8f), MouseButton.Left); yield return null; }
            }
            MouseEv(drop); yield return null; yield return null; yield return null;
        }

        IEnumerator Panel()
        {
            Log("P the testing panel (M17): open with the shuttle's tile in both columns, a drag half way, a shuttle of each side dropped, the own one boarded and unloaded");
            if (!OzCheatSpawner.I) { Check("P the testing panel is there (the dev flag on)", false); yield break; }
            Team mine = me, theirs = them; var cam = Camera.main; var mid = rts ? rts.focus : Vector3.zero;
            follow = false;
            Key1(Key.P); yield return null; yield return null; Key0(); yield return null; yield return null;
            Check("P opens the testing panel", OzCheatSpawner.Visible);
            var cells = new[] { FindUi("Cheat" + (mine == Team.Red ? "Red" : "Blue"), "Cell_" + ZWorld.ShuttleType), FindUi("Cheat" + (theirs == Team.Red ? "Red" : "Blue"), "Cell_" + ZWorld.ShuttleType) };
            Check("P the shuttle's tile in both columns, each with its portrait", cells.All(c => c && c.GetComponentInChildren<UnityEngine.UI.RawImage>(true) && c.GetComponentInChildren<UnityEngine.UI.RawImage>(true).texture));
            yield return Wait(0.3f); Shot("p1_panel_open_shuttle_tiles_h70"); yield return OzProofPictures.AfterGrab();
            // two drops close together, both in the frame
            var pa = FreePadNear(mid, 120, 360, (x, y) => OpenAround(x, y, 9f));
            var pb = pa != null ? FreePadNear(ZGame.PxToWorld(pa[0], pa[1]), 150, 260, (x, y) => OpenAround(x, y, 9f)) : null;
            if (pa == null || pb == null || !cells[0] || !cells[1]) { Check("P room for two drops", false); yield break; }
            var wa = ZGame.PxToWorld(pa[0], pa[1]); var wb = ZGame.PxToWorld(pb[0], pb[1]);
            look = (wa + wb) * 0.5f; yield return null; yield return null;
            ZObject own = null, foe = null;
            for (int k = 0; k < 2; k++)
            {
                var wp = k == 0 ? wa : wb; wp.y = g.GroundY(wp.x, wp.z); var sp = cam.WorldToScreenPoint(wp); int before = OzCheatShuttle.Spawned;
                yield return DragTo(cells[k], new Vector2(sp.x, sp.y), k == 0 ? "p2_panel_drag_half_way_h70" : null); yield return Wait(0.6f);
                var o = OzCheatShuttle.Spawned > before ? OzCheatShuttle.Last : null;
                if (k == 0) own = o; else foe = o;
                Log($"P dropped {(k == 0 ? mine : theirs)} shuttle #{(o != null ? o.Id : -1)} at px {(o != null ? o.CenterX + "," + o.CenterY : "-")} (refused {OzCheatShuttle.Refused}: {OzCheatShuttle.LastWhy})");
            }
            Key1(Key.P); yield return null; yield return null; Key0(); yield return null; yield return null;
            yield return Wait(0.5f);
            Shot("p3_both_dropped_ships_in_the_frame_h70"); yield return OzProofPictures.AfterGrab();
            bool bothIn = true;
            foreach (var o in new[] { own, foe }) { if (o == null) { bothIn = false; continue; } var q = cam.WorldToViewportPoint(PosOf(o)); bothIn &= q.z > 0f && q.x > 0.05f && q.x < 0.95f && q.y > 0.12f && q.y < 0.95f; }
            Check("P a shuttle of each side dropped, both in the frame", own != null && foe != null && bothIn);
            foreach (var o in new[] { own, foe })
            {
                var sv = o != null ? OzPlayableShuttle.Of(o) : null; if (!sv) continue;
                int n = InBuildings(sv, out string where);
                Log($"P #{o.Id}: its points inside a building's drawn parts {n} ({where})");
                Check($"P the dropped shuttle #{o.Id} stands clear of every building (0 of its points inside one)", n == 0);
            }
            look = null;
            // the own dropped ship: a squad boards it, then gets out (the ramp, the door, the latch, the crew)
            if (own == null) yield break;
            s = own; ship = OzPlayableShuttle.Of(own); follow = true;
            yield return Wait(0.5f);
            float t0 = Time.unscaledTime;
            double hd = s.ShuttleHeadingDeg * System.Math.PI / 180.0;
            var sq = Squad("grunt", (int)(s.CenterX - System.Math.Cos(hd) * 64) >> 4, (int)(s.CenterY - System.Math.Sin(hd) * 64) >> 4, me);
            yield return Wait(0.6f);
            Check("P the squad's ENTER into the dropped ship is taken", Enter(sq, s));
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Open, 30f); yield return Wait(0.6f);
            Shot("p4_dropped_ship_boarding_ramp_h70"); yield return OzProofPictures.AfterGrab();
            yield return WaitFor(() => s.Passengers.Count > 0 && s.ShuttleRampPhase == ZShuttleRamp.Shut, 30f);
            yield return Wait(1f);
            yield return WaitFor(() => s.CanEjectCrew(), 8f);
            Check("P EXIT from the dropped ship is taken", W.Eject(me, s));
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Open, 10f); yield return Wait(1.2f);
            Shot("p5_dropped_ship_unloading_ramp_h70"); yield return OzProofPictures.AfterGrab();
            yield return WaitFor(() => s.ShuttleRampPhase == ZShuttleRamp.Shut, 20f); yield return Wait(1f);
            string[] keys = { "shuttle_door_open", "shuttle_ramp_hydraulic", "shuttle_door_close", "latch", "crew_in", "crew_step" };
            var got = keys.Select(k => k + " " + Played(t0, k)).ToArray();
            Log("P the door sounds of the dropped ship: " + string.Join(", ", got));
            Check("P boarding and unloading the dropped ship: the door, the ramp's hydraulic, the latch and the crew in the sound log", keys.Take(4).All(k => Played(t0, k) > 0));
        }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.outcome.StartsWith("LOOP") || e.src == "DOOR")) n++; return n; }

        // ------------------------------------------------------------------ the end: the sound table (M15), the pictures (S7)
        static readonly string[] Actions =
        {
            "shuttle_spool_up", "shuttle_takeoff", "shuttle_hover_loop", "shuttle_jet_burn_loop", "shuttle_landing", "shuttle_downwash_gust", "shuttle_tree_rustle",
            "shuttle_door_open", "shuttle_ramp_hydraulic", "shuttle_door_close", "latch", "crew_in", "crew_step",
            "shuttle_station_crane", "shuttle_crane_hoist", "shuttle_hull_hit_energy", "shuttle_hull_hit_flame", "shuttle_strafe_whoosh",
            "shuttle_light_lock", "ngt_searchlight_on", "shuttle_piece_land", "vo_shuttle_landed", "vo_shuttle_no_land", "vo_shuttle_board", "ann_shuttle_landing",
        };
        static readonly HashSet<string> LoopsOrQuiet = new HashSet<string> { "shuttle_hover_loop", "shuttle_jet_burn_loop" };
        void Finish()
        {
            if (take)
            {
                var table = take.Table(Actions);
                Log("the take's sound table (every line of the take, no cap):\n" + table.TrimEnd());
                if (mode == "E")
                    foreach (var k in new[] { "shuttle_spool_up", "shuttle_takeoff", "shuttle_landing", "shuttle_door_open", "shuttle_station_crane", "shuttle_crane_hoist", "shuttle_hull_hit_energy" })
                    {
                        var r = take.Of(k); bool loud = r != null && r.played > 0 && (LoopsOrQuiet.Contains(k) || r.lo >= 0.12f);
                        Check($"M15 {k}: played in the take ({(r != null ? r.played : 0)}), its quietest play at least 0.12 ({(r != null && r.played > 0 ? r.lo : 0f):F3})", loud);
                    }
                take.End(Actions); take = null;
            }
            var dups = OzProofPictures.Duplicates(shots);
            Log($"pictures {shots.Count}; the same file twice: {(dups.Count == 0 ? "none" : string.Join(", ", dups))}");
            Check("S7 no two pictures of the take are the same file (md5)", dups.Count == 0);
            foreach (var o in made) if (o != null && !o.Destroyed && o != s) Vanish(o);
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)} ({passes} PASS), {shots.Count} pictures, {Time.unscaledTime - T0:F0} s");
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "proof_log_" + mode + ".txt"), sb.ToString()); }
            catch (System.Exception ex) { Debug.LogError("[OzShuttleEnvProof] could not write the log: " + ex.Message); }
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
            Destroy(gameObject);
        }
    }
}
