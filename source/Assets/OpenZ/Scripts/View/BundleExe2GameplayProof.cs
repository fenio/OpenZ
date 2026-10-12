using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using OpenZ.Sim;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace OpenZ
{
    /// <summary>
    /// ApcDoorProof). L02 (full): sunrise start; a red grunt squad clicked (salute) -> ENTER cursor on an own APC -> right-click boards;
    /// the APC fires out of its side hatches at a blue dummy; lone-crew rifles x3; a crane clicked -> REPAIR cursor on a damaged
    /// factory (and a destroyed building) -> right-click mends it; a blue dummy shells the red fort (roof defender up, fort gun
    /// shots with tilt + reload drop); a squad takes a quiet flag (celebration); a factory door held open (lit bay frame); last a blue
    /// squad parked by the red fort: DV60 sends it in -> loss by fort entry. L17 (short): sunrise start + the bays + one door frame.
    /// </summary>
    public class BundleExe2GameplayProof : MonoBehaviour
    {
        public static string Report = "idle";
        public static void Run(bool full = true)
        {
            Report = "running"; Full = full;
#if UNITY_EDITOR
            var gv = System.Type.GetType("UnityEditor.GameView,UnityEditor"); if (gv != null) UnityEditor.EditorWindow.FocusWindowIfItsOpen(gv);
#endif
            new GameObject("BundleExe2GameplayProof").AddComponent<BundleExe2GameplayProof>();
        }
        static bool Full = true, Recheck;
        /// <summary>
        /// clank count), with the editor's real mouse device switched off and the OS pointer parked over the Game view so only the
        /// proof's mouse drives PlayerInput / OpenZCursors / the HUD. Log: &lt;level&gt;/proof_log_recheck_11.txt.</summary>
        public static void RunRecheck() { Recheck = true; Run(false); }
#if UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
#endif
        static UnityEngine.InputSystem.Mouse proofMouse, realMouse;
        void ProofMouseOn()
        {
            realMouse = UnityEngine.InputSystem.Mouse.current;
#if UNITY_EDITOR_WIN
            var gvt = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            var gw = gvt != null ? Resources.FindObjectsOfTypeAll(gvt).FirstOrDefault() as UnityEditor.EditorWindow : null;
            if (gw) { gw.Focus(); float k = UnityEditor.EditorGUIUtility.pixelsPerPoint; var c = gw.position.center; SetCursorPos(Mathf.RoundToInt(c.x * k), Mathf.RoundToInt((c.y + 10f) * k)); Log($"OS pointer parked over the Game view centre ({c.x * k:F0}, {(c.y + 10f) * k:F0} px), game view focused {UnityEditor.EditorWindow.focusedWindow == gw}"); }
#endif
            proofMouse = InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("OzProofMouse");
            if (realMouse != null) InputSystem.DisableDevice(realMouse);
            proofMouse.MakeCurrent();
            Log($"mouse: proof device '{proofMouse.name}' current {UnityEngine.InputSystem.Mouse.current == proofMouse}, real '{realMouse?.name}' enabled {realMouse != null && realMouse.enabled}");
        }
        static void ProofMouseOff()
        {
            if (realMouse != null) { InputSystem.EnableDevice(realMouse); realMouse.MakeCurrent(); realMouse = null; }
            if (proofMouse != null) InputSystem.RemoveDevice(proofMouse);
            proofMouse = null;
        }
        void OnDestroy() { ProofMouseOff(); }

        ZGame g; ZWorld W; PlayerInput input; RtsCamera rts; readonly StringBuilder sb = new StringBuilder(); int fails, frames; string dir; float T0;
        readonly System.Collections.Generic.List<ZObject> immortal = new System.Collections.Generic.List<ZObject>();

        void Start()
        {
            g = ZGame.I; W = g.World; input = FindFirstObjectByType<PlayerInput>(); rts = FindFirstObjectByType<RtsCamera>();
            dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../../work/records/milestones/bundle_exe2_gameplay/" + g.level));
            System.IO.Directory.CreateDirectory(dir);
            StartCoroutine(Recheck ? EnterRecheck() : Full ? All() : Short());
        }

        void Update() { foreach (var o in immortal) if (o != null && !o.Destroyed && o.Health < o.MaxHealth) o.SetHealth(o.MaxHealth); }

        void Log(string s) { sb.AppendLine(s); Debug.Log("[Exe2Proof] " + s); }
        void Check(string what, bool ok) { if (!ok) fails++; Log((ok ? "PASS " : "FAIL ") + what); }
        void Shot(string name) { ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, name + ".png")); frames++; Log($"frame {name}.png"); }
        UnitView V(ZObject o) => o != null && g.Views.TryGetValue(o.Id, out var v) ? v : null;
        string Cursor() { var c = FindFirstObjectByType<OpenZCursors>(); return c ? c.Current : "?"; }
        static void MouseEv(Vector2 p, MouseButton? b = null) { var st = new MouseState { position = p }; if (b.HasValue) st = st.WithButton(b.Value); InputSystem.QueueStateEvent(proofMouse != null ? proofMouse : UnityEngine.InputSystem.Mouse.current, st); }
        IEnumerator Hover(Vector2 p) { for (int i = 0; i < 5; i++) { MouseEv(p); yield return null; } }
        IEnumerator HoverOn(ZObject o) { for (int i = 0; i < 8; i++) { MouseEv(At(o)); yield return null; } }
        IEnumerator Click(Vector2 p, MouseButton b) { MouseEv(p); yield return null; MouseEv(p, b); yield return null; yield return null; MouseEv(p); yield return null; yield return null; }
        Vector2 At(ZObject o) => input.ScreenRect(o).center;
        IEnumerator WaitFor(System.Func<bool> c, float max) { float t = 0f; while (t < max && !c()) { t += Time.deltaTime; yield return null; } }
        static int Played(float t0, string key) { int n = 0; foreach (var e in OzSoundLog.Since(t0)) if (e.key == key && e.outcome != null && (e.outcome.StartsWith("PLAY") || e.src == "HATCH" || e.src == "GESTURE")) n++; return n; }
        ZObject Spawn(string cat, string type, int tx, int ty, Team team)
        {
            OzCheatSpawner.FreeTile(W, tx, ty, cat == "robot" ? 1 : 2, cat == "robot", out tx, out ty);
            var o = cat == "robot" ? W.CreateRobotGroup(type, tx * 16, ty * 16, team) : W.CreateObject(cat, type, tx * 16, ty * 16, team);
            if (o == null) { Log($"spawn {cat}:{type} failed"); return null; }
            g.SpawnUnitView(o); foreach (var m in o.Minions) g.SpawnUnitView(m);
            return o;
        }
        static System.Collections.Generic.List<ZObject> Squad(ZObject l) { var s = new System.Collections.Generic.List<ZObject> { l }; s.AddRange(l.Minions); return s; }
        void Look(Vector3 p, float zoom) { if (rts) { rts.FlyTo(p); rts.ZoomTo(zoom); } }

        IEnumerator StartChecks()
        {
            var dn = FindFirstObjectByType<DayNight>();
            Log($"bundle_exe2_gameplay proof, {g.level}, {Screen.width}x{Screen.height}, test mode {PlayerPrefs.GetInt("oz_testmode", 0)}");
            float tod = DayNight.LevelStartTod;
            Log($"(28) time of day at the level start {tod:F3}, now {(dn ? dn.timeOfDay : -1f):F3} (sunrise {DayNight.SunriseStart:F3}, night 0.49-0.86), sun {(dn && dn.sun ? dn.sun.intensity.ToString("F2") + " " + dn.sun.color : "-")}");
            Check("(28) level starts at the warm sunrise", dn && tod > 0.90f && tod < 0.94f);
            int bays = 0, views = 0;
            if (FactoryViews.I) foreach (var kv in FactoryViews.I.Views) { views++; if (kv.Value && kv.Value.HasBay) bays++; }
            Log($"(8) factory / fort doors with a lit bay: {bays}/{views}, cut triangles {FactoryInterior.CutTris}");
            Check("(8) every factory / fort door has its interior bay", views > 0 && bays == views);
            yield return null;
        }

        IEnumerator DoorFrame(string tag)
        {
            FactoryView fv = null;
            if (FactoryViews.I) foreach (var kv in FactoryViews.I.Views) if (kv.Value && kv.Value.HasBay && kv.Value.B.Type != "fort_front" && kv.Value.B.Type != "fort_back" && !kv.Value.B.Destroyed) { fv = kv.Value; break; }
            if (fv == null) { Check("(8) a factory door to open", false); yield break; }
            var setOpen = typeof(FactoryView).GetProperty("Open").GetSetMethod(true); var apply = typeof(FactoryView).GetMethod("ApplyDoor", BindingFlags.NonPublic | BindingFlags.Instance);
            var door = fv.transform.TransformPoint((fv.DoorMin + fv.DoorMax) * 0.5f);
            Look(door + fv.transform.forward * 4f, 20f);
            for (float t = 0; t < 2.6f; t += Time.deltaTime) { setOpen.Invoke(fv, new object[] { 1f }); apply.Invoke(fv, new object[] { 1f }); yield return null; }
            var lamp = fv.GetComponentsInChildren<Light>(true).FirstOrDefault(l => l.name == "OZ_Bay_Light");
            Log($"(8) {fv.B.Type}#{fv.B.Id} door held open: bay lamp {(lamp ? lamp.enabled + " " + lamp.intensity.ToString("F2") : "none")}");
            Check("(8) the bay's work light is on with the door open", lamp && lamp.enabled && lamp.intensity > 1f);
            Shot(tag + "_factory_bay_open");
        }

        /// <summary>(18) + (11) on one of the level's own idle squads: real click (salute), ENTER cursor on an own APC, right-click boards.</summary>
        IEnumerator SaluteAndEnter()
        {
            ZObject sq = null;
            foreach (var o in W.Objects)
                if (o.Kind == ZKind.Robot && o.Owner == g.player && !o.IsMinion && !o.Destroyed && o.Type == "grunt" && Squad(o).All(m => !RobotGestures.Busy(m))) { sq = o; break; }
            if (sq == null) foreach (var o in W.Objects) if (o.Kind == ZKind.Robot && o.Owner == g.player && !o.IsMinion && !o.Destroyed && Squad(o).All(m => !RobotGestures.Busy(m))) { sq = o; break; }
            if (sq == null) { Check("(18) an idle own squad found", false); yield break; }
            var apc = Spawn("vehicle", "apc", (sq.CenterX >> 4) + 5, (sq.CenterY >> 4) + 1, Team.Red);
            Look(Vector3.Lerp(V(sq).transform.position, V(apc).transform.position, 0.5f), 24f);
            yield return new WaitForSeconds(1.6f);
            yield return WaitFor(() => Squad(sq).All(m => !m.IsMoving && !RobotGestures.Busy(m)), 6f);
            input.Selection.Clear();
            int sal0 = RobotGestures.Salutes, sp0 = RobotGestures.Peaks; float tSel = Time.unscaledTime;
            yield return Click(At(sq), MouseButton.Left);
            yield return new WaitForSeconds(0.5f); Shot("01_salute_on_select");
            yield return WaitFor(() => RobotGestures.Peaks > sp0, 2.5f);
            Log($"(18) {sq.Type}#{sq.Id} x{Squad(sq).Count} clicked: selected {input.Selection.Count}, salutes +{RobotGestures.Salutes - sal0}, hand lift {RobotGestures.LastLift:F2} arm lengths, gesture sounds {Played(tSel, "rob_gesture")}, skipped busy {RobotGestures.SkippedBusy}");
            Check("(18) the clicked idle squad salutes (every robot, with the servo sound), hand up at the brow", RobotGestures.Salutes - sal0 >= Squad(sq).Count && Played(tSel, "rob_gesture") >= 1 && RobotGestures.LastLift > 0.2f);
            yield return Hover(At(apc));
            string cur = Cursor();
            Log($"(11) cursor over the own APC with room: '{cur}' (hover {input.Hover?.Type}#{input.Hover?.Id})");
            Check("(11) ENTER cursor on an own APC for a selected squad", cur == "enter");
            yield return Click(At(apc), MouseButton.Right);
            yield return WaitFor(() => apc.Passengers.Count > 0, 18f);
            Log($"(11) passengers {apc.Passengers.Count} {apc.PassengerType}");
            Check("(11) the squad boarded the APC by the real mouse order", apc.Passengers.Count > 0);
        }

        /// <summary>(11) re-check: an own idle squad (else a fresh grunt squad on open ground) + an own APC 5 tiles off, both held still
        /// and framed on screen; left-click the squad, hover the APC (ENTER cursor), right-click it (boarding); L02 then (10): the boarded
        /// APC fires at a dummy, hatch opens / closes vs the CombatAudio PLAY lines of their clanks (1:1 each).</summary>
        IEnumerator EnterRecheck()
        {
            T0 = Time.unscaledTime; Application.targetFrameRate = 60;
            yield return new WaitForSeconds(1.5f);
            Log($"bundle_exe2_gameplay (11) re-check, {g.level}, {Screen.width}x{Screen.height}, test mode {PlayerPrefs.GetInt("oz_testmode", 0)}");
            ProofMouseOn();
            // top HUD bar (viewport y 0.89) and the click missed; now a fresh squad + APC on open ground near the map centre
            var f = Field();
            var sq = Spawn("robot", "grunt", f.x - 3, f.y, Team.Red);
            var apc = Spawn("vehicle", "apc", f.x + 3, f.y, Team.Red);
            var apc0 = new Vector2(apc.CenterX, apc.CenterY);
            Look(Vector3.Lerp(V(sq).transform.position, V(apc).transform.position, 0.5f), 24f);
            yield return new WaitForSeconds(1.6f);
            yield return WaitFor(() => Squad(sq).All(m => !m.IsMoving && !RobotGestures.Busy(m)), 8f);
            apc.Waypoints.Clear(); apc.StopMove();
            Look(Vector3.Lerp(V(sq).transform.position, V(apc).transform.position, 0.5f), 24f);   // re-frame both right before the clicks
            System.Func<ZObject, bool> framed = o => { var vp = Camera.main.WorldToViewportPoint(V(o).transform.position); return vp.z > 0 && vp.x > 0.15f && vp.x < 0.85f && vp.y > 0.25f && vp.y < 0.8f; };
            yield return WaitFor(() => framed(sq) && framed(apc), 4f);
            yield return new WaitForSeconds(0.4f);
            var vs = Camera.main.WorldToViewportPoint(V(sq).transform.position); var va = Camera.main.WorldToViewportPoint(V(apc).transform.position);
            Log($"squad {sq.Type}#{sq.Id} x{Squad(sq).Count} at viewport ({vs.x:F2}, {vs.y:F2}), APC #{apc.Id} at ({va.x:F2}, {va.y:F2}), APC moved {Vector2.Distance(apc0, new Vector2(apc.CenterX, apc.CenterY)):F0} px since spawn");
            input.Selection.Clear();
            yield return Click(At(sq), MouseButton.Left);
            yield return new WaitForSeconds(0.3f);
            Log($"(11) left-click on the squad at {At(sq)}: mouse reads {UnityEngine.InputSystem.Mouse.current.position.ReadValue()}, selected {input.Selection.Count} ({string.Join(",", input.Selection.Select(o => o.Type + "#" + o.Id))})");
            Check("(11) the squad is selected by the mouse click", input.Selection.Contains(sq));
            yield return HoverOn(apc);
            string cur = Cursor();
            Log($"(11) cursor over the own APC with room: '{cur}' (hover {input.Hover?.Type}#{input.Hover?.Id}, mouse {UnityEngine.InputSystem.Mouse.current.position.ReadValue()} vs APC {At(apc)})");
            Check("(11) ENTER cursor on an own APC for a selected squad", cur == "enter");
            Shot("09_recheck_enter_cursor");
            yield return Click(At(apc), MouseButton.Right);
            Log($"(11) after right-click: squad wp {(sq.Waypoints.Count > 0 ? sq.Waypoints[0].mode.ToString() : "-")}");
            yield return WaitFor(() => apc.Passengers.Count >= Squad(sq).Count, 18f);
            Log($"(11) passengers {apc.Passengers.Count} {apc.PassengerType} (squad of {Squad(sq).Count}), firepower x{apc.CrewFirepower}");
            Check("(11) the squad boarded the APC by the mouse order", apc.Passengers.Count > 0);
            Shot("10_recheck_boarded");
            ProofMouseOff();
            if (g.level == "L02" && apc.Passengers.Count > 0)
            {
                var dummy = Spawn("vehicle", "light", (apc.CenterX >> 4) + 7, (apc.CenterY >> 4) + 1, Team.Blue);
                if (dummy != null) { immortal.Add(dummy); dummy.Damage = 0; }
                int sh0 = ApcSideHatches.Shots, op0 = ApcSideHatches.Opens, cl0 = ApcSideHatches.Closes; float tH = Time.unscaledTime;
                W.SendWaypoints(Team.Red, apc, new[] { new ZWaypoint { mode = WpMode.Attack, refId = dummy.Id, x = dummy.LocX + 8, y = dummy.LocY + 8, attackTo = true, playerGiven = true } });
                Look(Vector3.Lerp(V(apc).transform.position, V(dummy).transform.position, 0.35f), 18f);
                yield return WaitFor(() => ApcSideHatches.Shots - sh0 >= 6, 10f);
                apc.Waypoints.Clear(); apc.Disengage(); apc.StopMove();
                yield return new WaitForSeconds(1.6f);
                var es = OzSoundLog.Since(tH);
                int opens = ApcSideHatches.Opens - op0, closes = ApcSideHatches.Closes - cl0;
                int oPlay = es.Count(e => e.key == "apc_hatch_open" && e.outcome != null && e.outcome.StartsWith("PLAY")), cPlay = es.Count(e => e.key == "apc_hatch_close" && e.outcome != null && e.outcome.StartsWith("PLAY"));
                var drops = es.Where(e => e.key != null && e.key.StartsWith("apc_hatch") && e.outcome != null && !e.outcome.StartsWith("PLAY")).Select(e => e.key + " " + e.outcome).Distinct();
                Log($"(10) hatch shots {ApcSideHatches.Shots - sh0}, opens {opens} / open clanks played {oPlay}, closes {closes} / close clanks played {cPlay}, log lines {es.Count(e => e.key != null && e.key.StartsWith("apc_hatch"))}, drops [{string.Join("; ", drops)}]");
                Check("(10) one open clank per hatch open and one close clank per hatch close", opens > 0 && closes > 0 && oPlay == opens && cPlay == closes);
            }
            Finish();
        }

        IEnumerator Short()
        {
            T0 = Time.unscaledTime; Application.targetFrameRate = 60;
            yield return new WaitForSeconds(1.5f);
            yield return StartChecks();
            Shot("00_start_sunrise");
            yield return SaluteAndEnter();
            yield return DoorFrame("L17");
            Finish();
        }

        IEnumerator All()
        {
            T0 = Time.unscaledTime; Application.targetFrameRate = 60;
            yield return new WaitForSeconds(1.5f);
            yield return StartChecks();
            Shot("00_start_sunrise");
            var f = Field();
            yield return new WaitForSeconds(0.8f);

            // ---- (11) + (18): click a squad (salute), ENTER cursor on an own APC, right-click boards
            var apc = Spawn("vehicle", "apc", f.x, f.y, Team.Red);
            var gr = Spawn("robot", "grunt", f.x - 4, f.y + 4, Team.Red);
            Log($"APC #{apc?.Id} drivers {apc?.Drivers.Count}, driver-only damage {apc?.Damage} per hit (grunt {W.S.Unit("grunt").Damage} x {ZWorld.CrewRifleMul})");
            Check("(9) lone APC driver fires a grunt squad's rifle", apc != null && apc.Damage == W.S.Unit("grunt").Damage * ZWorld.CrewRifleMul);
            Look(V(apc).transform.position + new Vector3(-1.5f, 0f, 1.5f), 26f);
            yield return new WaitForSeconds(1.2f);
            yield return WaitFor(() => Squad(gr).All(m => !m.IsMoving && !RobotGestures.Busy(m)), 8f);   // a fresh squad first walks into its formation
            yield return new WaitForSeconds(0.5f);
            input.Selection.Clear();
            int sal0 = RobotGestures.Salutes; float tSel = Time.unscaledTime;
            yield return Click(At(gr), MouseButton.Left);
            yield return new WaitForSeconds(0.45f); Shot("01_salute_on_select");
            yield return WaitFor(() => RobotGestures.Salutes - sal0 >= 3, 1.5f);
            int sp0 = RobotGestures.Peaks; yield return WaitFor(() => RobotGestures.Peaks > sp0, 2.5f);
            Log($"(18) selected {input.Selection.Count} ({string.Join(",", input.Selection.Select(o => o.Type))}), salutes +{RobotGestures.Salutes - sal0}, hand lift {RobotGestures.LastLift:F2} arm lengths, gesture sounds {Played(tSel, "rob_gesture")}");
            Check("(18) the clicked idle squad salutes (3 robots, with the servo sound), hand up at the brow", RobotGestures.Salutes - sal0 >= 3 && Played(tSel, "rob_gesture") >= 1 && RobotGestures.LastLift > 0.2f);
            yield return Hover(At(apc));
            string cur = Cursor();
            Log($"(11) cursor over the own APC with room: '{cur}' (hover {input.Hover?.Type}#{input.Hover?.Id})");
            Check("(11) ENTER cursor on an own APC for a selected squad", cur == "enter");
            yield return Click(At(apc), MouseButton.Right);
            Log($"(11) after right-click: squad wp {(gr.Waypoints.Count > 0 ? gr.Waypoints[0].mode.ToString() : "-")}");
            yield return WaitFor(() => apc.Passengers.Count > 0, 18f);
            Log($"(11) passengers {apc.Passengers.Count} {apc.PassengerType}, firepower x{apc.CrewFirepower}");
            Check("(11) the squad boarded the APC by the real mouse order", apc.Passengers.Count > 0);

            // ---- (10): the APC fires out of its side hatches
            var dummy = Spawn("vehicle", "light", (apc.CenterX >> 4) + 7, (apc.CenterY >> 4) + 1, Team.Blue);
            if (dummy != null) { immortal.Add(dummy); dummy.Damage = 0; }
            int sh0 = ApcSideHatches.Shots, op0 = ApcSideHatches.Opens; float tH = Time.unscaledTime;
            W.SendWaypoints(Team.Red, apc, new[] { new ZWaypoint { mode = WpMode.Attack, refId = dummy.Id, x = dummy.LocX + 8, y = dummy.LocY + 8, attackTo = true, playerGiven = true } });
            Look(Vector3.Lerp(V(apc).transform.position, V(dummy).transform.position, 0.35f) + new Vector3(0f, 0f, -1f), 18f);
            yield return WaitFor(() => ApcSideHatches.Shots - sh0 >= 4, 10f);
            yield return new WaitForSeconds(0.25f); Shot("02_apc_side_hatch_firing");
            yield return new WaitForSeconds(1.5f);
            Log($"(10) hatch shots {ApcSideHatches.Shots - sh0}, hatch opens {ApcSideHatches.Opens - op0}, open sounds {Played(tH, "apc_hatch_open")}, close sounds {Played(tH, "apc_hatch_close")}");
            Check("(10) passengers fire out of opening side hatches with the hatch sounds", ApcSideHatches.Shots - sh0 >= 4 && ApcSideHatches.Opens - op0 >= 1 && Played(tH, "apc_hatch_open") >= 1);
            apc.Waypoints.Clear(); apc.Disengage(); apc.StopMove();
            yield return new WaitForSeconds(1.2f);
            Log($"(10) after the fight: hatch closes {ApcSideHatches.Closes}, close sounds {Played(tH, "apc_hatch_close")}");

            // ---- (31): crane REPAIR cursor + order on a damaged factory (DV61) and on a destroyed building
            var fac = W.Objects.Where(o => o.Kind == ZKind.Building && !o.IsFort && !o.IsBridge && !o.Destroyed && o.GetCraneCenter(out _, out _) && (o.Owner == Team.Red || o.Owner == Team.Neutral))
                               .OrderBy(o => (o.CenterX - apc.CenterX) * (double)(o.CenterX - apc.CenterX) + (o.CenterY - apc.CenterY) * (double)(o.CenterY - apc.CenterY)).FirstOrDefault();
            if (fac != null)
            {
                fac.SetHealth(fac.MaxHealth / 2);
                var crane = Spawn("vehicle", "crane", (fac.CenterX >> 4) + 1, (fac.CenterY >> 4) + 9, Team.Red);
                Look(Vector3.Lerp(V(crane).transform.position, g.CenterWorld(fac), 0.5f), 34f);
                yield return new WaitForSeconds(1.3f);
                input.Selection.Clear();
                yield return Click(At(crane), MouseButton.Left);
                yield return Hover(At(fac));
                string cc = Cursor();
                Log($"(31) crane selected {input.Selection.Count == 1 && input.Selection[0] == crane}, cursor over the damaged {fac.Type}#{fac.Id} ({fac.Owner}, {fac.Health}/{fac.MaxHealth}): '{cc}'");
                Check("(31) REPAIR cursor over a damaged building with the crane selected", cc == "repair");
                Shot("03_crane_repair_cursor");
                int fx0 = ZObject.CraneFixes; float tR = Time.unscaledTime;
                yield return Click(At(fac), MouseButton.Right);
                Log($"(31) after right-click: crane wp {(crane.Waypoints.Count > 0 ? crane.Waypoints[0].mode.ToString() : "-")}");
                yield return WaitFor(() => ZObject.CraneFixes > fx0, 25f);
                yield return new WaitForSeconds(1.5f); Shot("04_crane_mending");
                yield return WaitFor(() => fac.Health >= fac.MaxHealth, 12f);
                Log($"(31) {fac.Type} health {fac.Health}/{fac.MaxHealth} after {Time.unscaledTime - tR:F1} s, mends {ZObject.CraneFixes - fx0}, crane sounds {OzSoundLog.Since(tR).Count(e => e.key != null && e.key.Contains("crane"))}");
                Check("(31) the crane goes and mends the damaged building to full health", fac.Health >= fac.MaxHealth && ZObject.CraneFixes > fx0);
                // a destroyed neutral building: the repair cursor too (Zod's rule)
                var dead = W.Objects.FirstOrDefault(o => o.Kind == ZKind.Building && !o.IsFort && !o.IsBridge && o != fac && o.Owner == Team.Neutral && !o.Destroyed && o.GetCraneCenter(out _, out _));
                if (dead != null)
                {
                    dead.SetHealth(0); W.UpdateObjectHealth(dead);
                    yield return new WaitForSeconds(2.0f);
                    Look(g.CenterWorld(dead), 34f); yield return new WaitForSeconds(1.0f);
                    if (!input.Selection.Contains(crane)) { input.Selection.Clear(); input.Selection.Add(crane); }
                    yield return Hover(At(dead));
                    Log($"(31) cursor over the destroyed {dead.Type}#{dead.Id}: '{Cursor()}'");
                    Check("(31) REPAIR cursor over a destroyed building", Cursor() == "repair");
                }
            }
            else Check("(31) a crane-repairable building found", false);

            // ---- (17): a squad takes a quiet flag and celebrates
            var flag = W.Flags.Where(fl => fl.Owner != Team.Red && !W.Objects.Any(o => o.IsUnit && o.Owner != Team.Red && o.Owner != Team.Neutral && !o.Destroyed && Mathf.Abs(o.CenterX - fl.CenterX) < 16 * 16 && Mathf.Abs(o.CenterY - fl.CenterY) < 16 * 16)).FirstOrDefault();
            if (flag != null)
            {
                var cs = Spawn("robot", "psycho", (flag.CenterX >> 4) + 3, (flag.CenterY >> 4) + 2, Team.Red);
                Look(g.CenterWorld(flag) + new Vector3(0f, 0f, -1f), 22f);
                yield return new WaitForSeconds(0.8f);
                int c0 = RobotGestures.Celebrations; float tC = Time.unscaledTime;
                W.SendWaypoints(Team.Red, cs, new[] { new ZWaypoint { mode = WpMode.Move, refId = flag.Id, x = flag.CenterX, y = flag.CenterY, playerGiven = true } });
                yield return WaitFor(() => flag.Owner == Team.Red, 20f);
                yield return WaitFor(() => RobotGestures.Celebrations > c0, 5f);
                yield return new WaitForSeconds(0.55f); Shot("05_flag_celebration");
                int pk0 = RobotGestures.Peaks; yield return WaitFor(() => RobotGestures.Peaks > pk0, 3f);
                Log($"(17) flag owner {flag.Owner}, celebrations +{RobotGestures.Celebrations - c0} ({RobotGestures.CelebrationOf("psycho")}), hand lift {RobotGestures.LastLift:F2}, sounds {Played(tC, "rob_gesture")}, skipped (enemy near) {RobotGestures.SkippedEnemyNear}");
                Check("(17) the capturing squad celebrates with no enemy near (with sound), the hand goes over the head", flag.Owner == Team.Red && RobotGestures.Celebrations - c0 >= 3 && Played(tC, "rob_gesture") >= 1 && RobotGestures.LastLift > 0.4f);
            }
            else Check("(17) a quiet flag found", false);

            // ---- (8): a factory door held open, the lit bay
            yield return DoorFrame("L02");

            // ---- (37): the red fort is shelled: roof defender up, fort gun shots with tilt + reload drop
            var rf = W.Objects.First(o => o.IsFort && o.Owner == Team.Red && !o.Destroyed);
            var gun = FindObjectsByType<FortGunView>(FindObjectsSortMode.None).FirstOrDefault(v => v.Fort == rf);
            int front = rf.Type == "fort_front" ? 1 : -1;                                                  // fort_back: rear arc south, so north is in front
            var att = Spawn("vehicle", "heavy", rf.CenterX >> 4, (rf.CenterY >> 4) + front * 10, Team.Blue);   // 160 px: inside the fort gun's 200 px, 64+ px off the walls
            if (att != null) { immortal.Add(att); att.MoveSpeed = 0; att.RealMoveSpeed = 0; }
            int gs0 = gun ? gun.Shots : 0, tc0 = gun ? gun.HeadTiltCycles : 0, rd0 = FortGunView.ReloadDrops, rr0 = FortGunView.ReloadRaises; float minEl = 99f, maxEl = -99f;
            W.SendWaypoints(Team.Blue, att, new[] { new ZWaypoint { mode = WpMode.Attack, refId = rf.Id, x = rf.CenterX, y = rf.CenterY, attackTo = true, playerGiven = true } });
            Look(g.CenterWorld(rf) + new Vector3(-6f, 0f, 0f), 40f);
            ZObject man = null; float tD = Time.unscaledTime;
            for (float t = 0; t < 75f; t += Time.deltaTime)
            {
                if (gun) { minEl = Mathf.Min(minEl, gun.Elevation); maxEl = Mathf.Max(maxEl, gun.Elevation); }
                if (man == null) { man = W.Objects.FirstOrDefault(o => o.IsFortDefender && !o.Destroyed); if (man != null) { Log($"(37) roof defender #{man.Id} up {Time.unscaledTime - tD:F1} s after the shelling began, damage {man.Damage} per hit"); } }
                if (man != null && t > 6f && frames < 40 && !shotDef) { shotDef = true; Shot("06_fort_defender_rts_zoom"); }
                if (gun && gun.Shots - gs0 >= 2 && FortGunView.ReloadDrops - rd0 >= 1 && FortGunView.ReloadRaises - rr0 >= 1) break;
                yield return null;
            }
            var mv = man != null ? V(man) : null;
            bool onScreen = mv && Camera.main && Camera.main.WorldToViewportPoint(mv.transform.position).z > 0;
            Log($"(37) defender {(man != null ? "#" + man.Id : "none")} drawn {(mv != null)} on screen {onScreen}; fort gun shots {(gun ? gun.Shots - gs0 : -1)}, head tilt cycles {(gun ? gun.HeadTiltCycles - tc0 : -1)}, max tilt {(gun ? gun.HeadTiltMax : 0):F1} deg, barrel elevation {minEl:F0}..{maxEl:F0} deg, reload drops {FortGunView.ReloadDrops - rd0}, climbs {FortGunView.ReloadRaises - rr0}, elevate sounds {Played(tD, "veh_fort_elevate")}");
            Check("(37) the roof defender comes up when the fort is hit (drawn on the roof)", man != null && mv != null);
            Check("(9) the roof defender fires a grunt squad's rifle", man != null && man.Damage == W.S.Unit("grunt").Damage * ZWorld.CrewRifleMul);
            Check("(37) the fort gun tilts for every shot and drops / climbs between shots", gun && gun.Shots - gs0 >= 1 && gun.HeadTiltCycles - tc0 >= 1 && FortGunView.ReloadDrops - rd0 >= 1 && maxEl - minEl > 20f);
            if (att != null) { immortal.Remove(att); att.SetHealth(0); W.UpdateObjectHealth(att); }

            // ---- (34): a blue squad parked by the red fort: DV60 sends it in -> loss by fort entry
            foreach (var o in W.Objects)
                if (o.Owner == Team.Red && (o.Kind == ZKind.Cannon || o.IsFortDefender || o.IsUnit) && !o.Destroyed && Mathf.Abs(o.CenterX - rf.CenterX) < 22 * 16 && Mathf.Abs(o.CenterY - rf.CenterY) < 22 * 16) o.KillAt = W.Time;   // nothing left to fight by the fort: the squad would be busy, not idle
            yield return new WaitForSeconds(1.0f);
            var bs = Spawn("robot", "grunt", (rf.CenterX >> 4) - 9, (rf.CenterY >> 4) + 3, Team.Blue);
            if (bs != null) { immortal.Add(bs); foreach (var m in bs.Minions) immortal.Add(m); }
            var bot = W.Bots.FirstOrDefault(b => b.Team == Team.Blue); int fo0 = bot != null ? bot.FinishOrders : 0; float tF = Time.unscaledTime; bool entered = false; double enteredAt = -1;
            Look(g.CenterWorld(rf) + new Vector3(-4f, 0f, 0f), 36f);
            for (float t = 0; t < 110f && !W.GameOver; t += Time.deltaTime)
            {
                foreach (var e in W.Events) if (e.type == ZEventType.FortEntered) { entered = true; enteredAt = W.Time; }
                if (!entered && t > 3f && frames < 40 && !shotIn && bs.Waypoints.Count > 0) { shotIn = true; Shot("07_blue_storms_red_fort"); }
                yield return null;
            }
            yield return new WaitForSeconds(1.0f);
            Log($"(34) DV60 finish orders +{(bot != null ? bot.FinishOrders - fo0 : -1)}, squad wp {(bs.Waypoints.Count > 0 ? bs.Waypoints[0].mode.ToString() : "-")}, game over {W.GameOver} winner {W.Winner} after {Time.unscaledTime - tF:F1} s (fort entered event {entered})");
            Check("(34) the idle blue squad by our fort storms it and the game ends with our loss", bot != null && bot.FinishOrders > fo0 && W.GameOver && W.Winner == Team.Blue);
            yield return new WaitForSeconds(2.5f); Shot("08_loss_by_fort_entry");
            Finish();
        }
        bool shotDef, shotIn;

        void Finish()
        {
            Log($"RESULT {(fails == 0 ? "PASS" : "FAIL " + fails)}, {frames} frames, {Time.unscaledTime - T0:F0} s");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, Recheck ? "proof_log_recheck_11.txt" : "proof_log.txt"), sb.ToString());
            ProofMouseOff(); Recheck = false;
            Report = fails == 0 ? "PASS" : "FAIL " + fails;
        }

        Vector2Int Field()
        {
            int wt = W.Path.WidthPix >> 4, ht = W.Path.HeightPix >> 4; var best = new Vector2Int(wt / 2, ht / 2); float bd = float.MaxValue;
            for (int y = 12; y < ht - 14; y += 2)
                for (int x = 12; x < wt - 12; x += 2)
                {
                    bool ok = true;
                    for (int j = -8; j <= 10 && ok; j++) for (int i = -9; i <= 12 && ok; i++) ok = W.Path.TilePassable(x + i, y + j, false);
                    if (!ok) continue;
                    foreach (var o in W.Objects) if (!o.Destroyed && Mathf.Abs((o.CenterX >> 4) - x) < 14 && Mathf.Abs((o.CenterY >> 4) - y) < 14) { ok = false; break; }
                    if (!ok) continue;
                    float d = (x - wt / 2) * (x - wt / 2) + (y - ht / 2) * (y - ht / 2);
                    if (d < bd) { bd = d; best = new Vector2Int(x, y); }
                }
            Look(ZGame.PxToWorld(best.x * 16 + 16, best.y * 16 + 16), 26f);
            return best;
        }
    }
}
